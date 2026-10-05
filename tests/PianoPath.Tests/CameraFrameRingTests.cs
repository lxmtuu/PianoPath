using Xunit;

namespace PianoPath.Tests;

/// <summary>The three buffers between the thread that reads the camera and the thread that draws.</summary>
public class CameraFrameRingTests
{
    private static void Put(CameraFrameRing ring, byte value, int width = 4, int height = 2, long ticks = 0)
    {
        var buffer = ring.BeginWrite(width * height * 4, out var slot);
        Array.Fill(buffer, value, 0, width * height * 4);
        ring.Publish(slot, width, height, ticks);
    }

    [Fact]
    public void An_empty_ring_has_nothing_to_take()
    {
        var ring = new CameraFrameRing();
        Assert.False(ring.TryTake(out _));
        Assert.Equal(0, ring.Published);
    }

    [Fact]
    public void A_published_frame_is_taken_once_with_its_size_and_its_clock_reading()
    {
        var ring = new CameraFrameRing();
        Put(ring, 7, 4, 2, ticks: 12345);
        Assert.True(ring.TryTake(out var frame));
        Assert.Equal((4, 2, 1L, 12345L), (frame.Width, frame.Height, frame.Sequence, frame.CaptureTicks));
        Assert.True(frame.Pixels.Length >= 32 && frame.Pixels.Take(32).All(b => b == 7));
        Assert.False(ring.TryTake(out _));
    }

    [Fact]
    public void A_slow_taker_gets_only_the_newest_frame_and_the_sequence_shows_the_skipped_ones()
    {
        var ring = new CameraFrameRing();
        for (byte value = 1; value <= 5; value++) Put(ring, value);
        Assert.True(ring.TryTake(out var frame));
        Assert.Equal(5, frame.Sequence);
        Assert.Equal(5, frame.Pixels[0]);
        Assert.False(ring.TryTake(out _));
    }

    [Fact]
    public void The_writer_never_gets_the_buffer_that_is_held_or_the_one_that_is_newest()
    {
        var ring = new CameraFrameRing();
        Put(ring, 1);
        Assert.True(ring.TryTake(out var held));             // held = newest = slot 0
        for (var i = 0; i < 20; i++)
        {
            var buffer = ring.BeginWrite(32, out var slot);
            Assert.NotSame(held.Pixels, buffer);
            buffer[0] = 99;
            ring.Publish(slot, 4, 2, 0);
            Assert.Equal(1, held.Pixels[0]);                  // the held picture is untouched however many frames come
        }
    }

    [Fact]
    public void Publishing_a_buffer_that_was_not_the_one_handed_out_does_nothing()
    {
        var ring = new CameraFrameRing();
        ring.BeginWrite(32, out var slot);
        ring.Publish((slot + 1) % 3, 4, 2, 0);
        Assert.Equal(0, ring.Published);
        Assert.False(ring.TryTake(out _));
    }

    [Fact]
    public void A_bigger_frame_gets_a_bigger_buffer_and_a_smaller_one_keeps_the_big_buffer()
    {
        var ring = new CameraFrameRing();
        Assert.True(ring.BeginWrite(100, out _).Length >= 100);
        Assert.True(ring.BeginWrite(5000, out var slot).Length >= 5000);
        ring.Publish(slot, 50, 25, 0);
        Assert.True(ring.TryTake(out var frame));
        Assert.Equal((50, 25), (frame.Width, frame.Height));
        Assert.True(frame.Pixels.Length >= 5000);
    }

    [Fact]
    public void Clear_forgets_what_was_published_but_new_frames_still_arrive()
    {
        var ring = new CameraFrameRing();
        Put(ring, 1);
        ring.Clear();
        Assert.False(ring.TryTake(out _));
        Put(ring, 2);
        Assert.True(ring.TryTake(out var frame));
        Assert.Equal(2, frame.Sequence);
        Assert.Equal(2, frame.Pixels[0]);
    }

    [Fact]
    public async Task A_writer_and_a_taker_on_two_threads_never_see_a_torn_frame_and_the_sequence_only_rises()
    {
        const int frames = 3000, size = 8192;
        var ring = new CameraFrameRing();
        var done = false;
        var writer = Task.Run(() =>
        {
            for (var n = 1; n <= frames; n++)
            {
                var buffer = ring.BeginWrite(size, out var slot);
                var value = (byte)(1 + n % 250);
                for (var i = 0; i < size; i++) buffer[i] = value;   // a taker that read this buffer half-way would see two values
                ring.Publish(slot, 64, 32, n);
            }
            Volatile.Write(ref done, true);
        });

        long last = 0;
        var taken = 0;
        string? failure = null;
        var finished = false;
        while (!finished)
        {
            finished = Volatile.Read(ref done);   // read before the take, so a frame published before "done" is still seen
            if (!ring.TryTake(out var frame)) { Thread.Yield(); continue; }
            taken++;
            if (frame.Sequence <= last) failure = $"sequence {frame.Sequence} after {last}";
            last = frame.Sequence;
            var expected = (byte)(1 + frame.Sequence % 250);
            for (var i = 0; i < size && failure is null; i++)
                if (frame.Pixels[i] != expected) failure = $"frame {frame.Sequence} torn at byte {i}: {frame.Pixels[i]} instead of {expected}";
            if (frame.CaptureTicks != frame.Sequence) failure ??= $"frame {frame.Sequence} carries the clock reading of frame {frame.CaptureTicks}";
        }
        await writer;
        Assert.Null(failure);
        Assert.True(taken > 0);
        Assert.Equal(frames, ring.Published);
    }

    [Fact]
    public void A_tap_sees_each_frame_before_it_is_the_newest_with_the_sequence_it_will_have()
    {
        var ring = new CameraFrameRing();
        var seen = new List<(long Sequence, long Ticks, int Width, byte First, bool Takeable)>();
        ring.FrameTap = frame => seen.Add((frame.Sequence, frame.CaptureTicks, frame.Width, frame.Pixels[0], ring.TryTake(out _)));
        Put(ring, 9, 4, 2, ticks: 100);
        Put(ring, 8, 4, 2, ticks: 200);
        Assert.Equal([(1L, 100L, 4, (byte)9, false), (2L, 200L, 4, (byte)8, true)], seen);     // the first frame was not there to take yet; the second saw the first
        Assert.True(ring.TryTake(out var frame));
        Assert.Equal(2, frame.Sequence);
        ring.FrameTap = null;
        Put(ring, 7);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void A_tap_that_throws_does_not_stop_the_frame_and_a_stale_slot_is_not_shown_to_it()
    {
        var ring = new CameraFrameRing();
        ring.FrameTap = _ => throw new InvalidOperationException("broken");
        Put(ring, 5);
        Assert.True(ring.TryTake(out var frame));
        Assert.Equal(1, frame.Sequence);
        var calls = 0;
        ring.FrameTap = _ => calls++;
        ring.Publish(2, 4, 2, 0);                                                              // never begun: nothing to publish and nothing to show
        Assert.Equal(0, calls);
        Assert.Equal(1, ring.Published);
    }
}
