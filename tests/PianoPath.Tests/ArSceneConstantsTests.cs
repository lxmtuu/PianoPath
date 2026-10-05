using System.Numerics;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The scene map as the shader holds it: three rows and the flag that switches the note pass over to them. The packing is the
/// contract between <see cref="ArSceneMap"/> and <c>VsNote</c> in Gpu/StageShaders.hlsl, so these tests run the shader's own
/// arithmetic on the packed rows and hold it to the map the CPU can answer exactly.
/// </summary>
public class ArSceneConstantsTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
    private static readonly (double X, double Y)[] BackNarrow = [(400, 100), (800, 100), (1200, 600), (80, 600)];
    private static readonly (double X, double Y)[] BackWide = [(80, 100), (1200, 100), (800, 600), (400, 600)];

    private const double SceneWidth = 1568, HitY = 700;

    private static void Near(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual} (tolerance {tolerance})");

    private static ArSceneMap Map((double X, double Y)[] corners, int outputWidth = 1280, int outputHeight = 720, bool mirror = false) =>
        ArSceneMap.Create(ArProjectionTests.Calibrate(corners), new ArView(1280, 720, outputWidth, outputHeight, PictureFit.Fit, mirror), SceneWidth, HitY);

    /// <summary>
    /// What <c>VsNote</c> does with the rows, in the same order and the same single precision: the weight as the clip <c>w</c>,
    /// the output pixel recovered from the clip position the way the rasteriser divides it, and the local scale that
    /// <c>PsNote</c> anti-aliases the note's edge with.
    /// </summary>
    private static (double X, double Y, double W, double Scale) ShaderVertex(ArSceneConstants c, int screenWidth, int screenHeight, double x, double y)
    {
        float fx = (float)x, fy = (float)y;
        float w = c.RowC.X * fx + c.RowC.Y * fy + c.RowC.Z;
        float mapX = c.RowA.X * fx + c.RowA.Y * fy + c.RowA.Z;
        float mapY = c.RowB.X * fx + c.RowB.Y * fy + c.RowB.Z;
        // the division by w is cancelled in the clip position, which is what keeps a vertex at the horizon finite
        float clipX = mapX / screenWidth * 2f - w;
        float clipY = w - mapY / screenHeight * 2f;
        float rw = 1f / (MathF.Abs(w) < 1e-3f ? (w < 0f ? -1e-3f : 1e-3f) : w);
        float jxX = c.RowA.X - mapX * rw * c.RowC.X, jxY = c.RowA.Y - mapX * rw * c.RowC.Y;
        float jyX = c.RowB.X - mapY * rw * c.RowC.X, jyY = c.RowB.Y - mapY * rw * c.RowC.Y;
        var scale = MathF.Min(MathF.Sqrt(MathF.Abs(jxX * jyY - jxY * jyX)) * MathF.Abs(rw), 1e4f);
        double ndcX = clipX / (double)w, ndcY = clipY / (double)w;
        return ((ndcX + 1) / 2 * screenWidth, (1 - ndcY) / 2 * screenHeight, w, scale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_rows_are_the_map_s_own_in_order_and_the_flag_says_the_note_pass_uses_them(bool mirror)
    {
        foreach (var corners in new[] { Sample88, BackNarrow })
        {
            var map = Map(corners, mirror: mirror);
            var constants = ArSceneConstants.Of(map, true);
            Assert.Equal(1f, constants.Flag);
            var rows = map.Rows();
            Assert.Equal(new Vector4(rows[0], rows[1], rows[2], 0), constants.RowA);
            Assert.Equal(new Vector4(rows[3], rows[4], rows[5], 0), constants.RowB);
            Assert.Equal(new Vector4(rows[6], rows[7], rows[8], 0), constants.RowC);
        }
    }

    [Fact]
    public void A_frame_that_did_not_ask_for_the_overlay_or_has_no_map_sends_nothing_and_no_flag()
    {
        var map = Map(Sample88);
        foreach (var constants in new[]
        {
            ArSceneConstants.Of(map, false),                    // the overlay is not drawing this frame
            ArSceneConstants.Of(default, true),                  // no map at all
            ArSceneConstants.Of(Map(Sample88, 0, 0), true),      // an output with no size
            ArSceneConstants.Off, default,
        })
        {
            Assert.Equal(0f, constants.Flag);
            Assert.Equal(Vector4.Zero, constants.RowA);
            Assert.Equal(Vector4.Zero, constants.RowB);
            Assert.Equal(Vector4.Zero, constants.RowC);
        }
    }

    [Fact]
    public void A_scene_no_float_can_hold_is_refused_rather_than_sent_to_the_shader_as_infinities()
    {
        // 1e300 is a finite double, so the map is built, but the rows it composes are far past what a float holds: the
        // shader would divide by an infinity it was handed. The vertex shader is never asked to.
        var absurd = ArSceneMap.Create(ArProjectionTests.Calibrate(Sample88), new ArView(1280, 720, 1280, 720, PictureFit.Fit, false), SceneWidth, 1e300);
        Assert.True(absurd.Valid);
        Assert.Contains(absurd.Rows(), value => !float.IsFinite(value));
        var constants = ArSceneConstants.Of(absurd, true);
        Assert.Equal(ArSceneConstants.Off, constants);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(640, 360)]
    [InlineData(240, 480)]
    public void The_vertex_shader_s_own_arithmetic_on_the_rows_lands_where_the_map_says(int outputWidth, int outputHeight)
    {
        var map = Map(Sample88, outputWidth, outputHeight);
        var constants = ArSceneConstants.Of(map, true);
        foreach (var (x, y) in new[] { (0.0, HitY), (SceneWidth * .45, HitY - 40), (SceneWidth * .5, HitY * .5), (SceneWidth, HitY + 60), (SceneWidth * .2, 12.0) })
        {
            Assert.True(map.TryProject(x, y, out var expectedX, out var expectedY, out var expectedWeight), $"the map should answer at ({x:0},{y:0})");
            var (actualX, actualY, weight, scale) = ShaderVertex(constants, outputWidth, outputHeight, x, y);
            Near(expectedX, actualX, .02);
            Near(expectedY, actualY, .02);
            Near(expectedWeight, weight, 1e-4 * expectedWeight);
            // and the scale the pixel shader anti-aliases with is ArSceneMap.Scale at the same point
            var expectedScale = map.Scale(x, y);
            Near(expectedScale, scale, .01 * expectedScale + 1e-6);
        }
    }

    [Fact]
    public void The_mirrored_picture_puts_a_note_on_the_other_side_of_the_output()
    {
        var plain = ArSceneConstants.Of(Map(Sample88, 1000, 600), true);
        var mirrored = ArSceneConstants.Of(Map(Sample88, 1000, 600, mirror: true), true);
        var (x, y, _, _) = ShaderVertex(plain, 1000, 600, SceneWidth * .3, HitY - 100);
        var (mx, my, _, _) = ShaderVertex(mirrored, 1000, 600, SceneWidth * .3, HitY - 100);
        Near(y, my, .02);
        Near(1000 - x, mx, .02);
        Assert.True(Math.Abs(x - mx) > 100, $"a mirrored note has to move (plain {x:0}, mirrored {mx:0})");
    }

    [Fact]
    public void A_vertex_at_or_beyond_the_horizon_stays_finite_so_the_clipper_can_cut_it_away()
    {
        // BackWide's sides meet behind the keys: half a key depth above them the weight is through zero. The clip position is
        // built with the division by w cancelled out, so nothing the shader is handed is an infinity or a NaN, and the same
        // points are the ones the CPU refuses - so the primitive is cut at the horizon instead of smeared across the screen.
        var map = Map(BackWide);
        var constants = ArSceneConstants.Of(map, true);
        var depth = map.DepthPixels;
        foreach (var v in new[] { -0.55, -0.6, -1.0, -3.0, -20.0 })
        {
            var (_, _, weight, scale) = ShaderVertex(constants, 1280, 720, SceneWidth * .5, HitY + v * depth);
            Assert.True(double.IsFinite(weight), $"the weight at {v} key depths behind the keys must be finite ({weight})");
            Assert.True(double.IsFinite(scale) && scale >= 0 && scale <= 1e4, $"the scale at {v} key depths must be finite and bounded ({scale})");
            Assert.False(map.TryProject(SceneWidth * .5, HitY + v * depth, out _, out _, out _), $"the CPU must refuse {v} key depths behind the keys");
        }
        Assert.True(map.TryProject(SceneWidth * .5, HitY - .2 * depth, out _, out _, out var near), "and just below the horizon it still answers");
        Assert.True(near >= ArProjection.MinW);
    }
}
