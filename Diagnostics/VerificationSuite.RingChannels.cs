namespace PianoPath;

internal static partial class VerificationSuite
{
    private static void VerifyRingChannelRouting(GpuArLayer layer, GpuArInput ar, ArView view,
        GpuInstanceList<GpuSpriteInstance> sprites, GpuInstanceList<GpuArQuad> quads)
    {
        foreach (var (channel, effect) in new (byte, string)[]
        {
            (0, "Ring"), (0, "Shockwave"), (0, "Ripple"),
            (1, "Absorb"), (1, "Bounce"), (2, "Snap Back"), (2, "Echo Rings")
        })
        foreach (var pitch in new[] { 60, 61 })
        {
            var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0], false);
            settings.ImpactWaveIntensity = channel == 0 ? 80 : 0;
            settings.ShowImpactRings = channel == 0;
            settings.ImpactWave = channel == 0 ? effect : "None";
            settings.ImpactMorph = channel == 1 ? effect : "None";
            settings.ImpactMorphIntensity = channel == 1 ? 70 : 0;
            settings.ReleaseEffect = channel == 2 ? effect : "Fade";
            settings.ReleaseIntensity = channel == 2 ? 70 : 0;
            settings.Clamp();
            var look = ParticleLook(settings); var feed = new GpuStageFeed();
            feed.SetLook(look); feed.SetStageHeight(720);
            feed.SetDeterministicState([], 0, false, new HashSet<int>()); feed.SetDeterministicBeatPulse(0);
            var input = new GpuFrameInput(); feed.Capture(input, GpuStageFeed.Now);
            var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
            if (channel == 2)
            {
                feed.LiveNote(pitch, true, .9); sim.Step(.001, input, feed, 1280);
                feed.SetSustainLevel(true, 1); feed.LiveNote(pitch, false, .9);
                feed.Capture(input, GpuStageFeed.Now);
            }
            else feed.Impact(pitch, .9);
            sim.Step(.05, input, feed, 1280);
            var rings = Enumerable.Range(0, sim.ArRingCount).Select(sim.ArRingAt).ToArray();
            Assert(rings.Length == (effect == "Echo Rings" ? 2 : 1)
                    && rings.All(r => r.Pitch == pitch && r.Channel == channel && r.Origin == (channel == 2 ? 1 : 0)),
                $"{effect}/{pitch}: ring regression must begin with actual shipping births in the selected channel.");
            var layout = new GpuSceneLayout(1280, 720, look.KeyboardFraction);
            GpuSpriteInstance[] StageRings(GpuLook selected)
            {
                sim.BuildSprites(selected, layout, sprites);
                return sprites.Span.ToArray().Where(s => s.PosSize.W == 10).ToArray();
            }
            var visible = StageRings(look);
            layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
            var pulses = layer.PulseCount; var time = sim.Time;
            Assert(visible.Length > 0 && pulses > 0,
                $"{effect}/{pitch}: Stage and AR must both build ring bands even when unrelated impact-wave gain is zero.");

            var edited = settings.Clone();
            if (channel == 0) { edited.ImpactMorphIntensity = 0; edited.ReleaseIntensity = 0; }
            else { edited.ImpactWaveIntensity = 150; edited.ShowImpactRings = true; }
            if (channel == 2) edited.ReleaseIntensity = 0; // editing future births must not re-weight an existing release tail
            var changedLook = ParticleLook(edited); input.Look = changedLook;
            var stillVisible = StageRings(changedLook);
            layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
            Assert(stillVisible.SequenceEqual(visible) && layer.PulseCount == pulses,
                $"{effect}/{pitch}: unrelated knobs cannot re-weight a born ring; release strength is captured once.");
            if (channel != 2)
            {
                if (channel == 0) edited.ImpactWaveIntensity = 0;
                else edited.ImpactMorphIntensity = 0;
                var muted = ParticleLook(edited); input.Look = muted;
                Assert(StageRings(muted).Length == 0, $"{effect}/{pitch}: only this channel's live gain may mute Stage rings.");
                layer.Build(input, ar, view, sim.KeyColors, sim, quads, projectedNotes: true);
                Assert(layer.PulseCount == 0, $"{effect}/{pitch}: AR must use the same live gain gate.");
            }
            Assert(sim.Time == time && Enumerable.Range(0, sim.ArRingCount).Select(sim.ArRingAt).SequenceEqual(rings),
                $"{effect}/{pitch}: gain edits/double-output builds cannot mutate or replay ring births.");
            input.Look = look;
            for (var f = 0; f < 60; f++) sim.Step(1.0 / 60, input, feed, 1280);
            Assert(sim.ArRingCount == 0, $"{effect}/{pitch}: channel routing must not prolong ring lifetimes.");
        }
        Results.Add("PASS P3a ring channel routing: Ring/Shockwave/Ripple, Absorb/Bounce, Snap Back/Echo Rings on white/black keys; shared wave/morph gain ownership, release birth-strength carry, independent knobs, read-only Stage/AR builders and expired tails. Not an art or pixel-parity certificate.");
    }
}
