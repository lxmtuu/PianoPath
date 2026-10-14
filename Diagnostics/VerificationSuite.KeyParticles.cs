using System.Numerics;
using System.Windows.Media;

namespace PianoPath;

internal static partial class VerificationSuite
{
    private static GpuLook ParticleLook(PianoVisualSettings settings) => GpuLook.From(settings, (_, _) => Color.FromRgb(40, 220, 190), .205);
    private static GpuArParticle[] ParticleSnapshot(GpuStageSimulation sim) => Enumerable.Range(0, sim.ArParticleCount).Select(sim.ArParticleAt).ToArray();

    /// <summary>Actual shipping simulation, not a reimplementation of its formulas in the test.</summary>
    private static void VerifyKeyParticleLifecycle()
    {
        var previousLayout = KeyboardLayout.Active;
        KeyboardLayout.Use(KeyboardLayout.Standard88);
        try
        {
            var calibration = ArCalibration.Create(KeyboardLayout.Standard88,
                [(70, 292), (1214, 268), (1236, 421), (52, 449)], 1280, 720, out var problem, 70)
                ?? throw new InvalidOperationException($"Lifecycle calibration: {problem}");
            var ar = new GpuArInput(calibration, ArNoteStyle.Default, 1, Quality: ArQualityTier.Ultra, ProjectedNotes: true, ShowDamperStrip: false);
            var layer = new GpuArLayer();
            var quads = new GpuInstanceList<GpuArQuad>(128);
            var sprites = new GpuInstanceList<GpuSpriteInstance>(128);
            var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
            foreach (var fixture in KeyParticleFixtures.All)
            {
                foreach (var pitch in fixture.Zone ? new[] { fixture.Pitch } : new[] { 60, 61 })
                {
                    var settings = KeyParticleFixtures.Settings(fixture);
                    var look = ParticleLook(settings);
                    var input = new GpuFrameInput { Look = look, StageHeightDip = 720 };
                    var feed = new GpuStageFeed();
                    var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
                    feed.Impact(pitch, .9); sim.Step(.001, input, feed, 1280);
                    var burst = ParticleSnapshot(sim);
                    Assert(burst.Length > 0 && burst.All(p => p.Pitch == pitch && p.Origin == 0 && !p.Wisp),
                        $"{fixture.Id}/{pitch}: one impact must create pitch-tagged burst particles, not a held emitter.");
                    var expectedShape = fixture.Style switch { "Splash" => 5, "Confetti" => 2, "Dust" => 3, _ => -1 };
                    Assert(burst.All(p => expectedShape < 0 ? p.Shape is 0 or 1 : p.Shape == expectedShape),
                        $"{fixture.Id}/{pitch}: retain the family silhouette (droplet/confetti/dust or ember/streak).");
                    sim.Clear(); input.Pressed[pitch] = true;
                    for (var frame = 0; frame < 36; frame++) sim.Step(1.0 / 60, input, feed, 1280);
                    Assert(sim.ParticleCount > 0 && sim.EmitterParticlesBorn == sim.ParticlesBorn && sim.ArImpacts.Count == 0,
                        $"{fixture.Id}/{pitch}: held state emits its family without synthesising another Note On burst.");
                    var snapshot = ParticleSnapshot(sim); var born = sim.ParticlesBorn; var time = sim.Time;
                    var layout = new GpuSceneLayout(1280, 720, look.KeyboardFraction);
                    sim.BuildSprites(look, layout, sprites);
                    layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
                    layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
                    sim.BuildSprites(look, layout, sprites);
                    Assert(layer.ParticleFxCount > 0 && sprites.Count > 0 && ParticleSnapshot(sim).SequenceEqual(snapshot)
                            && sim.Time == time && sim.ParticlesBorn == born,
                        $"{fixture.Id}/{pitch}: building Stage and AR twice must not age particles or spawn a second emitter.");
                    input.Pressed[pitch] = false; input.Sustain = true; input.SustainLevel = 1;
                    sim.Step(.01, input, feed, 1280);
                    Assert(sim.ParticleCount > 0 && sim.EmitterParticlesBorn == born && sim.EmissionCredits(pitch).Sparks == 0,
                        $"{fixture.Id}/{pitch}: physical release stops new births immediately; pedal does not impersonate a held key, and old particles keep their tail.");
                    for (var frame = 0; frame < 150; frame++) sim.Step(1.0 / 60, input, feed, 1280);
                    Assert(sim.ParticleCount == 0 && sim.EmitterParticlesBorn == born,
                        $"{fixture.Id}/{pitch}: all existing particles must expire even with the pedal still down.");
                }
                VerifyParticleCapRecovery(fixture);
                VerifyParticleDrawIndependence(fixture, layer, ar, view, sprites, quads);
            }
            VerifyRingChannelRouting(layer, ar, view, sprites, quads);
            VerifyAuxParticleLifecycle(layer, ar, view, sprites, quads);
            VerifyEmitterClockAndClear();
            VerifyTransientFrameEpoch();
            Results.Add("PASS P3a lifecycle: five burst/held families, both Zone routes, white/black keys, physical-release vs pedal, real 24000-particle cap recovery, soft clear and read-only Stage/AR builds. Not full MIDI routing, nonzero delay, motion parity or a family art upgrade.");
        }
        finally { KeyboardLayout.Use(previousLayout); }
    }

    private static void VerifyParticleCapRecovery(KeyParticleFixtures.Case fixture)
    {
        var settings = KeyParticleFixtures.Settings(fixture);
        settings.ParticleAmount = 120; settings.ParticleResponse = 100; settings.ParticleLife = .05;
        var look = ParticleLook(settings);
        var input = new GpuFrameInput { Look = look, StageHeightDip = 720 };
        var feed = new GpuStageFeed(); var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
        for (var i = 0; i < 80; i++) feed.Impact(fixture.Pitch, 1);
        sim.Step(.001, input, feed, 1280);
        Assert(sim.ParticleCount == GpuStageSimulation.MaxParticles, $"{fixture.Id}: cap test must actually fill the production pool.");
        input.Pressed[fixture.Pitch] = true;
        var frames = 0;
        while (sim.ParticleCount == GpuStageSimulation.MaxParticles && frames++ < 40)
        {
            sim.Step(.01, input, feed, 1280);
            Assert(sim.EmissionCredits(fixture.Pitch).Sparks is >= 0 and < 1,
                $"{fixture.Id}: a full pool must not accumulate integer emission debt.");
        }
        Assert(sim.ParticleCount == 0 && frames > 1, $"{fixture.Id}: the capped birth cohort must really age out before testing recovery.");
        sim.Step(.01, input, feed, 1280);
        Assert(sim.ParticleCount is >= 1 and <= 2,
            $"{fixture.Id}: recovering capacity must emit only the current 10 ms schedule, not a catch-up spray ({sim.ParticleCount}).");
    }

    private static void VerifyParticleDrawIndependence(KeyParticleFixtures.Case fixture, GpuArLayer layer, GpuArInput ar,
        ArView view, GpuInstanceList<GpuSpriteInstance> sprites, GpuInstanceList<GpuArQuad> quads)
    {
        GpuArParticle[] Replay(bool draw)
        {
            var look = ParticleLook(KeyParticleFixtures.Settings(fixture));
            var input = new GpuFrameInput { Look = look, StageHeightDip = 720 }; input.Pressed[fixture.Pitch] = true;
            var feed = new GpuStageFeed(); var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
            for (var frame = 0; frame < 30; frame++)
            {
                if (frame is 0 or 29) feed.Impact(fixture.Pitch, .9);
                sim.Step(1.0 / 60, input, feed, 1280);
                if (!draw) continue;
                sim.BuildSprites(look, new GpuSceneLayout(1280, 720, look.KeyboardFraction), sprites);
                layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
            }
            return ParticleSnapshot(sim);
        }
        Assert(Replay(true).SequenceEqual(Replay(false)),
            $"{fixture.Id}: extra output builds must not consume session RNG or change the next burst/held snapshot.");
    }

    private static void VerifyEmitterClockAndClear()
    {
        foreach (var factor in new[] { 10d, 100d, 300d })
        {
            var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0]); settings.PhysicsTimeFactor = factor;
            var input = new GpuFrameInput { Look = ParticleLook(settings), StageHeightDip = 720 }; input.Pressed[60] = true;
            var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var feed = new GpuStageFeed();
            for (var frame = 0; frame < 60; frame++) sim.Step(1.0 / 60, input, feed, 1280);
            Assert(sim.EmitterParticlesBorn is >= 23 and <= 24 && Math.Abs(sim.Time - 1) < .00001,
                "Physics speed scales age/motion, not emitter birth rate or musical elapsed time.");
        }
        foreach (var family in new[] { "sparks", "wisps", "flames" })
        {
            var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0]);
            settings.ShowEmbers = family == "sparks"; settings.ShowWisps = family == "wisps"; settings.ShowFlame = family == "flames";
            settings.WispAmount = 24; settings.FlameIntensity = 70; settings.NoteShimmer = settings.NoteLandingGlow = true;
            var input = new GpuFrameInput { Look = ParticleLook(settings), StageHeightDip = 720 }; input.Pressed[60] = true;
            var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var feed = new GpuStageFeed();
            for (var frame = 0; frame < 60; frame++) sim.Step(1.0 / 60, input, feed, 1280);
            Assert(sim.ParticleCount > 0, $"{family}: clear test must start with an active production emitter.");
            input.Pressed[60] = false;
            var beforeRelease = sim.EmitterParticlesBorn;
            sim.Step(.05, input, feed, 1280);
            Assert(family == "flames" ? sim.EmitterParticlesBorn > beforeRelease : sim.EmitterParticlesBorn == beforeRelease,
                $"{family}: flames intentionally emit while heat cools; sparks/wisps stop on key-active release.");
            input.Pressed[60] = true;
            var beforeOff = sim.ParticlesBorn;
            var originalLook = input.Look;
            var off = settings.Clone(); off.ShowEmbers = off.ShowWisps = off.ShowFlame = false;
            input.Look = ParticleLook(off);
            sim.Step(0, input, feed, 1280);
            Assert(sim.ParticlesBorn == beforeOff && sim.EmissionCredits(60) == (0d, 0d, 0d),
                $"{family}: turning off clears credit even at dt=0, but cannot create particles.");
            input.Look = originalLook;
            sim.Step(.001, input, feed, 1280);
            Assert(sim.ParticlesBorn == beforeOff, $"{family}: a toggle back on must not reuse old fractional credit.");
            var time = sim.Time;
            sim.Clear();
            Assert(sim.ParticleCount == 0 && sim.LiveTrailCount == 0 && sim.ArRingCount == 0 && sim.ArFlashCount == 0
                    && sim.ArImpacts.Count == 0 && sim.EmissionCredits(60) == (0d, 0d, 0d)
                    && sim.KeyColors.All(c => c == Vector4.Zero) && sim.Activity == 0 && sim.NoteTrailCount == 0
                    && sim.ParticlesBorn == 0 && sim.Time == time,
                $"{family}: soft clear must discard transient credit/lighting without rewinding the session clock.");
            input.Pressed[60] = false; input.Sustain = true; input.SustainLevel = 1;
            sim.Step(.01, input, feed, 1280);
            Assert(sim.ParticleCount == 0, $"{family}: cleared heat/credit must not resurrect particles under pedal.");
        }
        VerifyParticleFrameRates();
        VerifyParticleReleaseCursorClear();
        var stale = new GpuStageFeed(); stale.Impact(60, 1); stale.LiveNote(60, true, 1); stale.ClearTransient();
        stale.Impact(64, 1); stale.LiveNote(64, true, 1);
        var frameInput = new GpuFrameInput { Look = ParticleLook(KeyParticleFixtures.Settings(KeyParticleFixtures.All[0])) };
        stale.SetLook(frameInput.Look); stale.Capture(frameInput, GpuStageFeed.Now);
        var fresh = new GpuStageSimulation(KeyParticleFixtures.Seed); fresh.Step(.01, frameInput, stale, 1280);
        Assert(fresh.ArImpacts.Count == 1 && fresh.ArImpacts[0].Pitch == 64 && fresh.LivePresses.Count == 1 && fresh.LivePresses[0].Pitch == 64,
            "A consumed soft-clear request must drop old queued hits/presses but retain events posted afterward.");
        var finiteTime = fresh.Time;
        fresh.Step(double.NaN, frameInput, stale, 1280); fresh.Step(double.PositiveInfinity, frameInput, stale, 1280);
        Assert(fresh.Time == finiteTime && ParticleSnapshot(fresh).All(p => float.IsFinite(p.Age) && float.IsFinite(p.X)),
            "Non-finite dt must not poison simulation time or particles.");
        var stage = new PianoStage(); var wispBudget = (double[])Field(stage, "_wispBudget");
        wispBudget[60] = .9; stage.ClearTransient();
        Assert(wispBudget.All(c => c == 0), "The WPF fallback must also discard old wisp credit on soft clear.");
    }

    private static void VerifyParticleFrameRates()
    {
        foreach (var family in new[] { "sparks", "wisps", "flames" })
        {
            var totals = new List<long>();
            foreach (var fps in new[] { 30, 60, 120, 144 })
            {
                var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0]);
                settings.ShowEmbers = family == "sparks"; settings.ShowWisps = family == "wisps"; settings.ShowFlame = family == "flames";
                settings.WispAmount = 24; settings.FlameIntensity = 70;
                var input = new GpuFrameInput { Look = ParticleLook(settings), StageHeightDip = 720 }; input.Pressed[60] = true;
                var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var feed = new GpuStageFeed();
                for (var frame = 0; frame < fps * 4; frame++) sim.Step(1.0 / fps, input, feed, 1280);
                totals.Add(sim.EmitterParticlesBorn);
                Assert(Math.Abs(sim.Time - 4) < .00001, $"{family}/{fps}: real elapsed clock must not drift with FPS.");
            }
            Assert(totals.Min() > 0 && totals.Max() - totals.Min() <= 1,
                $"{family}: four-second birth totals at 30/60/120/144 FPS must stay within one birth ({string.Join(",", totals)}); not identical trajectories.");
        }
    }

    private static void VerifyParticleReleaseCursorClear()
    {
        var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0]);
        settings.ShowNotes = true; settings.FallingTrail = "Glow"; settings.FallingTrailIntensity = 60; settings.ReleaseEffect = "Dissolve"; settings.ReleaseIntensity = 80;
        settings.ShowEmbers = false;
        var notes = new[] { new NoteEvent { Pitch = 60, Start = 0, Duration = .1, Velocity = 100, Track = 0 }, new NoteEvent { Pitch = 64, Start = .2, Duration = .4, Velocity = 100, Track = 0 } };
        var input = new GpuFrameInput { Look = ParticleLook(settings), Notes = notes, MaxNoteDuration = .4, Playing = true, StageHeightDip = 720 };
        var sim = new GpuStageSimulation(KeyParticleFixtures.Seed); var feed = new GpuStageFeed();
        sim.Step(.01, input, feed, 1280);
        var instances = new GpuInstanceList<GpuNoteInstance>(16);
        sim.BuildNotes(input, new GpuSceneLayout(1280, 720, input.Look.KeyboardFraction), instances);
        Assert(sim.NoteTrailCount > 0, "Release/cache clear test must first build actual note-trail render caches.");
        sim.Clear(); input.Position = .15;
        sim.Step(.01, input, feed, 1280);
        Assert(sim.NoteTrailCount == 0 && sim.ParticleCount == 0,
            "Soft clear must drop render caches and rebase release scanning on the next snapshot, not emit a stale end from the old cursor.");
    }
}
