using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>
/// The modern Windows audio path (Vista+), event-driven, replacing the ~50–90 ms a four-buffer waveOut
/// queue costs with single-digit milliseconds. Two flavours share this class:
///
/// <para>
/// <b>Shared</b> opens the default device at the engine's own rate with
/// <c>AUDCLNT_STREAMFLAGS_AUTO_CONVERT_PCM</c>, so the Windows mixer resamples to whatever the device runs
/// at — every machine accepts it, and the buffer is whatever the engine asked for (bounded below by the
/// device period, ~10 ms on stock Windows). <b>Exclusive</b> bypasses the mixer entirely and opens the
/// device at 44.1 kHz/16-bit directly; it is refused when another app holds the device, when the device
/// cannot run 44.1 kHz natively, or when the user disabled exclusive mode in Windows sound settings —
/// each of those refusals is reported in words, and the factory then falls back to the shared path.
/// </para>
///
/// <para>
/// Everything COM — enumerator, device, client, render client — is created <em>on the render thread</em>
/// and never leaves it, so no call ever marshals across an apartment while audio is running. The
/// constructor-side <see cref="TryCreate"/> merely waits for that thread to report "streaming" or a
/// localized reason why not.
/// </para>
/// </summary>
internal sealed class WasapiAudioSink : IAudioSink
{
    private readonly AudioRenderCallback _render;
    private readonly bool _exclusive;
    private readonly double _requestedMilliseconds;
    private readonly string _endpointId;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly AutoResetEvent _deviceEvent = new(false);
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile string? _initError;
    private double _bufferMilliseconds;
    private int _disposed, _eventsDisposed;

    public string Description => _exclusive
        ? Loc.F("WASAPI exclusive · {0:0.#} ms", _bufferMilliseconds)
        : Loc.F("WASAPI shared · {0:0.#} ms", _bufferMilliseconds);
    public double BufferMilliseconds => _bufferMilliseconds;

    private WasapiAudioSink(bool exclusive, double latencyMilliseconds, string? endpointId, AudioRenderCallback render)
    {
        _exclusive = exclusive;
        _requestedMilliseconds = Math.Clamp(latencyMilliseconds, 2, 50);
        _endpointId = endpointId ?? "";
        _render = render;
    }

    /// <summary>Returns a streaming sink for the selected (or default) render endpoint, or a reason it refused.</summary>
    internal static WasapiAudioSink? TryCreate(bool exclusive, double latencyMilliseconds, AudioRenderCallback render, out string? failure, string? endpointId = null)
    {
        var sink = new WasapiAudioSink(exclusive, latencyMilliseconds, endpointId, render);
        sink._thread = new Thread(sink.Pump) { IsBackground = true, Name = "Keyflow WASAPI audio", Priority = ThreadPriority.Highest };
        sink._thread.Start();
        // The thread opens the device in well under a second on any healthy machine; five is paranoia
        // against a driver stuck in a power transition, after which the sink is abandoned as failed.
        if (!sink._started.Wait(5000)) sink._initError ??= Loc.T("The audio device did not answer while opening the output stream.");
        if (sink._initError is { } error) { failure = error; sink.Dispose(); return null; }
        failure = null;
        return sink;
    }

    /// <summary>The render thread: opens the whole COM stack, streams until stopped, tears it down.</summary>
    private void Pump()
    {
        IAudioClient? client = null;
        object? enumeratorObject = null, deviceObject = null, renderObject = null;
        var avrtHandle = IntPtr.Zero;
        try
        {
            var enumerator = (IMMDeviceEnumerator)(enumeratorObject = new MMDeviceEnumeratorComObject());
            IMMDevice? device;
            int hr;
            if (_endpointId.Length == 0)
            {
                hr = enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleConsole, out device);
                if (hr != 0 || device is null) { _initError = Loc.T("Windows reported no default audio output device."); return; }
            }
            else
            {
                hr = enumerator.GetDevice(_endpointId, out var devicePointer);
                if (hr != 0 || devicePointer == IntPtr.Zero) { _initError = Loc.T("Windows could not find the selected audio output device."); return; }
                try { device = (IMMDevice)Marshal.GetObjectForIUnknown(devicePointer); }
                finally { Marshal.Release(devicePointer); }
            }
            deviceObject = device;

            var clientId = IidAudioClient;
            hr = device.Activate(ref clientId, ClsCtxAll, IntPtr.Zero, out var clientUnknown);
            if (hr != 0 || clientUnknown is null) { _initError = Loc.F("WASAPI could not open the default audio device (0x{0:X8}).", hr); return; }
            client = (IAudioClient)clientUnknown;

            var format = PcmFormat(_exclusive);
            var duration = (long)(_requestedMilliseconds * 10_000); // 100-ns units
            if (_exclusive)
            {
                // Exclusive event-driven requires period == buffer; an unaligned size is answered with a
                // corrected frame count that must be retried on a freshly activated client.
                hr = client.Initialize(ShareModeExclusive, StreamFlagsEventCallback, duration, duration, ref format, ref _session);
                if (hr == unchecked((int)0x88890019) /* AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED */)
                {
                    client.GetBufferSize(out var alignedFrames);
                    Marshal.ReleaseComObject(client); client = null;
                    duration = (long)Math.Round(10_000_000.0 * alignedFrames / PianoAudioEngine.SampleRate);
                    hr = device.Activate(ref clientId, ClsCtxAll, IntPtr.Zero, out clientUnknown);
                    if (hr != 0 || clientUnknown is null) { _initError = Loc.F("WASAPI could not open the default audio device (0x{0:X8}).", hr); return; }
                    client = (IAudioClient)clientUnknown;
                    hr = client.Initialize(ShareModeExclusive, StreamFlagsEventCallback, duration, duration, ref format, ref _session);
                }
            }
            else
            {
                hr = client.Initialize(ShareModeShared, StreamFlagsEventCallback | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality, duration, 0, ref format, ref _session);
            }
            if (hr != 0) { _initError = DescribeInitializeFailure(hr); return; }

            hr = client.GetBufferSize(out var bufferFrames);
            if (hr != 0 || bufferFrames == 0) { _initError = Loc.F("WASAPI could not start the audio stream (0x{0:X8}).", hr); return; }
            _bufferMilliseconds = bufferFrames * 1000.0 / PianoAudioEngine.SampleRate;

            hr = client.SetEventHandle(_deviceEvent.SafeWaitHandle.DangerousGetHandle());
            if (hr != 0) { _initError = Loc.F("WASAPI could not start the audio stream (0x{0:X8}).", hr); return; }

            var renderId = IidAudioRenderClient;
            hr = client.GetService(ref renderId, out renderObject);
            if (hr != 0 || renderObject is not IAudioRenderClient renderClient) { _initError = Loc.F("WASAPI could not start the audio stream (0x{0:X8}).", hr); return; }

            // Pre-fill the whole buffer with one rendered block so Start() never plays stale memory.
            var samples = new float[bufferFrames * 2];
            var pcm16 = _exclusive ? new short[bufferFrames * 2] : null;
            if (!FillDeviceBuffer(renderClient, samples, pcm16, (int)bufferFrames)) return;

            hr = client.Start();
            if (hr != 0) { _initError = Loc.F("WASAPI could not start the audio stream (0x{0:X8}).", hr); return; }
            avrtHandle = EnterProAudioPriority();
            _started.Set();

            while (!_stopping)
            {
                if (!_deviceEvent.WaitOne(2000)) continue; // a silent device event only means "nothing due yet"
                if (_stopping) break;
                int due;
                if (_exclusive) due = (int)bufferFrames;
                else
                {
                    if (client.GetCurrentPadding(out var padding) != 0) break;
                    due = (int)bufferFrames - (int)padding;
                }
                if (due <= 0) continue;
                if (!FillDeviceBuffer(renderClient, samples, pcm16, due)) break;
            }
            client.Stop();
        }
        catch (Exception ex) { _initError ??= Loc.F("WASAPI could not start the audio stream (0x{0:X8}).", ex.HResult); }
        finally
        {
            _started.Set();
            LeaveProAudioPriority(avrtHandle);
            if (renderObject is not null) Marshal.ReleaseComObject(renderObject);
            if (client is not null) Marshal.ReleaseComObject(client);
            if (deviceObject is not null) Marshal.ReleaseComObject(deviceObject);
            if (enumeratorObject is not null) Marshal.ReleaseComObject(enumeratorObject);
            if (Volatile.Read(ref _disposed) != 0) DisposeEvents();
        }
    }

    /// <summary>Renders one block and hands it to the device; false stops the stream (device unplugged, …).</summary>
    private bool FillDeviceBuffer(IAudioRenderClient renderClient, float[] samples, short[]? pcm16, int frames)
    {
        if (renderClient.GetBuffer((uint)frames, out var target) != 0 || target == IntPtr.Zero) return false;
        _render(samples, frames);
        if (pcm16 is null) Marshal.Copy(samples, 0, target, frames * 2);
        else
        {
            for (var i = 0; i < frames * 2; i++) pcm16[i] = (short)Math.Clamp((int)Math.Round(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
            Marshal.Copy(pcm16, 0, target, frames * 2);
        }
        return renderClient.ReleaseBuffer((uint)frames, 0) == 0;
    }

    /// <summary>The refusals a player can actually do something about get words; the rest keep the HRESULT.</summary>
    private static string DescribeInitializeFailure(int hr) => (uint)hr switch
    {
        0x8889000A => Loc.T("Another application holds this audio device in exclusive mode."),
        0x88890008 => Loc.T("This audio device does not accept 44.1 kHz stereo in exclusive mode."),
        0x8889000E => Loc.T("Exclusive mode is disabled for this audio device in Windows sound settings."),
        _ => Loc.F("WASAPI could not start the audio stream (0x{0:X8}).", hr),
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping = true;
        try { _deviceEvent.Set(); } catch (ObjectDisposedException) { }
        var thread = _thread;
        if (thread is null || thread.Join(1500))
        {
            _thread = null;
            DisposeEvents();
        }
        // A stuck driver can outlive the bounded join. Pump() releases its handles after it eventually exits.
    }

    private void DisposeEvents()
    {
        if (Interlocked.Exchange(ref _eventsDisposed, 1) != 0) return;
        _deviceEvent.Dispose();
        _started.Dispose();
    }

    /// <summary>Asks the multimedia scheduler for "Pro Audio" priority; a machine without avrt.dll just declines.</summary>
    private static IntPtr EnterProAudioPriority()
    {
        try { uint taskIndex = 0; return AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex); }
        catch (DllNotFoundException) { return IntPtr.Zero; }
        catch (EntryPointNotFoundException) { return IntPtr.Zero; }
    }

    private static void LeaveProAudioPriority(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try { AvRevertMmThreadCharacteristics(handle); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }

    /// <summary>Stereo PCM at the engine rate: float32 for the shared mixer, int16 on the exclusive wire.</summary>
    private static WaveFormatEx PcmFormat(bool exclusive)
    {
        var bits = (ushort)(exclusive ? 16 : 32);
        var blockAlign = (ushort)(2 * bits / 8);
        return new WaveFormatEx
        {
            FormatTag = exclusive ? FormatPcm : FormatIeeeFloat,
            Channels = 2,
            SamplesPerSec = PianoAudioEngine.SampleRate,
            BitsPerSample = bits,
            BlockAlign = blockAlign,
            AverageBytesPerSecond = (uint)(PianoAudioEngine.SampleRate * blockAlign),
            ExtraSize = 0,
        };
    }

    private Guid _session = Guid.Empty;
    private const int DataFlowRender = 0;
    private const int RoleConsole = 0;
    private const int ShareModeShared = 0;
    private const int ShareModeExclusive = 1;
    private const uint ClsCtxAll = 0x17;
    private const uint StreamFlagsEventCallback = 0x00040000;
    private const uint StreamFlagsAutoConvertPcm = 0x80000000;
    private const uint StreamFlagsSrcDefaultQuality = 0x08000000;
    private const ushort FormatPcm = 1;
    private const ushort FormatIeeeFloat = 3;
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSec, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    /// <summary>Only the first two slots are called; the rest exist to keep the vtable layout truthful.</summary>
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormatEx format, ref Guid audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, ref WaveFormatEx format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? service);
    }

    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
        [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
    }

    [DllImport("avrt.dll", CharSet = CharSet.Unicode)] private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);
    [DllImport("avrt.dll")] private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
