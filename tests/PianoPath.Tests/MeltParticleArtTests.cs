using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class MeltParticleArtTests
{
    [Fact]
    public void RouteCannotBorrowSplashOrReleaseDroplets()
    {
        Assert.True(MeltParticleAppearance.Eligible(true,5,false,0,1));
        Assert.False(MeltParticleAppearance.Eligible(false,5,false,0,1));
        Assert.False(MeltParticleAppearance.Eligible(true,5,false,0,0));
        Assert.False(MeltParticleAppearance.Eligible(true,5,false,1,1));
        Assert.False(MeltParticleAppearance.Eligible(true,5,true,0,1));
        for(var shape=0;shape<8;shape++)
            Assert.Equal(shape==5,MeltParticleAppearance.Eligible(true,shape,false,0,1));
    }
    [Fact]
    public void SharedPigmentHasOneSlowEnvelopeAndRestrainedSameHueComponents()
    {
        Assert.True(MeltParticleAppearance.TryColor(new(.3f,.7f,1),.5f,1,1,.65f,out var body));
        Assert.Equal(.65f*MathF.Pow(.5f,.8f),body.W,6);
        Assert.True(body.Z>body.Y && body.Y>body.X);Assert.InRange(body.Z,0,.95f);
        var halo=MeltParticleAppearance.Halo(body);var glint=MeltParticleAppearance.Glint(body);
        Assert.Equal(new Vector3(body.X,body.Y,body.Z),new Vector3(glint.X,glint.Y,glint.Z));
        Assert.Equal(new Vector3(body.X,body.Y,body.Z),new Vector3(halo.X,halo.Y,halo.Z));
        Assert.Equal(body.W*.18f,halo.W);Assert.Equal(body.W*.30f,glint.W);
        Assert.False(MeltParticleAppearance.TryColor(Vector3.One,1,1,1,1,out _));
        Assert.False(MeltParticleAppearance.TryColor(new(float.NaN,0,0),0,1,1,1,out _));
    }
}
