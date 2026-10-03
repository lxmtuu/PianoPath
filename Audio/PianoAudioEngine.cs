namespace PianoPath;

/// <summary>
/// Streams piano audio to Windows through whichever backend the player chose — WASAPI shared or
/// exclusive, an ASIO driver, or the old waveOut queue (<see cref="AudioSinkFactory"/> holds the fallback
/// ladder). Two sample engines plug into the same pipeline: SoundFont 2 banks (the bundled Yamaha grand)
/// and SFZ instruments (the downloadable Steinway sets); <see cref="LoadInstrument"/> routes by file
/// extension. No instrument means no synthesized audio. The whole pipeline mixes in 32-bit float at the
/// fixed <see cref="SampleRate"/>; only the recording tap still speaks 16-bit PCM, because that is what
/// the WAV and MP4 writers store.
/// </summary>
internal sealed class PianoAudioEngine : IDisposable
{
    /// <summary>
    /// The one sample rate of the whole audio path — synthesiser, reverb, every output backend, and the
    /// recording writers (<see cref="Mp4Recorder.SampleRate"/> matches it). WASAPI shared lets Windows
    /// resample to the device's mix rate; the exclusive and ASIO paths open the device at this rate.
    /// </summary>
    public const int SampleRate = 44100;

    private Pipeline? _pipeline;
    /// <summary>What is loaded right now — a <see cref="SoundFontData"/> or an <see cref="SfzInstrumentData"/> —
    /// kept so an output switch can rebuild the pipeline around the same instrument.</summary>
    private object? _instrument;
    private int _reverbEnabled = 1;
    private int _resonanceEnabled = 1;
    private AudioOutputChoice _outputChoice = AudioOutputChoice.Auto;
    private double _latencyMilliseconds = 10;
    private (int Bank, int Program)? _selectedPreset;

    public bool HasSoundFont => Volatile.Read(ref _pipeline) is not null;
    public string? LoadedName => Volatile.Read(ref _pipeline)?.Synth.Name;
    /// <summary>SF2 presets of the loaded bank; an SFZ instrument is one patch and lists none.</summary>
    public IReadOnlyList<SoundFontPreset> Presets => (Volatile.Read(ref _pipeline)?.Synth as SoundFontSynthesizer)?.Font.Presets ?? [];
    /// <summary>True while the loaded instrument is an SFZ set rather than a SoundFont bank.</summary>
    public bool IsSfzInstrument => Volatile.Read(ref _pipeline)?.Synth is SfzSynthesizer;
    /// <summary>True while the loaded SoundFont is actually streaming to a Windows audio device.</summary>
    public bool HasAudioOutput => Volatile.Read(ref _pipeline)?.Sink is not null;
    /// <summary>Why the speakers are silent although a SoundFont is loaded; <c>null</c> when audio output works.</summary>
    public string? PlaybackError => Volatile.Read(ref _pipeline)?.OutputError;
    /// <summary>The live path ("WASAPI shared · 10.7 ms", "ASIO · Focusrite USB · 2.9 ms"), or <c>null</c> while silent.</summary>
    public string? OutputDescription => Volatile.Read(ref _pipeline)?.Sink?.Description;
    /// <summary>Why the <em>requested</em> backend was not used although a fallback is audible; usually <c>null</c>.</summary>
    public string? OutputFallbackNotice => Volatile.Read(ref _pipeline) is { Sink: not null } pipeline ? pipeline.FallbackNotice : null;
    /// <summary>True while the audible path is an ASIO driver, which is when its control panel can be shown.</summary>
    public bool IsAsioOutput => Volatile.Read(ref _pipeline)?.Sink is AsioAudioSink;

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

    /// <summary>
    /// Remembers the output request — backend plus the WASAPI latency target — and rebuilds the live
    /// pipeline around it when a SoundFont is already playing. Held notes are cut by the rebuild (the synth
    /// state lives beside the device buffers); the caller re-applies pedals, which is all a player can hold
    /// across an output switch anyway.
    /// </summary>
    public void ConfigureOutput(AudioOutputChoice choice, double latencyMilliseconds)
    {
        _outputChoice = choice;
        _latencyMilliseconds = Math.Clamp(latencyMilliseconds, 2, 50);
        if (Volatile.Read(ref _pipeline) is not null) Rebuild();
    }

    /// <summary>Opens the active ASIO driver's own settings window; a no-op on every other backend.</summary>
    public void ShowAsioControlPanel() => (Volatile.Read(ref _pipeline)?.Sink as AsioAudioSink)?.ShowControlPanel();

    /// <summary>The historical name; routes through <see cref="LoadInstrument"/> and accepts both formats.</summary>
    public void LoadSoundFont(string path) => LoadInstrument(path);

    /// <summary>Loads a piano by file type: <c>.sfz</c> builds the SFZ engine, anything else is read as SF2.</summary>
    public void LoadInstrument(string path)
    {
        _instrument = System.IO.Path.GetExtension(path).Equals(".sfz", StringComparison.OrdinalIgnoreCase)
            ? SfzReader.Read(path)
            : SoundFontReader.Read(path);
        Rebuild();
    }

    private void Rebuild()
    {
        if (_instrument is null) return;
        // The branches share no base class, so the first is cast to the interface for the conditional.
        var synth = _instrument is SfzInstrumentData sfz
            ? (IPianoSynthesizer)new SfzSynthesizer(sfz, SampleRate)
            : new SoundFontSynthesizer((SoundFontData)_instrument, SampleRate);
        if (synth is SoundFontSynthesizer sf2 && _selectedPreset is { } preset) sf2.SelectPreset(preset.Bank, preset.Program);
        var next = new Pipeline(synth, ReverbEnabled, StringResonanceEnabled, _outputChoice, _latencyMilliseconds);
        Interlocked.Exchange(ref _pipeline, next)?.Dispose();
    }

    public void SelectPreset(int bank, int program)
    {
        _selectedPreset = (bank, program);
        (Volatile.Read(ref _pipeline)?.Synth as SoundFontSynthesizer)?.SelectPreset(bank, program);
    }

    public void NoteOn(int pitch, int velocity = 96, int channel = 0) => Volatile.Read(ref _pipeline)?.Synth.NoteOn(channel, Math.Clamp(pitch, 0, 127), Math.Clamp(velocity, 1, 127));
    public void NoteOff(int pitch, int channel = 0) => Volatile.Read(ref _pipeline)?.Synth.NoteOff(channel, Math.Clamp(pitch, 0, 127));
    public void ProcessMidi(int channel, int command, int data1, int data2) => Volatile.Read(ref _pipeline)?.Synth.ProcessMidi(channel, command, data1, data2);
    public void ControlChange(int controller, int value, int channel = 0) => ProcessMidi(channel, 0xB0, controller, value);
    public void AllNotesOff() => Volatile.Read(ref _pipeline)?.Synth.AllNotesOff();

    /// <summary>
    /// Attaches a receiver for every rendered block — <c>(pcm, sampleCount)</c>, called on the audio thread —
    /// or <c>null</c> to detach. The recorder is the only caller: it writes the blocks into the WAV beside the
    /// video, so the file holds exactly what the machine played.
    /// </summary>
    public void SetTap(Action<short[], int>? tap) => Volatile.Read(ref _pipeline)?.SetTap(tap);

    /// <summary>True while a tap is attached; the recorder asks before pretending to capture audio.</summary>
    public bool HasTap => Volatile.Read(ref _pipeline)?.HasTap ?? false;

    /// <summary>
    /// Renders one block through the synthesiser and hands it to the tap without touching the sound device.
    /// The recorder drives this on machines with no audio output, so a silent session still records the piano;
    /// it returns false when nothing is tapped or no SoundFont is loaded.
    /// </summary>
    public bool PumpTapBlock() => Volatile.Read(ref _pipeline)?.RenderTapBlock() ?? false;
    public void UnloadSoundFont() { _instrument = null; Interlocked.Exchange(ref _pipeline, null)?.Dispose(); }
    public void Dispose() => UnloadSoundFont();

    /// <summary>
    /// Synthesiser, reverb and output sink of one loaded instrument (SF2 or SFZ), torn down and rebuilt as a
    /// unit when the instrument or the output backend changes. The sink pulls blocks through <see cref="Render"/> on its own
    /// audio thread; a machine with no usable device has no sink, keeps <see cref="OutputError"/> instead,
    /// and still renders for the recorder through <see cref="RenderTapBlock"/>.
    /// </summary>
    private sealed class Pipeline : IDisposable
    {
        /// <summary>Frames rendered per tap-only block; matches the recording clock's accounting.</summary>
        private const int TapBlockFrames = 512;
        private readonly StereoHallReverb _reverb = new(SampleRate);
        private readonly StringResonance _resonance = new(SampleRate);
        private readonly float[] _tapSamples = new float[TapBlockFrames * 2];
        private short[] _tapPcm = new short[TapBlockFrames * 2];
        private Action<short[], int>? _tap;
        private bool _disposed;

        public IPianoSynthesizer Synth { get; }
        public IAudioSink? Sink { get; }
        /// <summary>Reason the pipeline is silent, or <c>null</c> when a sink accepted the stream.</summary>
        public string? OutputError { get; }
        /// <summary>The explicit backend request's failure when a fallback sink answered instead.</summary>
        public string? FallbackNotice { get; }

        public Pipeline(IPianoSynthesizer synth, bool reverbEnabled, bool resonanceEnabled, AudioOutputChoice choice, double latencyMilliseconds)
        {
            Synth = synth;
            _reverb.Enabled = reverbEnabled;
            _resonance.Enabled = resonanceEnabled;
            Sink = AudioSinkFactory.TryCreate(choice, latencyMilliseconds, Render, out var failure);
            if (Sink is null) OutputError = failure; else FallbackNotice = failure;
        }

        public void SetReverbEnabled(bool enabled) => _reverb.Enabled = enabled;
        public void SetResonanceEnabled(bool enabled) => _resonance.Enabled = enabled;
        internal bool HasTap => Volatile.Read(ref _tap) is not null;
        internal void SetTap(Action<short[], int>? tap) => Volatile.Write(ref _tap, tap);

        /// <summary>One block for the device and, when attached, the recorder — called on the sink's thread.</summary>
        private void Render(float[] interleaved, int frames)
        {
            Synth.Render(interleaved, frames);
            // The open strings answer the dry piano before the room does, like in the instrument itself.
            _resonance.Process(interleaved, frames, Synth.DamperLevel);
            _reverb.Process(interleaved, frames);
            // The block the device is about to play is also the block the recording keeps, so the WAV cannot
            // drift away from what was heard. The tap runs on this thread and must be quick; the recorder locks.
            if (Volatile.Read(ref _tap) is not { } tap) return;
            if (_tapPcm.Length < frames * 2) _tapPcm = new short[frames * 2];
            var pcm = _tapPcm;
            for (var i = 0; i < frames * 2; i++) pcm[i] = (short)Math.Clamp((int)Math.Round(interleaved[i] * short.MaxValue), short.MinValue, short.MaxValue);
            tap(pcm, frames * 2);
        }

        /// <summary>
        /// Renders one block into the tap only: the synthesiser, the reverb and the receiver, with no device
        /// buffer involved. Whether the machine has an output device makes no difference to what is captured.
        /// </summary>
        internal bool RenderTapBlock()
        {
            if (Volatile.Read(ref _tap) is null) return false;
            Render(_tapSamples, TapBlockFrames);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Sink?.Dispose();
        }
    }
}

/// <summary>Low-cost stereo Schroeder room reverb used after SoundFont sample rendering.</summary>
internal sealed class StereoHallReverb
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
    public volatile bool Enabled = true;

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
