using System;
using Xunit;

namespace PianoPath.Tests;

public class ArElementHeightsTests
{
    private const int W = 1280, H = 720;

    private static readonly ArCalibration Lean45 = ArCalibration.Create(KeyboardLayout.Standard88,
        [(70, 292), (1214, 268), (1236, 421), (52, 449)], W, H, out _)!;

    private static readonly ArCalibration Overhead90 = ArCalibration.Create(KeyboardLayout.Standard88,
        [(40, 150), (1240, 150), (1240, 270), (40, 270)], W, H, out _)!;

    private static readonly ArCameraPose Pose45 = PoseOf(Lean45);
    private static readonly ArCameraPose Pose90 = PoseOf(Overhead90);
    private static readonly ArView View = new(W, H, W, H, PictureFit.Fit, false);

    private static ArCameraPose PoseOf(ArCalibration calibration) =>
        calibration.CameraPose ?? throw new InvalidOperationException("the sample corners must solve a camera pose");

    [Fact]
    public void The_table_locks_the_catalogue_numbers()
    {
        Assert.Equal(.2, ArElementHeights.ImpactGlowCentimetres);
        Assert.Equal(.3, ArElementHeights.PulseCentimetres);
    }

    [Fact]
    public void Units_convert_by_the_real_key_depth()
    {
        // One keyboard-depth unit is one key depth: 150 mm, so 15 cm.
        Assert.Equal(1d, ArElementHeights.ToKeyboardUnits(15));
        Assert.Equal(.1, ArElementHeights.ToKeyboardUnits(1.5));
        Assert.Equal(.2 / 15, ArElementHeights.ImpactGlow, 9);
        Assert.Equal(.02, ArElementHeights.PulseMinimum, 9);
    }

    [Fact]
    public void Lifted_raises_only_what_the_plane_swallows()
    {
        var raised = ArElementHeights.Lifted(new ArVec3(1, .12, 0), ArElementHeights.PulseMinimum);
        Assert.Equal(-ArElementHeights.PulseMinimum, raised.Z);
        Assert.Equal(1d, raised.X);
        Assert.Equal(.12, raised.Y);

        var shallow = ArElementHeights.Lifted(new ArVec3(1, .12, -.01), ArElementHeights.PulseMinimum);
        Assert.Equal(-ArElementHeights.PulseMinimum, shallow.Z);

        // What already flies higher keeps its own height: rising rings never get pinned back down.
        var flying = new ArVec3(1, .12, -.5);
        Assert.Equal(flying, ArElementHeights.Lifted(flying, ArElementHeights.PulseMinimum));
    }

    [Fact]
    public void Overhead_the_lift_walks_along_the_view_axis_but_lean_45_reads_it()
    {
        var ratio = Pose45.KeyboardWidthDepthRatio;
        var flat = new ArVec3(.5 * ratio, .12, 0);
        var lifted = flat with { Z = -ArElementHeights.ImpactGlow };

        Assert.True(Pose90.TryProject(flat, View, out var flat90));
        Assert.True(Pose90.TryProject(lifted, View, out var lifted90));
        // Fronto-parallel overhead: out-of-plane motion is toward the camera, so the picture must not move.
        Assert.True(Math.Abs(lifted90.Y - flat90.Y) < .3,
            $"Overhead, a 0.2 cm lift must stay sub-pixel, moved {lifted90.Y - flat90.Y:0.###} px.");

        Assert.True(Pose45.TryProject(flat, View, out var flat45));
        Assert.True(Pose45.TryProject(lifted, View, out var lifted45));
        // Leaned, the same lift climbs the picture toward the key backs — small, but the flare leaves the key fronts.
        Assert.True(flat45.Y - lifted45.Y > .4,
            $"Lean 45°, a 0.2 cm lift must read on screen, moved only {flat45.Y - lifted45.Y:0.###} px.");
        Assert.True(lifted45.Y < flat45.Y, "The flare must climb toward the key backs, never toward the player.");
    }
}
