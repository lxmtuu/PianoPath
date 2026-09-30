using System.Windows;

namespace PianoPath;

/// <summary>
/// The webcam overlay: a picture-in-picture of a live camera (or a video file) drawn over the stage, keyed
/// against the same green the Green Screen preset paints.
///
/// <para>
/// Everything here is plain arithmetic over bytes and numbers: where the picture sits for each corner, how a
/// frame is turned into top-down BGRA (a camera may hand out bottom-up rows, and a mirror flips them), and
/// which pixels the key removes. The stage only draws the result, and the checks state the answers instead of
/// looking at pixels.
/// </para>
/// </summary>
internal static class CameraOverlay
{
    /// <summary>The corners the overlay can sit in, in the order the picker offers them.</summary>
    internal static readonly string[] Corners = ["Bottom left", "Bottom right", "Top left", "Top right"];

    /// <summary>The key colour: pure green, the same colour the Green Screen preset fills the stage with.</summary>
    internal static readonly (byte B, byte G, byte R) KeyColour = (0, 255, 0);

    /// <summary>Distance in RGB that still counts as the key colour at tolerance 100.</summary>
    internal const double MaxKeyDistance = 220;

    /// <summary>An inset from the stage edge, as a share of its width, so the picture never touches the frame.</summary>
    internal const double MarginShare = .025;

    /// <summary>
    /// Where the overlay is drawn: a rectangle of <paramref name="widthShare"/> percent of the stage's width,
    /// as tall as the frame's own aspect ratio makes it (never taller than the stage), in the chosen corner and
    /// inset by <see cref="MarginShare"/>. An unknown corner falls back to the bottom left.
    /// </summary>
    internal static Rect Place(string? corner, double stageWidth, double stageHeight, double widthShare, double frameAspect)
    {
        var width = Math.Max(0, stageWidth) * Math.Clamp(widthShare, 1, 100) / 100;
        var aspect = double.IsFinite(frameAspect) && frameAspect > .01 ? frameAspect : 16.0 / 9;
        var height = Math.Min(width / aspect, Math.Max(0, stageHeight) * .8);
        width = Math.Min(width, Math.Max(0, stageWidth) - 2 * Margin());
        var margin = Margin();
        var right = corner?.EndsWith("right", StringComparison.OrdinalIgnoreCase) == true;
        var top = corner?.StartsWith("top", StringComparison.OrdinalIgnoreCase) == true;
        var x = right ? Math.Max(margin, stageWidth - width - margin) : margin;
        var y = top ? margin : Math.Max(margin, stageHeight - height - margin);
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>The inset used by <see cref="Place"/>, for a stage of a known width.</summary>
    internal static double Margin(double width = 1000) => width * MarginShare;

    /// <summary>
    /// Turns one decoded frame into the buffer the stage draws: top-down 32-bit BGRA of exactly
    /// <paramref name="width"/> × <paramref name="height"/> pixels.
    ///
    /// <para>
    /// <paramref name="sourceStride"/> is the stride Media Foundation reported for the sample; a negative
    /// stride means the rows come bottom-up, which is what a compressed-origin codec produces, so they are
    /// reversed. <paramref name="mirror"/> flips each row, the way a camera pointed at the player should look.
    /// Rows and pixels outside the frame are left alone, so a caller can hand in a buffer that is larger than
    /// the frame without the extra being painted. Every pixel of the frame leaves opaque: the fourth byte of a
    /// camera frame means nothing, and the stage draws the picture as premultiplied alpha, where a stray zero
    /// would make it disappear.
    /// </para>
    /// </summary>
    internal static void CopyFrame(byte[] source, int sourceStride, int width, int height, byte[] destination, bool mirror)
    {
        if (width <= 0 || height <= 0) return;
        var stride = sourceStride == 0 ? width * 4 : Math.Abs(sourceStride);
        var bottomUp = sourceStride < 0;
        for (var row = 0; row < height; row++)
        {
            var sourceRow = (bottomUp ? height - 1 - row : row) * stride;
            var destinationRow = row * width * 4;
            if (sourceRow < 0 || sourceRow + stride > source.Length) continue;
            if (destinationRow + width * 4 > destination.Length) break;
            if (!mirror)
            {
                Buffer.BlockCopy(source, sourceRow, destination, destinationRow, Math.Min(stride, width * 4));
                continue;
            }
            for (var x = 0; x < width; x++)
                Buffer.BlockCopy(source, sourceRow + x * 4, destination, destinationRow + (width - 1 - x) * 4, 4);
        }
        // Every pixel arrives opaque: the stage draws the frame as premultiplied alpha, so a fourth byte the
        // camera filled with anything at all would make the picture invisible. What the key removes is removed
        // afterwards, by ApplyKey.
        var end = Math.Min(Math.Max(0, width * height) * 4, destination.Length);
        for (var offset = 3; offset < end; offset += 4) destination[offset] = 255;
    }

    /// <summary>
    /// Whether a pixel counts as the key colour at this tolerance: 0 never matches (keying is off), 100 matches
    /// everything within <see cref="MaxKeyDistance"/> of pure green, and alpha is left out of the comparison
    /// because a camera's alpha channel means nothing.
    /// </summary>
    internal static bool IsKeyed(byte b, byte g, byte r, double tolerance)
    {
        if (tolerance <= 0) return false;
        var limit = Math.Clamp(tolerance, 0, 100) / 100 * MaxKeyDistance;
        var db = b - KeyColour.B; var dg = g - KeyColour.G; var dr = r - KeyColour.R;
        // Green dominates in a keyed pixel, so a pixel whose green does not stand out is never keyed even if
        // the distance is small (a dark grey would otherwise fall inside a wide tolerance).
        if (g <= b + 12 && g <= r + 12) return false;
        return db * db + dg * dg + dr * dr <= limit * limit;
    }

    /// <summary>
    /// Removes the key from a whole frame in place by clearing every keyed pixel — its colour and its alpha —
    /// which is what makes the picture see-through over the stage. The colour goes too, because the frame is
    /// handed to Windows as premultiplied alpha, where a pixel with no alpha must carry no colour either.
    /// Returns how many pixels were removed, so a caller can say whether the key did anything at all.
    /// </summary>
    internal static int ApplyKey(byte[] bgra, int pixels, double tolerance)
    {
        if (tolerance <= 0) return 0;
        var removed = 0;
        var count = Math.Min(pixels, bgra.Length / 4);
        for (var index = 0; index < count; index++)
        {
            var offset = index * 4;
            if (!IsKeyed(bgra[offset], bgra[offset + 1], bgra[offset + 2], tolerance)) continue;
            bgra[offset] = 0; bgra[offset + 1] = 0; bgra[offset + 2] = 0; bgra[offset + 3] = 0;
            removed++;
        }
        return removed;
    }

    /// <summary>Opacity of the overlay as a 0–1 factor; the settings hold a percentage.</summary>
    internal static double OpacityFactor(double percent) => Math.Clamp(percent, 0, 100) / 100;
}
