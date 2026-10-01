using System.Text;

namespace PianoPath.Tests;

/// <summary>
/// Standard MIDI File bytes, written out the way the format is specified rather than captured from an
/// application, so a test says what the reader is being asked to cope with.
///
/// <para>
/// These are the same fixtures <c>Diagnostics/VerificationSuite.cs</c> feeds <c>--verify</c>; they live
/// here as well because a fixture that only the Windows suite can reach cannot guard a regression on a
/// Linux runner. Keep the two in step: a change to the reader that needs a new fixture needs it twice.
/// </para>
/// </summary>
internal static class MidiFixture
{
    /// <summary>
    /// Format 1, 480 PPQ, two tracks. Track 0 is named "Lead" and holds C4 for one beat followed by a
    /// tempo change to 240 BPM; track 1 opens with a channel-10 drum hit (which the reader must drop),
    /// then E4 and G4. At the default tempo a beat is half a second, so the tempo change lands the
    /// following notes on a quarter-second grid.
    /// </summary>
    internal static byte[] FormatOne()
    {
        var trackZero = new byte[]
        {
            0, 0xFF, 0x03, 4, (byte)'L', (byte)'e', (byte)'a', (byte)'d',
            0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0,
            0, 0xFF, 0x51, 3, 3, 0xD0, 0x90,                                  // 250000 µs per quarter = 240 BPM
            0, 0xFF, 0x2F, 0,
        };
        var trackOne = new byte[]
        {
            0, 0x99, 36, 100, 0, 0x89, 36, 0,                                // channel 10: percussion
            0, 0x90, 64, 80, 0x81, 0x70, 0x80, 64, 0,
            0x81, 0x70, 0x90, 67, 90, 0x83, 0x60, 0x80, 67, 0,
            0, 0xFF, 0x2F, 0,
        };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Header(writer, format: 1, tracks: 2, division: 480);
        Track(writer, trackZero);
        Track(writer, trackOne);
        return stream.ToArray();
    }

    /// <summary>
    /// Format 2: each track is an independent pattern, so the patterns play one after another instead
    /// of stacked. "Bass" then "Lead", a beat each.
    /// </summary>
    internal static byte[] FormatTwo()
    {
        var bass = new byte[] { 0, 0xFF, 0x03, 4, (byte)'B', (byte)'a', (byte)'s', (byte)'s', 0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0, 0, 0xFF, 0x2F, 0 };
        var lead = new byte[] { 0, 0xFF, 0x03, 4, (byte)'L', (byte)'e', (byte)'a', (byte)'d', 0, 0x90, 67, 100, 0x83, 0x60, 0x80, 67, 0, 0, 0xFF, 0x2F, 0 };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Header(writer, format: 2, tracks: 2, division: 480);
        Track(writer, bass);
        Track(writer, lead);
        return stream.ToArray();
    }

    /// <summary>
    /// An SMPTE division: the high byte is a signed frame rate (-25) and the low one the ticks per
    /// frame (40), so a tick is absolute time and a tempo change moves the metronome but not the notes.
    /// </summary>
    internal static byte[] Smpte()
    {
        var track = new byte[]
        {
            0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20,                                  // 500000 µs per quarter
            0, 0x90, 60, 100, 0x83, 0x74, 0x80, 60, 0,                           // C4 for 500 ticks = half a second
            0x83, 0x74, 0xFF, 0x51, 3, 0x03, 0xD0, 0x90,                         // 250000 µs per quarter, one second in
            0, 0x90, 67, 100, 0x83, 0x74, 0x80, 67, 0, 0, 0xFF, 0x2F, 0,
        };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Header(writer, format: 1, tracks: 1, division: 0xE728);
        Track(writer, track);
        return stream.ToArray();
    }

    /// <summary>One empty track and a header declaring whatever it is told: everything else is valid.</summary>
    internal static byte[] HeaderOnly(int format, int division)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        Header(writer, format, tracks: 1, division);
        Track(writer, [0, 0xFF, 0x2F, 0]);
        return stream.ToArray();
    }

    private static void Header(BinaryWriter writer, int format, int tracks, int division)
    {
        writer.Write(Encoding.ASCII.GetBytes("MThd"));
        Write32(writer, 6);
        Write16(writer, format);
        Write16(writer, tracks);
        Write16(writer, division);
    }

    private static void Track(BinaryWriter writer, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes("MTrk"));
        Write32(writer, data.Length);
        writer.Write(data);
    }

    private static void Write16(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void Write32(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    /// <summary>Writes the bytes to a unique file and hands back its path; the caller owns the cleanup.</summary>
    internal static string WriteToTempFile(byte[] bytes, string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"keyflow-tests-{label}-{Guid.NewGuid():N}.mid");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
