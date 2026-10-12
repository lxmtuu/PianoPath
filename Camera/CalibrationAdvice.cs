namespace PianoPath;

/// <summary>
/// The live framing advice of the calibration window (AR roadmap §14.7 "Thêm 4 — hướng dẫn ngay trong cửa sổ"): the window
/// itself carries the two set-up sentences (over the shoulder, not a very wide lens, hands out), and this lookout answers
/// the one thing the camera can actually see — <b>is something skin-coloured inside the picture (or already inside the
/// marked keys) right now?</b> The arithmetic is pure BGRA so xUnit and <c>--verify</c> hold it without a camera:
/// a classic Y′CbCr skin lobe (Cb 77–127, Cr 133–173, with a minimal brightness so dark brown piano wood and black keys
/// cannot count as skin) read on a 3-pixel walk. It is a lookout, not a gate: it never blocks a calibration, a sunlit
/// beech stand can still borrow the colour of a hand, and that is the price for never shouting about the keys themselves.
/// </summary>
internal static class CalibrationAdvice
{
    /// <summary>The share of sampled pixels that must read as skin before the window says anything.</summary>
    internal const double SkinWarningFraction = 0.03;

    /// <summary>The walk the sampling takes over the frame, in pixels; 3 keeps a 1280 frame under a millisecond.</summary>
    internal const int SampleStep = 3;

    /// <summary>True when one BGRA pixel reads as skin: inside the Y′CbCr skin lobe and bright enough not to be wood or a shadow.</summary>
    internal static bool IsSkin(byte[] bgra, int offset)
    {
        var b = bgra[offset]; var g = bgra[offset + 1]; var r = bgra[offset + 2];
        var y = 0.2568 * r + 0.5041 * g + 0.0979 * b;
        if (y < 40) return false;                                   // dark wood, black keys, a dim room: never skin
        var cb = 128 - 0.1482 * r - 0.2910 * g + 0.4392 * b;
        var cr = 128 + 0.4392 * r - 0.3674 * g - 0.0718 * b;
        return cb >= 77 && cb <= 127 && cr >= 133 && cr <= 173;
    }

    /// <summary>The skin share of a rectangle of the picture (sampled on the walk), 0..1.</summary>
    internal static double SkinFraction(byte[] bgra, int width, int height, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, width); x1 = Math.Clamp(x1, 0, width);
        y0 = Math.Clamp(y0, 0, height); y1 = Math.Clamp(y1, 0, height);
        var skin = 0; var total = 0;
        for (var y = y0; y < y1; y += SampleStep)
            for (var x = x0; x < x1; x += SampleStep)
            {
                total++;
                if (IsSkin(bgra, (y * width + x) * 4)) skin++;
            }
        return total == 0 ? 0 : skin / (double)total;
    }

    /// <summary>The skin share of the whole picture.</summary>
    internal static double SkinFraction(byte[] bgra, int width, int height) => SkinFraction(bgra, width, height, 0, 0, width, height);

    /// <summary>
    /// What the window warns about: skin inside the marked key quad when all four corners are down (point-in-quad on the
    /// same walk — a waving arm beside the keys is nobody's business), else skin anywhere in the picture, because before
    /// the corners are down the whole picture is the future key area. The share that fired the verdict rides along.
    /// </summary>
    internal static (bool HandsInTheWay, double Share) CheckPicture(byte[] bgra, int width, int height, IReadOnlyList<(double X, double Y)>? corners)
    {
        if (corners is { Count: 4 } quad)
        {
            var skin = 0; var total = 0;
            for (var y = 0; y < height; y += SampleStep)
                for (var x = 0; x < width; x += SampleStep)
                    if (InsideQuad(quad, x + .5, y + .5))
                    {
                        total++;
                        if (IsSkin(bgra, (y * width + x) * 4)) skin++;
                    }
            var share = total == 0 ? 0 : skin / (double)total;
            return (share > SkinWarningFraction, share);
        }
        var overall = SkinFraction(bgra, width, height);
        return (overall > SkinWarningFraction, overall);
    }

    /// <summary>Point-in-quad as the surface draws it: the corners in back-left, back-right, front-right, front-left order.</summary>
    internal static bool InsideQuad(IReadOnlyList<(double X, double Y)> quad, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = 3; i < 4; j = i++)
        {
            var (xi, yi) = quad[i]; var (xj, yj) = quad[j];
            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
}
