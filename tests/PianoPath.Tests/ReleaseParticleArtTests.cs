using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class ReleaseParticleArtTests
{
    [Theory]
    [InlineData(3,5)] [InlineData(4,5)] [InlineData(5,3)]
    public void ProvenanceRequiresActualReleaseSource(int route,int shape)
    {
        Assert.True(ReleaseParticleAppearance.Eligible(true,shape,false,1,route));
        Assert.False(ReleaseParticleAppearance.Eligible(false,shape,false,1,route));
        Assert.False(ReleaseParticleAppearance.Eligible(true,shape,true,1,route));
        Assert.False(ReleaseParticleAppearance.Eligible(true,shape,false,0,route));
        foreach(var other in new[]{0,1,2,6})Assert.False(ReleaseParticleAppearance.Eligible(true,shape,false,1,other));
        Assert.False(ReleaseParticleAppearance.Eligible(true,shape==5?3:5,false,1,route));
    }
    [Fact]
    public void ThreeSurfacesHaveOneEnvelopeWithRestrainedSmokeAndNoWhiteGlint()
    {
        foreach(var route in new[]{3,4,5})
        {
            Assert.True(ReleaseParticleAppearance.TryColor(route,new(.3f,.6f,.9f),.5f,1,1,.65f,out var c));
            Assert.True(c.Z>c.Y && c.Y>c.X);Assert.InRange(c.Z,0,.95f);
            var alpha=.65f*(route==3?MathF.Pow(.5f,.8f):route==4?.5f:MathF.Pow(.5f,1.25f)*.35f);
            Assert.Equal(alpha,c.W,6);
            Assert.False(ReleaseParticleAppearance.TryColor(route,Vector3.One,1,1,1,1,out _));
            var g=ReleaseParticleAppearance.Glint(c,route);Assert.Equal(c.X,g.X);Assert.True(g.W<c.W);
        }
        Assert.False(ReleaseParticleAppearance.TryColor(0,Vector3.One,0,1,1,1,out _));
        Assert.False(ReleaseParticleAppearance.TryColor(3,new(float.NaN,0,0),0,1,1,1,out _));
        Assert.Equal(47,ReleaseParticleAppearance.StageKind(3,0,true));
        Assert.Equal(53,ReleaseParticleAppearance.StageKind(5,0,true));
    }
}
