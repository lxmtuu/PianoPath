using System.Text.Json;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The 3×3 map between keyboard coordinates and camera pixels. Checked against the picture and ground truth that
/// <c>tools/make_ar_sample.py</c> wrote (<c>docs/samples/ar-keyboard-topdown.json</c>), which were computed by
/// separate code, so a mistake in either side shows as a disagreement.
/// </summary>
public class HomographyTests
{
    private static readonly (double X, double Y)[] UnitSquare = [(0, 0), (1, 0), (1, 1), (0, 1)];

    private static (double X, double Y)[] Corners88 => SyntheticKeyboardFrame.Corners[88];

    private static JsonElement GroundTruth() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ar-keyboard-topdown.json"))).RootElement;

    [Fact]
    public void The_four_points_it_was_built_from_land_exactly_on_their_targets()
    {
        var map = Homography.FromPoints(UnitSquare, Corners88);
        Assert.NotNull(map);
        for (var k = 0; k < 4; k++)
        {
            var (x, y) = map.Value.Apply(UnitSquare[k].X, UnitSquare[k].Y);
            Assert.Equal(Corners88[k].X, x, 8);
            Assert.Equal(Corners88[k].Y, y, 8);
        }
    }

    [Fact]
    public void Identity_and_the_map_from_a_square_to_itself_move_nothing()
    {
        var same = Homography.FromPoints(UnitSquare, UnitSquare);
        Assert.NotNull(same);
        foreach (var map in new[] { Homography.Identity, same.Value })
        {
            var (x, y) = map.Apply(0.3, 0.7);
            Assert.Equal(0.3, x, 10);
            Assert.Equal(0.7, y, 10);
        }
    }

    [Fact]
    public void The_inverse_undoes_the_map_across_the_whole_key_area_and_beyond_it()
    {
        var map = Homography.FromPoints(UnitSquare, Corners88)!.Value;
        var inverse = map.Inverse();
        Assert.NotNull(inverse);
        for (var u = -0.2; u <= 1.2; u += 0.1)
            for (var v = -0.5; v <= 1.5; v += 0.25)
            {
                var (x, y) = map.Apply(u, v);
                var (backU, backV) = inverse.Value.Apply(x, y);
                Assert.Equal(u, backU, 9);
                Assert.Equal(v, backV, 9);
            }
    }

    [Fact]
    public void A_map_applied_after_another_is_the_two_applied_in_turn()
    {
        var first = Homography.FromPoints(UnitSquare, Corners88)!.Value;
        var second = Homography.FromPoints(UnitSquare, SyntheticKeyboardFrame.Corners[49])!.Value;
        var together = second.After(first);
        var (x, y) = together.Apply(0.37, 0.61);
        var (ex, ey) = second.Apply(first.Apply(0.37, 0.61).X, first.Apply(0.37, 0.61).Y);
        Assert.Equal(ex, x, 8);
        Assert.Equal(ey, y, 8);
    }

    [Fact]
    public void Corners_that_do_not_make_a_four_sided_area_give_no_map()
    {
        // Three on a line, two on top of each other, and the wrong number of points.
        Assert.Null(Homography.FromPoints(UnitSquare, [(0, 0), (1, 1), (2, 2), (0, 5)]));
        Assert.Null(Homography.FromPoints(UnitSquare, [(0, 0), (0, 0), (4, 4), (0, 4)]));
        Assert.Null(Homography.FromPoints(UnitSquare, [(0, 0), (1, 0), (1, 1)]));
        Assert.Null(Homography.FromPoints([(0, 0), (1, 0), (2, 0), (0, 1)], Corners88));
    }

    [Fact]
    public void A_map_that_flattens_the_plane_has_no_inverse()
    {
        Assert.Null(new Homography(1, 2, 3, 2, 4, 6, 0, 0, 1).Inverse()); // every row of the 2×2 part is a multiple of the first
    }

    [Fact]
    public void A_point_sent_to_infinity_comes_back_as_NaN()
    {
        var (x, y) = new Homography(1, 0, 0, 0, 1, 0, 1, 0, 0).Apply(0, 5); // w = x
        Assert.True(double.IsNaN(x) && double.IsNaN(y));
    }

    [Fact]
    public void It_matches_the_homography_the_Python_generator_wrote_for_the_sample_picture()
    {
        var map = Homography.FromPoints(UnitSquare, Corners88)!.Value;
        var truth = GroundTruth().GetProperty("homography");
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
            {
                var expected = truth[r][c].GetDouble();
                Assert.True(Math.Abs(map[r, c] - expected) <= 1e-6 * Math.Max(1, Math.Abs(expected)), $"entry [{r},{c}]: {map[r, c]} vs {expected}");
            }
    }

    [Fact]
    public void Every_key_of_the_sample_lands_where_the_ground_truth_says()
    {
        var truth = GroundTruth();
        Assert.Equal(88, truth.GetProperty("keys").GetInt32());
        var frame = SyntheticKeyboardFrame.Render(88);
        var centres = truth.GetProperty("keyTopCentres");
        var count = 0;
        foreach (var entry in centres.EnumerateObject())
        {
            var (x, y) = frame.KeyTopCentre(int.Parse(entry.Name));
            Assert.True(Math.Abs(x - entry.Value[0].GetDouble()) < 0.01 && Math.Abs(y - entry.Value[1].GetDouble()) < 0.01,
                $"key {entry.Name}: ({x:F3}, {y:F3}) vs ({entry.Value[0].GetDouble():F3}, {entry.Value[1].GetDouble():F3})");
            count++;
        }
        Assert.Equal(88, count);
    }
}
