namespace PianoPath;

internal static partial class VerificationSuite
{
    /// <summary>
    /// N5: the shared flash lifetime and the per-backend ceilings, measured on the shipping simulation.
    /// Sixteen notes at once is the effect audit's chord: it has to stay under both ceilings, so the two
    /// engines draw the same picture there, and the GPU flash has to expire inside the shared 0.38 s while
    /// the 0.55 s wave is still alive. The software stage's ceilings are frame-time budgets, not looks.
    /// </summary>
    private static void VerifyEffectBudget()
    {
        var settings = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0], false);
        settings.ShowImpactRings = true; settings.ImpactWave = "Ring"; settings.ImpactWaveIntensity = 80;
        settings.RingSize = 50;
        settings.ShowImpactFlash = true; settings.ImpactFlashIntensity = 70; settings.ImpactFlashStyle = "Flash";
        settings.ImpactMorph = "None"; settings.ReleaseEffect = "Fade"; settings.ReleaseIntensity = 0;
        settings.ArDamperStrip = false;
        settings.Clamp();
        var look = ParticleLook(settings);
        var feed = new GpuStageFeed();
        feed.SetLook(look); feed.SetStageHeight(720);
        feed.SetDeterministicState([], 0, false, new HashSet<int>());
        feed.SetDeterministicBeatPulse(0);
        var input = new GpuFrameInput(); feed.Capture(input, GpuStageFeed.Now);
        var sim = new GpuStageSimulation(KeyParticleFixtures.Seed);
        for (var pitch = 60; pitch < 76; pitch++) feed.Impact(pitch, .9);
        sim.Step(1.0 / 60, input, feed, 1280);
        Assert(sim.ArRingCount == 16 && sim.ArFlashCount == 16,
            $"N5: sixteen simultaneous impacts must birth sixteen waves and sixteen flashes, not {sim.ArRingCount}/{sim.ArFlashCount}.");
        Assert(sim.ArRingCount < EffectBudget.WpfRingCap && sim.ArFlashCount < EffectBudget.WpfFlashCap,
            "N5: the audit chord must fit under the software stage's ceilings, so both engines draw the same picture.");
        for (var frame = 0; frame < 24; frame++) sim.Step(1.0 / 60, input, feed, 1280);
        Assert(sim.ArFlashCount == 0 && sim.ArRingCount == 16,
            $"N5: all flashes share the 0.38 s life (left {sim.ArFlashCount}) while the 0.55 s waves (left {sim.ArRingCount}) remain.");
        Results.Add($"PASS N5 effect budget: 16 simultaneous impacts birth 16 waves + 16 flashes, below both WPF ceilings; every flash expires by 0.40 s on the shared {EffectBudget.FlareLifeSeconds:0.00} s life while the 0.55 s wave remains. Ceilings stay per backend: particles {EffectBudget.WpfParticleCap}/{EffectBudget.GpuParticleCap}, waves {EffectBudget.WpfRingCap}/{EffectBudget.GpuRingCap}, flashes {EffectBudget.WpfFlashCap}/{EffectBudget.GpuFlashCap}.");
    }
}
