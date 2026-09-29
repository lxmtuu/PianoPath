using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PianoPath;

/// <summary>Streams SoundFont 2 PCM to the Windows audio device. No SoundFont means no synthesized audio.</summary>
internal sealed class PianoAudioEngine : IDisposable
{
    private AudioPump? _pump;
    private int _reverbEnabled = 1;
    public bool HasSoundFont => Volatile.Read(ref _pump) is not null;
    public string? LoadedName => Volatile.Read(ref _pump)?.Font.Name;
    public IReadOnlyList<SoundFontPreset> Presets => Volatile.Read(ref _pump)?.Font.Presets ?? [];
    /// <summary>True while the loaded SoundFont is actually streaming to a Windows audio device.</summary>
    public bool HasAudioOutput => Volatile.Read(ref _pump)?.HasAudioOutput ?? false;
    /// <summary>Why the speakers are silent although a SoundFont is loaded; <c>null</c> when audio output works.</summary>
    public string? PlaybackError => Volatile.Read(ref _pump)?.OutputError;
    public bool ReverbEnabled
    {
        get => Volatile.Read(ref _reverbEnabled) != 0;
        set
        {
            Volatile.Write(ref _reverbEnabled, value ? 1 : 0);
            Volatile.Read(ref _pump)?.SetReverbEnabled(value);
        }
    }

    public void LoadSoundFont(string path)
    {
        var font = SoundFontReader.Read(path);
        var synth = new SoundFontSynthesizer(font);
        var next = new AudioPump(synth, ReverbEnabled);
        next.Start();
        Interlocked.Exchange(ref _pump, next)?.Dispose();
    }
    public void SelectPreset(int bank, int program) => Volatile.Read(ref _pump)?.Synth.SelectPreset(bank, program);
    public void NoteOn(int pitch, int velocity = 96, int channel = 0) => Volatile.Read(ref _pump)?.Synth.NoteOn(channel, Math.Clamp(pitch, 0, 127), Math.Clamp(velocity, 1, 127));
    public void NoteOff(int pitch, int channel = 0) => Volatile.Read(ref _pump)?.Synth.NoteOff(channel, Math.Clamp(pitch, 0, 127));
    public void ProcessMidi(int channel, int command, int data1, int data2) => Volatile.Read(ref _pump)?.Synth.ProcessMidi(channel, command, data1, data2);
    public void ControlChange(int controller, int value, int channel = 0) => ProcessMidi(channel, 0xB0, controller, value);
    public void AllNotesOff() => Volatile.Read(ref _pump)?.Synth.AllNotesOff();
    public void UnloadSoundFont() => Interlocked.Exchange(ref _pump, null)?.Dispose();
    public void Dispose() => UnloadSoundFont();

    private sealed class AudioPump : IDisposable
    {
        private const int SampleRate = 44100;
        private const int FramesPerBuffer = 512;
        private const int BufferCount = 4;
        private const uint WaveMapper = 0xFFFFFFFF;
        private const uint CallbackEvent = 0x00050000;
        private const uint HeaderDone = 0x00000001;
        private const ushort WaveFormatPcm = 1;
        private const int BytesPerBuffer = FramesPerBuffer * 4;
        private readonly AutoResetEvent _ready = new(false);
        private readonly List<AudioBuffer> _buffers = [];
        private readonly short[] _pcm = new short[FramesPerBuffer * 2];
        private readonly StereoHallReverb _reverb = new(SampleRate);
        private Thread? _thread;
        private IntPtr _device;
        private volatile bool _stopping;
        private bool _disposed;
        public SoundFontSynthesizer Synth { get; }
        public SoundFontData Font => Synth.Font;
        /// <summary>Reason the pump is silent, or <c>null</c> when waveOut accepted the stream.</summary>
        public string? OutputError { get; private set; }
        public bool HasAudioOutput => _device != IntPtr.Zero;

        public AudioPump(SoundFontSynthesizer synth, bool reverbEnabled) { Synth = synth; _reverb.Enabled = reverbEnabled; }
        public void SetReverbEnabled(bool enabled) => _reverb.Enabled = enabled;

        public void Start()
        {
            var format = new WaveFormatEx { FormatTag = WaveFormatPcm, Channels = 2, SamplesPerSec = SampleRate, BitsPerSample = 16, BlockAlign = 4, AverageBytesPerSecond = SampleRate * 4 };
            var eventHandle = _ready.SafeWaitHandle.DangerousGetHandle();
            var result = WaveOutOpen(out _device, WaveMapper, ref format, eventHandle, IntPtr.Zero, CallbackEvent);
            if (result != 0)
            {
                // A machine with no usable playback device (VM, headless CI runner, a sound card claimed
                // exclusively by another app) must not stop the SoundFont from loading: the stage, the
                // practice scoring and MIDI keep working and only the speakers stay silent.
                _device = IntPtr.Zero;
                OutputError = result == 2
                    ? Loc.T("Windows reported no audio output device (waveOut error 2).")
                    : Loc.F("Windows could not open the audio output (waveOut error {0}).", result);
                return;
            }
            try
            {
                for (var i = 0; i < BufferCount; i++)
                {
                    var buffer = new AudioBuffer { Data = new byte[BytesPerBuffer] };
                    buffer.DataPin = GCHandle.Alloc(buffer.Data, GCHandleType.Pinned);
                    buffer.Header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
                    _buffers.Add(buffer);
                    Marshal.StructureToPtr(new WaveHeader { Data = buffer.DataPin.AddrOfPinnedObject(), BufferLength = (uint)buffer.Data.Length }, buffer.Header, false);
                    Check(WaveOutPrepareHeader(_device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()), "prepare audio buffer");
                    RenderBuffer(buffer);
                    Check(WaveOutWrite(_device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()), "queue audio buffer");
                }
                // Render ahead of the UI thread so busy frames do not turn into audio drop-outs.
                _thread = new Thread(Pump) { IsBackground = true, Name = "Keyflow SoundFont audio", Priority = ThreadPriority.Highest };
                _thread.Start();
            }
            // The device opened but rejected the buffers: release it and keep synthesizing silently
            // instead of failing the whole SoundFont load.
            catch (Exception ex) { OutputError = Loc.F("Windows rejected the audio buffers: {0}", ex.Message); CloseDevice(); }
        }

        private void Pump()
        {
            while (!_stopping)
            {
                _ready.WaitOne(50);
                if (_stopping) break;
                foreach (var buffer in _buffers)
                {
                    var header = Marshal.PtrToStructure<WaveHeader>(buffer.Header);
                    if ((header.Flags & HeaderDone) == 0) continue;
                    try
                    {
                        RenderBuffer(buffer);
                        Check(WaveOutWrite(_device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()), "queue audio buffer");
                    }
                    catch (Exception ex) { OutputError = Loc.F("Windows stopped accepting audio buffers: {0}", ex.Message); _stopping = true; break; }
                }
            }
        }

        private void RenderBuffer(AudioBuffer buffer)
        {
            Synth.Render(_pcm, FramesPerBuffer);
            _reverb.Process(_pcm, FramesPerBuffer);
            Buffer.BlockCopy(_pcm, 0, buffer.Data, 0, BytesPerBuffer);
        }

        private void Check(uint result, string action)
        {
            if (result != 0) throw new InvalidOperationException(Loc.F("Could not {0} (waveOut error {1}).", Loc.T(action), result));
        }

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            CloseDevice();
            _ready.Dispose();
        }

        /// <summary>Tears the waveOut stream down but keeps the pump alive, so the synthesizer can keep running silently.</summary>
        private void CloseDevice()
        {
            _stopping = true;
            if (_device != IntPtr.Zero) WaveOutReset(_device);
            _ready.Set(); _thread?.Join(1000); _thread = null;
            foreach (var buffer in _buffers)
            {
                if (_device != IntPtr.Zero) WaveOutUnprepareHeader(_device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>());
                if (buffer.Header != IntPtr.Zero) Marshal.FreeHGlobal(buffer.Header);
                if (buffer.DataPin.IsAllocated) buffer.DataPin.Free();
            }
            _buffers.Clear();
            if (_device != IntPtr.Zero) { WaveOutClose(_device); _device = IntPtr.Zero; }
            _stopping = false;
        }
    }

    private sealed class AudioBuffer { public required byte[] Data; public GCHandle DataPin; public IntPtr Header; }
    [StructLayout(LayoutKind.Sequential)] private struct WaveFormatEx
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength, BytesRecorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }
    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")] private static extern uint WaveOutOpen(out IntPtr device, uint deviceId, ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")] private static extern uint WaveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")] private static extern uint WaveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")] private static extern uint WaveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll", EntryPoint = "waveOutReset")] private static extern uint WaveOutReset(IntPtr device);
    [DllImport("winmm.dll", EntryPoint = "waveOutClose")] private static extern uint WaveOutClose(IntPtr device);
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

    public void Process(short[] interleaved, int frames)
    {
        if (!Enabled)
        {
            // Bypass leaves the dry signal untouched. A tail that is still ringing fades out over
            // ~120 ms instead of cutting abruptly; once silent, the delay lines are cleared once.
            if (!_wetActive) return;
        }
        else { _fade = 1; _wetActive = true; }
        for (var i = 0; i < frames; i++)
        {
            var offset = i * 2;
            var dryLeft = interleaved[offset] / 32768f;
            var dryRight = interleaved[offset + 1] / 32768f;
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
            interleaved[offset] = ToPcm(dryLeft * dryGain + wetLeft * .38f);
            interleaved[offset + 1] = ToPcm(dryRight * dryGain + wetRight * .38f);
            if (!Enabled) _fade = Math.Max(0, _fade - _fadeStep);
        }
        if (!Enabled && _fade == 0) { Reset(); _wetActive = false; }
    }

    private void Reset()
    {
        foreach (var comb in _leftCombs) comb.Reset(); foreach (var comb in _rightCombs) comb.Reset();
        foreach (var diffuser in _leftDiffusers) diffuser.Reset(); foreach (var diffuser in _rightDiffusers) diffuser.Reset();
    }
    private static short ToPcm(float sample) => (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
}
