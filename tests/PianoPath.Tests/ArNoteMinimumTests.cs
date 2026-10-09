using Xunit;

namespace PianoPath.Tests;

/// <summary>AR-S4 remainder: the minimum note length and the cut a following note makes (Camera/ArNoteMinimum.cs).</summary>
public class ArNoteMinimumTests
{
    [Fact]
    public void A_short_falling_note_grows_upward_from_its_head_to_the_minimum()
    {
        // head at the bottom (y 102), width 10, minimum 2 × width = 20
        var (top, bottom) = ArNoteMinimum.Extend(100, 102, 10, 2, headAtBottom: true, double.NaN);
        Assert.Equal(82, top, 9);
        Assert.Equal(102, bottom, 9);
    }

    [Fact]
    public void A_short_rising_note_grows_downward_from_its_head_to_the_minimum()
    {
        var (top, bottom) = ArNoteMinimum.Extend(100, 102, 10, 2, headAtBottom: false, double.NaN);
        Assert.Equal(100, top, 9);
        Assert.Equal(120, bottom, 9);
    }

    [Fact]
    public void A_note_already_long_enough_is_not_changed()
    {
        Assert.Equal((100d, 150d), ArNoteMinimum.Extend(100, 150, 10, 2, true, double.NaN));
        Assert.Equal((100d, 150d), ArNoteMinimum.Extend(100, 150, 10, 2, false, double.NaN));
    }

    [Fact]
    public void Zero_or_an_unusable_width_changes_nothing()
    {
        Assert.Equal((100d, 102d), ArNoteMinimum.Extend(100, 102, 10, 0, true, double.NaN));
        Assert.Equal((100d, 102d), ArNoteMinimum.Extend(100, 102, 0, 2, true, double.NaN));
        Assert.Equal((100d, 102d), ArNoteMinimum.Extend(100, 102, double.NaN, 2, true, double.NaN));
    }

    [Fact]
    public void A_following_note_on_the_same_pitch_cuts_the_growth_short()
    {
        // the next note's head sits at y 90, so the grown top may not pass it
        var (top, bottom) = ArNoteMinimum.Extend(100, 102, 10, 2, headAtBottom: true, limit: 90);
        Assert.Equal(90, top, 9);
        Assert.Equal(102, bottom, 9);
        // a limit that is already past the wanted top changes nothing more than the minimum allows
        (top, bottom) = ArNoteMinimum.Extend(100, 102, 10, 2, headAtBottom: true, limit: 95);
        Assert.Equal(95, top, 9);
        // rising: the growth is cut at the next note's top
        (top, bottom) = ArNoteMinimum.Extend(100, 102, 10, 2, headAtBottom: false, limit: 110);
        Assert.Equal(110, bottom, 9);
    }

    [Fact]
    public void A_following_note_with_no_room_leaves_the_note_as_it_is()
    {
        // the next note's head is already below the note's top: there is nothing to grow into
        Assert.Equal((100d, 102d), ArNoteMinimum.Extend(100, 102, 10, 2, headAtBottom: true, limit: 101));
    }
}
