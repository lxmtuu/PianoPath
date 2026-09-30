using System.IO;
using System.Text.Json;

namespace PianoPath;

/// <summary>
/// Where themes the user made live: one JSON file per theme inside the settings folder, next to the user
/// presets. A read-only view of the folder is all the rest of the application needs — the id of a theme is
/// derived from its name, so a file that is renamed by hand simply arrives under a new id.
/// </summary>
internal sealed class UserThemeStore(string directory)
{
    internal string Directory { get; } = directory;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static UserThemeStore? _default;

    /// <summary>The store inside the current settings folder; rebuilt when that folder is redirected.</summary>
    internal static UserThemeStore Default => _default ??= new(Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "themes"));

    internal static void InvalidateDefault() => _default = null;

    /// <summary>Every theme the user made, in name order; a file that does not parse is skipped.</summary>
    internal IReadOnlyList<ShellTheme> Load()
    {
        var themes = new List<ShellTheme>();
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return themes;
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var stored = JsonSerializer.Deserialize<UserShellTheme>(File.ReadAllText(file), Options);
                    if (stored is null) continue;
                    // The file name wins over the stored name: renaming a file renames the theme, which is
                    // the same rule the user presets follow.
                    stored.Name = Path.GetFileNameWithoutExtension(file);
                    var theme = UserShellThemes.Build(stored);
                    if (themes.Any(existing => string.Equals(existing.Id, theme.Id, StringComparison.OrdinalIgnoreCase))) continue;
                    themes.Add(theme);
                }
                catch { /* a corrupt file should not hide the remaining themes */ }
            }
        }
        catch { }
        return themes;
    }

    /// <summary>Writes a theme, replacing one with the same name. Returns the theme as it will load back.</summary>
    internal ShellTheme Save(UserShellTheme theme)
    {
        var built = UserShellThemes.Build(theme);
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, built.Name + ".json");
        var temp = path + ".tmp";
        var stored = theme;
        stored.Name = built.Name;
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, Options));
        File.Move(temp, path, true);
        return built;
    }

    /// <summary>
    /// Deletes the file of a theme, by id or by name. The id is resolved against <em>this</em> folder — a
    /// store is a view of one directory, so it must not consult the global theme registry to know what it
    /// holds. A built-in theme has no file anywhere, so it cannot be deleted.
    /// </summary>
    internal bool Delete(string nameOrId)
    {
        if (!UserShellThemes.IsUserTheme(nameOrId)) return false;
        try
        {
            var theme = Load().FirstOrDefault(candidate => string.Equals(candidate.Id, nameOrId, StringComparison.OrdinalIgnoreCase));
            var name = theme?.Name ?? Path.GetFileNameWithoutExtension(nameOrId);
            var path = Path.Combine(Directory, name + ".json");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// The message key to show when a theme may not be named <paramref name="name"/>, or null when the name
    /// is free. A name that collides rewrites the theme of that name instead, so only the built-ins — which
    /// cannot be rewritten — are refused here.
    /// </summary>
    internal static string? NameConflict(string name)
    {
        if (ShellThemes.All.Any(theme => string.Equals(theme.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "“{0}” is a built-in theme. Choose another name.";
        return null;
    }
}
