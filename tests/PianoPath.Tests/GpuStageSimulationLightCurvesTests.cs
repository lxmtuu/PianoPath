using System;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the shared impact-and-hold light curves the renderer uses (Gpu/StageLightCurves.cs),
/// so a quiet tap stays a spark, a hard hit opens up, a flash blooms in instead of popping, and a held key's
/// breath stays a gentle sway rather than a strobe.
/// </summary>
public class GpuStageSimulationLightCurvesTests
{
    [Fact]
    public void ImpactDynamicWidensQuietToLoud()
    {
        var soft = StageLightCurves.ImpactDynamic(.3f);
        var loud = StageLightCurves.ImpactDynamic(1f);
        Assert.True(soft >= .40f && soft < .6f, $"A soft hit should stay a spark, got {soft:0.###}.");
        Assert.Equal(1.6f, loud, 3);
        Assert.True(loud / soft > 2.4f, $"The quiet-to-loud band should feel expressive (ratio {loud / soft:0.##}, was ~1.9 with the old linear mix).");
        // quadratic: the midpoint sits below the straight line between the endpoints, so dynamics lean quiet until the hand really digs in
        Assert.True(StageLightCurves.ImpactDynamic(.65f) < (soft + (loud - soft) * .5f));
    }

    [Fact]
    public void ImpactSizeBoostScalesTheFlareWithTheHand()
    {
        var soft = StageLightCurves.ImpactSizeBoost(.3f);
        var loud = StageLightCurves.ImpactSizeBoost(1f);
        Assert.True(soft > 1.3f && soft < 1.7f && loud > 3.9f && loud <= 4f, $"Bounds drifted ({soft:0.##} .. {loud:0.##}).");
        Assert.True(loud / soft > 2.2f, $"The size band should open wider than the old 1.67× spread ({loud / soft:0.##}).");
        // out-of-range velocities must not explode or invert the curve
        Assert.Equal(StageLightCurves.ImpactDynamic(1f), StageLightCurves.ImpactDynamic(2f));
        Assert.Equal(StageLightCurves.ImpactDynamic(0f), StageLightCurves.ImpactDynamic(-1f));
    }

    [Fact]
    public void FlashEnvelopeBloomsInAndSettlesSmoothly()
    {
        Assert.Equal(0, StageLightCurves.FlashEnvelope(0f));
        Assert.Equal(0, StageLightCurves.FlashEnvelope(1f));
        // attack: rises through the first ~55 ms instead of a one-frame pop, and is done with the attack by 6% of life
        Assert.True(StageLightCurves.FlashEnvelope(.01f) > 0 && StageLightCurves.FlashEnvelope(.01f) < .35f);
        Assert.True(Math.Abs(StageLightCurves.FlashEnvelope(.06f) - .8836f) < .05f);
        // tail: strictly decreasing well after the attack, and smooth (no harsh cut — the slope eases to 0 at the end)
        var last = StageLightCurves.FlashEnvelope(.3f);
        for (var u = .35f; u <= .99f; u += .05f)
        {
            var next = StageLightCurves.FlashEnvelope(u);
            Assert.True(next < last, $"The flash tail must keep settling ({u:0.##});");
            last = next;
        }
        Assert.True(StageLightCurves.FlashEnvelope(.9f) < .02f, $"The tail should be nearly dark before life ends ({StageLightCurves.FlashEnvelope(.9f):0.###}).");
    }

    [Fact]
    public void FlashStyleStrengthKeepsStyleChannelsExpressiveButAlive()
    {
        Assert.True(StageLightCurves.FlashStyleStrength(0f) >= .35f, "Even the lightest touch has to light something.");
        Assert.True(StageLightCurves.FlashStyleStrength(1f) <= 1.25f, "A hard hit should not blow the styled flash far past its old maximum.");
        Assert.True(StageLightCurves.FlashStyleStrength(1f) - StageLightCurves.FlashStyleStrength(.3f) > .7f, "The styled channel should swing clearly with velocity.");
    }

    [Fact]
    public void HoldBreathStaysQuietForTapsAndSwaysSoftlyForHolds()
    {
        Assert.Equal(0, StageLightCurves.HoldBreathAmp(0));
        Assert.Equal(0, StageLightCurves.HoldBreathAmp(.3));
        Assert.True(StageLightCurves.HoldBreathAmp(.8) > 0 && StageLightCurves.HoldBreathAmp(.8) < StageLightCurves.HoldBreathAmp(1.3),
            "The breath must ramp in, not snap.");
        Assert.Equal(.075f, StageLightCurves.HoldBreathAmp(1.25), 3);
        Assert.Equal(.075f, StageLightCurves.HoldBreathAmp(30), 3);
        var held = StageLightCurves.KeyBreathMultiplier(2.0, 3.0, 57);
        Assert.True(held is > .925f and < 1.075f, $"A breathing key must never strobe (multiplier {held:0.###}).");
        var tap = StageLightCurves.KeyBreathMultiplier(.1, 3.0, 57);
        Assert.Equal(1f, tap);
        // neighbouring keys sway a little out of phase so a long chord shimmers instead of pulsing as one column
        Assert.NotEqual(StageLightCurves.KeyBreathMultiplier(2.0, 3.0, 57), StageLightCurves.KeyBreathMultiplier(2.0, 3.0, 60), 4);
    }
}
