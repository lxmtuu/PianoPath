using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Background, atmosphere (stars, dust, grid, beams), the hit line, vignette and the stage HUD.</summary>
internal sealed partial class PianoStage
{
    private int _activeKeyCount;
    private Color _lastImpactColor = Color.FromRgb(198, 110, 255);

    private void DrawBackdrop(DrawingContext dc, double width, double keyTop, double lane)
    {
        var baseColor = ParseColor(_visual.BackgroundColor, Colors.Black);
        dc.DrawRectangle(Brush(baseColor), null, new Rect(-width * .6, -keyTop * .6, width * 2.2, keyTop * 2.4));
        if (!_visual.ShowBackground) return;
        if (_visual.BackgroundMode == "Image" && _backgroundImage is not null)
        {
            var imageScale = Math.Max(width / _backgroundImage.Width, keyTop / _backgroundImage.Height);
            var imageWidth = _backgroundImage.Width * imageScale; var imageHeight = _backgroundImage.Height * imageScale;
            dc.DrawImage(_backgroundImage, new Rect((width - imageWidth) / 2, (keyTop - imageHeight) / 2, imageWidth, imageHeight));
            if (_visual.BackgroundDim > 0) dc.DrawRectangle(Brush(Color.FromArgb((byte)(_visual.BackgroundDim * 2.1), 0, 0, 0)), null, new Rect(-width * .6, -keyTop * .6, width * 2.2, keyTop * 2.4));
        }
        if (_visual.BackgroundGradient)
        {
            var halo = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
            var aura = new RadialGradientBrush { Center = new Point(.5, .22), GradientOrigin = new Point(.5, .22), RadiusX = .78, RadiusY = .95, MappingMode = BrushMappingMode.RelativeToBoundingBox };
            aura.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(46 * _visual.BloomIntensity / 65), halo.R, halo.G, halo.B), 0));
            aura.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(18 * _visual.BloomIntensity / 65), 60, 52, 150), .5));
            aura.GradientStops.Add(new GradientStop(Color.FromArgb(0, 5, 4, 13), 1));
            aura.Freeze();
            dc.DrawRectangle(aura, null, new Rect(0, 0, width, keyTop));
        }
        if (_visual.ShowStars) DrawStars(dc, width, keyTop);
        if (_visual.ShowGrid && _visual.GridIntensity > 0) DrawStageGrid(dc, width, keyTop);
        if (_visual.BackgroundGuide) DrawLanes(dc, width, keyTop, lane);
    }

    private void DrawStars(DrawingContext dc, double width, double height)
    {
        // The star field is cleared on resize; rebuilding it here every frame made the stars flicker like noise.
        if (_stars.Count == 0) RebuildStars(width, height);
        var visible = (int)(_stars.Count * Math.Clamp(_visual.StarDensity / 100, 0, 1));
        var tint = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        for (var i = 0; i < visible; i++)
        {
            var star = _stars[i];
            var twinkle = .35 + .65 * (.5 + .5 * Math.Sin(_elapsed * star.Speed + star.Phase));
            var drift = (_elapsed * star.Speed * 1.6 + star.Phase * 12) % (height + 40);
            var y = star.Y - drift;
            if (y < -10) y += height + 40;
            var alpha = (byte)(star.Alpha * twinkle);
            var color = i % 5 == 0 ? Color.FromArgb(alpha, tint.R, tint.G, tint.B) : Color.FromArgb(alpha, 222, 226, 255);
            dc.DrawEllipse(Brush(color), null, new Point(star.X, y), star.Size, star.Size);
            if (star.Size > 1.1) dc.DrawEllipse(Brush(Color.FromArgb((byte)(alpha * .2), color.R, color.G, color.B)), null, new Point(star.X, y), star.Size * 3.4, star.Size * 3.4);
        }
    }

    private void RebuildStars(double width, double height)
    {
        _stars.Clear(); var count = (int)Math.Clamp(width * height / 5200, 90, 320);
        for (var i = 0; i < count; i++) _stars.Add(new Star(_random.NextDouble() * width, _random.NextDouble() * height, .45 + _random.NextDouble() * 1.2, (byte)(18 + _random.Next(58)), .5 + _random.NextDouble() * 2.0, _random.NextDouble() * 7));
    }

    /// <summary>Perspective grid receding behind the keyboard, drawn with the halo color.</summary>
    private void DrawStageGrid(DrawingContext dc, double width, double keyTop)
    {
        var strength = _visual.GridIntensity / 100;
        var color = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var alpha = Alpha(34 * strength);
        var pen = new Pen(Brush(Color.FromArgb(alpha, color.R, color.G, color.B)), .9); pen.Freeze();
        const int rows = 14;
        for (var i = 1; i <= rows; i++)
        {
            var t = i / (double)rows;
            var y = keyTop - keyTop * Math.Pow(t, 1.9) - 1;
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
        const int columns = 18;
        var vanish = width * .5;
        for (var i = 0; i <= columns; i++)
        {
            var x = i / (double)columns * width;
            var topX = vanish + (x - vanish) * .34;
            dc.DrawLine(pen, new Point(topX, 0), new Point(x, keyTop));
        }
        dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(70 * strength), color.R, color.G, color.B)), 1.6), new Point(0, keyTop - 1), new Point(width, keyTop - 1));
    }

    private void DrawLanes(DrawingContext dc, double width, double height, double lane)
    {
        for (var i = 0; i <= KeyCount; i++)
        {
            var pitch = FirstPitch + i;
            var alpha = pitch % 12 == 0 ? 26 : pitch % 12 is 2 or 4 or 7 or 9 or 11 ? 11 : 5;
            var color = Color.FromArgb((byte)alpha, 186, 141, 255);
            dc.DrawLine(new Pen(Brush(color), pitch % 12 == 0 ? 1 : .6), new Point(i * lane, 0), new Point(i * lane, height));
        }
    }

    private void DrawHorizonGlow(DrawingContext dc, double width, double keyTop)
    {
        var color = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var glowHeight = 40 + _visual.HorizonGlow * 2.6;
        var brush = HorizonBrush(color, Alpha(_visual.HorizonGlow * 1.7), Alpha(_visual.HorizonGlow));
        dc.DrawRectangle(brush, null, new Rect(0, keyTop - glowHeight, width, glowHeight));
    }

    private void DrawKeyBeams(DrawingContext dc, double width, double keyTop, double lane)
    {
        // Soft light columns rise from every sounding key, tinted with that key's note color.
        var beamWidth = Math.Max(18, lane * 2.8);
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!_activeKey[pitch]) continue;
            var color = AdjustColor(_activeKeyColor[pitch]);
            var brush = BeamBrush(color, Alpha(Math.Clamp(_visual.BeamIntensity, 0, 255)));
            var x = (pitch - FirstPitch + .5) * lane;
            dc.DrawRectangle(brush, null, new Rect(x - beamWidth / 2, 0, beamWidth, keyTop));
        }
    }

    private void DrawDust(DrawingContext dc)
    {
        var width = ActualWidth; var height = Math.Max(1, ActualHeight - KeyboardHeight);
        var tint = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var strength = Math.Clamp(_visual.DustDensity / 100, 0, 1);
        foreach (var mote in _dust)
        {
            var twinkle = .45 + .55 * (.5 + .5 * Math.Sin(mote.Phase));
            var alpha = Alpha(150 * strength * twinkle * (mote.Size / 2.2));
            if (alpha < 3) continue;
            var color = mote.Size > 1.6 ? Color.FromArgb(alpha, tint.R, tint.G, tint.B) : Color.FromArgb(alpha, 255, 255, 255);
            dc.DrawEllipse(DustGlowBrush(color), null, new Point(mote.X, mote.Y), mote.Size * 3.6, mote.Size * 3.6);
            dc.DrawEllipse(Brush(color), null, new Point(mote.X, mote.Y), mote.Size, mote.Size);
        }
    }

    private void AdvanceAtmosphere(double dt, double hitY)
    {
        if (dt <= 0 || ActualWidth < 1) return;
        var width = ActualWidth; var height = Math.Max(1, hitY);
        var target = (int)Math.Clamp(_visual.DustDensity / 100 * Math.Clamp(width * height / 11000, 24, 220), 0, 360);
        while (_dust.Count < target) { var mote = new Dust(); ResetDust(mote, width, height, seed: true); _dust.Add(mote); }
        while (_dust.Count > target) _dust.RemoveAt(_dust.Count - 1);
        for (var i = 0; i < _dust.Count; i++)
        {
            var mote = _dust[i];
            mote.Phase += mote.Spin * dt;
            mote.X += (mote.Vx + Math.Sin(mote.Phase) * 7) * dt;
            mote.Y += mote.Vy * dt;
            if (mote.Y < -14 || mote.X < -24 || mote.X > width + 24) ResetDust(mote, width, height, seed: false);
        }
    }

    private void ResetDust(Dust mote, double width, double height, bool seed)
    {
        mote.X = _random.NextDouble() * width;
        mote.Y = seed ? _random.NextDouble() * height : height + _random.NextDouble() * 40;
        mote.Vx = (_random.NextDouble() - .5) * 7;
        mote.Vy = -(5 + _random.NextDouble() * 16);
        mote.Size = .5 + _random.NextDouble() * 1.7;
        mote.Phase = _random.NextDouble() * Math.PI * 2;
        mote.Spin = .6 + _random.NextDouble() * 1.7;
    }

    private void DrawImpactLine(DrawingContext dc, double width, double y)
    {
        var intensity = _visual.HaloIntensity / 100;
        if (intensity <= .01) return;
        var energy = Math.Clamp(_activeKeyCount / 6.0, 0, 1);
        var pulse = 1 + _impactPulse * .8 * _visual.HaloPulse / 100 + energy * _visual.HaloPulse / 100;
        var baseColor = HaloColorForMode();
        var coreColor = _visual.HaloTintMode switch
        {
            "Note" => _lastImpactColor,
            _ => baseColor
        };
        var thickness = 1 + _visual.HaloThickness / 100 * 4.4;
        var glowWidth = (4 + _visual.HaloGlowSize / 100 * 34) * (1 + _impactPulse * .35);
        dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(16 * intensity * pulse), baseColor.R, baseColor.G, baseColor.B)), glowWidth * 3.4), new Point(0, y), new Point(width, y));
        dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(52 * intensity * pulse), baseColor.R, baseColor.G, baseColor.B)), glowWidth * 1.35), new Point(0, y), new Point(width, y));
        dc.DrawLine(new Pen(HaloBrush(width, coreColor), thickness), new Point(0, y), new Point(width, y));
        var flareX = width * .5;
        dc.DrawEllipse(Brush(Color.FromArgb(Alpha(70 * intensity * pulse), baseColor.R, baseColor.G, baseColor.B)), null, new Point(flareX, y), 120 + 240 * pulse * energy, 6 + 10 * pulse);
        if (_impactPulse > .02)
        {
            for (var i = 0; i < 3; i++)
            {
                var t = Math.Clamp(_impactPulse - i * .18, 0, 1);
                dc.DrawEllipse(null, new Pen(Brush(Color.FromArgb(Alpha(90 * t * intensity), 255, 255, 255)), 1.2), new Point(flareX, y), (60 + i * 90) * (1.4 - t * .4), (4 + i * 5) * (1.4 - t * .4));
            }
        }
    }

    private Color HaloColorForMode() => _visual.HaloTintMode switch
    {
        "Note" => _lastImpactColor,
        _ => AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)))
    };

    /// <summary>Core line brush: single color, full rainbow, or a gradient around the last impact color.</summary>
    private Brush HaloBrush(double width, Color core)
    {
        var mode = _visual.HaloTintMode;
        var key = ((byte)20, PackColor(core), mode == "Rainbow" ? 1u : 0u, (uint)Math.Round(width), 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        switch (mode)
        {
            case "Rainbow":
                gradient.GradientStops.Add(new GradientStop(AdjustColor(NoteColor(FirstPitch, 0)), 0));
                gradient.GradientStops.Add(new GradientStop(AdjustColor(core), .5));
                gradient.GradientStops.Add(new GradientStop(AdjustColor(NoteColor(FirstPitch + KeyCount - 1, 0)), 1));
                break;
            case "Note":
                gradient.GradientStops.Add(new GradientStop(Blend(core, Colors.White, .55), 0));
                gradient.GradientStops.Add(new GradientStop(core, .5));
                gradient.GradientStops.Add(new GradientStop(Blend(core, Colors.White, .55), 1));
                break;
            default:
                gradient.GradientStops.Add(new GradientStop(Blend(core, Colors.White, .18), 0));
                gradient.GradientStops.Add(new GradientStop(Blend(core, Colors.White, .6), .5));
                gradient.GradientStops.Add(new GradientStop(Blend(core, Colors.White, .18), 1));
                break;
        }
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush HorizonBrush(Color color, byte alphaEdge, byte alphaBase)
    {
        var key = ((byte)21, PackColor(color), alphaEdge, alphaBase, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(alphaEdge * .35), color.R, color.G, color.B), .6));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alphaBase, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush BeamBrush(Color color, byte alpha)
    {
        var key = ((byte)22, PackColor(color), alpha, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(10 + alpha * .5), color.R, color.G, color.B), .78));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(20 + alpha * .9), color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush DustGlowBrush(Color color)
    {
        var key = ((byte)23, PackColor(color), 0u, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var radial = new RadialGradientBrush { MappingMode = BrushMappingMode.RelativeToBoundingBox, Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(color.A * .35), color.R, color.G, color.B), 0));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        radial.Freeze();
        return CacheShade(key, radial);
    }

    private void DrawVignette(DrawingContext dc, double width, double keyTop)
    {
        if (_vignetteBrush is null || Math.Abs(_vignetteValue - _visual.Vignette) > .01)
        {
            var vignette = new RadialGradientBrush { Center = new Point(.5, .45), GradientOrigin = new Point(.5, .45), RadiusX = .78, RadiusY = .82, MappingMode = BrushMappingMode.RelativeToBoundingBox };
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), .55));
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(_visual.Vignette * 2.2), 0, 0, 0), 1));
            vignette.Freeze(); _vignetteBrush = vignette; _vignetteValue = _visual.Vignette;
        }
        dc.DrawRectangle(_vignetteBrush, null, new Rect(0, 0, width, keyTop));
    }

    private void DrawWatermark(DrawingContext dc, double width, double height)
    {
        var text = new FormattedText("KEYFLOW", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11, Brush(Color.FromArgb(120, 232, 224, 250)), _pixelsPerDip);
        var x = width / 2 - text.Width / 2;
        var y = height - KeyboardHeight - text.Height - 18;
        dc.DrawRectangle(Brush(Color.FromArgb(40, 12, 10, 22)), null, new Rect(x - 10, y - 4, text.Width + 20, text.Height + 8));
        dc.DrawText(text, new Point(x, y));
    }

    private void DrawCounter(DrawingContext dc, double width)
    {
        var parts = new List<string>();
        if (_visual.ShowCounter) parts.Add($"{_pressed.Count:00} KEYS");
        if (_visual.ShowFps) parts.Add($"{_fps:0} FPS · {_sparks.Count} PARTICLES");
        var text = new FormattedText(string.Join("   ", parts), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 12, Brush(Color.FromArgb(200, 243, 229, 255)), _pixelsPerDip);
        dc.DrawText(text, new Point(width - text.Width - 30, 28));
    }
}
