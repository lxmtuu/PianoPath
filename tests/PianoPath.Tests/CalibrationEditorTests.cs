using Xunit;

namespace PianoPath.Tests;

/// <summary>The model behind the calibration wizard: what a click, a drag, an arrow key and Done each do.</summary>
public class CalibrationEditorTests
{
    private const int W = 1280, H = 720;

    private static (double X, double Y)[] Sample() => SyntheticKeyboardFrame.Corners[88].ToArray();

    private static CalibrationEditor Filled(params (double X, double Y)[] points)
    {
        var editor = new CalibrationEditor(W, H);
        foreach (var (x, y) in points) Assert.True(editor.Place(x, y));
        return editor;
    }

    [Fact]
    public void Four_clicks_complete_it_and_a_fifth_is_refused()
    {
        var editor = new CalibrationEditor(W, H);
        Assert.False(editor.IsComplete);
        Assert.Equal(CalibrationProblem.NotFourPoints, editor.Problem);
        var s = Sample();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(editor.Place(s[i].X, s[i].Y));
            Assert.Equal(i, editor.Selected);
            Assert.Equal(i == 3, editor.IsComplete);
        }
        Assert.False(editor.Place(10, 10));
        Assert.Equal(4, editor.Points.Count);
        Assert.Equal(CalibrationProblem.None, editor.Problem);
        Assert.True(editor.CanFinish);
    }

    [Fact]
    public void A_click_that_is_not_a_number_is_ignored()
    {
        var editor = new CalibrationEditor(W, H);
        Assert.False(editor.Place(double.NaN, 5));
        Assert.False(editor.Place(5, double.NegativeInfinity));
        Assert.Empty(editor.Points);
    }

    [Fact]
    public void A_point_dragged_far_outside_the_picture_is_held_at_the_margin()
    {
        var editor = Filled((100, 100));
        Assert.True(editor.Move(0, -5000, 5000));
        Assert.Equal((-0.25 * W, 1.25 * H), editor.Points[0]);
        Assert.True(editor.Move(0, 99999, -99999));
        Assert.Equal((1.25 * W, -0.25 * H), editor.Points[0]);
    }

    [Fact]
    public void Only_a_placed_point_can_be_moved_and_only_to_a_real_position()
    {
        var editor = Filled((100, 100), (200, 100));
        Assert.False(editor.Move(2, 5, 5));
        Assert.False(editor.Move(-1, 5, 5));
        Assert.False(editor.Move(0, double.NaN, 5));
        Assert.False(editor.Nudge(5, 1, 1));
        Assert.Equal((100.0, 100.0), editor.Points[0]);
        Assert.True(editor.Nudge(1, -1, 2));
        Assert.Equal((199.0, 102.0), editor.Points[1]);
        Assert.Equal(1, editor.Selected);
    }

    [Fact]
    public void A_press_picks_up_the_nearest_point_within_reach_and_nothing_when_none_is()
    {
        var editor = Filled((100, 100), (110, 100), (600, 400));
        Assert.Equal(0, editor.HitTest(101, 100, 8));
        Assert.Equal(1, editor.HitTest(108, 100, 8));
        Assert.Equal(1, editor.HitTest(105.5, 100, 8));     // 5.5 from the first, 4.5 from the second
        Assert.Equal(-1, editor.HitTest(300, 300, 8));
        Assert.Equal(2, editor.HitTest(600, 400, 0));       // a radius of nothing still picks a click that is dead on
        Assert.Equal(-1, new CalibrationEditor(W, H).HitTest(0, 0, 100));
    }

    [Fact]
    public void Two_points_on_one_spot_give_the_later_one_so_it_can_be_pulled_out_from_under_the_first()
    {
        var editor = Filled((300, 300), (300, 300));
        Assert.Equal(1, editor.HitTest(300, 300, 5));
    }

    [Fact]
    public void Undo_takes_points_back_one_at_a_time_and_Clear_all_of_them()
    {
        var editor = Filled((1, 1), (2, 2), (3, 3));
        Assert.True(editor.Undo());
        Assert.Equal(2, editor.Points.Count);
        Assert.Equal(1, editor.Selected);
        editor.Clear();
        Assert.Empty(editor.Points);
        Assert.Equal(-1, editor.Selected);
        Assert.False(editor.Undo());
    }

    [Fact]
    public void Dragging_a_corner_so_the_outline_folds_shows_it_while_dragging_and_dragging_back_clears_it()
    {
        var s = Sample();
        var editor = Filled(s);
        Assert.Equal(CalibrationProblem.None, editor.Problem);
        editor.Move(2, 300, 200);                                    // the front-right corner pulled across to the far left
        Assert.NotEqual(CalibrationProblem.None, editor.Problem);
        Assert.False(editor.CanFinish);
        editor.Move(2, s[2].X, s[2].Y);
        Assert.Equal(CalibrationProblem.None, editor.Problem);
        Assert.True(editor.CanFinish);
    }

    [Fact]
    public void Finish_builds_the_calibration_for_the_layout_and_leaves_good_points_as_they_were()
    {
        var s = Sample();
        var editor = Filled(s);
        var calibration = editor.Finish(KeyboardLayout.Standard88, out var problem);
        Assert.Equal(CalibrationProblem.None, problem);
        Assert.NotNull(calibration);
        Assert.Same(KeyboardLayout.Standard88, calibration.Layout);
        Assert.Equal(s, editor.Points);
        Assert.Equal(s, calibration.Corners);
    }

    [Theory]
    [InlineData(3, 2, 1, 0)]     // started at the front-left and went round the other way
    [InlineData(1, 2, 3, 0)]     // started at the back-right but went the right way
    [InlineData(2, 3, 0, 1)]
    [InlineData(3, 0, 1, 2)]
    [InlineData(1, 0, 3, 2)]     // mirrored
    [InlineData(2, 1, 0, 3)]
    public void Corners_clicked_in_another_order_are_put_in_the_keyboard_s_order_by_Done(int a, int b, int c, int d)
    {
        var s = Sample();
        var editor = Filled(s[a], s[b], s[c], s[d]);
        Assert.True(editor.CanFinish);
        var calibration = editor.Finish(KeyboardLayout.Standard88, out var problem);
        Assert.Equal(CalibrationProblem.None, problem);
        Assert.NotNull(calibration);
        Assert.Equal(s, calibration.Corners);
        Assert.Equal(s, editor.Points);
    }

    [Fact]
    public void Finish_refuses_points_that_cannot_be_used_says_why_and_changes_nothing()
    {
        var editor = Filled((100, 100), (900, 100), (300, 300), (100, 500));        // a dart
        var before = editor.Points.ToArray();
        Assert.False(editor.CanFinish);
        Assert.Null(editor.Finish(KeyboardLayout.Standard88, out var problem));
        Assert.Equal(CalibrationProblem.NotConvex, problem);
        Assert.Equal(before, editor.Points);

        var three = Filled((100, 100), (900, 100), (900, 400));
        Assert.False(three.CanFinish);
        Assert.Null(three.Finish(KeyboardLayout.Standard88, out problem));
        Assert.Equal(CalibrationProblem.NotFourPoints, problem);
    }

    [Fact]
    public void Corners_that_cannot_be_named_by_where_they_are_are_taken_as_clicked_when_they_are_fine()
    {
        // A diamond: no corner is more "top-left" than another, so there is nothing to put in order — but it is a good
        // outline, and the person clicked it in the order the wizard asked, so it stands.
        var diamond = new (double X, double Y)[] { (640, 60), (1180, 360), (640, 660), (100, 360) };
        Assert.Null(CalibrationQuad.Order(diamond));
        var editor = Filled(diamond);
        Assert.True(editor.CanFinish);
        var calibration = editor.Finish(KeyboardLayout.Standard88, out var problem);
        Assert.NotNull(calibration);
        Assert.Equal(CalibrationProblem.None, problem);
        Assert.Equal(diamond, calibration.Corners);
    }

    [Fact]
    public void A_dart_that_cannot_be_ordered_either_is_refused()
    {
        var editor = Filled((640, 60), (1180, 360), (640, 200), (100, 360));
        Assert.False(editor.CanFinish);
        Assert.Null(editor.Finish(KeyboardLayout.Standard88, out var problem));
        Assert.NotEqual(CalibrationProblem.None, problem);
    }

    [Fact]
    public void The_names_of_the_corners_are_the_four_the_wizard_asks_for_in_order()
    {
        Assert.Equal(["back-left", "back-right", "front-right", "front-left"], CalibrationEditor.CornerNames);
    }

    [Fact]
    public void A_whole_session_of_random_clicks_drags_and_keys_never_throws_and_Finish_agrees_with_Problem()
    {
        var random = new Random(77);
        for (var session = 0; session < 300; session++)
        {
            var editor = new CalibrationEditor(W, H);
            for (var step = 0; step < 60; step++)
            {
                var x = random.NextDouble() * 2 * W - W * .5; var y = random.NextDouble() * 2 * H - H * .5;
                switch (random.Next(7))
                {
                    case 0: editor.Place(x, y); break;
                    case 1: editor.Move(random.Next(-1, 5), x, y); break;
                    case 2: editor.Nudge(random.Next(-1, 5), random.Next(-3, 4), random.Next(-3, 4)); break;
                    case 3: editor.Undo(); break;
                    case 4: if (random.Next(10) == 0) editor.Clear(); break;
                    case 5: editor.HitTest(x, y, random.NextDouble() * 20); break;
                    default:
                        var before = editor.Points.ToArray();
                        var finished = editor.Finish(KeyboardLayout.Standard88, out var problem);
                        Assert.Equal(editor.CanFinish || finished is not null, finished is not null);
                        if (finished is null) { Assert.NotEqual(CalibrationProblem.None, problem); Assert.Equal(before, editor.Points); }
                        else Assert.Equal(CalibrationProblem.None, problem);
                        break;
                }
                Assert.InRange(editor.Points.Count, 0, 4);
                foreach (var p in editor.Points)
                {
                    Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
                    Assert.InRange(p.X, -0.25 * W - 1e-9, 1.25 * W + 1e-9);
                    Assert.InRange(p.Y, -0.25 * H - 1e-9, 1.25 * H + 1e-9);
                }
                if (editor.Selected >= editor.Points.Count) Assert.Fail("the selection points at a point that is not there");
            }
        }
    }
}
