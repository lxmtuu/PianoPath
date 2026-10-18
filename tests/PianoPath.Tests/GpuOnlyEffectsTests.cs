using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// A software-rendered take cannot draw the three GPU-only effects, so the recording line has to name
/// every one the preset switched on. These facts pin that list on the pure half of the helper, which is
/// the half this project links; the default look is checked by <c>--verify</c> where the settings type exists.
/// </summary>
public class GpuOnlyEffectsTests
{
    [Fact]
    public void A_plain_preset_omits_nothing()
    {
        Assert.Empty(GpuOnlyEffects.Omitted(shimmer: false, landingGlow: false, shootingStars: false));
    }

    [Fact]
    public void Each_gpu_only_effect_is_named_by_itself()
    {
        Assert.Equal(new[] { "Note shimmer" }, GpuOnlyEffects.Omitted(shimmer: true, landingGlow: false, shootingStars: false));
        Assert.Equal(new[] { "Landing glow" }, GpuOnlyEffects.Omitted(shimmer: false, landingGlow: true, shootingStars: false));
        Assert.Equal(new[] { "Shooting stars" }, GpuOnlyEffects.Omitted(shimmer: false, landingGlow: false, shootingStars: true));
    }

    [Fact]
    public void The_list_keeps_the_interface_order()
    {
        Assert.Equal(new[] { "Note shimmer", "Landing glow", "Shooting stars" },
            GpuOnlyEffects.Omitted(shimmer: true, landingGlow: true, shootingStars: true));
    }
}
