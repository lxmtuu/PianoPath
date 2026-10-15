using System.Numerics;
using Xunit;
namespace PianoPath.Tests;
public sealed class ShatterBillboardTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Tumble_is_stage_clock_in_camera_tangent_plane_without_edge_on_collapse(int index)
    {
        var calibration=ArCalibration.Create(KeyboardLayout.Standard88,HeldParticleControlPlan.Views[index+1].Corners!,1280,720,out _,70)!;
        var pose=calibration.CameraPose!.Value;
        foreach(var values in ArParticleAlignmentTests.Matrix())
        foreach(var mirror in new[]{false,true})
        foreach(var fit in new[]{PictureFit.Fit,PictureFit.Fill})
        foreach(var age in new[]{0f,.05f,.18333334f,.45f,.89f})
        {
            var basis=ArParticleFrame.ForNotes(new((double)values[0],(double)values[1]));
            var view=new ArView(1280,720,640,360,fit,mirror);
            var center=basis.Point(new(pose.KeyboardWidthDepthRatio*.5,.12,0),.01,.08);
            Assert.True(ArParticleBillboard.TryAligned(pose,view,center,basis.Up,out var right,out var up));
            ShatterParticleAppearance.BillboardAxes(right,up,age,1.234f,out var x,out var y);
            var r=ShatterParticleAppearance.Orientation(age,1.234f);
            var angle=1.234f+age*7.3f;
            Assert.Equal(new Vector2(MathF.Cos(angle),MathF.Sin(angle)),r);
            Assert.InRange(Math.Abs(ArVec3.Dot(x,right)-r.X),0,1e-8);
            Assert.InRange(Math.Abs(ArVec3.Dot(x,up)+r.Y),0,1e-8);
            Assert.InRange(Math.Abs(ArVec3.Dot(y,right)+r.Y),0,1e-8);
            Assert.InRange(Math.Abs(ArVec3.Dot(y,up)+r.X),0,1e-8);
            var ray=pose.ToCamera(center).Normalized;
            foreach(var axis in new[]{x,y})
            {
                Assert.InRange(axis.Length,.999999,1.000001);
                Assert.InRange(Math.Abs(ArVec3.Dot(pose.DirectionToCamera(axis),ray)),0,1e-8);
            }
            Assert.InRange(Math.Abs(ArVec3.Dot(x,y)),0,1e-8);
            Assert.True(Ar3DNoteProjection.TryProjectBillboard(pose,view,center,x,y,.025*1.2,.025*.42,out var patch));
            Assert.True(patch.Area>.01 && double.IsFinite(patch.Area));
        }
    }
}
