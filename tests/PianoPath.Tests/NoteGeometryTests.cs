using System;
using Xunit;

namespace PianoPath.Tests;

public class NoteGeometryTests
{
    private const double Lane = 1920.0 / 88;                 // 21.818...
    private const double Black = 1920.0 / 52 * KeyboardLayout.BlackWidthShare; // 19.2
    private const double Speed = 258;                        // FallSpeed at NoteFallSpeed 550
    private const double Gap = 2;

    [Fact]
    public void BlackKeyWidth_matches_the_standard_keyboard()
    {
        Assert.Equal(19.2, NoteGeometry.BlackKeyWidth(1920, 52), 9);
        Assert.Equal(36.9230769, 1920.0 / 52, 6);
    }

    [Fact]
    public void Width_keeps_the_lane_share_at_the_default()
    {
        Assert.Equal(17.4545, NoteGeometry.Width(Lane, .8, Black), 4);
        Assert.Equal(47.2727, NoteGeometry.Width(Lane, .8, Black) / (1920.0 / 52) * 100, 3);
    }

    [Fact]
    public void Bar_width_takes_the_slider_without_a_floor_of_its_own()
    {
        // The settings clamp the slider to 30-100 and the dock starts it at 30, so the low end is reachable.
        // It used to be floored at 60 for the held bar only: a 30% preset drew a 60% bar on the software stage,
        // next to 30% falling notes and 30% bars on the GPU.
        Assert.Equal(30, NoteGeometry.BarWidth(100, 30, 100), 9);
        Assert.Equal(45, NoteGeometry.BarWidth(100, 45, 100), 9);
        Assert.Equal(60, NoteGeometry.BarWidth(100, 60, 100), 9);
        // The floor would have made 30 and 60 the same bar; they are not.
        Assert.NotEqual(NoteGeometry.BarWidth(100, 30, 100), NoteGeometry.BarWidth(100, 60, 100));
        // Same law as Width, which the engine and AR already call with a fraction.
        foreach (var percent in new[] { 30, 45, 60, 80, 100 })
        {
            Assert.Equal(NoteGeometry.Width(Lane, percent / 100.0, Black), NoteGeometry.BarWidth(Lane, percent, Black), 12);
            // And the black-key cap still wins at the top of the range.
            Assert.True(NoteGeometry.BarWidth(Lane, percent, Black) <= Black * NoteGeometry.BlackKeyFill + 1e-12);
        }
        Assert.Equal(19.6, NoteGeometry.BarWidth(100, 100, 20), 9);
    }

    [Theory]
    [InlineData(1600, 80, 14.5455)]   // 900p, unchanged
    [InlineData(1600, 100, 15.68)]     // 900p, 98% of a 16 px black key
    [InlineData(1920, 80, 17.4545)]   // 1080p, unchanged
    [InlineData(1920, 100, 18.816)]   // 1080p, 98% of a 19.2 px black key
    [InlineData(2560, 80, 23.2727)]   // 1440p, unchanged
    [InlineData(2560, 100, 25.088)]   // 1440p, 98% of a 25.6 px black key
    public void Width_clamps_the_same_at_every_aspect(int width, int percent, double expected)
    {
        var lane = width / 88.0;
        var black = NoteGeometry.BlackKeyWidth(width, 52);
        // percent is an int: divide by 100.0, or 80 / 100 is the integer 0 and every case expects a zero-width note.
        Assert.Equal(expected, NoteGeometry.Width(lane, percent / 100.0, black), 4);
    }

    [Fact]
    public void Width_clamps_only_past_eighty_six_percent()
    {
        Assert.Equal(18.7636, NoteGeometry.Width(Lane, .86, Black), 3);
        Assert.Equal(18.9818, Lane * .87, 3);
        Assert.Equal(18.816, NoteGeometry.Width(Lane, .87, Black), 3);
        Assert.Equal(18.816, NoteGeometry.Width(Lane, 1.0, Black), 3);
        Assert.Equal(98, NoteGeometry.Width(Lane, 1.0, Black) / Black * 100, 6);
    }

    [Fact]
    public void Height_is_the_sounding_time() =>
        Assert.Equal(1030, NoteGeometry.Height(4, Speed, Gap, 16), 9);

    [Fact]
    public void Height_keeps_a_long_note_whole()
    {
        Assert.Equal(1546, NoteGeometry.Height(6, Speed, Gap, 16), 9);
        Assert.Equal(4.0, (NoteGeometry.Height(4, Speed, Gap, 16) + Gap) / Speed, 9);
    }

    [Theory]
    [InlineData(715.5, 42.93)]    // 900p
    [InlineData(858.6, 51.516)]   // 1080p
    [InlineData(1212.0, 72.72)]   // 1440p
    public void Enter_fade_covers_six_percent_of_the_fall_height(double hitY, double expected) =>
        Assert.Equal(expected, NoteGeometry.EnterFadePixels(hitY), 2);

    [Fact]
    public void Enter_fade_matches_the_shader_band_and_is_shared_with_the_note_sprites()
    {
        Assert.Equal(0, NoteGeometry.EnterFade(0, 1000), 9);
        Assert.Equal(.5, NoteGeometry.EnterFade(30, 1000), 9);   // 6% of 1000 DIPs = 60
        Assert.Equal(1, NoteGeometry.EnterFade(60, 1000), 9);
        Assert.Equal(1, NoteGeometry.EnterFade(500, 1000), 9);
        Assert.Equal(0, NoteGeometry.EnterFade(-20, 1000), 9);
        Assert.Equal(0, NoteGeometry.EnterFade(0, 0), 9);        // the one-DIP floor
        Assert.Equal(1, NoteGeometry.EnterFade(1, 0), 9);
        Assert.Equal(1, NoteGeometry.EnterFade(double.NaN, 1000), 9);
    }

    [Fact]
    public void Enter_fade_never_collapses_on_a_tiny_stage() =>
        Assert.Equal(1, NoteGeometry.EnterFadePixels(0));

    [Fact]
    public void Height_stops_at_twenty_seconds()
    {
        Assert.Equal(5158, NoteGeometry.Height(20, Speed, Gap, 16), 9);
        Assert.Equal(5158, NoteGeometry.Height(30, Speed, Gap, 16), 9);
        Assert.Equal(20.0, NoteGeometry.MaxNoteSeconds, 9);
    }

    [Fact]
    public void Height_keeps_the_readable_minimum()
    {
        Assert.Equal(16, NoteGeometry.Height(0, Speed, Gap, 16), 9);
        Assert.Equal(16, NoteGeometry.Height(.001, Speed, Gap, 16), 9);
    }

    [Fact]
    public void Height_does_not_change_with_the_window()
    {
        // The helper has no window argument at all; this pins the reason N2 exists.
        var four = NoteGeometry.Height(4, Speed, Gap, 16);
        var six = NoteGeometry.Height(6, Speed, Gap, 16);
        Assert.Equal(1030, four, 9);
        Assert.Equal(1546, six, 9);
    }
}
