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

/// <summary>Bounded mixed live-scene diagnostic only. No shipping material, depth or preset changes.</summary>
internal static class KeyParticleEnsembleReview
{
    private static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
    internal sealed record Population(int Pitch,byte Origin,byte Route,int Count);
    internal sealed record Shot(string Id,string Recipe,string Palette,int Light,string View,string Phase,int Frame,string Variant,string File,
        string PixelSha256,string RawHash,string GeometryHash,string PacketHash,string KeyHash,int RawParticles,int Packets,
        Population[] Population,Population[] ArSelected,int StageNotes,int ArLiveBars,long Signal,int VisiblePixels,int ClippedDeltaPixels);
    internal sealed record FrameResult(byte[] Pixels,Shot Shot);
    internal static int Render(string output)
    {
        var previous=KeyboardLayout.Active;KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            Directory.CreateDirectory(output);using var renderer=GpuStageRenderer.Create(forceWarp:true);
            if(!renderer.IsWarp)throw new InvalidOperationException("Ensemble review requires WARP.");
            var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
            var shots=new List<Shot>();
            foreach(var recipe in KeyParticleEnsemblePlan.Recipes)
            foreach(var palette in KeyParticleEnsemblePlan.Palettes)
            foreach(var light in KeyParticleEnsemblePlan.Lights)
            foreach(var view in HeldParticleControlPlan.Views.Take(2))
            foreach(var sample in KeyParticleEnsemblePlan.Checkpoints)
            {
                FrameResult? off=null,classic=null,mixed=null;
                foreach(var variant in KeyParticleEnsemblePlan.Variants)
                {
                    var f=Draw(renderer,recipe,palette,light,view,camera,sample,variant);
                    if(variant=="off")off=f;
                    long signal=0;var visible=0;var clipped=0;
                    for(var i=0;i<f.Pixels.Length;i+=4)
                    {
                        var d0=Math.Abs(f.Pixels[i]-off!.Pixels[i]);var d1=Math.Abs(f.Pixels[i+1]-off.Pixels[i+1]);var d2=Math.Abs(f.Pixels[i+2]-off.Pixels[i+2]);
                        signal+=d0+d1+d2;
                        if(Math.Max(d0,Math.Max(d1,d2))>4){visible++;if(Math.Max(f.Pixels[i],Math.Max(f.Pixels[i+1],f.Pixels[i+2]))>=250)clipped++;}
                    }
                    f=f with {Shot=f.Shot with {Signal=signal,VisiblePixels=visible,ClippedDeltaPixels=clipped}};
                    if(variant=="classic")classic=f;
                    if(variant=="mixed")
                    {
                        mixed=f;Guard(classic!.Shot,f.Shot);
                        if(sample.Id is not ("rest" or "pedal-up") && (f.Shot.RawParticles==0 || signal==0))throw new InvalidDataException("Mixed scene has no particle/off signal: "+f.Shot.Id);
                        if(sample.Id=="release-pedal" && !f.Shot.Population.Any(p=>p.Origin==1))throw new InvalidDataException("Real Note Off did not spawn a release.");
                    }
                    if(variant=="front-control")
                    {
                        Guard(mixed!.Shot,f.Shot);
                        if(view.OverCamera && f.Shot.PixelSha256!=mixed.Shot.PixelSha256)throw new InvalidDataException("Stage depth flag changed AR.");
                    }
                    if((variant=="off" || sample.Id=="rest") && f.Shot.RawParticles!=0)throw new InvalidDataException("Off/rest contains live particles: "+f.Shot.Id);
                    if(sample.Id=="rest" && f.Shot.PixelSha256!=off!.Shot.PixelSha256)throw new InvalidDataException("Material changed particle-free rest.");
                    WritePng(Path.Combine(output,f.Shot.File),f.Pixels);shots.Add(f.Shot);
                }
            }
            // Projection and main key lighting must not change the actual simulation. No packed-count parity claim across outputs/caps.
            foreach(var shot in shots)
            {
                var stage=shots.Single(s=>s.Recipe==shot.Recipe && s.Palette==shot.Palette && s.Light==shot.Light && s.View=="live-stage" && s.Phase==shot.Phase && s.Variant==shot.Variant);
                var dark=shots.Single(s=>s.Recipe==shot.Recipe && s.Palette==shot.Palette && s.Light==0 && s.View==shot.View && s.Phase==shot.Phase && s.Variant==shot.Variant);
                if(shot.RawHash!=stage.RawHash || shot.RawHash!=dark.RawHash || shot.GeometryHash!=dark.GeometryHash || shot.PacketHash!=dark.PacketHash || shot.KeyHash!=dark.KeyHash)
                    throw new InvalidDataException("Lighting/output altered state or actual packets: "+shot.Id);
                if(shot.View=="ar-reference" && shot.PixelSha256!=dark.PixelSha256)throw new InvalidDataException("Virtual main light altered AR pixels.");
            }
            if(shots.Count!=KeyParticleEnsemblePlan.CaptureCount)throw new InvalidDataException("Incomplete ensemble matrix.");
            renderer.ObserveParticleStateForReview=null;renderer.ObserveStageSpriteForReview=null;renderer.ObserveArQuadForReview=null;
            renderer.ObserveStageKeyForReview=null;renderer.ObserveArParticleForReview=null;
            var root=Path.GetFullPath(Path.Combine(PrismFlowVisualReview.FindSamples(),"..",".."));
            string[] sources=["Gpu/StageShaders.hlsl","Gpu/GpuLook.cs","Gpu/GpuStageRenderer.cs","Gpu/GpuStageSimulation.cs","Gpu/GpuStageSimulation.Effects.cs",
                "Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs","Gpu/GpuRenderLoop.cs","Gpu/GpuStageFeed.cs","Gpu/HeldParticleAppearance.cs","Gpu/ReleaseParticleAppearance.cs",
                "Stage/PianoVisualSettings.cs","Stage/PianoStage.cs","Stage/PianoStage.Gpu.cs","Camera/ArParticleBillboard.cs","Camera/Ar3DNoteProjection.cs","Camera/ArCameraPose.cs",
                "Diagnostics/KeyParticleFixtures.cs","Diagnostics/HeldParticleControlPlan.cs","Diagnostics/KeyParticleEnsemblePlan.cs","Diagnostics/KeyParticleEnsembleReview.cs","docs/samples/ar-keyboard-topdown.png"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{schema=1,family="key-particle-ensemble",reviewOnly=true,
                sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA")??"workspace",adapter=renderer.AdapterName,width=640,height=360,fps=30,seed=KeyParticleEnsemblePlan.Seed,
                recipes=KeyParticleEnsemblePlan.Recipes,notes=KeyParticleEnsemblePlan.Notes,checkpoints=KeyParticleEnsemblePlan.Checkpoints,
                variants=KeyParticleEnsemblePlan.Variants,lights=KeyParticleEnsemblePlan.Lights,palettes=KeyParticleEnsemblePlan.Palettes,
                pedalOnFrame=24,pedalOffFrame=54,legacyParticleAxesForReview=false,
                materialPolicy="classic=held v2/release legacy; mixed=approved held v3/release material at existing depth; front-control=only release Stage depth .01; burst/morph stay shipping classic in every variant",
                lighting="rim62/emission149/gloss92/tilt38/shadow46/AO52/bloom35; tint/spill/notes on; not a saved user preset",
                limits="static checkpoints, not continuous playback; Stage and AR reference only; not full preset/hardware/device/performance coverage",
                sourceHashes=sources.ToDictionary(p=>p,p=>Hash(p.EndsWith(".png",StringComparison.Ordinal)?File.ReadAllBytes(Path.Combine(root,p)):
                    Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),captures=shots},Json));
            return shots.Count;
        }
        finally{KeyboardLayout.Use(previous);}
    }
    internal static FrameResult Draw(GpuStageRenderer renderer,KeyParticleEnsemblePlan.Recipe recipe,string palette,int light,
        HeldParticleControlPlan.View view,byte[] camera,KeyParticleEnsemblePlan.Checkpoint sample,string variant,bool budgetTrial=false,Action<GpuArQuad>? observeQuad=null)
    {
        var enabled=variant!="off";var art=variant is "mixed" or "front-control";
        var s=KeyParticleFixtures.Settings(KeyParticleFixtures.All[0],enabled);
        s.ImpactBurst=recipe.Burst;s.ShowWisps=enabled && recipe.Held=="Wisps";s.ShowFlame=enabled && recipe.Held=="Flames";
        s.WispAmount=32;s.WispHeight=35;s.WispSpeed=100;s.FlameIntensity=70;s.FlameHeight=45;s.FlameColorMode="Note";s.HeldParticleArt=true;
        s.ImpactMorph=enabled?recipe.Morph:"None";s.ImpactMorphIntensity=70;s.ReleaseEffect=enabled?recipe.Release:"Fade";s.ReleaseIntensity=enabled?70:0;
        s.ShowNotes=true;s.ShowHalo=true;s.ShaderKeyLight=light;s.ShaderRimLight=62;s.ShaderEmissive=149;s.ShaderGloss=92;
        s.ShaderCameraTilt=38;s.ShaderShadows=46;s.ShaderAmbientOcclusion=52;s.BloomIntensity=35;
        s.BackgroundMode="Solid";s.BackgroundColor="#10141B";s.PedalGlow=true;
        if(palette=="indigo"){s.ColorMode="Gradient";s.Palette="Custom";s.NoteColorStart=s.NoteColorEnd="#666DFF";}
        s.Clamp();var stage=new PianoStage{Width=1280,Height=720};stage.SetVisualSettings(s);
        stage.Measure(new Size(1280,720));stage.Arrange(new Rect(0,0,1280,720));stage.UpdateLayout();
        var feed=new GpuStageFeed();feed.SetStageHeight(720);
        var look=GpuLook.From(s,(p,t)=>stage.AdjustColor(stage.NoteColor(p,t)),stage.KeyboardHeight/stage.ActualHeight,stage.AdjustColor,view.OverCamera)
            .WithWispV3ForReview(art).WithFlameV3ForReview(art).WithReleaseArtForReview(art).WithReleaseFrontLayerForReview(variant=="front-control").WithArFairParticleBudgetForReview(budgetTrial);
        feed.SetLook(look);
        if(view.OverCamera)
        {
            var calibration=ArCalibration.Create(KeyboardLayout.Standard88,view.Corners!,1280,720,out var problem,70)
                ??throw new InvalidDataException("Ensemble calibration: "+problem);
            var ring=new CameraFrameRing();camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);ring.Publish(slot,1280,720,Stopwatch.GetTimestamp());
            feed.Camera=new GpuCameraInput(ring,PictureFit.Fit,false,1);
            feed.Ar=new GpuArInput(calibration,ArNoteStyle.Default,1,Quality:ArQualityTier.Ultra,ProjectedNotes:true,ShowNotes:true,ShowLiveTrails:true,ShowDamperStrip:false);
        }
        GpuArParticle[] raw=[];var selected=new List<GpuArParticle>();var packets=new List<object>();var geometry=new List<object>();var keys=new List<GpuKeyInstance>();
        renderer.ObserveParticleStateForReview=(sim,_)=>raw=Enumerable.Range(0,sim.ArParticleCount).Select(sim.ArParticleAt).ToArray();
        renderer.ObserveArParticleForReview=p=>selected.Add(p);renderer.ObserveStageKeyForReview=q=>keys.Add(q);
        renderer.ObserveStageSpriteForReview=q=>
        {
            packets.Add(q);var pos=q.PosSize;var dir=q.Dir;var k=pos.W;
            if(k>=47 && k<=53)k-=7;
            if(k is 40 or 42 or 43 or 45)k=8;else if(k is 41 or 44)k=9;else if(k==46)k=0;
            pos.W=k;if(k==17)dir.Z=dir.Z==8?7:dir.Z==6?4:dir.Z;
            geometry.Add(new{PosSize=pos,Dir=dir});
        };
        renderer.ObserveArQuadForReview=q=>
        {
            observeQuad?.Invoke(q);packets.Add(q);var misc=q.Misc;if(misc.Z is 40 or 43 or 46)misc.Z=2;
            if(misc.Z==17)misc.Y=misc.Y==8?7:misc.Y==6?4:misc.Y;
            geometry.Add(new{q.A,q.B,q.W,Misc=misc});
        };
        var pixels=GpuRenderLoop.RenderFrames(renderer,feed,640,360,sample.Frame,1d/KeyParticleEnsemblePlan.Fps,false,out _,f=>
        {
            feed.SetDeterministicState([],(f+1)/(double)KeyParticleEnsemblePlan.Fps,false,KeyParticleEnsemblePlan.PressedAt(f));
            feed.SetDeterministicBeatPulse(0);var pedal=KeyParticleEnsemblePlan.PedalAt(f);feed.SetSustainLevel(pedal,pedal?1:0);
            foreach(var n in KeyParticleEnsemblePlan.Notes)
            {
                if(f==n.On){feed.LiveNote(n.Pitch,true,n.Strength);feed.Impact(n.Pitch,n.Strength);}
                if(f==n.Off)feed.LiveNote(n.Pitch,false,n.Strength);
            }
        },KeyParticleEnsemblePlan.Seed);
        if(view.OverCamera && (renderer.StageKeyCount!=0 || renderer.StageNoteCount!=0 || renderer.StageSpriteCount!=0))throw new InvalidDataException("Stage geometry leaked onto AR.");
        if(sample.Id is "single-held" or "chord-held" && (view.OverCamera?renderer.ArLiveBarCount:renderer.StageNoteCount)==0)throw new InvalidDataException("Held note bars are absent from the integration scene.");
        var id=$"{recipe.Id}-{palette}-l{light}-{view.Id}-{sample.Id}-{variant}";
        var shot=new Shot(id,recipe.Id,palette,light,view.Id,sample.Id,sample.Frame,variant,id+".png",Hash(pixels),
            Hash(JsonSerializer.SerializeToUtf8Bytes(raw.Select(p=>new{Particle=p,p.MaterialRoute}),Json)),Hash(JsonSerializer.SerializeToUtf8Bytes(geometry,Json)),
            Hash(JsonSerializer.SerializeToUtf8Bytes(packets,Json)),Hash(JsonSerializer.SerializeToUtf8Bytes(keys,Json)),raw.Length,packets.Count,PopulationOf(raw),PopulationOf(selected),renderer.StageNoteCount,renderer.ArLiveBarCount,0,0,0);
        return new(pixels,shot);
    }
    private static Population[] PopulationOf(IEnumerable<GpuArParticle> particles)=>particles.GroupBy(p=>(p.Pitch,p.Origin,p.MaterialRoute))
        .OrderBy(g=>g.Key.Pitch).ThenBy(g=>g.Key.Origin).ThenBy(g=>g.Key.MaterialRoute).Select(g=>new Population(g.Key.Pitch,g.Key.Origin,g.Key.MaterialRoute,g.Count())).ToArray();
    private static void Guard(Shot a,Shot b)
    {
        if(a.RawHash!=b.RawHash || a.RawParticles!=b.RawParticles || a.GeometryHash!=b.GeometryHash || a.Packets!=b.Packets || a.KeyHash!=b.KeyHash)
            throw new InvalidDataException("Ensemble material/depth changed raw state, key packets or normalized geometry: "+b.Id);
    }
    private static void WritePng(string file,byte[] pixels)
    {
        var source=BitmapSource.Create(640,360,96,96,PixelFormats.Bgra32,null,pixels,640*4);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));
        using(var stream=File.Create(file))png.Save(stream);
        using var input=File.OpenRead(file);var decoded=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var converted=new FormatConvertedBitmap(decoded.Frames[0],PixelFormats.Bgra32,null,0);var copy=new byte[pixels.Length];converted.CopyPixels(copy,640*4,0);
        if(!copy.SequenceEqual(pixels))throw new InvalidDataException("Ensemble native PNG altered pixels.");
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
