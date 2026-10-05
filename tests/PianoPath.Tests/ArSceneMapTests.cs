using Xunit;

namespace PianoPath.Tests;

/// <summary>The stage's scene laid flat on the keyboard plane and carried into the camera picture.</summary>
public class ArSceneMapTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
    private static readonly (double X, double Y)[] BackNarrow = [(400, 100), (800, 100), (1200, 600), (80, 600)];
    private static readonly (double X, double Y)[] BackWide = [(80, 100), (1200, 100), (800, 600), (400, 600)];

    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    public static IEnumerable<object[]> Layouts()
    {
        foreach (var layout in KeyboardLayout.All)
            foreach (var mirror in new[] { false, true })
                yield return [layout.KeyCount, mirror];
    }

    private static KeyboardLayout ByKeys(int keys) => KeyboardLayout.All.First(layout => layout.KeyCount == keys);

    [Theory]
    [MemberData(nameof(Layouts))]
    public void A_key_s_centre_on_the_hit_line_lands_on_the_middle_of_its_back_edge_in_the_picture(int keys, bool mirror)
    {
        var layout = ByKeys(keys);
        var calibration = ArProjectionTests.Calibrate(Sample88, layout);
        var view = new ArView(1280, 720, 1000, 600, PictureFit.Fit, mirror);
        const double width = 1568, hitY = 700;
        var map = ArSceneMap.Create(calibration, view, width, hitY);
        Assert.True(map.Valid);
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch += 7)
        {
            var centre = layout.CenterX(pitch);
            var picture = calibration.ToPicture(centre, 0);
            var expected = view.ToOutput(picture.X, picture.Y);
            Assert.True(map.TryProject(centre * width, hitY, out var x, out var y, out var weight));
            Near(expected.X, x, 1e-6); Near(expected.Y, y, 1e-6);
            Near(calibration.ToImage[2, 0] * centre + calibration.ToImage[2, 2], weight, 1e-9);      // the calibration's own weight along the back edge
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_corners_and_weights_agree_with_the_ones_the_AR_quads_use(bool mirror)
    {
        foreach (var corners in new[] { Sample88, BackNarrow })
        {
            var calibration = ArProjectionTests.Calibrate(corners);
            var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, mirror);
            const double width = 1920, hitY = 860;
            var map = ArSceneMap.Create(calibration, view, width, hitY);
            double u0 = .3, u1 = .7, v0 = -.4, v1 = .6;
            Assert.True(ArProjection.TryProject(calibration, view, u0, u1, v0, v1, out var patch, out _, out _));
            var scene = new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };
            for (var i = 0; i < 4; i++)
            {
                Assert.True(map.TryProject(scene[i].Item1 * width, hitY + scene[i].Item2 * map.DepthPixels, out var x, out var y, out var weight));
                Near(patch[i].X, x, 1e-6); Near(patch[i].Y, y, 1e-6); Near(patch[i].W, weight, 1e-9);
            }
        }
    }

    [Theory]
    [InlineData(88)]
    [InlineData(76)]
    [InlineData(61)]
    [InlineData(49)]
    public void A_scene_pixel_is_the_same_real_length_across_the_keys_and_away_from_them(int keys)
    {
        var layout = ByKeys(keys);
        var calibration = ArProjectionTests.Calibrate(Sample88, layout);
        var map = ArSceneMap.Create(calibration, new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), 1600, 700);
        var (u0, v0) = map.ToKeys(500, 400);
        var (u1, v1) = map.ToKeys(600, 400);       // 100 scene pixels across the keys
        var (u2, v2) = map.ToKeys(500, 500);       // 100 scene pixels along them
        var acrossMm = (u1 - u0) * layout.WhiteCount * ArSceneMap.WhiteKeyMm;
        var alongMm = (v2 - v0) * ArSceneMap.KeyDepthMm;
        Near(acrossMm, alongMm, 1e-9);
        Near(0, v1 - v0, 1e-12); Near(0, u2 - u0, 1e-12);
        // and one white key's width is one lane of whites: 1/whiteCount of the scene
        Near(1.0 / layout.WhiteCount, layout.WhiteWidth, 1e-12);
    }

    [Fact]
    public void Above_the_hit_line_is_behind_the_keys_and_below_it_is_over_them()
    {
        var calibration = ArProjectionTests.Calibrate(Sample88);
        var map = ArSceneMap.Create(calibration, new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), 1568, 700);
        Assert.True(map.ToKeys(100, 700).V == 0);
        Assert.True(map.ToKeys(100, 300).V < 0);
        Assert.True(map.ToKeys(100, 760).V > 0);
        Near(1, map.ToKeys(map.SceneWidth, 700).U, 1e-12);
        // one key depth above the hit line is DepthPixels away
        Near(-1, map.ToKeys(10, 700 - map.DepthPixels).V, 1e-12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void The_scale_is_how_much_a_small_shape_grows_there(int which)
    {
        var calibration = ArProjectionTests.Calibrate(which == 0 ? Sample88 : BackNarrow);
        var map = ArSceneMap.Create(calibration, new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), 1568, 700);
        foreach (var (x, y) in new[] { (300.0, 650.0), (800.0, 400.0), (1400.0, 120.0) })
        {
            Assert.True(map.TryProject(x, y, out var ox, out var oy, out _));
            const double step = 1e-3;
            Assert.True(map.TryProject(x + step, y, out var ax, out var ay, out _));
            Assert.True(map.TryProject(x, y + step, out var bx, out var by, out _));
            var determinant = ((ax - ox) * (by - oy) - (bx - ox) * (ay - oy)) / (step * step);
            var expected = Math.Sqrt(Math.Abs(determinant));
            Near(expected, map.Scale(x, y), 1e-3 * expected + 1e-9);
            Assert.True(map.Scale(x, y) > 0);
        }
    }

    [Fact]
    public void Receding_toward_the_back_shrinks_the_picture_of_a_pixel()
    {
        // BackNarrow has its far edge narrower: a scene pixel high up the stage is smaller on the output than one on the hit line
        var calibration = ArProjectionTests.Calibrate(BackNarrow);
        var map = ArSceneMap.Create(calibration, new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), 1568, 700);
        Assert.True(map.Scale(800, 100) < map.Scale(800, 700));
        Assert.True(map.Scale(800, 0) > 0);
    }

    [Fact]
    public void A_point_on_or_beyond_the_horizon_is_refused_rather_than_thrown_across_the_screen()
    {
        var calibration = ArProjectionTests.Calibrate(BackWide);
        var map = ArSceneMap.Create(calibration, new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), 1568, 700);
        // at one key depth behind the keys the weight has gone negative, at a fifth of one it is still fine
        Assert.True(map.TryProject(784, 700 - .2 * map.DepthPixels, out _, out _, out var near));
        Assert.True(near >= ArProjection.MinW);
        Assert.False(map.TryProject(784, 700 - map.DepthPixels, out var x, out var y, out _));
        Assert.True(double.IsNaN(x) && double.IsNaN(y));
        Assert.Equal(0, map.Scale(784, 700 - map.DepthPixels));
        Assert.False(map.TryProject(double.NaN, 100, out _, out _, out _));
        Assert.False(map.TryProject(100, double.PositiveInfinity, out _, out _, out _));
    }

    [Fact]
    public void A_scene_or_a_picture_with_no_size_gives_a_map_that_draws_nothing()
    {
        var calibration = ArProjectionTests.Calibrate(Sample88);
        var good = new ArView(1280, 720, 1280, 720, PictureFit.Fit, false);
        foreach (var map in new[]
        {
            ArSceneMap.Create(calibration, good, 0, 700), ArSceneMap.Create(calibration, good, double.NaN, 700), ArSceneMap.Create(calibration, good, -5, 700),
            ArSceneMap.Create(calibration, good, 1568, double.NaN), ArSceneMap.Create(calibration, new ArView(1280, 720, 0, 0, PictureFit.Fit, false), 1568, 700), default,
        })
        {
            Assert.False(map.Valid);
            Assert.False(map.TryProject(100, 100, out _, out _, out _));
            Assert.Equal(0, map.Scale(100, 100));
        }
    }

    [Fact]
    public void The_rows_are_the_map_row_by_row_and_the_third_row_is_the_weight()
    {
        var calibration = ArProjectionTests.Calibrate(Sample88);
        var map = ArSceneMap.Create(calibration, new ArView(1280, 720, 1000, 600, PictureFit.Fit, true), 1568, 700);
        var rows = map.Rows();
        Assert.Equal(9, rows.Length);
        foreach (var (x, y) in new[] { (0.0, 0.0), (500.0, 300.0), (1568.0, 760.0) })
        {
            var weight = rows[6] * x + rows[7] * y + rows[8];
            Near(map.Weight(x, y), weight, 1e-4 * Math.Abs(weight) + 1e-6);
            Assert.True(map.TryProject(x, y, out var ox, out var oy, out _));
            Near(ox, (rows[0] * x + rows[1] * y + rows[2]) / weight, 1e-2);
            Near(oy, (rows[3] * x + rows[4] * y + rows[5]) / weight, 1e-2);
        }
    }
}
