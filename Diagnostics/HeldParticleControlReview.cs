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
        int StageKeys, int StageSprites, int ArKeys, int ArLights, int ArParticles);
    private sealed record Delta(int ChangedPixels, int OverFourPixels, int MaxChannelDelta, double MeanAbsoluteRgbDelta);

    internal static int Render(string output)
    {
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
            foreach (var scene in HeldParticleControlPlan.Scenes)
            {
                var route = AuxParticleTimeline.Routes.Single(r => r.Id == scene.Family);
                var overCamera = scene.View.OverCamera;
                var pixels = new List<byte[]>(); var captures = new List<Capture>();
                Crop? crop = null;
                foreach (var variant in HeldParticleControlPlan.Variants)
                {
                    var s = AuxParticleFixtures.Settings(route, variant.Particles);
                    s.HeldParticleArt = true; s.KeyboardSize = "88";
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
                    feed.SetLook(look);
                    // KeyLighting=0 only mutes spill; ShaderKeyLight changes static ivory illumination.
                    // Neither is an honest Stage "key light off" control. Remove packed tint/emission/spill
                    // at the render seam instead, leaving the simulation, colours and press geometry alone.
                    renderer.SuppressStageKeyLightingForReview = !overCamera && !variant.KeyLight;
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
                        feed.Ar = new GpuArInput(calibration,ArNoteStyle.Default,1,KeyLight:variant.KeyLight ? .6f : 0,
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
                        throw new InvalidDataException($"Unexpected particle packing: {scene.Id}/{variant.Id}={particles}");
                    if (overCamera && (renderer.StageKeyCount != 0 || renderer.StageSpriteCount != 0 || renderer.StageNoteCount != 0))
                        throw new InvalidDataException($"Stage geometry leaked into AR: {scene.Id}/{variant.Id}");
                    if (overCamera && (!variant.KeyLight ? renderer.ArKeyQuadCount != 0 || renderer.ArNoteLightCount != 0 : renderer.ArKeyQuadCount <= 0))
                        throw new InvalidDataException($"AR key-light gate failed: {scene.Id}/{variant.Id}");
                    var file = $"frames/{scene.Id}/{variant.Id}.png";
                    PrismFlowVisualReview.WriteAndVerifyPng(Path.Combine(output,file),image);
                    var hash = Hash(image);
                    if (!variant.Particles)
                    {
                        var key = $"{scene.Palette.Id}/{scene.Pattern.Id}/{scene.View.Id}/{variant.Id}";
                        if (effectOffHashes.TryGetValue(key,out var before) && before != hash)
                            throw new InvalidDataException($"Family changed its particle-off control: {key}");
                        effectOffHashes[key] = hash;
                    }
                    // GpuArLayer counters describe its last Build and remain cached while Stage is active.
                    // Record the active output only, not the previous AR scene's stale diagnostics.
                    captures.Add(new Capture(variant.Id,variant.Particles,variant.KeyLight,file,hash,renderer.StageKeyCount,
                        renderer.StageSpriteCount,overCamera ? renderer.ArKeyQuadCount : 0,
                        overCamera ? renderer.ArNoteLightCount : 0,overCamera ? renderer.ArParticleFxCount : 0));
                    pixels.Add(image); count++;
                    crop ??= CropFor(scene,look.KeyboardFraction);
                }
                // Light must not create/kill particle instances, and particles must not change AR key patches.
                if (captures[2].StageSprites != captures[3].StageSprites || captures[2].ArParticles != captures[3].ArParticles
                    || captures[0].ArKeys != captures[2].ArKeys || captures[1].ArKeys != captures[3].ArKeys
                    || captures[1].ArLights != captures[3].ArLights)
                    throw new InvalidDataException($"Factor changed an unrelated geometry count: {scene.Id}");
                var quartet = Quartet(pixels,crop!); var sheetFile = $"sheets/{scene.Id}.png";
                WritePng(Path.Combine(output,sheetFile),quartet,crop!.Width*2,crop.Height*2);
                scenes.Add(new
                {
                    scene.Id, scene.Family, palette=scene.Palette.Id, fixedColor=scene.Palette.FixedColor,
                    pattern=scene.Pattern.Id, pitches=scene.Pattern.Pitches, view=scene.View.Id,
                    cameraSha256=overCamera ? cameraHashes[scene.View.Id] : null,
                    crop, sheetFile, sheetPixelSha256=Hash(quartet), captures,
                    comparisons = new[]
                    {
                        Compare("particles-unlit",2,0), Compare("particles-lit",3,1),
                        Compare("light-without-particles",1,0), Compare("light-with-particles",3,2)
                    }
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
                "docs/samples/ar-keyboard-topdown.png"];
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new
            {
                schema=1, sourceRevision=Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "workspace",
                sourceHashMethod="SHA256; UTF-8 text without BOM, CRLF normalized to LF; PNG bytes unchanged",
                sourceHashes=sourceFiles.ToDictionary(p => p,p => Hash(p.EndsWith(".png",StringComparison.Ordinal)
                    ? File.ReadAllBytes(Path.Combine(root,p))
                    : Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root,p)).Replace("\r\n","\n")))),
                adapter=renderer.AdapterName, width=Width,height=Height, seed=KeyParticleFixtures.Seed,
                fps=HeldParticleControlPlan.Fps,frames=HeldParticleControlPlan.Frames,time=.6,velocity=.9,pedal=false,
                heldArt=true, arKeyLightOn=.6, arDelay=0, arQuality="Ultra", mirrored=false, fit="Fit",
                lightControl="Stage: packed key Emit.W/Base.Y=0 (static lighting and depression unchanged); AR: KeyLight=0 (tint + projected spill).",
                cameraMethod="Reference synthetic BGRA; other views nearest-neighbour planar reprojection with calibration-matched corners. Not re-lit 3D or real-camera evidence.",
                variants=HeldParticleControlPlan.Variants,
                views=HeldParticleControlPlan.Views.Select(v => new {v.Id, corners=v.Corners?.Select(c => new {c.X,c.Y})}),
                sheetOrder="top-left neither; top-right light-only; bottom-left particles-only; bottom-right combined; native pixels, no resizing",
                metric="8-bit display RGB differences only; >4 footprint is not brightness, quality or an additive decomposition.",
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
