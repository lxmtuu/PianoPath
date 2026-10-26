using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The sustained emitters' density, which the two renderers used to disagree about: the engine births
/// <c>ParticleAmount × 1.3</c> embers and <c>WispAmount × 4</c> wisps a second per held key, the software stage
/// birthed <c>WispAmount</c> and no embers at all.
/// </summary>
public class HeldEmissionTests
{
    [Fact]
    public void The_engine_wisp_rate_is_four_per_unit()
    {
        // Four because the wisps come in two strands that twist around each other as they climb.
        Assert.Equal(180, HeldEmission.Wisps(45));
        Assert.Equal(0, HeldEmission.Wisps(0));
        Assert.Equal(600, HeldEmission.Wisps(150));
        Assert.Equal(HeldEmission.Wisps(30) * 2, HeldEmission.Wisps(60), 12);
    }

    [Fact]
    public void The_engine_ember_rate_is_1_3_per_unit()
    {
        Assert.Equal(44.2, HeldEmission.Sparks(34), 12);
        Assert.Equal(0, HeldEmission.Sparks(0));
        Assert.Equal(156, HeldEmission.Sparks(120), 12);
        Assert.Equal(HeldEmission.Sparks(10) * 4, HeldEmission.Sparks(40), 12);
    }

    [Fact]
    public void A_held_chord_costs_what_the_engine_says_it_does()
    {
        // A four-note chord at the defaults: 4 x 180 wisp births a second and 4 x 44.2 embers, which is why a
        // ceiling has to exist on the software stage — it is a frame-time budget, not a smaller rate.
        var wisps = HeldEmission.Wisps(45) * 4;
        var sparks = HeldEmission.Sparks(34) * 4;
        Assert.Equal(720, wisps);
        Assert.Equal(176.8, sparks, 12);
        Assert.True(wisps + sparks > 800, "the fallback has to drop births at its ceiling rather than emit less");
    }

    [Fact]
    public void The_rates_are_linear_so_a_slider_move_is_predictable()
    {
        foreach (var amount in new[] { 1.0, 7.5, 33, 99.5 })
        {
            Assert.Equal(HeldEmission.Wisps(amount) / 4, HeldEmission.Wisps(amount / 4), 12);
            Assert.Equal(HeldEmission.Sparks(amount) / 1.3, HeldEmission.Sparks(amount / 1.3), 9);
        }
    }
}
