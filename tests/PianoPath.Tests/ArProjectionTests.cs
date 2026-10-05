using Xunit;

namespace PianoPath.Tests;

/// <summary>Rectangles of the keyboard plane put onto the output through a calibration and a view.</summary>
public class ArProjectionTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];

    internal static ArCalibration Calibrate((double X, double Y)[] corners, KeyboardLayout? layout = null) =>
        ArCalibration.Create(layout ?? KeyboardLayout.Standard88, corners, 1280, 720, out var problem)
        ?? throw new InvalidOperationException($"the corners were refused: {problem}");

    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    private static void NearPoint((double X, double Y) expected, ArCorner actual, double tolerance = 1e-6)
    {
        Near(expected.X, actual.X, tolerance); Near(expected.Y, actual.Y, tolerance);
    }

    [Theory]
    [InlineData(21, false)]
    [InlineData(60, false)]
    [InlineData(61, true)]
    [InlineData(108, false)]
    [InlineData(70, true)]
    public void A_key_s_rectangle_lands_exactly_on_its_polygon_in_the_picture(int pitch, bool mirror)
    {
        var calibration = Calibrate(Sample88);
        var view = new ArView(1280, 720, 1000, 600, PictureFit.Fit, mirror);
        double left, right, depth;
        if (KeyboardLayout.IsBlack(pitch)) { (left, right) = calibration.Layout.Span(pitch); depth = ArCalibration.BlackLengthShare; }
        else { var centre = calibration.Layout.CenterX(pitch); left = centre - calibration.Layout.WhiteWidth / 2; right = centre + calibration.Layout.WhiteWidth / 2; depth = 1; }
        Assert.True(ArProjection.TryProject(calibration, view, left, right, 0, depth, out var patch, out var back, out var front));
        Assert.Equal(0, back); Assert.Equal(depth, front);
        var polygon = calibration.KeyPolygon(pitch);
        for (var i = 0; i < 4; i++) NearPoint(view.ToOutput(polygon[i].X, polygon[i].Y), patch[i]);
    }

    [Fact]
    public void The_weights_make_a_blend_of_the_corners_land_where_the_map_puts_that_point()
    {
        // for a rectangle of the plane the projective map is exactly the weighted blend of its corners: that is
        // what the GPU does across a quad, so the weights must be the map's own
        foreach (var corners in new[] { Sample88, [(400, 100), (800, 100), (1200, 600), (80, 600)], [(80, 100), (1200, 100), (800, 600), (400, 600)] })
        {
            var calibration = Calibrate(corners);
            var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
            double u0 = .3, u1 = .7, v0 = -.4, v1 = .6;
            Assert.True(ArProjection.TryProject(calibration, view, u0, u1, v0, v1, out var patch, out _, out _));
            foreach (var (s, t) in new[] { (0.0, 0.0), (1.0, 1.0), (.5, .5), (.2, .9), (.75, .1) })
            {
                double weight = 0, x = 0, y = 0;
                for (var i = 0; i < 4; i++)
                {
                    var lambda = i switch { 0 => (1 - s) * (1 - t), 1 => s * (1 - t), 2 => s * t, _ => (1 - s) * t };
                    var corner = patch[i];
                    weight += lambda * corner.W; x += lambda * corner.W * corner.X; y += lambda * corner.W * corner.Y;
                }
                var truth = calibration.ToPicture(u0 + s * (u1 - u0), v0 + t * (v1 - v0));
                Near(truth.X, x / weight, 1e-6); Near(truth.Y, y / weight, 1e-6);
            }
        }
    }

    [Fact]
    public void Behind_the_keys_a_rectangle_gets_smaller_with_distance_and_never_inverts()
    {
        var calibration = Calibrate(Sample88);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        double previous = double.MaxValue;
        for (var k = 0; k < 8; k++)
        {
            double v1 = -k * .5, v0 = v1 - .5;
            Assert.True(ArProjection.TryProject(calibration, view, .4, .45, v0, v1, out var patch, out _, out _));
            Assert.True(patch.Area > 0 && patch.Area < previous, $"strip {k}: area {patch.Area} after {previous}");
            previous = patch.Area;
        }
    }

    [Fact]
    public void A_camera_whose_horizon_lies_behind_the_keys_cuts_a_rectangle_off_at_the_horizon()
    {
        // back edge 1120 px wide, front edge 400: here the weight falls to the minimum at v = -0.5
        var calibration = Calibrate([(80, 100), (1200, 100), (800, 600), (400, 600)]);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        Assert.True(ArProjection.TryProject(calibration, view, .4, .6, -1.5, 0, out var patch, out var back, out var front));
        Near(-0.5, back, 1e-9); Assert.Equal(0, front);
        Near(ArProjection.MinW, patch.BackLeft.W, 1e-9); Near(ArProjection.MinW, patch.BackRight.W, 1e-9);
        for (var i = 0; i < 4; i++) Assert.True(patch[i].W >= ArProjection.MinW * (1 - 1e-9));
        // a rectangle wholly beyond it is not drawn, one just short of it is whole
        Assert.False(ArProjection.TryProject(calibration, view, .4, .6, -1.5, -.7, out _, out _, out _));
        Assert.True(ArProjection.TryProject(calibration, view, .4, .6, -.45, 0, out _, out var wholeBack, out _));
        Assert.Equal(-.45, wholeBack);
    }

    [Fact]
    public void A_camera_whose_horizon_lies_beyond_the_front_cuts_the_front_off()
    {
        var calibration = Calibrate([(400, 100), (800, 100), (1200, 600), (80, 600)]);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        Assert.True(ArProjection.TryProject(calibration, view, .4, .6, .5, 3, out var patch, out var back, out var front));
        Near(1.4, front, 1e-9); Assert.Equal(.5, back);
        Near(ArProjection.MinW, patch.FrontLeft.W, 1e-9);
        Assert.False(ArProjection.TryProject(calibration, view, .4, .6, 1.6, 3, out _, out _, out _));
    }

    [Fact]
    public void Rectangles_with_nothing_to_draw_are_refused()
    {
        var calibration = Calibrate(Sample88);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        Assert.False(ArProjection.TryProject(calibration, view, .5, .5, 0, 1, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, view, .6, .5, 0, 1, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, view, .1, .2, .3, .3, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, view, .1, .2, 1, 0, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, view, double.NaN, .2, 0, 1, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, view, .1, double.PositiveInfinity, 0, 1, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, view, .1, .2, 0, double.NaN, out _, out _, out _));
        Assert.False(ArProjection.TryProject(calibration, new ArView(0, 0, 100, 100, PictureFit.Fit, false), .1, .2, 0, 1, out _, out _, out _));
    }

    [Fact]
    public void Whatever_the_calibration_and_rectangle_a_drawn_patch_is_finite_in_front_of_the_horizon_and_where_it_says()
    {
        var random = new Random(4321);
        var views = new[] { new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), new ArView(1280, 720, 800, 900, PictureFit.Fill, true) };
        int calibrations = 0, drawn = 0, clipped = 0, refused = 0;
        while (calibrations < 500)
        {
            var corners = new (double, double)[4];
            for (var i = 0; i < 4; i++) corners[i] = (random.NextDouble() * 1280, random.NextDouble() * 720);
            var calibration = ArCalibration.Create(KeyboardLayout.Standard88, corners, 1280, 720, out _);
            if (calibration is null) continue;
            calibrations++;
            foreach (var view in views)
                for (var k = 0; k < 20; k++)
                {
                    var u0 = random.NextDouble() * .8; var u1 = u0 + .02 + random.NextDouble() * (.98 - u0);
                    var v0 = random.NextDouble() * 5 - 4; var v1 = v0 + .01 + random.NextDouble() * 3;
                    if (!ArProjection.TryProject(calibration, view, u0, u1, v0, v1, out var patch, out var back, out var front)) { refused++; continue; }
                    drawn++;
                    if (back > v0 || front < v1) clipped++;
                    Assert.True(back >= v0 - 1e-12 && front <= v1 + 1e-12 && back < front);
                    for (var i = 0; i < 4; i++)
                    {
                        var corner = patch[i];
                        Assert.True(double.IsFinite(corner.X) && double.IsFinite(corner.Y) && corner.W >= ArProjection.MinW * (1 - 1e-9), $"corner {i}: {corner}");
                        var (px, py) = view.ToImage(corner.X, corner.Y);
                        var (u, v) = calibration.ToKeys(px, py);
                        var wantU = i is 0 or 3 ? u0 : u1; var wantV = i < 2 ? back : front;
                        Assert.True(Math.Abs(u - wantU) <= 1e-6 * (1 + Math.Abs(wantU)) && Math.Abs(v - wantV) <= 1e-6 * (1 + Math.Abs(wantV)),
                            $"corner {i} came back as ({u}, {v}), expected ({wantU}, {wantV})");
                    }
                }
        }
        Assert.True(drawn > 5000 && clipped > 50 && refused > 50, $"the fuzz must cover drawing, clipping and refusing: {drawn} drawn, {clipped} clipped, {refused} refused");
    }
}
