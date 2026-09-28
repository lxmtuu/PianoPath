using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>The 88-key stage: lit key spill, glossy reflections, beveled key bodies, contact shadows and felt.</summary>
internal sealed partial class PianoStage
{
    private void DrawKeyboard(DrawingContext dc, double width, double height, double lane, double top)
    {
        var style = _visual.KeyboardStyle; var glass = style == "Glass"; var studio = style == "Studio";
        var halo = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var keyArea = height - top;
        var gloss = _visual.KeyGloss / 100; var bevel = _visual.KeyBevel / 100;
        var press = _visual.KeyPressDepth / 100 * 4;
        var whites = WhitePitches; var whiteWidth = width / whites.Length;
        if (_recordingBrightPass) { DrawKeyLights(dc, width, top, lane, whiteWidth); return; }
        // Keyboard bed: the dark casing the keys are set into.
        dc.DrawRectangle(Brush(Color.FromArgb(glass ? (byte)118 : (byte)252, 7, 7, 14)), null, new Rect(0, top, width, keyArea));
        DrawFallboardBand(dc, width, top, halo, keyArea);
        if (_visual.ShowKeyFelt)
        {
            var felt = AdjustColor(ParseColor(_visual.KeyFeltColor, Color.FromRgb(196, 28, 74)));
            dc.DrawRectangle(Brush(Color.FromArgb(130, felt.R, felt.G, felt.B)), null, new Rect(0, top + 1, width, 7));
            dc.DrawRectangle(FeltBrush(felt), null, new Rect(0, top + 2.4, width, 3.4));
        }
        DrawKeyLights(dc, width, top, lane, whiteWidth);
        // Lid shadow falling across the top of the keys.
        if (_visual.ShowKeyShadow)
        {
            dc.DrawRectangle(ShadowFallBrush(), null, new Rect(0, top + 4, width, Math.Min(20, keyArea * .18)));
        }
        var labelSize = Math.Clamp(whiteWidth * .48, 7, 11);
        var whiteBrush = WhiteKeyBrush(glass, studio, gloss);
        var whiteEdge = new Pen(Brush(glass ? Color.FromArgb(120, 210, 220, 255) : Color.FromArgb(165, 68, 72, 94)), glass ? .8 : .7); whiteEdge.Freeze();
        for (var i = 0; i < whites.Length; i++)
        {
            var pitch = whites[i];
            var rect = new Rect(i * whiteWidth, top + 5, whiteWidth - 1, keyArea - 5);
            var active = _activeKey[pitch];
            if (active && _visual.AnimateKeys)
            {
                var color = KeyColor(pitch); var lit = Blend(color, Colors.White, .32);
                var pressed = new Rect(rect.X, rect.Y + press, rect.Width, rect.Height - press);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(52 + _visual.KeyLighting * 1.35), color.R, color.G, color.B)), null, Inflate(new Rect(rect.X - 4, top - 1, rect.Width + 8, rect.Height + 6), 2), 7, 7);
                dc.DrawRoundedRectangle(KeyLightBrush(color, lit), new Pen(Brush(Color.FromArgb(255, lit.R, lit.G, lit.B)), 1), pressed, 3, 3);
                if (gloss > .02) dc.DrawRoundedRectangle(KeySheenBrush(Alpha(130 * gloss)), null,
                    new Rect(pressed.X + 1, pressed.Y + 1, Math.Max(1, pressed.Width - 2), Math.Min(12, pressed.Height * .34)), 3, 3);
            }
            else
            {
                dc.DrawRoundedRectangle(whiteBrush, whiteEdge, rect, studio ? 2.4 : 1.6, studio ? 2.4 : 1.6);
                if (gloss > .02) dc.DrawRectangle(KeySheenBrush(Alpha(96 * gloss)), null,
                    new Rect(rect.X + 1, rect.Y + 1.5, Math.Max(1, rect.Width - 2), Math.Min(14, rect.Height * .28)));
                if (bevel > .02)
                {
                    dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(150 * bevel), 255, 255, 255)), 1), new Point(rect.X + .6, rect.Y + 2), new Point(rect.X + .6, rect.Bottom - 3));
                    dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(120 * bevel), 0, 0, 0)), 1), new Point(rect.Right - .8, rect.Y + 3), new Point(rect.Right - .8, rect.Bottom - 2));
                    dc.DrawRectangle(Brush(Color.FromArgb(Alpha(74 * bevel), 0, 0, 0)), null, new Rect(rect.Right - 1.6, rect.Y, 1.6, rect.Height));
                }
                // Front lip: the visible thickness of the key block.
                var lip = Math.Min(12, rect.Height * .2);
                dc.DrawRectangle(KeyLipBrush(), null, new Rect(rect.X, rect.Bottom - lip, rect.Width, lip));
                dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(120 * bevel + 30), 255, 255, 255)), 1), new Point(rect.X, rect.Bottom - lip), new Point(rect.Right, rect.Bottom - lip));
                if (studio) dc.DrawRectangle(Brush(Color.FromArgb(46, 0, 0, 0)), null, new Rect(rect.X, rect.Bottom - 4, rect.Width, 4));
            }
            var showLabel = _visual.KeyLabels == "All" ? whiteWidth >= 13 : _visual.KeyLabels == "C" && pitch % 12 == 0;
            if (showLabel) DrawLabel(dc, _visual.KeyLabels == "All" ? NoteLabel(pitch).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-') + (pitch % 12 == 0 ? (pitch / 12 - 1).ToString() : "") : NoteLabel(pitch),
                new Point(rect.X + rect.Width / 2, height - 14), labelSize, active ? Colors.White : glass ? Color.FromRgb(208, 213, 232) : Color.FromRgb(84, 86, 110), pitch % 12 == 0);
        }
        // Contact shadows the black keys cast on the white keys below them, plus the keys themselves.
        var contact = _visual.KeyContactShadow / 100;
        var blackBrush = BlackKeyBrush(glass, studio, gloss);
        var blackEdge = new Pen(Brush(Color.FromArgb(200, 78, 72, 104)), .75); blackEdge.Freeze();
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!IsBlack(pitch)) continue;
            var x = WhitesBelow[pitch] * whiteWidth - whiteWidth * .29;
            var rect = new Rect(x, top + 4, whiteWidth * .58, keyArea * (.52 + _visual.KeyOverhang / 100 * .35));
            if (contact > .02)
            {
                dc.DrawRectangle(ContactShadowBrush(Alpha(150 * contact)), null, new Rect(rect.X + 1, rect.Y + 6, rect.Width - 2, rect.Height + Math.Min(16, keyArea * .2)));
                dc.DrawRectangle(ContactShadowBrush(Alpha(96 * contact)), null, new Rect(rect.X - 5, rect.Y + 8, 6, rect.Height * .8));
                dc.DrawRectangle(ContactShadowBrush(Alpha(96 * contact)), null, new Rect(rect.Right - 1, rect.Y + 8, 6, rect.Height * .8));
            }
            if (studio) dc.DrawRoundedRectangle(Brush(Color.FromArgb(130, 0, 0, 0)), null, new Rect(rect.X - 1.5, rect.Y + 2.5, rect.Width + 3, rect.Height + 3), 3, 3);
            if (_activeKey[pitch] && _visual.AnimateKeys)
            {
                var color = KeyColor(pitch); var lit = Blend(color, Colors.White, .15);
                var pressed = new Rect(rect.X, rect.Y + press * .6, rect.Width, rect.Height - press * .6);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(105 + _visual.KeyLighting * .9), color.R, color.G, color.B)), null, Inflate(rect, 5), 7, 7);
                dc.DrawRoundedRectangle(Brush(lit), new Pen(Brush(Colors.White), 1), pressed, 5, 5);
                if (gloss > .02) dc.DrawRoundedRectangle(KeySheenBrush(Alpha(120 * gloss)), null,
                    new Rect(pressed.X + 1, pressed.Y + 1, Math.Max(1, pressed.Width - 2), Math.Min(10, pressed.Height * .3)), 4, 4);
            }
            else
            {
                dc.DrawRoundedRectangle(blackBrush, blackEdge, rect, 3, 3);
                if (gloss > .02) dc.DrawRectangle(KeySheenBrush(Alpha(72 * gloss)), null,
                    new Rect(rect.X + 1.5, rect.Y + 1.5, Math.Max(1, rect.Width - 3), Math.Min(18, rect.Height * .42)));
                if (studio || bevel > .02)
                {
                    dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(90 * gloss + 40), 255, 255, 255)), 1), new Point(rect.X + 2, rect.Y + 1.4), new Point(rect.Right - 2, rect.Y + 1.4));
                    dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(120 * bevel), 0, 0, 0)), 1), new Point(rect.Right - 1, rect.Y + 4), new Point(rect.Right - 1, rect.Bottom - 3));
                }
            }
        }
    }

    /// <summary>Bright-pass entry point: the light around the keys without the key bodies.</summary>
    private void DrawKeyboardLightPass(DrawingContext dc, double width, double top, double lane)
        => DrawKeyLights(dc, width, top, lane, width / WhitePitches.Length);

    /// <summary>Light spilling out of every sounding key: an upward halo plus a reflection on the key face.</summary>
    private void DrawKeyLights(DrawingContext dc, double width, double top, double lane, double whiteWidth)
    {
        var glowRadius = _visual.KeyGlowRadius / 100;
        var keyArea = Math.Max(1, ActualHeight - top);
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var pulse = _keyPulse[pitch];
            if (pulse <= .04) continue;
            var color = KeyColor(pitch);
            var x = IsBlack(pitch) ? WhitesBelow[pitch] * whiteWidth : (WhitesBelow[pitch] + .5) * whiteWidth;
            var radiusX = 12 + glowRadius * 66;
            var radiusY = 8 + glowRadius * 56;
            dc.DrawEllipse(KeyGlowBrush(color, pulse), null, new Point(x, top - 2), radiusX, radiusY);
            if (glowRadius > .02) dc.DrawEllipse(KeyGlowBrush(color, pulse * .55), null, new Point(x, top + radiusY * .35), radiusX * 1.5, radiusY * 1.1);
            if (_visual.ShowKeyReflection)
            {
                var reflection = Math.Min(keyArea * .9, 30 + glowRadius * 40);
                dc.DrawRectangle(KeyReflectionBrush(color, pulse), null, new Rect(x - lane * .5, top, lane, reflection));
            }
        }
        if (_visual.KeyLighting > 0)
        {
            var halo = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
            dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(24 + _visual.KeyLighting * .85), halo.R, halo.G, halo.B)), 4 + _visual.BloomSize / 12), new Point(0, top + 1), new Point(width, top + 1));
        }
    }

    /// <summary>Glossy case lip above the keys: reads as the front edge of the piano.</summary>
    private void DrawFallboardBand(DrawingContext dc, double width, double top, Color halo, double keyArea)
    {
        if (!_visual.ShowFallboard) return;
        var band = Math.Clamp(keyArea * .1, 6, 14);
        var rect = new Rect(0, top - band, width, band);
        dc.DrawRectangle(FallboardBrush(), null, rect);
        dc.DrawRectangle(Brush(Color.FromArgb(58, halo.R, halo.G, halo.B)), null, new Rect(0, top - 1.6, width, 1.6));
        dc.DrawRectangle(Brush(Color.FromArgb(26, 255, 255, 255)), null, new Rect(0, rect.Y + band * .38, width, 1));
        dc.DrawRectangle(Brush(Color.FromArgb(150, 0, 0, 0)), null, new Rect(0, top, width, 2));
    }

    private Brush WhiteKeyBrush(bool glass, bool studio, double gloss)
    {
        var key = ((byte)10, (uint)(glass ? 2 : studio ? 1 : 0), (uint)Math.Round(Math.Clamp(gloss, 0, 1) * 16), 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var top = glass ? Color.FromArgb(170, 255, 255, 255) : Color.FromRgb(255, 255, 255);
        var upper = glass ? Color.FromArgb(96, 214, 226, 246) : Blend(Color.FromRgb(255, 255, 255), Color.FromRgb(224, 230, 244), .5 + gloss * .3);
        var mid = glass ? Color.FromArgb(88, 186, 199, 228) : Color.FromRgb(241, 244, 252);
        var bottom = glass ? Color.FromArgb(120, 152, 168, 205) : Color.FromRgb(188, 195, 214);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(top, 0));
        gradient.GradientStops.Add(new GradientStop(upper, .1 + gloss * .06));
        gradient.GradientStops.Add(new GradientStop(mid, .45));
        gradient.GradientStops.Add(new GradientStop(bottom, 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush BlackKeyBrush(bool glass, bool studio, double gloss)
    {
        var key = ((byte)11, (uint)(glass ? 2 : studio ? 1 : 0), (uint)Math.Round(Math.Clamp(gloss, 0, 1) * 16), 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var top = glass ? Color.FromArgb(230, 62, 66, 88) : studio ? Color.FromRgb(74, 78, 98) : Color.FromRgb(44, 46, 62);
        var highlight = glass ? Color.FromArgb(150, 200, 210, 245) : Color.FromRgb(120, 126, 150);
        var body = glass ? Color.FromArgb(215, 20, 22, 34) : Color.FromRgb(16, 17, 26);
        var bottom = glass ? Color.FromArgb(205, 6, 7, 14) : Color.FromRgb(4, 5, 9);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(top, 0));
        gradient.GradientStops.Add(new GradientStop(highlight, .04 + gloss * .05));
        gradient.GradientStops.Add(new GradientStop(body, .3));
        gradient.GradientStops.Add(new GradientStop(bottom, 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush KeyGlowBrush(Color color, double strength)
    {
        var bucket = (uint)Math.Round(Math.Clamp(strength, 0, 1) * 12);
        var key = ((byte)12, PackColor(color), bucket, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var intensity = Math.Clamp(_visual.KeyLighting, 0, 100);
        var scale = .35 + bucket / 12.0 * .65;
        var radial = new RadialGradientBrush { MappingMode = BrushMappingMode.RelativeToBoundingBox, Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha((58 + intensity * 1.5) * scale), color.R, color.G, color.B), 0));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha((18 + intensity * .5) * scale), color.R, color.G, color.B), .45));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        radial.Freeze();
        return CacheShade(key, radial);
    }

    /// <summary>A soft mirrored glow on the polished key face, as if the note were reflected.</summary>
    private Brush KeyReflectionBrush(Color color, double strength)
    {
        var bucket = (uint)Math.Round(Math.Clamp(strength, 0, 1) * 12);
        var key = ((byte)13, PackColor(color), bucket, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var scale = .3 + bucket / 12.0 * .7;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(110 * scale), color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(54 * scale), color.R, color.G, color.B), .35));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush KeyLightBrush(Color color, Color lit)
    {
        var key = ((byte)14, PackColor(color), PackColor(lit), 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(lit, 0));
        gradient.GradientStops.Add(new GradientStop(Blend(color, Colors.White, .12), .35));
        gradient.GradientStops.Add(new GradientStop(color, 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush KeySheenBrush(byte alpha)
    {
        var key = ((byte)15, alpha, 0u, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(alpha * .35), 255, 255, 255), .55));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush KeyLipBrush()
    {
        var key = ((byte)16, 0u, 0u, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(120, 26, 26, 40), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush ContactShadowBrush(byte alpha)
    {
        var key = ((byte)17, alpha, 0u, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 4, 3, 10), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(alpha * .45), 4, 3, 10), .55));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 4, 3, 10), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private static Brush ShadowFallBrush()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(165, 0, 0, 0), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        gradient.Freeze();
        return gradient;
    }

    private static Brush FallboardBrush()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(9, 9, 16), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 46), .45));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(16, 15, 25), .72));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(44, 42, 62), 1));
        gradient.Freeze();
        return gradient;
    }

    private static Brush FeltBrush(Color felt)
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Blend(felt, Colors.White, .3), 0));
        gradient.GradientStops.Add(new GradientStop(felt, .45));
        gradient.GradientStops.Add(new GradientStop(Blend(felt, Colors.Black, .45), 1));
        gradient.Freeze();
        return gradient;
    }

    private Color KeyColor(int pitch)
    {
        var color = _visual.PressedKeyColorMode == "Fixed" ? ParseColor(_visual.PressedKeyColor, Color.FromRgb(247, 130, 255)) : _activeKeyColor[pitch];
        return AdjustColor(color);
    }

    private void DrawLabel(DrawingContext dc, string text, Point center, double size, Color color, bool bold)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(bold ? "Segoe UI Semibold" : "Segoe UI"), size, Brush(color), _pixelsPerDip);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }
}
