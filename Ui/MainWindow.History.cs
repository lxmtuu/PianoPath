using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace PianoPath;

/// <summary>
/// The History page of the dock: the runs of the open song, the best take of that song, and the two
/// buttons that export or clear the record.
///
/// <para>
/// A run is written by <see cref="Stop"/> as soon as a take with at least one graded note ends, so
/// finishing a song, stopping halfway or switching files all count, and pressing Stop twice does not
/// record the same take twice. The page is rebuilt on every change and when the language switches.
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Runs the page prints; the file keeps far more.</summary>
    internal const int HistoryRows = 12;

    /// <summary>Size of one ghost row, so both rows of a comparison are drawn on the same axes.</summary>
    internal const double GhostWidth = 268, GhostHeight = 44;

    /// <summary>Rebuilds the list: when a run ends, when the page is opened and when the language switches.</summary>
    internal void RefreshPracticeHistory()
    {
        if (PracticeHistoryHost is null) return;
        PracticeHistoryHost.Children.Clear();
        BuildPracticeChart();
        BuildPracticeGhost();
        var runs = PracticeHistory.Runs.Take(HistoryRows).ToList();
        if (HistoryEmptyLabel is not null) HistoryEmptyLabel.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryBestLabel is not null)
        {
            var best = PracticeHistory.BestFor(_songPath);
            HistoryBestLabel.Text = best is null
                ? Loc.T("No run of this song has been recorded yet.")
                : Loc.F("Best for this song: {0:0.#}% on {1}", best.Accuracy, best.PlayedUtc.ToLocalTime().ToString("g"));
        }
        foreach (var run in runs)
        {
            var row = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 3, 0, 3) };
            row.Text = Loc.F("{0} · {1} · {2:0.#}% · {3} hits · {4} missed · best streak {5}",
                run.PlayedUtc.ToLocalTime().ToString("g"), run.Song, run.Accuracy, run.Hits, run.Misses, run.BestStreak);
            PracticeHistoryHost.Children.Add(row);
        }
        if (HistorySummaryLabel is not null)
        {
            var songs = PracticeHistory.Summary();
            HistorySummaryLabel.Text = songs.Count == 0 ? "" : Loc.F("{0} songs · {1} runs recorded", songs.Count, PracticeHistory.TotalRuns);
        }
    }

    /// <summary>
    /// One bar per day over the window the model returns: taller the better that day's notes landed, and the
    /// accent colour only on days with a run, so an empty day is visibly empty rather than a zero-height bar
    /// the eye has to hunt for.
    /// </summary>
    private void BuildPracticeChart()
    {
        if (HistoryChartHost is null) return;
        HistoryChartHost.Children.Clear();
        var days = PracticeHistory.Daily(PracticeHistory.ChartDays, DateTime.Now);
        const double height = 52, slot = 18;
        HistoryChartHost.Width = slot * days.Count;
        var accent = (Brush)FindResource("AccentBrush"); var empty = (Brush)FindResource("TrackBrush");
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var bar = new Rectangle
            {
                Width = slot - 4, RadiusX = 2, RadiusY = 2,
                Height = PracticeChart.BarHeight(day.Accuracy, height, 3),
                Fill = day.Runs > 0 ? accent : empty,
                ToolTip = Loc.F("{0} · {1} runs · {2:0.#}%", day.Day.ToString("d"), day.Runs, day.Accuracy)
            };
            Canvas.SetLeft(bar, index * slot + 2); Canvas.SetTop(bar, height - bar.Height);
            HistoryChartHost.Children.Add(bar);
        }
        if (HistoryChartLabel is not null) HistoryChartLabel.Text = PracticeHistory.ChartCaption(days);
    }

    /// <summary>
    /// The ghost: the graded notes of the takes of the open song, the best on top and the latest below, drawn
    /// on axes they share so the two rows can be read against each other. Nothing is drawn for a song that has
    /// never been played, or whose takes were recorded before the ghost existed.
    /// </summary>
    private void BuildPracticeGhost()
    {
        if (HistoryGhostHost is null) return;
        HistoryGhostHost.Children.Clear();
        var (latest, best) = PracticeHistory.GhostPair(_songPath);
        var bestRun = best ?? latest;
        var points = (latest?.Ghost ?? Array.Empty<PracticePoint>()).Concat(bestRun?.Ghost ?? Array.Empty<PracticePoint>()).ToList();
        if (HistoryGhostLabel is not null)
            HistoryGhostLabel.Text = latest is null || points.Count == 0
                ? Loc.T("No take of this song has graded notes recorded yet, so there is no ghost to draw. Play it once and every graded note is kept for the next time.")
                : Loc.T("Each dot is a graded note of the take: its place in the song and its pitch. Cyan landed, red was missed, and the rows share their axes.");
        if (latest is null || bestRun is null || points.Count == 0) return;
        var (low, high) = PracticeChart.PitchWindow(points);
        var length = Math.Max(.001, SongDuration());
        var hit = (Brush)FindResource("SuccessBrush"); var missed = (Brush)FindResource("DangerBrush");
        // One take is not a comparison: it is drawn on its own and the label above says so.
        if (ReferenceEquals(bestRun, latest)) AddGhostRow(Loc.T("The only take"), bestRun, hit, missed, low, high, length);
        else
        {
            AddGhostRow(Loc.T("Best take"), bestRun, hit, missed, low, high, length);
            AddGhostRow(Loc.T("This take"), latest, hit, missed, low, high, length);
        }
    }

    /// <summary>One row of the ghost: a caption, then a dot per graded note on the axes the rows share.</summary>
    private void AddGhostRow(string caption, PracticeRun run, Brush hit, Brush missed, int low, int high, double length)
    {
        var captionRow = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 6, 0, 2) };
        captionRow.Text = Loc.F("{0} · {1}", caption, PracticeChart.RowCaption(run.Hits, run.Misses));
        HistoryGhostHost!.Children.Add(captionRow);
        var canvas = new Canvas { Width = GhostWidth, Height = GhostHeight, Margin = new Thickness(0, 0, 0, 2) };
        HistoryGhostHost.Children.Add(canvas);
        var area = new Rect(0, 0, canvas.Width, canvas.Height);
        foreach (var point in run.Ghost ?? Array.Empty<PracticePoint>())
        {
            var at = PracticeChart.Point(point.At, point.Pitch, length, low, high, area);
            var dot = new Ellipse
            {
                Width = PracticeChart.DotRadius * 2, Height = PracticeChart.DotRadius * 2,
                Fill = point.Hit ? hit : missed,
                ToolTip = Loc.F("{0:0.##} s · pitch {1}", point.At, point.Pitch)
            };
            Canvas.SetLeft(dot, at.X - PracticeChart.DotRadius); Canvas.SetTop(dot, at.Y - PracticeChart.DotRadius);
            canvas.Children.Add(dot);
        }
    }

    /// <summary>
    /// Writes the run that just ended, if the take graded anything at all. Called from
    /// <see cref="Stop"/>, so every way a take can end goes through here.
    /// </summary>
    private void RecordPracticeRunIfScored()
    {
        if (!_playing || _hits + _misses == 0) return;
        PracticeHistory.Record(_songLabel, _songPath, _hits, _misses, _bestStreak, PracticeGhost);
        RefreshPracticeHistory();
    }

    private void ExportHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "keyflow-practice-history.html",
            Filter = Loc.T("Practice history (*.html)|*.html|All files (*.*)|*.*"),
            Title = Loc.T("Export the practice history"),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PracticeHistory.ExportReport(dialog.FileName);
            ShowMessage(Loc.F("Practice history exported.\n{0}", dialog.FileName), "Export practice history", MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowMessage(ex.Message, "Export practice history", MessageBoxImage.Warning); }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm(Loc.T("Clear the practice history? This cannot be undone."), "Clear practice history")) return;
        PracticeHistory.Clear();
        RefreshPracticeHistory();
    }
}
