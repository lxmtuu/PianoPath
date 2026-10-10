using System.Numerics;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Legacy shader-map packing only: the active perspective note pass uses the calibration's 3D camera pose and AR
/// quads. Pixel-level render ownership, duplicate suppression and live-trail exclusion are checked by
/// <c>VerifyArNoteProjection</c> on WARP. The map itself is not re-derived here: <see cref="ArSceneProjectionTests"/>
/// holds it to <see cref="ArProjection"/>, corner by corner.
/// </summary>
public class GpuArMapTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];

    // the map's types are internal, and an xUnit test method has to be public, so the cases are plain flags
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 0f)]
    [InlineData(true, 2.5f)]
    [InlineData(true, -3f)]
    public void The_packed_rows_and_flags_are_the_map_the_shader_is_described_as_reading(bool notes, float minScalePx)
    {
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, Sample88, 1280, 720, out var problem)
            ?? throw new InvalidOperationException($"the sample corners were refused: {problem}");
        var keyboard = 720 * .28;
        var frame = new ArSceneFrame(1280, 720 - keyboard, keyboard);
        var view = new ArView(1280, 720, 640, 360, PictureFit.Fit, false);
        Assert.True(ArSceneProjection.TryBuild(calibration, view, frame, out var map));
        var packed = new GpuArMap(map, notes, minScalePx);

        // the three rows carry the matrix; the fourth lane of each keeps the cbuffer's 16-byte stride at zero
        Assert.Equal((float)map[0, 0], packed.Row0.X);
        Assert.Equal((float)map[0, 1], packed.Row0.Y);
        Assert.Equal((float)map[0, 2], packed.Row0.Z);
        Assert.Equal((float)map[1, 0], packed.Row1.X);
        Assert.Equal((float)map[1, 1], packed.Row1.Y);
        Assert.Equal((float)map[1, 2], packed.Row1.Z);
        Assert.Equal((float)map[2, 0], packed.Row2.X);
        Assert.Equal((float)map[2, 1], packed.Row2.Y);
        Assert.Equal((float)map[2, 2], packed.Row2.Z);
        Assert.Equal(0f, packed.Row0.W);
        Assert.Equal(0f, packed.Row1.W);
        Assert.Equal(0f, packed.Row2.W);

        // x = the flag the note branch reads (and the only one defined), y = sqrt|det M|, z = the floor on the magnification
        Assert.Equal(notes ? GpuArMap.NotesThroughMap : 0f, packed.Flags.X);
        Assert.Equal((float)Math.Sqrt(Math.Abs(map.Determinant)), packed.Flags.Y);
        Assert.Equal(Math.Max(minScalePx, 0), packed.Flags.Z);
        Assert.Equal(0f, packed.Flags.W);
    }

    [Fact]
    public void Without_a_map_the_new_lanes_are_zero_so_the_note_branch_cannot_open()
    {
        var none = GpuArMap.None;
        Assert.Equal(Vector4.Zero, none.Row0);
        Assert.Equal(Vector4.Zero, none.Row1);
        Assert.Equal(Vector4.Zero, none.Row2);
        Assert.Equal(Vector4.Zero, none.Flags);
    }
}
