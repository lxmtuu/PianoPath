namespace PianoPath;

/// <summary>
/// Legacy scene geometry for the retired scene→image AR shader map. It remains useful for projection regression tests,
/// but the active note path uses the calibrated 3D camera pose and camera-facing quads instead.
/// </summary>
/// <param name="Width">Scene DIPs across the whole keyboard. The stage puts a key's centre at
/// <c>KeyboardLayout.CenterX(pitch) * Width</c>, so x runs 0..Width exactly as <c>u</c> runs 0..1.</param>
/// <param name="HitY">Scene DIPs from the top of the scene to the hit line, which is the back edge of the keys: v = 0.</param>
/// <param name="KeyboardHeight">Scene DIPs the keyboard is drawn deep: the front edge of the keys, v = 1.</param>
internal readonly record struct ArSceneFrame(double Width, double HitY, double KeyboardHeight)
{
    /// <summary>True when the three numbers describe a scene a map can be built for.</summary>
    internal bool IsUsable => double.IsFinite(Width) && Width > 0 && double.IsFinite(HitY)
        && double.IsFinite(KeyboardHeight) && KeyboardHeight > 0;

    /// <summary>
    /// The keyboard coordinates of a scene point: <c>u</c> across the keys, <c>v</c> from the back of the keys (0) to
    /// their front (1). A point above the hit line — the air the notes fall through — has <c>v</c> below 0, which is
    /// the same side of the keys <see cref="ArProjection"/> puts a falling note on.
    /// </summary>
    internal (double U, double V) Plane(double x, double y) => (x / Width, (y - HitY) / KeyboardHeight);

    /// <summary>The scene point of keyboard coordinates: the way back.</summary>
    internal (double X, double Y) Scene(double u, double v) => (u * Width, HitY + v * KeyboardHeight);
}

/// <summary>
/// Legacy 3×3 scene→image helper for tests of the retired stage-space shader branch. It is deliberately not an input
/// to active AR note construction or visibility decisions.
/// </summary>
/// <remarks>
/// <para>It composes the calibration's homography, a scene affine transform and the output view transform. Tests retain
/// the old mathematical contract against <see cref="ArProjection"/>; current notes use <see cref="Ar3DNoteProjection"/>
/// and <see cref="ArCameraPose"/>.</para>
/// <para>The weight is 1 at the back-left corner of the keyboard. This class remains double-precision and independent
/// of the GPU so the dormant cbuffer layout can still be checked without a device.</para>
/// </remarks>
internal readonly struct ArSceneMatrix
{
    private readonly double _a, _b, _c, _d, _e, _f, _g, _h, _i;

    internal ArSceneMatrix(double a, double b, double c, double d, double e, double f, double g, double h, double i)
    {
        (_a, _b, _c, _d, _e, _f, _g, _h, _i) = (a, b, c, d, e, f, g, h, i);
    }

    /// <summary>One entry of the matrix, row and column 0..2.</summary>
    internal double this[int row, int column] => (row, column) switch
    {
        (0, 0) => _a, (0, 1) => _b, (0, 2) => _c,
        (1, 0) => _d, (1, 1) => _e, (1, 2) => _f,
        (2, 0) => _g, (2, 1) => _h, (2, 2) => _i,
        _ => throw new ArgumentOutOfRangeException(nameof(row), "A scene map is 3 by 3."),
    };

    /// <summary>The determinant: its sign is the map's orientation, its size sets the magnification below.</summary>
    internal double Determinant => _a * (_e * _i - _f * _h) - _b * (_d * _i - _f * _g) + _c * (_d * _h - _e * _g);

    /// <summary>False for the empty map and for one that flattens the scene onto a line or a point.</summary>
    internal bool IsValid => double.IsFinite(Determinant) && Math.Abs(Determinant) > 1e-18;

    /// <summary>The legacy map's homogeneous weight at a scene point: 1 at the back-left corner and zero at its horizon.</summary>
    internal double WeightAt(double x, double y) => _g * x + _h * y + _i;

    /// <summary>Where a scene point lands on the output, with its weight. NaN coordinates on or beyond the horizon.</summary>
    internal (double X, double Y, double W) Project(double x, double y)
    {
        var w = WeightAt(x, y);
        if (!(Math.Abs(w) > 1e-12)) return (double.NaN, double.NaN, w);
        return ((_a * x + _b * y + _c) / w, (_d * x + _e * y + _f) / w, w);
    }

    /// <summary>Legacy-map output pixels per scene DIP at a point; zero at or beyond that map's horizon.</summary>
    /// <remarks>For this projective map the Jacobian's determinant is <c>det(M) / w³</c>, so the old shader scale is
    /// <c>√|det(M)| / w^1.5</c>. The active 3D AR-note renderer does not consume this value.</remarks>
    internal double ScaleAt(double x, double y)
    {
        var w = WeightAt(x, y);
        if (!(w > 1e-12)) return 0;
        return Math.Sqrt(Math.Abs(Determinant)) / (w * Math.Sqrt(w));
    }
}

/// <summary>Builds and tests the legacy scene→image map retained for regression coverage; the active note renderer does not call it.</summary>
internal static class ArSceneProjection
{
    /// <summary>
    /// The map for a calibration, a view and a scene layout, or false when any of the three cannot carry one: no
    /// calibration, a view or a scene with no size, or a map that flattens the plane.
    /// </summary>
    internal static bool TryBuild(ArCalibration? calibration, ArView view, ArSceneFrame frame, out ArSceneMatrix map)
    {
        map = default;
        if (calibration is null || !view.Valid || !frame.IsUsable) return false;
        var h = calibration.ToImage;
        // the scene's own affine map: x across the keyboard into u, y measured from the hit line into v
        double across = 1 / frame.Width, depth = 1 / frame.KeyboardHeight, back = -frame.HitY / frame.KeyboardHeight;
        double p00 = h[0, 0] * across, p01 = h[0, 1] * depth, p02 = h[0, 1] * back + h[0, 2];
        double p10 = h[1, 0] * across, p11 = h[1, 1] * depth, p12 = h[1, 1] * back + h[1, 2];
        double p20 = h[2, 0] * across, p21 = h[2, 1] * depth, p22 = h[2, 1] * back + h[2, 2];
        // the view's affine map, picture pixels to output pixels; mirroring is a negative scale across x
        double scaleAcross = view.Width / view.ImageWidth, scaleDown = view.Height / view.ImageHeight;
        double mirrorAcross = view.Mirror ? -scaleAcross : scaleAcross;
        double left = view.Mirror ? view.Left + view.Width : view.Left, top = view.Top;
        map = new ArSceneMatrix(
            mirrorAcross * p00 + left * p20, mirrorAcross * p01 + left * p21, mirrorAcross * p02 + left * p22,
            scaleDown * p10 + top * p20, scaleDown * p11 + top * p21, scaleDown * p12 + top * p22,
            p20, p21, p22);
        return map.IsValid;
    }

    /// <summary>
    /// The patch the legacy scene rectangle would cover, using the same keyboard-plane horizon clip as
    /// <see cref="ArProjection.TryProject"/>. This helper is for regression tests only; current AR notes use 3D near-plane clipping.
    /// <paramref name="yBack"/> and <paramref name="yFront"/> are the retained scene y limits.
    /// </summary>
    internal static bool TryProjectRect(ArCalibration? calibration, ArView view, ArSceneFrame frame,
        double x0, double x1, double y0, double y1, out ArPatch patch, out double yBack, out double yFront)
    {
        patch = default; yBack = y0; yFront = y1;
        if (calibration is null || !frame.IsUsable || !(x0 < x1) || !(y0 < y1)) return false;
        var (u0, v0) = frame.Plane(x0, y0);
        var (u1, v1) = frame.Plane(x1, y1);
        if (!double.IsFinite(u0) || !double.IsFinite(u1) || !double.IsFinite(v0) || !double.IsFinite(v1)) return false;
        if (!ArProjection.TryProject(calibration, view, u0, u1, v0, v1, out patch, out var back, out var front)) return false;
        yBack = frame.HitY + back * frame.KeyboardHeight;
        yFront = frame.HitY + front * frame.KeyboardHeight;
        return true;
    }

    /// <summary>
    /// Legacy horizon clipping for a scene rectangle. The active 3D AR-note builder performs its own camera near-plane
    /// clipping and does not call this helper. Returns false when the old scene rectangle has no visible patch.
    /// </summary>
    internal static bool TruncateToPicture(ArCalibration? calibration, ArView view, ArSceneFrame frame,
        double x0, double x1, ref double top, ref double bottom)
    {
        if (!TryProjectRect(calibration, view, frame, x0, x1, top, bottom, out _, out var yBack, out var yFront)) return false;
        top = yBack;
        bottom = yFront;
        return true;
    }
}
