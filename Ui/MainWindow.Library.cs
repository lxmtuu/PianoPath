using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

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

    /// <summary>Rows the Play dialog shows of the indexed folder; the index keeps every file it found.</summary>
    internal const int LibraryRows = 8;

    /// <summary>How often the window asks whether the watched folder changed, once the watcher has raised it.</summary>
    internal const int LibraryWatchSeconds = 2;

    private SongFolderWatcher? _songWatcher;
    private DispatcherTimer? _songWatchTimer;

    /// <summary>Set by the file-system watcher (possibly on another thread) and cleared by the timer below.</summary>
    private volatile bool _libraryDirty;

    /// <summary>The split point of the song on the stage came from <see cref="HandSplit"/> rather than from the user.</summary>
    private bool _splitInferred;

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
        // A remembered song may be a score as well as a MIDI file; the extension decides which reader runs.
        OpenSongFile(entry.Path);
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
            SelectedPreset?.Name ?? "",
            _splitInferred);
        RefreshRecentSongs();
    }

    /// <summary>
    /// Picks the hand split point of a freshly opened song when the setting is on: a value already
    /// remembered for this file wins, so the split stays what it was the last time, and only a song the
    /// library has never seen is measured again. The result is written through the dock slider, so the
    /// colour mode, the practice modes and the settings file follow their ordinary paths.
    /// </summary>
    /// <summary>
    /// The hand split of a freshly opened song, in order of authority: what the score's staves say
    /// (<paramref name="score"/>), then a value already remembered for this file, then a measurement of the
    /// pitches. A remembered value still beats a fresh measurement, so the split stays what it was the last
    /// time. The result is written through the dock slider, so the colour mode, the practice modes and the
    /// settings file follow their ordinary paths.
    /// </summary>
    private void ApplySongHandSplit(string path, MidiSong song, MusicXmlScore? score)
    {
        if (score?.HandSplitPitch is { } fromStaves && _visualSettings.InferHandSplit)
        {
            _splitInferred = true;
            SetHandSplit(fromStaves);
            return;
        }
        ApplyInferredHandSplit(path, song);
    }

    /// <summary>
    /// Pushes a hand-split pitch through its dock slider, or straight into the settings when the row is not
    /// built. The pitch arrives as a double because the song library stores it next to the song's other
    /// remembered values; the slider rounds it the same way a hand on the slider would.
    /// </summary>
    private void SetHandSplit(double pitch)
    {
        if (_visualSliders.TryGetValue(nameof(PianoVisualSettings.HandSplitPitch), out var slider))
            slider.Value = Math.Clamp(pitch, slider.Minimum, slider.Maximum);
        else _visualSettings.HandSplitPitch = Math.Clamp(pitch, 21, 108);
    }

    private void ApplyInferredHandSplit(string path, MidiSong song)
    {
        _splitInferred = false;
        if (!_visualSettings.InferHandSplit) return;
        var remembered = SongLibrary.Find(path);
        var split = remembered is { SplitInferred: true }
            ? remembered.HandSplitPitch
            : HandSplit.Infer(song.Notes, _visualSettings.HandSplitPitch);
        _splitInferred = true;
        SetHandSplit(split);
    }

    /// <summary>Average tempo of the metronome grid; 0 when the file carries fewer than two beats.</summary>
    private static double AverageTempo(IReadOnlyList<double> beats) => SongFolderIndex.AverageTempo(beats);

    // =====================================================================================
    // Library of a folder
    // =====================================================================================

    /// <summary>
    /// Rebuilds the LIBRARY section of the Play dialog: where the indexed folder is, whether it is being
    /// watched, the songs matching what is typed in the search box, and the tags of each one. The list is the
    /// model's own answer (<see cref="SongFolderIndex.Search"/>), so what the dialog shows is what a scan found.
    /// </summary>
    internal void RefreshLibrarySongs()
    {
        if (LibrarySongHost is null) return;
        LibrarySongHost.Children.Clear();
        var folder = SongFolderIndex.Folder;
        var hasSongs = folder.Length > 0 && SongFolderIndex.Songs.Count > 0;
        if (LibrarySearchBox is not null) LibrarySearchBox.Visibility = hasSongs ? Visibility.Visible : Visibility.Collapsed;
        if (RescanSongFolderButton is not null) RescanSongFolderButton.IsEnabled = folder.Length > 0;
        if (LibraryFolderLabel is not null)
            LibraryFolderLabel.Text = folder.Length == 0
                ? Loc.T("No folder is indexed yet. Choose one and every MIDI file and MusicXML score under it is listed here with its notes, tracks, length and tempo.")
                : _songWatcher is { IsWatching: true }
                    ? Loc.F("{0} songs in {1} · watching for changes", SongFolderIndex.Songs.Count, folder)
                    : Loc.F("{0} songs in {1}", SongFolderIndex.Songs.Count, folder);
        var songs = SongFolderIndex.Search(LibrarySearchBox?.Text).Take(LibraryRows).ToList();
        foreach (var song in songs) LibrarySongHost.Children.Add(LibraryRow(song));
        if (LibraryEmptyLabel is not null)
            LibraryEmptyLabel.Text = folder.Length == 0 || SongFolderIndex.Songs.Count == 0
                ? ""
                : songs.Count == 0
                    ? Loc.T("No song in this folder matches what you typed. Tags and the file name are searched too.")
                    : Loc.F("Showing {0} of {1} matching songs", songs.Count, SongFolderIndex.Search(LibrarySearchBox?.Text).Count);
    }

    /// <summary>One library row: open it, read its facts, and change the tags it carries.</summary>
    private UIElement LibraryRow(SongFile song)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var open = new Button
        {
            Content = song.Title, Tag = song, Style = (Style)FindResource("GhostButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 7, 12, 7)
        };
        open.Click += LibrarySong_Click;
        Loc.Set(open, "Open this song from the library folder", FrameworkElement.ToolTipProperty);
        Loc.Set(open, "Open this song from the library folder", AutomationProperties.NameProperty);
        var body = new StackPanel();
        body.Children.Add(open);
        var facts = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(12, 4, 0, 0) };
        facts.Text = Loc.F("{0} · {1} notes · {2} tracks · {3:0} BPM", Fmt(song.Seconds), song.Notes, song.Tracks, song.BeatsPerMinute);
        body.Children.Add(facts);
        var tagRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4, 0, 0) };
        foreach (var tag in song.Tags)
        {
            var chip = new Button
            {
                Content = "#" + tag, Tag = (song, tag), Style = (Style)FindResource("MiniButtonStyle"),
                Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 3, 8, 3)
            };
            chip.Click += LibraryTagRemove_Click;
            Loc.Set(chip, "Remove this tag", FrameworkElement.ToolTipProperty);
            Loc.Set(chip, "Remove this tag", AutomationProperties.NameProperty);
            tagRow.Children.Add(chip);
        }
        if (song.Tags.Count < SongFolderIndex.MaxTags)
        {
            var add = new Button { Content = Loc.T("+ TAG"), Tag = song, Style = (Style)FindResource("MiniButtonStyle"), Padding = new Thickness(8, 3, 8, 3) };
            add.Click += LibraryTag_Click;
            Loc.Set(add, "Give this song a tag to search by", AutomationProperties.NameProperty);
            tagRow.Children.Add(add);
        }
        body.Children.Add(tagRow);
        Grid.SetColumn(body, 0);
        row.Children.Add(body);
        var info = new TextBlock { Text = song.Format.ToUpperInvariant(), Style = (Style)FindResource("MutedTextStyle"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 10, 0, 0) };
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        return row;
    }

    /// <summary>Indexes a folder and starts watching it, so the list follows the disk from then on.</summary>
    internal void IndexSongFolder(string folder)
    {
        SongFolderIndex.Scan(folder);
        StartSongFolderWatch(SongFolderIndex.Folder);
        RefreshLibrarySongs();
    }

    /// <summary>
    /// Starts (or restarts) watching a folder. The watcher only raises <see cref="_libraryDirty"/> — the timer
    /// below does the rescanning on the window's own thread, so a change never draws from a watcher thread.
    /// </summary>
    internal void StartSongFolderWatch(string folder)
    {
        _songWatcher ??= new SongFolderWatcher(() => _libraryDirty = true);
        if (_songWatchTimer is null)
        {
            _songWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(LibraryWatchSeconds) };
            _songWatchTimer.Tick += (_, _) =>
            {
                if (!_libraryDirty) return;
                _libraryDirty = false;
                if (SongFolderIndex.Folder.Length == 0) return;
                SongFolderIndex.Scan(SongFolderIndex.Folder);
                RefreshLibrarySongs();
            };
        }
        _songWatchTimer.Start();
        if (folder.Length > 0) _songWatcher.Watch(folder);
    }

    private void ChooseSongFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose the folder that holds your songs"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        IndexSongFolder(dialog.FolderName);
        Status(Loc.F("Indexed {0} song(s) in {1}", SongFolderIndex.Songs.Count, dialog.FolderName));
    }

    private void RescanSongFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SongFolderIndex.Folder.Length == 0) { ChooseSongFolder_Click(sender, e); return; }
        SongFolderIndex.Scan(SongFolderIndex.Folder, force: true);
        RefreshLibrarySongs();
        Status(Loc.F("Read {0} song(s) again", SongFolderIndex.Songs.Count));
    }

    private void LibrarySearch_Changed(object sender, TextChangedEventArgs e) => RefreshLibrarySongs();

    /// <summary>Prints what the library just did under its buttons.</summary>
    private void Status(string text)
    {
        if (LibraryStatusLabel is not null) LibraryStatusLabel.Text = text;
    }

    private void LibrarySong_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SongFile song }) OpenSongFile(song.Path);
    }

    private void LibraryTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SongFile song }) return;
        var prompt = new TextPromptWindow(Loc.T("Tag this song"), Loc.T("A tag to search by, for example a composer or a mood. Tags are not case sensitive."), "") { Owner = this };
        if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.Result)) return;
        if (SongFolderIndex.Tag(song.Path, prompt.Result) is null) return;
        RefreshLibrarySongs();
    }

    private void LibraryTagRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ValueTuple<SongFile, string> entry }) return;
        SongFolderIndex.Untag(entry.Item1.Path, entry.Item2);
        RefreshLibrarySongs();
    }

    /// <summary>Stops watching the folder; called when the window closes.</summary>
    private void StopSongFolderWatch()
    {
        _songWatchTimer?.Stop();
        _songWatcher?.Stop();
    }
}
