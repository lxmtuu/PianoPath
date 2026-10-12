using System.IO;
using System.IO.Compression;
using System.Text;

namespace PianoPath;

/// <summary>
/// The transport half of a Keyflow look share code: the version markers, the URL-safe
/// base64-of-gzip wrapping, the chat-line whitespace tolerance and the hard size limits. It knows
/// nothing about looks or settings — <c>Stage/VisualPresetShare.cs</c> turns the opened bytes into
/// the look — which is why this half is pure enough to be checked on any machine.
///
/// <para>
/// Version 2 marks the payload as one that carries the look and its AR graphs together (roadmap §14.9).
/// The envelope was already complete, so the version-1 payload is the same bytes behind the older marker,
/// and a code written by any older Keyflow keeps opening bit for bit — <see cref="Prefix"/> is only what
/// <em>new</em> codes leave under. Codes from any other version are refused with the version reason,
/// exactly as before.
/// </para>
/// </summary>
internal static class ShareCodeEnvelope
{
    /// <summary>Marks a new code as a Keyflow look, version 2 — the look and its AR graphs travel together.</summary>
    internal const string Prefix = "KEYFLOW-LOOK-2:";
    /// <summary>The marker every older Keyflow wrote; those codes still open, bit for bit.</summary>
    internal const string LegacyPrefix = "KEYFLOW-LOOK-1:";
    /// <summary>Longest accepted code; the real ones are a few hundred characters.</summary>
    internal const int MaxCodeLength = 32_000;
    /// <summary>Most decoded JSON that will ever be read out of a code.</summary>
    internal const int MaxPayloadBytes = 256 * 1024;

    // Every refusal states its reason in one sentence, so the caller can say which part of a paste was wrong.
    internal const string EmptyReason = "The code is empty.";
    internal const string LongReason = "The code is longer than a Keyflow look code can be.";
    internal const string VersionReason = "The code was made by a different version of Keyflow.";
    internal const string ForeignReason = "This does not look like a Keyflow look code.";
    internal const string TextReason = "The code is damaged: its text is not base64.";
    internal const string GzipReason = "The code is damaged: its payload cannot be decompressed.";
    internal const string SizeReason = "The code is larger than any Keyflow look.";
    internal const string GrowReason = "The code expands to more than any Keyflow look.";

    /// <summary>Wraps a payload as a fresh version-2 code: one line of URL-safe text.</summary>
    internal static string Wrap(byte[] payload)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(payload, 0, payload.Length);
        return Prefix + ToBase64Url(buffer.ToArray());
    }

    /// <summary>
    /// Opens a code of either known version back into its payload. A code is often pasted wrapped by a chat
    /// client, so every kind of whitespace inside it is ignored before anything is checked, and the limits
    /// are enforced while reading — a hostile code cannot exhaust memory on the way in.
    /// </summary>
    internal static bool TryUnwrap(string? code, out byte[] payload, out string error)
    {
        payload = Array.Empty<byte>();
        error = "";
        var text = new string((code ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (text.Length == 0) { error = EmptyReason; return false; }
        if (text.Length > MaxCodeLength) { error = LongReason; return false; }
        // Version 1 beside version 2: every code a Keyflow ever wrote for sharing a look keeps working.
        var prefix = text.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix
            : text.StartsWith(LegacyPrefix, StringComparison.Ordinal) ? LegacyPrefix
            : null;
        if (prefix is null)
        {
            error = text.StartsWith("KEYFLOW-", StringComparison.Ordinal) ? VersionReason : ForeignReason;
            return false;
        }
        byte[] gz;
        try { gz = FromBase64Url(text[prefix.Length..]); }
        catch (FormatException) { error = TextReason; return false; }
        if (gz.Length > MaxPayloadBytes) { error = SizeReason; return false; }
        try
        {
            using var input = new MemoryStream(gz);
            using var gunzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = gunzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > MaxPayloadBytes) { error = GrowReason; return false; }
                output.Write(chunk, 0, read);
            }
            payload = output.ToArray();
            return true;
        }
        catch (InvalidDataException) { error = GzipReason; return false; }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var standard = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(standard.PadRight((standard.Length + 3) / 4 * 4, '='));
    }
}
