using Xunit;

namespace PianoPath.Tests;

public sealed class EffectChannelTests
{
    [Fact]
    public void LayerOwnedEffectsStayBehindTheirOwnSwitch()
        => Assert.False(EffectChannel.OwnsItsDraw(EffectChannel.Layer));

    [Theory]
    [InlineData(EffectChannel.Morph)]
    [InlineData(EffectChannel.Release)]
    public void ChosenEffectsDrawWithEverySiblingLayerOff(byte channel)
    {
        Assert.True(EffectChannel.OwnsItsDraw(channel));
        Assert.NotEqual(EffectChannel.Layer, channel);
    }

    [Fact]
    public void ChannelsKeepTheGpuRingNumbering()
    {
        Assert.Equal(0, (int)EffectChannel.Layer);
        Assert.Equal(1, (int)EffectChannel.Morph);
        Assert.Equal(2, (int)EffectChannel.Release);
        // The stage resolves a ring's brightness with KeyRingChannelGain, so the numbers must mean the
        // same thing on both sides: wave knob, morph knob, and release strength captured at birth.
        Assert.Equal(.7f, KeyRingChannelGain.Resolve(EffectChannel.Morph, .2f, .7f));
        Assert.Equal(1f, KeyRingChannelGain.Resolve(EffectChannel.Release, .2f, .7f));
        Assert.Equal(.2f, KeyRingChannelGain.Resolve(EffectChannel.Layer, .2f, .7f));
    }
}
