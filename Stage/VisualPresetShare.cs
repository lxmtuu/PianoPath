using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PianoPath;

/// <summary>
/// Shares a look as one line of text: the settings JSON, gzipped and base64url-encoded behind a version
/// prefix, so a preset can travel through a chat message or a forum post instead of a file.
///
/// <para>
/// Two rules keep a code safe to paste. The <see cref="BackgroundImagePath"/> is dropped before
/// encoding — it points at a file on the sender's disk, so the recipient could never load it, and a look
/// that used an image falls back to its solid colour instead. And the decoded text is clamped and
/// migrated through <see cref="PianoVisualSettings.Clamp"/> exactly like a file that was just loaded, so
/// a hand-edited or truncated code cannot put the renderer into a state the settings file could not
/// produce. Codes are sized and read with hard limits, so a hostile one cannot exhaust memory.
/// </para>
/// </summary>
internal static class VisualPresetShare
{
    /// <summary>Marks the payload as a Keyflow look and as version 1 of the format.</summary>
    internal const string Prefix = "KEYFLOW-LOOK-1:";
    /// <summary>Longest accepted code; the real ones are a few hundred characters.</summary>
    internal const int MaxCodeLength = 32_000;
    /// <summary>Most decoded JSON that will ever be read out of a code.</summary>
    internal const int MaxPayloadBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The shareable code for a look, with the background image path removed.</summary>
    internal static string Encode(PianoVisualSettings settings)
    {
        var payload = settings.Clone();
        payload.BackgroundImagePath = "";
        if (payload.BackgroundMode == "Image") payload.BackgroundMode = "Solid";
        var json = Encoding.UTF8.GetBytes(payload.ToJson());
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(json, 0, json.Length);
        return Prefix + ToBase64Url(buffer.ToArray());
    }

    /// <summary>
    /// Reads a code back into a look. <paramref name="error"/> carries the reason a code was refused, so
    /// the caller can tell the user which part of the text was wrong instead of failing silently.
    /// </summary>
    internal static bool TryDecode(string? code, out PianoVisualSettings look, out string error)
    {
        look = new PianoVisualSettings();
        error = "";
        // A code is often pasted wrapped by a chat client, so every kind of whitespace inside it is ignored.
        var text = new string((code ?? "").Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (text.Length == 0) { error = "The code is empty."; return false; }
        if (text.Length > MaxCodeLength) { error = "The code is longer than a Keyflow look code can be."; return false; }
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = text.StartsWith("KEYFLOW-", StringComparison.Ordinal)
                ? "The code was made by a different version of Keyflow."
                : "This does not look like a Keyflow look code.";
            return false;
        }
        byte[] json;
        try { json = FromBase64Url(text[Prefix.Length..]); }
        catch (FormatException) { error = "The code is damaged: its text is not base64."; return false; }
        if (json.Length > MaxPayloadBytes) { error = "The code is larger than any Keyflow look."; return false; }
        try
        {
            using var input = new MemoryStream(json);
            using var gunzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = gunzip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (output.Length + read > MaxPayloadBytes) { error = "The code expands to more than any Keyflow look."; return false; }
                output.Write(chunk, 0, read);
            }
            var settings = PianoVisualSettings.FromJson(Encoding.UTF8.GetString(output.ToArray()));
            settings.Clamp();
            // A shared look never carries a picture; the background image path is quoted text the recipient
            // does not have, so the look keeps its colours and falls back to the solid background.
            settings.BackgroundImagePath = "";
            if (settings.BackgroundMode == "Image") settings.BackgroundMode = "Solid";
            look = settings;
            return true;
        }
        catch (InvalidDataException) { error = "The code is damaged: its payload cannot be decompressed."; return false; }
        catch (JsonException) { error = "The code is damaged: its payload is not a Keyflow look."; return false; }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var standard = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(standard.PadRight((standard.Length + 3) / 4 * 4, '='));
    }
}
