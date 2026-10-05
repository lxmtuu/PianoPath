using Xunit;

namespace PianoPath.Tests;

/// <summary>Which calibration is in force for the camera that is open, and what happens when it changes.</summary>
public sealed class ArCalibrationSessionTests : IDisposable
{
    private const string Cam = "\\\\?\\usb#vid_046d&pid_0825#cam1";
    private static readonly KeyboardLayout Full = KeyboardLayout.Standard88;
    private static readonly KeyboardLayout Sixty = KeyboardLayout.ForKeyCount(61);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ar-session-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ArCalibrationSession NewSession() => new(new ArCalibrationStore(_dir));

    private static (double X, double Y)[] Corners(int keys) => SyntheticKeyboardFrame.Corners[keys].ToArray();

    [Fact]
    public void Nothing_is_open_at_first()
    {
        var session = NewSession();
        Assert.Equal(ArCalibrationState.NoSource, session.State);
        Assert.Null(session.Current);
        Assert.Equal(ArSaveResult.NoSource, session.Save(Corners(88), out _));
        Assert.False(session.Reset());
    }

    [Fact]
    public void An_unknown_camera_needs_calibrating_and_asking_again_changes_nothing()
    {
        var session = NewSession();
        Assert.True(session.Use(Cam, 1280, 720, Full));
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        var version = session.Snapshot.Version;
        Assert.False(session.Use(Cam, 1280, 720, Full));
        Assert.False(session.Use(Cam.ToUpperInvariant(), 1280, 720, Full));      // a link is not case sensitive
        Assert.Equal(version, session.Snapshot.Version);
    }

    [Theory]
    [InlineData("", 1280, 720)]
    [InlineData(Cam, 0, 720)]
    [InlineData(Cam, 1280, 0)]
    [InlineData(Cam, -1, -1)]
    public void A_source_without_a_name_or_a_size_is_no_source(string source, int width, int height)
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        Assert.True(session.Use(source, width, height, Full));
        Assert.Equal(ArCalibrationState.NoSource, session.State);
        Assert.False(session.Use(source, width, height, Full));
    }

    [Fact]
    public void A_saved_calibration_is_in_force_and_the_next_run_finds_it()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        Assert.Equal(ArSaveResult.Saved, session.Save(Corners(88), out var problem));
        Assert.Equal(CalibrationProblem.None, problem);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.Equal(Corners(88), session.Current!.Corners);
        Assert.Same(Full, session.Current.Layout);

        var next = NewSession();                                                  // a new run of the app
        Assert.Equal(ArCalibrationState.NoSource, next.State);
        next.Use(Cam, 1280, 720, Full);
        Assert.Equal(ArCalibrationState.Ready, next.State);
        Assert.Equal(Corners(88), next.Current!.Corners);
    }

    [Fact]
    public void A_new_picture_size_asks_for_a_new_calibration_and_the_old_one_comes_back_with_the_old_size()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        session.Save(Corners(88), out _);
        session.Use(Cam, 1920, 1080, Full);
        Assert.Equal(ArCalibrationState.Outdated, session.State);
        Assert.Null(session.Current);
        session.Use(Cam, 1280, 720, Full);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.Equal(1280, session.Current!.ImageWidth);
    }

    [Fact]
    public void Another_keyboard_size_is_another_calibration()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Sixty);
        Assert.Equal(ArCalibrationState.Outdated, session.State);
        Assert.Equal(ArSaveResult.Saved, session.Save(Corners(61), out _));
        Assert.Equal(61, session.Current!.Layout.KeyCount);
        session.Use(Cam, 1280, 720, Full);
        Assert.Equal(Corners(88), session.Current!.Corners);
        session.Use(Cam, 1280, 720, Sixty);
        Assert.Equal(Corners(61), session.Current!.Corners);
    }

    [Fact]
    public void Another_camera_has_never_been_calibrated_which_is_not_the_same_as_out_of_date()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        session.Save(Corners(88), out _);
        session.Use("\\\\?\\usb#other", 1280, 720, Full);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
    }

    [Fact]
    public void A_video_file_is_calibrated_by_its_path_like_a_camera()
    {
        var session = NewSession();
        session.Use(@"C:\clips\practice.mp4", 1280, 720, Full);
        session.Save(Corners(88), out _);
        Assert.Equal(ArCalibrationState.Ready, NewSession().Also(s => s.Use(@"C:\clips\practice.mp4", 1280, 720, Full)).State);
        Assert.Equal(ArCalibrationState.NotCalibrated, NewSession().Also(s => s.Use(@"C:\clips\other.mp4", 1280, 720, Full)).State);
    }

    [Fact]
    public void Corners_that_are_not_a_key_area_are_refused_and_change_nothing()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        session.Save(Corners(88), out _);
        var before = session.Snapshot;
        var corners = Corners(88);
        var reversed = new[] { corners[0], corners[1], corners[3], corners[2] };       // a bow tie: no way round makes it a keyboard
        Assert.Equal(ArSaveResult.BadCorners, session.Save(reversed, out var problem));
        Assert.NotEqual(CalibrationProblem.None, problem);
        Assert.Equal(ArSaveResult.BadCorners, session.Save([(0, 0), (1, 1)], out _));
        Assert.Same(before, session.Snapshot);                                    // not even a new version
        Assert.Equal(Corners(88), NewSession().Also(s => s.Use(Cam, 1280, 720, Full)).Current!.Corners);
    }

    [Fact]
    public void Saving_again_replaces_the_calibration()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        session.Save(Corners(88), out _);
        var moved = Corners(88).Select(p => (p.X + 6, p.Y + 4)).ToArray();
        Assert.Equal(ArSaveResult.Saved, session.Save(moved, out _));
        Assert.Equal(moved, session.Current!.Corners);
        Assert.Equal(moved, NewSession().Also(s => s.Use(Cam, 1280, 720, Full)).Current!.Corners);
    }

    [Fact]
    public void Reset_forgets_this_calibration_only()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full); session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Sixty); session.Save(Corners(61), out _);
        Assert.True(session.Reset());
        Assert.Equal(ArCalibrationState.Outdated, session.State);                  // the 88-key one is still there
        Assert.Null(session.Current);
        Assert.False(session.Reset());                                             // nothing left to forget
        session.Use(Cam, 1280, 720, Full);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.True(session.Reset());
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        Assert.Equal(ArCalibrationState.NotCalibrated, NewSession().Also(s => s.Use(Cam, 1280, 720, Full)).State);
    }

    [Fact]
    public void Closing_the_camera_leaves_nothing_to_draw_with_and_nothing_to_save_to()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full); session.Save(Corners(88), out _);
        Assert.True(session.Clear());
        Assert.False(session.Clear());
        Assert.Equal(ArCalibrationState.NoSource, session.State);
        Assert.Null(session.Current);
        Assert.Equal(ArSaveResult.NoSource, session.Save(Corners(88), out _));
    }

    [Fact]
    public void Every_change_counts_a_new_version()
    {
        var session = NewSession();
        var seen = new List<int> { session.Snapshot.Version };
        session.Use(Cam, 1280, 720, Full); seen.Add(session.Snapshot.Version);
        session.Save(Corners(88), out _); seen.Add(session.Snapshot.Version);
        session.Use(Cam, 1920, 1080, Full); seen.Add(session.Snapshot.Version);
        session.Reset(); seen.Add(session.Snapshot.Version);
        session.Clear(); seen.Add(session.Snapshot.Version);
        Assert.Equal(seen.Distinct().Count(), seen.Count);
        Assert.Equal(seen.Order(), seen);
    }

    [Fact]
    public void A_damaged_file_means_not_calibrated_not_a_crash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ArCalibrationStore.FileName), "{ this is not json");
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        Assert.Equal(ArSaveResult.Saved, session.Save(Corners(88), out _));        // and it can be written over
        Assert.Equal(ArCalibrationState.Ready, NewSession().Also(s => s.Use(Cam, 1280, 720, Full)).State);
    }

    [Fact]
    public void When_the_file_cannot_be_written_the_calibration_still_works_for_now()
    {
        File.WriteAllText(_dir, "a file where the folder should be");              // CreateDirectory cannot succeed
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full);
        Assert.Equal(ArSaveResult.NotWritten, session.Save(Corners(88), out _));
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public void A_thread_drawing_while_the_window_changes_cameras_never_sees_a_mixed_state()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full); session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Sixty); session.Save(Corners(61), out _);
        using var stop = new CancellationTokenSource();
        var bad = new List<string>();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var s = session.Snapshot;
                if (s.State == ArCalibrationState.Ready)
                {
                    if (s.Calibration is null) { lock (bad) bad.Add("ready without a calibration"); continue; }
                    if (s.Calibration.Layout.KeyCount != s.KeyCount || s.Calibration.ImageWidth != s.ImageWidth) lock (bad) bad.Add($"{s.KeyCount} keys with a {s.Calibration.Layout.KeyCount}-key calibration");
                }
                else if (s.Calibration is not null) lock (bad) bad.Add("a calibration that is not ready");
            }
        });
        for (var i = 0; i < 3000; i++)
        {
            session.Use(Cam, 1280, 720, i % 2 == 0 ? Full : Sixty);
            if (i % 7 == 0) session.Use(Cam, 1920, 1080, Full);
            if (i % 11 == 0) session.Clear();
        }
        stop.Cancel();
        reader.Wait();
        Assert.Empty(bad);
    }
}

internal static class SessionTestExtensions
{
    /// <summary>Runs an action on the value and gives the value back, so a check can read as one line.</summary>
    internal static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
