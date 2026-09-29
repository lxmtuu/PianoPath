using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PianoPath;

/// <summary>
/// The recent-songs list in the Play dialog: the files the user opened, the facts read from each file and
/// the values that were in force the last time it was played.
///
/// Opening a song always goes through <see cref="OpenMidiFile"/>, which remembers it. Reopening a row of
/// this list runs the same load and first puts the stored hand split, fall speed and tempo back through
/// the very controls the user would move, so the stage, the settings file and the undo history all follow
/// their ordinary paths instead of a private shortcut.
/// </summary>
public partial class MainWindow
{
    /// <summary>Rows the Play dialog shows; the file keeps more for the library list.</summary>
    internal const int RecentSongRows = 5;

    /// <summary>Rebuilds the list: when the dialog opens, after a language switch and after every change.</summary>
    internal void RefreshRecentSongs()
    {
        if (RecentSongHost is null) return;
        RecentSongHost.Children.Clear();
        var recent = SongLibrary.Entries.Take(RecentSongRows).ToList();
        if (RecentSongsEmpty is not null) RecentSongsEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in recent)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = new Button
            {
                Content = entry.Title,
                Tag = entry,
                Style = (Style)FindResource("GhostButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 7, 12, 7)
            };
            open.Click += RecentSong_Click;
            Loc.Set(open, "Reopen this song and restore the values it was last played at", FrameworkElement.ToolTipProperty);
            Loc.Set(open, "Reopen this song and restore the values it was last played at", AutomationProperties.NameProperty);
            var body = new StackPanel();
            body.Children.Add(open);
            var facts = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(12, 4, 0, 0) };
            facts.Text = Loc.F("{0} · {1} notes · {2} tracks · {3:0} BPM", Fmt(entry.Seconds), entry.Notes, entry.Tracks, entry.BeatsPerMinute);
            body.Children.Add(facts);
            if (!string.IsNullOrWhiteSpace(entry.Preset))
            {
                var preset = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(12, 2, 0, 0) };
                preset.Text = Loc.F("Last preset: {0}", entry.Preset);
                body.Children.Add(preset);
            }
            var forget = new Button
            {
                Content = "×",
                Tag = entry,
                Style = (Style)FindResource("MiniButtonStyle"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6, 4, 0, 0)
            };
            forget.Click += RecentSongForget_Click;
            Loc.Set(forget, "Forget this song", FrameworkElement.ToolTipProperty);
            Loc.Set(forget, "Forget this song", AutomationProperties.NameProperty);
            Grid.SetColumn(forget, 1);
            row.Children.Add(body);
            row.Children.Add(forget);
            RecentSongHost.Children.Add(row);
        }
    }

    private void RecentSong_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SongEntry entry) return;
        OpenSongFromLibrary(entry);
    }

    private void RecentSongForget_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SongEntry entry) return;
        SongLibrary.Forget(entry.Path);
        RefreshRecentSongs();
    }

    /// <summary>Opens a remembered song and puts the values stored with it back on the controls.</summary>
    internal void OpenSongFromLibrary(SongEntry entry)
    {
        if (!File.Exists(entry.Path))
        {
            // A file moved or deleted outside the app is forgotten instead of being offered again.
            SongLibrary.Forget(entry.Path);
            RefreshRecentSongs();
            ShowMessage(Loc.F("This song is no longer on disk.\n{0}", entry.Path), "MIDI import");
            return;
        }
        RestoreSongValues(entry);
        OpenMidiFile(entry.Path);
    }

    /// <summary>Moves the controls that carry the values stored with a song, clamped to their ranges.</summary>
    private void RestoreSongValues(SongEntry entry)
    {
        if (_visualSliders.TryGetValue(nameof(PianoVisualSettings.HandSplitPitch), out var hand))
            hand.Value = Math.Clamp(entry.HandSplitPitch, hand.Minimum, hand.Maximum);
        if (_visualSliders.TryGetValue(nameof(PianoVisualSettings.NoteFallSpeed), out var speed))
            speed.Value = Math.Clamp(entry.FallSpeed, speed.Minimum, speed.Maximum);
        if (TempoSlider is not null) TempoSlider.Value = Math.Clamp(entry.TempoPercent, TempoSlider.Minimum, TempoSlider.Maximum);
    }

    /// <summary>Writes the entry for the song that just opened, with the values the controls carry now.</summary>
    private void RememberSong(string path, MidiSong song)
    {
        var seconds = song.Notes.Count == 0 ? 0 : song.Notes.Max(note => note.End);
        SongLibrary.Remember(
            path,
            Path.GetFileNameWithoutExtension(path),
            song.Notes.Count,
            song.Notes.Select(note => note.Track).Distinct().Count(),
            seconds,
            AverageTempo(song.BeatTimes),
            _visualSettings.HandSplitPitch,
            _visualSettings.NoteFallSpeed,
            TempoSlider?.Value ?? 100,
            SelectedPreset?.Name ?? "");
        RefreshRecentSongs();
    }

    /// <summary>Average tempo of the metronome grid; 0 when the file carries fewer than two beats.</summary>
    private static double AverageTempo(IReadOnlyList<double> beats)
    {
        if (beats.Count < 2) return 0;
        var span = beats[^1] - beats[0];
        return span > .001 ? 60 * (beats.Count - 1) / span : 0;
    }
}
