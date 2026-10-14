using Xunit;

namespace PianoPath.Tests;

public sealed class HeldParticleControlPlanTests
{
    [Fact]
    public void MatrixIsBoundedUniqueAndFullyCrossed()
    {
        var scenes = HeldParticleControlPlan.Scenes.ToArray();
        Assert.Equal(72, scenes.Length);
        Assert.Equal(72, scenes.Select(s => s.Id).Distinct().Count());
        Assert.Equal(288, scenes.Length * HeldParticleControlPlan.Variants.Count);
        Assert.Equal(new[] { (false,false),(false,true),(true,false),(true,true) },
            HeldParticleControlPlan.Variants.Select(v => (v.Particles, v.KeyLight)));
        Assert.Equal(new[] { 60,61,64,67 }, HeldParticleControlPlan.Patterns.Single(p => p.Id == "chord").Pitches);
        Assert.Equal(.6, HeldParticleControlPlan.Frames / (double)HeldParticleControlPlan.Fps);
    }

    [Fact]
    public void CameraAndCalibrationUseTheSamePlaneAndPhysicalPose()
    {
        foreach (var view in HeldParticleControlPlan.Views.Where(v => v.OverCamera))
        {
            var corners = view.Corners!;
            var calibration = ArCalibration.Create(KeyboardLayout.Standard88, corners, 1280,720,out _,70)!;
            Assert.NotNull(calibration);
            Assert.NotNull(calibration.CameraPose);
            var pose = calibration.CameraPose!.Value;
            if (view.Id == "ar-near-overhead") Assert.True(pose.Normal.Z > .98);
            if (view.Id == "ar-oblique") Assert.InRange(pose.Normal.Z, .5, .65);
            var toSource = Homography.FromPoints(corners, HeldParticleControlPlan.Views[1].Corners!)!.Value;
            for (var i = 0; i < 4; i++)
            {
                var p = toSource.Apply(corners[i].X, corners[i].Y);
                Assert.Equal(HeldParticleControlPlan.Views[1].Corners![i].X, p.X, 7);
                Assert.Equal(HeldParticleControlPlan.Views[1].Corners![i].Y, p.Y, 7);
            }
        }
    }

    [Fact]
    public void ReferenceCameraIsAnUnmodifiedIndependentCopyAndInvalidInputsFail()
    {
        var source = new byte[1280*720*4]; source[1000] = 241;
        var copy = HeldParticleControlPlan.CameraFor(HeldParticleControlPlan.Views[1], source);
        Assert.Equal(source, copy); Assert.NotSame(source, copy);
        Assert.Throws<ArgumentException>(() => HeldParticleControlPlan.CameraFor(HeldParticleControlPlan.Views[0], source));
        Assert.Throws<ArgumentException>(() => HeldParticleControlPlan.CameraFor(HeldParticleControlPlan.Views[1], []));
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void ReprojectionIsDeterministicOpaqueAndDoesNotMutateSource(int viewIndex)
    {
        var source = new byte[1280*720*4];
        for (var i = 0; i < source.Length; i += 4) { source[i] = 91; source[i+3] = 255; }
        var before = (byte[])source.Clone();
        var result = HeldParticleControlPlan.CameraFor(HeldParticleControlPlan.Views[viewIndex], source);
        Assert.Equal(result, HeldParticleControlPlan.CameraFor(HeldParticleControlPlan.Views[viewIndex], source));
        Assert.Equal(before, source);
        Assert.True(Enumerable.Range(0,1280*720).All(i => result[i*4+3] == 255));
    }
}
