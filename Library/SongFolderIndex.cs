using System.IO;
using System.Text;
using System.Text.Json;

namespace PianoPath;

/// <summary>
/// One song file the folder scan found, with the facts read out of the file itself and the tags the user put
/// on it. The facts come from the same readers the player uses, so the library can never disagree with what
/// the stage will show when the file is opened.
/// </summary>
/// <param name="Path">Full path; it is the identity of the entry.</param>
/// <param name="Title">File name without extension, the way the transport prints it.</param>
/// <param name="Format">Lower-case extension without the dot: <c>mid</c>, <c>midi</c>, <c>musicxml</c>, <c>xml</c> or <c>mxl</c>.</param>
/// <param name="Notes">Notes the reader kept.</param>
/// <param name="Tracks">Tracks those notes were spread over.</param>
/// <param name="Seconds">Length of the song, from the end of its last note.</param>
/// <param name="BeatsPerMinute">Average tempo of the file's metronome grid.</param>
/// <param name="Size">Bytes on disk, part of the cache key together with <paramref name="ModifiedUtc"/>.</param>
/// <param name="ModifiedUtc">When the file was last written; a change to it is what makes a rescan re-read it.</param>
/// <param name="Tags">Labels the user gave the song, lower-case, at most <see cref="SongFolderIndex.MaxTags"/>.</param>
internal sealed record SongFile(
    string Path,
    string Title,
    string Format,
    int Notes,
    int Tracks,
    double Seconds,
    double BeatsPerMinute,
    long Size,
    DateTime ModifiedUtc,
    List<string> Tags);

/// <summary>On-disk shape of the folder library: a schema version, the folder it was scanned from and its songs.</summary>
internal sealed record SongFolderFile(int Version, string Folder, List<SongFile> Songs);

/// <summary>
/// The song library of a folder: every readable file under it, indexed once and remembered between launches.
///
/// <para>
/// A scan reads a file only when it is new or when its size or write time changed, so pointing the library at
/// a folder with hundreds of files costs one pass and then nothing until something is edited. A file that the
/// reader refuses is left out instead of being listed as a song the app cannot open, and the tags of a song
/// survive every rescan because they are stored beside the facts.
/// </para>
///
/// <para>
/// The index lives in its own file next to the recent list (<c>library-index.json</c>) rather than in the
/// settings, because it is derived data: it can be thrown away and rebuilt from the folder at any time.
/// </para>
/// </summary>
internal static class SongFolderIndex
{
    internal const int Version = 1;

    /// <summary>Files one scan will read; a folder deeper than this is indexed by the first part of it.</summary>
    internal const int MaxFiles = 500;

    /// <summary>Folder levels below the chosen one that a scan walks (<c>0</c> is the chosen folder itself).</summary>
    internal const int MaxDepth = 3;

    /// <summary>Tags one song can carry.</summary>
    internal const int MaxTags = 8;

    /// <summary>Longest tag the library accepts; a longer one is cut instead of refused.</summary>
    internal const int MaxTagLength = 24;

    internal static readonly string[] Extensions = [".mid", ".midi", ".musicxml", ".xml", ".mxl"];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly List<SongFile> Songs_ = [];
    private static string _folder = "";
    private static string? _loadedFrom;

    internal static string FilePath => Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "library-index.json");

    /// <summary>The folder being indexed, or empty when the user has not chosen one.</summary>
    internal static string Folder { get { EnsureLoaded(); return _folder; } }

    /// <summary>The indexed songs, by title. Reloaded automatically when the settings folder changes.</summary>
    internal static IReadOnlyList<SongFile> Songs { get { EnsureLoaded(); return Songs_; } }

    /// <summary>Reads the index file again; used by <c>--verify</c> after it changes the file on purpose.</summary>
    internal static void Reload() { _loadedFrom = null; EnsureLoaded(); }

    /// <summary>True when the path looks like a song this application can open.</summary>
    internal static bool IsSong(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads one file into a library entry, or <c>null</c> when it is not a song this build can read (a broken
    /// file, a score with no notes, or something that only carries the extension).
    /// </summary>
    internal static SongFile? Read(string path, IReadOnlyList<string>? tags = null)
    {
        if (!IsSong(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            var song = extension is "mxl" or "musicxml" or "xml"
                ? MusicXmlReader.ReadScore(path).ToSong()
                : MidiReader.ReadSong(path);
            if (song.Notes.Count == 0) return null;
            var seconds = 0.0;
            foreach (var note in song.Notes) seconds = Math.Max(seconds, note.End);
            var remembered = new List<string>();
            if (tags is not null) remembered.AddRange(tags.Take(MaxTags));
            return new SongFile(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(path), extension, song.Notes.Count,
                song.Notes.Select(note => note.Track).Distinct().Count(), seconds, AverageTempo(song.BeatTimes),
                info.Length, info.LastWriteTimeUtc, remembered);
        }
        catch
        {
            // An unreadable file is simply not a song of this library; the folder stays usable.
            return null;
        }
    }

    /// <summary>
    /// The average tempo of a metronome grid: the beats it spans over the time they take. Zero when the grid
    /// has fewer than two beats, so a caller can tell "unknown" from "fast".
    /// </summary>
    internal static double AverageTempo(IReadOnlyList<double> beats)
    {
        if (beats.Count < 2) return 0;
        var span = beats[^1] - beats[0];
        return span > .001 ? 60 * (beats.Count - 1) / span : 0;
    }

    /// <summary>
    /// Every readable song under <paramref name="folder"/> up to <see cref="MaxDepth"/> levels and
    /// <see cref="MaxFiles"/> files, with the songs of the previous scan reused when their file did not
    /// change. Pass <paramref name="force"/> to re-read every file (the RESCAN button).
    /// </summary>
    internal static IReadOnlyList<SongFile> Scan(string folder, bool force = false)
    {
        EnsureLoaded();
        _folder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
        var previous = Songs_.ToDictionary(song => song.Path, StringComparer.OrdinalIgnoreCase);
        var found = new List<SongFile>();
        foreach (var path in Walk(_folder))
        {
            if (found.Count >= MaxFiles) break;
            var full = Path.GetFullPath(path);
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists) continue;
                if (!force && previous.TryGetValue(full, out var cached) && cached.Size == info.Length && cached.ModifiedUtc == info.LastWriteTimeUtc)
                {
                    found.Add(cached);      // the tags travel with the cached entry, so a rescan never loses them
                    continue;
                }
                previous.TryGetValue(full, out var known);
                if (Read(full, known?.Tags) is { } song) found.Add(song);
            }
            catch
            {
                // A file that vanished between the walk and the read is not worth a failed scan.
            }
        }
        Songs_.Clear();
        Songs_.AddRange(found.OrderBy(song => song.Title, StringComparer.OrdinalIgnoreCase).ThenBy(song => song.Path, StringComparer.OrdinalIgnoreCase));
        Save();
        return Songs_;
    }

    /// <summary>Forgets the folder and everything indexed; used by the dock's CLEAR and by the checks.</summary>
    internal static void Forget()
    {
        EnsureLoaded();
        Songs_.Clear();
        _folder = "";
        Save();
    }

    /// <summary>
    /// The songs matching a query: every whitespace-separated token must appear in the title, the file name or
    /// one of the tags, so "chopin nocturne" finds a nocturne tagged chopin and nothing else. An empty query
    /// matches everything.
    /// </summary>
    internal static IReadOnlyList<SongFile> Search(string? query)
    {
        EnsureLoaded();
        var tokens = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return Songs_;
        return Songs_.Where(song => tokens.All(token => Matches(song, token))).ToList();
    }

    private static bool Matches(SongFile song, string token) =>
        song.Title.Contains(token, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(song.Path).Contains(token, StringComparison.OrdinalIgnoreCase)
        || song.Tags.Any(tag => tag.Contains(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds a tag to a song and returns it, or <c>null</c> when the tag is empty, the song is not indexed or
    /// the tag is already there. Tags are lower-case so that searching is the same however they were typed.
    /// </summary>
    internal static SongFile? Tag(string path, string? tag)
    {
        EnsureLoaded();
        var clean = CleanTag(tag);
        if (clean.Length == 0) return null;
        var full = Path.GetFullPath(path);
        var index = Songs_.FindIndex(song => string.Equals(song.Path, full, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return null;
        var song = Songs_[index];
        if (song.Tags.Contains(clean, StringComparer.OrdinalIgnoreCase) || song.Tags.Count >= MaxTags) return null;
        var tagged = song with { Tags = [.. song.Tags, clean] };
        Songs_[index] = tagged;
        Save();
        return tagged;
    }

    /// <summary>Removes a tag and returns the song, or <c>null</c> when it was not there.</summary>
    internal static SongFile? Untag(string path, string? tag)
    {
        EnsureLoaded();
        var clean = CleanTag(tag);
        var full = Path.GetFullPath(path);
        var index = Songs_.FindIndex(song => string.Equals(song.Path, full, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || clean.Length == 0) return null;
        var song = Songs_[index];
        var tags = song.Tags.Where(known => !string.Equals(known, clean, StringComparison.OrdinalIgnoreCase)).ToList();
        if (tags.Count == song.Tags.Count) return null;
        var untagged = song with { Tags = tags };
        Songs_[index] = untagged;
        Save();
        return untagged;
    }

    /// <summary>A tag as the library stores it: trimmed, lower-case, single-spaced and cut to the limit.</summary>
    internal static string CleanTag(string? tag)
    {
        var words = (tag ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var clean = string.Join(' ', words).ToLowerInvariant();
        return clean.Length > MaxTagLength ? clean[..MaxTagLength].TrimEnd() : clean;
    }

    /// <summary>
    /// The song files under a folder, up to <see cref="MaxDepth"/> levels below it. Directories that cannot be
    /// read (a protected folder, a vanished share) are skipped rather than failing the whole scan, and the
    /// walk is breadth-first so the songs nearest the chosen folder are indexed first when the cap bites.
    /// </summary>
    private static IEnumerable<string> Walk(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) yield break;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((folder, 0));
        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();
            string[] files;
            try { files = Directory.GetFiles(directory); }
            catch { continue; }
            foreach (var file in files) if (IsSong(file)) yield return file;
            if (depth >= MaxDepth) continue;
            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch { continue; }
            foreach (var child in children) queue.Enqueue((child, depth + 1));
        }
    }

    private static void EnsureLoaded()
    {
        var directory = PianoVisualSettingsStore.SettingsDirectory;
        if (string.Equals(_loadedFrom, directory, StringComparison.OrdinalIgnoreCase)) return;
        _loadedFrom = directory;
        Songs_.Clear();
        _folder = "";
        try
        {
            if (!File.Exists(FilePath)) return;
            var file = JsonSerializer.Deserialize<SongFolderFile>(File.ReadAllText(FilePath), Options);
            if (file?.Songs is not { } songs) return;
            _folder = file.Folder ?? "";
            Songs_.AddRange(songs.Where(song => !string.IsNullOrWhiteSpace(song.Path)).Take(MaxFiles));
        }
        catch
        {
            // An index that cannot be read is rebuilt from the folder the next time the user scans it.
            Songs_.Clear();
            _folder = "";
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(PianoVisualSettingsStore.SettingsDirectory);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SongFolderFile(Version, _folder, [.. Songs_]), Options), new UTF8Encoding(false));
            File.Move(temp, FilePath, true);
        }
        catch
        {
            // The index is derived data: failing to write it only means the next launch scans again.
        }
    }
}
