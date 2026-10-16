using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class FireworkParticleArtTests
{
    [Fact]
    public void SharedTintRemainsBoundedAndFadesOnce()
    {
        Assert.True(FireworkParticleAppearance.TryColor(new(.8f,.2f,.6f),0,1,1,.65f,out var fresh));
        Assert.True(FireworkParticleAppearance.TryColor(new(.8f,.2f,.6f),.5f,1,1,.65f,out var half));
        Assert.Equal(new Vector3(fresh.X,fresh.Y,fresh.Z),new Vector3(half.X,half.Y,half.Z));
        Assert.Equal(fresh.W*.5f,half.W);Assert.Equal(.95f,fresh.X,5);
        Assert.True(FireworkParticleAppearance.TryColor(Vector3.Zero,0,1,0,1,out var black));Assert.Equal(Vector4.Zero,black);
        Assert.False(FireworkParticleAppearance.TryColor(Vector3.One,float.NaN,1,1,1,out _));
        Assert.False(FireworkParticleAppearance.TryColor(Vector3.One,1,1,1,1,out _));
    }
    [Theory]
    [InlineData(0,27)] [InlineData(1,26)] [InlineData(2,2)] [InlineData(3,2)]
    [InlineData(4,2)] [InlineData(5,2)] [InlineData(6,2)]
    public void OnlyRadialDotsAndStreaksAreSelected(int shape,float expected)
    {
        Assert.Equal(expected,FireworkParticleAppearance.StageKind(true,shape,false,2));
        Assert.Equal(2,FireworkParticleAppearance.StageKind(false,shape,false,2));
        Assert.Equal(2,FireworkParticleAppearance.StageKind(true,shape,true,2));
    }
}
