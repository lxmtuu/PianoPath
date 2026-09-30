using System.IO;

namespace PianoPath;

/// <summary>
/// The community shelf: preset files that ship with the application from the <c>presets/</c> folder of the
/// repository, embedded into the assembly at build time so an installed Keyflow has them too.
///
/// <para>
/// A shelf preset is an ordinary preset file, which is the point: anybody can copy one, change the values,
/// open a pull request, and the look shows up in the list on the next build — no code change and no
/// translation needed. The files are validated twice over: <c>tools/check_sources.py</c> checks every key
/// against the settings class and that the committed files match <c>tools/make_presets.py</c>, while
/// <see cref="Diagnostics"/> checks the loaded shelf in the running application.
/// </para>
///
/// <para>
/// Shelf entries are read-only. They are applied like any other preset, they cannot be deleted (they have
/// no file in the user's folder), and saving over one of their names is refused so the badge in the list
/// stays meaningful.
/// </para>
/// </summary>
internal static class CommunityPresets
{
    private const string ResourcePrefix = "PianoPath.presets.";
    private const string ResourceSuffix = ".json";

    private static IReadOnlyList<VisualPreset>? _shelf;

    /// <summary>The shelf, newest load cached. Empty when the build carries no shelf at all.</summary>
    internal static IReadOnlyList<VisualPreset> All => _shelf ??= Load(EmbeddedSources());

    internal static VisualPreset? Find(string name) =>
        All.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

    internal static bool IsCommunity(string name) => Find(name) is not null;

    /// <summary>Name and text of every file in the embedded shelf, in name order.</summary>
    internal static IReadOnlyList<(string Name, string Json)> EmbeddedSources()
    {
        var assembly = typeof(CommunityPresets).Assembly;
        var sources = new List<(string Name, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            sources.Add((resource[ResourcePrefix.Length..^ResourceSuffix.Length], reader.ReadToEnd()));
        }
        sources.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return sources;
    }

    /// <summary>
    /// Reads a shelf: names are sanitised the same way user presets are, a file that does not parse is
    /// skipped, and a repeated name keeps the first file, so one bad contribution cannot hide the rest.
    /// </summary>
    internal static IReadOnlyList<VisualPreset> Load(IEnumerable<(string Name, string Json)> sources)
    {
        var presets = new List<VisualPreset>();
        foreach (var (name, json) in sources)
        {
            try
            {
                var safe = VisualPresetStore.SanitizeName(name);
                if (presets.Any(preset => string.Equals(preset.Name, safe, StringComparison.OrdinalIgnoreCase))) continue;
                var file = VisualPresetStore.ReadPresetFile(json);
                file.Settings.PresetName = safe;
                presets.Add(new VisualPreset(safe, string.IsNullOrWhiteSpace(file.Description) ? safe : file.Description,
                    false, file.Settings, null, file.Thumbnail, true));
            }
            catch { /* a shelf file that does not parse is not worth showing an error for */ }
        }
        return presets;
    }

    /// <summary>
    /// The message key to show when a look may not be saved under <paramref name="name"/>, or null when the
    /// name is free. Built-in and shelf names are taken: the list would otherwise show the same name twice.
    /// </summary>
    internal static string? NameConflict(string name)
    {
        if (VisualPresets.FindBuiltIn(name) is not null) return "“{0}” is a built-in preset. Choose another name.";
        if (IsCommunity(name)) return "“{0}” is a community preset that ships with Keyflow. Choose another name.";
        return null;
    }
}
