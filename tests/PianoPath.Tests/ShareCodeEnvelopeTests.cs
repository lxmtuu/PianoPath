using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the transport half of a look share code (Stage/ShareCodeEnvelope.cs, roadmap §14.9): fresh codes
/// leave as KEYFLOW-LOOK-2 — the marker that the look now comes with its AR graphs — while every marker an
/// older Keyflow wrote still opens, and sloppy or hostile text is refused with the reason that fits it,
/// before any settings code ever sees a byte.
/// </summary>
public class ShareCodeEnvelopeTests
{
    [Fact]
    public void CodesLeaveAsVersionTwoUrlSafeSingleLines()
    {
        var code = ShareCodeEnvelope.Wrap(Encoding.UTF8.GetBytes("{\"NoteGlow\":42}"));
        Assert.StartsWith("KEYFLOW-LOOK-2:", code);
        Assert.DoesNotContain('+', code);           // base64url: drops straight into a chat or a URL
        Assert.DoesNotContain('/', code);
        Assert.DoesNotContain('=', code);
        Assert.True(ShareCodeEnvelope.TryUnwrap(code, out var payload, out var error) && error == "");
        Assert.Equal("{\"NoteGlow\":42}", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void EveryVersionOneCodeStillOpensBitForBit()
    {
        var json = Encoding.UTF8.GetBytes("{\"NoteGlow\":77}");
        var legacy = ShareCodeEnvelope.LegacyPrefix + ShareCodeEnvelope.Wrap(json)[ShareCodeEnvelope.Prefix.Length..];
        Assert.True(ShareCodeEnvelope.TryUnwrap(legacy, out var payload, out var error) && error == "",
            "A code pasted while the format was young must keep opening.");
        Assert.Equal(json, payload);
    }

    [Fact]
    public void CodesFromOtherKeyflowVersionsKeepTheirReason()
    {
        Assert.False(ShareCodeEnvelope.TryUnwrap("KEYFLOW-LOOK-9:AAAA", out _, out var future));
        Assert.Equal(ShareCodeEnvelope.VersionReason, future);
        Assert.False(ShareCodeEnvelope.TryUnwrap("KEYFLOW-TUNE-1:AAAA", out _, out var foreign));
        Assert.Equal(ShareCodeEnvelope.VersionReason, foreign);
        Assert.False(ShareCodeEnvelope.TryUnwrap("paste anything", out _, out var unrelated));
        Assert.Equal(ShareCodeEnvelope.ForeignReason, unrelated);
        Assert.False(ShareCodeEnvelope.TryUnwrap(null, out _, out var missing));
        Assert.Equal(ShareCodeEnvelope.EmptyReason, missing);
    }

    [Fact]
    public void ACodeAChatClientWrappedStillReads()
    {
        var code = ShareCodeEnvelope.Wrap(Encoding.UTF8.GetBytes("wrapped"));
        var wrapped = "  \n" + code[..24] + "\r\n\t" + code[24..] + "  \n";
        Assert.True(ShareCodeEnvelope.TryUnwrap(wrapped, out var payload, out _));
        Assert.Equal("wrapped", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void DamagedCodesSayWhatBroke()
    {
        Assert.False(ShareCodeEnvelope.TryUnwrap(ShareCodeEnvelope.Prefix + "not base64!!", out _, out var text));
        Assert.Equal(ShareCodeEnvelope.TextReason, text);
        var junk = Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not gzipped json"));
        Assert.False(ShareCodeEnvelope.TryUnwrap(ShareCodeEnvelope.Prefix + junk, out _, out var gzip));
        Assert.Equal(ShareCodeEnvelope.GzipReason, gzip);
        var tooLong = ShareCodeEnvelope.Prefix + new string('A', ShareCodeEnvelope.MaxCodeLength);
        Assert.False(ShareCodeEnvelope.TryUnwrap(tooLong, out _, out var size));
        Assert.Equal(ShareCodeEnvelope.LongReason, size);
    }

    [Fact]
    public void AZipBombStopsAtThePayloadLimitInsteadOfGrowing()
    {
        // Three hundred thousand zero bytes pack small but must never decompress into memory whole.
        byte[] bomb;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                var zeros = new byte[300_000];
                gzip.Write(zeros, 0, zeros.Length);
            }
            bomb = buffer.ToArray();
        }
        Assert.True(bomb.Length < ShareCodeEnvelope.MaxPayloadBytes,
            "The bomb is small enough to pass the outer size gate — the growth guard must be the one that trips.");
        var code = ShareCodeEnvelope.Prefix + Convert.ToBase64String(bomb).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.False(ShareCodeEnvelope.TryUnwrap(code, out _, out var grew));
        Assert.Equal(ShareCodeEnvelope.GrowReason, grew);
    }
}
