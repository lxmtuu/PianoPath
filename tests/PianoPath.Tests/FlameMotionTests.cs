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
    public void The_w2_candidate_keeps_the_numbers_the_lab_measured()
    {
        // The lab's flame panel (docs/prototypes/particle-motion-lab) is the model this candidate came from,
        // and check_particle_motion_lab.cjs reads these constants back out of this file. Pin them here too so
        // the C# build fails on a typo before the review clips are rendered.
        Assert.Equal(30f, FlameMotion.V3Buoyancy);
        Assert.Equal(1.7f, FlameMotion.V3Drag);
        Assert.Equal(.9f, FlameMotion.V3Lift);
        Assert.Equal(.9f, FlameMotion.V3Swell);
    }

    [Fact]
    public void A_w2_mote_accelerates_upward_and_still_slows_down()
    {
        // Same birth as the engine's default height: 140 px/s upward. Coasting (v2) would travel 70 px in the
        // half second this mote lives; buoyancy and drag make it climb less — the stream rises, but the plume
        // no longer shoots off the keyboard.
        float x = 0, y = 0, vx = 0, vy = -140, size = 5;
        const float dt = 1f / 60;
        for (var step = 0; step < 30; step++) FlameMotion.StepV3(ref x, ref y, ref vx, ref vy, ref size, step * dt, 0, dt);
        Assert.True(y < -40, $"a flame mote must still climb, but it moved {y:0.0} px");
        Assert.True(y > -70, $"the candidate must not out-run the shipped rise, but it moved {y:0.0} px");
        Assert.True(vy < 0, "a live mote is still travelling upward");
        Assert.True(MathF.Abs(vy) < 140, $"vertical drag must slow the climb, not speed it up (vy {vy:0.0})");
    }

    [Fact]
    public void A_w2_mote_swells_as_it_cools_and_its_flicker_saturates()
    {
        float x = 0, y = 0, vx = 0, vy = -140, size = 5;
        const float dt = 1f / 30;
        var widest = 0f;
        for (var step = 0; step < 15; step++)
        {
            FlameMotion.StepV3(ref x, ref y, ref vx, ref vy, ref size, step * dt, 1.2f, dt);
            widest = MathF.Max(widest, MathF.Abs(vx));
        }
        // Half a second, the life of a default-height mote: e^(.45) is about 1.57x, so it spreads instead of
        // ballooning to the width of a key.
        Assert.True(size > 5 * 1.5f && size < 5 * 1.6f, $"unexpected swell: {size:0.00} px");
        // The sway accelerates at 40 px/s², so lateral drag caps it at 40 / 1.7 px/s instead of letting the
        // wobble accumulate for the whole life.
        Assert.True(widest <= 40f / FlameMotion.V3Drag + .01f, $"lateral flicker ran to {widest:0.0} px/s");
    }

    [Fact]
    public void The_w2_step_is_a_pure_function_of_its_state()
    {
        float x1 = 3, y1 = 7, vx1 = -4, vy1 = -120, size1 = 6, x2 = x1, y2 = y1, vx2 = vx1, vy2 = vy1, size2 = size1;
        FlameMotion.StepV3(ref x1, ref y1, ref vx1, ref vy1, ref size1, .2f, 2.4f, 1f / 60);
        FlameMotion.StepV3(ref x2, ref y2, ref vx2, ref vy2, ref size2, .2f, 2.4f, 1f / 60);
        Assert.Equal(x1, x2);
        Assert.Equal(y1, y2);
        Assert.Equal(vx1, vx2);
        Assert.Equal(vy1, vy2);
        Assert.Equal(size1, size2);
        Assert.True(float.IsFinite(x1) && float.IsFinite(vx1) && float.IsFinite(size1), "no step may produce NaN");
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
