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
}
