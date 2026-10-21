using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Writing and re-reading the PNGs a WARP review produces, in one place.
///
/// <para>
/// The manifest of a review has to name the picture a reviewer opens, not the buffer the renderer held, so the
/// checksum is always taken after the file has been decoded again. The two are not the same buffer: PNG stores
/// straight alpha, a codec is free to round, and the flat matrix already failed once by hashing the wrong one.
/// </para>
/// </summary>
internal static class ReviewPng
{
    /// <summary>Encodes BGRA pixels, writes them, and returns the SHA-256 of the pixels the file decodes back to.
    /// The software stage hands out premultiplied BGRA and the Direct3D readback hands out straight BGRA, so a
    /// review that stores both names the format it was given instead of pretending they are the same buffer.</summary>
    internal static string Write(string path, byte[] pixels, int width, int height, string what,
        PixelFormat? format = null)
    {
        if (pixels.Length != width * height * 4)
            throw new InvalidDataException($"{what} returned {pixels.Length} bytes; expected {width}×{height} BGRA.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = BitmapSource.Create(width, height, 96, 96, format ?? PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return Convert.ToHexString(SHA256.HashData(Decode(path, width, height)));
    }

    /// <summary>The pixels a written PNG decodes to, in BGRA; also the one size check.</summary>
    internal static byte[] Decode(string path, int width, int height)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (frame.PixelWidth != width || frame.PixelHeight != height)
            throw new InvalidDataException($"{Path.GetFileName(path)} is {frame.PixelWidth}×{frame.PixelHeight}, not {width}×{height}.");
        var pixels = new byte[width * height * 4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, width * 4, 0);
        return pixels;
    }
}
