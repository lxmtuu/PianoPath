using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Regression coverage for the legacy scene→image map helpers retained for cbuffer compatibility. This is not the
/// active AR-note path: that path uses the calibrated 3D camera pose and camera-facing quads. These tests still ensure
/// the dormant map agrees with <see cref="ArProjection"/> and cannot be mistaken for a runtime note-presence gate.
/// </summary>
public class ArSceneProjectionTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];

    /// <summary>
    /// A camera beyond the far edge of the keyboard, so the back edge is the wide one and the map's horizon lies
    /// about two key depths above the keys: a note rectangle reaching that far is cut, which the mild keyboard above
    /// never asks for. The horizon of this map is at v = -1.917, worked out from these corners.
    /// </summary>
    private static readonly (double X, double Y)[] SampleFromBeyondTheKeys = [(300, 300), (1000, 300), (880, 420), (420, 420)];

    private static ArCalibration Calibrate((double X, double Y)[] corners, KeyboardLayout? layout = null) =>
        ArCalibration.Create(layout ?? KeyboardLayout.Standard88, corners, 1280, 720, out var problem)
        ?? throw new InvalidOperationException($"the corners were refused: {problem}");

    /// <summary>The scene layout the GPU stage uses: the keyboard is the bottom of the frame, the hit line its top edge.</summary>
    private static ArSceneFrame Frame(double width = 1280, double height = 720, double keyboardFraction = .28)
    {
        var keyboard = height * keyboardFraction;
        return new ArSceneFrame(width, height - keyboard, keyboard);
    }

    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    // the picture's fit is internal, and an xUnit test method has to be public, so the case is a flag
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void The_map_puts_a_rectangle_of_the_scene_where_the_verified_projection_puts_it(bool mirror, bool fill)
    {
        var calibration = Calibrate(Sample88);
        var view = new ArView(1280, 720, 1000, 620, fill ? PictureFit.Fill : PictureFit.Fit, mirror);
        var frame = Frame();
        Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map));
        foreach (var pitch in new[] { 21, 40, 60, 61, 84, 108 })
        {
            var (left, right) = calibration.Layout.Span(pitch);
            foreach (var (v0, v1) in new[] { (0.0, 1.0), (-1.5, 0.0), (-.4, .6), (0.0, ArCalibration.BlackLengthShare) })
            {
                Assert.True(ArProjection.TryProject(calibration, view, left, right, v0, v1, out var expected, out _, out _));
                // the map's own four corners, in the patch's order: back-left, back-right, front-right, front-left
                var (bx, by) = frame.Scene(left, v0); var (fx, fy) = frame.Scene(right, v1);
                var got = new[] { map.Project(bx, by), map.Project(fx, by), map.Project(fx, fy), map.Project(bx, fy) };
                for (var i = 0; i < 4; i++) { Near(expected[i].X, got[i].X); Near(expected[i].Y, got[i].Y); Near(expected[i].W, got[i].W); }
            }
        }
    }

    [Fact]
    public void Every_keyboard_size_maps_a_key_onto_the_polygon_the_calibration_gives_it()
    {
        var frame = Frame();
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        Assert.Equal(4, KeyboardLayout.All.Length);
        foreach (var layout in KeyboardLayout.All)
        {
            var calibration = Calibrate(Sample88, layout);
            Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map));
            foreach (var pitch in new[] { layout.FirstPitch, layout.FirstPitch + 1, 60, 61, layout.FirstPitch + layout.KeyCount - 1 })
            {
                if (!layout.Contains(pitch)) continue;
                double left, right, depth;
                if (KeyboardLayout.IsBlack(pitch)) { (left, right) = layout.Span(pitch); depth = ArCalibration.BlackLengthShare; }
                else { var centre = layout.CenterX(pitch); left = centre - layout.WhiteWidth / 2; right = centre + layout.WhiteWidth / 2; depth = 1; }
                var (x0, y0) = frame.Scene(left, 0); var (x1, y1) = frame.Scene(right, depth);
                var polygon = calibration.KeyPolygon(pitch);
                var got = new[] { map.Project(x0, y0), map.Project(x1, y0), map.Project(x1, y1), map.Project(x0, y1) };
                for (var i = 0; i < 4; i++)
                {
                    var (px, py) = view.ToOutput(polygon[i].X, polygon[i].Y);
                    Near(px, got[i].X, 1e-6); Near(py, got[i].Y, 1e-6);
                }
            }
        }
    }

    [Fact]
    public void The_weight_is_one_on_the_back_left_corner_and_the_map_is_the_homography_between_the_two_affine_maps()
    {
        foreach (var mirror in new[] { false, true })
        {
            var calibration = Calibrate(Sample88);
            var view = new ArView(1280, 720, 900, 700, PictureFit.Fit, mirror);
            var frame = Frame(width: 1600, height: 900, keyboardFraction: .3);
            Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map));
            // the calibration's homography is normalised to w = 1 at (u, v) = (0, 0), and neither affine map touches w
            Near(1, map.WeightAt(0, frame.HitY), 1e-12);
            for (var ix = 0; ix <= 8; ix++)
            {
                for (var iy = 0; iy <= 8; iy++)
                {
                    var x = ix / 8.0 * frame.Width; var y = frame.HitY + (iy / 8.0 * 2 - 1) * frame.KeyboardHeight;
                    var (u, v) = frame.Plane(x, y);
                    var (px, py) = view.ToOutput(calibration.ToPicture(u, v).X, calibration.ToPicture(u, v).Y);
                    var got = map.Project(x, y);
                    Near(px, got.X, 1e-9); Near(py, got.Y, 1e-9);
                }
            }
        }
    }

    [Fact]
    public void The_scale_is_the_magnification_the_map_really_has_there()
    {
        var calibration = Calibrate(SampleFromBeyondTheKeys);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        var frame = Frame();
        Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map));
        const double d = 1e-3;
        foreach (var (x, y) in new[] { (640.0, frame.HitY), (60.0, frame.HitY), (1200.0, frame.HitY + frame.KeyboardHeight), (640.0, frame.HitY - frame.KeyboardHeight) })
        {
            var here = map.Project(x, y); var across = map.Project(x + d, y); var down = map.Project(x, y + d);
            var area = Math.Abs((across.X - here.X) * (down.Y - here.Y) - (across.Y - here.Y) * (down.X - here.X)) / (d * d);
            var expected = Math.Sqrt(area);
            var actual = map.ScaleAt(x, y);
            Assert.True(expected > 0, "the map should magnify, not flatten, where a note can be drawn");
            Assert.True(Math.Abs(actual - expected) / expected < 1e-4, $"expected a scale of {expected}, got {actual}");
        }
    }

    [Fact]
    public void A_rectangle_reaching_the_horizon_is_cut_exactly_where_the_projection_cuts_it()
    {
        var calibration = Calibrate(SampleFromBeyondTheKeys);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        var frame = Frame();
        var cut = 0;
        for (var reach = 0.0; reach <= 4; reach += .25)
        {
            var y0 = frame.HitY - reach * frame.KeyboardHeight;
            var expected = ArProjection.TryProject(calibration, view, .3, .7, -reach, 0, out var patch, out var back, out var front);
            var actual = ArSceneProjection.TryProjectRect(calibration, view, frame, .3 * frame.Width, .7 * frame.Width, y0, frame.HitY, out var got, out var yBack, out var yFront);
            Assert.Equal(expected, actual);
            if (!actual) continue;
            Near(frame.HitY + back * frame.KeyboardHeight, yBack, 1e-9);
            Near(frame.HitY + front * frame.KeyboardHeight, yFront, 1e-9);
            for (var i = 0; i < 4; i++) { Near(patch[i].X, got[i].X, 1e-9); Near(patch[i].Y, got[i].Y, 1e-9); Near(patch[i].W, got[i].W, 1e-9); }
            if (back > -reach + 1e-9) cut++;
        }
        Assert.True(cut > 0, "the slanted keyboard should cut at least one of those rectangles, or the test proves nothing");
    }

    [Fact]
    public void A_scene_a_picture_or_a_keyboard_with_no_size_carries_no_map()
    {
        var calibration = Calibrate(Sample88);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        var frame = Frame();
        Assert.False(ArSceneProjection.TryBuild(null, view, frame, out _));
        Assert.False(ArSceneProjection.TryBuild(calibration, new ArView(0, 720, 1280, 720, PictureFit.Fit, false), frame, out _));
        Assert.False(ArSceneProjection.TryBuild(calibration, new ArView(1280, 720, 0, 0, PictureFit.Fit, false), frame, out _));
        Assert.False(ArSceneProjection.TryBuild(calibration, view, new ArSceneFrame(0, 500, 200), out _));
        Assert.False(ArSceneProjection.TryBuild(calibration, view, new ArSceneFrame(1280, 500, 0), out _));
        Assert.False(ArSceneProjection.TryBuild(calibration, view, new ArSceneFrame(double.NaN, 500, 200), out _));
        Assert.False(default(ArSceneMatrix).IsValid);
        Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map) && map.IsValid);
        // a rectangle the wrong way round, or one with no calibration, projects to nothing
        Assert.False(ArSceneProjection.TryProjectRect(calibration, view, frame, 400, 300, 0, 100, out _, out _, out _));
        Assert.False(ArSceneProjection.TryProjectRect(null, view, frame, 300, 400, 0, 100, out _, out _, out _));
    }

    [Fact]
    public void A_note_cut_to_the_picture_keeps_exactly_the_part_the_flat_quad_covers()
    {
        var calibration = Calibrate(SampleFromBeyondTheKeys);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        var frame = Frame();
        Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map));
        var (x0, x1) = (.3 * frame.Width, .7 * frame.Width);
        var truncated = 0;
        for (var reach = 0.0; reach <= 4; reach += .25)
        {
            double top = frame.HitY - reach * frame.KeyboardHeight, bottom = frame.HitY;
            var drawn = ArSceneProjection.TruncateToPicture(calibration, view, frame, x0, x1, ref top, ref bottom);
            var expected = ArProjection.TryProject(calibration, view, .3, .7, -reach, 0, out _, out var back, out var front);
            Assert.Equal(expected, drawn);
            if (!drawn) continue;
            // the kept part is the flat quad's cut, scene y for scene y
            Near(frame.HitY + back * frame.KeyboardHeight, top, 1e-9);
            Near(frame.HitY + front * frame.KeyboardHeight, bottom, 1e-9);
            // and every corner of it is on the picture: the weight there is one the projection can divide by
            foreach (var y in new[] { top, bottom })
                foreach (var x in new[] { x0, x1 })
                    Assert.True(map.WeightAt(x, y) >= ArProjection.MinW - 1e-9, $"a corner at y={y} has weight {map.WeightAt(x, y)}");
            if (back > -reach + 1e-9) truncated++;
        }
        Assert.True(truncated > 0, "the slanted keyboard should cut at least one of those notes, or the test proves nothing");
    }

    [Fact]
    public void A_note_wholly_on_the_picture_is_not_changed()
    {
        var calibration = Calibrate(Sample88);
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        var frame = Frame();
        double top = frame.HitY - 40, bottom = frame.HitY + frame.KeyboardHeight * .5;
        Assert.True(ArSceneProjection.TruncateToPicture(calibration, view, frame, 300, 500, ref top, ref bottom));
        Near(frame.HitY - 40, top, 1e-9);
        Near(frame.HitY + frame.KeyboardHeight * .5, bottom, 1e-9);
        Assert.False(ArSceneProjection.TruncateToPicture(null, view, frame, 300, 500, ref top, ref bottom));
    }
}
