using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// Borderless WPF/DWM surface for a premultiplied BGRA AR-effects tap. Transparent pixels reveal the
/// desktop; OBS can capture this window without the camera image or the stage background.
/// </summary>
internal sealed class GpuArAlphaOutputWindow : Window
{
    private readonly GpuAlphaOutputTap? _tap;
    private readonly GpuAlphaLayerOutputTap? _layerTap;
    private readonly int _outputWidth, _outputHeight;
    private readonly Image _image;
    private readonly byte[] _pixels;
    private readonly DispatcherTimer? _timer;
    private WriteableBitmap? _bitmap;
    private long _lastSerial;

    internal GpuArAlphaOutputWindow(GpuAlphaOutputTap tap, string? outputName = null)
        : this(tap.Width, tap.Height,
            string.IsNullOrWhiteSpace(outputName) ? "Keyflow · Transparent AR Output" : $"Keyflow · Transparent AR · {outputName}",
            dispatcherRefresh: true)
    {
        _tap = tap;
    }

    /// <summary>Geometry/glow windows are refreshed together by their owning paired output controller.</summary>
    internal GpuArAlphaOutputWindow(GpuAlphaLayerOutputTap tap)
        : this(tap.Width, tap.Height, $"Keyflow · Transparent AR · {tap.OutputName}", dispatcherRefresh: false)
    {
        _layerTap = tap;
    }

    private GpuArAlphaOutputWindow(int width, int height, string title, bool dispatcherRefresh)
    {
        _outputWidth = width;
        _outputHeight = height;
        _pixels = new byte[checked(width * height * 4)];
        Title = title;
        Width = width;
        Height = height;
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

        if (dispatcherRefresh)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += RefreshFrame;
            Loaded += (_, _) => _timer.Start();
        }
        Closed += (_, _) => _timer?.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) => Focus();
    }

    private void RefreshFrame(object? sender, EventArgs e)
    {
        var latest = _tap?.Serial ?? _layerTap!.Serial;
        if (latest == 0 || latest == _lastSerial || !TryReadNewest(out var serial)) return;
        PresentCopiedFrame(serial);
    }

    private bool TryReadNewest(out long serial)
    {
        Action<byte[]> copy = CopyFrameData;
        if (_tap is { } tap) return tap.TryRead(copy, out serial);
        return _layerTap!.TryRead(copy, out serial);
    }

    /// <summary>Copies the selected channel into this window's private WPF staging buffer.</summary>
    internal void CopyFrameData(byte[] frame) => Buffer.BlockCopy(frame, 0, _pixels, 0, _pixels.Length);

    /// <summary>Publishes a buffer copied by the shared layer timer, using the same pair serial for both windows.</summary>
    internal void PresentCopiedFrame(long serial)
    {
        if (serial == 0 || serial == _lastSerial) return;
        _lastSerial = serial;
        _bitmap ??= new WriteableBitmap(_outputWidth, _outputHeight, 96, 96, PixelFormats.Pbgra32, null);
        _image.Source = _bitmap;
        _bitmap.WritePixels(new Int32Rect(0, 0, _outputWidth, _outputHeight), _pixels, _outputWidth * 4, 0);
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
                Width = _outputWidth;
                Height = _outputHeight;
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
