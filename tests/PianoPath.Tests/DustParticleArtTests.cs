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
    [Fact]
    public void V2RecoversWeakBirthGlowWithoutChangingTintFadeOrFullGlowBursts()
    {
        var birth=new Vector3(.3f,.8f,.4f);
        Assert.True(DustParticleAppearance.TryColor(birth,0,1,.38f,.65f,out var v1));
        Assert.True(DustParticleAppearance.TryColor(birth,0,1,.38f,.65f,out var v2,true));
        var response=1-.62f*.62f*.62f;
        Assert.Equal(response*.65f*.35f,v2.W,6);
        Assert.True(v2.W>v1.W*2 && v2.W<v1.W*2.01f);
        Assert.Equal(new Vector3(v1.X,v1.Y,v1.Z),new Vector3(v2.X,v2.Y,v2.Z));
        DustParticleAppearance.TryColor(birth,.5f,1,.38f,.65f,out var half,true);
        Assert.Equal(v2.W*.5f,half.W,6);
        foreach(var glow in new[]{0f,1f,2f})
        {
            DustParticleAppearance.TryColor(birth,.23f,1,glow,.65f,out var old);
            DustParticleAppearance.TryColor(birth,.23f,1,glow,.65f,out var revised,true);
            Assert.Equal(old,revised);
        }
        DustParticleAppearance.TryColor(birth,0,1,.38f,0,out var zero,true);Assert.Equal(0,zero.W);
        DustParticleAppearance.TryColor(birth,0,1,1e-8f,.65f,out var tiny,true);
        Assert.True(tiny.W>0);
        var previous=0f;
        for(var i=0;i<=100;i++)
        {
            DustParticleAppearance.TryColor(birth,0,1,i/100f,.65f,out var c,true);
            Assert.InRange(c.W,previous,.35f);previous=c.W;
        }
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
