using System.Numerics;
using Xunit;

namespace PianoPath.Tests;

public sealed class HeldParticleArtTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void SurfaceHasFiniteSceneExtentsAndOneLifetimeEnvelope(bool wisp)
    {
        Assert.True(HeldParticleAppearance.TryResolve(wisp, 2, .2f, 1, .7f, .8f,
            new Vector3(.1f, .8f, .4f), .4f, true, out var s));
        Assert.InRange(s.Color.W, 0, 1);
        Assert.True(s.HalfHeight > s.HalfWidth && float.IsFinite(s.Color.X));
        Assert.Equal(wisp ? 7 : 4, s.Shape);
        Assert.Equal(!wisp, s.BaseAnchored);
        Assert.Equal(.2f, s.Progress);
        Assert.False(HeldParticleAppearance.TryResolve(wisp, 2, 1, 1, 0, 1, Vector3.One, .4f, false, out _));
        Assert.False(HeldParticleAppearance.TryResolve(wisp, 2, 0, 1, 0, 0, Vector3.One, .4f, false, out _));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void V3OnlyChangesTheWispMaterialDiscriminator(bool wisp)
    {
        Assert.True(HeldParticleAppearance.TryResolve(wisp,1.3f,.3f,1,.7f,.8f,new(.2f,.4f,1),.4f,true,out var v2));
        Assert.True(HeldParticleAppearance.TryResolve(wisp,1.3f,.3f,1,.7f,.8f,new(.2f,.4f,1),.4f,true,out var v3,true));
        Assert.Equal(v2 with { Shape=wisp?8:4 },v3);
        Assert.Equal(wisp?7:4,v2.Shape);
    }

    [Theory]
    [InlineData(true,false,false,7)] [InlineData(true,true,false,8)]
    [InlineData(true,false,true,7)] [InlineData(true,true,true,8)]
    [InlineData(false,false,false,4)] [InlineData(false,true,false,4)]
    [InlineData(false,false,true,6)] [InlineData(false,true,true,6)]
    public void FamilyCandidatesPreserveEnvelopeAndDoNotChangeTheOtherFamily(bool wisp,bool wispV3,bool flameV3,float shape)
    {
        Assert.True(HeldParticleAppearance.TryResolve(wisp,1.3f,.3f,1,.7f,.8f,new(.2f,.4f,1),.4f,true,out var original));
        Assert.True(HeldParticleAppearance.TryResolve(wisp,1.3f,.3f,1,.7f,.8f,new(.2f,.4f,1),.4f,true,out var candidate,wispV3,flameV3));
        Assert.Equal(original with{Shape=shape},candidate);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1f)]
    public void InvalidInputsCannotMakeGeometry(float invalid)
    {
        Assert.False(HeldParticleAppearance.TryResolve(true, invalid, .1f, 1, 0, 1, Vector3.One, .4f, false, out _));
        Assert.False(HeldParticleAppearance.TryResolve(false, 1, invalid, 1, 0, 1, Vector3.One, .4f, false, out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void FacingSurfaceDoesNotCollapseForOverheadOrObliqueCalibration(int viewIndex)
    {
        (double X, double Y)[][] views =
        [
            [(70,292),(1214,268),(1236,421),(52,449)],
            [(70,270),(1210,270),(1210,450),(70,450)],
            [(240,180),(1040,180),(1240,560),(40,560)]
        ];
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, views[viewIndex], 1280,720,out _,70)!;
        Assert.NotNull(calibration.CameraPose);
        var pose = calibration.CameraPose!.Value;
        var center = new ArVec3(pose.KeyboardWidthDepthRatio * .5, .12, -.15);
        Assert.True(ArParticleBillboard.TryFacing(pose, center, out var x, out var y));
        var ray = pose.ToCamera(center).Normalized;
        Assert.InRange(Math.Abs(ArVec3.Dot(pose.DirectionToCamera(x), ray)), 0, 1e-9);
        Assert.InRange(Math.Abs(ArVec3.Dot(pose.DirectionToCamera(y), ray)), 0, 1e-9);
        Assert.InRange(Math.Abs(ArVec3.Dot(x, y)), 0, 1e-9);
        foreach (var mirror in new[] { false, true })
        foreach (var fit in new[] { PictureFit.Fit, PictureFit.Fill })
        {
            var view = new ArView(1280,720,960,540,fit,mirror);
            Assert.True(Ar3DNoteProjection.TryProjectBillboard(pose, view, center, x,y,.03,.1,out var patch));
            Assert.True(patch.Area > 1);
        }
        Assert.False(ArParticleBillboard.TryFacing(pose, new ArVec3(double.NaN,0,0), out _,out _));
    }
}
