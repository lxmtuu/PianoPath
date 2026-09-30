using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Immersive piano-roll renderer: live key trails, optional MIDI playback notes, sparks, wisps, flames and a lit keyboard.</summary>
internal sealed class PianoStage : FrameworkElement
{
    private const int FirstPitch = 21, KeyCount = 88, MaxParticles = 2600;
    private const double FallSpeed = 258;
    private const int FireMaskWidth = 96, FireMaskHeight = 384;
    /// <summary>MIDI pitches of the 52 white keys, in keyboard order.</summary>
    private static readonly int[] WhitePitches = Enumerable.Range(FirstPitch, KeyCount).Where(p => !IsBlack(p)).ToArray();
    /// <summary>For every MIDI pitch, how many white keys lie below it; gives each black key its x position without per-frame counting.</summary>
    private static readonly int[] WhitesBelow = BuildWhitesBelow();
    /// <summary>Horizontal centre of every key as a fraction of the stage width, matching the drawn keyboard geometry
    /// so falling notes, sparks, flames and beams land exactly on their keys.</summary>
    private static readonly double[] KeyCenters = BuildKeyCenters();
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush ChromaGreen = Freeze(new SolidColorBrush(Color.FromRgb(0, 255, 0)));
    private static readonly Brush KeyBlack = Freeze(new LinearGradientBrush(Color.FromRgb(14, 15, 24), Color.FromRgb(3, 4, 9), 90));
    private static readonly Brush KeyBlackStudio = Freeze(new LinearGradientBrush(Color.FromRgb(46, 49, 62), Color.FromRgb(6, 7, 12), 90));
    private static readonly Brush KeyWhite = Freeze(new LinearGradientBrush(Color.FromRgb(253, 250, 255), Color.FromRgb(157, 166, 188), 90));
    private static readonly Brush KeyWhiteStudio = Freeze(new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(196, 201, 216), 90));
    private static readonly Brush KeyWhiteGlass = Freeze(new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(70, 200, 210, 235), 90));
    private static readonly Brush KeyBlackGlass = Freeze(new LinearGradientBrush(Color.FromArgb(220, 30, 32, 44), Color.FromArgb(200, 4, 5, 10), 90));
    private static readonly BitmapSource FireMask = BuildFireMask();
    private readonly Dictionary<uint, SolidColorBrush> _brushCache = [];
    private readonly Dictionary<ulong, Brush> _gradientCache = [];
    private readonly List<Star> _stars = [];
    private readonly List<Petal> _petals = [];
    /// <summary>Own random stream so the blossom field stays identical when particle effects consume their own numbers.</summary>
    private readonly Random _petalRandom = new(20240616);
    private int _petalCount = -1;
    private double _petalWidth = -1, _petalHeight = -1;
    private readonly List<Spark> _sparks = [];
    private readonly List<Ring> _rings = [];
    private readonly List<Flash> _flashes = [];
    private readonly List<LiveTrail> _liveTrails = [];
    private readonly Random _random = new(7331);
    private readonly bool[] _activeKey = new bool[128];
    private readonly Color[] _activeKeyColor = new Color[128];
    private readonly double[] _keyHeat = new double[128];
    private readonly double[] _wispBudget = new double[128];
    // ---- Ray-traced keyboard cache ----------------------------------------------------------------
    /// <summary>Empty light state used for the cached "all keys up" bake.</summary>
    private static readonly KeyLightState NoLights = new();
    /// <summary>Keys sounding right now; each one becomes an emissive surface plus a colored area light.</summary>
    private readonly KeyLightState _keyLights = new();
    /// <summary>Scratch light state reused for each overlay tile bake.</summary>
    private readonly KeyLightState _tileLights = new();
    private readonly Dictionary<long, ShadedKeyTile> _shadedTiles = [];
    /// <summary>Most recent tile per pitch, reused when a frame runs out of its shading budget.</summary>
    private readonly Dictionary<int, ShadedKeyTile> _lastTile = [];
    private int _tileBudget;
    private BitmapSource? _shadedBase;
    /// <summary>Fingerprint of the scene the current <see cref="_shadedBase"/> was baked from; equality avoids both a rebake and the per-frame signature string.</summary>
    private PianoShaderScene.SceneKey _shadedSceneKey;
    private bool _hasShadedSceneKey;
    private double _shadedMilliseconds;
    private int _shadedBakes;
    private PianoVisualSettings _visual = new();
    private BitmapSource? _backgroundImage;
    private Brush? _vignetteBrush;
    private double _vignetteValue = -1;
    private string? _loadedBackgroundPath = "\0";
    private double _pointerX = .5, _pointerY = .5;
    private IReadOnlyList<NoteEvent> _notes = [];
    private IReadOnlyList<double> _beats = [];
    private int _beatsPerBar = 4;
    private IReadOnlySet<int> _pressed = new HashSet<int>();
    private double _position, _elapsed, _maxNoteDuration, _pixelsPerDip, _fps;
    private bool _transparentBackdrop;
    private int _releaseScanIndex;
    private double _releaseScanPos;
    private double _beatPulse, _energyLevel;
    private bool _sustainPedal;
    private bool _playing, _anyHeat;
    private bool _mouseDown;
    private int _mousePitch = -1;
    public event Action<int, bool>? PianoKeyChanged;
    public double KeyboardHeight => Math.Max(80, Math.Min(300, Math.Max(110, Math.Min(228, ActualHeight * .205)) * _visual.KeyboardScale / 100));
    /// <summary>
    /// Renders the stage without its opaque background so an export can keep the alpha channel (the PNG
    /// sequence; see <see cref="PngSequenceRecorder"/>). Every layer the look enables is still drawn — the
    /// stars, the note roll, the keys and the effects — while the fills a viewer would otherwise see
    /// through are skipped: the ink, the background colour, the image, the gradient and the vignette.
    /// </summary>
    internal bool TransparentBackdrop
    {
        get => _transparentBackdrop;
        set { if (_transparentBackdrop == value) return; _transparentBackdrop = value; InvalidateVisual(); }
    }

    public int SparkCount => _sparks.Count;
    public int RingCount => _rings.Count;
    public int FlashCount => _flashes.Count;
    /// <summary>Number of blossom petals currently in the air; surfaced by the verification suite.</summary>
    public int PetalCount => _petals.Count;
    public int LiveTrailCount => _liveTrails.Count;
    /// <summary>True while anything on the stage still animates on its own (particles, rings, cooling flames or held keys).</summary>
    public bool HasActiveEffects => _sparks.Count > 0 || _rings.Count > 0 || _flashes.Count > 0 || _liveTrails.Count > 0 || _anyHeat || _pressed.Count > 0
        || (_visual.ShowPetals && _visual.PetalAmount > 0)
        || _visual.AmbientEnergy != "None" || _visual.AmbientNature != "None" || _visual.AmbientLight != "None" || _visual.AmbientCosmic != "None";
    public bool HasBackgroundImage => _backgroundImage is not null;
    public string? BackgroundLoadError { get; private set; }
    /// <summary>True while the ray-traced keyboard bake is driving the stage instead of the flat vector keys.</summary>
    public bool IsShadedKeyboardActive { get; private set; }
    /// <summary>Milliseconds the most recent keyboard bake took; surfaced by the verification suite.</summary>
    public double ShadedBakeMilliseconds => _shadedMilliseconds;
    /// <summary>How many times the keyboard had to be re-shaded; a slider that does not affect the shader must not raise it.</summary>
    public int ShadedBakeCount => _shadedBakes;
    public double FirstLiveTrailY => _liveTrails.Count == 0 ? -1 : _liveTrails[0].Age * _visual.NoteFallSpeed;
    /// <summary>Length of the visual bar for a pitch: falling notes keep the 28 px spawn offset at the top, rising notes are born at the key line.</summary>
    public double LiveTrailHeightFor(int pitch)
    {
        var trail = _liveTrails.LastOrDefault(note => note.Pitch == pitch);
        return trail is null ? -1 : (_visual.NoteDirection == "Up" ? trail.HeldSeconds * _visual.NoteFallSpeed : 28 + trail.HeldSeconds * _visual.NoteFallSpeed);
    }

    public void SetVisualSettings(PianoVisualSettings settings, bool reloadBackground = false)
    {
        var requestedPath = settings.BackgroundImagePath?.Trim() ?? "";
        var backgroundChanged = !string.Equals(_loadedBackgroundPath, requestedPath, StringComparison.OrdinalIgnoreCase);
        _visual = settings;
        if (backgroundChanged || reloadBackground)
        {
            _loadedBackgroundPath = requestedPath;
            _backgroundImage = null;
            BackgroundLoadError = null;
            if (!string.IsNullOrWhiteSpace(requestedPath))
            {
                try
                {
                    if (!File.Exists(requestedPath)) throw new FileNotFoundException(Loc.T("The selected background image could not be found."), requestedPath);
                    using var stream = File.OpenRead(requestedPath);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                    var sourceWidth = decoder.Frames[0].PixelWidth;
                    stream.Position = 0;
                    var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    if (sourceWidth > 1920) image.DecodePixelWidth = 1920;
                    image.EndInit(); image.Freeze(); _backgroundImage = image;
                }
                catch (Exception ex) { _backgroundImage = null; BackgroundLoadError = ex.Message; }
            }
        }
        InvalidateVisual();
    }

    public void SetPointerPosition(Point point)
    {
        if (ActualWidth < 1 || ActualHeight < 1) return;
        _pointerX = Math.Clamp(point.X / ActualWidth, 0, 1); _pointerY = Math.Clamp(point.Y / ActualHeight, 0, 1);
        if (_visual.CameraParallax > 0) InvalidateVisual();
    }

    public PianoStage()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        MouseDown += Stage_MouseDown;
        MouseMove += Stage_MouseMove;
        MouseUp += Stage_MouseUp;
        LostMouseCapture += (_, _) => ReleaseMouseKey();
    }

    /// <summary>
    /// The song's metronome grid, which the sheet layer draws its bar lines on. It is kept apart from
    /// <see cref="SetState"/> because the grid changes when a song is loaded, not on every frame.
    /// </summary>
    public void SetSheet(IReadOnlyList<double> beats, int beatsPerBar)
    {
        _beats = beats; _beatsPerBar = Math.Max(1, beatsPerBar);
        InvalidateVisual();
    }

    public void SetState(IReadOnlyList<NoteEvent> notes, double position, bool playing, IReadOnlySet<int> pressed)
    {
        if (!ReferenceEquals(_notes, notes))
        {
            _notes = notes;
            _maxNoteDuration = 0;
            foreach (var note in notes) if (note.Duration > _maxNoteDuration) _maxNoteDuration = note.Duration;
        }
        _position = position; _playing = playing; _pressed = pressed;
        RefreshActiveKeys();
        InvalidateVisual();
    }

    /// <summary>Which keys are currently sounding (song notes under the playhead plus live presses) and which color each one carries.</summary>
    private void RefreshActiveKeys()
    {
        Array.Clear(_activeKey);
        if (_playing && _notes.Count > 0)
        {
            for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - _maxNoteDuration); i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Start > _position) break;
                if (note.End <= _position || note.Pitch < 0 || note.Pitch > 127) continue;
                _activeKey[note.Pitch] = true; _activeKeyColor[note.Pitch] = NoteColor(note.Pitch, note.Track);
            }
        }
        foreach (var pitch in _pressed)
        {
            if (pitch < 0 || pitch > 127) continue;
            _activeKey[pitch] = true; _activeKeyColor[pitch] = NoteColor(pitch, 0);
        }
    }

    public void AddLiveNote(int pitch, double strength = 1)
    {
        _liveTrails.Add(new LiveTrail { Pitch = pitch, Age = 0, HeldSeconds = 0, KeyDown = true, Strength = strength });
        InvalidateVisual();
    }

    /// <summary>Sustain pedal state from the host; drives Pedal Glow.</summary>
    public void SetSustainPedal(bool down)
    {
        if (_sustainPedal == down) return;
        _sustainPedal = down;
        InvalidateVisual();
    }

    /// <summary>Beat pulse from the MIDI tempo map; drives Tempo Sync.</summary>
    public void PulseBeat(double strength)
    {
        _beatPulse = Math.Max(_beatPulse, Math.Clamp(strength, 0, 1.2));
        InvalidateVisual();
    }

    public void ReleaseLiveNote(int pitch)
    {
        var trail = _liveTrails.LastOrDefault(note => note.Pitch == pitch && note.KeyDown);
        if (trail is null) return;
        trail.KeyDown = false;
        trail.Released = true;
        SpawnReleaseFx(trail.Pitch);
        InvalidateVisual();
    }

    public void ClearTransient()
    {
        _liveTrails.Clear(); _sparks.Clear(); _rings.Clear(); _flashes.Clear(); Array.Clear(_keyHeat); Array.Clear(_activeKey); _anyHeat = false; InvalidateVisual();
    }

    public void Impact(int pitch, double strength = 1)
    {
        var keyWidth = ActualWidth / KeyCount;
        var clamped = Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1);
        var x = KeyCenters[clamped] * ActualWidth;
        var y = ActualHeight - KeyboardHeight - 1;
        var noteColor = AdjustColor(_activeKey[clamped] ? _activeKeyColor[clamped] : NoteColor(clamped, 0));
        if (_visual.VelocityColor && _visual.VelocityColorAmount > 0) // Velocity → Color on the burst, wave and flash.
            noteColor = Blend(noteColor, VelocityTint(strength), _visual.VelocityColorAmount / 100);
        _energyLevel = Math.Min(1.5, _energyLevel + .22 * strength); // Audio Reactive envelope attack.
        var burstStyle = _visual.ImpactBurst;
        if (_visual.ZoneSplit) // Zone Split: bass hits erupt fire, treble hits splash ice.
            burstStyle = clamped < _visual.ZoneSplitPitch ? "Embers" : "Splash";
        SpawnImpactWave(x, y, noteColor, strength);
        SpawnImpactFlash(x, y, noteColor, strength);
        SpawnImpactMorph(x, y, noteColor, strength);
        SpawnImpactBurst(pitch, x, y, noteColor, strength, keyWidth, burstStyle);
        InvalidateVisual();
    }

    /// <summary>Impact phase, burst channel: the particle explosion, styled per ImpactBurst (embers, splash, fireworks, confetti or dust).</summary>
    private void SpawnImpactBurst(int pitch, double x, double y, Color noteColor, double strength, double keyWidth, string burstStyle)
    {
        if (!_visual.ShowEmbers || _visual.ParticleAmount < 1 || _sparks.Count >= MaxParticles) return;
        var hue = Hue(pitch);
        var style = burstStyle;
        var amount = Math.Clamp((int)(_visual.ParticleAmount * strength * _visual.ParticleResponse / 55.0), 0, 120);
        for (var i = 0; i < amount; i++)
        {
            var kind = 0; var grav = 1.0; var dragK = 1.0;
            var speedScale = 1.0; var lifeScale = 1.0; var sizeScale = 1.0;
            var particleColor = i % 4 == 0 ? Colors.White : Blend(noteColor, ColorFromHue(hue + (_random.NextDouble() - .5) * 28), .2);
            switch (style)
            {
                case "Splash": // Liquid droplets: blue-white, heavy, arcing up then raining down.
                    kind = 1; grav = 1.6;
                    particleColor = Blend(Color.FromRgb(140, 200, 255), Colors.White, _random.NextDouble() * .5);
                    speedScale = .8; lifeScale = 1.1;
                    break;
                case "Fireworks": // Bright saturated shells with long white-hot cores.
                    speedScale = 1.25; lifeScale = 1.5; sizeScale = .9;
                    particleColor = ColorFromHue(_random.NextDouble() * 360);
                    break;
                case "Confetti": // Paper pieces: light, fluttering, keeping their color.
                    kind = 2; grav = .32; dragK = 3.2;
                    particleColor = _random.NextDouble() switch { < .2 => Colors.White, _ => ColorFromHue(_random.NextDouble() * 360) };
                    speedScale = .7; lifeScale = 2.2; sizeScale = 1.3;
                    break;
                case "Dust": // Soft gray puffs drifting up.
                    kind = 3; grav = -.12; dragK = 2.4;
                    particleColor = Blend(Color.FromRgb(150, 140, 130), noteColor, .25);
                    speedScale = .35; lifeScale = 2.4; sizeScale = 2.2;
                    break;
            }
            var isNeedle = i % 3 != 0 && kind != 2 && kind != 3;
            var spread = _visual.ParticleSpread / 100 * Math.PI;
            // The burst fountains upward off the key like the reference captures: centred on straight up
            // (screen -Y) instead of a sideways fan, so sparks rise, hang and rain back down.
            var angle = -Math.PI / 2 + (_random.NextDouble() - .5) * spread;
            angle += Math.Sin(i * .37 + _elapsed * _visual.EvolutionSpeed / 100) * _visual.Spiral / 100 * .3;
            var speedMultiplier = isNeedle
                ? (.65 + _random.NextDouble() * .85 * _visual.ParticleRandomness / 100)
                : (.35 + _random.NextDouble() * .45 * _visual.ParticleRandomness / 100);
            var speed = _visual.ParticleVelocity * speedMultiplier * _visual.ParticleSpeed / 100 * strength * speedScale;
            var emitter = _visual.EmitterSize / 100 * keyWidth * 2;
            var life = (isNeedle
                ? _visual.ParticleLife * (.45 + _random.NextDouble() * .55 * _visual.ParticleLifeRandomness / 100)
                : _visual.ParticleLife * (.85 + _random.NextDouble() * .75 * _visual.ParticleLifeRandomness / 100)) * lifeScale;
            _sparks.Add(new Spark
            {
                X = x + (_random.NextDouble() - .5) * emitter, Y = y,
                Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed - (isNeedle ? 35 : 15),
                Life = life, Age = 0,
                Mass = isNeedle ? 0.6 : 1.5,
                Phase = _random.NextDouble() * Math.PI * 2,
                Size = _visual.ParticleSize * (isNeedle ? (.4 + _random.NextDouble() * .6) : (.7 + _random.NextDouble() * .8)) * sizeScale,
                Color = particleColor, Kind = kind, Grav = grav, DragK = dragK
            });
        }
    }

    /// <summary>Impact phase, morph channel: what the note itself becomes when it lands.</summary>
    private void SpawnImpactMorph(double x, double y, Color color, double strength)
    {
        var morph = _visual.ImpactMorph;
        if (morph == "None" || ActualWidth < 1) return;
        var intensity = _visual.ImpactMorphIntensity / 100 * strength;
        if (intensity <= 0) return;
        switch (morph)
        {
            case "Shatter": // Glass shards flying off the hit point.
            {
                var count = Math.Clamp((int)(10 * intensity) + 4, 0, 24);
                for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
                {
                    var angle = -Math.PI / 2 + (_random.NextDouble() - .5) * 2.4;
                    var speed = (140 + _random.NextDouble() * 260) * (.6 + .4 * strength);
                    _sparks.Add(new Spark
                    {
                        Kind = 4, X = x, Y = y - 2, Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed,
                        Life = .5 + _random.NextDouble() * .4, Age = 0, Mass = 1, Phase = _random.NextDouble() * Math.PI * 2,
                        Size = 2 + _random.NextDouble() * 3.5, Grav = 1.4, Color = Blend(color, Colors.White, .45)
                    });
                }
                break;
            }
            case "Melt": // Wax-like drips oozing down from the key.
            {
                var count = Math.Clamp((int)(8 * intensity) + 3, 0, 20);
                for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
                {
                    _sparks.Add(new Spark
                    {
                        Kind = 1, X = x + (_random.NextDouble() - .5) * 22, Y = y + 2,
                        Vx = (_random.NextDouble() - .5) * 30, Vy = 40 + _random.NextDouble() * 90,
                        Life = .55 + _random.NextDouble() * .45, Age = 0, Mass = 2, Phase = _random.NextDouble() * Math.PI * 2,
                        Size = 1.6 + _random.NextDouble() * 2.2, Grav = 1.1, DragK = 1.6,
                        Color = Blend(color, Color.FromRgb(255, 220, 150), .3)
                    });
                }
                break;
            }
            case "Absorb": // The key sucks the note in: a ring collapsing into the hit point.
                if (_rings.Count <= 64) _rings.Add(new Ring { X = x, Y = y, Color = color, Life = .3, Implode = true, Strength = strength });
                break;
            case "Bounce": // The hit bounces back up: a narrow jet plus a small kick ring.
            {
                var count = Math.Clamp((int)(12 * intensity) + 4, 0, 26);
                for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
                {
                    var speed = 220 + _random.NextDouble() * 320;
                    _sparks.Add(new Spark
                    {
                        X = x + (_random.NextDouble() - .5) * 10, Y = y,
                        Vx = (_random.NextDouble() - .5) * 60, Vy = -speed,
                        Life = .4 + _random.NextDouble() * .3, Age = 0, Mass = .6, Phase = _random.NextDouble() * Math.PI * 2,
                        Size = _visual.ParticleSize * (.5 + _random.NextDouble() * .5),
                        Color = i % 3 == 0 ? Colors.White : color
                    });
                }
                if (_rings.Count <= 64) _rings.Add(new Ring { X = x, Y = y, Color = Colors.White, Life = .3, Strength = .6 * strength });
                break;
            }
            case "Morph": // The note head becomes a star that pops at the hit point.
                if (_flashes.Count <= 32) _flashes.Add(new Flash { X = x, Y = y, Color = color, Life = .3, Strength = strength, Style = 3 });
                break;
        }
        InvalidateVisual();
    }

    /// <summary>Impact phase, wave channel: a hollow acoustic ring or a filled shockwave, scaled by hit strength.</summary>
    private void SpawnImpactWave(double x, double y, Color color, double strength)
    {
        if (!_visual.ShowImpactRings || _visual.ImpactWave == "None" || _visual.RingSize <= 0 || ActualWidth < 1) return;
        if (_rings.Count > 64) _rings.RemoveAt(0);
        _rings.Add(new Ring { X = x, Y = y, Color = color, Life = .55, Shock = _visual.ImpactWave == "Shockwave", Ripple = _visual.ImpactWave == "Ripple", Strength = strength });
        InvalidateVisual();
    }

    /// <summary>Impact phase, flash channel: a short white-hot flare at the hit point.</summary>
    private void SpawnImpactFlash(double x, double y, Color color, double strength)
    {
        if (!_visual.ShowImpactFlash || _visual.ImpactFlashIntensity <= 0 || ActualWidth < 1) return;
        if (_flashes.Count > 32) _flashes.RemoveAt(0);
        _flashes.Add(new Flash { X = x, Y = y, Color = color, Life = .18, Strength = strength, Style = _visual.ImpactFlashStyle switch { "Lightning" => 1, "Plasma" => 2, _ => 0 } });
        InvalidateVisual();
    }

    /// <summary>Release phase: what happens at the key when a note ends (live release or song note-end).</summary>
    private void SpawnReleaseFx(int pitch)
    {
        var effect = _visual.ReleaseEffect;
        if (effect == "Fade" || ActualWidth < 1) return;
        var strength = _visual.ReleaseIntensity / 100;
        if (strength <= 0) return;
        var clamped = Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1);
        var x = KeyCenters[clamped] * ActualWidth;
        var y = ActualHeight - KeyboardHeight - 1;
        var color = AdjustColor(_activeKey[clamped] ? _activeKeyColor[clamped] : NoteColor(clamped, 0));
        switch (effect)
        {
            case "Float Up": // The note's last breath drifts upward.
            {
                var count = Math.Clamp((int)(6 * strength) + 2, 0, 10);
                for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
                    _sparks.Add(new Spark
                    {
                        Kind = 1, X = x + (_random.NextDouble() - .5) * 18, Y = y - 4,
                        Vx = (_random.NextDouble() - .5) * 24, Vy = -(50 + _random.NextDouble() * 90),
                        Life = .7 + _random.NextDouble() * .5, Age = 0, Mass = .5, Phase = _random.NextDouble() * Math.PI * 2,
                        Size = 1.4 + _random.NextDouble() * 1.6, Grav = -.25, Color = color
                    });
                break;
            }
            case "Dissolve": // The note crumbles into tiny fading dots.
            {
                var count = Math.Clamp((int)(10 * strength) + 3, 0, 16);
                for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
                {
                    var angle = _random.NextDouble() * Math.PI * 2;
                    var speed = 20 + _random.NextDouble() * 70;
                    _sparks.Add(new Spark
                    {
                        Kind = 1, X = x + (_random.NextDouble() - .5) * 26, Y = y - 6,
                        Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed - 20,
                        Life = .4 + _random.NextDouble() * .35, Age = 0, Mass = 1, Phase = _random.NextDouble() * Math.PI * 2,
                        Size = .9 + _random.NextDouble() * 1.1, Grav = .3, DragK = 2, Color = color
                    });
                }
                break;
            }
            case "Smoke": // A small gray puff.
            {
                var count = Math.Clamp((int)(4 * strength) + 2, 0, 8);
                for (var i = 0; i < count && _sparks.Count < MaxParticles; i++)
                    _sparks.Add(new Spark
                    {
                        Kind = 3, X = x + (_random.NextDouble() - .5) * 20, Y = y - 6,
                        Vx = (_random.NextDouble() - .5) * 20, Vy = -(30 + _random.NextDouble() * 50),
                        Life = .8 + _random.NextDouble() * .6, Age = 0, Mass = 1, Phase = _random.NextDouble() * Math.PI * 2,
                        Size = 2 + _random.NextDouble() * 2, Grav = -.1, DragK = 2, Color = Blend(Color.FromRgb(150, 145, 140), color, .2)
                    });
                break;
            }
            case "Snap Back": // The note snaps back into the key.
                if (_rings.Count <= 64) _rings.Add(new Ring { X = x, Y = y, Color = color, Life = .25, Implode = true, Strength = strength });
                break;
            case "Echo Rings": // Two soft rings echo the ending.
                if (_rings.Count <= 63)
                {
                    _rings.Add(new Ring { X = x, Y = y, Color = color, Life = .5, Strength = .8 * strength });
                    _rings.Add(new Ring { X = x, Y = y, Color = Colors.White, Life = .5, Strength = .4 * strength });
                }
                break;
        }
        InvalidateVisual();
    }

    /// <summary>Release phase: song notes whose End just passed the playhead emit the release effect.</summary>
    private void ScanReleaseFx()
    {
        if (!_playing || _visual.ReleaseEffect == "Fade" || _notes.Count == 0 || _visual.ReleaseIntensity <= 0)
        {
            _releaseScanPos = _position;
            return;
        }
        if (_position < _releaseScanPos - .001) // seek/loop backwards: skip the backlog instead of bursting stale releases
        {
            _releaseScanPos = _position;
            _releaseScanIndex = NoteTimeline.FirstIndexAtOrAfter(_notes, _position);
            return;
        }
        var firstUnended = -1; var budget = 24; // cap releases per frame so dense chords cannot flood the lists
        for (var j = _releaseScanIndex; j < _notes.Count; j++)
        {
            var note = _notes[j];
            if (note.Start > _position) break;
            if (note.End > _releaseScanPos && note.End <= _position)
            {
                if (budget > 0) { SpawnReleaseFx(note.Pitch); budget--; }
            }
            else if (note.End > _position && firstUnended < 0) firstUnended = j;
        }
        _releaseScanIndex = firstUnended >= 0 ? firstUnended : NoteTimeline.FirstIndexAtOrAfter(_notes, _position);
        _releaseScanPos = _position;
    }

    public void Advance(double seconds)
    {
        var dt = Math.Clamp(seconds, 0, .05) * _visual.PhysicsTimeFactor / 100; _elapsed += dt;
        _beatPulse *= Math.Exp(-6 * dt); _energyLevel *= Math.Exp(-2.2 * dt); // smart-modulator envelopes decay
        if (seconds > 0) _fps += (1 / seconds - _fps) * .08;
        var hitY = ActualHeight - KeyboardHeight;
        var lane = ActualWidth / KeyCount;
        // Flames follow a per-key "heat" value: it rises quickly while the key sounds and cools down after release.
        _anyHeat = false;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var heat = _keyHeat[pitch];
            heat = _activeKey[pitch] ? Math.Min(1, heat + dt * 7) : Math.Max(0, heat - dt * 2.4);
            _keyHeat[pitch] = heat;
            if (heat > .01) _anyHeat = true;
            if (_activeKey[pitch] && _visual.ShowWisps && _visual.WispAmount > 0 && ActualWidth >= 1) SpawnWisps(pitch, dt, hitY, lane);
        }
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
            p.Vx += field * dt * 14;
            // Thermal buoyancy + micro curl turbulence
            var heatRatio = Math.Clamp(1.0 - p.Age / p.Life, 0, 1);
            var buoyancy = -38.0 * heatRatio * (1.0 / Math.Max(0.2, p.Mass));
            p.Vy += (_visual.Gravity * p.Grav + buoyancy) * dt;
            if (p.Kind == 2) p.Vx += Math.Sin(p.Age * 9 + p.Phase) * 60 * dt; // confetti flutter
            var curl = Math.Sin(p.Y * 0.04 + p.Phase + _elapsed * 3.2) * 14.0 * (1.0 - p.Age / p.Life);
            p.Vx += curl * dt;
            var damping = Math.Exp(-_visual.Drag / 100 * p.DragK * dt); p.Vx *= damping; p.Vy *= damping;
        }
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            var ring = _rings[i]; ring.Age += dt;
            if (ring.Age >= ring.Life) _rings.RemoveAt(i);
        }
        for (var i = _flashes.Count - 1; i >= 0; i--)
        {
            var flash = _flashes[i]; flash.Age += dt;
            if (flash.Age >= flash.Life) _flashes.RemoveAt(i);
        }
        for (var i = _liveTrails.Count - 1; i >= 0; i--)
        {
            var trail = _liveTrails[i]; trail.Age += dt;
            if (trail.KeyDown) trail.HeldSeconds += dt;
            if (_visual.NoteDirection == "Up")
            {
                // The bar is born at the hit line on the keypress and climbs out of the top of the
                // stage; the press-time impact burst already fired, so no second landing burst.
                trail.Hit = true;
                var tailY = hitY - (trail.Age - trail.HeldSeconds) * _visual.NoteFallSpeed;
                if (trail.Released && tailY < -32) _liveTrails.RemoveAt(i);
            }
            else
            {
                var y = 28 + trail.Age * _visual.NoteFallSpeed;
                if (!trail.Hit && y >= hitY) { trail.Hit = true; Impact(trail.Pitch, .8); }
                var tailY = y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed;
                if (trail.Released && tailY > hitY + 32) _liveTrails.RemoveAt(i);
            }
        }
        ScanReleaseFx();
        InvalidateVisual();
    }

    private void SpawnWisps(int pitch, double dt, double hitY, double lane)
    {
        if (_sparks.Count >= MaxParticles) return;
        _wispBudget[pitch] += _visual.WispAmount * dt;
        var count = (int)_wispBudget[pitch]; if (count <= 0) return;
        _wispBudget[pitch] -= count;
        var color = AdjustColor(_activeKeyColor[pitch]);
        var x = KeyCenters[pitch] * ActualWidth;
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

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight; if (width < 1 || height < 1) return;
        // Read once; OnDpiChanged keeps it current. The value is asked for defensively because the stage is
        // also drawn off screen to make the picture a preset file carries (see PresetThumbnail), and an
        // element that is not attached to a window yet has no DPI to report on some systems.
        if (_pixelsPerDip <= 0) _pixelsPerDip = PixelsPerDip(this);
        var keyHeight = KeyboardHeight; var keyTop = height - keyHeight; var lane = width / KeyCount;
        var chroma = _visual.BackgroundMode == "ChromaGreen";
        var scale = _visual.CameraZoom / 100;
        var parallax = _visual.CameraParallax / 100;
        var offsetX = (width - width * scale) * _visual.CameraOffset / 100 + (_pointerX - .5) * parallax * 28;
        var offsetY = (height - height * scale) * .5 + (_pointerY - .5) * parallax * 20;
        if (!_transparentBackdrop) dc.DrawRectangle(chroma ? ChromaGreen : Ink, null, new Rect(0, 0, width, height));
        dc.PushTransform(new TranslateTransform(offsetX, offsetY)); dc.PushTransform(new ScaleTransform(scale, scale));
        if (chroma)
        {
            if (!_transparentBackdrop) dc.DrawRectangle(ChromaGreen, null, new Rect(0, 0, width, height));
        }
        else
        {
            // A transparent export skips the fills that would cover the alpha channel; the layers the look
            // enables (stars, lanes, horizon, beams, motes, ambient families) are drawn either way.
            if (!_transparentBackdrop) dc.DrawRectangle(Brush(ParseColor(_visual.BackgroundColor, Colors.Black)), null, new Rect(0, 0, width, height));
            if (!_transparentBackdrop && _visual.ShowBackground && _visual.BackgroundMode == "Image" && _backgroundImage is not null)
            {
                var imageScale = Math.Max(width / _backgroundImage.Width, height / _backgroundImage.Height);
                var imageWidth = _backgroundImage.Width * imageScale; var imageHeight = _backgroundImage.Height * imageScale;
                dc.DrawImage(_backgroundImage, new Rect((width - imageWidth) / 2, (height - imageHeight) / 2, imageWidth, imageHeight));
                if (_visual.BackgroundDim > 0) dc.DrawRectangle(Brush(Color.FromArgb((byte)(_visual.BackgroundDim * 2.1), 0, 0, 0)), null, new Rect(0, 0, width, height));
            }
            if (!_transparentBackdrop && _visual.ShowBackground && _visual.BackgroundGradient)
            {
                var aura = new RadialGradientBrush { Center = new Point(.5, .24), GradientOrigin = new Point(.5, .24), RadiusX = .72, RadiusY = .88, MappingMode = BrushMappingMode.RelativeToBoundingBox };
                aura.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(40 * _visual.BloomIntensity / 65), 121, 48, 174), 0));
                aura.GradientStops.Add(new GradientStop(Color.FromArgb(15, 49, 50, 137), .5)); aura.GradientStops.Add(new GradientStop(Color.FromArgb(0, 5, 4, 13), 1));
                aura.Freeze(); dc.DrawRectangle(aura, null, new Rect(0, 0, width, keyTop));
            }
            if (_visual.ShowBackground)
            {
                if (_visual.ShowStars) DrawStars(dc, width, keyTop);
                if (_visual.BackgroundGuide) DrawLanes(dc, width, keyTop, lane);
            }
            if (_visual.HorizonGlow > 0) DrawHorizonGlow(dc, width, keyTop);
            if (_visual.ShowLightBeams && _visual.BeamIntensity > 0) DrawKeyBeams(dc, width, keyTop, lane);
            // The ambient mote layer sits above the background but below the note roll, so the music stays readable.
            if (_visual.ShowPetals && _visual.PetalAmount > 0) DrawPetals(dc, width, keyTop);
            if (_visual.AmbientEnergy != "None") DrawAmbientEnergy(dc, width, keyTop);
            if (_visual.AmbientNature != "None") DrawAmbientNature(dc, width, keyTop);
            if (_visual.AmbientLight != "None") DrawAmbientLight(dc, width, keyTop);
            if (_visual.AmbientCosmic != "None") DrawAmbientCosmic(dc, width, keyTop);
        }
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, keyTop + 2)));
        DrawNotes(dc, width, keyTop, lane);
        DrawLiveTrails(dc, width, keyTop, lane);
        // The sheet sits above the roll: it is a reading layer, and the roll keeps moving behind it.
        if (_visual.ShowSheet) DrawSheet(dc, width, keyTop);
        dc.Pop();
        if (_visual.ShowFlame && _visual.FlameIntensity > 0) DrawFlames(dc, width, keyTop, lane);
        if (_visual.ShowImpactRings) DrawRings(dc);
        if (_visual.ShowImpactFlash) DrawImpactFlashes(dc);
        if (_visual.ShowEmbers || _visual.ShowWisps) DrawSparks(dc, width, keyTop);
        if (_visual.ShowHalo) DrawImpactLine(dc, width, keyTop);
        if (_visual.HoldElectricArc && _visual.HoldArcIntensity > 0) DrawElectricArcs(dc, width, keyTop);
        if (!chroma && !_transparentBackdrop && _visual.Vignette > 0) DrawVignette(dc, width, keyTop);
        if (_visual.ShowKeys) DrawKeyboard(dc, width, height, lane, keyTop);
        // The camera overlay is drawn last: the player is in front of everything the stage paints.
        DrawCameraOverlay(dc, width, height);
        if (_visual.ShowWatermark) DrawWatermark(dc, width, height);
        if (_visual.ShowCounter || _visual.ShowFps) DrawCounter(dc, width);
        dc.Pop(); dc.Pop();
    }

    /// <summary>Keeps the cached DPI current so per-frame text and shader scaling stay crisp after a display change.</summary>
    /// <summary>Pixels per DIP of a visual, or 1 when the visual cannot report one yet.</summary>
    private static double PixelsPerDip(Visual visual)
    {
        try { return VisualTreeHelper.GetDpi(visual).PixelsPerDip; }
        catch { return 1; }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _pixelsPerDip = newDpi.PixelsPerDip;
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    private void DrawStars(DrawingContext dc, double width, double height)
    {
        // The star field is cleared on resize; rebuilding it here every frame made the stars flicker like noise.
        if (_stars.Count == 0) RebuildStars(width, height);
        var visible = (int)(_stars.Count * Math.Clamp(_visual.StarDensity / 100, 0, 1));
        for (var i = 0; i < visible; i++)
        {
            var star = _stars[i];
            var twinkle = .35 + .65 * (.5 + .5 * Math.Sin(_elapsed * star.Speed + star.Phase));
            var alpha = (byte)(star.Alpha * twinkle); var brush = Brush(Color.FromArgb(alpha, 222, 191, 255));
            dc.DrawEllipse(brush, null, new Point(star.X, star.Y), star.Size, star.Size);
        }
    }

    private void RebuildStars(double width, double height)
    {
        _stars.Clear(); var count = (int)Math.Clamp(width * height / 5200, 90, 320);
        for (var i = 0; i < count; i++) _stars.Add(new Star(_random.NextDouble() * width, _random.NextDouble() * height, .45 + _random.NextDouble() * 1.2, (byte)(18 + _random.Next(48)), .5 + _random.NextDouble() * 2.0, _random.NextDouble() * 7));
    }

    private void DrawLanes(DrawingContext dc, double width, double height, double lane)
    {
        // Guide lines sit on the centre of every key so they line up with the falling notes. Only
        // three pens are ever needed, so they are cached once instead of built 88 times per frame.
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var alpha = pitch % 12 == 0 ? 23 : pitch % 12 is 2 or 4 or 7 or 9 or 11 ? 10 : 5;
            var color = Color.FromArgb((byte)alpha, 186, 141, 255);
            var x = KeyCenters[pitch] * width;
            dc.DrawLine(Pen(color, pitch % 12 == 0 ? 1 : .6), new Point(x, 0), new Point(x, height));
        }
    }

    private void DrawHorizonGlow(DrawingContext dc, double width, double keyTop)
    {
        var color = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var glowHeight = 40 + _visual.HorizonGlow * 2.6;
        var key = GradientKey(1, Color.FromArgb((byte)Math.Clamp(_visual.HorizonGlow, 0, 255), color.R, color.G, color.B));
        if (!_gradientCache.TryGetValue(key, out var brush))
        {
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(_visual.HorizonGlow * 1.7), color.R, color.G, color.B), 1));
            gradient.Freeze(); brush = CacheGradient(key, gradient);
        }
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
            var key = GradientKey(2, Color.FromArgb((byte)Math.Clamp(_visual.BeamIntensity, 0, 255), color.R, color.G, color.B));
            if (!_gradientCache.TryGetValue(key, out var brush))
            {
                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(10 + _visual.BeamIntensity * .55), color.R, color.G, color.B), .78));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(20 + _visual.BeamIntensity * .9), color.R, color.G, color.B), 1));
                gradient.Freeze(); brush = CacheGradient(key, gradient);
            }
            var x = KeyCenters[pitch] * width;
            dc.DrawRectangle(brush, null, new Rect(x - beamWidth / 2, 0, beamWidth, keyTop));
        }
    }

    /// <summary>Blossom petals drifting down the stage: the recital layer of the Sakura theme.</summary>
    private void DrawPetals(DrawingContext dc, double width, double height)
    {
        var count = Math.Clamp((int)Math.Round(_visual.PetalAmount * Math.Clamp(width / 1280, .45, 1.6)), 0, 150);
        if (count == 0) return;
        if (count != _petalCount || Math.Abs(width - _petalWidth) > .5 || Math.Abs(height - _petalHeight) > .5) RebuildPetals(width, height, count);
        var color = AdjustColor(ParseColor(_visual.PetalColor, Color.FromRgb(255, 179, 207)));
        var body = Brush(Color.FromArgb(232, color.R, color.G, color.B));
        var tipColor = Blend(color, Colors.White, .5);
        var tip = Brush(Color.FromArgb(205, tipColor.R, tipColor.G, tipColor.B));
        const double margin = 60; var span = height + margin * 2;
        foreach (var petal in _petals)
        {
            // Position is a pure function of elapsed time, so petals never accumulate drift and a
            // recording of the same song always shows the same flight path.
            var y = (petal.Y + _elapsed * petal.Speed) % span - margin;
            var x = petal.X + Math.Sin(_elapsed * petal.Drift + petal.Phase) * petal.Sway;
            var angle = (petal.Phase * 57.3 + _elapsed * petal.Spin * 57.3) % 360;
            var squash = .42 + .58 * Math.Abs(Math.Sin(_elapsed * petal.Drift * 1.35 + petal.Phase));
            dc.PushTransform(new RotateTransform(angle, x, y));
            dc.DrawEllipse(body, null, new Point(x, y), petal.Size, petal.Size * squash);
            dc.DrawEllipse(tip, null, new Point(x + petal.Size * .3, y + petal.Size * .16), petal.Size * .5, petal.Size * .5 * squash);
            dc.Pop();
        }
    }

    private void RebuildPetals(double width, double height, int count)
    {
        _petals.Clear(); _petalCount = count; _petalWidth = width; _petalHeight = height;
        var scale = Math.Clamp(height / 720, .6, 1.8);
        for (var i = 0; i < count; i++)
            _petals.Add(new Petal(
                X: _petalRandom.NextDouble() * width,
                Y: _petalRandom.NextDouble() * (height + 120),
                Size: (2.6 + _petalRandom.NextDouble() * 5.2) * scale,
                Speed: 18 + _petalRandom.NextDouble() * 52,
                Sway: 8 + _petalRandom.NextDouble() * 34,
                Drift: .35 + _petalRandom.NextDouble() * .9,
                Spin: (_petalRandom.NextDouble() - .5) * 1.6,
                Phase: _petalRandom.NextDouble() * Math.PI * 2));
    }

    /// <summary>
    /// The webcam overlay: the newest frame, placed by the corner and size settings and faded by the opacity,
    /// on top of the keyboard. Nothing is drawn when the layer is off or no frame has arrived, so a machine
    /// without a camera shows the stage exactly as it always did.
    /// </summary>
    private void DrawCameraOverlay(DrawingContext dc, double width, double height)
    {
        var frame = _cameraFrame;
        if (!_visual.ShowCameraOverlay || frame is null || width < 80 || height < 80) return;
        var aspect = frame.PixelHeight <= 0 ? 16.0 / 9 : frame.PixelWidth / (double)frame.PixelHeight;
        var area = CameraOverlay.Place(_visual.CameraCorner, width, height, _visual.CameraSize, aspect);
        if (area.Width < 8 || area.Height < 8) return;
        dc.PushOpacity(CameraOverlay.OpacityFactor(_visual.CameraOpacity));
        dc.DrawImage(frame, area);
        dc.Pop();
    }

    /// <summary>The newest camera frame, or <c>null</c> when nothing has arrived; handed over by the window.</summary>
    private BitmapSource? _cameraFrame;

    /// <summary>
    /// Publishes the newest frame of the overlay. The window builds it on the UI thread from the buffer the
    /// reader thread fills, so the stage never touches a camera itself.
    /// </summary>
    public void SetCameraFrame(BitmapSource? frame)
    {
        _cameraFrame = frame;
        InvalidateVisual();
    }

    /// <summary>True while a frame is available; the dock uses it to say whether the overlay is really drawing.</summary>
    public bool HasCameraFrame => _cameraFrame is not null;

    /// <summary>
    /// The staff band: the same seconds-per-pixel the roll uses, so a written note and its falling bar always
    /// line up under the playhead.
    /// </summary>
    private void DrawSheet(DrawingContext dc, double width, double hitY)
    {
        var noteSpeed = FallSpeed * _visual.NoteFallSpeed / 550;
        SheetLayer.Draw(dc, SheetLayer.Band(width, hitY, 34), _notes, _position, _visual.HandSplitPitch,
            _beats, _beatsPerBar, hitY / Math.Max(1, noteSpeed),
            Color.FromRgb(243, 229, 255), ParseColor(_visual.HaloColor, Color.FromRgb(198, 110, 255)), 1, _pixelsPerDip);
    }

    private void DrawNotes(DrawingContext dc, double width, double hitY, double lane)
    {
        if (!_visual.ShowNotes || !_playing) return;
        var noteSpeed = FallSpeed * _visual.NoteFallSpeed / 550;
        var lookBehind = hitY / noteSpeed + 1;
        // Notes are sorted by start time: anything still on screen started no earlier than (position - 1 s - longest note).
        var latestStart = _position + lookBehind;
        var noteWidth = lane * _visual.NoteWidth / 100;
        var gap = Math.Min(_visual.NoteGap, 12);
        var rising = _visual.NoteDirection == "Up";
        for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - 1 - _maxNoteDuration); i < _notes.Count; i++)
        {
            var note = _notes[i];
            if (note.Start > latestStart) break;
            if (note.Pitch < FirstPitch || note.Pitch >= FirstPitch + KeyCount || note.End < _position - 1) continue;
            var noteHeight = Math.Clamp(note.Duration * noteSpeed - gap, _visual.NoteMinLength, hitY * .9);
            var color = note.Played ? Color.FromRgb(82, 237, 208) : note.Missed ? Color.FromRgb(255, 83, 113) : NoteColor(note.Pitch, note.Track);
            if (_visual.VelocityColor && !note.Played && !note.Missed) // Velocity → Color on song notes.
                color = Blend(color, VelocityTint(note.Velocity / 127.0), _visual.VelocityColorAmount / 100);
            var sounding = note.Start <= _position && note.End > _position;
            if (rising)
            {
                // The head is born at the hit line on onset and climbs out of the top of the stage; the
                // pre-onset part of the bar sits behind the keyboard and the clip keeps it hidden.
                var headY = hitY + (note.Start - _position) * noteSpeed;
                if (headY > hitY || headY + noteHeight < 0) continue;
                DrawConfiguredNote(dc, new Rect(KeyCenters[note.Pitch] * width - noteWidth / 2, headY, noteWidth, noteHeight), color, note.Played ? .42 : 1, sounding, note.Pitch, true);
            }
            else
            {
                var bottom = hitY - (note.Start - _position) * noteSpeed;
                var top = bottom - noteHeight;
                if (top > hitY || bottom < 0) continue;
                DrawConfiguredNote(dc, new Rect(KeyCenters[note.Pitch] * width - noteWidth / 2, top, noteWidth, noteHeight), color, note.Played ? .42 : 1, sounding, note.Pitch);
            }
        }
    }

    private void DrawConfiguredNote(DrawingContext dc, Rect r, Color color, double opacity, bool sounding, int pitch, bool rising = false, bool ghostPass = false)
    {
        if (sounding && !ghostPass && _visual.HoldVibration) // Vibration: the held note trembles subtly.
            r.X += Math.Sin(_elapsed * 40 + pitch * 2.2) * _visual.HoldVibrationAmount / 100 * 4;
        if (sounding && !ghostPass && _visual.HoldColorCycle) // Color Cycle: the held note keeps shifting hue.
            color = Blend(color, ColorFromHue(Hue(pitch) + _elapsed * _visual.HoldColorCycleSpeed * 3), .8);
        if (_visual.FallingPulse && !sounding && !ghostPass) // Pulsing: the note breathes bright/dim while it travels.
            opacity *= .62 + .38 * Math.Sin(_elapsed * (1.5 + _visual.FallingPulseRate / 100 * 9) + pitch * .7);
        if (_visual.FallingTrail == "Rainbow" && !ghostPass) // Rainbow Shift: the note body cycles through the rainbow.
            color = ColorFromHue(_elapsed * (20 + _visual.FallingTrailIntensity * .8) + pitch * 9);
        color = AdjustColor(color);
        var style = _visual.NoteStyle;
        var radius = Math.Min(r.Height / 2, Math.Min(r.Width / 2, 2 + _visual.NoteRoundness / 100 * 12));
        var bloom = _visual.BloomSize / 100;
        var glow = _visual.NoteGlow / 100 * _visual.BloomIntensity / 65 * (sounding ? 1.35 * BreathFactor() : 1) * BeatBoost() * EnergyBoost();
        var outer = 2 + bloom * 10;
        var tint = _visual.NoteTint / 78;
        var edgeWidth = .4 + _visual.NoteEdgeWidth / 45;
        var (cr, cg, cb) = (color.R, color.G, color.B);
        var bright = Blend(color, Colors.White, .35);
        if (!ghostPass) DrawFallingTrail(dc, r, color, opacity, pitch, rising);
        // Outer bloom shared by every style: stacked rounded shells with exponentially decaying alpha so the
        // glow falls off like real light instead of showing two flat banded rings; neon spreads further.
        var bloomScale = style == "Neon" ? 1.4 : 1;
        for (var layer = 4; layer >= 1; layer--)
        {
            var f = layer / 4.0;
            var spread = outer * bloomScale * (.55 + 1.45 * f);
            var shellAlpha = Alpha(58 * opacity * glow * Math.Exp(-2.2 * f));
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(shellAlpha, cr, cg, cb)), null, Inflate(r, spread), radius + spread, radius + spread);
        }
        switch (style)
        {
            case "Neon":
            {
                // Hollow neon tube: a near-black interior, a wide tinted soft stroke and a white-hot core
                // stroke, matching the reference look of a glowing capsule outline.
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(50 * opacity * tint), cr, cg, cb)), null, r, radius, radius);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(120 * opacity), 4, 3, 10)), null, Inflate(r, -Math.Min(3, r.Width / 4)), radius, radius);
                var soft = Pen(Color.FromArgb(Alpha(150 * opacity * glow), cr, cg, cb), edgeWidth * 3 + 2);
                dc.DrawRoundedRectangle(null, soft, Inflate(r, -.7), radius, radius);
                var hot = Blend(color, Colors.White, .78);
                var core = Pen(Color.FromArgb(Alpha(255 * opacity * Math.Min(1, _visual.NoteEdge / 100)), hot.R, hot.G, hot.B), edgeWidth + 1);
                dc.DrawRoundedRectangle(null, core, Inflate(r, -.7), radius, radius);
                break;
            }
            case "Fire":
            {
                // Burning bar: a dim base, a bright layer punched through an animated ember mask, warm bloom and a glowing rim.
                var dim = Blend(color, Color.FromRgb(20, 4, 2), .55 * _visual.NoteTexture / 100);
                var hot = Blend(color, Color.FromRgb(255, 244, 190), .5);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(235 * opacity * tint), dim.R, dim.G, dim.B)), null, r, radius, radius);
                if (_visual.NoteTexture > 0)
                {
                    var mask = new ImageBrush(FireMask)
                    {
                        TileMode = TileMode.Tile, Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(pitch * 37 % FireMaskWidth, (pitch * 53 + _elapsed * 34) % FireMaskHeight, FireMaskWidth, FireMaskHeight),
                        Opacity = 1
                    };
                    mask.Freeze();
                    dc.PushOpacityMask(mask);
                    dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(245 * opacity * tint), hot.R, hot.G, hot.B)), null, r, radius, radius);
                    dc.Pop();
                }
                else dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(245 * opacity * tint), hot.R, hot.G, hot.B)), null, r, radius, radius);
                var rim = Pen(Color.FromArgb(Alpha(230 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B), edgeWidth);
                dc.DrawRoundedRectangle(null, rim, Inflate(r, -.7), radius, radius);
                break;
            }
            case "Glass":
            {
                dc.DrawRoundedRectangle(GlassBrush(color, opacity * tint), null, r, radius, radius);
                if (_visual.NoteEdge > 0)
                {
                    var rim = Pen(Color.FromArgb(Alpha(200 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B), edgeWidth * .7 + .2);
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
                if (_visual.Notes3D)
                {
                    // Cylindrical body: a lit rounded centre with darkened edges reads as a physical rod;
                    // a translucent vertical bevel on top adds thickness so the bar has real volume.
                    dc.PushOpacity(Math.Clamp(opacity * tint, 0, 1));
                    dc.DrawRoundedRectangle(NoteCylinderBrush(color), null, r, radius, radius);
                    dc.Pop();
                    dc.PushOpacity(.4 * Math.Clamp(opacity * tint, 0, 1));
                    dc.DrawRoundedRectangle(NoteBodyBrush(color), null, r, radius, radius);
                    dc.Pop();
                }
                else dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(205 * opacity * tint), cr, cg, cb)), null, r, radius, radius);
                if (_visual.NoteEdge > 0)
                {
                    var rim = Pen(Color.FromArgb(Alpha(235 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B), edgeWidth);
                    dc.DrawRoundedRectangle(null, rim, Inflate(r, -.7), radius, radius);
                }
                break;
            }
        }
        if (_visual.NoteRefraction > 0 && r.Width > 6)
        {
            var fringe = Alpha((style == "Neon" ? 90 : 170) * opacity * _visual.NoteRefraction / 100);
            dc.DrawLine(Pen(Color.FromArgb(fringe, 255, 255, 255), 1), new Point(r.X + 2, r.Y + 3), new Point(r.X + 2, r.Bottom - 3));
        }
        if (_visual.Notes3D && r.Height > 20 && style is "Solid" or "Glass")
        {
            // The solid style already carries its bevel gradient, so only glass needs the extra inner shade.
            if (style == "Glass")
            {
                var inner = new Rect(r.X + 3, r.Y + 4, Math.Max(2, r.Width - 6), Math.Max(3, r.Height - 8));
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(55 * opacity), 10, 7, 18)), null, inner, Math.Min(radius, inner.Width / 2), Math.Min(radius, inner.Width / 2));
            }
            dc.DrawLine(Pen(Color.FromArgb(Alpha((style == "Glass" ? 165 : 90) * opacity), 255, 250, 255), 1), new Point(r.X + 4, r.Y + 5), new Point(r.X + 4, r.Bottom - 5));
        }
        if (_visual.NoteHeadGlow > 0 && r.Height > 6)
        {
            // Bright cap on the edge that leads: the bottom while falling, the top while rising.
            var headHeight = Math.Min(r.Height * .35, 4 + _visual.NoteHeadGlow / 100 * 8);
            var head = rising
                ? new Rect(r.X + 1, r.Y + 1, Math.Max(1, r.Width - 2), headHeight)
                : new Rect(r.X + 1, r.Bottom - headHeight - 1, Math.Max(1, r.Width - 2), headHeight);
            var headAlpha = Alpha((sounding ? 230 : 120) * opacity * _visual.NoteHeadGlow / 100);
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(headAlpha, bright.R, bright.G, bright.B)), null, head, Math.Min(radius, headHeight / 2), Math.Min(radius, headHeight / 2));
        }
        if (_visual.ShowNoteLabels && r.Width >= 11 && r.Height >= 15)
        {
            var label = r.Width >= 20 ? NoteLabel(pitch) : ShortNoteLabel(pitch);
            var luminance = .2126 * cr + .7152 * cg + .0722 * cb;
            var textColor = style == "Neon" ? Colors.White : luminance > 150 ? Color.FromRgb(12, 8, 20) : Colors.White;
            DrawLabel(dc, label, new Point(r.X + r.Width / 2, r.Bottom - Math.Min(12, r.Height / 2)), Math.Min(11, r.Width * .62), Color.FromArgb(Alpha(230 * opacity), textColor.R, textColor.G, textColor.B), true);
        }
        if (_visual.FallingGhost && !ghostPass && r.Height > 4) DrawNoteGhosts(dc, r, color, opacity, pitch, rising);
        if (_visual.HoldBar && sounding && !ghostPass) DrawHoldBar(dc, r, color, opacity);
    }

    /// <summary>Vertical bevel for solid note bars: a lit top edge, the saturated core and a shadowed bottom.</summary>
    private Brush NoteBodyBrush(Color color)
    {
        var key = GradientKey(7, Color.FromArgb(255, color.R, color.G, color.B));
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;
        var top = Blend(color, Colors.White, .34);
        var bottom = Blend(color, Color.FromRgb(6, 4, 12), .46);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(top.R, top.G, top.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(color.R, color.G, color.B), .55));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(bottom.R, bottom.G, bottom.B), 1));
        gradient.Freeze();
        return CacheGradient(key, gradient);
    }

    /// <summary>
    /// A linear gradient whose alpha decays as a gaussian away from <paramref name="peak"/>, which reads on
    /// screen as light scattering through air (inverse-square-ish falloff) instead of a flat banded stripe.
    /// </summary>
    private static Brush FalloffBrush(Color color, double peakAlpha, double peak, double falloff, bool horizontal)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        const int steps = 8;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var distance = t - peak;
            var alpha = peakAlpha * Math.Exp(-falloff * distance * distance);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), color.R, color.G, color.B), t));
        }
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Horizontal cylinder shading for a 3D note bar: dark rounded edges, a lit shoulder and a hot specular
    /// centre, so the bar reads as a physical rod rather than a flat rectangle.
    /// </summary>
    private Brush NoteCylinderBrush(Color color)
    {
        var key = GradientKey(8, Color.FromArgb(255, color.R, color.G, color.B));
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;
        var edge = Blend(color, Color.FromRgb(6, 4, 12), .55);
        var shoulder = Blend(color, Colors.White, .18);
        var specular = Blend(color, Colors.White, .6);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(edge.R, edge.G, edge.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(shoulder.R, shoulder.G, shoulder.B), .28));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(specular.R, specular.G, specular.B), .46));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(color.R, color.G, color.B), .68));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(edge.R, edge.G, edge.B), 1));
        gradient.Freeze();
        return CacheGradient(key, gradient);
    }

    private Brush GlassBrush(Color color, double strength)
    {
        var key = GradientKey(3, Color.FromArgb(Alpha(strength * 255), color.R, color.G, color.B));
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;
        var light = Blend(color, Colors.White, .55); var deep = Blend(color, Color.FromRgb(8, 6, 18), .35);
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(225 * strength), light.R, light.G, light.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(210 * strength), color.R, color.G, color.B), .45));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(215 * strength), deep.R, deep.G, deep.B), 1));
        gradient.Freeze();
        return CacheGradient(key, gradient);
    }

    private Brush CacheGradient(ulong key, Brush brush)
    {
        if (_gradientCache.Count > 2048) _gradientCache.Clear();
        _gradientCache[key] = brush; return brush;
    }

    /// <summary>Resolves the color of a note according to the active color mode (gradient palette, hands, tracks or rainbows).</summary>
    private Color NoteColorCore(int pitch, int track)
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
                // The time term steps at 30 Hz so the hue cycles smoothly on screen but the brush
                // cache sees a stable colour between steps instead of a fresh one every frame.
                return ColorFromHue(Math.Floor(_elapsed * 30) / 30 * _visual.RainbowSpeed * 3.6 + t * 120);
        }
        return _visual.Palette switch
        {
            "Aurora" => Blend(ParseColor(_visual.NoteColorStart, Colors.DeepSkyBlue), ParseColor(_visual.NoteColorEnd, Colors.MediumPurple), t),
            "Fire" => Blend(Color.FromRgb(255, 204, 72), Color.FromRgb(255, 53, 91), t),
            "Ocean" => Blend(Color.FromRgb(70, 246, 237), Color.FromRgb(55, 106, 255), t),
            "Violet" => Blend(Color.FromRgb(161, 94, 255), Color.FromRgb(255, 70, 196), t),
            "Custom" => Blend(ParseColor(_visual.NoteColorStart, Colors.DeepSkyBlue), ParseColor(_visual.NoteColorEnd, Colors.MediumPurple), t),
            _ => ColorFromHue(Hue(pitch) + 24)
        };
    }

    /// <summary>Note color plus the smart color modulators (octave blend, zone tint).</summary>
    internal Color NoteColor(int pitch, int track)
    {
        var color = NoteColorCore(pitch, track);
        if (_visual.OctaveColor && _visual.OctaveColorBlend > 0) // Octave → hue: each octave owns a slice of the rainbow.
            color = Blend(color, ColorFromHue(pitch / 12 * 47 % 360), _visual.OctaveColorBlend / 100);
        if (_visual.ZoneSplit && _visual.ZoneSplitAmount > 0) // Zone tint: warm bass, cool treble.
            color = Blend(color, pitch < _visual.ZoneSplitPitch ? Color.FromRgb(255, 130, 60) : Color.FromRgb(120, 180, 255), _visual.ZoneSplitAmount / 100 * .5);
        return color;
    }

    /// <summary>Velocity → Color: soft hits cool blue, hard hits hot red.</summary>
    private static Color VelocityTint(double strength)
    {
        var t = Math.Clamp(strength, 0, 1.2) / 1.2;
        return Blend(Color.FromRgb(80, 140, 255), Color.FromRgb(255, 70, 60), t);
    }

    /// <summary>Live-trail color: the note color plus the velocity tint from the recorded hit strength.</summary>
    private Color LiveNoteColor(LiveTrail trail)
    {
        var color = NoteColor(trail.Pitch, 0);
        if (_visual.VelocityColor && _visual.VelocityColorAmount > 0)
            color = Blend(color, VelocityTint(trail.Strength), _visual.VelocityColorAmount / 100);
        return color;
    }

    /// <summary>Tempo Sync: 1 normally, surging on every beat of the MIDI tempo map while enabled.</summary>
    private double BeatBoost() => _visual.TempoSync ? 1 + _beatPulse * _visual.TempoSyncAmount / 100 : 1;
    /// <summary>Audio Reactive: 1 normally, surging with the musical energy envelope while enabled.</summary>
    private double EnergyBoost() => _visual.AudioReactive ? 1 + _energyLevel * _visual.AudioReactiveAmount / 100 : 1;
    /// <summary>Pedal Glow: 1 normally, brighter while the sustain pedal is held down.</summary>
    private double PedalBoost() => _sustainPedal && _visual.PedalGlow ? 1 + _visual.PedalGlowIntensity / 100 : 1;

    private Color AdjustColor(Color input)
    {
        var saturation = _visual.Saturation / 100;
        var gray = .2126 * input.R + .7152 * input.G + .0722 * input.B;
        var contrast = _visual.Contrast / 100;
        byte Channel(double c) => (byte)Math.Clamp((c - 128) * contrast + 128, 0, 255);
        return Color.FromRgb(Channel(gray + (input.R - gray) * saturation), Channel(gray + (input.G - gray) * saturation), Channel(gray + (input.B - gray) * saturation));
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static byte Alpha(double value) => (byte)Math.Clamp(value, 0, 255);

    private void DrawLiveTrails(DrawingContext dc, double width, double hitY, double lane)
    {
        var noteWidth = lane * Math.Max(_visual.NoteWidth, 60) / 100;
        var rising = _visual.NoteDirection == "Up";
        foreach (var trail in _liveTrails)
        {
            if (!_visual.ShowNotes) break;
            var x = KeyCenters[trail.Pitch] * width - noteWidth / 2;
            if (rising)
            {
                // The head is born at the hit line and climbs out of the top of the stage; while the
                // key is held the bar stays pinned to the line and grows upward.
                var headY = hitY - trail.Age * _visual.NoteFallSpeed;
                var tailY = hitY - (trail.Age - trail.HeldSeconds) * _visual.NoteFallSpeed;
                if (tailY <= headY) continue;
                var opacity = Math.Clamp(1 - (hitY - tailY) / Math.Max(1, hitY + 18), .08, 1) * _visual.NoteTint / 100;
                DrawConfiguredNote(dc, new Rect(x, headY, noteWidth, tailY - headY), LiveNoteColor(trail), opacity, trail.KeyDown && trail.Hit, trail.Pitch, true);
            }
            else
            {
                var y = 28 + trail.Age * _visual.NoteFallSpeed;
                var tailY = Math.Max(0, y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed);
                var bottom = Math.Min(hitY + 6, y);
                if (bottom <= tailY) continue;
                var opacity = Math.Clamp(1 - tailY / Math.Max(1, hitY + 18), .08, 1) * _visual.NoteTint / 100;
                DrawConfiguredNote(dc, new Rect(x, tailY, noteWidth, bottom - tailY), LiveNoteColor(trail), opacity, trail.KeyDown && trail.Hit, trail.Pitch);
            }
        }
    }

    private void DrawSparks(DrawingContext dc, double width, double height)
    {
        var bloom = 1.4 + _visual.BloomSize / 36;
        var wispGlow = _visual.WispGlow / 100;
        foreach (var particle in _sparks)
        {
            var u = Math.Clamp(particle.Age / particle.Life, 0, 1);
            var fade = 1 - u;
            if (particle.Wisp)
            {
                if (!_visual.ShowWisps) continue;
                var wispAlpha = Alpha(210 * Math.Pow(fade, 1.4) * wispGlow);
                var wispSize = particle.Size * (1.2 + particle.Age * 1.3);
                var wispCore = Color.FromArgb(wispAlpha, particle.Color.R, particle.Color.G, particle.Color.B);
                // Three concentric shells approximate a smooth gaussian falloff, so the wisp reads as a
                // soft mote of light/smoke instead of a flat disc with a hard rim.
                var wispMid = Color.FromArgb((byte)(wispAlpha * .42), particle.Color.R, particle.Color.G, particle.Color.B);
                var wispHalo = Color.FromArgb((byte)(wispAlpha * .16), particle.Color.R, particle.Color.G, particle.Color.B);
                dc.DrawEllipse(Brush(wispHalo), null, new Point(particle.X, particle.Y), wispSize * 3.2, wispSize * 3.2);
                dc.DrawEllipse(Brush(wispMid), null, new Point(particle.X, particle.Y), wispSize * 1.9, wispSize * 1.9);
                dc.DrawEllipse(Brush(wispCore), null, new Point(particle.X, particle.Y), wispSize, wispSize);
                continue;
            }
            if (!_visual.ShowEmbers) continue;
            if (particle.Kind == 1) { DrawDroplet(dc, particle, fade, bloom); continue; }
            if (particle.Kind == 2) { DrawConfetti(dc, particle, fade); continue; }
            if (particle.Kind == 3) { DrawDust(dc, particle, fade, bloom); continue; }
            if (particle.Kind == 4) { DrawShard(dc, particle, fade); continue; }

            // Thermal blackbody cooling: White-hot -> Incandescent Gold -> Molten Amber/Orange -> Ruby Ember -> Smoke
            Color thermalCore, thermalGlow;
            if (u < 0.20)
            {
                var t = u / 0.20;
                thermalCore = Blend(Colors.White, Color.FromRgb(255, 240, 160), t);
                thermalGlow = Blend(Color.FromRgb(255, 235, 140), particle.Color, t * 0.4);
            }
            else if (u < 0.55)
            {
                var t = (u - 0.20) / 0.35;
                thermalCore = Blend(Color.FromRgb(255, 240, 160), Color.FromRgb(255, 175, 50), t);
                thermalGlow = Blend(particle.Color, Color.FromRgb(255, 130, 30), t * 0.6);
            }
            else if (u < 0.85)
            {
                var t = (u - 0.55) / 0.30;
                thermalCore = Blend(Color.FromRgb(255, 175, 50), Color.FromRgb(220, 60, 20), t);
                thermalGlow = Blend(Color.FromRgb(255, 130, 30), Color.FromRgb(140, 20, 10), t);
            }
            else
            {
                var t = (u - 0.85) / 0.15;
                thermalCore = Blend(Color.FromRgb(220, 60, 20), Color.FromRgb(70, 15, 15), t);
                thermalGlow = Color.FromRgb(60, 10, 10);
            }

            var alpha = Alpha(255 * Math.Pow(fade, 1.2) * _visual.ParticleGlow / 100);
            var color = Color.FromArgb(alpha, thermalCore.R, thermalCore.G, thermalCore.B);
            // A tighter, dimmer halo keeps sparks reading as crisp incandescent points, not soft blobs.
            var glow = Color.FromArgb((byte)(alpha * 0.28), thermalGlow.R, thermalGlow.G, thermalGlow.B);

            var speedSq = particle.Vx * particle.Vx + particle.Vy * particle.Vy;
            var speed = Math.Sqrt(speedSq);
            var size = particle.Size * (0.6 + fade * 0.6);

            // Aerodynamic velocity-stretched incandescent streak
            if (speed > 25.0)
            {
                var angle = Math.Atan2(particle.Vy, particle.Vx) * 57.29577951308232;
                var stretch = Math.Clamp(1.0 + speed * 0.018, 1.2, 5.0);
                var streakLength = size * stretch;

                dc.PushTransform(new TranslateTransform(particle.X, particle.Y));
                dc.PushTransform(new RotateTransform(angle));

                // Outer optical dispersion
                dc.DrawEllipse(Brush(glow), null, new Point(0, 0), streakLength * 1.5 * bloom, size * 1.3 * bloom);
                // Incandescent streak body
                dc.DrawEllipse(Brush(color), null, new Point(0, 0), streakLength, size);
                // White-hot filament core
                if (u < 0.6)
                {
                    var whiteCoreAlpha = (byte)(alpha * (1.0 - u / 0.6));
                    dc.DrawEllipse(Brush(Color.FromArgb(whiteCoreAlpha, 255, 255, 255)), null, new Point(0, 0), streakLength * 0.45, size * 0.55);
                }

                dc.Pop();
                dc.Pop();
            }
            else
            {
                dc.DrawEllipse(Brush(glow), null, new Point(particle.X, particle.Y), size * 2.2 * bloom, size * 2.2 * bloom);
                dc.DrawEllipse(Brush(color), null, new Point(particle.X, particle.Y), size, size);
                if (u < 0.5)
                {
                    var whiteCoreAlpha = (byte)(alpha * (1.0 - u / 0.5));
                    dc.DrawEllipse(Brush(Color.FromArgb(whiteCoreAlpha, 255, 255, 255)), null, new Point(particle.X, particle.Y), size * 0.5, size * 0.5);
                }
            }
        }
    }

    private void DrawRings(DrawingContext dc)
    {
        // Impact phase, wave channel. Both styles share the size slider; brightness follows the
        // intensity slider and the velocity of the hit (the first smart modulator: velocity mapping).
        var intensity = _visual.ImpactWaveIntensity / 100;
        if (intensity <= 0) return;
        foreach (var ring in _rings)
        {
            if (ring.Shock) { DrawShockwave(dc, ring, intensity); continue; }
            if (ring.Implode) { DrawImplode(dc, ring, intensity); continue; }
            if (ring.Ripple) { DrawRipple(dc, ring, intensity); continue; }
            var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
            var strength = Math.Clamp(.5 + ring.Strength * .5, 0, 1.2);
            var progress = 1.0 - Math.Exp(-4.2 * t);
            var radius = (5 + progress * (18 + _visual.RingSize * 1.25)) * (.7 + .3 * ring.Strength);
            var alpha = Alpha(230 * Math.Pow(1 - t, 1.6) * intensity * strength);

            // Primary acoustic compression wavefront
            var penWidth = Math.Max(0.5, 2.2 * (1 - t));
            var primaryPen = Pen(Color.FromArgb(alpha, ring.Color.R, ring.Color.G, ring.Color.B), penWidth);
            dc.DrawEllipse(null, primaryPen, new Point(ring.X, ring.Y), radius, radius * .32);

            // Secondary harmonic resonance wave
            if (t > 0.08)
            {
                var harmonicRadius = radius * 0.68;
                var harmonicAlpha = (byte)(alpha * 0.45);
                var harmonicPen = Pen(Color.FromArgb(harmonicAlpha, ring.Color.R, ring.Color.G, ring.Color.B), penWidth * 0.7);
                dc.DrawEllipse(null, harmonicPen, new Point(ring.X, ring.Y), harmonicRadius, harmonicRadius * .32);
            }

            // Central impact optical flare during strike initiation
            if (t < .30)
            {
                var flashAlpha = Alpha(170 * (1 - t / .30) * intensity * strength);
                dc.DrawEllipse(Brush(Color.FromArgb(flashAlpha, 255, 255, 255)), null, new Point(ring.X, ring.Y), 4 + radius * .2, 2.5 + radius * .08);
            }
        }
    }

    /// <summary>Filled blast wave: a hot core that expands and cools into a thin bright rim.</summary>
    private void DrawShockwave(DrawingContext dc, Ring ring, double intensity)
    {
        var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
        var strength = Math.Clamp(.5 + ring.Strength * .5, 0, 1.2);
        var progress = 1.0 - Math.Exp(-4.6 * t);
        var radius = (6 + progress * (26 + _visual.RingSize * 1.6)) * (.6 + .4 * ring.Strength);
        var fade = Math.Pow(1 - t, 1.4) * intensity * strength;
        if (fade <= .01) return;
        var (r, g, b) = (ring.Color.R, ring.Color.G, ring.Color.B);
        // Filled body: a bright core cooling toward the rim.
        var body = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5, MappingMode = BrushMappingMode.RelativeToBoundingBox };
        body.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * fade), 255, 255, 255), 0));
        body.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(120 * fade), r, g, b), .45));
        body.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(40 * fade), r, g, b), .8));
        body.GradientStops.Add(new GradientStop(Color.FromArgb(0, r, g, b), 1));
        body.Freeze();
        dc.DrawEllipse(body, null, new Point(ring.X, ring.Y), radius, radius * .34);
        // Thin bright rim riding the leading edge.
        var rim = Pen(Color.FromArgb(Alpha(235 * fade), 255, 255, 255), Math.Max(.6, 2.4 * (1 - t)));
        dc.DrawEllipse(null, rim, new Point(ring.X, ring.Y), radius, radius * .34);
    }

    /// <summary>Impact phase, flash channel: white-hot flares fading within ~180 ms of the hit.</summary>
    private void DrawImpactFlashes(DrawingContext dc)
    {
        var intensity = _visual.ImpactFlashIntensity / 100;
        if (intensity <= 0) return;
        foreach (var flash in _flashes)
        {
            if (flash.Style == 1) { DrawLightning(dc, flash, intensity); continue; }
            if (flash.Style == 2) { DrawPlasma(dc, flash, intensity); continue; }
            if (flash.Style == 3) { DrawStarMorph(dc, flash, intensity); continue; }
            var t = Math.Clamp(flash.Age / flash.Life, 0, 1);
            var strength = Math.Clamp(.5 + flash.Strength * .5, 0, 1.2);
            var fade = (1 - t) * (1 - t) * intensity * strength;
            if (fade <= .01) continue;
            var radius = 10 + t * (26 + _visual.RingSize * .8);
            var (r, g, b) = (flash.Color.R, flash.Color.G, flash.Color.B);
            var glow = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5, MappingMode = BrushMappingMode.RelativeToBoundingBox };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(235 * fade), 255, 255, 255), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * fade), r, g, b), .4));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, r, g, b), 1));
            glow.Freeze();
            dc.DrawEllipse(glow, null, new Point(flash.X, flash.Y), radius * 1.4, radius * .8);
            // Short incandescent dashes flicking upward off the dome instead of one hard horizontal streak.
            var streak = Pen(Color.FromArgb(Alpha(150 * fade), 255, 255, 255), 1.2);
            for (var s = 0; s < 6; s++)
            {
                var ox = (SeededRandom(s * 57 + (int)flash.X) - .5) * radius * 1.5;
                var len = radius * (.45 + SeededRandom(s * 91 + (int)flash.Y) * .8) * (1 - t * .4);
                var y0 = flash.Y - 2 - SeededRandom(s * 13 + 5) * 5 - t * radius * .9;
                dc.DrawLine(streak, new Point(flash.X + ox, y0), new Point(flash.X + ox, y0 - len));
            }
        }
    }

    /// <summary>Deterministic 0-1 pseudo-random from an integer seed; keeps procedural effects stable between frames.</summary>
    private static double SeededRandom(int seed) { var x = Math.Sin(seed * 127.1 + 311.7) * 43758.5453; return x - Math.Floor(x); }

    /// <summary>Splash/melt droplet: a solid bead of liquid keeping its color, with a specular dot.</summary>
    private void DrawDroplet(DrawingContext dc, Spark p, double fade, double bloom)
    {
        var alpha = Alpha(245 * Math.Pow(fade, .8) * _visual.ParticleGlow / 100);
        if (alpha < 4) return;
        var size = p.Size * (.7 + fade * .5);
        var stretch = 1 + Math.Clamp(Math.Abs(p.Vy) / 700, 0, 1.2);
        dc.DrawEllipse(Brush(Color.FromArgb((byte)(alpha * .3), p.Color.R, p.Color.G, p.Color.B)), null, new Point(p.X, p.Y), size * 2 * bloom, size * 2 * stretch * bloom);
        dc.DrawEllipse(Brush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B)), null, new Point(p.X, p.Y), size, size * stretch);
        dc.DrawEllipse(Brush(Color.FromArgb(alpha, 255, 255, 255)), null, new Point(p.X - size * .3, p.Y - size * .35 * stretch), size * .32, size * .32);
    }

    /// <summary>Confetti: a tumbling paper rectangle keeping its bright color.</summary>
    private void DrawConfetti(DrawingContext dc, Spark p, double fade)
    {
        var alpha = Alpha(245 * Math.Min(1, fade * 2) * _visual.ParticleGlow / 100);
        if (alpha < 4) return;
        var w = p.Size * 1.7; var h = p.Size * 1.1 * (.35 + .65 * Math.Abs(Math.Sin(p.Age * 9 + p.Phase)));
        dc.PushTransform(new TranslateTransform(p.X, p.Y));
        dc.PushTransform(new RotateTransform((p.Phase * 57.3 + p.Age * 260) % 360));
        dc.DrawRectangle(Brush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B)), null, new Rect(-w / 2, -h / 2, w, h));
        dc.Pop(); dc.Pop();
    }

    /// <summary>Dust: a large soft gray puff that grows as it drifts.</summary>
    private void DrawDust(DrawingContext dc, Spark p, double fade, double bloom)
    {
        var alpha = Alpha(120 * fade * _visual.ParticleGlow / 100);
        if (alpha < 4) return;
        var size = p.Size * (1.5 + (1 - fade) * 2.2);
        dc.DrawEllipse(Brush(Color.FromArgb((byte)(alpha * .4), p.Color.R, p.Color.G, p.Color.B)), null, new Point(p.X, p.Y), size * 2.4 * bloom, size * 2.4 * bloom);
        dc.DrawEllipse(Brush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B)), null, new Point(p.X, p.Y), size, size);
    }

    /// <summary>Shatter: an angular glass shard tumbling away from the hit point.</summary>
    private void DrawShard(DrawingContext dc, Spark p, double fade)
    {
        var alpha = Alpha(255 * fade * _visual.ParticleGlow / 100);
        if (alpha < 4) return;
        var len = p.Size * 2.4; var w = p.Size * .8;
        dc.PushTransform(new TranslateTransform(p.X, p.Y));
        dc.PushTransform(new RotateTransform((p.Phase * 57.3 + p.Age * 420) % 360));
        var shard = new StreamGeometry();
        using (var ctx = shard.Open())
        {
            ctx.BeginFigure(new Point(-len / 2, 0), true, true);
            ctx.LineTo(new Point(len / 2, -w / 2), true, false);
            ctx.LineTo(new Point(len / 2, w / 2), true, false);
        }
        shard.Freeze();
        dc.DrawGeometry(Brush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B)), null, shard);
        var glint = Pen(Color.FromArgb(alpha, 255, 255, 255), 1);
        dc.DrawLine(glint, new Point(-len / 2, 0), new Point(len / 2, 0));
        dc.Pop(); dc.Pop();
    }

    /// <summary>Absorb morph: a ring collapsing into the key as it sucks the note in.</summary>
    private void DrawImplode(DrawingContext dc, Ring ring, double intensity)
    {
        var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
        var strength = Math.Clamp(.5 + ring.Strength * .5, 0, 1.2);
        var radius = (34 + _visual.RingSize * 1.1) * (1 - t) + 3;
        var fade = (1 - t) * intensity * strength;
        if (fade <= .01) return;
        var (r, g, b) = (ring.Color.R, ring.Color.G, ring.Color.B);
        var pen = Pen(Color.FromArgb(Alpha(235 * fade), r, g, b), Math.Max(.6, 2.6 * (1 - t) + .6));
        dc.DrawEllipse(null, pen, new Point(ring.X, ring.Y), radius, radius * .34);
        var suck = Pen(Color.FromArgb(Alpha(150 * fade), 255, 255, 255), 1.2);
        dc.DrawEllipse(null, suck, new Point(ring.X, ring.Y), radius * .55, radius * .2);
    }

    /// <summary>Lightning flash style: a jagged bolt striking down onto the key.</summary>
    private void DrawLightning(DrawingContext dc, Flash flash, double intensity)
    {
        var t = Math.Clamp(flash.Age / flash.Life, 0, 1);
        var strength = Math.Clamp(.5 + flash.Strength * .5, 0, 1.2);
        var fade = (1 - t) * intensity * strength;
        if (fade <= .01) return;
        DrawBolt(dc, flash.X, Math.Max(0, flash.Y - 260 - _visual.RingSize * 2), flash.Y, fade);
    }

    /// <summary>One jagged lightning bolt between two heights; shared by the flash style and the storm layer.</summary>
    private void DrawBolt(DrawingContext dc, double x, double top, double bottom, double fade)
    {
        var seed = (int)(x * 13 + bottom);
        var points = new Point[9];
        for (var i = 0; i < 9; i++)
        {
            var y = top + (bottom - top) * i / 8;
            var jitter = i == 0 || i == 8 ? 0 : (SeededRandom(seed + i * 7) - .5) * 44;
            points[i] = new Point(x + jitter, y);
        }
        var bolt = new PathGeometry();
        bolt.Figures.Add(new PathFigure(points[0], points.Skip(1).Select(p => new LineSegment(p, true)), false));
        bolt.Freeze();
        var glowPen = Pen(Color.FromArgb(Alpha(120 * fade), 140, 180, 255), 5);
        var corePen = Pen(Color.FromArgb(Alpha(255 * fade), 235, 244, 255), 1.8);
        dc.DrawGeometry(null, glowPen, bolt);
        dc.DrawGeometry(null, corePen, bolt);
    }

    /// <summary>Plasma flash style: a crackling energy ball in violet and cyan.</summary>
    private void DrawPlasma(DrawingContext dc, Flash flash, double intensity)
    {
        var t = Math.Clamp(flash.Age / flash.Life, 0, 1);
        var strength = Math.Clamp(.5 + flash.Strength * .5, 0, 1.2);
        var fade = (1 - t) * (1 - t) * intensity * strength;
        if (fade <= .01) return;
        var radius = 12 + t * (30 + _visual.RingSize);
        var core = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5, MappingMode = BrushMappingMode.RelativeToBoundingBox };
        core.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(235 * fade), 220, 240, 255), 0));
        core.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(170 * fade), 150, 110, 255), .45));
        core.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(110 * fade), 60, 220, 255), .75));
        core.GradientStops.Add(new GradientStop(Color.FromArgb(0, 60, 220, 255), 1));
        core.Freeze();
        dc.DrawEllipse(core, null, new Point(flash.X, flash.Y), radius * 1.3, radius * .8);
        var seed = (int)(flash.X * 7 + flash.Y * 3);
        for (var i = 0; i < 5; i++)
        {
            var a0 = SeededRandom(seed + i) * Math.PI * 2 + _elapsed * 6;
            var inner = radius * .5; var outer = radius * 1.5;
            var start = new Point(flash.X + Math.Cos(a0) * inner, flash.Y + Math.Sin(a0) * inner * .6);
            var mid = new Point(flash.X + Math.Cos(a0 + .5) * (inner + outer) / 2 + (SeededRandom(seed + i + 40) - .5) * 14,
                flash.Y + Math.Sin(a0 + .5) * (inner + outer) / 2 * .6);
            var end = new Point(flash.X + Math.Cos(a0 + .9) * outer, flash.Y + Math.Sin(a0 + .9) * outer * .6);
            var arc = new PathGeometry();
            arc.Figures.Add(new PathFigure(start, [new LineSegment(mid, true), new LineSegment(end, true)], false));
            arc.Freeze();
            var pen = Pen(Color.FromArgb(Alpha(200 * fade), 190, 220, 255), 1.4);
            dc.DrawGeometry(null, pen, arc);
        }
    }

    /// <summary>Morph channel, star: the note head pops into a pointed star at the hit point.</summary>
    private void DrawStarMorph(DrawingContext dc, Flash flash, double intensity)
    {
        var t = Math.Clamp(flash.Age / flash.Life, 0, 1);
        var strength = Math.Clamp(.5 + flash.Strength * .5, 0, 1.2);
        var fade = (1 - t) * intensity * strength;
        if (fade <= .01) return;
        var radius = (14 + _visual.RingSize * .9) * (.5 + .5 * t);
        var (r, g, b) = (flash.Color.R, flash.Color.G, flash.Color.B);
        var star = new StreamGeometry();
        using (var ctx = star.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var rad = i % 2 == 0 ? radius : radius * .45;
                var pt = new Point(flash.X + Math.Cos(angle) * rad, flash.Y + Math.Sin(angle) * rad * .8);
                if (i == 0) ctx.BeginFigure(pt, true, true); else ctx.LineTo(pt, true, false);
            }
        }
        star.Freeze();
        var edge = Pen(Color.FromArgb(Alpha(255 * fade), 255, 255, 255), 1.6);
        dc.DrawGeometry(Brush(Color.FromArgb(Alpha(200 * fade), r, g, b)), edge, star);
    }

    /// <summary>Falling phase, trail channel: Glow, Sparkles, Speed Lines, Blur, Ribbon, Rainbow or Stream behind the note.</summary>
    private void DrawFallingTrail(DrawingContext dc, Rect r, Color color, double opacity, int pitch, bool rising)
    {
        var trail = _visual.FallingTrail;
        if (trail == "None" || _visual.FallingTrailIntensity <= 0) return;
        var strength = _visual.FallingTrailIntensity / 100 * opacity;
        if (strength <= .01) return;
        var length = Math.Max(8, r.Height * _visual.FallingTrailLength / 100 + 10);
        // The trail drags behind the motion: above the note while falling, below it while rising.
        var trailRect = rising ? new Rect(r.X, r.Bottom, r.Width, length) : new Rect(r.X, r.Y - length, r.Width, length);
        var (cr, cg, cb) = (color.R, color.G, color.B);
        var cx = r.X + r.Width / 2;
        switch (trail)
        {
            case "Glow":
            {
                var fade = new LinearGradientBrush { StartPoint = new Point(0, rising ? 0 : 1), EndPoint = new Point(0, rising ? 1 : 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * strength), cr, cg, cb), 0));
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, cr, cg, cb), 1));
                fade.Freeze();
                dc.DrawRectangle(fade, null, trailRect);
                break;
            }
            case "Sparkles":
            {
                var count = Math.Clamp((int)(length / 9), 2, 14);
                for (var i = 0; i < count; i++)
                {
                    var f = (i + .5) / count;
                    var y = rising ? trailRect.Y + f * length : trailRect.Y + (1 - f) * length;
                    var x = r.X + SeededRandom(pitch * 31 + i * 7) * r.Width;
                    var twinkle = .35 + .65 * Math.Abs(Math.Sin(_elapsed * (3 + SeededRandom(pitch + i) * 5) + i * 1.7));
                    var a = Alpha(255 * strength * (1 - f) * twinkle);
                    if (a < 5) continue;
                    var s = 1 + SeededRandom(pitch * 57 + i * 13) * 2.2;
                    var c = SeededRandom(pitch * 91 + i) < .4 ? Colors.White : color;
                    dc.DrawEllipse(Brush(Color.FromArgb(a, c.R, c.G, c.B)), null, new Point(x, y), s, s);
                    if (s > 2)
                    {
                        var pen = Pen(Color.FromArgb((byte)(a * .7), c.R, c.G, c.B), 1);
                        dc.DrawLine(pen, new Point(x - s * 2, y), new Point(x + s * 2, y));
                        dc.DrawLine(pen, new Point(x, y - s * 2), new Point(x, y + s * 2));
                    }
                }
                break;
            }
            case "Speed Lines":
            {
                var lines = Math.Clamp((int)(r.Width / 5), 2, 6);
                for (var i = 0; i < lines; i++)
                {
                    var x = r.X + (i + .5) * r.Width / lines;
                    var scroll = (SeededRandom(pitch * 17 + i * 3) + _elapsed * 2.2) % 1;
                    var y0 = rising ? trailRect.Y + scroll * length * .5 : trailRect.Bottom - scroll * length * .5;
                    var len = length * (.35 + .3 * SeededRandom(pitch + i * 11));
                    var y1 = rising ? y0 + len : y0 - len;
                    var pen = Pen(Color.FromArgb(Alpha(190 * strength), 255, 255, 255), 1.4);
                    dc.DrawLine(pen, new Point(x, y0), new Point(x, y1));
                }
                break;
            }
            case "Blur": // Motion Blur: the body smears along its travel direction.
            {
                var smear = new Rect(r.X, rising ? r.Y : r.Y - length * .7, r.Width, r.Height + length * .7);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(70 * strength), cr, cg, cb)), null, smear, 4, 4);
                break;
            }
            case "Ribbon":
            {
                var steps = 16;
                var pts = new Point[steps + 1];
                for (var i = 0; i <= steps; i++)
                {
                    var f = i / (double)steps;
                    var y = rising ? trailRect.Y + f * length : trailRect.Bottom - f * length;
                    var sway = Math.Sin(f * 6.28 + _elapsed * 3 + pitch) * r.Width * .45 * f;
                    pts[i] = new Point(cx + sway, y);
                }
                var ribbon = new PathGeometry();
                ribbon.Figures.Add(new PathFigure(pts[0], pts.Skip(1).Select(p => new LineSegment(p, true)), false));
                ribbon.Freeze();
                var band = Pen(Color.FromArgb(Alpha(150 * strength), cr, cg, cb), Math.Max(2, r.Width * .5), roundCaps: true);
                var coreLine = Pen(Color.FromArgb(Alpha(200 * strength), 255, 255, 255), 1.2);
                dc.DrawGeometry(null, band, ribbon);
                dc.DrawGeometry(null, coreLine, ribbon);
                break;
            }
            case "Rainbow":
            {
                var bands = 7;
                for (var i = 0; i < bands; i++)
                {
                    var f0 = i / (double)bands; var f1 = (i + 1) / (double)bands;
                    var seg = rising ? new Rect(r.X, trailRect.Y + f0 * length, r.Width, length / bands + 1)
                        : new Rect(r.X, trailRect.Bottom - f1 * length, r.Width, length / bands + 1);
                    var c = ColorFromHue((_elapsed * 90 + i * 360.0 / bands + pitch * 9) % 360);
                    dc.DrawRectangle(Brush(Color.FromArgb(Alpha(170 * strength * (1 - f0 * .7)), c.R, c.G, c.B)), null, seg);
                }
                break;
            }
            case "Stream":
            {
                var count = Math.Clamp((int)(length / 7), 3, 18);
                for (var i = 0; i < count; i++)
                {
                    var speed = .5 + SeededRandom(pitch * 23 + i * 5) * 1.2;
                    var f = (SeededRandom(pitch * 41 + i * 3) + _elapsed * speed * .4) % 1;
                    var y = rising ? trailRect.Y + f * length : trailRect.Bottom - f * length;
                    var x = cx + (SeededRandom(pitch * 13 + i * 29) - .5) * r.Width * 1.2;
                    var a = Alpha(230 * strength * (1 - f * .8));
                    if (a < 5) continue;
                    var s = 1 + SeededRandom(pitch * 71 + i) * 1.8;
                    dc.DrawEllipse(Brush(Color.FromArgb(a, cr, cg, cb)), null, new Point(x, y), s, s);
                }
                break;
            }
        }
    }

    /// <summary>Ghost Notes: faint echo copies leading the note along its travel direction.</summary>
    private void DrawNoteGhosts(DrawingContext dc, Rect r, Color color, double opacity, int pitch, bool rising)
    {
        var amount = _visual.FallingGhostAmount / 100 * opacity;
        if (amount <= .01) return;
        var count = amount > .66 ? 3 : amount > .33 ? 2 : 1;
        var step = 10 + r.Height * .12;
        for (var i = 1; i <= count; i++)
        {
            var offset = step * i;
            var ghost = rising ? new Rect(r.X, r.Y - offset, r.Width, r.Height) : new Rect(r.X, r.Y + offset, r.Width, r.Height);
            DrawConfiguredNote(dc, ghost, color, amount * .3 / i, false, pitch, rising, true);
        }
    }

    /// <summary>Breathing Glow: 1 normally, oscillating while held keys breathe (cache-safe: only radii and glow scale).</summary>
    private double BreathFactor() => _visual.HoldBreath ? .72 + .28 * Math.Sin(_elapsed * (1 + _visual.HoldBreathRate / 100 * 5)) : 1;

    /// <summary>Hold Bar: the sounding bar burns brighter with a hot outline while the key is held.</summary>
    private void DrawHoldBar(DrawingContext dc, Rect r, Color color, double opacity)
    {
        var strength = _visual.HoldBarIntensity / 100 * opacity;
        if (strength <= .01) return;
        var rim = Pen(Color.FromArgb(Alpha(255 * strength), 255, 255, 255), 2);
        dc.DrawRoundedRectangle(null, rim, Inflate(r, 1.5), 5, 5);
        var halo = Pen(Color.FromArgb(Alpha(120 * strength), color.R, color.G, color.B), 5);
        dc.DrawRoundedRectangle(null, halo, Inflate(r, 3.5), 7, 7);
    }

    /// <summary>Hold phase, link channel: crackling arcs chaining simultaneously held keys (up to 6 links).</summary>
    private void DrawElectricArcs(DrawingContext dc, double width, double keyTop)
    {
        var strength = _visual.HoldArcIntensity / 100;
        if (strength <= .01) return;
        int prev = -1, pairs = 0;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount && pairs < 6; pitch++)
        {
            if (!_activeKey[pitch]) continue;
            if (prev >= 0) { DrawArc(dc, KeyCenters[prev] * width, KeyCenters[pitch] * width, keyTop, strength, pairs); pairs++; }
            prev = pitch;
        }
    }

    private void DrawArc(DrawingContext dc, double x0, double x1, double keyTop, double strength, int pair)
    {
        const int segs = 10;
        var lift = 26 + Math.Abs(x1 - x0) * .12;
        var pts = new Point[segs + 1];
        for (var i = 0; i <= segs; i++)
        {
            var f = i / (double)segs;
            pts[i] = new Point(x0 + (x1 - x0) * f,
                keyTop - 4 - Math.Sin(f * Math.PI) * lift + (SeededRandom(pair * 131 + i * 17 + (int)(_elapsed * 24)) - .5) * 16);
        }
        var arc = new PathGeometry();
        arc.Figures.Add(new PathFigure(pts[0], pts.Skip(1).Select(p => new LineSegment(p, true)), false));
        arc.Freeze();
        var glow = Pen(Color.FromArgb(Alpha(130 * strength), 130, 180, 255), 4);
        var corePen = Pen(Color.FromArgb(Alpha(255 * strength), 230, 242, 255), 1.5);
        dc.DrawGeometry(null, glow, arc);
        dc.DrawGeometry(null, corePen, arc);
    }

    /// <summary>Water Ripple wave style: flat expanding ellipse rings like rain on a pond.</summary>
    private void DrawRipple(DrawingContext dc, Ring ring, double intensity)
    {
        var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
        var strength = Math.Clamp(.5 + ring.Strength * .5, 0, 1.2);
        var progress = 1.0 - Math.Exp(-3.4 * t);
        var radius = (8 + progress * (30 + _visual.RingSize * 1.4)) * (.7 + .3 * ring.Strength);
        for (var i = 0; i < 3; i++)
        {
            var f = t - i * .12;
            if (f < 0) continue;
            var fade = Math.Pow(1 - f, 1.8) * intensity * strength * (1 - i * .25);
            if (fade <= .01) continue;
            var rr = radius * (1 - i * .22);
            var pen = Pen(Color.FromArgb(Alpha(215 * fade), 170, 215, 255), Math.Max(.6, 2 * (1 - f) + .4));
            dc.DrawEllipse(null, pen, new Point(ring.X, ring.Y), rr, rr * .3);
        }
    }

    /// <summary>Ambient layer, Particle &amp; Energy family: storm, lasers, confetti rain or fireworks behind the notes.</summary>
    private void DrawAmbientEnergy(DrawingContext dc, double width, double height)
    {
        var amount = _visual.AmbientEnergyAmount / 100;
        var speed = .25 + _visual.AmbientEnergySpeed / 100 * 1.75;
        if (amount <= .01) return;
        switch (_visual.AmbientEnergy)
        {
            case "Lightning Storm":
            {
                // Strikes roll across the stage every few seconds at seeded positions.
                var period = 3.2 / speed;
                var cycle = _elapsed / period;
                for (var k = 0; k < 2; k++)
                {
                    var f = cycle + k * .5 - Math.Floor(cycle + k * .5);
                    if (f > .3) continue;
                    var strike = (int)Math.Floor(cycle + k * .5);
                    var x = SeededRandom(strike * 3 + 11) * width;
                    DrawBolt(dc, x, 0, height * (.55 + SeededRandom(strike * 7 + 5) * .3), (1 - f / .3) * amount);
                }
                break;
            }
            case "Laser Beams":
            {
                var beams = 2 + (int)(amount * 3.99);
                for (var i = 0; i < beams; i++)
                {
                    var sweep = Math.Sin(_elapsed * speed * (0.5 + i * .23) + i * 2.4);
                    var x0 = width * (.15 + .7 * (i + .5) / beams) + sweep * width * .18;
                    var x1 = width * (.5 + Math.Sin(_elapsed * speed * .7 + i * 1.3) * .4);
                    var c = ColorFromHue(i * 360.0 / beams + _elapsed * 20);
                    var glow = Pen(Color.FromArgb(Alpha(90 * amount), c.R, c.G, c.B), 6);
                    var core = Pen(Color.FromArgb(Alpha(220 * amount), 255, 255, 255), 1.6);
                    dc.DrawLine(glow, new Point(x0, -10), new Point(x1, height));
                    dc.DrawLine(core, new Point(x0, -10), new Point(x1, height));
                }
                // Extra bolts firing out of the currently sounding keys.
                var fired = 0;
                for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount && fired < 8; pitch++)
                {
                    if (!_activeKey[pitch]) continue;
                    fired++;
                    var x = KeyCenters[pitch] * width;
                    var c = KeyColor(pitch);
                    var beam = Pen(Color.FromArgb(Alpha(200 * amount), c.R, c.G, c.B), 2.5);
                    dc.DrawLine(beam, new Point(x, height), new Point(x + Math.Sin(_elapsed * 9 + pitch) * 8, height * .15));
                }
                break;
            }
            case "Confetti Rain":
            {
                var count = (int)(30 + amount * 120);
                for (var i = 0; i < count; i++)
                {
                    var fall = (SeededRandom(i * 3 + 1) + _elapsed * speed * (.12 + SeededRandom(i * 5 + 2) * .2)) % 1;
                    var x = SeededRandom(i * 7 + 3) * width + Math.Sin(_elapsed * 2 + i) * 12;
                    var y = fall * (height + 40) - 20;
                    var c = ColorFromHue(SeededRandom(i * 11 + 4) * 360);
                    dc.PushTransform(new TranslateTransform(x, y));
                    dc.PushTransform(new RotateTransform((SeededRandom(i) * 360 + _elapsed * 120 * (SeededRandom(i + 50) > .5 ? 1 : -1)) % 360));
                    dc.DrawRectangle(Brush(Color.FromArgb(Alpha(230 * amount), c.R, c.G, c.B)), null, new Rect(-3, -2, 6, 4));
                    dc.Pop(); dc.Pop();
                }
                break;
            }
            case "Fireworks":
            {
                // Rockets launch, pop, and rain colored sparks — all on seeded cycles.
                var rockets = 1 + (int)(amount * 3.99);
                for (var r = 0; r < rockets; r++)
                {
                    var period = (2.6 + SeededRandom(r * 13 + 1) * 2.4) / speed;
                    var f = (_elapsed / period + SeededRandom(r * 17 + 2)) % 1;
                    var x = width * (.12 + .76 * SeededRandom(r * 19 + 3));
                    var topY = height * (.15 + SeededRandom(r * 23 + 4) * .3);
                    var c = ColorFromHue(SeededRandom(r * 29 + 5) * 360);
                    if (f < .35)
                    {
                        var y = height + 10 - (height + 10 - topY) * f / .35;
                        var trail = Pen(Color.FromArgb(Alpha(200 * amount), 255, 220, 150), 2);
                        dc.DrawLine(trail, new Point(x, y), new Point(x, y + 26));
                        dc.DrawEllipse(Brush(Color.FromArgb(Alpha(255 * amount), 255, 255, 255)), null, new Point(x, y), 2.5, 2.5);
                    }
                    else
                    {
                        var boom = (f - .35) / .65;
                        var dots = 26;
                        for (var i = 0; i < dots; i++)
                        {
                            var a = i / (double)dots * Math.PI * 2 + r;
                            var dist = boom * (46 + SeededRandom(r * 31 + i) * 60);
                            var px = x + Math.Cos(a) * dist;
                            var py = topY + Math.Sin(a) * dist * .8 + boom * boom * 90;
                            var a2 = Alpha(255 * amount * (1 - boom));
                            if (a2 < 5) continue;
                            dc.DrawEllipse(Brush(Color.FromArgb(a2, c.R, c.G, c.B)), null, new Point(px, py), 2.2, 2.2);
                        }
                        if (boom < .25)
                            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(200 * amount * (1 - boom * 4)), 255, 255, 255)), null, new Point(x, topY), 10, 10);
                    }
                }
                break;
            }
        }
    }

    /// <summary>Ambient layer, Nature family: rain, snow, smoke, leaves, butterflies, dust or aurora.</summary>
    private void DrawAmbientNature(DrawingContext dc, double width, double height)
    {
        var amount = _visual.AmbientNatureAmount / 100;
        var speed = .25 + _visual.AmbientNatureSpeed / 100 * 1.75;
        if (amount <= .01) return;
        switch (_visual.AmbientNature)
        {
            case "Rain":
            {
                var count = (int)(40 + amount * 160);
                var pen = Pen(Color.FromArgb(Alpha(150 * amount), 150, 190, 235), 1.2);
                for (var i = 0; i < count; i++)
                {
                    var fall = (SeededRandom(i * 3 + 7) + _elapsed * speed * (.5 + SeededRandom(i * 5 + 1) * .5)) % 1;
                    var x = SeededRandom(i * 7 + 2) * (width + 100) - 50;
                    var y = fall * (height + 40) - 20;
                    dc.DrawLine(pen, new Point(x, y), new Point(x - 7, y + 16));
                }
                break;
            }
            case "Snow":
            {
                var count = (int)(30 + amount * 120);
                for (var i = 0; i < count; i++)
                {
                    var fall = (SeededRandom(i * 3 + 9) + _elapsed * speed * (.05 + SeededRandom(i * 5 + 3) * .08)) % 1;
                    var x = SeededRandom(i * 7 + 4) * width + Math.Sin(_elapsed * (0.6 + SeededRandom(i) * 1.2) + i * 1.7) * 26;
                    var y = fall * (height + 30) - 15;
                    var s = 1 + SeededRandom(i * 11 + 6) * 2.4;
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(225 * amount), 240, 246, 255)), null, new Point(x, y), s, s);
                }
                break;
            }
            case "Smoke":
            {
                var puffs = 4 + (int)(amount * 8);
                for (var i = 0; i < puffs; i++)
                {
                    var drift = (SeededRandom(i * 13 + 1) + _elapsed * speed * .02 * (SeededRandom(i * 17 + 2) > .5 ? 1 : -1)) % 1;
                    if (drift < 0) drift += 1;
                    var x = drift * (width + 400) - 200;
                    var y = height * (.35 + SeededRandom(i * 19 + 3) * .5);
                    var s = 90 + SeededRandom(i * 23 + 4) * 150;
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(26 * amount), 170, 170, 185)), null, new Point(x, y), s, s * .42);
                }
                break;
            }
            case "Leaves":
            {
                var count = (int)(12 + amount * 40);
                for (var i = 0; i < count; i++)
                {
                    var fall = (SeededRandom(i * 3 + 5) + _elapsed * speed * (.06 + SeededRandom(i * 5 + 8) * .1)) % 1;
                    var sway = Math.Sin(_elapsed * (1 + SeededRandom(i * 7 + 1) * 2) + i * 2.2);
                    var x = SeededRandom(i * 11 + 2) * width + sway * 60 * fall;
                    var y = fall * (height + 40) - 20;
                    var autumn = SeededRandom(i * 13 + 6);
                    var c = autumn < .4 ? Color.FromRgb(235, 140, 60) : autumn < .7 ? Color.FromRgb(220, 90, 70) : Color.FromRgb(150, 190, 90);
                    dc.PushTransform(new TranslateTransform(x, y));
                    dc.PushTransform(new RotateTransform((_elapsed * (40 + SeededRandom(i) * 80) + i * 40) % 360));
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(235 * amount), c.R, c.G, c.B)), null, new Point(0, 0), 5, 2.6);
                    dc.Pop(); dc.Pop();
                }
                break;
            }
            case "Butterflies":
            {
                var count = 2 + (int)(amount * 8);
                for (var i = 0; i < count; i++)
                {
                    var t = _elapsed * speed * (.3 + SeededRandom(i * 3 + 2) * .3) + SeededRandom(i * 5 + 4) * 10;
                    var x = width * (.5 + .38 * Math.Sin(t * .7 + i * 2.1));
                    var y = height * (.45 + .3 * Math.Sin(t * 1.1 + i * 1.3));
                    var flap = Math.Abs(Math.Sin(_elapsed * 14 + i * 2));
                    var wing = 3 + flap * 7;
                    var c = ColorFromHue(SeededRandom(i * 7 + 8) * 360);
                    var wingBrush = Brush(Color.FromArgb(Alpha(235 * amount), c.R, c.G, c.B));
                    dc.DrawEllipse(wingBrush, null, new Point(x - wing * .7, y), wing, wing * .55);
                    dc.DrawEllipse(wingBrush, null, new Point(x + wing * .7, y), wing, wing * .55);
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(235 * amount), 40, 30, 50)), null, new Point(x, y), 1.6, 3.2);
                }
                break;
            }
            case "Dust":
            {
                var count = (int)(16 + amount * 60);
                for (var i = 0; i < count; i++)
                {
                    var drift = (SeededRandom(i * 3 + 3) + _elapsed * speed * .03 * (SeededRandom(i * 5 + 5) + .3)) % 1;
                    var x = SeededRandom(i * 7 + 7) * width + Math.Sin(_elapsed * .5 + i) * 20;
                    var y = drift * (height + 60) - 30;
                    var s = 2 + SeededRandom(i * 11 + 1) * 5;
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(60 * amount), 210, 190, 160)), null, new Point(x, y), s, s);
                }
                break;
            }
            case "Aurora":
            {
                var bands = 3 + (int)(amount * 4);
                for (var i = 0; i < bands; i++)
                {
                    var cx = width * (i + .5) / bands + Math.Sin(_elapsed * speed * .4 + i * 1.8) * width * .06;
                    var w = width / bands * (.5 + SeededRandom(i * 7 + 1) * .5);
                    var top = height * .05;
                    var bottom = height * (.45 + SeededRandom(i * 11 + 2) * .25);
                    var c = ColorFromHue(140 + SeededRandom(i * 13 + 3) * 140 + Math.Sin(_elapsed * speed * .3 + i) * 20);
                    var curtain = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
                    curtain.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0));
                    curtain.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(110 * amount), c.R, c.G, c.B), .55));
                    curtain.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * amount), c.R, c.G, c.B), .85));
                    curtain.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                    curtain.Freeze();
                    const int steps = 12;
                    var pts = new Point[(steps + 1) * 2];
                    for (var s = 0; s <= steps; s++)
                    {
                        var f = s / (double)steps;
                        pts[s] = new Point(cx - w / 2 + Math.Sin(f * 5 + _elapsed * speed + i * 2) * w * .18 * f, top + (bottom - top) * f);
                        pts[(steps + 1) * 2 - 1 - s] = new Point(cx + w / 2 + Math.Sin(f * 5 + _elapsed * speed + i * 2 + .8) * w * .18 * f, top + (bottom - top) * f);
                    }
                    var rays = new PathGeometry();
                    rays.Figures.Add(new PathFigure(pts[0], pts.Skip(1).Select(p => new LineSegment(p, true)), true));
                    rays.Freeze();
                    dc.DrawGeometry(curtain, null, rays);
                }
                break;
            }
        }
    }

    /// <summary>Ambient layer, Light &amp; Color family: gradient waves, prisms or color splashes.</summary>
    private void DrawAmbientLight(DrawingContext dc, double width, double height)
    {
        var amount = _visual.AmbientLightAmount / 100;
        var speed = .25 + _visual.AmbientLightSpeed / 100 * 1.75;
        if (amount <= .01) return;
        var tint = AdjustColor(ParseColor(_visual.AmbientLightColor, ColorFromHue(266)));
        switch (_visual.AmbientLight)
        {
            case "Gradient Wave":
            {
                var bands = 5;
                for (var i = 0; i < bands; i++)
                {
                    var f = (i / (double)bands + _elapsed * speed * .08) % 1;
                    var c = ColorFromHue(_elapsed * speed * 30 + i * 360.0 / bands);
                    dc.DrawRectangle(Brush(Color.FromArgb(Alpha(70 * amount), c.R, c.G, c.B)), null, new Rect(0, f * height - 30, width, 60));
                }
                var wash = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
                wash.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(50 * amount), tint.R, tint.G, tint.B), 0));
                wash.GradientStops.Add(new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1));
                wash.Freeze();
                dc.DrawRectangle(wash, null, new Rect(0, 0, width, height));
                break;
            }
            case "Prism":
            {
                var cx = width * .5; var cy = height * .34;
                var rays = 3 + (int)(amount * 4);
                for (var i = 0; i < rays; i++)
                {
                    var a = _elapsed * speed * .5 + i * Math.PI * 2 / rays;
                    var c = ColorFromHue(i * 360.0 / rays);
                    var beam = Pen(Color.FromArgb(Alpha(170 * amount), c.R, c.G, c.B), 3);
                    dc.DrawLine(beam, new Point(cx, cy), new Point(cx + Math.Cos(a) * width, cy + Math.Sin(a) * width));
                }
                dc.DrawEllipse(Brush(Color.FromArgb(Alpha(220 * amount), tint.R, tint.G, tint.B)), null, new Point(cx, cy), 12, 12);
                dc.DrawEllipse(Brush(Color.FromArgb(Alpha(255 * amount), 255, 255, 255)), null, new Point(cx, cy), 4.5, 4.5);
                break;
            }
            case "Color Splash":
            {
                var splats = 3 + (int)(amount * 7);
                for (var i = 0; i < splats; i++)
                {
                    var period = (3 + SeededRandom(i * 7 + 1) * 4) / speed;
                    var f = (_elapsed / period + SeededRandom(i * 11 + 2)) % 1;
                    var fade = Math.Sin(f * Math.PI);
                    var x = SeededRandom(i * 13 + 3) * width;
                    var y = SeededRandom(i * 17 + 4) * height;
                    var c = ColorFromHue(SeededRandom(i * 19 + 5) * 360);
                    var s = (14 + SeededRandom(i * 23 + 6) * 30) * (.4 + .6 * fade);
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(150 * amount * fade), c.R, c.G, c.B)), null, new Point(x, y), s, s * .7);
                    for (var d = 0; d < 5; d++)
                    {
                        var a = SeededRandom(i * 31 + d) * Math.PI * 2;
                        var dist = s * (1.1 + SeededRandom(i * 37 + d * 3) * .9);
                        dc.DrawEllipse(Brush(Color.FromArgb(Alpha(170 * amount * fade), c.R, c.G, c.B)), null,
                            new Point(x + Math.Cos(a) * dist, y + Math.Sin(a) * dist * .7), 2.5, 2.5);
                    }
                }
                break;
            }
        }
    }

    /// <summary>Ambient layer, Cosmic family: galaxy, black hole, matrix rain, geometric shapes or fractals.</summary>
    private void DrawAmbientCosmic(DrawingContext dc, double width, double height)
    {
        var amount = _visual.AmbientCosmicAmount / 100;
        var speed = .25 + _visual.AmbientCosmicSpeed / 100 * 1.75;
        if (amount <= .01) return;
        switch (_visual.AmbientCosmic)
        {
            case "Galaxy":
            {
                var cx = width * .5; var cy = height * .4;
                var maxR = Math.Min(width, height) * .45;
                var stars = (int)(120 + amount * 380);
                for (var i = 0; i < stars; i++)
                {
                    var f = SeededRandom(i * 3 + 1);
                    var r = f * maxR;
                    var a = f * 9 + (i % 3) * Math.PI * 2 / 3 + _elapsed * speed * .25;
                    var spread = (SeededRandom(i * 5 + 2) - .5) * (8 + f * 46);
                    var x = cx + Math.Cos(a) * r + Math.Cos(a + 1.57) * spread * .3;
                    var y = cy + Math.Sin(a) * r * .62 + Math.Sin(a + 1.57) * spread * .3;
                    var c = Blend(Color.FromRgb(255, 230, 200), ColorFromHue(210 + f * 90), f);
                    var twinkle = .5 + .5 * Math.Sin(_elapsed * (2 + SeededRandom(i * 7 + 3) * 4) + i);
                    var s = .8 + SeededRandom(i * 11 + 4) * 1.8 + (1 - f) * 1.2;
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(230 * amount * (.35 + .65 * twinkle)), c.R, c.G, c.B)), null, new Point(x, y), s, s);
                }
                dc.DrawEllipse(Brush(Color.FromArgb(Alpha(120 * amount), 255, 240, 220)), null, new Point(cx, cy), 22, 14);
                break;
            }
            case "Black Hole":
            {
                var cx = width * .5; var cy = height * .38;
                var r = Math.Min(width, height) * .13 * (.8 + amount * .4);
                for (var i = 0; i < 3; i++)
                {
                    var rr = r * (1.5 + i * .55 + Math.Sin(_elapsed * speed * 2 + i) * .06);
                    var c = i == 0 ? Color.FromRgb(255, 200, 130) : i == 1 ? Color.FromRgb(255, 140, 90) : Color.FromRgb(170, 90, 220);
                    var ring = Pen(Color.FromArgb(Alpha((170 - i * 45) * amount), c.R, c.G, c.B), 7 - i * 1.8);
                    dc.DrawEllipse(null, ring, new Point(cx, cy), rr, rr * .38);
                }
                dc.DrawEllipse(Brush(Colors.Black), null, new Point(cx, cy), r, r * .62);
                var rim = Pen(Color.FromArgb(Alpha(255 * amount), 255, 240, 220), 1.6);
                dc.DrawEllipse(null, rim, new Point(cx, cy), r, r * .62);
                for (var i = 0; i < 24; i++)
                {
                    var f = (SeededRandom(i * 3 + 5) + _elapsed * speed * (.2 + SeededRandom(i * 5 + 1) * .3)) % 1;
                    var a = f * 12 + i;
                    var rr = r * 3.2 * (1 - f) + r * .8;
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(220 * amount * (1 - f * .5)), 255, 220, 180)), null,
                        new Point(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr * .4), 1.6, 1.6);
                }
                break;
            }
            case "Matrix Rain":
            {
                var cols = (int)(6 + amount * 18);
                for (var i = 0; i < cols; i++)
                {
                    var x = (i + .5) * width / cols;
                    var fall = (SeededRandom(i * 7 + 1) + _elapsed * speed * (.25 + SeededRandom(i * 11 + 2) * .4)) % 1;
                    var headY = fall * (height + 100) - 50;
                    var tail = 6 + (int)(SeededRandom(i * 13 + 3) * 10);
                    for (var j = 0; j < tail; j++)
                    {
                        var y = headY - j * 16;
                        if (y < -20 || y > height + 20) continue;
                        var head = j == 0;
                        if (head || j % 4 == 0)
                        {
                            var code = (char)(0x30A0 + (int)(SeededRandom(i * 17 + j * 3 + (int)(_elapsed * speed * 3)) * 96));
                            var g = head ? 255 : 200;
                            DrawLabel(dc, code.ToString(), new Point(x, y), 13,
                                Color.FromArgb(Alpha((head ? 255 : 170) * amount), head ? (byte)220 : (byte)60, (byte)g, head ? (byte)220 : (byte)90), false);
                        }
                        else
                            dc.DrawRectangle(Brush(Color.FromArgb(Alpha(120 * amount * (1 - j / (double)tail)), 40, 200, 90)), null, new Rect(x - 4, y - 7, 8, 12));
                    }
                }
                break;
            }
            case "Geometric":
            {
                var cx = width * .5; var cy = height * .4;
                var shapes = 2 + (int)(amount * 3);
                for (var i = 0; i < shapes; i++)
                {
                    var sides = 3 + (i % 4);
                    var rr = (30 + i * 34) * (.7 + amount * .5);
                    var rot = _elapsed * speed * (.3 + i * .17) * (i % 2 == 0 ? 1 : -1) + i;
                    var c = ColorFromHue(i * 360.0 / shapes + _elapsed * 10);
                    var pen = Pen(Color.FromArgb(Alpha(200 * amount), c.R, c.G, c.B), 1.8);
                    var fig = new PathFigure();
                    for (var s = 0; s <= sides; s++)
                    {
                        var a = rot + s / (double)sides * Math.PI * 2;
                        var pt = new Point(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr * .8);
                        if (s == 0) fig.StartPoint = pt; else fig.Segments.Add(new LineSegment(pt, true));
                    }
                    var geo = new PathGeometry();
                    geo.Figures.Add(fig);
                    geo.Freeze();
                    dc.DrawGeometry(null, pen, geo);
                    var sa = -rot * 1.7;
                    dc.DrawEllipse(Brush(Color.FromArgb(Alpha(255 * amount), 255, 255, 255)), null,
                        new Point(cx + Math.Cos(sa) * rr, cy + Math.Sin(sa) * rr * .8), 2.5, 2.5);
                }
                break;
            }
            case "Fractal":
            {
                var size = Math.Min(width, height) * .4 * (.6 + amount * .6);
                var pulse = 1 + Math.Sin(_elapsed * speed * 1.5) * .04;
                DrawSierpinski(dc, new Point(width * .5, height * .42 - size * .55 * pulse), size * pulse, 0,
                    ColorFromHue(_elapsed * 12), ColorFromHue(_elapsed * 12 + 140), amount);
                break;
            }
        }
    }

    /// <summary>Sierpinski triangle, 4 levels deep, hue-shifting between two colors.</summary>
    private void DrawSierpinski(DrawingContext dc, Point apex, double size, int depth, Color a, Color b, double amount)
    {
        if (depth >= 4)
        {
            var tri = new StreamGeometry();
            using (var ctx = tri.Open())
            {
                ctx.BeginFigure(apex, true, true);
                ctx.LineTo(new Point(apex.X - size / 2, apex.Y + size * .866), true, false);
                ctx.LineTo(new Point(apex.X + size / 2, apex.Y + size * .866), true, false);
            }
            tri.Freeze();
            dc.DrawGeometry(Brush(Color.FromArgb(Alpha(120 * amount), a.R, a.G, a.B)), null, tri);
            return;
        }
        var mid = Blend(a, b, depth / 4.0);
        var half = size / 2;
        DrawSierpinski(dc, apex, half, depth + 1, mid, b, amount);
        DrawSierpinski(dc, new Point(apex.X - half / 2, apex.Y + half * .866), half, depth + 1, mid, b, amount);
        DrawSierpinski(dc, new Point(apex.X + half / 2, apex.Y + half * .866), half, depth + 1, mid, b, amount);
    }

    private void DrawImpactLine(DrawingContext dc, double width, double y)
    {
        var baseColor = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var intensity = Math.Clamp(_visual.HaloIntensity / 100.0 * BeatBoost() * EnergyBoost() * PedalBoost(), 0.0, 2.0);
        if (intensity <= 0.001) return;

        var haloSpread = 6 + _visual.BloomSize / 5.0;
        var haloAlpha = Alpha(Math.Clamp((24 + _visual.BloomIntensity * .75) * intensity, 0, 255));

        // 1 · Volumetric bloom: a wide band whose alpha decays as a gaussian away from the line, so the
        //     glow reads as light scattering in air instead of a flat stripe.
        var glowHeight = haloSpread * 6;
        dc.DrawRectangle(FalloffBrush(baseColor, haloAlpha * .8, .5, 6.0, false), null, new Rect(0, y - glowHeight * .5, width, glowHeight));

        // 2 · Light spilling down onto the key tops just below the line, grounding the glow on the keyboard.
        var spill = haloSpread * 3;
        dc.DrawRectangle(FalloffBrush(baseColor, haloAlpha * .5, 0.0, 5.0, false), null, new Rect(0, y, width, spill));

        // 3 · Hot emissive core: a tight gaussian saturating to white at the line, tinted along the run.
        var coreHeight = Math.Max(3, haloSpread * .8);
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        rainbow.GradientStops.Add(new GradientStop(AdjustColor(NoteColor(28, 0)), 0));
        rainbow.GradientStops.Add(new GradientStop(Blend(baseColor, Colors.White, .5), .5));
        rainbow.GradientStops.Add(new GradientStop(AdjustColor(NoteColor(100, 0)), 1));
        rainbow.Freeze();
        var corePen = new Pen(rainbow, Math.Max(1.5, 1.2 * intensity)); corePen.Freeze();
        dc.DrawLine(corePen, new Point(0, y), new Point(width, y));
        dc.DrawRectangle(FalloffBrush(Colors.White, Math.Clamp(160 * intensity, 0, 255), .5, 26.0, false), null, new Rect(0, y - coreHeight * .5, width, coreHeight));

        // 4 · Sparkle grain riding the line: tiny white-hot specks that twinkle in place, the way a real
        //     emissive strip reads on camera instead of a clean vector line.
        var sparkleCount = (int)Math.Clamp(width / 12, 24, 180);
        for (var i = 0; i < sparkleCount; i++)
        {
            var twinkle = Math.Pow(.5 + .5 * Math.Sin(_elapsed * (2 + SeededRandom(i) * 7) + i * 2.4), 3);
            var sparkleAlpha = Alpha(200 * intensity * twinkle);
            if (sparkleAlpha < 6) continue;
            var sx = SeededRandom(i * 131 + 7) * width;
            var sy = y + (SeededRandom(i * 17 + 3) - .5) * 3.4;
            var sr = .7 + SeededRandom(i * 29 + 1) * 1.0;
            dc.DrawEllipse(Brush(Color.FromArgb(sparkleAlpha, 255, 255, 255)), null, new Point(sx, sy), sr, sr);
        }

        // 5 · Per-key photon excitation: a radial bloom stretched upward like rising light plus a soft
        //     contact shadow on the key bed; no hard cross lines.
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!_activeKey[pitch]) continue;
            var hitX = KeyCenters[pitch] * width;
            var hitColor = KeyColor(pitch);
            var hitAmount = Math.Clamp(_keyHeat[pitch] > 0 ? _keyHeat[pitch] : 1.0, 0.35, 1.0);
            var flareRadius = (16 + _visual.BloomSize * .35) * hitAmount * BreathFactor();

            var burstKey = GradientKey(12, Color.FromArgb((byte)pitch, hitColor.R, hitColor.G, hitColor.B));
            if (!_gradientCache.TryGetValue(burstKey, out var burstBrush))
            {
                var burst = new RadialGradientBrush { Center = new Point(.5, .62), GradientOrigin = new Point(.5, .62), RadiusX = .5, RadiusY = .5, MappingMode = BrushMappingMode.RelativeToBoundingBox };
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(200 * intensity * hitAmount), 255, 255, 255), 0.0));
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(140 * intensity * hitAmount), hitColor.R, hitColor.G, hitColor.B), 0.3));
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(50 * intensity * hitAmount), hitColor.R, hitColor.G, hitColor.B), 0.62));
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(0, hitColor.R, hitColor.G, hitColor.B), 1.0));
                burst.Freeze();
                burstBrush = CacheGradient(burstKey, burst);
            }
            // A tall soft bloom rising off the key, fading in every direction.
            dc.DrawEllipse(burstBrush, null, new Point(hitX, y - flareRadius * .55), flareRadius * 1.1, flareRadius * 1.9);
            // Contact shadow grounding the flare on the key bed.
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(60 * hitAmount), 0, 0, 0)), null, new Point(hitX, y + 3), flareRadius * .9, 4);
        }
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

    private Color KeyColor(int pitch)
    {
        var color = _visual.PressedKeyColorMode == "Fixed" ? ParseColor(_visual.PressedKeyColor, Color.FromRgb(247, 130, 255)) : _activeKeyColor[pitch];
        return AdjustColor(color);
    }

    private void DrawKeyboard(DrawingContext dc, double width, double height, double lane, double top)
    {
        var style = _visual.KeyboardStyle; var glass = style == "Glass"; var studio = style == "Studio";
        var halo = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        dc.DrawRectangle(Brush(Color.FromArgb(glass ? (byte)110 : (byte)248, 8, 8, 15)), null, new Rect(0, top, width, height - top));
        var whites = WhitePitches; var whiteWidth = width / whites.Length;
        var press = _visual.KeyPressDepth / 100 * 4;
        var glowRadius = _visual.KeyGlowRadius / 100;
        // Glow bleeding upward from every lit key, drawn before the keys so the key faces stay crisp.
        if (glowRadius > 0)
        {
            for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
            {
                if (!_activeKey[pitch]) continue;
                var color = KeyColor(pitch);
                var x = KeyCenters[pitch] * width;
                var breath = BreathFactor() * PedalBoost(); var radiusX = (14 + glowRadius * 70) * breath; var radiusY = (10 + glowRadius * 60) * breath;
                var key = GradientKey(4, Color.FromArgb((byte)Math.Clamp(_visual.KeyLighting, 0, 255), color.R, color.G, color.B));
                if (!_gradientCache.TryGetValue(key, out var glowBrush))
                {
                    var radial = new RadialGradientBrush { MappingMode = BrushMappingMode.RelativeToBoundingBox, Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
                    radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(70 + _visual.KeyLighting * 1.4), color.R, color.G, color.B), 0));
                    radial.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(20 + _visual.KeyLighting * .5), color.R, color.G, color.B), .45));
                    radial.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
                    radial.Freeze(); glowBrush = CacheGradient(key, radial);
                }
                dc.DrawEllipse(glowBrush, null, new Point(x, top + 2), radiusX, radiusY);
            }
        }
        var pedalGlow = PedalBoost(); var glowPen = Pen(Color.FromArgb((byte)Math.Clamp((30 + _visual.KeyLighting * .95) * pedalGlow, 0, 255), halo.R, halo.G, halo.B), (5 + _visual.BloomSize / 10) * pedalGlow);
        dc.DrawLine(glowPen, new Point(0, top + 1), new Point(width, top + 1));
        if (TryDrawShadedKeyboard(dc, width, height - top, top))
        {
            // The bake already carries its own shadows, occlusion and press animation, so only the
            // engraved note names and the felt strip are drawn on top of it.
            DrawKeyLabels(dc, whites, whiteWidth, height, Color.FromRgb(58, 60, 82));
            if (_visual.ShowKeyFelt) DrawFelt(dc, width, top);
            return;
        }
        if (_visual.ShowKeyShadow)
        {
            var shadowKey = GradientKey(5, Colors.Black);
            if (!_gradientCache.TryGetValue(shadowKey, out var shadow))
            {
                var gradient = new LinearGradientBrush(Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90); gradient.Freeze(); shadow = CacheGradient(shadowKey, gradient);
            }
            dc.DrawRectangle(shadow, null, new Rect(0, top + 4, width, Math.Min(18, (height - top) * .16)));
        }
        var whiteBrush = glass ? KeyWhiteGlass : studio ? KeyWhiteStudio : KeyWhite;
        var whiteEdge = Pen(glass ? Color.FromArgb(120, 210, 220, 255) : Color.FromArgb(170, 68, 72, 94), glass ? .8 : .7);
        for (var i = 0; i < whites.Length; i++)
        {
            var pitch = whites[i]; var rect = new Rect(i * whiteWidth, top + 5, whiteWidth - 1, height - top - 5);
            var active = _activeKey[pitch];
            if (active && _visual.AnimateKeys)
            {
                var color = KeyColor(pitch); var lit = Blend(color, Colors.White, .32);
                var pressed = new Rect(rect.X, rect.Y + press, rect.Width, rect.Height - press);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(60 + _visual.KeyLighting * 1.4), color.R, color.G, color.B)), null, Inflate(new Rect(rect.X - 4, top - 1, rect.Width + 8, rect.Height + 6), 2), 7, 7);
                dc.DrawRoundedRectangle(KeyLightBrush(color, lit), Pen(Color.FromArgb(255, lit.R, lit.G, lit.B), 1), pressed, 3, 3);
            }
            else
            {
                dc.DrawRoundedRectangle(whiteBrush, whiteEdge, rect, studio ? 2 : 1.4, studio ? 2 : 1.4);
                if (studio) dc.DrawRectangle(Brush(Color.FromArgb(60, 0, 0, 0)), null, new Rect(rect.X, rect.Bottom - 6, rect.Width, 6));
            }
            DrawKeyLabel(dc, pitch, rect.X + rect.Width / 2, whiteWidth, height, active && _visual.AnimateKeys,
                glass ? Color.FromRgb(200, 205, 225) : Color.FromRgb(77, 79, 102));
        }
        var blackBrush = glass ? KeyBlackGlass : studio ? KeyBlackStudio : KeyBlack;
        var blackEdge = Pen(Color.FromArgb(200, 72, 66, 96), .75);
        var blackWidth = whiteWidth * .52;
        var blackHeight = (height - top) * (.61 + _visual.KeyOverhang / 100 * .18);
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!IsBlack(pitch)) continue;
            var x = KeyCenters[pitch] * width - blackWidth * .5;
            var rect = new Rect(x, top + 4, blackWidth, blackHeight);
            if (studio) dc.DrawRoundedRectangle(Brush(Color.FromArgb(120, 0, 0, 0)), null, new Rect(rect.X - 1.5, rect.Y + 2, rect.Width + 3, rect.Height + 3), 3, 3);
            if (_activeKey[pitch] && _visual.AnimateKeys)
            {
                var color = KeyColor(pitch); var lit = Blend(color, Colors.White, .15);
                var pressed = new Rect(rect.X, rect.Y + press * .6, rect.Width, rect.Height - press * .6);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(110 + _visual.KeyLighting * .9), color.R, color.G, color.B)), null, Inflate(rect, 5), 7, 7);
                dc.DrawRoundedRectangle(Brush(lit), Pen(Colors.White, 1), pressed, 5, 5);
            }
            else
            {
                dc.DrawRoundedRectangle(blackBrush, blackEdge, rect, 3, 3);
                if (studio) dc.DrawLine(Pen(Color.FromArgb(70, 255, 255, 255), 1), new Point(rect.X + 2, rect.Y + 1.5), new Point(rect.Right - 2, rect.Y + 1.5));
            }
        }
        if (_visual.ShowKeyFelt) DrawFelt(dc, width, top);
    }

    private void DrawFelt(DrawingContext dc, double width, double top)
    {
        var felt = AdjustColor(ParseColor(_visual.KeyFeltColor, Color.FromRgb(196, 28, 74)));
        dc.DrawRectangle(Brush(Color.FromArgb(120, felt.R, felt.G, felt.B)), null, new Rect(0, top - 1, width, 7));
        dc.DrawRectangle(Brush(felt), null, new Rect(0, top + 1, width, 3));
    }

    /// <summary>Note names engraved on the white keys; shared by the flat and the ray-traced keyboard.</summary>
    private void DrawKeyLabels(DrawingContext dc, int[] whites, double whiteWidth, double height, Color idleColor)
    {
        for (var i = 0; i < whites.Length; i++)
        {
            var pitch = whites[i];
            DrawKeyLabel(dc, pitch, i * whiteWidth + (whiteWidth - 1) / 2, whiteWidth, height, _activeKey[pitch] && _visual.AnimateKeys, idleColor);
        }
    }

    private void DrawKeyLabel(DrawingContext dc, int pitch, double centerX, double whiteWidth, double height, bool active, Color idleColor)
    {
        if (_visual.KeyLabels != "All" && !(_visual.KeyLabels == "C" && pitch % 12 == 0)) return;
        if (_visual.KeyLabels == "All" && whiteWidth < 13) return;
        var size = Math.Clamp(whiteWidth * .48, 7, 11);
        var text = _visual.KeyLabels == "All" ? AllKeyLabels[ClampPitch(pitch)] : NoteLabel(pitch);
        DrawLabel(dc, text, new Point(centerX, height - 14), size, active ? Colors.White : idleColor, pitch % 12 == 0);
    }

    /// <summary>
    /// Draws the ray-traced keyboard: one cached bake of the whole bed, plus a small overlay tile per
    /// sounding key so a key press never re-shades all 88 keys. Returns false when the shader is switched
    /// off or fails, and the caller then draws the flat vector keyboard instead.
    /// </summary>
    private bool TryDrawShadedKeyboard(DrawingContext dc, double width, double bandHeight, double top)
    {
        if (_visual.BackgroundMode == "ChromaGreen" || !PianoKeyboardRenderer.IsEnabled(_visual.ShadingQuality) || width < 80 || bandHeight < 28)
        {
            IsShadedKeyboardActive = false; return false;
        }
        try
        {
            var dpi = Math.Max(1, _pixelsPerDip);
            var bandWidth = Math.Max(2, (int)Math.Ceiling(width * dpi));
            var bandPixels = Math.Max(2, (int)Math.Ceiling(bandHeight * dpi));
            var scene = PianoShaderScene.From(_visual, bandWidth, bandPixels, _visual.ShadingQuality);
            // The struct key mirrors every field of Signature(), so equality here is exactly
            // "the cached bake is still valid" — without building the signature string per frame.
            var sceneKey = PianoShaderScene.SceneKey.Capture(scene);
            // Render bakes at the quality's internal render scale, so the cached bitmap must be
            // compared against the scaled resolution, not the full band size.
            var bakeScale = Math.Clamp(scene.RenderScale, .25, 1);
            var bakedWidth = Math.Max(1, (int)Math.Ceiling(bandWidth * bakeScale));
            var bakedHeight = Math.Max(1, (int)Math.Ceiling(bandPixels * bakeScale));
            if (_shadedBase is null || !_hasShadedSceneKey || !sceneKey.Equals(_shadedSceneKey) || _shadedBase.PixelWidth != bakedWidth || _shadedBase.PixelHeight != bakedHeight)
            {
                var clock = Stopwatch.StartNew();
                var bake = PianoKeyboardRenderer.Render(scene, NoLights, 0, 0, bandWidth, bandPixels, -1);
                _shadedMilliseconds = clock.Elapsed.TotalMilliseconds;
                _shadedBakes++;
                if (bake is null) { IsShadedKeyboardActive = false; return false; }
                _shadedBase = bake; _shadedSceneKey = sceneKey; _hasShadedSceneKey = true; _shadedTiles.Clear(); _lastTile.Clear();
            }
            dc.DrawImage(_shadedBase, new Rect(0, top, width, bandHeight));
            DrawShadedLitKeys(dc, scene, width, bandHeight, top);
            IsShadedKeyboardActive = true;
            return true;
        }
        catch (Exception)
        {
            // A memory or imaging failure must never take the stage down; the vector keyboard takes over.
            _shadedBase = null; _shadedTiles.Clear(); _lastTile.Clear(); _hasShadedSceneKey = false; IsShadedKeyboardActive = false;
            return false;
        }
    }

    private void DrawShadedLitKeys(DrawingContext dc, PianoShaderScene scene, double width, double bandHeight, double top)
    {
        _keyLights.Clear();
        if (!_visual.AnimateKeys) return;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!_activeKey[pitch]) continue;
            _keyLights.Light(pitch, KeyColor(pitch), .55 + Math.Clamp(_keyHeat[pitch], 0, 1) * .45);
        }
        if (_keyLights.Pitches.Count == 0) return;
        // A fast rainbow passage can ask for a brand new tile every frame; the budget caps the work per
        // frame and the per-pitch fallback keeps those keys lit with their previous color instead of flickering.
        _tileBudget = 6;
        var bleed = Math.Max(2, (int)Math.Ceiling(2.5 / PianoShaderScene.WorldWidth * scene.BandWidth));
        foreach (var pitch in _keyLights.Pitches)
        {
            var color = KeyColor(pitch);
            var cacheKey = ((long)pitch << 20) | (uint)ColorBucket(color);
            if (_shadedTiles.TryGetValue(cacheKey, out var cached)) { dc.DrawImage(cached.Bitmap, cached.Where); continue; }
            if (_tileBudget <= 0)
            {
                if (_lastTile.TryGetValue(pitch, out var previous)) dc.DrawImage(previous.Bitmap, previous.Where);
                continue;
            }
            _tileBudget--;
            if (_shadedTiles.Count > 192) { _shadedTiles.Clear(); _lastTile.Clear(); }
            var center = PianoShaderScene.KeyCenterX[pitch] / PianoShaderScene.WorldWidth * scene.BandWidth;
            var x0 = Math.Clamp((int)Math.Floor(center - bleed), 0, scene.BandWidth - 1);
            var tileWidth = Math.Min(bleed * 2, scene.BandWidth - x0);
            _tileLights.Clear(); _tileLights.Light(pitch, color, 1);
            var bitmap = PianoKeyboardRenderer.Render(scene, _tileLights, x0, 0, tileWidth, scene.BandHeight, pitch);
            if (bitmap is null) continue;
            var tile = new ShadedKeyTile(bitmap, new Rect(x0 / (double)scene.BandWidth * width, top, tileWidth / (double)scene.BandWidth * width, bandHeight));
            _shadedTiles[cacheKey] = tile;
            if (_lastTile.Count > 128) _lastTile.Clear();
            _lastTile[pitch] = tile;
            dc.DrawImage(tile.Bitmap, tile.Where);
        }
    }

    /// <summary>Quantizes a note color to 4 bits per channel so the tile cache survives smoothly animated colors.</summary>
    private static int ColorBucket(Color color) => (color.R >> 4) << 8 | (color.G >> 4) << 4 | (color.B >> 4);

    private Brush KeyLightBrush(Color color, Color lit)    {
        var key = GradientKey(6, Color.FromArgb(255, color.R, color.G, color.B));
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush(Color.FromRgb(lit.R, lit.G, lit.B), Color.FromRgb(color.R, color.G, color.B), 90); gradient.Freeze();
        return CacheGradient(key, gradient);
    }

    private void DrawFlames(DrawingContext dc, double width, double keyTop, double lane)
    {
        var intensity = _visual.FlameIntensity / 100;
        var heightScale = .4 + _visual.FlameHeight / 100 * 1.4;
        var warm = _visual.FlameColorMode != "Note";
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var heat = _keyHeat[pitch];
            if (heat <= .015) continue;
            var x = KeyCenters[pitch] * width;
            var sway1 = Math.Sin(_elapsed * 13 + pitch * 2.7) * (2.0 + 5.0 * heat);
            var sway2 = Math.Sin(_elapsed * 19 + pitch * 3.9) * (4.0 + 8.0 * heat) + Math.Cos(_elapsed * 25 + pitch) * 2.5;
            var flicker = .84 + .16 * Math.Sin(_elapsed * 22 + pitch * 3.3);
            var flameH = (22 + 62 * heat * heightScale) * flicker;
            var flameW = (lane * 0.45 + 14 * heat) * (.7 + intensity * .5);

            var tint = warm ? Color.FromRgb(255, 185, 75) : AdjustColor(_activeKey[pitch] ? _activeKeyColor[pitch] : NoteColor(pitch, 0));
            var deep = warm ? Color.FromRgb(255, 60, 20) : Blend(tint, Color.FromRgb(140, 10, 50), .45);

            // Layer 1: Ambient thermal bloom around combustion zone
            var thermalBloom = new RadialGradientBrush
            {
                Center = new Point(.5, .9), GradientOrigin = new Point(.5, .9), RadiusX = .85, RadiusY = .95,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            thermalBloom.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(45 * intensity * heat), tint.R, tint.G, tint.B), 0));
            thermalBloom.GradientStops.Add(new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1));
            thermalBloom.Freeze();
            dc.DrawEllipse(thermalBloom, null, new Point(x, keyTop - flameH * .35), flameW * 1.6, flameH * .8);

            // Layer 2: Main organic aerodynamic combustion plume
            var plume = new RadialGradientBrush
            {
                Center = new Point(.5, .92), GradientOrigin = new Point(.5, .92), RadiusX = .85, RadiusY = 1.15,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            plume.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(220 * intensity * heat), 255, 255, 235), 0));
            plume.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(160 * intensity * heat), tint.R, tint.G, tint.B), .28));
            plume.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(60 * intensity * heat), deep.R, deep.G, deep.B), .75));
            plume.GradientStops.Add(new GradientStop(Color.FromArgb(0, deep.R, deep.G, deep.B), 1));
            plume.Freeze();

            dc.DrawEllipse(plume, null, new Point(x + sway1 * .4, keyTop - flameH * .42), flameW, flameH * .55);

            // Layer 3: High-energy dancing flame tongues
            var tongueH1 = (9 + 20 * heat) * (.8 + .2 * Math.Sin(_elapsed * 26 + pitch * 2.1)) * heightScale;
            var tongueH2 = (7 + 16 * heat) * (.8 + .2 * Math.Sin(_elapsed * 30 + pitch * 3.7)) * heightScale;
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(180 * intensity * heat), 255, 252, 235)), null, new Point(x - flameW * .15 + sway2 * .4, keyTop - tongueH1 * .5), 2.2 + heat * 2.0, tongueH1 * .5);
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(150 * intensity * heat), 255, 245, 210)), null, new Point(x + flameW * .18 + sway2 * .6, keyTop - tongueH2 * .5), 1.8 + heat * 1.6, tongueH2 * .5);

            // Layer 4: Base plasma contact flash
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(230 * intensity * heat), 255, 255, 250)), null, new Point(x, keyTop - 1), flameW * .55, 2.5 + heat * 1.5);
        }
    }

    private void DrawWatermark(DrawingContext dc, double width, double height)
    {
        var text = Label("KEYFLOW", 11, Color.FromArgb(110, 232, 224, 250), bold: true);
        dc.DrawText(text, new Point(width / 2 - text.Width / 2, height - KeyboardHeight - text.Height - 18));
    }

    private void DrawCounter(DrawingContext dc, double width)
    {
        var parts = new List<string>();
        // The readouts are stage text, so they follow the interface language like every label does.
        if (_visual.ShowCounter) parts.Add(Loc.F("{0:00} KEYS", _pressed.Count));
        if (_visual.ShowFps) parts.Add(Loc.F("{0} FPS · {1} PARTICLES", _fps, _sparks.Count));
        var text = new FormattedText(string.Join("   ", parts), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 12, Brush(Color.FromArgb(190, 243, 229, 255)), _pixelsPerDip);
        dc.DrawText(text, new Point(width - text.Width - 30, 28));
    }

    private void DrawLabel(DrawingContext dc, string text, Point center, double size, Color color, bool bold)
    {
        var formatted = Label(text, size, color, bold);
        dc.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));
    }

    private void Stage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this); Focus(); if (point.Y < ActualHeight - KeyboardHeight) return;
        _mouseDown = true; _mousePitch = PitchAt(point); CaptureMouse(); PianoKeyChanged?.Invoke(_mousePitch, true); e.Handled = true;
    }
    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || !IsMouseCaptured) return;
        var point = e.GetPosition(this);
        if (point.Y < ActualHeight - KeyboardHeight)
        {
            if (_mousePitch >= 0)
            {
                PianoKeyChanged?.Invoke(_mousePitch, false);
                _mousePitch = -1;
            }
            return;
        }
        var newPitch = PitchAt(point);
        if (newPitch != _mousePitch)
        {
            if (_mousePitch >= 0) PianoKeyChanged?.Invoke(_mousePitch, false);
            _mousePitch = newPitch;
            PianoKeyChanged?.Invoke(_mousePitch, true);
        }
    }
    private void Stage_MouseUp(object sender, MouseButtonEventArgs e) { ReleaseMouseKey(); if (_mouseDown) e.Handled = true; }
    private void ReleaseMouseKey()
    {
        if (!_mouseDown) return; _mouseDown = false; if (_mousePitch >= 0) PianoKeyChanged?.Invoke(_mousePitch, false); _mousePitch = -1; if (IsMouseCaptured) ReleaseMouseCapture();
    }
    private int PitchAt(Point point)
    {
        var whites = WhitePitches; var keyWidth = ActualWidth / whites.Length;
        var blackWidth = keyWidth * .52;
        var blackHeight = KeyboardHeight * (.61 + _visual.KeyOverhang / 100 * .18);
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!IsBlack(pitch)) continue;
            var x = KeyCenters[pitch] * ActualWidth - blackWidth * .5;
            if (point.X >= x && point.X < x + blackWidth && point.Y < ActualHeight - KeyboardHeight + blackHeight) return pitch;
        }
        return whites[Math.Clamp((int)(point.X / keyWidth), 0, whites.Length - 1)];
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { _stars.Clear(); _petals.Clear(); _petalCount = -1; base.OnRenderSizeChanged(sizeInfo); InvalidateVisual(); }
    private static bool IsBlack(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;
    internal static double BlackKeyOffset(int pitch) => (pitch % 12) switch
    {
        1 => -0.08,
        3 => 0.08,
        6 => -0.10,
        8 => 0.00,
        10 => 0.10,
        _ => 0.0
    };
    private static int[] BuildWhitesBelow()
    {
        var result = new int[128]; var count = 0;
        for (var pitch = 0; pitch < 128; pitch++)
        {
            result[pitch] = count;
            if (pitch >= FirstPitch && pitch < FirstPitch + KeyCount && !IsBlack(pitch)) count++;
        }
        return result;
    }
    private static double[] BuildKeyCenters()
    {
        var result = new double[128]; var whiteWidth = 1.0 / WhitePitches.Length;
        for (var pitch = 0; pitch < 128; pitch++)
            result[pitch] = IsBlack(pitch)
                ? (WhitesBelow[pitch] + BlackKeyOffset(pitch)) * whiteWidth
                : (WhitesBelow[pitch] + .5) * whiteWidth;
        return result;
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

    /// <summary>Per-pitch label tables; building them once keeps the per-frame label paths allocation-free.</summary>
    private static readonly string[] NoteLabels = BuildNoteLabels();
    private static readonly string[] ShortNoteLabels = BuildNoteLabels(shortName: true);
    private static readonly string[] AllKeyLabels = BuildAllKeyLabels();
    private static string[] BuildNoteLabels(bool shortName = false)
    {
        string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
        var result = new string[128];
        for (var pitch = 0; pitch < result.Length; pitch++)
        {
            var label = $"{names[pitch % 12]}{pitch / 12 - 1}";
            result[pitch] = shortName ? label.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-') : label;
        }
        return result;
    }
    private static string[] BuildAllKeyLabels()
    {
        var result = new string[128];
        for (var pitch = 0; pitch < result.Length; pitch++)
            result[pitch] = ShortNoteLabels[pitch] + (pitch % 12 == 0 ? (pitch / 12 - 1).ToString() : "");
        return result;
    }
    private static string NoteLabel(int pitch) => NoteLabels[ClampPitch(pitch)];
    private static string ShortNoteLabel(int pitch) => ShortNoteLabels[ClampPitch(pitch)];
    private static int ClampPitch(int pitch) => pitch < 0 ? 0 : pitch > 127 ? 127 : pitch;
    private static double Hue(int pitch) => 188 + (pitch - FirstPitch) / 87.0 * 112;
    private static Color ColorFromHue(double hue)
    {
        hue = (hue % 360 + 360) % 360; var c = .9; var x = c * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = .1;
        var (r, g, b) = hue switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
    private static uint PackColor(Color color) => (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    private static ulong GradientKey(byte kind, Color color) => (ulong)kind << 32 | PackColor(color);
    /// <summary>Frozen solid brushes are cached by ARGB value; a busy frame would otherwise allocate thousands of brushes.</summary>
    private SolidColorBrush Brush(Color color)
    {
        var key = PackColor(color);
        if (_brushCache.TryGetValue(key, out var brush)) return brush;
        if (_brushCache.Count > 12000) _brushCache.Clear();
        brush = new SolidColorBrush(color); brush.Freeze(); _brushCache[key] = brush; return brush;
    }
    private readonly Dictionary<(uint Color, int Thickness, bool RoundCaps), Pen> _penCache = [];
    /// <summary>
    /// Frozen pens cached by ARGB value, thickness (quantized to 1/100 px, far below a visible difference)
    /// and round caps. Every draw path above used to allocate one or two pens per note and per particle
    /// per frame; with the cache a dense frame allocates nothing and renders identically.
    /// </summary>
    private Pen Pen(Color color, double thickness, bool roundCaps = false)
    {
        var key = (PackColor(color), (int)Math.Round(thickness * 100), roundCaps);
        if (_penCache.TryGetValue(key, out var pen)) return pen;
        if (_penCache.Count > 8192) _penCache.Clear();
        pen = new Pen(Brush(color), thickness);
        if (roundCaps) { pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; }
        pen.Freeze();
        _penCache[key] = pen;
        return pen;
    }
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Typeface LabelBoldTypeface = new("Segoe UI Semibold");
    private readonly Dictionary<(string Text, double Size, uint Color, bool Bold, int Dpi), FormattedText> _labelCache = [];
    /// <summary>Text shaping is expensive; note and key labels repeat every frame, so their <see cref="FormattedText"/> is memoized verbatim.</summary>
    private FormattedText Label(string text, double size, Color color, bool bold)
    {
        var key = (text, size, PackColor(color), bold, (int)Math.Round(_pixelsPerDip * 100));
        if (_labelCache.TryGetValue(key, out var formatted)) return formatted;
        if (_labelCache.Count > 1024) _labelCache.Clear();
        formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? LabelBoldTypeface : LabelTypeface, size, Brush(color), _pixelsPerDip);
        _labelCache[key] = formatted;
        return formatted;
    }
    private static readonly Dictionary<string, Color> ParsedColors = [];
    /// <summary><see cref="ColorConverter.ConvertFromString"/> re-parses the same handful of hex strings for every note on every frame; the result is memoized instead.</summary>
    private static Color ParseColor(string value, Color fallback)
    {
        if (value is null) return fallback;
        if (ParsedColors.TryGetValue(value, out var cached)) return cached;
        Color parsed;
        try { parsed = (Color)ColorConverter.ConvertFromString(value)!; }
        catch { parsed = fallback; }
        if (ParsedColors.Count > 512) ParsedColors.Clear();
        ParsedColors[value] = parsed;
        return parsed;
    }
    private static Brush Freeze(Brush brush) { if (brush.CanFreeze) brush.Freeze(); return brush; }
    private static Rect Inflate(Rect r, double amount) => new(r.X - amount, r.Y - amount, Math.Max(1, r.Width + amount * 2), Math.Max(1, r.Height + amount * 2));
    private sealed record Star(double X, double Y, double Size, byte Alpha, double Speed, double Phase);
    /// <summary>A petal of the blossom layer; its on-screen position derives from elapsed time plus these seeds.</summary>
    private sealed record Petal(double X, double Y, double Size, double Speed, double Sway, double Drift, double Spin, double Phase);
    /// <summary>A cached overlay tile of one sounding key plus where it belongs on the stage.</summary>
    private sealed record ShadedKeyTile(BitmapSource Bitmap, Rect Where);
    private sealed class Spark { public double X, Y, Vx, Vy, Life, Age, Size, Phase, Mass; public bool Wisp; public Color Color; public int Kind; public double Grav = 1, DragK = 1; }
    private sealed class Ring { public double X, Y, Age, Life, Strength = 1; public bool Shock, Implode, Ripple; public Color Color; }
    private sealed class Flash { public double X, Y, Age, Life, Strength = 1; public int Style; public Color Color; }
    private sealed class LiveTrail { public int Pitch; public double Age, HeldSeconds; public bool KeyDown = true, Released, Hit; public double Strength = 1; }
}
