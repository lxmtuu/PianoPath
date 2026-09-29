using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The animated backdrop behind the interface chrome.
///
/// Designed with a prestigious piano concert hall aesthetic: deep acoustic chamber gradient,
/// subtle harmonic resonance ribbons that evoke acoustic standing waves in a grand auditorium,
/// and delicate microscopic warm stardust motes drifting with organic fluid motion.
///
/// Everything is drawn with cached, frozen brushes so a full-screen backdrop costs minimal draw calls,
/// and the animation stops completely when hidden or when the motion level is Off.
/// </summary>
internal sealed class ChromeBackdrop : FrameworkElement
{
    private const int MaxParticles = 90;

    private readonly List<Mote> _motes = [];
    private readonly Dictionary<uint, Brush> _brushes = [];
    private readonly Random _random = new(20240831);

    private ShellTheme _theme = ShellThemes.Default;
    private double _motion = 1;
    private double _density = 1;
    private double _time;
    private bool _wantsFrames;
    private Drawing? _sky;
    private string _skyKey = "";
    private Brush? _vignette;
    private Size _vignetteSize;

    public ChromeBackdrop()
    {
        IsHitTestVisible = false;
        Focusable = false;
        Loaded += (_, _) => { FrameClock.Shared.Tick += OnFrame; SyncFrameDemand(); };
        Unloaded += (_, _) => { FrameClock.Shared.Tick -= OnFrame; SetFrameDemand(false); };
        IsVisibleChanged += (_, _) => SyncFrameDemand();
    }

    /// <summary>Current theme; the backdrop restyles in place when the palette changes.</summary>
    internal ShellTheme Theme => _theme;

    /// <summary>
    /// Publishes the theme, motion budget ("Off", "Calm", "Full") and particle density (0-200 %)
    /// from the visual settings. Off parks the element on a single static frame.
    /// </summary>
    internal void Configure(ShellTheme theme, string? motion, double density)
    {
        _theme = theme;
        _motion = motion switch { "Calm" => .5, "Off" => 0, _ => 1 };
        _density = Math.Clamp(density, 0, 200) / 100;
        _skyKey = ""; _vignette = null;
        SyncFrameDemand();
        RebuildField();
        InvalidateVisual();
    }

    private double ParticleCount => Math.Clamp((int)(MaxParticles * _density * (.35 + _motion * .65)), 0, MaxParticles * 2);

    private void SyncFrameDemand()
    {
        // Only a visible backdrop with motion needs frames; a hidden or still one must not keep the compositor awake.
        SetFrameDemand(_motion > 0 && IsVisible && ActualWidth > 2 && ActualHeight > 2);
    }

    private void SetFrameDemand(bool wanted)
    {
        if (wanted == _wantsFrames) return;
        _wantsFrames = wanted;
        if (wanted) FrameClock.Shared.Acquire(); else FrameClock.Shared.Release();
    }

    private void OnFrame(double delta)
    {
        if (!_wantsFrames) return;
        _time += delta * (.35 + _motion * .65);
        Advance(_time, delta);
        InvalidateVisual();
    }

    private void Advance(double time, double delta)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width < 2 || height < 2 || _motes.Count == 0) return;
        var speed = 18 * (.45 + _motion * .55);
        for (var i = 0; i < _motes.Count; i++)
        {
            var mote = _motes[i];
            mote.Y += mote.Fall * speed * delta * mote.Depth;
            mote.X += Math.Sin(time * mote.Sway + mote.Phase) * (4 + mote.Depth * 14) * delta + mote.Drift * delta * 6;
            mote.Spin += (float)(delta * (.15 + mote.Depth * .3) * (.5 + _motion * .5));
            if (mote.Y > height + 20) { mote.Y = -20; mote.X = _random.NextDouble() * width; }
            else if (mote.Y < -30) { mote.Y = height + 15; mote.X = _random.NextDouble() * width; }
            if (mote.X < -30) mote.X = width + 20; else if (mote.X > width + 30) mote.X = -20;
            _motes[i] = mote;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RebuildField(); SyncFrameDemand();
    }

    /// <summary>Scatters the particle field for the current size; called on resize and on a theme or density change.</summary>
    private void RebuildField()
    {
        _motes.Clear();
        var width = ActualWidth; var height = ActualHeight;
        if (width < 2 || height < 2) return;
        var count = (int)ParticleCount;
        for (var i = 0; i < count; i++)
        {
            var depth = .25 + _random.NextDouble() * .85;
            _motes.Add(new Mote
            {
                X = _random.NextDouble() * width,
                Y = _random.NextDouble() * height,
                Depth = depth,
                Size = (float)(1.2 + _random.NextDouble() * 2.4 * depth),
                Fall = (float)(-(0.15 + _random.NextDouble() * .45)), // gentle floating upward motes
                Sway = (float)(.3 + _random.NextDouble() * .9),
                Drift = (float)((_random.NextDouble() - .5) * .3),
                Spin = (float)(_random.NextDouble() * Math.PI * 2),
                Phase = (float)(_random.NextDouble() * Math.PI * 2),
                Alpha = (byte)(35 + _random.Next(95))
            });
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width < 2 || height < 2) return;
        dc.DrawDrawing(Sky(width, height));
        DrawAcousticWaves(dc, width, height);
        DrawMotes(dc, width, height);
        dc.DrawRectangle(Vignette(width, height), null, new Rect(0, 0, width, height));
    }

    /// <summary>Deep acoustic chamber gradient with subtle focal ambient warmth.</summary>
    private Drawing Sky(double width, double height)
    {
        var key = $"{_theme.Id}:{width:0}x{height:0}";
        if (_sky is not null && _skyKey == key) return _sky;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.15, 1) };
            sky.GradientStops.Add(new GradientStop(Blend(_theme.Window, _theme.Accent, .08), 0));
            sky.GradientStops.Add(new GradientStop(_theme.Window, .5));
            sky.GradientStops.Add(new GradientStop(Blend(_theme.Window, Colors.Black, .6), 1));
            sky.Freeze();
            dc.DrawRectangle(sky, null, new Rect(0, 0, width, height));

            // Subtle warm concert stage pool
            var pool = new RadialGradientBrush
            {
                Center = new Point(.4, .45), GradientOrigin = new Point(.4, .45),
                RadiusX = .85, RadiusY = .85, MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            pool.GradientStops.Add(new GradientStop(WithAlpha(_theme.Accent, 32), 0));
            pool.GradientStops.Add(new GradientStop(WithAlpha(_theme.Glow, 14), .4));
            pool.GradientStops.Add(new GradientStop(WithAlpha(_theme.Glow, 0), 1));
            pool.Freeze();
            dc.DrawRectangle(pool, null, new Rect(0, 0, width, height));
        }
        drawing.Freeze();
        _sky = drawing; _skyKey = key;
        return drawing;
    }

    /// <summary>Subtle acoustic standing resonance ribbons flowing smoothly across the concert backdrop.</summary>
    private void DrawAcousticWaves(DrawingContext dc, double width, double height)
    {
        var count = _motion > 0 ? 3 : 1;
        for (var i = 0; i < count; i++)
        {
            var phase = _time * (.12 + i * .04) + i * 2.3;
            var y = height * (.32 + i * .18) + Math.Sin(phase) * height * .035;
            var amplitude = height * (.045 + i * .012);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(-40, y), false, false);
                ctx.BezierTo(
                    new Point(width * .28, y - amplitude + Math.Sin(phase * 1.2) * amplitude * .4),
                    new Point(width * .68, y + amplitude + Math.Cos(phase) * amplitude * .35),
                    new Point(width + 40, y - amplitude * .25), true, false);
            }
            geometry.Freeze();
            var color = i % 2 == 0 ? _theme.Accent : _theme.Glow;
            var pen = new Pen(Brush(WithAlpha(color, (byte)(16 + i * 5))), height * (.08 + i * .02))
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    /// <summary>Delicate concert hall dust particles floating in the warm acoustic light.</summary>
    private void DrawMotes(DrawingContext dc, double width, double height)
    {
        if (_motes.Count == 0) return;
        foreach (var mote in _motes)
        {
            var alpha = (byte)Math.Clamp(mote.Alpha * (.6 + .4 * Math.Sin(_time * 1.2 + mote.Phase)), 10, 200);
            var color = WithAlpha(_theme.MoteAlt, alpha);
            var brush = Brush(color);
            var size = mote.Size;
            // Draw soft optical aura + crisp core
            dc.DrawEllipse(Brush(WithAlpha(color, (byte)(alpha * .22))), null, new Point(mote.X, mote.Y), size * 2.4, size * 2.4);
            dc.DrawEllipse(brush, null, new Point(mote.X, mote.Y), size, size);
        }
    }

    private Brush Vignette(double width, double height)
    {
        if (_vignette is not null && Math.Abs(_vignetteSize.Width - width) < 1 && Math.Abs(_vignetteSize.Height - height) < 1) return _vignette;
        var brush = new RadialGradientBrush
        {
            Center = new Point(.5, .45), GradientOrigin = new Point(.5, .45),
            RadiusX = .85, RadiusY = .95, MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .4));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(100, 0, 0, 0), .8));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(170, 0, 0, 0), 1));
        brush.Freeze();
        _vignette = brush; _vignetteSize = new Size(width, height);
        return brush;
    }

    private Brush Brush(Color color)
    {
        var key = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
        if (_brushes.TryGetValue(key, out var cached)) return cached;
        var created = new SolidColorBrush(color);
        created.Freeze();
        if (_brushes.Count > 1024) _brushes.Clear();
        _brushes[key] = created;
        return created;
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private struct Mote
    {
        public double X, Y, Depth;
        public float Size, Fall, Sway, Drift, Spin, Phase;
        public byte Alpha;
    }
}
