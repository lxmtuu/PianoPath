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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _chromeTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _recordClock = new();
    private readonly MidiDeviceService _midi = new();
    private readonly PianoAudioEngine _audio = new();
    private readonly Dictionary<string, Slider> _visualSliders = [];
    private readonly Dictionary<string, TextBox> _visualColorInputs = [];
    private readonly Dictionary<string, Button> _visualColorButtons = [];
    private PianoVisualSettings _visualSettings = new();
    private AviVideoRecorder? _videoRecorder;
    private DispatcherTimer? _recordTimer;
    private string? _recordingPath;
    private List<NoteEvent> _allNotes = [];
    private List<NoteEvent> _notes = [];
    private readonly HashSet<int> _pressed = [];
    private readonly HashSet<NoteEvent> _outputHeld = [];
    private readonly HashSet<NoteEvent> _audioHeld = [];
    private readonly HashSet<NoteEvent> _outputFinished = [];
    private readonly HashSet<PianoPedal> _pedalsDown = [];
    private readonly Dictionary<Key, int> _keyDownPitches = [];
    private int _hits, _misses, _streak, _bestStreak, _activeTrack = -1;
    private double _position, _tempo = 1, _loopA = -1, _loopB = -1, _metronomeOffAt = -1;
    private bool _playing, _isSeeking, _updatingSeek, _updatingPedals, _suppressDevices, _suppressTracks, _suppressPreset, _processCurrentOnsets, _fullScreen = true, _uiReady, _isBuiltInSoundFont, _closing, _chromeVisible = true, _loadingVisualSettings;
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
    private static readonly int[] ComputerMap = [0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21];
    private static readonly Key[] ComputerKeys = [Key.A, Key.W, Key.S, Key.E, Key.D, Key.F, Key.T, Key.G, Key.Y, Key.H, Key.U, Key.J, Key.K];

    public MainWindow(bool loadBuiltInSoundFont = true)
    {
        InitializeComponent();
        _visualSettings = PianoVisualSettingsStore.Load();
        BuildVisualSettingsControls();
        Stage.SetVisualSettings(_visualSettings);
        _chromeTimer.Tick += (_, _) => CheckChromeIdle();
        _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer.Stop(); SaveVisualSettings(); };
        _uiReady = true;
        _notes = _allNotes;
        _timer.Tick += (_, _) => Tick();
        _midi.NoteChanged += (pitch, velocity, on) => Dispatcher.BeginInvoke(() =>
        {
            if (on)
            {
                DeviceLabel.Text = $"MIDI IN · {NoteLabel(pitch)}";
                DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(75, 244, 187));
                PressNote(pitch, velocity);
            }
            else ReleaseNote(pitch);
        });
        _midi.PedalChanged += (pedal, down) => Dispatcher.BeginInvoke(() => SetPedalState(pedal, down));
        PopulateTracks(); RefreshDevices(); UpdateSoundFontUi(); UpdateSongUi(); UpdateStage(); UpdateStats(); UpdateTime();
        SetChromeVisible(true); _chromeTimer.Start();
        if (loadBuiltInSoundFont) Loaded += MainWindow_Loaded;
    }
    internal bool HasSoundFont => _audio.HasSoundFont;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await LoadSoundFontAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "ConcertGrand.sf2"), builtIn: true);
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
        _clock.Restart(); _playing = true; _processCurrentOnsets = true; _timer.Start(); PlayButton.Content = "Ⅱ"; UpdatePlaybackLabel();
        UpdateStage();
    }
    private void Stop()
    {
        _timer.Stop(); _clock.Stop(); _playing = false; _processCurrentOnsets = false; _metronomeOffAt = -1;
        PlayButton.Content = "▶";
        foreach (var note in _outputHeld.ToArray()) SendOutput(note.Pitch, 0, false);
        _outputHeld.Clear(); _audioHeld.Clear(); _audio.AllNotesOff(); ReleaseAllPressed(); Stage.ClearTransient(); UpdateStage(); UpdatePlaybackLabel();
    }

    private void Tick()
    {
        var elapsed = _clock.Elapsed.TotalSeconds; _clock.Restart();
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
            if (_audioHeld.Count > 0) foreach (var note in _audioHeld.Where(n => n.End <= _position).ToArray()) { _audioHeld.Remove(note); _audio.NoteOff(note.Pitch); }
            if (_outputHeld.Count > 0) foreach (var note in _outputHeld.Where(n => n.End <= _position).ToArray()) { _outputHeld.Remove(note); SendOutput(note.Pitch, 0, false); }
            // Onsets in (onsetFrom, position]; after a jump the window also covers notes within 25 ms of the new playhead.
            var onsetFrom = forceOnset ? Math.Min(previous, _position - .025) : previous;
            for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, onsetFrom); i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Start > _position) break;
                if (note.Start <= onsetFrom || !_outputFinished.Add(note) || note.Played) continue;
                _audio.NoteOn(note.Pitch, note.Velocity); _audioHeld.Add(note);
                SendOutput(note.Pitch, note.Velocity, true); _outputHeld.Add(note);
                Stage.Impact(note.Pitch, .82);
            }
            // Notes are sorted by start, so the miss scan only ever advances instead of re-reading the whole song every frame.
            var missLimit = _position - .38;
            while (_missScanIndex < _notes.Count && _notes[_missScanIndex].Start < missLimit)
            {
                var note = _notes[_missScanIndex++];
                if (!note.Played && !note.Missed) { note.Missed = true; _misses++; _streak = 0; }
            }
            TickMetronome(previous, forceOnset);
        }
        Stage.Advance(elapsed);
        if (_playing && _position >= SongDuration()) { _position = SongDuration(); Stop(); }
        UpdateStage(); UpdateTime(); UpdateStats();
        if (!_playing && !Stage.HasActiveEffects) { _timer.Stop(); _clock.Stop(); }
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
        if (MetronomeCheck.IsChecked != true || !_audio.HasSoundFont || _beatTimes.Count == 0) return;
        var click = false; var downbeat = false;
        while (_nextBeat < _beatTimes.Count && _beatTimes[_nextBeat] <= _position)
        {
            if (_beatTimes[_nextBeat] > previous || forceOnset) { click = true; downbeat = _beatsPerBar > 0 && _nextBeat % _beatsPerBar == 0; }
            _nextBeat++;
        }
        if (click) { _audio.NoteOn(MetronomePitch, downbeat ? 84 : 62); _metronomeOffAt = _position + .08; }
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
        var dialog = new OpenFileDialog { Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Stop(); var song = MidiReader.ReadSong(dialog.FileName); if (song.Notes.Count == 0) throw new InvalidDataException("No notes were found in this MIDI file.");
            _allNotes = song.Notes; _beatTimes = song.BeatTimes; _beatsPerBar = song.BeatsPerBar; _trackNames = song.TrackNames;
            SongTitle.Text = Path.GetFileNameWithoutExtension(dialog.FileName); _position = 0; ResetScore(); _outputFinished.Clear(); PopulateTracks(); ApplyTrackFilter(); UpdateSongUi(); UpdatePlaybackLabel(); UpdateTime(); UpdateStage();
        }
        catch (Exception ex) { MessageBox.Show(this, $"Could not read this MIDI file.\n{ex.Message}", "MIDI import", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void LoadSoundFont_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SoundFont 2 files (*.sf2)|*.sf2|All files (*.*)|*.*", Title = "Choose a piano SoundFont" };
        if (dialog.ShowDialog(this) != true) return;
        await LoadSoundFontAsync(dialog.FileName, builtIn: false);
    }

    private async Task LoadSoundFontAsync(string path, bool builtIn)
    {
        if (_closing) return;
        SoundFontButton.IsEnabled = false; SoundFontLabel.Text = "LOADING SOUNDFONT…"; SoundFontHint.Text = "Large sample banks may take a few seconds";
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
            SoundFontLabel.Text = _audio.HasSoundFont ? "PREVIOUS SOUNDFONT STILL ACTIVE" : "NO SOUNDFONT · SILENT";
            SoundFontHint.Text = ex.Message; MessageBox.Show(this, $"Could not load this SoundFont.\n{ex.Message}", "SoundFont", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { if (!_closing) SoundFontButton.IsEnabled = true; }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPreset || PresetCombo.SelectedIndex < 0 || PresetCombo.SelectedIndex >= _audio.Presets.Count) return;
        var preset = _audio.Presets[PresetCombo.SelectedIndex]; _audio.SelectPreset(preset.Bank, preset.Program);
        SoundFontHint.Text = $"Instrument · {preset.Name}";
    }
    private void UnloadSoundFont_Click(object sender, RoutedEventArgs e) { _audio.UnloadSoundFont(); _isBuiltInSoundFont = false; _suppressPreset = true; PresetCombo.ItemsSource = null; PresetCombo.SelectedIndex = -1; _suppressPreset = false; UpdateSoundFontUi(); }
    private void UpdateSoundFontUi()
    {
        var loaded = _audio.HasSoundFont;
        SoundFontLabel.Text = loaded ? (_isBuiltInSoundFont ? "BUILT-IN YAMAHA GRAND · READY" : $"SOUNDFONT READY · {_audio.LoadedName}") : "NO SOUNDFONT · SILENT";
        SoundFontLabel.Foreground = loaded ? new SolidColorBrush(Color.FromRgb(112, 242, 213)) : new SolidColorBrush(Color.FromRgb(255, 180, 209));
        SoundFontHint.Text = loaded ? $"Yamaha grand · Hall reverb {(_audio.ReverbEnabled ? "ON" : "OFF")}" : "The built-in grand piano is loading";
        if (!loaded && PresetCombo.Items.Count == 0)
        {
            _suppressPreset = true;
            PresetCombo.ItemsSource = new[] { "NO PRESET · LOAD .SF2" };
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
        if (_playing) NowPlayingLabel.Text = _audio.HasSoundFont ? "PLAYING · NOTES FALLING" : "PLAYING · SILENT WITHOUT SOUNDFONT";
        else if (_notes.Count == 0) NowPlayingLabel.Text = _audio.HasSoundFont ? "LIVE PLAY · SOUNDFONT READY" : "LIVE PLAY · PRESS A KEY";
        else NowPlayingLabel.Text = _audio.HasSoundFont ? "MIDI LOADED · READY TO PLAY" : "MIDI LOADED · SILENT UNTIL SF2";
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            // Escape first clears an active settings search, otherwise it toggles the settings dock, even while the stage is in its idle full-screen state.
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
        if (_settingsHiddenByIdle) { _settingsHiddenByIdle = false; SettingsPanel.Visibility = Visibility.Visible; }
        SetChromeVisible(true, showRecordButton: true);
        if (Stage is not null) Stage.SetPointerPosition(e.GetPosition(Stage));
    }
    private void CheckChromeIdle()
    {
        if (_closing || !AutoHideChrome) return;
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
        SettingsPanel.Visibility = Visibility.Visible;
        SetChromeVisible(true, showRecordButton: false); _lastPointerActivity = DateTime.UtcNow;
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
        HeaderRow.Height = new GridLength(visible ? 74 : 0);
        FooterRow.Height = new GridLength(visible ? 94 : 0);
        HeaderChrome.Opacity = visible ? 1 : 0;
        FooterChrome.Opacity = visible ? 1 : 0;
        HeaderChrome.IsHitTestVisible = visible; FooterChrome.IsHitTestVisible = visible;
        LiveChromeOverlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
        RecordButton.Visibility = showRecord ? Visibility.Visible : Visibility.Collapsed;
        DeviceStatusBadge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecordVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_videoRecorder is not null) { StopVideoRecording(showMessage: true); return; }
        var dialog = new SaveFileDialog { Filter = "AVI video (*.avi)|*.avi", DefaultExt = ".avi", AddExtension = true, FileName = $"Keyflow-{DateTime.Now:yyyyMMdd-HHmmss}.avi", Title = "Record piano visualizer" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var (width, height) = RecordingSize();
            _videoRecorder = new AviVideoRecorder(dialog.FileName, width, height, (int)Math.Clamp(_visualSettings.RecordingFrameRate, 15, 60));
            _recordingPath = dialog.FileName;
            // Poll twice per frame; frames are paced by the recording clock inside RecordTimer_Tick, not by timer ticks.
            _recordClock.Restart(); _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / (_videoRecorder.FrameRate * 2)) };
            _recordTimer.Tick += RecordTimer_Tick; _recordTimer.Start();
            RecordButton.Content = "■  REC 00:00"; RecordButton.Background = new SolidColorBrush(Color.FromRgb(104, 23, 42));
            var rawSeconds = AviVideoRecorder.SizeLimitBytes / (double)(AviVideoRecorder.BgrStride(_videoRecorder.Width) * _videoRecorder.Height * _videoRecorder.FrameRate);
            RecordButton.ToolTip = _videoRecorder.UsesMjpeg ? "Recording MJPEG AVI · click to stop" : $"Recording raw AVI (no MJPEG codec installed) · about {rawSeconds:0} s fit in the 2 GB AVI limit · click to stop";
            SettingsSaveLabel.Text = _videoRecorder.UsesMjpeg ? "Video recording started" : $"Recording raw AVI · about {rawSeconds:0} s fit before the 2 GB limit";
        }
        catch (Exception ex)
        {
            StopVideoRecording(showMessage: false);
            MessageBox.Show(this, ex.Message, "Video recording", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
                _videoRecorder.WriteBgrFrame(CaptureStageBgr(_videoRecorder.Width, _videoRecorder.Height), Math.Min(due, _videoRecorder.FrameRate * 2));
                if (_videoRecorder.IsNearSizeLimit) { StopVideoRecording(showMessage: true, "The AVI file reached the 2 GB limit of the AVI format, so recording stopped automatically."); return; }
            }
            var elapsed = _recordClock.Elapsed;
            RecordButton.Content = $"■  REC {elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }
        catch (Exception ex)
        {
            StopVideoRecording(showMessage: false);
            MessageBox.Show(this, ex.Message, "Video recording stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private RenderTargetBitmap? _captureBitmap;
    private byte[]? _captureSource, _captureTarget;
    private byte[] CaptureStageBgr(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(Stage) { Stretch = Stretch.Uniform }, null, new Rect(0, 0, width, height));
        // Reuse the capture bitmap and buffers between frames; at 1280×720 fresh arrays would add roughly 130 MB/s of garbage while recording.
        if (_captureBitmap is null || _captureBitmap.PixelWidth != width || _captureBitmap.PixelHeight != height)
        {
            _captureBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            _captureSource = new byte[width * 4 * height]; _captureTarget = new byte[AviVideoRecorder.BgrStride(width) * height];
        }
        var bitmap = _captureBitmap; bitmap.Clear(); bitmap.Render(visual);
        var sourceStride = width * 4; var source = _captureSource!; bitmap.CopyPixels(source, sourceStride, 0);
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
        try { recorder.Dispose(); } catch (Exception ex) { if (showMessage && !_closing) MessageBox.Show(this, ex.Message, "Video recording", MessageBoxButton.OK, MessageBoxImage.Warning); }
        RecordButton.Content = "●  REC"; RecordButton.ClearValue(BackgroundProperty);
        RecordButton.ToolTip = "Record the live piano visualizer";
        if (showMessage && !_closing) MessageBox.Show(this, $"Video saved.\n{path}\n\n{(note is null ? "" : note + "\n\n")}This AVI contains the piano visuals; system audio is not mixed into the recording.", "Recording complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    internal static int MapComputerKey(Key key) { var index = Array.IndexOf(ComputerKeys, key); return index < 0 ? -1 : 48 + ComputerMap[index]; }
    private static string NoteLabel(int pitch) { string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]; return $"{names[pitch % 12]}{pitch / 12 - 1}"; }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (_keyDownPitches.Remove(e.Key, out var pitch)) ReleaseNote(pitch); }

    private void PressNote(int pitch, int velocity = 100)
    {
        if (!_pressed.Add(pitch)) return;
        if (!_timer.IsEnabled) { _clock.Restart(); _timer.Start(); }
        Stage.AddLiveNote(pitch); Stage.Impact(pitch, .75); _audio.NoteOn(pitch, velocity); SendOutput(pitch, velocity, true);
        if (_playing)
        {
            var target = _notes.Where(n => !n.Played && !n.Missed && n.Pitch == pitch && Math.Abs(n.Start - _position) <= .8).OrderBy(n => Math.Abs(n.Start - _position)).FirstOrDefault();
            if (target != null)
            {
                target.Played = true; var delta = Math.Abs(target.Start - _position);
                if (delta <= .55) { _hits++; _streak++; _bestStreak = Math.Max(_bestStreak, _streak); } else { _misses++; _streak = 0; }
                target.Timing = Math.Max(0, 100 - delta * 180); NoteNameLabel.Text = delta < .11 ? "PERFECT" : delta < .28 ? "GREAT" : "KEEP GOING";
                if (ModeCombo.SelectedIndex == 1) _clock.Restart();
            }
            else { _misses++; _streak = 0; NoteNameLabel.Text = NoteLabel(pitch); }
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
            DeviceLabel.Text = "MIDI output disconnected"; DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(255, 180, 91));
        }
    }
    private void SendOutput(int pitch, int velocity, bool on)
    {
        if (OutputDeviceCombo.SelectedIndex <= 0) return;
        try { _midi.SendNote(pitch, velocity, on); }
        catch
        {
            _midi.CloseOutputDevice(); _suppressDevices = true; OutputDeviceCombo.SelectedIndex = 0; _suppressDevices = false;
            DeviceLabel.Text = "MIDI output disconnected"; DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(255, 180, 91));
        }
    }

    private void UpdateStats()
    {
        var accuracy = _hits + _misses == 0 ? 0 : 100.0 * _hits / (_hits + _misses);
        AccuracyLabel.Text = _hits + _misses == 0 ? "—" : $"{accuracy:0}%"; ProgressBar.Value = accuracy;
        ScoreLabel.Text = $"{_hits} hits · {_misses} missed"; StreakLabel.Text = $"✦ {_streak} streak · best {_bestStreak}";
    }
    private void UpdateTime()
    {
        if (TimeLabel is null) return; TimeLabel.Text = $"{Fmt(_position)} / {Fmt(SongDuration())}";
        if (!_updatingSeek && SongDuration() > 0) { _updatingSeek = true; SeekSlider.Value = Math.Clamp(100 * _position / SongDuration(), 0, 100); _updatingSeek = false; }
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
        if (_loopA < 0 || _loopB <= _loopA) { _loopA = -1; _loopB = -1; MessageBox.Show(this, "Set A first, then set B at a later point in the song.", "Practice loop", MessageBoxButton.OK, MessageBoxImage.Information); }
        UpdateLoopLabel();
    }
    private void ClearLoop_Click(object sender, RoutedEventArgs e) { _loopA = _loopB = -1; UpdateLoopLabel(); }
    private void UpdateLoopLabel() => LoopLabel.Text = _loopA >= 0 && _loopB > _loopA ? $"{Fmt(_loopA)}–{Fmt(_loopB)}" : "No loop";

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();
    private void RefreshDevices()
    {
        var previousInput = InputDeviceCombo.SelectedItem as string;
        var previousOutput = OutputDeviceCombo.SelectedItem as string;
        var inputs = MidiDeviceService.Inputs;
        var outputs = MidiDeviceService.Outputs;
        var inputItems = new[] { "Computer keyboard only" }.Concat(inputs).ToList();
        var outputItems = new[] { "No MIDI output" }.Concat(outputs).ToList();
        var inputIndex = previousInput is null ? -1 : inputItems.IndexOf(previousInput);
        // Automatically connect the first available keyboard on startup or after a hot-plug refresh.
        if (inputIndex < 1) inputIndex = inputItems.Count > 1 ? 1 : 0;
        var outputIndex = previousOutput is null ? 0 : Math.Max(0, outputItems.IndexOf(previousOutput));
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
        ConnectInput();
    }
    private void ConnectInput(bool showErrors = true)
    {
        ReleaseAllPressed();
        try
        {
            _midi.OpenInput(InputDeviceCombo.SelectedIndex - 1); var connected = InputDeviceCombo.SelectedIndex > 0 && _midi.InputOpen;
            DeviceLabel.Text = connected ? $"Listening · {InputDeviceCombo.SelectedItem}" : "Computer keyboard ready";
            DeviceDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(75, 244, 187) : Color.FromRgb(255, 180, 91));
        }
        catch (Exception ex)
        {
            DeviceLabel.Text = "MIDI input unavailable · retry"; DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(255, 91, 113));
            if (showErrors) MessageBox.Show(this, ex.Message, "MIDI input", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "MIDI output", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void PopulateTracks()
    {
        _suppressTracks = true; TrackCombo.Items.Clear(); TrackCombo.Items.Add("All notes");
        foreach (var track in _allNotes.Select(n => n.Track).Distinct().Order()) TrackCombo.Items.Add(_trackNames.TryGetValue(track, out var name) ? $"Track {track + 1} · {name}" : $"Track {track + 1}");
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
        _hits = _misses = _streak = _bestStreak = 0;
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
        _chromeTimer.Stop(); _settingsSaveTimer.Stop(); StopVideoRecording(false);
        try { SaveVisualSettings(); } catch { }
        Stop();
        foreach (var pedal in _pedalsDown.ToArray()) SetPedalState(pedal, false);
        _midi.Dispose(); _audio.Dispose();
    }
}
