namespace PianoPath;

/// <summary>
/// The pictures of the tiny camera video that render-to-video verification plays back with the app's own
/// <see cref="CameraFrameReader"/> (roadmap §14.10 AR‑S7, export UI): a flat grey that brightens with every
/// tick plus a bright stripe on the left twelfth, so a take made from the file must show its frames
/// ticking in order. The pictures are pure, so the Linux test job locks the recipe while the Windows
/// verify presses them into a real MP4 with the app's own writer and decodes that with the app's own
/// reader — writer and reader agreeing is the whole point of the card.
/// </summary>
internal static class CameraTestVideo
{
    /// <summary>Grey level of frame <paramref name="tick"/>: an eight-level step per tick, starting at 96.</summary>
    internal static int GreyOf(int tick) => Math.Min(255, 96 + tick * 8);

    /// <summary>The stripe's extra brightness over the baseline grey (also the contrast the take probe reads).</summary>
    internal const int StripeBoost = 60;

    /// <summary>
    /// One top-down BGRA frame of <paramref name="width"/>×<paramref name="height"/> at frame
    /// <paramref name="tick"/>: every pixel <see cref="GreyOf"/>, the left twelfth lifted by
    /// <see cref="StripeBoost"/>, alpha opaque.
    /// </summary>
    internal static byte[] Picture(int width, int height, int tick)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var level = x < width / 12 ? Math.Min(255, GreyOf(tick) + StripeBoost) : GreyOf(tick);
                var i = (y * width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)level;
                pixels[i + 3] = 255;
            }
        return pixels;
    }
}
