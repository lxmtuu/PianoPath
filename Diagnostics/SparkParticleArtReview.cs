using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Embers material-only review. Uses production aligned AR axes, never the historical fixture seam.</summary>
internal static class SparkParticleArtReview
{
    private const int Width=640, Height=360;
    private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    private sealed record Shot(string Id,string Palette,string View,int Pitch,string Phase,string Variant,string File,
        string PixelSha256,string RawHash,string PacketHash,int RawParticles,int PackedParticles,string? ColourHash=null,long Signal=0,int VisiblePixels=0);
    private sealed record Crop(int X,int Y,int Width,int Height);
    private sealed record Frame(byte[] Pixels, Shot Shot);

    internal static int Render(string output,bool v2=false)
    {
        var previous=KeyboardLayout.Active;KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            Directory.CreateDirectory(output);
            using var renderer=GpuStageRenderer.Create(forceWarp:true);
            if(!renderer.IsWarp)throw new InvalidOperationException("Spark review requires WARP.");
            renderer.SuppressStageKeyLightingForReview=true;
            renderer.SuppressArKeyTintForReview=renderer.SuppressArKeySpillForReview=true;
            var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            var captures=new List<Shot>();var motions=new List<object>();
            foreach(var palette in new[]{"prism","indigo"})
            foreach(var view in HeldParticleControlPlan.Views)
            foreach(var pitch in new[]{60,61})
            foreach(var phase in new[]{"burst","held","released","expired"})
            {
                Frame? baseline=null;byte[]? offPixels=null;
                foreach(var variant in new[]{"off","classic","art"})
                {
                    var f=Draw(renderer,view,camera,palette,pitch,phase,variant,phase=="burst"?2:phase=="expired"?149:35,60,v2:v2);
                    var id=$"{palette}-{view.Id}-p{pitch}-{phase}";
                    if(variant=="off")offPixels=f.Pixels;
                    long signal=0;var visible=0;
                    for(var i=0;i<f.Pixels.Length;i+=4)
                    {
                        var b=Math.Abs(f.Pixels[i]-offPixels![i]);var g=Math.Abs(f.Pixels[i+1]-offPixels[i+1]);var r=Math.Abs(f.Pixels[i+2]-offPixels[i+2]);
                        signal+=b+g+r;if(Math.Max(b,Math.Max(g,r))>4)visible++;
                    }
                    var shot=f.Shot with {Id=id,File=id+"-"+variant+".png",Signal=signal,VisiblePixels=visible};
                    WritePng(Path.Combine(output,shot.File),f.Pixels,Width,Height);
                    if(variant=="off")
                    {
                        if(shot.RawParticles!=0 || shot.PackedParticles!=0)throw new InvalidDataException("Spark off control emitted particles.");
                    }
                    else if(variant=="classic")baseline=f;
                    else GuardPair(baseline!.Shot,shot,phase!="expired");
                    captures.Add(shot);
                }
            }
            if(v2)
                foreach(var shot in captures.Where(c=>c.Variant=="art"))
                {
                    var stageShot=captures.Single(c=>c.Palette==shot.Palette && c.View=="live-stage" && c.Pitch==shot.Pitch && c.Phase==shot.Phase && c.Variant=="art");
                    if(shot.RawHash!=stageShot.RawHash || shot.PackedParticles!=stageShot.PackedParticles || shot.ColourHash!=stageShot.ColourHash)
                        throw new InvalidDataException($"Shared spark colour/alpha differs across outputs: {shot.Id}");
                }
            foreach(var view in HeldParticleControlPlan.Views.Take(2))
            foreach(var phase in new[]{"burst","held"})
            {
                // All three seeded tracks are buffered only for this 2s comparison. One common crop,
                // union of off-differences over all 60 native frames, is applied without resampling.
                var tracks=new Dictionary<string,List<Frame>>();
                foreach(var variant in new[]{"off","classic","art"})
                {
                    var frames=new List<Frame>();
                    for(var f=0;f<60;f++)frames.Add(Draw(renderer,view,camera,"prism",60,phase,variant,f,30,motion:true,v2:v2));
                    tracks.Add(variant,frames);
                }
                var x0=Width;var y0=Height;var x1=0;var y1=0;
                for(var f=0;f<60;f++)
                {
                    GuardPair(tracks["classic"][f].Shot,tracks["art"][f].Shot,tracks["classic"][f].Shot.PackedParticles>0,requireSignal:false);
                    foreach(var variant in new[]{"classic","art"})
                    {
                        var pixels=tracks[variant][f].Pixels;var off=tracks["off"][f].Pixels;
                        for(var i=0;i<pixels.Length;i+=4)
                            if(pixels[i]!=off[i] || pixels[i+1]!=off[i+1] || pixels[i+2]!=off[i+2])
                            {var x=i/4%Width;var y=i/4/Width;x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
                    }
                }
                if(Enumerable.Range(0,60).Count(f=>tracks["classic"][f].Shot.PixelSha256!=tracks["art"][f].Shot.PixelSha256)<6)
                    throw new InvalidDataException("Spark motion has too few changed frames.");
                if(x1<x0 || y1<y0)throw new InvalidDataException("Motion has no particle signal.");
                x0=Math.Max(0,x0-4);y0=Math.Max(0,y0-4);x1=Math.Min(Width-1,x1+4);y1=Math.Min(Height-1,y1+4);
                var crop=new Crop(x0,y0,x1-x0+1,y1-y0+1);var records=new List<object>();
                foreach(var variant in new[]{"off","classic","art"})
                {
                    var hashes=new List<string>();var frames=new List<object>();
                    for(var f=0;f<60;f++)
                    {
                        var frame=tracks[variant][f];var pixels=Extract(frame.Pixels,crop);
                        var file=$"motion/{view.Id}-{phase}/{variant}/{f:D3}.png";
                        WritePng(Path.Combine(output,file),pixels,crop.Width,crop.Height);
                        hashes.Add(Hash(pixels));frames.Add(new{frame.Shot.RawHash,frame.Shot.PacketHash,frame.Shot.ColourHash,frame.Shot.RawParticles,frame.Shot.PackedParticles,fullPixelSha256=frame.Shot.PixelSha256});
                    }
                    records.Add(new{variant,hashes,frames});
                }
                motions.Add(new{Id=$"{view.Id}-{phase}",view=view.Id,phase,fps=30,count=60,crop,releaseFrame=12,
                    emission=phase=="burst"?"single impact, no held emitter":"physical held f<12; no impact; pedal on after release",tracks=records});
            }
            renderer.ObserveParticleStateForReview=null;renderer.ObserveStageSpriteForReview=null;renderer.ObserveArQuadForReview=null;
            var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
            string[] sources=["Gpu/StageShaders.hlsl","Gpu/SparkParticleAppearance.cs","Gpu/GpuLook.cs","Gpu/GpuStageSimulation.cs",
                "Gpu/GpuStageSimulation.Effects.cs","Gpu/GpuStageRenderer.cs","Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs",
                "Gpu/GpuStageFeed.cs","Gpu/GpuRenderLoop.cs","Camera/ArParticleBillboard.cs","Camera/Ar3DNoteProjection.cs",
                "Camera/ArCameraPose.cs","Stage/PianoStage.cs","Diagnostics/KeyParticleFixtures.cs","Diagnostics/HeldParticleControlPlan.cs",
                "Diagnostics/SparkParticleArtReview.cs","docs/samples/ar-keyboard-topdown.png"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{
                schema=v2?2:1,family="embers",reviewOnly=true,legacyParticleAxesForReview=false,sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",
                adapter=renderer.AdapterName,width=Width,height=Height,seed=KeyParticleFixtures.Seed,staticFps=60,staticReleaseFrame=21,
                lighting="tint/spill masked; bloom off, isolated material review",captureCount=captures.Count,
                sourceHashes=sources.ToDictionary(p=>p,p=>Hash(p.EndsWith(".png",StringComparison.Ordinal)?File.ReadAllBytes(Path.Combine(root,p)):
                    Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),captures,motions},Json));
            return captures.Count;
        }
        finally{KeyboardLayout.Use(previous);}
    }

    private static Frame Draw(GpuStageRenderer renderer,HeldParticleControlPlan.View view,byte[] sourceCamera,
        string palette,int pitch,string phase,string variant,int sample,int fps,bool motion=false,bool v2=false)
    {
        var settings=KeyParticleFixtures.Settings(KeyParticleFixtures.All[0],variant!="off");
        settings.BloomIntensity=0;
        if(palette=="indigo"){settings.ColorMode="Gradient";settings.Palette="Custom";settings.NoteColorStart=settings.NoteColorEnd="#666DFF";}
        var stage=new PianoStage{Width=1280,Height=720};stage.SetVisualSettings(settings);
        stage.Measure(new Size(1280,720));stage.Arrange(new Rect(0,0,1280,720));stage.UpdateLayout();
        var feed=new GpuStageFeed();feed.SetStageHeight(720);
        feed.SetLook(GpuLook.From(settings,(p,t)=>stage.AdjustColor(stage.NoteColor(p,t)),stage.KeyboardHeight/stage.ActualHeight,
            stage.AdjustColor,view.OverCamera).WithSparkArtForReview(v2 || variant=="art").WithSparkV2ForReview(v2 && variant=="art"));
        if(view.OverCamera)
        {
            var calibration=ArCalibration.Create(KeyboardLayout.Standard88,view.Corners!,1280,720,out _,70)!;
            var camera=HeldParticleControlPlan.CameraFor(view,sourceCamera);var ring=new CameraFrameRing();
            camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);ring.Publish(slot,1280,720,Stopwatch.GetTimestamp());
            var direction=view.Id=="ar-oblique"?new ArNoteDirection(45,-45):view.Id=="ar-near-overhead"?new ArNoteDirection(90,90):new ArNoteDirection();
            feed.Camera=new GpuCameraInput(ring,PictureFit.Fit,false,1);
            feed.Ar=new GpuArInput(calibration,ArNoteStyle.Default,1,Quality:ArQualityTier.Ultra,ProjectedNotes:true,ShowNotes:false,ShowDamperStrip:false,NoteDirection:direction);
        }
        var raw="";var count=0;var packets=new List<object>();var colours=new List<Vector4>();var packed=0;
        renderer.ObserveParticleStateForReview=(sim,_)=>{count=sim.ArParticleCount;raw=Hash(JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0,count).Select(sim.ArParticleAt).ToArray(),Json));};
        renderer.ObserveStageSpriteForReview=q=>
        {
            var p=q.PosSize;if(p.W is 18 or 19 or 20 or 21)p.W=p.W is 18 or 20?1:0;
            var colour=q.Color;
            if(p.W is 0 or 1){packed++;colours.Add(colour);if(v2)colour=default;}
            packets.Add(new{PosSize=p,Color=colour,q.Dir});
        };
        renderer.ObserveArQuadForReview=q=>
        {
            var misc=q.Misc;if(misc.Z is 18 or 19 or 20 or 21)misc.Z=2;
            var colour=q.Color;
            if(misc.Z==2){packed++;colours.Add(colour);if(v2)colour=default;}
            packets.Add(new{q.A,q.B,q.W,Color=colour,Misc=misc});
        };
        var release=motion?12:21;
        var pixels=GpuRenderLoop.RenderFrames(renderer,feed,Width,Height,sample,1.0/fps,false,out _,f=>
        {
            var held=phase!="burst" && (motion?f<release:phase=="held" || f<release);
            feed.SetDeterministicState([],(f+1)/(double)fps,false,held?new HashSet<int>{pitch}:new HashSet<int>());
            feed.SetDeterministicBeatPulse(0);feed.SetSustainLevel(f>=release,f>=release?1:0);
            if(phase=="burst" && f==0)feed.Impact(pitch,.9);
        },KeyParticleFixtures.Seed);
        if(view.OverCamera && (renderer.StageSpriteCount!=0 || renderer.StageKeyCount!=0 || renderer.StageNoteCount!=0))
            throw new InvalidDataException("Stage geometry leaked onto AR spark review.");
        return new(pixels,new("",palette,view.Id,pitch,phase,variant,"",Hash(pixels),raw,Hash(JsonSerializer.SerializeToUtf8Bytes(packets,Json)),count,packed,v2?Hash(JsonSerializer.SerializeToUtf8Bytes(colours,Json)):null));
    }
    private static void GuardPair(Shot a,Shot b,bool active,bool requireSignal=true)
    {
        if(a.RawHash!=b.RawHash || a.PacketHash!=b.PacketHash || a.RawParticles!=b.RawParticles || a.PackedParticles!=b.PackedParticles)
            throw new InvalidDataException("Spark candidate changed state, population or normalized actual packets.");
        if(active && requireSignal && (b.PackedParticles==0 || a.PixelSha256==b.PixelSha256))throw new InvalidDataException("Spark material has no visible signal.");
        if(!active && a.PixelSha256!=b.PixelSha256)throw new InvalidDataException("Spark candidate changed an empty output.");
    }
    private static byte[] Extract(byte[] pixels,Crop c)
    {
        var result=new byte[c.Width*c.Height*4];for(var y=0;y<c.Height;y++)Buffer.BlockCopy(pixels,((y+c.Y)*Width+c.X)*4,result,y*c.Width*4,c.Width*4);return result;
    }
    private static void WritePng(string path,byte[] pixels,int width,int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));
        using(var stream=File.Create(path))encoder.Save(stream);
        using var input=File.OpenRead(path);var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var decoded=new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Bgra32,null,0);var copy=new byte[pixels.Length];decoded.CopyPixels(copy,width*4,0);
        if(!copy.SequenceEqual(pixels))throw new InvalidDataException("Spark PNG changed native pixels.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
