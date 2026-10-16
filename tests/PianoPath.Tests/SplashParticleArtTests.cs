using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class SplashParticleArtTests
{
    [Fact]
    public void SurfaceHasOneSlowAlphaEnvelopeAndBoundedColouredTint()
    {
        Assert.True(SplashParticleAppearance.TryColor(new(.3f,.7f,1),0,1,1,.65f,out var fresh));
        Assert.True(SplashParticleAppearance.TryColor(new(.3f,.7f,1),.5f,1,1,.65f,out var half));
        Assert.Equal(new Vector3(fresh.X,fresh.Y,fresh.Z),new Vector3(half.X,half.Y,half.Z));
        Assert.Equal(.65f*MathF.Pow(.5f,.8f),half.W,6);
        Assert.True(fresh.Z>fresh.Y && fresh.Y>fresh.X);Assert.Equal(.95f,fresh.Z,5);
        Assert.True(SplashParticleAppearance.TryColor(Vector3.Zero,0,1,1,1,out var black));
        Assert.Equal(Vector3.Zero,new Vector3(black.X,black.Y,black.Z));
        Assert.False(SplashParticleAppearance.TryColor(Vector3.One,1,1,1,1,out _));
        Assert.False(SplashParticleAppearance.TryColor(new(float.NaN,0,0),0,1,1,1,out _));
    }
    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,false)] [InlineData(3,false)]
    [InlineData(4,false)] [InlineData(5,true)] [InlineData(6,false)]
    public void DoesNotOverrideOtherFamilies(int shape,bool expected)
    {
        Assert.Equal(expected,SplashParticleAppearance.Eligible(true,shape,false));
        Assert.False(SplashParticleAppearance.Eligible(false,shape,false));
        Assert.False(SplashParticleAppearance.Eligible(true,shape,true));
    }
}
