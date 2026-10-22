using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// W1's flow is one law shared by the engine, the software stage and the review that measures them
/// (<c>docs/PARTICLE-MOTION-REVIEW.md</c> §5.1, prototype in <c>docs/prototypes/particle-motion-lab</c>).
/// These facts pin the four properties the look depends on, so a tinkered constant cannot quietly turn the
/// coherent wind back into a per-mote wobble — and so the two renderers, which feed it floats and doubles
/// respectively, cannot start disagreeing about what the law means.
/// </summary>
public class ParticleFlowTests
{
    private const double Frame = 1.0 / 60;

    [Fact]
    public void Zero_flow_leaves_the_particle_exactly_where_it_was()
    {
        // The default is zero, and the accepted wisps/flames captures are pinned to the arithmetic that runs
        // instead: the flow may not move a single bit until the slider is raised.
        double x = 120, y = 400, vx = 3, vy = -170;
        ParticleFlow.Step(ref x, ref y, ref vx, ref vy, .37, 1.2, Frame, 36, 100, 1, 0);
        Assert.Equal(120, x);
        Assert.Equal(400, y);
        Assert.Equal(3, vx);
        Assert.Equal(-170, vy);
    }

    [Fact]
    public void The_wind_bends_the_rise_instead_of_replacing_it()
    {
        // Vertical entrainment is 12% of the wind's, so a wisp still climbs: after a second the vertical
        // velocity is still most of the way from where it started to the wind's own vertical speed.
        double x = 120, y = 400, vx = 0, vy = -170;
        for (var i = 0; i < 60; i++) ParticleFlow.Step(ref x, ref y, ref vx, ref vy, .25, i * Frame, Frame, 36, 100, 1, 1);
        var wind = ParticleFlow.Wind(x, y, 1, 36, 100, 1);
        Assert.True(vy < -100, $"a held wisp must still climb, got {vy:0.##} px/s");
        // A second of entrainment leaves roughly three quarters of the original climb: the wind steers the
        // stream, it does not flatten it into a horizontal drift.
        Assert.True(Math.Abs(vy - wind.Y) > .7 * Math.Abs(-170 - wind.Y),
            $"the vertical entrainment is bounded well below the horizontal one (vy {vy:0.##}, wind {wind.Y:0.##})");
        Assert.True(y < 400 - 100, $"the particle has to have climbed, ended at {y:0.##}");
    }

    [Fact]
    public void Neighbours_share_the_eddy_and_two_seeds_do_not()
    {
        // The same seed a few pixels apart is nearly the same eddy (that is the "bends as a body" read); a
        // different seed at the same point is a different one.
        var left = ParticleFlow.Curl(300, 400, 1.4, .2);
        var right = ParticleFlow.Curl(304, 400, 1.4, .2);
        var other = ParticleFlow.Curl(300, 400, 1.4, .7);
        var neighbourDistance = Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
        var strangerDistance = Math.Abs(left.X - other.X) + Math.Abs(left.Y - other.Y);
        Assert.True(neighbourDistance < strangerDistance,
            $"neighbours must share the eddy: {neighbourDistance:0.###} vs {strangerDistance:0.###}");
    }

    [Fact]
    public void Diffusion_is_zero_mean_and_linear_in_the_slider()
    {
        // Zero mean over the seed, so raising the slider widens the stream without blowing it sideways.
        var sum = 0.0;
        for (var i = 0; i < 1000; i++) sum += ParticleFlow.Diffusion(i / 999.0, 55);
        Assert.Equal(0, sum, 6);
        Assert.Equal(ParticleFlow.DiffusionRate, ParticleFlow.Diffusion(1, 55), 9);
        Assert.Equal(-ParticleFlow.DiffusionRate, ParticleFlow.Diffusion(0, 55), 9);
        Assert.True(Math.Abs(ParticleFlow.Diffusion(.8, 100) - 2 * ParticleFlow.Diffusion(.8, 50)) < 1e-12,
            "twice the slider must be twice the kick");
    }

    [Fact]
    public void The_same_inputs_give_the_same_path_at_any_frame_rate()
    {
        // Slow motion scales dt; the path may not. Two steps at 120 Hz must land where one step at 60 Hz does
        // within a fraction of a pixel, and a seed outside one turn must fold into it.
        double x1 = 250, y1 = 420, vx1 = 6, vy1 = -160;
        double x2 = x1, y2 = y1, vx2 = vx1, vy2 = vy1;
        ParticleFlow.Step(ref x1, ref y1, ref vx1, ref vy1, .42, 2, Frame, 36, 100, 1, .8);
        ParticleFlow.Step(ref x2, ref y2, ref vx2, ref vy2, .42, 2, Frame / 2, 36, 100, 1, .8);
        ParticleFlow.Step(ref x2, ref y2, ref vx2, ref vy2, .42, 2 + Frame / 2, Frame / 2, 36, 100, 1, .8);
        Assert.Equal(x1, x2, 1);
        Assert.Equal(y1, y2, 1);

        double xa = 250, ya = 420, vxa = 6, vya = -160, xb = 250, yb = 420, vxb = 6, vyb = -160;
        for (var i = 0; i < 30; i++)
        {
            ParticleFlow.Step(ref xa, ref ya, ref vxa, ref vya, .42, 2 + i * Frame, Frame, 36, 100, 1, .8);
            ParticleFlow.Step(ref xb, ref yb, ref vxb, ref vyb, 1.42, 2 + i * Frame, Frame, 36, 100, 1, .8);
        }
        Assert.Equal(xa, xb, 6);
        Assert.Equal(ya, yb, 6);
    }
}
