using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>
/// The WinMM compatibility path — the output Keyflow shipped with before WASAPI and ASIO existed here.
/// Four 512-frame buffers (~46 ms of queue, plus whatever the driver adds) make it the slowest backend by
/// far, but <c>waveOut</c> has opened on every Windows since 3.1, including terminal sessions and VMs whose
/// audio stacks confuse newer APIs; it is therefore the last rung of the factory's fallback ladder rather
/// than a path anyone picks for live playing.
/// </summary>
internal sealed class WaveOutAudioSink : IAudioSink
{
    private const int FramesPerBuffer = 512;
    private const int BufferCount = 4;
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint CallbackEvent = 0x00050000;
    private const uint HeaderDone = 0x00000001;
    private const ushort WaveFormatPcm = 1;
    private const int BytesPerBuffer = FramesPerBuffer * 4;

    private readonly AudioRenderCallback _render;
    private readonly AutoResetEvent _ready = new(false);
    private readonly List<AudioBuffer> _buffers = [];
    private readonly float[] _samples = new float[FramesPerBuffer * 2];
    private readonly short[] _pcm = new short[FramesPerBuffer * 2];
    private Thread? _thread;
    private IntPtr _device;
    private volatile bool _stopping;
    private bool _disposed;

    public string Description => Loc.F("waveOut · {0:0.#} ms", BufferCount * FramesPerBuffer * 1000.0 / PianoAudioEngine.SampleRate);
    public double BufferMilliseconds => BufferCount * FramesPerBuffer * 1000.0 / PianoAudioEngine.SampleRate;

    private WaveOutAudioSink(AudioRenderCallback render) => _render = render;

    /// <summary>Opens the default waveOut device, or returns <c>null</c> and why — never throws.</summary>
    internal static WaveOutAudioSink? TryCreate(AudioRenderCallback render, out string? failure)
    {
        var sink = new WaveOutAudioSink(render);
        var format = new WaveFormatEx { FormatTag = WaveFormatPcm, Channels = 2, SamplesPerSec = PianoAudioEngine.SampleRate, BitsPerSample = 16, BlockAlign = 4, AverageBytesPerSecond = PianoAudioEngine.SampleRate * 4 };
        var result = WaveOutOpen(out sink._device, WaveMapper, ref format, sink._ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
        if (result != 0)
        {
            // A machine with no usable playback device (VM, headless CI runner, a sound card claimed
            // exclusively by another app) must not stop the SoundFont from loading: the stage, the
            // practice scoring and MIDI keep working and only the speakers stay silent.
            sink._device = IntPtr.Zero;
            failure = result == 2
                ? Loc.T("Windows reported no audio output device (waveOut error 2).")
                : Loc.F("Windows could not open the audio output (waveOut error {0}).", result);
            sink.Dispose();
            return null;
        }
        try
        {
            for (var i = 0; i < BufferCount; i++)
            {
                var buffer = new AudioBuffer { Data = new byte[BytesPerBuffer] };
                buffer.DataPin = GCHandle.Alloc(buffer.Data, GCHandleType.Pinned);
                buffer.Header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
                sink._buffers.Add(buffer);
                Marshal.StructureToPtr(new WaveHeader { Data = buffer.DataPin.AddrOfPinnedObject(), BufferLength = (uint)buffer.Data.Length }, buffer.Header, false);
                Check(WaveOutPrepareHeader(sink._device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()), "prepare audio buffer");
                sink.RenderBuffer(buffer);
                Check(WaveOutWrite(sink._device, buffer.Header, (uint)Marshal.SizeOf<WaveHeader>()), "queue audio buffer");
            }
            // Render ahead of the UI thread so busy frames do not turn into audio drop-outs.
            sink._thread = new Thread(sink.Pump) { IsBackground = true, Name = "Keyflow waveOut audio", Priority = ThreadPriority.Highest };
            sink._thread.Start();
            failure = null;
            return sink;
        }
        // The device opened but rejected the buffers: release it and report, instead of failing the
        // whole SoundFont load.
        catch (Exception ex)
        {
            failure = Loc.F("Windows rejected the audio buffers: {0}", ex.Message);
            sink.Dispose();
            return null;
        }
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
                // A device yanked mid-stream goes quiet; picking an output again rebuilds the sink.
                catch (Exception) { _stopping = true; break; }
            }
        }
    }

    private void RenderBuffer(AudioBuffer buffer)
    {
        _render(_samples, FramesPerBuffer);
        for (var i = 0; i < _pcm.Length; i++) _pcm[i] = (short)Math.Clamp((int)Math.Round(_samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
        Buffer.BlockCopy(_pcm, 0, buffer.Data, 0, BytesPerBuffer);
    }

    private static void Check(uint result, string action)
    {
        if (result != 0) throw new InvalidOperationException(Loc.F("Could not {0} (waveOut error {1}).", Loc.T(action), result));
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
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
        _ready.Dispose();
    }

    private sealed class AudioBuffer { public required byte[] Data; public GCHandle DataPin; public IntPtr Header; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
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
