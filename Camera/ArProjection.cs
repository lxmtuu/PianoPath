namespace PianoPath;

/// <summary>One corner of a shape drawn over the camera picture: its place on the output (pixels) and the homogeneous weight that place came from.</summary>
/// <param name="X">Output pixel, from the left.</param>
/// <param name="Y">Output pixel, from the top.</param>
/// <param name="W">The weight of the projective map at that corner, always above <see cref="ArProjection.MinW"/>. The GPU uses it to interpolate across the quad in perspective, so a note keeps its true shape instead of being sheared along the diagonal.</param>
internal readonly record struct ArCorner(double X, double Y, double W);

/// <summary>A rectangle of the keyboard plane as it appears on the output: back-left, back-right, front-right, front-left.</summary>
internal readonly record struct ArPatch(ArCorner BackLeft, ArCorner BackRight, ArCorner FrontRight, ArCorner FrontLeft)
{
    internal ArCorner this[int index] => index switch { 0 => BackLeft, 1 => BackRight, 2 => FrontRight, 3 => FrontLeft, _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    /// <summary>Area in output pixels² (shoelace); positive for a quad that is not turned inside out.</summary>
    internal double Area
    {
        get
        {
            double sum = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = this[i]; var b = this[(i + 1) % 4];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(sum) / 2;
        }
    }
}

/// <summary>
/// Puts rectangles of the keyboard plane — in the calibration's keyboard coordinates, <c>u</c> across the keys and
/// <c>v</c> from the back of the keys (0) to their front (1), <c>v</c> below 0 being the air behind the keys where notes
/// come from — onto the output.
/// </summary>
/// <remarks>
/// <para>This is done on the CPU: a screenful of notes is a few hundred corners, it is exact in doubles, and it can be
/// tested without a GPU. The GPU only receives the corners and draws them.</para>
/// <para>A homography sends one line of the plane to infinity (the horizon). A rectangle that reaches it would put
/// corners at absurd places or behind the camera, so the part of the rectangle beyond <see cref="MinW"/> is cut off
/// along <c>v</c>, and a rectangle entirely beyond it is not drawn.</para>
/// </remarks>
internal static class ArProjection
{
    /// <summary>
    /// Smallest weight a corner may have. The map is scaled so the weight is 1 at the keyboard's back-left corner; a
    /// weight of 0.1 is ten times closer to the horizon than that corner, where the picture is magnified ten times.
    /// </summary>
    internal const double MinW = 0.1;

    /// <summary>A corner further than this many pixels from the output is refused: no real picture needs it and floats lose their precision there.</summary>
    internal const double MaxCoordinate = 1e6;

    /// <summary>
    /// The patch for the keyboard rectangle <c>[u0,u1] × [v0,v1]</c> (<c>u0 &lt; u1</c>, <c>v0 &lt; v1</c>), or false when
    /// nothing of it can be drawn. <paramref name="vBack"/> and <paramref name="vFront"/> are the <c>v</c> range actually
    /// drawn, inside the one asked for.
    /// </summary>
    internal static bool TryProject(ArCalibration calibration, ArView view, double u0, double u1, double v0, double v1, out ArPatch patch, out double vBack, out double vFront)
    {
        patch = default; vBack = v0; vFront = v1;
        if (!view.Valid || !double.IsFinite(u0) || !double.IsFinite(u1) || !double.IsFinite(v0) || !double.IsFinite(v1) || !(u0 < u1) || !(v0 < v1)) return false;
        var map = calibration.ToImage;
        double g = map[2, 0], h = map[2, 1], i = map[2, 2];
        double lo = v0, hi = v1;
        foreach (var u in new[] { u0, u1 })
        {
            // the weight along this edge is rest + h·v, and must stay at least MinW for every v kept
            var rest = g * u + i;
            if (h > 1e-12) lo = Math.Max(lo, (MinW - rest) / h);
            else if (h < -1e-12) hi = Math.Min(hi, (MinW - rest) / h);
            else if (rest < MinW) return false;
        }
        if (!(lo < hi - 1e-9)) return false;
        if (!TryCorner(map, view, u0, lo, out var backLeft) || !TryCorner(map, view, u1, lo, out var backRight)
            || !TryCorner(map, view, u1, hi, out var frontRight) || !TryCorner(map, view, u0, hi, out var frontLeft)) return false;
        patch = new ArPatch(backLeft, backRight, frontRight, frontLeft);
        vBack = lo; vFront = hi;
        return true;
    }

    private static bool TryCorner(Homography map, ArView view, double u, double v, out ArCorner corner)
    {
        corner = default;
        var w = map[2, 0] * u + map[2, 1] * v + map[2, 2];
        if (!(w >= MinW * (1 - 1e-9))) return false;
        var x = (map[0, 0] * u + map[0, 1] * v + map[0, 2]) / w;
        var y = (map[1, 0] * u + map[1, 1] * v + map[1, 2]) / w;
        var (ox, oy) = view.ToOutput(x, y);
        if (!double.IsFinite(ox) || !double.IsFinite(oy) || Math.Abs(ox) > MaxCoordinate || Math.Abs(oy) > MaxCoordinate) return false;
        corner = new ArCorner(ox, oy, w);
        return true;
    }
}

/// <summary>How falling notes are laid over the keyboard.</summary>
/// <param name="FallSpeed">Key depths per second a note travels toward the keys (a key is 150 mm deep, so 1.5 is 225 mm/s).</param>
/// <param name="Reach">How many key depths ahead of the keys a note first shows. It is the same distance for every note, so what is on screen is always the next <c>Reach / FallSpeed</c> seconds.</param>
/// <param name="MinLength">Shortest a note is drawn, in key depths, so a staccato note is still a note.</param>
/// <param name="Gap">What is taken off a note's length so two notes of the same key do not touch, in key depths.</param>
/// <param name="WidthShare">How much of its key's width a note fills at full size, 0..1.</param>
/// <param name="WidthVelocity">How a note's width follows the strike's velocity (a <see cref="GraphVariable"/> over the simulation's 0..1 activity): flat keeps every note the same width, a graph makes hard strikes wider. Lives in the look, so presets carry it.</param>
/// <param name="FadeVelocity">How a note's fade-in follows the strike's velocity (a <see cref="GraphVariable"/> over the same input): flat keeps the fade the same for every note, a graph brings hard strikes in faster.</param>
internal sealed record ArNoteStyle(double FallSpeed = 1.5, double Reach = 4, double MinLength = .06, double Gap = .015, double WidthShare = .86,
    GraphVariable WidthVelocity = default, GraphVariable FadeVelocity = default)
{
    internal static readonly ArNoteStyle Default = new();

    /// <summary>True when the numbers can be used to draw anything.</summary>
    internal bool IsUsable => double.IsFinite(FallSpeed) && FallSpeed > 0 && double.IsFinite(Reach) && Reach > 0
        && double.IsFinite(MinLength) && MinLength > 0 && double.IsFinite(Gap) && Gap >= 0 && double.IsFinite(WidthShare) && WidthShare is > 0 and <= 1;

    /// <summary>How much of its key's width a note with this velocity fills, 0..1: the flat share, or the graph walked by the velocity.</summary>
    internal double WidthShareFor(double velocity) => Math.Clamp(WidthVelocity.Sample(velocity) * WidthShare, 0, 1);
}

/// <summary>A note on its way to the keys.</summary>
/// <param name="Patch">Where it is, on the output.</param>
/// <param name="Pitch">The key it is for.</param>
/// <param name="NoteIndex">Its place in the song's note list.</param>
/// <param name="Black">True for a black key's note; these are listed after every white one so they are drawn on top.</param>
/// <param name="Sounding">True from the moment the note reaches the keys until it ends.</param>
/// <param name="Fade">0..1, how far the note has come into view: it fades in over the last quarter of the reach instead of appearing from nothing.</param>
internal readonly record struct ArNoteShape(ArPatch Patch, int Pitch, int NoteIndex, bool Black, bool Sounding, double Fade);

/// <summary>
/// One key the player is pressing, or has just let go, as the render thread's simulation holds it: what a bar is drawn
/// from. A song's notes come out of the notes list; this is the player's own hands.
/// </summary>
/// <param name="Pitch">The key.</param>
/// <param name="Age">Seconds since the key went down.</param>
/// <param name="HeldSeconds">Seconds the key has been down: it stops growing when the finger leaves, so the bar stops stretching where the finger left it.</param>
/// <param name="Down">True while the finger is still on the key.</param>
/// <param name="Strength">How hard it was pressed, 0..1 (MIDI velocity), for the colour.</param>
internal readonly record struct ArLivePress(int Pitch, double Age, double HeldSeconds, bool Down, float Strength);

/// <summary>One bar of a pressed key: the patch to draw and how solid it is. There is no note behind it, so unlike
/// <see cref="ArNoteShape"/> it carries no index into a song.</summary>
/// <param name="Patch">Where on the output the bar goes: its leading edge on the back edge of the keys.</param>
/// <param name="Pitch">The key it belongs to.</param>
/// <param name="Fade">0..1: the opacity the bar is drawn with, falling as the bar stretches away from the key.</param>
/// <param name="Held">True while the key is still down.</param>
/// <param name="Strength">How hard it was pressed, 0..1, for the colour.</param>
internal readonly record struct ArLiveBar(ArPatch Patch, int Pitch, double Fade, bool Held, float Strength);

/// <summary>Works out where the notes of a song are on the picture for a moment of the song.</summary>
internal static class ArNotes
{
    /// <summary>How long the bar of a key that was let go stays on the picture after the finger left it, fading out.</summary>
    internal const double ReleaseLife = .35;

    /// <summary>
    /// Fills <paramref name="into"/> (cleared first) with the notes to draw at <paramref name="position"/> seconds: those that
    /// are within <see cref="ArNoteStyle.Reach"/> of the keys, or sounding now. A note's leading edge reaches the back of
    /// the keys at its start time and slides on across them while it sounds, shrinking as it is used up; a pitch the
    /// calibrated keyboard does not have is skipped.
    /// </summary>
    /// <param name="maxNoteDuration">The longest note in the list, to know how far back to look for one still sounding.</param>
    /// <param name="simulation">Maps a note's raw velocity through the velocity band before the style's graphs walk it; null maps velocity straight onto 0..1.</param>
    internal static void Build(ArCalibration calibration, ArView view, IReadOnlyList<NoteEvent> notes, double position, double maxNoteDuration, ArNoteStyle style, List<ArNoteShape> into, PianoSimulation? simulation = null)
    {
        into.Clear();
        if (!style.IsUsable || !double.IsFinite(position) || !view.Valid || notes.Count == 0) return;
        var layout = calibration.Layout;
        var until = position + style.Reach / style.FallSpeed;
        var first = NoteTimeline.FirstIndexAtOrAfter(notes, position - 1 - Math.Max(0, maxNoteDuration));
        for (var pass = 0; pass < 2; pass++)
        {
            var wantBlack = pass == 1;
            for (var index = first; index < notes.Count; index++)
            {
                var note = notes[index];
                if (note.Start > until) break;
                if (!layout.Contains(note.Pitch) || KeyboardLayout.IsBlack(note.Pitch) != wantBlack || note.End < position) continue;
                var length = Math.Max(note.Duration * style.FallSpeed - style.Gap, style.MinLength);
                var leading = -(note.Start - position) * style.FallSpeed;     // v of the leading edge, before the keys stop it
                if (leading < -style.Reach) continue;
                var near = Math.Min(leading, 0.0);
                var far = Math.Max(leading - length, -style.Reach);
                if (far >= near - 1e-9) continue;                              // used up
                var velocity = simulation?.ActivityForVelocity(note.Velocity) ?? Math.Clamp(note.Velocity / 127.0, 0, 1);
                var (left, right) = layout.Span(note.Pitch);
                var centre = (left + right) / 2; var half = (right - left) / 2 * style.WidthShareFor(velocity);
                if (!ArProjection.TryProject(calibration, view, centre - half, centre + half, far, near, out var patch, out _, out _)) continue;
                var fade = Math.Clamp((style.Reach + leading) / (style.Reach * .25), 0, 1) * style.FadeVelocity.Sample(velocity);
                into.Add(new ArNoteShape(patch, note.Pitch, index, wantBlack, note.Start <= position && note.End > position, fade));
            }
        }
    }

    /// <summary>
    /// Fills <paramref name="into"/> (cleared first) with a bar for every key the player is pressing, or has just let go
    /// of: the bar's leading edge sits on the back edge of the key (where the song's notes reach it) and its tail
    /// stretches away along the keyboard's own direction as the key is held, up to <see cref="ArNoteStyle.Reach"/>, fading
    /// as it stretches. Letting the key go stops the bar where the finger left it, dims it and takes it away
    /// <see cref="ReleaseLife"/> seconds later. It is the mark of the player's own hand, so it is drawn whatever the mode
    /// is — a song's notes to come are what Perform leaves out. A key the calibrated keyboard does not have is skipped.
    /// </summary>
    /// <param name="simulation">Maps a press's strength through the velocity band before the style's graphs walk it; null uses the strength as it is.</param>
    internal static void BuildLive(ArCalibration calibration, ArView view, IReadOnlyList<ArLivePress> presses, ArNoteStyle style, List<ArLiveBar> into, PianoSimulation? simulation = null)
    {
        into.Clear();
        if (!style.IsUsable || !view.Valid || presses.Count == 0) return;
        var layout = calibration.Layout;
        // black keys last, over the white ones, exactly as the notes are drawn
        for (var pass = 0; pass < 2; pass++)
        {
            var wantBlack = pass == 1;
            foreach (var press in presses)
            {
                if (!layout.Contains(press.Pitch) || KeyboardLayout.IsBlack(press.Pitch) != wantBlack) continue;
                if (!double.IsFinite(press.Age) || !double.IsFinite(press.HeldSeconds) || press.HeldSeconds <= 0) continue;
                var gone = 1.0;                                 // how much of the bar is left now the finger may have left the key
                if (!press.Down)
                {
                    var since = press.Age - press.HeldSeconds;
                    if (!(since >= 0) || since >= ReleaseLife) continue;   // let go and already gone, or an age that makes no sense
                    gone = .65 * Math.Clamp(1 - since / ReleaseLife, 0, 1);
                }
                var length = Math.Min(style.Reach, press.HeldSeconds * style.FallSpeed);
                if (length < style.MinLength) continue;         // a key that went down this instant has not left the key yet; its light says it is down
                var velocity = simulation?.ActivityForVelocity(press.Strength * 127) ?? Math.Clamp(press.Strength, 0, 1);
                var (left, right) = layout.Span(press.Pitch);
                var centre = (left + right) / 2; var half = (right - left) / 2 * style.WidthShareFor(velocity);
                if (!ArProjection.TryProject(calibration, view, centre - half, centre + half, -length, 0, out var patch, out _, out _)) continue;
                into.Add(new ArLiveBar(patch, press.Pitch, Math.Clamp(1 - length / style.Reach, .08, 1) * gone * style.FadeVelocity.Sample(velocity), press.Down, press.Strength));
            }
        }
    }
}
