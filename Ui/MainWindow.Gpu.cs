using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// The GPU graphics engine as the main window sees it: one <see cref="GpuStageFeed"/> the stage writes
/// into, one <see cref="GpuRenderLoop"/> (a render thread with its own Direct3D 11 device) started only
/// while something shows GPU frames, and the optional full-rate <see cref="GpuStageWindow"/>.
/// </summary>
public partial class MainWindow
{
    private readonly GpuStageFeed _gpuFeed = new();
    private GpuRenderLoop? _gpuLoop;
    private GpuStageWindow? _gpuWindow;
    private bool _gpuSessionOverride, _gpuFailureShown, _gpuHooked;
    private TextBlock? _gpuStatusLabel;

    /// <summary><c>--gpu</c>: use the GPU engine for this run without touching the settings file.</summary>
    internal void UseGpuForSession()
    {
        _gpuSessionOverride = true;
        ApplyRenderBackend();
    }

    /// <summary>True while the GPU engine draws the stage inside this window.</summary>
    internal bool GpuStageActive => Stage.UsesGpuFrame && _gpuLoop is { Error: null };
    internal GpuRenderLoop? GpuLoop => _gpuLoop;

    private bool WantsEmbeddedGpu => _gpuSessionOverride || _visualSettings.RenderBackend == "Gpu";

    /// <summary>Starts, reconfigures or stops the GPU engine to match the settings; cheap when nothing changed.</summary>
    private void ApplyRenderBackend()
    {
        if (_closing) return;
        if (!_gpuHooked)
        {
            _gpuHooked = true;
            Stage.GpuPixelSizeChanged += (width, height) => _gpuLoop?.SetEmbedded(Stage.UsesGpuFrame, width, height);
            Stage.GpuStats = () => (_gpuLoop?.Fps ?? 0, _gpuLoop?.ParticleCount ?? 0);
        }
        var embedded = WantsEmbeddedGpu;
        var needed = embedded || _gpuWindow is not null;
        if (needed && _gpuLoop is { Error: not null }) { StopGpuLoop(); needed = _gpuWindow is not null; embedded = false; }
        if (!needed)
        {
            Stage.AttachGpu(null, false);
            StopGpuLoop();
            RefreshGpuStatus();
            return;
        }
        if (_gpuLoop is null)
        {
            _gpuLoop = new GpuRenderLoop(_gpuFeed);
            _gpuLoop.StatusChanged += () => Dispatcher.BeginInvoke(new Action(OnGpuStatusChanged));
        }
        _gpuLoop.TargetFps = _visualSettings.GpuTargetFps;
        _gpuLoop.VSync = _visualSettings.GpuVSync;
        Stage.AttachGpu(_gpuFeed, embedded);
        var (width, height) = Stage.GpuPixelSize;
        _gpuLoop.SetEmbedded(Stage.UsesGpuFrame, width, height);
        RefreshGpuStatus();
    }

    /// <summary>
    /// MIDI callback thread: queues a note straight into the GPU feed (lock-free queues) when the GPU
    /// engine runs. Returns whether it did, so the dispatcher side does not queue the note again.
    /// </summary>
    private bool ForwardMidiToGpu(int pitch, int velocity, bool on)
    {
        if (Volatile.Read(ref _gpuLoop) is not { Error: null }) return false;
        if (on)
        {
            var hit = velocity / 127.0;
            _gpuFeed.LiveNote(pitch, true, hit);
            _gpuFeed.Impact(pitch, hit);
        }
        else _gpuFeed.LiveNote(pitch, false, 0);
        return true;
    }

    private void StopGpuLoop()
    {
        if (_gpuLoop is null) return;
        var loop = _gpuLoop;
        _gpuLoop = null;
        loop.Dispose();
    }

    /// <summary>The render thread started or failed; a failure falls back to the software stage once, with an explanation.</summary>
    private void OnGpuStatusChanged()
    {
        if (_closing) return;
        if (_gpuLoop is { Error: { } error })
        {
            Stage.AttachGpu(null, false);
            if (!_gpuFailureShown)
            {
                _gpuFailureShown = true;
                ShowMessage(Loc.F("The GPU engine could not start, so the stage stays on the software renderer.\n\n{0}", error), "Graphics engine");
            }
        }
        RefreshGpuStatus();
    }

    private void RefreshGpuStatus()
    {
        UpdateRecordingInfo(); // the next take's engine follows the GPU engine's state
        if (_gpuStatusLabel is null) return;
        Loc.Bind(_gpuStatusLabel, () =>
        {
            var loop = _gpuLoop;
            if (loop is null) return Loc.T("Engine: software (WPF) · the GPU engine is idle.");
            if (loop.Error is { } error) return Loc.F("Engine: software (WPF) · the GPU engine failed: {0}", error);
            if (!loop.IsReady) return Loc.T("Engine: starting Direct3D 11…");
            var adapter = loop.IsWarp ? Loc.T("software rasterizer (WARP)") : loop.AdapterName;
            return Loc.F("Engine: Direct3D 11 on {0} · shaders compiled in {1} ms", adapter, Math.Round(loop.ShaderCompileMilliseconds));
        });
    }

    private void OpenGpuStage_Click(object sender, RoutedEventArgs e) => OpenGpuStageWindow();

    /// <summary>Opens (or brings forward) the full-rate GPU stage window.</summary>
    internal void OpenGpuStageWindow()
    {
        if (_gpuWindow is not null) { _gpuWindow.Activate(); return; }
        if (_gpuLoop is { Error: not null }) StopGpuLoop();
        if (_gpuLoop is null)
        {
            _gpuLoop = new GpuRenderLoop(_gpuFeed);
            _gpuLoop.StatusChanged += () => Dispatcher.BeginInvoke(new Action(OnGpuStatusChanged));
        }
        _gpuWindow = new GpuStageWindow(_gpuLoop) { Owner = this };
        _gpuWindow.Closed += (_, _) => { _gpuWindow = null; ApplyRenderBackend(); };
        ApplyRenderBackend();
        _gpuWindow.Show();
    }

    private void ShutdownGpuStage()
    {
        _gpuWindow?.Close();
        _gpuWindow = null;
        Stage.AttachGpu(null, false);
        StopGpuLoop();
    }

    // =====================================================================================================
    // Recording from the GPU stage: exact-size frames rendered by the render thread
    // =====================================================================================================

    private GpuRecordingTap? _gpuRecording;
    private byte[]? _gpuRecordingFrame;

    /// <summary>True while the take is being rendered by the GPU stage rather than rasterized from WPF.</summary>
    internal bool RecordingFromGpu => _gpuRecording is not null;

    /// <summary>
    /// Points the GPU render thread at the new take when the GPU stage is what the user is looking at
    /// (in the main window or in the stage window). A transparent PNG sequence stays on the software
    /// stage: the GPU frame is composited opaque.
    /// </summary>
    private void StartGpuRecording(IFrameRecorder recorder)
    {
        StopGpuRecording();
        if (_gpuLoop is not { Error: null }) return;
        if (!Stage.UsesGpuFrame && _gpuWindow is null) return;
        if (recorder.HasAlpha && Stage.TransparentBackdrop) return;
        _gpuRecording = new GpuRecordingTap(recorder.Width, recorder.Height, recorder.FrameRate);
        _gpuFeed.Recording = _gpuRecording;
    }

    private void StopGpuRecording()
    {
        _gpuFeed.Recording = null;
        _gpuRecording = null;
        _gpuRecordingFrame = null;
    }

    /// <summary>
    /// The newest GPU-rendered frame in the layout <paramref name="recorder"/> expects, or null while the
    /// render thread has not delivered one yet (the session then rasterizes the WPF stage for that frame).
    /// </summary>
    private byte[]? TryCaptureGpuFrame(IFrameRecorder recorder)
    {
        var tap = _gpuRecording;
        if (tap is null || tap.Width != recorder.Width || tap.Height != recorder.Height) return null;
        // the engine stopped or failed mid-take (e.g. the stage window closed): the rest is drawn by the software stage
        if (_gpuLoop is null or { Error: not null } || !Stage.UsesGpuFrame && _gpuWindow is null) { StopGpuRecording(); return null; }
        var size = tap.Width * tap.Height * 4;
        if (_gpuRecordingFrame is null || _gpuRecordingFrame.Length != size) _gpuRecordingFrame = new byte[size];
        var frame = _gpuRecordingFrame;
        if (!tap.TryRead(pixels => Buffer.BlockCopy(pixels, 0, frame, 0, size))) return null;
        if (Stage.HasGpuOverlays) BlendStageOverlays(frame, tap.Width, tap.Height);
        return recorder.HasAlpha ? frame : ToBottomUpBgr(frame, tap.Width, tap.Height);
    }

    private RenderTargetBitmap? _gpuOverlayBitmap;
    private byte[]? _gpuOverlayPixels;

    /// <summary>
    /// Rasterizes the stage's WPF overlays (sheet, camera, hand marker, watermark, counter) at the
    /// recording size and blends them over the GPU frame. The overlay is stretched with the frame, so the
    /// sheet and the hand marker stay over the keys they belong to at any recording aspect.
    /// </summary>
    private void BlendStageOverlays(byte[] frame, int width, int height)
    {
        if (Stage.ActualWidth < 1 || Stage.ActualHeight < 1) return;
        if (_gpuOverlayBitmap is null || _gpuOverlayBitmap.PixelWidth != width || _gpuOverlayBitmap.PixelHeight != height)
        {
            _gpuOverlayBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            _gpuOverlayPixels = new byte[width * height * 4];
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(width / Stage.ActualWidth, height / Stage.ActualHeight));
            Stage.DrawGpuOverlays(dc);
            dc.Pop();
        }
        var bitmap = _gpuOverlayBitmap; bitmap.Clear(); bitmap.Render(visual);
        var overlay = _gpuOverlayPixels!;
        bitmap.CopyPixels(overlay, width * 4, 0);
        // premultiplied "over": frame = overlay + frame × (1 − overlay alpha)
        for (var i = 0; i < overlay.Length; i += 4)
        {
            var a = overlay[i + 3];
            if (a == 0) continue;
            var keep = 255 - a;
            frame[i] = (byte)(overlay[i] + frame[i] * keep / 255);
            frame[i + 1] = (byte)(overlay[i + 1] + frame[i + 1] * keep / 255);
            frame[i + 2] = (byte)(overlay[i + 2] + frame[i + 2] * keep / 255);
            frame[i + 3] = 255;
        }
    }
}
