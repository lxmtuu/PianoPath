using System.Text.Json;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The four-corner calibration: what is accepted, what is refused and why, and — on the synthetic pictures of all four
/// keyboard sizes — that the map it builds puts every key where the picture drew it.
/// </summary>
public class ArCalibrationTests
{
    private const int W = 1280, H = 720;

    public static TheoryData<int> Sizes => new() { 88, 76, 61, 49 };

    private static (double X, double Y)[] Sample(int keys = 88) => SyntheticKeyboardFrame.Corners[keys].ToArray();

    private static ArCalibration Make(int keys)
    {
        var made = ArCalibration.Create(KeyboardLayout.ForKeyCount(keys), Sample(keys), W, H, out var problem);
        Assert.Equal(CalibrationProblem.None, problem);
        return made!;
    }

    private static bool InsidePolygon((double X, double Y)[] polygon, (double X, double Y) point)
    {
        int positive = 0, negative = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (cross > 0) positive++; else if (cross < 0) negative++;
        }
        return positive == 0 || negative == 0;
    }

    // ---- What is accepted ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Sizes))]
    public void The_corners_of_every_synthetic_picture_are_accepted(int keys)
    {
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.Check(Sample(keys), W, H));
    }

    [Fact]
    public void The_same_keyboard_in_a_picture_of_another_size_is_still_accepted_when_the_corners_scale_with_it()
    {
        var scaled = Sample().Select(p => (p.X * 1.5, p.Y * 1.5)).ToArray();
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.Check(scaled, 1920, 1080));
    }

    [Fact]
    public void A_keyboard_that_runs_a_little_off_the_frame_is_accepted_and_one_far_off_is_not()
    {
        var corners = new (double X, double Y)[] { (-100, 292), (1214, 268), (1236, 421), (-120, 449) };
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.Check(corners, W, H));
        corners[0] = (-400, 292);
        Assert.Equal(CalibrationProblem.OutsideImage, CalibrationQuad.Check(corners, W, H));
        Assert.Equal(CalibrationProblem.OutsideImage, CalibrationQuad.Check(new (double, double)[] { (70, 292), (1214, 268), (1236, 421), (52, 5000) }, W, H));
    }

    // ---- What is refused, and why ----------------------------------------------------------------------

    [Fact]
    public void Anything_but_four_numbers_in_a_picture_with_a_size_is_refused()
    {
        var four = Sample();
        Assert.Equal(CalibrationProblem.NotFourPoints, CalibrationQuad.Check(four.Take(3).ToArray(), W, H));
        Assert.Equal(CalibrationProblem.NotFourPoints, CalibrationQuad.Check(four.Append((1, 1)).ToArray(), W, H));
        Assert.Equal(CalibrationProblem.NotFourPoints, CalibrationQuad.Check([], W, H));
        var broken = Sample(); broken[2] = (double.NaN, 421);
        Assert.Equal(CalibrationProblem.NotFinite, CalibrationQuad.Check(broken, W, H));
        broken[2] = (1236, double.PositiveInfinity);
        Assert.Equal(CalibrationProblem.NotFinite, CalibrationQuad.Check(broken, W, H));
        Assert.Equal(CalibrationProblem.NotFinite, CalibrationQuad.Check(four, 0, H));
        Assert.Equal(CalibrationProblem.NotFinite, CalibrationQuad.Check(four, W, -1));
    }

    [Fact]
    public void Corners_that_go_round_the_wrong_way_are_called_that_and_not_something_else()
    {
        var s = Sample();
        Assert.Equal(CalibrationProblem.WrongOrder, CalibrationQuad.Check([s[3], s[2], s[1], s[0]], W, H));   // turned over
        Assert.Equal(CalibrationProblem.WrongOrder, CalibrationQuad.Check([s[1], s[0], s[3], s[2]], W, H));   // mirrored left to right
    }

    [Fact]
    public void An_outline_that_crosses_itself_or_folds_in_is_not_convex()
    {
        var s = Sample();
        Assert.Equal(CalibrationProblem.NotConvex, CalibrationQuad.Check([s[0], s[1], s[3], s[2]], W, H));   // a bow tie
        Assert.Equal(CalibrationProblem.NotConvex, CalibrationQuad.Check(new (double, double)[] { (100, 100), (900, 100), (300, 300), (100, 500) }, W, H));
    }

    [Fact]
    public void Three_points_on_a_line_or_a_repeated_point_is_degenerate()
    {
        Assert.Equal(CalibrationProblem.Degenerate, CalibrationQuad.Check(new (double, double)[] { (100, 100), (500, 100), (900, 100), (100, 300) }, W, H));
        Assert.Equal(CalibrationProblem.Degenerate, CalibrationQuad.Check(new (double, double)[] { (100, 100), (900, 100), (900, 100), (100, 300) }, W, H));
        Assert.Equal(CalibrationProblem.Degenerate, CalibrationQuad.Check(new (double, double)[] { (5, 5), (5, 5), (5, 5), (5, 5) }, W, H));
    }

    [Fact]
    public void A_sliver_of_the_picture_or_a_keyboard_with_no_depth_is_refused()
    {
        Assert.Equal(CalibrationProblem.TooSmall, CalibrationQuad.Check(new (double, double)[] { (600, 300), (700, 300), (700, 330), (600, 330) }, W, H));
        Assert.Equal(CalibrationProblem.TooThin, CalibrationQuad.Check(new (double, double)[] { (100, 300), (1100, 300), (1100, 310), (100, 310) }, W, H));
    }

    [Fact]
    public void A_trapezoid_whose_ends_differ_by_many_times_is_too_skewed_to_be_a_keyboard()
    {
        Assert.Equal(CalibrationProblem.TooSkewed, CalibrationQuad.Check(new (double, double)[] { (100, 100), (1100, 100), (700, 300), (600, 300) }, W, H));
        // A strong but real perspective (back 3 times the front) is fine.
        Assert.Equal(CalibrationProblem.None, CalibrationQuad.Check(new (double, double)[] { (100, 100), (1100, 100), (700, 400), (500, 400) }, W, H));
    }

    // ---- Putting the points in order -------------------------------------------------------------------

    [Fact]
    public void Every_order_the_four_corners_can_be_given_in_comes_back_in_the_keyboard_s_order()
    {
        var s = Sample();
        var count = 0;
        foreach (var permutation in Permutations(new[] { 0, 1, 2, 3 }))
        {
            var ordered = CalibrationQuad.Order(permutation.Select(i => s[i]).ToArray());
            Assert.NotNull(ordered);
            Assert.Equal(s, ordered);
            Assert.Equal(CalibrationProblem.None, CalibrationQuad.Check(ordered, W, H));
            count++;
        }
        Assert.Equal(24, count);
    }

    [Fact]
    public void Points_that_do_not_name_four_different_corners_cannot_be_ordered()
    {
        Assert.Null(CalibrationQuad.Order(new (double, double)[] { (500, 100), (900, 300), (500, 500), (100, 300) }));   // a diamond: two corners tie
        Assert.Null(CalibrationQuad.Order(new (double, double)[] { (5, 5), (5, 5), (5, 5), (5, 5) }));
        Assert.Null(CalibrationQuad.Order(Sample().Take(3).ToArray()));
        var bad = Sample(); bad[1] = (double.NaN, 1);
        Assert.Null(CalibrationQuad.Order(bad));
    }

    [Fact]
    public void The_area_is_positive_for_the_keyboard_s_order_and_negative_for_the_reverse()
    {
        var square = new (double, double)[] { (0, 0), (1, 0), (1, 1), (0, 1) };
        Assert.Equal(1, CalibrationQuad.Area(square), 12);
        Assert.Equal(-1, CalibrationQuad.Area(square.Reverse().ToArray()), 12);
    }

    // ---- The map the corners define --------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Every_key_is_found_again_under_its_own_top_centre_black_keys_and_white(int keys)
    {
        var calibration = Make(keys);
        for (var pitch = calibration.Layout.FirstPitch; pitch <= calibration.Layout.LastPitch; pitch++)
        {
            var (x, y) = calibration.KeyTopCentre(pitch);
            Assert.True(calibration.PitchAt(x, y) == pitch, $"key {pitch} of {keys}: the pixel ({x:F1}, {y:F1}) is {calibration.PitchAt(x, y)?.ToString() ?? "outside"}");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void The_key_tops_are_where_the_synthetic_picture_drew_them_to_half_a_pixel(int keys)
    {
        var calibration = Make(keys);
        var frame = SyntheticKeyboardFrame.Render(keys);
        for (var pitch = calibration.Layout.FirstPitch; pitch <= calibration.Layout.LastPitch; pitch++)
        {
            var (x, y) = calibration.KeyTopCentre(pitch);
            var (ex, ey) = frame.KeyTopCentre(pitch);
            Assert.True(Math.Abs(x - ex) < 0.5 && Math.Abs(y - ey) < 0.5, $"key {pitch} of {keys}: ({x:F2}, {y:F2}) against ({ex:F2}, {ey:F2})");
            var (r, g, b) = frame.PixelAt((int)x, (int)y);
            var brightness = (r + g + b) / 3.0;
            if (KeyboardLayout.IsBlack(pitch)) Assert.True(brightness < 90, $"black key {pitch} of {keys} reads {brightness:F0} in the picture");
            else Assert.True(brightness > 120, $"white key {pitch} of {keys} reads {brightness:F0} in the picture");
        }
    }

    [Fact]
    public void The_full_keyboard_matches_the_key_centres_in_the_Python_ground_truth_to_half_a_pixel()
    {
        var truth = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ar-keyboard-topdown.json"))).RootElement;
        var calibration = Make(88);
        var count = 0;
        foreach (var entry in truth.GetProperty("keyTopCentres").EnumerateObject())
        {
            var (x, y) = calibration.KeyTopCentre(int.Parse(entry.Name));
            Assert.True(Math.Abs(x - entry.Value[0].GetDouble()) < 0.5 && Math.Abs(y - entry.Value[1].GetDouble()) < 0.5, $"key {entry.Name}");
            count++;
        }
        Assert.Equal(88, count);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void The_white_keys_tile_the_key_area_with_no_gap_and_no_overlap(int keys)
    {
        var calibration = Make(keys);
        var whiteArea = 0.0;
        foreach (var pitch in calibration.Layout.WhitePitches) whiteArea += CalibrationQuad.Area(calibration.KeyPolygon(pitch));
        var quad = CalibrationQuad.Area(calibration.Corners);
        Assert.True(Math.Abs(whiteArea - quad) < 1e-9 * quad);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Every_key_polygon_is_a_clockwise_quadrilateral_that_holds_its_top_centre_and_stays_in_the_key_area(int keys)
    {
        var calibration = Make(keys);
        for (var pitch = calibration.Layout.FirstPitch; pitch <= calibration.Layout.LastPitch; pitch++)
        {
            var polygon = calibration.KeyPolygon(pitch);
            Assert.Equal(4, polygon.Length);
            Assert.True(CalibrationQuad.Area(polygon) > 0, $"key {pitch} runs the wrong way");
            Assert.True(InsidePolygon(polygon, calibration.KeyTopCentre(pitch)), $"key {pitch}: its top centre is outside its polygon");
            foreach (var corner in polygon)
            {
                var (u, v) = calibration.ToKeys(corner.X, corner.Y);
                Assert.True(u >= -1e-9 && u <= 1 + 1e-9 && v >= -1e-9 && v <= 1 + 1e-9, $"key {pitch} reaches outside the key area at ({u:F4}, {v:F4})");
            }
        }
    }

    [Fact]
    public void A_black_key_covers_the_back_of_the_keys_and_a_white_key_all_of_them()
    {
        var calibration = Make(88);
        var black = calibration.KeyPolygon(22);   // A#0
        var white = calibration.KeyPolygon(21);   // A0
        var (_, blackV) = calibration.ToKeys(black[2].X, black[2].Y);
        var (_, whiteV) = calibration.ToKeys(white[2].X, white[2].Y);
        Assert.Equal(ArCalibration.BlackLengthShare, blackV, 9);
        Assert.Equal(1, whiteV, 9);
        Assert.Equal(95.0 / 150.0, ArCalibration.BlackLengthShare, 12);
    }

    [Fact]
    public void A_pixel_outside_the_key_area_has_no_key_and_a_pixel_that_is_not_a_number_has_none_either()
    {
        var calibration = Make(88);
        Assert.Null(calibration.PitchAt(10, 10));
        Assert.Null(calibration.PitchAt(640, 100));                   // above the back of the keys
        Assert.Null(calibration.PitchAt(640, 600));                   // below the front
        Assert.Null(calibration.PitchAt(20, 360));                    // left of the lowest key
        Assert.Null(calibration.PitchAt(double.NaN, 300));
        Assert.Null(calibration.PitchAt(300, double.PositiveInfinity));
        Assert.False(calibration.Contains(10, 10));
        Assert.True(calibration.Contains(640, 350));
    }

    [Fact]
    public void Across_the_black_key_row_a_black_key_wins_over_the_white_one_behind_it_and_below_it_the_white_key_does()
    {
        var calibration = Make(88);
        var black = 22;                                               // A#0, between A0 and B0
        var u = calibration.Layout.CenterX(black);
        Assert.Equal(black, calibration.PitchAt(calibration.ToPicture(u, 0.3).X, calibration.ToPicture(u, 0.3).Y));
        Assert.Equal(21, calibration.PitchAt(calibration.ToPicture(u - calibration.Layout.WhiteWidth * 0.45, 0.3).X, calibration.ToPicture(u - calibration.Layout.WhiteWidth * 0.45, 0.3).Y));
        var below = calibration.ToPicture(u, 0.9);
        Assert.Equal(23, calibration.PitchAt(below.X, below.Y));      // below the black key's end, at its middle: the white key it stands on the boundary of, B0
    }

    [Fact]
    public void A_pixel_and_its_keyboard_coordinates_agree_both_ways_across_the_whole_key_area()
    {
        var calibration = Make(88);
        var random = new Random(20261005);
        for (var i = 0; i < 5000; i++)
        {
            double u = random.NextDouble(), v = random.NextDouble();
            var (x, y) = calibration.ToPicture(u, v);
            var (backU, backV) = calibration.ToKeys(x, y);
            Assert.True(Math.Abs(backU - u) < 1e-9 && Math.Abs(backV - v) < 1e-9);
            Assert.True(calibration.Contains(x, y) || u < 1e-9 || v < 1e-9 || u > 1 - 1e-9 || v > 1 - 1e-9);
            if (calibration.PitchAt(x, y) is { } pitch)
                Assert.Equal(calibration.Layout.PitchAt(u, v < ArCalibration.BlackLengthShare), pitch);
        }
    }

    // ---- Clicking is not exact -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Corners_clicked_half_a_pixel_off_still_find_every_key(int keys)
    {
        var layout = KeyboardLayout.ForKeyCount(keys);
        for (var seed = 1; seed <= 12; seed++)
        {
            var random = new Random(seed * 31 + keys);
            var clicked = Sample(keys).Select(p => (p.X + (random.NextDouble() - .5), p.Y + (random.NextDouble() - .5))).ToArray();
            var calibration = ArCalibration.Create(layout, clicked, W, H, out var problem);
            Assert.NotNull(calibration);
            Assert.Equal(CalibrationProblem.None, problem);
            var truth = Make(keys);
            for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
            {
                var (x, y) = truth.KeyTopCentre(pitch);
                Assert.True(calibration!.PitchAt(x, y) == pitch, $"seed {seed}, key {pitch} of {keys}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void A_corner_clicked_one_pixel_off_moves_no_key_by_more_than_two_and_a_half_pixels(int keys)
    {
        // Measured on the synthetic pictures: the worst case is 1.98 px for 88 keys. An error at a corner shrinks toward
        // the middle of the keys but is a little larger at the far end, so the bound is not 1 px, and it is not unbounded.
        var layout = KeyboardLayout.ForKeyCount(keys);
        var good = Make(keys);
        for (var corner = 0; corner < 4; corner++)
            for (var step = 0; step < 16; step++)
            {
                var angle = 2 * Math.PI * step / 16;
                var off = Sample(keys);
                off[corner] = (off[corner].X + Math.Cos(angle), off[corner].Y + Math.Sin(angle));
                var moved = ArCalibration.Create(layout, off, W, H, out _)!;
                for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
                {
                    var a = good.KeyTopCentre(pitch); var b = moved.KeyTopCentre(pitch);
                    Assert.True(Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) <= 2.5, $"key {pitch} of {keys}, corner {corner}, step {step}");
                }
            }
    }

    // ---- Stability: nothing a click can do may throw or give a number that is not one ---------------------

    [Fact]
    public void Any_four_points_at_all_are_judged_without_a_throw_and_whatever_is_accepted_works_through_and_through()
    {
        var random = new Random(1234);
        var accepted = 0;
        for (var round = 0; round < 30000; round++)
        {
            var corners = new (double, double)[4];
            if (round % 2 == 0)
                for (var i = 0; i < 4; i++) corners[i] = (random.NextDouble() * 2 * W - W * .5, random.NextDouble() * 2 * H - H * .5);   // anywhere, even beyond the picture
            else
            {
                var s = Sample();
                for (var i = 0; i < 4; i++) corners[i] = (s[i].X + (random.NextDouble() - .5) * 400, s[i].Y + (random.NextDouble() - .5) * 250);   // a shaky hand
            }
            var calibration = ArCalibration.Create(KeyboardLayout.Standard88, corners, W, H, out var problem);
            Assert.Equal(CalibrationQuad.Check(corners, W, H), problem);
            if (calibration is null) { Assert.NotEqual(CalibrationProblem.None, problem); continue; }
            accepted++;
            for (var pitch = 21; pitch <= 108; pitch++)
            {
                var (x, y) = calibration.KeyTopCentre(pitch);
                Assert.True(double.IsFinite(x) && double.IsFinite(y), $"round {round}, key {pitch}: ({x}, {y})");
                Assert.True(calibration.PitchAt(x, y) == pitch, $"round {round}, key {pitch} resolves to {calibration.PitchAt(x, y)}");
                foreach (var corner in calibration.KeyPolygon(pitch)) Assert.True(double.IsFinite(corner.X) && double.IsFinite(corner.Y));
            }
        }
        Assert.True(accepted > 2000, $"only {accepted} of 30000 random quadrilaterals were accepted, so the test is not exercising the accepted path");
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, index) => index != i).ToArray();
            foreach (var tail in Permutations(rest)) yield return new[] { items[i] }.Concat(tail).ToArray();
        }
    }
}
