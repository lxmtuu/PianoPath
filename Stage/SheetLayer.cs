using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The sheet layer: a grand staff drawn across the top of the stage, above the piano roll and following the
/// playhead. Each note is written on the staff its hand split assigns it to, at the place the song's key spells
/// it, with a key signature at the head of both staves, the accidentals the bars really need, ledger lines where
/// a note leaves the staff, bar lines on the beat grid, short notes beamed within their beat (or flagged when
/// they stand alone), a rest wherever one hand is silent while the other plays, a curve where a note is carried
/// on by a tie, and a ring on whatever is sounding.
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
    /// with, the beams its short notes share, the silences of both hands, the notes carried on by ties, the lines
    /// handed from one hand to the other, and the way the reader groups the notes — the chords and the live notes — into single streams. None of it depends
    /// on the practice state or on where the playhead is, so a renderer can work it out once per song instead of
    /// once per frame.
    /// </summary>
    internal sealed record SheetPlan(
        NoteAccidental[] Accidentals, IReadOnlyList<Beam> Beams, IReadOnlyList<RestGap> Rests, IReadOnlyList<Tie> Ties,
        IReadOnlyList<Slur> Slurs, IReadOnlyList<IReadOnlyList<int>> Chords, IReadOnlyList<IReadOnlyList<int>>[] Streams);

    /// <summary>Works out the plan of a song: its accidentals, its beams, its rests, its ties and its hand-offs.</summary>
    internal static SheetPlan Plan(IReadOnlyList<NoteEvent> notes, IReadOnlyList<double> beats, int beatsPerBar, double handSplit, MusicKey key)
    {
        var beatSeconds = BeatSeconds(beats);
        var downbeats = Downbeats(beats, beatsPerBar);
        var ties = Ties(notes, handSplit);
        // A note a tie carries on from is a continuation of the one before it, which the accidentals have to know.
        var carried = new HashSet<int>();
        foreach (var tie in ties) carried.Add(tie.Second);
        return new SheetPlan(
            AccidentalPlan(notes, key, index => BarOf(notes[index].Start, downbeats), handSplit, carried),
            Beams(notes, beats, handSplit, key, beatSeconds),
            Bars(Rests(notes, handSplit), downbeats),
            ties,
            Slurs(notes, handSplit),
            Chords(notes, handSplit),
            Streams(notes, handSplit));
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
    ///
    /// <para>
    /// A note a tie carries on from <paramref name="carried"/> takes the sign the note before it was written with
    /// rather than a new one: the note did not stop sounding, so a score does not sign it again even when the bar
    /// line has passed — and everything after it in the new bar is written as if the sign had been put there.
    /// </para>
    /// </summary>
    internal static NoteAccidental[] AccidentalPlan(IReadOnlyList<NoteEvent> notes, MusicKey key, Func<int, int> barOf, double handSplit, IReadOnlySet<int>? carried = null)
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
            if (carried is not null && carried.Contains(index))
            {
                // The tie carries the sign in: record it for the rest of the bar and write nothing beside this note.
                bars[cell] = alteration;
                continue;
            }
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

    /// <summary>One note carried on into the next: the note a tie starts at and the note it is carried into.</summary>
    internal readonly record struct Tie(int First, int Second);

    /// <summary>
    /// How late a note may start and still be the same written note carried on. A score ties a note exactly, but a
    /// performance recorded to MIDI often leaves a few milliseconds between the two halves of it, so the tie
    /// tolerates that much air and no more.
    /// </summary>
    internal const double TieGapSeconds = .03;

    /// <summary>
    /// The ties of a song: a note that starts where the same pitch left off is not a new note but the same written
    /// note carried on, so it is written once and joined by a curve instead of being struck again. The notes of one
    /// pitch and one hand are walked in time and neighbouring pairs whose end and start meet — within
    /// <see cref="TieGapSeconds"/> of air, as a played file usually leaves — are tied. An overlap means the note was
    /// struck again, and a different pitch or a different hand is a different note. A chain of three is two ties,
    /// which is what an engraving draws.
    /// </summary>
    internal static IReadOnlyList<Tie> Ties(IReadOnlyList<NoteEvent> notes, double handSplit)
    {
        var ties = new List<Tie>();
        // One bucket per pitch and staff, in the order the song already has its notes sorted in.
        var voices = new Dictionary<(int Pitch, int Staff), int>();
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            var key = (note.Pitch, StaffOf(note.Pitch, handSplit));
            if (voices.TryGetValue(key, out var previous))
            {
                var air = note.Start - notes[previous].End;
                if (air >= -1e-6 && air <= TieGapSeconds) ties.Add(new Tie(previous, index));
            }
            voices[key] = index;
        }
        return ties;
    }

    /// <summary>
    /// Whether a tie arcs under its note or over it: a curve leans away from the stems, so it goes under when they
    /// point up and over when they point down, the way an engraving draws it.
    /// </summary>
    internal static bool TieUnder(int relativeStep) => StemUp(relativeStep);

    /// <summary>One line handed from one hand to the other: the note that gives the line up and the note that takes it.</summary>
    internal readonly record struct Slur(int First, int Second);

    /// <summary>
    /// How far apart two notes may sit and still be one line changing hands. A melody crossing into the other hand
    /// moves by a step or a leap, not by the two registers a bass line and a tune live in, so an octave or more is
    /// two voices and not a hand-off.
    /// </summary>
    internal const int SlurSpanSemitones = 12;

    /// <summary>
    /// The places a line changes hands: a note in one hand that stops exactly where a note in the other begins is
    /// the same line carried on, so the sheet draws one curve over the hand-off instead of leaving two notes that
    /// look like separate thoughts. The two notes have to be neighbours in the song, within
    /// <see cref="TieGapSeconds"/> of each other and less than <see cref="SlurSpanSemitones"/> semitones apart, and
    /// neither of them may be part of a chord: a block of notes taking over is a new chord, not a line continuing.
    /// </summary>
    internal static IReadOnlyList<Slur> Slurs(IReadOnlyList<NoteEvent> notes, double handSplit)
    {
        var sizes = new int[notes.Count];
        foreach (var group in Chords(notes, handSplit))
            foreach (var member in group) sizes[member] = group.Count;
        var slurs = new List<Slur>();
        for (var index = 1; index < notes.Count; index++)
        {
            var previous = notes[index - 1];
            var note = notes[index];
            if (StaffOf(previous.Pitch, handSplit) == StaffOf(note.Pitch, handSplit)) continue;
            var air = note.Start - previous.End;
            if (air < -1e-6 || air > TieGapSeconds) continue;
            if (Math.Abs(note.Pitch - previous.Pitch) >= SlurSpanSemitones) continue;
            if (sizes[index - 1] != 1 || sizes[index] != 1) continue;
            slurs.Add(new Slur(index - 1, index));
        }
        return slurs;
    }

    /// <summary>The shapes a written rest takes, longest first: a whole rest hangs under a line, a half rest sits on one.</summary>
    internal enum RestShape { Whole, Half, Quarter, Eighth, Sixteenth }

    /// <summary>
    /// How far two times may differ and still be the same moment of a grid: a bar line worked out from the beat
    /// grid and a gap worked out from the notes meet at the same instant to within a fraction of a millisecond.
    /// </summary>
    internal const double RestTolerance = 1e-3;

    /// <summary>
    /// The silences written the way a score writes them: one rest per bar the hand is quiet for, rather than one
    /// rest stretched over however long the hand stays off. A piece that fills a whole bar becomes a whole rest
    /// (how a full bar of silence is written in any meter), and the pieces at either end — where the hand stops or
    /// starts in the middle of a bar — keep the shape of their own length.
    /// </summary>
    internal static IReadOnlyList<RestGap> Bars(IReadOnlyList<RestGap> rests, IReadOnlyList<double> downbeats)
    {
        if (rests.Count == 0 || downbeats.Count < 2) return rests;
        var bars = new List<RestGap>();
        foreach (var rest in rests)
        {
            var start = rest.Start;
            var end = rest.Start + rest.Seconds;
            foreach (var boundary in downbeats)
            {
                if (boundary <= start + RestTolerance || boundary >= end - RestTolerance) continue;
                bars.Add(new RestGap(rest.Staff, start, boundary - start, FillsBar(downbeats, start, boundary)));
                start = boundary;
            }
            bars.Add(new RestGap(rest.Staff, start, end - start, FillsBar(downbeats, start, end)));
        }
        return bars;
    }

    /// <summary>Whether a silence runs from one bar line to the next, which is what makes it a whole rest.</summary>
    private static bool FillsBar(IReadOnlyList<double> downbeats, double start, double end)
    {
        for (var index = 0; index + 1 < downbeats.Count; index++)
            if (Math.Abs(downbeats[index] - start) <= RestTolerance && Math.Abs(downbeats[index + 1] - end) <= RestTolerance)
                return true;
        return false;
    }

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
    /// <summary>
    /// One silence on one staff. <paramref name="Whole"/> marks the piece that fills a whole bar: a score writes a
    /// full bar of silence as a whole rest whatever the meter is, so the shape cannot be read off the length alone.
    /// </summary>
    internal readonly record struct RestGap(int Staff, double Start, double Seconds, bool Whole = false);

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
            // A run has to join notes written at two different moments: a chord on its own is one column of heads
            // and gets one stem, never a beam across itself.
            if (run.Count >= 2 && notes[run[^1]].Start - notes[run[0]].Start > 1e-6)
            {
                var up = StemUp(Place(notes[run[0]].Pitch, handSplit, key).RelativeStep);
                var count = int.MaxValue;
                foreach (var index in run) count = Math.Min(count, Flags(notes[index].Duration, beatSeconds));
                beams.Add(new Beam(run[0], run[^1], up, count));
            }
            run.Clear();
        }
        var chords = Chords(notes, handSplit);
        for (var group = 0; group < chords.Count; group++)
        {
            // One stem per chord: the notes written together join the run together, and the notes of the chord
            // after them are the ones that decide whether the run carries on.
            var joins = true;
            var staff = -1;
            foreach (var index in chords[group])
            {
                // A note written hollow is a half note or longer and never carries a beam, however short the
                // song's beat makes its seconds look.
                staff = StaffOf(notes[index].Pitch, handSplit);
                if (Flags(notes[index].Duration, beatSeconds) == 0 || HollowHead(notes[index].Duration)) joins = false;
            }
            var beat = BarOf(notes[chords[group][0]].Start, beats);
            var continues = joins && run.Count > 0
                && staff == StaffOf(notes[run[^1]].Pitch, handSplit)
                && beat == BarOf(notes[run[^1]].Start, beats)
                && notes[chords[group][0]].Start - notes[run[^1]].Start > 1e-6;
            if (!continues) Close();
            if (!joins) continue;
            foreach (var index in chords[group]) run.Add(index);
        }
        Close();
        return beams;
    }

    /// <summary>
    /// The notes of the song gathered as they are written: the notes sounding at one moment in one hand. A chord
    /// is written with one head per pitch and one stem for the whole chord, so the sheet needs to know which notes
    /// belong together. The song's own order is kept — its notes are sorted by start, and the list is not
    /// reordered, so index-based checks keep working — and a group stands for a run of notes that start together.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<int>> Chords(IReadOnlyList<NoteEvent> notes, double handSplit)
    {
        var groups = new List<IReadOnlyList<int>>();
        var run = new List<int>();
        void Close()
        {
            if (run.Count > 0) groups.Add([.. run]);
            run.Clear();
        }
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            if (run.Count > 0)
            {
                var head = notes[run[0]];
                var together = Math.Abs(note.Start - head.Start) < 1e-6
                    && StaffOf(note.Pitch, handSplit) == StaffOf(head.Pitch, handSplit);
                if (!together) Close();
            }
            run.Add(index);
        }
        Close();
        return groups;
    }

    /// <summary>
    /// The song split the way a reader's eyes split it: the notes that are sounding together are one event, and
    /// the events of one hand are one stream even when they overlap the other hand's.
    ///
    /// <para>
    /// One list per hand, in the song's own order, with every note of a chord in the same event, so a renderer can
    /// draw a hand event by event — one grouped head sitting on the fingers that are pressed, rather than one
    /// marker per note head. The notes of one stream are the notes of one event, in pitch order.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<int>>[] Streams(IReadOnlyList<NoteEvent> notes, double handSplit)
    {
        var streams = new List<IReadOnlyList<int>>[2] { [], [] };
        var run = new List<int>(); var runStaff = -1; var runStart = double.NaN;
        void Close()
        {
            if (run.Count == 0) return;
            run.Sort((left, right) => notes[left].Pitch.CompareTo(notes[right].Pitch));
            streams[runStaff].Add([.. run]);
            run.Clear();
        }
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            var staff = StaffOf(note.Pitch, handSplit);
            var sameEvent = run.Count > 0 && staff == runStaff && Math.Abs(note.Start - runStart) < 1e-6;
            if (!sameEvent) { Close(); runStaff = staff; runStart = note.Start; }
            run.Add(index);
        }
        Close();
        return streams;
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
            var shape = rest.Whole ? RestShape.Whole : Rest(rest.Seconds, beatSeconds);
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
        // Which chord each note belongs to, so a head knows whether it is the one that carries the group's stem.
        var groupOf = new int[notes.Count];
        for (var index = 0; index < groupOf.Length; index++) groupOf[index] = -1;
        for (var group = 0; group < sheet.Chords.Count; group++)
            foreach (var member in sheet.Chords[group]) groupOf[member] = group;
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
            // A chord is stemmed once, by the chord pass below; here only a note standing on its own gets one — and
            // a plan handed over without a grouping falls back to every head carrying its own stem.
            var chord = groupOf[index] >= 0 ? sheet.Chords[groupOf[index]] : null;
            if (HasStem(note.Duration) && (chord is null || chord.Count == 1 || chord[0] != index))
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

        // The stems of a chord: one stem for the whole chord, from its far head through the middle of the group to
        // the standard length — or to the run's shared stem end when the chord is inside a beam — with the flags of
        // the shortest note of the chord at its end. A group of one head draws exactly the stem it always did.
        foreach (var group in sheet.Chords)
        {
            if (group.Count < 2) continue;
            var opening = notes[group[0]];
            var (staff, openingStep) = Place(opening.Pitch, handSplit, key);
            var bottom = StaffBottom(area, gap, staff);
            var up = StemUp(openingStep);
            var beamed = !double.IsNaN(beamEnds[group[0]]);
            double far = up ? double.MaxValue : double.MinValue; var middle = 0.0;
            foreach (var index in group)
            {
                var (_, relative) = Place(notes[index].Pitch, handSplit, key);
                var y = bottom - relative * half;
                far = up ? Math.Min(far, y) : Math.Max(far, y);
                middle += y;
            }
            middle /= group.Count;
            var end = beamed ? beamEnds[group[0]] : up ? far - gap * 3.2 : far + gap * 3.2;
            var x = NoteX(opening.Start, windowStart, secondsVisible, area, inset) + (up ? headWidth * .55 : -headWidth * .55);
            var brush = new SolidColorBrush(NoteColour(opening, position, dim, ink, accent));
            dc.DrawLine(new Pen(brush, 1.2), new Point(x, middle), new Point(x, end));
            if (beamed) continue;
            var flags = 0;
            foreach (var index in group) flags = Math.Max(flags, Flags(notes[index].Duration, beatSeconds));
            for (var flag = 0; flag < flags; flag++)
            {
                var root = up ? end + flag * gap * .38 : end - flag * gap * .38;
                dc.DrawLine(new Pen(brush, 1.2), new Point(x, root),
                    new Point(x + (up ? -headWidth * 1.5 : headWidth * 1.5), root + (up ? gap * 1.1 : -gap * 1.1)));
            }
        }
        // The ties: a curve from the head of the note a tie starts at to the head of the note it carries on into,
        // leaning away from the stems the way an engraving draws it. A tie whose other end is outside the window
        // is drawn up to the edge, so a note carried across the view is not left looking like it just stopped.
        foreach (var tie in sheet.Ties)
        {
            var from = notes[tie.First]; var to = notes[tie.Second];
            var (staff, relative) = Place(from.Pitch, handSplit, key);
            var bottom = StaffBottom(area, gap, staff);
            var y = bottom - relative * half;
            var under = TieUnder(relative);
            var start = Math.Clamp(NoteX(from.End, windowStart, secondsVisible, area, inset) - headWidth * .6, lineLeft, lineRight);
            var end = Math.Clamp(NoteX(to.Start, windowStart, secondsVisible, area, inset) + headWidth * .6, lineLeft, lineRight);
            if (end < lineLeft + 1) continue;
            var edge = y + (under ? half * 1.5 : -half * 1.5);
            var bulge = edge + (under ? half * 1.4 : -half * 1.4);
            var curve = new StreamGeometry();
            using (var figure = curve.Open())
            {
                figure.BeginFigure(new Point(start, edge), false, false);
                figure.QuadraticBezierTo(new Point((start + end) / 2, bulge), new Point(end, edge), true, false);
            }
            curve.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(NoteColour(from, position, dim, ink, accent)), Math.Max(1.1, gap * .22)), curve);
        }

        // The hand-offs: a curve from the head that gives the line up to the head that takes it, bowing the way it
        // travels so it hugs the gap between the staves instead of cutting through either of them. Like a tie, one
        // whose far end is off the window is drawn up to the edge rather than left looking finished.
        foreach (var slur in sheet.Slurs)
        {
            var from = notes[slur.First]; var to = notes[slur.Second];
            var (fromStaff, fromStep) = Place(from.Pitch, handSplit, key);
            var (toStaff, toStep) = Place(to.Pitch, handSplit, key);
            var fromY = StaffBottom(area, gap, fromStaff) - fromStep * half;
            var toY = StaffBottom(area, gap, toStaff) - toStep * half;
            var start = Math.Clamp(NoteX(from.End, windowStart, secondsVisible, area, inset) - headWidth * .6, lineLeft, lineRight);
            var end = Math.Clamp(NoteX(to.Start, windowStart, secondsVisible, area, inset) + headWidth * .6, lineLeft, lineRight);
            if (end < lineLeft + 1) continue;
            var span = end - start;
            var bow = toY < fromY ? -gap * 1.4 : gap * 1.4;
            var curve = new StreamGeometry();
            using (var figure = curve.Open())
            {
                figure.BeginFigure(new Point(start, fromY), false, false);
                figure.BezierTo(new Point(start + span * .3, fromY + bow), new Point(start + span * .7, toY + bow), new Point(end, toY), true, false);
            }
            curve.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(NoteColour(from, position, dim, ink, accent)), Math.Max(1.1, gap * .22)), curve);
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
