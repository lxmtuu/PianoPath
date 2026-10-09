using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// Borderless WPF/DWM surface for the GPU's premultiplied BGRA AR-effects tap. Transparent pixels reveal the
/// desktop; OBS can capture this window without the camera image or the stage background.
/// </summary>
internal sealed class GpuArAlphaOutputWindow : Window
{
    private readonly GpuAlphaOutputTap _tap;
    private readonly Image _image;
    private readonly byte[] _pixels;
    private readonly DispatcherTimer _timer;
    private WriteableBitmap? _bitmap;
    private long _lastSerial;

    internal GpuArAlphaOutputWindow(GpuAlphaOutputTap tap)
    {
        _tap = tap;
        _pixels = new byte[tap.Width * tap.Height * 4];
        Title = "Keyflow · Transparent AR Output";
        Width = tap.Width;
        Height = tap.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Focusable = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = false;

        _image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = true, Focusable = false };
        _image.MouseLeftButtonDown += OnMouseLeftButtonDown;
        Content = _image;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += RefreshFrame;
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) => Focus();
    }

    private void RefreshFrame(object? sender, EventArgs e)
    {
        var serial = _tap.Serial;
        if (serial == 0 || serial == _lastSerial) return;
        if (!_tap.TryRead(frame => Buffer.BlockCopy(frame, 0, _pixels, 0, _pixels.Length))) return;
        _lastSerial = serial;
        _bitmap ??= new WriteableBitmap(_tap.Width, _tap.Height, 96, 96, PixelFormats.Pbgra32, null);
        _image.Source = _bitmap;
        _bitmap.WritePixels(new Int32Rect(0, 0, _tap.Width, _tap.Height), _pixels, _tap.Width * 4, 0);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                Width = _tap.Width;
                Height = _tap.Height;
            }
            else WindowState = WindowState.Maximized;
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (WindowState == WindowState.Normal && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }
}
