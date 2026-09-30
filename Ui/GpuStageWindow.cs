using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// The full-rate GPU stage: a window whose whole client area is a swap chain the render thread presents
/// to directly. It mirrors the main window's stage (same song, same look, same live notes) and is meant
/// for a second monitor, a projector or OBS window capture. F11 toggles full screen, Esc leaves full
/// screen or closes the window.
/// </summary>
internal sealed class GpuStageWindow : Window
{
    private readonly GpuRenderLoop _loop;
    private readonly SwapChainHost _host = new();
    private readonly DispatcherTimer _status;
    private WindowState _restoreState = WindowState.Normal;
    private bool _fullScreen;

    internal GpuStageWindow(GpuRenderLoop loop)
    {
        _loop = loop;
        Width = 1280; Height = 760; MinWidth = 320; MinHeight = 200;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _host;
        _host.SurfaceChanged += (hwnd, width, height) => _loop.SetWindow(hwnd, width, height);
        _status = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => RefreshTitle(), Dispatcher);
        Loaded += (_, _) => { RefreshTitle(); _status.Start(); };
        Closed += (_, _) => { _status.Stop(); _loop.SetWindow(IntPtr.Zero, 0, 0); };
        PreviewKeyDown += OnKey;
        MouseDoubleClick += (_, _) => ToggleFullScreen();
        Loc.Changed += RefreshTitle;
        Closed += (_, _) => Loc.Changed -= RefreshTitle;
    }

    private void RefreshTitle()
    {
        var engine = _loop.IsWarp ? Loc.T("software rasterizer (WARP)") : _loop.AdapterName;
        Title = _loop.Error is { } error
            ? Loc.F("Keyflow GPU stage · stopped: {0}", error)
            : Loc.F("Keyflow GPU stage · {0} FPS · {1} · F11 full screen", Math.Round(_loop.Fps), engine);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
        else if (e.Key == Key.Escape) { if (_fullScreen) ToggleFullScreen(); else Close(); e.Handled = true; }
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _restoreState = WindowState;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            // Maximizing a normal window first makes a borderless window cover the taskbar as well.
            WindowState = WindowState.Normal; WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            WindowState = _restoreState;
        }
        _fullScreen = !_fullScreen;
    }
}
