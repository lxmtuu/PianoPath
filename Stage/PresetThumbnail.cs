using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// The picture that is stored inside a preset file: the stage itself, rendered once at a fixed moment
/// and carried with the look.
///
/// <para>
/// A user preset keeps the real render instead of being redrawn from its settings, so the list shows the
/// same picture on the machine that saved it and on the machine that imported it — even when the two
/// builds differ, and without paying for a render every time the Style page is opened. The built-in
/// presets keep their procedural miniature: they ship with the application and never travel.
/// </para>
///
/// <para>
/// The render is deterministic in layout (same size, same notes, same moment) though particles are not
/// pixel-identical between runs, which is why the checks assert the picture exists and has the right
/// size rather than comparing bytes.
/// </para>
/// </summary>
internal static class PresetThumbnail
{
    internal const int Width = 192;
    internal const int Height = 112;
    /// <summary>Longest base64 payload accepted from a preset file.</summary>
    internal const int MaxBase64Length = 512 * 1024;
    /// <summary>Everything before the PNG signature would mean the text is not a picture at all.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Renders the stage for a look and returns it as base64 PNG, or an empty string when the render is
    /// not possible (a preset without a picture is still a valid preset).
    /// </summary>
    internal static string Encode(PianoVisualSettings settings)
    {
        try { return Convert.ToBase64String(RenderPng(settings)); }
        catch { return ""; }
    }

    /// <summary>The PNG bytes of the look, at <see cref="Width"/> × <see cref="Height"/>.</summary>
    internal static byte[] RenderPng(PianoVisualSettings settings)
    {
        var stage = new PianoStage { Width = Width, Height = Height };
        stage.SetVisualSettings(settings);
        // A fixed, readable moment: a few bars above the hit line and three impacts lighting the keyboard,
        // so the picture shows the note style, the palette, the glow and the keys at once.
        var notes = new List<NoteEvent>();
        foreach (var (pitch, start, length) in Showcase)
            notes.Add(new NoteEvent { Pitch = pitch, Start = start, Duration = length, Track = pitch < 60 ? 0 : 1 });
        stage.SetState(notes, 1.0, true, new HashSet<int> { 52, 60 });
        foreach (var pitch in new[] { 48, 55, 64 }) stage.Impact(pitch, .9);
        stage.Advance(.12); stage.Advance(.12);
        stage.Measure(new Size(Width, Height));
        stage.Arrange(new Rect(0, 0, Width, Height));
        stage.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Reads a stored picture back. Anything that is not a PNG of the expected size is refused, so a
    /// hand-edited preset file can only lose its picture, never produce a broken image in the list.
    /// </summary>
    internal static BitmapSource? Decode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64) || base64.Length > MaxBase64Length) return null;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length < PngSignature.Length || !bytes.Take(PngSignature.Length).SequenceEqual(PngSignature)) return null;
            using var stream = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (frame.PixelWidth != Width || frame.PixelHeight != Height) return null;
            frame.Freeze();
            return frame;
        }
        catch { return null; }
    }

    /// <summary>The notes the picture shows: two hands, a melody over a bass line, all inside one bar.</summary>
    private static readonly (int Pitch, double Start, double Length)[] Showcase =
    [
        (43, .10, .70), (50, .10, .70), (55, .30, .55), (60, .55, .45),
        (67, .20, .40), (71, .45, .35), (74, .65, .30), (79, .80, .20),
    ];
}
