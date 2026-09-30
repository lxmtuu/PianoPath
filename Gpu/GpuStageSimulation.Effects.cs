using System.Numerics;

namespace PianoPath;

/// <summary>
/// The impact, hold and release phases of the GPU stage that go beyond plain particles: flat impact waves,
/// lightning / plasma / star flashes, the impact morphs, release effects, electric arcs between held keys
/// and the note names on the keys. Every formula mirrors the software stage (PianoStage.cs) so a look
/// reads the same on both engines; only the drawing is different (instanced shapes instead of WPF
/// geometry, and additive HDR light instead of alpha-blended sRGB).
/// </summary>
internal sealed partial class GpuStageSimulation
{
    // sprite kinds understood by VsSprite / PsSprite (see StageShaders.hlsl, section 4)
    private const float KindLine = 7, KindEllipse = 8, KindGlow = 9, KindRing = 10, KindGlyph = 11, KindSierpinski = 12, KindCurtain = 13, KindStar = 14, KindRect = 15;

    /// <summary>Glyph atlas layout, shared with <see cref="GpuGlyphAtlas"/>: cells 0-127 note names, 128-255 short names, 256-351 katakana.</summary>
    internal const int AtlasNoteCell = 0, AtlasShortCell = 128, AtlasKatakanaCell = 256;

    private readonly record struct NoteTrail(float X, float Y, float W, float H, Vector3 Color, float Opacity, int Pitch, bool Rising);
    private readonly List<NoteTrail> _noteTrails = [];
    /// <summary>Notes wide and tall enough to carry their name this frame (Note names on bars).</summary>
    private readonly List<NoteTrail> _noteLabels = [];

    /// <summary>
    /// The note name inside a bar, placed and coloured as the software stage does: near the bottom edge,
    /// white on Neon and on dark notes, near-black on light ones; the full name when the bar is wide enough.
    /// </summary>
    private static void AddNoteLabel(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, NoteTrail n)
    {
        var luminance = (.2126f * n.Color.X + .7152f * n.Color.Y + .0722f * n.Color.Z) * 255;
        var text = look.NoteStyle == 1 || luminance <= 150 ? Vector3.One : ToLinear(new Vector3(12 / 255f, 8 / 255f, 20 / 255f));
        var cell = n.W >= 20 ? AtlasNoteCell + n.Pitch : AtlasShortCell + n.Pitch;
        Glyph(sprites, n.X + n.W / 2, n.Y + n.H - Math.Min(12, n.H / 2), Math.Min(11, n.W * .62f), cell, text, 230 / 255f * Math.Clamp(n.Opacity, 0, 1));
    }

    private IReadOnlyList<NoteEvent>? _releaseNotes;
    private double _releaseScanPos;
    private int _releaseScanIndex;

    // ------------------------------------------------------------------------------------------------
    // shape helpers (scene px, linear colour, alpha 0..1)
    // ------------------------------------------------------------------------------------------------

    /// <summary>Linear-light colour of an sRGB byte triple.</summary>
    private static Vector3 Rgb(int r, int g, int b) => ToLinear(new Vector3(r / 255f, g / 255f, b / 255f));

    private static void Line(GpuInstanceList<GpuSpriteInstance> list, float x0, float y0, float x1, float y1, float halfWidth, Vector3 color, float alpha, float core)
    {
        if (alpha <= .004f) return;
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x0, y0, halfWidth, KindLine), Color = new Vector4(color, alpha), Dir = new Vector4(x1 - x0, y1 - y0, core, 0) });
    }

    private static void Ellipse(GpuInstanceList<GpuSpriteInstance> list, float x, float y, float rx, float ry, float angle, Vector3 color, float alpha, float softness)
    {
        if (alpha <= .004f || rx <= 0) return;
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, rx, KindEllipse), Color = new Vector4(color, alpha), Dir = new Vector4(MathF.Cos(angle), MathF.Sin(angle), ry / rx, softness) });
    }

    private static void Glow(GpuInstanceList<GpuSpriteInstance> list, float x, float y, float rx, float ry, Vector3 color, float alpha)
    {
        if (alpha <= .004f || rx <= 0) return;
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, rx, KindGlow), Color = new Vector4(color, alpha), Dir = new Vector4(1, 0, ry / rx, 0) });
    }

    private static void RingShape(GpuInstanceList<GpuSpriteInstance> list, float x, float y, float rx, float ry, float penWidth, Vector3 color, float alpha)
    {
        if (alpha <= .004f || rx <= .5f) return;
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, rx, KindRing), Color = new Vector4(color, alpha), Dir = new Vector4(1, 0, ry / rx, Math.Max(.01f, penWidth / rx * .6f)) });
    }

    /// <summary>A glyph from the atlas; <paramref name="fontSize"/> is the software stage's label size in DIPs.</summary>
    private static void Glyph(GpuInstanceList<GpuSpriteInstance> list, float x, float y, float fontSize, int cell, Vector3 color, float alpha)
    {
        if (alpha <= .004f) return;
        // atlas cells are 32 px tall around a 22 px font: half the cell height is 16/22 of the font size
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, fontSize * 16f / 22f, KindGlyph), Color = new Vector4(color, alpha), Dir = new Vector4(1, 0, cell, 0) });
    }

    private static void Rect(GpuInstanceList<GpuSpriteInstance> list, float x, float y, float w, float h, Vector3 color, float alpha, float top = 1, float bottom = 1)
    {
        if (alpha <= .004f || w <= 0 || h <= 0) return;
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x + w / 2, y + h / 2, w / 2, KindRect), Color = new Vector4(color, alpha), Dir = new Vector4(h / 2, 0, top, bottom) });
    }

    /// <summary>The software stage's deterministic 0-1 hash, so procedural layers match it frame for frame.</summary>
    private static float SeededRandom(int seed)
    {
        var x = Math.Sin(seed * 127.1 + 311.7) * 43758.5453;
        return (float)(x - Math.Floor(x));
    }

    /// <summary>The software stage's ColorFromHue (90 % chroma over a 10 % floor), sRGB 0..1.</summary>
    private static Vector3 ColorFromHue(double hue)
    {
        hue = (hue % 360 + 360) % 360;
        const double c = .9, m = .1;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r, g, b) = hue switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return new Vector3((float)(r + m), (float)(g + m), (float)(b + m));
    }

    /// <summary>The software stage's per-pitch base hue.</summary>
    private static double Hue188(int pitch) => 188 + (pitch - FirstPitch) / 87.0 * 112;

    // ------------------------------------------------------------------------------------------------
    // impact morphs and release effects
    // ------------------------------------------------------------------------------------------------

    /// <summary>Impact phase, morph channel: what the note itself becomes when it lands.</summary>
    private void SpawnImpactMorph(float x, float y, Vector3 color, float strength)
    {
        var look = _look;
        if (look.ImpactMorph == "None") return;
        var intensity = look.ImpactMorphIntensity * strength;
        if (intensity <= 0) return;
        switch (look.ImpactMorph)
        {
            case "Shatter":
            {
                var count = Math.Clamp((int)(10 * intensity) + 4, 0, 24);
                for (var i = 0; i < count; i++)
                {
                    var angle = -MathF.PI / 2 + (Rand() - .5f) * 2.4f;
                    var speed = (140 + Rand() * 260) * (.6f + .4f * strength);
                    Spawn(new Particle { Shape = Shape.Shard, X = x, Y = y - 2, Vx = MathF.Cos(angle) * speed, Vy = MathF.Sin(angle) * speed, Life = .5f + Rand() * .4f, Mass = 1, Phase = Rand() * MathF.Tau, Size = 2 + Rand() * 3.5f, Grav = 1.4f, DragK = 1, Glow = 1, Color = Vector3.Lerp(color, Vector3.One, .45f) });
                }
                break;
            }
            case "Melt":
            {
                var count = Math.Clamp((int)(8 * intensity) + 3, 0, 20);
                for (var i = 0; i < count; i++)
                    Spawn(new Particle { Shape = Shape.Droplet, X = x + (Rand() - .5f) * 22, Y = y + 2, Vx = (Rand() - .5f) * 30, Vy = 40 + Rand() * 90, Life = .55f + Rand() * .45f, Mass = 2, Phase = Rand() * MathF.Tau, Size = 1.6f + Rand() * 2.2f, Grav = 1.1f, DragK = 1.6f, Glow = 1, Color = Vector3.Lerp(color, new Vector3(1f, .86f, .59f), .3f) });
                break;
            }
            case "Absorb":
                if (_rings.Count < 96) _rings.Add(new Ring { X = x, Y = y, Life = .3f, Kind = 3, Strength = strength, Color = color });
                break;
            case "Bounce":
            {
                var count = Math.Clamp((int)(12 * intensity) + 4, 0, 26);
                for (var i = 0; i < count; i++)
                {
                    var speed = 220 + Rand() * 320;
                    Spawn(new Particle { Shape = Shape.Streak, X = x + (Rand() - .5f) * 10, Y = y, Vx = (Rand() - .5f) * 60, Vy = -speed, Life = .4f + Rand() * .3f, Mass = .6f, Phase = Rand() * MathF.Tau, Size = look.ParticleSize * (.5f + Rand() * .5f), Grav = 1, DragK = 1, Glow = 1, Color = i % 3 == 0 ? Vector3.One : color });
                }
                if (_rings.Count < 96) _rings.Add(new Ring { X = x, Y = y, Life = .3f, Strength = .6f * strength, Color = Vector3.One });
                break;
            }
            case "Morph":
                if (_flashes.Count < 48) _flashes.Add(new Flash { X = x, Y = y, Life = .3f, Style = 3, Strength = strength, Color = color, Intensity = look.ImpactFlashIntensity });
                break;
        }
    }

    /// <summary>Release phase: what happens at the key when a note ends (live key release or song note end).</summary>
    private void SpawnReleaseFx(int pitch, GpuSceneLayout layout)
    {
        var look = _look;
        if (look.ReleaseEffect == "Fade" || look.ReleaseIntensity <= 0) return;
        var strength = look.ReleaseIntensity;
        var clamped = Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1);
        var x = layout.X(clamped);
        var y = layout.HitY - 1;
        var color = _active[clamped] ? _keyColor[clamped] : look.NoteColor(clamped, 0);
        switch (look.ReleaseEffect)
        {
            case "Float Up":
            {
                var count = Math.Clamp((int)(6 * strength) + 2, 0, 10);
                for (var i = 0; i < count; i++)
                    Spawn(new Particle { Shape = Shape.Droplet, X = x + (Rand() - .5f) * 18, Y = y - 4, Vx = (Rand() - .5f) * 24, Vy = -(50 + Rand() * 90), Life = .7f + Rand() * .5f, Mass = .5f, Phase = Rand() * MathF.Tau, Size = 1.4f + Rand() * 1.6f, Grav = -.25f, DragK = 1, Glow = 1, Color = color });
                break;
            }
            case "Dissolve":
            {
                var count = Math.Clamp((int)(10 * strength) + 3, 0, 16);
                for (var i = 0; i < count; i++)
                {
                    var angle = Rand() * MathF.Tau; var speed = 20 + Rand() * 70;
                    Spawn(new Particle { Shape = Shape.Droplet, X = x + (Rand() - .5f) * 26, Y = y - 6, Vx = MathF.Cos(angle) * speed, Vy = MathF.Sin(angle) * speed - 20, Life = .4f + Rand() * .35f, Mass = 1, Phase = Rand() * MathF.Tau, Size = .9f + Rand() * 1.1f, Grav = .3f, DragK = 2, Glow = 1, Color = color });
                }
                break;
            }
            case "Smoke":
            {
                var count = Math.Clamp((int)(4 * strength) + 2, 0, 8);
                for (var i = 0; i < count; i++)
                    Spawn(new Particle { Shape = Shape.Dust, X = x + (Rand() - .5f) * 20, Y = y - 6, Vx = (Rand() - .5f) * 20, Vy = -(30 + Rand() * 50), Life = .8f + Rand() * .6f, Mass = 1, Phase = Rand() * MathF.Tau, Size = 2 + Rand() * 2, Grav = -.1f, DragK = 2, Glow = 1, Color = Vector3.Lerp(new Vector3(.59f, .57f, .55f), color, .2f) });
                break;
            }
            case "Snap Back":
                if (_rings.Count < 96) _rings.Add(new Ring { X = x, Y = y, Life = .25f, Kind = 3, Strength = strength, Color = color });
                break;
            case "Echo Rings":
                if (_rings.Count < 95)
                {
                    _rings.Add(new Ring { X = x, Y = y, Life = .5f, Strength = .8f * strength, Color = color });
                    _rings.Add(new Ring { X = x, Y = y, Life = .5f, Strength = .4f * strength, Color = Vector3.One });
                }
                break;
        }
    }

    /// <summary>Release phase for the song: notes whose end just passed the playhead emit the release effect.</summary>
    private void ScanReleaseFx(GpuFrameInput input, GpuSceneLayout layout)
    {
        var look = _look;
        var notes = input.Notes;
        if (!ReferenceEquals(notes, _releaseNotes))
        {
            _releaseNotes = notes; _releaseScanPos = input.Position;
            _releaseScanIndex = notes.Count > 0 ? NoteTimeline.FirstIndexAtOrAfter(notes, input.Position) : 0;
            return;
        }
        if (!input.Playing || look.ReleaseEffect == "Fade" || notes.Count == 0 || look.ReleaseIntensity <= 0) { _releaseScanPos = input.Position; return; }
        if (input.Position < _releaseScanPos - .001 || input.Position > _releaseScanPos + 1)
        {
            // a seek or a loop: skip the backlog instead of bursting stale releases
            _releaseScanPos = input.Position;
            _releaseScanIndex = NoteTimeline.FirstIndexAtOrAfter(notes, input.Position);
            return;
        }
        try
        {
            var firstUnended = -1; var budget = 24;
            for (var j = Math.Max(0, _releaseScanIndex); j < notes.Count; j++)
            {
                var note = notes[j];
                if (note.Start > input.Position) break;
                if (note.End > _releaseScanPos && note.End <= input.Position) { if (budget-- > 0) SpawnReleaseFx(note.Pitch, layout); }
                else if (note.End > input.Position && firstUnended < 0) firstUnended = j;
            }
            _releaseScanIndex = firstUnended >= 0 ? firstUnended : NoteTimeline.FirstIndexAtOrAfter(notes, input.Position);
        }
        catch (ArgumentOutOfRangeException) { _releaseScanIndex = 0; }
        _releaseScanPos = input.Position;
    }

    // ------------------------------------------------------------------------------------------------
    // sprites for waves, flash styles, arcs and labels
    // ------------------------------------------------------------------------------------------------

    /// <summary>Impact waves lie flat on the key plane (radius × 0.32), exactly like the software rings.</summary>
    private static void AddRingSprites(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, Ring ring, float scaleX)
    {
        var intensity = look.ImpactWaveIntensity;
        if (intensity <= 0) return;
        var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
        var strength = Math.Clamp(.5f + ring.Strength * .5f, 0, 1.2f);
        var x = ring.X * scaleX; var y = ring.Y;
        var c = ToLinear(ring.Color) * 1.6f;
        switch (ring.Kind)
        {
            case 1: // shockwave: a hot filled core cooling into a thin white rim
            {
                var progress = 1 - MathF.Exp(-4.6f * t);
                var radius = (6 + progress * (26 + look.RingSizeRaw * 1.6f)) * (.6f + .4f * ring.Strength);
                var fade = MathF.Pow(1 - t, 1.4f) * intensity * strength;
                Glow(sprites, x, y, radius, radius * .34f, Vector3.Lerp(c, Vector3.One * 1.6f, .35f), .75f * fade);
                RingShape(sprites, x, y, radius, radius * .34f, Math.Max(.6f, 2.4f * (1 - t)), Vector3.One * 1.8f, .92f * fade);
                break;
            }
            case 2: // ripple: three flat rings like rain on a pond
            {
                var progress = 1 - MathF.Exp(-3.4f * t);
                var radius = (8 + progress * (30 + look.RingSizeRaw * 1.4f)) * (.7f + .3f * ring.Strength);
                for (var i = 0; i < 3; i++)
                {
                    var f = t - i * .12f;
                    if (f < 0) continue;
                    var fade = MathF.Pow(1 - f, 1.8f) * intensity * strength * (1 - i * .25f);
                    var rr = radius * (1 - i * .22f);
                    RingShape(sprites, x, y, rr, rr * .3f, Math.Max(.6f, 2 * (1 - f) + .4f), Rgb(170, 215, 255) * 1.6f, .84f * fade);
                }
                break;
            }
            case 3: // implosion (Absorb, Snap Back): a ring collapsing into the key
            {
                var radius = (34 + look.RingSizeRaw * 1.1f) * (1 - t) + 3;
                var fade = (1 - t) * intensity * strength;
                RingShape(sprites, x, y, radius, radius * .34f, Math.Max(.6f, 2.6f * (1 - t) + .6f), c, .92f * fade);
                RingShape(sprites, x, y, radius * .55f, radius * .2f, 1.2f, Vector3.One * 1.4f, .6f * fade);
                break;
            }
            default: // acoustic ring with a harmonic echo and a short white flare
            {
                var progress = 1 - MathF.Exp(-4.2f * t);
                var radius = (5 + progress * (18 + look.RingSizeRaw * 1.25f)) * (.7f + .3f * ring.Strength);
                var alpha = .9f * MathF.Pow(1 - t, 1.6f) * intensity * strength;
                var pen = Math.Max(.5f, 2.2f * (1 - t));
                RingShape(sprites, x, y, radius, radius * .32f, pen, c, alpha);
                if (t > .08f) RingShape(sprites, x, y, radius * .68f, radius * .68f * .32f, pen * .7f, c, alpha * .45f);
                if (t < .3f) Glow(sprites, x, y, 4 + radius * .2f, 2.5f + radius * .08f, Vector3.One * 2, .67f * (1 - t / .3f) * intensity * strength);
                break;
            }
        }
    }

    /// <summary>The Lightning, Plasma and star (Morph) flash styles.</summary>
    private void AddFlashSprites(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, Flash flash, float scaleX)
    {
        var t = Math.Clamp(flash.Age / flash.Life, 0, 1);
        var strength = Math.Clamp(.5f + flash.Strength * .5f, 0, 1.2f);
        var x = flash.X * scaleX; var y = flash.Y;
        switch (flash.Style)
        {
            case 1:
            {
                var fade = (1 - t) * flash.Intensity * strength;
                AddBolt(sprites, x, Math.Max(0, y - 260 - look.RingSizeRaw * 2), y, fade);
                break;
            }
            case 2:
            {
                var fade = (1 - t) * (1 - t) * flash.Intensity * strength;
                var radius = 12 + t * (30 + look.RingSizeRaw);
                Glow(sprites, x, y, radius * 1.3f, radius * .8f, Rgb(150, 110, 255) * 1.8f, .8f * fade);
                Glow(sprites, x, y, radius * .6f, radius * .4f, Rgb(220, 240, 255) * 2.2f, .9f * fade);
                Glow(sprites, x, y, radius * 1.1f, radius * .7f, Rgb(60, 220, 255) * 1.2f, .45f * fade);
                var seed = (int)(flash.X * 7 + flash.Y * 3);
                for (var i = 0; i < 5; i++)
                {
                    var a0 = SeededRandom(seed + i) * MathF.Tau + (float)_time * 6;
                    var inner = radius * .5f; var outer = radius * 1.5f;
                    var sx = x + MathF.Cos(a0) * inner; var sy = y + MathF.Sin(a0) * inner * .6f;
                    var mx = x + MathF.Cos(a0 + .5f) * (inner + outer) / 2 + (SeededRandom(seed + i + 40) - .5f) * 14; var my = y + MathF.Sin(a0 + .5f) * (inner + outer) / 2 * .6f;
                    var ex = x + MathF.Cos(a0 + .9f) * outer; var ey = y + MathF.Sin(a0 + .9f) * outer * .6f;
                    Line(sprites, sx, sy, mx, my, .8f, Rgb(190, 220, 255) * 1.6f, .78f * fade, 1);
                    Line(sprites, mx, my, ex, ey, .8f, Rgb(190, 220, 255) * 1.6f, .78f * fade, 1);
                }
                break;
            }
            case 3:
            {
                var fade = (1 - t) * flash.Intensity * strength;
                var radius = (14 + look.RingSizeRaw * .9f) * (.5f + .5f * t);
                sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, radius, KindStar), Color = new Vector4(ToLinear(flash.Color) * 1.5f, Math.Min(1, .8f * fade)), Dir = new Vector4(1, 0, 0, 0) });
                Glow(sprites, x, y, radius * 1.8f, radius * 1.4f, ToLinear(flash.Color), .5f * fade);
                break;
            }
        }
    }

    /// <summary>One jagged bolt between two heights; shared by the Lightning flash and the Lightning Storm layer.</summary>
    private static void AddBolt(GpuInstanceList<GpuSpriteInstance> sprites, float x, float top, float bottom, float fade)
    {
        if (fade <= .01f) return;
        var seed = (int)(x * 13 + bottom);
        float px = x, py = top;
        var glow = Rgb(140, 180, 255) * 1.8f;
        for (var i = 1; i < 9; i++)
        {
            var y = top + (bottom - top) * i / 8;
            var jitter = i == 8 ? 0 : (SeededRandom(seed + i * 7) - .5f) * 44;
            var nx = x + jitter;
            Line(sprites, px, py, nx, y, 2.2f, glow, .9f * fade, 1);
            px = nx; py = y;
        }
    }

    /// <summary>Hold phase, link channel: crackling arcs chaining simultaneously held keys (up to 6 links).</summary>
    private void AddElectricArcs(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, GpuSceneLayout layout)
    {
        int previous = -1, pairs = 0;
        var keyTop = layout.HitY;
        var glow = Rgb(130, 180, 255) * 1.8f;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount && pairs < 6; pitch++)
        {
            if (!_active[pitch]) continue;
            if (previous >= 0)
            {
                var x0 = layout.X(previous); var x1 = layout.X(pitch);
                var lift = 26 + Math.Abs(x1 - x0) * .12f;
                float px = x0, py = keyTop - 4;
                for (var i = 1; i <= 10; i++)
                {
                    var f = i / 10f;
                    var nx = x0 + (x1 - x0) * f;
                    var ny = keyTop - 4 - MathF.Sin(f * MathF.PI) * lift + (SeededRandom(pairs * 131 + i * 17 + (int)(_time * 24)) - .5f) * 16;
                    Line(sprites, px, py, nx, ny, 1.6f, glow, look.HoldArcIntensity, 1);
                    px = nx; py = ny;
                }
                pairs++;
            }
            previous = pitch;
        }
    }

    /// <summary>
    /// Note names engraved near the front of the white keys (only the Cs, or every white key when it is
    /// wide enough), sinking with the key when it is pressed.
    /// </summary>
    private void AddKeyLabels(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, GpuSceneLayout layout)
    {
        var whiteWidth = layout.Width / 52f;
        if (look.KeyLabels == 2 && whiteWidth < 13) return;
        var fontSize = Math.Clamp(whiteWidth * .48f, 7, 11);
        var frontHeight = layout.KeyboardHeight * (.05f + look.ShaderCameraTilt * .1f);
        var idle = look.KeyboardStyle == 2 ? Rgb(200, 205, 225) : Rgb(77, 79, 102);
        var whiteIndex = 0;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (PianoStage.IsBlackKey(pitch)) continue;
            var x = (whiteIndex + .5f) * whiteWidth;
            whiteIndex++;
            var isC = pitch % 12 == 0;
            if (look.KeyLabels == 1 && !isC) continue;
            var active = _active[pitch] && look.AnimateKeys;
            var sink = look.AnimateKeys ? _press[pitch] * frontHeight * .9f : 0; // the same hinge drop VsKey applies at the front edge
            var y = layout.Height - 14 + Math.Min(sink, frontHeight);
            var cell = look.KeyLabels == 2 ? AtlasShortCell + pitch : AtlasNoteCell + pitch;
            Glyph(sprites, x, y, fontSize, cell, active ? Vector3.One * 1.2f : idle, active ? 1 : .95f);
        }
    }
}
