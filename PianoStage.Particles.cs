using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Sparks, plasma wisps, flames and impact rings: simulation plus rendering.</summary>
internal sealed partial class PianoStage
{
    private void AdvanceParticles(double dt)
    {
        if (dt <= 0) return;
        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i]; p.Age += dt;
            if (p.Age >= p.Life) { _sparks.RemoveAt(i); continue; }
            if (p.Wisp)
            {
                var turbulence = _visual.WispTurbulence / 100;
                p.X += (p.Vx + Math.Sin(p.Age * (3 + turbulence * 7) + p.Phase) * (8 + turbulence * 46) * Math.Min(1, p.Age * 2)) * dt;
                p.Y += p.Vy * dt;
                var flow = _visual.VectorField / 100 * Math.Sin(p.Y / Math.Max(1, _visual.FieldScale) + _elapsed * _visual.EvolutionSpeed / 100);
                p.X += flow * dt * 6;
                p.Vy *= Math.Exp(-.35 * dt);
                continue;
            }
            p.X += p.Vx * dt; p.Y += p.Vy * dt;
            var field = _visual.VectorField / 100 * Math.Sin(p.Y / Math.Max(1, _visual.FieldScale) + _elapsed * _visual.EvolutionSpeed / 100);
            p.Vx += field * dt * 14; p.Vy += _visual.Gravity * dt;
            var damping = Math.Exp(-_visual.Drag / 100 * dt); p.Vx *= damping; p.Vy *= damping;
        }
    }

    private void SpawnWisps(int pitch, double dt, double hitY, double lane)
    {
        if (_sparks.Count >= MaxParticles) return;
        _wispBudget[pitch] += _visual.WispAmount * dt;
        var count = (int)_wispBudget[pitch]; if (count <= 0) return;
        _wispBudget[pitch] -= count;
        var color = AdjustColor(_activeKeyColor[pitch]);
        var x = (pitch - FirstPitch + .5) * lane;
        var life = .35 + _visual.WispHeight / 100 * 1.9;
        for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
        {
            var spreadX = (_random.NextDouble() - .5) * lane * (.25 + _visual.WispWidth / 100 * 1.1);
            _sparks.Add(new Spark
            {
                Wisp = true, X = x + spreadX, Y = hitY - 2 - _random.NextDouble() * 4,
                Vx = (_random.NextDouble() - .5) * 18, Vy = -_visual.WispSpeed * (.55 + _random.NextDouble() * .9),
                Life = life * (.6 + _random.NextDouble() * .8), Age = 0, Phase = _random.NextDouble() * Math.PI * 2,
                Size = .7 + _random.NextDouble() * 1.5,
                Color = _random.NextDouble() < .18 ? Blend(color, Colors.White, .6) : color
            });
        }
    }

    private void DrawSparks(DrawingContext dc)
    {
        var bloom = 1.5 + _visual.BloomSize / 32;
        var wispGlow = _visual.WispGlow / 100;
        foreach (var particle in _sparks)
        {
            var fade = Math.Clamp(1 - particle.Age / particle.Life, 0, 1);
            if (particle.Wisp)
            {
                if (!_visual.ShowWisps) continue;
                var strength = Math.Pow(fade, 1.3) * wispGlow;
                var wispSize = particle.Size * (1 + particle.Age * 1.1);
                dc.DrawEllipse(SparkGlowBrush(particle.Color, strength * .9), null, new Point(particle.X, particle.Y), wispSize * 3.4, wispSize * 3.4);
                dc.DrawEllipse(Brush(Color.FromArgb(Alpha(205 * strength), particle.Color.R, particle.Color.G, particle.Color.B)), null, new Point(particle.X, particle.Y), wispSize, wispSize);
                continue;
            }
            if (!_visual.ShowEmbers) continue;
            var alpha = Alpha(228 * fade * _visual.ParticleGlow / 100);
            if (alpha < 2) continue;
            var size = particle.Size * (.55 + fade * .55);
            var color = Color.FromArgb(alpha, particle.Color.R, particle.Color.G, particle.Color.B);
            // A short streak along the velocity vector sells the motion far better than a plain dot.
            if (!_recordingBrightPass)
            {
                var tail = new Pen(Brush(Color.FromArgb((byte)(alpha * .5), particle.Color.R, particle.Color.G, particle.Color.B)), Math.Max(.7, size * .8));
                tail.Freeze();
                dc.DrawLine(tail, new Point(particle.X - particle.Vx * .022, particle.Y - particle.Vy * .022), new Point(particle.X, particle.Y));
            }
            dc.DrawEllipse(SparkGlowBrush(particle.Color, fade * _visual.ParticleGlow / 100), null, new Point(particle.X, particle.Y), size * bloom, size * bloom);
            dc.DrawEllipse(Brush(color), null, new Point(particle.X, particle.Y), size, size);
        }
    }

    private void DrawFlames(DrawingContext dc, double width, double keyTop, double lane)
    {
        var intensity = _visual.FlameIntensity / 100; var heightScale = .4 + _visual.FlameHeight / 100 * 1.4;
        var warm = _visual.FlameColorMode != "Note";
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var heat = _keyHeat[pitch];
            if (heat <= .02) continue;
            var x = (pitch - FirstPitch + .5) * lane;
            var pulse = .65 + .35 * Math.Sin(_elapsed * 13 + pitch);
            var radius = (8 + 30 * pulse) * heat * (.6 + intensity * .6);
            var tint = warm ? Color.FromRgb(255, 178, 73) : AdjustColor(_activeKey[pitch] ? _activeKeyColor[pitch] : NoteColor(pitch, 0));
            var deep = warm ? Color.FromRgb(255, 83, 54) : Blend(tint, Color.FromRgb(120, 0, 60), .45);
            var strength = Alpha(150 * intensity * (.55 + heat * .45));
            dc.DrawEllipse(SparkGlowBrush(tint, intensity * heat * .9), null, new Point(x, keyTop + 3), radius * 1.5, (20 + radius * 1.5) * heightScale);
            dc.DrawEllipse(FlameBrush(tint, deep, strength), null, new Point(x, keyTop + 3), radius, (20 + radius * 1.35) * heightScale);
            if (_recordingBrightPass) continue;
            // A flickering inner tongue gives the flame some motion instead of a static blob.
            var tongue = (6 + 14 * heat) * (.7 + .3 * Math.Sin(_elapsed * 21 + pitch * 1.7)) * heightScale;
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(120 * intensity * heat), 255, 250, 225)), null, new Point(x + Math.Sin(_elapsed * 17 + pitch) * 2, keyTop - tongue * .5), 2.5 + heat * 2, tongue);
            // Light pooling on the keys under the fire.
            dc.DrawEllipse(SparkGlowBrush(tint, heat * intensity * .6), null, new Point(x, keyTop + 6), radius * 2.1, 12 + radius * .5);
        }
    }

    private void DrawRings(DrawingContext dc)
    {
        foreach (var ring in _rings)
        {
            var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
            var radius = 6 + t * (16 + _visual.RingSize * 1.1);
            var alpha = Alpha(215 * (1 - t) * (1 - t) * (.6 + ring.Strength * .5));
            var pen = new Pen(Brush(Color.FromArgb(alpha, ring.Color.R, ring.Color.G, ring.Color.B)), .6 + (1 - t) * 2.6); pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(ring.X, ring.Y), radius * (ring.Width / 40.0), radius * .32);
            if (t < .35 && !_recordingBrightPass) dc.DrawEllipse(Brush(Color.FromArgb(Alpha(140 * (1 - t / .35)), 255, 255, 255)), null, new Point(ring.X, ring.Y), 3 + radius * .25, 2 + radius * .1);
        }
    }

    private Brush SparkGlowBrush(Color color, double strength)
    {
        var bucket = (uint)Math.Round(Math.Clamp(strength, 0, 1) * 10);
        var key = ((byte)25, PackColor(color), bucket, 0u, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var scale = bucket / 10.0;
        var radial = new RadialGradientBrush { MappingMode = BrushMappingMode.RelativeToBoundingBox, Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(120 * scale), color.R, color.G, color.B), 0));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(40 * scale), color.R, color.G, color.B), .45));
        radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        radial.Freeze();
        return CacheShade(key, radial);
    }

    private Brush FlameBrush(Color tint, Color deep, byte strength)
    {
        var key = ((byte)26, PackColor(tint), PackColor(deep), strength, 0);
        if (_shadeCache.TryGetValue(key, out var cached)) return cached;
        var flame = new RadialGradientBrush
        {
            Center = new Point(.5, .84), GradientOrigin = new Point(.5, .84), RadiusX = .8, RadiusY = 1.1,
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        flame.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(strength * 1.5), 255, 255, 220), 0));
        flame.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(strength * .75), tint.R, tint.G, tint.B), .28));
        flame.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(strength * .3), deep.R, deep.G, deep.B), .72));
        flame.GradientStops.Add(new GradientStop(Color.FromArgb(0, deep.R, deep.G, deep.B), 1));
        flame.Freeze();
        return CacheShade(key, flame);
    }
}
