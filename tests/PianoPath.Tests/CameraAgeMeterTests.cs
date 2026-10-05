using Xunit;

namespace PianoPath.Tests;

public sealed class CameraAgeMeterTests
{
    private const long PerSecond = 1000;      // one tick a millisecond keeps the sums readable

    [Fact]
    public void A_frame_is_counted_once_at_the_age_it_had_when_first_drawn()
    {
        var meter = new CameraAgeMeter();
        Assert.Equal(new CameraAge(0, 0, 0, 0), meter.Snapshot);
        Assert.True(meter.Observe(1, 1000, 1042, PerSecond));
        Assert.False(meter.Observe(1, 1000, 1058, PerSecond));            // drawn again: the same picture, not a new delay
        Assert.False(meter.Observe(1, 1000, 1075, PerSecond));
        Assert.Equal(new CameraAge(1, 42, 42, 42), meter.Snapshot);
        Assert.True(meter.Observe(2, 1100, 1130, PerSecond));
        var age = meter.Snapshot;
        Assert.Equal(2, age.Frames);
        Assert.Equal(30, age.LastMilliseconds, 9);
        Assert.Equal(42, age.WorstMilliseconds, 9);
        Assert.Equal(42 + (30 - 42) * .1, age.AverageMilliseconds, 9);
    }

    [Fact]
    public void Ticks_are_turned_into_milliseconds_by_their_own_frequency()
    {
        var meter = new CameraAgeMeter();
        meter.Observe(1, 5_000_000, 5_000_000 + 250_000, 10_000_000);       // a tick is 100 ns: 250 000 of them are 25 ms
        Assert.Equal(25, meter.Snapshot.LastMilliseconds, 9);
    }

    [Theory]
    [InlineData(0L, 1000L, 1010L, PerSecond)]       // no sequence number: no frame yet
    [InlineData(-3L, 1000L, 1010L, PerSecond)]
    [InlineData(1L, 0L, 1010L, PerSecond)]          // a frame with no time says nothing
    [InlineData(1L, -5L, 1010L, PerSecond)]
    [InlineData(1L, 2000L, 1010L, PerSecond)]       // from the future
    [InlineData(1L, 1000L, 1010L, 0L)]              // no clock
    public void Something_that_says_nothing_is_not_counted(long sequence, long captured, long now, long perSecond)
    {
        var meter = new CameraAgeMeter();
        Assert.False(meter.Observe(sequence, captured, now, perSecond));
        Assert.Equal(new CameraAge(0, 0, 0, 0), meter.Snapshot);
    }

    [Fact]
    public void The_average_follows_a_change_of_delay_and_the_worst_keeps_the_worst()
    {
        var meter = new CameraAgeMeter();
        long clock = 1000;
        for (var i = 1; i <= 100; i++) { meter.Observe(i, clock, clock + 10, PerSecond); clock += 33; }
        Assert.Equal(10, meter.Snapshot.AverageMilliseconds, 6);
        for (var i = 101; i <= 200; i++) { meter.Observe(i, clock, clock + 90, PerSecond); clock += 33; }          // the camera got slow
        var age = meter.Snapshot;
        Assert.InRange(age.AverageMilliseconds, 89.9, 90.0);
        Assert.Equal(90, age.WorstMilliseconds, 9);
        for (var i = 201; i <= 300; i++) { meter.Observe(i, clock, clock + 10, PerSecond); clock += 33; }          // and recovered
        age = meter.Snapshot;
        Assert.InRange(age.AverageMilliseconds, 10, 10.1);
        Assert.Equal(90, age.WorstMilliseconds, 9);                                                              // the worst stays until a reset
        Assert.Equal(300, age.Frames);
    }

    [Fact]
    public void A_frame_skipped_is_not_counted_but_the_next_one_is()
    {
        var meter = new CameraAgeMeter();
        meter.Skip(7);
        Assert.False(meter.Observe(7, 1000, 1900, PerSecond));            // the picture already on show when the take began
        Assert.Equal(new CameraAge(0, 0, 0, 0), meter.Snapshot);
        Assert.True(meter.Observe(8, 2000, 2015, PerSecond));
        Assert.Equal(new CameraAge(1, 15, 15, 15), meter.Snapshot);
    }

    [Fact]
    public void Reset_forgets_everything_so_the_same_sequence_numbers_count_again()
    {
        var meter = new CameraAgeMeter();
        meter.Observe(1, 1000, 1500, PerSecond);
        meter.Reset();
        Assert.Equal(new CameraAge(0, 0, 0, 0), meter.Snapshot);
        Assert.True(meter.Observe(1, 1000, 1020, PerSecond));            // another camera starts at 1 again
        Assert.Equal(new CameraAge(1, 20, 20, 20), meter.Snapshot);
    }

    [Fact]
    public void Reading_while_the_render_thread_writes_never_sees_a_torn_picture_of_the_numbers()
    {
        var meter = new CameraAgeMeter();
        var stop = false;
        var failure = "";
        var writer = new Thread(() =>
        {
            for (var i = 1; i <= 200_000 && Volatile.Read(ref stop) == false; i++) meter.Observe(i, 1000, 1000 + 1 + i % 97, PerSecond);
            Volatile.Write(ref stop, true);
        }) { IsBackground = true };
        var reader = new Thread(() =>
        {
            long frames = 0;
            while (!Volatile.Read(ref stop))
            {
                var age = meter.Snapshot;
                if (age.Frames < frames) failure = "the count went back";
                if (age.Frames > 0 && !(age.LastMilliseconds >= 1 && age.LastMilliseconds <= age.WorstMilliseconds && age.AverageMilliseconds >= 1 && age.AverageMilliseconds <= age.WorstMilliseconds)) failure = $"torn: {age}";
                frames = age.Frames;
                Thread.Yield();
            }
        }) { IsBackground = true };
        writer.Start(); reader.Start();
        Assert.True(writer.Join(TimeSpan.FromSeconds(30)), "the writer did not finish");
        Assert.True(reader.Join(TimeSpan.FromSeconds(10)), "the reader did not finish");
        Assert.Equal("", failure);
        Assert.Equal(200_000, meter.Snapshot.Frames);
    }
}
