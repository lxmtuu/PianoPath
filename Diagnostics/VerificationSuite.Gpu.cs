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
        Assert(new PianoVisualSettings().RenderBackend == "Gpu", "The GPU stage is the main stage: a fresh install must default to the Direct3D 11 engine.");
        var legacy = new PianoVisualSettings { RenderBackend = "Software" }; legacy.Clamp();
        Assert(legacy.RenderBackend == "Gpu", "A settings file that still names the software renderer must migrate to the GPU engine on load.");
        var settings = new PianoVisualSettings { RenderBackend = "Gpu", GpuFrameRate = "240", GpuVSync = false };
        settings.CopyFrom(new PianoVisualSettings());
        Assert(settings.RenderBackend == "Gpu" && settings.GpuFrameRate == "240" && !settings.GpuVSync,
            "Applying a preset must keep the graphics engine, its frame rate and VSync: they belong to the computer.");
        var odd = new PianoVisualSettings { RenderBackend = "Vulkan", GpuFrameRate = "75" }; odd.Clamp();
        Assert(odd.RenderBackend == "Gpu" && odd.GpuFrameRate == "144", "Unknown engine names and frame rates should fall back to the defaults.");
        Assert(new PianoVisualSettings { GpuFrameRate = "Unlimited" }.GpuTargetFps == 0 && new PianoVisualSettings { GpuFrameRate = "120" }.GpuTargetFps == 120,
            "The frame-rate choice should map to the loop's target (0 = unlimited).");

        // ---- look snapshot ----------------------------------------------------------------------------
        var look = GpuLook.From(new PianoVisualSettings(), (pitch, track) => Color.FromRgb((byte)(pitch * 2), 40, 200), .2);
        Assert(Math.Abs(look.NoteColor(60, 0).X - 120 / 255f) < .01 && look.KeyboardFraction == .2f,
            "The GPU look should carry the stage's own note colours and keyboard proportion.");

        // ---- GPU-exclusive effects: the settings must reach the render look with their slider values ----
        var gpuEffects = new PianoVisualSettings
        {
            NoteShimmer = true, NoteShimmerAmount = 60, HaloPulse = true, HaloPulseIntensity = 70,
            HaloPulseStyle = "Electric Arc", HaloPulseSpeed = 75,
            BackgroundMotion = "Aurora", BackgroundMotionAmount = 68, BackgroundMotionSpeed = 40, BackgroundMotionColor = "#54DFFF",
            ShootingStars = true, ShootingStarsAmount = 30
        };
        var effectLook = GpuLook.From(gpuEffects, (pitch, track) => Color.FromRgb(255, 80, 220), .2);
        Assert(effectLook.NoteShimmer && effectLook.HaloPulse && effectLook.ShootingStars
            && Math.Abs(effectLook.NoteShimmerAmount - .6f) < .01f && Math.Abs(effectLook.HaloPulseIntensity - .7f) < .01f && Math.Abs(effectLook.ShootingStarsAmount - .3f) < .01f
            && effectLook.HaloPulseStyle == "Electric Arc" && Math.Abs(effectLook.HaloPulseSpeed - 1.5625f) < .01f
            && effectLook.BackgroundMotion == "Aurora" && Math.Abs(effectLook.BackgroundMotionAmount - .68f) < .01f
            && Math.Abs(effectLook.BackgroundMotionSpeed - .95f) < .01f && Math.Abs(effectLook.BackgroundMotionColor.X - 84 / 255f) < .01f,
            "Note shimmer, halo style/speed, procedural backdrop settings and shooting stars must flow into the GPU look.");
        var invalidMotion = new PianoVisualSettings { BackgroundMotion = "Warp", BackgroundMotionAmount = 140, BackgroundMotionSpeed = -3, HaloPulseStyle = "Flash", HaloPulseSpeed = 180 };
        invalidMotion.Clamp();
        Assert(invalidMotion.BackgroundMotion == "None" && invalidMotion.BackgroundMotionAmount == 100 && invalidMotion.BackgroundMotionSpeed == 0
            && invalidMotion.HaloPulseStyle == "Pulse" && invalidMotion.HaloPulseSpeed == 100,
            "The new procedural motion choices and sliders must clamp and fall back safely on malformed preset data.");
        Assert(Array.IndexOf(PianoVisualSettings.AmbientLights, "Spotlights") >= 0,
            "The Spotlights light layer should be a choice the GPU stage draws.");
        var landing = new PianoVisualSettings();
        Assert(landing.NoteLandingGlow && Math.Abs(landing.NoteLandingGlowAmount - 45) < .01,
            "The landing glow should gather where notes are about to land by default.");
        var landingLook = GpuLook.From(new PianoVisualSettings { NoteLandingGlowAmount = 80 }, (pitch, track) => Color.FromRgb(255, 80, 220), .2);
        Assert(landingLook.NoteLandingGlow && Math.Abs(landingLook.NoteLandingGlowAmount - .8f) < .01f,
            "The landing glow must flow into the GPU look with its slider value.");

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

        // ---- note sparks: the burst family shapes the sustained emitter; physics time does not slow the stage clock ----
        var burstSettings = new PianoVisualSettings
        {
            ShowEmbers = true, ImpactBurst = "Confetti", ParticleAmount = 80, PhysicsTimeFactor = 50,
            ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false,
            ImpactMorph = "None", ShowHalo = false, ShowKeys = false
        };
        var burstLook = GpuLook.From(burstSettings, (pitch, track) => Color.FromRgb(255, 120, 90), .205);
        var burstFeed = new GpuStageFeed(); burstFeed.Impact(60, 1);
        var burstInput = new GpuFrameInput { Look = burstLook, StageHeightDip = 360 };
        var burstSimulation = new GpuStageSimulation();
        burstSimulation.Step(.04, burstInput, burstFeed, 640);
        var burstSprites = new GpuInstanceList<GpuSpriteInstance>(64);
        burstSimulation.BuildSprites(burstLook, new GpuSceneLayout(640, 360, .205f), burstSprites);
        var hasImpactConfetti = false;
        foreach (var sprite in burstSprites.Span) if (sprite.PosSize.W == 3) { hasImpactConfetti = true; break; }
        burstFeed.ClearTransient(); burstInput.Pressed[60] = true;
        burstSimulation.Step(.04, burstInput, burstFeed, 640);
        var sustainedSprites = new GpuInstanceList<GpuSpriteInstance>(64);
        burstSimulation.BuildSprites(burstLook, new GpuSceneLayout(640, 360, .205f), sustainedSprites);
        var hasSustainedConfetti = false;
        foreach (var sprite in sustainedSprites.Span) if (sprite.PosSize.W == 3) { hasSustainedConfetti = true; break; }
        burstInput.Pressed[60] = false;
        var fastSettings = burstSettings.Clone(); fastSettings.PhysicsTimeFactor = 200;
        var fastLook = GpuLook.From(fastSettings, (pitch, track) => Color.FromRgb(255, 120, 90), .205);
        var fastInput = new GpuFrameInput { Look = fastLook, StageHeightDip = 360 }; fastInput.Pressed[60] = true;
        var fastSimulation = new GpuStageSimulation(); var fastFeed = new GpuStageFeed();
        fastSimulation.Step(.04, fastInput, fastFeed, 640); fastInput.Pressed[60] = false;
        fastSimulation.Step(.04, fastInput, fastFeed, 640);
        for (var i = 0; i < 30; i++)
        {
            burstSimulation.Step(.05, burstInput, burstFeed, 640);
            fastSimulation.Step(.05, fastInput, fastFeed, 640);
        }
        Assert(burstSimulation.ParticleCount > fastSimulation.ParticleCount && fastSimulation.ParticleCount == 0 && hasImpactConfetti && hasSustainedConfetti
            && Math.Abs(burstSimulation.Time - 1.58) < .002 && Math.Abs(fastSimulation.Time - 1.58) < .002,
            "Impact and held-key emitters should use their selected shape, particle ageing should honor slow/fast physics, and both stage clocks should remain real-time.");

        // ---- shaders ------------------------------------------------------------------------------------
        var source = GpuStageRenderer.ShaderSource();
        foreach (var entry in new[] { "VsFullscreen", "PsBackground", "VsNote", "PsNote", "VsKey", "PsKey", "VsSprite", "PsSprite", "PsBloomPrefilter", "PsBloomDown", "PsBloomUp", "PsComposite" })
            Assert(source.Contains(entry + "(", StringComparison.Ordinal), $"The embedded shader source should define {entry}.");
        Assert(source.Contains("float3 BackgroundMotion(", StringComparison.Ordinal) && source.Contains("float4 SceneFxColor", StringComparison.Ordinal)
            && source.Contains("HitFx.x < 5.5", StringComparison.Ordinal) && source.Contains("the centre of the keyboard", StringComparison.Ordinal),
            "The embedded HLSL should contain the procedural background modes and animated hit-line families.");

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
        double ImageAverage(byte[] image, int y0, int y1) { var sum = 0.0; var count = 0; for (var y = y0; y < y1; y++) for (var x = 0; x < width; x += 3) { var i = (y * width + x) * 4; sum += .0722 * image[i] + .7152 * image[i + 1] + .2126 * image[i + 2]; count++; } return sum / count; }
        var keyboard = Average((int)(height * .86), height - 4);
        var sky = Average(4, (int)(height * .3));
        var distinct = new HashSet<int>();
        for (var i = 0; i < pixels.Length; i += 4 * 97) distinct.Add(pixels[i] | pixels[i + 1] << 8 | pixels[i + 2] << 16);
        Assert(keyboard > 60, $"The GPU frame should show a lit white-key keyboard along the bottom (average luma {keyboard:0}).");
        Assert(keyboard > sky + 20, $"The keyboard should stand out from the dark stage above it (keys {keyboard:0}, stage {sky:0}).");
        Assert(distinct.Count > 40, $"The GPU frame should be a picture, not a flat fill ({distinct.Count} distinct sampled colours).");
        var motionFeed = new GpuStageFeed(); motionFeed.SetStageHeight(height);
        var motionSettings = new PianoVisualSettings
        {
            BackgroundMotion = "Nebula", BackgroundMotionAmount = 85, BackgroundMotionSpeed = 38, BackgroundMotionColor = "#9B6BFF",
            BackgroundGradient = false, ShowStars = false, ShowHalo = false, ShowKeys = false, ShowNotes = false,
            ShowEmbers = false, ShowFlame = false, ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false,
            ShowLightBeams = false, ShowPetals = false, HorizonGlow = 0, Vignette = 0, BloomIntensity = 0
        };
        motionFeed.SetLook(GpuLook.From(motionSettings, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
        motionFeed.SetState([], 0, false, new HashSet<int>());
        var motionPixels = GpuRenderLoop.RenderOnce(motionFeed, width, height, 4, 1 / 60.0, out _);
        var baselineFeed = new GpuStageFeed(); baselineFeed.SetStageHeight(height);
        var baselineSettings = motionSettings.Clone(); baselineSettings.BackgroundMotion = "None";
        baselineFeed.SetLook(GpuLook.From(baselineSettings, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
        baselineFeed.SetState([], 0, false, new HashSet<int>());
        var baselinePixels = GpuRenderLoop.RenderOnce(baselineFeed, width, height, 4, 1 / 60.0, out _);
        var proceduralBaselineSky = ImageAverage(baselinePixels, 4, (int)(height * .55));
        var proceduralSky = ImageAverage(motionPixels, 4, (int)(height * .55));
        Assert(proceduralSky > proceduralBaselineSky + 1.5, $"A selected procedural nebula should visibly light the GPU background ({proceduralSky:0.0} versus {proceduralBaselineSky:0.0}).");
        var chromaFeed = new GpuStageFeed(); chromaFeed.SetStageHeight(height);
        var chromaSettings = new PianoVisualSettings
        {
            BackgroundMode = "ChromaGreen", BackgroundMotion = "Nebula", BackgroundMotionAmount = 100,
            ShowHalo = false, ShowKeys = false, ShowNotes = false, ShowEmbers = false, ShowFlame = false,
            ShowWisps = false, ShowImpactRings = false, ShowImpactFlash = false, ShowLightBeams = false, ShowPetals = false
        };
        chromaFeed.SetLook(GpuLook.From(chromaSettings, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
        chromaFeed.SetState([], 0, false, new HashSet<int>());
        var chromaPixels = GpuRenderLoop.RenderOnce(chromaFeed, width, height, 2, 1 / 60.0, out _);
        var centerPixel = ((height / 2) * width + width / 2) * 4;
        Assert(chromaPixels[centerPixel + 1] > 240 && chromaPixels[centerPixel] < 10 && chromaPixels[centerPixel + 2] < 10,
            "An enabled procedural motion layer must never contaminate the pure green chroma-key frame.");
        Results.Add($"PASS gpu stage: settings, feed and {source.Length / 1024} KiB of HLSL check out; a {width}×{height} frame with 13 simulated steps rendered on {adapter} in {elapsed:0} ms (keys {keyboard:0}, stage {sky:0}, {distinct.Count} colours); procedural sky {proceduralBaselineSky:0.0} → {proceduralSky:0.0}, chroma stays pure green.");

        // ---- round 2: the software stage's effect families on the GPU ------------------------------------
        var fxSettings = new PianoVisualSettings
        {
            AmbientCosmic = "Galaxy", FallingTrail = "Sparkles", KeyLabels = "All", ImpactMorph = "Shatter", ImpactFlashStyle = "Lightning",
            ReleaseEffect = "Echo Rings", HoldElectricArc = true, HoldBar = true, BackgroundGuide = true, ShowPetals = true,
            BackgroundMotion = "Aurora", BackgroundMotionAmount = 55, BackgroundMotionColor = "#55FFD8",
            HaloPulse = true, HaloPulseStyle = "Electric Arc", HaloPulseIntensity = 65, HaloPulseSpeed = 60
        };
        var fxLook = GpuLook.From(fxSettings, (pitch, track) => Color.FromRgb(255, 80, 220), .205);
        Assert(fxLook.AmbientCosmic == "Galaxy" && fxLook.FallingTrail == "Sparkles" && fxLook.KeyLabels == 2 && fxLook.ImpactFlashStyle == 1 && fxLook.HoldElectricArc && fxLook.ShowPetals
            && fxLook.BackgroundMotion == "Aurora" && fxLook.HaloPulse && fxLook.HaloPulseStyle == "Electric Arc",
            "The GPU look should carry the ambient, trail, label, flash, hit-line motion, procedural sky, arc and petal settings.");
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
        // ---- recording: the render thread alone renders exact-size frames for a take ----------------------
        var recordFeed = new GpuStageFeed { LabelAtlas = atlas };
        recordFeed.SetStageHeight(360);
        recordFeed.SetLook(fxLook);
        recordFeed.SetState(notes, 2.0, false, new HashSet<int> { 60 });
        var tap = new GpuRecordingTap(320, 180, 30);
        long recorded;
        double recordedKeys = 0;
        using (var loop = new GpuRenderLoop(recordFeed, forceWarp: true))
        {
            recordFeed.Recording = tap;
            var wait = Stopwatch.StartNew();
            while (tap.Serial < 3 && loop.Error is null && wait.Elapsed.TotalSeconds < 20) Thread.Sleep(20);
            recorded = tap.Serial;
            Assert(loop.Error is null, $"The GPU render loop should run for a recording ({loop.Error}).");
            tap.TryRead(frame =>
            {
                var sum = 0.0; var count = 0;
                for (var y = 160; y < 176; y++) for (var x = 0; x < 320; x += 2) { var i = (y * 320 + x) * 4; sum += .0722 * frame[i] + .7152 * frame[i + 1] + .2126 * frame[i + 2]; count++; }
                recordedKeys = sum / count;
            });
            recordFeed.Recording = null;
        }
        Assert(recorded >= 3 && recordedKeys > 60, $"A take on the GPU stage should receive exact-size frames with the keyboard in them ({recorded} frames, keys {recordedKeys:0}).");
        Results.Add($"PASS gpu recording: {recorded} frames of 320×180 rendered by the loop with no window and no preview (keys {recordedKeys:0}).");
        Results.Add($"PASS gpu stage effects: {ambient.Count} ambient shapes, a {atlas.Width}×{atlas.Height} glyph atlas ({inked} inked px), sky {plainSky:0.0} → {fxSky:0.0} with the galaxy on.");
    }

    /// <summary>
    /// The README previews can only repeat byte for byte if the GPU frame is a pure function of the feed and
    /// the number of simulation steps. The capture relies on three facts about <see cref="GpuRenderLoop.RenderOnce"/>,
    /// and each is asserted here on WARP, in this process, so a regression shows up in the verification log
    /// instead of as another commit of changed pictures:
    ///
    /// * two renders from identical feeds after the same number of steps are identical, which includes the
    ///   final pass that dithers with the simulation's clock;
    /// * drawing only the last step gives the same picture as drawing every step, which is what lets the
    ///   previews step 480 frames for the price of one draw;
    /// * a different number of steps gives a different picture, or the two assertions above would hold for a
    ///   renderer that ignored time altogether.
    /// </summary>
    private static void VerifyDeterministicGpuFrame()
    {
        const int width = 320, height = 180, frames = 48;
        var settings = new PianoVisualSettings
        {
            AmbientCosmic = "Galaxy", FallingTrail = "Sparkles", ImpactMorph = "Shatter", ImpactFlashStyle = "Lightning",
            ReleaseEffect = "Echo Rings", HoldElectricArc = true, HoldBar = true, ShowPetals = true,
            BackgroundMotion = "Aurora", BackgroundMotionAmount = 55, BackgroundMotionColor = "#55FFD8",
            HaloPulse = true, HaloPulseStyle = "Electric Arc", HaloPulseIntensity = 65, HaloPulseSpeed = 60
        };
        GpuStageFeed Feed()
        {
            var feed = new GpuStageFeed();
            feed.SetStageHeight(height);
            feed.SetLook(GpuLook.From(settings, (pitch, track) => Color.FromRgb(255, 80, 220), .205));
            feed.SetState([], 0, false, new HashSet<int> { 60, 64, 67 });
            foreach (var pitch in new[] { 60, 64, 67 }) feed.Impact(pitch, 1);
            return feed;
        }
        var first = GpuRenderLoop.RenderOnce(Feed(), width, height, frames - 1, 1 / 60.0, out var adapter);
        var second = GpuRenderLoop.RenderOnce(Feed(), width, height, frames - 1, 1 / 60.0, out _);
        var lastOnly = GpuRenderLoop.RenderOnce(Feed(), width, height, frames - 1, 1 / 60.0, out _, renderWarmup: false);
        var later = GpuRenderLoop.RenderOnce(Feed(), width, height, frames + 11, 1 / 60.0, out _, renderWarmup: false);
        Assert(first.AsSpan().SequenceEqual(second),
            "Two GPU frames rendered from identical feeds after the same number of steps must be identical byte for byte: the README previews depend on it.");
        Assert(first.AsSpan().SequenceEqual(lastOnly),
            "Drawing only the last step must give the same picture as drawing every step, or the previews cannot skip the frames in between.");
        Assert(!first.AsSpan().SequenceEqual(later),
            "A GPU frame after more simulation steps must differ from an earlier one, otherwise the determinism checks prove nothing.");
        Results.Add($"PASS gpu determinism: the {width}×{height} frame after {frames} fixed steps repeats byte for byte on {adapter}, drawing only the last step gives the same picture, and {frames + 12} steps give a different one.");
    }

    /// <summary>
    /// The perf gate (<c>--bench</c>, <see cref="FrameBudget"/>): the budgets it judges by, the arithmetic of
    /// the percentiles, the words it prints, the JSON contract CI reads — and one real measurement taken
    /// through the very code path <c>--bench</c> runs, so a gate that stopped measuring cannot stay green.
    /// </summary>
    private static void VerifyFrameBudget()
    {
        // ---- the budget table: what the gate is allowed to judge ---------------------------------------
        Assert(FrameBudget.All.Count == 2 && FrameBudget.All.All(budget => budget.Width == 1920 && budget.Height == 1080),
            "A frame budget is one look at one size: the gate budgets the default look and the busiest built-in look, both at 1080p.");
        Assert(FrameBudget.All.Select(budget => budget.Scene).Distinct().Count() == FrameBudget.All.Count
            && FrameBudget.Find("default") == FrameBudget.DefaultLook && FrameBudget.Find("Heavy") == FrameBudget.HeavyLook && FrameBudget.Find("cinematic") is null,
            "Every budgeted scene needs its own id, found without regard to case, and a name that is not a scene must not resolve to one.");
        Assert(FrameBudget.DefaultLook.P95LimitMs == 8 && string.IsNullOrWhiteSpace(FrameBudget.DefaultLook.Preset),
            "The default scene budgets the look a first run gets (p95 < 8 ms), built from fresh settings rather than from a preset.");
        Assert(FrameBudget.HeavyLook.P95LimitMs == 16 && VisualPresets.FindBuiltIn(FrameBudget.HeavyLook.Preset) is not null,
            "The heavy scene must name a preset the app really ships, or the gate would measure a look nobody can choose.");
        Assert(FrameBudget.All.All(budget => budget.P95LimitMs > 0 && budget.Description.Length > 20),
            "Every budget needs a limit and a sentence saying what the scene draws, so a report can be read without opening the source.");

        // ---- percentiles: nearest rank, and the ordering a reader checks first -------------------------
        var hundred = new List<double>();
        for (var i = 1; i <= 100; i++) hundred.Add(i);
        var stats = FrameStats.From(hundred);
        Assert(stats.Count == 100 && Math.Abs(stats.MeanMs - 50.5) < .0001 && stats.MinMs == 1 && stats.MaxMs == 100,
            "The mean and the extremes of the frame times 1..100 are the arithmetic a report is read by.");
        Assert(stats.MedianMs == 50 && stats.P95Ms == 95 && stats.P99Ms == 99 && FrameStats.Percentile(new double[] { 1, 2, 3, 4 }, .5) == 2,
            "Percentiles use the nearest rank: p95 of 100 frames is the 95th fastest frame, not an interpolation between two of them.");
        Assert(FrameStats.From([7, 1, 4]).P95Ms == 7 && FrameStats.From([7, 1, 4]).MinMs == 1 && hundred[0] == 1 && hundred[99] == 100,
            "The samples arrive in the order the loop drew them, so the distribution sorts its own copy and leaves the caller's list alone.");
        Assert(FrameStats.From([]).Count == 0 && FrameStats.From([double.NaN, -3, double.PositiveInfinity]).Count == 0 && Math.Abs(FrameStats.From([]).P95Ms) < .0001,
            "A frame time that is not a finite, non-negative number is a broken measurement and must not become a fast frame.");
        Assert(FrameStats.From([4]).P95Ms == 4 && FrameStats.From([4]).MedianMs == 4 && FrameStats.From([4]).MeanMs == 4,
            "One sampled frame is its own whole distribution.");
        Assert(stats.Ordered && FrameStats.From([1, 2, 3]).Ordered
            && !new FrameStats { Count = 3, MinMs = 5, MedianMs = 4, P95Ms = 6, P99Ms = 7, MaxMs = 8, MeanMs = 6 }.Ordered,
            "A distribution whose numbers cannot come from one sorted sample list is a defect, and the check has to be able to say so.");

        // ---- which machines a budget applies to --------------------------------------------------------
        Assert(FrameBudget.IsSoftwareAdapter(true, "NVIDIA GeForce RTX 4070"),
            "A loop that says it is WARP is drawing in software whatever the adapter string claims.");
        Assert(FrameBudget.IsSoftwareAdapter(false, "Microsoft Basic Render Driver") && FrameBudget.IsSoftwareAdapter(false, "  "),
            "Windows presents its software adapter as a hardware one on a machine with no card, so the name decides: a CI runner must never be judged against a graphics card's budget.");
        Assert(!FrameBudget.IsSoftwareAdapter(false, "NVIDIA GeForce RTX 4070") && !FrameBudget.IsSoftwareAdapter(false, "AMD Radeon RX 7900 XTX"),
            "A real graphics card is what the budgets were written for, and it has to be recognised as one.");
        var gate = FrameBudget.DefaultLook;
        Assert(gate.Judge(true, 400, "Microsoft Basic Render Driver").Verdict == FrameVerdict.NotApplicable
            && gate.Judge(false, 400, "Microsoft Basic Render Driver").Verdict == FrameVerdict.NotApplicable,
            "A frame drawn by a software rasterizer is measured and compared with the previous run, never judged against the budget.");
        Assert(gate.Judge(false, 7.9, "NVIDIA GeForce RTX 4070").Verdict == FrameVerdict.Pass
            && gate.Judge(false, 8, "NVIDIA GeForce RTX 4070").Verdict == FrameVerdict.Pass
            && gate.Judge(false, 8.1, "NVIDIA GeForce RTX 4070").Verdict == FrameVerdict.Fail,
            "On a graphics card the p95 budget is a hard gate, and its edge belongs to the side that passes.");
        Assert((gate with { P95LimitMs = 0 }).Judge(false, 999, "NVIDIA GeForce RTX 4070").Verdict == FrameVerdict.NotApplicable,
            "A scene without a budget only measures, so this run can measure on any machine without failing it.");

        // ---- the report CI reads back -----------------------------------------------------------------
        var samples = new List<double>();
        for (var i = 0; i < 60; i++) samples.Add(10 + i % 5);
        var (verdict, note) = gate.Judge(true, FrameStats.From(samples).P95Ms, "Microsoft Basic Render Driver");
        var scene = new FrameBenchReport
        {
            Scene = "default", Preset = VisualPresets.DefaultPresetName, Description = gate.Description,
            Width = 1920, Height = 1080, Frames = 60, WarmupFrames = 24, WarmupMs = 1500, Particles = 321,
            BudgetP95LimitMs = gate.P95LimitMs, Verdict = verdict, VerdictNote = note,
            Stats = FrameStats.From(samples), Samples = samples
        };
        var run = new FrameBenchRun
        {
            TakenUtc = "2026-10-01T00:00:00.0000000Z", AppVersion = "0.4.0", Adapter = "Microsoft Basic Render Driver",
            Warp = false, SoftwareAdapter = true, ShaderCompileMs = 1234.5, Scenes = [scene]
        };
        Assert(FrameBenchRun.TryParse(run.ToJson(), out var reread) && reread.Adapter == run.Adapter && reread.SoftwareAdapter && !reread.Warp
            && reread.Scene("default") is { } readScene && readScene.Measured && readScene.Frames == 60 && readScene.Samples.Count == 60
            && Math.Abs(readScene.Stats.P95Ms - scene.Stats.P95Ms) < .001 && readScene.Verdict == verdict && readScene.BudgetP95LimitMs == 8,
            "A report has to survive the trip through JSON: CI's baseline is the previous run's file, and a field that does not round-trip silently becomes a zero.");
        Assert(!FrameBenchRun.TryParse("", out _) && !FrameBenchRun.TryParse("not json at all", out _) && !FrameBenchRun.TryParse("{\"schema\":1}", out _)
            && !FrameBenchRun.TryParse("[1,2,3]", out _) && !FrameBenchRun.TryParse("{\"scenes\":[]}", out _),
            "A baseline that is missing, truncated or somebody else's file leaves the run with nothing to compare against, not with a crash.");

        // ---- the lines the run prints -----------------------------------------------------------------
        var lines = run.LogLines(null);
        Assert(lines.Count == 1 + 3 * run.Scenes.Count
            && lines.All(line => line.StartsWith("NOTE perf: ", StringComparison.Ordinal) || line.StartsWith("PASS perf: ", StringComparison.Ordinal)
                || line.StartsWith("FAIL perf: ", StringComparison.Ordinal) || line.StartsWith("WARN perf: ", StringComparison.Ordinal)),
            "Every line the gate prints carries one of the suite's own prefixes, so CI can publish them as annotations the way it publishes the verification log.");
        Assert(lines.Any(line => line.Contains("p95 14.00 ms", StringComparison.Ordinal)) && lines.All(line => !line.Contains("14,00", StringComparison.Ordinal)),
            "The printed numbers keep a decimal dot in any regional format — a Windows set to Vietnamese once failed a dozen checks over a decimal comma.");
        Assert(lines[2].StartsWith("NOTE perf: the frame was drawn by ", StringComparison.Ordinal),
            "A budget that is not applied has to say so out loud, in the line a reader of the log meets first.");

        // ---- the relative gate: same scene, same size, same kind of adapter ---------------------------
        FrameBenchReport SceneAt(string id, double p95, int width = 1920, int height = 1080)
        {
            var values = new List<double>();
            for (var i = 1; i <= 100; i++) values.Add(p95 * i / 95);
            return new FrameBenchReport { Scene = id, Width = width, Height = height, Frames = 100, Stats = FrameStats.From(values), Samples = values };
        }
        FrameBenchRun RunOf(bool software, FrameBenchReport measured) => new()
        {
            TakenUtc = "2026-10-01T00:00:00.0000000Z", AppVersion = "0.4.0",
            Adapter = software ? "Microsoft Basic Render Driver" : "NVIDIA GeForce RTX 4070",
            Warp = software, SoftwareAdapter = software, Scenes = [measured]
        };
        var baseline = RunOf(true, SceneAt("default", 60));
        Assert(Math.Abs(baseline.Scene("default")!.Stats.P95Ms - 60) < .0001,
            "The comparison is judged by p95, so the fixture has to land exactly on the p95 it was built for.");
        Assert(run.Compare(null, scene).Trend == FrameTrend.Unknown
            && baseline.Compare(RunOf(true, SceneAt("heavy", 10)), baseline.Scene("default")!).Trend == FrameTrend.Unknown
            && baseline.Compare(new FrameBenchRun { SoftwareAdapter = true, Scenes = [] }, baseline.Scene("default")!).Trend == FrameTrend.Unknown,
            "A run with no baseline, a scene the previous run did not measure and a previous run with no scenes at all are all simply not comparable.");
        Assert(baseline.Compare(RunOf(true, SceneAt("default", 60, 3840, 2160)), baseline.Scene("default")!).Trend == FrameTrend.Unknown
            && baseline.Compare(RunOf(false, SceneAt("default", 60)), baseline.Scene("default")!).Trend == FrameTrend.Unknown,
            "Comparing 4K with 1080p, or a graphics card with a software rasterizer, would report the two machines instead of the code between them.");
        var held = RunOf(true, SceneAt("default", 62));
        var quicker = RunOf(true, SceneAt("default", 50));
        Assert(held.Compare(baseline, held.Scene("default")!).Trend == FrameTrend.Steady
            && quicker.Compare(baseline, quicker.Scene("default")!).Trend == FrameTrend.Faster,
            "A run that holds or clearly beats the previous p95 is not a regression, whatever else the runner was doing that day.");
        var slower = RunOf(true, SceneAt("default", 96));
        var regression = slower.Compare(baseline, slower.Scene("default")!);
        Assert(regression.Trend == FrameTrend.Regression && regression.DeltaPercent > 59
            && slower.LogLines(baseline).Any(line => line.StartsWith("WARN perf: ", StringComparison.Ordinal)),
            "Half again as slow as the previous run on the same kind of adapter is the one thing CI can judge absolutely: the code between the two commits did it.");
        Assert(reread.Compare(run, reread.Scene("default")!).Trend == FrameTrend.Steady,
            "A report read back from its own JSON compares steady against the run that wrote it, which is what CI does every time a baseline is fresh.");

        // ---- one real measurement, through the code path --bench takes --------------------------------
        var measured = FrameBenchmark.Measure(FrameBudget.DefaultLook with { Scene = "verify", Width = 320, Height = 180, P95LimitMs = 0 }, 24);
        if (measured is null)
        {
            Results.Add("SKIP perf gate measurement: this machine has no Direct3D device, so the render loop could not be sampled; the budgets, the percentiles and the report contract were checked without it.");
            return;
        }
        Assert(measured.Frames == measured.Samples.Count && measured.Frames > 0 && measured.Frames <= 24 && measured.Samples.All(ms => ms > 0),
            "A sampled run has to hand back one positive length per frame it recorded, and never more frames than it was asked for.");
        Assert(measured.Stats.Ordered && measured.Stats.Count == measured.Frames && measured.Width == 320 && measured.Height == 180 && measured.Verdict == FrameVerdict.NotApplicable,
            "The measured distribution has to cover exactly the frames recorded, at the size asked for, and a scene with no budget has to stay unjudged.");
        Assert(measured.WarmupMs > 0 && measured.Preset == VisualPresets.DefaultPresetName && measured.Stats.FpsAtP95 > 0,
            "The warm-up really ran (the first frames are where the shaders, the atlas and the texture ring are built) and the scene drew the default look.");
        Assert(FrameBenchRun.TryParse(new FrameBenchRun { TakenUtc = "2026-10-01T00:00:00.0000000Z", AppVersion = "0.4.0", Adapter = "verify", SoftwareAdapter = true, Scenes = [measured] }.ToJson(), out var measuredBack)
            && measuredBack.Scene("verify") is { } measuredScene && measuredScene.Samples.Count == 24,
            "A report built from a real measurement has to survive the round trip through JSON with every frame time still in it.");
        Results.Add($"PASS perf gate: 2 budgets at 1080p (p95 < {FrameBudget.Ms(FrameBudget.DefaultLook.P95LimitMs)} ms for the default look, < {FrameBudget.Ms(FrameBudget.HeavyLook.P95LimitMs)} ms for {FrameBudget.HeavyLook.Preset}), " +
            $"nearest-rank percentiles, the software-adapter rule, the JSON round trip and the relative gate check out, and {measured.Frames} frames of {measured.Width}×{measured.Height} sampled on the loop itself " +
            $"came back at mean {FrameBudget.Ms(measured.Stats.MeanMs)} ms, p95 {FrameBudget.Ms(measured.Stats.P95Ms)} ms, max {FrameBudget.Ms(measured.Stats.MaxMs)} ms.");
    }
}
