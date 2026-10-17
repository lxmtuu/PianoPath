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
        var shape = Assert.Single(shapes);
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
        var shape = Assert.Single(Build([Note(60, 11, .5)], 10));
        var (left, right) = Calibration.Layout.Span(60);
        var centre = (left + right) / 2;
        var (u, v) = Keys(shape.Patch.FrontRight);
        Near(centre + (right - left) / 2 * Style.WidthShare, u, 1e-6);
        Near(-1.5, v, 1e-6);                               // one second at 1.5 key depths per second
        Assert.False(shape.Sounding);
        // twice the time, twice the distance
        var later = Assert.Single(Build([Note(60, 12, .5)], 10));
        Near(-3, Keys(later.Patch.FrontRight).V, 1e-6);
    }

    [Fact]
    public void A_note_comes_into_view_at_the_reach_and_fades_in_over_its_last_quarter()
    {
        var edge = Style.Reach / Style.FallSpeed;
        Assert.Empty(Build([Note(60, 10 + edge + .01, .5)], 10));
        var barely = Assert.Single(Build([Note(60, 10 + edge - .01, .5)], 10));
        Assert.InRange(barely.Fade, 0, .05);
        var halfway = Assert.Single(Build([Note(60, 10 + edge - .5 * Style.Reach * .25 / Style.FallSpeed, .5)], 10));
        Near(.5, halfway.Fade, 1e-9);
        var nearer = Assert.Single(Build([Note(60, 10 + edge * .6, .5)], 10));
        Assert.Equal(1, nearer.Fade);
    }

    [Fact]
    public void A_long_note_is_cut_off_at_the_reach()
    {
        var shape = Assert.Single(Build([Note(60, 10, 20)], 10));
        Near(-Style.Reach, Keys(shape.Patch.BackLeft).V, 1e-6);
        Near(0, Keys(shape.Patch.FrontLeft).V, 1e-6);
    }

    [Fact]
    public void A_sounding_note_slides_over_the_keys_and_is_used_up_as_it_goes()
    {
        var note = Note(60, 10, .5);
        var length = .5 * Style.FallSpeed - Style.Gap;
        var early = Assert.Single(Build([note], 10.2));
        Near(0, Keys(early.Patch.FrontLeft).V, 1e-6);                          // held at the keys
        Near(.2 * Style.FallSpeed - length, Keys(early.Patch.BackLeft).V, 1e-6); // its tail drawn in
        Assert.True(early.Sounding);
        var late = Assert.Single(Build([note], 10.45));
        Assert.True(late.Patch.Area < early.Patch.Area);
        Assert.Empty(Build([note], 10.5));                              // nothing is left of it
        Assert.Empty(Build([note], 12));
        Assert.Empty(Build([note], 10.6));
    }

    [Fact]
    public void A_very_short_note_is_still_drawn_at_the_shortest_length()
    {
        var shape = Assert.Single(Build([Note(60, 11, .01)], 10));
        var length = Keys(shape.Patch.FrontLeft).V - Keys(shape.Patch.BackLeft).V;
        Near(Style.MinLength, length, 1e-6);
    }

    [Fact]
    public void A_note_is_as_wide_as_its_key_allows_and_black_notes_come_last()
    {
        var notes = new[] { Note(61, 10.5, .5), Note(60, 10.5, .5), Note(63, 10.6, .5), Note(62, 10.6, .5) };
        var shapes = Build(notes, 10).ToList();
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
        var shapes = Build([Note(keys61.FirstPitch - 1, 10.5, .5), Note(keys61.FirstPitch, 10.5, .5), Note(keys61.LastPitch, 10.5, .5), Note(keys61.LastPitch + 1, 10.5, .5), Note(0, 10.5, .5), Note(127, 10.5, .5)], 10, calibration: calibration);
        Assert.Equal(new[] { keys61.FirstPitch, keys61.LastPitch }, shapes.Select(s => s.Pitch).Order());
    }

    [Fact]
    public void Nothing_is_drawn_for_a_style_that_cannot_draw_or_a_position_that_is_not_a_time()
    {
        var notes = new[] { Note(60, 10.5, .5) };
        foreach (var style in new[] { new ArNoteStyle(FallSpeed: 0), new ArNoteStyle(FallSpeed: double.NaN), new ArNoteStyle(Reach: -1), new ArNoteStyle(MinLength: 0), new ArNoteStyle(Gap: -1), new ArNoteStyle(WidthShare: 0), new ArNoteStyle(WidthShare: 1.5) })
        {
            Assert.False(style.IsUsable);
            Assert.Empty(Build(notes, 10, style));
        }
        Assert.True(ArNoteStyle.Default.IsUsable);
        Assert.Empty(Build(notes, double.NaN));
        Assert.Empty(Build(notes, double.PositiveInfinity));
        Assert.Empty(Build([], 10));
        Assert.Empty(Build(notes, 10, view: new ArView(0, 0, 10, 10, PictureFit.Fit, false)));
    }

    [Fact]
    public void A_note_that_began_long_ago_is_found_when_the_longest_duration_is_given()
    {
        var notes = new[] { Note(60, 0, 20), Note(64, 10.2, .5) };
        var found = new List<ArNoteShape>();
        ArNotes.Build(Calibration, View, notes, 10, 20, Style, found);
        Assert.Equal(new[] { 60, 64 }, found.Select(s => s.Pitch).Order());
    }

    // ---- the keys under the player's own hands: a bar of their own -------------------------------------------------

    private static List<ArLiveBar> BuildLive(IReadOnlyList<ArLivePress> presses, ArNoteStyle? style = null, ArCalibration? calibration = null, ArView? view = null)
    {
        var into = new List<ArLiveBar> { default };        // proves the list is cleared first
        ArNotes.BuildLive(calibration ?? Calibration, view ?? View, presses, style ?? Style, into);
        return into;
    }

    private static ArLivePress Down(int pitch, double heldSeconds, float strength = 1f) => new(pitch, heldSeconds, heldSeconds, true, strength);
    private static ArLivePress LetGo(int pitch, double heldSeconds, double since = 0, float strength = 1f) => new(pitch, heldSeconds + since, heldSeconds, false, strength);

    [Fact]
    public void A_pressed_key_draws_a_bar_that_leaves_the_key_along_the_keyboard()
    {
        var bar = Assert.Single(BuildLive([Down(60, .5)]));
        var (left, right) = Calibration.Layout.Span(60);
        var centre = (left + right) / 2; var half = (right - left) / 2 * Style.WidthShare;
        var length = .5 * Style.FallSpeed;
        // the leading edge is on the back edge of the keys, where the song's notes reach them, and the tail is how long the key has been down
        var (flx, fly) = Out(centre - half, 0); var (frx, fry) = Out(centre + half, 0);
        Near(flx, bar.Patch.FrontLeft.X); Near(fly, bar.Patch.FrontLeft.Y); Near(frx, bar.Patch.FrontRight.X); Near(fry, bar.Patch.FrontRight.Y);
        var (blx, bly) = Out(centre - half, -length); var (brx, bry) = Out(centre + half, -length);
        Near(blx, bar.Patch.BackLeft.X); Near(bly, bar.Patch.BackLeft.Y); Near(brx, bar.Patch.BackRight.X); Near(bry, bar.Patch.BackRight.Y);
        Assert.True(bar.Held); Assert.Equal(60, bar.Pitch); Assert.Equal(1f, bar.Strength);
        Near(1, bar.Fade, 1e-9);                                         // a held key stays fully visible without an opt-in fade graph
        Assert.True(Keys(bar.Patch.FrontRight).U - Keys(bar.Patch.FrontLeft).U < Calibration.Layout.WhiteWidth, "a bar is narrower than its key, as a note is");
        // twice as long a press, twice as far from the key
        var later = Assert.Single(BuildLive([Down(60, 1)]));
        Near(-2 * length, Keys(later.Patch.BackLeft).V, 1e-6);
        Near(0, Keys(later.Patch.FrontLeft).V, 1e-6);
    }

    [Fact]
    public void A_bar_of_a_press_and_a_note_of_the_song_take_the_same_lane()
    {
        var bar = Assert.Single(BuildLive([Down(60, .5)]));
        var note = Assert.Single(Build([Note(60, 10, .5)], 10));            // the same key, its leading edge just reaching the keys
        Near(note.Patch.FrontLeft.X, bar.Patch.FrontLeft.X, 1e-9);
        Near(note.Patch.FrontRight.X, bar.Patch.FrontRight.X, 1e-9);
        Near(note.Patch.FrontLeft.Y, bar.Patch.FrontLeft.Y, 1e-9);
        // both cover the same lane of the keyboard — the same four lines, the same two u values
        Near(Keys(note.Patch.FrontLeft).U, Keys(bar.Patch.FrontLeft).U, 1e-6);
        Near(Keys(note.Patch.BackLeft).U, Keys(bar.Patch.BackLeft).U, 1e-6);
        Near(Keys(note.Patch.BackRight).U, Keys(bar.Patch.BackRight).U, 1e-6);
        // the only difference is the gap a note keeps so two of them do not touch
        Near(Keys(note.Patch.BackLeft).V, Keys(bar.Patch.BackLeft).V + Style.Gap, 1e-6);
    }

    [Fact]
    public void The_bar_follows_the_keyboard_whichever_way_the_camera_sees_it()
    {
        var corners61 = new (double X, double Y)[] { (190, 276), (1088, 258), (1106, 432), (172, 452) };
        var mirrored88 = new (double X, double Y)[] { (1214, 268), (70, 292), (52, 449), (1236, 421) };
        foreach (var (calibration, what) in new[]
        {
            (Calibration, "the sample keyboard"),
            (ArProjectionTests.Calibrate(corners61, KeyboardLayout.ForKeyCount(61)), "a 61-key keyboard"),
            (ArProjectionTests.Calibrate(mirrored88), "the keyboard seen the other way round"),
        })
        {
            var bar = Assert.Single(BuildLive([Down(60, .5)], calibration: calibration));
            Near(0, Keys(bar.Patch.FrontLeft, calibration).V, 1e-6);              // the leading edge is on the back edge of the keys
            Near(-.75, Keys(bar.Patch.BackLeft, calibration).V, 1e-6);            // the tail lies along the keyboard's own v axis
            Assert.True(bar.Patch.Area > 0, $"{what}: the bar must not be inside out");
        }
        // the mirror is the same keyboard seen from the other side: the bar still leaves the key away from the player, up the picture
        var plain = Assert.Single(BuildLive([Down(60, .5)]));
        var flipped = Assert.Single(BuildLive([Down(60, .5)], calibration: ArProjectionTests.Calibrate(mirrored88)));
        Assert.True(plain.Patch.BackLeft.Y < plain.Patch.FrontLeft.Y && flipped.Patch.BackRight.Y < flipped.Patch.FrontRight.Y,
            "the bar goes away from the keys up the picture, whichever side of the keyboard the picture puts it on");
    }

    [Fact]
    public void A_key_that_went_down_this_instant_has_no_bar_yet()
    {
        Assert.Empty(BuildLive([Down(60, 0)]));
        Assert.Empty(BuildLive([Down(60, Style.MinLength / Style.FallSpeed - 1e-6)]));
        Assert.Single(BuildLive([Down(60, Style.MinLength / Style.FallSpeed + 1e-6)]));
    }

    [Fact]
    public void A_bar_stops_at_the_reach_and_stays_solid_while_the_key_is_held()
    {
        var bar = Assert.Single(BuildLive([Down(60, 20)]));
        Near(-Style.Reach, Keys(bar.Patch.BackLeft).V, 1e-6);
        Near(1, bar.Fade, 1e-9);
        Assert.True(bar.Held);
    }

    [Fact]
    public void A_key_that_was_let_go_stops_its_bar_and_takes_it_away_a_moment_later()
    {
        var held = Assert.Single(BuildLive([Down(60, .5)]));
        var justLetGo = Assert.Single(BuildLive([LetGo(60, .5)]));
        Assert.Equal(held.Patch, justLetGo.Patch);                          // the hand let go; the geometry did not change
        Assert.True(justLetGo.Fade < held.Fade);
        Assert.False(justLetGo.Held); Assert.True(held.Held);
        Assert.Equal(.9f, Assert.Single(BuildLive([Down(60, .5, .9f)])).Strength);
        // a moment later the bar is where the finger left it, only dimmer — it never grows after the key is up
        var later = Assert.Single(BuildLive([LetGo(60, .5, .12)]));
        Assert.Equal(held.Patch, later.Patch);
        Assert.True(later.Fade < justLetGo.Fade);
        // and once the moment has passed it is gone: the bar is about what the hand just did, not about the key
        Assert.Single(BuildLive([LetGo(60, .5, ArNotes.ReleaseLife - 1e-6)]));
        Assert.Empty(BuildLive([LetGo(60, .5, ArNotes.ReleaseLife)]));
        Assert.Empty(BuildLive([LetGo(60, .5, 5)]));
    }

    [Fact]
    public void A_lifetime_fade_walks_forward_while_the_key_is_held_and_the_default_style_is_untouched_by_it()
    {
        // The graphless default stays fully solid while held; this test pins the explicit lifetime-graph walk.
        var style = Style with { LiveFade = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .2, Max: 1) };
        var f = .5 * style.FallSpeed / style.Reach;
        var young = Assert.Single(BuildLive([Down(60, .5)], style: style));
        var grown = Assert.Single(BuildLive([Down(60, 20)], style: style));                       // at full stretch
        Near(.2 + .8 * f, young.Fade, 1e-9);
        Near(1, grown.Fade, 1e-9);
    }

    [Fact]
    public void A_lifetime_fade_walks_backwards_over_the_release_and_the_release_dim_still_takes_the_bar()
    {
        var style = Style with { LiveFade = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .2, Max: 1) };
        var f = .5 * style.FallSpeed / style.Reach;
        var held = Assert.Single(BuildLive([Down(60, .5)], style: style));
        var halfGone = Assert.Single(BuildLive([LetGo(60, .5, ArNotes.ReleaseLife / 2)], style: style));
        // the graph walked halfway back from where the finger left the bar, then the usual release dim on top
        Near((.2 + .8 * f * .5) * (.65 * .5), halfGone.Fade, 1e-9);
        Assert.True(halfGone.Fade < held.Fade);
    }

    [Fact]
    public void A_cover_fraction_lets_the_bar_run_onto_the_keys_and_a_negative_one_holds_it_free()
    {
        var covering = Assert.Single(BuildLive([Down(60, .5)], style: Style with { LiveBarCoverFraction = .6 }));
        Near(.6, Keys(covering.Patch.FrontLeft).V, 1e-6);    // the leading edge sits 60% of the key depth onto the keys
        Near(.6, Keys(covering.Patch.FrontRight).V, 1e-6);
        Near(-.75, Keys(covering.Patch.BackLeft).V, 1e-6);   // the tail keeps the stretch it always had
        var detached = Assert.Single(BuildLive([Down(60, 2)], style: Style with { LiveBarCoverFraction = -.5 }));
        Near(-.5, Keys(detached.Patch.FrontLeft).V, 1e-6);   // held free of the keys instead
        // a head pulled back behind the tail makes no bar at all: coming up short is "not visible yet", not an error
        Assert.Empty(BuildLive([Down(60, .4)], style: Style with { LiveBarCoverFraction = -.62 }));   // length .6 < .62
        Assert.Single(BuildLive([Down(60, .42)], style: Style with { LiveBarCoverFraction = -.62 })); // length .63 ≥ .62
    }

    [Fact]
    public void Black_keys_are_drawn_after_the_white_ones_at_their_own_width()
    {
        var bars = BuildLive([Down(61, .5), Down(60, .5), Down(63, .5), Down(62, .5)]).ToList();
        Assert.Equal(new[] { false, false, true, true }, bars.Select(bar => KeyboardLayout.IsBlack(bar.Pitch)));
        var white = bars.First(bar => bar.Pitch == 60); var black = bars.First(bar => bar.Pitch == 61);
        var whiteWidth = Keys(white.Patch.FrontRight).U - Keys(white.Patch.FrontLeft).U;
        var blackWidth = Keys(black.Patch.FrontRight).U - Keys(black.Patch.FrontLeft).U;
        Near(Calibration.Layout.WhiteWidth * Style.WidthShare, whiteWidth, 1e-6);
        Near(Calibration.Layout.WhiteWidth * KeyboardLayout.BlackWidthShare * Style.WidthShare, blackWidth, 1e-6);
    }

    [Fact]
    public void Keys_the_calibrated_keyboard_does_not_have_are_skipped_and_nothing_is_drawn_when_nothing_can_be()
    {
        var presses = new[] { Down(20, .5), Down(21, .5), Down(108, .5), Down(109, .5) };
        Assert.Equal(new[] { 21, 108 }, BuildLive(presses).Select(bar => bar.Pitch).Order());
        foreach (var style in new[] { new ArNoteStyle(FallSpeed: 0), new ArNoteStyle(Reach: -1), new ArNoteStyle(MinLength: 0), new ArNoteStyle(WidthShare: 0) })
            Assert.Empty(BuildLive([Down(60, .5)], style));
        Assert.Empty(BuildLive([Down(60, .5)], view: new ArView(0, 0, 10, 10, PictureFit.Fit, false)));
        Assert.Empty(BuildLive([]));
        // ages that make no sense: not a number, never went down, let go before it went down
        Assert.Empty(BuildLive([new ArLivePress(60, double.NaN, .5, true, 1f), new ArLivePress(64, double.PositiveInfinity, .5, true, 1f)]));
        Assert.Empty(BuildLive([new ArLivePress(67, 1, double.NaN, true, 1f), new ArLivePress(69, 1, double.PositiveInfinity, true, 1f)]));
        Assert.Empty(BuildLive([LetGo(60, .5, -.2)]));
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
