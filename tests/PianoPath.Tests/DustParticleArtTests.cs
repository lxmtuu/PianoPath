using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class DustParticleArtTests
{
    [Fact]
    public void SharedTintIsBoundedAndFadeOccursOnceInOpacity()
    {
        Assert.True(DustParticleAppearance.TryColor(new(.3f,.8f,.4f),0,1,.38f,.65f,out var fresh));
        Assert.True(DustParticleAppearance.TryColor(new(.3f,.8f,.4f),.5f,1,.38f,.65f,out var half));
        Assert.Equal(.35f*.38f*.65f,fresh.W,6);
        Assert.Equal(fresh.W*.5f,half.W,6);
        Assert.Equal(new Vector3(fresh.X,fresh.Y,fresh.Z),new Vector3(half.X,half.Y,half.Z));
        Assert.Equal(.95f,fresh.Y,5);Assert.True(fresh.Y>fresh.Z && fresh.Z>fresh.X);
        Assert.True(DustParticleAppearance.TryColor(Vector3.Zero,0,1,1,1,out var black));
        Assert.Equal(Vector3.Zero,new Vector3(black.X,black.Y,black.Z));
        Assert.True(DustParticleAppearance.TryColor(Vector3.One,0,1,1,0,out var disabled));Assert.Equal(0,disabled.W);
        Assert.True(DustParticleAppearance.TryColor(Vector3.One,0,1,20,20,out var capped));Assert.Equal(.35f,capped.W);
        Assert.False(DustParticleAppearance.TryColor(Vector3.One,1,1,1,1,out _));
        Assert.False(DustParticleAppearance.TryColor(Vector3.One,-.1f,1,1,1,out _));
        Assert.False(DustParticleAppearance.TryColor(Vector3.One,0,0,1,1,out _));
        Assert.False(DustParticleAppearance.TryColor(new(float.NaN,0,0),0,1,1,1,out _));
        Assert.False(DustParticleAppearance.TryColor(Vector3.One,0,1,-1,1,out _));
        Assert.False(DustParticleAppearance.TryColor(Vector3.One,0,1,1,float.PositiveInfinity,out _));
    }
    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,false)] [InlineData(3,true)]
    [InlineData(4,false)] [InlineData(5,false)] [InlineData(6,false)]
    public void OnlyDustAndNeverWispsAreSelected(int shape,bool expected)
    {
        Assert.Equal(expected,DustParticleAppearance.Eligible(true,shape,false));
        Assert.False(DustParticleAppearance.Eligible(false,shape,false));
        Assert.False(DustParticleAppearance.Eligible(true,shape,true));
    }
}
