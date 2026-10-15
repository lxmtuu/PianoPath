using Xunit;
using System.Numerics;
namespace PianoPath.Tests;
public sealed class SparkParticleArtTests
{
    [Fact]
    public void SharedColourUsesOneLifetimeEnvelopeAndRetainsHue()
    {
        var birth=new Vector3(.4f,.8f,.2f);
        Assert.True(SparkParticleAppearance.TryColor(birth,0,1,1,.65f,out var fresh));
        Assert.True(SparkParticleAppearance.TryColor(birth,.5f,1,1,.65f,out var half));
        Assert.Equal(new Vector3(fresh.X,fresh.Y,fresh.Z),new Vector3(half.X,half.Y,half.Z));
        Assert.Equal(fresh.W*.5f,half.W);
        Assert.Equal(.95f, fresh.Y,5);
        Assert.True(fresh.Y>fresh.X && fresh.X>fresh.Z);
        Assert.True(SparkParticleAppearance.TryColor(Vector3.Zero,0,1,1,1,out var black));
        Assert.Equal(Vector3.Zero,new Vector3(black.X,black.Y,black.Z));
        Assert.True(SparkParticleAppearance.TryColor(birth,0,1,0,1,out var off));Assert.Equal(0,off.W);
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1f)]
    public void InvalidSharedColourInputsAreRejected(float value)
    {
        Assert.False(SparkParticleAppearance.TryColor(new(value,0,0),0,1,1,1,out _));
        Assert.False(SparkParticleAppearance.TryColor(Vector3.One,value,1,1,1,out _));
        Assert.False(SparkParticleAppearance.TryColor(Vector3.One,0,value,1,1,out _));
        Assert.False(SparkParticleAppearance.TryColor(Vector3.One,0,1,value,1,out _));
        Assert.False(SparkParticleAppearance.TryColor(Vector3.One,0,1,1,value,out _));
    }
    [Theory]
    [InlineData(0,21)] [InlineData(1,20)] [InlineData(2,2)] [InlineData(3,2)] [InlineData(4,2)]
    public void V2NeverOverridesOtherFamilies(int shape,float expected)
    {
        Assert.Equal(expected,SparkParticleAppearance.ArKind(true,shape,false,true));
        Assert.Equal(2,SparkParticleAppearance.ArKind(true,shape,true,true));
        Assert.Equal(2,SparkParticleAppearance.ArKind(false,shape,false,true));
    }
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
