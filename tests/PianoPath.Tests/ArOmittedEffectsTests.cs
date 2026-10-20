using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The AR overlay draws fewer things than the stage, and the AR card has to say which of the look's
/// switched-on effects it will leave out. These facts pin that list on the pure half of the helper,
/// which is the half this project links; the look's own switches are checked by <c>--verify</c>, where
/// the WPF settings type exists.
/// </summary>
public class ArOmittedEffectsTests
{
    [Fact]
    public void A_projected_path_over_a_quiet_look_omits_nothing()
    {
        Assert.Empty(ArOmittedEffects.Omitted(projected: true, trail: false, ghosts: false, landingGlow: false,
            labels: false, stars: false));
    }

    [Fact]
    public void The_flat_path_names_every_note_effect_it_draws_only_when_projected()
    {
        Assert.Equal(new[] { "Falling trail", "Echo ghosts", "Landing glow", "Note names" },
            ArOmittedEffects.Omitted(projected: false, trail: true, ghosts: true, landingGlow: true, labels: true,
                stars: false));
        Assert.Equal(new[] { "Landing glow" },
            ArOmittedEffects.Omitted(projected: false, trail: false, ghosts: false, landingGlow: true, labels: false,
                stars: false));
    }

    [Fact]
    public void The_same_note_effects_are_not_reported_when_projected()
    {
        // Perspective notes build trails, ghosts, landing glow and labels from calibrated patches, so a
        // switch that is on is drawn — reporting it here would teach people to ignore the line.
        Assert.Empty(ArOmittedEffects.Omitted(projected: true, trail: true, ghosts: true, landingGlow: true,
            labels: true, stars: false));
    }

    [Fact]
    public void Backdrop_stars_are_always_named_and_come_last()
    {
        Assert.Equal(new[] { "Backdrop stars" },
            ArOmittedEffects.Omitted(projected: true, trail: false, ghosts: false, landingGlow: false, labels: false,
                stars: true));
        Assert.Equal(new[] { "Landing glow", "Backdrop stars" },
            ArOmittedEffects.Omitted(projected: false, trail: false, ghosts: false, landingGlow: true, labels: false,
                stars: true));
    }

    [Fact]
    public void Shimmer_and_pulse_are_never_reported()
    {
        // Both are applied by AddSongNote on the flat and the projected path alike (GpuArLayer.cs:327/336),
        // so an older contract comment that called all three falling families unsupported in AR was out of
        // date. There is no way to ask this helper for them, and this fact says that is deliberate.
        var names = ArOmittedEffects.Omitted(projected: false, trail: true, ghosts: true, landingGlow: true,
            labels: true, stars: true);
        Assert.DoesNotContain("Note shimmer", names);
        Assert.DoesNotContain("Falling pulse", names);
    }
}
