using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
    private readonly Dictionary<string, TextBlock> _visualValueLabels = [];
    private readonly Dictionary<string, TextBox> _visualColorInputs = [];
    private readonly Dictionary<string, Button> _visualColorButtons = [];
    private ComboBox? _paletteCombo;
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
    private int _lastBeat = -1;
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
        if (_position >= SongDuration()) { _position = 0; _outputFinished.Clear(); foreach (var n in _notes) { n.Played = false; n.Missed = false; } }
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
            }
            if (ModeCombo.SelectedIndex == 1)
            {
                var next = _notes.Where(n => !n.Played && !n.Missed).OrderBy(n => n.Start).FirstOrDefault();
                if (next != null && _position >= next.Start) { _position = next.Start; if (previous > _position) forceOnset = true; _clock.Restart(); }
            }
            foreach (var note in _notes)
            {
                var forced = forceOnset && Math.Abs(note.Start - _position) < .025;
                if (!_outputFinished.Contains(note) && note.Start <= _position && (note.Start > previous || forced))
                {
                    if (!note.Played)
                    {
                        _audio.NoteOn(note.Pitch, note.Velocity); _audioHeld.Add(note);
                        SendOutput(note.Pitch, note.Velocity, true); _outputHeld.Add(note);
                        Stage.Impact(note.Pitch, .82);
                    }
                    _outputFinished.Add(note);
                }
                if (note.End <= _position)
                {
                    if (_outputHeld.Remove(note)) SendOutput(note.Pitch, 0, false);
                    if (_audioHeld.Remove(note)) _audio.NoteOff(note.Pitch);
                    _outputFinished.Add(note);
                }
                if (!note.Played && !note.Missed && note.Start < _position - .38) { note.Missed = true; _misses++; _streak = 0; }
            }

            if (MetronomeCheck.IsChecked == true && _audio.HasSoundFont)
            {
                if (_metronomeOffAt >= 0 && _position >= _metronomeOffAt) { _audio.NoteOff(77); _metronomeOffAt = -1; }
                var beat = (int)Math.Floor(_position / .6);
                if (beat > _lastBeat) { _lastBeat = beat; _audio.NoteOn(77, 62); _metronomeOffAt = _position + .08; }
            }
        }
        Stage.Advance(elapsed);
        if (_playing && _position >= SongDuration()) { _position = SongDuration(); Stop(); }
        UpdateStage(); UpdateTime(); UpdateStats();
        if (!_playing && Stage.LiveTrailCount == 0 && Stage.SparkCount == 0) { _timer.Stop(); _clock.Stop(); }
    }

    private void UpdateStage() => Stage.SetState(_notes, _position, _playing, _pressed);
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
            Stop(); _allNotes = MidiReader.Read(dialog.FileName); if (_allNotes.Count == 0) throw new InvalidDataException("No notes were found in this MIDI file.");
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
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                SettingsPanel.Visibility = Visibility.Collapsed; SetChromeVisible(true, showRecordButton: false); _lastPointerActivity = DateTime.UtcNow;
            }
            else if (!_chromeVisible)
            {
                // Escape first restores the live controls; pressing it again opens settings.
                SetChromeVisible(true, showRecordButton: false); _lastPointerActivity = DateTime.UtcNow;
            }
            else
            {
                SettingsTabs.SelectedIndex = 0; SettingsPanel.Visibility = Visibility.Visible; SetChromeVisible(true, showRecordButton: false);
            }
            e.Handled = true; return;
        }
        var focused = Keyboard.FocusedElement;
        if (focused is ComboBox or TextBox or Slider or CheckBox or Button) return;
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
        _lastPointerActivity = DateTime.UtcNow;
        SetChromeVisible(true, showRecordButton: true);
        if (Stage is not null) Stage.SetPointerPosition(e.GetPosition(Stage));
    }
    private void CheckChromeIdle()
    {
        if (_closing || SettingsPanel.Visibility == Visibility.Visible) return;
        if (DateTime.UtcNow - _lastPointerActivity >= TimeSpan.FromSeconds(2.8)) SetChromeVisible(false);
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

    private void BuildVisualSettingsControls()
    {
        _loadingVisualSettings = true;
        AddSection(SceneSettingsHost, "LIVE SCENE", "Toggle each layer without interrupting MIDI input.");
        AddToggleGrid(SceneSettingsHost,
            ("Background", nameof(PianoVisualSettings.ShowBackground)), ("Gradient", nameof(PianoVisualSettings.BackgroundGradient)),
            ("Guide lanes", nameof(PianoVisualSettings.BackgroundGuide)), ("Stars", nameof(PianoVisualSettings.ShowStars)),
            ("Falling notes", nameof(PianoVisualSettings.ShowNotes)),
            ("Embers", nameof(PianoVisualSettings.ShowEmbers)), ("Halo", nameof(PianoVisualSettings.ShowHalo)),
            ("Flame impact", nameof(PianoVisualSettings.ShowFlame)), ("Piano keys", nameof(PianoVisualSettings.ShowKeys)),
            ("Key animation", nameof(PianoVisualSettings.AnimateKeys)), ("Key counter", nameof(PianoVisualSettings.ShowCounter)),
            ("Keyflow watermark", nameof(PianoVisualSettings.ShowWatermark)), ("3D note shading", nameof(PianoVisualSettings.Notes3D)));
        SceneSettingsHost.Children.Add(SettingsButton("CHOOSE BACKGROUND IMAGE", ChooseStageBackground));
        SceneSettingsHost.Children.Add(SettingsButton("CLEAR BACKGROUND IMAGE", (_, _) => { _visualSettings.BackgroundImagePath = ""; ApplyVisualSettings("Background image removed"); }));
        AddSingleColor(SceneSettingsHost, "Halo color", nameof(PianoVisualSettings.HaloColor));
        AddSection(SceneSettingsHost, "SCENE PERFORMANCE", "Layers and bloom are applied directly to the live renderer.");
        AddSlider(SceneSettingsHost, "Keyboard light", nameof(PianoVisualSettings.KeyLighting), 0, 100);
        AddSlider(SceneSettingsHost, "Black key overhang", nameof(PianoVisualSettings.KeyOverhang), 0, 100);

        AddSection(NoteSettingsHost, "NOTE STYLE", "Choose a color family or specify custom endpoint colors.");
        _paletteCombo = new ComboBox { ItemsSource = new[] { "Spectrum", "Aurora", "Fire", "Ocean", "Violet", "Custom" }, SelectedItem = _visualSettings.Palette, Height = 36, Margin = new Thickness(0, 3, 0, 12) };
        _paletteCombo.SelectionChanged += (_, _) => { if (_paletteCombo.SelectedItem is string value) { _visualSettings.Palette = value; ApplyVisualSettings("Note palette updated"); } };
        NoteSettingsHost.Children.Add(_paletteCombo);
        AddColorPair(NoteSettingsHost, "Gradient start", nameof(PianoVisualSettings.NoteColorStart), "Gradient end", nameof(PianoVisualSettings.NoteColorEnd));
        AddSlider(NoteSettingsHost, "Tint / opacity", nameof(PianoVisualSettings.NoteTint), 0, 100);
        AddSlider(NoteSettingsHost, "Bloom / glow", nameof(PianoVisualSettings.NoteGlow), 0, 200);
        AddSlider(NoteSettingsHost, "Edge brightness", nameof(PianoVisualSettings.NoteEdge), 0, 200);
        AddSlider(NoteSettingsHost, "Light refraction", nameof(PianoVisualSettings.NoteRefraction), 0, 100);
        AddSlider(NoteSettingsHost, "Corner roundness", nameof(PianoVisualSettings.NoteRoundness), 0, 100);
        AddSlider(NoteSettingsHost, "Edge width", nameof(PianoVisualSettings.NoteEdgeWidth), 0, 100);
        AddSlider(NoteSettingsHost, "Live fall speed", nameof(PianoVisualSettings.NoteFallSpeed), 100, 1000);
        NoteSettingsHost.Children.Add(new TextBlock { Text = "Only a physically held key extends its visual note. Pedals sustain the audio without stretching the bar after key release.", Foreground = new SolidColorBrush(Color.FromRgb(143, 132, 157)), FontSize = 9, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 13) });

        AddSection(ParticleSettingsHost, "EMBERS · EMITTER", "Particle response, spread, life and motion are adjustable in real time.");
        AddSlider(ParticleSettingsHost, "Emitter size", nameof(PianoVisualSettings.EmitterSize), 0, 100);
        AddSlider(ParticleSettingsHost, "Spiral", nameof(PianoVisualSettings.Spiral), 0, 100);
        AddSlider(ParticleSettingsHost, "Speed", nameof(PianoVisualSettings.ParticleSpeed), 0, 300);
        AddSlider(ParticleSettingsHost, "Amount", nameof(PianoVisualSettings.ParticleAmount), 0, 120);
        AddSlider(ParticleSettingsHost, "Velocity", nameof(PianoVisualSettings.ParticleVelocity), 0, 800);
        AddSlider(ParticleSettingsHost, "Velocity randomness", nameof(PianoVisualSettings.ParticleRandomness), 0, 100);
        AddSlider(ParticleSettingsHost, "Spread", nameof(PianoVisualSettings.ParticleSpread), 0, 100);
        AddSlider(ParticleSettingsHost, "Note response", nameof(PianoVisualSettings.ParticleResponse), 0, 100);
        AddSection(ParticleSettingsHost, "PARTICLE · MOTION", "Simulated gravity, drag and a scrolling vector field.");
        AddSlider(ParticleSettingsHost, "Particle lifetime", nameof(PianoVisualSettings.ParticleLife), .05, 3);
        AddSlider(ParticleSettingsHost, "Lifetime randomness", nameof(PianoVisualSettings.ParticleLifeRandomness), 0, 100);
        AddSlider(ParticleSettingsHost, "Particle size", nameof(PianoVisualSettings.ParticleSize), .2, 16);
        AddSlider(ParticleSettingsHost, "Size randomness", nameof(PianoVisualSettings.ParticleSizeRandomness), 0, 100);
        AddSlider(ParticleSettingsHost, "Particle glow", nameof(PianoVisualSettings.ParticleGlow), 0, 200);
        AddSlider(ParticleSettingsHost, "Gravity", nameof(PianoVisualSettings.Gravity), -600, 1200);
        AddSlider(ParticleSettingsHost, "Drag", nameof(PianoVisualSettings.Drag), 0, 100);
        AddSlider(ParticleSettingsHost, "Vector field", nameof(PianoVisualSettings.VectorField), 0, 1000);
        AddSlider(ParticleSettingsHost, "Field scale", nameof(PianoVisualSettings.FieldScale), 10, 300);
        AddSlider(ParticleSettingsHost, "Evolution speed", nameof(PianoVisualSettings.EvolutionSpeed), 0, 400);
        AddSlider(ParticleSettingsHost, "Physics time factor", nameof(PianoVisualSettings.PhysicsTimeFactor), 10, 300);

        AddSection(CameraSettingsHost, "CAMERA & BACKGROUND", "Subtle parallax and framing keep the keyboard anchored.");
        AddSlider(CameraSettingsHost, "Parallax", nameof(PianoVisualSettings.CameraParallax), 0, 100);
        AddSlider(CameraSettingsHost, "Zoom", nameof(PianoVisualSettings.CameraZoom), 65, 150);
        AddSlider(CameraSettingsHost, "Horizontal framing", nameof(PianoVisualSettings.CameraOffset), 0, 100);
        AddSlider(CameraSettingsHost, "Background dim", nameof(PianoVisualSettings.BackgroundDim), 0, 100);
        AddSection(CameraSettingsHost, "IMAGE & POST FX", "Tone and bloom are evaluated by the live stage renderer.");
        AddSlider(CameraSettingsHost, "Saturation", nameof(PianoVisualSettings.Saturation), 0, 200);
        AddSlider(CameraSettingsHost, "Contrast", nameof(PianoVisualSettings.Contrast), 0, 200);
        AddSlider(CameraSettingsHost, "Bloom intensity", nameof(PianoVisualSettings.BloomIntensity), 0, 150);
        AddSlider(CameraSettingsHost, "Bloom size", nameof(PianoVisualSettings.BloomSize), 0, 150);
        CameraSettingsHost.Children.Add(SettingsButton("CHOOSE BACKGROUND IMAGE", ChooseStageBackground));
        CameraSettingsHost.Children.Add(SettingsButton("CLEAR BACKGROUND IMAGE", (_, _) => { _visualSettings.BackgroundImagePath = ""; ApplyVisualSettings("Background image removed"); }));
        _loadingVisualSettings = false;
    }

    private static void AddSection(Panel host, string title, string subtitle)
    {
        host.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.FromRgb(224, 184, 244)), FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 3) });
        host.Children.Add(new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(137, 128, 151)), FontSize = 8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
    }

    private void AddToggleGrid(Panel host, params (string label, string property)[] toggles)
    {
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 7) };
        foreach (var (label, property) in toggles)
        {
            var check = new CheckBox { Content = label, Tag = property, IsChecked = (bool)typeof(PianoVisualSettings).GetProperty(property)!.GetValue(_visualSettings)!, Margin = new Thickness(0, 5, 8, 5), FontSize = 9 };
            check.Checked += VisualToggle_Changed; check.Unchecked += VisualToggle_Changed; grid.Children.Add(check);
        }
        host.Children.Add(grid);
    }

    private void AddSlider(Panel host, string label, string property, double minimum, double maximum)
    {
        var prop = typeof(PianoVisualSettings).GetProperty(property)!;
        var row = new StackPanel { Margin = new Thickness(0, 2, 0, 7) };
        var header = new DockPanel(); header.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(222, 216, 231)), FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = FormatSetting(property, (double)prop.GetValue(_visualSettings)!), Foreground = new SolidColorBrush(Color.FromRgb(234, 180, 255)), FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(value, Dock.Right); header.Children.Add(value); row.Children.Add(header);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = Math.Clamp((double)prop.GetValue(_visualSettings)!, minimum, maximum), Tag = property, Margin = new Thickness(0, 1, 0, 0), ToolTip = label };
        slider.ValueChanged += VisualSlider_ValueChanged; _visualSliders[property] = slider; _visualValueLabels[property] = value; row.Children.Add(slider); host.Children.Add(row);
    }

    private static string FormatSetting(string property, double value) => property switch
    {
        nameof(PianoVisualSettings.ParticleLife) => $"{value:0.00} s",
        nameof(PianoVisualSettings.ParticleAmount) => $"{value:0}",
        nameof(PianoVisualSettings.NoteFallSpeed) or nameof(PianoVisualSettings.ParticleVelocity) or nameof(PianoVisualSettings.Gravity) or nameof(PianoVisualSettings.VectorField) => $"{value:0}",
        nameof(PianoVisualSettings.ParticleSize) => $"{value:0.0}",
        _ => $"{value:0}%"
    };

    private void AddColorPair(Panel host, string firstLabel, string firstProperty, string secondLabel, string secondProperty)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 11) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddColorBox(grid, 0, firstLabel, firstProperty); AddColorBox(grid, 1, secondLabel, secondProperty); host.Children.Add(grid);
    }

    private void AddSingleColor(Panel host, string label, string property)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 11) }; grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddColorBox(grid, 0, label, property); host.Children.Add(grid);
    }

    private void AddColorBox(Grid grid, int column, string label, string property)
    {
        var stack = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 0 ? 8 : 0, 0) };
        if (!string.IsNullOrWhiteSpace(label)) stack.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(152, 143, 165)), FontSize = 8, Margin = new Thickness(0, 0, 0, 4) });
        var currentValue = (string)typeof(PianoVisualSettings).GetProperty(property)!.GetValue(_visualSettings)!;
        var row = new DockPanel { LastChildFill = true };
        var swatch = new Button { Tag = property, Width = 30, Height = 30, Padding = new Thickness(2), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Open color picker" };
        swatch.Click += VisualColorButton_Click; SetColorSwatch(swatch, currentValue); DockPanel.SetDock(swatch, Dock.Left); row.Children.Add(swatch);
        var box = new TextBox { Text = currentValue, Tag = property, Background = new SolidColorBrush(Color.FromRgb(25, 20, 33)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(84, 64, 101)), Padding = new Thickness(8, 6, 8, 6), FontSize = 10, VerticalContentAlignment = VerticalAlignment.Center };
        box.LostFocus += VisualColor_LostFocus; row.Children.Add(box); stack.Children.Add(row);
        _visualColorInputs[property] = box; _visualColorButtons[property] = swatch;
        Grid.SetColumn(stack, column); grid.Children.Add(stack);
    }

    private static void SetColorSwatch(Button button, string value)
    {
        try { button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!); }
        catch { button.Background = new SolidColorBrush(Color.FromRgb(198, 110, 255)); }
    }

    private void VisualColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string property }) return;
        var input = _visualColorInputs[property];
        var picker = new ColorPickerWindow(input.Text) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } selected) return;
        input.Text = selected;
        typeof(PianoVisualSettings).GetProperty(property)!.SetValue(_visualSettings, selected);
        SetColorSwatch(_visualColorButtons[property], selected);
        if (property is nameof(PianoVisualSettings.NoteColorStart) or nameof(PianoVisualSettings.NoteColorEnd))
        {
            _visualSettings.Palette = "Custom";
            if (_paletteCombo is not null) _paletteCombo.SelectedItem = "Custom";
        }
        ApplyVisualSettings(property == nameof(PianoVisualSettings.HaloColor) ? "Halo color applied" : "Custom note color applied");
    }

    private static Button SettingsButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 7, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += click;
        return button;
    }

    private void VisualToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingVisualSettings || sender is not CheckBox check || check.Tag is not string property) return;
        typeof(PianoVisualSettings).GetProperty(property)!.SetValue(_visualSettings, check.IsChecked == true);
        ApplyVisualSettings($"{check.Content} {(check.IsChecked == true ? "on" : "off")}");
    }

    private void VisualSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings || sender is not Slider slider || slider.Tag is not string property) return;
        typeof(PianoVisualSettings).GetProperty(property)!.SetValue(_visualSettings, slider.Value);
        if (_visualValueLabels.TryGetValue(property, out var label)) label.Text = FormatSetting(property, slider.Value);
        ApplyVisualSettings("Visual changes apply live");
    }

    private void VisualColor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string property) return;
        try
        {
            _ = ColorConverter.ConvertFromString(box.Text) ?? throw new FormatException();
            typeof(PianoVisualSettings).GetProperty(property)!.SetValue(_visualSettings, box.Text);
            if (_visualColorButtons.TryGetValue(property, out var swatch)) SetColorSwatch(swatch, box.Text);
            if (property is nameof(PianoVisualSettings.NoteColorStart) or nameof(PianoVisualSettings.NoteColorEnd))
            {
                _visualSettings.Palette = "Custom";
                if (_paletteCombo is not null) _paletteCombo.SelectedItem = "Custom";
            }
            ApplyVisualSettings("Custom note colors applied");
        }
        catch { box.Text = (string)typeof(PianoVisualSettings).GetProperty(property)!.GetValue(_visualSettings)!; }
    }

    private void ApplyVisualSettings(string status, bool reloadBackground = false)
    {
        _visualSettings.Clamp(); Stage.SetVisualSettings(_visualSettings, reloadBackground);
        if (reloadBackground && Stage.BackgroundLoadError is { } error)
        {
            SettingsSaveLabel.Text = "Background image failed to load";
            MessageBox.Show(this, $"Keyflow could not load this image. Choose a PNG, JPEG, BMP, GIF or TIFF file.\n\n{error}", "Background image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else SettingsSaveLabel.Text = status;
        _settingsSaveTimer.Stop(); _settingsSaveTimer.Start();
    }

    private void SaveVisualSettings_Click(object sender, RoutedEventArgs e) => SaveVisualSettings();
    private void SaveVisualSettings()
    {
        try { PianoVisualSettingsStore.Save(_visualSettings); SettingsSaveLabel.Text = "Saved to this computer"; }
        catch (Exception ex) { SettingsSaveLabel.Text = "Save failed"; if (!_closing) MessageBox.Show(this, ex.Message, "Stage settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ChooseStageBackground(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*", Title = "Choose a piano visualizer background", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) { _visualSettings.BackgroundImagePath = dialog.FileName; ApplyVisualSettings("Background image applied", reloadBackground: true); }
    }

    private void RecordVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_videoRecorder is not null) { StopVideoRecording(showMessage: true); return; }
        var dialog = new SaveFileDialog { Filter = "AVI video (*.avi)|*.avi", DefaultExt = ".avi", AddExtension = true, FileName = $"Keyflow-{DateTime.Now:yyyyMMdd-HHmmss}.avi", Title = "Record piano visualizer" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var ratio = Stage.ActualHeight / Math.Max(1, Stage.ActualWidth);
            var width = Math.Max(640, Math.Min(1280, (int)Stage.ActualWidth)) & ~1;
            var height = Math.Max(360, (int)(width * ratio)) & ~1;
            _videoRecorder = new AviVideoRecorder(dialog.FileName, width, height, 20);
            _recordingPath = dialog.FileName;
            _recordClock.Restart(); _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _recordTimer.Tick += RecordTimer_Tick; _recordTimer.Start();
            RecordButton.Content = "■  REC 00:00"; RecordButton.Background = new SolidColorBrush(Color.FromRgb(104, 23, 42));
            RecordButton.ToolTip = _videoRecorder.UsesMjpeg ? "Recording MJPEG AVI · click to stop" : "Recording raw AVI · large file · click to stop";
            SettingsSaveLabel.Text = "Video recording started";
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
            _videoRecorder.WriteBgrFrame(CaptureStageBgr(_videoRecorder.Width, _videoRecorder.Height));
            var elapsed = _recordClock.Elapsed;
            RecordButton.Content = $"■  REC {elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }
        catch (Exception ex)
        {
            StopVideoRecording(showMessage: false);
            MessageBox.Show(this, ex.Message, "Video recording stopped", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private byte[] CaptureStageBgr(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(Stage) { Stretch = Stretch.Uniform }, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var sourceStride = width * 4; var source = new byte[sourceStride * height]; bitmap.CopyPixels(source, sourceStride, 0);
        var targetStride = AviVideoRecorder.BgrStride(width); var target = new byte[targetStride * height];
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

    private void StopVideoRecording(bool showMessage)
    {
        _recordTimer?.Stop(); _recordTimer = null; _recordClock.Stop();
        var recorder = _videoRecorder; _videoRecorder = null;
        if (recorder is null) return;
        var path = _recordingPath; _recordingPath = null;
        try { recorder.Dispose(); } catch (Exception ex) { if (showMessage && !_closing) MessageBox.Show(this, ex.Message, "Video recording", MessageBoxButton.OK, MessageBoxImage.Warning); }
        RecordButton.Content = "●  REC"; RecordButton.Background = new SolidColorBrush(Color.FromRgb(39, 19, 24));
        RecordButton.ToolTip = "Record the live piano visualizer";
        if (showMessage && !_closing) MessageBox.Show(this, $"Video saved.\n{path}\n\nThis AVI contains the piano visuals; system audio is not mixed into the recording.", "Recording complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
    private double SongDuration() => _notes.Count == 0 ? 0 : _notes.Max(n => n.End);
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
        foreach (var track in _allNotes.Select(n => n.Track).Distinct().Order()) TrackCombo.Items.Add($"Track {track + 1}");
        TrackCombo.SelectedIndex = 0; _activeTrack = -1; _suppressTracks = false;
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
        if (ModeCombo.SelectedIndex == 2) notes = notes.Where(n => n.Pitch >= 60);
        if (ModeCombo.SelectedIndex == 3) notes = notes.Where(n => n.Pitch < 60);
        _notes = notes.OrderBy(n => n.Start).ToList();
    }
    private void ResetScore()
    {
        _hits = _misses = _streak = _bestStreak = 0;
        foreach (var note in _allNotes) { note.Played = false; note.Missed = false; note.Timing = 0; }
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
        var opening = SettingsPanel.Visibility != Visibility.Visible;
        SettingsPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        if (opening) SettingsTabs.SelectedIndex = Math.Max(0, SettingsTabs.SelectedIndex);
        SetChromeVisible(true); _lastPointerActivity = DateTime.UtcNow;
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

internal sealed class NoteEvent
{
    public int Pitch { get; init; }
    public double Start { get; init; }
    public double Duration { get; init; }
    public double End => Start + Duration;
    public int Velocity { get; init; } = 90;
    public int Track { get; init; }
    public bool Played { get; set; }
    public bool Missed { get; set; }
    public double Timing { get; set; }
}

internal static class MidiReader
{
    public static List<NoteEvent> Read(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "MThd") throw new InvalidDataException("Missing MIDI header.");
        var headerLength = Read32(reader); var format = Read16(reader); var tracks = Read16(reader); var division = Read16(reader);
        if (headerLength < 6 || division == 0 || (division & 0x8000) != 0) throw new InvalidDataException("Unsupported MIDI time division.");
        if (headerLength > 6) reader.ReadBytes(headerLength - 6);
        var raw = new List<(long start, long end, int pitch, int velocity, int track)>(); var tempos = new SortedDictionary<long, int> { [0] = 500000 };
        for (var track = 0; track < tracks && stream.Position < stream.Length; track++)
        {
            if (new string(reader.ReadChars(4)) != "MTrk") throw new InvalidDataException("Invalid MIDI track chunk.");
            var chunkLength = Read32(reader); var end = stream.Position + chunkLength; long tick = 0; var running = 0; var active = new Dictionary<(int channel, int pitch), Queue<(long tick, int velocity)>>();
            while (stream.Position < end)
            {
                tick += ReadVar(reader); var status = reader.ReadByte();
                if (status < 0x80) { stream.Position--; if (running == 0) throw new InvalidDataException("Invalid MIDI running status."); status = (byte)running; }
                else if (status < 0xF0) running = status;
                if (status == 0xFF)
                {
                    var type = reader.ReadByte(); var length = ReadVar(reader);
                    if (type == 0x51 && length == 3) { var b = reader.ReadBytes(3); tempos[tick] = (b[0] << 16) | (b[1] << 8) | b[2]; }
                    else stream.Position += length;
                    continue;
                }
                if (status is 0xF0 or 0xF7) { stream.Position += ReadVar(reader); continue; }
                var kind = status & 0xF0; var channel = status & 15; var pitch = reader.ReadByte(); var velocity = kind is 0xC0 or 0xD0 ? 0 : reader.ReadByte();
                if (kind == 0x90 && velocity > 0) { var key = (channel, pitch); if (!active.ContainsKey(key)) active[key] = new Queue<(long, int)>(); active[key].Enqueue((tick, velocity)); }
                else if (kind == 0x80 || kind == 0x90 && velocity == 0)
                {
                    var key = (channel, pitch); if (active.TryGetValue(key, out var queue) && queue.Count > 0) { var on = queue.Dequeue(); raw.Add((on.tick, Math.Max(on.tick + 1, tick), pitch, on.velocity, track)); }
                }
            }
            stream.Position = end;
        }
        if (format == 2) throw new InvalidDataException("MIDI format 2 sequences are not supported yet.");
        var points = tempos.ToArray();
        double Seconds(long target) { long prev = 0; double seconds = 0; var micros = 500000; foreach (var t in points) { if (t.Key >= target) break; seconds += (t.Key - prev) * (double)micros / division / 1_000_000; prev = t.Key; micros = t.Value; } return seconds + (target - prev) * (double)micros / division / 1_000_000; }
        return raw.Select(n => new NoteEvent { Pitch = n.pitch, Start = Seconds(n.start), Duration = Math.Max(.06, Seconds(n.end) - Seconds(n.start)), Velocity = n.velocity, Track = n.track }).OrderBy(n => n.Start).ToList();
    }
    private static long ReadVar(BinaryReader reader) { long value = 0; byte current; do { current = reader.ReadByte(); value = (value << 7) | (uint)(current & 127); } while ((current & 128) != 0); return value; }
    private static int Read16(BinaryReader reader) => (reader.ReadByte() << 8) | reader.ReadByte();
    private static int Read32(BinaryReader reader) => (reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
}
