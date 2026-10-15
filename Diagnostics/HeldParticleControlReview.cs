using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Lighting × particle controls, fixed input/time/seed. All output is synthetic WARP evidence;
/// no shader changes, temporal review, real camera, hardware cost or visual acceptance implied.</summary>
internal static class HeldParticleControlReview
{
    private const int Width = HeldParticleControlPlan.Width, Height = HeldParticleControlPlan.Height;
    private sealed record Crop(int X, int Y, int Width, int Height);
    private sealed record Capture(string Variant, bool Particles, bool KeyLight, string File, string PixelSha256,
        int StageKeys, int StageSprites, int ArKeys, int ArLights, int ArParticles,
        bool Tint, bool Spill, Coverage? Coverage, Geometry? Geometry);
    private sealed record Geometry(int Count, double? MinWidth, double? MaxWidth, double? MeanWidth,
        double? MinHeight, double? MaxHeight, double? MeanHeight, double? CenterYMin, double? CenterYMax, string Hash);
    private sealed record Coverage(IReadOnlyList<HeldLightIsolationPlan.Row> Raw,
        IReadOnlyList<HeldLightIsolationPlan.Row> Eligible, IReadOnlyList<HeldLightIsolationPlan.Row> Packed,
        string RawHash, string EligibleHash, string PackedHash);
    private sealed record Delta(int ChangedPixels, int OverFourPixels, int MaxChannelDelta, double MeanAbsoluteRgbDelta);

    internal static int Render(string output, bool isolateLight = false, bool wispV3 = false)
    {
        if (wispV3 && !isolateLight) throw new ArgumentException("Wisps v3 requires independent light controls.");
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            Directory.CreateDirectory(output);
            var samples = PrismFlowVisualReview.FindSamples();
            var original = PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(samples,"ar-keyboard-topdown.png"));
            var cameras = HeldParticleControlPlan.Views.Where(v => v.OverCamera)
                .ToDictionary(v => v.Id, v => HeldParticleControlPlan.CameraFor(v, original));
            var cameraHashes = cameras.ToDictionary(p => p.Key, p => Hash(p.Value));
            using var renderer = GpuStageRenderer.Create(forceWarp:true);
            if (!renderer.IsWarp) throw new InvalidOperationException("Held controls require WARP.");
            var scenes = new List<object>(); var count = 0;
            var effectOffHashes = new Dictionary<string,string>();
            using var baseline = isolateLight ? JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"..",wispV3 ? "held-light-isolation" : "held-particle-controls","manifest.json"))) : null;
            var entries = isolateLight ? HeldLightIsolationPlan.Entries
                : HeldParticleControlPlan.Scenes.Select(s => new HeldLightIsolationPlan.Entry(s.Id,s,"normal",32));
            if (wispV3) entries=entries.Where(e=>e.Scene.Family=="wisps" && e.Load=="low");
            var variants = isolateLight ? HeldLightIsolationPlan.Variants
                : HeldParticleControlPlan.Variants.Select(v => new HeldLightIsolationPlan.Variant(v.Id,v.Particles,v.KeyLight,v.KeyLight)).ToArray();
            var populations = new Dictionary<string,string>();
            foreach (var entry in entries)
            {
                var scene = entry.Scene; var id = entry.Id;
                var route = AuxParticleTimeline.Routes.Single(r => r.Id == scene.Family);
                var overCamera = scene.View.OverCamera;
                var pixels = new List<byte[]>(); var captures = new List<Capture>();
                Crop? crop = null;
                foreach (var variant in variants)
                {
                    var s = AuxParticleFixtures.Settings(route, variant.Particles);
                    s.HeldParticleArt = true; s.KeyboardSize = "88"; s.WispAmount = entry.WispAmount;
                    if (scene.Palette.FixedColor is { } color)
                    {
                        s.ColorMode = "Gradient"; s.Palette = "Custom";
                        s.NoteColorStart = s.NoteColorEnd = color;
                    }
                    s.Clamp();
                    var stage = new PianoStage { Width=Width, Height=Height };
                    stage.SetVisualSettings(s);
                    stage.Measure(new Size(Width,Height)); stage.Arrange(new Rect(0,0,Width,Height)); stage.UpdateLayout();
                    var feed = new GpuStageFeed(); feed.SetStageHeight(Height);
                    var look = GpuLook.From(s, (pitch,track) => stage.AdjustColor(stage.NoteColor(pitch,track)),
                        stage.KeyboardHeight / stage.ActualHeight, stage.AdjustColor, overCamera);
                    look=look with { WispArtV3ForReview=wispV3 };
                    feed.SetLook(look);
                    // KeyLighting=0 only mutes spill; ShaderKeyLight changes static ivory illumination.
                    // Neither is an honest Stage "key light off" control. Remove packed tint/emission/spill
                    // at the render seam instead, leaving the simulation, colours and press geometry alone.
                    renderer.SuppressStageKeyLightingForReview = false;
                    renderer.SuppressStageKeyTintForReview = !overCamera && !variant.Tint;
                    renderer.SuppressStageKeySpillForReview = !overCamera && !variant.Spill;
                    renderer.SuppressArKeyTintForReview = !variant.Tint;
                    renderer.SuppressArKeySpillForReview = !variant.Spill;
                    var raw = new List<HeldLightIsolationPlan.Sample>();
                    var eligible = new List<HeldLightIsolationPlan.Sample>();
                    var packed = new List<HeldLightIsolationPlan.Sample>();
                    HeldLightIsolationPlan.Sample Sample(GpuArParticle p) => new(p.Pitch,p.Age,p.Life);
                    renderer.ObserveParticleStateForReview = isolateLight ? (simulation, currentLook) =>
                    {
                        for (var i=0;i<simulation.ArParticleCount;i++)
                        {
                            var p=simulation.ArParticleAt(i); raw.Add(Sample(p));
                            if (HeldParticleAppearance.TryResolve(p.Wisp,p.Size,p.Age,p.Life,p.Phase,p.Glow,p.Color,
                                currentLook.WispWidth,currentLook.FlameNoteColor,out var surface,currentLook.WispArtV3ForReview) && surface.Color.W>.003f)
                                eligible.Add(Sample(p));
                        }
                    } : null;
                    renderer.ObserveStageParticleForReview = isolateLight && !overCamera ? p => packed.Add(Sample(p)) : null;
                    renderer.ObserveArParticleForReview = isolateLight && overCamera ? p => packed.Add(Sample(p)) : null;
                    var corners=new List<float[]>();
                    renderer.ObserveHeldSpriteGeometryForReview=isolateLight && !overCamera ? s=>
                    {
                        var x=s.PosSize.X;var y=s.PosSize.Y;var w=s.PosSize.Z;var h=s.Dir.X;
                        corners.Add([x-w,y-h,x+w,y-h,x+w,y+h,x-w,y+h]);
                    } : null;
                    renderer.ObserveHeldQuadGeometryForReview=isolateLight && overCamera ? q=>
                        corners.Add([q.A.X,q.A.Y,q.A.Z,q.A.W,q.B.X,q.B.Y,q.B.Z,q.B.W]) : null;
                    if (overCamera)
                    {
                        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, scene.View.Corners!,
                            Width,Height,out var problem,HeldParticleControlPlan.Fov)
                            ?? throw new InvalidDataException($"Held control calibration: {problem}");
                        var camera = cameras[scene.View.Id]; var ring = new CameraFrameRing();
                        camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);
                        ring.Publish(slot,Width,Height,Stopwatch.GetTimestamp());
                        feed.Camera = new GpuCameraInput(ring,PictureFit.Fit,false,1);
                        // AR's native control gates both key patches and projected neighbouring-key spill.
                        feed.Ar = new GpuArInput(calibration,ArNoteStyle.Default,1,KeyLight:(variant.Tint || variant.Spill) ? .6f : 0,
                            Quality:ArQualityTier.Ultra,ProjectedNotes:true,ShowDamperStrip:false);
                    }
                    var pressed = scene.Pattern.Pitches.ToHashSet();
                    void BeforeFrame(int frame)
                    {
                        feed.SetDeterministicState([], (frame+1)/(double)HeldParticleControlPlan.Fps, false, pressed);
                        feed.SetDeterministicBeatPulse(0); feed.SetSustainLevel(false,0);
                        if (frame == 0) foreach (var pitch in scene.Pattern.Pitches) feed.LiveNote(pitch,true,.9);
                    }
                    var image = GpuRenderLoop.RenderFrames(renderer,feed,Width,Height,HeldParticleControlPlan.Frames-1,
                        1d/HeldParticleControlPlan.Fps,false,out _,BeforeFrame,KeyParticleFixtures.Seed);
                    var particles = overCamera ? renderer.ArParticleFxCount : renderer.StageSpriteCount;
                    if (variant.Particles ? particles <= 0 : particles != 0)
                        throw new InvalidDataException($"Unexpected particle packing: {id}/{variant.Id}={particles}");
                    if (overCamera && (renderer.StageKeyCount != 0 || renderer.StageSpriteCount != 0 || renderer.StageNoteCount != 0))
                        throw new InvalidDataException($"Stage geometry leaked into AR: {id}/{variant.Id}");
                    if (overCamera && ((!variant.Tint ? renderer.ArKeyQuadCount != 0 : renderer.ArKeyQuadCount <= 0)
                        || (!variant.Spill ? renderer.ArNoteLightCount != 0 : renderer.ArNoteLightCount <= 0)))
                        throw new InvalidDataException($"AR key-light gate failed: {id}/{variant.Id}");
                    var file = $"frames/{id}/{variant.Id}.png";
                    PrismFlowVisualReview.WriteAndVerifyPng(Path.Combine(output,file),image);
                    var hash = Hash(image);
                    if (isolateLight && entry.Load=="normal" && variant.Tint==variant.Spill)
                    {
                        var prior=baseline!.RootElement.GetProperty("scenes").EnumerateArray().Single(x=>x.GetProperty("Id").GetString()==scene.Id);
                        var priorVariant=variant.Particles ? (variant.Tint ? "combined" : "particles-only") : (variant.Tint ? "light-only" : "neither");
                        var priorHash=prior.GetProperty("captures").EnumerateArray().Single(x=>x.GetProperty("Variant").GetString()==priorVariant).GetProperty("PixelSha256").GetString();
                        if (hash!=priorHash) throw new InvalidDataException($"Split seam changed grouped-light baseline pixels: {id}/{variant.Id}");
                    }
                    if (!variant.Particles)
                    {
                        var key = $"{scene.Palette.Id}/{scene.Pattern.Id}/{scene.View.Id}/{variant.Id}";
                        if (effectOffHashes.TryGetValue(key,out var before) && before != hash)
                            throw new InvalidDataException($"Family changed its particle-off control: {key}");
                        effectOffHashes[key] = hash;
                    }
                    Coverage? coverage = null;
                    if (isolateLight)
                    {
                        if (packed.Count!=particles) throw new InvalidDataException($"Observer/count mismatch: {id}");
                        coverage = new Coverage(HeldLightIsolationPlan.Summarize(scene.Pattern.Pitches,raw),
                            HeldLightIsolationPlan.Summarize(scene.Pattern.Pitches,eligible),
                            HeldLightIsolationPlan.Summarize(scene.Pattern.Pitches,packed),
                            SampleHash(raw),SampleHash(eligible),SampleHash(packed));
                        if (variant.Particles && (entry.Load=="low" || scene.Family=="flames"))
                        {
                            if (eligible.Count>=192 || coverage.PackedHash!=coverage.EligibleHash
                                || coverage.Packed.Any(r=>r.Count==0))
                                throw new InvalidDataException($"Sub-cap fixture lost an eligible pitch/particle: {id}");
                        }
                        if (variant.Particles && overCamera && scene.Family=="wisps" && entry.Load=="normal"
                            && (eligible.Count<=192 || packed.Count!=192))
                            throw new InvalidDataException($"Normal wisp chord no longer exercises the existing AR cap: {id}");
                        var populationKey=$"{scene.Family}/{entry.Load}/{scene.Palette.Id}/{variant.Particles}";
                        if (populations.TryGetValue(populationKey,out var previous) && previous!=coverage.RawHash)
                            throw new InvalidDataException($"Lighting/output altered simulated population: {id}");
                        populations[populationKey]=coverage.RawHash;
                    }
                    Geometry? geometry=null;
                    if (isolateLight)
                    {
                        if(corners.Count!=particles) throw new InvalidDataException("Geometry observer/count mismatch.");
                        double Distance(float[] c,int a,int b)=>Math.Sqrt(Math.Pow(c[a]-c[b],2)+Math.Pow(c[a+1]-c[b+1],2));
                        var widths=corners.Select(c=>(Distance(c,0,2)+Distance(c,6,4))*.5).ToArray();
                        var heights=corners.Select(c=>(Distance(c,0,6)+Distance(c,2,4))*.5).ToArray();
                        var centres=corners.Select(c=>(c[1]+c[3]+c[5]+c[7])/4d).ToArray();
                        geometry=new(corners.Count,widths.Length==0?null:widths.Min(),widths.Length==0?null:widths.Max(),widths.Length==0?null:widths.Average(),
                            heights.Length==0?null:heights.Min(),heights.Length==0?null:heights.Max(),heights.Length==0?null:heights.Average(),
                            centres.Length==0?null:centres.Min(),centres.Length==0?null:centres.Max(),Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(corners))));
                    }
                    if(wispV3)
                    {
                        var prior=baseline!.RootElement.GetProperty("scenes").EnumerateArray().Single(x=>x.GetProperty("Id").GetString()==id)
                            .GetProperty("captures").EnumerateArray().Single(c=>c.GetProperty("Variant").GetString()==variant.Id);
                        var canonical=JsonSerializer.Serialize(JsonSerializer.Deserialize<Coverage>(prior.GetProperty("Coverage").GetRawText()));
                        if(JsonSerializer.Serialize(coverage)!=canonical) throw new InvalidDataException("V3 changed simulation/eligible/packed samples.");
                        if(geometry!.Hash!=prior.GetProperty("Geometry").GetProperty("Hash").GetString())
                            throw new InvalidDataException("V3 changed packed geometry.");
                        if((!variant.Particles)!=(hash==prior.GetProperty("PixelSha256").GetString()))
                            throw new InvalidDataException("V3 must change active pixels and preserve particle-off controls.");
                    }
                    // GpuArLayer counters describe its last Build and remain cached while Stage is active.
                    // Record the active output only, not the previous AR scene's stale diagnostics.
                    captures.Add(new Capture(variant.Id,variant.Particles,variant.Tint || variant.Spill,file,hash,renderer.StageKeyCount,
                        renderer.StageSpriteCount,overCamera ? renderer.ArKeyQuadCount : 0,
                        overCamera ? renderer.ArNoteLightCount : 0,overCamera ? renderer.ArParticleFxCount : 0,
                        variant.Tint,variant.Spill,coverage,geometry));
                    pixels.Add(image); count++;
                    crop ??= CropFor(scene,look.KeyboardFraction);
                }
                foreach (var group in captures.GroupBy(c=>c.Particles))
                    if (group.Select(c=>(c.StageSprites,c.ArParticles,c.Coverage?.PackedHash,c.Coverage?.EligibleHash)).Distinct().Count()!=1)
                        throw new InvalidDataException($"Light changed particle packing: {id}");
                foreach (var group in captures.GroupBy(c=>c.Tint))
                    if (group.Select(c=>c.ArKeys).Distinct().Count()!=1)
                        throw new InvalidDataException($"Spill/particles changed key-tint packing: {id}");
                foreach (var group in captures.GroupBy(c=>c.Spill))
                    if (group.Select(c=>c.ArLights).Distinct().Count()!=1)
                        throw new InvalidDataException($"Tint/particles changed spill packing: {id}");
                var sheets = new List<object>();
                for (var offset=0;offset<captures.Count;offset+=4)
                {
                    var quartet=Quartet(pixels.Skip(offset).Take(4).ToArray(),crop!);
                    var name=isolateLight ? $"{id}-{(offset==0 ? "off" : "on")}" : id;
                    var file=$"sheets/{name}.png";
                    WritePng(Path.Combine(output,file),quartet,crop!.Width*2,crop.Height*2);
                    sheets.Add(new { particles=offset!=0,sheetFile=file,sheetPixelSha256=Hash(quartet),indices=Enumerable.Range(offset,4).ToArray() });
                }
                var legacyQuartet=Quartet(pixels.Take(4).ToArray(),crop!);
                var comparisons=new List<object>();
                if (!isolateLight)
                {
                    comparisons.Add(Compare("particles-unlit",2,0)); comparisons.Add(Compare("particles-lit",3,1));
                    comparisons.Add(Compare("light-without-particles",1,0)); comparisons.Add(Compare("light-with-particles",3,2));
                }
                else
                {
                    for (var k=0;k<4;k++) comparisons.Add(Compare($"particles-{k}",k+4,k));
                    foreach (var offset in new[] {0,4})
                    {
                        comparisons.Add(Compare($"tint-no-spill-{offset}",offset+1,offset));
                        comparisons.Add(Compare($"tint-with-spill-{offset}",offset+3,offset+2));
                        comparisons.Add(Compare($"spill-no-tint-{offset}",offset+2,offset));
                        comparisons.Add(Compare($"spill-with-tint-{offset}",offset+3,offset+1));
                    }
                }
                scenes.Add(new
                {
                    Id=id, scene.Family, palette=scene.Palette.Id, fixedColor=scene.Palette.FixedColor,
                    pattern=scene.Pattern.Id, pitches=scene.Pattern.Pitches, view=scene.View.Id, load=entry.Load,wispAmount=entry.WispAmount,
                    cameraSha256=overCamera ? cameraHashes[scene.View.Id] : null,
                    crop, sheetFile=isolateLight ? null : $"sheets/{id}.png",
                    sheetPixelSha256=isolateLight ? null : Hash(legacyQuartet),
                    sheets=isolateLight ? sheets : null, captures, comparisons
                });
                object Compare(string id,int after,int before) => new
                {
                    id, after=captures[after].Variant, before=captures[before].Variant,
                    fullFrame=Difference(pixels[after],pixels[before]),
                    crop=Difference(Extract(pixels[after],crop!),Extract(pixels[before],crop!))
                };
            }
            var root = Path.GetFullPath(Path.Combine(samples,"..",".."));
            string[] sourceFiles = ["Gpu/StageShaders.hlsl","Gpu/GpuStageRenderer.cs","Gpu/GpuStageSimulation.cs",
                "Gpu/GpuStageSimulation.Effects.cs","Gpu/GpuArLayer.cs","Gpu/GpuArLayer.Effects.cs","Gpu/GpuLook.cs",
                "Gpu/GpuStageFeed.cs","Gpu/HeldParticleAppearance.cs","Camera/ArCameraPose.cs","Camera/ArParticleBillboard.cs",
                "Stage/PianoStage.cs","Stage/PrismFlowPalette.cs","Diagnostics/HeldParticleControlPlan.cs",
                "Diagnostics/HeldParticleControlReview.cs","Diagnostics/AuxParticleFixtures.cs","Diagnostics/KeyParticleFixtures.cs",
                "docs/samples/ar-keyboard-topdown.png","Diagnostics/HeldLightIsolationPlan.cs"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new
            {
                schema=wispV3 ? 3 : isolateLight ? 2 : 1, wispV3ForReview=wispV3, sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "workspace",
                sourceHashMethod="SHA256; UTF-8 text without BOM, CRLF normalized to LF; PNG bytes unchanged",
                sourceHashes=sourceFiles.ToDictionary(p => p,p => Hash(p.EndsWith(".png",StringComparison.Ordinal)
                    ? File.ReadAllBytes(Path.Combine(root,p))
                    : Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),
                adapter=renderer.AdapterName, width=Width,height=Height, seed=KeyParticleFixtures.Seed,
                fps=HeldParticleControlPlan.Fps,frames=HeldParticleControlPlan.Frames,time=.6,velocity=.9,pedal=false,
                heldArt=true, arKeyLightOn=.6, arDelay=0, arQuality="Ultra", mirrored=false, fit="Fit",
                lightControl=isolateLight ? "Stage tint/emission group Emit.W and spill Base.Y independently masked; AR key patches and projected note-light spill independently gated. Static lighting/depression unchanged; default-off review seams only."
                    : "Stage: packed key Emit.W/Base.Y=0 (static lighting and depression unchanged); AR: KeyLight=0 (tint + projected spill).",
                cameraMethod="Reference synthetic BGRA; other views nearest-neighbour planar reprojection with calibration-matched corners. Not re-lit 3D or real-camera evidence.",
                variants,
                views=HeldParticleControlPlan.Views.Select(v => new {v.Id, corners=v.Corners?.Select(c => new {c.X,c.Y})}),
                sheetOrder=isolateLight ? "two quartets: particles off/on; TL neither, TR tint, BL spill, BR both; native pixels"
                    : "top-left neither; top-right light-only; bottom-left particles-only; bottom-right combined; native pixels, no resizing",
                coverageMethod=isolateLight ? "Raw live pool; Stage-eligible alpha >.003 surface candidates; actually packed Stage/AR callbacks. Absolute-age bins 0/.2/.4 seconds; not screen-space visibility. Sorted sample hashes (pitch,age,life); AR cap unchanged at 192." : null,
                metric="8-bit display RGB differences only; >4 footprint is not brightness, quality or an additive decomposition.",
                geometryMethod=isolateLight ? "Actual packed corners in output pixels; edge lengths and center-Y range, not visible shader area. Candidate preserves geometry hash exactly." : null,
                captureCount=count,sceneCount=scenes.Count,scenes
            },new JsonSerializerOptions {WriteIndented=true}));
            return count;
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static Crop CropFor(HeldParticleControlPlan.Scene scene,double keyboardFraction)
    {
        var u = scene.Pattern.Pitches.Average(p => KeyboardLayout.Standard88.CenterX(p));
        var center = scene.View.OverCamera
            ? Homography.FromPoints([(0,0),(1,0),(1,1),(0,1)],scene.View.Corners!)!.Value.Apply(u,.12)
            : (X:u*Width,Y:Height*(1-keyboardFraction));
        return new Crop(Math.Clamp((int)Math.Floor(center.X)-192,0,Width-384),
            Math.Clamp((int)Math.Floor(center.Y)-240,0,Height-360),384,360);
    }
    private static string SampleHash(IEnumerable<HeldLightIsolationPlan.Sample> samples) =>
        Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(samples.OrderBy(s=>s.Pitch).ThenBy(s=>s.Age).ThenBy(s=>s.Life))));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Extract(byte[] image,Crop crop)
    {
        var output = new byte[crop.Width*crop.Height*4];
        for (var y=0;y<crop.Height;y++) Buffer.BlockCopy(image,((y+crop.Y)*Width+crop.X)*4,output,y*crop.Width*4,crop.Width*4);
        return output;
    }
    private static byte[] Quartet(IReadOnlyList<byte[]> images,Crop crop)
    {
        var sheet = new byte[crop.Width*crop.Height*16]; var stride=crop.Width*2*4;
        for (var k=0;k<4;k++)
        for (var y=0;y<crop.Height;y++)
            Buffer.BlockCopy(images[k],((y+crop.Y)*Width+crop.X)*4,sheet,
                ((k/2)*crop.Height+y)*stride+(k%2)*crop.Width*4,crop.Width*4);
        return sheet;
    }
    private static Delta Difference(byte[] after,byte[] before)
    {
        int changed=0,overFour=0,max=0; long sum=0;
        for (var i=0;i<after.Length;i+=4)
        {
            var d=Math.Max(Math.Abs(after[i]-before[i]),Math.Max(Math.Abs(after[i+1]-before[i+1]),Math.Abs(after[i+2]-before[i+2])));
            if (d>0) changed++; if (d>4) overFour++; max=Math.Max(max,d);
            sum+=Math.Abs(after[i]-before[i])+Math.Abs(after[i+1]-before[i+1])+Math.Abs(after[i+2]-before[i+2]);
        }
        return new Delta(changed,overFour,max,sum/(after.Length/4d*3));
    }
    private static void WritePng(string path,byte[] pixels,int width,int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream=File.Create(path)) encoder.Save(stream);
        using var read=File.OpenRead(path);
        var decoded=new FormatConvertedBitmap(BitmapDecoder.Create(read,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0],PixelFormats.Bgra32,null,0);
        var roundTrip=new byte[pixels.Length]; decoded.CopyPixels(roundTrip,width*4,0);
        if (decoded.PixelWidth!=width || decoded.PixelHeight!=height || !pixels.AsSpan().SequenceEqual(roundTrip))
            throw new InvalidDataException("Control quartet PNG failed BGRA round trip.");
    }
}
