using Xunit;

namespace PianoPath.Tests;

/// <summary>The still the calibration window shows, how it is taken from the camera's ring, and when a result may be saved.</summary>
public class CalibrationPictureTests
{
    /// <summary>A picture whose every pixel says where it is: B = x, G = y, R = 7, A = 255.</summary>
    private static byte[] Gradient(int width, int height)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                bytes[i] = (byte)x; bytes[i + 1] = (byte)y; bytes[i + 2] = 7; bytes[i + 3] = 255;
            }
        return bytes;
    }

    private static void Put(CameraFrameRing ring, byte[] pixels, int width, int height)
    {
        var buffer = ring.BeginWrite(pixels.Length, out var slot);
        Buffer.BlockCopy(pixels, 0, buffer, 0, pixels.Length);
        ring.Publish(slot, width, height, 1);
    }

    // ---- the ring's peek --------------------------------------------------------------------------------

    [Fact]
    public void Peeking_an_empty_ring_gives_nothing()
    {
        Assert.False(new CameraFrameRing().TryPeek(out _));
    }

    [Fact]
    public void A_peek_copies_the_newest_frame_exactly_and_leaves_it_to_be_taken()
    {
        var ring = new CameraFrameRing();
        Put(ring, Gradient(6, 4), 6, 4);
        Put(ring, [.. Gradient(5, 3)], 5, 3);
        Assert.True(ring.TryPeek(out var copy));
        Assert.Equal((5, 3), (copy.Width, copy.Height));
        Assert.Equal(5 * 3 * 4, copy.Pixels.Length);
        Assert.Equal(Gradient(5, 3), copy.Pixels);
        Assert.True(ring.TryTake(out var taken));                      // the peek did not use the frame up
        Assert.Equal(copy.Sequence, taken.Sequence);
        Assert.True(ring.TryPeek(out var again));                      // and taking does not stop a peek either
        Assert.Equal(copy.Pixels, again.Pixels);
    }

    [Fact]
    public void A_peeked_copy_is_the_callers_own()
    {
        var ring = new CameraFrameRing();
        Put(ring, Gradient(4, 4), 4, 4);
        Assert.True(ring.TryPeek(out var first));
        Array.Fill(first.Pixels, (byte)0);
        Assert.True(ring.TryPeek(out var second));
        Assert.Equal(Gradient(4, 4), second.Pixels);
        Assert.True(ring.TryTake(out var taken));
        Assert.Equal(Gradient(4, 4), taken.Pixels.Take(64).ToArray());
    }

    [Fact]
    public void A_cleared_ring_has_nothing_to_peek()
    {
        var ring = new CameraFrameRing();
        Put(ring, Gradient(4, 4), 4, 4);
        ring.Clear();
        Assert.False(ring.TryPeek(out _));
    }

    [Fact]
    public void Peeking_while_the_camera_publishes_and_the_engine_takes_never_shows_a_torn_frame()
    {
        var ring = new CameraFrameRing();
        const int width = 64, height = 36, size = width * height * 4;
        var stop = false;
        // Own threads, not the pool's: other tests keep the pool busy and a task could start long after the loop below has ended.
        var writer = new Thread(() =>
        {
            byte value = 0;
            while (!Volatile.Read(ref stop))
            {
                var buffer = ring.BeginWrite(size, out var slot);
                value = (byte)(value % 250 + 1);
                for (var i = 0; i < size; i++) buffer[i] = value;           // slow on purpose: a torn frame would show
                ring.Publish(slot, width, height, 0);
                Thread.Yield();
            }
        }) { IsBackground = true };
        var taker = new Thread(() => { while (!Volatile.Read(ref stop)) { ring.TryTake(out _); Thread.Sleep(1); } }) { IsBackground = true };
        writer.Start(); taker.Start();
        var torn = 0; var peeked = 0;
        var end = DateTime.UtcNow.AddMilliseconds(800);
        while (DateTime.UtcNow < end)
        {
            Thread.Sleep(1);                                                  // the lock is not fair: a loop that never lets go would starve the others
            if (!ring.TryPeek(out var copy)) continue;
            peeked++;
            if (copy.Pixels.Length != size || copy.Pixels.Any(b => b != copy.Pixels[0])) torn++;
        }
        Volatile.Write(ref stop, true);
        Assert.True(writer.Join(5000) && taker.Join(5000));
        Assert.True(peeked > 0, "the writer never published, or every peek was starved");
        Assert.Equal(0, torn);
    }

    // ---- the picture ------------------------------------------------------------------------------------

    [Fact]
    public void A_picture_must_have_exactly_the_pixels_of_its_size()
    {
        Assert.Throws<ArgumentException>(() => new CalibrationPicture(new byte[10], 2, 2));
        Assert.Throws<ArgumentException>(() => new CalibrationPicture(new byte[16], 0, 4));
        Assert.Throws<ArgumentException>(() => new CalibrationPicture(new byte[32], 2, 2));
        var picture = new CalibrationPicture(new byte[16], 2, 2);
        Assert.Equal((2, 2), (picture.Width, picture.Height));
    }

    [Fact]
    public void Capturing_from_an_empty_ring_gives_no_picture()
    {
        Assert.Null(CalibrationPicture.Capture(new CameraFrameRing(), framesAreMirrored: false));
    }

    [Fact]
    public void Frames_as_the_camera_delivered_them_are_kept_as_they_are()
    {
        var ring = new CameraFrameRing();
        Put(ring, Gradient(7, 3), 7, 3);
        var picture = CalibrationPicture.Capture(ring, framesAreMirrored: false);
        Assert.NotNull(picture);
        Assert.Equal(Gradient(7, 3), picture!.Bgra);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(7, 3)]
    [InlineData(64, 36)]
    public void Frames_the_camera_thread_turned_round_are_turned_back(int width, int height)
    {
        var original = Gradient(width, height);
        var mirrored = (byte[])original.Clone();
        CalibrationPicture.FlipRows(mirrored, width, height);
        var ring = new CameraFrameRing();
        Put(ring, mirrored, width, height);
        var picture = CalibrationPicture.Capture(ring, framesAreMirrored: true);
        Assert.Equal(original, picture!.Bgra);
    }

    [Fact]
    public void Turning_a_picture_round_moves_each_pixel_to_the_other_end_of_its_row_and_nothing_else()
    {
        var pixels = Gradient(5, 2);
        CalibrationPicture.FlipRows(pixels, 5, 2);
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 5; x++)
            {
                var i = (y * 5 + x) * 4;
                Assert.Equal((byte)(4 - x), pixels[i]);                    // B was x, now the mirrored column
                Assert.Equal((byte)y, pixels[i + 1]);
                Assert.Equal((byte)7, pixels[i + 2]);
                Assert.Equal((byte)255, pixels[i + 3]);
            }
        CalibrationPicture.FlipRows(pixels, 5, 2);
        Assert.Equal(Gradient(5, 2), pixels);
    }

    // ---- when the window may open and when its result may be kept -----------------------------------------

    private static ArCalibrationSnapshot Snapshot(ArCalibrationState state, string source = "cam-a", int width = 1280, int height = 720, int keys = 88) =>
        new(state, null, state == ArCalibrationState.NoSource ? "" : source, state == ArCalibrationState.NoSource ? 0 : width, state == ArCalibrationState.NoSource ? 0 : height, state == ArCalibrationState.NoSource ? 0 : keys, 1);

    private static CalibrationPicture Picture(int width, int height) => new(new byte[width * height * 4], width, height);

    [Fact]
    public void The_window_opens_only_on_a_picture_of_the_size_the_overlay_reads()
    {
        Assert.Equal(CalibrationLaunch.NoCamera, CalibrationLaunchRules.Check(Snapshot(ArCalibrationState.NoSource), Picture(4, 4)));
        Assert.Equal(CalibrationLaunch.NoPicture, CalibrationLaunchRules.Check(Snapshot(ArCalibrationState.NotCalibrated), null));
        Assert.Equal(CalibrationLaunch.SizeMismatch, CalibrationLaunchRules.Check(Snapshot(ArCalibrationState.NotCalibrated), Picture(640, 480)));
        Assert.Equal(CalibrationLaunch.SizeMismatch, CalibrationLaunchRules.Check(Snapshot(ArCalibrationState.Ready, width: 1280, height: 721), Picture(1280, 720)));
        foreach (var state in new[] { ArCalibrationState.NotCalibrated, ArCalibrationState.Outdated, ArCalibrationState.Ready })
            Assert.Equal(CalibrationLaunch.Ready, CalibrationLaunchRules.Check(Snapshot(state), Picture(1280, 720)));
    }

    [Fact]
    public void A_result_is_kept_only_when_the_camera_picture_size_and_keyboard_are_still_the_same()
    {
        var before = Snapshot(ArCalibrationState.NotCalibrated);
        Assert.True(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.Ready)));                     // saving and resetting change the calibration only
        Assert.True(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.Outdated, source: "CAM-A")));   // links compare without case
        Assert.False(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.NoSource)));
        Assert.False(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.NotCalibrated, source: "cam-b")));
        Assert.False(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.NotCalibrated, width: 1920, height: 1080)));
        Assert.False(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.NotCalibrated, height: 719)));
        Assert.False(CalibrationLaunchRules.SameSource(before, Snapshot(ArCalibrationState.NotCalibrated, keys: 61)));
    }

    // ---- the words ----------------------------------------------------------------------------------------

    [Fact]
    public void Every_sentence_of_the_window_is_in_both_string_tables()
    {
        var sentences = CalibrationHints.All().ToList();
        Assert.True(sentences.Count >= 13);
        foreach (var sentence in sentences)
        {
            Assert.True(Loc.Known(sentence), $"not in the English table: {sentence}");
            Assert.True(StringsVietnamese.Table.TryGetValue(sentence, out var vietnamese) && vietnamese.Length > 0, $"not in the Vietnamese table: {sentence}");
        }
    }

    [Fact]
    public void Each_step_and_each_problem_has_its_own_sentence()
    {
        var steps = new List<string>();
        var wizard = new CalibrationWizard(1280, 720, KeyboardLayout.Standard88);
        wizard.SetViewport(1000, 600, false);
        (double X, double Y)[] corners = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
        for (var i = 0; i < 4; i++)
        {
            steps.Add(CalibrationHints.ForStep(wizard));
            var (x, y) = wizard.View.ToOutput(corners[i].X, corners[i].Y);
            Assert.True(wizard.PointerDown(x, y)); wizard.PointerUp();
        }
        steps.Add(CalibrationHints.ForStep(wizard));                                 // all four placed and good
        Assert.Equal(5, steps.Distinct().Count());
        Assert.Contains("1 of 4", steps[0]); Assert.Contains("4 of 4", steps[3]);

        var problems = Enum.GetValues<CalibrationProblem>().Where(p => p != CalibrationProblem.None).Select(CalibrationHints.ForProblem).ToList();
        Assert.Equal(problems.Count, problems.Distinct().Count());

        var flat = new CalibrationWizard(1280, 720, KeyboardLayout.Standard88);
        flat.SetViewport(1000, 600, false);
        foreach (var x in new[] { 100.0, 300, 500, 700 }) { flat.PointerDown(x, 300); flat.PointerUp(); }
        Assert.Equal(WizardStep.Fix, flat.Step);
        Assert.Equal(CalibrationHints.ForProblem(flat.Problem), CalibrationHints.ForStep(flat));
    }

    [Fact]
    public void Key_names_follow_the_usual_way_with_middle_C_as_C4()
    {
        Assert.Equal("C4", CalibrationHints.KeyName(60)); Assert.Equal("A0", CalibrationHints.KeyName(21));
        Assert.Equal("C8", CalibrationHints.KeyName(108)); Assert.Equal("C♯5", CalibrationHints.KeyName(73));
        Assert.Equal("C-1", CalibrationHints.KeyName(0)); Assert.Equal("B-2", CalibrationHints.KeyName(-1));
    }
}
