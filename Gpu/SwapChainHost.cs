using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// A plain child HWND inside WPF for the GPU stage's swap chain. WPF never draws into it: the render
/// thread presents straight to it (flip model), so the stage window is not limited by WPF's composition
/// rate and can run at 120, 144 or 240 Hz with VSync off and tearing allowed.
/// </summary>
/// <remarks>WPF content cannot overlap an HwndHost (airspace), which is why the stage window keeps its
/// readouts in the title bar rather than over the picture.</remarks>
internal sealed class SwapChainHost : HwndHost
{
    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000, WsClipSiblings = 0x04000000;
    private IntPtr _hwnd;

    /// <summary>The child window and its size in device pixels; a zero handle means the surface is gone.</summary>
    internal event Action<IntPtr, int, int>? SurfaceChanged;

    internal IntPtr Surface => _hwnd;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren | WsClipSiblings, 0, 0, 16, 16, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
        Dispatcher.BeginInvoke(new Action(Report));
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        SurfaceChanged?.Invoke(IntPtr.Zero, 0, 0);
        DestroyWindow(hwnd.Handle);
        _hwnd = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Report();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Report();
    }

    private void Report()
    {
        if (_hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        SurfaceChanged?.Invoke(_hwnd, (int)Math.Round(ActualWidth * dpi.DpiScaleX), (int)Math.Round(ActualHeight * dpi.DpiScaleY));
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
