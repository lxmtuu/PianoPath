using Xunit;

namespace PianoPath.Tests;

public sealed class LatencyMeasurementTests
{
    private const double Ticks = MotionEnergyTests.Ticks;
    private static readonly PixelRegion Region = new(40, 30, 120, 80);
    private static readonly CameraAge Age = new(300, 20, 22, 45);

    private static long At(double seconds) => (long)(seconds * Ticks);

    /// <summary>Plays a recorded evening into the measurement in time order and polls at every frame; returns when it finishes.</summary>
    private static (LatencyReport? Report, double FinishedAt) Run(LatencyMeasurement measurement, IReadOnlyList<double> notes, IReadOnlyList<(long Ticks, byte[] Frame)> frames, CameraAge age, double start = 0)
    {
        measurement.Start(Region, At(start));
        var next = 0; long sequence = 0;
        foreach (var (ticks, frame) in frames)
        {
            var time = ticks / Ticks;
            while (next < notes.Count && notes[next] <= time) measurement.NoteOn(At(notes[next++]));
            measurement.AddFrame(new CameraFrameView(frame, MotionEnergyTests.W, MotionEnergyTests.H, ++sequence, ticks));
            if (measurement.Poll(ticks, age) is { } report) return (report, time);
        }
        return (null, double.NaN);
    }

    [Theory]
    [InlineData(.06)]
    [InlineData(.13)]
    [InlineData(.22)]
    public void It_finishes_when_the_twentieth_press_has_had_time_to_show_and_suggests_the_delay_with_the_apps_own_share(double delay)
    {
        var (notes, frames) = MotionEnergyTests.Playing(delay, 12);
        var measurement = new LatencyMeasurement(Ticks);
        var (report, at) = Run(measurement, notes, frames, Age);
        Assert.NotNull(report);
        Assert.Equal(MeasurementState.Finished, measurement.State);
        Assert.Same(report, measurement.Report);
        Assert.Equal(20, measurement.Presses);
        Assert.InRange(at, notes[19] + LatencyMeasurement.SettleSeconds - .001, notes[19] + LatencyMeasurement.SettleSeconds + .05);   // not before the picture of the last press could be here
        Assert.True(report.Estimate.IsMeasured, report.Estimate.ToString());
        Assert.Equal(22, report.AppAgeMilliseconds);
        Assert.Equal((int)Math.Round(report.Estimate.DelaySeconds * 1000 + 22), report.SuggestedMilliseconds);
        Assert.InRange(report.SuggestedMilliseconds, delay * 1000 - 40 + 22, delay * 1000 + 60 + 22);
        Assert.False(report.Clamped);
        Assert.Null(measurement.Poll(At(at + 1), Age));                          // nothing more once it is over
        Assert.Null(measurement.Finish(Age));
    }

    [Fact]
    public void A_delay_the_setting_cannot_hold_is_cut_to_its_largest_and_says_so()
    {
        var (notes, frames) = MotionEnergyTests.Playing(.35, 4);
        var (report, _) = Run(new LatencyMeasurement(Ticks), notes, frames, Age);
        Assert.NotNull(report);
        Assert.True(report.Estimate.IsMeasured, report.Estimate.ToString());
        Assert.Equal((int)ArOptions.DelayMax, report.SuggestedMilliseconds);
        Assert.True(report.Clamped);
    }

    [Fact]
    public void An_age_that_was_not_counted_or_is_not_a_number_adds_nothing()
    {
        var (notes, frames) = MotionEnergyTests.Playing(.1, 3);
        foreach (var age in new[] { default, new CameraAge(0, 50, 50, 50), new CameraAge(10, 1, double.NaN, 1), new CameraAge(10, 1, -30, 1), new CameraAge(10, 1, double.PositiveInfinity, 1) })
        {
            var (report, _) = Run(new LatencyMeasurement(Ticks), notes, frames, age);
            Assert.NotNull(report);
            Assert.True(report.Estimate.IsMeasured);
            Assert.Equal(0, report.AppAgeMilliseconds);
            Assert.Equal((int)Math.Round(report.Estimate.DelaySeconds * 1000), report.SuggestedMilliseconds);
        }
    }

    [Fact]
    public void Nothing_is_kept_before_it_starts_or_after_it_is_cancelled()
    {
        var measurement = new LatencyMeasurement(Ticks);
        var frame = MotionEnergyTests.Grey(30);
        Assert.Equal(MeasurementState.Idle, measurement.State);
        measurement.NoteOn(5);
        measurement.AddFrame(new CameraFrameView(frame, MotionEnergyTests.W, MotionEnergyTests.H, 1, 5));
        Assert.Equal(0, measurement.Presses);
        Assert.Null(measurement.Poll(At(1000), Age));
        Assert.Null(measurement.Finish(Age));
        Assert.Null(measurement.Report);

        measurement.Start(Region, 0);
        measurement.NoteOn(At(1));
        Assert.Equal(1, measurement.Presses);
        measurement.Cancel();
        Assert.Equal(MeasurementState.Idle, measurement.State);
        Assert.Equal(0, measurement.Presses);
        measurement.NoteOn(At(2));
        Assert.Equal(0, measurement.Presses);
        Assert.Null(measurement.Poll(At(1000), Age));
    }

    [Fact]
    public void Stopping_early_gives_the_reason_and_starting_again_forgets_the_last_run()
    {
        var (notes, frames) = MotionEnergyTests.Playing(.1, 6);
        var measurement = new LatencyMeasurement(Ticks);
        measurement.Start(Region, 0);
        long sequence = 0;
        var next = 0;
        foreach (var (ticks, frame) in frames.TakeWhile(f => f.Ticks / Ticks < notes[4] + 1))
        {
            while (next < 5 && notes[next] <= ticks / Ticks) measurement.NoteOn(At(notes[next++]));
            measurement.AddFrame(new CameraFrameView(frame, MotionEnergyTests.W, MotionEnergyTests.H, ++sequence, ticks));
            Assert.Null(measurement.Poll(ticks, Age));                           // five presses are not the twenty it wants
        }
        var report = measurement.Finish(Age);
        Assert.NotNull(report);
        Assert.Equal(MeasurementState.Finished, measurement.State);
        Assert.Equal(5, measurement.Presses);
        Assert.Equal(LatencyOutcome.NotEnoughNotes, report.Estimate.Outcome);
        Assert.Equal(0, report.SuggestedMilliseconds);
        Assert.False(report.Estimate.IsMeasured);
        measurement.NoteOn(At(99));                                              // after the end: not counted
        Assert.Equal(5, measurement.Presses);

        measurement.Start(Region, At(200));
        Assert.Equal(MeasurementState.Collecting, measurement.State);
        Assert.Equal(0, measurement.Presses);
        Assert.Null(measurement.Report);
    }

    [Fact]
    public void It_gives_up_after_the_time_allowed_with_nothing_to_suggest()
    {
        var measurement = new LatencyMeasurement(Ticks);
        measurement.Start(Region, At(10));
        Assert.Null(measurement.Poll(At(10 + LatencyMeasurement.TimeoutSeconds - .1), Age));
        var report = measurement.Poll(At(10 + LatencyMeasurement.TimeoutSeconds), Age);
        Assert.NotNull(report);
        Assert.Equal(LatencyOutcome.NotEnoughPicture, report.Estimate.Outcome);
        Assert.Equal(0, report.SuggestedMilliseconds);
    }

    [Fact]
    public void The_keys_of_a_chord_are_one_press()
    {
        var measurement = new LatencyMeasurement(Ticks);
        measurement.Start(Region, 0);
        foreach (var chord in new[] { 1.0, 2.0, 3.0 })
            foreach (var offset in new[] { 0.0, .003, .009 }) measurement.NoteOn(At(chord + offset));
        Assert.Equal(3, measurement.Presses);
    }

    [Fact]
    public void A_picture_that_never_moves_is_said_to_be_so_even_though_the_presses_were_there()
    {
        var notes = LatencyProbeTests.Notes(new Random(2), 20);
        var frame = MotionEnergyTests.Grey(80);
        var frames = Enumerable.Range(0, (int)((notes[^1] + 1.5) * 60)).Select(i => (At(i / 60.0), frame)).ToList();
        var (report, _) = Run(new LatencyMeasurement(Ticks), notes, frames, Age);
        Assert.NotNull(report);
        Assert.Equal(LatencyOutcome.NoMotion, report.Estimate.Outcome);
        Assert.Equal(0, report.SuggestedMilliseconds);
    }

    [Fact]
    public void Frames_notes_and_polls_from_three_threads_come_to_one_answer_without_an_exception()
    {
        var (notes, frames) = MotionEnergyTests.Playing(.1, 14);
        var measurement = new LatencyMeasurement(Ticks);
        measurement.Start(Region, 0);
        var stop = false;
        var camera = new Thread(() =>
        {
            long sequence = 0;
            foreach (var (ticks, frame) in frames) measurement.AddFrame(new CameraFrameView(frame, MotionEnergyTests.W, MotionEnergyTests.H, ++sequence, ticks));
            Volatile.Write(ref stop, true);
        }) { IsBackground = true };
        var midi = new Thread(() => { foreach (var note in notes) measurement.NoteOn(At(note)); }) { IsBackground = true };
        var ui = new Thread(() =>
        {
            while (!Volatile.Read(ref stop)) { _ = measurement.Presses; _ = measurement.State; Thread.Sleep(1); }
        }) { IsBackground = true };
        camera.Start(); midi.Start(); ui.Start();
        Assert.True(camera.Join(TimeSpan.FromSeconds(60)));
        Assert.True(midi.Join(TimeSpan.FromSeconds(10)));
        Assert.True(ui.Join(TimeSpan.FromSeconds(10)));
        var report = measurement.Finish(Age);
        Assert.NotNull(report);
        Assert.Equal(20, measurement.Presses);
        Assert.True(report.Estimate.IsMeasured, report.Estimate.ToString());
    }
}
