using Xunit;

namespace PianoPath.Tests;

public sealed class MotionEnergyTests
{
    internal const int W = 160, H = 90;

    internal static byte[] Grey(byte value)
    {
        var frame = new byte[W * H * 4];
        for (var i = 0; i < frame.Length; i += 4) { frame[i] = frame[i + 1] = frame[i + 2] = value; frame[i + 3] = 255; }
        return frame;
    }

    internal static void Paint(byte[] frame, int left, int top, int right, int bottom, byte value)
    {
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++) { var i = (y * W + x) * 4; frame[i] = frame[i + 1] = frame[i + 2] = value; }
    }

    private static double Energy(byte[] before, byte[] after, PixelRegion region)
    {
        var count = MotionEnergy.SampleCount(region);
        byte[] a = new byte[count], b = new byte[count];
        Assert.True(MotionEnergy.Sample(before, W, H, region, a));
        Assert.True(MotionEnergy.Sample(after, W, H, region, b));
        return MotionEnergy.Between(a, b, count);
    }

    [Fact]
    public void A_still_picture_has_no_energy_and_black_to_white_has_all_of_it()
    {
        var all = new PixelRegion(0, 0, W, H);
        Assert.Equal(0, Energy(Grey(90), Grey(90), all));
        Assert.Equal(1, Energy(Grey(0), Grey(255), all), 9);
        Assert.Equal(40 / 255.0, Energy(Grey(100), Grey(140), all), 9);
        Assert.Equal(40 / 255.0, Energy(Grey(140), Grey(100), all), 9);          // the same either way
    }

    [Fact]
    public void Only_the_region_is_looked_at()
    {
        var before = Grey(50);
        var after = Grey(50);
        Paint(after, 100, 50, 150, 80, 250);                                     // something moved down in the right-hand corner
        Assert.Equal(0, Energy(before, after, new PixelRegion(0, 0, 90, 45)));
        Assert.True(Energy(before, after, new PixelRegion(90, 45, W, H)) > .1);
    }

    [Fact]
    public void Brightness_follows_the_eye_green_most_blue_least()
    {
        var region = new PixelRegion(0, 0, W, H);
        byte[] Colour(byte b, byte g, byte r)
        {
            var frame = new byte[W * H * 4];
            for (var i = 0; i < frame.Length; i += 4) { frame[i] = b; frame[i + 1] = g; frame[i + 2] = r; frame[i + 3] = 255; }
            return frame;
        }
        var black = Colour(0, 0, 0);
        var blue = Energy(black, Colour(255, 0, 0), region);
        var red = Energy(black, Colour(0, 0, 255), region);
        var green = Energy(black, Colour(0, 255, 0), region);
        Assert.True(blue < red && red < green, $"{blue} {red} {green}");
        Assert.Equal(1, blue + red + green, 1);
    }

    [Fact]
    public void A_bad_picture_or_region_is_refused_without_writing_anything()
    {
        var into = new byte[W * H];
        Assert.False(MotionEnergy.Sample(new byte[100], W, H, new PixelRegion(0, 0, W, H), into));          // the picture is too short for its size
        Assert.False(MotionEnergy.Sample(Grey(1), W, H, new PixelRegion(50, 50, 50, 80), into));            // nothing in it
        Assert.False(MotionEnergy.Sample(Grey(1), W, H, new PixelRegion(W + 5, 0, W + 9, 10), into));       // outside
        Assert.False(MotionEnergy.Sample(Grey(1), W, H, new PixelRegion(0, 0, W, H), new byte[3]));         // nowhere to put it
        Assert.False(MotionEnergy.Sample(Grey(1), 0, H, new PixelRegion(0, 0, W, H), into));
        Assert.All(into, value => Assert.Equal(0, value));
        Assert.Equal(0, MotionEnergy.Between([1, 2], [1, 2], 5));
        Assert.Equal(0, MotionEnergy.Between([], [], 0));
    }

    [Fact]
    public void The_sample_count_is_what_sample_writes()
    {
        foreach (var region in new[] { new PixelRegion(0, 0, W, H), new PixelRegion(7, 5, 100, 61), new PixelRegion(0, 0, 1, 1), new PixelRegion(3, 3, 6, 6), new PixelRegion(0, 0, 4, 7) })
        {
            var count = MotionEnergy.SampleCount(region);
            var into = new byte[count + 10];
            Array.Fill(into, (byte)7);
            Assert.True(MotionEnergy.Sample(Grey(200), W, H, region, into));
            for (var i = 0; i < count; i++) Assert.Equal(200, into[i]);              // all written (grey 200 has luma 200: the weights add to 256)
            for (var i = count; i < into.Length; i++) Assert.Equal(7, into[i]);      // and no more
        }
    }

    [Fact]
    public void The_keys_region_is_the_box_around_the_corners_with_room_above_for_fingers()
    {
        (double X, double Y)[] corners = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
        var region = MotionEnergy.KeysRegion(corners, 1280, 720);
        Assert.Equal(52, region.Left);
        Assert.Equal(1236, region.Right);
        Assert.Equal(231, region.Top);                                           // 268 less a fifth of the 181 high
        Assert.Equal(468, region.Bottom);                                        // 449 and a tenth more
        // fitted to the picture
        var edge = MotionEnergy.KeysRegion([(-50, -50), (900, -40), (950, 700), (-60, 710)], 800, 600);
        Assert.Equal(new PixelRegion(0, 0, 800, 600), edge);
        // not usable
        Assert.True(MotionEnergy.KeysRegion([(1, 1)], 100, 100).IsEmpty);
        Assert.True(MotionEnergy.KeysRegion(corners, 0, 0).IsEmpty);
        Assert.True(MotionEnergy.KeysRegion([(double.NaN, 1), (2, 2), (3, 3), (4, 4)], 100, 100).IsEmpty);
    }

    // ---- the recorder: frames and notes in, a delay out ----------------------------------------------------

    internal const double Ticks = 1_000_000;

    /// <summary>A camera at 60 fps watching keys: the picture flashes bright for 20 ms, <paramref name="delay"/> seconds after each note.</summary>
    internal static (List<double> Notes, List<(long Ticks, byte[] Frame)> Frames) Playing(double delay, int seed, bool flashOutsideRegion = false)
    {
        var random = new Random(seed);
        var notes = LatencyProbeTests.Notes(random, 20);
        var frames = new List<(long, byte[])>();
        for (var i = 0; i * (1 / 60.0) < notes[^1] + 1; i++)
        {
            var time = i / 60.0;
            var frame = Grey(60);
            for (var n = 0; n < frame.Length; n += 4) { var jitter = (byte)random.Next(0, 4); frame[n] += jitter; frame[n + 1] += jitter; frame[n + 2] += jitter; }
            if (notes.Any(note => time >= note + delay && time < note + delay + .02)) Paint(frame, flashOutsideRegion ? 0 : 60, flashOutsideRegion ? 0 : 40, flashOutsideRegion ? 40 : 100, flashOutsideRegion ? 30 : 70, 200);
            frames.Add(((long)(time * Ticks), frame));
        }
        return (notes, frames);
    }

    [Theory]
    [InlineData(.05)]
    [InlineData(.1)]
    [InlineData(.2)]
    public void Frames_and_notes_in_the_recorder_give_the_delay_of_the_flash(double delay)
    {
        var (notes, frames) = Playing(delay, 7);
        var recorder = new LatencyRecorder(Ticks);
        recorder.SetRegion(new PixelRegion(40, 30, 120, 80));
        long sequence = 0;
        foreach (var (ticks, frame) in frames) recorder.AddFrame(new CameraFrameView(frame, W, H, ++sequence, ticks));
        foreach (var note in notes) recorder.NoteOn((long)(note * Ticks));
        Assert.Equal(frames.Count - 1, recorder.Frames);                           // the first frame has nothing before it to differ from
        Assert.Equal(20, recorder.Notes);
        var estimate = recorder.Estimate();
        Assert.True(estimate.IsMeasured, estimate.ToString());
        // the flash is a frame or two long and starts at the delay (up to a frame later, when the camera next looks)
        Assert.InRange(estimate.DelaySeconds, delay - .04, delay + .06);
    }

    [Fact]
    public void Movement_outside_the_region_is_not_the_keys()
    {
        var (notes, frames) = Playing(.1, 9, flashOutsideRegion: true);
        var recorder = new LatencyRecorder(Ticks);
        recorder.SetRegion(new PixelRegion(80, 40, 150, 85));
        long sequence = 0;
        foreach (var (ticks, frame) in frames) recorder.AddFrame(new CameraFrameView(frame, W, H, ++sequence, ticks));
        foreach (var note in notes) recorder.NoteOn((long)(note * Ticks));
        Assert.False(recorder.Estimate().IsMeasured);                              // the flash is in the other corner
    }

    [Fact]
    public void A_frame_seen_twice_a_new_size_or_no_region_is_not_counted_and_reset_starts_over()
    {
        var recorder = new LatencyRecorder(Ticks);
        var one = Grey(10); var two = Grey(80);
        Assert.False(recorder.AddFrame(new CameraFrameView(one, W, H, 1, 1000)));            // no region yet
        recorder.SetRegion(new PixelRegion(0, 0, W, H));
        Assert.False(recorder.AddFrame(new CameraFrameView(one, W, H, 2, 2000)));            // the first one with a region: nothing before it
        Assert.True(recorder.AddFrame(new CameraFrameView(two, W, H, 3, 3000)));
        Assert.False(recorder.AddFrame(new CameraFrameView(one, W, H, 3, 4000)));            // the same frame again
        Assert.Equal(1, recorder.Frames);
        var small = new byte[40 * 20 * 4];
        Assert.False(recorder.AddFrame(new CameraFrameView(small, 40, 20, 4, 5000)));        // another size: starts again
        Assert.True(recorder.AddFrame(new CameraFrameView(small, 40, 20, 5, 6000)));
        Assert.False(recorder.AddFrame(new CameraFrameView(new byte[10], W, H, 6, 7000)));   // a short buffer
        Assert.False(recorder.AddFrame(new CameraFrameView(two, W, H, 7, 8000)));      // after the bad one there is nothing to compare with
        recorder.NoteOn(1234);
        recorder.Reset();
        Assert.Equal(0, recorder.Frames);
        Assert.Equal(0, recorder.Notes);
        Assert.Equal(LatencyOutcome.NotEnoughPicture, recorder.Estimate().Outcome);
    }

    [Fact]
    public void Frames_from_one_thread_and_notes_from_another_do_not_disturb_each_other()
    {
        var recorder = new LatencyRecorder(Ticks);
        recorder.SetRegion(new PixelRegion(0, 0, W, H));
        var frames = new[] { Grey(10), Grey(200) };
        var done = false;
        var camera = new Thread(() =>
        {
            for (var i = 1; i <= 3000; i++) recorder.AddFrame(new CameraFrameView(frames[i % 2], W, H, i, i * 16_000L));
            Volatile.Write(ref done, true);
        }) { IsBackground = true };
        var midi = new Thread(() =>
        {
            var i = 0;
            do { recorder.NoteOn(i++ * 100_000L); Thread.Yield(); } while (!Volatile.Read(ref done));
        }) { IsBackground = true };
        camera.Start(); midi.Start();
        Assert.True(camera.Join(TimeSpan.FromSeconds(60)));
        Assert.True(midi.Join(TimeSpan.FromSeconds(10)));
        Assert.Equal(2999, recorder.Frames);
        Assert.InRange(recorder.Notes, 1, LatencyRecorder.MaxNotes);
        _ = recorder.Estimate();                                                             // whatever it says, it does not throw
    }
}
