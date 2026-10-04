using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The keyboard geometry every layer of the stage (and, later, the AR overlay) places things by. The first group
/// pins the full keyboard to the numbers the stage drew with before the layout existed — those are re-derived here
/// from the written-out rule, not read back from the layout — so moving the geometry into one class cannot have moved
/// a single key. The rest states what makes a keyboard a keyboard, for every size the app offers.
/// </summary>
public class KeyboardLayoutTests
{
    public static TheoryData<int> Sizes => new() { 88, 76, 61, 49 };

    private static KeyboardLayout Layout(int keys) => KeyboardLayout.ForKeyCount(keys);

    private static IEnumerable<int> Pitches(KeyboardLayout layout) => Enumerable.Range(layout.FirstPitch, layout.KeyCount);

    // ---- The full keyboard is the keyboard the stage always had --------------------------------------------

    [Fact]
    public void The_full_keyboard_is_A0_to_C8_with_52_white_keys_and_36_black_ones()
    {
        var full = KeyboardLayout.Standard88;
        Assert.Equal(21, full.FirstPitch);
        Assert.Equal(108, full.LastPitch);
        Assert.Equal(52, full.WhiteCount);
        Assert.Equal(36, Pitches(full).Count(KeyboardLayout.IsBlack));
    }

    [Fact]
    public void Every_centre_of_the_full_keyboard_is_where_the_stage_drew_it_before()
    {
        // The rule exactly as the stage wrote it: white keys 1/52 wide, a white key's centre half a key past the
        // whites below it, a black key's centre at those whites plus its offset.
        double[] offset = [0, -0.08, 0, 0.08, 0, 0, -0.10, 0, 0.00, 0, 0.10, 0];
        var whitesBelow = 0;
        for (var pitch = 0; pitch < 128; pitch++)
        {
            var black = pitch % 12 is 1 or 3 or 6 or 8 or 10;
            var expected = black ? (whitesBelow + offset[pitch % 12]) / 52.0 : (whitesBelow + .5) / 52.0;
            Assert.Equal(expected, KeyboardLayout.Standard88.CenterX(pitch), 12);
            if (pitch >= 21 && pitch < 109 && !black) whitesBelow++;
        }
    }

    // ---- What every size has to satisfy -----------------------------------------------------------------

    [Theory]
    [InlineData(88, 21, 52)]
    [InlineData(76, 28, 45)]
    [InlineData(61, 36, 36)]
    [InlineData(49, 36, 29)]
    public void Each_size_has_the_range_and_the_white_keys_of_the_usual_instrument(int keys, int first, int whites)
    {
        var layout = Layout(keys);
        Assert.Equal(keys, layout.KeyCount);
        Assert.Equal(first, layout.FirstPitch);
        Assert.Equal(whites, layout.WhiteCount);
        Assert.Equal(first + keys - 1, layout.LastPitch);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void A_keyboard_starts_and_ends_on_a_white_key(int keys)
    {
        var layout = Layout(keys);
        Assert.False(KeyboardLayout.IsBlack(layout.FirstPitch));
        Assert.False(KeyboardLayout.IsBlack(layout.LastPitch));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void White_keys_are_equally_wide_and_tile_the_whole_width_without_a_gap(int keys)
    {
        var layout = Layout(keys);
        var whites = Pitches(layout).Where(pitch => !KeyboardLayout.IsBlack(pitch)).ToArray();
        Assert.Equal(layout.WhiteCount, whites.Length);
        Assert.Equal(0, layout.Span(whites[0]).Left, 12);
        Assert.Equal(1, layout.Span(whites[^1]).Right, 12);
        for (var index = 1; index < whites.Length; index++)
        {
            var before = layout.Span(whites[index - 1]);
            var after = layout.Span(whites[index]);
            Assert.Equal(before.Right, after.Left, 12);
            Assert.Equal(layout.WhiteWidth, after.Right - after.Left, 12);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void A_black_key_is_narrower_and_sits_between_its_two_white_neighbours(int keys)
    {
        var layout = Layout(keys);
        foreach (var pitch in Pitches(layout).Where(KeyboardLayout.IsBlack))
        {
            var black = layout.Span(pitch);
            Assert.Equal(KeyboardLayout.BlackWidthShare * layout.WhiteWidth, black.Right - black.Left, 12);
            Assert.True(layout.Contains(pitch - 1) && layout.Contains(pitch + 1), "a black key is never the end of a keyboard");
            Assert.True(black.Left >= layout.Span(pitch - 1).Left && black.Right <= layout.Span(pitch + 1).Right,
                $"black key {pitch} has to stay inside the two white keys around it");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Centres_rise_from_key_to_key_and_stay_inside_the_keyboard(int keys)
    {
        var layout = Layout(keys);
        var previous = -1.0;
        foreach (var pitch in Pitches(layout))
        {
            var centre = layout.CenterX(pitch);
            Assert.InRange(centre, 0, 1);
            Assert.True(centre > previous, $"pitch {pitch} should sit to the right of the key before it");
            previous = centre;
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void The_key_under_a_key_s_own_centre_is_that_key(int keys)
    {
        var layout = Layout(keys);
        foreach (var pitch in Pitches(layout))
        {
            var black = KeyboardLayout.IsBlack(pitch);
            Assert.Equal(pitch, layout.PitchAt(layout.CenterX(pitch), inBlackRow: black));
            if (black)
                Assert.False(KeyboardLayout.IsBlack(layout.PitchAt(layout.CenterX(pitch), inBlackRow: false)),
                    "below the black keys the same position is a white key");
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void A_position_off_the_keyboard_is_held_at_its_ends(int keys)
    {
        var layout = Layout(keys);
        Assert.Equal(layout.FirstPitch, layout.PitchAt(-3, false));
        Assert.Equal(layout.LastPitch, layout.PitchAt(7, false));
        Assert.Equal(layout.FirstPitch, layout.PitchAt(double.NaN, true));
    }

    [Fact]
    public void A_pitch_the_keyboard_does_not_have_stays_inside_the_table()
    {
        var layout = KeyboardLayout.ForKeyCount(61);
        Assert.False(layout.Contains(21));
        Assert.Equal(0, layout.WhitesBelow(-5));
        Assert.Equal(layout.WhiteCount, layout.WhitesBelow(500));
        Assert.InRange(layout.CenterX(21), 0, 1);
        Assert.InRange(layout.CenterX(127), 1, 1.1); // past the right edge, where the keyboard would go on
    }

    // ---- Choosing a layout ------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_key_count_is_the_full_keyboard()
    {
        Assert.Same(KeyboardLayout.Standard88, KeyboardLayout.ForKeyCount(0));
        Assert.Same(KeyboardLayout.Standard88, KeyboardLayout.ForKeyCount(73));
    }

    [Theory]
    [InlineData(36, 84, 49)]   // C2 to C6 fits the smallest
    [InlineData(36, 96, 61)]   // up to C7 needs 61
    [InlineData(30, 100, 76)]  // wider than C2 to C7 but inside E1 to G7
    [InlineData(21, 108, 88)]  // the whole range
    [InlineData(10, 100, 88)]  // nothing offered holds it: the full keyboard
    public void The_smallest_keyboard_that_holds_what_was_played_is_chosen(int lowest, int highest, int keys)
    {
        Assert.Equal(keys, KeyboardLayout.Fitting(lowest, highest).KeyCount);
    }
}
