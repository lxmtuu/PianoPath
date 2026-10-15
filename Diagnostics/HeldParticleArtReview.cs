using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace PianoPath;

internal static class HeldParticleArtReview
{
    internal static void Render(string output, string classicOutput)
    {
        if (AuxParticleVisualReview.Render(output, heldArt: true, heldOnly: true) != 32)
            throw new InvalidDataException("Held art review must have 32 captures.");
        using var classic = JsonDocument.Parse(File.ReadAllText(Path.Combine(classicOutput,"manifest.json")));
        using var art = JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"manifest.json")));
        var baseline = classic.RootElement.GetProperty("captures").EnumerateArray().ToArray();
        foreach (var shot in art.RootElement.GetProperty("captures").EnumerateArray())
        {
            var key = shot.GetProperty("File").GetString();
            var before = baseline.Single(b => b.GetProperty("File").GetString() == key);
            var phase = shot.GetProperty("Phase").GetString();
            var same = shot.GetProperty("PixelSha256").GetString() == before.GetProperty("PixelSha256").GetString();
            if ((phase is "off" or "expired") != same)
            {
                // Only active must differ. A pedal-tail may have expired already at this setting.
                if (phase != "pedal-tail") throw new InvalidDataException($"Unexpected art/control difference: {key}");
            }
        }
        RenderMotion(Path.Combine(output,"motion"));
    }

    internal static void RenderWispV3Motion(string output) => RenderMotion(output,wispV3:true);

    private static void RenderMotion(string output, bool wispV3 = false)
    {
        const int width=640,height=360,fps=30,frames=60;
        Directory.CreateDirectory(output);
        using var renderer = GpuStageRenderer.Create(forceWarp:true);
        var captures = new List<object>();
        foreach (var route in AuxParticleTimeline.Routes.Where(r => r.Source == AuxParticleTimeline.Channel.Held && (!wispV3 || r.Wisp)))
        foreach (var ar in new[] { false,true })
        foreach (var refined in new[] { false,true })
        {
            var settings = AuxParticleFixtures.Settings(route); settings.HeldParticleArt=wispV3 || refined;
            if(wispV3) settings.WispAmount=8;
            var stage = new PianoStage { Width=1280,Height=720 }; stage.SetVisualSettings(settings);
            stage.Measure(new Size(1280,720)); stage.Arrange(new Rect(0,0,1280,720)); stage.UpdateLayout();
            var feed = new GpuStageFeed(); feed.SetStageHeight(720);
            feed.SetLook(GpuLook.From(settings,(pitch,track)=>stage.AdjustColor(stage.NoteColor(pitch,track)),
                stage.KeyboardHeight/stage.ActualHeight,stage.AdjustColor,ar) with { WispArtV3ForReview=wispV3 && refined });
            if (ar)
            {
                var calibration=ArCalibration.Create(KeyboardLayout.Standard88,
                    [(70,292),(1214,268),(1236,421),(52,449)],1280,720,out _,70)!;
                var camera=PrismFlowVisualReview.ReadSyntheticCamera(Path.Combine(PrismFlowVisualReview.FindSamples(),"ar-keyboard-topdown.png"));
                var ring=new CameraFrameRing(); camera.CopyTo(ring.BeginWrite(camera.Length,out var slot),0);
                ring.Publish(slot,1280,720,Stopwatch.GetTimestamp());
                feed.Camera=new GpuCameraInput(ring,PictureFit.Fit,false,1);
                feed.Ar=new GpuArInput(calibration,ArNoteStyle.Default,1,Quality:ArQualityTier.Ultra,ProjectedNotes:true,ShowDamperStrip:false);
            }
            var name=$"{route.Id}-{(ar ? "ar" : "live-stage")}-{(wispV3 ? (refined ? "v3" : "v2") : (refined ? "art" : "classic"))}";
            var folder=Path.Combine(output,name); Directory.CreateDirectory(folder);
            var hashes=new List<string>();
            var cropX=ar ? 235 : 240; var cropY=ar ? 90 : 180; const int cropWidth=140,cropHeight=160;
            for (var frame=0;frame<frames;frame++)
            {
                // Replay the exact seeded prefix; one final draw, no async recorder or dropped frames.
                var pixels=GpuRenderLoop.RenderFrames(renderer,feed,width,height,frame,1.0/fps,false,out _,
                    BeforeFrame,KeyParticleFixtures.Seed);
                void BeforeFrame(int f)
                {
                    if(!wispV3) { AuxParticleFixtures.Publish(feed,route,"expired",f*2,60);return; }
                    var held=f<12;
                    int[] pitches=[60,61,64,67];
                    feed.SetDeterministicState([], (f+1)/(double)fps, false,held?pitches.ToHashSet():new HashSet<int>());
                    feed.SetDeterministicBeatPulse(0);feed.SetSustainLevel(!held,held?0:1);
                    foreach(var pitch in pitches)
                    {
                        if(f==0)feed.LiveNote(pitch,true,.9);
                        if(f==12)feed.LiveNote(pitch,false,.9);
                    }
                }
                var crop=new byte[cropWidth*cropHeight*4];
                for (var y=0;y<cropHeight;y++)
                    Buffer.BlockCopy(pixels,((y+cropY)*width+cropX)*4,crop,y*cropWidth*4,cropWidth*4);
                WritePng(Path.Combine(folder,$"{frame:D3}.png"),crop,cropWidth,cropHeight);
                hashes.Add(Convert.ToHexString(SHA256.HashData(crop)));
            }
            captures.Add(new {name,fps,frames,width,height,cropX,cropY,cropWidth,cropHeight,
                heldFrames=11,releaseFrame=wispV3?12:(int?)null,firstReleasedSampleSeconds=wispV3?13.0/30:(double?)null,
                releaseFirstStepSeconds=12.0/30,pedal=true,hashes});
        }
        File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new
        {seed=KeyParticleFixtures.Seed,wispV3ForReview=wispV3,pitches=wispV3?new[]{60,61,64,67}:new[]{60},wispAmount=wispV3?8:32,format="lossless cropped PNG frames; APNG assembled in CI",captures},new JsonSerializerOptions {WriteIndented=true}));
    }

    private static void WritePng(string path,byte[] pixels,int width,int height)
    {
        var source=System.Windows.Media.Imaging.BitmapSource.Create(width,height,96,96,
            System.Windows.Media.PixelFormats.Bgra32,null,pixels,width*4);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream=File.Create(path); png.Save(stream);
    }
}
