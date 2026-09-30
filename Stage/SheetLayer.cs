using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The sheet layer: a grand staff drawn across the top of the stage, above the piano roll and following the
/// playhead. Each note is written on the staff its hand split assigns it to, at the place the song's key spells
/// it, with a key signature at the head of both staves, the accidentals the bars really need, ledger lines where
/// a note leaves the staff, bar lines on the beat grid, short notes beamed within their beat (or flagged when
/// they stand alone), a rest wherever one hand is silent while the other plays, and a ring on whatever is
/// sounding.
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

    private static readonly Typeface MusicTypeface = new(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Dictionary<(string Text, double Size, uint Color, int Dpi), FormattedText> Cache = [];
    private static readonly bool GlyphsAvailable = ProbeGlyphs(0x1D11E);
    private static readonly bool SignsAvailable = ProbeGlyphs(0x266F);

    /// <summary>True when the installed symbol font carries the clef glyphs, so <see cref="ClefText"/> draws them.</summary>
    internal static bool MusicGlyphsAvailable => GlyphsAvailable;

    /// <summary>True when the font carries the sharp, flat and natural signs, so notes are signed with music glyphs.</summary>
    internal static bool MusicSignsAvailable => SignsAvailable;

    private static bool ProbeGlyphs(int codePoint)
    {
        try { return MusicTypeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CharacterToGlyphMap.ContainsKey(codePoint); }
        catch { return false; }
    }

    /// <summary>
    /// The diatonic step of a pitch in scientific notation: C-1 is 0, so middle C is 28 (four octaves of seven
    /// steps) and B4 is 35. Which line a black key lands on is the key's business: without a signature it is
    /// spelled as the white key below it (C♯ sits on the C line), while F major writes B♭ on the B line.
    /// The constants above are this function's values for the bottom lines of the two staves.
    /// </summary>
    internal static int Step(int pitch) => Step(pitch, MusicKey.CMajor);

    /// <summary>The diatonic step of a pitch as this key spells it.</summary>
    internal static int Step(int pitch, MusicKey key)
    {
        var (letter, octave, _) = key.Spell(pitch);
        return octave * 7 + letter;
    }

    /// <summary>True when the written note needs a sharp sign beside its head in a song with no signature.</summary>
    internal static bool NeedsSharp(int pitch) => NeedsSign(pitch, MusicKey.CMajor);

    /// <summary>True when this key spells the pitch with a sign of its own — a sharp, a flat or a natural.</summary>
    internal static bool NeedsSign(int pitch, MusicKey key) => key.Spell(pitch).Alteration != 0;

    /// <summary>
    /// Which staff a pitch belongs to — 0 treble, 1 bass, the split deciding — and how many steps its note
    /// head sits above that staff's bottom line: 0 the bottom line, 8 the top line, negative below the staff
    /// and above eight above it. The split is the same one that colours the roll, so sheet and roll agree.
    /// </summary>
    internal static (int Staff, int RelativeStep) Place(int pitch, double handSplit) => Place(pitch, handSplit, MusicKey.CMajor);

    /// <summary>Which staff a pitch belongs to: 0 treble, 1 bass, <paramref name="handSplit"/> deciding.</summary>
    internal static int StaffOf(int pitch, double handSplit) => pitch >= handSplit ? 0 : 1;

    /// <summary>Where a pitch is written on which staff, as this key spells it.</summary>
    internal static (int Staff, int RelativeStep) Place(int pitch, double handSplit, MusicKey key)
    {
        var staff = StaffOf(pitch, handSplit);
        return (staff, Step(pitch, key) - (staff == 0 ? TrebleBottomStep : BassBottomStep));
    }

    /// <summary>
    /// Where a signature sign sits on a staff, as a step from that staff's bottom line. Every letter has its own
    /// place — F on the top line of a treble staff, C in the third space, and so on — and the bass staff writes
    /// the same letters two steps lower, which is what makes one table serve both staves.
    /// </summary>
    internal static int SignatureStep(int staff, int letter)
    {
        var treble = letter switch { 0 => 5, 1 => 6, 2 => 7, 3 => 8, 4 => 9, 5 => 3, _ => 4 };
        return staff == 1 ? treble - 2 : treble;
    }

    /// <summary>The spacing between two staff lines for a band of this height.</summary>
    internal static double StaffGap(Rect area) => Math.Clamp(area.Height * GapRatio, GapMin, GapMax);

    /// <summary>The room the clef takes at the head of the staff, in pixels.</summary>
    internal static double ClefSpace(Rect area) => Math.Clamp(area.Height * .30, 18, 44);

    /// <summary>How much room a signature takes between the clef and the music, in pixels.</summary>
    internal static double SignatureWidth(MusicKey key, double gap) => key.Count == 0 ? 0 : gap * (1.3 + 1.2 * key.Count);

    /// <summary>Everything written in time starts after this much of the band: the clef and the signature.</summary>
    internal static double LeftInset(Rect area, MusicKey key) => ClefSpace(area) + SignatureWidth(key, StaffGap(area));

    /// <summary>
    /// Everything the sheet works out for a song before it draws anything: the accidental each note is written
    /// with, the beams its short notes share and the silences of both hands. None of it depends on the practice
    /// state or on where the playhead is, so a renderer can work it out once per song instead of once per frame.
    /// </summary>
    internal sealed record SheetPlan(NoteAccidental[] Accidentals, IReadOnlyList<Beam> Beams, IReadOnlyList<RestGap> Rests);

    /// <summary>Works out the plan of a song: its accidentals, its beams and its rests.</summary>
    internal static SheetPlan Plan(IReadOnlyList<NoteEvent> notes, IReadOnlyList<double> beats, int beatsPerBar, double handSplit, MusicKey key)
    {
        var beatSeconds = BeatSeconds(beats);
        var downbeats = Downbeats(beats, beatsPerBar);
        return new SheetPlan(
            AccidentalPlan(notes, key, index => BarOf(notes[index].Start, downbeats), handSplit),
            Beams(notes, beats, handSplit, key, beatSeconds),
            Rests(notes, handSplit));
    }

    /// <summary>What is written beside a note head: nothing, a sharp, a flat or a natural.</summary>
    internal enum NoteAccidental { None, Sharp, Flat, Natural }

    /// <summary>
    /// The sign really written beside each note, in the order the notes are handed over.
    ///
    /// <para>
    /// The key says what a note needs on its own; the bar says whether it has to be written again. An accidental
    /// holds from where it is written to the end of its bar — and only for the same written note, not for the
    /// rest of the song — so the second C♯ of a bar is bare, while a C♮ after it needs a natural to take the
    /// sharp back. The two staves keep their own bars, and <paramref name="barOf"/> says which bar of the song
    /// each note index sits in (the sheet derives it from the beat grid).
    /// </para>
    /// </summary>
    internal static NoteAccidental[] AccidentalPlan(IReadOnlyList<NoteEvent> notes, MusicKey key, Func<int, int> barOf, double handSplit)
    {
        var plan = new NoteAccidental[notes.Count];
        var bars = new Dictionary<(int Staff, int Bar, int Step), int>();
        for (var index = 0; index < notes.Count; index++)
        {
            var pitch = Math.Clamp(notes[index].Pitch, 0, 127);
            var (letter, octave, alteration) = key.Spell(pitch);
            var (staff, _) = Place(pitch, handSplit, key);
            var cell = (staff, barOf(index), octave * 7 + letter);
            var inForce = bars.TryGetValue(cell, out var altered) ? altered : key.Signature(letter);
            if (alteration == inForce) continue;
            plan[index] = alteration switch { > 0 => NoteAccidental.Sharp, < 0 => NoteAccidental.Flat, _ => NoteAccidental.Natural };
            bars[cell] = alteration;
        }
        return plan;
    }

    /// <summary>The sign drawn beside a note: the music glyphs when the symbol font has them, letters when it does not.</summary>
    internal static string AccidentalText(NoteAccidental accidental) => accidental switch
    {
        NoteAccidental.Sharp => SignsAvailable ? "♯" : "#",
        NoteAccidental.Flat => SignsAvailable ? "♭" : "b",
        NoteAccidental.Natural => SignsAvailable ? "♮" : "n",
        _ => "",
    };

    /// <summary>The measure a note falls in, as the count of downbeats already passed; -1 is before the first one.</summary>
    internal static int BarOf(double start, IReadOnlyList<double> downbeats)
    {
        int low = 0, high = downbeats.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (downbeats[middle] <= start + 1e-9) { found = middle; low = middle + 1; } else high = middle - 1;
        }
        return found;
    }

    /// <summary>The times a measure starts: every <paramref name="beatsPerBar"/>-th beat of the song's grid.</summary>
    internal static IReadOnlyList<double> Downbeats(IReadOnlyList<double> beats, int beatsPerBar)
    {
        var perBar = Math.Max(1, beatsPerBar);
        var downbeats = new List<double>();
        for (var index = 0; index < beats.Count; index += perBar) downbeats.Add(beats[index]);
        return downbeats;
    }

    /// <summary>One note joined to the notes beside it by a beam: the run of note indices it covers, its stems' direction and how many beams it carries.</summary>
    internal readonly record struct Beam(int First, int Last, bool Up, int Beams);

    /// <summary>
    /// How long one beat of the song lasts: the middle gap between the beats of its own grid. A grid with fewer
    /// than two beats — a live performance — has no beat to measure against, so half a second stands in.
    /// </summary>
    internal static double BeatSeconds(IReadOnlyList<double> beats)
    {
        var gaps = new List<double>();
        for (var index = 1; index < beats.Count; index++)
        {
            var gap = beats[index] - beats[index - 1];
            if (gap > 1e-6) gaps.Add(gap);
        }
        if (gaps.Count == 0) return .5;
        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    /// <summary>
    /// How many flags a note carries: none for a quarter note or anything longer, then one for an eighth, two for
    /// a sixteenth and three for a shorter note still. The note's own length is compared with the beat the song's
    /// grid gives, so the same seconds are an eighth in a slow song and a quarter in a fast one.
    /// </summary>
    internal static int Flags(double durationSeconds, double beatSeconds)
    {
        if (beatSeconds <= 0 || durationSeconds <= 0) return 0;
        var flags = 0; var slot = beatSeconds;
        while (flags < 3 && durationSeconds <= slot * .7) { flags++; slot /= 2; }
        return flags;
    }

    /// <summary>The shapes a written rest takes, longest first: a whole rest hangs under a line, a half rest sits on one.</summary>
    internal enum RestShape { Whole, Half, Quarter, Eighth, Sixteenth }

    /// <summary>
    /// The shape that writes a silence of this length, counted in beats of the song's own grid: a quarter rest
    /// lasts a beat, a half two and a whole four, an eighth half a beat and a sixteenth a quarter of one. A
    /// silence longer than a whole rest is still drawn as one, the way a score writes a whole rest for a bar it
    /// cannot fill, and a silence with no beat to measure it — a live take — reads as a quarter rest.
    /// </summary>
    internal static RestShape Rest(double seconds, double beatSeconds)
    {
        if (beatSeconds <= 0 || seconds <= 0) return RestShape.Quarter;
        var beats = seconds / beatSeconds;
        if (beats <= .25) return RestShape.Sixteenth;
        if (beats <= .5) return RestShape.Eighth;
        if (beats <= 1) return RestShape.Quarter;
        if (beats <= 2) return RestShape.Half;
        return RestShape.Whole;
    }

    /// <summary>What is written for a rest: the musical glyph when the font has it, otherwise the shape's own initial.</summary>
    internal static string RestText(RestShape shape, bool glyphs) => shape switch
    {
        RestShape.Whole => glyphs ? "\uD834\uDD3B" : "W",
        RestShape.Half => glyphs ? "\uD834\uDD3C" : "H",
        RestShape.Quarter => glyphs ? "\uD834\uDD3D" : "Q",
        RestShape.Eighth => glyphs ? "\uD834\uDD3E" : "E",
        _ => glyphs ? "\uD834\uDD3F" : "S",
    };

    /// <summary>A silence written on one staff: where it starts and how long it lasts.</summary>
    internal readonly record struct RestGap(int Staff, double Start, double Seconds);

    /// <summary>
    /// The silences the staves are written with: a grand staff has a voice per hand and each voice accounts for
    /// its whole span, so wherever one hand is not sounding while the other is, that hand rests. The span runs
    /// from the first moment either hand plays to the last, and it is swept once with a running count per staff,
    /// so a piece with thousands of notes costs a sort rather than a scan per note.
    /// </summary>
    internal static IReadOnlyList<RestGap> Rests(IReadOnlyList<NoteEvent> notes, double handSplit)
    {
        var gaps = new List<RestGap>();
        if (notes.Count == 0) return gaps;
        // Each hand's sounding time as a list of +1/-1 edges, so one sweep reads both hands at once.
        List<(double Time, int Delta)>[] edges = [[], []];
        foreach (var note in notes)
        {
            var staff = StaffOf(note.Pitch, handSplit);
            edges[staff].Add((note.Start, 1));
            edges[staff].Add((note.End, -1));
        }
        for (var staff = 0; staff < 2; staff++) edges[staff].Sort((left, right) => left.Time.CompareTo(right.Time));
        int[] cursor = [0, 0], playing = [0, 0];
        var time = double.MaxValue;
        for (var staff = 0; staff < 2; staff++)
            if (edges[staff].Count > 0) time = Math.Min(time, edges[staff][0].Time);
        while (time < double.MaxValue)
        {
            // Take every edge written at this moment, then read what the two hands play until the next one.
            for (var staff = 0; staff < 2; staff++)
                while (cursor[staff] < edges[staff].Count && Math.Abs(edges[staff][cursor[staff]].Time - time) < 1e-9)
                    playing[staff] += edges[staff][cursor[staff]++].Delta;
            var next = double.MaxValue;
            for (var staff = 0; staff < 2; staff++)
                if (cursor[staff] < edges[staff].Count) next = Math.Min(next, edges[staff][cursor[staff]].Time);
            if (next == double.MaxValue) break;
            if (next - time > 1e-6)
                for (var staff = 0; staff < 2; staff++)
                    if (playing[staff] == 0)
                    {
                        // Silence that carries on from one moment of the other hand's music to the next is one
                        // rest, not one per note that hand plays.
                        var contiguous = gaps.Count > 0 && gaps[^1].Staff == staff
                            && Math.Abs(gaps[^1].Start + gaps[^1].Seconds - time) < 1e-6;
                        if (contiguous) gaps[^1] = gaps[^1] with { Seconds = next - gaps[^1].Start };
                        else gaps.Add(new RestGap(staff, time, next - time));
                    }
            time = next;
        }
        return gaps;
    }

    /// <summary>
    /// The runs of notes that are joined by a beam instead of each carrying its own flags. Two neighbouring notes
    /// share a beam when both are short enough to carry one, sit on the same staff (one beam never crosses from
    /// one hand to the other), start at different moments inside the same beat of the song's grid, and are
    /// neighbours in time — anything written between them ends the run. A run of one note is not a beam, so it is
    /// left out and the note keeps its own flags.
    /// </summary>
    internal static IReadOnlyList<Beam> Beams(IReadOnlyList<NoteEvent> notes, IReadOnlyList<double> beats, double handSplit, MusicKey key, double beatSeconds)
    {
        var beams = new List<Beam>();
        var run = new List<int>();
        void Close()
        {
            if (run.Count >= 2)
            {
                var up = StemUp(Place(notes[run[0]].Pitch, handSplit, key).RelativeStep);
                var count = int.MaxValue;
                foreach (var index in run) count = Math.Min(count, Flags(notes[index].Duration, beatSeconds));
                beams.Add(new Beam(run[0], run[^1], up, count));
            }
            run.Clear();
        }
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            var flags = Flags(note.Duration, beatSeconds);
            // A note written hollow is a half note or longer and never carries a beam, however short the song's
            // beat makes its seconds look.
            var joins = flags > 0 && !HollowHead(note.Duration);
            var staff = StaffOf(note.Pitch, handSplit);
            var beat = BarOf(note.Start, beats);
            var continues = joins && run.Count > 0
                && staff == StaffOf(notes[run[^1]].Pitch, handSplit)
                && beat == BarOf(notes[run[^1]].Start, beats)
                && note.Start - notes[run[^1]].Start > 1e-6;
            if (!continues) Close();
            if (!joins) continue;
            run.Add(index);
        }
        Close();
        return beams;
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

    /// <summary>
    /// Where a note that starts at <paramref name="start"/> is drawn: the window's left edge is the playhead
    /// minus a quarter of its own width. <paramref name="leftInset"/> is the room the clef and the key signature
    /// take at the head of the staff, and everything written in time — notes, bar lines and the playhead — is
    /// mapped inside what is left, so they stay aligned with each other.
    /// </summary>
    internal static double NoteX(double start, double windowStart, double secondsVisible, Rect area, double leftInset = 0) =>
        area.X + leftInset + (start - windowStart) / Math.Max(.001, secondsVisible) * Math.Max(1, area.Width - leftInset);

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
    /// <summary>
    /// Draws one frame. <paramref name="plan"/> is the working-out <see cref="Plan"/> returns, which a renderer
    /// that draws on every frame should hand back in so the arithmetic runs once per song; left out, the layer
    /// works it out itself.
    /// </summary>
    internal static void Draw(
        DrawingContext dc, Rect area, IReadOnlyList<NoteEvent> notes, double position, double handSplit, MusicKey key,
        IReadOnlyList<double> beats, int beatsPerBar, double secondsVisible, Color ink, Color accent, double opacity, double pixelsPerDip,
        SheetPlan? plan = null)
    {
        if (area.Width < 40 || area.Height < 24) return;
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(41 * opacity, 0, 235), 6, 7, 12)),
            new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(70 * opacity, 0, 200), ink.R, ink.G, ink.B)), 1), area, 8, 8);

        var windowStart = WindowStart(position, secondsVisible);
        var dim = Color.FromArgb((byte)Math.Clamp(150 * opacity, 20, 210), ink.R, ink.G, ink.B);
        var gap = StaffGap(area);
        // One diatonic step is half of the line spacing, which is the grid every note position is put on.
        var half = gap / 2;
        // The clef and the key signature sit in front of the music: the room they take is measured once and
        // everything written in time is mapped inside what is left of the band.
        var clefSpace = ClefSpace(area);
        var inset = LeftInset(area, key);
        var lineLeft = area.X + inset;
        var lineRight = area.Right - 8;
        var downbeats = Downbeats(beats, beatsPerBar);
        var sheet = plan ?? Plan(notes, beats, beatsPerBar, handSplit, key);
        var accidentals = sheet.Accidentals;
        var beatSeconds = BeatSeconds(beats);

        for (var staff = 0; staff < 2; staff++)
        {
            var bottom = StaffBottom(area, gap, staff);
            for (var line = 0; line < 5; line++)
                dc.DrawLine(new Pen(new SolidColorBrush(dim), 1), new Point(lineLeft, bottom - line * gap), new Point(lineRight, bottom - line * gap));
            // The clef glyph is several times taller than the spacing, so it is centred on the staff.
            var clef = Text(ClefText(staff), gap * 4.4, ink, pixelsPerDip);
            dc.DrawText(clef, new Point(area.X + 6, bottom - 4 * gap - (clef.Height - 4 * gap) * .62));
            // The signature: one sign per altered letter, in the order a signature writes them, each one at the
            // place that letter has on this staff.
            var sign = Text(AccidentalText(key.UsesFlats ? NoteAccidental.Flat : NoteAccidental.Sharp), gap * 2.6, ink, pixelsPerDip);
            var index = 0;
            foreach (var letter in key.SignedLetters)
            {
                var x = area.X + clefSpace + gap * .8 + index++ * gap * 1.15;
                dc.DrawText(sign, new Point(x, bottom - SignatureStep(staff, letter) * half - sign.Height * .55));
            }
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
            var x = NoteX(beat, windowStart, secondsVisible, area, inset);
            if (startsMeasure && x - lastBarX < 6) continue;      // two downbeats inside one pixel must not darken it
            if (startsMeasure) lastBarX = x;
            var alpha = (byte)Math.Clamp((startsMeasure ? 120 : 55) * opacity, 0, 220);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B)), startsMeasure ? 1.3 : 1);
            dc.DrawLine(pen, new Point(x, StaffBottom(area, gap, 0) - 4 * gap), new Point(x, StaffBottom(area, gap, 1)));
        }

        // The playhead, drawn through both staves so the reader can see where the music is.
        var playheadX = NoteX(position, windowStart, secondsVisible, area, inset);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(200 * opacity, 0, 240), accent.R, accent.G, accent.B)), 1.6),
            new Point(playheadX, area.Y + 4), new Point(playheadX, area.Bottom - 4));

        // The silences: each hand that is not playing rests on its own staff, written with the shape that lasts
        // as long as the gap does. A gap that started before the window is written where the staff begins, the
        // way a scrolled score shows a rest that is already under way.
        var restGlyphs = MusicGlyphsAvailable;
        foreach (var rest in sheet.Rests)
        {
            if (rest.Start + rest.Seconds < windowStart || rest.Start > windowStart + secondsVisible) continue;
            var bottom = StaffBottom(area, gap, rest.Staff);
            var shape = Rest(rest.Seconds, beatSeconds);
            var anchor = bottom - (shape == RestShape.Whole ? StaffSteps : 4) * half;
            var text = Text(RestText(shape, restGlyphs), gap * 2.6, dim, pixelsPerDip);
            var x = Math.Max(NoteX(rest.Start, windowStart, secondsVisible, area, inset), lineLeft);
            dc.DrawText(text, new Point(x + gap * .4, anchor - text.Height * .5));
        }

        var headWidth = Math.Clamp(gap * 1.35, 3.5, 12);
        // Which notes share a beam, and where the stem ends of each run sit: a run's stems all reach one line, so
        // the beam that joins them is straight.
        var beams = sheet.Beams;
        var beamEnds = new double[notes.Count];
        for (var index = 0; index < beamEnds.Length; index++) beamEnds[index] = double.NaN;
        foreach (var beam in beams)
        {
            var end = beam.Up ? double.MaxValue : double.MinValue;
            for (var index = beam.First; index <= beam.Last; index++)
            {
                var (staff, relative) = Place(notes[index].Pitch, handSplit, key);
                var y = StaffBottom(area, gap, staff) - relative * half;
                end = beam.Up ? Math.Min(end, y - gap * 3.2) : Math.Max(end, y + gap * 3.2);
            }
            for (var index = beam.First; index <= beam.Last; index++) beamEnds[index] = end;
        }
        var first = NoteTimeline.FirstIndexAtOrAfter(notes, windowStart);
        for (var index = first; index < notes.Count; index++)
        {
            var note = notes[index];
            if (note.Start > windowStart + secondsVisible) break;
            var (staff, relative) = Place(note.Pitch, handSplit, key);
            var bottom = StaffBottom(area, gap, staff);
            var y = bottom - relative * half;
            var x = NoteX(note.Start, windowStart, secondsVisible, area, inset);
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
            // The sign this bar really needs: nothing when the signature already writes the note that way, a
            // sharp or a flat when the note leaves the key, and a natural when an earlier note of the same bar
            // altered it.
            var accidental = accidentals[index];
            if (accidental != NoteAccidental.None)
                dc.DrawText(Text(AccidentalText(accidental), gap * 1.7, colour, pixelsPerDip), new Point(x - headWidth * 2.1, y - gap * .8));
            if (HasStem(note.Duration))
            {
                var up = StemUp(relative);
                var stemX = x + (up ? headWidth * .55 : -headWidth * .55);
                // A note inside a beam reaches the run's own stem end; every other note keeps the standard
                // length, and a short one carries its flags at that end.
                var beamed = !double.IsNaN(beamEnds[index]);
                var end = beamed ? beamEnds[index] : up ? y - gap * 3.2 : y + gap * 3.2;
                dc.DrawLine(new Pen(brush, 1.2), new Point(stemX, y), new Point(stemX, end));
                var flags = beamed ? 0 : Flags(note.Duration, beatSeconds);
                for (var flag = 0; flag < flags; flag++)
                {
                    var root = up ? end + flag * gap * .38 : end - flag * gap * .38;
                    dc.DrawLine(new Pen(brush, 1.2), new Point(stemX, root),
                        new Point(stemX + (up ? -headWidth * 1.5 : headWidth * 1.5), root + (up ? gap * 1.1 : -gap * 1.1)));
                }
            }
        }
        // The beams themselves, drawn last so they sit on top of the stems they join.
        foreach (var beam in beams)
        {
            var left = NoteX(notes[beam.First].Start, windowStart, secondsVisible, area, inset) + headWidth * .55;
            var right = NoteX(notes[beam.Last].Start, windowStart, secondsVisible, area, inset) + headWidth * .55;
            var beamBrush = new SolidColorBrush(NoteColour(notes[beam.First], position, dim, ink, accent));
            for (var line = 0; line < beam.Beams; line++)
            {
                var y = beam.Up ? beamEnds[beam.First] + line * gap * .38 : beamEnds[beam.First] - line * gap * .38;
                dc.DrawLine(new Pen(beamBrush, Math.Max(1.5, gap * .34)), new Point(left, y), new Point(right, y));
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
