using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// A software-rendered take cannot draw the three GPU-only effects, so the recording line has to name
/// every one the preset switched on. These facts pin that list; the UI wording itself is checked by
/// <c>tools/check_sources.py</c> against both string tables.
/// </summary>
public class GpuOnlyEffectsTests
{
    [Fact]
    public void A_plain_preset_omits_nothing()
    {
        var settings = new PianoVisualSettings { NoteShimmer = false, NoteLandingGlow = false, ShootingStars = false };
        Assert.Empty(GpuOnlyEffects.Omitted(settings));
    }

    [Fact]
    public void Each_gpu_only_effect_is_named_by_itself()
    {
        Assert.Equal(new[] { "Note shimmer" }, GpuOnlyEffects.Omitted(new PianoVisualSettings { NoteShimmer = true, NoteLandingGlow = false, ShootingStars = false }));
        Assert.Equal(new[] { "Landing glow" }, GpuOnlyEffects.Omitted(new PianoVisualSettings { NoteShimmer = false, NoteLandingGlow = true, ShootingStars = false }));
        Assert.Equal(new[] { "Shooting stars" }, GpuOnlyEffects.Omitted(new PianoVisualSettings { NoteShimmer = false, NoteLandingGlow = false, ShootingStars = true }));
    }

    [Fact]
    public void The_list_keeps_the_interface_order()
    {
        var settings = new PianoVisualSettings { ShootingStars = true, NoteLandingGlow = true, NoteShimmer = true };
        Assert.Equal(new[] { "Note shimmer", "Landing glow", "Shooting stars" }, GpuOnlyEffects.Omitted(settings));
    }

    [Fact]
    public void The_defaults_landing_glow_is_reported_because_that_is_what_a_take_would_lose()
    {
        // Landing glow ships on by default; a software take has to say so rather than quietly drop it.
        var settings = new PianoVisualSettings();
        Assert.True(settings.NoteLandingGlow);
        Assert.Contains("Landing glow", GpuOnlyEffects.Omitted(settings));
    }
}
