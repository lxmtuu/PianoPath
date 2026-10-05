using Xunit;

namespace PianoPath.Tests;

/// <summary>The visible rectangles of the keys: the geometry the AR key lights are drawn on.</summary>
public class ArKeysTests
{
    public static TheoryData<int> KeyCounts => new() { 88, 76, 61, 49 };

    private static (double X, double Y)[] CornersFor(int keys) => keys switch
    {
        88 => [(70, 292), (1214, 268), (1236, 421), (52, 449)],
        76 => [(110, 288), (1180, 266), (1200, 428), (92, 452)],
        61 => [(190, 276), (1088, 258), (1106, 432), (172, 452)],
        _ => [(262, 270), (1010, 254), (1030, 436), (246, 456)],
    };

    private static List<(int Pitch, ArKeys.Rect Rect)> All(KeyboardLayout layout)
    {
        var all = new List<(int, ArKeys.Rect)>();
        var scratch = new List<ArKeys.Rect>();
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
        {
            scratch.Clear();
            ArKeys.Rects(layout, pitch, scratch);
            foreach (var rect in scratch) all.Add((pitch, rect));
        }
        return all;
    }

    [Theory]
    [MemberData(nameof(KeyCounts))]
    public void The_rectangles_of_a_keyboard_tile_its_key_area_exactly_once(int keys)
    {
        var layout = KeyboardLayout.ForKeyCount(keys);
        var all = All(layout);
        Assert.Equal(keys, all.Select(a => a.Pitch).Distinct().Count());     // every key has at least one
        var total = 0.0;
        foreach (var (_, rect) in all)
        {
            Assert.True(rect.U0 >= -1e-12 && rect.U1 <= 1 + 1e-12 && rect.V0 >= 0 && rect.V1 <= 1 + 1e-12 && rect.U1 > rect.U0 && rect.V1 > rect.V0, $"{rect} is not inside the key area");
            total += rect.Area;
        }
        Assert.Equal(1.0, total, 9);
        // and none of them overlaps another
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
            {
                var a = all[i].Rect; var b = all[j].Rect;
                var overlapU = Math.Min(a.U1, b.U1) - Math.Max(a.U0, b.U0);
                var overlapV = Math.Min(a.V1, b.V1) - Math.Max(a.V0, b.V0);
                Assert.False(overlapU > 1e-9 && overlapV > 1e-9, $"keys {all[i].Pitch} and {all[j].Pitch} overlap: {a} and {b}");
            }
    }

    [Theory]
    [MemberData(nameof(KeyCounts))]
    public void A_point_of_the_key_area_is_in_the_rectangles_of_exactly_the_key_that_picking_names(int keys)
    {
        var layout = KeyboardLayout.ForKeyCount(keys);
        var all = All(layout);
        var calibration = ArProjectionTests.Calibrate(CornersFor(keys), layout);
        var random = new Random(keys);
        for (var n = 0; n < 4000; n++)
        {
            double u = random.NextDouble(), v = random.NextDouble();
            var owners = all.Where(a => u > a.Rect.U0 && u < a.Rect.U1 && v > a.Rect.V0 && v < a.Rect.V1).Select(a => a.Pitch).ToList();
            Assert.Single(owners);
            var (x, y) = calibration.ToPicture(u, v);
            Assert.Equal(calibration.PitchAt(x, y), owners[0]);
        }
    }

    [Fact]
    public void A_white_key_leaves_out_the_black_keys_beside_it_and_a_white_key_between_two_whites_has_no_stem_cut()
    {
        var layout = KeyboardLayout.Standard88;
        var rects = new List<ArKeys.Rect>();
        // C4 (60) has C#4 on its right and no black key on its left (B3 is white)
        ArKeys.Rects(layout, 60, rects);
        Assert.Equal(2, rects.Count);
        var (slotLeft, slotRight) = layout.Span(60);
        var (blackLeft, _) = layout.Span(61);
        Assert.Equal(slotLeft, rects[1].U0, 12); Assert.Equal(blackLeft, rects[1].U1, 12);
        Assert.Equal(slotLeft, rects[0].U0, 12); Assert.Equal(slotRight, rects[0].U1, 12);
        // E4 (64): D#4 on its left, no black key on its right (F is next)
        rects.Clear(); ArKeys.Rects(layout, 64, rects);
        var (eLeft, eRight) = layout.Span(64);
        Assert.Equal(layout.Span(63).Right, rects[1].U0, 12); Assert.Equal(eRight, rects[1].U1, 12);
        // D4 (62): black keys on both sides
        rects.Clear(); ArKeys.Rects(layout, 62, rects);
        Assert.Equal(layout.Span(61).Right, rects[1].U0, 12); Assert.Equal(layout.Span(63).Left, rects[1].U1, 12);
        Assert.True(eLeft < eRight);
    }

    [Fact]
    public void A_black_key_is_one_rectangle_over_the_back_of_the_key_area_and_missing_pitches_have_none()
    {
        var layout = KeyboardLayout.Standard88;
        var rects = new List<ArKeys.Rect>();
        ArKeys.Rects(layout, 61, rects);
        var only = Assert.Single(rects);
        Assert.Equal(0, only.V0); Assert.Equal(ArCalibration.BlackLengthShare, only.V1, 12);
        Assert.Equal(layout.Span(61).Left, only.U0, 12); Assert.Equal(layout.Span(61).Right, only.U1, 12);
        rects.Clear();
        ArKeys.Rects(layout, 20, rects); ArKeys.Rects(layout, 109, rects); ArKeys.Rects(layout, -3, rects); ArKeys.Rects(layout, 400, rects);
        Assert.Empty(rects);
        // a 61-key keyboard has nothing below C2
        ArKeys.Rects(KeyboardLayout.ForKeyCount(61), 35, rects);
        Assert.Empty(rects);
    }

    [Theory]
    [MemberData(nameof(KeyCounts))]
    public void On_the_output_a_key_s_patches_meet_its_polygon_in_the_picture(int keys)
    {
        var layout = KeyboardLayout.ForKeyCount(keys);
        var calibration = ArProjectionTests.Calibrate(CornersFor(keys), layout);
        var view = new ArView(1280, 720, 1000, 600, PictureFit.Fit, false);
        var patches = new List<ArPatch>();
        for (var pitch = layout.FirstPitch; pitch <= layout.LastPitch; pitch++)
        {
            patches.Clear();
            ArKeys.Patches(calibration, view, pitch, patches);
            Assert.NotEmpty(patches);
            var polygon = calibration.KeyPolygon(pitch);
            var area = patches.Sum(p => p.Area);
            Assert.True(area > 0);
            if (KeyboardLayout.IsBlack(pitch))
            {
                for (var i = 0; i < 4; i++)
                {
                    var expected = view.ToOutput(polygon[i].X, polygon[i].Y);
                    Assert.True(Math.Abs(expected.X - patches[0][i].X) < 1e-6 && Math.Abs(expected.Y - patches[0][i].Y) < 1e-6);
                }
            }
            else
            {
                // the first patch is the front: it ends on the polygon's front edge; a stem, when there is one, comes after it
                var frontLeft = view.ToOutput(polygon[3].X, polygon[3].Y); var frontRight = view.ToOutput(polygon[2].X, polygon[2].Y);
                Assert.True(Math.Abs(frontLeft.X - patches[0].FrontLeft.X) < 1e-6 && Math.Abs(frontLeft.Y - patches[0].FrontLeft.Y) < 1e-6 && Math.Abs(frontRight.X - patches[0].FrontRight.X) < 1e-6 && Math.Abs(frontRight.Y - patches[0].FrontRight.Y) < 1e-6);
                Assert.True(patches.Count is 1 or 2);
            }
        }
    }
}
