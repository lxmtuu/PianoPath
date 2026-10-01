using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
// DXGI declares its own MapFlags (IDXGISurface::Map); the stage maps D3D11 resources.
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace PianoPath;

/// <summary>
/// The GPU stage's game loop: a dedicated background thread that owns the Direct3D device and renders
/// continuously at the configured rate (60, 120, 144, 240 or unlimited frames per second).
/// </summary>
/// <remarks>
/// <para><b>Why a thread of its own.</b> WPF renders on its composition thread at the monitor rate and
/// runs input, MIDI callbacks, audio scheduling and layout on the dispatcher. Rendering the stage on the
/// dispatcher would put every GPU stall directly in front of the next MIDI note. Here the dispatcher
/// only posts state into <see cref="GpuStageFeed"/> (a short lock) and the loop pulls it once per frame,
/// so a heavy frame costs frame rate, never input latency.</para>
/// <para><b>Outputs.</b> The loop can feed two outputs from one simulation step: the stage window's swap
/// chain (flip-discard, tearing-capable for VSync off) and the embedded preview inside the main window,
/// which is read back through a ring of staging textures one frame late so the CPU never waits for the
/// GPU to finish the frame it just submitted.</para>
/// </remarks>
internal sealed class GpuRenderLoop : IDisposable
{
    private const int StagingRing = 3;

    private readonly GpuStageFeed _feed;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private volatile bool _running = true;
    private readonly object _outputGate = new();

    // embedded preview (read back into the WPF stage)
    private int _embeddedWidth, _embeddedHeight;
    private bool _embeddedEnabled;
    // stage window (swap chain)
    private IntPtr _windowHandle;
    private int _windowWidth, _windowHeight;
    private bool _windowChanged;
    private volatile int _targetFps = 60;
    private volatile bool _vsync = true;

    internal GpuRenderLoop(GpuStageFeed feed, bool forceWarp = false)
    {
        _feed = feed;
        ForceWarp = forceWarp;
        _thread = new Thread(Run) { IsBackground = true, Name = "Keyflow GPU stage", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    internal bool ForceWarp { get; }
    /// <summary>Set once the device exists and every shader compiled.</summary>
    internal bool IsReady { get; private set; }
    /// <summary>The failure that stopped the loop, if any; the UI falls back to the software stage.</summary>
    internal string? Error { get; private set; }
    internal string AdapterName { get; private set; } = "";
    internal bool IsWarp { get; private set; }
    internal double ShaderCompileMilliseconds { get; private set; }
    /// <summary>Smoothed frames per second actually rendered.</summary>
    internal double Fps { get; private set; }
    internal double FrameMilliseconds { get; private set; }
    internal int ParticleCount { get; private set; }
    internal long FramesRendered => Interlocked.Read(ref _frames);
    private long _frames;

    internal event Action? StatusChanged;

    /// <summary>Frames per second the loop aims for; 0 means as fast as possible.</summary>
    internal int TargetFps { get => _targetFps; set => _targetFps = Math.Clamp(value, 0, 1000); }
    /// <summary>Wait for the display's vertical blank when presenting the stage window.</summary>
    internal bool VSync { get => _vsync; set => _vsync = value; }

    private volatile bool _parked;
    private readonly ManualResetEventSlim _parkedAck = new(false);

    /// <summary>
    /// Stops the render thread from stepping the simulation or publishing frames, and returns once it has
    /// acknowledged, so nothing is in flight afterwards.
    ///
    /// A screenshot run parks the loop before it presses anything and builds its own frame synchronously
    /// (see <see cref="RenderOnce"/>). Left running, the thread would take the preview note's events off the
    /// feed before that frame ever saw them, and it would publish frames of its own on top of it.
    /// </summary>
    internal bool Park(TimeSpan timeout)
    {
        _parkedAck.Reset();
        _parked = true;
        return _parkedAck.Wait(timeout);
    }

    internal void SetEmbedded(bool enabled, int width, int height)
    {
        lock (_outputGate) { _embeddedEnabled = enabled; _embeddedWidth = Math.Clamp(width, 0, 3840); _embeddedHeight = Math.Clamp(height, 0, 2160); }
    }

    internal void SetWindow(IntPtr handle, int width, int height)
    {
        lock (_outputGate)
        {
            if (_windowHandle != handle || _windowWidth != width || _windowHeight != height) _windowChanged = true;
            _windowHandle = handle; _windowWidth = Math.Max(0, width); _windowHeight = Math.Max(0, height);
        }
    }

    internal bool WaitUntilStarted(TimeSpan timeout) => _started.Wait(timeout);

    private void Run()
    {
        GpuStageRenderer? renderer = null;
        SwapChainOutput? window = null;
        var embeddedTarget = new GpuRenderTarget();
        var windowTarget = new GpuRenderTarget();
        var readback = new ReadbackRing();
        var recordTarget = new GpuRenderTarget();
        var recordReadback = new ReadbackRing();
        GpuRecordingTap? recording = null;
        var nextRecordFrame = 0.0;
        try
        {
            renderer = GpuStageRenderer.Create(ForceWarp);
            AdapterName = renderer.AdapterName; IsWarp = renderer.IsWarp; ShaderCompileMilliseconds = renderer.ShaderCompileMilliseconds;
            try { using var dxgi = renderer.Device.QueryInterface<IDXGIDevice1>(); dxgi.MaximumFrameLatency = 1; } catch { }
            IsReady = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            renderer?.Dispose();
            _started.Set();
            StatusChanged?.Invoke();
            return;
        }
        _started.Set();
        StatusChanged?.Invoke();
        var gpu = renderer!;

        var simulation = new GpuStageSimulation();
        var input = new GpuFrameInput();
        var notes = new GpuInstanceList<GpuNoteInstance>(1024);
        var keys = new GpuInstanceList<GpuKeyInstance>(128);
        var sprites = new GpuInstanceList<GpuSpriteInstance>(8192);
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalSeconds;
        var fpsWindowStart = last; var fpsFrames = 0; var lastEmbedded = double.NegativeInfinity;
        TimerResolution.Begin();
        try
        {
            while (_running)
            {
                // Parked: acknowledge before touching the feed or the device, so Park() returning means no frame is in flight.
                if (_parked) { _parkedAck.Set(); Thread.Sleep(5); last = clock.Elapsed.TotalSeconds; continue; }
                var frameStart = clock.Elapsed.TotalSeconds;
                bool embeddedEnabled; int ew, eh; IntPtr hwnd; int ww, wh; bool windowChanged;
                lock (_outputGate)
                {
                    embeddedEnabled = _embeddedEnabled; ew = _embeddedWidth; eh = _embeddedHeight;
                    hwnd = _windowHandle; ww = _windowWidth; wh = _windowHeight; windowChanged = _windowChanged; _windowChanged = false;
                }
                var hasEmbedded = embeddedEnabled && ew >= 16 && eh >= 16;

                // ---- stage window: create, resize or drop the swap chain on this thread ----
                if (windowChanged)
                {
                    if (hwnd == IntPtr.Zero) { window?.Dispose(); window = null; }
                    else if (window is null || window.Handle != hwnd) { window?.Dispose(); window = SwapChainOutput.Create(gpu, hwnd, ww, wh); }
                    else window.Resize(ww, wh);
                }
                var hasWindow = window is not null && ww >= 16 && wh >= 16;
                var tap = _feed.Recording;
                if (!ReferenceEquals(tap, recording))
                {
                    // a new take (or the end of one): start its clock now, and free the old frame's memory
                    recording = tap; nextRecordFrame = clock.Elapsed.TotalSeconds;
                    recordReadback.Dispose(); recordTarget.Dispose();
                }
                if (!hasEmbedded && !hasWindow && tap is null) { Thread.Sleep(15); last = clock.Elapsed.TotalSeconds; continue; }

                _feed.Capture(input, GpuStageFeed.Now);
                gpu.UpdateBackground(_feed.Background);
                gpu.UpdateAtlas(_feed.LabelAtlas);
                var now = clock.Elapsed.TotalSeconds;
                var dt = now - last; last = now;
                // the simulation runs in the layout of the primary output (the window when it is open)
                var primaryAspect = hasWindow ? ww / (float)wh : hasEmbedded ? ew / (float)eh : tap!.Width / (float)tap.Height;
                var sceneHeight = (float)Math.Max(120, input.StageHeightDip);
                simulation.Step(dt, input, _feed, sceneHeight * primaryAspect);

                if (hasWindow)
                {
                    windowTarget.Ensure(gpu.Device, window!.Width, window.Height);
                    var layout = new GpuSceneLayout(sceneHeight * window.Width / (float)window.Height, sceneHeight, input.Look.KeyboardFraction);
                    gpu.Render(windowTarget, window.View, simulation, input, layout, notes, keys, sprites);
                    window.Present(_vsync);
                }
                // while the stage window runs at its own rate, the preview inside the main window is refreshed at 60 Hz
                if (hasEmbedded && hasWindow && now - lastEmbedded < 1 / 60.0) hasEmbedded = false;
                if (hasEmbedded)
                {
                    lastEmbedded = now;
                    embeddedTarget.Ensure(gpu.Device, ew, eh);
                    readback.Ensure(gpu.Device, ew, eh);
                    var layout = new GpuSceneLayout(sceneHeight * ew / (float)eh, sceneHeight, input.Look.KeyboardFraction);
                    gpu.Render(embeddedTarget, readback.CurrentView, simulation, input, layout, notes, keys, sprites);
                    readback.Submit(gpu.Context);
                    readback.CollectInto(gpu.Context, _feed);
                }
                // ---- recording: an exact-size frame at the take's own rate ----
                if (tap is not null && now >= nextRecordFrame)
                {
                    var interval = 1.0 / tap.FrameRate;
                    nextRecordFrame = Math.Max(nextRecordFrame + interval, now - interval);
                    recordTarget.Ensure(gpu.Device, tap.Width, tap.Height);
                    recordReadback.Ensure(gpu.Device, tap.Width, tap.Height);
                    var layout = new GpuSceneLayout(sceneHeight * tap.Width / (float)tap.Height, sceneHeight, input.Look.KeyboardFraction);
                    gpu.Render(recordTarget, recordReadback.CurrentView, simulation, input, layout, notes, keys, sprites);
                    recordReadback.Submit(gpu.Context);
                    recordReadback.Collect(gpu.Context, tap.BackBuffer, tap.Publish);
                }
                ParticleCount = simulation.ParticleCount;
                Interlocked.Increment(ref _frames);

                var end = clock.Elapsed.TotalSeconds;
                FrameMilliseconds += ((end - frameStart) * 1000 - FrameMilliseconds) * .1;
                fpsFrames++;
                if (end - fpsWindowStart >= .5) { Fps = fpsFrames / (end - fpsWindowStart); fpsFrames = 0; fpsWindowStart = end; }

                // ---- pacing: the swap chain paces itself with VSync; otherwise sleep to the target rate ----
                var target = _targetFps;
                if (hasWindow && _vsync && target == 0) continue;
                if (!hasWindow && target == 0) target = 240; // the embedded preview is shown at the WPF composition rate anyway
                if (tap is not null && target > 0 && target < tap.FrameRate) target = tap.FrameRate; // a take never drops below its own rate
                if (target > 0) SleepUntil(clock, frameStart + 1.0 / target);
            }
        }
        catch (Exception ex)
        {
            Error = ex is SharpGenException sharp ? $"Direct3D: {sharp.Message}" : ex.Message;
            IsReady = false;
            StatusChanged?.Invoke();
        }
        finally
        {
            TimerResolution.End();
            readback.Dispose();
            recordReadback.Dispose();
            recordTarget.Dispose();
            window?.Dispose();
            embeddedTarget.Dispose();
            windowTarget.Dispose();
            gpu.Dispose();
        }
    }

    private void SleepUntil(Stopwatch clock, double deadline)
    {
        while (_running)
        {
            var remaining = deadline - clock.Elapsed.TotalSeconds;
            if (remaining <= 0) return;
            if (remaining > .002) Thread.Sleep(1);
            else Thread.SpinWait(200);
        }
    }

    public void Dispose()
    {
        _running = false;
        if (!_thread.Join(TimeSpan.FromSeconds(2))) Trace.WriteLine("Keyflow: the GPU stage thread did not stop within 2 s.");
        _started.Dispose();
        _parkedAck.Dispose();
    }

    /// <summary>
    /// Renders one frame synchronously on a private WARP device and returns it as BGRA pixels. Used by
    /// the verification suite and <c>--gpu-snapshot</c>, where a deterministic software rasterizer
    /// matters more than speed.
    ///
    /// <paramref name="renderWarmup"/> false steps the simulation through the warm-up frames without drawing
    /// them, so a long run (the README previews step 480 frames) costs one draw instead of 480.
    /// </summary>
    internal static byte[] RenderOnce(GpuStageFeed feed, int width, int height, int warmupFrames, double frameSeconds, out string adapter, bool forceWarp = true, bool renderWarmup = true)
    {
        using var renderer = GpuStageRenderer.Create(forceWarp);
        adapter = renderer.AdapterName;
        var simulation = new GpuStageSimulation();
        var input = new GpuFrameInput();
        var notes = new GpuInstanceList<GpuNoteInstance>(256);
        var keys = new GpuInstanceList<GpuKeyInstance>(128);
        var sprites = new GpuInstanceList<GpuSpriteInstance>(1024);
        using var target = new GpuRenderTarget();
        target.Ensure(renderer.Device, width, height);
        using var output = renderer.Device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, mipLevels: 1, bindFlags: BindFlags.RenderTarget);
        using var view = renderer.Device.CreateRenderTargetView(output);
        using var staging = renderer.Device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, mipLevels: 1, bindFlags: BindFlags.None, usage: ResourceUsage.Staging, cpuAccessFlags: CpuAccessFlags.Read);
        for (var frame = 0; frame <= warmupFrames; frame++)
        {
            feed.Capture(input, GpuStageFeed.Now);
            var sceneHeight = (float)Math.Max(120, input.StageHeightDip);
            var layout = new GpuSceneLayout(sceneHeight * width / (float)height, sceneHeight, input.Look.KeyboardFraction);
            simulation.Step(frameSeconds, input, feed, layout.Width);
            // Every frame is drawn from scratch out of the simulation and the input, so the frames before the last
            // only matter through the simulation, which the step above already advanced.
            if (!renderWarmup && frame < warmupFrames) continue;
            renderer.UpdateBackground(feed.Background);
            renderer.UpdateAtlas(feed.LabelAtlas);
            renderer.Render(target, view, simulation, input, layout, notes, keys, sprites);
        }
        renderer.Context.CopyResource(staging, output);
        var pixels = new byte[width * height * 4];
        var mapped = renderer.Context.Map(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), pixels, y * width * 4, width * 4);
        }
        finally { renderer.Context.Unmap(staging, 0); }
        return pixels;
    }

    /// <summary>The embedded preview's output textures plus a staging ring read back one frame late.</summary>
    private sealed class ReadbackRing : IDisposable
    {
        private readonly ID3D11Texture2D?[] _outputs = new ID3D11Texture2D?[StagingRing];
        private readonly ID3D11RenderTargetView?[] _views = new ID3D11RenderTargetView?[StagingRing];
        private readonly ID3D11Texture2D?[] _staging = new ID3D11Texture2D?[StagingRing];
        private readonly bool[] _pending = new bool[StagingRing];
        private int _index, _width, _height;

        internal ID3D11RenderTargetView CurrentView => _views[_index]!;

        internal void Ensure(ID3D11Device device, int width, int height)
        {
            if (width == _width && height == _height && _outputs[0] is not null) return;
            Dispose();
            _width = width; _height = height;
            for (var i = 0; i < StagingRing; i++)
            {
                _outputs[i] = device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, mipLevels: 1, bindFlags: BindFlags.RenderTarget);
                _views[i] = device.CreateRenderTargetView(_outputs[i]!);
                _staging[i] = device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, mipLevels: 1, bindFlags: BindFlags.None, usage: ResourceUsage.Staging, cpuAccessFlags: CpuAccessFlags.Read);
                _pending[i] = false;
            }
            _index = 0;
        }

        internal void Submit(ID3D11DeviceContext context)
        {
            context.CopyResource(_staging[_index]!, _outputs[_index]!);
            _pending[_index] = true;
            _index = (_index + 1) % StagingRing;
        }

        /// <summary>Maps the oldest submitted frame (usually finished by now) and publishes it to the feed.</summary>
        internal void CollectInto(ID3D11DeviceContext context, GpuStageFeed feed) => Collect(context, feed.BackBuffer, feed.PublishBackBuffer);

        /// <summary>Maps the oldest submitted frame and copies it into the buffer <paramref name="buffer"/> hands out, then calls <paramref name="publish"/>.</summary>
        internal void Collect(ID3D11DeviceContext context, Func<int, int, byte[]> buffer, Action publish)
        {
            // _index now points at the oldest slot in the ring
            var slot = -1;
            for (var k = 0; k < StagingRing; k++)
            {
                var candidate = (_index + k) % StagingRing;
                if (_pending[candidate] && candidate != (_index + StagingRing - 1) % StagingRing) { slot = candidate; break; }
            }
            if (slot < 0) return;
            var staging = _staging[slot]!;
            MappedSubresource mapped;
            try { mapped = context.Map(staging, 0, MapMode.Read, MapFlags.None); }
            catch (SharpGenException) { return; }
            try
            {
                var target = buffer(_width, _height);
                var row = _width * 4;
                for (var y = 0; y < _height; y++)
                    Marshal.Copy(mapped.DataPointer + (nint)(y * mapped.RowPitch), target, y * row, row);
            }
            finally { context.Unmap(staging, 0); }
            _pending[slot] = false;
            publish();
        }

        public void Dispose()
        {
            for (var i = 0; i < StagingRing; i++)
            {
                _views[i]?.Dispose(); _outputs[i]?.Dispose(); _staging[i]?.Dispose();
                _views[i] = null; _outputs[i] = null; _staging[i] = null; _pending[i] = false;
            }
            _width = _height = 0;
        }
    }

    /// <summary>A flip-model swap chain on the stage window's child HWND.</summary>
    private sealed class SwapChainOutput : IDisposable
    {
        private readonly GpuStageRenderer _renderer;
        private readonly IDXGISwapChain1 _swapChain;
        private readonly bool _tearing;
        private ID3D11Texture2D? _backBuffer;
        internal ID3D11RenderTargetView View { get; private set; } = null!;
        internal IntPtr Handle { get; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }

        private SwapChainOutput(GpuStageRenderer renderer, IntPtr handle, IDXGISwapChain1 swapChain, bool tearing, int width, int height)
        {
            _renderer = renderer; Handle = handle; _swapChain = swapChain; _tearing = tearing; Width = width; Height = height;
            AcquireBackBuffer();
        }

        internal static SwapChainOutput Create(GpuStageRenderer renderer, IntPtr hwnd, int width, int height)
        {
            width = Math.Max(16, width); height = Math.Max(16, height);
            using var dxgiDevice = renderer.Device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();
            var tearing = false;
            try { using var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>(); tearing = factory5 is not null && factory5.PresentAllowTearing; } catch { }
            var description = new SwapChainDescription1
            {
                Width = (uint)width, Height = (uint)height, Format = Format.B8G8R8A8_UNorm, BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore,
                Flags = tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None
            };
            var swapChain = factory.CreateSwapChainForHwnd(renderer.Device, hwnd, description, new SwapChainFullscreenDescription { Windowed = true });
            factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
            return new SwapChainOutput(renderer, hwnd, swapChain, tearing, width, height);
        }

        private void AcquireBackBuffer()
        {
            _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            View = _renderer.Device.CreateRenderTargetView(_backBuffer);
        }

        internal void Resize(int width, int height)
        {
            width = Math.Max(16, width); height = Math.Max(16, height);
            if (width == Width && height == Height) return;
            _renderer.Context.ClearState();
            View.Dispose(); _backBuffer?.Dispose();
            _renderer.Context.Flush();
            _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None).CheckError();
            Width = width; Height = height;
            AcquireBackBuffer();
        }

        internal void Present(bool vsync)
        {
            var result = vsync ? _swapChain.Present(1, PresentFlags.None) : _swapChain.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None);
            if (result.Failure) result.CheckError();
        }

        public void Dispose()
        {
            View?.Dispose(); _backBuffer?.Dispose(); _swapChain.Dispose();
        }
    }

    /// <summary>1 ms scheduler resolution while the loop runs, so frame pacing by sleep is accurate.</summary>
    private static class TimerResolution
    {
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
        internal static void Begin() { try { timeBeginPeriod(1); } catch { } }
        internal static void End() { try { timeEndPeriod(1); } catch { } }
    }
}
