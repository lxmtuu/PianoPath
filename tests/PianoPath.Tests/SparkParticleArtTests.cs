using Xunit;
namespace PianoPath.Tests;
public sealed class SparkParticleArtTests
{
    [Theory]
    [InlineData(0,19)] [InlineData(1,18)] [InlineData(2,2)] [InlineData(3,2)]
    [InlineData(4,2)] [InlineData(5,2)] [InlineData(6,2)]
    public void CandidateOnlySelectsCompactDotsAndStreaks(int shape, float expected)
    {
        Assert.Equal(expected, SparkParticleAppearance.ArKind(true,shape,false));
        Assert.Equal(2, SparkParticleAppearance.ArKind(false,shape,false));
        Assert.Equal(2, SparkParticleAppearance.ArKind(true,shape,true));
        var legacy = shape == 1 ? 1 : 0;
        Assert.Equal(legacy, SparkParticleAppearance.StageKind(false,shape,false,legacy));
        Assert.Equal(legacy, SparkParticleAppearance.StageKind(true,shape,true,legacy));
        Assert.Equal(shape is 0 or 1 ? expected : legacy,
            SparkParticleAppearance.StageKind(true,shape,false,legacy));
    }
}
