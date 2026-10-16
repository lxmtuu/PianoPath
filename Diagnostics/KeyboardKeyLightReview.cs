using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Native main-lamp response diagnostic. No image synthesis, preset migration or particle art promotion.</summary>
internal static class KeyboardKeyLightReview
{
    private const int Width=640,Height=360;
    private static readonly int[] Levels=KeyboardKeyLightPlan.Levels;
    private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    internal sealed record Capture(string Id,string Engine,string Scene,string State,int Percent,string Variant,string File,
        int Width,int Height,string PixelSha256,string RawHash,string PacketHash,double KeyboardMean);
    private sealed record Frame(byte[] Pixels,Capture Record);

    internal static int Render(string output)
    {
        Directory.CreateDirectory(output);
        var previous=KeyboardLayout.Active;KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            using var renderer=GpuStageRenderer.Create(forceWarp:true);
            if(!renderer.IsWarp)throw new InvalidOperationException("Key-light review requires WARP.");
            var records=new List<Capture>();
            foreach(var scene in new[]{"isolated","mixed","ar"})
            foreach(var state in new[]{"idle","held"})
            {
                var frames=new List<Frame>();
                foreach(var percent in Levels)
                foreach(var variant in percent==0?new[]{"legacy","fixed","forced-off"}:new[]{"legacy","fixed"})
                {
                    var f=Gpu(renderer,scene,state,percent,variant);Save(output,f);records.Add(f.Record);frames.Add(f);
                }
                Check(frames,ar:scene=="ar",gpu:true);
            }
            foreach(var scene in new[]{"isolated","mixed"})
            foreach(var state in new[]{"idle","held"})
            {
                var frames=new List<Frame>();
                foreach(var percent in Levels)
                {
                    var f=Software(scene,state,percent,"fixed");Save(output,f);records.Add(f.Record);frames.Add(f);
                    if(percent==0){var off=Software(scene,state,percent,"forced-off");Save(output,off);records.Add(off.Record);frames.Add(off);}
                }
                Check(frames,ar:false,gpu:false);
            }
            renderer.ObserveParticleStateForReview=null;renderer.ObserveStageKeyForReview=null;
            renderer.ObserveStageSpriteForReview=null;renderer.ObserveArQuadForReview=null;
            var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
            string[] sources=["Gpu/StageShaders.hlsl","Gpu/GpuLook.cs","Gpu/GpuStageRenderer.cs","Gpu/GpuStageSimulation.cs",
                "Gpu/GpuStageSimulation.Effects.cs","Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs","Gpu/GpuRenderLoop.cs","Gpu/GpuStageFeed.cs",
                "Stage/PianoVisualSettings.cs","Stage/PianoStage.cs","Stage/PianoStage.Gpu.cs","Stage/Shading/PianoShaderScene.cs",
                "Stage/Shading/PianoKeyboardRenderer.cs","Ui/MainWindow.Settings.cs","Diagnostics/KeyboardKeyLightReview.cs",
                "Diagnostics/KeyboardKeyLightPlan.cs","Diagnostics/KeyParticleFixtures.cs","Diagnostics/HeldParticleControlPlan.cs","docs/samples/ar-keyboard-topdown.png"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{
                schema=1,family="keyboard-key-light",sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",adapter=renderer.AdapterName,
                levels=Levels,gpuWidth=Width,gpuHeight=Height,softwareWidth=640,softwareHeight=128,
                curve="t*(4.05-5.6*t) below .25; .35+1.25*t at/above .25; main lamp only",
                referenceModes="production=0; legacy-floor=1; independently forced-off=2; never settings",
                mixed="tilt38/shadow46/AO52/gloss92/rim62/emission149, not the user's complete saved preset",
                software="existing linear softbox gain unchanged; Cinematic native keyboard-band bake, not AR or full app UI",
                sourceHashes=sources.ToDictionary(p=>p,p=>Hash(p.EndsWith(".png",StringComparison.Ordinal)?File.ReadAllBytes(Path.Combine(root,p)):
                    Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),captures=records},Json));
            return records.Count;
        }
        finally{KeyboardLayout.Use(previous);}
    }

    private static PianoVisualSettings Settings(string scene,int percent)
    {
        var s=KeyParticleFixtures.Settings(KeyParticleFixtures.All[0],false);
        s.KeyboardStyle="Studio";s.ShadingQuality="Cinematic";s.ShaderKeyLight=percent;
        s.ShaderCameraTilt=38;s.ShaderShadows=46;s.ShaderAmbientOcclusion=52;s.ShaderGloss=92;
        s.ShowHalo=scene!="isolated";s.ShaderRimLight=scene=="isolated"?0:62;
        s.ShaderEmissive=scene=="isolated"?0:149;s.KeyLighting=scene=="isolated"?0:32;
        s.BloomIntensity=0;s.BackgroundMode="Solid";s.BackgroundColor="#10141B";s.Clamp();
        if(s.ShaderKeyLight!=percent)throw new InvalidDataException("The setting value was changed before rendering.");
        return s;
    }
    private static Frame Gpu(GpuStageRenderer renderer,string scene,string state,int percent,string variant)
    {
        var settings=Settings(scene,percent);
        var stage=new PianoStage{Width=1280,Height=720};stage.SetVisualSettings(settings);
        stage.Measure(new Size(1280,720));stage.Arrange(new Rect(0,0,1280,720));stage.UpdateLayout();
        var feed=new GpuStageFeed();feed.SetStageHeight(720);
        var look=GpuLook.From(settings,(p,t)=>stage.AdjustColor(stage.NoteColor(p,t)),stage.KeyboardHeight/stage.ActualHeight,stage.AdjustColor,scene=="ar");
        if(look.ShaderKeyLight!=percent/100f || look.KeyboardSoftboxReferenceForReview!=0)
            throw new InvalidDataException("GpuLook dropped the exact slider value or enabled a diagnostic by default.");
        feed.SetLook(look.WithKeyboardSoftboxReferenceForReview(variant=="legacy"?1:variant=="forced-off"?2:0));
        if(scene=="ar")
        {
            var view=HeldParticleControlPlan.Views[1];
            var calibration=ArCalibration.Create(KeyboardLayout.Standard88,view.Corners!,1280,720,out _,70)!;
            var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            var ring=new CameraFrameRing();camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);
            ring.Publish(slot,1280,720,Stopwatch.GetTimestamp());feed.Camera=new GpuCameraInput(ring,PictureFit.Fit,false,1);
            feed.Ar=new GpuArInput(calibration,ArNoteStyle.Default,1,Quality:ArQualityTier.Ultra,ProjectedNotes:true,ShowNotes:false,ShowDamperStrip:false);
        }
        var raw="";var packets=new List<object>();
        renderer.ObserveParticleStateForReview=(sim,_)=>raw=Hash(JsonSerializer.SerializeToUtf8Bytes(
            Enumerable.Range(0,sim.ArParticleCount).Select(sim.ArParticleAt).ToArray(),Json));
        renderer.ObserveStageKeyForReview=q=>packets.Add(q);
        renderer.ObserveStageSpriteForReview=q=>packets.Add(q);
        renderer.ObserveArQuadForReview=q=>packets.Add(q);
        var pixels=GpuRenderLoop.RenderFrames(renderer,feed,Width,Height,11,1.0/60,false,out _,f=>
        {
            feed.SetDeterministicState([],(f+1)/60.0,false,state=="held"?new HashSet<int>{60,61}:new HashSet<int>());
            feed.SetDeterministicBeatPulse(0);feed.SetSustainLevel(false,0);
        },KeyParticleFixtures.Seed);
        if(scene=="ar" && (renderer.StageKeyCount!=0 || renderer.StageNoteCount!=0 || renderer.StageSpriteCount!=0))
            throw new InvalidDataException("Stage geometry leaked over the camera.");
        return Make(pixels,"gpu",scene,state,percent,variant,Width,Height,raw,Hash(JsonSerializer.SerializeToUtf8Bytes(packets,Json)));
    }
    private static Frame Software(string scene,string state,int percent,string variant)
    {
        var s=Settings(scene,percent);var model=PianoShaderScene.From(s,640,128,"Cinematic");
        if(model.KeyLightIntensity!=s.ShaderKeyLight/100*3.4)throw new InvalidDataException("Software slider mapping changed.");
        if(variant=="forced-off")model.KeyLightIntensity=0;
        var lights=new KeyLightState();
        if(state=="held"){lights.Light(60,Colors.LimeGreen,1);lights.Light(61,Colors.CornflowerBlue,1);}
        var bitmap=PianoKeyboardRenderer.Render(model,lights,0,0,640,128,-1)??throw new InvalidDataException("Software bake missing.");
        var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];
        converted.CopyPixels(pixels,converted.PixelWidth*4,0);
        return Make(pixels,"software",scene,state,percent,variant,converted.PixelWidth,converted.PixelHeight,Hash([]),Hash([]));
    }
    private static Frame Make(byte[] pixels,string engine,string scene,string state,int percent,string variant,int width,int height,string raw,string packet)
    {
        var id=$"{engine}-{scene}-{state}-p{percent}-{variant}";
        var top=engine=="gpu"?height-120:0;long total=0;var count=0;
        for(var y=top;y<height;y++)for(var x=20;x<width-20;x++)
        {var i=(y*width+x)*4;total+=pixels[i]+pixels[i+1]+pixels[i+2];count+=3;}
        return new(pixels,new(id,engine,scene,state,percent,variant,id+".png",width,height,Hash(pixels),raw,packet,total/(double)count));
    }
    private static void Check(List<Frame> frames,bool ar,bool gpu)
    {
        Frame At(int p,string v)=>frames.Single(f=>f.Record.Percent==p && f.Record.Variant==v);
        if(At(0,"fixed").Record.PixelSha256!=At(0,"forced-off").Record.PixelSha256)
            throw new InvalidDataException("Zero does not equal the independently forced-off main lamp.");
        if(ar)
        {
            if(frames.Any(f=>f.Record.PixelSha256!=frames[0].Record.PixelSha256 || f.Record.RawHash!=frames[0].Record.RawHash || f.Record.PacketHash!=frames[0].Record.PacketHash))
                throw new InvalidDataException("Stage key light changed camera/AR pixels or actual packets.");
            return;
        }
        for(var i=1;i<Levels.Length;i++)
            if(At(Levels[i],"fixed").Record.KeyboardMean<=At(Levels[i-1],"fixed").Record.KeyboardMean)
                throw new InvalidDataException("Fixed-scene keyboard brightness is not strictly increasing.");
        if(!gpu)return;
        if(At(0,"fixed").Record.KeyboardMean>=At(0,"legacy").Record.KeyboardMean)
            throw new InvalidDataException("Removing the lamp floor did not darken the actual keyboard.");
        foreach(var p in Levels)
        {
            var a=At(p,"fixed");var b=At(p,"legacy");
            if(a.Record.RawHash!=b.Record.RawHash || a.Record.PacketHash!=b.Record.PacketHash)
                throw new InvalidDataException("Lamp-only change altered simulation or ordered key/sprite packets.");
            if(p>=25 && a.Record.PixelSha256!=b.Record.PixelSha256)
                throw new InvalidDataException("Existing illumination from 25 percent upwards changed.");
            if(!a.Pixels.AsSpan(0,Width*(Height-130)*4).SequenceEqual(b.Pixels.AsSpan(0,Width*(Height-130)*4)))
                throw new InvalidDataException("Lamp-only change altered the non-keyboard region.");
        }
    }
    private static void Save(string output,Frame f)
    {
        var r=f.Record;var source=BitmapSource.Create(r.Width,r.Height,96,96,PixelFormats.Bgra32,null,f.Pixels,r.Width*4);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));
        using(var stream=File.Create(Path.Combine(output,r.File)))png.Save(stream);
        using var input=File.OpenRead(Path.Combine(output,r.File));
        var decoded=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var converted=new FormatConvertedBitmap(decoded.Frames[0],PixelFormats.Bgra32,null,0);var bytes=new byte[f.Pixels.Length];converted.CopyPixels(bytes,r.Width*4,0);
        if(Hash(bytes)!=r.PixelSha256)throw new InvalidDataException("Native PNG changed pixels.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
