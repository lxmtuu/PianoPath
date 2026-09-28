using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Falling notes, live key trails, their shading helpers and the note color model.</summary>
internal sealed partial class PianoStage
{
    /// <summary>Blocks the bright pass draws softer or harder depending on the shader threshold.</summary>
    private double BrightGate => Math.Clamp(1.25 - _visual.BloomThreshold / 100 * 1.1, .1, 1.25);

    private void DrawNotes(DrawingContext dc, double width, double hitY, double lane)
    {
        if (!_visual.ShowNotes || !_playing) return;
        var noteSpeed = FallSpeed * _visual.NoteFallSpeed / 550;
        var lookBehind = hitY / noteSpeed + 1;
        var latestStart = _position + lookBehind;
        var noteWidth = lane * _visual.NoteWidth / 100;
        var inset = (lane - noteWidth) / 2;
        var gap = Math.Min(_visual.NoteGap, 12);
        // Notes are sorted by start time: anything still on screen started no earlier than (position - 1 s - longest note).
        for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - 1 - _maxNoteDuration); i < _notes.Count; i++)
        {
            var note = _notes[i];
            if (note.Start > latestStart) break;
            if (note.Pitch < FirstPitch || note.Pitch >= FirstPitch + KeyCount || note.End < _position - 1) continue;
            var noteHeight = Math.Clamp(note.Duration * noteSpeed - gap, _visual.NoteMinLength, hitY * .9);
            var bottom = hitY - (note.Start - _position) * noteSpeed;
            var top = bottom - noteHeight;
            if (top > hitY || bottom < 0) continue;
            var rect = new Rect((note.Pitch - FirstPitch) * lane + inset, top, noteWidth, noteHeight);
            var color = note.Played ? Color.FromRgb(82, 237, 208) : note.Missed ? Color.FromRgb(255, 83, 113) : NoteColor(note.Pitch, note.Track);
            var sounding = note.Start <= _position && note.End > _position;
            DrawConfiguredNote(dc, rect, color, note.Played ? .42 : 1, sounding, note.Pitch);
        }
    }

    /// <summary>Comet trails that stream off the top of every falling note.</summary>
    private void DrawNoteTrails(DrawingContext dc, double width, double hitY, double lane)
    {
        if (!_visual.ShowNotes || !_playing || _visual.NoteTrail <= 0) return;
        var noteSpeed = FallSpeed * _visual.NoteFallSpeed / 550;
        var latestStart = _position + hitY / noteSpeed + 1;
        var noteWidth = lane * _visual.NoteWidth / 100;
        var inset = (lane - noteWidth) / 2;
        var trail = 16 + _visual.NoteTrail * 3.4;
        var gap = Math.Min(_visual.NoteGap, 12);
        for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - 1 - _maxNoteDuration); i < _notes.Count; i++)
        {
            var note = _notes[i];
            if (note.Start > latestStart) break;
            if (note.Pitch < FirstPitch || note.Pitch >= FirstPitch + KeyCount || note.End < _position - 1) continue;
            var noteHeight = Math.Clamp(note.Duration * noteSpeed - gap, _visual.NoteMinLength, hitY * .9);
            var bottom = hitY - (note.Start - _position) * noteSpeed;
            var top = bottom - noteHeight;
            if (top > hitY || bottom < 0 || top <= 0) continue;
            var color = AdjustColor(note.Played ? Color.FromRgb(82, 237, 208) : note.Missed ? Color.FromRgb(255, 83, 113) : NoteColor(note.Pitch, note.Track));
            var strength = _visual.NoteTrail / 100 * (_recordingBrightPass ? BrightGate * .8 : .55) * _visual.NoteTint / 100;
            var rect = new Rect((note.Pitch - FirstPitch) * lane + inset, Math.Max(0, top - trail), noteWidth, Math.Min(trail, top));
            if (rect.Height < 2) continue;
            dc.DrawRectangle(TrailBrush(color, Alpha(150 * strength)), null, rect);
        }
    }

    private void DrawLiveTrails(DrawingContext dc, double width, double hitY, double lane)
    {
        var noteWidth = lane * Math.Max(_visual.NoteWidth, 60) / 100;
        var inset = (lane - noteWidth) / 2;
        foreach (var trail in _liveTrails)
        {
            if (!_visual.ShowNotes) break;
            var y = 28 + trail.Age * _visual.NoteFallSpeed;
            var tailY = Math.Max(0, y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed);
            var bottom = Math.Min(hitY + 6, y);
            if (bottom <= tailY) continue;
            var opacity = Math.Clamp(1 - tailY / Math.Max(1, hitY + 18), .08, 1) * _visual.NoteTint / 100;
            var r = new Rect((trail.Pitch - FirstPitch) * lane + inset, tailY, noteWidth, bottom - tailY);
            DrawConfiguredNote(dc, r, NoteColor(trail.Pitch, 0), opacity, trail.KeyDown && trail.Hit, trail.Pitch);
        }
    }

    /// <summary>Draws one note bar: soft shadow, extruded depth, shaded face, rim light, specular and head glow.</summary>
    private void DrawConfiguredNote(DrawingContext dc, Rect r, Color color, double opacity, bool sounding, int pitch)
    {
        color = AdjustColor(color);
        var style = _visual.NoteStyle;
        var radius = Math.Min(r.Height / 2, Math.Min(r.Width / 2, 2 + _visual.NoteRoundness / 100 * 12));
        var bloom = _visual.BloomSize / 100;
        var glow = _visual.NoteGlow / 100 * _visual.BloomIntensity / 65 * (sounding ? 1.35 : 1);
        var outer = 2 + bloom * 10;
        var tint = _visual.NoteTint / 78;
        var edgeWidth = .4 + _visual.NoteEdgeWidth / 45;
        var bright = Blend(color, Colors.White, .35);
        var deep = Blend(color, Color.FromRgb(5, 3, 12), .72);
        if (_recordingBrightPass)
        {
            // The bright pass only records light: a wide halo and a hot core, gated by the bloom threshold.
            if (glow <= .01 && _visual.NoteEdge <= 0) return;
            var gate = BrightGate;
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(58 * opacity * Math.Min(1.8, glow) * gate), color.R, color.G, color.B)), null, Inflate(r, outer * 1.6), radius + outer, radius + outer);
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(120 * opacity * Math.Min(1.5, glow) * tint * gate), bright.R, bright.G, bright.B)), null, Inflate(r, 2 + bloom * 4), radius + 4, radius + 4);
            return;
        }
        if (_visual.ShowNoteShadow && _visual.NoteShadowStrength > 0 && r.Height > 6 && r.Width > 4)
        {
            var distance = _visual.NoteShadowDistance;
            var blur = 2 + _visual.NoteShadowBlur / 100 * 15;
            DrawSoftShadow(dc, r, radius, blur, distance * .32, distance * .46, deep, _visual.NoteShadowStrength / 100 * opacity * .78);
        }
        var depth = _visual.NoteDepth / 100;
        if (depth > .02 && r.Width > 5)
        {
            var extrusion = Math.Min(7, r.Width * .22) * depth;
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(205 * opacity * tint), deep.R, deep.G, deep.B)), null,
                new Rect(r.X + extrusion * .55, r.Y + extrusion * .75, r.Width, Math.Max(2, r.Height - extrusion * .2)), radius, radius);
        }
        if (glow > .01)
        {
            var bloomScale = style == "Neon" ? 1.4 : 1;
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(16 * opacity * glow), color.R, color.G, color.B)), null, Inflate(r, outer * 1.8 * bloomScale), radius + outer, radius + outer);
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(36 * opacity * glow), color.R, color.G, color.B)), null, Inflate(r, outer * .75 * bloomScale), radius + outer * .6, radius + outer * .6);
        }
        switch (style)
        {
            case "Neon":
            {
                // Hollow tube: tinted interior, a wide soft stroke and a crisp bright core stroke.
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(78 * opacity * tint), deep.R, deep.G, deep.B)), null, r, radius, radius);
                dc.DrawRoundedRectangle(NoteFaceBrush(Blend(color, Color.FromRgb(12, 8, 22), .55), opacity * .8, .35), null, r, radius, radius);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(96 * opacity), 6, 4, 14)), null, Inflate(r, -Math.Min(3, r.Width / 4)), radius, radius);
                var soft = new Pen(Brush(Color.FromArgb(Alpha(120 * opacity * glow), color.R, color.G, color.B)), edgeWidth * 2.6 + 1.5); soft.Freeze();
                dc.DrawRoundedRectangle(null, soft, Inflate(r, -.7), radius, radius);
                var core = new Pen(Brush(Color.FromArgb(Alpha(255 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B)), edgeWidth + .8); core.Freeze();
                dc.DrawRoundedRectangle(null, core, Inflate(r, -.7), radius, radius);
                break;
            }
            case "Fire":
            {
                // Burning bar: a dim base, a bright layer punched through an animated ember mask, warm bloom and a glowing rim.
                var dim = Blend(color, Color.FromRgb(24, 5, 2), .55 * _visual.NoteTexture / 100);
                var hot = Blend(color, Color.FromRgb(255, 246, 196), .5);
                dc.DrawRoundedRectangle(NoteFaceBrush(dim, opacity * tint, .25), null, r, radius, radius);
                if (_visual.NoteTexture > 0)
                {
                    var mask = new ImageBrush(FireMask)
                    {
                        TileMode = TileMode.Tile, Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(pitch * 37 % FireMaskWidth, (pitch * 53 + _elapsed * 34) % FireMaskHeight, FireMaskWidth, FireMaskHeight)
                    };
                    mask.Freeze();
                    dc.PushOpacityMask(mask);
                    dc.DrawRoundedRectangle(NoteFaceBrush(hot, opacity * tint, .5), null, r, radius, radius);
                    dc.Pop();
                }
                else dc.DrawRoundedRectangle(NoteFaceBrush(hot, opacity * tint, .5), null, r, radius, radius);
                // Ember sparks that climb the bar make the fire feel alive.
                if (_visual.NoteTexture > 0 && r.Height > 16)
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var phase = (_elapsed * (.5 + i * .27) + pitch * .13 + i * .41) % 1;
                        var ex = r.X + r.Width * (.2 + ((pitch * 31 + i * 57) % 60) / 100.0);
                        var ey = r.Bottom - phase * r.Height;
                        dc.DrawEllipse(Brush(Color.FromArgb(Alpha(190 * opacity * (1 - phase) * tint), 255, 236, 180)), null, new Point(ex, ey), 1.1 + (1 - phase) * 1.1, 1.4 + (1 - phase) * 1.6);
                    }
                }
                var rim = new Pen(Brush(Color.FromArgb(Alpha(230 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B)), edgeWidth); rim.Freeze();
                dc.DrawRoundedRectangle(null, rim, Inflate(r, -.7), radius, radius);
                break;
            }
            case "Glass":
            {
                dc.DrawRoundedRectangle(GlassBrush(color, opacity * tint), null, r, radius, radius);
                if (_visual.NoteEdge > 0)
                {
                    var rim = new Pen(Brush(Color.FromArgb(Alpha(200 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B)), edgeWidth * .7 + .2); rim.Freeze();
                    dc.DrawRoundedRectangle(null, rim, Inflate(r, -.6), radius, radius);
                }
                if (r.Height > 14)
                {
                    var shade = new Rect(r.X + 1.5, r.Bottom - Math.Min(10, r.Height * .3), Math.Max(1, r.Width - 3), Math.Min(10, r.Height * .3));
                    dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(70 * opacity), 5, 3, 12)), null, shade, radius * .6, radius * .6);
                }
                break;
            }
            default:
            {
                dc.DrawRoundedRectangle(NoteFaceBrush(color, opacity * tint, 1), null, r, radius, radius);
                if (_visual.NoteEdge > 0)
                {
                    var rim = new Pen(Brush(Color.FromArgb(Alpha(235 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B)), edgeWidth); rim.Freeze();
                    dc.DrawRoundedRectangle(null, rim, Inflate(r, -.7), radius, radius);
                }
                break;
            }
        }
        // Rim light on both long edges, as if a back light grazed the bar.
        if (_visual.NoteRimLight > 0 && r.Width > 6 && r.Height > 8)
        {
            var rimPen = new Pen(RimBrush(bright, Alpha(215 * opacity * _visual.NoteRimLight / 100)), Math.Max(.8, Math.Min(2.4, r.Width * .09)));
            dc.DrawRoundedRectangle(null, rimPen, Inflate(r, -.4), radius, radius);
        }
        // Glossy specular streak across the face.
        if (_visual.NoteSpecular > 0 && r.Width > 7 && r.Height > 12)
        {
            var specular = _visual.NoteSpecular / 100;
            var bandHeight = Math.Min(3 + specular * 6, r.Height * .32);
            var band = new Rect(r.X + r.Width * .17, r.Y + Math.Min(2.5, r.Height * .1), Math.Max(1, r.Width * .66), bandHeight);
            dc.DrawRoundedRectangle(SpecularBrush(Alpha(165 * opacity * specular)), null, band, bandHeight / 2, bandHeight / 2);
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(195 * opacity * specular), 255, 255, 255)), null,
                new Point(r.X + r.Width * .34, band.Y + bandHeight * .55), .9 + specular * 1.5, .8 + specular * 1.1);
        }
        if (_visual.NoteRefraction > 0 && r.Width > 6)
        {
            var fringe = Alpha((style == "Neon" ? 90 : 170) * opacity * _visual.NoteRefraction / 100);
            dc.DrawLine(new Pen(Brush(Color.FromArgb(fringe, 255, 255, 255)), 1), new Point(r.X + 2, r.Y + 3), new Point(r.X + 2, r.Bottom - 3));
        }
        if (_visual.Notes3D && r.Height > 20 && style is "Solid" or "Glass")
        {
            var inner = new Rect(r.X + 3, r.Y + 4, Math.Max(2, r.Width - 6), Math.Max(3, r.Height - 8));
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha((style == "Glass" ? 55 : 95) * opacity), 10, 7, 18)), null, inner, Math.Min(radius, inner.Width / 2), Math.Min(radius, inner.Width / 2));
            dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(165 * opacity), 255, 250, 255)), 1), new Point(r.X + 4, r.Y + 5), new Point(r.X + 4, r.Bottom - 5));
        }
        if (_visual.NoteHeadGlow > 0 && r.Height > 6)
        {
            // Bright leading edge at the bottom of the bar, stronger while the note sounds.
            var headHeight = Math.Min(r.Height * .35, 4 + _visual.NoteHeadGlow / 100 * 8);
            var head = new Rect(r.X + 1, r.Bottom - headHeight - 1, Math.Max(1, r.Width - 2), headHeight);
            var headAlpha = Alpha((sounding ? 235 : 125) * opacity * _visual.NoteHeadGlow / 100);
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(headAlpha, bright.R, bright.G, bright.B)), null, head, Math.Min(radius, headHeight / 2), Math.Min(radius, headHeight / 2));
            if (sounding) dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(220 * opacity), 255, 255, 255)), 1.4), new Point(r.X + 1.5, r.Bottom - 1), new Point(r.Right - 1.5, r.Bottom - 1));
        }
        if (_visual.ShowNoteLabels && r.Width >= 11 && r.Height >= 15)
        {
            var label = r.Width >= 20 ? NoteLabel(pitch) : NoteLabel(pitch).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');
            var luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
            var textColor = style == "Neon" ? Colors.White : luminance > 150 ? Color.FromRgb(12, 8, 20) : Colors.White;
            DrawLabel(dc, label, new Point(r.X + r.Width / 2, r.Bottom - Math.Min(12, r.Height / 2)), Math.Min(11, r.Width * .62), Color.FromArgb(Alpha(230 * opacity), textColor.R, textColor.G, textColor.B), true);
        }
    }

    // =====================================================================================================
    // Shading helpers
    // =====================================================================================================

    private Brush CacheShade((byte Kind, uint A, uint B, uint C, int N) key, Brush brush)
    {
        if (_shadeCache.Count > 900) _shadeCache.Clear();
        _shadeCache[key] = brush; return brush;
    }

    /// <summary>Vertical shade of a bar face: lit top, saturated middle and a darker base.</summary>
    private Brush NoteFaceBrush(Color color, double alpha, double gloss)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        var quantized = (int)Math.Round(alpha * 32);
        var key = ((byte)1, PackColor(color), (uint)quantized, (uint)Math.Round(gloss * 32), 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var a = alpha * 235;
        var highlight = Blend(color, Colors.White, .18 + gloss * .22);
        var mid = Blend(color, Colors.White, .04);
        var baseColor = Blend(color, Color.FromRgb(4, 2, 10), .3 + gloss * .12);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(a), highlight.R, highlight.G, highlight.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(a * .96), mid.R, mid.G, mid.B), .22));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(a * .92), color.R, color.G, color.B), .55));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(a * .95), baseColor.R, baseColor.G, baseColor.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush GlassBrush(Color color, double strength)
    {
        var quantized = (int)Math.Round(Math.Clamp(strength, 0, 1) * 32);
        var key = ((byte)3, PackColor(color), (uint)quantized, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var light = Blend(color, Colors.White, .6); var deep = Blend(color, Color.FromRgb(8, 6, 18), .38);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(228 * strength), light.R, light.G, light.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * strength), color.R, color.G, color.B), .38));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(210 * strength), color.R, color.G, color.B), .52));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(232 * strength), deep.R, deep.G, deep.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    /// <summary>Horizontal gradient that is bright on both long edges and clear in the middle: rim lighting.</summary>
    private Brush RimBrush(Color color, byte alpha)
    {
        var key = ((byte)4, PackColor(color), alpha, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), .22));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), .78));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush SpecularBrush(byte alpha)
    {
        var key = ((byte)5, (uint)alpha, 0u, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), .35));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    private Brush TrailBrush(Color color, byte alpha)
    {
        var key = ((byte)6, PackColor(color), alpha, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(alpha * .55), color.R, color.G, color.B), .55));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheShade(key, gradient);
    }

    /// <summary>Layered shadow that fakes a blurred drop shadow without a per-note bitmap effect.</summary>
    private void DrawSoftShadow(DrawingContext dc, Rect rect, double radius, double blur, double dx, double dy, Color color, double strength)
    {
        const int layers = 4;
        for (var i = layers; i >= 1; i--)
        {
            var spread = blur * i / layers;
            var alpha = Alpha(255 * strength * (i == 1 ? .85 : 1.0 / (1 + (i - 1) * 1.7)));
            if (alpha == 0) continue;
            var r = new Rect(rect.X + dx - spread, rect.Y + dy - spread, rect.Width + spread * 2, rect.Height + spread * 2);
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(alpha, color.R, color.G, color.B)), null, r, radius + spread, radius + spread);
        }
    }

    /// <summary>Global grade: hue rotation, vibrance, saturation, contrast and temperature.</summary>
    private Color AdjustColor(Color input)
    {
        var r = input.R / 255.0; var g = input.G / 255.0; var b = input.B / 255.0;
        var temperature = _visual.ColorTemperature / 100;
        if (temperature != 0) { r *= 1 + temperature * .16; b *= 1 - temperature * .16; }
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        var saturation = max <= 0 ? 0 : delta / max;
        hue = (hue + _visual.HueShift) % 360; if (hue < 0) hue += 360;
        var vibrance = _visual.Vibrance / 100 - .45;
        saturation = Math.Clamp(saturation + vibrance * (1 - saturation) * .55, 0, 1) * _visual.Saturation / 100;
        saturation = Math.Clamp(saturation, 0, 1);
        var value = Math.Clamp(max, 0, 1);
        var chroma = value * saturation;
        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var match = value - chroma;
        var (rr, gg, bb) = hue switch
        {
            < 60 => (chroma, second, 0d), < 120 => (second, chroma, 0d), < 180 => (0d, chroma, second),
            < 240 => (0d, second, chroma), < 300 => (second, 0d, chroma), _ => (chroma, 0d, second)
        };
        var contrast = _visual.Contrast / 100;
        byte Channel(double c) => (byte)Math.Clamp((c - .5) * contrast * 255 + 127.5, 0, 255);
        return Color.FromArgb(input.A, Channel(rr + match), Channel(gg + match), Channel(bb + match));
    }

    /// <summary>Resolves the color of a note according to the active color mode (gradient palette, hands, tracks or rainbows).</summary>
    internal Color NoteColor(int pitch, int track)
    {
        var t = Math.Clamp((pitch - FirstPitch) / (double)(KeyCount - 1), 0, 1);
        switch (_visual.ColorMode)
        {
            case "PerHand":
                return pitch < _visual.HandSplitPitch ? ParseColor(_visual.LeftHandColor, Color.FromRgb(63, 169, 255)) : ParseColor(_visual.RightHandColor, Color.FromRgb(255, 111, 216));
            case "PerTrack":
            {
                var colors = _visual.TrackColors; if (colors is null || colors.Count == 0) return ColorFromHue(Hue(pitch) + 24);
                var index = ((track % colors.Count) + colors.Count) % colors.Count;
                return ParseColor(colors[index], ColorFromHue(index * 47 + 190));
            }
            case "RainbowPitch":
                return ColorFromHue(t * 300);
            case "RainbowTime":
                return ColorFromHue(_elapsed * _visual.RainbowSpeed * 3.6 + t * 120);
        }
        if (_visual.Palette == "Spectrum") return ColorFromHue(Hue(pitch) + 24);
        var (start, mid, end) = PaletteStops();
        if (!_visual.ShowMidStop) return Blend(start, end, t);
        return t < .5 ? Blend(start, mid, t * 2) : Blend(mid, end, (t - .5) * 2);
    }

    /// <summary>Three-stop definitions of the built-in palettes; "Custom" reads the color pickers.</summary>
    private (Color Start, Color Mid, Color End) PaletteStops()
    {
        var start = ParseColor(_visual.NoteColorStart, Colors.DeepSkyBlue);
        var mid = ParseColor(_visual.NoteColorMid, Colors.MediumPurple);
        var end = ParseColor(_visual.NoteColorEnd, Colors.MediumPurple);
        if (PianoVisualSettings.PaletteStops.TryGetValue(_visual.Palette, out var stops))
            return (ParseColor(stops.Start, start), ParseColor(stops.Mid, mid), ParseColor(stops.End, end));
        return (start, mid, end);
    }

    /// <summary>The three stops the palette editor and the live preview show.</summary>
    internal (Color Start, Color Mid, Color End) CurrentPaletteStops() => PaletteStops();
}
