using System.Windows;
using System.Windows.Controls;
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

    /// <summary>Rebuilds the list: when a run ends, when the page is opened and when the language switches.</summary>
    internal void RefreshPracticeHistory()
    {
        if (PracticeHistoryHost is null) return;
        PracticeHistoryHost.Children.Clear();
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
            HistorySummaryLabel.Text = songs.Count == 0 ? "" : Loc.F("{0} songs · {1} runs recorded", songs.Count, PracticeHistory.Runs.Count);
        }
    }

    /// <summary>
    /// Writes the run that just ended, if the take graded anything at all. Called from
    /// <see cref="Stop"/>, so every way a take can end goes through here.
    /// </summary>
    private void RecordPracticeRunIfScored()
    {
        if (!_playing || _hits + _misses == 0) return;
        PracticeHistory.Record(_songLabel, _songPath, _hits, _misses, _bestStreak);
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
