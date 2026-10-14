using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PianoPath;

/// <summary>The ASIO drivers installed on this machine, as Windows lists them under <c>HKLM\SOFTWARE\ASIO</c>.</summary>
internal static class AsioDriverRegistry
{
    /// <summary>Driver display names, sorted; an empty list on machines without any ASIO driver (most of them).</summary>
    internal static IReadOnlyList<string> Names
    {
        get
        {
            try
            {
                // Escaped rather than verbatim strings: a verbatim literal ending in a backslash reads
                // like an escaped quote to simpler tooling, which then misparses the rest of the file.
                using var root = Registry.LocalMachine.OpenSubKey("SOFTWARE\\ASIO");
                return root?.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            }
            catch (Exception) { return []; } // a locked-down registry is the same as no drivers
        }
    }

    /// <summary>The COM class id registered for <paramref name="driverName"/>, or null when absent/garbled.</summary>
    internal static Guid? ClassIdOf(string driverName)
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey("SOFTWARE\\ASIO\\" + driverName);
            return Guid.TryParse(root?.GetValue("CLSID") as string, out var clsid) ? clsid : null;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// Plays through an installed ASIO driver — the studio-interface path, typically 1–5 ms of buffer.
///
/// <para>
/// ASIO is COM only by courtesy: the driver is created with <c>CoCreateInstance</c> where the interface id
/// <em>equals</em> the class id, and the resulting interface is not a declarable COM interface — its methods
/// use <c>thiscall</c> and return ASIO's own error codes, so this class reads the vtable by hand and binds
/// each slot as a <see cref="CallingConvention.ThisCall"/> delegate (the same approach NAudio takes; no
/// Steinberg SDK is involved, which also keeps the licence situation trivial). The driver calls back on a
/// thread it owns: <c>bufferSwitch</c> renders straight into the device's half-buffer there.
/// </para>
///
/// <para>
/// The stream runs at the engine's fixed 44.1 kHz; a driver that cannot run that rate is refused in words
/// and the factory falls back to WASAPI. Int16/Int24/Int32/Float32 little-endian output buffers are
/// supported, which covers every interface in circulation.
/// </para>
/// </summary>
internal sealed class AsioAudioSink : IAudioSink
{
    private readonly AudioRenderCallback _render;
    private readonly string _driverName;
    private IntPtr _driver;
    private AsioMethods _asio;
    private AsioBufferInfo[] _buffers = [];
    private int _bufferFrames;
    private int _sampleType;
    private bool _outputReadyChecked, _supportsOutputReady;
    private int _outputChannels;
    private double _outputLatencyMs;
    private float[] _samples = [];
    private int[] _packInt = [];
    private short[] _packShort = [];
    private byte[] _packBytes = [];
    private IntPtr _callbacksMemory;
    // The four delegates live for the sink's whole lifetime: the driver keeps raw function pointers to them.
    private BufferSwitchHandler? _bufferSwitch;
    private SampleRateChangedHandler? _sampleRateChanged;
    private AsioMessageHandler? _asioMessage;
    private BufferSwitchTimeInfoHandler? _bufferSwitchTimeInfo;
    private volatile bool _stopping;
    private volatile string? _streamFailure;
    private bool _buffersCreateAttempted, _started, _disposed;

    public string Description => Loc.F("ASIO · {0} · {1:0.#} ms · {2} frames", _driverName, _outputLatencyMs, _bufferFrames);
    public double BufferMilliseconds => _outputLatencyMs;
    public string? StreamFailure => _streamFailure;

    private readonly double _targetLatencyMilliseconds;
    private readonly IntPtr _systemHandle;

    private AsioAudioSink(string driverName, double targetLatencyMilliseconds, IntPtr systemHandle, AudioRenderCallback render)
    {
        _driverName = driverName;
        _targetLatencyMilliseconds = Math.Clamp(targetLatencyMilliseconds, 2, 50);
        _systemHandle = systemHandle;
        _render = render;
    }

    /// <summary>Opens and starts <paramref name="driverName"/>, choosing a size near the user's latency target.</summary>
    internal static AsioAudioSink? TryCreate(string driverName, double latencyMilliseconds, IntPtr systemHandle, AudioRenderCallback render, out string? failure)
    {
        if (AsioDriverRegistry.Names.Count == 0) { failure = Loc.T("No ASIO driver is installed on this machine."); return null; }
        if (AsioDriverRegistry.ClassIdOf(driverName) is not { } clsid) { failure = Loc.F("The ASIO driver '{0}' is not installed.", driverName); return null; }
        var sink = new AsioAudioSink(driverName, latencyMilliseconds, systemHandle, render);
        try
        {
            if (sink.Open(clsid, out failure)) return sink;
        }
        catch (Exception ex) { failure = Loc.F("The ASIO driver could not start (code {0}).", ex.HResult); }
        sink.Dispose();
        return null;
    }

    /// <summary>Shows the driver's own settings window (buffer size, clocking); a no-op once disposed.</summary>
    internal void ShowControlPanel() { if (!_disposed && _driver != IntPtr.Zero) _asio.ControlPanel(_driver); }

    private bool Open(Guid clsid, out string? failure)
    {
        // ASIO's one COM eccentricity: the driver is asked for an interface whose IID is its own CLSID.
        var iid = clsid;
        var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsCtxInprocServer, ref iid, out _driver);
        if (hr != 0 || _driver == IntPtr.Zero) { failure = Loc.F("The ASIO driver could not be created (0x{0:X8}).", hr); return false; }
        _asio = AsioMethods.Bind(_driver);

        // ASIO drivers expect the host's HWND in init(); passing null works with a few drivers but leaves
        // others in a degraded/default path. The desktop window is a safe last resort for non-WPF hosts.
        var systemHandle = _systemHandle != IntPtr.Zero ? _systemHandle : GetDesktopWindow();
        if (_asio.Init(_driver, systemHandle) == 0)
        {
            var message = new byte[256];
            _asio.GetErrorMessage(_driver, message);
            var text = Encoding.ASCII.GetString(message, 0, Math.Max(0, Array.IndexOf(message, (byte)0))).Trim();
            failure = Loc.F("The ASIO driver refused to initialize ({0}).", text.Length > 0 ? text : _driverName);
            return false;
        }

        _asio.GetChannels(_driver, out _, out var outputs);
        if (outputs <= 0) { failure = Loc.T("The ASIO driver has no output channels."); return false; }
        _outputChannels = Math.Min(2, outputs);

        if (_asio.CanSampleRate(_driver, PianoAudioEngine.SampleRate) != 0 || _asio.SetSampleRate(_driver, PianoAudioEngine.SampleRate) != 0)
        {
            failure = Loc.T("The ASIO driver cannot run at 44.1 kHz.");
            return false;
        }

        var sizeResult = _asio.GetBufferSize(_driver, out var minimum, out var maximum, out var preferred, out var granularity);
        if (sizeResult != 0)
        {
            failure = Loc.F("The ASIO driver could not report its buffer sizes (code {0}).", sizeResult);
            return false;
        }
        var targetFrames = (int)Math.Round(_targetLatencyMilliseconds * PianoAudioEngine.SampleRate / 1000.0);
        _bufferFrames = AsioBufferSizing.SelectFrames(minimum, maximum, preferred, granularity, targetFrames);
        _samples = new float[checked(_bufferFrames * 2)];

        _bufferSwitch = OnBufferSwitch;
        _sampleRateChanged = OnSampleRateChanged;
        _asioMessage = OnAsioMessage;
        _bufferSwitchTimeInfo = OnBufferSwitchTimeInfo;
        var callbacks = new AsioCallbacks
        {
            BufferSwitch = Marshal.GetFunctionPointerForDelegate(_bufferSwitch),
            SampleRateDidChange = Marshal.GetFunctionPointerForDelegate(_sampleRateChanged),
            AsioMessage = Marshal.GetFunctionPointerForDelegate(_asioMessage),
            BufferSwitchTimeInfo = Marshal.GetFunctionPointerForDelegate(_bufferSwitchTimeInfo),
        };
        // The specification keeps the callbacks struct owned by the host for the driver's lifetime, so it
        // lives in unmanaged memory until Dispose rather than on this stack frame.
        _callbacksMemory = Marshal.AllocHGlobal(Marshal.SizeOf<AsioCallbacks>());
        Marshal.StructureToPtr(callbacks, _callbacksMemory, false);

        _buffers = new AsioBufferInfo[_outputChannels];
        for (var channel = 0; channel < _outputChannels; channel++) _buffers[channel] = new AsioBufferInfo { IsInput = 0, ChannelNumber = channel };
        _buffersCreateAttempted = true; // if the driver failed partway through, still ask it to release partial allocations
        var code = _asio.CreateBuffers(_driver, _buffers, _outputChannels, _bufferFrames, _callbacksMemory);
        if (code != 0) { failure = Loc.F("The ASIO driver could not start (code {0}).", code); return false; }

        var info = new AsioChannelInfo { Channel = 0, IsInput = 0, Name = new byte[32] };
        var channelInfoResult = _asio.GetChannelInfo(_driver, ref info);
        if (channelInfoResult != 0) { failure = Loc.F("The ASIO driver could not describe output channel 1 (code {0}).", channelInfoResult); return false; }
        _sampleType = info.Type;
        if (_sampleType is not (SampleTypeInt16 or SampleTypeInt24 or SampleTypeInt32 or SampleTypeFloat32))
        {
            failure = Loc.F("The ASIO driver uses an unsupported sample format ({0}).", _sampleType);
            return false;
        }
        _packInt = new int[_bufferFrames];
        _packShort = new short[_bufferFrames];
        _packBytes = new byte[checked(_bufferFrames * 3)];

        _asio.GetLatencies(_driver, out _, out var outputLatency);
        _outputLatencyMs = Math.Max(outputLatency, _bufferFrames) * 1000.0 / PianoAudioEngine.SampleRate;

        code = _asio.Start(_driver);
        if (code != 0) { failure = Loc.F("The ASIO driver could not start (code {0}).", code); return false; }
        _started = true;
        failure = null;
        return true;
    }

    /// <summary>The driver's clock: render one block and pack it into the half-buffer that just freed up.</summary>
    private void OnBufferSwitch(int doubleBufferIndex, int directProcess)
    {
        if (_stopping) return;
        try
        {
            var frames = _bufferFrames;
            _render(_samples, frames);
            for (var channel = 0; channel < _outputChannels; channel++)
            {
                var target = doubleBufferIndex == 0 ? _buffers[channel].Buffer0 : _buffers[channel].Buffer1;
                if (target == IntPtr.Zero) continue;
                // A mono interface gets the average of both engine channels instead of the left one only.
                var source = channel; var mono = _outputChannels == 1;
                switch (_sampleType)
                {
                    case SampleTypeInt32:
                        for (var i = 0; i < frames; i++) _packInt[i] = (int)Math.Clamp(Math.Round(Sample(i, source, mono) * int.MaxValue), int.MinValue, int.MaxValue);
                        Marshal.Copy(_packInt, 0, target, frames);
                        break;
                    case SampleTypeInt16:
                        for (var i = 0; i < frames; i++) _packShort[i] = (short)Math.Clamp((int)Math.Round(Sample(i, source, mono) * short.MaxValue), short.MinValue, short.MaxValue);
                        Marshal.Copy(_packShort, 0, target, frames);
                        break;
                    case SampleTypeInt24:
                        for (var i = 0; i < frames; i++)
                        {
                            var value = (int)Math.Clamp(Math.Round(Sample(i, source, mono) * 8388607.0), -8388608, 8388607);
                            _packBytes[i * 3] = (byte)value; _packBytes[i * 3 + 1] = (byte)(value >> 8); _packBytes[i * 3 + 2] = (byte)(value >> 16);
                        }
                        Marshal.Copy(_packBytes, 0, target, frames * 3);
                        break;
                    default: // SampleTypeFloat32
                        if (mono) { for (var i = 0; i < frames; i++) _packInt[i] = BitConverter.SingleToInt32Bits(Sample(i, source, mono)); Marshal.Copy(_packInt, 0, target, frames); }
                        else { for (var i = 0; i < frames; i++) _packInt[i] = BitConverter.SingleToInt32Bits(_samples[i * 2 + source]); Marshal.Copy(_packInt, 0, target, frames); }
                        break;
                }
            }
            // The outputReady method reports ASE_NotPresent on unsupported drivers; probe once after the
            // first completed block, then use it only when the driver says it is meaningful.
            if (!_outputReadyChecked)
            {
                _outputReadyChecked = true;
                _supportsOutputReady = _asio.OutputReady(_driver) == 0;
            }
            else if (_supportsOutputReady) _asio.OutputReady(_driver);
        }
        catch { /* never let a managed exception cross into the driver's thread */ }
    }

    private float Sample(int frame, int channel, bool mono) => mono ? (_samples[frame * 2] + _samples[frame * 2 + 1]) * .5f : _samples[frame * 2 + channel];

    private void OnSampleRateChanged(double sampleRate) { /* the engine stays at 44.1 kHz; a rate change ends in Dispose/reopen via the settings page */ }

    private int OnAsioMessage(int selector, int value, IntPtr message, IntPtr opt)
    {
        // A driver reset ends this stream as surely as an unplugged device ends a WASAPI one: report it
        // the same way, so the engine can surface it and hand the recording to its own clock.
        if (selector == ResetRequest)
            _streamFailure = Loc.T("The audio output device stopped answering; pick the output again to restart it.");
        return selector switch
        {
            SelectorSupported => value is EngineVersion or ResetRequest ? 1 : 0,
            EngineVersion => 2,
            ResetRequest => 1, // acknowledged; the user re-picks the output to rebuild after a driver reset
            _ => 0,
        };
    }

    private IntPtr OnBufferSwitchTimeInfo(IntPtr timeInfo, int doubleBufferIndex, int directProcess)
    {
        // kAsioSupportsTimeInfo answers 0, so a spec-abiding driver uses OnBufferSwitch; this guard keeps a
        // driver that calls the rich variant anyway audible instead of silent.
        OnBufferSwitch(doubleBufferIndex, directProcess);
        return timeInfo;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _stopping = true;
        if (_driver != IntPtr.Zero)
        {
            // Attempt each teardown stage independently; a failed Stop must not skip releasing prepared buffers.
            if (_started) { try { _asio.Stop(_driver); } catch { } _started = false; }
            if (_buffersCreateAttempted) { try { _asio.DisposeBuffers(_driver); } catch { } _buffersCreateAttempted = false; }
            Marshal.Release(_driver);
            _driver = IntPtr.Zero;
        }
        if (_callbacksMemory != IntPtr.Zero) { Marshal.FreeHGlobal(_callbacksMemory); _callbacksMemory = IntPtr.Zero; }
    }

    // ---- the IASIO vtable, bound by hand -----------------------------------------------------------

    /// <summary>
    /// The driver's methods in vtable order after IUnknown's three slots. Only the slots this sink calls
    /// are bound; the record keeps them as delegates so the GC cannot collect the thunks while playing.
    /// </summary>
    private readonly struct AsioMethods
    {
        public required InitHandler Init { get; init; }
        public required GetErrorMessageHandler GetErrorMessage { get; init; }
        public required SimpleHandler Start { get; init; }
        public required SimpleHandler Stop { get; init; }
        public required GetChannelsHandler GetChannels { get; init; }
        public required GetLatenciesHandler GetLatencies { get; init; }
        public required GetBufferSizeHandler GetBufferSize { get; init; }
        public required SampleRateHandler CanSampleRate { get; init; }
        public required SampleRateHandler SetSampleRate { get; init; }
        public required GetChannelInfoHandler GetChannelInfo { get; init; }
        public required CreateBuffersHandler CreateBuffers { get; init; }
        public required SimpleHandler DisposeBuffers { get; init; }
        public required SimpleHandler ControlPanel { get; init; }
        public required SimpleHandler OutputReady { get; init; }

        public static AsioMethods Bind(IntPtr driver)
        {
            var vtable = Marshal.ReadIntPtr(driver);
            T Slot<T>(int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, index * IntPtr.Size));
            // Slot order is fixed by the ASIO binary contract: 0-2 IUnknown, 3 init, 4 getDriverName,
            // 5 getDriverVersion, 6 getErrorMessage, 7 start, 8 stop, 9 getChannels, 10 getLatencies,
            // 11 getBufferSize, 12 canSampleRate, 13 getSampleRate, 14 setSampleRate, 15 getClockSources,
            // 16 setClockSource, 17 getSamplePosition, 18 getChannelInfo, 19 createBuffers,
            // 20 disposeBuffers, 21 controlPanel, 22 future, 23 outputReady.
            return new AsioMethods
            {
                Init = Slot<InitHandler>(3),
                GetErrorMessage = Slot<GetErrorMessageHandler>(6),
                Start = Slot<SimpleHandler>(7),
                Stop = Slot<SimpleHandler>(8),
                GetChannels = Slot<GetChannelsHandler>(9),
                GetLatencies = Slot<GetLatenciesHandler>(10),
                GetBufferSize = Slot<GetBufferSizeHandler>(11),
                CanSampleRate = Slot<SampleRateHandler>(12),
                SetSampleRate = Slot<SampleRateHandler>(14),
                GetChannelInfo = Slot<GetChannelInfoHandler>(18),
                CreateBuffers = Slot<CreateBuffersHandler>(19),
                DisposeBuffers = Slot<SimpleHandler>(20),
                ControlPanel = Slot<SimpleHandler>(21),
                OutputReady = Slot<SimpleHandler>(23),
            };
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int InitHandler(IntPtr self, IntPtr sysHandle);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void GetErrorMessageHandler(IntPtr self, [Out] byte[] message);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int SimpleHandler(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int GetChannelsHandler(IntPtr self, out int inputs, out int outputs);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int GetLatenciesHandler(IntPtr self, out int input, out int output);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int GetBufferSizeHandler(IntPtr self, out int minimum, out int maximum, out int preferred, out int granularity);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int SampleRateHandler(IntPtr self, double sampleRate);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int GetChannelInfoHandler(IntPtr self, ref AsioChannelInfo info);
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int CreateBuffersHandler(IntPtr self, [In, Out] AsioBufferInfo[] infos, int channelCount, int bufferFrames, IntPtr callbacks);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BufferSwitchHandler(int doubleBufferIndex, int directProcess);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SampleRateChangedHandler(double sampleRate);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AsioMessageHandler(int selector, int value, IntPtr message, IntPtr opt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr BufferSwitchTimeInfoHandler(IntPtr timeInfo, int doubleBufferIndex, int directProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct AsioBufferInfo { public int IsInput; public int ChannelNumber; public IntPtr Buffer0; public IntPtr Buffer1; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct AsioChannelInfo
    {
        public int Channel, IsInput, IsActive, ChannelGroup, Type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AsioCallbacks { public IntPtr BufferSwitch, SampleRateDidChange, AsioMessage, BufferSwitchTimeInfo; }

    private const int SampleTypeInt16 = 16;
    private const int SampleTypeInt24 = 17;
    private const int SampleTypeInt32 = 18;
    private const int SampleTypeFloat32 = 19;
    private const int SelectorSupported = 1;
    private const int EngineVersion = 2;
    private const int ResetRequest = 3;
    private const uint ClsCtxInprocServer = 1;

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
}
