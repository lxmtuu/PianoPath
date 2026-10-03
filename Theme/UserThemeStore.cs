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
    private static readonly char[] InvalidNameCharacters = "<>:\"/\\|?*".Concat(Path.GetInvalidFileNameChars()).Distinct().ToArray();
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
                catch { /* a corrupt theme file should not hide the remaining themes */ }
            }
        }
        catch { }
        return themes;
    }

    /// <summary>Writes a theme, replacing its old file only after the new file has been written successfully.</summary>
    internal ShellTheme Save(UserShellTheme theme, string? replacingId = null)
    {
        var name = theme.Name?.Trim() ?? "";
        if (NameValidationError(name) is { } validationError)
            throw new ArgumentException(Loc.T(validationError), nameof(theme));
        var built = UserShellThemes.Build(theme);
        var existing = Load();
        var collision = existing.FirstOrDefault(candidate => string.Equals(candidate.Id, built.Id, StringComparison.OrdinalIgnoreCase));
        if (collision is not null)
        {
            var isReplacement = replacingId is not null
                && string.Equals(collision.Id, replacingId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(collision.Name, name, StringComparison.OrdinalIgnoreCase);
            var sameNameOverwrite = replacingId is null && string.Equals(collision.Name, name, StringComparison.OrdinalIgnoreCase);
            if (!isReplacement && !sameNameOverwrite)
                throw new InvalidDataException(Loc.T("Another user theme has the same normalized name. Choose a different name."));
        }

        var old = replacingId is null
            ? null
            : existing.FirstOrDefault(candidate => string.Equals(candidate.Id, replacingId, StringComparison.OrdinalIgnoreCase));
        var path = PathFor(name);
        var oldPath = old is null ? null : PathFor(old.Name);
        var temp = path + ".tmp";
        var stored = theme;
        stored.Name = name;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(stored, Options));
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        if (oldPath is not null && !string.Equals(oldPath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            try { File.Delete(oldPath); }
            catch
            {
                // A rename must not lose the old theme if cleanup is blocked. Roll back the new file when
                // possible, then let the caller report that the rename did not complete.
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                throw;
            }
        }
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
            var path = PathFor(name);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Returns an English-key error when a name cannot safely become a Windows file name.</summary>
    internal static string? NameValidationError(string? name)
    {
        var clean = name?.Trim() ?? "";
        if (clean.Length == 0) return "A theme needs a name.";
        if (clean.Length > 32 || clean is "." or ".." || clean[^1] is '.' or ' '
            || clean.Any(character => char.IsControl(character) || InvalidNameCharacters.Contains(character)))
            return "Theme names must be 1–32 characters and cannot contain path separators or reserved filename characters.";
        return null;
    }

    /// <summary>
    /// The message key to show when a theme may not be named <paramref name="name"/>, or null when the name
    /// is free. A name that exactly matches an existing user theme replaces it; a different name that would
    /// normalize to the same theme id is refused instead of silently resolving to the other file.
    /// </summary>
    internal static string? NameConflict(string name, string? replacingId = null)
    {
        if (NameValidationError(name) is { } validationError) return validationError;
        if (ShellThemes.All.Any(theme => string.Equals(theme.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "“{0}” is a built-in theme. Choose another name.";

        var id = UserShellThemes.Id(name);
        var collision = Default.Load().FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase));
        if (collision is null) return null;
        if (string.Equals(collision.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
            && (replacingId is null || string.Equals(collision.Id, replacingId, StringComparison.OrdinalIgnoreCase)))
            return null;
        return "Another user theme has the same normalized name. Choose a different name.";
    }

    private string PathFor(string name)
    {
        if (NameValidationError(name) is { } error) throw new ArgumentException(Loc.T(error), nameof(name));
        var directory = Path.GetFullPath(Directory);
        var path = Path.GetFullPath(Path.Combine(directory, name.Trim() + ".json"));
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The theme path must remain inside the themes folder.");
        return path;
    }
}
