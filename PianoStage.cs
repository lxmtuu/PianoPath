using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Immersive piano-roll renderer: shaded falling notes, live key trails, sparks, flames, dust and a
/// physically-styled 88-key stage. A second "bright pass" of the scene feeds the bloom, lens streak and
/// color-fringing shader layers, which are composited with GPU blur passes.
/// </summary>
internal sealed partial class PianoStage : FrameworkElement
{
    private const int FirstPitch = 21, KeyCount = 88, MaxParticles = 2600;
    private const double FallSpeed = 258;
    private const int FireMaskWidth = 96, FireMaskHeight = 384;
    /// <summary>Resolution factor of the bright pass (bloom / streaks / fringing) relative to the stage.</summary>
    private const double ShaderScale = .5;
    /// <summary>MIDI pitches of the 52 white keys, in keyboard order.</summary>
    private static readonly int[] WhitePitches = Enumerable.Range(FirstPitch, KeyCount).Where(p => !IsBlack(p)).ToArray();
    /// <summary>For every MIDI pitch, how many white keys lie below it; gives each black key its x position without per-frame counting.</summary>
    private static readonly int[] WhitesBelow = BuildWhitesBelow();
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush ChromaGreen = Freeze(new SolidColorBrush(Color.FromRgb(0, 255, 0)));
    private static readonly BitmapSource FireMask = BuildFireMask();
    private static readonly BitmapSource GrainTexture = BuildNoiseTexture(160, 3, 96);
    private static readonly BitmapSource ScanlineTexture = BuildScanlineTexture();
    private readonly Dictionary<uint, SolidColorBrush> _brushCache = [];
    private readonly Dictionary<(byte Kind, uint A, uint B, uint C, int N), Brush> _shadeCache = [];
    private readonly List<Star> _stars = [];
    private readonly List<Dust> _dust = [];
    private readonly List<Spark> _sparks = [];
    private readonly List<Ring> _rings = [];
    private readonly List<LiveTrail> _liveTrails = [];
    private readonly Random _random = new(7331);
    private readonly bool[] _activeKey = new bool[128];
    private readonly Color[] _activeKeyColor = new Color[128];
    private readonly double[] _keyHeat = new double[128];
    private readonly double[] _keyPulse = new double[128];
    private readonly double[] _wispBudget = new double[128];
    private DrawingVisual _brightVisual = new();
    private Brush? _bloomBrush;
    private double _bloomBrushWidth = -1, _bloomBrushHeight = -1;
    private bool _recordingBrightPass;
    private PianoVisualSettings _visual = new();
    private BitmapSource? _backgroundImage;
    private Brush? _vignetteBrush;
    private double _vignetteValue = -1;
    private string? _loadedBackgroundPath = "\0";
    private double _pointerX = .5, _pointerY = .5;
    private IReadOnlyList<NoteEvent> _notes = [];
    private IReadOnlySet<int> _pressed = new HashSet<int>();
    private double _position, _elapsed, _maxNoteDuration, _pixelsPerDip = 1, _fps;
    private double _impactPulse, _grainSeed = .37;
    private bool _playing, _anyHeat, _mouseDown;
    private int _mousePitch = -1;
    public event Action<int, bool>? PianoKeyChanged;
    public double KeyboardHeight => Math.Max(80, Math.Min(300, Math.Max(110, Math.Min(228, ActualHeight * .205)) * _visual.KeyboardScale / 100));
    public int SparkCount => _sparks.Count;
    public int RingCount => _rings.Count;
    public int LiveTrailCount => _liveTrails.Count;
    /// <summary>True while anything on the stage still animates on its own (particles, rings, cooling flames or held keys).</summary>
    public bool HasActiveEffects => _sparks.Count > 0 || _rings.Count > 0 || _liveTrails.Count > 0 || _anyHeat || _pressed.Count > 0 || _impactPulse > .01;
    public bool HasBackgroundImage => _backgroundImage is not null;
    public string? BackgroundLoadError { get; private set; }
    public double FirstLiveTrailY => _liveTrails.Count == 0 ? -1 : _liveTrails[0].Age * _visual.NoteFallSpeed;
    public double LiveTrailHeightFor(int pitch)
    {
        var trail = _liveTrails.LastOrDefault(note => note.Pitch == pitch);
        return trail is null ? -1 : 28 + trail.HeldSeconds * _visual.NoteFallSpeed;
    }

    public PianoStage()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        MouseDown += Stage_MouseDown;
        MouseUp += Stage_MouseUp;
        LostMouseCapture += (_, _) => ReleaseMouseKey();
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
        _activeKeyCount = 0;
        if (_playing && _notes.Count > 0)
        {
            for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - _maxNoteDuration); i < _notes.Count; i++)
            {
                var note = _notes[i];
                if (note.Start > _position) break;
                if (note.End <= _position || note.Pitch < 0 || note.Pitch > 127) continue;
                if (!_activeKey[note.Pitch]) _activeKeyCount++;
                _activeKey[note.Pitch] = true; _activeKeyColor[note.Pitch] = NoteColor(note.Pitch, note.Track);
            }
        }
        foreach (var pitch in _pressed)
        {
            if (pitch < 0 || pitch > 127) continue;
            if (!_activeKey[pitch]) _activeKeyCount++;
            _activeKey[pitch] = true; _activeKeyColor[pitch] = NoteColor(pitch, 0);
        }
    }

    public void AddLiveNote(int pitch)
    {
        _liveTrails.Add(new LiveTrail { Pitch = pitch, Age = 0, HeldSeconds = 0, KeyDown = true });
        if (pitch is >= 0 and < 128) _keyPulse[pitch] = 1;
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
        _liveTrails.Clear(); _sparks.Clear(); _rings.Clear(); Array.Clear(_keyHeat); Array.Clear(_activeKey); Array.Clear(_keyPulse);
        _anyHeat = false; _impactPulse = 0; InvalidateVisual();
    }

    public void Impact(int pitch, double strength = 1)
    {
        var keyWidth = ActualWidth / KeyCount;
        var clamped = Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1);
        var x = (clamped - FirstPitch + .5) * keyWidth;
        var y = ActualHeight - KeyboardHeight - 1;
        var noteColor = AdjustColor(_activeKey[clamped] ? _activeKeyColor[clamped] : NoteColor(clamped, 0));
        _lastImpactColor = noteColor;
        _keyPulse[clamped] = 1;
        _impactPulse = Math.Min(1.35, _impactPulse + .55 * strength);
        if (_visual.ShowImpactRings && _visual.RingSize > 0 && ActualWidth >= 1) _rings.Add(new Ring { X = x, Y = y, Color = noteColor, Life = .55, Strength = strength, Width = Math.Max(26, keyWidth * 3.4) });
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
        _grainSeed = (_grainSeed + dt * 27) % 1;
        _impactPulse = Math.Max(0, _impactPulse - dt * 1.9);
        var hitY = ActualHeight - KeyboardHeight;
        var lane = ActualWidth / KeyCount;
        // Flames and key reflections follow a per-key "heat" value: it rises while the key sounds and cools down after release.
        _anyHeat = false;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            var heat = _keyHeat[pitch];
            heat = _activeKey[pitch] ? Math.Min(1, heat + dt * 7) : Math.Max(0, heat - dt * 2.4);
            _keyHeat[pitch] = heat;
            if (_activeKey[pitch]) _keyPulse[pitch] = Math.Max(_keyPulse[pitch], .82);
            else _keyPulse[pitch] = Math.Max(0, _keyPulse[pitch] - dt * 1.7);
            if (heat > .01) _anyHeat = true;
            if (_activeKey[pitch] && _visual.ShowWisps && _visual.WispAmount > 0 && ActualWidth >= 1) SpawnWisps(pitch, dt, hitY, lane);
        }
        AdvanceParticles(dt);
        for (var i = _rings.Count - 1; i >= 0; i--)
        {
            var ring = _rings[i]; ring.Age += dt;
            if (ring.Age >= ring.Life) _rings.RemoveAt(i);
        }
        for (var i = _liveTrails.Count - 1; i >= 0; i--)
        {
            var trail = _liveTrails[i]; trail.Age += dt;
            if (trail.KeyDown) trail.HeldSeconds += dt;
            var y = 28 + trail.Age * _visual.NoteFallSpeed;
            if (!trail.Hit && y >= hitY) { trail.Hit = true; Impact(trail.Pitch, .8); }
            var tailY = y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed;
            if (trail.Released && tailY > hitY + 32) _liveTrails.RemoveAt(i);
        }
        AdvanceAtmosphere(dt, hitY);
        InvalidateVisual();
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
            DrawBackdrop(dc, width, keyTop, lane);
            if (_visual.HorizonGlow > 0) DrawHorizonGlow(dc, width, keyTop);
            if (_visual.ShowLightBeams && _visual.BeamIntensity > 0) DrawKeyBeams(dc, width, keyTop, lane);
        }
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, keyTop + 2)));
        DrawNoteTrails(dc, width, keyTop, lane);
        DrawNotes(dc, width, keyTop, lane);
        DrawLiveTrails(dc, width, keyTop, lane);
        dc.Pop();
        if (_visual.ShowFlame && _visual.FlameIntensity > 0) DrawFlames(dc, width, keyTop, lane);
        if (_visual.ShowImpactRings) DrawRings(dc);
        if (_visual.ShowEmbers || _visual.ShowWisps) DrawSparks(dc);
        if (_visual.ShowDust && _visual.DustDensity > 0) DrawDust(dc);
        if (_visual.ShowHalo) DrawImpactLine(dc, width, keyTop);
        if (!chroma)
        {
            CompositeShaderLayers(dc, width, height, keyTop, lane);
            if (_visual.Vignette > 0) DrawVignette(dc, width, keyTop);
            DrawCinematicBars(dc, width, height);
        }
        if (_visual.ShowKeys) DrawKeyboard(dc, width, height, lane, keyTop);
        if (_visual.ShowWatermark) DrawWatermark(dc, width, height);
        if (_visual.ShowCounter || _visual.ShowFps) DrawCounter(dc, width);
        dc.Pop(); dc.Pop();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        _stars.Clear(); _dust.Clear(); _bloomBrush = null; _bloomBrushWidth = _bloomBrushHeight = -1;
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    private static bool IsBlack(int pitch) => pitch % 12 is 1 or 3 or 6 or 8 or 10;
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

    private static string NoteLabel(int pitch) { string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]; return $"{names[pitch % 12]}{pitch / 12 - 1}"; }
    private static double Hue(int pitch) => 188 + (pitch - FirstPitch) / 87.0 * 112;
    private static Color ColorFromHue(double hue)
    {
        hue = (hue % 360 + 360) % 360; var c = .9; var x = c * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = .1;
        var (r, g, b) = hue switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static uint PackColor(Color color) => (uint)color.A << 24 | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    /// <summary>Frozen solid brushes are cached by ARGB value; a busy frame would otherwise allocate thousands of brushes.</summary>
    private SolidColorBrush Brush(Color color)
    {
        var key = PackColor(color);
        if (_brushCache.TryGetValue(key, out var brush)) return brush;
        if (_brushCache.Count > 7000) _brushCache.Clear();
        brush = new SolidColorBrush(color); brush.Freeze(); _brushCache[key] = brush; return brush;
    }

    private static Brush Freeze(Brush brush) { if (brush.CanFreeze) brush.Freeze(); return brush; }
    private static Rect Inflate(Rect r, double amount) => new(r.X - amount, r.Y - amount, Math.Max(1, r.Width + amount * 2), Math.Max(1, r.Height + amount * 2));
    private static byte Alpha(double value) => (byte)Math.Clamp(value, 0, 255);
    private static Color Blend(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Color ParseColor(string value, Color fallback) { try { return (Color)ColorConverter.ConvertFromString(value)!; } catch { return fallback; } }

    private void Stage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this); Focus(); if (point.Y < ActualHeight - KeyboardHeight) return;
        _mouseDown = true; _mousePitch = PitchAt(point); CaptureMouse(); PianoKeyChanged?.Invoke(_mousePitch, true); e.Handled = true;
    }
    private void Stage_MouseUp(object sender, MouseButtonEventArgs e) { ReleaseMouseKey(); if (_mouseDown) e.Handled = true; }
    private void ReleaseMouseKey()
    {
        if (!_mouseDown) return; _mouseDown = false; if (_mousePitch >= 0) PianoKeyChanged?.Invoke(_mousePitch, false); _mousePitch = -1; if (IsMouseCaptured) ReleaseMouseCapture();
    }
    private int PitchAt(Point point)
    {
        var whites = WhitePitches; var keyWidth = ActualWidth / whites.Length;
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!IsBlack(pitch)) continue;
            var x = WhitesBelow[pitch] * keyWidth - keyWidth * .29;
            if (point.X >= x && point.X < x + keyWidth * .58 && point.Y < ActualHeight - KeyboardHeight + KeyboardHeight * .63) return pitch;
        }
        return whites[Math.Clamp((int)(point.X / keyWidth), 0, whites.Length - 1)];
    }

    private sealed record Star(double X, double Y, double Size, byte Alpha, double Speed, double Phase);
    private sealed class Dust { public double X, Y, Vx, Vy, Size, Phase, Spin; }
    private sealed class Spark { public double X, Y, Vx, Vy, Life, Age, Size, Phase; public bool Wisp; public Color Color; }
    private sealed class Ring { public double X, Y, Age, Life, Strength, Width; public Color Color; }
    private sealed class LiveTrail { public int Pitch; public double Age, HeldSeconds; public bool KeyDown = true, Released, Hit; }
}
