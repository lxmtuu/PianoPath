using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the look share code (Stage/VisualPresetShare.cs, roadmap §14.9): fresh codes leave as
/// KEYFLOW-LOOK-2 — the marker that the look now comes with its AR graphs — while every code an older
/// Keyflow wrote still opens bit for bit, the sender's pictures and cameras never travel, and text that
/// is not a decodable code is refused with the reason that fits it.
/// </summary>
public class VisualPresetShareTests
{
    [Fact]
    public void CodesLeaveAsVersionTwoAndCarryTheArGraphs()
    {
        var look = new PianoVisualSettings
        {
            NoteGlow = 123,
            ArNoteWidthByVelocity = true,
            ArNoteWidthSoft = 72,
            ArNoteWidthHard = 146,
            ArNoteFadeByVelocity = true,
            ArNoteFadeHard = 170,
            ArLiveFadeByLifetime = true,
            ArLiveFadeStart = 33,
            ArLiveFadeEnd = 88,
        };
        var code = VisualPresetShare.Encode(look);
        Assert.StartsWith("KEYFLOW-LOOK-2:", code);
        Assert.DoesNotContain('+', code);           // base64url: drops straight into a chat or a URL
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
        Assert.True(VisualPresetShare.TryDecode(code, out var decoded, out var error) && error == "");
        Assert.Equal(look.NoteGlow, decoded.NoteGlow);
        Assert.True(decoded.ArNoteWidthByVelocity && decoded.ArNoteFadeByVelocity && decoded.ArLiveFadeByLifetime);
        Assert.Equal(72, decoded.ArNoteWidthSoft);
        Assert.Equal(146, decoded.ArNoteWidthHard);
        Assert.Equal(170, decoded.ArNoteFadeHard);
        Assert.Equal(33, decoded.ArLiveFadeStart);
        Assert.Equal(88, decoded.ArLiveFadeEnd);
    }

    [Fact]
    public void EveryVersionOneCodeStillOpensBitForBit()
    {
        var look = new PianoVisualSettings { NoteGlow = 77, ArNoteWidthHard = 150 };
        // A v1 code is exactly what an older Keyflow wrote: the same payload behind the older marker.
        var legacy = VisualPresetShare.LegacyPrefix + VisualPresetShare.Encode(look)[VisualPresetShare.Prefix.Length..];
        Assert.True(VisualPresetShare.TryDecode(legacy, out var decoded, out var error) && error == "",
            "A code pasted while the format was young must keep opening.");
        Assert.Equal(77, decoded.NoteGlow);
        Assert.Equal(150, decoded.ArNoteWidthHard);
    }

    [Fact]
    public void CodesFromOtherKeyflowVersionsKeepTheirReason()
    {
        Assert.False(VisualPresetShare.TryDecode("KEYFLOW-LOOK-9:AAAA", out _, out var future));
        Assert.Equal("The code was made by a different version of Keyflow.", future);
        Assert.False(VisualPresetShare.TryDecode("KEYFLOW-TUNE-1:AAAA", out _, out var foreign));
        Assert.Equal("The code was made by a different version of Keyflow.", foreign);
        Assert.False(VisualPresetShare.TryDecode("paste anything", out _, out var unrelated));
        Assert.Equal("This does not look like a Keyflow look code.", unrelated);
        Assert.False(VisualPresetShare.TryDecode(null, out _, out var missing));
        Assert.Equal("The code is empty.", missing);
    }

    [Fact]
    public void TheMachinesPicturesAndCamerasStayHome()
    {
        var look = new PianoVisualSettings
        {
            BackgroundImagePath = @"C:\pictures\mine.png",
            BackgroundMode = "Image",
            CameraVideoPath = @"C:\videos\mine.mp4",
            CameraSourceLink = @"\\?\usb#cam",
        };
        var code = VisualPresetShare.Encode(look);
        Assert.True(VisualPresetShare.TryDecode(code, out var decoded, out _));
        Assert.Equal("", decoded.BackgroundImagePath);
        Assert.Equal("Solid", decoded.BackgroundMode);
        Assert.Equal("", decoded.CameraVideoPath);
        Assert.Equal("", decoded.CameraSourceLink);
    }

    [Fact]
    public void ACodeAChatClientWrappedStillReads()
    {
        var code = VisualPresetShare.Encode(new PianoVisualSettings { NoteGlow = 99 });
        var wrapped = "  \n" + code[..40] + "\r\n\t" + code[40..] + "  \n";
        Assert.True(VisualPresetShare.TryDecode(wrapped, out var decoded, out _));
        Assert.Equal(99, decoded.NoteGlow);
    }

    [Fact]
    public void DamagedCodesSayWhatBroke()
    {
        Assert.False(VisualPresetShare.TryDecode(VisualPresetShare.Prefix + "not base64!!", out _, out var text));
        Assert.Equal("The code is damaged: its text is not base64.", text);
        var junk = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not gzipped json"));
        Assert.False(VisualPresetShare.TryDecode(VisualPresetShare.Prefix + junk, out _, out var gzip));
        Assert.Equal("The code is damaged: its payload cannot be decompressed.", gzip);
        Assert.False(VisualPresetShare.TryDecode(PackVersionTwo("plain text, not a look"), out _, out var json));
        Assert.Equal("The code is damaged: its payload is not a Keyflow look.", json);
        var tooLong = VisualPresetShare.Prefix + new string('A', VisualPresetShare.MaxCodeLength);
        Assert.False(VisualPresetShare.TryDecode(tooLong, out _, out var size));
        Assert.Equal("The code is longer than a Keyflow look code can be.", size);
    }

    /// <summary>Builds a version-2 code whose gzip decompression succeeds but whose bytes are not a look.</summary>
    private static string PackVersionTwo(string payload)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return VisualPresetShare.Prefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
