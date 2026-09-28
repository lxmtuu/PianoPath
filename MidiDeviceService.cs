using System.Runtime.InteropServices;

namespace PianoPath;

internal enum PianoPedal { Soft, Sostenuto, Sustain }

/// <summary>Windows WinMM MIDI input/output bridge; callback methods stay rooted for the device lifetime.</summary>
internal sealed class MidiDeviceService : IDisposable
{
    private const int CallbackFunction = 0x00030000;
    internal const uint MidiInputDataMessage = 0x3C3; // MIM_DATA
    private IntPtr _input = IntPtr.Zero;
    private IntPtr _output = IntPtr.Zero;
    private MidiInProc? _callback;
    public event Action<int, int, bool>? NoteChanged;
    public event Action<PianoPedal, bool>? PedalChanged;
    public bool InputOpen => _input != IntPtr.Zero;
    public static IReadOnlyList<string> Inputs => Enumerable.Range(0, (int)midiInGetNumDevs()).Select(i => GetName(i, true)).ToArray();
    public static IReadOnlyList<string> Outputs => Enumerable.Range(0, (int)midiOutGetNumDevs()).Select(i => GetName(i, false)).ToArray();

    public void OpenInput(int index)
    {
        CloseInput();
        if (index < 0) return;
        var inputCount = midiInGetNumDevs();
        if ((uint)index >= inputCount) throw new InvalidOperationException("The selected MIDI input is no longer available. Refresh the MIDI device list and select it again.");
        _callback = OnMidiInput;
        var result = midiInOpen(out _input, (uint)index, _callback, UIntPtr.Zero, CallbackFunction);
        if (result != 0) { _input = IntPtr.Zero; _callback = null; throw new InvalidOperationException($"Windows MIDI input could not be opened (code {result})."); }
        result = midiInStart(_input);
        if (result != 0) { CloseInput(); throw new InvalidOperationException($"Windows MIDI input could not start (code {result})."); }
    }
    public void OpenOutput(int index)
    {
        CloseOutput();
        if (index < 0 || index >= midiOutGetNumDevs()) return;
        var result = midiOutOpen(out _output, (uint)index, IntPtr.Zero, UIntPtr.Zero, 0);
        if (result != 0) { _output = IntPtr.Zero; throw new InvalidOperationException($"Windows MIDI output could not be opened (code {result})."); }
    }
    public void SendNote(int pitch, int velocity, bool on, int channel = 0)
    {
        if (_output == IntPtr.Zero) return;
        var result = midiOutShortMsg(_output, PackNoteMessage(pitch, velocity, on, channel));
        if (result != 0) throw new InvalidOperationException($"Windows MIDI output rejected a note message (code {result}).");
    }
    public void SendController(int controller, int value, int channel = 0)
    {
        if (_output == IntPtr.Zero) return;
        var result = midiOutShortMsg(_output, PackControllerMessage(controller, value, channel));
        if (result != 0) throw new InvalidOperationException($"Windows MIDI output rejected a controller message (code {result}).");
    }
    public void CloseOutputDevice() => CloseOutput();
    internal static uint PackNoteMessage(int pitch, int velocity, bool on, int channel = 0) =>
        (uint)((on ? 0x90 : 0x80) | (channel & 15) | ((pitch & 127) << 8) | ((on ? velocity : 0) << 16));
    internal static uint PackControllerMessage(int controller, int value, int channel = 0) =>
        (uint)(0xB0 | (channel & 15) | ((controller & 127) << 8) | ((value & 127) << 16));
    internal static int ControllerFor(PianoPedal pedal) => pedal switch { PianoPedal.Sustain => 64, PianoPedal.Sostenuto => 66, _ => 67 };
    private void OnMidiInput(IntPtr handle, uint message, UIntPtr instance, UIntPtr parameter1, UIntPtr parameter2)
    {
        try { ProcessNativeInputMessage(message, parameter1); }
        catch { /* Never allow a managed exception to cross the WinMM callback boundary. */ }
    }
    internal void ProcessNativeInputMessage(uint message, UIntPtr parameter1)
    {
        if (message != MidiInputDataMessage) return;
        ProcessShortMessage(unchecked((uint)parameter1.ToUInt64()));
    }
    internal void ProcessShortMessage(uint packed)
    {
        var status = (int)(packed & 255); var kind = status & 0xF0;
        var data1 = (int)((packed >> 8) & 127); var data2 = (int)((packed >> 16) & 127);
        if (kind is 0x80 or 0x90) NoteChanged?.Invoke(data1, data2, kind == 0x90 && data2 > 0);
        else if (kind == 0xB0)
        {
            var pedal = data1 switch { 67 => PianoPedal.Soft, 66 => PianoPedal.Sostenuto, 64 => PianoPedal.Sustain, _ => (PianoPedal?)null };
            if (pedal is { } value) PedalChanged?.Invoke(value, data2 >= 64);
        }
    }
    private static string GetName(int id, bool input)
    {
        var caps = new MidiCaps();
        var result = input ? midiInGetDevCaps((UIntPtr)id, ref caps, (uint)Marshal.SizeOf<MidiCaps>()) : midiOutGetDevCaps((UIntPtr)id, ref caps, (uint)Marshal.SizeOf<MidiCaps>());
        return result == 0 && !string.IsNullOrWhiteSpace(caps.Name) ? caps.Name : $"MIDI device {id + 1}";
    }
    private void CloseInput() { if (_input != IntPtr.Zero) { midiInStop(_input); midiInReset(_input); midiInClose(_input); _input = IntPtr.Zero; } _callback = null; }
    private void CloseOutput() { if (_output != IntPtr.Zero) { midiOutReset(_output); midiOutClose(_output); _output = IntPtr.Zero; } }
    public void Dispose() { CloseInput(); CloseOutput(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MidiCaps { public ushort Manufacturer; public ushort Product; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public ushort Technology; public ushort Voices; public ushort Notes; public ushort ChannelMask; public uint Support; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void MidiInProc(IntPtr handle, uint message, UIntPtr instance, UIntPtr parameter1, UIntPtr parameter2);
    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Auto)] private static extern uint midiInGetDevCaps(UIntPtr id, ref MidiCaps caps, uint size);
    [DllImport("winmm.dll", CharSet = CharSet.Auto)] private static extern uint midiOutGetDevCaps(UIntPtr id, ref MidiCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr handle, uint id, MidiInProc callback, UIntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiOutOpen(out IntPtr handle, uint id, IntPtr callback, UIntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern uint midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern uint midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiOutClose(IntPtr handle);
}
