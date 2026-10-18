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
    public void LiftFromPedal_keeps_the_first_quarter_dark_and_is_full_at_half_travel()
    {
        Assert.Equal(0, ArDampers.LiftFromPedal(0));
        Assert.Equal(0, ArDampers.LiftFromPedal(.25));
        Assert.Equal(.5, ArDampers.LiftFromPedal(.375), 6);
        Assert.Equal(1, ArDampers.LiftFromPedal(.5));
        Assert.Equal(1, ArDampers.LiftFromPedal(1));
        Assert.Equal(0, ArDampers.LiftFromPedal(double.NaN));
        Assert.Equal(0, ArDampers.LiftFromPedal(-2));
    }

    [Fact]
    public void Smooth_uses_the_shared_attack_and_release_times()
    {
        Assert.Equal(1 - Math.Exp(-1), ArDampers.Smooth(0, 1, ArDampers.AttackSeconds), 9);
        Assert.Equal(Math.Exp(-1), ArDampers.Smooth(1, 0, ArDampers.ReleaseSeconds), 9);
        Assert.Equal(.3, ArDampers.Smooth(.3, .3, .02), 9);
        Assert.Equal(.3, ArDampers.Smooth(.3, 1, 0), 9);           // a paused frame never moves the pedal
        Assert.Equal(0, ArDampers.Smooth(0, 1, double.NaN), 9);    // no real time, no change
    }

    [Fact]
    public void Core_stays_on_the_edge_with_the_pedal_up_and_runs_partway_back_at_full_lift()
    {
        Assert.Equal(1, ArDampers.CorePositionFor(0), 9);
        Assert.Equal(1 - .55 * .05 / .11, ArDampers.CorePositionFor(.5), 9);
        Assert.Equal(1 - .55 * .10 / .16, ArDampers.CorePositionFor(1), 9);
        Assert.Equal(.65625, ArDampers.CorePositionFor(1), 9);   // 8.18 px of travel at 45 degrees
    }

    [Fact]
    public void Cells_and_ticks_keep_the_prototype_widths_and_opacities()
    {
        var white = 1.0 / 52;
        Assert.Equal(.70 * white, ArDampers.CellWidth(white, black: false), 9);
        Assert.Equal(.46 * white, ArDampers.CellWidth(.52 * white, black: true), 9);
        Assert.Equal(.05, ArDampers.CellBodyAlpha(0), 9);
        Assert.Equal(.47, ArDampers.CellBodyAlpha(1), 9);
        Assert.Equal(.16, ArDampers.CellRimAlpha(0), 9);
        Assert.Equal(.70, ArDampers.CellRimAlpha(1), 9);
        Assert.Equal(.18, ArDampers.TickAlpha(0), 9);
        Assert.Equal(.76, ArDampers.TickAlpha(1), 9);
    }

    [Fact]
    public void A_held_key_lifts_without_the_pedal_and_the_pedal_lifts_every_key()
    {
        Assert.Equal(1, ArDampers.TickTarget(held: true, 0), 9);
        Assert.Equal(.4, ArDampers.TickTarget(held: false, .4), 9);
        Assert.Equal(1, ArDampers.TickTarget(held: true, .4), 9);
        // an out-of-range or missing lift must not push a cell past its ends
        Assert.Equal(0, ArDampers.TickTarget(held: false, double.NaN), 9);
        Assert.Equal(1, ArDampers.TickTarget(held: false, 3), 9);
    }

    [Fact]
    public void A_damper_cell_settles_in_about_a_fifth_of_a_second()
    {
        // 30 ms attack: one 60 Hz frame covers 42.6% of the way, and ten frames are all but there
        var one = ArDampers.SmoothTick(0, 1, 1.0 / 60);
        Assert.InRange(one, .40, .44);
        var ten = 0.0;
        for (var i = 0; i < 10; i++) ten = ArDampers.SmoothTick(ten, 1, 1.0 / 60);
        Assert.True(ten > .99, $"ten frames should have settled, got {ten:0.####}");
    }

    [Fact]
    public void Tick_smoothing_answers_faster_than_the_bar()
    {
        Assert.Equal(1 - Math.Exp(-1), ArDampers.SmoothTick(0, 1, ArDampers.TickAttackSeconds), 9);
        Assert.Equal(Math.Exp(-1), ArDampers.SmoothTick(1, 0, ArDampers.TickReleaseSeconds), 9);
        // the same step moves a cell further than the bar, so the comb reads as a press and not a swell
        Assert.True(ArDampers.SmoothTick(0, 1, .03) > ArDampers.Smooth(0, 1, .03));
        Assert.True(ArDampers.SmoothTick(1, 0, .04) < ArDampers.Smooth(1, 0, .04));
    }

    [Fact]
    public void Half_travel_lifts_the_strip_halfway_through_its_band()
    {
        var lift = ArDampers.LiftFromPedal(.375);
        Assert.Equal(ArDampers.BaseDepth + ArDampers.LiftDepth * .5, ArDampers.DepthFor(lift), 9);
        Assert.Equal(ArDampers.AlphaBase + ArDampers.AlphaLift * .5, ArDampers.AlphaFor(lift), 9);
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
    public void Strip_colour_modes_are_just_the_halo_family_or_a_custom_pick()
    {
        Assert.Equal(new[] { "Halo", "Custom" }, ArDampers.ColorModes);
        Assert.Equal("Halo", ArDampers.DefaultColorMode);
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
