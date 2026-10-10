using System.Numerics;
using System.Runtime.InteropServices;

namespace PianoPath;

[StructLayout(LayoutKind.Sequential)]
internal struct GpuNoteInstance { public Vector4 Rect, Color, Misc; }

[StructLayout(LayoutKind.Sequential)]
internal struct GpuKeyInstance { public Vector4 Box, Emit, Misc, Base; }

[StructLayout(LayoutKind.Sequential)]
internal struct GpuSpriteInstance { public Vector4 PosSize, Color, Dir; }

/// <summary>A recent MIDI/note impact retained briefly so the camera-calibrated AR pass can build its 3D transient.</summary>
internal readonly record struct GpuArImpact(int Pitch, double Age, float Strength, Vector3 Color);

/// <summary>A live stage particle with its source pitch, so the AR pass can lift the same simulated effect into the calibrated piano pose.</summary>
internal readonly record struct GpuArParticle(int Pitch, float X, float Y, float Vx, float Vy, float Age, float Life, float Size,
    float Phase, float Glow, Vector3 Color, byte Shape, bool Wisp, byte Origin);

/// <summary>A live stage impact/release wave, retaining its pitch and visual channel for camera-space projection.</summary>
internal readonly record struct GpuArRing(int Pitch, float X, float Y, float Age, float Life, float Strength, byte Kind,
    Vector3 Color, byte Channel, byte Origin);

/// <summary>A live stage impact/morph flash, retaining its pitch and style for camera-space projection.</summary>
internal readonly record struct GpuArFlash(int Pitch, float X, float Y, float Age, float Life, float Size, float Strength,
    byte Style, Vector3 Color, float Intensity, byte Channel, byte Origin);

/// <summary>Scene layout of one output, in stage DIPs (the software stage's coordinate system).</summary>
internal readonly struct GpuSceneLayout
{
    internal GpuSceneLayout(float width, float height, float keyboardFraction)
    {
        Width = width; Height = height;
        KeyboardHeight = height * keyboardFraction;
        HitY = height - KeyboardHeight;
        Keys = KeyboardLayout.Active;
        Lane = width / Keys.KeyCount;
    }
    internal float Width { get; }
    internal float Height { get; }
    internal float KeyboardHeight { get; }
    internal float HitY { get; }
    internal float Lane { get; }
    /// <summary>The keyboard this output was laid out for, taken once so one frame never mixes two keyboards.</summary>
    internal KeyboardLayout Keys { get; }
    internal float X(int pitch) => (float)PianoStage.KeyCenterOf(pitch) * Width;
}

/// <summary>
/// Everything that moves on the GPU stage and is not a song note: particles, rings, flashes, live
/// trails, key presses and key heat. It lives on the render thread only and advances once per rendered
/// frame, so its motion is as smooth as the output's refresh rate - 144 Hz output means 144 simulation
/// steps per second, independent of the WPF composition rate.
/// </summary>
internal sealed partial class GpuStageSimulation
{
    internal const int MaxParticles = 24000;
    /// <summary>The keyboard being drawn (<see cref="KeyboardLayout.Active"/>); read on the render thread, one reference at a time.</summary>
    private static int FirstPitch => KeyboardLayout.Active.FirstPitch;
    private static int KeyCount => KeyboardLayout.Active.KeyCount;
    private static bool OnKeyboard(int pitch) => KeyboardLayout.Active.Contains(pitch);

    private enum Shape : byte { Dot, Streak, Confetti, Dust, Flame, Droplet, Shard }

    private struct Particle
    {
        public float X, Y, Vx, Vy, Age, Life, Size, Phase, Grav, DragK, Mass, Glow;
        public int Pitch;
        public byte Origin;
        public Vector3 Color;
        public Shape Shape;
        public bool Wisp;
    }

    /// <summary>Impact/release waves; Channel 0 = impact wave, 1 = impact morph, 2 = release effect.</summary>
    private struct Ring { public float X, Y, Age, Life, Strength; public int Pitch; public byte Kind, Channel, Origin; public Vector3 Color; }
    /// <summary>Impact/morph flash; Channel 0 = flash setting, 1 = impact morph.</summary>
    private struct Flash { public float X, Y, Age, Life, Size, Strength; public int Pitch; public byte Style, Channel, Origin; public Vector3 Color; public float Intensity; }

    private sealed class LiveTrail
    {
        public int Pitch; public float Age, Held, Strength; public bool KeyDown, Released, Hit;
    }

    private readonly Particle[] _particles = new Particle[MaxParticles];
    private int _count;
    private readonly List<Ring> _rings = [];
    private readonly List<Flash> _flashes = [];
    private readonly List<LiveTrail> _trails = [];
    private readonly List<ArLivePress> _livePresses = [];
    private readonly List<GpuArImpact> _arImpacts = [];
    private readonly float[] _press = new float[128], _glow = new float[128], _heat = new float[128], _spill = new float[128];
    private readonly float[] _sparkBudget = new float[128], _wispBudget = new float[128], _flameBudget = new float[128];
    private readonly bool[] _active = new bool[128];
    private readonly Vector3[] _keyColor = new Vector3[128];
    private readonly Random _random;
    private double _time;

    internal GpuStageSimulation() : this(20260930) { }
    internal GpuStageSimulation(int seed) => _random = new Random(seed);
    private float _simWidth = 1280, _activity, _beatPulse;
    private GpuLook _look = new();

    internal double Time => _time;
    internal int ParticleCount => _count;
    internal int LiveTrailCount => _trails.Count;
    internal int NoteTrailCount => _noteTrails.Count;

    /// <summary>
    /// The keys the player is pressing, or has just let go of, one entry per live trail, written where the trails are
    /// aged (<see cref="Step"/>) and read by the AR layer. The player's own hands: a song's notes travel in the notes
    /// list, this is what the keys under the fingers draw.
    /// </summary>
    internal IReadOnlyList<ArLivePress> LivePresses => _livePresses;
    /// <summary>Recent strikes for the camera-projected impact-light pass.</summary>
    internal IReadOnlyList<GpuArImpact> ArImpacts => _arImpacts;
    /// <summary>The same simulated particles, waves and flashes used by the stage, with the source pitch preserved for 3D AR projection.</summary>
    internal int ArParticleCount => _count;
    internal int ArRingCount => _rings.Count;
    internal int ArFlashCount => _flashes.Count;
    internal GpuArParticle ArParticleAt(int index)
    {
        ref var p = ref _particles[index];
        return new GpuArParticle(p.Pitch, p.X, p.Y, p.Vx, p.Vy, p.Age, p.Life, p.Size, p.Phase, p.Glow, p.Color, (byte)p.Shape, p.Wisp, p.Origin);
    }
    internal GpuArRing ArRingAt(int index)
    {
        var r = _rings[index];
        return new GpuArRing(r.Pitch, r.X, r.Y, r.Age, r.Life, r.Strength, r.Kind, r.Color, r.Channel, r.Origin);
    }
    internal GpuArFlash ArFlashAt(int index)
    {
        var f = _flashes[index];
        return new GpuArFlash(f.Pitch, f.X, f.Y, f.Age, f.Life, f.Size, f.Strength, f.Style, f.Color, f.Intensity, f.Channel, f.Origin);
    }
    internal bool IsActive(int pitch) => pitch is >= 0 and < 128 && _active[pitch];
    internal Vector3 HorizonColor { get; private set; }
    internal float Activity => _activity;
    private float _pedalBoost = 1;
    /// <summary>Pedal Glow: 1, or up to 2 while the sustain pedal is down (eased so the glow swells and settles).</summary>
    internal float PedalBoost => _pedalBoost;
    private float _energy;
    /// <summary>Audio reactive: 1, or more after a run of note onsets (the software stage's EnergyBoost envelope).</summary>
    internal float EnergyBoost => _look.AudioReactive ? 1 + _energy * _look.AudioReactiveAmount : 1;
    /// <summary>Pedal glow and audio reactive together, for everything the software stage lights with both.</summary>
    internal float GlowBoost => _pedalBoost * EnergyBoost;
    internal float SimulationWidth => _simWidth;
    internal float SimulationHitY { get; private set; }
    internal float SimulationKeyboardHeight { get; private set; }
    /// <summary>Linear note colour and activity (0..1) per pitch, uploaded for the hit line.</summary>
    internal readonly Vector4[] KeyColors = new Vector4[128];

    internal static Vector3 ToLinear(Vector3 srgb) => new(MathF.Pow(srgb.X, 2.2f), MathF.Pow(srgb.Y, 2.2f), MathF.Pow(srgb.Z, 2.2f));

    private static Vector3 Hue(double degrees)
    {
        var h = ((degrees % 360) + 360) % 360 / 60;
        var x = (float)(1 - Math.Abs(h % 2 - 1));
        return (int)h switch
        {
            0 => new Vector3(1, x, 0), 1 => new Vector3(x, 1, 0), 2 => new Vector3(0, 1, x),
            3 => new Vector3(0, x, 1), 4 => new Vector3(x, 0, 1), _ => new Vector3(1, 0, x)
        };
    }

    /// <summary>The colour a hard press tints toward, for the live trails and for the AR bars of the keys the player presses.</summary>
    internal static Vector3 VelocityTint(float strength)
    {
        var t = Math.Clamp(strength, 0, 1.2f) / 1.2f;
        return Vector3.Lerp(new Vector3(80 / 255f, 140 / 255f, 1f), new Vector3(1f, 70 / 255f, 60 / 255f), t);
    }

    private float Rand() => (float)_random.NextDouble();

    internal void Clear()
    {
        _count = 0; _rings.Clear(); _flashes.Clear(); _trails.Clear(); _arImpacts.Clear();
        Array.Clear(_press); Array.Clear(_glow); Array.Clear(_heat); Array.Clear(_active);
    }

    /// <summary>Advances the simulation by one rendered frame.</summary>
    internal void Step(double seconds, GpuFrameInput input, GpuStageFeed feed, float sceneWidth)
    {
        var look = _look = input.Look;
        if (feed.TakeClearRequest()) Clear();
        var frameDt = (float)Math.Clamp(seconds, 0, .05);
        for (var i = _arImpacts.Count - 1; i >= 0; i--)
        {
            var aged = _arImpacts[i] with { Age = _arImpacts[i].Age + frameDt };
            if (aged.Age > .85) _arImpacts.RemoveAt(i);
            else _arImpacts[i] = aged;
        }
        var particleDt = frameDt * look.PhysicsTimeFactor;
        // The physics slow-motion control belongs to particles; the stage clock, notes and ambient layers
        // must keep their real tempo so changing it cannot slow the whole composition.
        _time += frameDt;
        _beatPulse = look.TempoSync ? input.BeatPulse : 0;
        _simWidth = Math.Max(1, sceneWidth);
        var layout = new GpuSceneLayout(_simWidth, (float)input.StageHeightDip, look.KeyboardFraction);
        SimulationHitY = layout.HitY;
        SimulationKeyboardHeight = layout.KeyboardHeight;

        // ---- which keys sound, and in which colour ----
        Array.Clear(_active);
        if (input.Playing && input.Notes.Count > 0)
        {
            try
            {
                var notes = input.Notes;
                for (var i = NoteTimeline.FirstIndexAtOrAfter(notes, input.Position - input.MaxNoteDuration); i < notes.Count; i++)
                {
                    var note = notes[i];
                    if (note.Start > input.Position) break;
                    if (note.End <= input.Position || note.Pitch is < 0 or > 127) continue;
                    _active[note.Pitch] = true; _keyColor[note.Pitch] = look.NoteColor(note.Pitch, note.Track);
                }
            }
            catch (ArgumentOutOfRangeException) { /* the song was swapped mid-scan; the next frame reads the new list */ }
        }
        for (var pitch = 0; pitch < 128; pitch++)
        {
            if (input.Pressed[pitch]) { _active[pitch] = true; _keyColor[pitch] = look.NoteColor(pitch, 0); }
            if (!_active[pitch]) _keyColor[pitch] = look.NoteColor(pitch, 0);
            if (look.PressedKeyFixed && _active[pitch]) _keyColor[pitch] = look.PressedKeyColor;
        }

        // ---- live events and hits from the UI thread ----
        while (feed.TryDequeueLive(out var live))
        {
            if (live.Down) _trails.Add(new LiveTrail { Pitch = live.Pitch, KeyDown = true, Strength = live.Strength });
            else
            {
                for (var i = _trails.Count - 1; i >= 0; i--)
                    if (_trails[i].Pitch == live.Pitch && _trails[i].KeyDown) { _trails[i].KeyDown = false; _trails[i].Released = true; SpawnReleaseFx(live.Pitch, layout); break; }
            }
        }
        while (feed.TryDequeueHit(out var hit)) Impact(hit.Pitch, hit.Strength, layout);
        ScanReleaseFx(input, layout);

        // ---- keys: press animation, glow, heat, spill ----
        var totalGlow = 0f; var horizon = Vector3.Zero;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var target = _active[pitch] ? 1f : 0f;
            _press[pitch] += (target - _press[pitch]) * (1 - MathF.Exp(-frameDt * (target > _press[pitch] ? 38 : 16)));
            _glow[pitch] = _active[pitch] ? Math.Max(_glow[pitch], Math.Min(1, _glow[pitch] + frameDt * 12)) : Math.Max(0, _glow[pitch] - frameDt * 3.2f);
            _heat[pitch] = _active[pitch] ? Math.Min(1, _heat[pitch] + frameDt * 7) : Math.Max(0, _heat[pitch] - frameDt * 2.4f);
            totalGlow += _glow[pitch];
            horizon += _keyColor[pitch] * _glow[pitch];
        }
        var radius = 1 + look.KeyGlowRadius * 5;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var spill = 0f;
            for (var other = Math.Max(FirstPitch, pitch - 8); other <= Math.Min(FirstPitch + KeyCount - 1, pitch + 8); other++)
                if (other != pitch && _glow[other] > 0) spill += _glow[other] * MathF.Exp(-Math.Abs(other - pitch) / radius);
            _spill[pitch] = Math.Min(1, spill);
        }
        _activity = Math.Min(1, totalGlow / 4);
        var pedalTarget = look.PedalGlow && input.Sustain ? 1 + look.PedalGlowIntensity : 1;
        _pedalBoost += (pedalTarget - _pedalBoost) * (1 - MathF.Exp(-frameDt * 10));
        _energy *= MathF.Exp(-2.2f * frameDt);
        // the theme's halo colour, as the software stage paints it, warmed a little by the keys that sound
        HorizonColor = ToLinear(totalGlow > .01f ? Vector3.Lerp(look.HaloColor, horizon / totalGlow, .35f * Math.Min(1, totalGlow)) : look.HaloColor);
        for (var pitch = 0; pitch < 128; pitch++)
        {
            var c = ToLinear(_keyColor[pitch]);
            KeyColors[pitch] = new Vector4(c, OnKeyboard(pitch) ? _glow[pitch] : 0);
        }

        // ---- continuous emitters on sounding keys ----
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!_active[pitch] && _heat[pitch] <= .01f) continue;
            var x = layout.X(pitch);
            var color = _keyColor[pitch];
            if (_active[pitch] && look.ShowEmbers && look.ParticleAmount > 0)
            {
                // The held-key emitter follows the selected burst family, so a water or confetti preset
                // stays coherent between the initial hit and the note's sustain.
                _sparkBudget[pitch] += look.ParticleAmount * 1.3f * frameDt;
                var emitterStyle = look.ImpactBurst == "Zone"
                    ? pitch < look.ZoneSplitPitch ? "Embers" : "Splash"
                    : look.ImpactBurst;
                while (_sparkBudget[pitch] >= 1 && _count < MaxParticles)
                {
                    _sparkBudget[pitch] -= 1;
                    var angle = -MathF.PI / 2 + (Rand() - .5f) * look.ParticleSpread * MathF.PI;
                    var speedScale = 1f; var lifeScale = 1f; var sizeScale = 1f;
                    var grav = .55f; var drag = 1.2f; var glow = 1f;
                    var shape = Shape.Streak;
                    var particleColor = Vector3.Lerp(color, Vector3.One, .24f);
                    switch (emitterStyle)
                    {
                        case "Splash":
                            shape = Shape.Droplet; grav = 1.35f; drag = .85f; speedScale = .86f; lifeScale = 1.08f;
                            particleColor = Vector3.Lerp(color, new Vector3(.58f, .82f, 1f), .48f);
                            break;
                        case "Fireworks":
                            angle = Rand() * MathF.Tau; speedScale = 1.15f; lifeScale = 1.22f; sizeScale = .9f;
                            particleColor = Vector3.Lerp(color, Hue(Rand() * 360), .56f);
                            break;
                        case "Confetti":
                            shape = Shape.Confetti; grav = .38f; drag = 2.8f; speedScale = .68f; lifeScale = 1.75f; sizeScale = 1.25f; glow = .72f;
                            particleColor = Vector3.Lerp(color, Hue(Rand() * 360), .52f);
                            break;
                        case "Dust":
                            shape = Shape.Dust; grav = -.1f; drag = 2.2f; speedScale = .3f; lifeScale = 1.65f; sizeScale = 1.8f; glow = .38f;
                            particleColor = Vector3.Lerp(new Vector3(.72f, .67f, .6f), color, .38f);
                            break;
                        default:
                            particleColor = Rand() < .22f ? Vector3.One : particleColor;
                            if (Rand() < .3f) shape = Shape.Dot;
                            break;
                    }
                    var speedVariation = 1 + (Rand() - .5f) * 2 * look.ParticleRandomness;
                    var lifeVariation = 1 + (Rand() - .5f) * 2 * look.ParticleLifeRandomness;
                    var sizeVariation = 1 + (Rand() - .5f) * 2 * look.ParticleSizeRandomness;
                    var speed = look.ParticleVelocity * Math.Max(.08f, speedVariation) * look.ParticleSpeed * speedScale;
                    Spawn(new Particle
                    {
                        X = x + (Rand() - .5f) * layout.Lane * (.3f + look.EmitterSize), Y = layout.HitY - 1,
                        Vx = MathF.Cos(angle) * speed, Vy = MathF.Sin(angle) * speed,
                        Life = Math.Max(.05f, look.ParticleLife * lifeVariation * lifeScale),
                        Size = Math.Max(.2f, look.ParticleSize * sizeVariation * sizeScale), Mass = .7f, Grav = grav, DragK = drag,
                        Phase = Rand() * MathF.Tau, Glow = glow, Color = particleColor, Shape = shape
                    }, pitch, origin: 2);
                }
            }
            if (_active[pitch] && look.ShowWisps && look.WispAmount > 0)
            {
                // two strands per key that twist around each other as they climb (the rainbow capture's smoke ribbons)
                _wispBudget[pitch] += look.WispAmount * 4f * frameDt;
                var life = .35f + look.WispHeight * 1.9f;
                while (_wispBudget[pitch] >= 1 && _count < MaxParticles)
                {
                    _wispBudget[pitch] -= 1;
                    var strand = _random.Next(2);
                    Spawn(new Particle
                    {
                        Wisp = true, X = x + (Rand() - .5f) * layout.Lane * .25f, Y = layout.HitY - 2 - Rand() * 3,
                        Vx = (Rand() - .5f) * 8, Vy = -look.WispSpeed * (.75f + Rand() * .5f),
                        Life = life * (.7f + Rand() * .6f), Phase = strand * MathF.PI + (float)(_time * 1.7) + (Rand() - .5f) * .5f,
                        Size = .6f + Rand() * 1.2f, Glow = look.WispGlow,
                        Color = Rand() < .2f ? Vector3.Lerp(color, Vector3.One, .6f) : color, Shape = Shape.Dot
                    }, pitch, origin: 2);
                }
            }
            if (look.ShowFlame && _heat[pitch] > .01f)
            {
                _flameBudget[pitch] += 70 * look.FlameIntensity * _heat[pitch] * frameDt;
                while (_flameBudget[pitch] >= 1 && _count < MaxParticles)
                {
                    _flameBudget[pitch] -= 1;
                    Spawn(new Particle
                    {
                        X = x + (Rand() - .5f) * layout.Lane * .7f, Y = layout.HitY - 1,
                        Vx = (Rand() - .5f) * 14, Vy = -(50 + 150 * look.FlameHeight) * (.6f + Rand() * .6f),
                        Life = .22f + look.FlameHeight * .45f * (.6f + Rand() * .6f),
                        Size = layout.Lane * (.35f + Rand() * .3f), Phase = Rand() * MathF.Tau, Glow = look.FlameIntensity,
                        Color = look.FlameNoteColor ? color : new Vector3(1f, .55f, .15f), Shape = Shape.Flame
                    }, pitch, origin: 2);
                }
            }
        }

        // ---- integrate ----
        var turbulence = look.WispTurbulence;
        var evolution = (float)(_time * look.EvolutionSpeed);
        var damping = look.Drag;
        for (var i = _count - 1; i >= 0; i--)
        {
            ref var p = ref _particles[i];
            p.Age += particleDt;
            if (p.Age >= p.Life) { _particles[i] = _particles[--_count]; continue; }
            if (p.Wisp)
            {
                p.Y += p.Vy * particleDt;
                p.X += (p.Vx + look.VectorField * MathF.Sin(p.Y / look.FieldScale + evolution) * 6) * particleDt;
                p.Vy *= MathF.Exp(-.35f * particleDt);
                continue;
            }
            if (p.Shape == Shape.Flame)
            {
                p.X += p.Vx * particleDt; p.Y += p.Vy * particleDt;
                p.Vx += MathF.Sin(p.Age * 11 + p.Phase) * 40 * particleDt;
                continue;
            }
            p.X += p.Vx * particleDt; p.Y += p.Vy * particleDt;
            p.Vx += look.VectorField * MathF.Sin(p.Y / look.FieldScale + evolution) * 14 * particleDt;
            var heatRatio = Math.Clamp(1 - p.Age / p.Life, 0, 1);
            p.Vy += (look.Gravity * p.Grav - 38 * heatRatio / Math.Max(.2f, p.Mass)) * particleDt;
            if (p.Shape == Shape.Confetti) p.Vx += MathF.Sin(p.Age * 9 + p.Phase) * 60 * particleDt;
            p.Vx += MathF.Sin(p.Y * .04f + p.Phase + (float)_time * 3.2f) * 14 * heatRatio * particleDt;
            var d = MathF.Exp(-damping * p.DragK * particleDt); p.Vx *= d; p.Vy *= d;
        }
        for (var i = _rings.Count - 1; i >= 0; i--) { var r = _rings[i]; r.Age += frameDt; if (r.Age >= r.Life) _rings.RemoveAt(i); else _rings[i] = r; }
        for (var i = _flashes.Count - 1; i >= 0; i--) { var f = _flashes[i]; f.Age += frameDt; if (f.Age >= f.Life) _flashes.RemoveAt(i); else _flashes[i] = f; }
        for (var i = _trails.Count - 1; i >= 0; i--)
        {
            var trail = _trails[i];
            trail.Age += frameDt;
            if (trail.KeyDown) trail.Held += frameDt;
            if (look.Rising)
            {
                trail.Hit = true;
                var tailY = layout.HitY - (trail.Age - trail.Held) * look.LiveFallSpeed;
                if (trail.Released && tailY < -32) _trails.RemoveAt(i);
            }
            else
            {
                var y = 28 + trail.Age * look.LiveFallSpeed;
                if (!trail.Hit && y >= layout.HitY) { trail.Hit = true; Impact(trail.Pitch, .8f, layout); }
                var tailY = y - 28 - trail.Held * look.LiveFallSpeed;
                if (trail.Released && tailY > layout.HitY + 32) _trails.RemoveAt(i);
            }
        }
        // what the AR layer draws for the player's own hands, in the order the keys went down
        _livePresses.Clear();
        foreach (var trail in _trails) _livePresses.Add(new ArLivePress(trail.Pitch, trail.Age, trail.Held, trail.KeyDown, trail.Strength));
    }

    private void Spawn(in Particle particle, int pitch, byte origin = 0)
    {
        if (_count >= MaxParticles) return;
        var tagged = particle;
        tagged.Pitch = pitch;
        tagged.Origin = origin;
        _particles[_count++] = tagged;
    }

    /// <summary>A note reached the keys: wave, flash, morph and burst, styled like the software stage.</summary>
    private void Impact(int pitch, float strength, GpuSceneLayout layout)
    {
        var look = _look;
        var clamped = Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1);
        var x = layout.X(clamped);
        var y = layout.HitY - 1;
        var color = _active[clamped] ? _keyColor[clamped] : look.NoteColor(clamped, 0);
        if (look.VelocityColor && look.VelocityColorAmount > 0) color = Vector3.Lerp(color, VelocityTint(strength), look.VelocityColorAmount);
        if (_arImpacts.Count >= 48) _arImpacts.RemoveAt(0);
        _arImpacts.Add(new GpuArImpact(clamped, 0, Math.Clamp(strength, 0, 1), color));
        _glow[clamped] = Math.Max(_glow[clamped], Math.Min(1, .6f + strength * .4f));
        _energy = Math.Min(1.5f, _energy + .22f * strength); // audio reactive envelope attack
        // wave channel
        if (look.ShowImpactRings && look.ImpactWave != "None" && look.RingSizeRaw > 0 && _rings.Count < 96)
            _rings.Add(new Ring { X = x, Y = y, Life = .55f, Strength = strength, Color = color, Pitch = clamped, Kind = (byte)(look.ImpactWave switch { "Shockwave" => 1, "Ripple" => 2, _ => 0 }), Channel = 0, Origin = 0 });
        // flash channel: the anamorphic flare, a lightning strike or a plasma ball
        if (look.ShowImpactFlash && look.ImpactFlashIntensity > 0 && _flashes.Count < 48)
        {
            if (look.ImpactFlashStyle == 0)
                _flashes.Add(new Flash { X = x, Y = y, Life = .28f, Size = layout.Lane * (1.6f + strength * 1.6f) * (.5f + look.ImpactFlashIntensity), Color = Vector3.Lerp(color, Vector3.One, .35f), Intensity = 2.4f * look.ImpactFlashIntensity * (.5f + strength), Strength = strength, Pitch = clamped, Channel = 0, Origin = 0 });
            else
                _flashes.Add(new Flash { X = x, Y = y, Life = .18f, Color = color, Strength = strength, Style = (byte)look.ImpactFlashStyle, Intensity = look.ImpactFlashIntensity, Pitch = clamped, Channel = 0, Origin = 0 });
        }
        SpawnImpactMorph(clamped, x, y, color, strength);
        if (!look.ShowEmbers || look.ParticleAmount < 1) return;
        var style = look.ImpactBurst == "Zone" ? (clamped < look.ZoneSplitPitch ? "Embers" : "Splash") : look.ImpactBurst;
        var amount = Math.Clamp((int)(look.ParticleAmount * strength * look.ParticleResponse / 55f * 2.2f), 0, 300);
        for (var i = 0; i < amount && _count < MaxParticles; i++)
        {
            var shape = Shape.Dot; var grav = 1f; var drag = 1f; var speedScale = 1f; var lifeScale = 1f; var sizeScale = 1f;
            var c = i % 4 == 0 ? Vector3.Lerp(color, Vector3.One, .72f) : Vector3.Lerp(color, Hue(clamped * 4.1 + (Rand() - .5f) * 28), .18f);
            switch (style)
            {
                case "Splash":
                    shape = Shape.Droplet; grav = 1.6f; c = Vector3.Lerp(color, new Vector3(.58f, .82f, 1f), .46f);
                    c = Vector3.Lerp(c, Vector3.One, Rand() * .24f); speedScale = .82f; lifeScale = 1.1f; break;
                case "Fireworks":
                    speedScale = 1.25f; lifeScale = 1.5f; sizeScale = .9f; c = Vector3.Lerp(color, Hue(Rand() * 360), .64f); break;
                case "Confetti":
                    shape = Shape.Confetti; grav = .32f; drag = 3.2f; c = Rand() < .2f ? Vector3.One : Vector3.Lerp(color, Hue(Rand() * 360), .54f);
                    speedScale = .7f; lifeScale = 2.2f; sizeScale = 1.3f; break;
                case "Dust":
                    shape = Shape.Dust; grav = -.12f; drag = 2.4f; c = Vector3.Lerp(new Vector3(.66f, .61f, .55f), color, .34f);
                    speedScale = .35f; lifeScale = 2.4f; sizeScale = 2.2f; break;
            }
            var needle = i % 3 != 0 && shape == Shape.Dot;
            if (needle) shape = Shape.Streak;
            var angle = -MathF.PI / 2 + (Rand() - .5f) * look.ParticleSpread * MathF.PI;
            angle += MathF.Sin(i * .37f + (float)_time * look.EvolutionSpeed) * look.Spiral * .3f;
            if (style == "Fireworks") angle = Rand() * MathF.Tau;
            var speedVariation = 1 + (Rand() - .5f) * 2 * look.ParticleRandomness;
            var lifeVariation = 1 + (Rand() - .5f) * 2 * look.ParticleLifeRandomness;
            var sizeVariation = 1 + (Rand() - .5f) * 2 * look.ParticleSizeRandomness;
            var speed = look.ParticleVelocity * Math.Max(.08f, speedVariation) * look.ParticleSpeed * strength * speedScale;
            var life = Math.Max(.05f, look.ParticleLife * lifeVariation * lifeScale);
            Spawn(new Particle
            {
                X = x + (Rand() - .5f) * look.EmitterSize * layout.Lane * 2, Y = y,
                Vx = MathF.Cos(angle) * speed, Vy = MathF.Sin(angle) * speed - (needle ? 35 : 15),
                Life = life, Mass = needle ? .6f : 1.5f, Phase = Rand() * MathF.Tau,
                Size = Math.Max(.2f, look.ParticleSize * sizeVariation * sizeScale),
                Color = c, Shape = shape, Grav = grav, DragK = drag, Glow = 1
            }, clamped, origin: 0);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // instance building (per output)
    // ------------------------------------------------------------------------------------------------

    private int[] _nextOfPitch = new int[64];

    /// <summary>
    /// For each song note, the index of the next note of the same pitch in <paramref name="list"/> (sorted by start), or -1.
    /// AR-S4: the minimum note length uses it so that a following note on the same key cuts the growth of the one before.
    /// </summary>
    private int[] NextOfSamePitch(IReadOnlyList<NoteEvent> list)
    {
        if (_nextOfPitch.Length < list.Count) _nextOfPitch = new int[Math.Max(list.Count, _nextOfPitch.Length * 2)];
        Span<int> last = stackalloc int[128];
        last.Fill(-1);
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var pitch = Math.Clamp(list[i].Pitch, 0, 127);
            _nextOfPitch[i] = last[pitch];
            last[pitch] = i;
        }
        return _nextOfPitch;
    }

    /// <summary>
    /// Fills <paramref name="notes"/> for the frame: the song's notes (unless <paramref name="songNotes"/> is off, which
    /// the AR overlay asks for in Perform mode) and, when <paramref name="liveTrails"/> is on, the live trails of the keys the player holds.
    /// </summary>
    /// <param name="liveTrails">Whether player press bars are included; key tint is controlled separately.</param>
    internal void BuildNotes(GpuFrameInput input, GpuSceneLayout layout, GpuInstanceList<GpuNoteInstance> notes, bool songNotes = true, bool onStage = true, float minAspect = 0, bool liveTrails = true)
    {
        notes.Clear();
        _noteTrails.Clear();
        _noteLabels.Clear();
        _shimmerBars.Clear();
        _landings.Clear();
        var look = input.Look;
        // Over the camera, the AR layer owns song notes in both flat and projected modes; suppress stage-space notes and trails.
        if (!look.ShowNotes || !onStage) return;
        var noteWidth = layout.Lane * look.NoteWidth;
        var hitY = layout.HitY;
        const float landingWindow = .55f;
        if (songNotes && input.Playing && input.Notes.Count > 0)
        {
            var speed = look.SongFallSpeed;
            var latestStart = input.Position + hitY / speed + 1;
            try
            {
                var list = input.Notes;
                // AR-S4: minimum note length. The next note of the same pitch is found once per frame, so a note can be cut short by it.
                var nextSame = minAspect > 0 ? NextOfSamePitch(list) : null;
                for (var i = NoteTimeline.FirstIndexAtOrAfter(list, input.Position - 1 - input.MaxNoteDuration); i < list.Count; i++)
                {
                    var note = list[i];
                    if (note.Start > latestStart) break;
                    if (!OnKeyboard(note.Pitch) || note.End < input.Position - 1) continue;
                    var height = (float)Math.Clamp(note.Duration * speed - look.NoteGap, look.NoteMinLength, hitY * .9);
                    var played = note.Played; var missed = note.Missed;
                    var color = played ? new Vector3(82 / 255f, 237 / 255f, 208 / 255f) : missed ? new Vector3(1f, 83 / 255f, 113 / 255f) : look.NoteColor(note.Pitch, note.Track);
                    if (look.VelocityColor && !played && !missed) color = Vector3.Lerp(color, VelocityTint(note.Velocity / 127f), look.VelocityColorAmount);
                    var sounding = note.Start <= input.Position && note.End > input.Position;
                    var opacity = played ? .42f : 1f;
                    float top, bottom, direction;
                    if (look.Rising)
                    {
                        top = (float)(hitY + (note.Start - input.Position) * speed);
                        if (top > hitY || top + height < 0) continue;
                        bottom = Math.Min(top + height, hitY + 2); direction = -1;
                    }
                    else
                    {
                        bottom = (float)(hitY - (note.Start - input.Position) * speed);
                        top = bottom - height;
                        if (top > hitY || bottom < 0) continue;
                        bottom = Math.Min(bottom, hitY + 2); direction = 1;
                    }
                    if (nextSame is not null)
                    {
                        var j = nextSame[i];
                        // the next note's head: where it sits on the same side as this note's head
                        var limit = j < 0 ? double.NaN : look.Rising
                            ? hitY + (list[j].Start - input.Position) * speed
                            : hitY - (list[j].Start - input.Position) * speed;
                        var extended = ArNoteMinimum.Extend(top, bottom, noteWidth, minAspect, !look.Rising, limit);
                        top = (float)extended.Top; bottom = (float)extended.Bottom;
                    }
                    if (bottom - top < 1) continue;
                    AddNoteWithFx(notes, look, layout.X(note.Pitch) - noteWidth / 2, top, noteWidth, bottom - top, color, opacity, sounding, note.Pitch, i, direction);
                    // landing glow: a gathering light where the note is about to strike (GPU-exclusive)
                    var until = (float)(note.Start - input.Position);
                    if (look.NoteLandingGlow && !look.Chroma && !played && !missed && until > 0 && until <= landingWindow)
                        _landings.Add(new Landing(layout.X(note.Pitch), until / landingWindow, color));
                }
            }
            catch (ArgumentOutOfRangeException) { }
        }
        if (liveTrails) foreach (var trail in _trails)
        {
            var x = layout.X(trail.Pitch) - noteWidth / 2;
            var color = look.NoteColor(trail.Pitch, 0);
            if (look.VelocityColor) color = Vector3.Lerp(color, VelocityTint(trail.Strength), look.VelocityColorAmount);
            var sounding = trail.KeyDown && trail.Hit;
            if (look.Rising)
            {
                var headY = hitY - trail.Age * look.LiveFallSpeed;
                var tailY = Math.Min(hitY, hitY - (trail.Age - trail.Held) * look.LiveFallSpeed);
                if (tailY - headY < 1) continue;
                var opacity = Math.Clamp(1 - (hitY - tailY) / Math.Max(1, hitY + 18), .08f, 1) * look.NoteTint;
                AddNoteWithFx(notes, look, x, headY, noteWidth, tailY - headY, color, opacity, sounding, trail.Pitch, trail.Pitch, -1);
            }
            else
            {
                var y = 28 + trail.Age * look.LiveFallSpeed;
                var tailY = Math.Max(0, y - 28 - trail.Held * look.LiveFallSpeed);
                var bottom = Math.Min(hitY + 2, y);
                if (bottom - tailY < 1) continue;
                var opacity = Math.Clamp(1 - tailY / Math.Max(1, hitY + 18), .08f, 1) * look.NoteTint;
                AddNoteWithFx(notes, look, x, tailY, noteWidth, bottom - tailY, color, opacity, sounding, trail.Pitch, trail.Pitch, 1);
            }
        }
    }

    /// <summary>
    /// One note with its hold-phase (vibration, breathing, hold bar) and falling-phase (ghost copies, trail)
    /// effects, in the order the software stage applies them.
    /// </summary>
    private void AddNoteWithFx(GpuInstanceList<GpuNoteInstance> notes, GpuLook look, float x, float y, float w, float h, Vector3 color, float opacity, bool sounding, int pitch, int seed, float direction)
    {
        var rising = direction < 0;
        if (sounding && look.HoldVibration) x += MathF.Sin((float)_time * 40 + pitch * 2.2f) * look.HoldVibrationAmount * 4;
        if (look.FallingGhost && look.FallingGhostAmount * opacity > .01f && !sounding)
        {
            // faint echo copies leading the note along its travel direction
            var amount = look.FallingGhostAmount * opacity;
            var count = amount > .66f ? 3 : amount > .33f ? 2 : 1;
            var step = 10 + h * .12f;
            for (var i = 1; i <= count; i++)
                AddNote(notes, look, x, rising ? y - step * i : y + step * i, w, h, color, amount * .3f / i, false, pitch, seed, direction, 0, 1);
        }
        var breath = sounding && look.HoldBreath ? .72f + .28f * MathF.Sin((float)_time * (1 + look.HoldBreathRate * 5)) : 1f;
        var holdBar = sounding && look.HoldBar ? look.HoldBarIntensity * opacity : 0;
        var shown = AddNote(notes, look, x, y, w, h, color, opacity, sounding, pitch, seed, direction, holdBar, breath);
        if (look.ShowNoteLabels && w >= 11 && h >= 15) _noteLabels.Add(new NoteTrail(x, y, w, h, shown, opacity, pitch, rising));
        if (look.NoteShimmer && look.NoteShimmerAmount > 0) _shimmerBars.Add(new NoteTrail(x, y, w, h, shown, opacity, pitch, rising));
        if (look.FallingTrail != "None" && look.FallingTrailIntensity > 0)
            _noteTrails.Add(new NoteTrail(x, y, w, h, shown, opacity, pitch, rising));
    }

    /// <summary>Adds one note capsule and returns the sRGB colour it was drawn in (after cycling and rainbow shifts).</summary>
    private Vector3 AddNote(GpuInstanceList<GpuNoteInstance> notes, GpuLook look, float x, float y, float w, float h, Vector3 color, float opacity, bool sounding, int pitch, int seed, float direction, float holdBar, float brightness)
    {
        if (sounding && look.HoldColorCycle) color = Vector3.Lerp(color, ColorFromHue(Hue188(pitch) + _time * look.HoldColorCycleSpeed * 3), .8f);
        if (look.FallingPulse && !sounding) opacity *= .62f + .38f * MathF.Sin((float)_time * (1.5f + look.FallingPulseRate * 9) + pitch * .7f);
        if (look.RainbowTrail) color = ColorFromHue(_time * look.RainbowHueSpeed + pitch * 9);
        var linear = ToLinear(color) * brightness;
        notes.Add(new GpuNoteInstance
        {
            Rect = new Vector4(x, y, w, h),
            Color = new Vector4(linear, opacity),
            Misc = new Vector4(sounding ? 1 : 0, (seed % 97) / 97f, direction, holdBar)
        });
        return color;
    }

    /// <summary>The 88 keys (plus the felt strip) as 3D boxes; a full-screen camera always suppresses these uncalibrated stage bodies.</summary>
    internal void BuildKeys(GpuLook look, GpuSceneLayout layout, GpuInstanceList<GpuKeyInstance> keys, bool cameraOverlayActive, out float frontHeight, out float blackLength, out float blackHeight, out float whiteWidth, out float blackWidth)
    {
        keys.Clear();
        whiteWidth = layout.Width / layout.Keys.WhiteCount;
        blackWidth = whiteWidth * .52f;
        frontHeight = layout.KeyboardHeight * (.05f + look.ShaderCameraTilt * .1f);
        var topLength = layout.KeyboardHeight - frontHeight;
        blackLength = layout.KeyboardHeight * (.61f + look.KeyOverhang * .18f);
        blackHeight = frontHeight * .8f + 2;
        if (!look.ShowKeys || cameraOverlayActive) return;
        var whiteIndex = 0;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (PianoStage.IsBlackKey(pitch)) continue;
            var x0 = whiteIndex * whiteWidth + .5f; var x1 = (whiteIndex + 1) * whiteWidth - .5f;
            whiteIndex++;
            var left = PianoStage.IsBlackKey(pitch - 1) && pitch - 1 >= FirstPitch ? layout.X(pitch - 1) + blackWidth / 2 - x0 : 0;
            var right = PianoStage.IsBlackKey(pitch + 1) && pitch + 1 < FirstPitch + KeyCount ? x1 - (layout.X(pitch + 1) - blackWidth / 2) : 0;
            keys.Add(Key(look, pitch, x0, x1, topLength, frontHeight, 0, 0, 0, left, right));
        }
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!PianoStage.IsBlackKey(pitch)) continue;
            var x = layout.X(pitch);
            keys.Add(Key(look, pitch, x - blackWidth / 2, x + blackWidth / 2, blackLength - blackHeight, blackHeight, 1, frontHeight, blackHeight, 0, 0));
        }
        if (look.ShowKeyFelt)
        {
            var felt = ToLinear(look.KeyFeltColor);
            keys.Add(new GpuKeyInstance
            {
                Box = new Vector4(0, layout.Width, Math.Max(3, layout.KeyboardHeight * .035f), 1.5f),
                Emit = new Vector4(felt, 0), Misc = new Vector4(2, 0, 0, 0), Base = new Vector4(frontHeight + blackHeight + .5f, 0, blackHeight, 0)
            });
        }
    }

    private GpuKeyInstance Key(GpuLook look, int pitch, float x0, float x1, float length, float height, float kind, float baseHeight, float depthOffset, float left, float right)
    {
        var color = ToLinear(_keyColor[pitch]) * 1.25f;
        return new GpuKeyInstance
        {
            Box = new Vector4(x0, x1, length, height),
            Emit = new Vector4(color, _glow[pitch]),
            Misc = new Vector4(kind, look.AnimateKeys ? _press[pitch] : 0, Math.Max(0, left), Math.Max(0, right)),
            Base = new Vector4(baseHeight, _spill[pitch], depthOffset, 0)
        };
    }

    /// <summary>Particles, rings, flashes, beams, held-key glows and the hit line.</summary>
    internal void BuildSprites(GpuLook look, GpuSceneLayout layout, GpuInstanceList<GpuSpriteInstance> sprites, bool cameraOverlayActive = false)
    {
        sprites.Clear();
        var scaleX = layout.Width / _simWidth;
        var hitY = layout.HitY;
        var glowGain = .9f + look.ParticleGlow * 1.6f;

        // Suppress stage-space draw when any camera is the background, even before calibration marks OverCamera.
        // These sprites are not calibrated to the physical piano; pitch-tagged state remains available to GpuArLayer.
        if (look.OverCamera || cameraOverlayActive) return;

        if (look.ShowLightBeams && look.BeamIntensity > 0)
        {
            for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
            {
                if (_glow[pitch] <= .01f) continue;
                var c = ToLinear(_keyColor[pitch]) * look.BeamIntensity * .9f * _glow[pitch];
                sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(layout.X(pitch), hitY, layout.Lane * 1.1f, 5), Color = new Vector4(c, 1), Dir = new Vector4(0, -1, hitY * .95f, 0) });
            }
        }
        foreach (var ring in _rings) AddRingSprites(sprites, look, ring, scaleX);
        for (var i = 0; i < _count; i++)
        {
            ref var p = ref _particles[i];
            var u = p.Age / p.Life;
            var fade = 1 - u;
            var x = p.X * scaleX;
            var c = ToLinear(p.Color);
            switch (p.Shape)
            {
                case Shape.Streak:
                {
                    var speed = MathF.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                    var dir = speed > 1e-3f ? new Vector2(p.Vx / speed, p.Vy / speed) : new Vector2(0, -1);
                    sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(x, p.Y, p.Size * 1.4f, 1), Color = new Vector4(c * glowGain * p.Glow * fade * 1.3f, 1), Dir = new Vector4(dir, Math.Clamp(speed * .018f, .5f, 7), u) });
                    break;
                }
                case Shape.Droplet:
                {
                    // a bead of liquid keeping its colour, stretched by its fall, with a specular dot
                    var alpha = MathF.Pow(fade, .8f) * look.ParticleGlow;
                    var size = p.Size * (.7f + fade * .5f);
                    var stretch = 1 + Math.Clamp(Math.Abs(p.Vy) / 700, 0, 1.2f);
                    Glow(sprites, x, p.Y, size * 2.4f, size * 2.4f * stretch, c, alpha * .5f);
                    Ellipse(sprites, x, p.Y, size, size * stretch, 0, c * 1.2f, alpha, .15f);
                    Ellipse(sprites, x - size * .3f, p.Y - size * .35f * stretch, size * .32f, size * .32f, 0, Vector3.One * 1.5f, alpha, .2f);
                    break;
                }
                case Shape.Shard:
                {
                    // a tumbling glass shard: a thin bright wedge with a white glint along it
                    var alpha = fade * look.ParticleGlow;
                    var angle = p.Phase + p.Age * 7.3f;
                    var len = p.Size * 2.4f;
                    Ellipse(sprites, x, p.Y, len * .5f, p.Size * .4f, angle, c * 1.4f, alpha, .1f);
                    var dx = MathF.Cos(angle) * len * .5f; var dy = MathF.Sin(angle) * len * .5f;
                    Line(sprites, x - dx, p.Y - dy, x + dx, p.Y + dy, .5f, Vector3.One, alpha * .9f, 1);
                    break;
                }
                case Shape.Confetti:
                    sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(x, p.Y, p.Size * 1.6f, 3), Color = new Vector4(c * 1.1f, Math.Min(1, fade * 1.5f)), Dir = new Vector4(MathF.Cos(p.Age * 7 + p.Phase), MathF.Sin(p.Age * 7 + p.Phase), 0, u) });
                    break;
                case Shape.Flame:
                {
                    // white core -> yellow -> orange -> deep red, swelling then shrinking
                    var t = u;
                    var fire = t < .25f ? Vector3.Lerp(new Vector3(1f, .95f, .8f), new Vector3(1f, .78f, .3f), t / .25f)
                        : t < .6f ? Vector3.Lerp(new Vector3(1f, .78f, .3f), new Vector3(1f, .38f, .08f), (t - .25f) / .35f)
                        : Vector3.Lerp(new Vector3(1f, .38f, .08f), new Vector3(.45f, .03f, .01f), (t - .6f) / .4f);
                    var tint = look.FlameNoteColor ? Vector3.Lerp(fire, p.Color, .6f) : fire;
                    var size = p.Size * (.6f + MathF.Sin(MathF.Min(1, u * 1.4f) * MathF.PI) * .7f);
                    sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(x, p.Y, size, 0), Color = new Vector4(ToLinear(tint) * 1.3f * p.Glow * fade, 1), Dir = new Vector4(0, -1, 0, u) });
                    break;
                }
                default:
                {
                    var px = x;
                    var size = p.Size * 2.4f;
                    var gain = glowGain;
                    if (p.Wisp)
                    {
                        var amp = (5 + look.WispTurbulence * 26) * (.45f + look.WispWidth) * MathF.Min(1, p.Age * 1.6f);
                        px += MathF.Sin(p.Age * (3 + look.WispTurbulence * 5) + p.Phase) * amp;
                        gain = .6f + p.Glow * 1.6f;
                        fade = MathF.Min(1, u * 6) * (1 - u);
                    }
                    if (p.Shape == Shape.Dust) { size *= 1.8f; gain *= .35f; }
                    sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(px, p.Y, size, 0), Color = new Vector4(c * gain * p.Glow * fade, 1), Dir = new Vector4(0, -1, 0, u) });
                    break;
                }
            }
        }
        foreach (var flash in _flashes)
        {
            var u = flash.Age / flash.Life;
            if (flash.Style != 0) { AddFlashSprites(sprites, look, flash, scaleX); continue; }
            var c = ToLinear(flash.Color) * flash.Intensity * (1 - u) * (1 - u);
            sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(flash.X * scaleX, flash.Y, flash.Size * (.8f + u * .6f), 4), Color = new Vector4(c, 1), Dir = new Vector4(1, 0, 0, u) });
        }
        if (look.HoldElectricArc && look.HoldArcIntensity > .01f) AddElectricArcs(sprites, look, layout);
        foreach (var landing in _landings) AddLandingGlow(sprites, look, landing, hitY);
        if (look.ShowHalo || look.ShowImpactFlash)
        {
            // steady white-hot glow where a held note meets its key
            for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
            {
                if (_glow[pitch] <= .02f) continue;
                var flicker = .85f + .15f * MathF.Sin((float)_time * 23 + pitch * 1.7f);
                var c = ToLinear(Vector3.Lerp(_keyColor[pitch], Vector3.One, .3f)) * 1.7f * _glow[pitch] * flicker * (.4f + look.HaloIntensity * .8f) * GlowBoost;
                sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(layout.X(pitch), hitY - 1, layout.Lane * (1.2f + .5f * _glow[pitch]), 4), Color = new Vector4(c, 1), Dir = new Vector4(1, 0, 0, 0) });
            }
        }
        if (look.ShowHalo)
        {
            var c = ToLinear(look.HaloColor) * (.5f + look.HaloIntensity * .9f) * (1 + _activity * .6f) * GlowBoost;
            sprites.Add(new GpuSpriteInstance { PosSize = new Vector4(layout.Width / 2, hitY, layout.Width / 2, 6), Color = new Vector4(c, 1), Dir = new Vector4(1, 0, 3.2f, 0) });
            if (look.HaloPulse && look.HaloPulseIntensity > 0) AddHaloPulses(sprites, look, layout);
        }
        if (look.ShowKeys && look.KeyLabels > 0) AddKeyLabels(sprites, look, layout);
        foreach (var label in _noteLabels) AddNoteLabel(sprites, look, label);
        foreach (var bar in _shimmerBars) AddNoteShimmer(sprites, look, bar);
    }

    /// <summary>
    /// The GPU stage's shimmer: a bright, narrow sheen that sweeps back and forth along every travelling
    /// bar (Style → GLOW &amp; EDGES → Note shimmer), tinted by the bar's own colour so a rainbow or
    /// colour-cycling look shimmers in its current hue. Two bands at different speeds give the surface
    /// depth; the additive pass over the note turns them into moving gloss.
    /// </summary>
    private void AddNoteShimmer(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, NoteTrail n)
    {
        var strength = look.NoteShimmerAmount * Math.Clamp(n.Opacity, 0, 1);
        if (strength <= .01f || n.W < 5 || n.H < 6) return;
        var e = (float)_time;
        var center = n.Y + n.H * .5f;
        var tint = Vector3.Lerp(ToLinear(n.Color), Vector3.One * 1.3f, .72f);
        for (var k = 0; k < 2; k++)
        {
            var phase = SeededRandom(n.Pitch * 17 + k * 3 + 1) * MathF.Tau;
            var sweep = .5f + .5f * MathF.Sin(e * (.5f + k * .27f) + phase);
            var x = n.X + n.W * (.16f + .68f * sweep);
            // the sheen brightens towards the middle of its sweep and fades at the bar's edges
            var alpha = strength * (.1f + .2f * MathF.Pow(MathF.Sin(sweep * MathF.PI), 2)) * (1 - k * .45f);
            Glow(sprites, x, center, MathF.Max(1.1f, n.W * .13f), MathF.Min(n.H * .72f, 26 + n.H * .18f), tint, alpha);
        }
    }

    /// <summary>
    /// Halo motion accents (Style → HIT LINE): compact, style-specific sprite geometry complements the
    /// note-aware HLSL filament. Pedal glow, audio reactivity and Tempo sync modulate its brightness.
    /// </summary>
    /// <param name="sprites">The additive instance list.</param>
    /// <param name="look">The current look.</param>
    /// <param name="layout">Scene layout of this output.</param>
    private void AddHaloPulses(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, GpuSceneLayout layout)
    {
        var gain = look.HaloPulseIntensity * GlowBoost;
        if (look.TempoSync) gain *= 1 + _beatPulse * look.TempoSyncAmount * .8f;
        gain = Math.Clamp(gain, 0, 2);
        var width = layout.Width;
        var y = layout.HitY;
        var color = ToLinear(look.HaloColor) * 2.2f;
        var time = (float)_time * look.HaloPulseSpeed;
        switch (look.HaloPulseStyle)
        {
            case "Sweep":
            {
                var f = Frac(time * .19f);
                var x = f * width;
                var tail = Math.Max(0, x - width * .24f);
                Line(sprites, tail, y, x, y, 2.2f, color, .42f * gain, 1.2f);
                Glow(sprites, x, y, width * .045f, 8, color, .56f * gain);
                Glow(sprites, x, y, width * .015f, 3.5f, Vector3.One * 2.3f, .75f * gain);
                break;
            }
            case "Twin Comets":
                for (var i = 0; i < 2; i++)
                {
                    var f = i == 0 ? Frac(time * .145f) : Frac(.5f - time * .145f);
                    var x = f * width;
                    var tailX = i == 0 ? Math.Max(0, x - width * .14f) : Math.Min(width, x + width * .14f);
                    var cometColor = i == 0 ? color : ToLinear(new Vector3(.25f, .62f, 1f)) * 2.1f;
                    Line(sprites, tailX, y, x, y, 1.8f, cometColor, .48f * gain, 1.1f);
                    Glow(sprites, x, y, width * .026f, 6, cometColor, .66f * gain);
                    Glow(sprites, x, y, width * .009f, 3, Vector3.One * 2.2f, .8f * gain);
                }
                break;
            case "Spectrum":
                for (var i = 0; i < 12; i++)
                {
                    var left = width * i / 12f;
                    var right = width * (i + 1) / 12f;
                    var segmentColor = ToLinear(ColorFromHue(time * 36 + i * 30)) * 1.9f;
                    var pulse = .62f + .38f * MathF.Sin(time * 1.4f - i * .48f);
                    Line(sprites, left, y, right, y, 1.5f, segmentColor, .55f * gain * pulse, 1.1f);
                    Glow(sprites, (left + right) * .5f, y, width / 18f, 4, segmentColor, .16f * gain * pulse);
                }
                break;
            case "Electric Arc":
            {
                const int segments = 28;
                var px = 0f;
                var py = y + MathF.Sin(time * 8) * 1.5f;
                for (var i = 1; i <= segments; i++)
                {
                    var f = i / (float)segments;
                    var nx = f * width;
                    var ny = y + MathF.Sin(f * 51 + time * 5.2f) * 3.2f + MathF.Sin(f * 117 - time * 7.1f) * 1.4f;
                    var arcColor = i % 4 == 0 ? Vector3.One * 1.8f : ToLinear(new Vector3(.42f, .78f, 1f)) * 1.8f;
                    Line(sprites, px, py, nx, ny, 1.05f, arcColor, .7f * gain, 1.25f);
                    if (i % 4 == 0) Glow(sprites, nx, ny, 4, 4, arcColor, .22f * gain);
                    px = nx; py = ny;
                }
                break;
            }
            case "Ripple":
                for (var i = 0; i < 3; i++)
                {
                    var phase = Frac(time * .16f + i / 3f);
                    var rx = width * (.035f + phase * .23f);
                    var alpha = MathF.Sin(phase * MathF.PI) * .76f * gain;
                    RingShape(sprites, width * .5f, y, rx, Math.Max(2, rx * .11f), 2.2f, color, alpha);
                }
                break;
            default: // Pulse: a slow, even breath across the entire key bed
            {
                var breath = .72f + .28f * MathF.Sin(time * 1.55f) + _activity * .12f;
                Line(sprites, 0, y, width, y, 1.5f, color, .22f * gain * breath, 1.05f);
                Glow(sprites, width * .5f, y, width * .5f, 7, color, .18f * gain * breath);
                for (var i = 0; i < 3; i++)
                {
                    var f = Frac(time * .1f + i / 3f);
                    var x = f * width;
                    var envelope = MathF.Sin(f * MathF.PI);
                    Glow(sprites, x, y, width * .035f, 7, color, .34f * envelope * gain);
                    Glow(sprites, x, y, width * .012f, 3.5f, Vector3.One * 2.4f, .48f * envelope * gain);
                }
                break;
            }
        }
    }

    /// <summary>
    /// The landing glow (Style → IMPACT · WAVE &amp; FLASH): a soft light gathers on the key a note is
    /// about to strike, then hands over to the impact burst. Doubles as a practice aid — the lane that
    /// is about to sound is lit before the sound arrives.
    /// </summary>
    private void AddLandingGlow(GpuInstanceList<GpuSpriteInstance> sprites, GpuLook look, Landing landing, float hitY)
    {
        var t = Math.Clamp(landing.T, 0, 1);              // 1 = far away, 0 = striking now
        var rise = 1 - t;                                 // grows as the note approaches
        var strength = look.NoteLandingGlowAmount * rise * rise * (.7f + .3f * _activity);
        if (strength <= .01f) return;
        var c = ToLinear(landing.Color);
        var radius = 5 + 15 * rise;
        Glow(sprites, landing.X, hitY - 2, radius, radius * .34f, c * 1.7f, .5f * strength);
        Glow(sprites, landing.X, hitY - 2, radius * .45f, radius * .18f, Vector3.One * 1.9f, .8f * strength);
    }
}
