using Xunit;

namespace PianoPath.Tests;

/// <summary>Where the notes of a song are on the picture at a moment of the song.</summary>
public class ArNotesTests
{
    private static readonly (double X, double Y)[] Sample88 = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
    private static readonly ArCalibration Calibration = ArProjectionTests.Calibrate(Sample88);
    private static readonly ArView View = new(1280, 720, 1000, 600, PictureFit.Fit, false);
    private static readonly ArNoteStyle Style = ArNoteStyle.Default;

    private static NoteEvent Note(int pitch, double start, double duration) => new() { Pitch = pitch, Start = start, Duration = duration };

    private static List<ArNoteShape> Build(IReadOnlyList<NoteEvent> notes, double position, ArNoteStyle? style = null, ArCalibration? calibration = null, ArView? view = null)
    {
        var into = new List<ArNoteShape> { default };       // proves the list is cleared first
        var longest = notes.Count == 0 ? 0 : notes.Max(n => n.Duration);
        ArNotes.Build(calibration ?? Calibration, view ?? View, notes, position, longest, style ?? Style, into);
        return into;
    }

    private static void Near(double expected, double actual, double tolerance = 1e-6) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    /// <summary>Where the picture says a point of the keyboard is, on the output.</summary>
    private static (double X, double Y) Out(double u, double v, ArCalibration? calibration = null, ArView? view = null)
    {
        var p = (calibration ?? Calibration).ToPicture(u, v);
        return (view ?? View).ToOutput(p.X, p.Y);
    }

    private static (double U, double V) Keys(ArCorner corner, ArCalibration? calibration = null, ArView? view = null)
    {
        var (px, py) = (view ?? View).ToImage(corner.X, corner.Y);
        return (calibration ?? Calibration).ToKeys(px, py);
    }

    [Fact]
    public void A_note_reaches_the_back_of_the_keys_at_its_start_time_and_trails_behind_it()
    {
        var shapes = Build([Note(60, 10, .5)], 10);
        var shape = Assert.Single(shapes.Skip(1));
        var (left, right) = Calibration.Layout.Span(60);
        var centre = (left + right) / 2; var half = (right - left) / 2 * Style.WidthShare;
        var length = .5 * Style.FallSpeed - Style.Gap;
        var patch = shape.Patch;
        // the leading edge is the front of the patch, on the back edge of the keys (v = 0)
        var (flx, fly) = Out(centre - half, 0); var (frx, fry) = Out(centre + half, 0);
        Near(flx, patch.FrontLeft.X); Near(fly, patch.FrontLeft.Y); Near(frx, patch.FrontRight.X); Near(fry, patch.FrontRight.Y);
        var (blx, bly) = Out(centre - half, -length); var (brx, bry) = Out(centre + half, -length);
        Near(blx, patch.BackLeft.X); Near(bly, patch.BackLeft.Y); Near(brx, patch.BackRight.X); Near(bry, patch.BackRight.Y);
        Assert.True(shape.Sounding);                       // start <= position < end
        Assert.False(shape.Black);
        Assert.Equal(60, shape.Pitch); Assert.Equal(0, shape.NoteIndex);
        Assert.Equal(1, shape.Fade);
    }

    [Fact]
    public void A_note_still_to_come_is_that_far_behind_the_keys_for_the_time_it_has_to_wait()
    {
        var shape = Assert.Single(Build([Note(60, 11, .5)], 10).Skip(1));
        var (left, right) = Calibration.Layout.Span(60);
        var centre = (left + right) / 2;
        var (u, v) = Keys(shape.Patch.FrontRight);
        Near(centre + (right - left) / 2 * Style.WidthShare, u, 1e-6);
        Near(-1.5, v, 1e-6);                               // one second at 1.5 key depths per second
        Assert.False(shape.Sounding);
        // twice the time, twice the distance
        var later = Assert.Single(Build([Note(60, 12, .5)], 10).Skip(1));
        Near(-3, Keys(later.Patch.FrontRight).V, 1e-6);
    }

    [Fact]
    public void A_note_comes_into_view_at_the_reach_and_fades_in_over_its_last_quarter()
    {
        var edge = Style.Reach / Style.FallSpeed;
        Assert.Empty(Build([Note(60, 10 + edge + .01, .5)], 10).Skip(1));
        var barely = Assert.Single(Build([Note(60, 10 + edge - .01, .5)], 10).Skip(1));
        Assert.InRange(barely.Fade, 0, .05);
        var halfway = Assert.Single(Build([Note(60, 10 + edge - .5 * Style.Reach * .25 / Style.FallSpeed, .5)], 10).Skip(1));
        Near(.5, halfway.Fade, 1e-9);
        var nearer = Assert.Single(Build([Note(60, 10 + edge * .6, .5)], 10).Skip(1));
        Assert.Equal(1, nearer.Fade);
    }

    [Fact]
    public void A_long_note_is_cut_off_at_the_reach()
    {
        var shape = Assert.Single(Build([Note(60, 10, 20)], 10).Skip(1));
        Near(-Style.Reach, Keys(shape.Patch.BackLeft).V, 1e-6);
        Near(0, Keys(shape.Patch.FrontLeft).V, 1e-6);
    }

    [Fact]
    public void A_sounding_note_slides_over_the_keys_and_is_used_up_as_it_goes()
    {
        var note = Note(60, 10, .5);
        var length = .5 * Style.FallSpeed - Style.Gap;
        var early = Assert.Single(Build([note], 10.2).Skip(1));
        Near(0, Keys(early.Patch.FrontLeft).V, 1e-6);                          // held at the keys
        Near(.2 * Style.FallSpeed - length, Keys(early.Patch.BackLeft).V, 1e-6); // its tail drawn in
        Assert.True(early.Sounding);
        var late = Assert.Single(Build([note], 10.45).Skip(1));
        Assert.True(late.Patch.Area < early.Patch.Area);
        Assert.Empty(Build([note], 10.5).Skip(1));                              // nothing is left of it
        Assert.Empty(Build([note], 12).Skip(1));
        Assert.Empty(Build([note], 10.6).Skip(1));
    }

    [Fact]
    public void A_very_short_note_is_still_drawn_at_the_shortest_length()
    {
        var shape = Assert.Single(Build([Note(60, 11, .01)], 10).Skip(1));
        var length = Keys(shape.Patch.FrontLeft).V - Keys(shape.Patch.BackLeft).V;
        Near(Style.MinLength, length, 1e-6);
    }

    [Fact]
    public void A_note_is_as_wide_as_its_key_allows_and_black_notes_come_last()
    {
        var notes = new[] { Note(61, 10.5, .5), Note(60, 10.5, .5), Note(63, 10.6, .5), Note(62, 10.6, .5) };
        var shapes = Build(notes, 10).Skip(1).ToList();
        Assert.Equal(4, shapes.Count);
        Assert.Equal(new[] { false, false, true, true }, shapes.Select(s => s.Black));
        var white = shapes.First(s => s.Pitch == 60); var black = shapes.First(s => s.Pitch == 61);
        var whiteWidth = Keys(white.Patch.FrontRight).U - Keys(white.Patch.FrontLeft).U;
        var blackWidth = Keys(black.Patch.FrontRight).U - Keys(black.Patch.FrontLeft).U;
        Near(Calibration.Layout.WhiteWidth * Style.WidthShare, whiteWidth, 1e-6);
        Near(Calibration.Layout.WhiteWidth * KeyboardLayout.BlackWidthShare * Style.WidthShare, blackWidth, 1e-6);
        // and a black note is centred on its key
        var (bl, br) = Calibration.Layout.Span(61);
        Near((bl + br) / 2, (Keys(black.Patch.FrontRight).U + Keys(black.Patch.FrontLeft).U) / 2, 1e-6);
    }

    [Fact]
    public void Pitches_the_calibrated_keyboard_does_not_have_are_skipped()
    {
        var corners61 = new (double X, double Y)[] { (190, 276), (1088, 258), (1106, 432), (172, 452) };
        var keys61 = KeyboardLayout.ForKeyCount(61);
        var calibration = ArProjectionTests.Calibrate(corners61, keys61);
        var shapes = Build([Note(keys61.FirstPitch - 1, 10.5, .5), Note(keys61.FirstPitch, 10.5, .5), Note(keys61.LastPitch, 10.5, .5), Note(keys61.LastPitch + 1, 10.5, .5), Note(0, 10.5, .5), Note(127, 10.5, .5)], 10, calibration: calibration).Skip(1);
        Assert.Equal(new[] { keys61.FirstPitch, keys61.LastPitch }, shapes.Select(s => s.Pitch).Order());
    }

    [Fact]
    public void Nothing_is_drawn_for_a_style_that_cannot_draw_or_a_position_that_is_not_a_time()
    {
        var notes = new[] { Note(60, 10.5, .5) };
        foreach (var style in new[] { new ArNoteStyle(FallSpeed: 0), new ArNoteStyle(FallSpeed: double.NaN), new ArNoteStyle(Reach: -1), new ArNoteStyle(MinLength: 0), new ArNoteStyle(Gap: -1), new ArNoteStyle(WidthShare: 0), new ArNoteStyle(WidthShare: 1.5) })
        {
            Assert.False(style.IsUsable);
            Assert.Empty(Build(notes, 10, style).Skip(1));
        }
        Assert.True(ArNoteStyle.Default.IsUsable);
        Assert.Empty(Build(notes, double.NaN).Skip(1));
        Assert.Empty(Build(notes, double.PositiveInfinity).Skip(1));
        Assert.Empty(Build([], 10).Skip(1));
        Assert.Empty(Build(notes, 10, view: new ArView(0, 0, 10, 10, PictureFit.Fit, false)).Skip(1));
    }

    [Fact]
    public void A_note_that_began_long_ago_is_found_when_the_longest_duration_is_given()
    {
        var notes = new[] { Note(60, 0, 20), Note(64, 10.2, .5) };
        var found = new List<ArNoteShape>();
        ArNotes.Build(Calibration, View, notes, 10, 20, Style, found);
        Assert.Equal(new[] { 60, 64 }, found.Select(s => s.Pitch).Order());
    }

    [Fact]
    public void A_whole_song_at_any_moment_draws_what_a_plain_count_says_it_should()
    {
        var random = new Random(99);
        var notes = new List<NoteEvent>();
        for (var i = 0; i < 600; i++) notes.Add(Note(random.Next(15, 115), random.NextDouble() * 120, random.NextDouble() < .1 ? random.NextDouble() * 12 : random.NextDouble() * 1.2 + .02));
        notes.Sort((a, b) => a.Start.CompareTo(b.Start));
        var longest = notes.Max(n => n.Duration);
        var shapes = new List<ArNoteShape>();
        foreach (var view in new[] { View, new ArView(1280, 720, 700, 700, PictureFit.Fill, true) })
            for (var step = 0; step < 300; step++)
            {
                var position = random.NextDouble() * 125 - 2;
                ArNotes.Build(Calibration, view, notes, position, longest, Style, shapes);
                var expected = 0;
                foreach (var note in notes)
                {
                    if (!Calibration.Layout.Contains(note.Pitch) || note.End < position) continue;
                    var length = Math.Max(note.Duration * Style.FallSpeed - Style.Gap, Style.MinLength);
                    var leading = -(note.Start - position) * Style.FallSpeed;
                    if (leading < -Style.Reach || Math.Max(leading - length, -Style.Reach) >= Math.Min(leading, 0) - 1e-9) continue;
                    expected++;
                }
                Assert.Equal(expected, shapes.Count);
                var seenBlack = false;
                foreach (var shape in shapes)
                {
                    Assert.True(shape.Patch.Area > 0 && shape.Fade is >= 0 and <= 1);
                    Assert.True(!seenBlack || shape.Black, "a white note came after a black one");
                    seenBlack |= shape.Black;
                    Assert.Equal(KeyboardLayout.IsBlack(shape.Pitch), shape.Black);
                    Assert.Equal(notes[shape.NoteIndex].Pitch, shape.Pitch);
                }
            }
    }
}
