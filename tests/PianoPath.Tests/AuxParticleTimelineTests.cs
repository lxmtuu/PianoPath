using Xunit;

namespace PianoPath.Tests;

public sealed class AuxParticleTimelineTests
{
    [Fact]
    public void InventoryHasEightUniqueParticleChannelsAnd128CaptureSlots()
    {
        Assert.Equal(8, AuxParticleTimeline.Routes.Count);
        Assert.Equal(8, AuxParticleTimeline.Routes.Select(r => r.Id).Distinct().Count());
        Assert.Equal(128, AuxParticleTimeline.Routes.Count * 2 * AuxParticleTimeline.Phases.Count * 2);
        Assert.DoesNotContain(AuxParticleTimeline.Routes, r => r.Effect is "Absorb" or "Morph" or "Snap Back" or "Echo Rings");
    }

    [Fact]
    public void ActiveAndOffControlsHaveExactlyTheSameInputsAndSampleTime()
    {
        foreach (var r in AuxParticleTimeline.Routes)
        {
            Assert.Equal(AuxParticleTimeline.SampleTime(r, "active"), AuxParticleTimeline.SampleTime(r, "off"));
            for (var f = 0; f < 180; f++)
                Assert.Equal(AuxParticleTimeline.At(r, "active", f), AuxParticleTimeline.At(r, "off", f));
        }
    }

    [Fact]
    public void ChannelsHaveSeparateImpactHeldAndReleaseTriggers()
    {
        foreach (var r in AuxParticleTimeline.Routes)
        foreach (var phase in AuxParticleTimeline.Phases)
        {
            var ticks = Enumerable.Range(0, 180).Select(f => AuxParticleTimeline.At(r, phase, f)).ToArray();
            Assert.Equal(r.Source == AuxParticleTimeline.Channel.Impact ? 1 : 0, ticks.Count(t => t.Impact));
            Assert.Equal(r.Source == AuxParticleTimeline.Channel.Release ? 1 : 0, ticks.Count(t => t.Down));
            Assert.Equal(r.Source == AuxParticleTimeline.Channel.Release ? 1 : 0, ticks.Count(t => t.Up));
            if (r.Source == AuxParticleTimeline.Channel.Impact) Assert.All(ticks, t => Assert.False(t.Pressed));
            if (r.Source == AuxParticleTimeline.Channel.Release)
            {
                Assert.True(ticks[20].Pressed);
                Assert.True(ticks[21].Up);
                Assert.False(ticks[21].Pressed);
                Assert.True(ticks[21].Pedal);
            }
            if (phase is "pedal-tail" or "expired")
                Assert.All(ticks.Skip(21), t => { Assert.False(t.Pressed); Assert.True(t.Pedal); });
        }
    }

    [Fact]
    public void SampleTimesAreExactFrameBoundariesAndExpiredIsAfterAllSelectedLifetimes()
    {
        foreach (var r in AuxParticleTimeline.Routes)
        foreach (var phase in AuxParticleTimeline.Phases)
        {
            var time = AuxParticleTimeline.SampleTime(r, phase);
            Assert.Equal(Math.Round(time * 60), time * 60, precision: 8);
        }
        Assert.Equal(3, AuxParticleTimeline.SampleTime(AuxParticleTimeline.Routes[0], "expired"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuxParticleTimeline.At(AuxParticleTimeline.Routes[0], "unknown", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuxParticleTimeline.At(AuxParticleTimeline.Routes[0], "active", -1));
    }
}
