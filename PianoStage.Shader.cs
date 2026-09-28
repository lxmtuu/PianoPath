using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Shader-style post processing. The emissive parts of the stage are recorded a second time into a
/// half-resolution "bright pass" visual; GPU blur passes then feed bloom, chromatic fringing and
/// anamorphic lens streaks, followed by grain, scanlines and cinematic bars.
/// </summary>
internal sealed partial class PianoStage
{
    private readonly BlurEffect _bloomEffect = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance, Radius = 12 };

    /// <summary>Re-records the emissive layer of the scene; the blur on this visual is what makes the glow soft.</summary>
    private void BuildBrightPass(double width, double keyTop, double lane)
    {
        var radius = (5 + _visual.BloomSize / 100 * 24 + _visual.AnamorphicStreaks / 100 * 6) * ShaderScale;
        _bloomEffect.Radius = Math.Clamp(radius, 0, 64);
        _brightVisual.Effect = _bloomEffect.Radius < .1 ? null : _bloomEffect;
        using var context = _brightVisual.RenderOpen();
        context.PushTransform(new ScaleTransform(ShaderScale, ShaderScale));
        _recordingBrightPass = true;
        try
        {
            if (_visual.ShowLightBeams && _visual.BeamIntensity > 0) DrawKeyBeams(context, width, keyTop, lane);
            DrawNoteTrails(context, width, keyTop, lane);
            DrawNotes(context, width, keyTop, lane);
            DrawLiveTrails(context, width, keyTop, lane);
            if (_visual.ShowFlame && _visual.FlameIntensity > 0) DrawFlames(context, width, keyTop, lane);
            if (_visual.ShowImpactRings) DrawRings(context);
            if (_visual.ShowEmbers || _visual.ShowWisps) DrawSparks(context);
            if (_visual.ShowHalo) DrawImpactLine(context, width, keyTop);
            if (_visual.ShowStars) DrawStars(context, width, keyTop);
            if (_visual.ShowKeys) DrawKeyboardLightPass(context, width, keyTop, lane);
        }
        finally { _recordingBrightPass = false; }
        context.Pop();
    }

    /// <summary>Bloom, fringe, streaks, grain and scanlines are composited here, in stage coordinates.</summary>
    private void CompositeShaderLayers(DrawingContext dc, double width, double height, double keyTop, double lane)
    {
        var bloom = _visual.BloomIntensity / 65.0;
        var streaks = _visual.AnamorphicStreaks / 100.0;
        var fringe = _visual.ChromaticAberration / 100.0;
        var area = new Rect(0, 0, width, keyTop + 2);
        if ((bloom > .02 || streaks > .02 || fringe > .02) && width > 40 && keyTop > 40)
        {
            BuildBrightPass(width, keyTop, lane);
            var brush = BloomBrush(width, keyTop);
            if (brush is not null && bloom > .02)
            {
                // Two passes: a tight core glow and a stretched, wider halo.
                dc.PushOpacity(Math.Clamp(bloom * .78, 0, 1));
                dc.DrawRectangle(brush, null, area);
                dc.Pop();
                dc.PushOpacity(Math.Clamp(bloom * .42, 0, 1));
                dc.DrawRectangle(brush, null, new Rect(-width * .025, -keyTop * .03, width * 1.05, keyTop * 1.06));
                dc.Pop();
            }
            if (brush is not null && fringe > .02) DrawChromaticFringe(dc, brush, area, fringe);
            if (streaks > .02) DrawAnamorphicStreaks(dc, width, keyTop, lane, streaks);
        }
        if (_visual.FilmGrain > 0) DrawFilmGrain(dc, width, height);
        if (_visual.Scanlines > 0) DrawScanlines(dc, width, height);
    }

    private Brush? BloomBrush(double width, double keyTop)
    {
        if (width < 1 || keyTop < 1) return null;
        if (_bloomBrush is not null && Math.Abs(_bloomBrushWidth - width) < .5 && Math.Abs(_bloomBrushHeight - keyTop) < .5) return _bloomBrush;
        var brush = new VisualBrush(_brightVisual)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, width * ShaderScale, keyTop * ShaderScale),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, width, keyTop)
        };
        _bloomBrush = brush; _bloomBrushWidth = width; _bloomBrushHeight = keyTop;
        return brush;
    }

    /// <summary>Lens color fringing: the blurred bright pass acts as an alpha mask for two offset color plates.</summary>
    private void DrawChromaticFringe(DrawingContext dc, Brush mask, Rect area, double amount)
    {
        var shift = 1 + amount * 9;
        var plate = new Rect(area.X - shift * 3, area.Y - shift * 3, area.Width + shift * 6, area.Height + shift * 6);
        dc.PushOpacity(Math.Clamp(amount * .55, 0, .8));
        dc.PushOpacityMask(mask);
        dc.PushTransform(new TranslateTransform(shift, shift * .35));
        dc.DrawRectangle(Brush(Color.FromRgb(255, 46, 120)), null, plate);
        dc.Pop();
        dc.PushTransform(new TranslateTransform(-shift, -shift * .35));
        dc.DrawRectangle(Brush(Color.FromRgb(38, 210, 255)), null, plate);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Anamorphic lens streaks: a very wide, very thin flare across every lit key.</summary>
    private void DrawAnamorphicStreaks(DrawingContext dc, double width, double keyTop, double lane, double amount)
    {
        var bloom = Math.Max(.25, _visual.BloomIntensity / 65.0);
        dc.PushOpacity(Math.Clamp(amount * bloom, 0, 1));
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var pulse = _keyPulse[pitch];
            if (pulse <= .04) continue;
            var color = AdjustColor(_activeKey[pitch] ? _activeKeyColor[pitch] : NoteColor(pitch, 0));
            var x = (pitch - FirstPitch + .5) * lane;
            var y = keyTop - 3;
            var halfLength = lane * (5 + amount * 22) * (.45 + pulse * .55);
            var brush = StreakBrush(color, pulse);
            dc.DrawEllipse(brush, null, new Point(x, y), halfLength, .8 + amount * 2.2);
            dc.DrawEllipse(brush, null, new Point(x, y), halfLength * .45, 1.4 + amount * 3.4);
        }
        dc.Pop();
    }

    private Brush StreakBrush(Color color, double strength)
    {
        var key = ((byte)7, PackColor(color), PackColor(Color.FromArgb(Alpha(255 * strength), color.R, color.G, color.B)), 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * strength), color.R, color.G, color.B), .42));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(225 * strength), 255, 255, 255), .5));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * strength), color.R, color.G, color.B), .58));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private void DrawFilmGrain(DrawingContext dc, double width, double height)
    {
        var offset = _grainSeed * 160;
        var brush = new ImageBrush(GrainTexture)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(offset, offset * .7, 160, 160)
        };
        brush.Freeze();
        dc.PushOpacity(Math.Clamp(_visual.FilmGrain / 100 * .38, 0, .55));
        dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        dc.Pop();
    }

    private void DrawScanlines(DrawingContext dc, double width, double height)
    {
        var brush = new ImageBrush(ScanlineTexture)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 4, 4)
        };
        brush.Freeze();
        dc.PushOpacity(Math.Clamp(_visual.Scanlines / 100 * .55, 0, .7));
        dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        dc.Pop();
    }

    private void DrawCinematicBars(DrawingContext dc, double width, double height)
    {
        var bar = height * Math.Clamp(_visual.CinematicBars, 0, 25) / 100;
        if (bar < .5) return;
        dc.DrawRectangle(Ink, null, new Rect(0, 0, width, bar));
        dc.DrawRectangle(Ink, null, new Rect(0, height - bar, width, bar));
        var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(90, 0, 0, 0), 0));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        edge.Freeze();
        dc.DrawRectangle(edge, null, new Rect(0, bar, width, 22));
        var bottom = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        bottom.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        bottom.GradientStops.Add(new GradientStop(Color.FromArgb(90, 0, 0, 0), 1));
        bottom.Freeze();
        dc.DrawRectangle(bottom, null, new Rect(0, height - bar - 22, width, 22));
    }

    /// <summary>Tileable monochrome value noise used as animated film grain.</summary>
    private static BitmapSource BuildNoiseTexture(int size, int cellSize, double contrast)
    {
        var random = new Random(20240927);
        var grid = new double[size / cellSize + 1, size / cellSize + 1];
        for (var x = 0; x < grid.GetLength(0); x++) for (var y = 0; y < grid.GetLength(1); y++) grid[x, y] = random.NextDouble();
        var stride = size * 4; var pixels = new byte[stride * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = x / (double)cellSize; var v = y / (double)cellSize;
                var x0 = (int)u % (grid.GetLength(0) - 1); var y0 = (int)v % (grid.GetLength(1) - 1);
                var fx = u - (int)u; var fy = v - (int)v;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                var top = grid[x0, y0] + (grid[x0 + 1, y0] - grid[x0, y0]) * fx;
                var bottom = grid[x0, y0 + 1] + (grid[x0 + 1, y0 + 1] - grid[x0, y0 + 1]) * fx;
                var noise = .5 + ((top + (bottom - top) * fy) - .5) * contrast / 96;
                var value = (byte)Math.Clamp(noise * 255, 0, 255);
                var offset = y * stride + x * 4;
                pixels[offset] = value; pixels[offset + 1] = value; pixels[offset + 2] = value; pixels[offset + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Tileable CRT scanline texture: one darker row every four pixels.</summary>
    private static BitmapSource BuildScanlineTexture()
    {
        const int size = 4; var stride = size * 4; var pixels = new byte[stride * size];
        for (var y = 0; y < size; y++)
        {
            var alpha = (byte)(y % 2 == 0 ? 0 : 150);
            for (var x = 0; x < size; x++)
            {
                var offset = y * stride + x * 4;
                pixels[offset] = 0; pixels[offset + 1] = 0; pixels[offset + 2] = 0; pixels[offset + 3] = alpha;
            }
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Tileable value-noise alpha texture used as an opacity mask for the burning note style.</summary>
    private static BitmapSource BuildFireMask()
    {
        const int gridX = 12, gridY = 48;
        var random = new Random(4242);
        var coarse = new double[gridX, gridY]; var fine = new double[gridX * 2, gridY * 2];
        for (var x = 0; x < gridX; x++) for (var y = 0; y < gridY; y++) coarse[x, y] = random.NextDouble();
        for (var x = 0; x < gridX * 2; x++) for (var y = 0; y < gridY * 2; y++) fine[x, y] = random.NextDouble();
        static double Sample(double[,] grid, int sizeX, int sizeY, double u, double v)
        {
            var x0 = (int)Math.Floor(u) % sizeX; var y0 = (int)Math.Floor(v) % sizeY; var x1 = (x0 + 1) % sizeX; var y1 = (y0 + 1) % sizeY;
            var fx = u - Math.Floor(u); var fy = v - Math.Floor(v); fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            var top = grid[x0, y0] + (grid[x1, y0] - grid[x0, y0]) * fx; var bottom = grid[x0, y1] + (grid[x1, y1] - grid[x0, y1]) * fx;
            return top + (bottom - top) * fy;
        }
        var stride = FireMaskWidth * 4; var pixels = new byte[stride * FireMaskHeight];
        for (var y = 0; y < FireMaskHeight; y++)
        {
            for (var x = 0; x < FireMaskWidth; x++)
            {
                var u = x / (double)FireMaskWidth; var v = y / (double)FireMaskHeight;
                var noise = Sample(coarse, gridX, gridY, u * gridX, v * gridY) * .68 + Sample(fine, gridX * 2, gridY * 2, u * gridX * 2, v * gridY * 2) * .32;
                var alpha = Math.Clamp((noise - .34) / .2, 0, 1);
                var a = (byte)(alpha * 255); var offset = y * stride + x * 4;
                pixels[offset] = a; pixels[offset + 1] = a; pixels[offset + 2] = a; pixels[offset + 3] = a;
            }
        }
        var bitmap = BitmapSource.Create(FireMaskWidth, FireMaskHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
