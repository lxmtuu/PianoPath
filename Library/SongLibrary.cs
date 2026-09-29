using System.IO;
using System.Text.Json;

namespace PianoPath;

/// <summary>
/// One song the user has opened: the facts read from the file plus the values that were in force when it
/// was last played, so reopening it from the recent list puts the piano back the way that song was
/// practised.
/// </summary>
/// <param name="Path">Full path of the MIDI file; it is the identity of the entry.</param>
/// <param name="Title">File name without extension, the way the transport prints it.</param>
/// <param name="Notes">Notes the reader kept (empty channels and percussion are already gone).</param>
/// <param name="Tracks">Tracks the notes were spread over.</param>
/// <param name="Seconds">Length of the song, from the end of its last note.</param>
/// <param name="BeatsPerMinute">Average tempo of the file's metronome grid.</param>
/// <param name="HandSplitPitch">Hand split point in force when the song was last opened.</param>
/// <param name="FallSpeed">Fall speed in force when the song was last opened.</param>
/// <param name="TempoPercent">Playback tempo in force when the song was last opened.</param>
/// <param name="Preset">Name of the look that was selected then; metadata for the library list.</param>
/// <param name="SplitInferred">True when <paramref name="HandSplitPitch"/> was inferred from the song; such a
/// value is reused as it is the next time the song opens instead of being guessed again.</param>
/// <param name="OpenedUtc">When it was last opened; the list is kept newest first.</param>
internal sealed record SongEntry(
    string Path,
    string Title,
    int Notes,
    int Tracks,
    double Seconds,
    double BeatsPerMinute,
    double HandSplitPitch,
    double FallSpeed,
    double TempoPercent,
    string Preset,
    bool SplitInferred,
    DateTime OpenedUtc);

/// <summary>On-disk shape of the library file: a schema version plus the entries, newest first.</summary>
internal sealed record SongLibraryFile(int Version, List<SongEntry> Songs);

/// <summary>
/// The recent-songs index: a small JSON file next to the visual settings, newest first, deduplicated by
/// path and capped at <see cref="Capacity"/> entries.
///
/// It is written with the same temp-file-then-move dance as the settings file, and a damaged file is
/// simply forgotten — a library that cannot be read must never stop a song from opening.
/// </summary>
internal static class SongLibrary
{
    internal const int Capacity = 12;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly List<SongEntry> Songs = [];
    private static string? _loadedFrom;

    internal static string FilePath => Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "library.json");

    /// <summary>Entries, newest first. Reloaded automatically when the settings folder changes.</summary>
    internal static IReadOnlyList<SongEntry> Entries { get { EnsureLoaded(); return Songs; } }

    /// <summary>Reads the file again; used by <c>--verify</c> after it damages the file on purpose.</summary>
    internal static void Reload() { _loadedFrom = null; EnsureLoaded(); }

    internal static SongEntry? Find(string path)
    {
        EnsureLoaded();
        var full = Path.GetFullPath(path);
        return Songs.FirstOrDefault(song => string.Equals(song.Path, full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Remembers a song that just opened and returns the entry that was written.</summary>
    internal static SongEntry Remember(string path, string title, int notes, int tracks, double seconds, double beatsPerMinute,
        double handSplitPitch, double fallSpeed, double tempoPercent, string preset, bool splitInferred = false)
    {
        EnsureLoaded();
        var full = Path.GetFullPath(path);
        var entry = new SongEntry(full, title, notes, tracks, seconds, beatsPerMinute, handSplitPitch, fallSpeed, tempoPercent, preset, splitInferred, DateTime.UtcNow);
        Songs.RemoveAll(song => string.Equals(song.Path, full, StringComparison.OrdinalIgnoreCase));
        Songs.Insert(0, entry);
        if (Songs.Count > Capacity) Songs.RemoveRange(Capacity, Songs.Count - Capacity);
        Save();
        return entry;
    }

    internal static void Forget(string path)
    {
        EnsureLoaded();
        var full = Path.GetFullPath(path);
        if (Songs.RemoveAll(song => string.Equals(song.Path, full, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    internal static void Clear()
    {
        EnsureLoaded();
        Songs.Clear();
        Save();
    }

    private static void EnsureLoaded()
    {
        var directory = PianoVisualSettingsStore.SettingsDirectory;
        if (string.Equals(_loadedFrom, directory, StringComparison.OrdinalIgnoreCase)) return;
        _loadedFrom = directory;
        Songs.Clear();
        Songs.AddRange(Read());
    }

    private static List<SongEntry> Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var file = JsonSerializer.Deserialize<SongLibraryFile>(File.ReadAllText(FilePath), Options);
            if (file?.Songs is not { } songs) return [];
            return songs.Where(song => !string.IsNullOrWhiteSpace(song.Path)).Take(Capacity).ToList();
        }
        catch { return []; }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(PianoVisualSettingsStore.SettingsDirectory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SongLibraryFile(1, [.. Songs]), Options));
            File.Move(temp, FilePath, true);
        }
        catch
        {
            // A library that cannot be written is not worth failing a song over: the list is written again
            // the next time the file happens to be writable.
        }
    }
}
