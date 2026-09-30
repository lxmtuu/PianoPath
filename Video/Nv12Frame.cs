namespace PianoPath;

/// <summary>
/// Turns the stage's own pixels into the frame layout the H.264 encoder takes: NV12 — one full-size plane of
/// brightness samples followed by one plane in which a pair of colour samples is shared by every 2×2 block of
/// pixels. That is the format Media Foundation's encoder accepts without a colour converter in between, so a
/// machine whose media stack is missing that converter can still encode.
///
/// <para>
/// The arithmetic is BT.601 in video range — the same conversion a media stack does — and it is pure, so the
/// checks can read the numbers instead of a file: black lands on 16, white on 235, and a neutral grey has both
/// colour samples on 128.
/// </para>
/// </summary>
internal static class Nv12Frame
{
    /// <summary>The bytes one frame of this size takes: a full Y plane and a quarter as much again for the colours.</summary>
    internal static int Size(int width, int height) => width * height * 3 / 2;

    /// <summary>
    /// Converts one frame of packed 32-bit pixels (blue, green, red, then an unused fourth byte, top row first)
    /// into <paramref name="target"/>, which must hold <see cref="Size"/> bytes.
    /// </summary>
    internal static void FromBgra(byte[] source, int width, int height, byte[] target)
    {
        if (width < 2 || height < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (source.Length < width * height * 4) throw new ArgumentException(Loc.F("Expected a {0}×{1} BGRA frame ({2} bytes).", width, height, width * height * 4), nameof(source));
        if (target.Length < Size(width, height)) throw new ArgumentException(Loc.F("Expected room for a {0}×{1} NV12 frame ({2} bytes).", width, height, Size(width, height)), nameof(target));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = (y * width + x) * 4;
                var blue = source[pixel]; var green = source[pixel + 1]; var red = source[pixel + 2];
                target[y * width + x] = (byte)Math.Clamp(((66 * red + 129 * green + 25 * blue + 128) >> 8) + 16, 0, 255);
            }
        }
        // The colour plane: one U and one V for each 2×2 block, the average of the four pixels in it.
        var chroma = width * height;
        for (var y = 0; y < height; y += 2)
        {
            for (var x = 0; x < width; x += 2)
            {
                var red = 0; var green = 0; var blue = 0;
                for (var row = 0; row < 2; row++)
                {
                    for (var column = 0; column < 2; column++)
                    {
                        var pixel = ((y + row) * width + x + column) * 4;
                        blue += source[pixel]; green += source[pixel + 1]; red += source[pixel + 2];
                    }
                }
                red /= 4; green /= 4; blue /= 4;
                var index = chroma + y / 2 * width + x;
                target[index] = (byte)Math.Clamp(((-38 * red - 74 * green + 112 * blue + 128) >> 8) + 128, 0, 255);
                target[index + 1] = (byte)Math.Clamp(((112 * red - 94 * green - 18 * blue + 128) >> 8) + 128, 0, 255);
            }
        }
    }
}
