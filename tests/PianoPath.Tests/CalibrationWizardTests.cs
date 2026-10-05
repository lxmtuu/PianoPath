using Xunit;

namespace PianoPath.Tests;

/// <summary>The calibration window's behaviour without the window: clicks, drags, arrow keys, the preview and finishing.</summary>
public class CalibrationWizardTests
{
    private const int W = 1280, H = 720;
    private static readonly (double X, double Y)[] Truth = [(70, 292), (1214, 268), (1236, 421), (52, 449)];

    private static CalibrationWizard Make(bool mirror, double width = 1000, double height = 600, KeyboardLayout? layout = null)
    {
        var wizard = new CalibrationWizard(W, H, layout ?? KeyboardLayout.Standard88);
        wizard.SetViewport(width, height, mirror);
        wizard.SetOrientation(KeyboardOrientations.Default(mirror));        // upright in the picture as delivered, like the window starts
        return wizard;
    }

    private static (double X, double Y) Screen(CalibrationWizard wizard, (double X, double Y) imagePoint) => wizard.View.ToOutput(imagePoint.X, imagePoint.Y);

    private static void Click(CalibrationWizard wizard, (double X, double Y) imagePoint)
    {
        var (x, y) = Screen(wizard, imagePoint);
        Assert.True(wizard.PointerDown(x, y));
        wizard.PointerUp();
    }

    private static void PlaceAll(CalibrationWizard wizard, (double X, double Y)[]? corners = null)
    {
        foreach (var corner in corners ?? Truth) Click(wizard, corner);
    }

    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_click_places_the_corner_where_the_picture_is_under_it_however_the_picture_is_shown(bool mirror)
    {
        var wizard = Make(mirror);
        PlaceAll(wizard);
        for (var i = 0; i < 4; i++) { Near(Truth[i].X, wizard.Points[i].X); Near(Truth[i].Y, wizard.Points[i].Y); }
    }

    [Theory]
    [InlineData(1000, 600, false)]
    [InlineData(1000, 600, true)]
    [InlineData(640, 900, false)]      // taller than the picture: bars above and below
    [InlineData(1600, 400, true)]      // wider: bars at the sides
    [InlineData(1280, 720, false)]
    public void Corners_come_back_to_the_pixels_they_were_clicked_on_in_every_box(double width, double height, bool mirror)
    {
        var wizard = Make(mirror, width, height);
        PlaceAll(wizard);
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = wizard.PointOnScreen(i);
            var back = wizard.View.ToImage(x, y);
            Near(Truth[i].X, back.X); Near(Truth[i].Y, back.Y);
        }
    }

    [Fact]
    public void Corners_are_asked_for_in_order_and_a_fifth_click_places_nothing()
    {
        var wizard = Make(false);
        var asked = new List<string?>();
        foreach (var corner in Truth) { asked.Add(wizard.NextCorner); Click(wizard, corner); }
        asked.Add(wizard.NextCorner);
        Assert.Equal(["back-left", "back-right", "front-right", "front-left", null], asked);
        var (x, y) = Screen(wizard, (600, 100));
        Assert.False(wizard.PointerDown(x, y));
        Assert.Equal(4, wizard.Points.Count);
        Assert.False(wizard.IsDragging);
    }

    [Fact]
    public void A_press_near_a_corner_picks_it_up_and_a_press_far_from_all_places_a_new_one()
    {
        var wizard = Make(false);
        Click(wizard, Truth[0]); Click(wizard, Truth[1]);
        var (x, y) = Screen(wizard, Truth[0]);
        Assert.True(wizard.PointerDown(x + CalibrationWizard.GrabRadius - 1, y));
        Assert.Equal(2, wizard.Points.Count);                       // picked, not placed
        Assert.Equal(0, wizard.Selected);
        wizard.PointerUp();
        Assert.True(wizard.PointerDown(x + CalibrationWizard.GrabRadius + 3, y + 40));
        Assert.Equal(3, wizard.Points.Count);                       // out of reach: a new corner
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dragged_corner_does_not_jump_and_follows_the_pointer_by_exactly_what_it_moves(bool mirror)
    {
        var wizard = Make(mirror);
        PlaceAll(wizard);
        var (x, y) = Screen(wizard, Truth[2]);
        Assert.True(wizard.PointerDown(x + 6, y - 5));              // grabbed a little off the corner
        Near(Truth[2].X, wizard.Points[2].X); Near(Truth[2].Y, wizard.Points[2].Y);
        Assert.True(wizard.PointerMove(x + 6 + 20, y - 5 + 8));
        var scale = wizard.View.Scale;
        Near(Truth[2].X + (mirror ? -20 : 20) / scale, wizard.Points[2].X, 1e-9);
        Near(Truth[2].Y + 8 / scale, wizard.Points[2].Y, 1e-9);
        wizard.PointerUp();
        Assert.False(wizard.PointerMove(0, 0));                      // nothing follows a released pointer
        Near(Truth[2].Y + 8 / scale, wizard.Points[2].Y, 1e-9);
    }

    [Fact]
    public void A_corner_cannot_be_dragged_further_out_than_the_picture_allows()
    {
        var wizard = Make(false);
        PlaceAll(wizard);
        var (x, y) = Screen(wizard, Truth[0]);
        wizard.PointerDown(x, y);
        wizard.PointerMove(-5000, -5000);
        Near(-CalibrationQuad.OutsideShare * W, wizard.Points[0].X, 1e-9);
        Near(-CalibrationQuad.OutsideShare * H, wizard.Points[0].Y, 1e-9);
    }

    [Theory]
    [InlineData(false, 1, 0, 1, 0)]
    [InlineData(true, 1, 0, -1, 0)]       // a mirrored picture: Right on the screen is left in the picture
    [InlineData(false, 0, 1, 0, 1)]
    [InlineData(true, 0, -1, 0, -1)]
    public void An_arrow_key_moves_the_corner_the_way_it_points_on_the_screen(bool mirror, int right, int down, double expectedDx, double expectedDy)
    {
        var wizard = Make(mirror);
        PlaceAll(wizard);
        Assert.Equal(3, wizard.Selected);
        Assert.True(wizard.Nudge(right, down, large: false));
        Near(Truth[3].X + expectedDx, wizard.Points[3].X); Near(Truth[3].Y + expectedDy, wizard.Points[3].Y);
        Assert.True(wizard.Nudge(right, down, large: true));
        Near(Truth[3].X + expectedDx * 11, wizard.Points[3].X); Near(Truth[3].Y + expectedDy * 11, wizard.Points[3].Y);
    }

    [Fact]
    public void Nothing_is_nudged_without_a_selection_or_during_a_drag()
    {
        var wizard = Make(false);
        Assert.False(wizard.Nudge(1, 0, false));
        Click(wizard, Truth[0]);
        var (x, y) = Screen(wizard, Truth[0]);
        wizard.PointerDown(x, y);
        Assert.False(wizard.Nudge(1, 0, false));
        Assert.False(wizard.SelectNext(false));
        Assert.False(wizard.Undo());
        wizard.PointerUp();
        Assert.True(wizard.Nudge(1, 0, false));
    }

    [Fact]
    public void Tab_walks_round_the_corners_both_ways_and_an_arrow_then_moves_the_one_it_landed_on()
    {
        var wizard = Make(false);
        PlaceAll(wizard);
        Assert.True(wizard.SelectNext(false)); Assert.Equal(0, wizard.Selected);
        Assert.True(wizard.SelectNext(false)); Assert.Equal(1, wizard.Selected);
        Assert.True(wizard.SelectNext(true)); Assert.Equal(0, wizard.Selected);
        Assert.True(wizard.SelectNext(true)); Assert.Equal(3, wizard.Selected);
        wizard.Nudge(1, 0, false);
        Near(Truth[3].X + 1, wizard.Points[3].X); Near(Truth[0].X, wizard.Points[0].X);
        Assert.False(Make(false).SelectNext(false));
    }

    [Fact]
    public void Backspace_takes_the_last_corner_back_and_clear_starts_again()
    {
        var wizard = Make(false);
        PlaceAll(wizard);
        Assert.True(wizard.Undo());
        Assert.Equal(3, wizard.Points.Count);
        Assert.Equal(WizardStep.PlaceCorner, wizard.Step);
        Assert.Equal("front-left", wizard.NextCorner);
        wizard.Clear();
        Assert.Empty(wizard.Points);
        Assert.False(wizard.Undo());
    }

    [Fact]
    public void The_step_follows_the_corners_placed_and_whether_they_are_usable()
    {
        var wizard = Make(false);
        Assert.Equal(WizardStep.PlaceCorner, wizard.Step);
        PlaceAll(wizard);
        Assert.Equal(WizardStep.Adjust, wizard.Step);
        Assert.True(wizard.CanFinish);
        Assert.Equal(CalibrationProblem.None, wizard.Problem);

        var flat = Make(false);                                   // four corners on one line: no key area at all
        PlaceAll(flat, [(100, 300), (400, 300), (700, 300), (1000, 300)]);
        Assert.Equal(WizardStep.Fix, flat.Step);
        Assert.False(flat.CanFinish);
        Assert.NotEqual(CalibrationProblem.None, flat.Problem);
        Assert.Empty(flat.PreviewKeys());
        Assert.Null(flat.Finish(out var problem));
        Assert.NotEqual(CalibrationProblem.None, problem);
    }

    [Fact]
    public void Dragging_a_good_outline_into_a_bad_one_shows_the_problem_at_once_and_back_again_clears_it()
    {
        var wizard = Make(false);
        PlaceAll(wizard);
        var (x, y) = Screen(wizard, Truth[0]);
        var (ox, oy) = Screen(wizard, Truth[2]);
        wizard.PointerDown(x, y);
        wizard.PointerMove(ox, oy);                                 // back-left on top of front-right
        Assert.Equal(WizardStep.Fix, wizard.Step);
        Assert.NotEqual(CalibrationProblem.None, wizard.Problem);
        wizard.PointerMove(x, y);
        Assert.Equal(WizardStep.Adjust, wizard.Step);
        Assert.Equal(CalibrationProblem.None, wizard.Problem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_preview_is_every_key_of_the_keyboard_on_the_screen_white_ones_first(bool mirror)
    {
        var wizard = Make(mirror);
        Assert.Empty(wizard.PreviewKeys());                         // nothing to show before there are four corners
        PlaceAll(wizard);
        var keys = wizard.PreviewKeys();
        Assert.Equal(88, keys.Count);
        Assert.Equal(52, keys.Count(k => !k.Black));
        Assert.True(keys.Take(52).All(k => !k.Black) && keys.Skip(52).All(k => k.Black));
        Assert.Equal(Enumerable.Range(21, 88).Order(), keys.Select(k => k.Pitch).Order());

        var calibration = ArProjectionTests.Calibrate(Truth);
        foreach (var key in keys)
        {
            var expected = calibration.KeyPolygon(key.Pitch).Select(p => wizard.View.ToOutput(p.X, p.Y)).ToArray();
            for (var i = 0; i < 4; i++) { Near(expected[i].X, key.Corners[i].X, 1e-9); Near(expected[i].Y, key.Corners[i].Y, 1e-9); }
        }
    }

    [Theory]
    [InlineData(88)]
    [InlineData(76)]
    [InlineData(61)]
    [InlineData(49)]
    public void The_preview_has_the_keys_of_the_chosen_keyboard(int keyCount)
    {
        var layout = KeyboardLayout.ForKeyCount(keyCount);
        var wizard = Make(false, layout: layout);
        PlaceAll(wizard, SyntheticKeyboardFrame.Corners[keyCount].ToArray());
        var keys = wizard.PreviewKeys();
        Assert.Equal(keyCount, keys.Count);
        Assert.Equal(layout.FirstPitch, keys.Min(k => k.Pitch));
        Assert.Equal(layout.LastPitch, keys.Max(k => k.Pitch));
    }

    [Fact]
    public void The_preview_moves_with_a_corner_being_dragged()
    {
        var wizard = Make(false);
        PlaceAll(wizard);
        var before = wizard.PreviewKeys().First(k => k.Pitch == 60).Corners[0];
        var (x, y) = Screen(wizard, Truth[0]);
        wizard.PointerDown(x, y);
        wizard.PointerMove(x + 15, y + 6);
        var after = wizard.PreviewKeys().First(k => k.Pitch == 60).Corners[0];
        Assert.True(Math.Abs(after.X - before.X) > .5 || Math.Abs(after.Y - before.Y) > .5);
    }

    [Fact]
    public void Corners_clicked_from_the_wrong_corner_or_the_wrong_way_round_still_finish_in_order()
    {
        var wizard = Make(false);
        PlaceAll(wizard, [Truth[3], Truth[2], Truth[1], Truth[0]]);        // started at the front-left, went the other way
        Assert.Equal(WizardStep.Adjust, wizard.Step);
        var keys = wizard.PreviewKeys();
        Assert.Equal(88, keys.Count);
        var calibration = wizard.Finish(out var problem);
        Assert.NotNull(calibration);
        Assert.Equal(CalibrationProblem.None, problem);
        Assert.Equal(Truth, calibration!.Corners);
        Assert.Same(KeyboardLayout.Standard88, calibration.Layout);
    }

    [Fact]
    public void Finishing_gives_a_calibration_the_session_accepts()
    {
        var wizard = Make(true);
        PlaceAll(wizard);
        var calibration = wizard.Finish(out _)!;
        Assert.Equal(W, calibration.ImageWidth);
        Assert.Equal(H, calibration.ImageHeight);
        var dir = Path.Combine(Path.GetTempPath(), "keyflow-wizard-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new ArCalibrationSession(new ArCalibrationStore(dir));
            session.Use("cam", W, H, KeyboardLayout.Standard88);
            Assert.Equal(ArSaveResult.Saved, session.Save(calibration.Corners, out _));
            Assert.Equal(ArCalibrationState.Ready, session.State);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void The_magnifier_shows_a_fixed_square_of_the_picture_centred_on_the_corner()
    {
        var wizard = Make(true);
        PlaceAll(wizard);
        for (var i = 0; i < 4; i++)
        {
            var (x, y, size) = wizard.MagnifierSource(i);
            Near(CalibrationWizard.MagnifierSize, size);
            Near(wizard.Points[i].X, x + size / 2); Near(wizard.Points[i].Y, y + size / 2);
        }
    }

    [Fact]
    public void Resizing_the_window_keeps_the_corners_in_the_picture_and_moves_them_on_the_screen()
    {
        var wizard = Make(false, 1000, 600);
        PlaceAll(wizard);
        var before = wizard.PointOnScreen(1);
        wizard.SetViewport(500, 300, false);
        var after = wizard.PointOnScreen(1);
        Near(Truth[1].X, wizard.Points[1].X); Near(Truth[1].Y, wizard.Points[1].Y);
        Near(before.X / 2, after.X, 1e-9); Near(before.Y / 2, after.Y, 1e-9);
        wizard.SetViewport(500, 300, true);                           // turning the mirror on moves nothing in the picture
        Near(Truth[1].X, wizard.Points[1].X);
        Near(500 - after.X, wizard.PointOnScreen(1).X, 1e-9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, double.NaN)]
    public void Nothing_works_without_a_usable_box_and_nothing_is_taken_from_a_pointer_that_is_not_a_number(double width, double height)
    {
        var wizard = new CalibrationWizard(W, H, KeyboardLayout.Standard88);
        wizard.SetViewport(width, height, false);
        Assert.False(wizard.PointerDown(10, 10));
        Assert.False(wizard.PointerMove(10, 10));
        Assert.Empty(wizard.Points);
        Assert.Empty(wizard.PreviewKeys());
        var normal = Make(false);
        Assert.False(normal.PointerDown(double.NaN, 5));
        Assert.False(normal.PointerDown(5, double.PositiveInfinity));
        Assert.Empty(normal.Points);
    }

    [Fact]
    public void A_press_on_the_bars_beside_the_picture_places_a_corner_just_outside_it_not_far_away()
    {
        var wizard = Make(false, 1600, 400);                          // bars left and right
        Assert.True(wizard.PointerDown(5, 200));
        Assert.True(wizard.Points[0].X < 0);
        Assert.True(wizard.Points[0].X >= -CalibrationQuad.OutsideShare * W);
    }

    [Fact]
    public void A_long_random_session_never_leaves_the_wizard_in_a_state_that_throws_or_lies()
    {
        var random = new Random(7);
        var wizard = Make(false);
        for (var step = 0; step < 4000; step++)
        {
            switch (random.Next(9))
            {
                case 0: wizard.SetViewport(random.Next(50, 2000), random.Next(50, 1200), random.Next(2) == 0); break;
                case 1: case 2: wizard.PointerDown(random.NextDouble() * 1800, random.NextDouble() * 1000); break;
                case 3: case 4: wizard.PointerMove(random.NextDouble() * 1800, random.NextDouble() * 1000); break;
                case 5: wizard.PointerUp(); break;
                case 6: wizard.Nudge(random.Next(-1, 2), random.Next(-1, 2), random.Next(2) == 0); break;
                case 7: wizard.SelectNext(random.Next(2) == 0); break;
                default: if (random.Next(4) == 0) wizard.Undo(); else if (random.Next(20) == 0) wizard.Clear(); break;
            }
            Assert.InRange(wizard.Points.Count, 0, 4);
            foreach (var p in wizard.Points)
            {
                Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
                Assert.InRange(p.X, -CalibrationQuad.OutsideShare * W - 1e-9, (1 + CalibrationQuad.OutsideShare) * W + 1e-9);
                Assert.InRange(p.Y, -CalibrationQuad.OutsideShare * H - 1e-9, (1 + CalibrationQuad.OutsideShare) * H + 1e-9);
            }
            if (wizard.Step == WizardStep.Adjust) Assert.Equal(88, wizard.PreviewKeys().Count); else Assert.Empty(wizard.PreviewKeys());
            Assert.Equal(wizard.Step == WizardStep.Adjust, wizard.CanFinish);
            Assert.Equal(wizard.Step == WizardStep.Fix, wizard.Problem != CalibrationProblem.None);
        }
    }
}
