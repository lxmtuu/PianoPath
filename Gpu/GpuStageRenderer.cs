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
    public Vector4 NoteA, NoteB, NoteC, KeyA, KeyB, KeyC, KeyD, RimColor, Post, Post2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GpuPassConstants { public Vector4 PassA; }

/// <summary>
/// The size-dependent resources of one output: the HDR scene, its depth buffer and the bloom pyramid.
/// The embedded preview and the stage window each own one, so both can render from the same device.
/// </summary>
internal sealed class GpuRenderTarget : IDisposable
{
    internal const int BloomLevels = 6;
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

    private ID3D11VertexShader _vsFullscreen = null!, _vsNote = null!, _vsKey = null!, _vsSprite = null!;
    private ID3D11PixelShader _psBackground = null!, _psNote = null!, _psKey = null!, _psSprite = null!, _psPrefilter = null!, _psDown = null!, _psUp = null!, _psComposite = null!;
    private ID3D11BlendState _blendPremultiplied = null!, _blendAdditive = null!, _blendOpaque = null!;
    private ID3D11DepthStencilState _depthOff = null!, _depthWrite = null!, _depthRead = null!;
    private ID3D11RasterizerState _raster = null!;
    private ID3D11SamplerState _linear = null!, _point = null!;
    private ID3D11Buffer _frameBuffer = null!, _passBuffer = null!;
    private readonly DynamicStructuredBuffer<GpuNoteInstance> _notes;
    private readonly DynamicStructuredBuffer<GpuKeyInstance> _keys;
    private readonly DynamicStructuredBuffer<GpuSpriteInstance> _sprites;
    private readonly DynamicStructuredBuffer<Vector4> _keyColors;
    private ID3D11Texture2D? _backgroundTexture;
    private ID3D11ShaderResourceView? _backgroundView;
    private float _backgroundAspect = 1;
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
        CreateStates();
        CompileShaders();
    }

    /// <summary>Creates the device (hardware first, WARP as the fallback) and compiles every shader.</summary>
    internal static GpuStageRenderer Create(bool forceWarp = false)
    {
        var flags = DeviceCreationFlags.BgraSupport;
        if (!forceWarp)
        {
            var result = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, flags, FeatureLevels, out ID3D11Device device, out FeatureLevel level, out ID3D11DeviceContext context);
            if (result.Success) return new GpuStageRenderer(device, context, level, false);
        }
        D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, flags, FeatureLevels, out ID3D11Device warpDevice, out FeatureLevel warpLevel, out ID3D11DeviceContext warpContext).CheckError();
        return new GpuStageRenderer(warpDevice, warpContext, warpLevel, true);
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
        _psBackground = Device.CreatePixelShader(Compile("PsBackground", "ps_5_0").Span);
        _psNote = Device.CreatePixelShader(Compile("PsNote", "ps_5_0").Span);
        _psKey = Device.CreatePixelShader(Compile("PsKey", "ps_5_0").Span);
        _psSprite = Device.CreatePixelShader(Compile("PsSprite", "ps_5_0").Span);
        _psPrefilter = Device.CreatePixelShader(Compile("PsBloomPrefilter", "ps_5_0").Span);
        _psDown = Device.CreatePixelShader(Compile("PsBloomDown", "ps_5_0").Span);
        _psUp = Device.CreatePixelShader(Compile("PsBloomUp", "ps_5_0").Span);
        _psComposite = Device.CreatePixelShader(Compile("PsComposite", "ps_5_0").Span);
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

    /// <summary>Renders one frame of the stage into <paramref name="output"/> (a BGRA8 render target of the target's size).</summary>
    internal void Render(GpuRenderTarget target, ID3D11RenderTargetView output, GpuStageSimulation simulation, GpuFrameInput input, GpuSceneLayout layout,
        GpuInstanceList<GpuNoteInstance> notes, GpuInstanceList<GpuKeyInstance> keys, GpuInstanceList<GpuSpriteInstance> sprites)
    {
        var look = input.Look;
        simulation.BuildNotes(input, layout, notes);
        simulation.BuildKeys(look, layout, keys, out var frontHeight, out var blackLength, out var blackHeight, out var whiteWidth, out var blackWidth);
        simulation.BuildSprites(look, layout, sprites);

        var constants = BuildConstants(target, input, layout, simulation, frontHeight, blackLength, blackHeight, whiteWidth, blackWidth);
        var context = Context;
        context.UpdateSubresource(in constants, _frameBuffer);
        _notes.Upload(context, notes.Span);
        _keys.Upload(context, keys.Span);
        _sprites.Upload(context, sprites.Span);
        _keyColors.Upload(context, simulation.KeyColors);

        context.ClearState();
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
        context.PSSetShader(_psBackground);
        if (_backgroundView is not null && look.ShowBackground && !look.Chroma) context.PSSetShaderResource(1, _backgroundView);
        context.Draw(3, 0);
        context.PSUnsetShaderResource(1);

        if (notes.Count > 0)
        {
            context.OMSetBlendState(_blendPremultiplied);
            context.VSSetShader(_vsNote);
            context.PSSetShader(_psNote);
            context.VSSetShaderResource(2, _notes.View);
            context.DrawInstanced(6, (uint)notes.Count, 0, 0);
        }
        if (keys.Count > 0)
        {
            context.OMSetBlendState(_blendOpaque);
            context.OMSetDepthStencilState(_depthWrite, 0);
            context.VSSetShader(_vsKey);
            context.PSSetShader(_psKey);
            context.VSSetShaderResource(3, _keys.View);
            context.DrawInstanced(30, (uint)keys.Count, 0, 0);
        }
        if (sprites.Count > 0)
        {
            context.OMSetBlendState(_blendPremultiplied);
            context.OMSetDepthStencilState(_depthRead, 0);
            context.VSSetShader(_vsSprite);
            context.PSSetShader(_psSprite);
            context.VSSetShaderResource(4, _sprites.View);
            context.PSSetShaderResource(5, _keyColors.View);
            context.DrawInstanced(6, (uint)sprites.Count, 0, 0);
        }

        // ---- bloom pyramid ----
        context.OMSetDepthStencilState(_depthOff, 0);
        context.VSSetShader(_vsFullscreen);
        var levels = target.BloomCount;
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

        // ---- composite to the output ----
        context.OMSetBlendState(_blendOpaque);
        context.OMSetRenderTargets(output);
        context.RSSetViewport(new Viewport(target.Width, target.Height));
        context.PSSetShader(_psComposite);
        context.PSSetShaderResource(0, target.HdrSrv!);
        context.PSSetShaderResource(1, target.BloomSrv[0]!);
        context.Draw(3, 0);
        context.PSUnsetShaderResources(0, 2);
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

    private GpuFrameConstants BuildConstants(GpuRenderTarget target, GpuFrameInput input, GpuSceneLayout layout, GpuStageSimulation simulation,
        float frontHeight, float blackLength, float blackHeight, float whiteWidth, float blackWidth)
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
        var stars = look.ShowBackground && look.ShowStars && !look.Chroma ? .12f + look.StarDensity * .45f : 0;
        var edge = .9f + look.NoteEdgeWidth * 3.4f;
        return new GpuFrameConstants
        {
            ScreenTime = new Vector4(target.Width, target.Height, (float)simulation.Time, layout.HitY),
            SceneSize = new Vector4(layout.Width, layout.Height, pixelsPerUnit, 0),
            Camera = new Vector4(zoom * scaleX, zoom * pixelsPerUnit, offsetX * scaleX, offsetY * pixelsPerUnit),
            Background = new Vector4(GpuStageSimulation.ToLinear(look.BackgroundColor), look.Chroma ? 1 : 0),
            Aura = new Vector4(.19f, .03f, .42f, aura),
            Horizon = new Vector4(simulation.HorizonColor, look.HorizonGlow * (.18f + simulation.Activity * .9f) * (1 + beat)),
            SceneA = new Vector4(stars, 1.4f, _backgroundView is not null && look.ShowBackground && !look.Chroma ? 1 : 0, look.BackgroundDim),
            SceneB = new Vector4(_backgroundAspect, 1, beat, .88f),
            NoteA = new Vector4(look.NoteStyle, 2 + look.NoteRoundness * 12, edge, 5 + look.BloomSize * 16),
            NoteB = new Vector4(look.NoteTint, look.NoteEdge * 1.15f, look.NoteHeadGlow, look.NoteRefraction),
            NoteC = new Vector4(look.NoteTexture, look.Notes3D ? 1 : 0, look.NoteGlow * look.BloomIntensity / .65f * .75f * (1 + beat * .5f), 150),
            KeyA = new Vector4(look.ShaderKeyLight, look.ShaderShadows, look.ShaderAmbientOcclusion, look.ShaderGloss),
            KeyB = new Vector4(look.ShaderRimLight, look.ShaderEmissive * 1.7f, look.ShaderCameraTilt, look.KeyboardStyle),
            KeyC = new Vector4(layout.KeyboardHeight, blackLength, look.KeyLighting, frontHeight),
            KeyD = new Vector4(whiteWidth, blackWidth, blackHeight, .22f),
            RimColor = new Vector4(GpuStageSimulation.ToLinear(look.HaloColor), 0),
            Post = new Vector4(look.ShaderExposure, look.ShaderFilmic ? 1 : 0, 1, 1),
            // threshold 1.8: the lit white keys reach about 1.4 and must not bloom (their blur greys the
            // black keys); notes, sparks, flares and pressed keys are emissive and sit well above it
            Post2 = new Vector4(look.Chroma ? 0 : look.Vignette * .9f, look.BloomIntensity * .85f, 1.8f, .8f / 255f)
        };
    }

    public void Dispose()
    {
        try { Context.ClearState(); Context.Flush(); } catch { }
        _notes.Dispose(); _keys.Dispose(); _sprites.Dispose(); _keyColors.Dispose();
        _backgroundView?.Dispose(); _backgroundTexture?.Dispose();
        foreach (var disposable in new IDisposable?[] { _vsFullscreen, _vsNote, _vsKey, _vsSprite, _psBackground, _psNote, _psKey, _psSprite, _psPrefilter, _psDown, _psUp, _psComposite,
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
