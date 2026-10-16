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

/// <summary>Bounce material and separate AR tangent-billboard review. Uses production aligned AR axes, never the historical fixture seam.</summary>
internal static class BounceParticleArtReview
{
    private const int Width=640, Height=360;
    private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    internal sealed record Shot(string Id,string Palette,string View,int Pitch,string Phase,string Variant,string File,
        string PixelSha256,string RawHash,string PacketHash,int RawParticles,int PackedParticles,string? ColourHash=null,long Signal=0,int VisiblePixels=0,long BodySignal=0,int BodyVisiblePixels=0,string NonBodyHash="",string SelectedHash="",double MaxCornerError=0);
    private sealed record Crop(int X,int Y,int Width,int Height);
    private sealed record Frame(byte[] Pixels, Shot Shot);

    internal static int Render(string output,bool v2=true)
    {
        var previous=KeyboardLayout.Active;KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            Directory.CreateDirectory(output);
            using var renderer=GpuStageRenderer.Create(forceWarp:true);
            if(!renderer.IsWarp)throw new InvalidOperationException("Bounce review requires WARP.");
            renderer.SuppressStageKeyLightingForReview=true;
            renderer.SuppressArKeyTintForReview=renderer.SuppressArKeySpillForReview=true;
            var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            var captures=new List<Shot>();var motions=new List<object>();var bodyControls=new List<Shot>();var geometryControls=new List<Shot>();
            foreach(var palette in new[]{"prism","indigo"})
            foreach(var view in HeldParticleControlPlan.Views)
            foreach(var pitch in new[]{60,61})
            foreach(var phase in new[]{"burst","held","released","expired"})
            {
                Frame? baseline=null;byte[]? offPixels=null;
                foreach(var variant in new[]{"off","classic","art"})
                {
                    var f=Draw(renderer,view,camera,palette,pitch,phase,variant,phase=="burst"?2:phase=="held"?10:phase=="released"?26:89,60,v2:v2);
                    var id=$"{palette}-{view.Id}-p{pitch}-{phase}";
                    if(variant=="off")offPixels=f.Pixels;
                    long signal=0;var visible=0;
                    for(var i=0;i<f.Pixels.Length;i+=4)
                    {
                        var b=Math.Abs(f.Pixels[i]-offPixels![i]);var g=Math.Abs(f.Pixels[i+1]-offPixels[i+1]);var r=Math.Abs(f.Pixels[i+2]-offPixels[i+2]);
                        signal+=b+g+r;if(Math.Max(b,Math.Max(g,r))>4)visible++;
                    }
                    var shot=f.Shot with {Id=id,File=id+"-"+variant+".png",Signal=signal,VisiblePixels=visible};
                    if(variant=="art")
                    {
                        var dark=Draw(renderer,view,camera,palette,pitch,phase,"art",phase=="burst"?2:phase=="held"?10:phase=="released"?26:89,60,v2:v2,suppressBody:true);
                        long bodySignal=0;var bodyPixels=0;
                        for(var i=0;i<f.Pixels.Length;i+=4)
                        {
                            var b=Math.Abs(f.Pixels[i]-dark.Pixels[i]);var g=Math.Abs(f.Pixels[i+1]-dark.Pixels[i+1]);var r=Math.Abs(f.Pixels[i+2]-dark.Pixels[i+2]);
                            bodySignal+=b+g+r;if(Math.Max(b,Math.Max(g,r))>4)bodyPixels++;
                        }
                        var flat=Draw(renderer,view,camera,palette,pitch,phase,"art",phase=="burst"?2:phase=="held"?10:phase=="released"?26:89,60,v2:v2,billboard:false);
                        var geometry=flat.Shot with {Id=id,Variant="material-only",File=id+"-material-only.png"};
                        if(geometry.RawHash!=shot.RawHash || geometry.NonBodyHash!=shot.NonBodyHash || geometry.SelectedHash!=shot.SelectedHash || geometry.ColourHash!=shot.ColourHash || geometry.PackedParticles!=shot.PackedParticles || geometry.PacketHash!=baseline!.Shot.PacketHash)
                            throw new InvalidDataException("Bounce geometry trial changed state, identities, material, non-body packets or old-axis baseline geometry.");
                        if(!view.OverCamera && (geometry.PixelSha256!=shot.PixelSha256 || geometry.PacketHash!=shot.PacketHash))
                            throw new InvalidDataException("AR-only Bounce billboard changed Stage output.");
                        if(view.OverCamera && phase!="expired" && geometry.PacketHash==shot.PacketHash)
                            throw new InvalidDataException("Bounce billboard did not change actual AR geometry.");
                        WritePng(Path.Combine(output,geometry.File),flat.Pixels,Width,Height);geometryControls.Add(geometry);
                        var control=dark.Shot with {Id=id,Variant="body-off",File=id+"-body-off.png"};
                        if(control.NonBodyHash!=shot.NonBodyHash || control.SelectedHash!=shot.SelectedHash || control.RawHash!=shot.RawHash || control.PacketHash!=shot.PacketHash || control.RawParticles!=shot.RawParticles || control.PackedParticles!=shot.PackedParticles)
                            throw new InvalidDataException("Bounce body-off control changed state/geometry or a non-body packet.");
                        if(phase!="expired" && bodySignal==0)
                            throw new InvalidDataException($"Bounce body raster-empty: {id}; raw={shot.RawParticles}, bodies={shot.PackedParticles}, classic/off same={baseline?.Shot.PixelSha256==Hash(offPixels!)}. Ring signal cannot substitute for body signal.");
                        if(phase=="expired" && bodySignal!=0)throw new InvalidDataException("Expired Bounce body contributed pixels.");
                        WritePng(Path.Combine(output,control.File),dark.Pixels,Width,Height);bodyControls.Add(control);
                        shot=shot with {BodySignal=bodySignal,BodyVisiblePixels=bodyPixels};
                    }
                    WritePng(Path.Combine(output,shot.File),f.Pixels,Width,Height);
                    if(variant=="off")
                    {
                        if(shot.RawParticles!=0 || shot.PackedParticles!=0)throw new InvalidDataException("Bounce off control emitted particles.");
                    }
                    else if(variant=="classic")baseline=f with {Shot=shot};
                    else GuardPair(baseline!.Shot,shot,phase!="expired");
                    captures.Add(shot);
                }
            }
            if(v2)
                foreach(var shot in captures.Where(c=>c.Variant=="art"))
                {
                    var stageShot=captures.Single(c=>c.Palette==shot.Palette && c.View=="live-stage" && c.Pitch==shot.Pitch && c.Phase==shot.Phase && c.Variant=="art");
                    if(shot.RawHash!=stageShot.RawHash || shot.PackedParticles!=stageShot.PackedParticles || shot.ColourHash!=stageShot.ColourHash)
                        throw new InvalidDataException($"Shared bounce colour/alpha differs across outputs: {shot.Id}");
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
                    throw new InvalidDataException("Bounce motion has too few changed frames.");
                if(Enumerable.Range(0,60).Count(f=>tracks["art"][f].Shot.PixelSha256!=tracks["off"][f].Shot.PixelSha256)<6)
                    throw new InvalidDataException("Bounce candidate motion contributes too few nonblank frames.");
                // Bounce's companion ring expires at .3 s. f10 at 30 Hz is beyond it;
                // common impact lighting is present in the off control too. Require body-only tail signal.
                if(Enumerable.Range(10,50).Count(f=>tracks["art"][f].Shot.PixelSha256!=tracks["off"][f].Shot.PixelSha256)<3)
                    throw new InvalidDataException("Bounce motion lacks body signal after companion ring expiry.");
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
                        hashes.Add(Hash(pixels));frames.Add(new{frame.Shot.RawHash,frame.Shot.PacketHash,frame.Shot.NonBodyHash,frame.Shot.SelectedHash,frame.Shot.MaxCornerError,frame.Shot.ColourHash,frame.Shot.RawParticles,frame.Shot.PackedParticles,fullPixelSha256=frame.Shot.PixelSha256});
                    }
                    records.Add(new{variant,hashes,frames});
                }
                motions.Add(new{Id=$"{view.Id}-{phase}",view=view.Id,phase,fps=30,count=60,crop,releaseFrame=12,
                    emission=phase=="burst"?"single firm impact .9, no held emitter; pedal f12":"single soft impact .45, no held emitter; pedal f12",tracks=records});
            }
            renderer.SuppressBounceBodyForReview=false;
            renderer.ObserveParticleStateForReview=null;renderer.ObserveStageSpriteForReview=null;renderer.ObserveArQuadForReview=null;renderer.ObserveArParticleForReview=null;
            var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
            string[] sources=["Gpu/StageShaders.hlsl","Gpu/SparkParticleAppearance.cs","Gpu/BounceParticleAppearance.cs","Gpu/GpuLook.cs","Gpu/GpuStageSimulation.cs",
                "Gpu/GpuStageSimulation.Effects.cs","Gpu/GpuStageRenderer.cs","Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs",
                "Gpu/GpuStageFeed.cs","Gpu/GpuRenderLoop.cs","Camera/ArParticleBillboard.cs","Camera/Ar3DNoteProjection.cs",
                "Camera/ArCameraPose.cs","Stage/PianoStage.cs","Diagnostics/KeyParticleFixtures.cs","Diagnostics/HeldParticleControlPlan.cs",
                "Diagnostics/BounceParticleArtReview.cs","Diagnostics/AuxParticleFixtures.cs","Diagnostics/AuxParticleTimeline.cs","docs/samples/ar-keyboard-topdown.png"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{
                schema=3,family="bounce",bodyControls,geometryControls,geometryTrial="AR tangent billboard follows Stage velocity; unchanged extents; Stage unchanged",motionBodyCheckStartFrame=10,staticSampleFrames=new[]{2,10,26,89},motionPressVelocities=new[]{.9,.45},storageLaneMeaning="burst=early/firm; held=mid/soft; released=late; expired=empty; no held or release emitter",reviewOnly=true,legacyParticleAxesForReview=false,sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",
                adapter=renderer.AdapterName,width=Width,height=Height,seed=KeyParticleFixtures.Seed,staticFps=60,staticReleaseFrame=21,
                lighting="tint/spill masked; bloom off, isolated material review",captureCount=captures.Count,
                sourceHashes=sources.ToDictionary(p=>p,p=>Hash(p.EndsWith(".png",StringComparison.Ordinal)?File.ReadAllBytes(Path.Combine(root,p)):
                    Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),captures,motions},Json));
            return captures.Count;
        }
        finally{KeyboardLayout.Use(previous);}
    }

    private static Frame Draw(GpuStageRenderer renderer,HeldParticleControlPlan.View view,byte[] sourceCamera,
        string palette,int pitch,string phase,string variant,int sample,int fps,bool motion=false,bool v2=true,bool suppressBody=false,bool billboard=true)
    {
        renderer.SuppressBounceBodyForReview=suppressBody;
        var settings=AuxParticleFixtures.Settings(AuxParticleTimeline.Routes.Single(r=>r.Id=="morph-bounce"),variant!="off");
        settings.BloomIntensity=0;
        if(palette=="indigo"){settings.ColorMode="Gradient";settings.Palette="Custom";settings.NoteColorStart=settings.NoteColorEnd="#666DFF";}
        var stage=new PianoStage{Width=1280,Height=720};stage.SetVisualSettings(settings);
        stage.Measure(new Size(1280,720));stage.Arrange(new Rect(0,0,1280,720));stage.UpdateLayout();
        var feed=new GpuStageFeed();feed.SetStageHeight(720);
        var look=GpuLook.From(settings,(p,t)=>stage.AdjustColor(stage.NoteColor(p,t)),stage.KeyboardHeight/stage.ActualHeight,
            stage.AdjustColor,view.OverCamera).WithBounceArtForReview(variant=="art").WithBounceBillboardForReview(variant=="art" && billboard);feed.SetLook(look);
        ArCameraPose? pose=null;
        var direction=(view.Id=="ar-oblique"?new ArNoteDirection(45,-45):view.Id=="ar-near-overhead"?new ArNoteDirection(90,90):new ArNoteDirection());
        if(view.OverCamera)
        {
            var calibration=ArCalibration.Create(KeyboardLayout.Standard88,view.Corners!,1280,720,out _,70)!;
            pose=calibration.CameraPose;
            var camera=HeldParticleControlPlan.CameraFor(view,sourceCamera);var ring=new CameraFrameRing();
            camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);ring.Publish(slot,1280,720,Stopwatch.GetTimestamp());
            feed.Camera=new GpuCameraInput(ring,PictureFit.Fit,false,1);
            feed.Ar=new GpuArInput(calibration,ArNoteStyle.Default,1,Quality:ArQualityTier.Ultra,ProjectedNotes:true,ShowNotes:false,ShowDamperStrip:false,NoteDirection:direction);
        }
        float simWidth=0,simHitY=0;var actualQuads=new List<GpuArQuad>();var stablePackets=new List<object>();
        var raw="";var count=0;var packets=new List<object>();var colours=new List<Vector4>();var selected=new List<GpuArParticle>();GpuArParticle[] rawParticles=[];var packed=0;
        renderer.ObserveParticleStateForReview=(sim,_)=>
        {
            simWidth=sim.SimulationWidth;simHitY=sim.SimulationHitY;
            count=sim.ArParticleCount;rawParticles=Enumerable.Range(0,count).Select(sim.ArParticleAt).ToArray();
            raw=Hash(JsonSerializer.SerializeToUtf8Bytes(rawParticles.Select(p=>new{Particle=p,p.MaterialRoute}),Json));
        };
        renderer.ObserveArParticleForReview=p=>selected.Add(p);
        renderer.ObserveStageSpriteForReview=q=>
        {
            var p=q.PosSize;if(p.W==39)p.W=1;
            var colour=q.Color;
            if(p.W==1){packed++;colours.Add(colour);if(v2)colour=default;}
            var packet=new{PosSize=p,Color=colour,q.Dir};packets.Add(packet);stablePackets.Add(packet);
        };
        renderer.ObserveArQuadForReview=q=>
        {
            actualQuads.Add(q);
            var misc=q.Misc;if(misc.Z==39)misc.Z=2;
            var colour=q.Color;
            if(misc.Z==2){packed++;colours.Add(colour);if(v2)colour=default;}
            packets.Add(new{q.A,q.B,q.W,Color=colour,Misc=misc});
            var unchanged=q;
            if(misc.Z==2)unchanged=new GpuArQuad{Misc=misc};
            stablePackets.Add(unchanged);
        };
        var release=motion?12:21;
        var pixels=GpuRenderLoop.RenderFrames(renderer,feed,Width,Height,sample,1.0/fps,false,out _,f=>
        {
            // Both motion lanes are single presses: burst=firm .9, held=soft .45. Never a held emitter.
            feed.SetDeterministicState([],(f+1)/(double)fps,false,new HashSet<int>());
            feed.SetDeterministicBeatPulse(0);feed.SetSustainLevel(f>=release,f>=release?1:0);
            if(f==0)feed.Impact(pitch,motion && phase=="held"?.45:.9);
        },KeyParticleFixtures.Seed);
        if(view.OverCamera && (renderer.StageSpriteCount!=0 || renderer.StageKeyCount!=0 || renderer.StageNoteCount!=0))
            throw new InvalidDataException("Stage geometry leaked onto AR bounce review.");
        string? colourHash=null;
        if(v2)
        {
            // Stage emits ascending simulation slots; AR intentionally emits newest-first / origin priority.
            // Join actual material payload to the actual selected raw particle, then canonicalise identity.
            // Do NOT reorder production packets or erase per-particle colour identity to make outputs match.
            var identities=view.OverCamera?selected.ToArray():rawParticles;
            if(identities.Length!=colours.Count)throw new InvalidDataException("Cannot link bounce colours to actual particles.");
            var linked=identities.Select((particle,i)=>new{Id=Hash(JsonSerializer.SerializeToUtf8Bytes(particle,Json)),Color=colours[i]})
                .OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            colourHash=Hash(JsonSerializer.SerializeToUtf8Bytes(linked,Json));
        }
        var error=view.OverCamera && variant=="art" && billboard
            ?CheckActualCorners(selected.ToArray(),actualQuads.ToArray(),pose!.Value,direction,simWidth,simHitY):0;
        return new(pixels,new("",palette,view.Id,pitch,phase,variant,"",Hash(pixels),raw,Hash(JsonSerializer.SerializeToUtf8Bytes(packets,Json)),count,packed,colourHash,
            NonBodyHash:Hash(JsonSerializer.SerializeToUtf8Bytes(stablePackets,Json)),
            SelectedHash:Hash(JsonSerializer.SerializeToUtf8Bytes(view.OverCamera?selected.ToArray():rawParticles,Json)),MaxCornerError:error));
    }
    private static double CheckActualCorners(GpuArParticle[] selected,GpuArQuad[] quads,ArCameraPose pose,
        ArNoteDirection direction,float width,float hitY)
    {
        var bodies=quads.Where(q=>q.Misc.Z==39).ToArray();
        if(bodies.Length!=selected.Length)throw new InvalidDataException("Cannot join Bounce bodies to actual selected identities.");
        var view=new ArView(1280,720,Width,Height,PictureFit.Fit,false);var basis=ArParticleFrame.ForNotes(direction);var error=0.0;
        for(var i=0;i<bodies.Length;i++)
        {
            var p=selected[i];var q=bodies[i];var unit=pose.KeyboardWidthDepthRatio/Math.Max(1,width);
            var anchor=KeyboardLayout.Standard88.CenterX(p.Pitch)*pose.KeyboardWidthDepthRatio;
            var lateral=(p.X/Math.Max(1,width)-PianoStage.KeyCenterOf(p.Pitch))*pose.KeyboardWidthDepthRatio;
            var center=basis.Point(new(anchor,.12,0),lateral,Math.Clamp((hitY-p.Y)*unit,-.2,2.4));
            if(!ArParticleBillboard.TryAligned(pose,view,center,basis.Up,out var right,out var up))throw new InvalidDataException("No independent Bounce tangent basis.");
            // Independent of BounceParticleAppearance.Orientation/BillboardAxes: check the actual upload.
            var speed=Math.Sqrt((double)p.Vx*p.Vx+(double)p.Vy*p.Vy);
            var c=speed>1e-3?p.Vx/speed:0;var s=speed>1e-3?p.Vy/speed:-1;
            var axisX=right*c-up*s;var axisY=right*-s-up*c;
            var size=Math.Clamp(p.Size*unit,.0015,.16);var stretch=Math.Clamp(speed*.018,.5,7);
            if(!Ar3DNoteProjection.TryProjectBillboard(pose,view,center,axisX,axisY,size*1.4*(1+stretch),size*.75,out var patch))
                throw new InvalidDataException("Independent Bounce projection failed.");
            var expected=new[]{patch.BackLeft,patch.BackRight,patch.FrontRight,patch.FrontLeft};
            var actual=new[]{(q.A.X,q.A.Y,q.W.X),(q.A.Z,q.A.W,q.W.Y),(q.B.X,q.B.Y,q.W.Z),(q.B.Z,q.B.W,q.W.W)};
            for(var j=0;j<4;j++)error=Math.Max(error,Math.Max(Math.Abs(actual[j].Item1-expected[j].X),Math.Max(Math.Abs(actual[j].Item2-expected[j].Y),Math.Abs(actual[j].Item3-expected[j].W))));
        }
        if(error>.0003)throw new InvalidDataException("Bounce corners/weights disagree with independent Stage-velocity projection: "+error);
        return error;
    }
    private static void GuardPair(Shot a,Shot b,bool active,bool requireSignal=true)
    {
        if(a.RawHash!=b.RawHash || a.NonBodyHash!=b.NonBodyHash || a.SelectedHash!=b.SelectedHash || a.RawParticles!=b.RawParticles || a.PackedParticles!=b.PackedParticles)
            throw new InvalidDataException("Bounce candidate changed state, selection, population or normalized non-body packets.");
        if(active && requireSignal && (b.PackedParticles==0 || a.PixelSha256==b.PixelSha256))throw new InvalidDataException("Bounce material has no visible signal.");
        if(!active && a.PixelSha256!=b.PixelSha256)throw new InvalidDataException("Bounce candidate changed an empty output.");
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
        if(!copy.SequenceEqual(pixels))throw new InvalidDataException("Bounce PNG changed native pixels.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
