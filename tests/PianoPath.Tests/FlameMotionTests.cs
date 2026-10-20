using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The held-key flame used to be one ellipse plume pinned to the key top in the software renderer, while the
/// engine drew a stream of cooling motes. These facts pin the law the two now share, so a change to the
/// engine's numbers cannot quietly leave the fallback drawing a different effect.
/// </summary>
public class FlameMotionTests
{
    [Fact]
    public void The_engine_emission_rule_is_reproduced()
    {
        // GpuStageSimulation births 70 * FlameIntensity * heat motes per second per key.
        Assert.Equal(49, FlameMotion.EmissionRate(70, 1), 12);
        Assert.Equal(70, FlameMotion.EmissionRate(100, 1), 12);
        Assert.Equal(24.5, FlameMotion.EmissionRate(70, .5), 12);
        Assert.Equal(0, FlameMotion.EmissionRate(0, 1), 12);
    }

    [Fact]
    public void The_rise_span_matches_the_engine()
    {
        // -(50 + 150 * FlameHeight) * (.6 + rand * .6): 84 to 168 px/s at the default height.
        Assert.Equal(-84, FlameMotion.RiseSpeed(60, 0), 12);
        Assert.Equal(-168, FlameMotion.RiseSpeed(60, 1), 12);
        Assert.Equal(-30, FlameMotion.RiseSpeed(0, 0), 12);
        Assert.Equal(-240, FlameMotion.RiseSpeed(100, 1), 12);
        for (var jitter = 0.0; jitter <= 1; jitter += .1)
            Assert.True(FlameMotion.RiseSpeed(60, jitter) < 0, "a flame mote always climbs");
    }

    [Fact]
    public void A_mote_lives_a_third_to_two_thirds_of_a_second()
    {
        // .22 + FlameHeight * .45 * (.6 + rand * .6), which is what keeps the stream continuous rather than
        // flickering: at the default height a mote outlives its own birth interval by roughly ten times.
        Assert.Equal(.382, FlameMotion.LifeSeconds(60, 0), 12);
        Assert.Equal(.544, FlameMotion.LifeSeconds(60, 1), 12);
        Assert.True(FlameMotion.LifeSeconds(100, 1) > FlameMotion.LifeSeconds(40, 1),
            "a taller flame must also live longer, or the height slider would only change speed");
    }

    [Fact]
    public void A_mote_is_about_half_a_lane_across()
    {
        Assert.Equal(5.075, FlameMotion.Size(14.5, 0), 12);
        Assert.Equal(9.425, FlameMotion.Size(14.5, 1), 12);
        Assert.Equal(0, FlameMotion.Size(0, .5), 12);
    }

    [Fact]
    public void The_cooling_ramp_starts_white_and_ends_deep_red()
    {
        var start = FlameMotion.Ramp(0);
        var end = FlameMotion.Ramp(1);
        Assert.Equal(1, start.R, 12);
        Assert.True(start.G > .9 && start.B > .7, "a fresh mote is white-hot");
        Assert.True(end.R < .5 && end.G < .05 && end.B < .05, "a dying mote is deep red");
        // Green only ever falls: white -> yellow -> orange -> red, with no second flare.
        var previous = double.MaxValue;
        for (var u = 0.0; u <= 1; u += .05)
        {
            var (r, g, _) = FlameMotion.Ramp(u);
            Assert.True(g <= previous + 1e-12, $"the ramp must cool monotonically, but green rose at {u:0.00}");
            Assert.True(r >= .44, "the red channel stays high so the mote reads as fire, not as a grey fade");
            previous = g;
        }
        // Out-of-range ages clamp instead of wrapping to another colour.
        Assert.Equal(FlameMotion.Ramp(0), FlameMotion.Ramp(-.5));
        Assert.Equal(FlameMotion.Ramp(1), FlameMotion.Ramp(1.5));
    }
}
