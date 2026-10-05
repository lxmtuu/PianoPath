namespace PianoPath.Tests;

/// <summary>
/// A drawn "camera picture" of a piano keyboard seen from above, with the homography that made it — the C# twin of
/// <c>tools/make_ar_sample.py</c>, so a detector or a calibration can be tried on any of the four keyboard sizes
/// and any camera pose without a camera, a photo or a Python run. It uses the application's own
/// <see cref="KeyboardLayout"/> for the key geometry, and <see cref="SyntheticKeyboardFrameTests"/> holds it to the
/// Python picture in <c>docs/samples</c> pixel for pixel, so the two cannot drift apart unnoticed.
/// </summary>
internal sealed class SyntheticKeyboardFrame
{
    internal const int Width = 1280, Height = 720;

    /// <summary>Real dimensions in millimetres: they give the key area its true shape.</summary>
    private const double WhiteMm = 23.5, WhiteLengthMm = 150.0, BlackLengthMm = 95.0;

    /// <summary>Where the key area lands in the picture, per key count: back-left, back-right, front-right, front-left.</summary>
    internal static readonly IReadOnlyDictionary<int, (double X, double Y)[]> Corners = new Dictionary<int, (double X, double Y)[]>
    {
        [88] = [(70, 292), (1214, 268), (1236, 421), (52, 449)],
        [76] = [(110, 288), (1180, 266), (1200, 428), (92, 452)],
        [61] = [(190, 276), (1088, 258), (1106, 432), (172, 452)],
        [49] = [(262, 270), (1010, 254), (1030, 436), (246, 456)],
    };

    internal KeyboardLayout Layout { get; }

    /// <summary>The key area's four image corners, in the order of <see cref="Corners"/>.</summary>
    internal (double X, double Y)[] ImageCorners { get; }

    /// <summary>Keyboard coordinates (u across 0..1, v from the back of the keys 0 to their front 1) to image pixels.</summary>
    internal Homography ToImage { get; }

    /// <summary>Image pixels back to keyboard coordinates.</summary>
    internal Homography ToKeyboard { get; }

    /// <summary>The picture: <see cref="Width"/> × <see cref="Height"/> pixels, three bytes (R, G, B) each, rows from the top.</summary>
    internal byte[] Rgb { get; }

    private SyntheticKeyboardFrame(KeyboardLayout layout, (double X, double Y)[] corners, Homography toImage, Homography toKeyboard, byte[] rgb)
    {
        Layout = layout; ImageCorners = corners; ToImage = toImage; ToKeyboard = toKeyboard; Rgb = rgb;
    }

    /// <summary>Where the centre of the top edge of a key is in the picture: v = 0.25 for a black key, 0.8 for a white one.</summary>
    internal (double X, double Y) KeyTopCentre(int pitch) =>
        ToImage.Apply(Layout.CenterX(pitch), KeyboardLayout.IsBlack(pitch) ? 0.25 : 0.8);

    /// <summary>Draws the picture for <paramref name="keyCount"/> keys (88, 76, 61 or 49) seen from the camera pose in <see cref="Corners"/>.</summary>
    internal static SyntheticKeyboardFrame Render(int keyCount) =>
        Render(KeyboardLayout.ForKeyCount(keyCount), Corners[KeyboardLayout.ForKeyCount(keyCount).KeyCount]);

    /// <summary>Draws <paramref name="layout"/> with its key area at the four image <paramref name="corners"/>.</summary>
    internal static SyntheticKeyboardFrame Render(KeyboardLayout layout, (double X, double Y)[] corners)
    {
        var toImage = Homography.FromPoints([(0, 0), (1, 0), (1, 1), (0, 1)], corners)
            ?? throw new ArgumentException("The four corners do not make a four-sided key area.", nameof(corners));
        var toKeyboard = toImage.Inverse() ?? throw new ArgumentException("The corners flatten the keyboard.", nameof(corners));

        var left = toImage.Apply(0, 0.5);
        var right = toImage.Apply(1, 0.5);
        var aspect = 1.0 / Math.Max(1.0, Math.Sqrt((right.X - left.X) * (right.X - left.X) + (right.Y - left.Y) * (right.Y - left.Y)));

        var rgb = new byte[Width * Height * 3];
        var o = 0;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var (u, v) = toKeyboard.Apply(x + 0.5, y + 0.5);
                var (r, g, b) = Shade(layout, u, v, aspect, x, y);
                // Light that falls off toward the corners, a warm lamp from the upper left, and sensor grain.
                double dx = (x - Width / 2.0) / (Width / 2.0), dy = (y - Height / 2.0) / (Height / 2.0);
                var light = 1.0 - 0.22 * (dx * dx + dy * dy) + 0.05 * (-dx - dy);
                var grain = 3.0 * Noise(x, y, 7);
                rgb[o++] = ToByte(r * light * 1.02 + grain);
                rgb[o++] = ToByte(g * light + grain);
                rgb[o++] = ToByte(b * light * 0.96 + grain);
            }
        }
        return new SyntheticKeyboardFrame(layout, corners, toImage, toKeyboard, rgb);
    }

    /// <summary>The red, green and blue of the pixel at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    internal (byte R, byte G, byte B) PixelAt(int x, int y)
    {
        var o = (y * Width + x) * 3;
        return (Rgb[o], Rgb[o + 1], Rgb[o + 2]);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)value, 0, 255); // truncates toward zero, like Python's int()

    private static double Smooth(double edge0, double edge1, double x)
    {
        var t = Math.Min(1.0, Math.Max(0.0, (x - edge0) / (edge1 - edge0)));
        return t * t * (3 - 2 * t);
    }

    /// <summary>Deterministic hash noise in -1..1: a sensor's grain, the same on every run.</summary>
    private static double Noise(int x, int y, int seed)
    {
        unchecked
        {
            var n = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2147483647u;
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xFFFFu) / 32767.5 - 1.0;
        }
    }

    /// <summary>Colour of one point of the keyboard plane. u 0..1 across, v 0 at the back of the keys and 1 at their front.</summary>
    private static (double R, double G, double B) Shade(KeyboardLayout layout, double u, double v, double aspectUPerPx, int px, int py)
    {
        var whites = layout.WhiteCount;
        var whiteWidth = layout.WhiteWidth;
        // Pixel footprint in key units, so edges soften by about one pixel whatever the zoom.
        var fu = Math.Max(1e-6, aspectUPerPx);
        var fv = fu * (WhiteMm * whites) / WhiteLengthMm;
        if (v < -0.55 || v > 1.45 || u < -0.06 || u > 1.06)
        {
            // The desk around the instrument: dark wood, a slow grain across it.
            var grain = 0.5 + 0.5 * Math.Sin(py * 0.05 + 3 * Math.Sin(px * 0.004));
            return (38 + 14 * grain, 30 + 10 * grain, 25 + 8 * grain);
        }
        if (v < 0)
        {
            // The fallboard behind the keys: black lacquer with a soft sheen.
            var sheen = 18 * Smooth(-0.55, -0.05, v) + 10 * Math.Exp(-((u - 0.5) * (u - 0.5)) * 14);
            return (14 + sheen, 14 + sheen, 18 + sheen);
        }
        if (v > 1)
        {
            // The key slip in front of the keys: a darker strip with a bright lip.
            var lip = Smooth(1.0, 1.0 + fv, v) * (1 - Smooth(1.0 + fv, 1.0 + 3 * fv, v));
            return (34 + 60 * lip, 30 + 52 * lip, 28 + 46 * lip);
        }
        if (u < 0 || u > 1) return (30, 28, 30);

        var index = Math.Min(whites - 1, (int)(u * whites));
        var local = u * whites - index; // 0..1 inside this white slot
        // Gap between two white keys: a dark hairline that does not reach past the front edge.
        var gap = 1 - Smooth(0.012, 0.012 + 1.2 * fu * whites, Math.Min(local, 1 - local));
        var baseTone = 236 - 22 * v + 6 * Math.Sin(index * 1.7); // a hint of difference from key to key, and a darker front
        double r = baseTone, g = baseTone - 3, b = baseTone - 11;
        r *= 1 - 0.75 * gap; g *= 1 - 0.75 * gap; b *= 1 - 0.75 * gap;

        // Black keys: stand in the back 0.62 of the key length, with a lit top and a shadow thrown to the right.
        var blackEnd = BlackLengthMm / WhiteLengthMm;
        var white = layout.WhitePitchAt(index);
        for (var side = 0; side < 2; side++)
        {
            var pitch = white + (side == 0 ? -1 : 1);
            if (!layout.Contains(pitch) || !KeyboardLayout.IsBlack(pitch)) continue;
            var half = whiteWidth * KeyboardLayout.BlackWidthShare / 2;
            var leftEdge = layout.CenterX(pitch) - half;
            var rightEdge = layout.CenterX(pitch) + half;
            if (!(v < blackEnd + 3 * fv && leftEdge - 0.4 * whiteWidth <= u && u <= rightEdge + 1.2 * whiteWidth)) continue;

            var insideU = Smooth(leftEdge - fu, leftEdge + fu, u) * (1 - Smooth(rightEdge - fu, rightEdge + fu, u));
            var insideV = 1 - Smooth(blackEnd - fv, blackEnd + fv, v);
            var body = insideU * insideV;
            var top = 1 - Smooth(0.0, 0.5 * blackEnd, v); // slight sheen toward the back
            double blackR = 18 + 22 * top * (1 - v), blackG = 18 + 22 * top * (1 - v), blackB = 22 + 26 * top * (1 - v);
            // The lit chamfer along the front end of the black key.
            var chamfer = insideU * Smooth(blackEnd - 4 * fv, blackEnd - fv, v) * (1 - Smooth(blackEnd - fv, blackEnd + fv, v));
            blackR += 55 * chamfer; blackG += 55 * chamfer; blackB += 55 * chamfer;
            // Shadow of the black key on the white one beside it, falling to the right.
            var shadowU = Smooth(rightEdge, rightEdge + 0.9 * whiteWidth, u) * (1 - Smooth(rightEdge + 0.9 * whiteWidth, rightEdge + 1.2 * whiteWidth, u));
            var shadow = 0.45 * shadowU * insideV * (1 - body);
            r = r * (1 - shadow) * (1 - body) + blackR * body;
            g = g * (1 - shadow) * (1 - body) + blackG * body;
            b = b * (1 - shadow) * (1 - body) + blackB * body;
        }
        return (r, g, b);
    }
}
