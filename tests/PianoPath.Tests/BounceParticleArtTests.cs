using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class BounceParticleArtTests
{
    [Fact]
    public void SourceGateCannotBorrowPrimaryStreaksOrMelt()
    {
        Assert.True(BounceParticleAppearance.Eligible(true,1,false,0,2));
        Assert.False(BounceParticleAppearance.Eligible(false,1,false,0,2));
        Assert.False(BounceParticleAppearance.Eligible(true,1,false,0,0));
        Assert.False(BounceParticleAppearance.Eligible(true,1,false,0,1));
        Assert.False(BounceParticleAppearance.Eligible(true,1,false,1,2));
        Assert.False(BounceParticleAppearance.Eligible(true,1,true,0,2));
        for(var shape=0;shape<8;shape++)Assert.Equal(shape==1,BounceParticleAppearance.Eligible(true,shape,false,0,2));
    }
    [Fact]
    public void BillboardFollowsStageVelocityWithoutChangingScale()
    {
        var right=new ArVec3(1,0,0);var up=new ArVec3(0,1,0);
        foreach(var v in new[]{new Vector2(20,-300),new Vector2(-15,70),Vector2.Zero})
        {
            BounceParticleAppearance.BillboardAxes(right,up,v.X,v.Y,out var x,out var y);
            var d=BounceParticleAppearance.Orientation(v.X,v.Y);
            Assert.InRange(Math.Abs(x.Length-1),0,1e-6);Assert.InRange(Math.Abs(y.Length-1),0,1e-6);
            Assert.InRange(Math.Abs(ArVec3.Dot(x,y)),0,1e-6);
            Assert.InRange((x-(right*d.X-up*d.Y)).Length,0,1e-6);
        }
    }
    [Fact]
    public void SingleLinearEnvelopePreservesBothTintedAndNeutralBirths()
    {
        Assert.True(BounceParticleAppearance.TryColor(new(.2f,.6f,1),.5f,1,1,.65f,out var body));
        Assert.Equal(.325f,body.W,6);Assert.True(body.Z>body.Y && body.Y>body.X);
        Assert.InRange(body.Z,0,.95f);
        Assert.True(BounceParticleAppearance.TryColor(Vector3.One,0,1,1,1,out var white));
        Assert.Equal(white.X,white.Y);Assert.Equal(white.Y,white.Z);Assert.InRange(white.X,0,.95f);
        Assert.False(BounceParticleAppearance.TryColor(Vector3.One,1,1,1,1,out _));
        Assert.False(BounceParticleAppearance.TryColor(new(float.NaN,0,0),0,1,1,1,out _));
    }
}
