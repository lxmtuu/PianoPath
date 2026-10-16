using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
// DXGI declares its own MapFlags (IDXGISurface::Map); the stage maps D3D11 resources.
using MapFlags = Vortice.Direct3D11.MapFlags;
using Vortice.Mathematics;

namespace PianoPath;

/// <summary>Frame constants; the field order is the <c>Frame</c> cbuffer in Gpu/StageShaders.hlsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuFrameConstants
{
    public Vector4 ScreenTime, SceneSize, Camera, Background, Aura, Horizon, SceneA, SceneB;
    public Vector4 NoteA, NoteB, NoteC, KeyA, KeyB, KeyC, KeyD, RimColor, Post, Post2, SceneFx, SceneFxColor, HitFx, KeyRange, ArPicture, ArFlags;
    public Vector4 ArMatrix0, ArMatrix1, ArMatrix2, ArMatrixFlags;
    public Vector4 NoteD; // appended: preserve the offsets of existing constants
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPassConstants { public Vector4 PassA; }

/// <summary>
/// The size-dependent resources of one output: the HDR scene, its depth buffer and the bloom pyramid.
/// The embedded preview and the stage window each own one, so both can render from the same device.
/// </summary>
internal sealed class GpuRenderTarget : IDisposable
{
    internal const int BloomLevels = GpuAdaptiveQualityProfile.FullBloomLevelCount;
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal ID3D11Texture2D? Hdr;
    internal ID3D11RenderTargetView? HdrRtv;
    internal ID3D11ShaderResourceView? HdrSrv;
    internal ID3D11Texture2D? Depth;
    internal ID3D11DepthStencilView? DepthView;
    internal readonly ID3D11Texture2D?[] BloomTextures = new ID3D11Texture2D?[BloomLevels];
    internal readonly ID3D11RenderTargetView?[] BloomRtv = new ID3D11RenderTargetView?[BloomLevels];
    internal readonly ID3D11ShaderResourceView?[] BloomSrv = new ID3D11ShaderResourceView?[BloomLevels];
    internal readonly (int Width, int Height)[] BloomSize = new (int, int)[BloomLevels];
    internal int BloomCount { get; private set; }

    internal void Ensure(ID3D11Device device, int width, int height)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        if (width == Width && height == Height && Hdr is not null) return;
        Release();
        Width = width; Height = height;
        Hdr = device.CreateTexture2D(Format.R16G16B16A16_Float, (uint)width, (uint)height, mipLevels: 1, bindFlags: BindFlags.RenderTarget | BindFlags.ShaderResource);
        HdrRtv = device.CreateRenderTargetView(Hdr);
        HdrSrv = device.CreateShaderResourceView(Hdr);
        Depth = device.CreateTexture2D(Format.D32_Float, (uint)width, (uint)height, mipLevels: 1, bindFlags: BindFlags.DepthStencil);
        DepthView = device.CreateDepthStencilView(Depth, new DepthStencilViewDescription(Depth, DepthStencilViewDimension.Texture2D));
        var w = width; var h = height; BloomCount = 0;
        for (var i = 0; i < BloomLevels; i++)
        {
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
            BloomSize[i] = (w, h);
            BloomTextures[i] = device.CreateTexture2D(Format.R16G16B16A16_Float, (uint)w, (uint)h, mipLevels: 1, bindFlags: BindFlags.RenderTarget | BindFlags.ShaderResource);
            BloomRtv[i] = device.CreateRenderTargetView(BloomTextures[i]!);
            BloomSrv[i] = device.CreateShaderResourceView(BloomTextures[i]!);
            BloomCount++;
            if (w <= 2 || h <= 2) break;
        }
    }

    private void Release()
    {
        HdrSrv?.Dispose(); HdrRtv?.Dispose(); Hdr?.Dispose(); DepthView?.Dispose(); Depth?.Dispose();
        HdrSrv = null; HdrRtv = null; Hdr = null; DepthView = null; Depth = null;
        for (var i = 0; i < BloomLevels; i++)
        {
            BloomSrv[i]?.Dispose(); BloomRtv[i]?.Dispose(); BloomTextures[i]?.Dispose();
            BloomSrv[i] = null; BloomRtv[i] = null; BloomTextures[i] = null;
        }
        Width = Height = 0;
    }

    public void Dispose() => Release();
}

/// <summary>
/// The Direct3D 11 stage renderer: one <see cref="ID3D11Device"/> for the resources, one immediate
/// <see cref="ID3D11DeviceContext"/> for the draw commands, and HLSL vertex/pixel shaders for the
/// background, the note capsules, the 3D keys, the particles, the bloom and the final grade.
/// </summary>
/// <remarks>
/// Every method must be called from the render thread that created the renderer (see
/// <see cref="GpuRenderLoop"/>): the immediate context is not thread-safe, which is exactly why the
/// UI thread only ever talks to <see cref="GpuStageFeed"/>.
/// </remarks>
internal sealed class GpuStageRenderer : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];

    internal ID3D11Device Device { get; }
    internal ID3D11DeviceContext Context { get; }
    internal bool IsWarp { get; }
    internal string AdapterName { get; } = "";
    internal FeatureLevel Level { get; }
    internal double ShaderCompileMilliseconds { get; private set; }

    private ID3D11VertexShader _vsFullscreen = null!, _vsNote = null!, _vsKey = null!, _vsSprite = null!, _vsArQuad = null!;
    private ID3D11PixelShader _psArQuad = null!, _psBackground = null!, _psNote = null!, _psKey = null!, _psSprite = null!, _psPrefilter = null!, _psDown = null!, _psUp = null!, _psComposite = null!, _psCompositeGeometry = null!, _psCompositeGlow = null!;
    private ID3D11BlendState _blendPremultiplied = null!, _blendAdditive = null!, _blendOpaque = null!;
    private ID3D11DepthStencilState _depthOff = null!, _depthWrite = null!, _depthRead = null!;
    private ID3D11RasterizerState _raster = null!;
    private ID3D11SamplerState _linear = null!, _point = null!;
    private ID3D11Buffer _frameBuffer = null!, _passBuffer = null!;
    private readonly DynamicStructuredBuffer<GpuNoteInstance> _notes;
    private readonly DynamicStructuredBuffer<GpuKeyInstance> _keys;
    private readonly DynamicStructuredBuffer<GpuSpriteInstance> _sprites;
    private readonly DynamicStructuredBuffer<Vector4> _keyColors;
    private readonly DynamicStructuredBuffer<GpuArQuad> _arQuads;
    private readonly GpuInstanceList<GpuArQuad> _arList = new(512);
    private readonly GpuArLayer _arLayer = new();
    private GpuArInput? _arInput;
    // the layers behind the notes (lanes, petals, ambient, note trails) need their own buffer:
    // SV_InstanceID restarts at 0 for every draw, so one buffer cannot serve two sprite batches
    private readonly DynamicStructuredBuffer<GpuSpriteInstance> _ambient;
    private const float KindWatermark = 16f;
    private readonly GpuInstanceList<GpuSpriteInstance> _ambientList = new(2048);
    private GpuNotePassQueryProfiler? _notePassProfiler;
    private GpuAdaptiveQualityController? _adaptiveQualityController;
    private GpuAdaptiveQualityQueryProfiler? _adaptiveQualityProfiler;
    private ID3D11Texture2D? _atlasTexture;
    private ID3D11ShaderResourceView? _atlasView;
    private long _atlasVersion = -1;
    private ID3D11Texture2D? _markTexture;
    private ID3D11ShaderResourceView? _markView;
    private long _markVersion = -1;
    private int _markWidth, _markHeight;
    private ID3D11Texture2D? _backgroundTexture;
    private ID3D11ShaderResourceView? _backgroundView;
    private float _backgroundAspect = 1;
    private GpuCameraTexture? _camera;
    private GpuCameraInput? _cameraInput;
    private long _backgroundVersion = -1;

    private GpuStageRenderer(ID3D11Device device, ID3D11DeviceContext context, FeatureLevel level, bool warp)
    {
        Device = device; Context = context; Level = level; IsWarp = warp;
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var adapter1 = adapter.QueryInterface<IDXGIAdapter1>();
            AdapterName = adapter1.Description1.Description;
        }
        catch { AdapterName = warp ? "Microsoft Basic Render Driver (WARP)" : "Direct3D 11 adapter"; }
        _notes = new DynamicStructuredBuffer<GpuNoteInstance>(device, 1024);
        _keys = new DynamicStructuredBuffer<GpuKeyInstance>(device, 128);
        _sprites = new DynamicStructuredBuffer<GpuSpriteInstance>(device, 4096);
        _keyColors = new DynamicStructuredBuffer<Vector4>(device, 128);
        _arQuads = new DynamicStructuredBuffer<GpuArQuad>(device, 512);
        _ambient = new DynamicStructuredBuffer<GpuSpriteInstance>(device, 2048);
        CreateStates();
        CompileShaders();
    }

    /// <summary>Creates the device (hardware first, WARP as the fallback) and compiles every shader.</summary>
    /// <remarks>
    /// With <paramref name="adapterRequest"/> the device is created on the one adapter whose DXGI
    /// description contains that text (case-insensitive, must be unique); a request that matches none or
    /// several throws with the machine's adapter list, so a hardware gate can never silently measure the
    /// wrong GPU. Without it the OS default adapter is used, exactly as the main window always has.
    /// </remarks>
    internal static GpuStageRenderer Create(bool forceWarp = false, string? adapterRequest = null)
    {
        var flags = DeviceCreationFlags.BgraSupport;
        if (!forceWarp && !string.IsNullOrWhiteSpace(adapterRequest))
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            var adapters = new List<IDXGIAdapter1>();
            var names = new List<string>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                adapters.Add(adapter);
                names.Add(adapter.Description1.Description);
            }
            try
            {
                var (index, problem) = GpuAdapterSelection.Select(names, adapterRequest);
                if (problem is not null) throw new InvalidOperationException(problem);
                // D3D11CreateDevice demands DriverType.Unknown when an explicit adapter is passed —
                // Hardware with a non-null adapter is E_INVALIDARG (it only works with the null adapter).
                var result = D3D11.D3D11CreateDevice(adapters[index], DriverType.Unknown, flags, FeatureLevels, out ID3D11Device device, out FeatureLevel level, out ID3D11DeviceContext context);
                result.CheckError();
                return new GpuStageRenderer(device, context, level, false);
            }
            finally
            {
                foreach (var adapter in adapters) adapter.Dispose();
            }
        }
        if (!forceWarp)
        {
            var result = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, flags, FeatureLevels, out ID3D11Device device, out FeatureLevel level, out ID3D11DeviceContext context);
            if (result.Success) return new GpuStageRenderer(device, context, level, false);
        }
        D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, flags, FeatureLevels, out ID3D11Device warpDevice, out FeatureLevel warpLevel, out ID3D11DeviceContext warpContext).CheckError();
        return new GpuStageRenderer(warpDevice, warpContext, warpLevel, true);
    }

    /// <summary>Enables optional D3D11 timestamp-query instrumentation for a diagnostic run only.</summary>
    internal void EnableNotePassProfiling(GpuNotePassSampleBuffer samples)
    {
        if (_notePassProfiler is not null) throw new InvalidOperationException("Note-pass timestamp profiling is already enabled.");
        _notePassProfiler = new GpuNotePassQueryProfiler(Device, Context, samples);
    }

    /// <summary>Enables runtime whole-stage GPU timing for the render loop's adaptive-quality controller.</summary>
    internal void EnableAdaptiveQualityProfiling(GpuAdaptiveQualityController quality)
    {
        if (_adaptiveQualityProfiler is not null) throw new InvalidOperationException("Adaptive-quality timestamp profiling is already enabled.");
        var profiler = new GpuAdaptiveQualityQueryProfiler(Device, Context, quality);
        _adaptiveQualityController = quality;
        _adaptiveQualityProfiler = profiler;
    }

    internal bool AdaptiveQualityProfilingAvailable => _adaptiveQualityProfiler is not null;

    /// <summary>
    /// Verification seam: an explicit AR budget override (roadmap §14.8) used only by <c>--verify</c>, so a WARP
    /// rehearsal can ask for a deeper cut than the timestamp profiler would choose by itself. Null in the app.
    /// </summary>
    internal GpuAdaptiveQualityTier? ArBudgetTierOverride { get; set; }

    /// <summary>Polls completed GPU timings without waiting; disables the optional probe if the driver reports an error.</summary>
    internal bool CollectAdaptiveQualitySamples()
    {
        if (_adaptiveQualityProfiler is null) return true;
        try
        {
            _adaptiveQualityProfiler.CollectReady();
            return true;
        }
        catch (Exception exception)
        {
            StopAdaptiveQualityProfiling(exception);
            return false;
        }
    }

    private int BeginAdaptiveQualitySample()
    {
        try { return _adaptiveQualityProfiler?.BeginFrame() ?? -1; }
        catch (Exception exception) { StopAdaptiveQualityProfiling(exception); return -1; }
    }

    private void EndAdaptiveQualitySample(int slot)
    {
        try { _adaptiveQualityProfiler?.EndFrame(slot); }
        catch (Exception exception) { StopAdaptiveQualityProfiling(exception); }
    }

    private void StopAdaptiveQualityProfiling(Exception exception)
    {
        System.Diagnostics.Trace.WriteLine($"Keyflow: adaptive-quality timestamp profiling stopped: {exception.Message}");
        _adaptiveQualityProfiler?.Dispose();
        _adaptiveQualityProfiler = null;
        _adaptiveQualityController?.ResetToFull();
        _adaptiveQualityController = null;
    }

    internal static string ShaderSource()
    {
        using var stream = typeof(GpuStageRenderer).Assembly.GetManifestResourceStream("PianoPath.Gpu.StageShaders.hlsl")
            ?? throw new InvalidOperationException("The embedded GPU stage shaders (Gpu/StageShaders.hlsl) are missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void CompileShaders()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var source = ShaderSource();
        ReadOnlyMemory<byte> Compile(string entry, string profile)
        {
            try { return Compiler.Compile(source, entry, "StageShaders.hlsl", profile, ShaderFlags.OptimizationLevel3); }
            catch (SharpGenException ex) { throw new InvalidOperationException($"HLSL {entry} ({profile}) failed to compile: {ex.Message}", ex); }
        }
        _vsFullscreen = Device.CreateVertexShader(Compile("VsFullscreen", "vs_5_0").Span);
        _vsNote = Device.CreateVertexShader(Compile("VsNote", "vs_5_0").Span);
        _vsKey = Device.CreateVertexShader(Compile("VsKey", "vs_5_0").Span);
        _vsSprite = Device.CreateVertexShader(Compile("VsSprite", "vs_5_0").Span);
        _vsArQuad = Device.CreateVertexShader(Compile("VsArQuad", "vs_5_0").Span);
        _psArQuad = Device.CreatePixelShader(Compile("PsArQuad", "ps_5_0").Span);
        _psBackground = Device.CreatePixelShader(Compile("PsBackground", "ps_5_0").Span);
        _psNote = Device.CreatePixelShader(Compile("PsNote", "ps_5_0").Span);
        _psKey = Device.CreatePixelShader(Compile("PsKey", "ps_5_0").Span);
        _psSprite = Device.CreatePixelShader(Compile("PsSprite", "ps_5_0").Span);
        _psPrefilter = Device.CreatePixelShader(Compile("PsBloomPrefilter", "ps_5_0").Span);
        _psDown = Device.CreatePixelShader(Compile("PsBloomDown", "ps_5_0").Span);
        _psUp = Device.CreatePixelShader(Compile("PsBloomUp", "ps_5_0").Span);
        _psComposite = Device.CreatePixelShader(Compile("PsComposite", "ps_5_0").Span);
        _psCompositeGeometry = Device.CreatePixelShader(Compile("PsCompositeGeometry", "ps_5_0").Span);
        _psCompositeGlow = Device.CreatePixelShader(Compile("PsCompositeGlow", "ps_5_0").Span);
        ShaderCompileMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    private void CreateStates()
    {
        _blendPremultiplied = Device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha));
        _blendAdditive = Device.CreateBlendState(new BlendDescription(Blend.One, Blend.One));
        _blendOpaque = Device.CreateBlendState(BlendDescription.Opaque);
        _depthOff = Device.CreateDepthStencilState(DepthStencilDescription.None);
        _depthWrite = Device.CreateDepthStencilState(DepthStencilDescription.Default);
        _depthRead = Device.CreateDepthStencilState(DepthStencilDescription.DepthRead);
        _raster = Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _linear = Device.CreateSamplerState(SamplerDescription.LinearClamp);
        _point = Device.CreateSamplerState(SamplerDescription.PointClamp);
        _frameBuffer = Device.CreateBuffer((uint)Marshal.SizeOf<GpuFrameConstants>(), BindFlags.ConstantBuffer);
        _passBuffer = Device.CreateBuffer((uint)Marshal.SizeOf<GpuPassConstants>(), BindFlags.ConstantBuffer);
    }

    /// <summary>Uploads a new background picture (BGRA, tightly packed) when the feed's version changed.</summary>
    internal void UpdateBackground(GpuBackgroundImage? image)
    {
        var version = image?.Version ?? 0;
        if (version == _backgroundVersion) return;
        _backgroundVersion = version;
        _backgroundView?.Dispose(); _backgroundTexture?.Dispose(); _backgroundView = null; _backgroundTexture = null;
        if (image is null || image.Width <= 0 || image.Height <= 0) return;
        _backgroundTexture = Device.CreateTexture2D(Format.B8G8R8A8_UNorm_SRgb, (uint)image.Width, (uint)image.Height, mipLevels: 1, bindFlags: BindFlags.ShaderResource);
        Context.UpdateSubresource(image.Pixels, _backgroundTexture, 0, (uint)(image.Width * 4));
        _backgroundView = Device.CreateShaderResourceView(_backgroundTexture);
        _backgroundAspect = image.Width / (float)image.Height;
    }

    /// <summary>
    /// Takes the newest camera frame from the feed's ring into the camera texture. Call once per loop iteration, from the
    /// render thread, before any <see cref="Render"/>: every output (preview, window, recording) then draws the same picture.
    /// Null removes the layer and frees the texture.
    /// </summary>
    internal void UpdateCamera(GpuCameraInput? input)
    {
        _cameraInput = input;
        if (input is null)
        {
            _camera?.Dispose(); _camera = null;
            return;
        }
        if (!input.Ring.TryTake(out var frame)) return;
        _camera ??= new GpuCameraTexture(Device);
        _camera.Upload(Context, frame);
    }

    /// <summary>Sets what the AR layer draws (null for none). Call next to <see cref="UpdateCamera"/>, from the render thread.</summary>
    internal void UpdateAr(GpuArInput? input) => _arInput = input;

    /// <summary>Sprites appended after the stage's own, for <c>VerifyArNoteProjection</c> only; never set by the app.</summary>
    internal GpuSpriteInstance[]? ExtraSprites { get; set; }

    private Vector4[]? _arKeyColors;

    /// <summary>
    /// The glow the AR layer lights the keys with instead of the simulation's own, or null for the simulation's: the loop hands in
    /// the glow of a camera-delay ago here. Set next to <see cref="UpdateAr"/>, from the render thread.
    /// </summary>
    internal void SetArKeyColors(Vector4[]? colors) => _arKeyColors = colors;

    /// <summary>Verification-only render seam: removes pressed-key tint/emission/spill, not static
    /// ivory lighting, press geometry, simulation glow, emitter input or particle colour. Default off.</summary>
    internal bool SuppressStageKeyLightingForReview { get; set; }
    internal bool SuppressStageKeyTintForReview { get; set; }
    internal bool SuppressStageKeySpillForReview { get; set; }
    internal bool SuppressArKeyTintForReview { get; set; }
    internal bool SuppressArKeySpillForReview { get; set; }
    internal Action<GpuKeyInstance>? ObserveStageKeyForReview { get; set; }
    internal Action<GpuStageSimulation, GpuLook>? ObserveParticleStateForReview { get; set; }
    internal Action<GpuArParticle>? ObserveStageParticleForReview { get; set; }
    internal Action<GpuArParticle>? ObserveArParticleForReview { get; set; }
    internal Action<GpuSpriteInstance>? ObserveHeldSpriteGeometryForReview { get; set; }
    internal Action<GpuArQuad>? ObserveHeldQuadGeometryForReview { get; set; }
    // Null in production; regression checks partition actual key-anchored and particle packets.
    internal bool SuppressBounceBodyForReview { get; set; }
    internal Action<GpuSpriteInstance>? ObserveStageSpriteForReview { get; set; }
    internal Action<GpuArQuad>? ObserveArQuadForReview { get; set; }


    internal static void MaskPressedKeyLightForReview(Span<GpuKeyInstance> keys, bool suppressTint = true, bool suppressSpill = true)
    {
        foreach (ref var key in keys)
            if (key.Misc.X < 1.5f)
            {
                if (suppressTint) key.Emit.W = 0;
                if (suppressSpill) key.Base.Y = 0;
            }
    }

    /// <summary>How many instances the stage-space note pass built in the last frame; camera regressions ensure it is zero over camera.</summary>
    internal int StageNoteCount { get; private set; }
    /// <summary>How many stage-space falling-note trails were built in the last frame.</summary>
    internal int StageNoteTrailCount { get; private set; }
    /// <summary>How many virtual key bodies the stage pass built; camera output must never use uncalibrated stage keys.</summary>
    internal int StageKeyCount { get; private set; }
    /// <summary>How many stage-space transient sprites were built before drawing the last frame.</summary>
    internal int StageSpriteCount { get; private set; }

    /// <summary>How many quads the last frame drew into the AR layer; verification reads it.</summary>
    internal int ArQuadCount => _arList.Count;
    /// <summary>The AR list minus the scene elements (damper strip, alignment guides): the count every per-note/per-key
    /// assertion means by AR quads. Both counters answer the last built frame, so an idle layer must never subtract
    /// more than the list holds.</summary>
    internal int ArObjectQuadCount => Math.Max(0,
        _arList.Count - Math.Min(_arList.Count, _arLayer.DamperStripCount + _arLayer.AlignmentGuideCount));
    /// <summary>How many song-note quads the AR layer built, excluding key tint and Live Stage light spill.</summary>
    internal int ArShapeCount => _arLayer.ShapeCount;
    internal int AmbientCount => _ambientList.Count;

    /// <summary>How many AR quads were lit-key patches; notes, bars, light spill and Live Stage effects are counted separately.</summary>
    internal int ArKeyQuadCount => _arLayer.KeyQuadCount;

    /// <summary>How many of those were bars of keys the player is pressing; verification reads it.</summary>
    internal int ArLiveBarCount => _arLayer.LiveBarCount;
    internal int ArRisingMidiNoteCount => _arLayer.RisingMidiNoteCount;
    internal int ArNoteTrailQuadCount => _arLayer.NoteTrailQuadCount;
    internal int ArGhostNoteQuadCount => _arLayer.GhostNoteQuadCount;
    internal int ArLandingGlowCount => _arLayer.LandingGlowCount;
    internal int ArNoteLabelCount => _arLayer.NoteLabelCount;
    internal int ArImpactLightCount => _arLayer.ImpactLightCount;
    internal int ArDamperStripCount => _arLayer.DamperStripCount;
    /// <summary>How many alignment-check guide quads the last calibrated frame drew; verification reads it.</summary>
    internal int ArAlignmentGuideCount => _arLayer.AlignmentGuideCount;
    internal int ArNoteLightCount => _arLayer.NoteLightCount;
    internal int ArPulseCount => _arLayer.PulseCount;
    internal int ArParticleFxCount => _arLayer.ParticleFxCount;
    internal int ArFlashCount => _arLayer.FlashCount;
    internal int ArHoldArcCount => _arLayer.HoldArcCount;
    internal int ArBeamCount => _arLayer.BeamCount;
    internal int ArReleaseEffectCount => _arLayer.ReleaseEffectCount;

    /// <summary>The camera texture (null when there is no layer or no frame yet); verification reads its counters.</summary>
    internal GpuCameraTexture? CameraTexture => _camera;

    /// <summary>True when the next frame draws the camera picture: a layer is set, a frame has arrived, and the output is not a chroma-key frame.</summary>
    private bool CameraActive(GpuLook look) => _camera?.View is not null && _cameraInput is not null && !look.Chroma;

    /// <summary>Uploads the glyph atlas (note names, Matrix Rain glyphs) once; coverage is read from its alpha channel.</summary>
    internal void UpdateAtlas(GpuBackgroundImage? image)
    {
        var version = image?.Version ?? 0;
        if (version == _atlasVersion) return;
        _atlasVersion = version;
        _atlasView?.Dispose(); _atlasTexture?.Dispose(); _atlasView = null; _atlasTexture = null;
        if (image is null || image.Width <= 0 || image.Height <= 0) return;
        _atlasTexture = Device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)image.Width, (uint)image.Height, mipLevels: 1, bindFlags: BindFlags.ShaderResource);
        Context.UpdateSubresource(image.Pixels, _atlasTexture, 0, (uint)(image.Width * 4));
        _atlasView = Device.CreateShaderResourceView(_atlasTexture);
    }

    /// <summary>Uploads the export watermark strip once, like the atlas; sprite kind 16 samples its alpha to tint the line.</summary>
    internal void UpdateWatermark(GpuBackgroundImage? image)
    {
        var version = image?.Version ?? 0;
        if (version == _markVersion) return;
        _markVersion = version;
        _markView?.Dispose(); _markTexture?.Dispose(); _markView = null; _markTexture = null;
        if (image is null || image.Width <= 0 || image.Height <= 0) { _markWidth = _markHeight = 0; return; }
        _markWidth = image.Width; _markHeight = image.Height;
        _markTexture = Device.CreateTexture2D(Format.B8G8R8A8_UNorm, (uint)image.Width, (uint)image.Height, mipLevels: 1, bindFlags: BindFlags.ShaderResource);
        Context.UpdateSubresource(image.Pixels, _markTexture, 0, (uint)(image.Width * 4));
        _markView = Device.CreateShaderResourceView(_markTexture);
    }

    /// <summary>Renders one frame of the stage into <paramref name="output"/> (a BGRA8 render target of the target's size).</summary>
    /// <param name="alphaOutput">Writes premultiplied alpha-effects pixels without a camera or stage background.</param>
    /// <param name="alphaGeometryOutput">Optional base-geometry output for the split geometry/glow stream mode.</param>
    /// <param name="alphaGlowOutput">Optional bloom-only output for the split geometry/glow stream mode.</param>
    internal void Render(GpuRenderTarget target, ID3D11RenderTargetView? output, GpuStageSimulation simulation, GpuFrameInput input, GpuSceneLayout layout,
        GpuInstanceList<GpuNoteInstance> notes, GpuInstanceList<GpuKeyInstance> keys, GpuInstanceList<GpuSpriteInstance> sprites,
        GpuAdaptiveQualityTier qualityTier = GpuAdaptiveQualityTier.Full, bool sampleAdaptiveQuality = false, bool alphaOutput = false,
        ID3D11RenderTargetView? alphaGeometryOutput = null, ID3D11RenderTargetView? alphaGlowOutput = null)
    {
        _notePassProfiler?.CollectReady();
        var quality = GpuAdaptiveQualityProfile.For(qualityTier);
        var look = input.Look;
        var cameraActive = CameraActive(look);
        var alphaOverlayActive = !alphaOutput || _arInput is not null && cameraActive;
        // Notes in perspective are owned by GpuArLayer and built from the calibration's metric 3D camera pose.
        // Do not gate them on the old scene→image homography: that map has a horizon, and using it as a presence
        // check made notes disappear when the flat map could not cover a scene rectangle. The shader's old map branch
        // stays closed; its 2D mapping is not the active AR note path.
        GpuArInput? projectedInput = null;
        if (_arInput is { ProjectedNotes: true } arInput && arInput.Calibration.CameraPose is not null
            && TryCameraView(target, look, out _)
            && _camera!.Width == arInput.Calibration.ImageWidth && _camera.Height == arInput.Calibration.ImageHeight)
            projectedInput = arInput;
        var arProjectedNotes = projectedInput is not null;

        // A full-screen camera background never receives scene-space notes or trails, calibrated or not.
        // Virtual key bodies and uncalibrated stage sprites are gated below by cameraActive as well; only the calibrated
        // GpuArLayer may add key tint, live bars or projected Live Stage effects over the camera.
        simulation.BuildNotes(input, layout, notes, songNotes: projectedInput?.ShowNotes ?? true,
            onStage: !cameraActive && !look.OverCamera && !arProjectedNotes, minAspect: projectedInput?.MinAspect ?? 0,
            liveTrails: !cameraActive && !look.OverCamera && !arProjectedNotes);
        StageNoteCount = notes.Count;
        StageNoteTrailCount = simulation.NoteTrailCount;
        simulation.BuildKeys(look, layout, keys, cameraActive,
            out var frontHeight, out var blackLength, out var blackHeight, out var whiteWidth, out var blackWidth);
        if (!cameraActive && (SuppressStageKeyLightingForReview || SuppressStageKeyTintForReview || SuppressStageKeySpillForReview))
            MaskPressedKeyLightForReview(keys.Writable, SuppressStageKeyLightingForReview || SuppressStageKeyTintForReview,
                SuppressStageKeyLightingForReview || SuppressStageKeySpillForReview);
        ObserveParticleStateForReview?.Invoke(simulation, look);
        simulation.ObservePackedHeldParticleForReview = ObserveStageParticleForReview;
        StageKeyCount = keys.Count;
        if (ObserveStageKeyForReview is { } observeKey)
            foreach(var key in keys.Span) observeKey(key);
        simulation.BuildSprites(look, layout, sprites, cameraOverlayActive: cameraActive);
        if(SuppressBounceBodyForReview)
            for(var i=0;i<sprites.Count;i++)
                if(sprites.Writable[i].PosSize.W==BounceParticleAppearance.Kind)sprites.Writable[i].Color=default;
        if (ObserveStageSpriteForReview is { } observeAllSprites)
            foreach (var sprite in sprites.Span) observeAllSprites(sprite);
        if (ObserveHeldSpriteGeometryForReview is { } observeSprite)
            foreach (var sprite in sprites.Span)
                if (sprite.PosSize.W == 17) observeSprite(sprite);
        // test-only hook: the verification suite adds sprites of its own (a sprite at a known scene point) to read the sprite pass back
        if (ExtraSprites is { } extraSprites) foreach (var extra in extraSprites) sprites.Add(in extra);
        // The export watermark draws on its own terms: a whole-line text sprite in front of the keys,
        // independent of the camera gate that quietens the scene sprites.
        if (look.ShowWatermark && _markView is not null
                && ArVideoWatermark.Place(layout.Width, layout.Height, layout.HitY, _markWidth, _markHeight) is { } mark)
            sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(mark.CenterX, mark.CenterY, mark.HalfHeight, KindWatermark),
                Color = new Vector4(GpuStageSimulation.ToLinear(new Vector3(232f / 255f, 224f / 255f, 250f / 255f)), 1),
                Dir = new Vector4(1, 0, mark.Aspect, 0) });
        StageSpriteCount = sprites.Count;
        simulation.BuildAmbient(look, layout, _ambientList, quality.AmbientDensity, cameraOverlayActive: cameraActive);

        var constants = BuildConstants(target, input, layout, simulation, frontHeight, blackLength, blackHeight, whiteWidth, blackWidth, GpuArMap.None, alphaOutput);
        var context = Context;
        context.UpdateSubresource(in constants, _frameBuffer);
        _notes.Upload(context, notes.Span);
        _keys.Upload(context, keys.Span);
        _sprites.Upload(context, sprites.Span);
        _ambient.Upload(context, _ambientList.Span);
        _keyColors.Upload(context, simulation.KeyColors);
        // the AR layer draws only over the picture the calibration was made on: a camera that changed size needs a new calibration
        _arList.Clear();
        // roadmap §14.8: every AR build hears the adaptive tier (the verify seam first, the profiler otherwise) and
        // drops what roadmap's sentence says, never what it forbids.
        var arBudgetTier = ArBudgetTierOverride ?? _adaptiveQualityController?.Tier ?? GpuAdaptiveQualityTier.Full;
        _arLayer.ObservePackedParticleForReview = ObserveArParticleForReview;
        if (_arInput is { } ar && TryCameraView(target, look, out var arView) && _camera!.Width == ar.Calibration.ImageWidth && _camera.Height == ar.Calibration.ImageHeight)
            _arLayer.Build(input, ar, arView, _arKeyColors ?? simulation.KeyColors, simulation, _arList, arProjectedNotes, arBudgetTier,
                SuppressArKeyTintForReview, SuppressArKeySpillForReview);
        if(SuppressBounceBodyForReview)
            for(var i=0;i<_arList.Count;i++)
                if(_arList.Writable[i].Misc.Z==BounceParticleAppearance.Kind)_arList.Writable[i].Color=default;
        if (ObserveArQuadForReview is { } observeArQuad)
            foreach (var quad in _arList.Span) observeArQuad(quad);
        if (ObserveHeldQuadGeometryForReview is { } observeQuad)
            foreach (var quad in _arList.Span)
                if (quad.Misc.Z == 17) observeQuad(quad);
        _arQuads.Upload(context, _arList.Span);

        context.ClearState();
        var qualityQuerySlot = sampleAdaptiveQuality ? BeginAdaptiveQualitySample() : -1;
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.RSSetState(_raster);
        context.VSSetConstantBuffer(0, _frameBuffer);
        context.PSSetConstantBuffer(0, _frameBuffer);
        context.VSSetConstantBuffer(1, _passBuffer);
        context.PSSetConstantBuffer(1, _passBuffer);
        context.PSSetSampler(0, _linear);
        context.PSSetSampler(1, _point);

        // ---- scene (HDR) ----
        context.ClearRenderTargetView(target.HdrRtv!, new Color4(0, 0, 0, 0));
        context.ClearDepthStencilView(target.DepthView!, DepthStencilClearFlags.Depth, 1f, 0);
        context.OMSetRenderTargets(target.HdrRtv!, target.DepthView);
        context.RSSetViewport(new Viewport(target.Width, target.Height));

        context.OMSetBlendState(_blendOpaque);
        context.OMSetDepthStencilState(_depthOff, 0);
        context.VSSetShader(_vsFullscreen);
        // The camera picture replaces the backdrop in the normal composite; transparent AR output omits both backdrop and camera.
        if (!alphaOutput && !cameraActive)
        {
            context.PSSetShader(_psBackground);
            if (_backgroundView is not null && look.ShowBackground && !look.Chroma) context.PSSetShaderResource(1, _backgroundView);
            context.Draw(3, 0);
            context.PSUnsetShaderResource(1);
        }

        if (_atlasView is not null) context.PSSetShaderResource(6, _atlasView);
        if (_markView is not null) context.PSSetShaderResource(8, _markView);
        if (alphaOverlayActive && _ambientList.Count > 0)
        {
            // Ambient petals, meteors and other atmosphere are always emitted screen-space; the 3D note path
            // lives in the separate AR quad pass and never changes the sprite projection constants.
            context.OMSetBlendState(_blendPremultiplied);
            context.VSSetShader(_vsSprite);
            context.PSSetShader(_psSprite);
            context.VSSetShaderResource(4, _ambient.View);
            context.PSSetShaderResource(5, _keyColors.View);
            context.DrawInstanced(6, (uint)_ambientList.Count, 0, 0);
        }

        if (alphaOverlayActive && notes.Count > 0)
        {
            context.OMSetBlendState(_blendPremultiplied);
            context.VSSetShader(_vsNote);
            context.PSSetShader(_psNote);
            context.VSSetShaderResource(2, _notes.View);
            var notePassQuery = _notePassProfiler?.BeginDraw(notes.Count) ?? -1;
            context.DrawInstanced(6, (uint)notes.Count, 0, 0);
            _notePassProfiler?.EndDraw(notePassQuery);
        }
        if (alphaOverlayActive && keys.Count > 0)
        {
            context.OMSetBlendState(_blendOpaque);
            context.OMSetDepthStencilState(_depthWrite, 0);
            context.VSSetShader(_vsKey);
            context.PSSetShader(_psKey);
            context.VSSetShaderResource(3, _keys.View);
            context.DrawInstanced(30, (uint)keys.Count, 0, 0);
        }
        if (alphaOverlayActive && sprites.Count > 0)
        {
            context.OMSetBlendState(_blendPremultiplied);
            context.OMSetDepthStencilState(_depthRead, 0);
            context.VSSetShader(_vsSprite);
            context.PSSetShader(_psSprite);
            context.VSSetShaderResource(4, _sprites.View);
            context.PSSetShaderResource(5, _keyColors.View);
            context.DrawInstanced(6, (uint)sprites.Count, 0, 0);
        }

        // ---- AR layer: notes projected onto the camera picture ----
        if (alphaOverlayActive && _arList.Count > 0)
        {
            context.OMSetBlendState(_blendPremultiplied);
            context.OMSetDepthStencilState(_depthOff, 0);
            context.VSSetShader(_vsArQuad);
            context.PSSetShader(_psArQuad);
            context.VSSetShaderResource(8, _arQuads.View);
            context.DrawInstanced(6, (uint)_arList.Count, 0, 0);
        }

        // ---- bloom pyramid ----
        context.OMSetDepthStencilState(_depthOff, 0);
        context.VSSetShader(_vsFullscreen);
        var levels = Math.Min(target.BloomCount, quality.MaxBloomLevels);
        if (look.BloomIntensity > 0 && levels > 0)
        {
            context.OMSetBlendState(_blendOpaque);
            Pass(target.BloomRtv[0]!, target.BloomSize[0], _psPrefilter, target.HdrSrv!, 1f / target.Width, 1f / target.Height, .2f, 0); // narrow knee: bloom starts near 1.44, above any lit surface
            for (var i = 1; i < levels; i++)
                Pass(target.BloomRtv[i]!, target.BloomSize[i], _psDown, target.BloomSrv[i - 1]!, 1f / target.BloomSize[i - 1].Width, 1f / target.BloomSize[i - 1].Height, 0, 0);
            context.OMSetBlendState(_blendAdditive);
            var radius = .75f + look.BloomSize * 1.1f;
            for (var i = levels - 1; i >= 1; i--)
                Pass(target.BloomRtv[i - 1]!, target.BloomSize[i - 1], _psUp, target.BloomSrv[i]!, 1f / target.BloomSize[i].Width, 1f / target.BloomSize[i].Height, radius, 1);
        }
        else if (levels > 0)
        {
            context.ClearRenderTargetView(target.BloomRtv[0]!, new Color4(0, 0, 0, 0));
        }

        // ---- composite outputs ----
        context.OMSetBlendState(_blendOpaque);
        context.RSSetViewport(new Viewport(target.Width, target.Height));
        if (output is not null)
            CompositeTo(output, _psComposite, cameraActive);
        if (alphaGeometryOutput is not null)
            CompositeTo(alphaGeometryOutput, _psCompositeGeometry);
        if (alphaGlowOutput is not null)
            CompositeTo(alphaGlowOutput, _psCompositeGlow);
        if (qualityQuerySlot >= 0) EndAdaptiveQualitySample(qualityQuerySlot);

        void CompositeTo(ID3D11RenderTargetView destination, ID3D11PixelShader shader, bool sampleCamera = false)
        {
            // Unbind the scene HDR target before sampling it. Binding its SRV first is a D3D11 read/write
            // hazard: the runtime silently nulls that SRV while the same texture is still bound as an RTV,
            // producing a black composite (caught by the WARP Gem/Crystal pixel checks).
            context.OMSetRenderTargets(destination);
            context.PSSetShader(shader);
            context.PSSetShaderResource(0, target.HdrSrv!);
            context.PSSetShaderResource(1, target.BloomSrv[0]!);
            if (sampleCamera) context.PSSetShaderResource(7, _camera!.View!);
            context.Draw(3, 0);
            context.PSUnsetShaderResources(0, 2);
            if (sampleCamera) context.PSUnsetShaderResource(7);
        }
    }

    private void Pass(ID3D11RenderTargetView destination, (int Width, int Height) size, ID3D11PixelShader shader, ID3D11ShaderResourceView source, float texelX, float texelY, float z, float w)
    {
        var context = Context;
        context.PSUnsetShaderResource(0);
        context.OMSetRenderTargets(destination);
        context.RSSetViewport(new Viewport(size.Width, size.Height));
        var pass = new GpuPassConstants { PassA = new Vector4(texelX, texelY, z, w) };
        context.UpdateSubresource(in pass, _passBuffer);
        context.PSSetShader(shader);
        context.PSSetShaderResource(0, source);
        context.Draw(3, 0);
        context.PSUnsetShaderResource(0);
    }

    private static float BackgroundMotionId(string style) => style switch
    {
        "Aurora" => 1, "Nebula" => 2, "Prism" => 3, "Ember Haze" => 4, "Ocean Flow" => 5, "Retro Grid" => 6, _ => 0
    };

    private static float HaloMotionId(string style) => style switch
    {
        "Pulse" => 1, "Sweep" => 2, "Twin Comets" => 3, "Spectrum" => 4, "Electric Arc" => 5, "Ripple" => 6, _ => 0
    };

    private GpuFrameConstants BuildConstants(GpuRenderTarget target, GpuFrameInput input, GpuSceneLayout layout, GpuStageSimulation simulation,
        float frontHeight, float blackLength, float blackHeight, float whiteWidth, float blackWidth, GpuArMap arMap, bool alphaOutput)
    {
        var look = input.Look;
        var pixelsPerUnit = target.Height / Math.Max(1f, layout.Height);
        var scaleX = target.Width / Math.Max(1f, layout.Width);
        // camera zoom / offset / parallax, as the software stage computes them (in stage DIPs)
        var zoom = look.CameraZoom;
        var offsetX = (layout.Width - layout.Width * zoom) * look.CameraOffset + (input.PointerX - .5f) * look.CameraParallax * 28;
        var offsetY = (layout.Height - layout.Height * zoom) * .5f + (input.PointerY - .5f) * look.CameraParallax * 20;
        var beat = look.TempoSync ? input.BeatPulse * look.TempoSyncAmount : 0;
        var aura = look.ShowBackground && look.BackgroundGradient && !look.Chroma ? .22f * look.BloomIntensity / .65f : 0;
        // the software star field: one star per 5200 DIP² at full density (a 26 DIP cell is 676 DIP²), drawn faint
        var stars = look.ShowBackground && look.ShowStars && !look.Chroma ? Math.Max(.02f, look.StarDensity * 676f / 5200f) : 0;
        var edge = .9f + look.NoteEdgeWidth * 3.4f;
        return new GpuFrameConstants
        {
            ScreenTime = new Vector4(target.Width, target.Height, (float)simulation.Time, layout.HitY),
            SceneSize = new Vector4(layout.Width, layout.Height, pixelsPerUnit, look.ShowKeyShadow ? 1 : 0),
            Camera = new Vector4(zoom * scaleX, zoom * pixelsPerUnit, offsetX * scaleX, offsetY * pixelsPerUnit),
            Background = new Vector4(GpuStageSimulation.ToLinear(look.BackgroundColor), look.Chroma ? 1 : 0),
            Aura = new Vector4(.19f, .03f, .42f, aura),
            // the software stage's horizon: peak alpha glow x 1.7 / 255 in sRGB (0.667 at full), a little brighter while keys sound
            Horizon = new Vector4(simulation.HorizonColor, MathF.Pow(look.HorizonGlow * .667f, 2.2f) * (1 + simulation.Activity * .5f) * (1 + beat)),
            SceneA = new Vector4(stars, .14f, _backgroundView is not null && look.ShowBackground && !look.Chroma ? 1 : 0, look.BackgroundDim),
            SceneB = new Vector4(_backgroundAspect, 1, beat, .88f),
            NoteA = new Vector4(look.NoteStyle, 2 + look.NoteRoundness * 12, edge, 5 + look.BloomSize * 16),
            NoteB = new Vector4(look.NoteTint, look.NoteEdge * 1.15f, look.NoteHeadGlow, look.NoteRefraction),
            NoteD = new Vector4(look.NoteSatin ? 1 : 0, look.KeyboardSoftboxReferenceForReview, 0, 0),
            NoteC = new Vector4(look.NoteTexture, look.Notes3D ? 1 : 0, look.NoteGlow * look.BloomIntensity / .65f * .75f * (1 + beat * .5f) * simulation.EnergyBoost, 150),
            KeyA = new Vector4(look.ShaderKeyLight, look.ShaderShadows, look.ShaderAmbientOcclusion, look.ShaderGloss),
            KeyB = new Vector4(look.ShaderRimLight, look.ShaderEmissive * 1.7f * simulation.PedalBoost, look.ShaderCameraTilt, look.KeyboardStyle),
            KeyC = new Vector4(layout.KeyboardHeight, blackLength, look.KeyLighting, frontHeight),
            KeyD = new Vector4(whiteWidth, blackWidth, blackHeight, .22f),
            RimColor = new Vector4(GpuStageSimulation.ToLinear(look.HaloColor), 40 + look.HorizonGlow * 260),
            Post = new Vector4(look.ShaderExposure, look.ShaderFilmic ? 1 : 0, 1, 1),
            // threshold 1.8: the lit white keys reach about 1.4 and must not bloom (their blur greys the
            // black keys); notes, sparks, flares and pressed keys are emissive and sit well above it
            Post2 = new Vector4(look.Chroma ? 0 : look.Vignette * .9f, look.BloomIntensity * .85f, 1.8f, .8f / 255f),
            SceneFx = new Vector4(look.ShowBackground && !look.Chroma ? BackgroundMotionId(look.BackgroundMotion) : 0,
                look.BackgroundMotionAmount, look.BackgroundMotionSpeed, simulation.Activity),
            SceneFxColor = new Vector4(GpuStageSimulation.ToLinear(look.BackgroundMotionColor), 1),
            HitFx = new Vector4(look.ShowHalo && look.HaloPulse ? HaloMotionId(look.HaloPulseStyle) : 0,
                look.HaloPulseIntensity * simulation.GlowBoost, look.HaloPulseSpeed, simulation.Activity),
            KeyRange = new Vector4(layout.Keys.FirstPitch, Math.Max(1, layout.Keys.KeyCount - 1), 0, 0),
            ArPicture = ComputeArPicture(target, look, alphaOutput, out var arFlags),
            ArFlags = arFlags,
            ArMatrix0 = arMap.Row0,
            ArMatrix1 = arMap.Row1,
            ArMatrix2 = arMap.Row2,
            ArMatrixFlags = arMap.Flags
        };
    }

    /// <summary>Where the camera picture lands on this output; false when no camera picture is drawn.</summary>
    private bool TryCameraView(GpuRenderTarget target, GpuLook look, out ArView view)
    {
        view = default;
        if (!CameraActive(look)) return false;
        var input = _cameraInput!;
        view = new ArView(_camera!.Width, _camera.Height, target.Width, target.Height, input.Fit, input.Mirror);
        return view.Valid;
    }

    /// <summary>Where the camera picture lands on this output and composite flags: x mirror, y camera active, z opacity, w transparent alpha-effects output.</summary>
    private Vector4 ComputeArPicture(GpuRenderTarget target, GpuLook look, bool alphaOutput, out Vector4 flags)
    {
        flags = new Vector4(0, 0, 0, alphaOutput ? 1 : 0);
        if (!TryCameraView(target, look, out var view)) return Vector4.Zero;
        var input = _cameraInput!;
        flags.X = input.Mirror ? 1 : 0;
        flags.Y = 1;
        flags.Z = Math.Clamp(input.Opacity, 0f, 1f);
        return new Vector4((float)view.Left, (float)view.Top, (float)view.Width, (float)view.Height);
    }

    public void Dispose()
    {
        try { Context.ClearState(); Context.Flush(); } catch { }
        _notes.Dispose(); _keys.Dispose(); _sprites.Dispose(); _keyColors.Dispose(); _arQuads.Dispose(); _ambient.Dispose();
        _atlasView?.Dispose(); _atlasTexture?.Dispose();
        _markView?.Dispose(); _markTexture?.Dispose();
        _backgroundView?.Dispose(); _backgroundTexture?.Dispose();
        _camera?.Dispose();
        _notePassProfiler?.Dispose();
        _notePassProfiler = null;
        _adaptiveQualityProfiler?.Dispose();
        _adaptiveQualityProfiler = null;
        _adaptiveQualityController = null;
        foreach (var disposable in new IDisposable?[] { _vsFullscreen, _vsNote, _vsKey, _vsSprite, _vsArQuad, _psArQuad, _psBackground, _psNote, _psKey, _psSprite, _psPrefilter, _psDown, _psUp, _psComposite, _psCompositeGeometry, _psCompositeGlow,
            _blendPremultiplied, _blendAdditive, _blendOpaque, _depthOff, _depthWrite, _depthRead, _raster, _linear, _point, _frameBuffer, _passBuffer })
            disposable?.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}

/// <summary>A dynamic structured buffer that grows when a frame needs more instances than it holds.</summary>
internal sealed class DynamicStructuredBuffer<T> : IDisposable where T : unmanaged
{
    private readonly ID3D11Device _device;
    private ID3D11Buffer _buffer = null!;
    private int _capacity;
    internal ID3D11ShaderResourceView View { get; private set; } = null!;

    internal DynamicStructuredBuffer(ID3D11Device device, int capacity)
    {
        _device = device;
        Allocate(capacity);
    }

    private void Allocate(int capacity)
    {
        View?.Dispose(); _buffer?.Dispose();
        _capacity = capacity;
        var stride = (uint)Marshal.SizeOf<T>();
        _buffer = _device.CreateBuffer((uint)capacity * stride, BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, stride);
        // A structured buffer's view needs no description: D3D11 derives the element count from the stride.
        View = _device.CreateShaderResourceView(_buffer);
    }

    internal void Upload(ID3D11DeviceContext context, ReadOnlySpan<T> items)
    {
        if (items.Length == 0) return;
        if (items.Length > _capacity) Allocate(Math.Max(items.Length, _capacity * 2));
        var mapped = context.Map(_buffer, 0, MapMode.WriteDiscard, MapFlags.None);
        items.CopyTo(mapped.AsSpan<T>(_capacity));
        context.Unmap(_buffer, 0);
    }

    public void Dispose() { View?.Dispose(); _buffer?.Dispose(); }
}

/// <summary>A decoded background picture handed from the UI thread to the renderer (BGRA, tightly packed).</summary>
internal sealed class GpuBackgroundImage
{
    internal required byte[] Pixels { get; init; }
    internal required int Width { get; init; }
    internal required int Height { get; init; }
    internal required long Version { get; init; }
}
