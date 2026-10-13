using System.Text;
using System.Text.Json;

namespace PianoPath;

/// <summary>
/// Shares a look as one line of text: the settings JSON inside <see cref="ShareCodeEnvelope"/>'s version-2
/// wrapper, so a preset can travel through a chat message or a forum post instead of a file.
///
/// <para>
/// Two rules keep a code safe to paste. The <see cref="BackgroundImagePath"/> is dropped before
/// encoding — it points at a file on the sender's disk, so the recipient could never load it, and a look
/// that used an image falls back to its solid colour instead. And the decoded text is clamped and
/// migrated through <see cref="PianoVisualSettings.Clamp"/> exactly like a file that was just loaded, so
/// a hand-edited or truncated code cannot put the renderer into a state the settings file could not
/// produce.
/// </para>
/// </summary>
internal static class VisualPresetShare
{
    /// <summary>Marks a new code as a Keyflow look, version 2 — the look and its AR graphs travel together.</summary>
    internal const string Prefix = ShareCodeEnvelope.Prefix;
    /// <summary>The marker every older Keyflow wrote; those codes still open, bit for bit.</summary>
    internal const string LegacyPrefix = ShareCodeEnvelope.LegacyPrefix;
    /// <summary>Longest accepted code; the real ones are a few hundred characters.</summary>
    internal const int MaxCodeLength = ShareCodeEnvelope.MaxCodeLength;
    /// <summary>Most decoded JSON that will ever be read out of a code.</summary>
    internal const int MaxPayloadBytes = ShareCodeEnvelope.MaxPayloadBytes;

    internal const string JsonReason = "The code is damaged: its payload is not a Keyflow look.";

    /// <summary>The shareable code for a look, with the background image path removed.</summary>
    internal static string Encode(PianoVisualSettings settings)
    {
        var payload = settings.Clone();
        payload.ClearMachineLocal();
        if (payload.BackgroundMode == "Image") payload.BackgroundMode = "Solid";
        return ShareCodeEnvelope.Wrap(Encoding.UTF8.GetBytes(payload.ToJson()));
    }

    /// <summary>
    /// Reads a code back into a look. <paramref name="error"/> carries the reason a code was refused, so
    /// the caller can tell the user which part of the text was wrong instead of failing silently.
    /// </summary>
    internal static bool TryDecode(string? code, out PianoVisualSettings look, out string error)
    {
        look = new PianoVisualSettings();
        if (!ShareCodeEnvelope.TryUnwrap(code, out var json, out error)) return false;
        try
        {
            var settings = PianoVisualSettings.FromJson(Encoding.UTF8.GetString(json));
            settings.Clamp();
            // A shared look never carries a picture; the background image path is quoted text the recipient
            // does not have, so the look keeps its colours and falls back to the solid background.
            settings.ClearMachineLocal();       // nor a camera: the link and the video path are this machine's
            if (settings.BackgroundMode == "Image") settings.BackgroundMode = "Solid";
            look = settings;
            return true;
        }
        catch (JsonException) { error = JsonReason; return false; }
    }
}
