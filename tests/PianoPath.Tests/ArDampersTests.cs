using System;
using Xunit;

namespace PianoPath.Tests;

public class ArDampersTests
{
    private const int W = 1280, H = 720;

    private static ArCalibration Lean45 = ArCalibration.Create(KeyboardLayout.Standard88,
        [(70, 292), (1214, 268), (1236, 421), (52, 449)], W, H, out var p45)!;

    private static ArCalibration Overhead90 = ArCalibration.Create(KeyboardLayout.Standard88,
        [(40, 150), (1240, 150), (1240, 270), (40, 270)], W, H, out var p90)!;

    private static readonly ArView View = new(W, H, W, H, PictureFit.Fit, false);

    [Fact]
    public void ClampLevel_sustains_any_machine_state_to_a_real_pedal()
    {
        Assert.Equal(0, ArDampers.ClampLevel(double.NaN));
        Assert.Equal(0, ArDampers.ClampLevel(-3));
        Assert.Equal(1, ArDampers.ClampLevel(7));
        Assert.Equal(.5, ArDampers.ClampLevel(.5));
    }

    [Fact]
    public void DepthAndAlpha_walk_with_the_pedal_and_never_leave_the_catalogue_bands()
    {
        Assert.Equal(ArDampers.BaseDepth, ArDampers.DepthFor(0));
        Assert.Equal(ArDampers.AlphaBase, ArDampers.AlphaFor(0));
        for (var lift = 0.0; lift <= 1; lift += .125)
        {
            var depth = ArDampers.DepthFor(lift);
            var alpha = ArDampers.AlphaFor(lift);
            Assert.InRange(depth, ArDampers.BaseDepth, ArDampers.BaseDepth + ArDampers.LiftDepth);
            Assert.InRange(alpha, ArDampers.AlphaBase, ArDampers.AlphaBase + ArDampers.AlphaLift);
            if (lift > 0)
            {
                Assert.True(depth > ArDampers.DepthFor(lift - .125));
                Assert.True(alpha > ArDampers.AlphaFor(lift - .125));
            }
        }
    }

    [Fact]
    public void Strip_starts_where_the_keys_start_and_lifts_behind_them()
    {
        Assert.True(ArDampers.TryPatch(Lean45, View, 0, out var pedalUp));
        Assert.True(ArDampers.TryPatch(Lean45, View, 1, out var pedalDown));
        // The lifted strip reaches further past the key back edge, so its area must grow with the pedal.
        Assert.True(pedalDown.Area > pedalUp.Area);
    }

    [Fact]
    public void Overhead_90_view_projects_the_strip_across_the_whole_keyboard()
    {
        Assert.True(ArDampers.TryPatch(Overhead90, View, .5, out var patch));
        var width = patch.BackRight.X - patch.BackLeft.X;
        var height = Math.Abs(patch.FrontLeft.Y - patch.BackLeft.Y);
        // Fronto-parallel at this calibration: 1200 px wide, .5 lift reaches .11 key depths = 13.2 px tall.
        Assert.InRange(width, 1150, 1250);
        Assert.InRange(height, 10, 18);
        foreach (var corner in new[] { patch.BackLeft, patch.BackRight, patch.FrontRight, patch.FrontLeft })
            Assert.True(corner.X >= 0 && corner.X <= W && corner.Y >= 0 && corner.Y <= H,
                $"The strip corner ({corner.X:0.#},{corner.Y:0.#}) must stay on the picture.");
    }

    [Fact]
    public void Catalogue_draws_the_strip_at_Standard_and_never_at_Lite()
    {
        Assert.False(ArElementCatalog.CanDraw(ArQualityTier.Lite, ArElement.Dampers));
        Assert.True(ArElementCatalog.CanDraw(ArQualityTier.Standard, ArElement.Dampers));
        Assert.True(ArElementCatalog.CanDraw(ArQualityTier.Ultra, ArElement.Dampers));
        Assert.Equal(ArElementFamily.Scene, ArElementCatalog.FamilyOf(ArElement.Dampers));
    }
}
