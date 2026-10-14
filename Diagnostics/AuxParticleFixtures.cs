namespace PianoPath;

internal static class AuxParticleFixtures
{
    internal static PianoVisualSettings Settings(AuxParticleTimeline.Route route, bool enabled = true)
    {
        var s = KeyParticleFixtures.Settings(KeyParticleFixtures.All[0], false);
        s.ParticleAmount = 0;
        s.ShowWisps = enabled && route.Effect == "Wisps";
        s.ShowFlame = enabled && route.Effect == "Flames";
        s.WispAmount = 32; s.WispHeight = 35; s.WispSpeed = 100;
        s.FlameIntensity = 70; s.FlameHeight = 45; s.FlameNoteColor = true;
        s.ImpactMorph = enabled && route.Source == AuxParticleTimeline.Channel.Impact ? route.Effect : "None";
        s.ImpactMorphIntensity = 70;
        s.ReleaseEffect = enabled && route.Source == AuxParticleTimeline.Channel.Release ? route.Effect : "Fade";
        s.ReleaseIntensity = enabled ? 70 : 0;
        s.Clamp();
        return s;
    }

    internal static void Publish(GpuStageFeed feed, AuxParticleTimeline.Route route, string phase, int frame, int pitch)
    {
        var tick = AuxParticleTimeline.At(route, phase, frame);
        feed.SetDeterministicState([], (frame + 1) / (double)AuxParticleTimeline.Fps, false,
            tick.Pressed ? new HashSet<int> { pitch } : new HashSet<int>());
        feed.SetDeterministicBeatPulse(0);
        feed.SetSustainLevel(tick.Pedal, tick.Pedal ? 1 : 0);
        if (tick.Down) feed.LiveNote(pitch, true, .9);
        if (tick.Up) feed.LiveNote(pitch, false, .9);
        if (tick.Impact) feed.Impact(pitch, .9);
    }
}
