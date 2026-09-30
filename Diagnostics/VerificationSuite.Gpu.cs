using System.Diagnostics;
using System.Windows.Media;

namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// The Direct3D 11 stage: settings plumbing, the UI → render-thread feed, the embedded shaders and a
    /// real frame rendered on WARP (Windows' software rasterizer, present on every CI runner), read back
    /// and checked for the parts of the picture that must be there.
    /// </summary>
    private static void VerifyGpuStage()
    {
        // ---- settings: a machine preference, never carried by a look --------------------------------
        var settings = new PianoVisualSettings { RenderBackend = "Gpu", GpuFrameRate = "240", GpuVSync = false };
        settings.CopyFrom(new PianoVisualSettings());
        Assert(settings.RenderBackend == "Gpu" && settings.GpuFrameRate == "240" && !settings.GpuVSync,
            "Applying a preset must keep the graphics engine, its frame rate and VSync: they belong to the computer.");
        var odd = new PianoVisualSettings { RenderBackend = "Vulkan", GpuFrameRate = "75" }; odd.Clamp();
        Assert(odd.RenderBackend == "Software" && odd.GpuFrameRate == "144", "Unknown engine names and frame rates should fall back to the defaults.");
        Assert(new PianoVisualSettings { GpuFrameRate = "Unlimited" }.GpuTargetFps == 0 && new PianoVisualSettings { GpuFrameRate = "120" }.GpuTargetFps == 120,
            "The frame-rate choice should map to the loop's target (0 = unlimited).");

        // ---- look snapshot ----------------------------------------------------------------------------
        var look = GpuLook.From(new PianoVisualSettings(), (pitch, track) => Color.FromRgb((byte)(pitch * 2), 40, 200), .2);
        Assert(Math.Abs(look.NoteColor(60, 0).X - 120 / 255f) < .01 && look.KeyboardFraction == .2f,
            "The GPU look should carry the stage's own note colours and keyboard proportion.");

        // ---- feed: the render thread extrapolates the song clock between UI updates ---------------------
        var feed = new GpuStageFeed();
        var notes = MainWindow.CreateDemoSong();
        feed.SetState(notes, 1.0, true, new HashSet<int>());
        Thread.Sleep(20);
        feed.SetState(notes, 1.02, true, new HashSet<int> { 60 });
        var input = new GpuFrameInput();
        feed.Capture(input, GpuStageFeed.Now + Stopwatch.Frequency / 100);
        Assert(input.Position > 1.02 && input.Position <= 1.02 + .05 * 4 && input.Pressed[60] && ReferenceEquals(input.Notes, notes),
            "The feed should hand the render thread the song, the held keys and a clock extrapolated a few milliseconds ahead (never more than 50 ms).");
        feed.Impact(60, 1); feed.LiveNote(64, true, .9);
        Assert(feed.TryDequeueHit(out var hit) && hit.Pitch == 60 && feed.TryDequeueLive(out var live) && live.Pitch == 64 && live.Down,
            "Hits and live notes should queue losslessly from the UI thread to the render thread.");
        feed.ClearTransient();
        Assert(feed.TakeClearRequest() && !feed.TakeClearRequest(), "A clear request should be taken exactly once.");

        // ---- shaders ------------------------------------------------------------------------------------
        var source = GpuStageRenderer.ShaderSource();
        foreach (var entry in new[] { "VsFullscreen", "PsBackground", "VsNote", "PsNote", "VsKey", "PsKey", "VsSprite", "PsSprite", "PsBloomPrefilter", "PsBloomDown", "PsBloomUp", "PsComposite" })
            Assert(source.Contains(entry + "(", StringComparison.Ordinal), $"The embedded shader source should define {entry}.");

        // ---- a real frame on WARP -----------------------------------------------------------------------
        const int width = 640, height = 360;
        var frameFeed = new GpuStageFeed();
        frameFeed.SetStageHeight(360);
        frameFeed.SetLook(GpuLook.From(new PianoVisualSettings(), (pitch, track) => Color.FromRgb(255, 80, 220), .205));
        frameFeed.SetState(notes, 2.0, false, new HashSet<int> { 60, 64 });
        frameFeed.Impact(60, 1.1); frameFeed.Impact(64, 1);
        var clock = Stopwatch.StartNew();
        var pixels = GpuRenderLoop.RenderOnce(frameFeed, width, height, 12, 1 / 60.0, out var adapter);
        var elapsed = clock.Elapsed.TotalMilliseconds;
        double Luma(int x, int y) { var i = (y * width + x) * 4; return .0722 * pixels[i] + .7152 * pixels[i + 1] + .2126 * pixels[i + 2]; }
        double Average(int y0, int y1) { var sum = 0.0; var count = 0; for (var y = y0; y < y1; y++) for (var x = 0; x < width; x += 3) { sum += Luma(x, y); count++; } return sum / count; }
        var keyboard = Average((int)(height * .86), height - 4);
        var sky = Average(4, (int)(height * .3));
        var distinct = new HashSet<int>();
        for (var i = 0; i < pixels.Length; i += 4 * 97) distinct.Add(pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16);
        Assert(keyboard > 60, $"The GPU frame should show a lit white-key keyboard along the bottom (average luma {keyboard:0}).");
        Assert(keyboard > sky + 20, $"The keyboard should stand out from the dark stage above it (keys {keyboard:0}, stage {sky:0}).");
        Assert(distinct.Count > 40, $"The GPU frame should be a picture, not a flat fill ({distinct.Count} distinct sampled colours).");
        Results.Add($"PASS gpu stage: settings, feed and {source.Length / 1024} KiB of HLSL check out; a {width}×{height} frame with 13 simulated steps rendered on {adapter} in {elapsed:0} ms (keys {keyboard:0}, stage {sky:0}, {distinct.Count} colours).");

        // ---- round 2: the software stage's effect families on the GPU ------------------------------------
        var fxSettings = new PianoVisualSettings
        {
            AmbientCosmic = "Galaxy", FallingTrail = "Sparkles", KeyLabels = "All", ImpactMorph = "Shatter", ImpactFlashStyle = "Lightning",
            ReleaseEffect = "Echo Rings", HoldElectricArc = true, HoldBar = true, BackgroundGuide = true, ShowPetals = true
        };
        var fxLook = GpuLook.From(fxSettings, (pitch, track) => Color.FromRgb(255, 80, 220), .205);
        Assert(fxLook.AmbientCosmic == "Galaxy" && fxLook.FallingTrail == "Sparkles" && fxLook.KeyLabels == 2 && fxLook.ImpactFlashStyle == 1 && fxLook.HoldElectricArc && fxLook.ShowPetals,
            "The GPU look should carry the ambient, trail, label, flash, arc and petal settings.");
        var ambient = new GpuInstanceList<GpuSpriteInstance>(64);
        new GpuStageSimulation().BuildAmbient(fxLook, new GpuSceneLayout(640, 360, .205f), ambient);
        Assert(ambient.Count > 300, $"Galaxy, guide lanes and petals should put hundreds of shapes behind the notes ({ambient.Count}).");
        var atlas = PianoStage.GpuGlyphAtlas;
        var inked = 0;
        for (var i = 3; i < atlas.Pixels.Length; i += 4) if (atlas.Pixels[i] > 128) inked++;
        Assert(atlas.Width == 1024 && atlas.Height == 768 && inked > 2000, $"The glyph atlas should hold rendered note names ({inked} inked pixels).");
        var fxFeed = new GpuStageFeed { LabelAtlas = atlas };
        fxFeed.SetStageHeight(360);
        fxFeed.SetLook(fxLook);
        fxFeed.SetState(notes, 2.0, false, new HashSet<int> { 60, 64, 67 });
        fxFeed.Impact(60, 1.1); fxFeed.Impact(67, 1);
        var fxPixels = GpuRenderLoop.RenderOnce(fxFeed, width, height, 12, 1 / 60.0, out _);
        double FxAverage(int y0, int y1) { var sum = 0.0; var count = 0; for (var y = y0; y < y1; y++) for (var x = 0; x < width; x += 3) { var i = (y * width + x) * 4; sum += .0722 * fxPixels[i] + .7152 * fxPixels[i + 1] + .2126 * fxPixels[i + 2]; count++; } return sum / count; }
        var fxSky = FxAverage((int)(height * .2), (int)(height * .5));
        var plainSky = Average((int)(height * .2), (int)(height * .5));
        Assert(fxSky > plainSky + 1.5, $"The galaxy should light the sky of the GPU frame (with {fxSky:0.0}, without {plainSky:0.0}).");
        Results.Add($"PASS gpu stage effects: {ambient.Count} ambient shapes, a {atlas.Width}×{atlas.Height} glyph atlas ({inked} inked px), sky {plainSky:0.0} → {fxSky:0.0} with the galaxy on.");
    }
}
