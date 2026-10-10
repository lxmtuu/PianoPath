using System.Numerics;

namespace PianoPath;

/// <summary>
/// Legacy scene→image map packing for the dormant AR shader branch in <c>Gpu/StageShaders.hlsl</c>. The active
/// perspective-note route keeps these flags zero: <see cref="Ar3DNoteProjection"/> builds camera-facing quads from
/// <see cref="ArCameraPose"/> and submits them through the AR layer. This remains for the portable cbuffer-layout
/// tests and the flat-map helpers; it is not a gate on whether AR notes are emitted.
/// </summary>
/// <remarks>The map is <see cref="ArSceneMatrix"/>, built by <see cref="ArSceneProjection.TryBuild"/> from the
/// calibration, the picture's output view and a stage-scene layout.</remarks>
internal readonly struct GpuArMap
{
    /// <summary>Bit 0 of <c>ArMatrixFlags.x</c>: only the note pass may draw through this map. Ambient sprites stay flat on the camera.</summary>
    internal const float NotesThroughMap = 1f;
    /// <summary>Bit 1 is reserved for an explicitly opted-in future sprite projection; AR overlay ambient remains flat by default.</summary>
    internal const float SpritesThroughMap = 2f;

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
