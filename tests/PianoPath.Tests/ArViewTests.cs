using Xunit;

namespace PianoPath.Tests;

/// <summary>Where the camera picture lands on the output, and the maps between the two.</summary>
public class ArViewTests
{
    private static void Near(double expected, double actual, double tolerance = 1e-9) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void A_picture_of_the_output_s_own_shape_covers_it_exactly_in_both_fits()
    {
        foreach (var fit in new[] { PictureFit.Fit, PictureFit.Fill })
        {
            var view = new ArView(1280, 720, 1920, 1080, fit, false);
            Assert.True(view.Valid);
            Near(0, view.Left); Near(0, view.Top); Near(1920, view.Width); Near(1080, view.Height);
            Near(1.5, view.Scale);
            Near(1, view.VisibleShare);
        }
    }

    [Fact]
    public void Fit_leaves_bars_and_keeps_the_whole_picture()
    {
        var view = new ArView(1280, 720, 1000, 1000, PictureFit.Fit, false);
        Near(0, view.Left); Near(1000, view.Width);
        Near(562.5, view.Height); Near(218.75, view.Top);
        Near(1, view.VisibleShare);
        Assert.False(view.CoversOutputPoint(500, 100));
        Assert.True(view.CoversOutputPoint(500, 500));
        // the picture's own corners land on the corners of the covered area
        var (x0, y0) = view.ToOutput(0, 0);
        var (x1, y1) = view.ToOutput(1280, 720);
        Near(0, x0); Near(218.75, y0); Near(1000, x1); Near(781.25, y1);
    }

    [Fact]
    public void Fill_crops_the_picture_and_reports_how_much_stays_in_view()
    {
        var view = new ArView(1280, 720, 1000, 1000, PictureFit.Fill, false);
        Near(1000, view.Height);
        Near(1000 * 1280.0 / 720, view.Width, 1e-6);
        Near((1000 - view.Width) / 2, view.Left, 1e-6);
        Near(1000 / view.Width, view.VisibleShare, 1e-9);
        Assert.True(view.VisibleShare < 1);
        Assert.True(view.CoversOutputPoint(0, 0) && view.CoversOutputPoint(1000, 1000));
        // a picture pixel at the far left is off the output
        Assert.True(view.ToOutput(0, 360).X < 0);
    }

    [Fact]
    public void Mirror_flips_the_picture_left_to_right_on_the_output_only()
    {
        var plain = new ArView(1280, 720, 1920, 1080, PictureFit.Fit, false);
        var mirrored = new ArView(1280, 720, 1920, 1080, PictureFit.Fit, true);
        var (px, py) = plain.ToOutput(100, 200);
        var (mx, my) = mirrored.ToOutput(100, 200);
        Near(1920 - px, mx); Near(py, my);
        var (ix, iy) = mirrored.ToImage(mx, my);
        Near(100, ix, 1e-9); Near(200, iy, 1e-9);
        // the centre does not move
        Near(960, mirrored.ToOutput(640, 360).X);
    }

    [Theory]
    [InlineData(1280, 720, 1920, 1080, PictureFit.Fit, false)]
    [InlineData(1280, 720, 800, 1000, PictureFit.Fit, true)]
    [InlineData(1920, 1080, 1000, 1000, PictureFit.Fill, false)]
    [InlineData(640, 480, 1537, 861, PictureFit.Fill, true)]
    [InlineData(1280, 720, 333.3, 777.7, PictureFit.Fit, true)]
    public void A_point_goes_to_the_output_and_comes_back_to_where_it_was(int w, int h, double ow, double oh, PictureFit fit, bool mirror)
    {
        var view = new ArView(w, h, ow, oh, fit, mirror);
        var random = new Random(77);
        for (var i = 0; i < 2000; i++)
        {
            var x = random.NextDouble() * w * 1.4 - w * .2;
            var y = random.NextDouble() * h * 1.4 - h * .2;
            var (ox, oy) = view.ToOutput(x, y);
            var (bx, by) = view.ToImage(ox, oy);
            Near(x, bx, 1e-7); Near(y, by, 1e-7);
        }
    }

    [Fact]
    public void Scale_is_the_same_in_both_directions_so_the_picture_is_never_stretched()
    {
        foreach (var (ow, oh) in new[] { (1920.0, 1080.0), (1000.0, 1000.0), (640.0, 1200.0), (333.0, 91.0) })
            foreach (var fit in new[] { PictureFit.Fit, PictureFit.Fill })
            {
                var view = new ArView(1280, 720, ow, oh, fit, false);
                Near(view.Width / 1280, view.Height / 720, 1e-9);
                Near(view.Scale, view.Width / 1280);
                // one picture pixel to the right and one down move the output by the same amount
                var (ax, ay) = view.ToOutput(100, 100);
                var (bx, by) = view.ToOutput(101, 101);
                Near(bx - ax, by - ay, 1e-9);
            }
    }

    [Theory]
    [InlineData(0, 720, 100, 100)]
    [InlineData(1280, 0, 100, 100)]
    [InlineData(1280, 720, 0, 100)]
    [InlineData(1280, 720, 100, -5)]
    [InlineData(1280, 720, double.NaN, 100)]
    [InlineData(1280, 720, double.PositiveInfinity, 100)]
    public void A_picture_or_output_without_a_size_is_not_valid_and_changes_nothing(int w, int h, double ow, double oh)
    {
        var view = new ArView(w, h, ow, oh, PictureFit.Fit, true);
        Assert.False(view.Valid);
        Assert.Equal((12.5, 7.0), view.ToOutput(12.5, 7.0));
        Assert.Equal((12.5, 7.0), view.ToImage(12.5, 7.0));
        Assert.False(view.CoversOutputPoint(1, 1));
        Assert.Equal(0, view.VisibleShare);
        Assert.False(view.ContainsQuad([(1, 1), (2, 1), (2, 2), (1, 2)]));
        Assert.Equal(1, view.Scale);
    }

    [Fact]
    public void The_keyboard_is_in_view_when_all_four_corners_are_on_the_output()
    {
        var sample = new (double X, double Y)[] { (70, 292), (1214, 268), (1236, 421), (52, 449) };
        Assert.True(new ArView(1280, 720, 1920, 1080, PictureFit.Fit, false).ContainsQuad(sample));
        Assert.True(new ArView(1280, 720, 1920, 1080, PictureFit.Fit, true).ContainsQuad(sample));
        // a narrow stage that crops the picture's sides cuts the keyboard off
        Assert.False(new ArView(1280, 720, 600, 720, PictureFit.Fill, false).ContainsQuad(sample));
        // with bars, the same keyboard stays in view
        Assert.True(new ArView(1280, 720, 600, 720, PictureFit.Fit, false).ContainsQuad(sample));
        Assert.False(new ArView(1280, 720, 1920, 1080, PictureFit.Fit, false).ContainsQuad([]));
    }
}
