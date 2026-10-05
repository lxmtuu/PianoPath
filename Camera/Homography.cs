namespace PianoPath;

/// <summary>
/// A 3×3 projective map of the plane — what a camera does to a flat keyboard seen from a slant. The AR overlay
/// (docs/AR-OVERLAY-ROADMAP.md) draws in keyboard coordinates (u across the keys, v from the back of the keys to their
/// front) and a <see cref="Homography"/> carries those points to camera pixels, and back.
/// </summary>
/// <remarks>
/// Pure arithmetic on doubles, with nothing from WPF or Direct3D, so it is compiled into the test project and checked on any
/// machine. <c>Apply</c> is <c>(x', y') = ((a x + b y + c) / w, (d x + e y + f) / w)</c> with <c>w = g x + h y + i</c>.
/// </remarks>
internal readonly struct Homography
{
    private readonly double _a, _b, _c, _d, _e, _f, _g, _h, _i;

    internal Homography(double a, double b, double c, double d, double e, double f, double g, double h, double i)
    {
        (_a, _b, _c, _d, _e, _f, _g, _h, _i) = (a, b, c, d, e, f, g, h, i);
    }

    /// <summary>The map that leaves every point where it is.</summary>
    internal static Homography Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>One entry of the matrix, row and column 0..2.</summary>
    internal double this[int row, int column] => (row, column) switch
    {
        (0, 0) => _a, (0, 1) => _b, (0, 2) => _c,
        (1, 0) => _d, (1, 1) => _e, (1, 2) => _f,
        (2, 0) => _g, (2, 1) => _h, (2, 2) => _i,
        _ => throw new ArgumentOutOfRangeException(nameof(row), "A homography is 3 by 3."),
    };

    /// <summary>
    /// The map that takes the four points <paramref name="from"/> to the four points <paramref name="to"/> (the direct linear
    /// transform: eight unknowns, the last matrix entry fixed at 1). Null when the points cannot define one — fewer than four,
    /// or three of them on a line.
    /// </summary>
    internal static Homography? FromPoints(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to)
    {
        if (from.Count != 4 || to.Count != 4) return null;
        if (AnyThreeOnALine(from) || AnyThreeOnALine(to)) return null;
        var matrix = new double[8, 9];
        for (var k = 0; k < 4; k++)
        {
            var (x, y) = from[k];
            var (u, v) = to[k];
            double[] first = [x, y, 1, 0, 0, 0, -u * x, -u * y, u];
            double[] second = [0, 0, 0, x, y, 1, -v * x, -v * y, v];
            for (var c = 0; c < 9; c++) { matrix[2 * k, c] = first[c]; matrix[2 * k + 1, c] = second[c]; }
        }
        var h = Solve(matrix);
        if (h is null) return null;
        return new Homography(h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0);
    }

    /// <summary>The map that undoes this one; null when this one flattens the plane to a line or a point.</summary>
    internal Homography? Inverse()
    {
        var det = _a * (_e * _i - _f * _h) - _b * (_d * _i - _f * _g) + _c * (_d * _h - _e * _g);
        if (det == 0 || !double.IsFinite(det)) return null;
        return new Homography(
            (_e * _i - _f * _h) / det, (_c * _h - _b * _i) / det, (_b * _f - _c * _e) / det,
            (_f * _g - _d * _i) / det, (_a * _i - _c * _g) / det, (_c * _d - _a * _f) / det,
            (_d * _h - _e * _g) / det, (_b * _g - _a * _h) / det, (_a * _e - _b * _d) / det);
    }

    /// <summary>Where the point (<paramref name="x"/>, <paramref name="y"/>) goes; NaN when it is sent to infinity.</summary>
    internal (double X, double Y) Apply(double x, double y)
    {
        var w = _g * x + _h * y + _i;
        if (w == 0) return (double.NaN, double.NaN);
        return ((_a * x + _b * y + _c) / w, (_d * x + _e * y + _f) / w);
    }

    /// <summary>This map applied after <paramref name="first"/>: <c>After(f).Apply(p)</c> is <c>Apply(f.Apply(p))</c>.</summary>
    internal Homography After(Homography first) => Multiply(this, first);

    private static Homography Multiply(Homography left, Homography right)
    {
        double Cell(int r, int c) => left[r, 0] * right[0, c] + left[r, 1] * right[1, c] + left[r, 2] * right[2, c];
        return new Homography(Cell(0, 0), Cell(0, 1), Cell(0, 2), Cell(1, 0), Cell(1, 1), Cell(1, 2), Cell(2, 0), Cell(2, 1), Cell(2, 2));
    }

    /// <summary>True when some three of the points lie on one line (the sine of their angle is under a billionth).</summary>
    private static bool AnyThreeOnALine(IReadOnlyList<(double X, double Y)> points)
    {
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                for (var k = j + 1; k < points.Count; k++)
                {
                    double ux = points[j].X - points[i].X, uy = points[j].Y - points[i].Y;
                    double vx = points[k].X - points[i].X, vy = points[k].Y - points[i].Y;
                    var cross = Math.Abs(ux * vy - uy * vx);
                    var scale = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
                    if (!(cross > 1e-9 * scale)) return true; // also true for coincident points (scale 0) and for NaN
                }
        return false;
    }

    /// <summary>Gaussian elimination with partial pivoting on an 8×9 augmented matrix; null when a pivot vanishes.</summary>
    private static double[]? Solve(double[,] m)
    {
        const int n = 8;
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) return null;
            if (pivot != col)
                for (var c = 0; c <= n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            for (var r = col + 1; r < n; r++)
            {
                var factor = m[r, col] / m[col, col];
                for (var c = col; c <= n; c++) m[r, c] -= factor * m[col, c];
            }
        }
        var x = new double[n];
        for (var r = n - 1; r >= 0; r--)
        {
            var sum = m[r, n];
            for (var c = r + 1; c < n; c++) sum -= m[r, c] * x[c];
            x[r] = sum / m[r, r];
        }
        return x;
    }
}
