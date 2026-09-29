using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PianoPath;

/// <summary>
/// One finished run of the open song: the moment it was played and how it was graded.
/// </summary>
/// <param name="Song">Title the transport printed, i.e. the file name or the demo song.</param>
/// <param name="SongPath">Full path of the MIDI file, or empty for the built-in demo song.</param>
/// <param name="PlayedUtc">When the run ended; the list and the report are ordered by it.</param>
/// <param name="Hits">Notes scored on time.</param>
/// <param name="Misses">Notes missed or played late.</param>
/// <param name="BestStreak">Longest run of correct notes in this take.</param>
internal sealed record PracticeRun(string Song, string SongPath, DateTime PlayedUtc, int Hits, int Misses, int BestStreak)
{
    /// <summary>Correct notes as a percentage; zero when the run scored nothing.</summary>
    [JsonIgnore]
    public double Accuracy => Hits + Misses == 0 ? 0 : 100.0 * Hits / (Hits + Misses);
}

/// <summary>
/// The practice history: one JSON line per finished run, under <c>history/practice.jsonl</c> in the
/// settings folder.
///
/// <para>
/// Lines are appended rather than rewritten, so a long session never loses an earlier take, and a line
/// that cannot be read is skipped instead of failing the whole file. The in-memory list keeps the newest
/// <see cref="Capacity"/> runs, which is what the dock page and the HTML report show; the best accuracy
/// per song is derived from it, so a take can be compared with the best one before it.
/// </para>
/// </summary>
internal static class PracticeHistory
{
    internal const int Capacity = 200;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private static readonly List<PracticeRun> Runs_ = [];
    private static string? _loadedFrom;

    internal static string FilePath => Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "history", "practice.jsonl");

    /// <summary>Recorded runs, newest first. Reloaded automatically when the settings folder changes.</summary>
    internal static IReadOnlyList<PracticeRun> Runs { get { EnsureLoaded(); return Runs_; } }

    /// <summary>Reads the file again; used by <c>--verify</c> after it damages the file on purpose.</summary>
    internal static void Reload() { _loadedFrom = null; EnsureLoaded(); }

    /// <summary>Appends one finished run and returns it.</summary>
    internal static PracticeRun Record(string song, string songPath, int hits, int misses, int bestStreak)
    {
        EnsureLoaded();
        var run = new PracticeRun(song, songPath, DateTime.UtcNow, hits, misses, bestStreak);
        Runs_.Insert(0, run);
        if (Runs_.Count > Capacity) Runs_.RemoveRange(Capacity, Runs_.Count - Capacity);
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(run, Options) + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // A history that cannot be written is not worth interrupting a practice session over.
        }
        return run;
    }

    /// <summary>The best accuracy recorded for a song, or <c>null</c> when it has never been played.</summary>
    internal static PracticeRun? BestFor(string songPath)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(songPath)) return null;
        return Runs_.Where(run => string.Equals(run.SongPath, songPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.Accuracy).FirstOrDefault();
    }

    /// <summary>Runs grouped by song, best accuracy first; the summary of the HTML report.</summary>
    internal static IReadOnlyList<(string Song, int Runs, double Best)> Summary()
    {
        EnsureLoaded();
        return Runs_.GroupBy(run => run.SongPath.Length == 0 ? run.Song : run.SongPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.First().Song, group.Count(), group.Max(run => run.Accuracy)))
            .OrderByDescending(entry => entry.Item3).ThenBy(entry => entry.Song, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static void Clear()
    {
        EnsureLoaded();
        Runs_.Clear();
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch
        {
            // Leaving the file behind only means the cleared runs come back on the next launch.
        }
    }

    /// <summary>Writes the history as a self-contained HTML report (the file the EXPORT button saves).</summary>
    internal static void ExportReport(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, BuildReportHtml(), new UTF8Encoding(true));
    }

    /// <summary>
    /// The report markup: the runs in a table plus the best accuracy per song. Everything printed here
    /// goes through <see cref="Loc"/> like the rest of the interface, so the exported file is in the
    /// language the application is in.
    /// </summary>
    internal static string BuildReportHtml()
    {
        EnsureLoaded();
        var text = new StringBuilder();
        text.AppendLine("<!DOCTYPE html>");
        text.AppendLine("<html lang=\"" + Loc.Current.Id + "\">");
        text.AppendLine("<head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(Loc.T("Keyflow practice history")) + "</title>");
        text.AppendLine("<style>body{font:14px/1.5 system-ui,sans-serif;margin:32px;color:#1b1b1f}h1{font-size:22px;margin:0 0 4px}"
            + "p.sub{margin:0 0 20px;color:#666}table{border-collapse:collapse;margin:0 0 28px}th,td{padding:6px 12px;border-bottom:1px solid #ddd;text-align:left}"
            + "th{font-size:11px;letter-spacing:.08em;text-transform:uppercase;color:#666}</style></head><body>");
        text.AppendLine("<h1>" + WebUtility.HtmlEncode(Loc.T("Keyflow practice history")) + "</h1>");
        text.AppendLine("<p class=\"sub\">" + WebUtility.HtmlEncode(Loc.F("Exported {0} · {1} runs", DateTime.Now.ToString("g"), Runs_.Count)) + "</p>");
        text.AppendLine("<table><tr><th>" + WebUtility.HtmlEncode(Loc.T("Song")) + "</th><th>" + WebUtility.HtmlEncode(Loc.T("Played")) + "</th><th>"
            + WebUtility.HtmlEncode(Loc.T("Accuracy")) + "</th><th>" + WebUtility.HtmlEncode(Loc.T("Hits")) + "</th><th>" + WebUtility.HtmlEncode(Loc.T("Missed"))
            + "</th><th>" + WebUtility.HtmlEncode(Loc.T("Best streak")) + "</th></tr>");
        foreach (var run in Runs_)
            text.AppendLine("<tr><td>" + WebUtility.HtmlEncode(run.Song) + "</td><td>" + run.PlayedUtc.ToLocalTime().ToString("g") + "</td><td>"
                + run.Accuracy.ToString("0.#") + "%</td><td>" + run.Hits + "</td><td>" + run.Misses + "</td><td>" + run.BestStreak + "</td></tr>");
        text.AppendLine("</table>");
        if (Runs_.Count > 0)
        {
            text.AppendLine("<h2>" + WebUtility.HtmlEncode(Loc.T("Best per song")) + "</h2><table><tr><th>" + WebUtility.HtmlEncode(Loc.T("Song")) + "</th><th>"
                + WebUtility.HtmlEncode(Loc.T("Runs")) + "</th><th>" + WebUtility.HtmlEncode(Loc.T("Accuracy")) + "</th></tr>");
            foreach (var (song, runs, best) in Summary())
                text.AppendLine($"<tr><td>{WebUtility.HtmlEncode(song)}</td><td>{runs}</td><td>{best:0.#}%</td></tr>");
            text.AppendLine("</table>");
        }
        text.AppendLine("</body></html>");
        return text.ToString();
    }

    private static void EnsureLoaded()
    {
        var directory = PianoVisualSettingsStore.SettingsDirectory;
        if (string.Equals(_loadedFrom, directory, StringComparison.OrdinalIgnoreCase)) return;
        _loadedFrom = directory;
        Runs_.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                PracticeRun? run = null;
                try { run = JsonSerializer.Deserialize<PracticeRun>(line, Options); }
                catch { /* a half-written line from a crash is skipped, the rest of the file survives */ }
                if (run is not null && run.PlayedUtc != default) Runs_.Add(run);
            }
            Runs_.Reverse(); // the file is the order the runs happened in; the list shows the newest first
            if (Runs_.Count > Capacity) Runs_.RemoveRange(Capacity, Runs_.Count - Capacity);
        }
        catch { Runs_.Clear(); }
    }
}
