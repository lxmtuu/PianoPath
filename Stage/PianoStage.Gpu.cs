using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// The stage's bridge to the Direct3D 11 engine (<see cref="GpuRenderLoop"/>). The stage keeps owning the
/// song state and the settings; it forwards every change into a <see cref="GpuStageFeed"/> and, when the
/// GPU engine is the active one, shows the frames the render thread reads back instead of painting its
/// own layers.
/// </summary>
internal sealed partial class PianoStage
{
    private GpuStageFeed? _gpu;
    private bool _gpuEmbedded;
    private WriteableBitmap? _gpuBitmap;
    private long _gpuSerial = -1;
    private bool _gpuRenderingHooked;
    private double _gpuLookElapsed = double.NegativeInfinity;
    private static long s_gpuBackgroundVersion;
    private BitmapSource? _gpuBackgroundSource;

    /// <summary>
    /// Set while the main window replays a note the MIDI callback already handed to the GPU feed
    /// directly (see <c>MainWindow.ForwardMidiToGpu</c>), so the live trail and the burst are not queued twice.
    /// </summary>
    internal bool SuppressGpuForward { get; set; }
    private GpuStageFeed? GpuForward => SuppressGpuForward ? null : _gpu;

    /// <summary>Frame rate and particle count of the render thread, for the FPS readout.</summary>
    internal Func<(double Fps, int Particles)>? GpuStats { get; set; }

    /// <summary>Raised with the stage's size in device pixels whenever it changes while a feed is attached.</summary>
    internal event Action<int, int>? GpuPixelSizeChanged;

    /// <summary>
    /// True while the GPU engine's frame covers the stage. A transparent capture (PNG sequence with
    /// alpha) always uses the software layers: the GPU frame is opaque.
    /// </summary>
    internal bool UsesGpuFrame => _gpu is not null && _gpuEmbedded && !_transparentBackdrop;

    /// <summary>Stage size in device pixels (capped at 2560×1440 for the read-back preview).</summary>
    internal (int Width, int Height) GpuPixelSize
    {
        get
        {
            var ppd = _pixelsPerDip > 0 ? _pixelsPerDip : PixelsPerDip(this);
            var w = ActualWidth * ppd; var h = ActualHeight * ppd;
            var fit = Math.Min(1, Math.Min(2560 / Math.Max(1, w), 1440 / Math.Max(1, h)));
            return ((int)Math.Round(w * fit), (int)Math.Round(h * fit));
        }
    }

    /// <summary>
    /// Connects the stage to a GPU feed (or disconnects it with null). <paramref name="embedded"/> says
    /// whether the GPU frame replaces the software stage in this element; when false the feed only
    /// mirrors the stage for the separate GPU stage window.
    /// </summary>
    internal void AttachGpu(GpuStageFeed? feed, bool embedded)
    {
        if (!ReferenceEquals(feed, _gpu))
        {
            _gpu = feed;
            _gpuSerial = -1;
            if (feed is not null)
            {
                _gpuBackgroundSource = null;
                PublishGpuBackground();
                feed.LabelAtlas ??= GpuGlyphAtlas;
                PublishGpuLook();
                feed.SetStageHeight(ActualHeight);
                feed.SetPointer(_pointerX, _pointerY);
                ForwardGpuState();
            }
        }
        _gpuEmbedded = feed is not null && embedded;
        if (!_gpuEmbedded) _gpuBitmap = null;
        var hook = _gpuEmbedded;
        if (hook != _gpuRenderingHooked)
        {
            if (hook) CompositionTarget.Rendering += OnGpuRendering; else CompositionTarget.Rendering -= OnGpuRendering;
            _gpuRenderingHooked = hook;
        }
        ReportGpuSize();
        InvalidateVisual();
    }

    private void ReportGpuSize()
    {
        if (_gpu is null) return;
        _gpu.SetStageHeight(ActualHeight);
        var (w, h) = GpuPixelSize;
        GpuPixelSizeChanged?.Invoke(w, h);
    }

    /// <summary>Rebuilds the GPU look from the current settings (the note colour table included).</summary>
    private void PublishGpuLook()
    {
        if (_gpu is null) return;
        var height = ActualHeight;
        var fraction = height >= 1 ? KeyboardHeight / height : .205 * _visual.KeyboardScale / 100;
        _gpu.SetLook(GpuLook.From(_visual, (pitch, track) => AdjustColor(NoteColor(pitch, track)), Math.Clamp(fraction, .05, .6), AdjustColor));
        _gpuLookElapsed = _elapsed;
    }

    private static GpuBackgroundImage? s_gpuGlyphAtlas;
    /// <summary>
    /// The text the GPU stage draws (note names on the keys, Matrix Rain glyphs), rendered once with WPF's
    /// own text stack into a 1024 × 768 atlas of 64 × 32 px cells: 0-127 note names ("C4"), 128-255 the
    /// short key names of the All mode ("C4", "C♯", "D"), 256-351 katakana. White text, coverage in alpha.
    /// Must be called on a WPF (STA) thread; the pixels are then immutable and shared by every renderer.
    /// </summary>
    internal static GpuBackgroundImage GpuGlyphAtlas
    {
        get
        {
            if (s_gpuGlyphAtlas is not null) return s_gpuGlyphAtlas;
            const int columns = 16, rows = 24, cellWidth = 64, cellHeight = 32;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (var cell = 0; cell < GpuStageSimulation.AtlasKatakanaCell + 96; cell++)
                {
                    string text; bool bold;
                    if (cell < GpuStageSimulation.AtlasShortCell) { text = NoteLabels[cell]; bold = cell % 12 == 0; }
                    else if (cell < GpuStageSimulation.AtlasKatakanaCell) { var pitch = cell - GpuStageSimulation.AtlasShortCell; text = AllKeyLabels[pitch]; bold = pitch % 12 == 0; }
                    else { text = ((char)(0x30A0 + cell - GpuStageSimulation.AtlasKatakanaCell)).ToString(); bold = false; }
                    var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        bold ? LabelBoldTypeface : LabelTypeface, 22, Brushes.White, 1.0);
                    var x = cell % columns * cellWidth + (cellWidth - formatted.Width) / 2;
                    var y = cell / columns * cellHeight + (cellHeight - formatted.Height) / 2;
                    dc.DrawText(formatted, new Point(x, y));
                }
            }
            var bitmap = new RenderTargetBitmap(columns * cellWidth, rows * cellHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return s_gpuGlyphAtlas = new GpuBackgroundImage { Pixels = pixels, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Version = 1 };
        }
    }

    /// <summary>Hands the decoded background picture to the render thread (BGRA, straight from the file).</summary>
    private void PublishGpuBackground()
    {
        if (_gpu is null) return;
        var wanted = _visual.ShowBackground && _visual.BackgroundMode == "Image" ? _backgroundImage : null;
        if (ReferenceEquals(wanted, _gpuBackgroundSource) && (wanted is null || _gpu.Background is not null)) return;
        _gpuBackgroundSource = wanted;
        if (wanted is null) { _gpu.Background = null; return; }
        try
        {
            var source = wanted;
            // Very large photos are scaled down first: the stage never shows more than a screen of them.
            var scale = Math.Min(1, 2560.0 / Math.Max(source.PixelWidth, source.PixelHeight));
            if (scale < 1) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            _gpu.Background = new GpuBackgroundImage { Pixels = pixels, Width = converted.PixelWidth, Height = converted.PixelHeight, Version = Interlocked.Increment(ref s_gpuBackgroundVersion) };
        }
        catch { _gpu.Background = null; }
    }

    private void ForwardGpuState()
    {
        if (_gpu is null) return;
        _gpu.SetState(_notes, _position, _playing, _pressed);
        // Rainbow (time) colours move with the clock: refresh the colour table at the 30 Hz the software stage steps at.
        if (_visual.ColorMode == "RainbowTime" && Math.Abs(_elapsed - _gpuLookElapsed) >= 1 / 30.0) PublishGpuLook();
    }

    /// <summary>Copies the newest frame from the render thread into the bitmap the stage displays.</summary>
    private void OnGpuRendering(object? sender, EventArgs e)
    {
        var feed = _gpu;
        if (feed is null || !_gpuEmbedded) return;
        // the rainbow clock also ticks while nothing else calls SetState
        if (_visual.ColorMode == "RainbowTime" && !_playing) { _elapsed += 1 / 60.0; ForwardGpuState(); }
        var serial = feed.FrameSerial;
        if (serial == _gpuSerial) return;
        _gpuSerial = serial;
        var resized = false;
        feed.ReadFrontBuffer((pixels, width, height) =>
        {
            if (_gpuBitmap is null || _gpuBitmap.PixelWidth != width || _gpuBitmap.PixelHeight != height)
            {
                _gpuBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
                resized = true;
            }
            _gpuBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        });
        if (resized) InvalidateVisual();
    }

    /// <summary>
    /// The GPU path of <see cref="OnRender"/>: the frame (which already contains the camera transform),
    /// then the vector overlays that stay crisp in WPF: sheet music, hand marker, camera picture and readouts.
    /// </summary>
    private void RenderGpuFrame(DrawingContext dc, double width, double height, double keyTop, double lane)
    {
        dc.DrawImage(_gpuBitmap, new Rect(0, 0, width, height));
        var scale = _visual.CameraZoom / 100;
        var parallax = _visual.CameraParallax / 100;
        var offsetX = (width - width * scale) * _visual.CameraOffset / 100 + (_pointerX - .5) * parallax * 28;
        var offsetY = (height - height * scale) * .5 + (_pointerY - .5) * parallax * 20;
        dc.PushTransform(new TranslateTransform(offsetX, offsetY)); dc.PushTransform(new ScaleTransform(scale, scale));
        if (_visual.ShowSheet)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, keyTop + 2)));
            DrawSheet(dc, width, keyTop);
            dc.Pop();
        }
        DrawHandMarker(dc, width, keyTop, lane);
        DrawCameraOverlay(dc, width, height);
        if (_visual.ShowWatermark) DrawWatermark(dc, width, height);
        if (_visual.ShowCounter || _visual.ShowFps) DrawCounter(dc, width);
        dc.Pop(); dc.Pop();
    }
}
