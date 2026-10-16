using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class ShatterParticleArtTests
{
    [Fact]
    public void FacetAndGlintRetainBoundedHueAndSingleLifetimeEnvelope()
    {
        Assert.True(ShatterParticleAppearance.TryColor(new(.3f,.8f,.4f),0,1,1,.65f,out var body));
        Assert.True(ShatterParticleAppearance.TryColor(new(.3f,.8f,.4f),.5f,1,1,.65f,out var half));
        Assert.Equal(.65f,body.W);Assert.Equal(body.W*.5f,half.W);
        Assert.Equal(new Vector3(body.X,body.Y,body.Z),new Vector3(half.X,half.Y,half.Z));
        var glint=ShatterParticleAppearance.Glint(body);
        Assert.Equal(new Vector3(body.X,body.Y,body.Z),new Vector3(glint.X,glint.Y,glint.Z));
        Assert.Equal(body.W*.45f,glint.W);Assert.Equal(.95f,body.Y,5);
        Assert.True(ShatterParticleAppearance.TryColor(Vector3.One,0,1,1,0,out var zero));Assert.Equal(0,zero.W);
        Assert.False(ShatterParticleAppearance.TryColor(Vector3.One,1,1,1,1,out _));
        Assert.False(ShatterParticleAppearance.TryColor(new(float.NaN,0,0),0,1,1,1,out _));
    }
    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,false)] [InlineData(3,false)]
    [InlineData(4,false)] [InlineData(5,false)] [InlineData(6,true)]
    public void OnlyImpactOriginShardAndNeverWispsAreSelected(int shape,bool expected)
    {
        Assert.Equal(expected,ShatterParticleAppearance.Eligible(true,shape,false,0));
        Assert.False(ShatterParticleAppearance.Eligible(false,shape,false,0));
        Assert.False(ShatterParticleAppearance.Eligible(true,shape,true,0));
        Assert.False(ShatterParticleAppearance.Eligible(true,shape,false,1));
        Assert.False(ShatterParticleAppearance.Eligible(true,shape,false,2));
    }
}
