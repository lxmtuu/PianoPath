namespace PianoPath;

/// <summary>Isolated, fixed-seed fixtures for the five burst/held families plus both Zone routes.
/// Existing family recipes, not a new art direction or live MIDI/CC64 import test.</summary>
internal static class KeyParticleFixtures
{
    internal const int Seed = 20261014;
    internal sealed record Case(string Id, string Style, int Pitch, bool Zone = false);
    internal static IReadOnlyList<Case> All { get; } = Array.AsReadOnly(new[]
    {
        new Case("embers", "Embers", 60), new Case("splash", "Splash", 61),
        new Case("fireworks", "Fireworks", 60), new Case("confetti", "Confetti", 61),
        new Case("dust", "Dust", 60), new Case("zone-low", "Embers", 55, true),
        new Case("zone-high", "Splash", 67, true)
    });
    internal static IReadOnlyList<string> Phases { get; } = Array.AsReadOnly(new[] { "held-off", "burst", "held", "released", "tail" });
    internal static double SampleTime(string phase) => phase switch
    {
        "burst" => .05, "held" or "held-off" or "released" => .6, "tail" => 2.5,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    internal static bool PressedAt(string phase, double time) => phase is "held" or "held-off"
        || (phase is "released" or "tail" && time <= .35);

    internal static PianoVisualSettings Settings(Case fixture, bool enabled = true)
    {
        var s = CommunityPresets.Find("Prism Flow Preview")!.Settings.Clone();
        s.ShowNotes = false; s.ShowHalo = false; s.HorizonGlow = 0;
        s.ShowEmbers = enabled; s.ShowWisps = s.ShowFlame = s.ShowLightBeams = false;
        s.ImpactBurst = fixture.Style; s.ZoneSplit = fixture.Zone; s.ZoneSplitPitch = 60; s.ZoneSplitAmount = 0;
        s.ParticleAmount = 18; s.ParticleResponse = 55; s.ParticleVelocity = 170;
        s.ParticleSpeed = 100; s.ParticleSpread = 38; s.ParticleLife = .7; s.ParticleSize = 1.8;
        s.ParticleLifeRandomness = s.ParticleSizeRandomness = s.ParticleRandomness = 0;
        s.ParticleGlow = 65; s.Gravity = 180; s.Drag = 32; s.Spiral = 0; s.EmitterSize = 8;
        s.ShowImpactFlash = s.ShowImpactRings = false; s.ImpactWave = s.ImpactMorph = "None";
        s.ReleaseEffect = "Fade"; s.ReleaseIntensity = 0; s.HoldBar = s.HoldElectricArc = false;
        s.PhysicsTimeFactor = 100; s.ArDamperStrip = false;
        s.Clamp();
        return s;
    }
}
