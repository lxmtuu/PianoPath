using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The sheet layer: a grand staff drawn across the top of the stage, above the piano roll and following the
/// playhead. It is a reading aid rather than an engraving — each note is written on the staff its hand split
/// assigns it to, with ledger lines where it leaves the staff, bar lines on the beat grid and a ring on
/// whatever is sounding.
///
/// <para>
/// The geometry is plain arithmetic (<see cref="Step"/>, <see cref="Place"/>, <see cref="LedgerLines"/>,
/// <see cref="NoteX"/>), so the checks can state exactly where a note lands instead of comparing pixels.
/// <see cref="Draw"/> only turns those numbers into lines, ellipses and two clefs.
/// </para>
///
/// <para>
/// The clefs are the real Unicode glyphs (𝄞 U+1D11E and 𝄢 U+1D122) when the installed symbol font carries
/// them, and the staff's letter (G/F) when it does not: a missing font must not leave empty boxes on stage.
/// Either way the characters are music notation, not interface text, which is why they are not in the string
/// tables; everything the user reads as a sentence comes from <c>Localization</c> as usual.
/// </para>
/// </summary>
internal static class SheetLayer
{
    /// <summary>Diatonic step of the bottom line of each staff: E4 on the treble staff, G2 on the bass staff.</summary>
    internal const int TrebleBottomStep = 4 * 7 + 2;
    internal const int BassBottomStep = 2 * 7 + 4;

    /// <summary>How many diatonic steps the five lines of a staff span, bottom line to top line.</summary>
    internal const int StaffSteps = 8;

    /// <summary>A staff line every <see cref="GapRatio"/> of the band's height, between <see cref="GapMin"/> and <see cref="GapMax"/> pixels.</summary>
    internal const double GapRatio = .052;
    internal const double GapMin = 4;
    internal const double GapMax = 9;

    private const string TrebleGlyph = "\U0001D11E";
    private const string BassGlyph = "\U0001D122";

    /// <summary>The unicode letter of each pitch class when written with sharps, plus whether it needs a sharp sign.</summary>
    private static readonly (int Letter, bool Sharp)[] Spelling =
    [
        (0, false), (0, true), (1, false), (1, true), (2, false), (3, false), (3, true),
        (4, false), (4, true), (5, false), (5, true), (6, false)
    ];

    private static readonly Typeface MusicTypeface = new(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Dictionary<(string Text, double Size, uint Color, int Dpi), FormattedText> Cache = [];
    private static readonly bool GlyphsAvailable = ProbeGlyphs();

    /// <summary>True when the installed symbol font carries the clef glyphs, so <see cref="ClefText"/> draws them.</summary>
    internal static bool MusicGlyphsAvailable => GlyphsAvailable;

    private static bool ProbeGlyphs()
    {
        try { return MusicTypeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey(0x1D11E); }
        catch { return false; }
    }

    /// <summary>
    /// The diatonic step of a pitch in scientific notation: C-1 is 0, so middle C is 28 (four octaves of seven
    /// steps) and B4 is 35. A black key is spelled as the white key below it (C♯ sits on the C line), which is
    /// how a piano-roll's worth of pitches can be written without a key signature and still land on the right
    /// line. The constants above are this function's values for the bottom lines of the two staves.
    /// </summary>
    internal static int Step(int pitch)
    {
        var clamped = Math.Clamp(pitch, 0, 127);
        return (clamped / 12 - 1) * 7 + Spelling[clamped % 12].Letter;
    }

    /// <summary>True when the written note needs a sharp sign beside its head.</summary>
    internal static bool NeedsSharp(int pitch) => Spelling[Math.Clamp(pitch, 0, 127) % 12].Sharp;

    /// <summary>
    /// Which staff a pitch belongs to — 0 treble, 1 bass, the split deciding — and how many steps its note
    /// head sits above that staff's bottom line: 0 the bottom line, 8 the top line, negative below the staff
    /// and above eight above it. The split is the same one that colours the roll, so sheet and roll agree.
    /// </summary>
    internal static (int Staff, int RelativeStep) Place(int pitch, double handSplit)
    {
        var staff = pitch >= handSplit ? 0 : 1;
        return (staff, Step(pitch) - (staff == 0 ? TrebleBottomStep : BassBottomStep));
    }

    /// <summary>
    /// The ledger lines a note needs, as step offsets from the bottom line of its staff: even offsets are
    /// lines, so a note in the space above the staff needs none while one on the line above needs one through
    /// its head, and a note sitting on a line below the staff needs exactly that line.
    /// </summary>
    internal static IReadOnlyList<int> LedgerLines(int relativeStep)
    {
        var lines = new List<int>();
        if (relativeStep < 0)
        {
            var lowest = relativeStep % 2 == 0 ? relativeStep : relativeStep + 1;
            for (var offset = -2; offset >= lowest; offset -= 2) lines.Add(offset);
        }
        else if (relativeStep > StaffSteps)
        {
            var highest = relativeStep % 2 == 0 ? relativeStep : relativeStep - 1;
            for (var offset = StaffSteps + 2; offset <= highest; offset += 2) lines.Add(offset);
        }
        return lines;
    }

    /// <summary>A note held longer than a slow beat is written hollow, as half and whole notes are.</summary>
    internal static bool HollowHead(double durationSeconds) => durationSeconds >= 1.2;

    /// <summary>Whether a note is short enough to be written filled, and therefore to carry a stem.</summary>
    internal static bool HasStem(double durationSeconds) => !HollowHead(durationSeconds);

    /// <summary>
    /// A stem points up when its note sits below the middle of the staff and down when it sits above, so the
    /// stems of a chord do not all lean the same way and stay readable.
    /// </summary>
    internal static bool StemUp(int relativeStep) => relativeStep < StaffSteps / 2;

    /// <summary>The clef of a staff: the musical glyph when the font has it, otherwise the staff's letter.</summary>
    internal static string ClefText(int staff)
    {
        if (staff == 1) return GlyphsAvailable ? BassGlyph : "F";
        return GlyphsAvailable ? TrebleGlyph : "G";
    }

    /// <summary>Where a note that starts at <paramref name="start"/> is drawn: the window's left edge is the playhead minus a quarter of its own width.</summary>
    internal static double NoteX(double start, double windowStart, double secondsVisible, Rect area) =>
        area.X + (start - windowStart) / Math.Max(.001, secondsVisible) * area.Width;

    /// <summary>The left edge of the visible window: the playhead sits a quarter of the way in from the left.</summary>
    internal static double WindowStart(double position, double secondsVisible) => position - secondsVisible * .25;

    /// <summary>The y of the bottom line of a staff: treble in the upper half of the band, bass below it.</summary>
    internal static double StaffBottom(Rect area, double gap, int staff) => area.Y + area.Height * (staff == 0 ? .16 : .60) + 4 * gap;

    /// <summary>The height of the staff band for a stage: a share of the height, never so tall that it hides the roll.</summary>
    internal static Rect Band(double width, double height, double share)
    {
        var bandHeight = Math.Clamp(height * share / 100, 90, height * .46);
        return new Rect(width * .04, height * .03, width * .92, bandHeight);
    }

    /// <summary>
    /// Draws the layer. <paramref name="beats"/> is the metronome grid the song came with (empty for a live
    /// performance, in which case the staves are drawn without bar lines) and <paramref name="beatsPerBar"/>
    /// says which beats start a measure.
    /// </summary>
    internal static void Draw(
        DrawingContext dc, Rect area, IReadOnlyList<NoteEvent> notes, double position, double handSplit,
        IReadOnlyList<double> beats, int beatsPerBar, double secondsVisible, Color ink, Color accent, double opacity, double pixelsPerDip)
    {
        if (area.Width < 40 || area.Height < 24) return;
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(41 * opacity, 0, 235), 6, 7, 12)),
            new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(70 * opacity, 0, 200), ink.R, ink.G, ink.B)), 1), area, 8, 8);

        var windowStart = WindowStart(position, secondsVisible);
        var dim = Color.FromArgb((byte)Math.Clamp(150 * opacity, 20, 210), ink.R, ink.G, ink.B);
        var gap = Math.Clamp(area.Height * GapRatio, GapMin, GapMax);
        var half = gap / 2;
        var lineLeft = area.X + Math.Clamp(area.Height * .30, 18, 44);
        var lineRight = area.Right - 8;

        for (var staff = 0; staff < 2; staff++)
        {
            var bottom = StaffBottom(area, gap, staff);
            for (var line = 0; line < 5; line++)
                dc.DrawLine(new Pen(new SolidColorBrush(dim), 1), new Point(lineLeft, bottom - line * gap), new Point(lineRight, bottom - line * gap));
            // The clef glyph is several times taller than the spacing, so it is centred on the staff.
            var clef = Text(ClefText(staff), gap * 4.4, ink, pixelsPerDip);
            dc.DrawText(clef, new Point(area.X + 6, bottom - 4 * gap - (clef.Height - 4 * gap) * .62));
        }
        // One brace linking the two staves into a grand staff.
        var braceTop = StaffBottom(area, gap, 0) - 4 * gap;
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(dim), 1.4), new Rect(area.X + 2, braceTop, 3.5, StaffBottom(area, gap, 1) - braceTop), 1.5, 1.5);

        // Bar lines: a solid one where a measure starts, a fainter one on the other beats of the bar.
        var lastBarX = double.NegativeInfinity;
        for (var index = 0; index < beats.Count; index++)
        {
            var beat = beats[index];
            if (beat < windowStart || beat > windowStart + secondsVisible) continue;
            var startsMeasure = index % Math.Max(1, beatsPerBar) == 0;
            var x = NoteX(beat, windowStart, secondsVisible, area);
            if (startsMeasure && x - lastBarX < 6) continue;      // two downbeats inside one pixel must not darken it
            if (startsMeasure) lastBarX = x;
            var alpha = (byte)Math.Clamp((startsMeasure ? 120 : 55) * opacity, 0, 220);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B)), startsMeasure ? 1.3 : 1);
            dc.DrawLine(pen, new Point(x, StaffBottom(area, gap, 0) - 4 * gap), new Point(x, StaffBottom(area, gap, 1)));
        }

        // The playhead, drawn through both staves so the reader can see where the music is.
        var playheadX = NoteX(position, windowStart, secondsVisible, area);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(200 * opacity, 0, 240), accent.R, accent.G, accent.B)), 1.6),
            new Point(playheadX, area.Y + 4), new Point(playheadX, area.Bottom - 4));

        var headWidth = Math.Clamp(gap * 1.35, 3.5, 12);
        var first = NoteTimeline.FirstIndexAtOrAfter(notes, windowStart);
        for (var index = first; index < notes.Count; index++)
        {
            var note = notes[index];
            if (note.Start > windowStart + secondsVisible) break;
            var (staff, relative) = Place(note.Pitch, handSplit);
            var bottom = StaffBottom(area, gap, staff);
            var y = bottom - relative * half;
            var x = NoteX(note.Start, windowStart, secondsVisible, area);
            var colour = NoteColour(note, position, dim, ink, accent);
            var brush = new SolidColorBrush(colour);
            foreach (var offset in LedgerLines(relative))
                dc.DrawLine(new Pen(brush, 1), new Point(x - headWidth, bottom - offset * half), new Point(x + headWidth, bottom - offset * half));
            // The head is tilted the way a hand-written note is, so neighbouring seconds stay distinguishable.
            var head = new Point(x, y + half * .35);
            dc.PushTransform(new RotateTransform(-18, head.X, head.Y));
            var hollow = HollowHead(note.Duration);
            dc.DrawEllipse(hollow ? null : brush, new Pen(brush, 1.1), head, headWidth * .62, half * .95);
            if (note.Start <= position && note.End >= position)
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(160 * opacity, 0, 220), accent.R, accent.G, accent.B)), 1.2), head, headWidth, half * 1.4);
            dc.Pop();
            if (NeedsSharp(note.Pitch))
                dc.DrawText(Text("♯", gap * 1.6, colour, pixelsPerDip), new Point(x - headWidth * 1.9, y - gap * .8));
            if (HasStem(note.Duration))
            {
                var up = StemUp(relative);
                var stemX = x + (up ? headWidth * .55 : -headWidth * .55);
                dc.DrawLine(new Pen(brush, 1.2), new Point(stemX, y), new Point(stemX, up ? y - gap * 3.2 : y + gap * 3.2));
            }
        }
    }

    /// <summary>
    /// The colour of a written note: red once the run missed it, the accent while it sounds, and the plain ink
    /// of the staff otherwise — a played note keeps full ink, since a faded sheet is harder to read back.
    /// </summary>
    internal static Color NoteColour(NoteEvent note, double position, Color dim, Color ink, Color accent)
    {
        if (note.Missed) return Color.FromRgb(255, 118, 130);
        if (note.Start <= position && note.End >= position) return accent;
        return note.Played ? dim : ink;
    }

    private static FormattedText Text(string text, double size, Color colour, double pixelsPerDip)
    {
        var key = (text, Math.Round(size, 1), (uint)(colour.A << 24 | colour.R << 16 | colour.G << 8 | colour.B), (int)Math.Round(pixelsPerDip * 100));
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (Cache.Count > 256) Cache.Clear();
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MusicTypeface, size, new SolidColorBrush(colour), pixelsPerDip);
        Cache[key] = formatted;
        return formatted;
    }
}
