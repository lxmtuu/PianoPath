using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// Streams piano audio through WASAPI, ASIO or waveOut. MIDI commands are handed off through a small
/// producer queue so a hardware keyboard callback never waits for synthesis; the render thread consumes them
/// at the next buffer. Device changes and effect construction run on a dedicated STA control thread, not the
/// WPF UI, and recording writes are moved off the real-time callback by <see cref="AudioTapDispatcher"/>.
/// </summary>
internal sealed class PianoAudioEngine : IDisposable
{
    /// <summary>The synthesiser, reverb and recording track all run at this rate.</summary>
    public const int SampleRate = 44100;

    private readonly object _configurationGate = new();
    private readonly AudioControlDispatcher _deviceControl = new();
    private readonly AudioTapDispatcher _tapDispatcher = new();
    private readonly AutoResetEvent _tapClockWake = new(false);
    private readonly short[] _silentTapPcm = new short[512 * 2];
    private readonly ConcurrentQueue<AudioCommand> _commands = new();
    private readonly Thread _tapClockThread;
    private Pipeline? _pipeline;
    private SfzInstrumentData? _instrument;
    private int _reverbEnabled = 1;
    private PianoReverbStyle _reverbStyle = PianoReverbStyle.Hall;
    private string? _reverbImpulsePath;
    private WavData? _customImpulse;
    private int _resonanceEnabled = 1;
    private AudioOutputChoice _outputChoice = AudioOutputChoice.Auto;
    private double _latencyMilliseconds = 5;
    private IntPtr _asioSystemHandle;
    private float _masterGain = 1.25f;
    private string? _rebuildError;
    private int _tapClockEnabled, _rebuildVersion, _rebuildPosted, _disposed;
    private int _appliedRebuildVersion = -1;
    private int _reverbRequestVersion;

    private enum AudioCommandKind : byte { NoteOn, NoteOff, Midi, AllNotesOff }
    private readonly record struct AudioCommand(AudioCommandKind Kind, int Channel, int Data1, int Data2);
    private readonly record struct PipelineConfiguration(
        SfzInstrumentData? Instrument, bool ReverbEnabled, PianoReverbStyle ReverbStyle, WavData? CustomImpulse,
        bool ResonanceEnabled, AudioOutputChoice OutputChoice, double LatencyMilliseconds,
        IntPtr AsioSystemHandle);

    /// <summary>Raised after an async output/effect rebuild so the settings page can refresh its live status.</summary>
    public event Action? OutputChanged;

    internal PianoAudioEngine()
    {
        _tapClockThread = new Thread(PumpTapClock)
        {
            IsBackground = true,
            Name = "Keyflow silent recording audio clock",
            Priority = ThreadPriority.Normal,
        };
        _tapClockThread.Start();
    }

    public bool HasInstrument => Volatile.Read(ref _pipeline) is not null;
    public string? InstrumentName => Volatile.Read(ref _pipeline)?.Synth.Name;
    public bool HasAudioOutput => Volatile.Read(ref _pipeline)?.Sink is not null;
    public string? PlaybackError => Volatile.Read(ref _pipeline)?.OutputError ?? (Volatile.Read(ref _pipeline) is null ? Volatile.Read(ref _rebuildError) : null);
    public string? OutputDescription => Volatile.Read(ref _pipeline)?.Sink?.Description;
    public string? OutputFallbackNotice => Volatile.Read(ref _pipeline) is { Sink: not null } pipeline
        ? pipeline.FallbackNotice ?? Volatile.Read(ref _rebuildError)
        : Volatile.Read(ref _rebuildError);
    public bool IsAsioOutput => Volatile.Read(ref _pipeline)?.Sink is AsioAudioSink;

    /// <summary>Post-mix gain; a soft limiter catches peaks so raising a quiet hardware keyboard stays clean.</summary>
    public double MasterGain
    {
        get => Volatile.Read(ref _masterGain);
        set => Volatile.Write(ref _masterGain, (float)Math.Clamp(value, .5, 2));
    }

    /// <summary>The HWND supplied to ASIO init; set when the WPF window receives SourceInitialized.</summary>
    public void SetAsioSystemHandle(IntPtr handle) => Interlocked.Exchange(ref _asioSystemHandle, handle);

    public bool ReverbEnabled
    {
        get => Volatile.Read(ref _reverbEnabled) != 0;
        set
        {
            Volatile.Write(ref _reverbEnabled, value ? 1 : 0);
            Volatile.Read(ref _pipeline)?.SetReverbEnabled(value);
        }
    }

    /// <summary>Sympathetic string resonance while the sustain pedal is down; see <see cref="StringResonance"/>.</summary>
    public bool StringResonanceEnabled
    {
        get => Volatile.Read(ref _resonanceEnabled) != 0;
        set
        {
            Volatile.Write(ref _resonanceEnabled, value ? 1 : 0);
            Volatile.Read(ref _pipeline)?.SetResonanceEnabled(value);
        }
    }

    /// <summary>Which room the reverb stage renders; <see cref="SetReverb"/> changes it.</summary>
    public PianoReverbStyle ReverbStyle { get { lock (_configurationGate) return _reverbStyle; } }
    /// <summary>The user's impulse-response file when <see cref="ReverbStyle"/> is <see cref="PianoReverbStyle.CustomImpulse"/>.</summary>
    public string? ReverbImpulsePath { get { lock (_configurationGate) return _reverbImpulsePath; } }

    /// <summary>
    /// Selects the classic hall, a generated convolution room, or a user WAV/FLAC impulse. A custom impulse
    /// is validated before changing state; convolution setup and device reopening run on the control thread.
    /// </summary>
    public void SetReverb(PianoReverbStyle style, string? impulsePath = null)
    {
        var requestVersion = Interlocked.Increment(ref _reverbRequestVersion);
        var impulse = style == PianoReverbStyle.CustomImpulse ? ReadCustomImpulse(impulsePath) : null;
        ApplyReverb(style, impulsePath, impulse, requestVersion);
    }

    /// <summary>Reads large WAV/FLAC room responses on a worker; a newer room choice supersedes this request.</summary>
    public async Task<bool> SetReverbAsync(PianoReverbStyle style, string? impulsePath = null)
    {
        var requestVersion = Interlocked.Increment(ref _reverbRequestVersion);
        WavData? impulse = null;
        if (style == PianoReverbStyle.CustomImpulse)
        {
            try { impulse = await Task.Run(() => ReadCustomImpulse(impulsePath)).ConfigureAwait(false); }
            catch (Exception) when (requestVersion != Volatile.Read(ref _reverbRequestVersion)) { return false; }
        }
        return ApplyReverb(style, impulsePath, impulse, requestVersion);
    }

    private static WavData ReadCustomImpulse(string? impulsePath)
    {
        if (impulsePath is not { Length: > 0 }) throw new ArgumentException("A custom reverb needs an impulse response file.", nameof(impulsePath));
        return System.IO.Path.GetExtension(impulsePath).Equals(".flac", StringComparison.OrdinalIgnoreCase)
            ? FlacReader.Read(impulsePath)
            : WavReader.Read(impulsePath);
    }

    private bool ApplyReverb(PianoReverbStyle style, string? impulsePath, WavData? impulse, int requestVersion)
    {
        lock (_configurationGate)
        {
            if (requestVersion != Volatile.Read(ref _reverbRequestVersion) || Volatile.Read(ref _disposed) != 0) return false;
            _customImpulse = impulse;
            _reverbImpulsePath = style == PianoReverbStyle.CustomImpulse ? impulsePath : null;
            _reverbStyle = style;
        }
        if (HasInstrumentConfiguration()) RequestRebuild(wait: false);
        return true;
    }

    /// <summary>Remembers the output and latency target, then reopens it away from the WPF thread.</summary>
    public void ConfigureOutput(AudioOutputChoice choice, double latencyMilliseconds)
    {
        lock (_configurationGate)
        {
            _outputChoice = choice;
            _latencyMilliseconds = Math.Clamp(latencyMilliseconds, 2, 50);
        }
        if (HasInstrumentConfiguration()) RequestRebuild(wait: false);
    }

    /// <summary>Opens the active ASIO driver's panel on the same STA that owns the driver.</summary>
    public void ShowAsioControlPanel() => _deviceControl.Post(() => (Volatile.Read(ref _pipeline)?.Sink as AsioAudioSink)?.ShowControlPanel());

    /// <summary>Loads the installed SFZ piano away from the UI and waits for the first live pipeline.</summary>
    public void LoadSfzInstrument(string path)
    {
        if (!System.IO.Path.GetExtension(path).Equals(".sfz", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Loc.T("Keyflow piano instruments must use the SFZ format."));
        var instrument = SfzReader.Read(path);
        lock (_configurationGate) _instrument = instrument;
        RequestRebuild(wait: true);
    }

    private bool HasInstrumentConfiguration()
    {
        lock (_configurationGate) return _instrument is not null;
    }

    /// <summary>
    /// Keeps a recording's audio clock off the WPF frame loop when no output device is available. A 512-frame
    /// block is rendered every 11.6 ms; the tap writer remains a separate, bounded worker.
    /// </summary>
    private void PumpTapClock()
    {
        const int frames = 512;
        var periodTicks = Math.Max(1, (long)Math.Round(Stopwatch.Frequency * (double)frames / SampleRate));
        while (Volatile.Read(ref _disposed) == 0)
        {
            if (Volatile.Read(ref _tapClockEnabled) == 0 || !HasTap || HasAudioOutput)
            {
                _tapClockWake.WaitOne(50);
                continue;
            }

            var nextBlock = Stopwatch.GetTimestamp();
            while (Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _tapClockEnabled) != 0 && HasTap && !HasAudioOutput)
            {
                var now = Stopwatch.GetTimestamp();
                var remaining = nextBlock - now;
                if (remaining > 0)
                {
                    var waitMilliseconds = (int)Math.Clamp(Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency), 1, 50);
                    if (_tapClockWake.WaitOne(waitMilliseconds)) break;
                    continue;
                }

                var pipeline = Volatile.Read(ref _pipeline);
                if (pipeline is null) _tapDispatcher.Enqueue(_silentTapPcm, _silentTapPcm.Length);
                else if (!pipeline.HasAudioOutput) pipeline.RenderTapBlock();
                else break;

                nextBlock += periodTicks;
                now = Stopwatch.GetTimestamp();
                if (now - nextBlock > periodTicks) nextBlock = now + periodTicks; // do not burst-render after a stalled frame
            }
        }
    }

    private PipelineConfiguration CaptureConfiguration()
    {
        lock (_configurationGate)
            return new PipelineConfiguration(_instrument, ReverbEnabled, _reverbStyle, _customImpulse,
                StringResonanceEnabled, _outputChoice, _latencyMilliseconds, _asioSystemHandle);
    }

    private void RequestRebuild(bool wait)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _rebuildVersion);
        if (wait)
        {
            _deviceControl.Invoke(() =>
            {
                if (Volatile.Read(ref _appliedRebuildVersion) != Volatile.Read(ref _rebuildVersion))
                    BuildLatestPipeline(throwOnFailure: true);
            });
            return;
        }

        if (Interlocked.CompareExchange(ref _rebuildPosted, 1, 0) != 0) return;
        _deviceControl.Post(() =>
        {
            var builtVersion = -1;
            try
            {
                var requestedVersion = Volatile.Read(ref _rebuildVersion);
                builtVersion = requestedVersion == Volatile.Read(ref _appliedRebuildVersion)
                    ? requestedVersion
                    : BuildLatestPipeline();
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _rebuildError, ex.Message);
                builtVersion = Volatile.Read(ref _rebuildVersion);
                try { OutputChanged?.Invoke(); } catch { }
            }
            finally { Interlocked.Exchange(ref _rebuildPosted, 0); }
            if (builtVersion >= 0 && builtVersion != Volatile.Read(ref _rebuildVersion)) RequestRebuild(wait: false);
        });
    }

    /// <summary>Runs only on the dedicated control STA; retries if settings changed while a driver was opening.</summary>
    private int BuildLatestPipeline(bool throwOnFailure = false)
    {
        while (Volatile.Read(ref _disposed) == 0)
        {
            var version = Volatile.Read(ref _rebuildVersion);
            var configuration = CaptureConfiguration();
            if (configuration.Instrument is null)
            {
                Volatile.Write(ref _appliedRebuildVersion, version);
                return version;
            }

            Pipeline next;
            try { next = CreatePipeline(configuration); }
            catch (Exception ex)
            {
                if (version != Volatile.Read(ref _rebuildVersion)) continue;
                Volatile.Write(ref _rebuildError, ex.Message);
                OutputChanged?.Invoke();
                if (throwOnFailure) throw;
                return version;
            }

            if (version != Volatile.Read(ref _rebuildVersion) || Volatile.Read(ref _disposed) != 0)
            {
                next.Dispose();
                continue;
            }

            // Most backends can overlap briefly, letting the replacement open before the old one stops.
            // ASIO drivers commonly permit only one client, so release an active ASIO stream before reopening
            // any output (and release any old endpoint before opening ASIO). The next pipeline is prepared
            // first, keeping slow synth/effect construction from creating an unnecessary silent gap.
            if (Volatile.Read(ref _pipeline)?.Sink is AsioAudioSink || configuration.OutputChoice.Kind == AudioBackendKind.Asio)
            {
                var exclusivePrevious = Interlocked.Exchange(ref _pipeline, null);
                try { _tapClockWake.Set(); } catch (ObjectDisposedException) { }
                exclusivePrevious?.Dispose(); // ASIO stop/dispose stays on the same STA that created the driver
                OutputChanged?.Invoke();
            }

            next.OpenOutput(configuration.OutputChoice, configuration.LatencyMilliseconds, configuration.AsioSystemHandle);
            if (version != Volatile.Read(ref _rebuildVersion) || Volatile.Read(ref _disposed) != 0)
            {
                next.Dispose();
                continue;
            }

            // A rebuild intentionally cuts currently sounding voices, as it did before, but never leaves a
            // queued old note-on to become a stuck note in the replacement synthesiser.
            ClearPendingCommands();
            var previous = Interlocked.Exchange(ref _pipeline, next);
            try { _tapClockWake.Set(); } catch (ObjectDisposedException) { }
            Volatile.Write(ref _rebuildError, null);
            Volatile.Write(ref _appliedRebuildVersion, version);
            OutputChanged?.Invoke();
            previous?.Dispose(); // ASIO stop/dispose stays on the same STA that created the driver
            return version;
        }
        return -1;
    }

    private Pipeline CreatePipeline(PipelineConfiguration configuration)
    {
        var synth = new SfzSynthesizer(configuration.Instrument!, SampleRate);
        return new Pipeline(this, synth, configuration.ReverbEnabled, configuration.ReverbStyle,
            configuration.CustomImpulse, configuration.ResonanceEnabled);
    }

    public void NoteOn(int pitch, int velocity = 96, int channel = 0) =>
        Enqueue(new AudioCommand(AudioCommandKind.NoteOn, channel & 15, Math.Clamp(pitch, 0, 127), Math.Clamp(velocity, 1, 127)));
    public void NoteOff(int pitch, int channel = 0) =>
        Enqueue(new AudioCommand(AudioCommandKind.NoteOff, channel & 15, Math.Clamp(pitch, 0, 127), 0));
    public void ProcessMidi(int channel, int command, int data1, int data2) =>
        Enqueue(new AudioCommand(AudioCommandKind.Midi, channel & 15, command & 0xF0, (data1 & 0xFF) | ((data2 & 0xFF) << 8)));
    public void ControlChange(int controller, int value, int channel = 0) => ProcessMidi(channel, 0xB0, controller, value);
    public void AllNotesOff() => Enqueue(new AudioCommand(AudioCommandKind.AllNotesOff, 0, 0, 0));

    private void Enqueue(AudioCommand command)
    {
        var pipeline = Volatile.Read(ref _pipeline);
        if (pipeline is null || (!pipeline.HasAudioOutput && !HasTap) || Volatile.Read(ref _disposed) != 0) return;
        _commands.Enqueue(command);
    }

    private void DrainCommands(IPianoSynthesizer synth)
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case AudioCommandKind.NoteOn: synth.NoteOn(command.Channel, command.Data1, command.Data2); break;
                case AudioCommandKind.NoteOff: synth.NoteOff(command.Channel, command.Data1); break;
                case AudioCommandKind.Midi:
                    synth.ProcessMidi(command.Channel, command.Data1, command.Data2 & 0xFF, (command.Data2 >> 8) & 0xFF);
                    break;
                case AudioCommandKind.AllNotesOff: synth.AllNotesOff(); break;
            }
        }
    }

    private void ClearPendingCommands() { while (_commands.TryDequeue(out _)) { } }

    public void SetTap(Action<short[], int>? tap)
    {
        _tapDispatcher.SetTap(tap);
        try { _tapClockWake.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Enables the fallback clock only for an active recording, never for diagnostic/manual taps.</summary>
    public void SetTapClockEnabled(bool enabled)
    {
        Volatile.Write(ref _tapClockEnabled, enabled ? 1 : 0);
        try { _tapClockWake.Set(); } catch (ObjectDisposedException) { }
    }

    public bool HasTap => _tapDispatcher.HasTap;
    public bool FlushTap() => _tapDispatcher.Flush(TimeSpan.FromSeconds(10));
    public bool PumpTapBlock() => Volatile.Read(ref _pipeline)?.RenderTapBlock() ?? false;

    public void UnloadInstrument()
    {
        lock (_configurationGate) _instrument = null;
        Interlocked.Increment(ref _rebuildVersion);
        ClearPendingCommands();
        var old = Interlocked.Exchange(ref _pipeline, null);
        try { _tapClockWake.Set(); } catch (ObjectDisposedException) { }
        _deviceControl.Post(() => old?.Dispose(), DispatcherPriority.Send);
        OutputChanged?.Invoke();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _tapClockWake.Set(); } catch (ObjectDisposedException) { }
        var tapClockStopped = _tapClockThread.Join(TimeSpan.FromSeconds(1));
        lock (_configurationGate) _instrument = null;
        Interlocked.Increment(ref _rebuildVersion);
        ClearPendingCommands();
        var pipeline = Interlocked.Exchange(ref _pipeline, null);
        try { _deviceControl.Invoke(() => pipeline?.Dispose()); } catch { /* a driver teardown error must not abort app shutdown */ }
        _tapDispatcher.SetTap(null);
        _tapDispatcher.Flush(TimeSpan.FromSeconds(10));
        _tapDispatcher.Dispose();
        _deviceControl.Dispose();
        if (tapClockStopped) _tapClockWake.Dispose();
    }

    /// <summary>
    /// Synthesiser, reverb and output sink of one loaded SFZ instrument, torn down and rebuilt as a
    /// unit when the instrument or the output backend changes. The sink pulls blocks through <see cref="Render"/> on its own
    /// audio thread; a machine with no usable device has no sink, keeps <see cref="OutputError"/> instead,
    /// and still renders for the recorder through <see cref="RenderTapBlock"/>.
    /// </summary>
    private sealed class Pipeline : IDisposable
    {
        private const int TapBlockFrames = 512;
        private readonly PianoAudioEngine _owner;
        private readonly IStereoReverb _reverb;
        private readonly StringResonance _resonance = new(SampleRate);
        private readonly float[] _tapSamples = new float[TapBlockFrames * 2];
        // Covers large WASAPI periods and the largest common ASIO blocks without a first-callback allocation.
        private readonly short[] _tapPcm = new short[32768];
        private bool _disposed;

        public IPianoSynthesizer Synth { get; }
        public IAudioSink? Sink { get; private set; }
        public string? OutputError { get; private set; }
        public string? FallbackNotice { get; private set; }
        internal bool HasAudioOutput => Sink is not null;

        public Pipeline(PianoAudioEngine owner, IPianoSynthesizer synth, bool reverbEnabled,
            PianoReverbStyle reverbStyle, WavData? customImpulse, bool resonanceEnabled)
        {
            _owner = owner;
            Synth = synth;
            _reverb = reverbStyle switch
            {
                PianoReverbStyle.ConcertHall => new ConvolutionReverb(ReverbImpulse.ConcertHall(SampleRate), SampleRate, wet: .32f),
                PianoReverbStyle.Soundboard => new ConvolutionReverb(ReverbImpulse.Soundboard(SampleRate), SampleRate, wet: .5f),
                PianoReverbStyle.CustomImpulse when customImpulse is not null => new ConvolutionReverb(customImpulse, SampleRate, wet: .35f),
                _ => new StereoHallReverb(SampleRate)
            };
            _reverb.Enabled = reverbEnabled;
            _resonance.Enabled = resonanceEnabled;
        }

        internal void OpenOutput(AudioOutputChoice choice, double latencyMilliseconds, IntPtr asioSystemHandle)
        {
            try
            {
                Sink = AudioSinkFactory.TryCreate(choice, latencyMilliseconds, Render, asioSystemHandle, out var failure);
                if (Sink is null) OutputError = failure; else FallbackNotice = failure;
            }
            catch (Exception ex)
            {
                OutputError = ex.Message;
            }
        }

        public void SetReverbEnabled(bool enabled) => _reverb.Enabled = enabled;
        public void SetResonanceEnabled(bool enabled) => _resonance.Enabled = enabled;

        /// <summary>One block for the device and, when attached, the recorder — called on the sink's thread.</summary>
        private void Render(float[] interleaved, int frames)
        {
            if (!ReferenceEquals(Volatile.Read(ref _owner._pipeline), this))
            {
                Array.Clear(interleaved, 0, frames * 2);
                return;
            }

            _owner.DrainCommands(Synth);
            Synth.Render(interleaved, frames);
            _resonance.Process(interleaved, frames, Synth.DamperLevel);
            _reverb.Process(interleaved, frames);
            var gain = Volatile.Read(ref _owner._masterGain);
            for (var i = 0; i < frames * 2; i++) interleaved[i] = LimitOutput(interleaved[i] * gain);

            if (!_owner._tapDispatcher.HasTap || frames * 2 > _tapPcm.Length) return;
            for (var i = 0; i < frames * 2; i++)
                _tapPcm[i] = (short)Math.Clamp((int)Math.Round(interleaved[i] * short.MaxValue), short.MinValue, short.MaxValue);
            _owner._tapDispatcher.Enqueue(_tapPcm, frames * 2);
        }

        /// <summary>Leaves ordinary samples linear; only the top few percent are smoothly limited.</summary>
        private static float LimitOutput(float sample)
        {
            if (!float.IsFinite(sample)) return 0;
            const float knee = .94f;
            var magnitude = Math.Abs(sample);
            if (magnitude <= knee) return sample;
            var limited = knee + (1 - knee) * (1 - MathF.Exp(-(magnitude - knee) / (1 - knee)));
            return MathF.CopySign(Math.Min(limited, 1f), sample);
        }

        internal bool RenderTapBlock()
        {
            if (!_owner.HasTap || !ReferenceEquals(Volatile.Read(ref _owner._pipeline), this)) return false;
            Render(_tapSamples, TapBlockFrames);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Sink?.Dispose();
        }
    }
}

/// <summary>The reverb choices the Settings page offers; styles other than the hall run the convolver.</summary>
internal enum PianoReverbStyle
{
    /// <summary>The classic algorithmic Schroeder hall — the default, unchanged sound.</summary>
    Hall,
    /// <summary>Convolution with the built-in generated concert-hall response.</summary>
    ConcertHall,
    /// <summary>Convolution with the short, close soundboard response — almost dry.</summary>
    Soundboard,
    /// <summary>Convolution with an impulse response the user loaded (WAV or FLAC).</summary>
    CustomImpulse,
}

/// <summary>Low-cost stereo Schroeder room reverb used after SFZ sample rendering.</summary>
internal sealed class StereoHallReverb : IStereoReverb
{
    private sealed class Comb
    {
        private readonly float[] _buffer;
        private int _position;
        private float _filter;
        public Comb(int delaySamples) => _buffer = new float[Math.Max(1, delaySamples)];
        public float Process(float input)
        {
            var delayed = _buffer[_position];
            _filter = delayed * .72f + _filter * .28f;
            _buffer[_position] = input + _filter * .78f;
            if (++_position == _buffer.Length) _position = 0;
            return delayed;
        }
        public void Reset() { Array.Clear(_buffer); _position = 0; _filter = 0; }
    }

    private sealed class AllPass
    {
        private readonly float[] _buffer;
        private int _position;
        public AllPass(int delaySamples) => _buffer = new float[Math.Max(1, delaySamples)];
        public float Process(float input)
        {
            var delayed = _buffer[_position];
            var output = delayed - input * .5f;
            _buffer[_position] = input + delayed * .5f;
            if (++_position == _buffer.Length) _position = 0;
            return output;
        }
        public void Reset() { Array.Clear(_buffer); _position = 0; }
    }

    private readonly Comb[] _leftCombs;
    private readonly Comb[] _rightCombs;
    private readonly AllPass[] _leftDiffusers;
    private readonly AllPass[] _rightDiffusers;
    private double _fade;
    private bool _wetActive;
    private readonly double _fadeStep;
    private volatile bool _enabled = true;
    public bool Enabled { get => _enabled; set => _enabled = value; }

    public StereoHallReverb(int sampleRate)
    {
        _fadeStep = 1 / (sampleRate * 0.12);
        int Samples(double milliseconds) => Math.Max(1, (int)Math.Round(sampleRate * milliseconds / 1000));
        _leftCombs = new double[] { 29.7, 37.1, 41.1, 43.7 }.Select(ms => new Comb(Samples(ms))).ToArray();
        _rightCombs = new double[] { 31.1, 36.7, 40.3, 44.1 }.Select(ms => new Comb(Samples(ms))).ToArray();
        _leftDiffusers = [new AllPass(Samples(5.1)), new AllPass(Samples(1.7))];
        _rightDiffusers = [new AllPass(Samples(5.5)), new AllPass(Samples(1.9))];
    }

    /// <summary>The float path every live backend uses; output stays within [-1, 1].</summary>
    public void Process(float[] interleaved, int frames)
    {
        if (!BeginBlock()) return;
        for (var i = 0; i < frames; i++)
        {
            var left = interleaved[i * 2]; var right = interleaved[i * 2 + 1];
            ProcessFrame(ref left, ref right);
            interleaved[i * 2] = Math.Clamp(left, -1f, 1f);
            interleaved[i * 2 + 1] = Math.Clamp(right, -1f, 1f);
        }
        EndBlock();
    }

    /// <summary>The 16-bit path, kept for the tests that feed PCM impulses; same math as the float path.</summary>
    public void Process(short[] interleaved, int frames)
    {
        if (!BeginBlock()) return;
        for (var i = 0; i < frames; i++)
        {
            var left = interleaved[i * 2] / 32768f; var right = interleaved[i * 2 + 1] / 32768f;
            ProcessFrame(ref left, ref right);
            interleaved[i * 2] = ToPcm(left);
            interleaved[i * 2 + 1] = ToPcm(right);
        }
        EndBlock();
    }

    /// <summary>
    /// Bypass leaves the dry signal untouched. A tail that is still ringing fades out over ~120 ms instead
    /// of cutting abruptly; once silent, the delay lines are cleared once (<see cref="EndBlock"/>).
    /// </summary>
    private bool BeginBlock()
    {
        if (!Enabled) return _wetActive;
        _fade = 1; _wetActive = true;
        return true;
    }

    private void EndBlock()
    {
        if (!Enabled && _fade == 0) { Reset(); _wetActive = false; }
    }

    private void ProcessFrame(ref float dryLeft, ref float dryRight)
    {
        // While fading out the delay lines keep ringing but no new energy is fed in.
        var sendLeft = Enabled ? dryLeft + dryRight * .16f : 0f;
        var sendRight = Enabled ? dryRight + dryLeft * .16f : 0f;
        var wetLeft = 0f; var wetRight = 0f;
        foreach (var comb in _leftCombs) wetLeft += comb.Process(sendLeft);
        foreach (var comb in _rightCombs) wetRight += comb.Process(sendRight);
        wetLeft *= .25f * (float)_fade; wetRight *= .25f * (float)_fade;
        foreach (var diffuser in _leftDiffusers) wetLeft = diffuser.Process(wetLeft);
        foreach (var diffuser in _rightDiffusers) wetRight = diffuser.Process(wetRight);
        // The enabled path keeps dry at .91 to leave headroom for the wet sum; while the tail
        // fades, ramp dry back to unity so bypassing never clicks.
        var dryGain = Enabled ? .91f : .91f + .09f * (1 - (float)_fade);
        dryLeft = dryLeft * dryGain + wetLeft * .38f;
        dryRight = dryRight * dryGain + wetRight * .38f;
        if (!Enabled) _fade = Math.Max(0, _fade - _fadeStep);
    }

    private void Reset()
    {
        foreach (var comb in _leftCombs) comb.Reset(); foreach (var comb in _rightCombs) comb.Reset();
        foreach (var diffuser in _leftDiffusers) diffuser.Reset(); foreach (var diffuser in _rightDiffusers) diffuser.Reset();
    }
    private static short ToPcm(float sample) => (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
}
