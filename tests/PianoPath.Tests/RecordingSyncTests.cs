using Xunit;

namespace PianoPath.Tests;

public sealed class RecordingSyncTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"keyflow-tests-sync-{Guid.NewGuid():N}.wav");

    public void Dispose()
    {
        try { File.Delete(_path); } catch { /* never created */ }
    }

    private sealed class CountingTrack : IAudioTrack
    {
        internal long Silence;
        internal int Calls;
        public void Append(short[] samples, int count) { }
        public void AppendSilence(long frames) { Silence += frames; Calls++; }
        public double Seconds => Silence / (double)RecordingSync.SampleRate;
        public void Dispose() { }
    }

    [Theory]
    [InlineData(.083, true, .083)]
    [InlineData(0.0, true, 0.0)]
    [InlineData(.083, false, 0.0)]                  // a take without the camera has nothing to wait for
    [InlineData(-.2, true, 0.0)]
    [InlineData(.9, true, .3)]                      // never more than the setting's largest
    [InlineData(double.NaN, true, 0.0)]
    [InlineData(double.PositiveInfinity, true, 0.0)]
    public void The_wait_is_the_cameras_delay_only_when_the_camera_is_in_the_take(double delay, bool camera, double expected) =>
        Assert.Equal(expected, RecordingSync.LeadInSeconds(delay, camera), 9);

    [Fact]
    public void The_wait_in_frames_is_the_nearest_whole_frame_and_never_off_by_more_than_half_of_one()
    {
        for (var ms = 0; ms <= 300; ms++)
        {
            var frames = RecordingSync.LeadInFrames(ms / 1000.0, true);
            Assert.InRange(Math.Abs(frames - ms / 1000.0 * RecordingSync.SampleRate), 0, .5000001);
        }
        Assert.Equal(3660, RecordingSync.LeadInFrames(.083, true));
        Assert.Equal(13230, RecordingSync.LeadInFrames(.3, true));
    }

    [Fact]
    public void Beginning_a_track_writes_the_wait_once_and_nothing_when_there_is_none()
    {
        var track = new CountingTrack();
        var seconds = RecordingSync.Begin(track, .083, true);
        Assert.Equal(3660, track.Silence);
        Assert.Equal(1, track.Calls);
        Assert.Equal(3660 / 44100.0, seconds, 9);

        var plain = new CountingTrack();
        Assert.Equal(0, RecordingSync.Begin(plain, .083, false));
        Assert.Equal(0, RecordingSync.Begin(plain, 0, true));
        Assert.Equal(0, RecordingSync.Begin(plain, double.NaN, true));
        Assert.Equal(0, plain.Calls);
    }

    /// <summary>The sample frame at which a written track first makes a sound, read back from the file.</summary>
    private int FirstSoundFrame()
    {
        var bytes = File.ReadAllBytes(_path);
        var data = bytes.Length - WavWriter.HeaderBytes;
        for (var i = 0; i + 1 < data; i += 2)
            if (BitConverter.ToInt16(bytes, WavWriter.HeaderBytes + i) != 0) return i / 4;
        return -1;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(.05)]
    [InlineData(.083)]
    [InlineData(.2)]
    [InlineData(.3)]
    public void A_click_played_at_the_start_of_the_recording_lands_in_the_file_a_camera_delay_later(double delay)
    {
        using (var wav = WavWriter.TryCreate(_path)!)
        {
            RecordingSync.Begin(wav, delay, true);
            var click = new short[] { 12000, 12000, -12000, -12000 };
            wav.Append(click, click.Length);
        }
        Assert.Equal(RecordingSync.LeadInFrames(delay, true), FirstSoundFrame());
    }

    [Theory]
    [InlineData(24)]
    [InlineData(30)]
    [InlineData(60)]
    public void The_sound_of_a_press_and_the_frame_that_shows_it_are_never_more_than_one_frame_apart(int fps)
    {
        var random = new Random(fps);
        for (var ms = 0; ms <= 300; ms += 3)
            for (var trial = 0; trial < 5; trial++)
            {
                var delay = ms / 1000.0;
                var press = random.NextDouble() * 20;                                   // when the key goes down, on the recording's clock
                var lead = RecordingSync.LeadInFrames(delay, true) / (double)RecordingSync.SampleRate;
                // the camera shows the press `delay` later; the first frame that can show it is the next one the recording makes
                var shown = Math.Ceiling((press + delay) * fps) / fps;
                var sound = press + lead;                                               // where the engine's sound of it sits in the file
                Assert.InRange(shown - sound, -1.0 / RecordingSync.SampleRate, 1.0 / fps + 1.0 / RecordingSync.SampleRate);
                // and without the wait the sound would come ahead of the picture by the delay itself
                Assert.Equal(-delay, RecordingSync.Skew(delay, 0), 9);
                Assert.True(Math.Abs(RecordingSync.Skew(delay, lead)) <= .5 / RecordingSync.SampleRate + 1e-12);
            }
    }

    [Fact]
    public void A_take_with_no_sound_device_is_kept_as_long_as_the_clock_plus_the_wait_with_no_drift()
    {
        const double lead = 3660 / 44100.0;
        using var wav = WavWriter.TryCreate(_path)!;
        RecordingSync.Begin(wav, .083, true);
        var elapsed = 0.0;
        var random = new Random(3);
        for (var tick = 0; tick < 2000; tick++)
        {
            elapsed += .004 + random.NextDouble() * .03;                                // the timer is not on the dot
            var due = RecordingSync.SilenceDue(elapsed, lead, wav.Frames / (double)RecordingSync.SampleRate);
            Assert.True(due >= 0);
            if (due > 0) wav.AppendSilence(due);
            var wanted = (long)((elapsed + lead) * RecordingSync.SampleRate);
            Assert.InRange(wav.Frames - wanted, 0, 1);                                  // never behind, never more than a sample ahead
        }
    }

    [Fact]
    public void Silence_due_is_never_negative_and_a_bad_clock_asks_for_nothing()
    {
        Assert.Equal(26460, RecordingSync.SilenceDue(1, .1, .5));
        Assert.Equal(0, RecordingSync.SilenceDue(1, .1, 5));
        Assert.Equal(0, RecordingSync.SilenceDue(double.NaN, .1, 0));
        Assert.Equal(0, RecordingSync.SilenceDue(1, .1, double.NaN));
        Assert.Equal(0, RecordingSync.SilenceDue(-5, 0, 0));
        Assert.Equal(4410, RecordingSync.SilenceDue(0, .1, 0));                          // the wait alone, before the clock has moved
        Assert.Equal(44100, RecordingSync.SilenceDue(1, -3, 0));                         // a negative wait is none
    }
}
