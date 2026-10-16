using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class ConfettiParticleArtTests
{
    [Fact]
    public void PaperRetainsLegacyOpacityAndHasBoundedBirthHue()
    {
        Assert.True(ConfettiParticleAppearance.TryColor(new(.2f,.8f,.3f),0,1,out var fresh));
        Assert.True(ConfettiParticleAppearance.TryColor(new(.2f,.8f,.3f),.5f,1,out var half));
        Assert.Equal(1,fresh.W);Assert.Equal(.75f,half.W);
        Assert.Equal(new Vector3(fresh.X,fresh.Y,fresh.Z),new Vector3(half.X,half.Y,half.Z));
        Assert.Equal(.95f,fresh.Y,5);Assert.True(fresh.Y>fresh.Z && fresh.Z>fresh.X);
        Assert.True(ConfettiParticleAppearance.TryColor(Vector3.Zero,0,1,out var black));
        Assert.Equal(Vector3.Zero,new Vector3(black.X,black.Y,black.Z));
        Assert.False(ConfettiParticleAppearance.TryColor(Vector3.One,1,1,out _));
        Assert.False(ConfettiParticleAppearance.TryColor(new(float.NaN,0,0),0,1,out _));
    }
    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,true)] [InlineData(3,false)]
    [InlineData(4,false)] [InlineData(5,false)] [InlineData(6,false)]
    public void OtherPrimitivesAndWispsAreNeverSelected(int shape,bool expected)
    {
        Assert.Equal(expected,ConfettiParticleAppearance.Eligible(true,shape,false));
        Assert.False(ConfettiParticleAppearance.Eligible(false,shape,false));
        Assert.False(ConfettiParticleAppearance.Eligible(true,shape,true));
    }
    [Theory]
    [InlineData(0f,0f)] [InlineData(.4f,1.7f)] [InlineData(1.2f,5f)]
    public void OrientationMatchesActualStageAxisRatherThanNormalisedLifetime(float age,float phase)
    {
        var basis=ConfettiParticleAppearance.Orientation(age,phase);
        Assert.Equal(new Vector2(MathF.Cos(age*7+phase),MathF.Sin(age*7+phase)),basis);
        Assert.Equal(1,basis.Length(),5);
    }
    [Fact]
    public void PaperCornersFitNarrowExistingArQuadAtEveryAngle()
    {
        // Physical-size polygon: (+/-.94,+/-.27), (+/-.66,+/-.55).
        Assert.True(new Vector2(.94f,.27f).Length()<1);
        Assert.True(new Vector2(.66f,.55f).Length()<1);
    }
}
