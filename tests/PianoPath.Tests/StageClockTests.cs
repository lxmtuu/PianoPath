using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// W0: the slow-motion setting is a particle clock, not a stage clock. The GPU simulation has always
/// worked that way (<c>particleDt = frameDt × PhysicsTimeFactor</c> for particles, <c>frameDt</c> for
/// rings, flashes, live bars and the beat envelope); the software stage used one scaled clock for
/// everything, so at any factor but 100 its live bars fell at a different speed from the engine's.
/// </summary>
public class StageClockTests
{
    [Fact]
    public void The_default_factor_leaves_the_frame_clock_untouched()
    {
        // Not "close to": the accepted previews and WARP evidence were rendered at 100, and the arithmetic
        // must return exactly the same double or a rasterized edge can move.
        foreach (var frame in new[] { 0.0, 1.0 / 60, 1.0 / 144, .0163943, .05, .03333333333333333 })
            Assert.Equal(frame, StageClock.Particle(frame, 100));
    }

    [Fact]
    public void The_slider_scales_the_particle_clock_linearly()
    {
        Assert.Equal(.5, StageClock.Particle(1, 50), 12);
        Assert.Equal(1, StageClock.Particle(2.5, 40), 12);
        Assert.Equal(.1, StageClock.Particle(1, 10), 12);
        Assert.Equal(3, StageClock.Particle(1, 300), 12);   // the settings clamp allows speed-up as well
        Assert.Equal(0, StageClock.Particle(1, 0), 12);
    }

    [Fact]
    public void It_matches_the_gpu_arithmetic()
    {
        // GpuStageSimulation multiplies the frame by the look's fraction (settings value / 100), which is
        // why the shared helper multiplies by a fraction instead of dividing by 100 after multiplying.
        // At 10% the two spellings really do differ in the last bit (x * 10 / 100 rounds twice), so pinning
        // this exact pair is what proves the helper follows the GPU and not the older `dt * percent / 100`.
        const double frame = 1.0 / 60;
        var gpuStyle = frame * (10 / 100.0);
        Assert.Equal(gpuStyle, StageClock.Particle(frame, 10));
        Assert.NotEqual(frame * 10 / 100, StageClock.Particle(frame, 10));
        foreach (var percent in new[] { 40.0, 55, 100, 175, 300 })
            Assert.Equal(frame * (percent / 100), StageClock.Particle(frame, percent));
    }

    [Fact]
    public void A_frozen_frame_is_frozen_on_both_clocks()
    {
        Assert.Equal(0, StageClock.Particle(0, 100));
        Assert.Equal(0, StageClock.Particle(0, 40));
    }

    [Fact]
    public void Half_speed_is_the_midpoint_of_every_pair()
    {
        // A slow take must be the same animation filmed slower: half the factor, half the particle travel,
        // and no dependence on the frame length.
        foreach (var frame in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144 })
            Assert.Equal(StageClock.Particle(frame, 50) * 2, StageClock.Particle(frame, 100), 15);
    }
}
