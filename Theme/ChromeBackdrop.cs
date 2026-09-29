using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The animated backdrop behind the interface chrome.
///
/// It is deliberately one element with one render pass: a themed gradient sky, a stage light cone,
/// drifting ribbons and a small particle field (blossom petals, glowing motes or golden dust,
/// depending on the active <see cref="ShellTheme"/>). Everything is drawn with cached, frozen
/// brushes so a full-screen backdrop costs a few dozen draw calls instead of allocating per frame,
/// and the animation stops completely when the element is hidden or the motion level is Off.
/// </summary>
internal sealed class ChromeBackdrop : FrameworkElement
{
    private const int MaxParticles = 130;

    private readonly List<Mote> _motes = [];
    private readonly Dictionary<uint, Brush> _brushes = [];
    private readonly Random _random = new(20240831);

    private ShellTheme _theme = ShellThemes.Default;
    private double _motion = 1;
    private double _density = 1;
    private double _time, _width, _height;
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
        _time += delta * (.4 + _motion);
        Advance(_time, delta);
        InvalidateVisual();
    }

    private void Advance(double time, double delta)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width < 2 || height < 2 || _motes.Count == 0) return;
        var speed = 28 * (.45 + _motion);
        for (var i = 0; i < _motes.Count; i++)
        {
            var mote = _motes[i];
            mote.Y += mote.Fall * speed * delta * mote.Depth;
            mote.X += Math.Sin(time * mote.Sway + mote.Phase) * (6 + mote.Depth * 22) * delta + mote.Drift * delta * 8;
            mote.Spin += delta * (.25 + mote.Depth) * (.6 + _motion);
            if (mote.Y > height + 30) { mote.Y = -30; mote.X = _random.NextDouble() * width; }
            else if (mote.Y < -40) { mote.Y = height + 20; mote.X = _random.NextDouble() * width; }
            if (mote.X < -40) mote.X = width + 30; else if (mote.X > width + 40) mote.X = -30;
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
            var depth = .25 + _random.NextDouble() * .95;
            _motes.Add(new Mote
            {
                X = _random.NextDouble() * width,
                Y = _random.NextDouble() * height,
                Depth = depth,
                Size = (float)((StyleIs(MoteKind.Petal) ? 4.5 : 1.4) + _random.NextDouble() * (StyleIs(MoteKind.Petal) ? 7.5 : 3.2) * depth),
                Fall = (float)(StyleIs(MoteKind.Rising) ? -(0.25 + _random.NextDouble() * .6) : .35 + _random.NextDouble() * 1.05),
                Sway = (float)(.4 + _random.NextDouble() * 1.5),
                Drift = (float)((_random.NextDouble() - .5) * .5),
                Spin = (float)(_random.NextDouble() * Math.PI * 2),
                Phase = (float)(_random.NextDouble() * Math.PI * 2),
                Alpha = (byte)(StyleIs(MoteKind.Gold) ? 40 + _random.Next(90) : 60 + _random.Next(120))
            });
        }
    }

    private enum MoteKind { Petal, Rising, Gold }

    private bool StyleIs(MoteKind kind) => _theme.Backdrop switch
    {
        BackdropStyle.Sakura => kind == MoteKind.Petal,
        BackdropStyle.Curtain => kind == MoteKind.Gold,
        _ => kind == MoteKind.Rising
    };

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        if (width < 2 || height < 2) return;
        dc.DrawDrawing(Sky(width, height));
        dc.DrawDrawing(StageLight(width, height));
        if (_theme.Backdrop == BackdropStyle.Curtain) DrawFolds(dc, width, height);
        DrawRibbons(dc, width, height);
        DrawMotes(dc, width, height);
        dc.DrawRectangle(Vignette(width, height), null, new Rect(0, 0, width, height));
    }

    /// <summary>Vertical sky gradient plus the theme's dominant colour pooling behind the content.</summary>
    private Drawing Sky(double width, double height)
    {
        var key = $"{_theme.Id}:{width:0}x{height:0}";
        if (_sky is not null && _skyKey == key) return _sky;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.25, 1) };
            sky.GradientStops.Add(new GradientStop(Blend(_theme.Window, _theme.Accent, .10), 0));
            sky.GradientStops.Add(new GradientStop(_theme.Window, .55));
            sky.GradientStops.Add(new GradientStop(Blend(_theme.Window, Colors.Black, .45), 1));
            sky.Freeze();
            dc.DrawRectangle(sky, null, new Rect(0, 0, width, height));

            var pool = new RadialGradientBrush
            {
                Center = new Point(.32, .55), GradientOrigin = new Point(.32, .55),
                RadiusX = .78, RadiusY = .85, MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            pool.GradientStops.Add(new GradientStop(WithAlpha(_theme.Accent, 44), 0));
            pool.GradientStops.Add(new GradientStop(WithAlpha(_theme.Glow, 22), .45));
            pool.GradientStops.Add(new GradientStop(WithAlpha(_theme.Glow, 0), 1));
            pool.Freeze();
            dc.DrawRectangle(pool, null, new Rect(0, 0, width, height));
        }
        drawing.Freeze();
        _sky = drawing; _skyKey = key;
        return drawing;
    }

    /// <summary>Two soft light cones from above, the visual signature of a lit recital stage.</summary>
    private Drawing StageLight(double width, double height)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            DrawCone(dc, width * .30, width * .22, height, WithAlpha(_theme.AccentAlt, 26));
            DrawCone(dc, width * .72, width * .18, height * .92, WithAlpha(_theme.Glow, 22));
        }
        drawing.Freeze();
        return drawing;
    }

    private static void DrawCone(DrawingContext dc, double apexX, double spread, double height, Color color)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(apexX - 8, -20), true, true);
            ctx.LineTo(new Point(apexX + 8, -20), true, false);
            ctx.LineTo(new Point(apexX + spread, height), true, false);
            ctx.LineTo(new Point(apexX - spread, height), true, false);
        }
        geometry.Freeze();
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 90), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 30), .5));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
        brush.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    /// <summary>Velvet folds: soft vertical bands that make the chrome read as a theatre curtain.</summary>
    private void DrawFolds(DrawingContext dc, double width, double height)
    {
        var bands = Math.Max(6, (int)(width / 150));
        var bandWidth = width / bands;
        for (var i = 0; i < bands; i++)
        {
            var shade = (float)(.5 + .5 * Math.Sin(_time * .25 + i * 1.1));
            var alpha = (byte)(10 + shade * 26);
            var x = i * bandWidth + Math.Sin(_time * .3 + i) * 3;
            dc.DrawRectangle(Brush(WithAlpha(Blend(_theme.PanelTop, _theme.Accent, .18), alpha)), null, new Rect(x, 0, bandWidth * .55, height));
        }
    }

    /// <summary>Long, slow ribbons of haze; rebuilt each frame because their shape is what animates.</summary>
    private void DrawRibbons(DrawingContext dc, double width, double height)
    {
        if (_theme.Backdrop == BackdropStyle.Curtain) return;
        var count = _motion > 0 ? 3 : 1;
        for (var i = 0; i < count; i++)
        {
            var phase = _time * (.16 + i * .05) + i * 2.1;
            var y = height * (.30 + i * .17) + Math.Sin(phase) * height * .05;
            var amplitude = height * (.06 + i * .015);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(-40, y), false, false);
                ctx.BezierTo(
                    new Point(width * .3, y - amplitude + Math.Sin(phase * 1.3) * amplitude * .5),
                    new Point(width * .65, y + amplitude + Math.Cos(phase) * amplitude * .4),
                    new Point(width + 40, y - amplitude * .3), true, false);
            }
            geometry.Freeze();
            var color = i % 2 == 0 ? _theme.Accent : _theme.Glow;
            var pen = new Pen(Brush(WithAlpha(color, (byte)(26 + i * 6))), height * (.10 + i * .03))
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawMotes(DrawingContext dc, double width, double height)
    {
        if (_motes.Count == 0) return;
        var petal = StyleIs(MoteKind.Petal);
        var gold = StyleIs(MoteKind.Gold);
        foreach (var mote in _motes)
        {
            var alpha = (byte)Math.Clamp(mote.Alpha * (.55 + .45 * Math.Sin(_time * 1.4 + mote.Phase)), 12, 235);
            var color = gold ? WithAlpha(_theme.PetalAlt, alpha)
                : petal ? WithAlpha(mote.Depth > .8 ? _theme.PetalAlt : _theme.Petal, alpha)
                : WithAlpha(_theme.Glow, alpha);
            var brush = Brush(color);
            if (petal)
            {
                dc.PushTransform(new TranslateTransform(mote.X, mote.Y));
                dc.PushTransform(new RotateTransform(mote.Spin * 57.2958));
                dc.DrawEllipse(brush, null, new Point(0, 0), mote.Size * 1.5, mote.Size * .62);
                dc.Pop(); dc.Pop();
            }
            else
            {
                var size = mote.Size * (gold ? .8 : 1.25);
                dc.DrawEllipse(Brush(WithAlpha(color, (byte)(alpha * .28))), null, new Point(mote.X, mote.Y), size * 2.6, size * 2.6);
                dc.DrawEllipse(brush, null, new Point(mote.X, mote.Y), size, size);
            }
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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 0, 0), .8));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(190, 0, 0, 0), 1));
        brush.Freeze();
        _vignette = brush; _vignetteSize = new Size(width, height);
        return brush;
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Frozen solid brushes cached by ARGB; a busy backdrop would otherwise allocate thousands of them.</summary>
    private Brush Brush(Color color)
    {
        var key = (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
        if (_brushes.TryGetValue(key, out var cached)) return cached;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        if (_brushes.Count > 512) _brushes.Clear();
        _brushes[key] = brush;
        return brush;
    }

    /// <summary>One drifting element: a blossom petal, a glowing mote or a grain of golden dust.</summary>
    private struct Mote
    {
        public double X, Y;
        public float Size;
        public float Fall;
        public float Sway;
        public float Drift;
        public float Spin;
        public float Phase;
        public byte Alpha;
        public double Depth;
    }
}
