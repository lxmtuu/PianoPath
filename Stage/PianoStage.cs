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
    private string _shadedSignature = "";
    private double _shadedMilliseconds;
    private int _shadedBakes;
    private PianoVisualSettings _visual = new();
    private BitmapSource? _backgroundImage;
    private Brush? _vignetteBrush;
    private double _vignetteValue = -1;
    private string? _loadedBackgroundPath = "\0";
    private double _pointerX = .5, _pointerY = .5;
    private IReadOnlyList<NoteEvent> _notes = [];
    private IReadOnlySet<int> _pressed = new HashSet<int>();
    private double _position, _elapsed, _maxNoteDuration, _pixelsPerDip = 1, _fps;
    private bool _playing, _anyHeat;
    private bool _mouseDown;
    private int _mousePitch = -1;
    public event Action<int, bool>? PianoKeyChanged;
    public double KeyboardHeight => Math.Max(80, Math.Min(300, Math.Max(110, Math.Min(228, ActualHeight * .205)) * _visual.KeyboardScale / 100));
    public int SparkCount => _sparks.Count;
    public int RingCount => _rings.Count;
    /// <summary>Number of blossom petals currently in the air; surfaced by the verification suite.</summary>
    public int PetalCount => _petals.Count;
    public int LiveTrailCount => _liveTrails.Count;
    /// <summary>True while anything on the stage still animates on its own (particles, rings, cooling flames or held keys).</summary>
    public bool HasActiveEffects => _sparks.Count > 0 || _rings.Count > 0 || _liveTrails.Count > 0 || _anyHeat || _pressed.Count > 0
        || (_visual.ShowPetals && _visual.PetalAmount > 0) || (_visual.ShowSpotlights && _visual.SpotlightIntensity > 0);
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
                    if (!File.Exists(requestedPath)) throw new FileNotFoundException("The selected background image could not be found.", requestedPath);
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

    public void AddLiveNote(int pitch)
    {
        _liveTrails.Add(new LiveTrail { Pitch = pitch, Age = 0, HeldSeconds = 0, KeyDown = true });
        InvalidateVisual();
    }

    public void ReleaseLiveNote(int pitch)
    {
        var trail = _liveTrails.LastOrDefault(note => note.Pitch == pitch && note.KeyDown);
        if (trail is null) return;
        trail.KeyDown = false;
        trail.Released = true;
        InvalidateVisual();
    }

    public void ClearTransient()
    {
        _liveTrails.Clear(); _sparks.Clear(); _rings.Clear(); Array.Clear(_keyHeat); Array.Clear(_activeKey); _anyHeat = false; InvalidateVisual();
    }

    public void Impact(int pitch, double strength = 1)
    {
        var keyWidth = ActualWidth / KeyCount;
        var clamped = Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1);
        var x = KeyCenters[clamped] * ActualWidth;
        var y = ActualHeight - KeyboardHeight - 1;
        var noteColor = AdjustColor(_activeKey[clamped] ? _activeKeyColor[clamped] : NoteColor(clamped, 0));
        if (_visual.ShowImpactRings && _visual.RingSize > 0 && ActualWidth >= 1) _rings.Add(new Ring { X = x, Y = y, Color = noteColor, Life = .55 });
        if (!_visual.ShowEmbers || _visual.ParticleAmount < 1 || _sparks.Count >= MaxParticles) { InvalidateVisual(); return; }
        var hue = Hue(pitch);
        var amount = Math.Clamp((int)(_visual.ParticleAmount * strength * _visual.ParticleResponse / 55.0), 0, 120);
        for (var i = 0; i < amount; i++)
        {
            var spread = _visual.ParticleSpread / 100 * Math.PI;
            var angle = Math.PI - spread / 2 + _random.NextDouble() * spread;
            angle += Math.Sin(i * .37 + _elapsed * _visual.EvolutionSpeed / 100) * _visual.Spiral / 100 * .3;
            var speed = _visual.ParticleVelocity * (.45 + _random.NextDouble() * .9 * _visual.ParticleRandomness / 100) * _visual.ParticleSpeed / 100 * strength;
            var emitter = _visual.EmitterSize / 100 * keyWidth * 2;
            var life = _visual.ParticleLife * (.5 + _random.NextDouble() * _visual.ParticleLifeRandomness / 100);
            _sparks.Add(new Spark
            {
                X = x + (_random.NextDouble() - .5) * emitter, Y = y,
                Vx = Math.Cos(angle) * speed, Vy = Math.Sin(angle) * speed - 30,
                Life = life, Age = 0,
                Size = _visual.ParticleSize * (.5 + _random.NextDouble() * _visual.ParticleSizeRandomness / 100),
                Color = i % 4 == 0 ? Colors.White : Blend(noteColor, ColorFromHue(hue + (_random.NextDouble() - .5) * 28), .2)
            });
        }
        InvalidateVisual();
    }

    public void Advance(double seconds)
    {
        var dt = Math.Clamp(seconds, 0, .05) * _visual.PhysicsTimeFactor / 100; _elapsed += dt;
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
            p.Vx += field * dt * 14; p.Vy += _visual.Gravity * dt; var damping = Math.Exp(-_visual.Drag / 100 * dt); p.Vx *= damping; p.Vy *= damping;
        }
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            var ring = _rings[i]; ring.Age += dt;
            if (ring.Age >= ring.Life) _rings.RemoveAt(i);
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
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var keyHeight = KeyboardHeight; var keyTop = height - keyHeight; var lane = width / KeyCount;
        var chroma = _visual.BackgroundMode == "ChromaGreen";
        var scale = _visual.CameraZoom / 100;
        var parallax = _visual.CameraParallax / 100;
        var offsetX = (width - width * scale) * _visual.CameraOffset / 100 + (_pointerX - .5) * parallax * 28;
        var offsetY = (height - height * scale) * .5 + (_pointerY - .5) * parallax * 20;
        dc.DrawRectangle(chroma ? ChromaGreen : Ink, null, new Rect(0, 0, width, height));
        dc.PushTransform(new TranslateTransform(offsetX, offsetY)); dc.PushTransform(new ScaleTransform(scale, scale));
        if (chroma) dc.DrawRectangle(ChromaGreen, null, new Rect(0, 0, width, height));
        else
        {
            dc.DrawRectangle(Brush(ParseColor(_visual.BackgroundColor, Colors.Black)), null, new Rect(0, 0, width, height));
            if (_visual.ShowBackground && _visual.BackgroundMode == "Image" && _backgroundImage is not null)
            {
                var imageScale = Math.Max(width / _backgroundImage.Width, height / _backgroundImage.Height);
                var imageWidth = _backgroundImage.Width * imageScale; var imageHeight = _backgroundImage.Height * imageScale;
                dc.DrawImage(_backgroundImage, new Rect((width - imageWidth) / 2, (height - imageHeight) / 2, imageWidth, imageHeight));
                if (_visual.BackgroundDim > 0) dc.DrawRectangle(Brush(Color.FromArgb((byte)(_visual.BackgroundDim * 2.1), 0, 0, 0)), null, new Rect(0, 0, width, height));
            }
            if (_visual.ShowBackground && _visual.BackgroundGradient)
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
            // Concert layers sit above the background but below the note roll, so the music stays readable.
            if (_visual.ShowSpotlights && _visual.SpotlightIntensity > 0) DrawSpotlights(dc, width, keyTop, lane);
            if (_visual.ShowPetals && _visual.PetalAmount > 0) DrawPetals(dc, width, keyTop);
        }
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, keyTop + 2)));
        DrawNotes(dc, width, keyTop, lane);
        DrawLiveTrails(dc, width, keyTop, lane);
        dc.Pop();
        if (_visual.ShowFlame && _visual.FlameIntensity > 0) DrawFlames(dc, width, keyTop, lane);
        if (_visual.ShowImpactRings) DrawRings(dc);
        if (_visual.ShowEmbers || _visual.ShowWisps) DrawSparks(dc, width, keyTop);
        if (_visual.ShowHalo) DrawImpactLine(dc, width, keyTop);
        if (!chroma && _visual.Vignette > 0) DrawVignette(dc, width, keyTop);
        if (_visual.ShowKeys) DrawKeyboard(dc, width, height, lane, keyTop);
        if (_visual.ShowWatermark) DrawWatermark(dc, width, height);
        if (_visual.ShowCounter || _visual.ShowFps) DrawCounter(dc, width);
        dc.Pop(); dc.Pop();
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
        // three pens are ever needed, so they are frozen once instead of built 88 times per frame.
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var alpha = pitch % 12 == 0 ? 23 : pitch % 12 is 2 or 4 or 7 or 9 or 11 ? 10 : 5;
            var color = Color.FromArgb((byte)alpha, 186, 141, 255);
            var x = KeyCenters[pitch] * width;
            dc.DrawLine(LanePen(color, pitch % 12 == 0 ? 1 : .6), new Point(x, 0), new Point(x, height));
        }
    }

    private readonly Dictionary<(uint Color, int Thickness), Pen> _lanePens = [];

    private Pen LanePen(Color color, double thickness)
    {
        var key = (PackColor(color), (int)Math.Round(thickness * 100));
        if (_lanePens.TryGetValue(key, out var pen)) return pen;
        pen = new Pen(Brush(color), thickness);
        pen.Freeze();
        _lanePens[key] = pen;
        return pen;
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

    /// <summary>
    /// Coloured follow-spots sweeping the stage from above — the recital-hall layer of the concert
    /// themes. Two beams (a third on wide stages) with a soft pool of light where each one lands.
    /// </summary>
    private void DrawSpotlights(DrawingContext dc, double width, double keyTop, double lane)
    {
        var warm = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var cool = AdjustColor(ParseColor(_visual.PetalColor, Color.FromRgb(255, 179, 207)));
        DrawSpotlight(dc, width, keyTop, lane, warm, 0, .30, .55);
        DrawSpotlight(dc, width, keyTop, lane, cool, 2.1, .22, .78);
        if (width > 1100) DrawSpotlight(dc, width, keyTop, lane, Blend(warm, cool, .5), 4.2, .17, .64);
    }

    private void DrawSpotlight(DrawingContext dc, double width, double keyTop, double lane, Color color, double phase, double speed, double reach)
    {
        var apexX = width * (.5 + Math.Sin(_elapsed * speed + phase) * .34);
        var hitX = width * (.5 + Math.Sin(_elapsed * speed + phase + .5) * .42);
        var hitY = keyTop + lane * 1.6;
        var spread = lane * (4.5 + reach * 9);
        var cone = new StreamGeometry();
        using (var ctx = cone.Open())
        {
            ctx.BeginFigure(new Point(apexX, -28), true, true);
            ctx.LineTo(new Point(hitX - spread, hitY), true, false);
            ctx.LineTo(new Point(hitX + spread, hitY), true, false);
        }
        cone.Freeze();
        dc.DrawGeometry(SpotlightConeBrush(color), null, cone);
        dc.DrawEllipse(SpotlightPoolBrush(color), null, new Point(hitX, hitY), spread * .8, lane * 1.7);
    }

    /// <summary>Vertical beam gradient: nearly clear at the fixture, brightest just above the keys.</summary>
    private Brush SpotlightConeBrush(Color color)
    {
        var intensity = _visual.SpotlightIntensity / 100;
        var bottom = Color.FromArgb(Alpha(130 * intensity), color.R, color.G, color.B);
        var key = GradientKey(22, bottom);
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(34 * intensity), color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(bottom, .84));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheGradient(key, gradient);
    }

    /// <summary>Radial pool of light where a spot lands on the stage floor.</summary>
    private Brush SpotlightPoolBrush(Color color)
    {
        var intensity = _visual.SpotlightIntensity / 100;
        var core = Color.FromArgb(Alpha(64 * intensity), color.R, color.G, color.B);
        var key = GradientKey(23, core);
        if (_gradientCache.TryGetValue(key, out var cached)) return cached;
        var gradient = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5, MappingMode = BrushMappingMode.RelativeToBoundingBox };
        gradient.GradientStops.Add(new GradientStop(core, 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        gradient.Freeze();
        return CacheGradient(key, gradient);
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

    private void DrawConfiguredNote(DrawingContext dc, Rect r, Color color, double opacity, bool sounding, int pitch, bool rising = false)
    {
        color = AdjustColor(color);
        var style = _visual.NoteStyle;
        var radius = Math.Min(r.Height / 2, Math.Min(r.Width / 2, 2 + _visual.NoteRoundness / 100 * 12));
        var bloom = _visual.BloomSize / 100;
        var glow = _visual.NoteGlow / 100 * _visual.BloomIntensity / 65 * (sounding ? 1.35 : 1);
        var outer = 2 + bloom * 10;
        var tint = _visual.NoteTint / 78;
        var edgeWidth = .4 + _visual.NoteEdgeWidth / 45;
        var (cr, cg, cb) = (color.R, color.G, color.B);
        var bright = Blend(color, Colors.White, .35);
        // Outer bloom shared by every style; the neon style spreads it further to read as a glowing tube.
        var bloomScale = style == "Neon" ? 1.4 : 1;
        dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(16 * opacity * glow), cr, cg, cb)), null, Inflate(r, outer * 1.8 * bloomScale), radius + outer, radius + outer);
        dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(34 * opacity * glow), cr, cg, cb)), null, Inflate(r, outer * .75 * bloomScale), radius + outer * .6, radius + outer * .6);
        switch (style)
        {
            case "Neon":
            {
                // Hollow tube: a dark tinted interior, a wide soft stroke and a crisp bright core stroke.
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(70 * opacity * tint), cr, cg, cb)), null, r, radius, radius);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(90 * opacity), 6, 4, 14)), null, Inflate(r, -Math.Min(3, r.Width / 4)), radius, radius);
                var soft = new Pen(Brush(Color.FromArgb(Alpha(120 * opacity * glow), cr, cg, cb)), edgeWidth * 2.6 + 1.5); soft.Freeze();
                dc.DrawRoundedRectangle(null, soft, Inflate(r, -.7), radius, radius);
                var core = new Pen(Brush(Color.FromArgb(Alpha(255 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B)), edgeWidth + .8); core.Freeze();
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
                if (_visual.Notes3D)
                {
                    // Vertical bevel: lit top edge, saturated middle, shadowed bottom, like a bar with thickness.
                    dc.PushOpacity(Math.Clamp(opacity * tint, 0, 1));
                    dc.DrawRoundedRectangle(NoteBodyBrush(color), null, r, radius, radius);
                    dc.Pop();
                }
                else dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(205 * opacity * tint), cr, cg, cb)), null, r, radius, radius);
                if (_visual.NoteEdge > 0)
                {
                    var rim = new Pen(Brush(Color.FromArgb(Alpha(235 * opacity * Math.Min(1, _visual.NoteEdge / 100)), bright.R, bright.G, bright.B)), edgeWidth); rim.Freeze();
                    dc.DrawRoundedRectangle(null, rim, Inflate(r, -.7), radius, radius);
                }
                break;
            }
        }
        if (_visual.NoteRefraction > 0 && r.Width > 6)
        {
            var fringe = Alpha((style == "Neon" ? 90 : 170) * opacity * _visual.NoteRefraction / 100);
            dc.DrawLine(new Pen(Brush(Color.FromArgb(fringe, 255, 255, 255)), 1), new Point(r.X + 2, r.Y + 3), new Point(r.X + 2, r.Bottom - 3));
        }
        if (_visual.Notes3D && r.Height > 20 && style is "Solid" or "Glass")
        {
            // The solid style already carries its bevel gradient, so only glass needs the extra inner shade.
            if (style == "Glass")
            {
                var inner = new Rect(r.X + 3, r.Y + 4, Math.Max(2, r.Width - 6), Math.Max(3, r.Height - 8));
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(55 * opacity), 10, 7, 18)), null, inner, Math.Min(radius, inner.Width / 2), Math.Min(radius, inner.Width / 2));
            }
            dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha((style == "Glass" ? 165 : 90) * opacity), 255, 250, 255)), 1), new Point(r.X + 4, r.Y + 5), new Point(r.X + 4, r.Bottom - 5));
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
            var label = r.Width >= 20 ? NoteLabel(pitch) : NoteLabel(pitch).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');
            var luminance = .2126 * cr + .7152 * cg + .0722 * cb;
            var textColor = style == "Neon" ? Colors.White : luminance > 150 ? Color.FromRgb(12, 8, 20) : Colors.White;
            DrawLabel(dc, label, new Point(r.X + r.Width / 2, r.Bottom - Math.Min(12, r.Height / 2)), Math.Min(11, r.Width * .62), Color.FromArgb(Alpha(230 * opacity), textColor.R, textColor.G, textColor.B), true);
        }
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

    private Color AdjustColor(Color input)
    {
        var saturation = _visual.Saturation / 100;
        var gray = .2126 * input.R + .7152 * input.G + .0722 * input.B;
        var contrast = _visual.Contrast / 100;
        byte Channel(double c) => (byte)Math.Clamp((c - 128) * contrast + 128, 0, 255);
        return Color.FromRgb(Channel(gray + (input.R - gray) * saturation), Channel(gray + (input.G - gray) * saturation), Channel(gray + (input.B - gray) * saturation));
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Color ParseColor(string value, Color fallback) { try { return (Color)ColorConverter.ConvertFromString(value)!; } catch { return fallback; } }
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
                DrawConfiguredNote(dc, new Rect(x, headY, noteWidth, tailY - headY), NoteColor(trail.Pitch, 0), opacity, trail.KeyDown && trail.Hit, trail.Pitch, true);
            }
            else
            {
                var y = 28 + trail.Age * _visual.NoteFallSpeed;
                var tailY = Math.Max(0, y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed);
                var bottom = Math.Min(hitY + 6, y);
                if (bottom <= tailY) continue;
                var opacity = Math.Clamp(1 - tailY / Math.Max(1, hitY + 18), .08, 1) * _visual.NoteTint / 100;
                DrawConfiguredNote(dc, new Rect(x, tailY, noteWidth, bottom - tailY), NoteColor(trail.Pitch, 0), opacity, trail.KeyDown && trail.Hit, trail.Pitch);
            }
        }
    }

    private void DrawSparks(DrawingContext dc, double width, double height)
    {
        var bloom = 1.5 + _visual.BloomSize / 32;
        var wispGlow = _visual.WispGlow / 100;
        foreach (var particle in _sparks)
        {
            var fade = Math.Clamp(1 - particle.Age / particle.Life, 0, 1);
            if (particle.Wisp)
            {
                if (!_visual.ShowWisps) continue;
                var wispAlpha = Alpha(210 * Math.Pow(fade, 1.3) * wispGlow);
                var wispSize = particle.Size * (1 + particle.Age * 1.1);
                var wispCore = Color.FromArgb(wispAlpha, particle.Color.R, particle.Color.G, particle.Color.B);
                var wispHalo = Color.FromArgb((byte)(wispAlpha * .22), particle.Color.R, particle.Color.G, particle.Color.B);
                dc.DrawEllipse(Brush(wispHalo), null, new Point(particle.X, particle.Y), wispSize * 3.2, wispSize * 3.2);
                dc.DrawEllipse(Brush(wispCore), null, new Point(particle.X, particle.Y), wispSize, wispSize);
                continue;
            }
            if (!_visual.ShowEmbers) continue;
            var alpha = Alpha(225 * fade * _visual.ParticleGlow / 100); var size = particle.Size * (.55 + fade * .55);
            var color = Color.FromArgb(alpha, particle.Color.R, particle.Color.G, particle.Color.B);
            var glow = Color.FromArgb((byte)(alpha * .3), particle.Color.R, particle.Color.G, particle.Color.B);
            dc.DrawEllipse(Brush(glow), null, new Point(particle.X, particle.Y), size * bloom, size * bloom);
            dc.DrawEllipse(Brush(color), null, new Point(particle.X, particle.Y), size, size);
        }
    }

    private void DrawRings(DrawingContext dc)
    {
        foreach (var ring in _rings)
        {
            var t = Math.Clamp(ring.Age / ring.Life, 0, 1);
            var radius = 6 + t * (16 + _visual.RingSize * 1.1);
            var alpha = Alpha(210 * (1 - t) * (1 - t));
            var pen = new Pen(Brush(Color.FromArgb(alpha, ring.Color.R, ring.Color.G, ring.Color.B)), .6 + (1 - t) * 2.6); pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(ring.X, ring.Y), radius, radius * .32);
            if (t < .35) dc.DrawEllipse(Brush(Color.FromArgb(Alpha(140 * (1 - t / .35)), 255, 255, 255)), null, new Point(ring.X, ring.Y), 3 + radius * .25, 2 + radius * .1);
        }
    }

    private void DrawImpactLine(DrawingContext dc, double width, double y)
    {
        var baseColor = AdjustColor(ParseColor(_visual.HaloColor, ColorFromHue(266)));
        var intensity = Math.Clamp(_visual.HaloIntensity / 100.0, 0.0, 2.0);
        if (intensity <= 0.001) return;

        var haloSpread = 6 + _visual.BloomSize / 5.0;
        var haloAlpha = Alpha(Math.Clamp((24 + _visual.BloomIntensity * .75) * intensity, 0, 255));

        // 1 · Soft volumetric bloom across the hit line
        var glowHeight = haloSpread * 2.5;
        var haloGlowBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        haloGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B), 0.0));
        haloGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(haloAlpha * .55), baseColor.R, baseColor.G, baseColor.B), 0.5));
        haloGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, baseColor.R, baseColor.G, baseColor.B), 1.0));
        haloGlowBrush.Freeze();
        dc.DrawRectangle(haloGlowBrush, null, new Rect(0, y - glowHeight * .5, width, glowHeight));

        // 2 · Radiant horizontal beams
        var outerPen = new Pen(Brush(Color.FromArgb((byte)(haloAlpha * .4), baseColor.R, baseColor.G, baseColor.B)), haloSpread * 2.2); outerPen.Freeze();
        dc.DrawLine(outerPen, new Point(0, y), new Point(width, y));
        var midPen = new Pen(Brush(Color.FromArgb((byte)Math.Clamp(50 * intensity, 0, 255), baseColor.R, baseColor.G, baseColor.B)), haloSpread * .9); midPen.Freeze();
        dc.DrawLine(midPen, new Point(0, y), new Point(width, y));

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        rainbow.GradientStops.Add(new GradientStop(AdjustColor(NoteColor(28, 0)), 0));
        rainbow.GradientStops.Add(new GradientStop(baseColor, .5));
        rainbow.GradientStops.Add(new GradientStop(AdjustColor(NoteColor(100, 0)), 1));
        rainbow.Freeze();
        var corePen = new Pen(rainbow, Math.Max(2.0, 1.6 * intensity)); corePen.Freeze();
        dc.DrawLine(corePen, new Point(0, y), new Point(width, y));

        var whiteCore = new Pen(Brush(Color.FromArgb((byte)Math.Clamp(180 * intensity, 0, 255), 255, 255, 255)), 1.2); whiteCore.Freeze();
        dc.DrawLine(whiteCore, new Point(0, y), new Point(width, y));

        // 3 · Active note photon excitation & flares
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!_activeKey[pitch]) continue;
            var hitX = KeyCenters[pitch] * width;
            var hitColor = KeyColor(pitch);
            var hitAmount = Math.Clamp(_keyHeat[pitch] > 0 ? _keyHeat[pitch] : 1.0, 0.35, 1.0);
            var flareRadius = (16 + _visual.BloomSize * .35) * hitAmount;

            var burstKey = GradientKey(12, Color.FromArgb((byte)pitch, hitColor.R, hitColor.G, hitColor.B));
            if (!_gradientCache.TryGetValue(burstKey, out var burstBrush))
            {
                var burst = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5, MappingMode = BrushMappingMode.RelativeToBoundingBox };
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(190 * intensity * hitAmount), 255, 255, 255), 0.0));
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(130 * intensity * hitAmount), hitColor.R, hitColor.G, hitColor.B), 0.35));
                burst.GradientStops.Add(new GradientStop(Color.FromArgb(0, hitColor.R, hitColor.G, hitColor.B), 1.0));
                burst.Freeze();
                burstBrush = CacheGradient(burstKey, burst);
            }
            dc.DrawEllipse(burstBrush, null, new Point(hitX, y), flareRadius * 1.5, flareRadius * .9);

            var flarePen = new Pen(Brush(Color.FromArgb(Alpha(160 * intensity * hitAmount), 255, 255, 255)), 1.8); flarePen.Freeze();
            var flareWidth = 32 + flareRadius * 1.8;
            dc.DrawLine(flarePen, new Point(hitX - flareWidth, y), new Point(hitX + flareWidth, y));

            var rayPen = new Pen(Brush(Color.FromArgb(Alpha(110 * intensity * hitAmount), hitColor.R, hitColor.G, hitColor.B)), 2.0); rayPen.Freeze();
            dc.DrawLine(rayPen, new Point(hitX, y - 14), new Point(hitX, y + 20));
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
                var radiusX = 14 + glowRadius * 70; var radiusY = 10 + glowRadius * 60;
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
        var glowPen = new Pen(Brush(Color.FromArgb((byte)(30 + _visual.KeyLighting * .95), halo.R, halo.G, halo.B)), 5 + _visual.BloomSize / 10); glowPen.Freeze();
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
        var whiteEdge = new Pen(Brush(glass ? Color.FromArgb(120, 210, 220, 255) : Color.FromArgb(170, 68, 72, 94)), glass ? .8 : .7); whiteEdge.Freeze();
        for (var i = 0; i < whites.Length; i++)
        {
            var pitch = whites[i]; var rect = new Rect(i * whiteWidth, top + 5, whiteWidth - 1, height - top - 5);
            var active = _activeKey[pitch];
            if (active && _visual.AnimateKeys)
            {
                var color = KeyColor(pitch); var lit = Blend(color, Colors.White, .32);
                var pressed = new Rect(rect.X, rect.Y + press, rect.Width, rect.Height - press);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(60 + _visual.KeyLighting * 1.4), color.R, color.G, color.B)), null, Inflate(new Rect(rect.X - 4, top - 1, rect.Width + 8, rect.Height + 6), 2), 7, 7);
                dc.DrawRoundedRectangle(KeyLightBrush(color, lit), new Pen(Brush(Color.FromArgb(255, lit.R, lit.G, lit.B)), 1), pressed, 3, 3);
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
        var blackEdge = new Pen(Brush(Color.FromArgb(200, 72, 66, 96)), .75); blackEdge.Freeze();
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
                dc.DrawRoundedRectangle(Brush(lit), new Pen(Brush(Colors.White), 1), pressed, 5, 5);
            }
            else
            {
                dc.DrawRoundedRectangle(blackBrush, blackEdge, rect, 3, 3);
                if (studio) dc.DrawLine(new Pen(Brush(Color.FromArgb(70, 255, 255, 255)), 1), new Point(rect.X + 2, rect.Y + 1.5), new Point(rect.Right - 2, rect.Y + 1.5));
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
        var text = _visual.KeyLabels == "All"
            ? NoteLabel(pitch).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-') + (pitch % 12 == 0 ? (pitch / 12 - 1).ToString() : "")
            : NoteLabel(pitch);
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
            var signature = scene.Signature();
            // Render bakes at the quality's internal render scale, so the cached bitmap must be
            // compared against the scaled resolution, not the full band size.
            var bakeScale = Math.Clamp(scene.RenderScale, .25, 1);
            var bakedWidth = Math.Max(1, (int)Math.Ceiling(bandWidth * bakeScale));
            var bakedHeight = Math.Max(1, (int)Math.Ceiling(bandPixels * bakeScale));
            if (_shadedBase is null || signature != _shadedSignature || _shadedBase.PixelWidth != bakedWidth || _shadedBase.PixelHeight != bakedHeight)
            {
                var clock = Stopwatch.StartNew();
                var bake = PianoKeyboardRenderer.Render(scene, NoLights, 0, 0, bandWidth, bandPixels, -1);
                _shadedMilliseconds = clock.Elapsed.TotalMilliseconds;
                _shadedBakes++;
                if (bake is null) { IsShadedKeyboardActive = false; return false; }
                _shadedBase = bake; _shadedSignature = signature; _shadedTiles.Clear(); _lastTile.Clear();
            }
            dc.DrawImage(_shadedBase, new Rect(0, top, width, bandHeight));
            DrawShadedLitKeys(dc, scene, width, bandHeight, top);
            IsShadedKeyboardActive = true;
            return true;
        }
        catch (Exception)
        {
            // A memory or imaging failure must never take the stage down; the vector keyboard takes over.
            _shadedBase = null; _shadedTiles.Clear(); _lastTile.Clear(); _shadedSignature = ""; IsShadedKeyboardActive = false;
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
        var intensity = _visual.FlameIntensity / 100; var heightScale = .4 + _visual.FlameHeight / 100 * 1.4;
        var warm = _visual.FlameColorMode != "Note";
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var heat = _keyHeat[pitch];
            if (heat <= .02) continue;
            var x = KeyCenters[pitch] * width;
            var pulse = .65 + .35 * Math.Sin(_elapsed * 13 + pitch);
            var radius = (8 + 30 * pulse) * heat * (.6 + intensity * .6);
            var tint = warm ? Color.FromRgb(255, 178, 73) : AdjustColor(_activeKey[pitch] ? _activeKeyColor[pitch] : NoteColor(pitch, 0));
            var deep = warm ? Color.FromRgb(255, 83, 54) : Blend(tint, Color.FromRgb(120, 0, 60), .45);
            var flame = new RadialGradientBrush
            {
                Center = new Point(.5, .84), GradientOrigin = new Point(.5, .84), RadiusX = .8, RadiusY = 1.1,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(150 * intensity), 255, 255, 220), 0));
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(105 * intensity), tint.R, tint.G, tint.B), .28));
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(Alpha(42 * intensity), deep.R, deep.G, deep.B), .72));
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(0, deep.R, deep.G, deep.B), 1)); flame.Freeze();
            dc.DrawEllipse(flame, null, new Point(x, keyTop + 3), radius, (20 + radius * 1.35) * heightScale);
            // A flickering inner tongue gives the flame some motion instead of a static blob.
            var tongue = (6 + 14 * heat) * (.7 + .3 * Math.Sin(_elapsed * 21 + pitch * 1.7)) * heightScale;
            dc.DrawEllipse(Brush(Color.FromArgb(Alpha(120 * intensity * heat), 255, 250, 225)), null, new Point(x + Math.Sin(_elapsed * 17 + pitch) * 2, keyTop - tongue * .5), 2.5 + heat * 2, tongue);
        }
    }

    private void DrawWatermark(DrawingContext dc, double width, double height)
    {
        var text = new FormattedText("KEYFLOW", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 11, Brush(Color.FromArgb(110, 232, 224, 250)), _pixelsPerDip);
        dc.DrawText(text, new Point(width / 2 - text.Width / 2, height - KeyboardHeight - text.Height - 18));
    }

    private void DrawCounter(DrawingContext dc, double width)
    {
        var parts = new List<string>();
        if (_visual.ShowCounter) parts.Add($"{_pressed.Count:00} KEYS");
        if (_visual.ShowFps) parts.Add($"{_fps:0} FPS · {_sparks.Count} PARTICLES");
        var text = new FormattedText(string.Join("   ", parts), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 12, Brush(Color.FromArgb(190, 243, 229, 255)), _pixelsPerDip);
        dc.DrawText(text, new Point(width - text.Width - 30, 28));
    }

    private void DrawLabel(DrawingContext dc, string text, Point center, double size, Color color, bool bold)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(bold ? "Segoe UI Semibold" : "Segoe UI"), size, Brush(color), _pixelsPerDip);
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

    private static string NoteLabel(int pitch) { string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]; return $"{names[pitch % 12]}{pitch / 12 - 1}"; }
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
    private static Brush Freeze(Brush brush) { if (brush.CanFreeze) brush.Freeze(); return brush; }
    private static Rect Inflate(Rect r, double amount) => new(r.X - amount, r.Y - amount, Math.Max(1, r.Width + amount * 2), Math.Max(1, r.Height + amount * 2));
    private sealed record Star(double X, double Y, double Size, byte Alpha, double Speed, double Phase);
    /// <summary>A petal of the blossom layer; its on-screen position derives from elapsed time plus these seeds.</summary>
    private sealed record Petal(double X, double Y, double Size, double Speed, double Sway, double Drift, double Spin, double Phase);
    /// <summary>A cached overlay tile of one sounding key plus where it belongs on the stage.</summary>
    private sealed record ShadedKeyTile(BitmapSource Bitmap, Rect Where);
    private sealed class Spark { public double X, Y, Vx, Vy, Life, Age, Size, Phase; public bool Wisp; public Color Color; }
    private sealed class Ring { public double X, Y, Age, Life; public Color Color; }
    private sealed class LiveTrail { public int Pitch; public double Age, HeldSeconds; public bool KeyDown = true, Released, Hit; }
}
