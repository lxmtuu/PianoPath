using System.Numerics;

namespace PianoPath;

/// <summary>
/// The layers the GPU stage draws behind the note roll: guide lanes, drifting petals, the four ambient
/// families (energy, nature, light, cosmic) and the trails behind travelling notes. Positions are pure
/// functions of the simulation clock and the software stage's seeded hash, so both engines show the
/// same storm, the same galaxy and the same petal flight for the same moment of a song.
/// </summary>
internal sealed partial class GpuStageSimulation
{
    private readonly record struct Petal(float X, float Y, float Size, float Speed, float Sway, float Drift, float Spin, float Phase);
    private readonly List<Petal> _petals = [];
    private readonly Random _petalRandom = new(7);
    private int _petalCount;
    private float _petalWidth, _petalHeight;

    private static float Frac(double value) => (float)(value - Math.Floor(value));

    /// <summary>A soft glowing dot of roughly <paramref name="radius"/> px (the kind 0 particle).</summary>
    private static void Dot(GpuInstanceList<GpuSpriteInstance> list, float x, float y, float radius, Vector3 color, float alpha)
    {
        if (alpha <= .004f) return;
        list.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, radius * 2.2f, 0), Color = new Vector4(color, alpha), Dir = new Vector4(0, -1, 0, 0) });
    }

    /// <summary>Everything drawn after the background and before the notes. Call after <see cref="BuildNotes"/> (it reads the note trails).</summary>
    internal void BuildAmbient(GpuLook look, GpuSceneLayout layout, GpuInstanceList<GpuSpriteInstance> list)
    {
        list.Clear();
        var width = layout.Width;
        var height = layout.HitY;
        if (width < 1 || height < 1) return;
        if (!look.Chroma)
        {
            if (look.BackgroundGuide) AddLanes(list, layout);
            if (look.ShowPetals && look.PetalAmount > 0) AddPetals(list, look, width, height);
            if (look.ShowBackground && look.ShootingStars && look.ShootingStarsAmount > 0) AddShootingStars(list, look, width, height);
            if (look.AmbientEnergy != "None" && look.AmbientEnergyAmount > .01f) AddAmbientEnergy(list, look, layout);
            if (look.AmbientNature != "None" && look.AmbientNatureAmount > .01f) AddAmbientNature(list, look, width, height);
            if (look.AmbientLight != "None" && look.AmbientLightAmount > .01f) AddAmbientLight(list, look, width, height);
            if (look.AmbientCosmic != "None" && look.AmbientCosmicAmount > .01f) AddAmbientCosmic(list, look, width, height);
        }
        foreach (var trail in _noteTrails) AddNoteTrail(list, look, trail);
    }

    private static void AddLanes(GpuInstanceList<GpuSpriteInstance> list, GpuSceneLayout layout)
    {
        var color = Rgb(186, 141, 255);
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var c = pitch % 12 == 0;
            var alpha = (c ? 23 : pitch % 12 is 2 or 4 or 7 or 9 or 11 ? 10 : 5) / 255f;
            var x = layout.X(pitch);
            Line(list, x, 0, x, layout.HitY, c ? .55f : .35f, color * 1.4f, alpha, 0);
        }
    }

    private void AddPetals(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, float width, float height)
    {
        var count = Math.Clamp((int)Math.Round(look.PetalAmount * Math.Clamp(width / 1280, .45f, 1.6f)), 0, 150);
        if (count == 0) return;
        if (count != _petalCount || Math.Abs(width - _petalWidth) > .5f || Math.Abs(height - _petalHeight) > .5f)
        {
            _petals.Clear(); _petalCount = count; _petalWidth = width; _petalHeight = height;
            var scale = Math.Clamp(height / 720, .6f, 1.8f);
            float R() => (float)_petalRandom.NextDouble();
            for (var i = 0; i < count; i++)
                _petals.Add(new Petal(R() * width, R() * (height + 120), (2.6f + R() * 5.2f) * scale, 18 + R() * 52, 8 + R() * 34, .35f + R() * .9f, (R() - .5f) * 1.6f, R() * MathF.Tau));
        }
        var body = ToLinear(look.PetalColor);
        var tip = ToLinear(Vector3.Lerp(look.PetalColor, Vector3.One, .5f));
        const float margin = 60; var span = height + margin * 2;
        var e = _time;
        foreach (var petal in _petals)
        {
            var y = Frac((petal.Y + e * petal.Speed) / span) * span - margin;
            var x = petal.X + MathF.Sin((float)(e * petal.Drift) + petal.Phase) * petal.Sway;
            var angle = petal.Phase + (float)(e * petal.Spin);
            var squash = .42f + .58f * MathF.Abs(MathF.Sin((float)(e * petal.Drift * 1.35) + petal.Phase));
            Ellipse(list, x, y, petal.Size, petal.Size * squash, angle, body, 232 / 255f, .12f);
            var (sin, cos) = MathF.SinCos(angle);
            var ox = petal.Size * .3f; var oy = petal.Size * .16f;
            Ellipse(list, x + ox * cos - oy * sin, y + ox * sin + oy * cos, petal.Size * .5f, petal.Size * .5f * squash, angle, tip, 205 / 255f, .2f);
        }
    }

    /// <summary>
    /// Shooting stars (Style → ATMOSPHERE): every so often a meteor crosses the sky above the keyboard —
    /// a bright head with a soft skirt and a fading tail, on the same seeded schedule as the other
    /// ambient layers so both engines show it at the same moment. Drawn behind the note roll.
    /// </summary>
    private void AddShootingStars(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, float width, float height)
    {
        var count = 1 + (int)(look.ShootingStarsAmount * 4.99f);            // 1..5 meteors on their own clocks
        var e = _time;
        var visibility = .45f + .55f * look.ShootingStarsAmount;
        for (var k = 0; k < count; k++)
        {
            var period = 3.5f + SeededRandom(k * 7 + 1) * 5.5f;
            const float flight = .2f;                                        // the visible slice of each cycle
            var f = Frac((float)(e / period) + SeededRandom(k * 11 + 2));
            if (f >= flight) continue;
            var t = f / flight;
            var envelope = MathF.Sin(t * MathF.PI) * visibility;
            var sign = SeededRandom(k * 13 + 3) < .5f ? -1 : 1;
            var x0 = width * (.06f + SeededRandom(k * 17 + 4) * .88f);
            var y0 = -12 + SeededRandom(k * 19 + 5) * height * .28f;
            var len = height * (.1f + SeededRandom(k * 23 + 6) * .12f);
            var dx = sign * len * (1.1f + SeededRandom(k * 29 + 7) * .8f);
            var dy = len * (1f + SeededRandom(k * 31 + 8) * .5f);
            const float travel = 1.7f;
            var hx = x0 + dx * t * travel; var hy = y0 + dy * t * travel;
            var norm = MathF.Sqrt(dx * dx + dy * dy);
            var ux = dx / norm; var uy = dy / norm;
            var tx = hx - ux * len; var ty = hy - uy * len;
            var c = Rgb(225, 238, 255) * 1.5f;
            Line(list, tx, ty, hx, hy, 2.8f, c * .7f, 80 / 255f * envelope, 0);          // the soft skirt of the tail
            Line(list, tx + ux * len * .35f, ty + uy * len * .35f, hx, hy, .9f, c, 190 / 255f * envelope, 1);
            Glow(list, hx, hy, 3.4f, 3.4f, Vector3.One * 2f, .8f * envelope);
        }
    }

    private void AddAmbientEnergy(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, GpuSceneLayout layout)
    {
        var amount = look.AmbientEnergyAmount; var speed = look.AmbientEnergySpeed;
        var width = layout.Width; var height = layout.HitY; var e = _time;
        switch (look.AmbientEnergy)
        {
            case "Lightning Storm":
            {
                var cycle = e / (3.2 / speed);
                for (var k = 0; k < 2; k++)
                {
                    var f = Frac(cycle + k * .5);
                    if (f > .3f) continue;
                    var strike = (int)Math.Floor(cycle + k * .5);
                    AddBolt(list, SeededRandom(strike * 3 + 11) * width, 0, height * (.55f + SeededRandom(strike * 7 + 5) * .3f), (1 - f / .3f) * amount);
                }
                break;
            }
            case "Laser Beams":
            {
                var beams = 2 + (int)(amount * 3.99f);
                for (var i = 0; i < beams; i++)
                {
                    var sweep = MathF.Sin((float)(e * speed * (.5 + i * .23)) + i * 2.4f);
                    var x0 = width * (.15f + .7f * (i + .5f) / beams) + sweep * width * .18f;
                    var x1 = width * (.5f + MathF.Sin((float)(e * speed * .7) + i * 1.3f) * .4f);
                    Line(list, x0, -10, x1, height, 3, ToLinear(ColorFromHue(i * 360.0 / beams + e * 20)) * 1.6f, .8f * amount, 1);
                }
                var fired = 0;
                for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount && fired < 8; pitch++)
                {
                    if (!_active[pitch]) continue;
                    fired++;
                    var x = layout.X(pitch);
                    Line(list, x, height, x + MathF.Sin((float)e * 9 + pitch) * 8, height * .15f, 1.25f, ToLinear(_keyColor[pitch]) * 1.6f, 200 / 255f * amount, .3f);
                }
                break;
            }
            case "Confetti Rain":
            {
                var count = (int)(30 + amount * 120);
                for (var i = 0; i < count; i++)
                {
                    var fall = Frac(SeededRandom(i * 3 + 1) + e * speed * (.12 + SeededRandom(i * 5 + 2) * .2));
                    var x = SeededRandom(i * 7 + 3) * width + MathF.Sin((float)e * 2 + i) * 12;
                    var y = fall * (height + 40) - 20;
                    var angle = (SeededRandom(i) * 360 + (float)e * 120 * (SeededRandom(i + 50) > .5f ? 1 : -1)) * MathF.PI / 180;
                    list.Add(new GpuSpriteInstance { PosSize = new Vector4(x, y, 3.2f, 3), Color = new Vector4(ToLinear(ColorFromHue(SeededRandom(i * 11 + 4) * 360)), 230 / 255f * amount), Dir = new Vector4(MathF.Cos(angle), MathF.Sin(angle), 0, 0) });
                }
                break;
            }
            case "Fireworks":
            {
                var rockets = 1 + (int)(amount * 3.99f);
                for (var r = 0; r < rockets; r++)
                {
                    var period = (2.6 + SeededRandom(r * 13 + 1) * 2.4) / speed;
                    var f = Frac(e / period + SeededRandom(r * 17 + 2));
                    var x = width * (.12f + .76f * SeededRandom(r * 19 + 3));
                    var topY = height * (.15f + SeededRandom(r * 23 + 4) * .3f);
                    var c = ToLinear(ColorFromHue(SeededRandom(r * 29 + 5) * 360)) * 1.8f;
                    if (f < .35f)
                    {
                        var y = height + 10 - (height + 10 - topY) * f / .35f;
                        Line(list, x, y, x, y + 26, 1, Rgb(255, 220, 150) * 1.6f, 200 / 255f * amount, .6f);
                        Glow(list, x, y, 4, 4, Vector3.One * 2.2f, amount);
                    }
                    else
                    {
                        var boom = (f - .35f) / .65f;
                        var fade = amount * (1 - boom);
                        for (var i = 0; i < 26; i++)
                        {
                            var a = i / 26f * MathF.Tau + r;
                            var dist = boom * (46 + SeededRandom(r * 31 + i) * 60);
                            Dot(list, x + MathF.Cos(a) * dist, topY + MathF.Sin(a) * dist * .8f + boom * boom * 90, 2.2f, c, fade);
                        }
                        if (boom < .25f) Glow(list, x, topY, 14, 14, Vector3.One * 2.2f, 200 / 255f * amount * (1 - boom * 4));
                    }
                }
                break;
            }
        }
    }

    private void AddAmbientNature(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, float width, float height)
    {
        var amount = look.AmbientNatureAmount; var speed = look.AmbientNatureSpeed; var e = _time;
        switch (look.AmbientNature)
        {
            case "Rain":
            {
                var count = (int)(40 + amount * 160);
                var c = Rgb(150, 190, 235) * 1.3f;
                for (var i = 0; i < count; i++)
                {
                    var fall = Frac(SeededRandom(i * 3 + 7) + e * speed * (.5 + SeededRandom(i * 5 + 1) * .5));
                    var x = SeededRandom(i * 7 + 2) * (width + 100) - 50;
                    var y = fall * (height + 40) - 20;
                    Line(list, x, y, x - 7, y + 16, .6f, c, 150 / 255f * amount, .3f);
                }
                break;
            }
            case "Snow":
            {
                var count = (int)(30 + amount * 120);
                var c = Rgb(240, 246, 255);
                for (var i = 0; i < count; i++)
                {
                    var fall = Frac(SeededRandom(i * 3 + 9) + e * speed * (.05 + SeededRandom(i * 5 + 3) * .08));
                    var x = SeededRandom(i * 7 + 4) * width + MathF.Sin((float)(e * (.6 + SeededRandom(i) * 1.2)) + i * 1.7f) * 26;
                    var s = 1 + SeededRandom(i * 11 + 6) * 2.4f;
                    Ellipse(list, x, fall * (height + 30) - 15, s, s, 0, c, 225 / 255f * amount, .45f);
                }
                break;
            }
            case "Smoke":
            {
                var puffs = 4 + (int)(amount * 8);
                var c = Rgb(170, 170, 185);
                for (var i = 0; i < puffs; i++)
                {
                    var drift = Frac(SeededRandom(i * 13 + 1) + e * speed * .02 * (SeededRandom(i * 17 + 2) > .5f ? 1 : -1));
                    var s = 90 + SeededRandom(i * 23 + 4) * 150;
                    Ellipse(list, drift * (width + 400) - 200, height * (.35f + SeededRandom(i * 19 + 3) * .5f), s, s * .42f, 0, c, 40 / 255f * amount, 1);
                }
                break;
            }
            case "Leaves":
            {
                var count = (int)(12 + amount * 40);
                for (var i = 0; i < count; i++)
                {
                    var fall = Frac(SeededRandom(i * 3 + 5) + e * speed * (.06 + SeededRandom(i * 5 + 8) * .1));
                    var sway = MathF.Sin((float)(e * (1 + SeededRandom(i * 7 + 1) * 2)) + i * 2.2f);
                    var x = SeededRandom(i * 11 + 2) * width + sway * 60 * fall;
                    var autumn = SeededRandom(i * 13 + 6);
                    var c = autumn < .4f ? Rgb(235, 140, 60) : autumn < .7f ? Rgb(220, 90, 70) : Rgb(150, 190, 90);
                    var angle = (float)((e * (40 + SeededRandom(i) * 80) + i * 40) * Math.PI / 180);
                    Ellipse(list, x, fall * (height + 40) - 20, 5, 2.6f, angle, c, 235 / 255f * amount, .1f);
                }
                break;
            }
            case "Butterflies":
            {
                var count = 2 + (int)(amount * 8);
                for (var i = 0; i < count; i++)
                {
                    var t = e * speed * (.3 + SeededRandom(i * 3 + 2) * .3) + SeededRandom(i * 5 + 4) * 10;
                    var x = width * (.5f + .38f * MathF.Sin((float)(t * .7) + i * 2.1f));
                    var y = height * (.45f + .3f * MathF.Sin((float)(t * 1.1) + i * 1.3f));
                    var wing = 3 + MathF.Abs(MathF.Sin((float)e * 14 + i * 2)) * 7;
                    var c = ToLinear(ColorFromHue(SeededRandom(i * 7 + 8) * 360));
                    Ellipse(list, x - wing * .7f, y, wing, wing * .55f, 0, c, 235 / 255f * amount, .1f);
                    Ellipse(list, x + wing * .7f, y, wing, wing * .55f, 0, c, 235 / 255f * amount, .1f);
                    Ellipse(list, x, y, 1.6f, 3.2f, 0, Rgb(40, 30, 50), 235 / 255f * amount, .1f);
                }
                break;
            }
            case "Dust":
            {
                var count = (int)(16 + amount * 60);
                var c = Rgb(210, 190, 160);
                for (var i = 0; i < count; i++)
                {
                    var drift = Frac(SeededRandom(i * 3 + 3) + e * speed * .03 * (SeededRandom(i * 5 + 5) + .3));
                    var x = SeededRandom(i * 7 + 7) * width + MathF.Sin((float)e * .5f + i) * 20;
                    var s = 2 + SeededRandom(i * 11 + 1) * 5;
                    Ellipse(list, x, drift * (height + 60) - 30, s, s, 0, c, 70 / 255f * amount, .8f);
                }
                break;
            }
            case "Aurora":
            {
                var bands = 3 + (int)(amount * 4);
                for (var i = 0; i < bands; i++)
                {
                    var cx = width * (i + .5f) / bands + MathF.Sin((float)(e * speed * .4) + i * 1.8f) * width * .06f;
                    var w = width / bands * (.5f + SeededRandom(i * 7 + 1) * .5f);
                    var top = height * .05f;
                    var bottom = height * (.45f + SeededRandom(i * 11 + 2) * .25f);
                    var c = ToLinear(ColorFromHue(140 + SeededRandom(i * 13 + 3) * 140 + Math.Sin(e * speed * .3 + i) * 20)) * 1.4f;
                    list.Add(new GpuSpriteInstance { PosSize = new Vector4(cx, (top + bottom) / 2, w / 2, KindCurtain), Color = new Vector4(c, amount), Dir = new Vector4(1, 0, (float)(e * speed) + i * 2, (bottom - top) / 2) });
                }
                break;
            }
        }
    }

    private void AddAmbientLight(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, float width, float height)
    {
        var amount = look.AmbientLightAmount; var speed = look.AmbientLightSpeed; var e = _time;
        var tint = ToLinear(look.AmbientLightColor);
        switch (look.AmbientLight)
        {
            case "Spotlights":
            {
                // three hanging stage lights whose soft shafts sway with the music; the warm white is
                // pulled towards the layer tint so the look's colour still leads
                var shaft = Vector3.Lerp(Rgb(255, 244, 224), tint, .45f);
                var beat = look.TempoSync ? _beatPulse : _activity;   // with Tempo sync the rig rides the beat
                for (var i = 0; i < 3; i++)
                {
                    var anchor = width * (.22f + .28f * i);
                    var sway = MathF.Sin((float)(e * speed * .35) + i * 2.1f + beat * (1.4f + i * .3f));
                    var target = anchor + sway * width * (.07f + beat * .05f) + MathF.Sin((float)(e * speed * .13) + i * 1.3f) * width * .03f;
                    var flicker = .92f + .08f * MathF.Sin((float)(e * (9 + i * 2.3)) + i * 5) + beat * .12f;
                    for (var layer = 0; layer < 3; layer++)
                    {
                        var halfWidth = width * (.028f + layer * .034f);
                        var alpha = (.32f - layer * .09f) * amount * flicker;
                        Line(list, anchor, -12, target, height * .97f, halfWidth, shaft * (1.5f - layer * .25f), alpha, 0);
                    }
                    Glow(list, target, height * .97f, width * .05f, 9, shaft * 1.8f, .3f * amount * flicker);
                }
                break;
            }
            case "Gradient Wave":
            {
                for (var i = 0; i < 5; i++)
                {
                    var f = Frac(i / 5.0 + e * speed * .08);
                    var c = ToLinear(ColorFromHue(e * speed * 30 + i * 72));
                    // a soft band: fading in over its upper half and out over its lower half
                    Rect(list, 0, f * height - 30, width, 30, c, 70 / 255f * amount, 0, 1);
                    Rect(list, 0, f * height, width, 30, c, 70 / 255f * amount, 1, 0);
                }
                Rect(list, 0, 0, width, height, tint, 50 / 255f * amount, 1, 0);
                break;
            }
            case "Prism":
            {
                var cx = width * .5f; var cy = height * .34f;
                var rays = 3 + (int)(amount * 4);
                for (var i = 0; i < rays; i++)
                {
                    var a = (float)(e * speed * .5) + i * MathF.Tau / rays;
                    Line(list, cx, cy, cx + MathF.Cos(a) * width, cy + MathF.Sin(a) * width, 1.5f, ToLinear(ColorFromHue(i * 360.0 / rays)) * 1.5f, 170 / 255f * amount, .4f);
                }
                Glow(list, cx, cy, 16, 16, tint * 2, 220 / 255f * amount);
                Glow(list, cx, cy, 6, 6, Vector3.One * 2.5f, amount);
                break;
            }
            case "Color Splash":
            {
                var splats = 3 + (int)(amount * 7);
                for (var i = 0; i < splats; i++)
                {
                    var period = (3 + SeededRandom(i * 7 + 1) * 4) / speed;
                    var f = Frac(e / period + SeededRandom(i * 11 + 2));
                    var fade = MathF.Sin(f * MathF.PI);
                    var x = SeededRandom(i * 13 + 3) * width; var y = SeededRandom(i * 17 + 4) * height;
                    var c = ToLinear(ColorFromHue(SeededRandom(i * 19 + 5) * 360));
                    var s = (14 + SeededRandom(i * 23 + 6) * 30) * (.4f + .6f * fade);
                    Ellipse(list, x, y, s, s * .7f, 0, c, 150 / 255f * amount * fade, .35f);
                    for (var d = 0; d < 5; d++)
                    {
                        var a = SeededRandom(i * 31 + d) * MathF.Tau;
                        var dist = s * (1.1f + SeededRandom(i * 37 + d * 3) * .9f);
                        Ellipse(list, x + MathF.Cos(a) * dist, y + MathF.Sin(a) * dist * .7f, 2.5f, 2.5f, 0, c, 170 / 255f * amount * fade, .2f);
                    }
                }
                break;
            }
        }
    }

    private void AddAmbientCosmic(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, float width, float height)
    {
        var amount = look.AmbientCosmicAmount; var speed = look.AmbientCosmicSpeed; var e = _time;
        switch (look.AmbientCosmic)
        {
            case "Galaxy":
            {
                var cx = width * .5f; var cy = height * .4f;
                var maxR = Math.Min(width, height) * .45f;
                var stars = (int)(120 + amount * 380);
                var warm = new Vector3(1f, .9f, .78f);
                for (var i = 0; i < stars; i++)
                {
                    var f = SeededRandom(i * 3 + 1);
                    var r = f * maxR;
                    var a = f * 9 + (i % 3) * MathF.Tau / 3 + (float)(e * speed * .25);
                    var spread = (SeededRandom(i * 5 + 2) - .5f) * (8 + f * 46);
                    var x = cx + MathF.Cos(a) * r + MathF.Cos(a + 1.57f) * spread * .3f;
                    var y = cy + MathF.Sin(a) * r * .62f + MathF.Sin(a + 1.57f) * spread * .3f;
                    var c = ToLinear(Vector3.Lerp(warm, ColorFromHue(210 + f * 90), f)) * 1.5f;
                    var twinkle = .5f + .5f * MathF.Sin((float)(e * (2 + SeededRandom(i * 7 + 3) * 4)) + i);
                    var s = .8f + SeededRandom(i * 11 + 4) * 1.8f + (1 - f) * 1.2f;
                    Dot(list, x, y, s, c, 230 / 255f * amount * (.35f + .65f * twinkle));
                }
                Glow(list, cx, cy, 34, 22, Rgb(255, 240, 220) * 2, 120 / 255f * amount);
                break;
            }
            case "Black Hole":
            {
                var cx = width * .5f; var cy = height * .38f;
                var r = Math.Min(width, height) * .13f * (.8f + amount * .4f);
                Vector3[] ringColors = [Rgb(255, 200, 130), Rgb(255, 140, 90), Rgb(170, 90, 220)];
                for (var i = 0; i < 3; i++)
                {
                    var rr = r * (1.5f + i * .55f + MathF.Sin((float)(e * speed * 2) + i) * .06f);
                    RingShape(list, cx, cy, rr, rr * .38f, 7 - i * 1.8f, ringColors[i] * 1.8f, (170 - i * 45) / 255f * amount);
                }
                Ellipse(list, cx, cy, r, r * .62f, 0, Vector3.Zero, 1, .04f);
                RingShape(list, cx, cy, r, r * .62f, 1.6f, Rgb(255, 240, 220) * 2, amount);
                for (var i = 0; i < 24; i++)
                {
                    var f = Frac(SeededRandom(i * 3 + 5) + e * speed * (.2 + SeededRandom(i * 5 + 1) * .3));
                    var a = f * 12 + i;
                    var rr = r * 3.2f * (1 - f) + r * .8f;
                    Dot(list, cx + MathF.Cos(a) * rr, cy + MathF.Sin(a) * rr * .4f, 1.6f, Rgb(255, 220, 180) * 1.6f, 220 / 255f * amount * (1 - f * .5f));
                }
                break;
            }
            case "Matrix Rain":
            {
                var cols = (int)(6 + amount * 18);
                var headColor = Rgb(220, 255, 220) * 1.5f; var bodyColor = Rgb(60, 200, 90) * 1.2f; var cellColor = Rgb(40, 200, 90);
                for (var i = 0; i < cols; i++)
                {
                    var x = (i + .5f) * width / cols;
                    var fall = Frac(SeededRandom(i * 7 + 1) + e * speed * (.25 + SeededRandom(i * 11 + 2) * .4));
                    var headY = fall * (height + 100) - 50;
                    var tail = 6 + (int)(SeededRandom(i * 13 + 3) * 10);
                    for (var j = 0; j < tail; j++)
                    {
                        var y = headY - j * 16;
                        if (y < -20 || y > height + 20) continue;
                        var head = j == 0;
                        if (head || j % 4 == 0)
                        {
                            var code = (int)(SeededRandom(i * 17 + j * 3 + (int)(e * speed * 3)) * 96);
                            Glyph(list, x, y, 13, AtlasKatakanaCell + Math.Clamp(code, 0, 95), head ? headColor : bodyColor, (head ? 1f : 170 / 255f) * amount);
                        }
                        else Rect(list, x - 4, y - 7, 8, 12, cellColor, 120 / 255f * amount * (1 - j / (float)tail));
                    }
                }
                break;
            }
            case "Geometric":
            {
                var cx = width * .5f; var cy = height * .4f;
                var shapes = 2 + (int)(amount * 3);
                for (var i = 0; i < shapes; i++)
                {
                    var sides = 3 + (i % 4);
                    var rr = (30 + i * 34) * (.7f + amount * .5f);
                    var rot = (float)(e * speed * (.3 + i * .17)) * (i % 2 == 0 ? 1 : -1) + i;
                    var c = ToLinear(ColorFromHue(i * 360.0 / shapes + e * 10)) * 1.6f;
                    for (var s = 0; s < sides; s++)
                    {
                        var a0 = rot + s / (float)sides * MathF.Tau; var a1 = rot + (s + 1) / (float)sides * MathF.Tau;
                        Line(list, cx + MathF.Cos(a0) * rr, cy + MathF.Sin(a0) * rr * .8f, cx + MathF.Cos(a1) * rr, cy + MathF.Sin(a1) * rr * .8f, .9f, c, 200 / 255f * amount, .5f);
                    }
                    var sa = -rot * 1.7f;
                    Glow(list, cx + MathF.Cos(sa) * rr, cy + MathF.Sin(sa) * rr * .8f, 4, 4, Vector3.One * 2.2f, amount);
                }
                break;
            }
            case "Fractal":
            {
                var size = Math.Min(width, height) * .4f * (.6f + amount * .6f);
                var pulse = 1 + MathF.Sin((float)(e * speed * 1.5)) * .04f;
                var side = size * pulse;
                var apexY = height * .42f - size * .55f * pulse;
                var a = ToLinear(ColorFromHue(e * 12)) * 1.3f; var b = ToLinear(ColorFromHue(e * 12 + 140)) * 1.3f;
                list.Add(new GpuSpriteInstance { PosSize = new Vector4(width * .5f, apexY + side * .866f / 2, side / 2, KindSierpinski), Color = new Vector4(a, 120 / 255f * amount), Dir = new Vector4(b, 0) });
                break;
            }
        }
    }

    /// <summary>Falling phase, trail channel: Glow, Sparkles, Speed Lines, Blur, Ribbon, Rainbow or Stream behind a note.</summary>
    private void AddNoteTrail(GpuInstanceList<GpuSpriteInstance> list, GpuLook look, NoteTrail t)
    {
        var strength = look.FallingTrailIntensity * t.Opacity;
        if (strength <= .01f) return;
        var length = Math.Max(8, t.H * look.FallingTrailLength + 10);
        // the trail drags behind the motion: above the note while falling, below it while rising
        var top = t.Rising ? t.Y + t.H : t.Y - length;
        var bottom = top + length;
        var color = ToLinear(t.Color);
        var cx = t.X + t.W / 2;
        var e = _time; var pitch = t.Pitch;
        switch (look.FallingTrail)
        {
            case "Glow":
                Rect(list, t.X, top, t.W, length, color * 1.3f, 150 / 255f * strength, t.Rising ? 1 : 0, t.Rising ? 0 : 1);
                break;
            case "Sparkles":
            {
                var count = Math.Clamp((int)(length / 9), 2, 14);
                for (var i = 0; i < count; i++)
                {
                    var f = (i + .5f) / count;
                    var y = t.Rising ? top + f * length : top + (1 - f) * length;
                    var x = t.X + SeededRandom(pitch * 31 + i * 7) * t.W;
                    var twinkle = .35f + .65f * MathF.Abs(MathF.Sin((float)(e * (3 + SeededRandom(pitch + i) * 5)) + i * 1.7f));
                    var a = strength * (1 - f) * twinkle;
                    var s = 1 + SeededRandom(pitch * 57 + i * 13) * 2.2f;
                    var c = SeededRandom(pitch * 91 + i) < .4f ? Vector3.One * 1.6f : color * 1.6f;
                    Dot(list, x, y, s, c, a);
                    if (s > 2)
                    {
                        Line(list, x - s * 2, y, x + s * 2, y, .5f, c, a * .7f, 1);
                        Line(list, x, y - s * 2, x, y + s * 2, .5f, c, a * .7f, 1);
                    }
                }
                break;
            }
            case "Speed Lines":
            {
                var lines = Math.Clamp((int)(t.W / 5), 2, 6);
                for (var i = 0; i < lines; i++)
                {
                    var x = t.X + (i + .5f) * t.W / lines;
                    var scroll = Frac(SeededRandom(pitch * 17 + i * 3) + e * 2.2);
                    var y0 = t.Rising ? top + scroll * length * .5f : bottom - scroll * length * .5f;
                    var len = length * (.35f + .3f * SeededRandom(pitch + i * 11));
                    Line(list, x, y0, x, t.Rising ? y0 + len : y0 - len, .7f, Vector3.One * 1.4f, 190 / 255f * strength, 1);
                }
                break;
            }
            case "Blur":
                Rect(list, t.X, t.Rising ? t.Y : t.Y - length * .7f, t.W, t.H + length * .7f, color, 70 / 255f * strength);
                break;
            case "Ribbon":
            {
                float px = 0, py = 0;
                for (var i = 0; i <= 16; i++)
                {
                    var f = i / 16f;
                    var y = t.Rising ? top + f * length : bottom - f * length;
                    var x = cx + MathF.Sin(f * 6.28f + (float)e * 3 + pitch) * t.W * .45f * f;
                    if (i > 0) Line(list, px, py, x, y, Math.Max(1, t.W * .25f), color * 1.4f, 150 / 255f * strength, .8f);
                    px = x; py = y;
                }
                break;
            }
            case "Rainbow":
            {
                for (var i = 0; i < 7; i++)
                {
                    var f0 = i / 7f; var f1 = (i + 1) / 7f;
                    var y = t.Rising ? top + f0 * length : bottom - f1 * length;
                    var c = ToLinear(ColorFromHue((e * 90 + i * 360.0 / 7 + pitch * 9) % 360)) * 1.3f;
                    Rect(list, t.X, y, t.W, length / 7 + 1, c, 170 / 255f * strength * (1 - f0 * .7f));
                }
                break;
            }
            case "Stream":
            {
                var count = Math.Clamp((int)(length / 7), 3, 18);
                for (var i = 0; i < count; i++)
                {
                    var rate = .5 + SeededRandom(pitch * 23 + i * 5) * 1.2;
                    var f = Frac(SeededRandom(pitch * 41 + i * 3) + e * rate * .4);
                    var y = t.Rising ? top + f * length : bottom - f * length;
                    var x = cx + (SeededRandom(pitch * 13 + i * 29) - .5f) * t.W * 1.2f;
                    Dot(list, x, y, 1 + SeededRandom(pitch * 71 + i) * 1.8f, color * 1.5f, 230 / 255f * strength * (1 - f * .8f));
                }
                break;
            }
        }
    }
}
