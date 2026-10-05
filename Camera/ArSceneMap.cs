using System.Numerics;

namespace PianoPath;

/// <summary>
/// Where a point of the stage's own scene lands on the output when the stage is drawn over a camera picture of the real keys
/// (docs/AR-OVERLAY-ROADMAP.md, AR-6.2). The stage draws in scene pixels: a key's centre is at <c>x = CenterX · width</c>, the
/// keys' back edge is the hit line at <c>y = HitY</c>, and everything the player plays rises from there toward <c>y = 0</c>.
/// This lays that scene flat on the keyboard plane — across the keys and away from them — and the calibration carries the plane
/// into the picture, so the stage's own notes, sparks, wisps and rings land on the real piano.
/// </summary>
/// <remarks>
/// <para><b>One scale for both directions.</b> The plane's two axes are not in the same unit: <c>u</c> runs 0..1 across every
/// white key, <c>v</c> runs 0..1 along one key's depth. A scene pixel has to mean the same real length either way or a round
/// ring would land as an oval, so a key is taken to be <see cref="KeyDepthMm"/> deep and <see cref="WhiteKeyMm"/> wide —
/// the same figures a piano has, and the same 150 mm <see cref="ArNoteStyle"/> counts its key depths in.</para>
/// <para><b>Everything is one 3×3 matrix.</b> Scene → keyboard (a scale and a shift), keyboard → picture (the calibration's
/// homography), picture → output (<see cref="ArView"/>'s fit and mirror) are composed once here; the vertex shader applies the
/// result and uses its weight as clip <c>w</c>, so the rasteriser interpolates in perspective — the way
/// <see cref="ArProjection"/> already hands corners to the AR quads. This type is pure arithmetic so a test can hold it to
/// <see cref="ArProjection"/> without a GPU.</para>
/// </remarks>
internal readonly struct ArSceneMap
{
    /// <summary>A white key's width, in millimetres: 52 of them make the 1 225 mm of an 88-key keyboard.</summary>
    internal const double WhiteKeyMm = 23.5;

    /// <summary>A white key's depth, in millimetres; <see cref="ArCalibration.BlackLengthShare"/> counts the black keys in the same 150.</summary>
    internal const double KeyDepthMm = 150;

    /// <summary>One key depth in white-key widths, about 6.4.</summary>
    internal const double DepthInWhiteWidths = KeyDepthMm / WhiteKeyMm;

    private readonly Homography _map;

    private ArSceneMap(Homography map, double width, double hitY, double depthPixels, bool valid)
    {
        _map = map; SceneWidth = width; HitY = hitY; DepthPixels = depthPixels; Valid = valid;
    }

    /// <summary>False when the scene or the picture has no size; nothing is then mapped.</summary>
    internal bool Valid { get; }

    /// <summary>The scene's width in scene pixels (the keyboard spans all of it).</summary>
    internal double SceneWidth { get; }

    /// <summary>Scene <c>y</c> of the keys' back edge, where notes arrive and sound rises from.</summary>
    internal double HitY { get; }

    /// <summary>How many scene pixels one key depth is.</summary>
    internal double DepthPixels { get; }

    /// <summary>
    /// Builds the map. <paramref name="sceneWidth"/> and <paramref name="hitY"/> are the scene's (<c>GpuSceneLayout.Width</c>
    /// and <c>HitY</c>); <paramref name="view"/> says where the picture is on the output.
    /// </summary>
    internal static ArSceneMap Create(ArCalibration calibration, ArView view, double sceneWidth, double hitY)
    {
        var whites = calibration.Layout.WhiteCount;
        if (!view.Valid || !(sceneWidth > 0) || !double.IsFinite(sceneWidth) || !double.IsFinite(hitY) || whites <= 0) return default;
        var depthPixels = DepthInWhiteWidths * sceneWidth / whites;
        // scene → keyboard: u = x / width, v = (y − hitY) / depth; v is negative above the hit line, in the air behind the keys
        var toKeys = new Homography(1 / sceneWidth, 0, 0, 0, 1 / depthPixels, -hitY / depthPixels, 0, 0, 1);
        // picture → output: the view's fit, with the left-right flip when the picture is mirrored
        var scaleX = view.Width / view.ImageWidth; var scaleY = view.Height / view.ImageHeight;
        var toOutput = view.Mirror
            ? new Homography(-scaleX, 0, view.Left + view.Width, 0, scaleY, view.Top, 0, 0, 1)
            : new Homography(scaleX, 0, view.Left, 0, scaleY, view.Top, 0, 0, 1);
        var map = toOutput.After(calibration.ToImage.After(toKeys));
        return new ArSceneMap(map, sceneWidth, hitY, depthPixels, true);
    }

    /// <summary>The nine numbers, row by row, as the shader reads them.</summary>
    internal float[] Rows()
    {
        var rows = new float[9];
        for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++) rows[r * 3 + c] = (float)_map[r, c];
        return rows;
    }

    /// <summary>The weight the map gives a scene point: below <see cref="ArProjection.MinW"/> it is on or beyond the horizon and cannot be drawn.</summary>
    internal double Weight(double x, double y) => _map[2, 0] * x + _map[2, 1] * y + _map[2, 2];

    /// <summary>
    /// The output pixel of a scene point and the weight it came with, or false when the map has no sensible answer there
    /// (an invalid map, a point on or beyond the horizon, or one so far away that a float would lose it).
    /// </summary>
    internal bool TryProject(double x, double y, out double outputX, out double outputY, out double weight)
    {
        outputX = outputY = double.NaN; weight = 0;
        if (!Valid || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        weight = Weight(x, y);
        if (!(weight >= ArProjection.MinW)) return false;
        var (px, py) = _map.Apply(x, y);
        if (!double.IsFinite(px) || !double.IsFinite(py) || Math.Abs(px) > ArProjection.MaxCoordinate || Math.Abs(py) > ArProjection.MaxCoordinate) return false;
        outputX = px; outputY = py;
        return true;
    }

    /// <summary>
    /// Output pixels per scene pixel at a scene point: the square root of the Jacobian's determinant, which is how much a small
    /// shape grows or shrinks there. A ring that is 40 scene pixels across is this much × 40 across at that spot. Zero where
    /// <see cref="TryProject"/> fails.
    /// </summary>
    internal double Scale(double x, double y)
    {
        if (!TryProject(x, y, out var ox, out var oy, out var w)) return 0;
        // d(out)/d(scene) of ((a x + b y + c) / w, (d x + e y + f) / w) is (row − out · row3) / w
        double a = _map[0, 0] - ox * _map[2, 0], b = _map[0, 1] - ox * _map[2, 1];
        double d = _map[1, 0] - oy * _map[2, 0], e = _map[1, 1] - oy * _map[2, 1];
        var determinant = (a * e - b * d) / (w * w);
        return Math.Sqrt(Math.Abs(determinant));
    }

    /// <summary>The keyboard coordinates of a scene point: <c>u</c> across the keys, <c>v</c> along them (negative behind the keys).</summary>
    internal (double U, double V) ToKeys(double x, double y) => Valid ? (x / SceneWidth, (y - HitY) / DepthPixels) : (double.NaN, double.NaN);
}

/// <summary>
/// A scene map as the shader holds it (Gpu/StageShaders.hlsl, AR-6.2): the three rows, each in the <c>xyz</c> of one
/// <c>float4</c> of the <c>Frame</c> constant buffer, and the flag in <c>ArFlags.w</c> that tells the note pass to draw through
/// them. Packing them here rather than in the renderer keeps the whole contract with the shader — the order of the nine
/// numbers, what the flag means, and when a map must not be sent — in one pure place a test can hold on any machine.
/// </summary>
/// <param name="RowA">Row 0: dotted with (<c>x</c>, <c>y</c>, 1) it is the output's <c>x</c>, before the division by the weight.</param>
/// <param name="RowB">Row 1: the output's <c>y</c>, before the same division.</param>
/// <param name="RowC">Row 2: the weight itself, which the vertex shader hands to the rasteriser as the clip <c>w</c>.</param>
/// <param name="Flag">1 when the note pass draws through the rows, 0 when it draws the stage's own way.</param>
internal readonly record struct ArSceneConstants(Vector4 RowA, Vector4 RowB, Vector4 RowC, float Flag)
{
    /// <summary>What a frame without the overlay carries: zeros and no flag, so the note pass is left exactly as it was.</summary>
    internal static readonly ArSceneConstants Off = default;

    /// <summary>
    /// Packs <paramref name="map"/> for the shader. <paramref name="drawn"/> says whether this frame is drawing the overlay at
    /// all; the rows are sent only when the map can also answer, so a flag of 1 always has a usable map behind it and the vertex
    /// shader is never asked to divide by a weight it was not given.
    /// </summary>
    internal static ArSceneConstants Of(ArSceneMap map, bool drawn)
    {
        if (!drawn || !map.Valid) return Off;
        var rows = map.Rows();
        // A calibration that nearly folds the plane over itself would send infinities the shader cannot divide by; the
        // wizard refuses such a quad, and so does this, which leaves the note pass drawing the stage's own way.
        foreach (var value in rows) if (!float.IsFinite(value)) return Off;
        return new ArSceneConstants(new Vector4(rows[0], rows[1], rows[2], 0), new Vector4(rows[3], rows[4], rows[5], 0),
            new Vector4(rows[6], rows[7], rows[8], 0), 1);
    }
}
