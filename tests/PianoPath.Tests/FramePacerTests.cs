using Xunit;

namespace PianoPath.Tests;

/// <summary>How a video file standing in for a camera is held to its own times, while a live camera is never slowed down.</summary>
public class FramePacerTests
{
    private const long Tenth = 1_000_000; // 0.1 s in the 100 ns units Media Foundation uses

    [Fact]
    public void The_first_frame_is_shown_at_once()
    {
        Assert.Equal(0, new FramePacer().WaitSeconds(5 * Tenth, nowSeconds: 100));
    }

    [Fact]
    public void A_frame_is_shown_when_its_time_has_come_counted_from_the_first_frame()
    {
        var pacer = new FramePacer();
        pacer.WaitSeconds(10 * Tenth, 100.0);                                  // first frame, stamped 1.0 s, shown at clock 100.0
        Assert.Equal(0.1 - 0.04, pacer.WaitSeconds(11 * Tenth, 100.04), 9);    // due at 100.1, it is 100.04
        Assert.Equal(0.2 - 0.15, pacer.WaitSeconds(12 * Tenth, 100.15), 9);    // due at 100.2
    }

    [Fact]
    public void A_frame_that_is_late_is_shown_at_once_and_the_stall_is_not_paid_back_in_a_burst()
    {
        var pacer = new FramePacer();
        pacer.WaitSeconds(0, 0);
        Assert.Equal(0, pacer.WaitSeconds(1 * Tenth, 5.0));                    // 5 s late
        // Counting restarted from there: the next frame, 0.1 s later in the file, waits 0.1 s of the clock.
        Assert.Equal(0.1, pacer.WaitSeconds(2 * Tenth, 5.0), 9);
    }

    [Fact]
    public void Times_that_do_not_rise_fall_back_to_a_steady_thirty_frames_a_second()
    {
        var pacer = new FramePacer();
        pacer.WaitSeconds(3 * Tenth, 0);
        Assert.Equal(FramePacer.FallbackSeconds, pacer.WaitSeconds(3 * Tenth, 0.01));
        Assert.Equal(FramePacer.FallbackSeconds, pacer.WaitSeconds(0, 0.02));
        Assert.Equal(1.0 / 30, FramePacer.FallbackSeconds, 12);
    }

    [Fact]
    public void A_corrupt_time_far_in_the_future_cannot_freeze_the_overlay()
    {
        var pacer = new FramePacer();
        pacer.WaitSeconds(0, 0);
        Assert.Equal(FramePacer.MaxWaitSeconds, pacer.WaitSeconds(100_000 * Tenth, 0.001));
    }

    [Fact]
    public void Reset_starts_counting_again_as_for_a_file_that_was_rewound()
    {
        var pacer = new FramePacer();
        pacer.WaitSeconds(0, 0);
        pacer.WaitSeconds(5 * Tenth, 0.5);
        pacer.Reset();
        Assert.Equal(0, pacer.WaitSeconds(0, 10));
        Assert.Equal(0.1, pacer.WaitSeconds(1 * Tenth, 10), 9);
    }
}
