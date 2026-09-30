using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Writes a recording as a numbered folder of 32-bit PNG frames instead of a video file, for compositing
/// in Premiere, Resolve, After Effects or OBS: the alpha channel travels with every frame, so the piano can
/// be layered over somebody's own footage or over their face in a video call.
///
/// <para>
/// The frames are exactly what the stage drew for the chosen size and frame rate; a frame the renderer
/// could not keep up with is written as a copy, the same rule the AVI recorder follows, so a sequence
/// imported at its stated fps plays at the right speed. Files are named <c>frame-000001.png</c> — an ffmpeg
/// command that turns them into alpha video again is written into <c>sequence.json</c> next to them.
/// </para>
///
/// <para>
/// There is no 2 GB limit here (that is an AVI format limit), so <see cref="IsNearSizeLimit"/> stays false
/// and recording stops when the user stops it.
/// </para>
/// </summary>
internal sealed class PngSequenceRecorder : IFrameRecorder
{
    private readonly string _directory;
    private readonly byte[] _row = [];
    private bool _disposed;

    internal const string Pattern = "frame-000001.png";
    internal const string ManifestName = "sequence.json";

    public int Width { get; }
    public int Height { get; }
    public int FrameRate { get; }
    public int FrameCount { get; private set; }
    public long BytesWritten { get; private set; }

    /// <summary>Always false: a folder of PNG files has no format limit to run into.</summary>
    public bool IsNearSizeLimit => false;

    public string OutputPath => _directory;

    /// <summary>A tightly packed BGRA frame, which is what <see cref="PianoStage"/> renders.</summary>
    public int FrameBytes => Width * 4 * Height;

    public bool HasAlpha => true;

    public PngSequenceRecorder(string directory, int width, int height, int frameRate)
    {
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (frameRate is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(frameRate));
        _directory = directory; Width = width; Height = height; FrameRate = frameRate;
        System.IO.Directory.CreateDirectory(_directory);
    }

    public void WriteFrame(byte[] frame, int repeat = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.Length != FrameBytes) throw new ArgumentException(Loc.F("Expected a {0}×{1} BGRA frame ({2} bytes).", Width, Height, FrameBytes), nameof(frame));
        if (repeat < 1) return;
        // The pixels come from a RenderTargetBitmap, so they are already premultiplied: writing them as
        // Pbgra32 keeps the glow around a note composited exactly as the stage drew it.
        for (var copy = 0; copy < repeat; copy++)
        {
            var source = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, frame, Width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            var path = Path.Combine(_directory, $"frame-{++FrameCount:D6}.png");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
                BytesWritten += stream.Length;
            }
        }
    }

    /// <summary>
    /// Writes the manifest that tells a compositor what it is looking at: size, frame rate, frame count and
    /// the command that turns the folder back into an alpha video.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            var manifest = new
            {
                format = "png32",
                width = Width,
                height = Height,
                fps = FrameRate,
                frames = FrameCount,
                pattern = Pattern,
                alpha = true,
                ffmpeg = $"ffmpeg -framerate {FrameRate} -i frame-%06d.png -c:v libvpx-vp9 -pix_fmt yuva420p keyflow.webm"
            };
            File.WriteAllText(Path.Combine(_directory, ManifestName), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* a missing manifest must not lose the frames that were recorded */ }
    }
}
