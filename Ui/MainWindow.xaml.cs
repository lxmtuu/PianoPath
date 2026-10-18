using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
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
    private readonly ConcurrentQueue<PendingMidiUiEvent> _pendingMidiUi = new();
    private int _midiUiDispatchPending;
    private int _audioOutputCatalogVersion, _audioOutputCatalogRefreshPending;
    private IReadOnlyList<AudioOutputEndpoint> _audioOutputEndpoints = [];
    private Task _storedReverbTask = Task.CompletedTask;
    private int _reverbUiRequestVersion;
    private readonly record struct PendingMidiUiEvent(int Pitch, int Velocity, bool On, bool GpuAlreadyForwarded);
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
    private bool _playing, _isSeeking, _updatingSeek, _updatingPedals, _suppressDevices, _suppressTracks, _processCurrentOnsets, _fullScreen = true, _uiReady, _closing, _chromeVisible = true, _loadingVisualSettings;
    /// <summary>True once the user deliberately picked a MIDI input; until then refreshes auto-connect the first keyboard.</summary>
    private bool _inputChoiceByUser;
    private DateTime _lastPointerActivity = DateTime.UtcNow;
    private bool _chromeHasRendered;
    private Point? _lastPointerPoint;
    /// <summary>Notes before this index are already played, missed or intentionally skipped by a seek; only later notes can still be missed.</summary>
    private int _missScanIndex;
    private IReadOnlyList<double> _beatTimes = [];
    private IReadOnlyList<int> _downbeatIndices = [];
    private IReadOnlyDictionary<int, string> _trackNames = new Dictionary<int, string>();
    private int _beatsPerBar = 4, _nextBeat;
    private IReadOnlyList<NoteEvent>? _songDurationSource;
    private double _songDuration;
    private const int MetronomePitch = 77;
    private static readonly TimeSpan ChromeIdleDelay = TimeSpan.FromSeconds(2.8);
    /// <summary>Set to false for automated snapshots so the toolbar and settings never disappear while a capture is pending.</summary>
    internal bool AutoHideChrome { get; set; } = true;

    /// <summary>False for automated captures and verification runs, which must never touch the network.</summary>
    internal bool AllowUpdateCheck { get; set; } = true;
    /// <summary>Set by the verification suite so a problem lands in the log instead of a modal dialog nobody can dismiss.</summary>
    internal bool SuppressErrorDialogs { get; set; }
    private static readonly int[] ComputerMap = [0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21];
    private static readonly Key[] ComputerKeys = [Key.A, Key.W, Key.S, Key.E, Key.D, Key.F, Key.T, Key.G, Key.Y, Key.H, Key.U, Key.J, Key.K];

    public MainWindow(bool loadSalamanderOnStart = true)
    {
        InitializeComponent();
        ShellThemeManager.Changed += OnShellThemeChanged;
        Closed += (_, _) => ShellThemeManager.Changed -= OnShellThemeChanged;
        _visualSettings = PianoVisualSettingsStore.Load();
        // The output request must be on the engine before the bundled piano loads, so the very first
        // pipeline already opens the backend the player chose last time instead of opening waveOut and
        // immediately rebuilding.
        _audio.ConfigureOutput(AudioOutputChoice.Parse(_visualSettings.AudioOutputMode), _visualSettings.AudioLatencyMs);
        _audio.MasterGain = _visualSettings.AudioMasterGain;
        _audio.ReverbAmount = _visualSettings.AudioReverbAmount / 100.0;
        _audio.PlaybackEnabled = _visualSettings.PianoSoundEnabled;
        SyncPianoSoundControls();
        _audio.OutputChanged += AudioEngine_OutputChanged;
        SourceInitialized += (_, _) => { _audio.SetAsioSystemHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle); HookCameraChanges(); };
        // Same for the reverb room, which also shapes the very first pipeline.
        _storedReverbTask = ApplyStoredReverbAsync();
        RefreshReverbStyleCombo();
        Loc.OnChanged(RefreshReverbStyleCombo);
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
        _midi.NoteChanged += (pitch, velocity, on) =>
        {
            // Audio note events are queued without taking the synth's render lock. The audio thread consumes
            // them at the next buffer, so a heavy sample block cannot stall WinMM's MIDI callback.
            if (on) _audio.NoteOn(pitch, velocity); else _audio.NoteOff(pitch);
            if (on) _latency.NoteOn(System.Diagnostics.Stopwatch.GetTimestamp());       // timed here, on the MIDI thread, as close to the key as it gets
            // The GPU gets the note immediately; only its WPF overlay work is batched below.
            var direct = ForwardMidiToGpu(pitch, velocity, on);
            QueueMidiUiEvent(new PendingMidiUiEvent(pitch, velocity, on, direct));
        };
        // The raw controller value reaches the synthesiser so a continuous pedal can half-pedal; it is sent
        // from the MIDI thread for the same latency reason as the notes above.
        _midi.PedalLevelChanged += (pedal, value) => _audio.ControlChange(MidiDeviceService.ControllerFor(pedal), value);
        _midi.PedalChanged += (pedal, down) => Dispatcher.BeginInvoke(() =>
        {
            _liveAudioSent = true;
            try { SetPedalState(pedal, down); }
            finally { _liveAudioSent = false; }
        });
        PopulateTracks(); RefreshDevices(); UpdateInstrumentUi(); RefreshPracticeHistory(); UpdateSongUi(); UpdateStage(); UpdateStats(); UpdateTime();
        // A folder indexed in an earlier session is watched from the start, so the library is live whether or
        // not the Play dialog has been opened yet.
        RefreshLibrarySongs(); StartSongFolderWatch(SongFolderIndex.Folder);
        ApplyChromeTheme();
        StartChromeSweeps();
        // Construction, bindings and the first layout are not user idle time. In a slow startup
        // they can exceed the hide delay before the person has seen a single frame.
        ContentRendered += (_, _) => { _lastPointerActivity = DateTime.UtcNow; _chromeHasRendered = true; };
        SetChromeVisible(true); _chromeTimer.Start();
        // The GPU stage is the main stage: the render loop starts with the window and the software
        // renderer only steps in if Direct3D cannot start (or for a --software run).
        ApplyRenderBackend();
        // The overlay needs the engine, which only exists from here on, so a saved "AR on" starts now.
        SyncCameraOverlay();
        UpdateInstrumentUi();
        if (loadSalamanderOnStart) Loaded += MainWindow_Loaded;
        // Off unless the person turned it on in General: a fresh install makes no request at all.
        Loaded += (_, _) => { if (AllowUpdateCheck && _visualSettings.CheckForUpdates) _ = RunUpdateCheckAsync(); };
    }
    internal bool HasInstrument => _audio.HasInstrument;

    private void AudioEngine_OutputChanged()
    {
        if (_closing || Dispatcher.HasShutdownStarted) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_closing) return;
                // A device/effect rebuild creates a fresh synthesiser. Restore pedal positions once it is live.
                if (_audio.HasInstrument)
                    foreach (var pedal in _pedalsDown) _audio.ControlChange(MidiDeviceService.ControllerFor(pedal), 127);
                UpdateInstrumentUi();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void QueueMidiUiEvent(PendingMidiUiEvent midiEvent)
    {
        if (_closing) return;
        _pendingMidiUi.Enqueue(midiEvent);
        if (Interlocked.CompareExchange(ref _midiUiDispatchPending, 1, 0) == 0) ScheduleMidiUiDrain();
    }

    private void ScheduleMidiUiDrain()
    {
        try { Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(DrainMidiUiEvents)); }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _midiUiDispatchPending, 0); }
    }

    private void DrainMidiUiEvents()
    {
        var handled = 0;
        while (handled++ < 256 && _pendingMidiUi.TryDequeue(out var midiEvent))
        {
            Stage.SuppressGpuForward = midiEvent.GpuAlreadyForwarded;
            _liveAudioSent = true;
            try
            {
                if (midiEvent.On)
                {
                    Loc.Format(DeviceLabel, "MIDI IN · {0}", NoteLabel(midiEvent.Pitch));
                    DeviceDot.Fill = new SolidColorBrush(Color.FromRgb(75, 244, 187));
                    PressNote(midiEvent.Pitch, midiEvent.Velocity);
                }
                else ReleaseNote(midiEvent.Pitch);
            }
            finally { Stage.SuppressGpuForward = false; _liveAudioSent = false; }
        }

        if (!_pendingMidiUi.IsEmpty) { ScheduleMidiUiDrain(); return; }
        Interlocked.Exchange(ref _midiUiDispatchPending, 0);
        if (!_pendingMidiUi.IsEmpty && Interlocked.CompareExchange(ref _midiUiDispatchPending, 1, 0) == 0)
            ScheduleMidiUiDrain();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await _storedReverbTask; // a custom impulse may be large; never read or decode it on the WPF thread
        if (_closing) return;
        // Salamander is the sole piano. It is downloaded only by explicit request; a fresh install stays silent
        // until the user starts the one-time, resumable 748 MB setup from Audio settings.
        if (SalamanderPiano.IsInstalled) await LoadSalamanderAsync();
        else UpdateInstrumentUi();
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
        if (PlayDialogPrimaryButton is not null) RefreshPlayDialogState();
        UpdateStage();
    }
    private void Stop()
    {
        RecordPracticeRunIfScored(); // a take that graded something is written before the transport resets
        StopStageFrames(); _playing = false; _processCurrentOnsets = false; _metronomeOffAt = -1;
        PlayButton.Tag = FindResource("IconPlay");
        foreach (var note in _outputHeld.ToArray()) SendOutput(note.Pitch, 0, false);
        _outputHeld.Clear(); _audioHeld.Clear(); _audio.AllNotesOff(); ReleaseAllPressed(); Stage.ClearTransient(); UpdateStage(); UpdatePlaybackLabel();
        if (PlayDialogPrimaryButton is not null) RefreshPlayDialogState();
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

    /// <summary>
    /// Puts the window into the state the README previews are captured from: the GPU render thread stopped
    /// and every animator back at frame zero.
    ///
    /// The previews have to come out byte for byte the same from one build to the next, or the drift check
    /// cannot tell "the interface changed" from "the machine was a little slower". Measured by comparing two
    /// CI renders of the same code, the chrome was already identical and the whole difference sat in the GPU
    /// frame, apart from the main menu, whose backdrop kept moving. So the render thread, which free-runs on
    /// its own clock, is parked before anything is pressed, and the frame is built synchronously afterwards by
    /// <see cref="RunDeterministicPreviewFrames"/>.
    /// </summary>
    internal void BeginDeterministicPreview()
    {
        // The thread would otherwise take the preview note's events off the feed before the synchronous frame
        // saw them, and publish frames of its own over it. A loop that is still starting cannot be parked, so
        // wait for its device first (it answers on failure too, so this never waits out the full ten seconds).
        var timer = Stopwatch.StartNew();
        _gpuLoop?.WaitUntilStarted(TimeSpan.FromSeconds(10));
        var waited = timer.ElapsedMilliseconds;
        var parked = _gpuLoop is { Error: null, IsReady: true } loop && loop.Park(TimeSpan.FromSeconds(3));
        var parkedAt = timer.ElapsedMilliseconds;
        Stage.ResetAnimation();
        DockBackdrop.ResetAnimation();
        MenuBackdrop.ResetAnimation();
        PreviewTimings = $"begin[wait:{waited} park:{parkedAt - waited}({(parked ? "ok" : "none")}) reset:{timer.ElapsedMilliseconds - parkedAt}]";
    }

    /// <summary>
    /// Where the time of the last deterministic preview went, in milliseconds, for the capture report. A
    /// preview that costs twenty seconds more than it should is found by looking, not by guessing.
    /// </summary>
    internal string PreviewTimings { get; private set; } = "";

    /// <summary>
    /// Advances the stage <paramref name="frames"/> steps of <paramref name="stepSeconds"/>, builds the GPU frame
    /// for that same moment, and leaves the window ready to be captured.
    ///
    /// Everything happens in this one call and nothing yields to the dispatcher until the capture, so no
    /// composition frame can add a step of the machine's own length: the picture is a function of the two
    /// arguments. Waiting for the compositor instead does not work on a CI runner. Measured over one build,
    /// <c>CompositionTarget.Rendering</c> delivered 8 to 19 frames in the 17 seconds a screenshot took, and the
    /// render thread ran 156 to 200 over the same time, each picture landing on a different count.
    /// </summary>
    internal void RunDeterministicPreviewFrames(int frames, double stepSeconds)
    {
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++) Stage.Advance(stepSeconds);
        var advanced = timer.ElapsedMilliseconds;
        var gpu = GpuStageActive ? ShowDeterministicGpuFrame(frames, stepSeconds) : "";
        var drawn = timer.ElapsedMilliseconds;
        FrameClock.Shared.Freeze();
        Stage.InvalidateVisual();
        FlushRenderPass();
        PreviewTimings += $" run[advance:{advanced} gpu:{drawn - advanced}{gpu} flush:{timer.ElapsedMilliseconds - drawn}]";
    }

    /// <summary>
    /// The GPU stage after <paramref name="frames"/> fixed steps from a fresh simulation, rendered on the
    /// calling thread (<see cref="GpuRenderLoop.RenderOnce"/>, the same path <c>--gpu-snapshot</c> and the
    /// verification suite use) and shown in place of whatever the parked render thread last produced.
    /// </summary>
    private string ShowDeterministicGpuFrame(int frames, double stepSeconds)
    {
        var (width, height) = Stage.GpuPixelSize;
        if (width < 16 || height < 16) return "{too small}";
        try
        {
            // The parked loop's own device has been drawing since the window opened, so the frame costs it a
            // fraction of a second; a brand-new software device spends about seventeen seconds on its first frame.
            // Only a software (WARP) device is reused: the previews are WARP renders on every machine, so a real
            // graphics card must not make them differ. Anything else, or any failure, falls back to a fresh device.
            var pixels = _gpuLoop is { IsWarp: true } loop ? loop.RenderParked(width, height, frames - 1, stepSeconds, TimeSpan.FromSeconds(60)) : null;
            var reused = pixels is not null;
            pixels ??= GpuRenderLoop.RenderOnce(_gpuFeed, width, height, frames - 1, stepSeconds, out _);
            Stage.ShowGpuFrame(pixels, width, height);
            return "{" + (reused ? "parked " : "fresh ") + GpuRenderLoop.LastRenderTimings + "}";
        }
        catch (Exception ex)
        {
            // The screenshot is still taken, with whatever frame the stage already shows.
            Trace.WriteLine($"Keyflow: the deterministic GPU frame could not be built: {ex.Message}");
            return "{failed}";
        }
    }

    /// <summary>
    /// Lets WPF run the layout and render passes the invalidations above queued. A live visual reuses its
    /// cached drawing until one has run, and neither <c>RenderTargetBitmap.Render</c> nor a same-tick
    /// <c>InvalidateVisual</c> re-runs <c>OnRender</c>, so a capture taken without this shows the old stage.
    /// </summary>
    private void FlushRenderPass()
    {
        var frame = new DispatcherFrame();
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
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
                // Notes at or past B keep the flags they earned: clearing them too would let a later pass
                // (once the loop is switched off) judge them a second time and count them twice.
                for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _loopA); i < _notes.Count; i++)
                {
                    if (_notes[i].Start >= _loopB) break;
                    _notes[i].Played = false; _notes[i].Missed = false; _notes[i].Timing = 0;
                }
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
                if (!note.Played && !note.Missed) { note.Missed = true; _misses++; _streak = 0; RecordPracticeNote(false, note.Pitch, note.Start); }
            }
            TickMetronome(previous, forceOnset);
        }
        Stage.Advance(elapsed);
        if (_playing && _position >= SongDuration()) { _position = SongDuration(); Stop(); }
        UpdateStage(); UpdateTime(); UpdateStats();
        if (!_playing && !Stage.HasActiveEffects) StopStageFrames();
    }

    private void UpdateStage() => Stage.SetState(_notes, _position, _playing, _pressed);

    /// <summary>Hands the sheet layer the grid to draw bar lines on; called whenever a song is loaded.</summary>
    private void UpdateSheet() => Stage.SetSheet(_beatTimes, _beatsPerBar, _downbeatIndices);
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
            if (_beatTimes[_nextBeat] > previous || forceOnset) { click = true; downbeat = IsDownbeat(_nextBeat); }
            _nextBeat++;
        }
        if (click) Stage.PulseBeat(downbeat ? 1 : .6); // Tempo Sync follows the MIDI tempo map even when the click is muted.
        if (click && MetronomeCheck.IsChecked == true && _audio.HasInstrument) { _audio.NoteOn(MetronomePitch, downbeat ? 84 : 62); _metronomeOffAt = _position + .08; }
    }

    private bool IsDownbeat(int beatIndex)
    {
        if (_downbeatIndices.Count == 0) return _beatsPerBar > 0 && beatIndex % _beatsPerBar == 0;
        int low = 0, high = _downbeatIndices.Count - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            if (_downbeatIndices[middle] == beatIndex) return true;
            if (_downbeatIndices[middle] < beatIndex) low = middle + 1; else high = middle - 1;
        }
        return false;
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
        var dialog = new OpenFileDialog { Filter = Loc.T("Songs (*.mid;*.midi;*.musicxml;*.xml;*.mxl)|*.mid;*.midi;*.musicxml;*.xml;*.mxl|MIDI files (*.mid;*.midi)|*.mid;*.midi|MusicXML scores (*.musicxml;*.mxl)|*.musicxml;*.mxl|All files (*.*)|*.*") };
        if (dialog.ShowDialog(this) != true) return;
        OpenSongFile(dialog.FileName);
    }

    /// <summary>True when the extension names a MusicXML score rather than a MIDI file.</summary>
    internal static bool IsMusicXml(string path) =>
        path.EndsWith(".musicxml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads a song by extension: a MusicXML score (.musicxml, .xml, .mxl) or a Standard MIDI file. Both end
    /// on the stage the same way; the score brings the staff of every note with it, which is the hand split
    /// the file states rather than one measured from the pitches.
    /// </summary>
    internal void OpenSongFile(string path)
    {
        try
        {
            if (!IsMusicXml(path)) { OpenMidiFile(path); return; }
            var score = MusicXmlReader.ReadScore(path);
            LoadSong(path, score.ToSong(), score);
        }
        catch (Exception ex) { ShowMessage(Loc.F("Could not read this MusicXML file.\n{0}", ex.Message), "MusicXML import"); }
    }

    /// <summary>Loads a Standard MIDI file — from the open dialog, the Play dialog or a drop on the window.</summary>
    internal void OpenMidiFile(string path)
    {
        try
        {
            var song = MidiReader.ReadSong(path); if (song.Notes.Count == 0) throw new InvalidDataException(Loc.T("No notes were found in this MIDI file."));
            LoadSong(path, song, null);
        }
        catch (Exception ex) { ShowMessage(Loc.F("Could not read this MIDI file.\n{0}", ex.Message), "MIDI import"); }
    }

    /// <summary>
    /// Puts a parsed song on the stage: the notes, the metronome grid, the track list and the labels, then
    /// the hand split and the recent list. The recent list is written only after the file really parsed, so
    /// the Play dialog never offers a song that failed to open.
    /// </summary>
    private void LoadSong(string path, MidiSong song, MusicXmlScore? score)
    {
        Stop();
        _allNotes = song.Notes; _beatTimes = song.BeatTimes; _downbeatIndices = song.DownbeatIndices; _beatsPerBar = song.BeatsPerBar; _trackNames = song.TrackNames;
        _songLabel = Path.GetFileNameWithoutExtension(path); _songPath = path;
        Loc.Bind(SongTitle, () => _songLabel); // a file name is the user's text, not a key
        _position = 0; ResetScore(); _outputFinished.Clear(); PopulateTracks(); ApplyTrackFilter(); UpdateSongUi(); UpdatePlaybackLabel(); UpdateTime(); UpdateSheet(); UpdateStage();
        ApplySongHandSplit(path, song, score);
        RememberSong(path, song);
    }

    private CancellationTokenSource? _salamanderCancel;
    private bool _instrumentLoading;
    private string? _instrumentLoadError;

    /// <summary>Loads the one supported instrument off the UI thread; FLAC decoding can take a moment.</summary>
    private async Task LoadSalamanderAsync()
    {
        if (_closing || _instrumentLoading || _audio.HasInstrument || !SalamanderPiano.IsInstalled) return;
        _instrumentLoading = true;
        _instrumentLoadError = null;
        UpdateInstrumentUi();
        try
        {
            await Task.Run(() => _audio.LoadSfzInstrument(SalamanderPiano.SfzPath));
            if (_closing) return;
            foreach (var pedal in _pedalsDown) _audio.ControlChange(MidiDeviceService.ControllerFor(pedal), 127);
        }
        catch (Exception ex)
        {
            if (_closing) return;
            _instrumentLoadError = ex.Message;
        }
        finally
        {
            _instrumentLoading = false;
            if (!_closing) UpdateInstrumentUi();
        }
    }

    /// <summary>One status line and one primary action keep install, download, and playback state together.</summary>
    private void UpdateInstrumentUi()
    {
        if (InstrumentStatusLabel is null) return;
        var loaded = _audio.HasInstrument;
        var audible = loaded && _audio.HasAudioOutput;
        var downloading = _salamanderCancel is not null;
        var installed = SalamanderPiano.IsInstalled;

        SalamanderProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        SalamanderButton.IsEnabled = ! _instrumentLoading;
        SalamanderButton.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
        SalamanderPlayButton.Visibility = installed && !loaded && !downloading && !_instrumentLoading ? Visibility.Visible : Visibility.Collapsed;
        Loc.Set(SalamanderButton, downloading ? "Cancel download" : installed ? "Verify / repair piano files" : "Download piano · 748 MB");
        // The glyph follows the state too: a stop square while cancelling, a refresh ring while verifying.
        SalamanderButton.Tag = FindResource(downloading ? "IconStop" : installed ? "IconRefresh" : "IconDownload");
        SalamanderPlayButton.IsEnabled = !_instrumentLoading && !downloading;

        if (downloading) Loc.Set(InstrumentStatusLabel, "SALAMANDER GRAND · DOWNLOADING");
        else if (_instrumentLoading) Loc.Set(InstrumentStatusLabel, "SALAMANDER GRAND · LOADING SAMPLES");
        else if (loaded) Loc.Set(InstrumentStatusLabel, audible ? "SALAMANDER GRAND · READY" : "SALAMANDER GRAND · NO AUDIO DEVICE");
        else if (installed && _instrumentLoadError is not null) Loc.Set(InstrumentStatusLabel, "SALAMANDER GRAND · CHECK FILES");
        else if (installed) Loc.Set(InstrumentStatusLabel, "SALAMANDER GRAND · INSTALLED");
        else Loc.Set(InstrumentStatusLabel, "SALAMANDER GRAND · SETUP REQUIRED");

        if (downloading)
        {
            // The progress callback owns the changing byte count; keep this label steady between callbacks.
        }
        else if (_instrumentLoading) Loc.Set(InstrumentHint, "Loading the verified samples; the window remains responsive.");
        else if (loaded && _audio.PlaybackError is { } playbackError) Loc.Format(InstrumentHint, "Instrument loaded, but no audio output is available · {0}", playbackError);
        else if (loaded) Loc.Set(InstrumentHint, "Yamaha C5 · 16 velocity layers · release and pedal samples · CC BY 3.0");
        else if (_instrumentLoadError is { } loadError) Loc.Format(InstrumentHint, "Could not load the piano samples · {0}", loadError);
        else if (installed) Loc.Set(InstrumentHint, "The piano is installed. Load it now, or verify/repair the sample files.");
        else Loc.Set(InstrumentHint, "One-time download · about 748 MB. Each sample is verified; interrupted setup can resume.");

        InstrumentStatusLabel.Foreground = audible
            ? new SolidColorBrush(Color.FromRgb(112, 242, 213))
            : new SolidColorBrush(Color.FromRgb(255, 180, 209));
        if (MetronomeCheck is not null) { MetronomeCheck.IsEnabled = loaded; if (!loaded) MetronomeCheck.IsChecked = false; }
        UpdateAudioOutputHint();
        UpdatePlaybackLabel();
    }

    private async void SalamanderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_salamanderCancel is { } running) { running.Cancel(); return; }
        if (_audio.HasInstrument || _instrumentLoading) return;

        using var cancel = _salamanderCancel = new CancellationTokenSource();
        SalamanderProgress.Value = 0;
        UpdateInstrumentUi();
        try
        {
            var total = (SalamanderPiano.TotalBytes / 1_000_000).ToString();
            var progress = new Progress<long>(done =>
            {
                SalamanderProgress.Value = done * 1000.0 / SalamanderPiano.TotalBytes;
                Loc.Format(InstrumentHint, "Downloaded {0} of {1} MB · samples are verified before use", (done / 1_000_000).ToString(), total);
            });
            await SalamanderPiano.DownloadAsync(progress, cancel.Token);
            _salamanderCancel = null;
            UpdateInstrumentUi();
            if (!_closing) await LoadSalamanderAsync();
        }
        catch (OperationCanceledException)
        {
            _salamanderCancel = null;
            UpdateInstrumentUi();
            Loc.Set(InstrumentHint, "Download cancelled · completed files are kept so setup can resume.");
        }
        catch (Exception ex)
        {
            _salamanderCancel = null;
            UpdateInstrumentUi();
            Loc.Format(InstrumentHint, "Download failed · {0}", ex.Message);
        }
    }

    private async void SalamanderPlay_Click(object sender, RoutedEventArgs e) => await LoadSalamanderAsync();

    // =============================================================================================
    // Reverb style (classic hall, built-in convolution rooms, or a user impulse response)
    // =============================================================================================

    private static readonly string[] ReverbStyleIds = ["hall", "concert", "soundboard", "custom"];
    private bool _suppressReverbStyle;

    /// <summary>Hands the stored reverb choice to the engine; custom impulse decoding runs away from the UI.</summary>
    private Task ApplyStoredReverbAsync()
    {
        if (_visualSettings.ReverbStyle == "custom") return ApplyStoredCustomReverbAsync(_visualSettings.ReverbImpulseFile);
        _audio.SetReverb(_visualSettings.ReverbStyle switch
        {
            "concert" => PianoReverbStyle.ConcertHall,
            "soundboard" => PianoReverbStyle.Soundboard,
            _ => PianoReverbStyle.Hall,
        });
        return Task.CompletedTask;
    }

    private async Task ApplyStoredCustomReverbAsync(string? path)
    {
        var requestVersion = Interlocked.Increment(ref _reverbUiRequestVersion);
        try
        {
            if (await _audio.SetReverbAsync(PianoReverbStyle.CustomImpulse, path)) return;
        }
        catch
        {
            if (_closing || requestVersion != Volatile.Read(ref _reverbUiRequestVersion) || Dispatcher.HasShutdownStarted) return;
            try
            {
                await Dispatcher.InvokeAsync((Action)(() =>
                {
                    if (requestVersion != Volatile.Read(ref _reverbUiRequestVersion)) return;
                    _visualSettings.ReverbStyle = "hall";
                    _visualSettings.ReverbImpulseFile = "";
                    _audio.SetReverb(PianoReverbStyle.Hall);
                    RefreshReverbStyleCombo();
                }));
            }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>(Re)fills the style list — called at startup and on every language switch.</summary>
    private void RefreshReverbStyleCombo()
    {
        _suppressReverbStyle = true;
        try
        {
            ReverbStyleCombo.ItemsSource = new[]
            {
                Loc.T("HALL · CLASSIC"), Loc.T("CONCERT HALL · CONVOLUTION"),
                Loc.T("SOUNDBOARD · CLOSE"), Loc.T("CUSTOM IMPULSE RESPONSE"),
            };
            var index = Array.IndexOf(ReverbStyleIds, _visualSettings.ReverbStyle);
            ReverbStyleCombo.SelectedIndex = index >= 0 ? index : 0;
        }
        finally { _suppressReverbStyle = false; }
    }

    private void ReverbStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressReverbStyle || ReverbStyleCombo.SelectedIndex < 0) return;
        var id = ReverbStyleIds[ReverbStyleCombo.SelectedIndex];
        if (id == "custom") { ChooseReverbImpulse(); return; } // picking the entry asks for the file
        Interlocked.Increment(ref _reverbUiRequestVersion);
        _audio.SetReverb(id switch { "concert" => PianoReverbStyle.ConcertHall, "soundboard" => PianoReverbStyle.Soundboard, _ => PianoReverbStyle.Hall });
        _visualSettings.ReverbStyle = id;
        AfterReverbChange();
    }

    private void ReverbImpulse_Click(object sender, RoutedEventArgs e) => ChooseReverbImpulse();

    private async void ChooseReverbImpulse()
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("Impulse responses (*.wav, *.flac)|*.wav;*.flac|All files (*.*)|*.*"), Title = Loc.T("Choose an impulse response") };
        if (dialog.ShowDialog(this) != true) { RefreshReverbStyleCombo(); return; } // put the combo back
        var requestVersion = Interlocked.Increment(ref _reverbUiRequestVersion);
        ReverbStyleCombo.IsEnabled = false;
        ReverbImpulseButton.IsEnabled = false;
        try
        {
            if (!await _audio.SetReverbAsync(PianoReverbStyle.CustomImpulse, dialog.FileName)) return;
            if (_closing || requestVersion != Volatile.Read(ref _reverbUiRequestVersion)) return;
            _visualSettings.ReverbStyle = "custom";
            _visualSettings.ReverbImpulseFile = dialog.FileName;
            AfterReverbChange();
        }
        catch (Exception ex)
        {
            if (_closing || requestVersion != Volatile.Read(ref _reverbUiRequestVersion)) return;
            RefreshReverbStyleCombo();
            ShowError(Loc.F("Could not load this impulse response.\n{0}", ex.Message), "Audio");
        }
        finally
        {
            if (!_closing)
            {
                ReverbStyleCombo.IsEnabled = true;
                ReverbImpulseButton.IsEnabled = true;
            }
        }
    }

    /// <summary>A reverb switch rebuilds the pipeline, so the held pedals must be re-pressed, like an output switch.</summary>
    private void AfterReverbChange()
    {
        foreach (var pedal in _pedalsDown) _audio.ControlChange(MidiDeviceService.ControllerFor(pedal), 127);
        RefreshReverbStyleCombo();
        _settingsSaveTimer.Stop(); _settingsSaveTimer.Start();
    }
    /// <summary>Shows a warning dialog, or only records it when dialogs are suppressed (verification runs, snapshots).</summary>
    private void ShowError(string message, string titleKey, MessageBoxImage icon = MessageBoxImage.Warning) =>
        ShowMessage(message, titleKey, icon);

    /// <summary>
    /// The line under the output pickers: the live path and its buffer ("WASAPI shared · 10.7 ms"), the
    /// explicit request's failure when a fallback answered instead, or why the machine is silent. The ASIO
    /// control panel button only lights up while an ASIO driver is actually the audible path.
    /// </summary>
    private void UpdateAudioOutputHint()
    {
        if (AsioPanelButton is null || AudioOutputHint is null) return;
        AsioPanelButton.IsEnabled = _audio.IsAsioOutput;
        if (!_audio.HasInstrument) { Loc.Set(AudioOutputHint, "Download Salamander Grand to enable piano audio."); return; }
        if (_audio.OutputDescription is { } description)
        {
            if (_audio.OutputFallbackNotice is { } notice) Loc.Format(AudioOutputHint, "Output · {0} · {1}", description, notice);
            else Loc.Format(AudioOutputHint, "Output · {0}", description);
        }
        else Loc.Format(AudioOutputHint, "No audio output · {0}", _audio.PlaybackError ?? "");
    }

    private void ReverbToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        _audio.ReverbEnabled = toggle.IsChecked == true;
        if (_uiReady) UpdateInstrumentUi();
    }

    private void ResonanceToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        _audio.StringResonanceEnabled = toggle.IsChecked == true;
    }

    private void UpdateSongUi()
    {
        var hasNotes = _notes.Count > 0;
        PlayButton.IsEnabled = hasNotes;
        RestartButton.IsEnabled = hasNotes;
        SeekSlider.IsEnabled = hasNotes;
        if (!hasNotes) { _position = 0; _outputFinished.Clear(); }
        SyncPlayhead();
        if (PlayDialogPrimaryButton is not null) RefreshPlayDialogState();
    }

    private void UpdatePlaybackLabel()
    {
        if (_playing) Loc.Set(NowPlayingLabel, _audio.HasInstrument ? "PLAYING · NOTES FALLING" : "PLAYING · AUDIO SETUP NEEDED");
        else if (_notes.Count == 0) Loc.Set(NowPlayingLabel, _audio.HasInstrument ? "LIVE PLAY · PIANO READY" : "LIVE PLAY · SET UP PIANO IN AUDIO SETTINGS");
        else Loc.Set(NowPlayingLabel, _audio.HasInstrument ? "MIDI LOADED · READY TO PLAY" : "MIDI LOADED · SET UP PIANO AUDIO");
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
            // Escape unwinds the visible surface first: help → quick adjustments → hand presets → Play → dock → menu → stage.
            if (ShortcutsVisible) { HideShortcuts(); e.Handled = true; return; }
            if (QuickAdjustOverlay.Visibility == Visibility.Visible) { CloseQuickAdjust(); e.Handled = true; return; }
            if (PlayDialogOverlay.Visibility == Visibility.Visible)
            {
                if (HandPresetPanel.Visibility == Visibility.Visible) HideHandPresetPanel(restoreFocus: true);
                else if (LibrarySearchBox.IsKeyboardFocused && LibrarySearchBox.Text.Length > 0) LibrarySearchBox.Text = "";
                else PlayDialogBack_Click(this, new RoutedEventArgs());
                e.Handled = true; return;
            }
            if (SettingsSearchBox.IsKeyboardFocused && SettingsSearchBox.Text.Length > 0) { SettingsSearchBox.Text = ""; e.Handled = true; return; }
            if (SettingsPanel.Visibility == Visibility.Visible) ReturnFromSettings();
            else if (MainMenuOverlay.Visibility == Visibility.Visible) { HideStartupMenu(); SetChromeVisible(true); Stage.Focus(); }
            else OpenSettingsPanel();
            e.Handled = true; return;
        }
        // Tab is the fast path back to session setup from the live stage. Modal surfaces and the
        // settings dock keep their normal focus-navigation behaviour (including Shift+Tab).
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None
            && PlayDialogOverlay.Visibility != Visibility.Visible
            && QuickAdjustOverlay.Visibility != Visibility.Visible
            && MainMenuOverlay.Visibility != Visibility.Visible
            && SettingsPanel.Visibility != Visibility.Visible
            && !ShortcutsVisible)
        {
            OpenPlayDialog();
            e.Handled = true;
            return;
        }
        // Modal surfaces own keyboard focus: never let a piano-mapped key or Space start playback behind them.
        if (PlayDialogOverlay.Visibility == Visibility.Visible || QuickAdjustOverlay.Visibility == Visibility.Visible
            || MainMenuOverlay.Visibility == Visibility.Visible || ShortcutsVisible) return;
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
        // WPF can re-raise MouseMove when chrome hit-test targets change; only actual pointer movement counts.
        var point = e.GetPosition(this);
        if (_lastPointerPoint is { } last && Math.Abs(last.X - point.X) < .5 && Math.Abs(last.Y - point.Y) < .5) return;
        _lastPointerPoint = point;
        _lastPointerActivity = DateTime.UtcNow;
        if (MainMenuOverlay?.Visibility == Visibility.Visible || PlayDialogOverlay?.Visibility == Visibility.Visible
            || QuickAdjustOverlay?.Visibility == Visibility.Visible || ShortcutsVisible) return;
        SetChromeVisible(true, showRecordButton: true);
        if (Stage is not null) Stage.SetPointerPosition(e.GetPosition(Stage));
    }
    private void CheckChromeIdle()
    {
        if (_closing || !AutoHideChrome || !_chromeHasRendered) return;
        // Modal surfaces and the advanced settings dock are explicit destinations, not hover chrome: leave them open until the user closes them.
        if (MainMenuOverlay?.Visibility == Visibility.Visible || PlayDialogOverlay?.Visibility == Visibility.Visible
            || QuickAdjustOverlay?.Visibility == Visibility.Visible || SettingsPanel?.Visibility == Visibility.Visible || ShortcutsVisible) return;
        // Keep everything on screen while a color picker is open or the user is dragging a slider / browsing a drop-down.
        if (OwnedWindows.Count > 0 || Mouse.Captured is not null) return;
        if (DateTime.UtcNow - _lastPointerActivity >= ChromeIdleDelay) HideChromeForIdle();
    }
    /// <summary>Idle state fades the hover chrome over the full-window stage, so the keyboard stays anchored at the bottom.</summary>
    private void HideChromeForIdle()
    {
        if (SettingsPanel?.Visibility == Visibility.Visible) return;
        SetChromeVisible(false);
    }
    private void OpenSettingsPanel() => OpenSettingsPanelFor(NavigationSurface.Stage);

    private void OpenSettingsPanelFor(NavigationSurface returnSurface)
    {
        _settingsReturnSurface = returnSurface;
        UpdateSettingsReturnButton();
        SettingsTabs.SelectedIndex = Math.Max(0, SettingsTabs.SelectedIndex);
        var wasHidden = SettingsPanel.Visibility != Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Visible;
        SetChromeVisible(true, showRecordButton: false); _lastPointerActivity = DateTime.UtcNow;
        // The dock slides in from the stage edge; the clip of the stage grid keeps it inside the frame.
        if (wasHidden) ChromeMotion.SlideIn(SettingsPanel, 56, 0, 320);
    }

    private void UpdateSettingsReturnButton()
    {
        if (SettingsReturnButton is null) return;
        var hasDestination = _settingsReturnSurface != NavigationSurface.Stage;
        SettingsReturnButton.Visibility = hasDestination ? Visibility.Visible : Visibility.Collapsed;
        if (!hasDestination) return;
        var destination = _settingsReturnSurface == NavigationSurface.PlayDialog
            ? "Return to the Play dialog"
            : "Back to the main menu";
        Loc.Set(SettingsReturnButton, destination, FrameworkElement.ToolTipProperty);
        Loc.Set(SettingsReturnButton, destination, AutomationProperties.NameProperty);
    }

    private void ReturnFromSettings()
    {
        var destination = _settingsReturnSurface;
        _settingsReturnSurface = NavigationSurface.Stage;
        UpdateSettingsReturnButton();
        CloseSettingsPanel();
        switch (destination)
        {
            case NavigationSurface.MainMenu:
                ShowStartupMenu();
                break;
            case NavigationSurface.PlayDialog:
                ShowPlayDialog();
                break;
        }
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e) => ReturnFromSettings();

    private void SettingsClose_Click(object sender, RoutedEventArgs e)
    {
        _settingsReturnSurface = NavigationSurface.Stage;
        UpdateSettingsReturnButton();
        CloseSettingsPanel();
    }

    private void CloseSettingsPanel()
    {
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
            HeaderChrome.Opacity = 1; FooterChrome.Opacity = 1;
            ChromeMotion.FadeIn(HeaderChrome, 240); ChromeMotion.FadeIn(FooterChrome, 240);
        }
        else
        {
            // Fade only the chrome layered over the full-window stage; revealing controls never changes
            // the stage's render size or moves notes and keys under a stationary pointer.
            if (ChromeMotion.Enabled) { ChromeMotion.FadeOut(HeaderChrome, 170); ChromeMotion.FadeOut(FooterChrome, 170); }
            else { HeaderChrome.Opacity = 0; FooterChrome.Opacity = 0; }
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
    private static ShellTheme PublishedChromeTheme => ShellThemeManager.IsHighContrast
        ? ShellThemes.HighContrast()
        : ShellThemeManager.Current;

    private string ChromeBackdropSignature(ShellTheme theme) =>
        $"{theme.Id}|{_visualSettings.ShellTheme}|{_visualSettings.ChromeMotion}|{_visualSettings.BackdropDensity:0}|{(_chromeMotionLocked ? "locked" : "live")}";

    internal void ApplyChromeTheme()
    {
        var selectedTheme = ShellThemes.Find(_visualSettings.ShellTheme);
        // Compare the whole record, not just its stable id: saving new colours under the same user-theme
        // name must still replace the published brushes and redraw every dependent surface.
        var themeChanged = selectedTheme != _appliedShellTheme;
        if (themeChanged)
        {
            // Set the guard before Apply raises Changed, so the callback sees the final selection.
            _appliedShellTheme = selectedTheme;
            ShellThemeManager.Apply(selectedTheme);
        }
        ChromeMotion.Enabled = !_chromeMotionLocked && _visualSettings.ChromeMotion != "Off" && !ShellThemeManager.IsHighContrast;
        var published = PublishedChromeTheme;
        var signature = ChromeBackdropSignature(published);
        var backdropChanged = signature != _backdropSignature;
        if (backdropChanged) ConfigureBackdrops(published, signature);
        if (backdropChanged)
        {
            if (_sweepStarted) UpdateChromeSweeps(published);
            else if (ChromeMotion.Enabled) StartChromeSweeps();
        }
    }

    private void OnShellThemeChanged(ShellTheme published)
    {
        void RefreshPublishedTheme()
        {
            ChromeMotion.Enabled = !_chromeMotionLocked && _visualSettings.ChromeMotion != "Off" && !ShellThemeManager.IsHighContrast;
            ConfigureBackdrops(published, ChromeBackdropSignature(published));
            UpdateThemeChrome(published);
        }

        if (Dispatcher.CheckAccess()) RefreshPublishedTheme();
        else Dispatcher.BeginInvoke(new Action(RefreshPublishedTheme));
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

    private void ConfigureBackdrops(ShellTheme theme, string signature)
    {
        _backdropSignature = signature;
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
        if (_sweepStarted) UpdateChromeSweeps(theme);
    }

    /// <summary>The header and footer carry a slow comet of the accent colour (unless motion is off).</summary>
    private void StartChromeSweeps()
    {
        if (_sweepStarted) return;
        UpdateChromeSweeps(PublishedChromeTheme);
    }

    private void UpdateChromeSweeps(ShellTheme theme)
    {
        if (!ChromeMotion.Enabled || _chromeMotionLocked || theme.Id == ShellThemes.HighContrastId)
        {
            HeaderSweep.Background = null;
            FooterSweep.Background = null;
            return;
        }
        _sweepStarted = true;
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

    /// <summary>
    /// How long the MP4 writer gets to open before the machine is treated as one whose media stack stopped
    /// answering. Opening a take is a handful of native calls and takes well under a second wherever it works at
    /// all; ten seconds is generous for a slow machine and short enough that a wedged one never looks like a
    /// window that has hung.
    /// </summary>
    private static readonly TimeSpan RecorderOpenLimit = TimeSpan.FromSeconds(10);

    /// <summary>A raw AVI that would fill the format's 2 GB in less time than this is written as MP4 instead.</summary>
    private const double MinRawAviSeconds = 120;

    /// <summary>Set by the <c>--soak-record</c> diagnostics so a take starts without the save dialog; null for a person at the window.</summary>
    internal string? DiagnosticRecordTarget { get; set; }

    private void RecordVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_videoRecorder is not null) { StopVideoRecording(showMessage: true); return; }
        var sequence = _visualSettings.RecordingFormat == RecordingFormatIds.PngSequence;
        var mp4 = _visualSettings.RecordingFormat == RecordingFormatIds.Mp4;
        var webm = _visualSettings.RecordingFormat == RecordingFormatIds.Webm;
        var (width, height) = RecordingSize();
        var frameRate = (int)Math.Clamp(_visualSettings.RecordingFrameRate, 15, 60);
        // A folder for the frames, or a file for the video: one choice in the dock decides which recorder runs.
        var target = DiagnosticRecordTarget ?? (sequence ? ChooseFrameFolder(width, height, frameRate) : ChooseVideoFile(mp4, webm));
        if (target is null) return;
        try
        {
            // The MP4 writer owns its audio stream, so it has to know before it opens the file whether the
            // engine will hand it samples.
            var withAudio = _visualSettings.RecordAudio && _audio.HasInstrument;
            var path = target!;
            var mp4Fallback = false;
            if (target.Length > 0 && sequence)
            {
                _videoRecorder = new PngSequenceRecorder(path, width, height, frameRate);
            }
            else if (webm)
            {
                // ffmpeg does this encoding (Windows has no VP9 encoder of its own), so the constructor
                // says so in one sentence when the machine has none; the take never starts half-open.
                _videoRecorder = new WebmRecorder(path, width, height, frameRate, withAudio, _visualSettings.RecordingTransparent);
            }
            else if (mp4)
            {
                // The MP4 writer is native code that can stop answering rather than fail, so it is opened on a
                // thread of its own with a few seconds to come back (see Mp4Recorder.TryOpen): a machine like
                // that records an AVI instead of losing both the take and the window.
                var take = Mp4Recorder.TryOpen(path, width, height, frameRate, withAudio, RecorderOpenLimit, out var failure, out var stopped);
                if (take is not null) _videoRecorder = take;
                else if (!stopped) throw failure ?? new InvalidOperationException(Loc.T("A media type could not be prepared."));
                else
                {
                    path = Path.ChangeExtension(path, ".avi");
                    _videoRecorder = new AviVideoRecorder(path, width, height, frameRate);
                    ShowMessage(Loc.T("This machine's media stack stopped answering while the MP4 recorder was being opened, so this take is being recorded as AVI instead."), "Video recording", MessageBoxImage.Warning);
                }
            }
            else
            {
                var avi = new AviVideoRecorder(path, width, height, frameRate);
                _videoRecorder = avi;
                // Without an MJPEG codec an AVI is raw 24-bit frames: 1080p fills the format's 2 GB ceiling in
                // seconds. A take that short is no use to anyone, so the same take goes to MP4 instead whenever
                // this machine can write one; the AVI is kept only when it cannot.
                var rawSeconds = AviVideoRecorder.SizeLimitBytes / (double)(AviVideoRecorder.BgrStride(width) * height * frameRate);
                if (!avi.UsesMjpeg && rawSeconds < MinRawAviSeconds)
                {
                    var mp4Path = Path.ChangeExtension(path, ".mp4");
                    var take = Mp4Recorder.TryOpen(mp4Path, width, height, frameRate, withAudio, RecorderOpenLimit, out _, out _);
                    if (take is not null)
                    {
                        avi.Dispose();
                        try { File.Delete(path); } catch { }
                        _videoRecorder = take; path = mp4Path; mp4Fallback = true;
                    }
                }
            }
            _recordingPath = path;
            // The PNG sequence and an alpha WebM carry the alpha channel, so the stage draws without its
            // opaque background while either runs; the switch is the user's and honoured only there.
            Stage.TransparentBackdrop = (sequence || webm) && _visualSettings.RecordingTransparent;
            StartGpuRecording(_videoRecorder);
            _audioTrackStarted = BeginAudioTrack();
            // Poll twice per frame; frames are paced by the recording clock inside RecordTimer_Tick, not by timer ticks.
            _recordClock.Restart(); _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / (_videoRecorder.FrameRate * 2)) };
            _recordTimer.Tick += RecordTimer_Tick; _recordTimer.Start();
            Loc.Set(RecordButton, "REC 00:00"); RecordButton.Background = new SolidColorBrush(Color.FromRgb(104, 23, 42));
            if (_videoRecorder is PngSequenceRecorder)
            {
                Loc.Set(RecordButton, "Recording PNG frames · click to stop", FrameworkElement.ToolTipProperty);
                Loc.Set(SettingsSaveLabel, "Recording a PNG sequence with alpha");
            }
            else if (_videoRecorder is WebmRecorder webmRecorder)
            {
                Loc.Set(RecordButton, "Recording WebM · click to stop", FrameworkElement.ToolTipProperty);
                Loc.Set(SettingsSaveLabel, webmRecorder.HasAudio
                    ? webmRecorder.Alpha ? "Recording WebM (VP9 + Opus, with alpha)" : "Recording WebM (VP9 + Opus)"
                    : "Recording WebM (VP9, the sound stayed out)");
            }
            else if (_videoRecorder is Mp4Recorder mp4Recorder)
            {
                Loc.Set(RecordButton, "Recording MP4 with the audio inside · click to stop", FrameworkElement.ToolTipProperty);
                Loc.Set(SettingsSaveLabel, mp4Fallback ? "No MJPEG codec on this machine, so this take is recorded as MP4 instead of a raw AVI"
                    : mp4Recorder.HasAudio ? "Recording MP4 (H.264 + AAC)" : "Recording MP4 (H.264, the sound stayed out)");
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

    /// <summary>The video file the next recording goes into, or null when the user cancels.</summary>
    private string? ChooseVideoFile(bool mp4, bool webm)
    {
        var dialog = new SaveFileDialog
        {
            Filter = webm ? Loc.T("WebM video (*.webm)|*.webm") : mp4 ? Loc.T("MP4 video (*.mp4)|*.mp4") : Loc.T("AVI video (*.avi)|*.avi"),
            DefaultExt = webm ? ".webm" : mp4 ? ".mp4" : ".avi",
            AddExtension = true,
            FileName = $"Keyflow-{DateTime.Now:yyyyMMdd-HHmmss}{(webm ? ".webm" : mp4 ? ".mp4" : ".avi")}",
            Title = Loc.T("Record piano visualizer")
        };
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
                var frame = TryCaptureGpuFrame(recorder)
                    ?? (recorder.HasAlpha ? CaptureStageBgra(recorder.Width, recorder.Height) : CaptureStageBgr(recorder.Width, recorder.Height));
                recorder.WriteFrame(frame, Math.Min(due, recorder.FrameRate * 2));
                if (_videoRecorder.IsNearSizeLimit) { StopVideoRecording(showMessage: true, Loc.T("The AVI file reached the 2 GB limit of the AVI format, so recording stopped automatically.")); return; }
            }
            var elapsed = _recordClock.Elapsed;
            PumpAudioTrack(elapsed);
            Loc.Format(RecordButton, "REC {0:00}:{1:00}", elapsed.Minutes, elapsed.Seconds);
        }
        catch (Exception ex)
        {
            StopVideoRecording(showMessage: false);
            ShowMessage(ex.Message, "Video recording stopped");
        }
    }

    // =====================================================================================================
    // Webcam overlay: one reader on its own thread, the newest frame handed to the stage
    // =====================================================================================================

    private CameraFrameReader? _camera;
    private Thread? _cameraThread;
    private CameraSession? _cameraSession;
    /// <summary>The frames from the camera thread to the drawing thread: three buffers, no copy.</summary>
    private readonly CameraFrameRing _cameraFrames = new();
    private long _lastHandTrackTicks;
    private WriteableBitmap? _cameraBitmap;
    private DispatcherTimer? _cameraUiTimer;
    private string _cameraSignature = "";
    private ArCalibrationSession? _arCalibration;
    private ArDelaySession? _arDelays;
    private string _arSource = "";
    private int _arWidth, _arHeight;
    private ArPlan _arPlan = ArPlan.Off;
    private int _arStatusTicks;
    /// <summary>Set from the plan: the camera thread reads frames unmirrored because the engine turns them round when it draws.</summary>
    private volatile bool _arRawFrames;
    /// <summary>What the AR card reads before the session has ever been told about a camera: nothing open, the default view.</summary>
    private static readonly ArCalibrationSnapshot NoArSource = new(ArCalibrationState.NoSource, null, ArCalibrationProfiles.Legacy, "", 0, 0, 0, 0);

    /// <summary>What the overlay is doing now, as <see cref="ArPlan"/> decided it.</summary>
    internal ArPlan ArPlanNow => _arPlan;

    private ArPlan DecideAr() => ArPlan.Decide(_visualSettings.ArOptionsValue, _gpuLoop is { Error: null }, _arSource.Length > 0,
        _arCalibration?.Snapshot ?? NoArSource, _visualSettings.BackgroundMode == "ChromaGreen");

    /// <summary>
    /// Carries out the plan for the overlay: the camera and the AR layer go to the engine (or leave it), the stage's own notes and
    /// keys step aside while the overlay is drawn, and the engine becomes the only reader of the camera's frames. Cheap, and safe to
    /// call after anything that could change the answer: a setting, the engine, the camera, the calibration.
    /// </summary>
    internal void UpdateArFeed()
    {
        var plan = DecideAr();
        var arLayerChanged = plan.ArLayer != _arPlan.ArLayer;
        _arPlan = plan;
        if (arLayerChanged && SettingsSearchBox is not null) RefreshDependentRows();
        Stage.ArOverlayActive = plan.HideStageLayers;
        if (!plan.CameraLayer)
        {
            _gpuFeed.Ar = null;
            _gpuFeed.Camera = null;
            RefreshArStatus();
            return;
        }
        var options = _visualSettings.ArOptionsValue;
        // The engine's render thread now takes the frames from the ring; the window's own picture-in-picture and hand tracker
        // would take them from it too, so they stand down while the overlay owns the camera.
        Stage.SetCameraFrame(null);
        Stage.SetHandReading(null);
        HandStatus = "";
        _gpuFeed.Camera = new GpuCameraInput(_cameraFrames, PictureFit.Fit, _visualSettings.CameraMirror, 1f);
        var calibration = plan.ArLayer ? _arCalibration?.Current : null;
        _gpuFeed.Ar = calibration is null ? null
            : new GpuArInput(calibration, options.NoteStyle, options.LayerOpacity, options.KeyLightStrength, options.ShowsNotes,
                (float)options.DelaySeconds, options.Quality, options.NotesProjected, MinAspect: options.NoteMinAspect,
                ShowLiveTrails: options.LivePressTrails, NoteDirection: options.NoteDirection,
                ShowRisingMidiNotes: options.ShowsRisingMidiNotes, AlignmentGuides: _visualSettings.ArAlignmentCheck,
                ShowDamperStrip: _visualSettings.ArDamperStrip, AlignmentGuideColor: ArGuideColor(),
                ViewProfileId: _visualSettings.ArViewProfileId);
        RefreshHandStatus();
        RefreshArStatus();
    }

    /// <summary>
    /// The calibration of the camera that is open, kept in the settings folder next to everything else. It is made on first
    /// use, so a check that points the settings folder somewhere temporary gets a store there.
    /// </summary>
    internal ArCalibrationSession ArCalibration => _arCalibration ??= new ArCalibrationSession(new ArCalibrationStore(PianoVisualSettingsStore.SettingsDirectory));

    /// <summary>The alignment frame colour from the settings pick; unknown ids fall back to the off-white default.</summary>
    private System.Numerics.Vector3 ArGuideColor()
    {
        var (r, g, b) = ArAlignmentGuides.ColorFor(_visualSettings.ArAlignmentGuideColor);
        return new System.Numerics.Vector3(r, g, b);
    }

    /// <summary>
    /// The camera delay of the camera that is open (AR‑3.5): a USB webcam and a virtual camera are late by different
    /// amounts, so the Camera delay slider's value is kept per camera and comes back when that camera opens again.
    /// Lazy for the same reason as the calibration: a check that points the settings folder somewhere temporary gets
    /// a store there.
    /// </summary>
    internal ArDelaySession ArDelays => _arDelays ??= new ArDelaySession(new ArDelayStore(PianoVisualSettingsStore.SettingsDirectory));

    /// <summary>
    /// Tells the calibration which source is open (null for none) and brings it up to date with the keyboard size and the
    /// view of the camera the settings name. It is safe to call whenever anything changes; nothing happens unless the
    /// source, its picture size, the keyboard size or the view is different from last time.
    /// </summary>
    private void ApplyArSource(string? source, int width, int height)
    {
        // AR‑3.5: the delay belongs to the camera. Leaving one files the slider's value under its link; arriving at
        // one whose delay was ever tuned brings that delay back, with the slider and the engine following (the same
        // source re-applied changes nothing — SwitchTo says so with a null).
        var stored = ArDelays.SwitchTo(source, _visualSettings.ArDelay);
        _arSource = source ?? ""; _arWidth = width; _arHeight = height;
        if (_arSource.Length == 0) _arCalibration?.Clear();       // nothing open and nothing made yet: no reason to read the file
        else ArCalibration.Use(_arSource, _arWidth, _arHeight, _visualSettings.KeyboardLayoutChoice, _visualSettings.ArViewProfileId);
        if (stored is { } delay) ApplyStoredArDelay(delay);
    }

    /// <summary>
    /// What the overlay is doing right now — the camera it opened, the file it is looping, or why neither
    /// worked. Read by the Camera &amp; FX page, so the dock says the same thing the stage does.
    /// </summary>
    internal string CameraStatus { get; private set; } = "";

    /// <summary>
    /// What the hand tracker is seeing right now, in words: the key the hand is over and the fingers it holds
    /// up, or that no hand was found. Filled by the same pump that feeds the stage, so the dock and the stage
    /// always agree on what the tracker saw in the newest frame.
    /// </summary>
    internal string HandStatus { get; private set; } = "";

    /// <summary>
    /// Restarts the overlay when one of the settings that decide <em>what</em> it reads has changed; the corner,
    /// the size, the opacity, the mirror flag and the key are applied while drawing or per frame, so they never
    /// cost a camera reconnect.
    /// </summary>
    private void SyncCameraOverlay()
    {
        // The keyboard size may have changed with this very settings change; the calibration follows it.
        ApplyArSource(_arSource, _arWidth, _arHeight);
        // The reader opens for either layer: the picture can stay hidden while the hand is still followed, so a
        // user who wants only the key markers never has to put the camera on the stage to get them.
        var plan = DecideAr();
        var wanted = string.Join("|", _visualSettings.ShowCameraOverlay || _visualSettings.ShowHandTracking || plan.NeedsCamera,
            _visualSettings.CameraSourceLink, _visualSettings.CameraVideoPath, plan.RawFrames);
        if (wanted != _cameraSignature)
        {
            _cameraSignature = wanted;
            StartCameraOverlay();
        }
        UpdateArFeed();
    }

    /// <summary>
    /// Starts the overlay for the current settings: a live camera or a file, read on its own thread so looking
    /// for a frame never blocks a drawing pass. Nothing is opened when the layer is off.
    /// </summary>
    private void StartCameraOverlay()
    {
        StartCameraOverlayCore();
        // "In use" in the camera list moves to the camera that opened (or off the one that would not).
        Dispatcher.BeginInvoke(new Action(RebuildCameraList), DispatcherPriority.Background);
    }

    private void StartCameraOverlayCore()
    {
        StopCameraOverlay();
        var arPlan = DecideAr();
        _arRawFrames = arPlan.RawFrames;
        if (!_visualSettings.ShowCameraOverlay && !_visualSettings.ShowHandTracking && !arPlan.NeedsCamera) { CameraStatus = ""; HandStatus = ""; return; }
        var live = string.IsNullOrWhiteSpace(_visualSettings.CameraVideoPath);
        var link = _visualSettings.CameraSourceLink;
        var path = _visualSettings.CameraVideoPath;
        // The reader is opened, read and closed on ONE thread, its own. A Media Foundation reader that is created on the
        // UI thread (an STA) and read from another one fails on the first read with E_NOINTERFACE ("Unable to cast COM
        // object ... to IMFSourceReader"), because the call has to be marshalled into the apartment that made it and the
        // reader has no marshalling support. So the thread opens it, tells this one how that went, and carries on reading.
        var session = new CameraSession();
        var opened = new ManualResetEventSlim();
        var thread = new Thread(() => RunCamera(session, live, link, path, opened)) { IsBackground = true, Name = "Keyflow camera overlay" };
        _cameraSession = session;
        _cameraThread = thread;
        _cameraFrames.Clear();
        thread.Start();
        if (!opened.Wait(TimeSpan.FromSeconds(20)))
        {
            session.Stop = true;                 // the thread closes whatever it manages to open, and goes
            _cameraSession = null; _cameraThread = null;
            CameraStatus = Loc.T("The camera did not answer in time.");
            return;
        }
        var reader = session.Reader;
        if (reader is null)
        {
            _cameraSession = null; _cameraThread = null;
            CameraStatus = session.OpenError ?? Loc.T("The camera overlay could not be opened.");
            return;
        }
        var error = session.OpenNote;
        _camera = reader;
        ApplyArSource(reader.Source, reader.Width, reader.Height);
        CameraStatus = live
            ? Loc.F("Live: {0} · {1}", reader.Label, reader.Mode.Label)
            : Loc.F("Video: {0} · {1} × {2}", reader.Label, reader.Width, reader.Height);
        // A reader that opened but had something to add (the stored camera is gone, or it would not take the
        // mode that was chosen) is heard too.
        if (error is { Length: > 0 } note) CameraStatus = $"{CameraStatus} {note}";
        if (reader.ModeNote.Length > 0) CameraStatus = $"{CameraStatus} {reader.ModeNote}";
        // The frames are turned into a bitmap on the UI thread, once per frame time at most.
        if (_cameraUiTimer is null)
        {
            _cameraUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / CameraFrameReader.DisplayRate) };
            _cameraUiTimer.Tick += (_, _) => PumpCameraFrame();
        }
        _cameraUiTimer.Start();
        UpdateArFeed();
    }

    /// <summary>Stops the overlay, closes the reader and clears the picture from the stage.</summary>
    private void StopCameraOverlay()
    {
        if (_cameraSession is { } session) session.Stop = true;
        _cameraSession = null;
        _cameraUiTimer?.Stop();
        var thread = _cameraThread;
        _cameraThread = null;
        // The camera thread closes the reader itself (see StartCameraOverlay), as soon as the read it is waiting in comes
        // back — one frame time on a live camera. A camera that never answers leaves a background thread behind, not a
        // frozen window.
        _camera = null;
        if (thread is { IsAlive: true }) thread.Join(TimeSpan.FromMilliseconds(250));
        _cameraFrames.Clear();
        ApplyArSource(null, 0, 0);
        Stage.SetCameraFrame(null);
        Stage.SetHandReading(null);
        HandStatus = "";
    }

    /// <summary>
    /// The reading thread: one frame at a time into a free buffer of the ring, with a file rewound when it ends and a
    /// reader that fails reported instead of spinning. A live camera is read as fast as it delivers — asking for a
    /// frame returns when there is one, so a 60 fps mode gives 60 frames a second and no sleep gets in the way. Only a
    /// video file is held back, to the times written in it.
    /// </summary>
    private void RunCamera(CameraSession session, bool live, string link, string path, ManualResetEventSlim opened)
    {
        CameraFrameReader? reader = null;
        try
        {
            string? error;
            reader = live ? CameraFrameReader.OpenDevice(link, out error) : CameraFrameReader.OpenFile(path, out error);
            session.Reader = reader;
            session.OpenError = reader is null ? error : null;
            session.OpenNote = reader is null ? null : error;
        }
        catch (Exception ex) { session.OpenError = ex.Message; }
        finally { opened.Set(); }
        if (reader is null) return;
        try { if (!session.Stop) CameraLoop(session, reader); }
        finally { reader.Dispose(); }
    }

    /// <summary>What the UI thread and the camera thread share for one run of the overlay.</summary>
    private sealed class CameraSession
    {
        internal volatile bool Stop;
        internal CameraFrameReader? Reader;
        internal string? OpenError;
        internal string? OpenNote;
    }

    private void CameraLoop(CameraSession session, CameraFrameReader reader)
    {
        var pacer = new FramePacer();
        try
        {
            while (!session.Stop && reader.Error is null)
            {
                var size = reader.Width * reader.Height * 4;
                if (size <= 0) { Thread.Sleep(20); continue; }
                var buffer = _cameraFrames.BeginWrite(size, out var slot);
                if (!reader.TryReadFrame(buffer, _visualSettings.CameraMirror && !_arRawFrames, out var width, out var height))
                {
                    if (reader.AtEnd) { reader.Rewind(); pacer.Reset(); Thread.Sleep(20); continue; }
                    if (reader.Error is null) { Thread.Sleep(5); continue; }
                    break;
                }
                _cameraFrames.Publish(slot, width, height, reader.LastFrameTicks);
                if (reader.IsLive) continue;
                var wait = pacer.WaitSeconds(reader.LastSampleTime, System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
                if (wait > 0) Thread.Sleep((int)Math.Ceiling(wait * 1000));
            }
            if (!session.Stop && reader.Error is { Length: > 0 } failure) CameraStatus = failure;
        }
        catch (Exception ex) { if (!session.Stop) CameraStatus = ex.Message; }
    }

    /// <summary>
    /// Hands the pump a frame the way the camera thread does. The checks use it to put a known picture on the stage
    /// without a camera; it is the same ring the real frames go through.
    /// </summary>
    internal void InjectCameraFrame(byte[] bgra, int width, int height)
    {
        var buffer = _cameraFrames.BeginWrite(width * height * 4, out var slot);
        Buffer.BlockCopy(bgra, 0, buffer, 0, Math.Min(bgra.Length, width * height * 4));
        _cameraFrames.Publish(slot, width, height, System.Diagnostics.Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Turns the newest frame the reader produced into a bitmap for the stage, on the UI thread. The copy also
    /// applies the key, so the stage receives pixels that are already see-through where the green was.
    /// </summary>
    private void PumpCameraFrame()
    {
        PollLatency();
        PollDriftWatch();
        if (_arPlan.CameraLayer)               // the engine's render thread is the ring's reader while the overlay is drawn
        {
            if (++_arStatusTicks % 30 == 0) { RefreshArStatus(); RefreshCameraOverlayStatus(announce: false); }
            return;
        }
        if (!_cameraFrames.TryTake(out var taken)) return;
        var frame = taken.Pixels; var width = taken.Width; var height = taken.Height;
        if (width <= 0 || height <= 0 || frame.Length < width * height * 4) return;
        // The tracker reads the frame before the key is applied: it wants the colours the camera really saw, and
        // making the green see-through would only take pixels away from the count. It runs whatever the picture
        // layer is set to, because the key markers are a layer of their own.
        // At most 30 times a second however fast the camera is: a 60 fps mode is for the picture and the latency
        // probe, not a reason to run the tracker twice as often.
        var trackNow = System.Diagnostics.Stopwatch.GetTimestamp();
        var trackDue = trackNow - _lastHandTrackTicks >= System.Diagnostics.Stopwatch.Frequency / 30 * 9 / 10;
        if (_visualSettings.ShowHandTracking && trackDue)
        {
            _lastHandTrackTicks = trackNow;
            var reading = HandTracker.Track(frame, width, height, _visualSettings.HandTrackingSensitivity);
            Stage.SetHandReading(reading);
            HandStatus = reading.Found
                ? Loc.F("Hand: {0} finger(s), over {1} · {2:0}% of the picture", reading.Fingers, NoteLabel(HandTracker.KeyPitch(reading.CenterX, KeyboardLayout.Active)), reading.Coverage * 100)
                : Loc.T("Hand: nothing found in the newest frame.");
        }
        else if (!_visualSettings.ShowHandTracking && Stage.HasHand)
        {
            Stage.SetHandReading(null);
            HandStatus = "";
        }
        RefreshHandStatus();
        CameraOverlay.ApplyKey(frame, width * height, _visualSettings.CameraKeyTolerance);
        if (_cameraBitmap is null || _cameraBitmap.PixelWidth != width || _cameraBitmap.PixelHeight != height)
            _cameraBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        _cameraBitmap.WritePixels(new Int32Rect(0, 0, width, height), frame, width * 4, 0);
        Stage.SetCameraFrame(_cameraBitmap);
    }

    private IAudioTrack? _audioTrack;
    private readonly Lock _audioTrackLock = new();
    private bool _audioTrackStarted;
    private double _audioTrackSeconds;
    /// <summary>Seconds of silence this take's audio began with, to keep the sound with a delayed camera picture (see <see cref="RecordingSync"/>).</summary>
    private double _audioLeadIn;

    /// <summary>
    /// Where the audio of a recording goes: a WAV beside the AVI, or <c>audio.wav</c> inside the PNG folder.
    /// Pure so the side the user never sees is still checkable.
    /// </summary>
    internal static string AudioTrackPath(string target, bool sequence) =>
        sequence ? Path.Combine(target, "audio.wav") : Path.ChangeExtension(target, ".wav");

    /// <summary>
    /// The name a muxed copy gets, next to the recording; empty when there is nothing to derive one from.
    /// The line that prints the ffmpeg hint only wants a string, so deriving the name must not be the
    /// place where a path that was already cleared turns into an exception.
    /// </summary>
    internal static string MuxedName(string? target) =>
        string.IsNullOrEmpty(target) ? "" : Path.ChangeExtension(target, null) + ".mp4";

    /// <summary>
    /// Opens the WAV for this take and attaches the engine's tap to it. Returns false when there is nothing to
    /// record — the setting is off, the Salamander piano is not loaded, or the file cannot be written — in which case the
    /// video records alone, exactly as it did before the audio track existed.
    /// </summary>
    internal bool BeginAudioTrack()
    {
        EndAudioTrack();
        _audioLeadIn = 0;
        if (!_visualSettings.RecordAudio || !_audio.HasInstrument || _recordingPath is null) return false;
        // An MP4 carries its own audio stream, so the same tap goes straight into the file being written;
        // every other format gets a WAV beside the video to mux afterwards.
        IAudioTrack? track = _videoRecorder is Mp4Recorder { HasAudio: true } mp4
            ? mp4
            : _videoRecorder is WebmRecorder { HasAudio: true } webmTrack
                ? webmTrack
                : WavWriter.TryCreate(AudioTrackPath(_recordingPath, _videoRecorder is PngSequenceRecorder));
        if (track is null) return false;
        _audioTrack = track; _audioTrackSeconds = 0;
        // A take with the camera in it starts its sound as late as the camera's picture is, so the finger and the sound of a press
        // land on the same stamp in the file (the picture is moved no earlier than zero, so the sound is the one that waits).
        _audioLeadIn = RecordingSync.Begin(track, _visualSettings.ArOptionsValue.DelaySeconds, _arPlan.CameraLayer);
        // The tap runs on the audio thread, so the writer is only ever touched under its lock.
        _audio.SetTap((samples, count) => { lock (_audioTrackLock) _audioTrack?.Append(samples, count); });
        _audio.SetTapClockEnabled(true);
        return true;
    }

    /// <summary>
    /// Closes the audio track and returns its path, or <c>null</c> when this take had none. Detaching the tap
    /// first means no block can arrive while the file is being finalized.
    /// </summary>
    internal string? EndAudioTrack()
    {
        _audio.SetTapClockEnabled(false);
        _audio.SetTap(null);
        _audio.FlushTap(); // all background audio-writer blocks are done before the WAV/MP4 is closed
        IAudioTrack? track;
        lock (_audioTrackLock) { track = _audioTrack; _audioTrack = null; }
        if (track is null) return null;
        _audioTrackSeconds = track.Seconds;
        // A recorder that carries its own track (the MP4's, the WebM's) is closed with its file, not
        // here, and there is no second file left behind to mux.
        if (track is not WavWriter) return null;
        var path = ((WavWriter)track).Path;
        track.Dispose();
        return path;
    }

    /// <summary>
    /// Keeps the audio track clocked while recording. A live device fills it from its audio callback; with no
    /// output device, PianoAudioEngine runs a separate 44.1 kHz tap clock so synthesis and file writes never
    /// hitch the WPF frame loop.
    /// </summary>
    private void PumpAudioTrack(TimeSpan elapsed)
    {
        if (!_audioTrackStarted) return;
        if (_audio.HasAudioOutput || _audio.HasTap)
        {
            _audioTrackSeconds = AudioTrackSeconds();
            return;
        }
        var due = RecordingSync.SilenceDue(elapsed.TotalSeconds, _audioLeadIn, AudioTrackSeconds());
        if (due > 0) { lock (_audioTrackLock) _audioTrack?.AppendSilence(due); }
        _audioTrackSeconds = AudioTrackSeconds();
    }

    /// <summary>Seconds of audio written so far, read under the lock the audio thread also holds.</summary>
    private double AudioTrackSeconds()
    {
        lock (_audioTrackLock) return _audioTrack?.Seconds ?? _audioTrackSeconds;
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

    private byte[] CaptureStageBgr(int width, int height) => ToBottomUpBgr(RenderStagePixels(width, height), width, height);

    /// <summary>Repacks tightly packed BGRA into the bottom-up, stride-aligned BGR rows an AVI frame is made of.</summary>
    private byte[] ToBottomUpBgr(byte[] source, int width, int height)
    {
        if (_captureTarget is null || _captureTarget.Length != AviVideoRecorder.BgrStride(width) * height) _captureTarget = new byte[AviVideoRecorder.BgrStride(width) * height];
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
        StopGpuRecording();
        var recorder = _videoRecorder; _videoRecorder = null;
        if (recorder is null) return;
        var path = _recordingPath; _recordingPath = null;
        var frames = recorder.FrameCount;
        var audioPath = EndAudioTrack();
        try { recorder.Dispose(); } catch (Exception ex) { if (showMessage && !_closing) ShowMessage(ex.Message, "Video recording"); }
        // The export is over: the stage goes back to painting its own background, whatever the framing was.
        Stage.TransparentBackdrop = false;
        Loc.Set(RecordButton, "REC"); RecordButton.ClearValue(BackgroundProperty);
        Loc.Set(RecordButton, "Record the live piano visualizer", FrameworkElement.ToolTipProperty);
        if (showMessage && !_closing)
        {
            var noteText = note is null ? "" : note + "\n\n";
            var audio = recorder is Mp4Recorder take
                ? take.AudioDropped
                    ? Loc.T(" The AAC encoder on this machine refused the audio stream, so this take has no sound.")
                    : Loc.F("\n\nAudio: {0:0.#} s of 16-bit stereo AAC, written inside the file.", _audioTrackSeconds)
                : recorder is WebmRecorder webmTake
                    ? audioPath is not null
                        ? Loc.F("\n\nAudio: {0} ({1:0.#} s, 16-bit stereo WAV) beside the WebM; the take itself kept no sound.", audioPath, _audioTrackSeconds)
                        : webmTake.HasAudio
                            ? Loc.F("\n\nAudio: {0:0.#} s of Opus, written inside the file.", _audioTrackSeconds)
                            : Loc.T(" No audio track was written for this take.")
                    : audioPath is null
                    ? Loc.T(" No audio track was written for this take.")
                    : Loc.F("\n\nAudio: {0} ({1:0.#} s, 16-bit stereo WAV) — mux it with\nffmpeg -i \"{2}\" -i \"{0}\" -c:v copy -c:a aac \"{3}\"", audioPath, _audioTrackSeconds, audioPath, path, MuxedName(path));
            if (recorder is Mp4Recorder)
                ShowMessage(Loc.F("Video saved with its audio inside.\n{0}\n\n{1}This MP4 holds the piano visuals and the samples the engine played, encoded as H.264 and AAC, so it is ready to upload as it is.", path, noteText) + audio, "Recording complete", MessageBoxImage.Information);
            else if (recorder is WebmRecorder)
                ShowMessage(Loc.F("Video saved.\n{0}\n\n{1}This WebM holds the piano visuals encoded as VP9 by ffmpeg.", path, noteText) + audio, "Recording complete", MessageBoxImage.Information);
            else if (recorder is PngSequenceRecorder)
                ShowMessage(Loc.F("Frames saved.\n{0}\n\n{1}{2} PNG frames with an alpha channel. Import them at the frame rate you chose, or follow the ffmpeg line in sequence.json to turn them into alpha video; system audio is not mixed in.", path, noteText, frames) + audio, "Recording complete", MessageBoxImage.Information);
            else
                ShowMessage(Loc.F("Video saved.\n{0}\n\n{1}This AVI contains the piano visuals; system audio is not mixed into the recording.", path, noteText) + audio, "Recording complete", MessageBoxImage.Information);
        }
    }
    internal static int MapComputerKey(Key key) { var index = Array.IndexOf(ComputerKeys, key); return index < 0 ? -1 : 48 + ComputerMap[index]; }
    private static string NoteLabel(int pitch) { string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]; return $"{names[pitch % 12]}{pitch / 12 - 1}"; }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (_keyDownPitches.Remove(e.Key, out var pitch)) ReleaseNote(pitch); }

    /// <summary>True while a MIDI input event is being handled on the dispatcher: the MIDI thread has already
    /// sent it to the synthesizer, so the handlers below must not sound it a second time.</summary>
    private bool _liveAudioSent;

    private void PressNote(int pitch, int velocity = 100)
    {
        if (!_pressed.Add(pitch)) return;
        NoteKeyboardRange(pitch);
        StartStageFrames();
        var hit = velocity / 127.0; Stage.AddLiveNote(pitch, hit); Stage.Impact(pitch, hit); if (!_liveAudioSent) _audio.NoteOn(pitch, velocity); SendOutput(pitch, velocity, true);
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
                if (delta <= .55) { _hits++; _streak++; _bestStreak = Math.Max(_bestStreak, _streak); RecordPracticeNote(true, target.Pitch, target.Start); } else { _misses++; _streak = 0; RecordPracticeNote(false, target.Pitch, target.Start); }
                target.Timing = Math.Max(0, 100 - delta * 180); Loc.Set(NoteNameLabel, delta < .11 ? "PERFECT" : delta < .28 ? "GREAT" : "KEEP GOING");
                if (ModeCombo.SelectedIndex == 1) _clock.Restart();
            }
            // A key pressed where the song has no note still happened at this moment, so the ghost keeps it.
            else { _misses++; _streak = 0; RecordPracticeNote(false, pitch, _position); NoteNameLabel.Text = NoteLabel(pitch); }
        }
        else NoteNameLabel.Text = NoteLabel(pitch);
        UpdateStats(); UpdateStage();
    }
    private void ReleaseNote(int pitch)
    {
        if (!_pressed.Remove(pitch)) return;
        if (!_liveAudioSent) _audio.NoteOff(pitch);
        SendOutput(pitch, 0, false); Stage.ReleaseLiveNote(pitch); UpdateStage();
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
        Stage.Simulation.SetSostenuto(_pedalsDown.Contains(PianoPedal.Sostenuto));
        Stage.Simulation.SetSoft(_pedalsDown.Contains(PianoPedal.Soft));
        var controller = MidiDeviceService.ControllerFor(pedal);
        if (!_liveAudioSent) _audio.ControlChange(controller, down ? 127 : 0);
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
        RefreshAudioOutputs();
        RefreshAudioOutputCatalog(); // MMDevice COM enumeration can be slow; never hold up the WPF input thread.
        ConnectInput(showErrors: false);
        if (outputIndex > 0) ConnectOutput();
    }

    /// <summary>The latency rungs of the settings combo, in combo order: Safe, Balanced, Low, Ultra.</summary>
    private static readonly double[] AudioLatencySteps = [20, 10, 5, 3];

    /// <summary>
    /// Shows every active Windows playback endpoint (including Voicemeeter virtual inputs), both shared and
    /// exclusive WASAPI, plus the installed ASIO drivers. Entries keep stable ids while their friendly names
    /// remain exactly as Windows reports them.
    /// </summary>
    private void RefreshAudioOutputs()
    {
        var items = new List<DeviceOption>
        {
            new(AudioOutputChoice.AutoId, Loc.T("Auto · WASAPI, then waveOut")),
            new(AudioOutputChoice.WasapiSharedId, Loc.T("WASAPI shared · low latency")),
            new(AudioOutputChoice.WasapiExclusiveId, Loc.T("WASAPI exclusive · owns the device")),
            new(AudioOutputChoice.WaveOutId, Loc.T("waveOut · compatibility")),
        };
        foreach (var endpoint in _audioOutputEndpoints)
        {
            var name = endpoint.IsDefault ? Loc.F("{0} · default", endpoint.FriendlyName) : endpoint.FriendlyName;
            var shared = AudioOutputChoice.ForWasapiEndpoint(endpoint.Id);
            var exclusive = AudioOutputChoice.ForWasapiEndpoint(endpoint.Id, exclusive: true);
            items.Add(new DeviceOption(shared.Id, Loc.F("WASAPI shared · {0}", name)));
            items.Add(new DeviceOption(exclusive.Id, Loc.F("WASAPI exclusive · {0}", name)));
        }
        foreach (var driver in AsioDriverRegistry.Names)
            items.Add(new DeviceOption(AudioOutputChoice.ForAsio(driver).Id, Loc.F("ASIO · {0}", driver)));

        var stored = AudioOutputChoice.Parse(_visualSettings.AudioOutputMode);
        if (items.All(item => item.Id != stored.Id))
        {
            if (stored.Kind == AudioBackendKind.Asio)
                items.Add(new DeviceOption(stored.Id, Loc.F("ASIO · {0}", stored.AsioDriver)));
            else if (stored.EndpointId.Length > 0)
                items.Add(new DeviceOption(stored.Id, Loc.F("Unavailable audio output · {0}", stored.EndpointId)));
        }

        _suppressDevices = true;
        try
        {
            AudioOutputCombo.ItemsSource = items;
            AudioOutputCombo.SelectedValue = stored.Id;
            if (AudioOutputCombo.SelectedIndex < 0) AudioOutputCombo.SelectedIndex = 0;
            var latencyIndex = Array.IndexOf(AudioLatencySteps, _visualSettings.AudioLatencyMs);
            AudioLatencyCombo.SelectedIndex = latencyIndex >= 0 ? latencyIndex : 2;
            AudioGainSlider.Value = Math.Clamp(_visualSettings.AudioMasterGain * 100, 50, 200);
            AudioGainValue.Text = $"{AudioGainSlider.Value:0}%";
            AudioReverbSlider.Value = Math.Clamp(_visualSettings.AudioReverbAmount, 0, 150);
            AudioReverbValue.Text = $"{AudioReverbSlider.Value:0}%";
        }
        finally { _suppressDevices = false; }
        UpdateAudioOutputHint();
    }

    private void RefreshAudioOutputCatalog()
    {
        Interlocked.Increment(ref _audioOutputCatalogVersion);
        StartAudioOutputCatalogRefresh();
    }

    private void StartAudioOutputCatalogRefresh()
    {
        if (Interlocked.CompareExchange(ref _audioOutputCatalogRefreshPending, 1, 0) != 0) return;
        var version = Volatile.Read(ref _audioOutputCatalogVersion);
        _ = Task.Run(WasapiDeviceCatalog.GetActiveRenderEndpoints).ContinueWith(task =>
        {
            Interlocked.Exchange(ref _audioOutputCatalogRefreshPending, 0);
            if (_closing || Dispatcher.HasShutdownStarted) return;
            if (version != Volatile.Read(ref _audioOutputCatalogVersion)) { StartAudioOutputCatalogRefresh(); return; }
            if (task.Status != TaskStatus.RanToCompletion) return;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (_closing || version != Volatile.Read(ref _audioOutputCatalogVersion)) return;
                    _audioOutputEndpoints = task.Result;
                    RefreshAudioOutputs(); // repaint from the cache; translation changes do not rescan Windows
                }));
            }
            catch (InvalidOperationException) { }
        }, TaskScheduler.Default);
    }

    private void AudioOutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDevices || AudioOutputCombo.SelectedValue is not string id) return;
        _visualSettings.AudioOutputMode = id;
        ApplyAudioOutput();
    }

    private void AudioLatencyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDevices || AudioLatencyCombo.SelectedIndex < 0) return;
        _visualSettings.AudioLatencyMs = AudioLatencySteps[AudioLatencyCombo.SelectedIndex];
        ApplyAudioOutput();
    }

    private void AsioPanel_Click(object sender, RoutedEventArgs e) => _audio.ShowAsioControlPanel();

    private void AudioGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressDevices || sender is not Slider slider) return;
        var percent = (int)Math.Round(slider.Value);
        _visualSettings.AudioMasterGain = percent / 100.0;
        _audio.MasterGain = _visualSettings.AudioMasterGain;
        if (AudioGainValue is not null) AudioGainValue.Text = $"{percent}%";
        if (_uiReady) { _settingsSaveTimer.Stop(); _settingsSaveTimer.Start(); }
    }

    private void AudioReverbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressDevices || sender is not Slider slider) return;
        var percent = (int)Math.Round(slider.Value);
        _visualSettings.AudioReverbAmount = percent;
        _audio.ReverbAmount = percent / 100.0;
        if (AudioReverbValue is not null) AudioReverbValue.Text = $"{percent}%";
        if (_uiReady) { _settingsSaveTimer.Stop(); _settingsSaveTimer.Start(); }
    }

    /// <summary>Applies a device choice asynchronously; the status refresh and pedal restore happen when it is live.</summary>
    private void ApplyAudioOutput()
    {
        _audio.ConfigureOutput(AudioOutputChoice.Parse(_visualSettings.AudioOutputMode), _visualSettings.AudioLatencyMs);
        UpdateAudioOutputHint();
        _settingsSaveTimer.Stop(); _settingsSaveTimer.Start();
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
        if (SettingsPanel.Visibility == Visibility.Visible) ReturnFromSettings(); else OpenSettingsPanel();
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
        StopSongFolderWatch();
        StopCameraOverlay();
        ShutdownGpuStage();
        _midi.Dispose(); _audio.Dispose();
    }
}
