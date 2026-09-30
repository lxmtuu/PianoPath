using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PianoPath;

/// <summary>
/// One file that carries a whole setup: the stage settings, the interface language and the shell
/// theme. It is the answer to "move my look to the other machine" — the presets folder travels by
/// itself, but the language, the theme and the current look are three different stores otherwise.
///
/// <para>
/// The file is plain, indented JSON with a marker and a schema version, so a file that is not a
/// Keyflow profile is rejected with a message instead of being half-applied. The stage settings are
/// embedded as the same object <c>visual-settings.json</c> holds, which means loading a profile gets
/// the migrations and range clamping of <see cref="PianoVisualSettings.FromJson"/> for free.
/// </para>
/// </summary>
internal sealed record SettingsProfile(PianoVisualSettings Visual, string Language, string ShellTheme)
{
    /// <summary>Marker written into every profile; a file without it is not ours.</summary>
    internal const string Kind = "keyflow-profile";
    internal const int SchemaVersion = 1;
    /// <summary>Default file name offered by the export dialog.</summary>
    internal const string SuggestedName = "Keyflow.profile.json";

    /// <summary>Snapshot of the live window: the language rides along, the theme id is the published one.</summary>
    internal static SettingsProfile Capture(PianoVisualSettings settings, string shellTheme) =>
        new(settings.Clone(), settings.Language, shellTheme);

    internal string ToJson()
    {
        var root = new JsonObject
        {
            ["kind"] = Kind,
            ["version"] = SchemaVersion,
            ["language"] = Language,
            ["shellTheme"] = ShellTheme,
            ["visual"] = JsonNode.Parse(Visual.ToJson()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Parses a profile; returns null for anything that is not one (caller reports it).</summary>
    internal static SettingsProfile? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null;
            if (root["kind"]?.GetValue<string>() != Kind) return null;
            if (root["visual"]?.ToJsonString() is not { Length: > 0 } visual) return null;
            return new SettingsProfile(PianoVisualSettings.FromJson(visual), Text(root, "language"), Text(root, "shellTheme"));
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
    }

    /// <summary>Reads a string member; a missing or non-string value is an empty string, never a throw.</summary>
    private static string Text(JsonObject root, string name)
    {
        try { return root[name]?.GetValue<string>() ?? ""; }
        catch (InvalidOperationException) { return ""; }
        catch (FormatException) { return ""; }
    }
}
