using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace PianoPath;

/// <summary>One offline take owns one simulation/RNG and reusable render/readback resources.
/// The renderer is borrowed; single-frame snapshot helpers intentionally remain independent.</summary>
internal sealed class GpuOfflineFrameSequence : IDisposable
{
    private readonly GpuStageRenderer _renderer;
    private readonly GpuStageSimulation _simulation;
    private readonly GpuFrameInput _input = new();
    private readonly GpuInstanceList<GpuNoteInstance> _notes = new(256);
    private readonly GpuInstanceList<GpuKeyInstance> _keys = new(128);
    private readonly GpuInstanceList<GpuSpriteInstance> _sprites = new(1024);
    private readonly GpuRenderTarget _target = new();
    private readonly int _width, _height;
    private ID3D11Texture2D? _output, _staging;
    private ID3D11RenderTargetView? _view;
    private bool _disposed;

    internal GpuOfflineFrameSequence(GpuStageRenderer renderer,int width,int height,int seed)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if(width<=0 || height<=0)throw new ArgumentOutOfRangeException(nameof(width));
        _renderer=renderer;_width=width;_height=height;_simulation=new GpuStageSimulation(seed);
        try
        {
            _target.Ensure(renderer.Device,width,height);
            _output=renderer.Device.CreateTexture2D(Format.B8G8R8A8_UNorm,(uint)width,(uint)height,mipLevels:1,bindFlags:BindFlags.RenderTarget);
            _view=renderer.Device.CreateRenderTargetView(_output);
            _staging=renderer.Device.CreateTexture2D(Format.B8G8R8A8_UNorm,(uint)width,(uint)height,mipLevels:1,
                bindFlags:BindFlags.None,usage:ResourceUsage.Staging,cpuAccessFlags:CpuAccessFlags.Read);
        }
        catch{Dispose();throw;}
    }

    internal byte[] RenderFrame(GpuStageFeed feed,double frameSeconds,bool alphaOutput=false)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        ArgumentNullException.ThrowIfNull(feed);
        if(!double.IsFinite(frameSeconds) || frameSeconds<=0 || frameSeconds>1)
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        feed.Capture(_input,GpuStageFeed.Now);
        var sceneHeight=(float)Math.Max(120,_input.StageHeightDip);
        var layout=new GpuSceneLayout(sceneHeight*_width/_height,sceneHeight,_input.Look.KeyboardFraction);
        // Step clamps dt to 50 ms. Substep low-FPS exports instead of silently slowing physics.
        // The feed is consumed once on the first substep; subsequent steps do not replay queued hits.
        var steps=(int)Math.Ceiling(frameSeconds/.05);
        for(var step=0;step<steps;step++)_simulation.Step(frameSeconds/steps,_input,feed,layout.Width);
        sceneHeight=(float)Math.Max(120,_input.StageHeightDip);
        layout=new GpuSceneLayout(sceneHeight*_width/_height,sceneHeight,_input.Look.KeyboardFraction);
        _renderer.UpdateBackground(feed.Background);
        _renderer.UpdateCamera(feed.Camera);
        _renderer.UpdateAr(feed.Ar);
        _renderer.UpdateAtlas(feed.LabelAtlas);
        _renderer.UpdateWatermark(feed.Watermark);
        _renderer.Render(_target,_view!,_simulation,_input,layout,_notes,_keys,_sprites,alphaOutput:alphaOutput);
        _renderer.Context.CopyResource(_staging!,_output!);
        var pixels=new byte[checked(_width*_height*4)];
        var mapped=_renderer.Context.Map(_staging!,0,MapMode.Read,MapFlags.None);
        try
        {
            for(var y=0;y<_height;y++)
                Marshal.Copy(mapped.DataPointer+(nint)(y*mapped.RowPitch),pixels,y*_width*4,_width*4);
        }
        finally{_renderer.Context.Unmap(_staging!,0);}
        return pixels;
    }

    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;_staging?.Dispose();_view?.Dispose();_output?.Dispose();_target.Dispose();
    }
}
