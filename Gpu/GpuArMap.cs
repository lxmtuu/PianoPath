using System.Numerics;

namespace PianoPath;

/// <summary>
/// The scene→image map as the note pass reads it out of the Frame cbuffer (AR-S4b, Gpu/StageShaders.hlsl):
/// the map's three rows and one float4 of (AR flags, √|det M|, min scale px). The packing lives here, in one
/// place the portable test project can hold to the shader's layout without a GPU; the pixel half of the
/// contract is <c>VerifyArNoteProjection</c>, which reads a rendered frame back on WARP and finds each note
/// inside the patch <see cref="ArProjection.TryProject"/> reports for its own rectangle.
/// </summary>
/// <remarks>The map is <see cref="ArSceneMatrix"/>, built by <see cref="ArSceneProjection.TryBuild"/> from the
/// same calibration, view and scene layout the flat AR quads use. The shader's <c>VsNote</c> multiplies a note's
/// Local and Half by √|det M| / w^1.5 at the note's centre (floored at min scale px), so the SDF measures real
/// output pixels; the weight at each rect corner becomes the clip w, which is what makes the quad interpolate
/// in perspective.</remarks>
internal readonly struct GpuArMap
{
    /// <summary><c>ArMatrixFlags.x</c>: the note pass draws through the map — the only flag the shader reads today.</summary>
    internal const float NotesThroughMap = 1f;

    internal GpuArMap(ArSceneMatrix map, bool notes, float minScalePx)
    {
        Row0 = new Vector4((float)map[0, 0], (float)map[0, 1], (float)map[0, 2], 0);
        Row1 = new Vector4((float)map[1, 0], (float)map[1, 1], (float)map[1, 2], 0);
        Row2 = new Vector4((float)map[2, 0], (float)map[2, 1], (float)map[2, 2], 0);
        Flags = new Vector4(notes ? NotesThroughMap : 0, (float)Math.Sqrt(Math.Abs(map.Determinant)),
            float.IsFinite(minScalePx) ? Math.Max(minScalePx, 0) : 0, 0);
    }

    /// <summary>The empty lanes: no map was built, so the note branch cannot open.</summary>
    internal static GpuArMap None => default;

    /// <summary>Row 0 of the map (a, b, c); the fourth lane keeps the cbuffer's 16-byte stride.</summary>
    internal Vector4 Row0 { get; }

    /// <summary>Row 1 (d, e, f).</summary>
    internal Vector4 Row1 { get; }

    /// <summary>Row 2 (g, h, i): the homogeneous weight row.</summary>
    internal Vector4 Row2 { get; }

    /// <summary>x = AR flags, y = √|det M| (ScaleAt's numerator), z = min scale px, w = 0.</summary>
    internal Vector4 Flags { get; }
}
