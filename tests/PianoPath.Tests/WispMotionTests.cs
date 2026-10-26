using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// W0: the wisp's sideways wander used to be four copies of one expression — and a fifth, different one in
/// the software stage that integrated it as velocity, which drew a mote 10.6 px from where the GPU drew the
/// same mote after 0.8 s. These facts pin the shared curve so the stage, the AR quads and the review
/// diagnostic cannot drift apart again; <c>--verify</c> pins that the shipped stage really calls it.
/// </summary>
public class WispMotionTests
{
    [Fact]
    public void A_wisp_is_born_on_its_column()
    {
        // A short tap must leave a straight column, not a mote that jumps sideways on its first frame.
        Assert.Equal(0f, WispMotion.Offset(.55f, .4f, 0, 0));
        Assert.Equal(0f, WispMotion.Offset(.55f, .4f, 0, 2.9f));
    }

    [Fact]
    public void The_default_look_reaches_the_amplitude_and_rate_the_review_measured()
    {
        // docs/PARTICLE-MOTION-REVIEW.md §4.3: 16.4 px of sway at 5.75 rad/s for turbulence 55, width 40.
        Assert.Equal(16.405f, WispMotion.Amplitude(.55f, .4f), 2);
        Assert.Equal(5.75f, WispMotion.AngularFrequency(.55f), 4);
    }

    [Fact]
    public void A_mote_never_leaves_its_own_span()
    {
        foreach (var turbulence in new[] { 0f, .25f, .55f, 1f })
        foreach (var width in new[] { 0f, .4f, 1f })
        {
            var amplitude = WispMotion.Amplitude(turbulence, width);
            for (var age = 0f; age < 3f; age += .05f)
            for (var phase = 0f; phase < 6.3f; phase += .7f)
                Assert.InRange(Math.Abs(WispMotion.Offset(turbulence, width, age, phase)), 0f, amplitude + 1e-4f);
        }
    }

    [Fact]
    public void More_turbulence_and_more_width_both_widen_the_sway()
    {
        Assert.True(WispMotion.Amplitude(0, .4f) < WispMotion.Amplitude(.55f, .4f));
        Assert.True(WispMotion.Amplitude(.55f, .4f) < WispMotion.Amplitude(1, .4f));
        Assert.True(WispMotion.Amplitude(.55f, 0) < WispMotion.Amplitude(.55f, .4f));
        Assert.True(WispMotion.Amplitude(.55f, .4f) < WispMotion.Amplitude(.55f, 1));
        // A quiet wisp still drifts a little: turbulence zero is a calm mote, not a dead one.
        Assert.True(WispMotion.Amplitude(0, .4f) > 3f);
    }

    [Fact]
    public void Percentages_outside_the_slider_are_clamped_not_extrapolated()
    {
        // The settings model clamps to 0..100 before normalising, but a preset or a hand-edited JSON file can
        // arrive with anything; the curve has to stay inside the span it promises.
        Assert.Equal(WispMotion.Amplitude(0, .4f), WispMotion.Amplitude(-2, .4f));
        Assert.Equal(WispMotion.Amplitude(1, .4f), WispMotion.Amplitude(9, .4f));
        Assert.Equal(WispMotion.Amplitude(.55f, 0), WispMotion.Amplitude(.55f, -1));
        Assert.Equal(WispMotion.Amplitude(.55f, 1), WispMotion.Amplitude(.55f, 3));
        Assert.Equal(0f, WispMotion.Offset(.55f, .4f, -.5f, 1.2f));
    }

    [Fact]
    public void The_sway_fades_in_over_the_first_three_fifths_of_a_second()
    {
        Assert.Equal(0f, WispMotion.Envelope(0));
        Assert.Equal(1f, WispMotion.Envelope(.625f), 5);
        Assert.Equal(1f, WispMotion.Envelope(4));
        Assert.True(WispMotion.Envelope(.2f) is > 0f and < 1f);
    }

    [Fact]
    public void Phase_and_age_are_the_same_clock()
    {
        // sin(age·ω + φ) must equal sin((age + φ/ω)·ω): the phase is an offset on the mote's own clock, which
        // is what lets the GPU, the AR quads and the stage share one call with different phase sources.
        var rate = WispMotion.AngularFrequency(.55f);
        foreach (var phase in new[] { .4f, 1.9f, 5.5f })
            Assert.Equal(WispMotion.Offset(.55f, .4f, 1.1f, phase), WispMotion.Offset(.55f, .4f, 1.1f + phase / rate, 0), 3);
    }

    [Fact]
    public void The_two_precisions_describe_the_same_curve()
    {
        // The sprite paths evaluate the sine in float and the AR projection in double; that is what each
        // renderer always did, and keeping it is what lets this be a pure refactor of the accepted frames.
        // If one of the two ever drifts, the renderers part company again.
        for (var age = 0f; age < 2.5f; age += .07f)
        for (var phase = 0f; phase < 6.3f; phase += .9f)
        {
            var single = WispMotion.Offset(.8f, .65f, age, phase);
            var precise = WispMotion.OffsetPrecise(.8f, .65f, age, phase);
            Assert.InRange(Math.Abs(precise - single), 0, 1e-5);
        }
    }

    [Fact]
    public void The_curve_is_deterministic_and_repeatable()
    {
        // Same inputs, same pixels: no clock, no RNG, no ambient state.
        Assert.Equal(WispMotion.Offset(.7f, .3f, 1.25f, 2.1f), WispMotion.Offset(.7f, .3f, 1.25f, 2.1f));
    }
}
