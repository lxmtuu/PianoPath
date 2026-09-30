using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PianoPath;

/// <summary>
/// One graded note of a run: where it sits in the song and whether it was hit. This is the ghost the History
/// page draws, so a take can be compared with an earlier one note by note instead of by its average alone.
/// </summary>
/// <param name="At">Seconds into the song, i.e. the note's own start, so the ghost lines up with the roll.</param>
/// <param name="Pitch">MIDI pitch of the graded note.</param>
/// <param name="Hit">True when the note was scored on time.</param>
internal sealed record PracticePoint(double At, int Pitch, bool Hit);

/// <summary>
/// One day of practice, for the chart on the History page: the runs that ended that local day and the notes
/// they graded together. A day without a single run is still a row, with zeros, so the chart has a fixed axis.
/// </summary>
/// <param name="Day">The local date, midnight.</param>
/// <param name="Runs">Finished runs that ended that day.</param>
/// <param name="Hits">Notes scored on time in them.</param>
/// <param name="Misses">Notes missed or played late in them.</param>
internal sealed record PracticeDay(DateTime Day, int Runs, int Hits, int Misses)
{
    /// <summary>Correct notes of the whole day as a percentage; zero when nothing was graded.</summary>
    [JsonIgnore]
    public double Accuracy => Hits + Misses == 0 ? 0 : 100.0 * Hits / (Hits + Misses);
}

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
    /// <summary>
    /// The graded notes in the order they were scored, capped at <see cref="PracticeHistory.GhostCapacity"/>.
    /// A line written before the ghost existed has none, and the History page then simply draws nothing.
    /// </summary>
    public IReadOnlyList<PracticePoint>? Ghost { get; init; }

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

    /// <summary>
    /// How many graded notes a run keeps for the ghost. A full song can be long, and the ghost is a reading
    /// aid rather than a transcript, so the first notes of the take are what is kept — the opening bars are
    /// where a take is usually decided, and the cap keeps a long practice session's history file small.
    /// </summary>
    internal const int GhostCapacity = 256;

    /// <summary>Days the chart on the History page and the exported report cover.</summary>
    internal const int ChartDays = 14;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private static readonly List<PracticeRun> Runs_ = [];
    private static string? _loadedFrom;

    internal static string FilePath => Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "history", "practice.jsonl");

    /// <summary>Recorded runs, newest first. Reloaded automatically when the settings folder changes.</summary>
    internal static IReadOnlyList<PracticeRun> Runs { get { EnsureLoaded(); return Runs_; } }

    /// <summary>Reads the file again; used by <c>--verify</c> after it damages the file on purpose.</summary>
    internal static void Reload() { _loadedFrom = null; EnsureLoaded(); }

    /// <summary>Appends one finished run and returns it; <paramref name="ghost"/> is capped and may be empty.</summary>
    internal static PracticeRun Record(string song, string songPath, int hits, int misses, int bestStreak, IReadOnlyList<PracticePoint>? ghost = null)
    {
        EnsureLoaded();
        var run = new PracticeRun(song, songPath, DateTime.UtcNow, hits, misses, bestStreak)
        {
            Ghost = ghost is { Count: > 0 } ? ghost.Take(GhostCapacity).ToList() : null
        };
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

    /// <summary>
    /// The runs of the last <paramref name="days"/> local days as one row per day, oldest first and every day
    /// present even when nothing was played. The runs the chart covers are bucketed by the local day they
    /// ended on, which is the day the player remembers practising.
    /// </summary>
    internal static IReadOnlyList<PracticeDay> Daily(int days, DateTime now)
    {
        EnsureLoaded();
        days = Math.Max(1, days);
        var last = now.Date;
        var first = last.AddDays(1 - days);
        var buckets = new Dictionary<DateTime, (int Runs, int Hits, int Misses)>();
        foreach (var run in Runs_)
        {
            var day = run.PlayedUtc.ToLocalTime().Date;
            if (day < first || day > last) continue;
            buckets.TryGetValue(day, out var bucket);
            buckets[day] = (bucket.Runs + 1, bucket.Hits + run.Hits, bucket.Misses + run.Misses);
        }
        var result = new List<PracticeDay>(days);
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            buckets.TryGetValue(day, out var bucket);
            result.Add(new PracticeDay(day, bucket.Runs, bucket.Hits, bucket.Misses));
        }
        return result;
    }

    /// <summary>Accuracy of a whole window of days: every note graded in it counts once.</summary>
    internal static double AverageAccuracy(IReadOnlyList<PracticeDay> days)
    {
        var hits = days.Sum(day => day.Hits); var misses = days.Sum(day => day.Misses);
        return hits + misses == 0 ? 0 : 100.0 * hits / (hits + misses);
    }

    /// <summary>The one line under the chart, shared by the dock page and the exported report.</summary>
    internal static string ChartCaption(IReadOnlyList<PracticeDay> days) =>
        Loc.F("Accuracy by day: {0} runs · {1:0.#}% average", days.Sum(day => day.Runs), AverageAccuracy(days));

    /// <summary>
    /// The two takes the ghost compares: the newest run of the song (the take the player just played) and its
    /// best one, which may be the same run when there is only one. Empty when the song has never been played.
    /// </summary>
    internal static (PracticeRun? Latest, PracticeRun? Best) GhostPair(string songPath)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(songPath)) return (null, null);
        var runs = Runs_.Where(run => string.Equals(run.SongPath, songPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (runs.Count == 0) return (null, null);
        return (runs[0], runs.OrderByDescending(run => run.Accuracy).ThenByDescending(run => run.PlayedUtc).First());
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
        // The report carries the same chart caption as the page, so the exported file explains itself.
        var days = Daily(ChartDays, DateTime.Now);
        var played = days.Where(day => day.Runs > 0).ToList();
        if (played.Count > 0)
        {
            text.AppendLine("<h2>" + WebUtility.HtmlEncode(ChartCaption(days)) + "</h2>");
            text.AppendLine("<table><tr><th>" + WebUtility.HtmlEncode(Loc.T("Day")) + "</th><th>" + WebUtility.HtmlEncode(Loc.T("Runs")) + "</th><th>"
                + WebUtility.HtmlEncode(Loc.T("Accuracy")) + "</th></tr>");
            foreach (var day in played)
                text.AppendLine($"<tr><td>{WebUtility.HtmlEncode(day.Day.ToString("d"))}</td><td>{day.Runs}</td><td>{day.Accuracy:0.#}%</td></tr>");
            text.AppendLine("</table>");
        }
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
