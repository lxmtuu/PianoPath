using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace PianoPath;

/// <summary>Production AR geometry vs frozen historical axes, same simulation and approved materials.</summary>
internal static class ArParticleAlignmentReview
{
    private sealed record Shot(string Id, string Family, string View, double Tilt, double Azimuth, int Frame,
        bool Legacy, string File, string PixelSha256, string StateHash, int Particles, int Notes,
        double MaxAxisErrorDegrees, string IntrinsicHash);

    internal static int Render(string output)
    {
        Directory.CreateDirectory(output);
        var oldLayout = KeyboardLayout.Active; KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            using var renderer = GpuStageRenderer.Create(forceWarp:true);
            var sourceCamera = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            var captures = new List<Shot>();
            var directions = new[] {new ArNoteDirection(),new ArNoteDirection(45,-45),new ArNoteDirection(90,90),new ArNoteDirection(135,60)};
            foreach (var family in new[] {"wisps","flames"})
            foreach (var cameraView in HeldParticleControlPlan.Views.Where(v=>v.OverCamera))
            foreach (var direction in directions)
            foreach (var frame in new[] {11,18})
            foreach (var legacy in new[] {true,false})
            {
                var route = AuxParticleTimeline.Routes.Single(r=>r.Source==AuxParticleTimeline.Channel.Held && r.Wisp==(family=="wisps"));
                var settings = AuxParticleFixtures.Settings(route); settings.HeldParticleArt=true; settings.WispAmount=8;
                var stage = new PianoStage {Width=1280,Height=720};stage.SetVisualSettings(settings);
                stage.Measure(new Size(1280,720));stage.Arrange(new Rect(0,0,1280,720));stage.UpdateLayout();
                var look = GpuLook.From(settings,(pitch,track)=>stage.AdjustColor(stage.NoteColor(pitch,track)),
                    stage.KeyboardHeight/stage.ActualHeight,stage.AdjustColor,true).WithWispV3ForReview(true).WithFlameV3ForReview(true);
                var feed = new GpuStageFeed();feed.SetStageHeight(720);feed.SetLook(look);
                var calibration = ArCalibration.Create(KeyboardLayout.Standard88,cameraView.Corners!,1280,720,out _,70)!;
                var pose = calibration.CameraPose!.Value;
                var camera = HeldParticleControlPlan.CameraFor(cameraView,sourceCamera);var ring = new CameraFrameRing();
                camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);ring.Publish(slot,1280,720,Stopwatch.GetTimestamp());
                feed.Camera=new GpuCameraInput(ring,PictureFit.Fit,false,1);
                feed.Ar=new GpuArInput(calibration,ArNoteStyle.Default,1,Quality:ArQualityTier.Ultra,ProjectedNotes:true,
                    ShowDamperStrip:false,NoteDirection:direction,ShowRisingMidiNotes:true) {LegacyParticleAxesForReview=legacy};
                var packed = new List<GpuArParticle>();var quads = new List<GpuArQuad>();
                var rawHash="";var width=0f;var hitY=0f;
                renderer.ObserveArParticleForReview=p=>packed.Add(p);
                renderer.ObserveHeldQuadGeometryForReview=q=>quads.Add(q);
                renderer.ObserveParticleStateForReview=(sim,_)=>
                {
                    rawHash=Hash(JsonSerializer.Serialize(Enumerable.Range(0,sim.ArParticleCount).Select(sim.ArParticleAt).ToArray(),JsonOptions));
                    width=sim.SimulationWidth;hitY=sim.SimulationHitY;
                };
                var pixels=GpuRenderLoop.RenderFrames(renderer,feed,640,360,frame,1.0/30,false,out _,f=>
                {
                    var held=f<12;
                    feed.SetDeterministicState([],(f+1)/30.0,false,held?new HashSet<int>{60}:new HashSet<int>());
                    feed.SetDeterministicBeatPulse(0);feed.SetSustainLevel(!held,held?0:1);
                    if(f==0)feed.LiveNote(60,true,.9);if(f==12)feed.LiveNote(60,false,.9);
                },KeyParticleFixtures.Seed);
                if(packed.Count==0 || packed.Count!=quads.Count || renderer.ArRisingMidiNoteCount==0)
                    throw new InvalidDataException("Alignment fixture needs actual particles and notes.");
                var maxError=0.0;var view=new ArView(1280,720,640,360,PictureFit.Fit,false);
                for(var i=0;i<packed.Count;i++)
                {
                    var p=packed[i];var q=quads[i];var unit=pose.KeyboardWidthDepthRatio/Math.Max(1,width);
                    var anchorX=KeyboardLayout.Standard88.CenterX(p.Pitch)*pose.KeyboardWidthDepthRatio;
                    var lateral=(p.X/Math.Max(1,width)-PianoStage.KeyCenterOf(p.Pitch))*pose.KeyboardWidthDepthRatio;
                    if(p.Wisp)
                    {
                        if(look.ParticleFlow<=0)lateral+=WispMotion.OffsetPrecise(look.WispTurbulence,look.WispWidth,(float)p.Age,(float)p.Phase)*unit;
                    }
                    var basis=ArParticleFrame.ForNotes(direction);
                    var rise=Math.Clamp((hitY-p.Y)*unit,-.2,2.4);
                    var center=legacy ? new ArVec3(anchorX+lateral,.12,-rise) : basis.Point(new(anchorX,.12,0),lateral,rise);
                    ArVec3 up;
                    var facing=legacy ? ArParticleBillboard.TryFacing(pose,center,out _,out up)
                        : ArParticleBillboard.TryAligned(pose,view,center,basis.Up,out _,out up);
                    if(!facing || !HeldParticleAppearance.TryResolve(p.Wisp,p.Size,p.Age,p.Life,p.Phase,p.Glow,p.Color,
                            look.WispWidth,look.FlameNoteColor,out var surface,true,true)) throw new InvalidDataException("Invalid aligned surface.");
                    if(surface.BaseAnchored)center+=up*(surface.HalfHeight*unit);
                    if(!pose.TryProjectDelta(pose.ToCamera(center),pose.DirectionToCamera(basis.Up),view,out var expected))
                        throw new InvalidDataException("Cannot project expected axis.");
                    var dx=(q.B.X+q.B.Z-q.A.X-q.A.Z)*.5;var dy=(q.B.Y+q.B.W-q.A.Y-q.A.W)*.5;
                    var n=Math.Sqrt(expected.X*expected.X+expected.Y*expected.Y)*Math.Sqrt(dx*dx+dy*dy);
                    if(n>1e-8 && expected.X*expected.X+expected.Y*expected.Y>1e-10) maxError=Math.Max(maxError,Math.Acos(Math.Clamp((dx*expected.X+dy*expected.Y)/n,-1,1))*180/Math.PI);
                }
                if(!legacy && maxError>1)throw new InvalidDataException($"Actual packed particle axis misses note path: {maxError} degrees.");
                var id=$"{family}-{cameraView.Id}-t{direction.TiltDegrees}-a{direction.AzimuthDegrees}-f{frame}";
                var file=$"{id}-{(legacy?"legacy":"aligned")}.png";
                WritePng(Path.Combine(output,file),pixels);
                var intrinsic=Hash(JsonSerializer.Serialize(quads.Select(q=>new{q.Color,q.Misc}).ToArray(),JsonOptions));
                var shot=new Shot(id,family,cameraView.Id,direction.TiltDegrees,direction.AzimuthDegrees,frame,legacy,file,
                    Convert.ToHexString(SHA256.HashData(pixels)),rawHash,packed.Count,renderer.ArRisingMidiNoteCount,maxError,intrinsic);
                if(!legacy)
                {
                    var before=captures.Last();
                    if(before.StateHash!=shot.StateHash || before.IntrinsicHash!=shot.IntrinsicHash || before.Particles!=shot.Particles
                        || before.Notes!=shot.Notes || before.PixelSha256==shot.PixelSha256)
                        throw new InvalidDataException("Alignment must change geometry only, not state/selection/material or note count.");
                }
                captures.Add(shot);
            }
            renderer.ObserveArParticleForReview=null;renderer.ObserveHeldQuadGeometryForReview=null;renderer.ObserveParticleStateForReview=null;
            var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
            string[] sources=["Gpu/StageShaders.hlsl","Gpu/GpuStageRenderer.cs","Gpu/GpuStageSimulation.cs","Gpu/GpuStageSimulation.Effects.cs",
                "Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs","Gpu/GpuLook.cs","Gpu/GpuStageFeed.cs","Gpu/HeldParticleAppearance.cs",
                "Camera/ArCameraPose.cs","Camera/ArParticleBillboard.cs","Stage/PianoStage.cs","Stage/PrismFlowPalette.cs",
                "Diagnostics/HeldParticleControlPlan.cs","Diagnostics/HeldParticleControlReview.cs","Diagnostics/AuxParticleFixtures.cs",
                "Diagnostics/KeyParticleFixtures.cs","docs/samples/ar-keyboard-topdown.png","Diagnostics/HeldLightIsolationPlan.cs",
                "Camera/Ar3DNoteProjection.cs","Diagnostics/ArParticleAlignmentReview.cs","Gpu/GpuRenderLoop.cs"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new
            {
                sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",width=640,height=360,seed=KeyParticleFixtures.Seed,
                fps=30,releaseFrame=12,pitch=60,wispAmount=8,flameIntensity=70,captureCount=captures.Count,
                sourceHashes=sources.ToDictionary(p=>p,p=>p.EndsWith(".png",StringComparison.Ordinal)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,p))))
                    : Hash(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n"))),captures
            },new JsonSerializerOptions{WriteIndented=true}));
            return captures.Count;
        }
        finally {KeyboardLayout.Use(oldLayout);}
    }
    private static void WritePng(string path,byte[] pixels)
    {
        var source=System.Windows.Media.Imaging.BitmapSource.Create(640,360,96,96,System.Windows.Media.PixelFormats.Bgra32,null,pixels,640*4);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using(var stream=File.Create(path))encoder.Save(stream);
        using var input=File.OpenRead(path);
        var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(input,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var decoded=new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0],System.Windows.Media.PixelFormats.Bgra32,null,0);
        var copy=new byte[pixels.Length];decoded.CopyPixels(copy,640*4,0);
        if(!copy.SequenceEqual(pixels))throw new InvalidDataException("Alignment PNG round trip changed pixels.");
    }
    private static readonly JsonSerializerOptions JsonOptions=new(){IncludeFields=true};
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
