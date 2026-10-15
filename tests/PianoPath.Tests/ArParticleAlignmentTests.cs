using Xunit;

namespace PianoPath.Tests;

public sealed class ArParticleAlignmentTests
{
    public static IEnumerable<object[]> Matrix()
    {
        foreach (var tilt in new[] {0d, 25, 70, 90, 90.000001, 145, 180})
        foreach (var azimuth in new[] {-180d, -90, -35, 0, 45, 90, 180})
            yield return new object[] {tilt, azimuth};
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Entire_particle_frame_rotates_rigidly_towards_the_OUTWARD_note_path(double tilt, double azimuth)
    {
        var note = new ArNoteDirection(tilt, azimuth); var f = ArParticleFrame.ForNotes(note);
        Assert.InRange((f.Up + note.Vector).Length, 0, 1e-9);
        foreach (var axis in new[] {f.Up, f.Across, f.Depth}) Assert.InRange(axis.Length, .999999999, 1.000000001);
        Assert.InRange(Math.Abs(ArVec3.Dot(f.Across, f.Up)), 0, 1e-9);
        Assert.InRange(Math.Abs(ArVec3.Dot(f.Depth, f.Up)), 0, 1e-9);
        Assert.InRange((ArVec3.Cross(f.Across,f.Depth)+f.Up).Length, 0, 1e-9);
        var origin = new ArVec3(4,.12,0);
        Assert.InRange((f.Point(origin,0,.4)-origin+note.Vector*.4).Length,0,1e-9);
        Assert.InRange((f.Velocity(0,-3)+note.Vector*3).Length,0,1e-9);
        Assert.InRange(f.Velocity(2,-3).Length,Math.Sqrt(13)-1e-9,Math.Sqrt(13)+1e-9);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Projected_long_axis_follows_notes_for_all_views_mirror_fit_and_directions(int poseIndex)
    {
        var corners = HeldParticleControlPlan.Views[poseIndex+1].Corners!;
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88,corners,1280,720,out _,70)!;
        var pose = calibration.CameraPose!.Value;
        foreach (var values in Matrix())
        foreach (var mirror in new[] {false,true})
        foreach (var fit in new[] {PictureFit.Fit, PictureFit.Fill})
        {
            var direction = new ArNoteDirection((double)values[0],(double)values[1]);
            var f = ArParticleFrame.ForNotes(direction); var view = new ArView(1280,720,960,600,fit,mirror);
            var center = f.Point(new(pose.KeyboardWidthDepthRatio*.5,.12,0),.01,.08);
            Assert.True(ArParticleBillboard.TryAligned(pose,view,center,f.Up,out var x,out var y));
            var ray = pose.ToCamera(center).Normalized;
            Assert.InRange(Math.Abs(ArVec3.Dot(pose.DirectionToCamera(x),ray)),0,1e-9);
            Assert.InRange(Math.Abs(ArVec3.Dot(pose.DirectionToCamera(y),ray)),0,1e-9);
            Assert.InRange(Math.Abs(ArVec3.Dot(x,y)),0,1e-9);
            Assert.True(Ar3DNoteProjection.TryProjectBillboard(pose,view,center,x,y,.025,.06,out var patch));
            Assert.True(double.IsFinite(patch.Area) && patch.Area>.01);
            Assert.True(pose.TryProjectDelta(pose.ToCamera(center),pose.DirectionToCamera(f.Up),view,out var expected));
            var length = Math.Sqrt(expected.X*expected.X+expected.Y*expected.Y);
            if (length<=1e-5) continue; // into-lens projection has no unique 2D direction; separately tested below
            Assert.True(pose.TryProjectDelta(pose.ToCamera(center),pose.DirectionToCamera(y),view,out var actual));
            var actualLength = Math.Sqrt(actual.X*actual.X+actual.Y*actual.Y);
            Assert.True(Ar3DNoteProjection.TryPatch(pose,view,center+direction.Vector*.04,direction.Vector,0,.08,.025,
                out var notePatch,out _,out _,out _));
            var noteX=notePatch.FrontLeft.X+notePatch.FrontRight.X-notePatch.BackLeft.X-notePatch.BackRight.X;
            var noteY=notePatch.FrontLeft.Y+notePatch.FrontRight.Y-notePatch.BackLeft.Y-notePatch.BackRight.Y;
            var noteLength=Math.Sqrt(noteX*noteX+noteY*noteY);
            Assert.InRange((actual.X*noteX+actual.Y*noteY)/(actualLength*noteLength),-1.000001,-.999999);
            Assert.InRange((actual.X*expected.X+actual.Y*expected.Y)/(length*actualLength),.999999,1.000001);
        }
    }

    [Fact]
    public void Ray_aligned_travel_uses_a_visible_finite_fallback_and_rejects_bad_inputs()
    {
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88,HeldParticleControlPlan.Views[1].Corners!,1280,720,out _,70)!;
        var pose = calibration.CameraPose!.Value; var view = new ArView(1280,720,1280,720,PictureFit.Fit,false);
        var center = new ArVec3(pose.KeyboardWidthDepthRatio*.5,.12,0); var ray = pose.ToCamera(center).Normalized;
        var localRay = new ArVec3(ArVec3.Dot(ray,pose.Right),ArVec3.Dot(ray,pose.Down),ArVec3.Dot(ray,pose.Normal));
        Assert.True(ArParticleBillboard.TryAligned(pose,view,center,localRay,out var x,out var y));
        Assert.True(Ar3DNoteProjection.TryProjectBillboard(pose,view,center,x,y,.02,.06,out var patch));
        Assert.True(patch.Area>1);
        Assert.False(ArParticleBillboard.TryAligned(pose,view,center,new(double.NaN,0,0),out _,out _));
        Assert.False(ArParticleBillboard.TryAligned(pose,default,center,localRay,out _,out _));
        Assert.False(ArParticleBillboard.TryAligned(pose,view,center,default,out _,out _));
    }
}
