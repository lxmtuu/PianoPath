using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Immersive piano-roll renderer: live key trails, optional MIDI playback notes and impact sparks.</summary>
internal sealed class PianoStage : FrameworkElement
{
    private const int FirstPitch = 21, KeyCount = 88;
    private const double FallSpeed = 258;
    /// <summary>MIDI pitches of the 52 white keys, in keyboard order.</summary>
    private static readonly int[] WhitePitches = Enumerable.Range(FirstPitch, KeyCount).Where(p => !IsBlack(p)).ToArray();
    /// <summary>For every MIDI pitch, how many white keys lie below it; gives each black key its x position without per-frame counting.</summary>
    private static readonly int[] WhitesBelow = BuildWhitesBelow();
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush KeyBlack = Freeze(new LinearGradientBrush(Color.FromRgb(14, 15, 24), Color.FromRgb(3, 4, 9), 90));
    private static readonly Brush KeyWhite = Freeze(new LinearGradientBrush(Color.FromRgb(253, 250, 255), Color.FromRgb(157, 166, 188), 90));
    private readonly List<Star> _stars = [];
    private readonly List<Spark> _sparks = [];
    private readonly List<LiveTrail> _liveTrails = [];
    private readonly Random _random = new(7331);
    private PianoVisualSettings _visual = new();
    private BitmapSource? _backgroundImage;
    private string? _loadedBackgroundPath = "\0";
    private double _pointerX = .5, _pointerY = .5;
    private IReadOnlyList<NoteEvent> _notes = [];
    private IReadOnlySet<int> _pressed = new HashSet<int>();
    private double _position, _elapsed, _maxNoteDuration, _pixelsPerDip = 1;
    private bool _playing;
    private bool _mouseDown;
    private int _mousePitch = -1;
    public event Action<int, bool>? PianoKeyChanged;
    public double KeyboardHeight => Math.Max(110, Math.Min(228, ActualHeight * .205));
    public int SparkCount => _sparks.Count;
    public int LiveTrailCount => _liveTrails.Count;
    public bool HasBackgroundImage => _backgroundImage is not null;
    public string? BackgroundLoadError { get; private set; }
    public double FirstLiveTrailY => _liveTrails.Count == 0 ? -1 : _liveTrails[0].Age * _visual.NoteFallSpeed;
    public double LiveTrailHeightFor(int pitch)
    {
        var trail = _liveTrails.LastOrDefault(note => note.Pitch == pitch);
        return trail is null ? -1 : 28 + trail.HeldSeconds * _visual.NoteFallSpeed;
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
        InvalidateVisual();
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
        _liveTrails.Clear(); _sparks.Clear(); InvalidateVisual();
    }

    public void Impact(int pitch, double strength = 1)
    {
        if (!_visual.ShowEmbers || _visual.ParticleAmount < 1) return;
        var keyWidth = ActualWidth / KeyCount;
        var x = (Math.Clamp(pitch, FirstPitch, FirstPitch + KeyCount - 1) - FirstPitch + .5) * keyWidth;
        var y = ActualHeight - KeyboardHeight - 1;
        var hue = Hue(pitch);
        var noteColor = AdjustColor(NoteColor(pitch));
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
        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i]; p.Age += dt; p.X += p.Vx * dt; p.Y += p.Vy * dt;
            var flow = _visual.VectorField / 100 * Math.Sin(p.Y / Math.Max(1, _visual.FieldScale) + _elapsed * _visual.EvolutionSpeed / 100);
            p.Vx += flow * dt * 14; p.Vy += _visual.Gravity * dt; var damping = Math.Exp(-_visual.Drag / 100 * dt); p.Vx *= damping; p.Vy *= damping;
            if (p.Age >= p.Life) _sparks.RemoveAt(i);
        }
        var hitY = ActualHeight - KeyboardHeight;
        for (var i = _liveTrails.Count - 1; i >= 0; i--)
        {
            var trail = _liveTrails[i]; trail.Age += dt;
            if (trail.KeyDown) trail.HeldSeconds += dt;
            var y = 28 + trail.Age * _visual.NoteFallSpeed;
            if (!trail.Hit && y >= hitY) { trail.Hit = true; Impact(trail.Pitch, .8); }
            var tailY = y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed;
            if (trail.Released && tailY > hitY + 32) _liveTrails.RemoveAt(i);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight; if (width < 1 || height < 1) return;
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var keyHeight = KeyboardHeight; var keyTop = height - keyHeight; var lane = width / KeyCount;
        var scale = _visual.CameraZoom / 100;
        var parallax = _visual.CameraParallax / 100;
        var offsetX = (width - width * scale) * _visual.CameraOffset / 100 + (_pointerX - .5) * parallax * 28;
        var offsetY = (height - height * scale) * .5 + (_pointerY - .5) * parallax * 20;
        dc.PushTransform(new TranslateTransform(offsetX, offsetY)); dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRectangle(Ink, null, new Rect(0, 0, width, height));
        if (_visual.ShowBackground && _backgroundImage is not null)
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
        DrawNotes(dc, width, keyTop, lane);
        DrawLiveTrails(dc, width, keyTop, lane);
        if (_visual.ShowFlame) DrawFlames(dc, width, keyTop, lane);
        if (_visual.ShowEmbers) DrawSparks(dc, width, keyTop);
        if (_visual.AnimateKeys && _visual.ShowKeys) DrawKeyFlashes(dc, width, height);
        if (_visual.ShowHalo) DrawImpactLine(dc, width, keyTop);
        if (_visual.ShowKeys) DrawKeyboard(dc, width, height, lane, keyTop);
        if (_visual.ShowWatermark) DrawWatermark(dc, width, height);
        if (_visual.ShowCounter) DrawCounter(dc, width);
        dc.Pop(); dc.Pop();
    }

    private void DrawStars(DrawingContext dc, double width, double height)
    {
        // The star field is cleared on resize; rebuilding it here every frame made the stars flicker like noise.
        if (_stars.Count == 0) RebuildStars(width, height);
        foreach (var star in _stars)
        {
            var twinkle = .35 + .65 * (.5 + .5 * Math.Sin(_elapsed * star.Speed + star.Phase));
            var alpha = (byte)(star.Alpha * twinkle); var brush = Brush(Color.FromArgb(alpha, 222, 191, 255));
            dc.DrawEllipse(brush, null, new Point(star.X, star.Y), star.Size, star.Size);
        }
    }

    private void RebuildStars(double width, double height)
    {
        _stars.Clear(); var count = (int)Math.Clamp(width * height / 8200, 65, 180);
        for (var i = 0; i < count; i++) _stars.Add(new Star(_random.NextDouble() * width, _random.NextDouble() * height, .45 + _random.NextDouble() * 1.2, (byte)(18 + _random.Next(48)), .5 + _random.NextDouble() * 2.0, _random.NextDouble() * 7));
    }

    private static void DrawLanes(DrawingContext dc, double width, double height, double lane)
    {
        for (var i = 0; i <= KeyCount; i++)
        {
            var pitch = FirstPitch + i;
            var alpha = pitch % 12 == 0 ? 23 : pitch % 12 is 2 or 4 or 7 or 9 or 11 ? 10 : 5;
            var color = Color.FromArgb((byte)alpha, 186, 141, 255);
            dc.DrawLine(new Pen(Brush(color), pitch % 12 == 0 ? 1 : .6), new Point(i * lane, 0), new Point(i * lane, height));
        }
    }

    private void DrawNotes(DrawingContext dc, double width, double hitY, double lane)
    {
        if (!_visual.ShowNotes || !_playing) return;
        var noteSpeed = FallSpeed * _visual.NoteFallSpeed / 550;
        var lookBehind = hitY / noteSpeed + 1;
        // Notes are sorted by start time: anything still on screen started no earlier than (position - 1 s - longest note).
        var latestStart = _position + lookBehind;
        for (var i = NoteTimeline.FirstIndexAtOrAfter(_notes, _position - 1 - _maxNoteDuration); i < _notes.Count; i++)
        {
            var note = _notes[i];
            if (note.Start > latestStart) break;
            if (note.Pitch < FirstPitch || note.Pitch >= FirstPitch + KeyCount || note.End < _position - 1) continue;
            var noteHeight = Math.Clamp(note.Duration * noteSpeed, 16, hitY * .9);
            var bottom = hitY - (note.Start - _position) * noteSpeed;
            var top = bottom - noteHeight;
            if (top > hitY || bottom < 0) continue;
            var rect = new Rect((note.Pitch - FirstPitch) * lane + lane * .1, top, lane * .8, noteHeight);
            var color = note.Played ? Color.FromRgb(82, 237, 208) : note.Missed ? Color.FromRgb(255, 83, 113) : NoteColor(note.Pitch);
            DrawConfiguredNote(dc, rect, color, note.Played ? .42 : 1);
        }
    }

    private void DrawConfiguredNote(DrawingContext dc, Rect r, Color color, double opacity)
    {
        color = AdjustColor(color);
        var radius = Math.Min(r.Height / 2, Math.Min(r.Width / 2, 2 + _visual.NoteRoundness / 100 * 12));
        var bloom = _visual.BloomSize / 100;
        var glow = _visual.NoteGlow / 100 * _visual.BloomIntensity / 65;
        var outer = 2 + bloom * 10;
        dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(16 * opacity * glow), color.R, color.G, color.B)), null, Inflate(r, outer * 1.8), radius + outer, radius + outer);
        dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(34 * opacity * glow), color.R, color.G, color.B)), null, Inflate(r, outer * .75), radius + outer * .6, radius + outer * .6);
        dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(64 * opacity * _visual.NoteTint / 78), color.R, color.G, color.B)), null, r, radius, radius);
        var outline = Alpha(235 * opacity * _visual.NoteEdge / 100);
        if (_visual.NoteEdge > 0)
            dc.DrawRoundedRectangle(null, new Pen(Brush(Color.FromArgb(outline, color.R, color.G, color.B)), .4 + _visual.NoteEdgeWidth / 45), Inflate(r, -.7), radius, radius);
        if (_visual.NoteRefraction > 0)
        {
            var fringe = Alpha(170 * opacity * _visual.NoteRefraction / 100);
            dc.DrawLine(new Pen(Brush(Color.FromArgb(fringe, 255, 255, 255)), 1), new Point(r.X + 2, r.Y + 3), new Point(r.X + 2, r.Bottom - 3));
        }
        if (_visual.Notes3D && r.Height > 20)
        {
            var inner = new Rect(r.X + 3, r.Y + 4, Math.Max(2, r.Width - 6), Math.Max(3, r.Height - 8));
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(Alpha(120 * opacity), 10, 7, 18)), null, inner, Math.Min(radius, inner.Width / 2), Math.Min(radius, inner.Width / 2));
            dc.DrawLine(new Pen(Brush(Color.FromArgb(Alpha(165 * opacity), 255, 250, 255)), 1), new Point(r.X + 4, r.Y + 5), new Point(r.X + 4, r.Bottom - 5));
        }
    }

    private Color NoteColor(int pitch)
    {
        var t = Math.Clamp((pitch - FirstPitch) / (double)(KeyCount - 1), 0, 1);
        var color = _visual.Palette switch
        {
            "Aurora" => Blend(ParseColor(_visual.NoteColorStart, Colors.DeepSkyBlue), ParseColor(_visual.NoteColorEnd, Colors.MediumPurple), t),
            "Fire" => Blend(Color.FromRgb(255, 204, 72), Color.FromRgb(255, 53, 91), t),
            "Ocean" => Blend(Color.FromRgb(70, 246, 237), Color.FromRgb(55, 106, 255), t),
            "Violet" => Blend(Color.FromRgb(161, 94, 255), Color.FromRgb(255, 70, 196), t),
            "Custom" => Blend(ParseColor(_visual.NoteColorStart, Colors.DeepSkyBlue), ParseColor(_visual.NoteColorEnd, Colors.MediumPurple), t),
            _ => ColorFromHue(Hue(pitch) + 24)
        };
        return color;
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
        foreach (var trail in _liveTrails)
        {
            if (!_visual.ShowNotes) break;
            var y = 28 + trail.Age * _visual.NoteFallSpeed;
            var tailY = Math.Max(0, y - 28 - trail.HeldSeconds * _visual.NoteFallSpeed);
            var bottom = Math.Min(hitY + 6, y);
            if (bottom <= tailY) continue;
            var opacity = Math.Clamp(1 - tailY / Math.Max(1, hitY + 18), .08, 1) * _visual.NoteTint / 100;
            var r = new Rect((trail.Pitch - FirstPitch) * lane + lane * .03, tailY, lane * .94, bottom - tailY);
            DrawConfiguredNote(dc, r, NoteColor(trail.Pitch), opacity);
        }
    }

    private void DrawSparks(DrawingContext dc, double width, double height)
    {
        foreach (var particle in _sparks)
        {
            var fade = Math.Clamp(1 - particle.Age / particle.Life, 0, 1);
            var alpha = Alpha(225 * fade * _visual.ParticleGlow / 100); var size = particle.Size * (.55 + fade * .55);
            var color = Color.FromArgb(alpha, particle.Color.R, particle.Color.G, particle.Color.B);
            var glow = Color.FromArgb((byte)(alpha * .3), particle.Color.R, particle.Color.G, particle.Color.B);
            var bloom = 1.5 + _visual.BloomSize / 32;
            dc.DrawEllipse(Brush(glow), null, new Point(particle.X, particle.Y), size * bloom, size * bloom);
            dc.DrawEllipse(Brush(color), null, new Point(particle.X, particle.Y), size, size);
        }
    }

    private void DrawKeyFlashes(DrawingContext dc, double width, double height)
    {
        // Keep key-light animation independent of the optional ember-particle layer.
        foreach (var pitch in _pressed)
        {
            if (pitch < FirstPitch || pitch >= FirstPitch + KeyCount) continue;
            var x = (pitch - FirstPitch + .5) * (width / KeyCount);
            var brush = new LinearGradientBrush { StartPoint = new Point(x / width, .72), EndPoint = new Point(x / width, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 51, 212), 0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(42, 255, 51, 212), .7)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 51, 212), 1)); brush.Freeze();
            dc.DrawRectangle(brush, null, new Rect(x - 24, 0, 48, height));
        }
    }

    private void DrawImpactLine(DrawingContext dc, double width, double y)
    {
        var baseColor = ParseColor(_visual.HaloColor, ColorFromHue(266));
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        rainbow.GradientStops.Add(new GradientStop(NoteColor(28), 0)); rainbow.GradientStops.Add(new GradientStop(baseColor, .51)); rainbow.GradientStops.Add(new GradientStop(NoteColor(100), 1)); rainbow.Freeze();
        var center = new Point(width / 2, y);
        var glowWidth = 8 + _visual.BloomSize / 4; var haloAlpha = Alpha(20 + _visual.BloomIntensity * .8);
        var halo = new Pen(Brush(Color.FromArgb(haloAlpha, baseColor.R, baseColor.G, baseColor.B)), glowWidth * 2.5); halo.Freeze();
        dc.DrawLine(halo, new Point(0, y), new Point(width, y));
        dc.DrawLine(new Pen(Brush(Color.FromArgb(60, baseColor.R, baseColor.G, baseColor.B)), glowWidth), new Point(0, y), new Point(width, y));
        dc.DrawLine(new Pen(rainbow, 2.5), new Point(0, y), new Point(width, y));
        dc.DrawEllipse(Brush(Color.FromArgb(82, 244, 89, 210)), null, center, 170, 8);
    }

    private void DrawKeyboard(DrawingContext dc, double width, double height, double lane, double top)
    {
        dc.DrawRectangle(Brush(Color.FromArgb(248, 8, 8, 15)), null, new Rect(0, top, width, height - top));
        var whites = WhitePitches; var whiteWidth = width / whites.Length;
        var glowPen = new Pen(Brush(Color.FromArgb((byte)(30 + _visual.KeyLighting * .95), 246, 92, 255)), 5 + _visual.BloomSize / 10); dc.DrawLine(glowPen, new Point(0, top + 1), new Point(width, top + 1));
        for (var i = 0; i < whites.Length; i++)
        {
            var pitch = whites[i]; var rect = new Rect(i * whiteWidth, top + 5, whiteWidth - 1, height - top - 5);
            if (_pressed.Contains(pitch))
            {
                dc.DrawRoundedRectangle(Brush(Color.FromArgb((byte)(85 + _visual.KeyLighting * 1.7), 255, 61, 221)), null, Inflate(new Rect(rect.X - 5, top - 2, rect.Width + 10, rect.Height + 8), 3), 8, 8);
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(255, 248, 130, 255)), new Pen(Brush(Color.FromArgb(255, 255, 213, 255)), 1), rect, 3, 3);
            }
            else dc.DrawRoundedRectangle(KeyWhite, new Pen(Brush(Color.FromArgb(170, 68, 72, 94)), .7), rect, 1.4, 1.4);
            if (pitch % 12 == 0) DrawLabel(dc, NoteLabel(pitch), new Point(rect.X + rect.Width / 2, height - 20), 10, _pressed.Contains(pitch) ? Colors.White : Color.FromRgb(77, 79, 102), true);
        }
        for (var pitch = FirstPitch; pitch < FirstPitch + KeyCount; pitch++)
        {
            if (!IsBlack(pitch)) continue;
            var x = WhitesBelow[pitch] * whiteWidth - whiteWidth * .29;
            var rect = new Rect(x, top + 4, whiteWidth * .58, (height - top) * (.52 + _visual.KeyOverhang / 100 * .35));
            if (_pressed.Contains(pitch))
            {
                dc.DrawRoundedRectangle(Brush(Color.FromArgb(165, 255, 40, 225)), null, Inflate(rect, 5), 7, 7);
                dc.DrawRoundedRectangle(Brush(Color.FromRgb(224, 95, 255)), new Pen(Brush(Colors.White), 1), rect, 5, 5);
            }
            else dc.DrawRoundedRectangle(KeyBlack, new Pen(Brush(Color.FromArgb(200, 72, 66, 96)), .75), rect, 3, 3);
        }
    }

    private void DrawFlames(DrawingContext dc, double width, double keyTop, double lane)
    {
        foreach (var trail in _liveTrails.Where(n => n.Hit && n.Age < 1.4))
        {
            var age = trail.Age - Math.Max(0, (keyTop - 28) / _visual.NoteFallSpeed);
            if (age < 0 || age > 1.4) continue;
            var x = (trail.Pitch - FirstPitch + .5) * lane;
            var pulse = .65 + .35 * Math.Sin(_elapsed * 13 + trail.Pitch);
            var radius = (8 + 30 * pulse) * Math.Clamp(1 - age / 1.4, 0, 1);
            var flame = new RadialGradientBrush
            {
                Center = new Point(.5, .84), GradientOrigin = new Point(.5, .84), RadiusX = .8, RadiusY = 1.1,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 220), 0));
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(105, 255, 178, 73), .28));
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(42, 255, 83, 54), .72));
            flame.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 35, 95), 1)); flame.Freeze();
            dc.DrawEllipse(flame, null, new Point(x, keyTop + 3), radius, 20 + radius * 1.35);
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
        var text = new FormattedText($"{_pressed.Count:00} KEYS", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
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
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { _stars.Clear(); base.OnRenderSizeChanged(sizeInfo); InvalidateVisual(); }
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
    private static Brush Brush(Color color) => Freeze(new SolidColorBrush(color));
    private static Brush Freeze(Brush brush) { if (brush.CanFreeze) brush.Freeze(); return brush; }
    private static Rect Inflate(Rect r, double amount) => new(r.X - amount, r.Y - amount, Math.Max(1, r.Width + amount * 2), Math.Max(1, r.Height + amount * 2));
    private sealed record Star(double X, double Y, double Size, byte Alpha, double Speed, double Phase);
    private sealed class Spark { public double X, Y, Vx, Vy, Life, Age, Size; public Color Color; }
    private sealed class LiveTrail { public int Pitch; public double Age, HeldSeconds; public bool KeyDown = true, Released, Hit; }
}
