namespace PianoPath;

/// <summary>
/// Pure pixel packing for frame pipelines: converting tightly packed BGRA (what a GPU readback delivers)
/// into the bottom-up, stride-aligned BGR rows an AVI frame is made of, and 2×2 box-average downsampling
/// for output at a smaller size. No device state, no threads — everything here is byte arithmetic the
/// portable tests can check on any machine.
/// </summary>
internal static class BgraPack
{
    /// <summary>Bytes one AVI row takes: three per pixel, rounded up to a multiple of four.</summary>
    internal static int AviStride(int width) => (width * 3 + 3) / 4 * 4;

    /// <summary>
    /// Repacks tightly packed top-down BGRA into bottom-up, stride-aligned BGR. AVI frames are stored
    /// upside down, so the last source row lands first; the padding bytes of each row stay zero, so two
    /// runs over the same picture produce byte-identical frames.
    /// </summary>
    internal static byte[] ToBottomUpBgr(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < 1 || height < 1 || bgra.Length < width * 4L * height) throw new ArgumentOutOfRangeException(nameof(bgra));
        var stride = AviStride(width);
        var target = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var s = y * width * 4;
            var d = (height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                target[d + x * 3] = bgra[s + x * 4];         // B
                target[d + x * 3 + 1] = bgra[s + x * 4 + 1]; // G
                target[d + x * 3 + 2] = bgra[s + x * 4 + 2]; // R
            }
        }
        return target;
    }

    /// <summary>
    /// Averages every 2×2 block of a tightly packed BGRA frame into one pixel — the same box the visual
    /// review uses for its half-size probes. Two passes make a 4×4 box. Alpha is averaged like the
    /// colours: these are finished frames, not layers to composite.
    /// </summary>
    internal static byte[] Box2(byte[] source, int width, int height)
    {
        if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0 || source.Length < width * 4L * height)
            throw new ArgumentOutOfRangeException(nameof(source), "A 2×2 box needs an even-sized BGRA frame of at least two pixels per side.");
        var outWidth = width / 2;
        var outHeight = height / 2;
        var target = new byte[(long)outWidth * 4 * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            var row0 = (y * 2) * width * 4;
            var row1 = row0 + width * 4;
            for (var x = 0; x < outWidth; x++)
            {
                var a = row0 + x * 8;
                var t = (long)y * outWidth * 4 + x * 4;
                for (var ch = 0; ch < 4; ch++)
                    target[t + ch] = (byte)((source[a + ch] + source[a + 4 + ch] + source[row1 + x * 8 + ch] + source[row1 + x * 8 + 4 + ch] + 2) / 4);
            }
        }
        return target;
    }
}
