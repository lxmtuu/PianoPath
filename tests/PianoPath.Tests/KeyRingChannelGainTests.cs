using Xunit;

namespace PianoPath.Tests;

public sealed class KeyRingChannelGainTests
{
    [Theory]
    [InlineData((byte)0, 1.5f, 0f, 1.5f)]
    [InlineData((byte)0, 0f, .7f, 0f)]
    [InlineData((byte)1, 0f, .7f, .7f)]
    [InlineData((byte)1, 1.5f, 0f, 0f)]
    [InlineData((byte)2, 0f, 0f, 1f)]
    [InlineData((byte)2, 1.5f, .8f, 1f)]
    [InlineData((byte)3, 1f, 1f, 0f)]
    [InlineData((byte)255, 1f, 1f, 0f)]
    public void EachRingUsesItsOwnChannelWithoutSquaringReleaseStrength(byte channel, float wave, float morph, float expected)
        => Assert.Equal(expected, KeyRingChannelGain.Resolve(channel, wave, morph));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void InvalidSelectedKnobsCannotPoisonDrawValues(float invalid)
    {
        Assert.Equal(0, KeyRingChannelGain.Resolve(0, invalid, 1));
        Assert.Equal(0, KeyRingChannelGain.Resolve(1, 1, invalid));
        Assert.Equal(1, KeyRingChannelGain.Resolve(2, invalid, invalid));
    }
}
