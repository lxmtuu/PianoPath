using System.Runtime.InteropServices;

namespace PianoPath;

/// <summary>Reads the current display mode for the monitor containing an output window (0 when unavailable).</summary>
internal static class GpuDisplayRefreshRate
{
    private const uint MonitorDefaultToNearest = 2;
    private const int CurrentDisplaySettings = -1;

    internal static int ForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        try
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return 0;
            var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
            if (!GetMonitorInfo(monitor, ref info) || string.IsNullOrWhiteSpace(info.DeviceName)) return 0;
            var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>(), DeviceName = string.Empty, FormName = string.Empty };
            if (!EnumDisplaySettings(info.DeviceName, CurrentDisplaySettings, ref mode)) return 0;
            return mode.DisplayFrequency is > 1 and <= 1000 ? (int)mode.DisplayFrequency : 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return 0;
        }
    }

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow", ExactSpelling = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DeviceMode mode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string DeviceName;
        internal ushort SpecVersion, DriverVersion, Size, DriverExtra;
        internal uint Fields;
        internal short Orientation, PaperSize, PaperLength, PaperWidth, Scale, Copies, DefaultSource, PrintQuality;
        internal short Color, Duplex, YResolution, TrueTypeOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string FormName;
        internal ushort LogPixels;
        internal uint BitsPerPixel, PixelWidth, PixelHeight, DisplayFlags, DisplayFrequency;
        internal uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
}
