using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PianoPath;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _chromeTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly Stopwatch _clock = new();
    /// <summary>True while the stage is subscribed to the shared frame clock (playback, live keys or running effects).</summary>
    private bool _stageFrames;
    /// <summary>Theme currently published to Application.Resources; a change repaints the whole shell.</summary>
    private ShellTheme _appliedShellTheme = ShellThemes.Default;
    private string _backdropSignature = "";
    private bool _chromeMotionLocked, _sweepStarted;
    private readonly Stopwatch _recordClock = new();
    private readonly MidiDeviceService _midi = new();
    private readonly PianoAudioEngine _audio = new();
    private readonly Dictionary<string, Slider> _visualSliders = [];
    private readonly Dictionary<string, TextBox> _visualColorInputs = [];
    private readonly Dictionary<string, Button> _visualColorButtons = [];
    private PianoVisualSettings _visualSettings = new();
    private IFrameRecorder? _videoRecorder;
    private DispatcherTimer? _recordTimer;
    private string? _recordingPath;
    private List<NoteEvent> _allNotes = [];
    private List<NoteEvent> _notes = [];
    private readonly HashSet<int> _pressed = [];
    /// <summary>Scratch buffer for the per-frame held-note sweeps; reused so playback allocates nothing.</summary>
    private readonly List<NoteEvent> _finishedNotes = [];
    private readonly HashSet<NoteEvent> _outputHeld = [];
    private readonly HashSet<NoteEvent> _audioHeld = [];
    private readonly HashSet<NoteEvent> _outputFinished = [];
    private readonly HashSet<PianoPedal> _pedalsDown = [];
    private readonly Dictionary<Key, int> _keyDownPitches = [];
    private int _hits, _misses, _streak, _bestStreak, _activeTrack = -1;
    private double _position, _tempo = 1, _loopA = -1, _loopB = -1, _metronomeOffAt = -1;
    private bool _playing, _isSeeking, _updatingSeek, _updatingPedals, _suppressDevices, _suppressTracks, _suppressPreset, _processCurrentOnsets, _fullScreen = true, _uiReady, _isBuiltInSoundFont, _closing, _chromeVisible = true, _loadingVisualSettings;
    /// <summary>True once the user deliberately picked a MIDI input; until then refreshes auto-connect the first keyboard.</summary>
    private bool _inputChoiceByUser;
    private DateTime _lastPointerActivity = DateTime.UtcNow;
    private Point? _lastPointerPoint;
    private bool _settingsHiddenByIdle;
    /// <summary>Notes before this index are already played, missed or intentionally skipped by a seek; only later notes can still be missed.</summary>
    private int _missScanIndex;
    private IReadOnlyList<double> _beatTimes = [];
    private IReadOnlyDictionary<int, string> _trackNames = new Dictionary<int, string>();
    private int _beatsPerBar = 4, _nextBeat;
    private IReadOnlyList<NoteEvent>? _songDurationSource;
    private double _songDuration;
    private const int MetronomePitch = 77;
    private static readonly TimeSpan ChromeIdleDelay = TimeSpan.FromSeconds(2.8);
    /// <summary>Set to false for automated snapshots so the toolbar and settings never disappear while a capture is pending.</summary>
    internal bool AutoHideChrome { get; set; } = true;
    /// <summary>Set by the verification suite so a problem lands in the log instead of a modal dialog nobody can dismiss.</summary>
    internal bool SuppressErrorDialogs { get; set; }
    private static readonly int[] ComputerMap = [0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21];
    private static readonly Key[] ComputerKeys = [Key.A, Key.W, Key.S, Key.E, Key.D, Key.F, Key.T, Key.G, Key.Y, Key.H, Key.U, Key.J, Key.K];

    public MainWindow(bool loadBuiltInSoundFont = true)
    {
        InitializeComponent();
        _visualSettings = PianoVisualSettingsStore.Load();
        BuildVisualSettingsControls();
        // The XAML literals are the translation keys (see Ui/MainWindow.xaml), so the window marks
        // its own tree and paints it once; every later switch repaints through the same registry.
        Loc.LocalizeTree(this);
        Loc.Refresh();
        SettingsTabs.Loaded += (_, _) => RefreshSectionHeaders();
        Stage.SetVisualSettings(_visualSettings);
        _chromeTimer.Tick += (_, _) => CheckChromeIdle();
        // The same idle tick that writes the settings file closes the undo step in progress: one
        // settled change (or one slider drag) is one history point.
        _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer.Stop(); CommitHistory(); SaveVisualSettings(); };
        _uiReady = true;
        StartHistory();
        _notes = _allNotes;
        FrameClock.Shared.Tick += OnFrame;
        _midi.NoteChanged += (pitch, velocity, on) => Dispatcher.BeginInvoke(() =>
        {
            if (on)
            {
                Loc.Format(DeviceLabel, "MIDI IN · {0}", NoteLabel(pitch));
                DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(75, 244, 187));
                PressNote(pitch, velocity);
            }
            else ReleaseNote(pitch);
        });
        _midi.PedalChanged += (pedal, down) => Dispatcher.BeginInvoke(() => SetPedalState(pedal, down));
        PopulateTracks(); RefreshDevices(); UpdateSoundFontUi(); RefreshPracticeHistory(); UpdateSongUi(); UpdateStage(); UpdateStats(); UpdateTime();
        ApplyChromeTheme();
        StartChromeSweeps();
        SetChromeVisible(true); _chromeTimer.Start();
        if (loadBuiltInSoundFont) Loaded += MainWindow_Loaded;
    }
    internal bool HasSoundFont => _audio.HasSoundFont;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "ConcertGrand.sf2");
        if (SoundFontReader.IsLfsPointer(bundled))
        {
            // The checkout skipped Git LFS. Report the fix instead of a confusing "not an SF2 file" parse error.
            UpdateSoundFontUi();
            Loc.Set(SoundFontHint, "ConcertGrand.sf2 is a Git LFS pointer · run 'git lfs install' then 'git lfs pull', or load another .sf2");
            return;
        }
        await LoadSoundFontAsync(bundled, builtIn: true);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        Stop(); _position = 0; _processCurrentOnsets = true; ResetScore(); _outputFinished.Clear(); UpdateStage(); UpdateStats(); UpdateTime(); StartPlayback();
    }
    private void TogglePlay() { if (_playing) Stop(); else StartPlayback(); }
    private void StartPlayback()
    {
        if (_playing || SongDuration() <= 0) return;
        if (_position >= SongDuration()) { _position = 0; _outputFinished.Clear(); foreach (var n in _notes) { n.Played = false; n.Missed = false; } SyncPlayhead(); }
        _playing = true; _processCurrentOnsets = true; StartStageFrames(); PlayButton.Tag = FindResource("IconPause"); UpdatePlaybackLabel();
        UpdateStage();
    }
    private void Stop()
    {
        RecordPracticeRunIfScored(); // a take that graded something is written before the transport resets
        StopStageFrames(); _playing = false; _processCurrentOnsets = false; _metronomeOffAt = -1;
        PlayButton.Tag = FindResource("IconPlay");
        foreach (var note in _outputHeld.ToArray()) SendOutput(note.Pitch, 0, false);
        _outputHeld.Clear(); _audioHeld.Clear(); _audio.AllNotesOff(); ReleaseAllPressed(); Stage.ClearTransient(); UpdateStage(); UpdatePlaybackLabel();
    }

    /// <summary>
    /// One animation frame, delivered by the shared <see cref="FrameClock"/> on the compositor's
    /// cadence. The wall-clock delta is clamped so a stall (window drag, debugger, sleep) cannot
    /// teleport the playhead; the stage clamps its own physics separately.
    /// </summary>
    private void OnFrame(double delta)
    {
        if (!_stageFrames) return;
        Tick(Math.Clamp(delta, 0, .25));
    }

    /// <summary>Subscribes the stage to the frame clock; the clock unloads itself once every consumer leaves.</summary>
    private void StartStageFrames()
    {
        if (_stageFrames) return;
        _stageFrames = true;
        _clock.Restart();
        FrameClock.Shared.Acquire();
    }

    private void StopStageFrames()
    {
        if (!_stageFrames) return;
        _stageFrames = false;
        _clock.Stop();
        FrameClock.Shared.Release();
    }

    private void Tick(double elapsed)
    {
        if (_playing)
        {
            var previous = _position; var forceOnset = _processCurrentOnsets; _processCurrentOnsets = false;
            if (!_isSeeking) _position += elapsed * _tempo;
            if (_loopB > _loopA && _loopA >= 0 && _position >= _loopB)
            {
                _position = _loopA; previous = _loopA; forceOnset = true; _outputFinished.Clear(); ReleasePlaybackNotes();
                // Every pass through the loop is a fresh attempt: clear the scoring flags inside the loop so the notes can be played and judged again.
                for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _loopA); i < _notes.Count; i++) { _notes[i].Played = false; _notes[i].Missed = false; _notes[i].Timing = 0; }
                SyncPlayhead();
            }
            if (ModeCombo.SelectedIndex == 1)
            {
                var next = NextExpectedNote();
                if (next != null && _position >= next.Start) { _position = next.Start; if (previous > _position) forceOnset = true; _clock.Restart(); }
            }
            // Release song notes whose end has passed before starting new ones, so a repeated pitch is not cut off by the previous note's Note Off.
            // Both sweeps run every frame, so they collect into a reused scratch list instead of allocating LINQ arrays 60 times a second.
            if (_audioHeld.Count > 0)
            {
                _finishedNotes.Clear();
                foreach (var note in _audioHeld) if (note.End <= _position) _finishedNotes.Add(note);
                foreach (var note in _finishedNotes) { _audioHeld.Remove(note); _audio.NoteOff(note.Pitch); }
            }
            if (_outputHeld.Count > 0)
            {
                _finishedNotes.Clear();
                foreach (var note in _outputHeld) if (note.End <= _position) _finishedNotes.Add(note);
                foreach (var note in _finishedNotes) { _outputHeld.Remove(note); SendOutput(note.Pitch, 0, false); }
            }
            // Onsets in (onsetFrom, position]; after a jump the window also covers notes within 25 ms of the new playhead.
            var onsetFrom = forceOnset ? Math.Min(previous, _position - .025) : previous;
            for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, onsetFrom); i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Start > _position) break;
                if (note.Start <= onsetFrom || !_outputFinished.Add(note) || note.Played) continue;
                _audio.NoteOn(note.Pitch, note.Velocity); _audioHeld.Add(note);
                SendOutput(note.Pitch, note.Velocity, true); _outputHeld.Add(note);
                Stage.Impact(note.Pitch, note.Velocity / 127.0); // real per-note velocity drives size, brightness and color modulators
            }
            // Notes are sorted by start, so the miss scan only ever advances instead of re-reading the whole song every frame.
            var missLimit = _position - .38;
            while (_missScanIndex < _notes.Count && _notes[_missScanIndex].Start < missLimit)
            {
                var note = _notes[_missScanIndex++];
                if (!note.Played && !note.Missed) { note.Missed = true; _misses++; _streak = 0; RecordPracticeNote(false); }
            }
            TickMetronome(previous, forceOnset);
        }
        Stage.Advance(elapsed);
        if (_playing && _position >= SongDuration()) { _position = SongDuration(); Stop(); }
        UpdateStage(); UpdateTime(); UpdateStats();
        if (!_playing && !Stage.HasActiveEffects) StopStageFrames();
    }

    private void UpdateStage() => Stage.SetState(_notes, _position, _playing, _pressed);
    private NoteEvent? NextExpectedNote()
    {
        for (var i = _missScanIndex; i < _notes.Count; i++) { var note = _notes[i]; if (!note.Played && !note.Missed) return note; }
        return null;
    }
    /// <summary>Re-anchors the miss scanner and metronome after the playhead jumps (seek, restart, loop, filter change).</summary>
    private void SyncPlayhead()
    {
        _missScanIndex = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - .38);
        if (_metronomeOffAt >= 0) { _audio.NoteOff(MetronomePitch); _metronomeOffAt = -1; }
        _nextBeat = NoteTimeline.FirstIndexAtOrAfter(_beatTimes, _position);
    }
    /// <summary>Metronome that follows the MIDI tempo map and time signature instead of a fixed 100 BPM grid.</summary>
    private void TickMetronome(double previous, bool forceOnset)
    {
        if (_metronomeOffAt >= 0 && _position >= _metronomeOffAt) { _audio.NoteOff(MetronomePitch); _metronomeOffAt = -1; }
        if (_beatTimes.Count == 0) return;
        var click = false; var downbeat = false;
        while (_nextBeat < _beatTimes.Count && _beatTimes[_nextBeat] <= _position)
        {
            if (_beatTimes[_nextBeat] > previous || forceOnset) { click = true; downbeat = _beatsPerBar > 0 && _nextBeat % _beatsPerBar == 0; }
            _nextBeat++;
        }
        if (click) Stage.PulseBeat(downbeat ? 1 : .6); // Tempo Sync follows the MIDI tempo map even when the click is muted.
        if (click && MetronomeCheck.IsChecked == true && _audio.HasSoundFont) { _audio.NoteOn(MetronomePitch, downbeat ? 84 : 62); _metronomeOffAt = _position + .08; }
    }
    private void TempoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tempo = e.NewValue / 100; if (TempoLabel is not null) TempoLabel.Text = $"{e.NewValue:0}%";
    }
    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || ModeCombo.SelectedIndex < 0) return;
        ApplyTrackFilter(); UpdateSongUi(); UpdatePlaybackLabel(); ResetScore(); UpdateStage(); UpdateTime();
    }
    private void Stage_PianoKeyChanged(int pitch, bool down) { if (down) PressNote(pitch); else ReleaseNote(pitch); }

    private void OpenMidi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("MIDI files (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*") };
        if (dialog.ShowDialog(this) != true) return;
        OpenMidiFile(dialog.FileName);
    }

    /// <summary>Loads a Standard MIDI file — from the open dialog, the Play dialog or a drop on the window.</summary>
    internal void OpenMidiFile(string path)
    {
        try
        {
            Stop(); var song = MidiReader.ReadSong(path); if (song.Notes.Count == 0) throw new InvalidDataException(Loc.T("No notes were found in this MIDI file."));
            _allNotes = song.Notes; _beatTimes = song.BeatTimes; _beatsPerBar = song.BeatsPerBar; _trackNames = song.TrackNames;
            _songLabel = Path.GetFileNameWithoutExtension(path); _songPath = path;
            Loc.Bind(SongTitle, () => _songLabel); // a file name is the user's text, not a key
            _position = 0; ResetScore(); _outputFinished.Clear(); PopulateTracks(); ApplyTrackFilter(); UpdateSongUi(); UpdatePlaybackLabel(); UpdateTime(); UpdateStage();
            // The recent list is written only after the file really parsed, so the Play dialog never
            // offers a song that failed to open; the split point follows before the entry remembers it.
            ApplyInferredHandSplit(path, song);
            RememberSong(path, song);
        }
        catch (Exception ex) { ShowMessage(Loc.F("Could not read this MIDI file.\n{0}", ex.Message), "MIDI import"); }
    }

    private async void LoadSoundFont_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("SoundFont 2 files (*.sf2)|*.sf2|All files (*.*)|*.*"), Title = Loc.T("Choose a piano SoundFont") };
        if (dialog.ShowDialog(this) != true) return;
        await LoadSoundFontAsync(dialog.FileName, builtIn: false);
    }

    private async Task LoadSoundFontAsync(string path, bool builtIn)
    {
        if (_closing) return;
        SoundFontButton.IsEnabled = false;
        Loc.Set(SoundFontLabel, "LOADING SOUNDFONT…");
        Loc.Set(SoundFontHint, "Large sample banks may take a few seconds");
        try
        {
            await Task.Run(() => _audio.LoadSoundFont(path));
            if (_closing) { _audio.UnloadSoundFont(); return; }
            foreach (var pedal in _pedalsDown) _audio.ControlChange(MidiDeviceService.ControllerFor(pedal), 127);
            _isBuiltInSoundFont = builtIn;
            _suppressPreset = true;
            try
            {
                PresetCombo.ItemsSource = _audio.Presets.Select(p => $"{p.Bank:000} · {p.Program:000}  {p.Name}").ToList();
                var preset = _audio.Presets.FirstOrDefault(p => p.Bank == 0 && p.Program == 0) ?? _audio.Presets.First();
                PresetCombo.SelectedIndex = _audio.Presets.ToList().IndexOf(preset);
            }
            finally { _suppressPreset = false; }
            UpdateSoundFontUi();
        }
        catch (Exception ex)
        {
            if (_closing) return;
            Loc.Set(SoundFontLabel, _audio.HasSoundFont ? "PREVIOUS SOUNDFONT STILL ACTIVE" : "NO SOUNDFONT · SILENT");
            Loc.Bind(SoundFontHint, () => ex.Message); ShowError(Loc.F("Could not load this SoundFont.\n{0}", ex.Message), "SoundFont");
        }
        finally { if (!_closing) SoundFontButton.IsEnabled = true; }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPreset || PresetCombo.SelectedIndex < 0 || PresetCombo.SelectedIndex >= _audio.Presets.Count) return;
        var preset = _audio.Presets[PresetCombo.SelectedIndex]; _audio.SelectPreset(preset.Bank, preset.Program);
        Loc.Format(SoundFontHint, "Instrument · {0}", preset.Name);
    }
    private void UnloadSoundFont_Click(object sender, RoutedEventArgs e) { _audio.UnloadSoundFont(); _isBuiltInSoundFont = false; _suppressPreset = true; PresetCombo.ItemsSource = null; PresetCombo.SelectedIndex = -1; _suppressPreset = false; UpdateSoundFontUi(); }
    /// <summary>Shows a warning dialog, or only records it when dialogs are suppressed (verification runs, snapshots).</summary>
    private void ShowError(string message, string titleKey, MessageBoxImage icon = MessageBoxImage.Warning) =>
        ShowMessage(message, titleKey, icon);

    private void UpdateSoundFontUi()
    {
        var loaded = _audio.HasSoundFont;
        // A loaded SoundFont without a usable playback device still drives the stage and scoring, but must not claim to be audible.
        var audible = loaded && _audio.HasAudioOutput;
        if (loaded)
        {
            if (_isBuiltInSoundFont) Loc.Format(SoundFontLabel, "BUILT-IN YAMAHA GRAND · {0}", Loc.T(audible ? "READY" : "NO AUDIO DEVICE"));
            else Loc.Format(SoundFontLabel, "SOUNDFONT {0} · {1}", Loc.T(audible ? "READY" : "LOADED · NO AUDIO DEVICE"), _audio.LoadedName);
        }
        else Loc.Set(SoundFontLabel, "NO SOUNDFONT · SILENT");
        SoundFontLabel.Foreground = audible ? new SolidColorBrush(Color.FromRgb(112, 242, 213)) : new SolidColorBrush(Color.FromRgb(255, 180, 209));
        if (loaded)
        {
            if (_audio.PlaybackError is { } playbackError) Loc.Format(SoundFontHint, "No audio output · {0}", playbackError);
            else Loc.Format(SoundFontHint, "{0} · Hall reverb {1}", _isBuiltInSoundFont ? Loc.T("Yamaha grand") : _audio.LoadedName, Loc.T(_audio.ReverbEnabled ? "ON" : "OFF"));
        }
        else Loc.Set(SoundFontHint, "Load a .sf2 SoundFont to enable piano audio");
        if (!loaded && PresetCombo.Items.Count == 0)
        {
            _suppressPreset = true;
            PresetCombo.ItemsSource = new[] { Loc.T("NO PRESET · LOAD .SF2") };
            PresetCombo.SelectedIndex = 0;
            _suppressPreset = false;
        }
        PresetCombo.IsEnabled = loaded;
        if (MetronomeCheck is not null) { MetronomeCheck.IsEnabled = loaded; if (!loaded) MetronomeCheck.IsChecked = false; }
        UpdatePlaybackLabel();
    }

    private void ReverbToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        _audio.ReverbEnabled = toggle.IsChecked == true;
        if (_uiReady) UpdateSoundFontUi();
    }

    private void UpdateSongUi()
    {
        var hasNotes = _notes.Count > 0;
        PlayButton.IsEnabled = hasNotes;
        RestartButton.IsEnabled = hasNotes;
        SeekSlider.IsEnabled = hasNotes;
        if (!hasNotes) { _position = 0; _outputFinished.Clear(); }
        SyncPlayhead();
    }

    private void UpdatePlaybackLabel()
    {
        if (_playing) Loc.Set(NowPlayingLabel, _audio.HasSoundFont ? "PLAYING · NOTES FALLING" : "PLAYING · SILENT WITHOUT SOUNDFONT");
        else if (_notes.Count == 0) Loc.Set(NowPlayingLabel, _audio.HasSoundFont ? "LIVE PLAY · SOUNDFONT READY" : "LIVE PLAY · PRESS A KEY");
        else Loc.Set(NowPlayingLabel, _audio.HasSoundFont ? "MIDI LOADED · READY TO PLAY" : "MIDI LOADED · SILENT UNTIL SF2");
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; return; }
        if (e.Key == Key.F1) { ToggleShortcuts(); e.Handled = true; return; }
        // Ctrl+Z / Ctrl+Shift+Z (and Ctrl+Y) walk the design dock's history. A text box keeps its own
        // undo, so the shortcut only takes over when the focus is not in one.
        if (e.Key is Key.Z or Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Y || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) RedoVisualSettings(); else UndoVisualSettings();
            e.Handled = true; return;
        }
        if (e.Key == Key.Escape)
        {
            // Escape closes the help card first, then clears an active settings search, then toggles
            // the settings dock — the layer that is on screen always wins.
            if (ShortcutsVisible) { HideShortcuts(); e.Handled = true; return; }
            if (SettingsSearchBox.IsKeyboardFocused && SettingsSearchBox.Text.Length > 0) { SettingsSearchBox.Text = ""; e.Handled = true; return; }
            if (SettingsPanel.Visibility == Visibility.Visible) CloseSettingsPanel(); else OpenSettingsPanel();
            e.Handled = true; return;
        }
        // Typing inside the settings panel (hex colors, combo boxes) counts as activity and must not play piano keys.
        if (SettingsPanel.IsKeyboardFocusWithin || Keyboard.FocusedElement is TextBox) { _lastPointerActivity = DateTime.UtcNow; return; }
        var pitch = MapComputerKey(e.Key);
        if (pitch >= 0)
        {
            if (!_keyDownPitches.ContainsKey(e.Key)) _keyDownPitches[e.Key] = pitch;
            PressNote(pitch); e.Handled = true;
        }
        else if (e.Key == Key.Space) { TogglePlay(); e.Handled = true; }
    }
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        // WPF re-raises MouseMove when the layout under a stationary cursor changes (which hiding the toolbar does); only real movement counts.
        var point = e.GetPosition(this);
        if (_lastPointerPoint is { } last && Math.Abs(last.X - point.X) < .5 && Math.Abs(last.Y - point.Y) < .5) return;
        _lastPointerPoint = point;
        _lastPointerActivity = DateTime.UtcNow;
        if (MainMenuOverlay?.Visibility == Visibility.Visible || PlayDialogOverlay?.Visibility == Visibility.Visible || ShortcutsVisible) return;
        if (_settingsHiddenByIdle) { _settingsHiddenByIdle = false; SettingsPanel.Visibility = Visibility.Visible; }
        SetChromeVisible(true, showRecordButton: true);
        if (Stage is not null) Stage.SetPointerPosition(e.GetPosition(Stage));
    }
    private void CheckChromeIdle()
    {
        if (_closing || !AutoHideChrome) return;
        if (MainMenuOverlay?.Visibility == Visibility.Visible || PlayDialogOverlay?.Visibility == Visibility.Visible || ShortcutsVisible) return;
        // Keep everything on screen while a color picker is open or the user is dragging a slider / browsing a drop-down.
        if (OwnedWindows.Count > 0 || Mouse.Captured is not null) return;
        if (DateTime.UtcNow - _lastPointerActivity >= ChromeIdleDelay) HideChromeForIdle();
    }
    /// <summary>Idle state: only the stage (keys, background, falling notes) stays visible; the toolbar and settings return on mouse movement.</summary>
    private void HideChromeForIdle()
    {
        if (SettingsPanel.Visibility == Visibility.Visible) { _settingsHiddenByIdle = true; SettingsPanel.Visibility = Visibility.Collapsed; }
        SetChromeVisible(false);
    }
    private void OpenSettingsPanel()
    {
        _settingsHiddenByIdle = false;
        SettingsTabs.SelectedIndex = Math.Max(0, SettingsTabs.SelectedIndex);
        var wasHidden = SettingsPanel.Visibility != Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Visible;
        SetChromeVisible(true, showRecordButton: false); _lastPointerActivity = DateTime.UtcNow;
        // The dock slides in from the stage edge; the clip of the stage grid keeps it inside the frame.
        if (wasHidden) ChromeMotion.SlideIn(SettingsPanel, 56, 0, 320);
    }
    private void CloseSettingsPanel()
    {
        _settingsHiddenByIdle = false;
        SettingsPanel.Visibility = Visibility.Collapsed;
        SetChromeVisible(true, showRecordButton: false); _lastPointerActivity = DateTime.UtcNow;
        Stage.Focus();
    }
    private void SetChromeVisible(bool visible, bool showRecordButton = true)
    {
        var showOverlay = visible && SettingsPanel.Visibility != Visibility.Visible;
        var showRecord = showOverlay && showRecordButton;
        if (_chromeVisible == visible && LiveChromeOverlay.Visibility == (showOverlay ? Visibility.Visible : Visibility.Collapsed) && RecordButton.Visibility == (showRecord ? Visibility.Visible : Visibility.Collapsed)) return;
        _chromeVisible = visible;
        if (visible)
        {
            HeaderRow.Height = new GridLength(74);
            FooterRow.Height = new GridLength(94);
            HeaderChrome.Opacity = 1; FooterChrome.Opacity = 1;
            ChromeMotion.FadeIn(HeaderChrome, 240); ChromeMotion.FadeIn(FooterChrome, 240);
        }
        else
        {
            // Fade first, then collapse the rows: collapsing instantly while the fade runs would make
            // the stage jump twice. The guard keeps a re-show during the fade from leaving a 0-height row.
            var remaining = 2;
            void Collapse()
            {
                if (--remaining > 0 || _chromeVisible) return;
                HeaderRow.Height = new GridLength(0); FooterRow.Height = new GridLength(0);
            }
            if (ChromeMotion.Enabled) { ChromeMotion.FadeOut(HeaderChrome, 170, Collapse); ChromeMotion.FadeOut(FooterChrome, 170, Collapse); }
            else { HeaderChrome.Opacity = 0; FooterChrome.Opacity = 0; Collapse(); Collapse(); }
        }
        HeaderChrome.IsHitTestVisible = visible; FooterChrome.IsHitTestVisible = visible;
        LiveChromeOverlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.Visibility = showRecord ? Visibility.Visible : Visibility.Collapsed;
        DeviceStatusBadge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // =====================================================================================================
    // Shell theme, backdrop and chrome animation
    // =====================================================================================================

    /// <summary>
    /// Publishes the shell theme, the motion budget and the backdrop density taken from the visual
    /// settings. Called on startup and after every settings change; only a real change does work, so
    /// dragging an unrelated slider never rebuilds the backdrop.
    /// </summary>
    internal void ApplyChromeTheme()
    {
        var theme = ShellThemes.Find(_visualSettings.ShellTheme);
        var themeChanged = !string.Equals(theme.Id, _appliedShellTheme.Id, StringComparison.OrdinalIgnoreCase);
        if (themeChanged) { ShellThemeManager.Apply(theme); _appliedShellTheme = theme; }
        ChromeMotion.Enabled = !_chromeMotionLocked && _visualSettings.ChromeMotion != "Off";
        var signature = $"{theme.Id}|{_visualSettings.ChromeMotion}|{_visualSettings.BackdropDensity:0}|{(_chromeMotionLocked ? "locked" : "live")}";
        if (themeChanged || signature != _backdropSignature) ConfigureBackdrops(signature);
        if (themeChanged) UpdateThemeChrome(theme);
    }

    /// <summary>Freezes the chrome animation for automated captures, so screenshots are deterministic.</summary>
    internal void DisableChromeMotion()
    {
        _chromeMotionLocked = true;
        ChromeMotion.Enabled = false;
        _sweepStarted = true;
        HeaderSweep.Background = null; FooterSweep.Background = null;
        ApplyChromeTheme();
    }

    private void ConfigureBackdrops()
    {
        var theme = ShellThemeManager.Current;
        var signature = $"{theme.Id}|{_visualSettings.ChromeMotion}|{_visualSettings.BackdropDensity:0}";
        ConfigureBackdrops(signature);
    }

    private void ConfigureBackdrops(string signature)
    {
        _backdropSignature = signature;
        var theme = ShellThemeManager.Current;
        var motion = _chromeMotionLocked ? "Off" : _visualSettings.ChromeMotion;
        MenuBackdrop.Configure(theme, motion, _visualSettings.BackdropDensity);
        // The dock keeps a quieter version of the same backdrop so the settings text stays readable.
        DockBackdrop.Configure(theme, motion == "Full" ? "Calm" : motion, Math.Min(90, _visualSettings.BackdropDensity));
    }

    /// <summary>Repaints the chrome elements whose colour is not driven by a resource key.</summary>
    private void UpdateThemeChrome(ShellTheme theme)
    {
        if (PlayDialogThemeOrb is not null) PlayDialogThemeOrb.Background = new SolidColorBrush(theme.Accent);
        RefreshMenuThemeChips();
        RefreshMenuStageLook();
        if (_sweepStarted) StartChromeSweeps();
    }

    /// <summary>The header and footer carry a slow comet of the accent colour (unless motion is off).</summary>
    private void StartChromeSweeps()
    {
        if (_sweepStarted || !ChromeMotion.Enabled) return;
        _sweepStarted = true;
        var theme = ShellThemeManager.Current;
        HeaderSweep.Background = SweepBrush(theme, 4.2);
        FooterSweep.Background = SweepBrush(theme, 5.6);
    }

    private static LinearGradientBrush SweepBrush(ShellTheme theme, double period)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(-.4, 0), EndPoint = new Point(.6, 0) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), .34));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(210, theme.Accent.R, theme.Accent.G, theme.Accent.B), .50));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), .66));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        ChromeMotion.StartSweep(brush, period);
        return brush;
    }

    private void RecordVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_videoRecorder is not null) { StopVideoRecording(showMessage: true); return; }
        var sequence = _visualSettings.RecordingFormat == RecordingFormatIds.PngSequence;
        var (width, height) = RecordingSize();
        var frameRate = (int)Math.Clamp(_visualSettings.RecordingFrameRate, 15, 60);
        // A folder for the frames, or a file for the video: one choice in the dock decides which recorder runs.
        var target = sequence ? ChooseFrameFolder(width, height, frameRate) : ChooseVideoFile();
        if (target is null) return;
        try
        {
            _videoRecorder = target.Length > 0 && sequence
                ? new PngSequenceRecorder(target, width, height, frameRate)
                : new AviVideoRecorder(target!, width, height, frameRate);
            _recordingPath = target!;
            // The PNG sequence carries alpha, so the stage draws without its opaque background while it runs;
            // the switch is the user's and is only honoured for that format.
            Stage.TransparentBackdrop = sequence && _visualSettings.RecordingTransparent;
            // Poll twice per frame; frames are paced by the recording clock inside RecordTimer_Tick, not by timer ticks.
            _recordClock.Restart(); _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / (_videoRecorder.FrameRate * 2)) };
            _recordTimer.Tick += RecordTimer_Tick; _recordTimer.Start();
            Loc.Set(RecordButton, "REC 00:00"); RecordButton.Background = new SolidColorBrush(Color.FromRgb(104, 23, 42));
            if (_videoRecorder is PngSequenceRecorder)
            {
                Loc.Set(RecordButton, "Recording PNG frames · click to stop", FrameworkElement.ToolTipProperty);
                Loc.Set(SettingsSaveLabel, "Recording a PNG sequence with alpha");
            }
            else if (((AviVideoRecorder)_videoRecorder).UsesMjpeg)
            {
                Loc.Set(RecordButton, "Recording MJPEG AVI · click to stop", FrameworkElement.ToolTipProperty);
                Loc.Set(SettingsSaveLabel, "Video recording started");
            }
            else
            {
                var rawSeconds = AviVideoRecorder.SizeLimitBytes / (double)(AviVideoRecorder.BgrStride(_videoRecorder.Width) * _videoRecorder.Height * _videoRecorder.FrameRate);
                Loc.Format(RecordButton, "Recording raw AVI (no MJPEG codec installed) · about {0:0} s fit in the 2 GB AVI limit · click to stop", FrameworkElement.ToolTipProperty, rawSeconds);
                Loc.Format(SettingsSaveLabel, "Recording raw AVI · about {0:0} s fit before the 2 GB limit", rawSeconds);
            }
        }
        catch (Exception ex)
        {
            StopVideoRecording(showMessage: false);
            ShowMessage(ex.Message, "Video recording");
        }
    }

    /// <summary>The AVI file the next recording goes into, or null when the user cancels.</summary>
    private string? ChooseVideoFile()
    {
        var dialog = new SaveFileDialog { Filter = Loc.T("AVI video (*.avi)|*.avi"), DefaultExt = ".avi", AddExtension = true, FileName = $"Keyflow-{DateTime.Now:yyyyMMdd-HHmmss}.avi", Title = Loc.T("Record piano visualizer") };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>
    /// The folder the PNG frames go into: the user picks a parent, and the frames land in a timestamped
    /// subfolder so two recordings never overwrite each other.
    /// </summary>
    private string? ChooseFrameFolder(int width, int height, int frameRate)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("Choose a folder for the PNG frames"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return null;
        return System.IO.Path.Combine(dialog.FolderName, $"Keyflow-{DateTime.Now:yyyyMMdd-HHmmss}");
    }

    private void RecordTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_videoRecorder is null) return;
            // Frames are due by wall-clock time. A slow capture repeats the last frame instead of letting the video play back too fast.
            var due = (int)Math.Floor(_recordClock.Elapsed.TotalSeconds * _videoRecorder.FrameRate) + 1 - _videoRecorder.FrameCount;
            if (due > 0)
            {
                var recorder = _videoRecorder;
                // The recorder names the buffer it wants: stride-aligned BGR for AVI, tightly packed BGRA
                // (with the stage's alpha) for the PNG sequence.
                var frame = recorder.HasAlpha ? CaptureStageBgra(recorder.Width, recorder.Height) : CaptureStageBgr(recorder.Width, recorder.Height);
                recorder.WriteFrame(frame, Math.Min(due, recorder.FrameRate * 2));
                if (_videoRecorder.IsNearSizeLimit) { StopVideoRecording(showMessage: true, Loc.T("The AVI file reached the 2 GB limit of the AVI format, so recording stopped automatically.")); return; }
            }
            var elapsed = _recordClock.Elapsed;
            Loc.Format(RecordButton, "REC {0:00}:{1:00}", elapsed.Minutes, elapsed.Seconds);
        }
        catch (Exception ex)
        {
            StopVideoRecording(showMessage: false);
            ShowMessage(ex.Message, "Video recording stopped");
        }
    }

    private RenderTargetBitmap? _captureBitmap;
    private byte[]? _captureSource, _captureTarget;

    /// <summary>
    /// Draws the stage into the shared capture bitmap at the recording size and hands back the tightly
    /// packed BGRA pixels. The bitmap and the buffers are reused between frames; at 1280×720 fresh arrays
    /// would add roughly 130 MB/s of garbage while recording.
    /// </summary>
    private byte[] RenderStagePixels(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(Stage) { Stretch = Stretch.Uniform }, null, new Rect(0, 0, width, height));
        if (_captureBitmap is null || _captureBitmap.PixelWidth != width || _captureBitmap.PixelHeight != height)
        {
            _captureBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            _captureSource = new byte[width * 4 * height]; _captureTarget = new byte[AviVideoRecorder.BgrStride(width) * height];
        }
        var bitmap = _captureBitmap; bitmap.Clear(); bitmap.Render(visual);
        var source = _captureSource!; bitmap.CopyPixels(source, width * 4, 0);
        return source;
    }

    /// <summary>
    /// One frame as premultiplied BGRA, alpha included: what the PNG sequence writes (see
    /// <see cref="PngSequenceRecorder"/>). The buffer belongs to the session and is reused every frame.
    /// </summary>
    internal byte[] CaptureStageBgra(int width, int height) => RenderStagePixels(width, height);

    private byte[] CaptureStageBgr(int width, int height)
    {
        var source = RenderStagePixels(width, height);
        var sourceStride = width * 4;
        var targetStride = AviVideoRecorder.BgrStride(width); var target = _captureTarget!;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * sourceStride; var targetRow = (height - 1 - y) * targetStride;
            for (var x = 0; x < width; x++)
            {
                var s = sourceRow + x * 4; var d = targetRow + x * 3;
                target[d] = source[s]; target[d + 1] = source[s + 1]; target[d + 2] = source[s + 2];
            }
        }
        return target;
    }

    private void StopVideoRecording(bool showMessage, string? note = null)
    {
        _recordTimer?.Stop(); _recordTimer = null; _recordClock.Stop();
        var recorder = _videoRecorder; _videoRecorder = null;
        if (recorder is null) return;
        var path = _recordingPath; _recordingPath = null;
        var frames = recorder.FrameCount;
        try { recorder.Dispose(); } catch (Exception ex) { if (showMessage && !_closing) ShowMessage(ex.Message, "Video recording"); }
        // The export is over: the stage goes back to painting its own background, whatever the framing was.
        Stage.TransparentBackdrop = false;
        Loc.Set(RecordButton, "REC"); RecordButton.ClearValue(BackgroundProperty);
        Loc.Set(RecordButton, "Record the live piano visualizer", FrameworkElement.ToolTipProperty);
        if (showMessage && !_closing)
        {
            var noteText = note is null ? "" : note + "\n\n";
            if (recorder is PngSequenceRecorder)
                ShowMessage(Loc.F("Frames saved.\n{0}\n\n{1}{2} PNG frames with an alpha channel. Import them at the frame rate you chose, or follow the ffmpeg line in sequence.json to turn them into alpha video; system audio is not mixed in.", path, noteText, frames), "Recording complete", MessageBoxImage.Information);
            else
                ShowMessage(Loc.F("Video saved.\n{0}\n\n{1}This AVI contains the piano visuals; system audio is not mixed into the recording.", path, noteText), "Recording complete", MessageBoxImage.Information);
        }
    }
    internal static int MapComputerKey(Key key) { var index = Array.IndexOf(ComputerKeys, key); return index < 0 ? -1 : 48 + ComputerMap[index]; }
    private static string NoteLabel(int pitch) { string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]; return $"{names[pitch % 12]}{pitch / 12 - 1}"; }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (_keyDownPitches.Remove(e.Key, out var pitch)) ReleaseNote(pitch); }

    private void PressNote(int pitch, int velocity = 100)
    {
        if (!_pressed.Add(pitch)) return;
        StartStageFrames();
        var hit = velocity / 127.0; Stage.AddLiveNote(pitch, hit); Stage.Impact(pitch, hit); _audio.NoteOn(pitch, velocity); SendOutput(pitch, velocity, true);
        if (_playing)
        {
            // Same result as the previous LINQ Where/OrderBy/First chain, without allocating per keypress.
            NoteEvent? target = null; var bestDelta = .8;
            foreach (var note in _notes)
            {
                if (note.Played || note.Missed || note.Pitch != pitch) continue;
                var delta = Math.Abs(note.Start - _position);
                if (delta > bestDelta) continue;
                if (target is not null && delta >= bestDelta) continue;
                target = note; bestDelta = delta;
            }
            if (target != null)
            {
                target.Played = true; var delta = Math.Abs(target.Start - _position);
                if (delta <= .55) { _hits++; _streak++; _bestStreak = Math.Max(_bestStreak, _streak); RecordPracticeNote(true); } else { _misses++; _streak = 0; RecordPracticeNote(false); }
                target.Timing = Math.Max(0, 100 - delta * 180); Loc.Set(NoteNameLabel, delta < .11 ? "PERFECT" : delta < .28 ? "GREAT" : "KEEP GOING");
                if (ModeCombo.SelectedIndex == 1) _clock.Restart();
            }
            else { _misses++; _streak = 0; RecordPracticeNote(false); NoteNameLabel.Text = NoteLabel(pitch); }
        }
        else NoteNameLabel.Text = NoteLabel(pitch);
        UpdateStats(); UpdateStage();
    }
    private void ReleaseNote(int pitch)
    {
        if (!_pressed.Remove(pitch)) return;
        _audio.NoteOff(pitch); SendOutput(pitch, 0, false); Stage.ReleaseLiveNote(pitch); UpdateStage();
    }

    private void PedalToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingPedals || sender is not ToggleButton toggle) return;
        var pedal = toggle.Name switch
        {
            "SoftPedalToggle" => PianoPedal.Soft,
            "SostenutoPedalToggle" => PianoPedal.Sostenuto,
            "SustainPedalToggle" => PianoPedal.Sustain,
            _ => (PianoPedal?)null
        };
        if (pedal is { } value) SetPedalState(value, toggle.IsChecked == true);
    }

    private void SetPedalState(PianoPedal pedal, bool down)
    {
        var changed = down ? _pedalsDown.Add(pedal) : _pedalsDown.Remove(pedal);
        if (!changed) return;
        Stage.SetSustainPedal(_pedalsDown.Contains(PianoPedal.Sustain));
        var controller = MidiDeviceService.ControllerFor(pedal);
        _audio.ControlChange(controller, down ? 127 : 0);
        _updatingPedals = true;
        try { PedalToggle(pedal).IsChecked = down; }
        finally { _updatingPedals = false; }
        SendController(controller, down ? 127 : 0);
    }

    private ToggleButton PedalToggle(PianoPedal pedal) => pedal switch
    {
        PianoPedal.Soft => SoftPedalToggle,
        PianoPedal.Sostenuto => SostenutoPedalToggle,
        _ => SustainPedalToggle
    };

    private void SendController(int controller, int value)
    {
        if (OutputDeviceCombo.SelectedIndex <= 0) return;
        try { _midi.SendController(controller, value); }
        catch
        {
            _midi.CloseOutputDevice(); _suppressDevices = true; OutputDeviceCombo.SelectedIndex = 0; _suppressDevices = false;
            Loc.Set(DeviceLabel, "MIDI output disconnected"); DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(255, 180, 91));
        }
    }
    private void SendOutput(int pitch, int velocity, bool on)
    {
        if (OutputDeviceCombo.SelectedIndex <= 0) return;
        try { _midi.SendNote(pitch, velocity, on); }
        catch
        {
            _midi.CloseOutputDevice(); _suppressDevices = true; OutputDeviceCombo.SelectedIndex = 0; _suppressDevices = false;
            Loc.Set(DeviceLabel, "MIDI output disconnected"); DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(255, 180, 91));
        }
    }

    // The three stat labels only change on a scored event, but this runs on every animation frame;
    // formatting the same strings 60 times a second would churn garbage and re-layout the footer.
    private string _accuracyText = "", _scoreText = "", _streakText = "";

    private void UpdateStats()
    {
        var accuracy = _hits + _misses == 0 ? 0 : 100.0 * _hits / (_hits + _misses);
        var accuracyText = _hits + _misses == 0 ? "—" : $"{accuracy:0}%";
        if (accuracyText != _accuracyText) { _accuracyText = accuracyText; AccuracyLabel.Text = accuracyText; ProgressBar.Value = accuracy; }
        var scoreText = Loc.F("{0} hits · {1} missed", _hits, _misses);
        if (scoreText != _scoreText) { _scoreText = scoreText; ScoreLabel.Text = scoreText; }
        var streakText = Loc.F("✦ {0} streak · best {1}", _streak, _bestStreak);
        if (streakText != _streakText) { _streakText = streakText; StreakLabel.Text = streakText; }
    }
    /// <summary>English key of the song title: the MIDI file name, or "Live Piano" before one is open.</summary>
    private string _songLabel = "Live Piano";
    /// <summary>Full path of the open MIDI file, or empty for the built-in demo song; the history keys its best take on it.</summary>
    private string _songPath = "";

    private string _timeText = "";

    private void UpdateTime()
    {
        if (TimeLabel is null) return;
        var duration = SongDuration();
        var text = $"{Fmt(_position)} / {Fmt(duration)}";
        if (text != _timeText) { _timeText = text; TimeLabel.Text = text; }
        if (!_updatingSeek && duration > 0)
        {
            var percent = Math.Clamp(100 * _position / duration, 0, 100);
            if (Math.Abs(SeekSlider.Value - percent) < .01) return;
            _updatingSeek = true; SeekSlider.Value = percent; _updatingSeek = false;
        }
    }
    private static string Fmt(double seconds) => $"{(int)seconds / 60:00}:{(int)seconds % 60:00}";
    private double SongDuration()
    {
        // Called several times per frame; recompute only when the note list itself is replaced.
        if (!ReferenceEquals(_songDurationSource, _notes)) { _songDurationSource = _notes; _songDuration = _notes.Count == 0 ? 0 : _notes.Max(n => n.End); }
        return _songDuration;
    }
    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;
    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _isSeeking = false; SeekToSlider(); }
    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_isSeeking && !_updatingSeek) SeekToSlider(); }
    private void SeekToSlider()
    {
        ReleasePlaybackNotes(); _position = SongDuration() * SeekSlider.Value / 100; _processCurrentOnsets = true; _outputFinished.Clear();
        foreach (var note in _notes) { note.Played = false; note.Missed = false; }
        ResetScore(); UpdateStage(); UpdateTime();
    }
    private void SetLoopA_Click(object sender, RoutedEventArgs e) { _loopA = _position; UpdateLoopLabel(); }
    private void SetLoopB_Click(object sender, RoutedEventArgs e)
    {
        _loopB = _position;
        if (_loopA < 0 || _loopB <= _loopA) { _loopA = -1; _loopB = -1; ShowMessage(Loc.T("Set A first, then set B at a later point in the song."), "Practice loop", MessageBoxImage.Information); }
        UpdateLoopLabel();
    }
    private void ClearLoop_Click(object sender, RoutedEventArgs e) { _loopA = _loopB = -1; UpdateLoopLabel(); }
    private void UpdateLoopLabel()
    {
        if (_loopA >= 0 && _loopB > _loopA) Loc.Format(LoopLabel, "{0}–{1}", Fmt(_loopA), Fmt(_loopB));
        else Loc.Set(LoopLabel, "No loop");
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();
    private void RefreshDevices()
    {
        var previousInput = (InputDeviceCombo.SelectedItem as DeviceOption)?.Id;
        var previousOutput = (OutputDeviceCombo.SelectedItem as DeviceOption)?.Id;
        var inputs = MidiDeviceService.Inputs;
        var outputs = MidiDeviceService.Outputs;
        // The pickers show a translated caption but keep the English identity, so a refresh never
        // loses the user's choice and a switch of language never changes what a device is called on
        // the wire (real device names come from Windows and are never translated).
        var inputItems = new List<DeviceOption> { DeviceOption.Placeholder("Computer keyboard only") };
        inputItems.AddRange(inputs.Select(name => new DeviceOption(name, name)));
        var outputItems = new List<DeviceOption> { DeviceOption.Placeholder("No MIDI output") };
        outputItems.AddRange(outputs.Select(name => new DeviceOption(name, name)));
        var inputIndex = previousInput is null ? -1 : inputItems.FindIndex(item => item.Id == previousInput);
        // Without a deliberate user choice (startup, or a refresh after plugging a keyboard in) connect
        // the first available input. An explicit selection - including "Computer keyboard only" -
        // survives refreshes; a selected device that vanished falls back to auto-connect.
        if (!_inputChoiceByUser || inputIndex < 0) inputIndex = inputItems.Count > 1 ? 1 : 0;
        var outputIndex = previousOutput is null ? 0 : Math.Max(0, outputItems.FindIndex(item => item.Id == previousOutput));
        _suppressDevices = true;
        try
        {
            InputDeviceCombo.ItemsSource = inputItems; InputDeviceCombo.SelectedIndex = inputIndex;
            OutputDeviceCombo.ItemsSource = outputItems; OutputDeviceCombo.SelectedIndex = outputIndex;
        }
        finally { _suppressDevices = false; }
        ConnectInput(showErrors: false);
        if (outputIndex > 0) ConnectOutput();
    }
    private void InputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDevices || InputDeviceCombo.SelectedIndex < 0) return;
        _inputChoiceByUser = true;
        ConnectInput();
    }
    private void ConnectInput(bool showErrors = true)
    {
        ReleaseAllPressed();
        try
        {
            _midi.OpenInput(InputDeviceCombo.SelectedIndex - 1); var connected = InputDeviceCombo.SelectedIndex > 0 && _midi.InputOpen;
            if (connected) Loc.Format(DeviceLabel, "Listening · {0}", DeviceOption.Name(InputDeviceCombo));
            else Loc.Set(DeviceLabel, "Computer keyboard ready");
            DeviceDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(75, 244, 187) : Color.FromRgb(255, 180, 91));
        }
        catch (Exception ex)
        {
            Loc.Set(DeviceLabel, "MIDI input unavailable · retry"); DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(255, 91, 113));
            if (showErrors) ShowError(ex.Message, "MIDI input");
        }
    }
    private void OutputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDevices || OutputDeviceCombo.SelectedIndex < 0) return;
        ConnectOutput();
    }
    private void ConnectOutput()
    {
        ReleasePlaybackNotes();
        try
        {
            _midi.OpenOutput(OutputDeviceCombo.SelectedIndex - 1);
            foreach (var pedal in _pedalsDown) _midi.SendController(MidiDeviceService.ControllerFor(pedal), 127);
        }
        catch (Exception ex)
        {
            // Windows lists devices it cannot always open (a synthesizer owned by another process, a
            // unplugged USB port). Fall back to "No MIDI output" so the picker never shows a dead choice.
            _suppressDevices = true;
            try { OutputDeviceCombo.SelectedIndex = 0; }
            finally { _suppressDevices = false; }
            ShowError(ex.Message, "MIDI output");
        }
    }
    /// <param name="preserve">True when only the language changed: keep the selected track and the mute set.</param>
    private void PopulateTracks(bool preserve = false)
    {
        var previous = TrackCombo.SelectedIndex;
        _suppressTracks = true; TrackCombo.Items.Clear(); TrackCombo.Items.Add(Loc.T("All notes"));
        foreach (var track in _allNotes.Select(n => n.Track).Distinct().Order())
            TrackCombo.Items.Add(_trackNames.TryGetValue(track, out var name) ? Loc.F("Track {0} · {1}", track + 1, name) : Loc.F("Track {0}", track + 1));
        if (preserve && previous >= 0 && previous < TrackCombo.Items.Count)
        {
            TrackCombo.SelectedIndex = previous; _suppressTracks = false;
            RebuildTrackList();
            return;
        }
        TrackCombo.SelectedIndex = 0; _activeTrack = -1; _suppressTracks = false;
        _mutedTracks.Clear(); RebuildTrackList();
    }
    private void TrackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressTracks || TrackCombo.SelectedIndex < 0) return;
        _activeTrack = TrackCombo.SelectedIndex == 0 ? -1 : TrackCombo.SelectedIndex - 1; ApplyTrackFilter(); UpdateSongUi(); UpdatePlaybackLabel(); ResetScore(); UpdateStage(); UpdateTime();
    }
    private void ApplyTrackFilter()
    {
        ReleasePlaybackNotes();
        IEnumerable<NoteEvent> notes = _activeTrack < 0 ? _allNotes : _allNotes.Where(n => n.Track == _activeTrack);
        if (_mutedTracks.Count > 0) notes = notes.Where(n => !_mutedTracks.Contains(n.Track));
        // The hand split is shared with the per-hand color mode (Notes page), so practice filters and colors always agree.
        var split = (int)Math.Round(_visualSettings.HandSplitPitch);
        if (ModeCombo.SelectedIndex == 2) notes = notes.Where(n => n.Pitch >= split);
        if (ModeCombo.SelectedIndex == 3) notes = notes.Where(n => n.Pitch < split);
        _notes = notes.OrderBy(n => n.Start).ToList();
        SyncPlayhead();
    }
    private void ResetScore()
    {
        _hits = _misses = _streak = _bestStreak = 0; ResetPracticeTempoRuns();
        foreach (var note in _allNotes) { note.Played = false; note.Missed = false; note.Timing = 0; }
        // Notes already behind the playhead are skipped, not counted as misses, so seeking or changing filters never zeroes the accuracy.
        SyncPlayhead();
        UpdateStats();
    }
    private void ReleasePlaybackNotes()
    {
        foreach (var note in _outputHeld.ToArray()) SendOutput(note.Pitch, 0, false);
        foreach (var note in _audioHeld.ToArray()) _audio.NoteOff(note.Pitch);
        _outputHeld.Clear(); _audioHeld.Clear();
    }
    private void ReleaseAllPressed() { foreach (var pitch in _pressed.ToArray()) ReleaseNote(pitch); }
    private void Window_Deactivated(object? sender, EventArgs e) { ReleaseAllPressed(); }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible) CloseSettingsPanel(); else OpenSettingsPanel();
    }
    private void ToggleFullScreen()
    {
        if (_fullScreen) { WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = WindowState.Normal; _fullScreen = false; }
        else { WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; _fullScreen = true; }
        SetChromeVisible(true); _lastPointerActivity = DateTime.UtcNow;
    }
    internal static List<NoteEvent> CreateDemoSong()
    {
        var result = new List<NoteEvent>(); int[] melody = [68,72,75,72,68,72,75,72,67,70,75,70,67,70,75,70,65,68,72,68,65,68,72,68,63,67,72,67,63,67,72,67];
        for (var i = 0; i < melody.Length; i++) result.Add(new NoteEvent { Pitch = melody[i], Start = i * .5 + .5, Duration = .42, Track = 1 });
        int[] bass = [44,51,56,51,44,51,56,51,43,50,55,50,41,48,53,48];
        for (var i = 0; i < bass.Length; i++) result.Add(new NoteEvent { Pitch = bass[i], Start = i * 1.0 + .5, Duration = .8, Track = 0 });
        return result.OrderBy(n => n.Start).ToList();
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closing = true;
        FrameClock.Shared.Tick -= OnFrame;
        StopStageFrames();
        _chromeTimer.Stop(); _settingsSaveTimer.Stop(); StopVideoRecording(false);
        try { SaveVisualSettings(); } catch { }
        Stop();
        foreach (var pedal in _pedalsDown.ToArray()) SetPedalState(pedal, false);
        _midi.Dispose(); _audio.Dispose();
    }
}
