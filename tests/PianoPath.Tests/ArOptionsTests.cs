using Xunit;

namespace PianoPath.Tests;

/// <summary>What the player's overlay choices mean for drawing it.</summary>
public class ArOptionsTests
{
    private static NoteEvent Note(int pitch, double start, double duration) => new() { Pitch = pitch, Start = start, Duration = duration };

    [Fact]
    public void The_default_is_off_in_learn_mode_with_values_inside_their_ranges()
    {
        var d = ArOptions.Default;
        Assert.False(d.Enabled);
        Assert.Equal(ArModes.Learn, d.Mode);
        Assert.Equal(d, d.Normalised());
        Assert.True(d.Opacity is >= ArOptions.OpacityMin and <= ArOptions.OpacityMax);
        Assert.True(d.Lookahead is >= ArOptions.LookaheadMin and <= ArOptions.LookaheadMax);
        Assert.True(d.KeyLight is >= ArOptions.KeyLightMin and <= ArOptions.KeyLightMax);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1e300)]
    [InlineData(1e300)]
    [InlineData(0.0)]
    public void Any_number_ends_up_in_range_and_drawable(double wild)
    {
        var o = new ArOptions(true, "?", wild, wild, wild).Normalised();
        Assert.True(o.Opacity is >= ArOptions.OpacityMin and <= ArOptions.OpacityMax);
        Assert.True(o.Lookahead is >= ArOptions.LookaheadMin and <= ArOptions.LookaheadMax);
        Assert.True(o.KeyLight is >= ArOptions.KeyLightMin and <= ArOptions.KeyLightMax);
        Assert.True(o.NoteStyle.IsUsable);
        Assert.InRange(o.LayerOpacity, 0f, 1f);
        Assert.InRange(o.KeyLightStrength, 0f, 1f);
        Assert.Equal(o, o.Normalised());                     // normalising twice changes nothing
    }

    [Fact]
    public void A_not_a_number_takes_the_default_and_an_infinity_is_not_mistaken_for_one()
    {
        Assert.Equal(ArOptions.OpacityDefault, new ArOptions(true, "Learn", double.NaN, 3, 60).Normalised().Opacity);
        Assert.Equal(ArOptions.LookaheadDefault, new ArOptions(true, "Learn", 100, double.NaN, 60).Normalised().Lookahead);
    }

    [Theory]
    [InlineData("Learn", "Learn")]
    [InlineData("Perform", "Perform")]
    [InlineData("learn", "Learn")]          // names are stored exactly; a different case is not one of ours
    [InlineData("", "Learn")]
    [InlineData(null, "Learn")]
    [InlineData("Karaoke", "Learn")]
    public void An_unknown_mode_is_learn(string? stored, string expected) => Assert.Equal(expected, ArModes.Normalize(stored));

    [Fact]
    public void Only_learn_draws_the_notes_to_come()
    {
        Assert.True(new ArOptions(true, ArModes.Learn, 100, 3, 60).ShowsNotes);
        Assert.False(new ArOptions(true, ArModes.Perform, 100, 3, 60).ShowsNotes);
        Assert.True(new ArOptions(true, "nonsense", 100, 3, 60).ShowsNotes);
    }

    [Fact]
    public void Rising_live_midi_notes_are_enabled_by_default_but_only_draw_in_perform_mode()
    {
        Assert.True(ArOptions.Default.RisingMidiNotes);
        Assert.False(ArOptions.Default.ShowsRisingMidiNotes);
        var perform = new ArOptions(true, ArModes.Perform, 100, 3, 60);
        Assert.True(perform.ShowsRisingMidiNotes);
        Assert.False((perform with { RisingMidiNotes = false }).ShowsRisingMidiNotes);
        Assert.False((perform with { Mode = ArModes.Learn }).ShowsRisingMidiNotes);
    }

    [Fact]
    public void Percent_becomes_a_fraction()
    {
        var o = new ArOptions(true, ArModes.Learn, 40, 3, 25);
        Assert.Equal(.4f, o.LayerOpacity, 4);
        Assert.Equal(.25f, o.KeyLightStrength, 4);
        Assert.Equal(1f, new ArOptions(true, ArModes.Learn, 100, 3, 100).LayerOpacity);
        Assert.Equal(0f, new ArOptions(true, ArModes.Learn, 100, 3, 0).KeyLightStrength);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(3.0)]
    [InlineData(6.0)]
    public void The_lookahead_is_how_many_seconds_before_it_sounds_a_note_appears(double seconds)
    {
        var options = new ArOptions(true, ArModes.Learn, 100, seconds, 60);
        var style = options.NoteStyle;
        Assert.Equal(seconds, style.Reach / style.FallSpeed, 9);
        var calibration = ArProjectionTests.Calibrate([(70, 292), (1214, 268), (1236, 421), (52, 449)]);
        var view = new ArView(1280, 720, 1000, 600, PictureFit.Fit, false);
        int Count(double noteStart)
        {
            var into = new List<ArNoteShape>();
            ArNotes.Build(calibration, view, [Note(60, noteStart, .5)], 10, .5, style, into);
            return into.Count;
        }
        Assert.Equal(0, Count(10 + seconds * 1.02));         // not yet
        Assert.Equal(1, Count(10 + seconds * .98));          // just come into view
        Assert.Equal(1, Count(10 + seconds * .25));          // on its way down
    }

    [Fact]
    public void A_longer_lookahead_shows_more_of_the_song()
    {
        var calibration = ArProjectionTests.Calibrate([(70, 292), (1214, 268), (1236, 421), (52, 449)]);
        var view = new ArView(1280, 720, 1000, 600, PictureFit.Fit, false);
        NoteEvent[] song = [.. Enumerable.Range(0, 12).Select(i => Note(48 + i, 10 + i * .5, .4))];
        int Visible(double seconds)
        {
            var into = new List<ArNoteShape>();
            ArNotes.Build(calibration, view, song, 10, .4, new ArOptions(true, ArModes.Learn, 100, seconds, 60).NoteStyle, into);
            return into.Count;
        }
        Assert.True(Visible(1) < Visible(2) && Visible(2) < Visible(4));
    }

    [Fact]
    public void Perspective_notes_require_the_user_toggle_but_share_one_path_across_quality_tiers()
    {
        Assert.False(ArOptions.Default.NotesProjected);
        Assert.False(ArOptions.Default.LivePressTrails);
        foreach (var quality in new[] { ArQualityTier.Lite, ArQualityTier.Standard, ArQualityTier.Ultra })
        {
            var options = ArOptions.Default with { NotesInPerspective = true, Quality = quality };
            Assert.True(options.NotesProjected);
            Assert.False(options.LivePressTrails);
            Assert.True((options with { LivePressTrails = true }).LivePressTrails);
        }
        Assert.False((ArOptions.Default with { NotesInPerspective = false, Quality = ArQualityTier.Ultra }).NotesProjected);
    }

    [Fact]
    public void Note_direction_is_a_separate_3d_setting_normalized_on_every_path()
    {
        var options = new ArOptions(true, ArModes.Learn, 100, 3, 60,
            NoteDirectionTilt: 125, NoteDirectionAzimuth: -70).Normalised();
        Assert.Equal(125, options.NoteDirection.TiltDegrees);
        Assert.Equal(-70, options.NoteDirection.AzimuthDegrees);
        Assert.NotEqual(ArNoteDirection.Default.Vector, options.NoteDirection.Vector);

        var clamped = new ArOptions(true, ArModes.Learn, 100, 3, 60,
            NoteDirectionTilt: 999, NoteDirectionAzimuth: -999).Normalised();
        Assert.Equal(ArOptions.NoteDirectionTiltMax, clamped.NoteDirection.TiltDegrees);
        Assert.Equal(ArOptions.NoteDirectionAzimuthMin, clamped.NoteDirection.AzimuthDegrees);
        var wild = new ArOptions(true, ArModes.Learn, 100, 3, 60,
            NoteDirectionTilt: double.NaN, NoteDirectionAzimuth: double.PositiveInfinity).Normalised();
        Assert.Equal(ArNoteDirection.Default, wild.NoteDirection);
        Assert.Equal(wild, wild.Normalised());
    }

    [Fact]
    public void The_camera_delay_defaults_to_none_and_stays_inside_zero_to_three_hundred_milliseconds()
    {
        Assert.Equal(0, ArOptions.Default.Delay);
        Assert.Equal(0, ArOptions.Default.DelaySeconds);
        Assert.Equal(0, new ArOptions(true, "Learn", 100, 3, 60).Delay);                       // the five-value form still means no delay
        Assert.Equal(.12, new ArOptions(true, "Learn", 100, 3, 60, 120).DelaySeconds, 12);
        Assert.Equal(300, new ArOptions(true, "Learn", 100, 3, 60, 9000).Normalised().Delay);
        Assert.Equal(0, new ArOptions(true, "Learn", 100, 3, 60, -40).Normalised().Delay);
        foreach (var wild in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300, -1e300 })
        {
            var delay = new ArOptions(true, "Learn", 100, 3, 60, wild).Normalised().Delay;
            Assert.InRange(delay, ArOptions.DelayMin, ArOptions.DelayMax);
            Assert.InRange(new ArOptions(true, "Learn", 100, 3, 60, wild).DelaySeconds, 0, .3);
        }
        Assert.Equal(ArOptions.DelayDefault, new ArOptions(true, "Learn", 100, 3, 60, double.NaN).Normalised().Delay);
    }
}
