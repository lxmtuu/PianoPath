using Xunit;

namespace PianoPath.Tests;

/// <summary>Which calibration is in force for the camera that is open — and which view of it — and what happens when either changes.</summary>
public sealed class ArCalibrationSessionTests : IDisposable
{
    private const string Cam = "\\\\?\\usb#vid_046d&pid_0825#cam1";
    private const string Default = ArCalibrationProfiles.LegacyId;
    private static readonly KeyboardLayout Full = KeyboardLayout.Standard88;
    private static readonly KeyboardLayout Sixty = KeyboardLayout.ForKeyCount(61);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ar-session-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private ArCalibrationSession NewSession() => new(new ArCalibrationStore(_dir));

    private static (double X, double Y)[] Corners(int keys) => SyntheticKeyboardFrame.Corners[keys].ToArray();

    private static (double X, double Y)[] Moved(int keys, double dx, double dy) => Corners(keys).Select(p => (p.X + dx, p.Y + dy)).ToArray();

    [Fact]
    public void Nothing_is_open_at_first()
    {
        var session = NewSession();
        Assert.Equal(ArCalibrationState.NoSource, session.State);
        Assert.Null(session.Current);
        Assert.Equal(ArCalibrationProfiles.Legacy, session.Snapshot.Profile);
        Assert.Equal(ArSaveResult.NoSource, session.Save(Corners(88), out _));
        Assert.False(session.Reset());
    }

    [Fact]
    public void An_unknown_camera_needs_calibrating_and_asking_again_changes_nothing()
    {
        var session = NewSession();
        Assert.True(session.Use(Cam, 1280, 720, Full, Default));
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        var version = session.Snapshot.Version;
        Assert.False(session.Use(Cam, 1280, 720, Full, Default));
        Assert.False(session.Use(Cam.ToUpperInvariant(), 1280, 720, Full, Default));      // a link is not case sensitive
        Assert.False(session.Use(Cam, 1280, 720, Full, "DEFAULT"));                       // and neither is a view id
        Assert.Equal(version, session.Snapshot.Version);
        Assert.True(session.Use(Cam, 1280, 720, Full, ArCalibrationProfiles.Tilt45.Id));  // but another view is another question
        Assert.Equal(ArCalibrationProfiles.Tilt45, session.Snapshot.Profile);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
    }

    [Theory]
    [InlineData("", 1280, 720)]
    [InlineData(Cam, 0, 720)]
    [InlineData(Cam, 1280, 0)]
    [InlineData(Cam, -1, -1)]
    public void A_source_without_a_name_or_a_size_is_no_source(string source, int width, int height)
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.True(session.Use(source, width, height, Full, Default));
        Assert.Equal(ArCalibrationState.NoSource, session.State);
        Assert.False(session.Use(source, width, height, Full, Default));
    }

    [Fact]
    public void A_saved_calibration_is_in_force_and_the_next_run_finds_it()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArSaveResult.Saved, session.Save(Corners(88), out var problem));
        Assert.Equal(CalibrationProblem.None, problem);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.Equal(Corners(88), session.Current!.Corners);
        Assert.Same(Full, session.Current.Layout);

        var next = NewSession();                                                  // a new run of the app
        Assert.Equal(ArCalibrationState.NoSource, next.State);
        next.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArCalibrationState.Ready, next.State);
        Assert.Equal(Corners(88), next.Current!.Corners);
    }

    [Fact]
    public void A_new_picture_size_asks_for_a_new_calibration_and_the_old_one_comes_back_with_the_old_size()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        session.Use(Cam, 1920, 1080, Full, Default);
        Assert.Equal(ArCalibrationState.Outdated, session.State);
        Assert.Null(session.Current);
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.Equal(1280, session.Current!.ImageWidth);
    }

    [Fact]
    public void Another_keyboard_size_is_another_calibration()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Sixty, Default);
        Assert.Equal(ArCalibrationState.Outdated, session.State);
        Assert.Equal(ArSaveResult.Saved, session.Save(Corners(61), out _));
        Assert.Equal(61, session.Current!.Layout.KeyCount);
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(Corners(88), session.Current!.Corners);
        session.Use(Cam, 1280, 720, Sixty, Default);
        Assert.Equal(Corners(61), session.Current!.Corners);
    }

    [Fact]
    public void Another_camera_has_never_been_calibrated_which_is_not_the_same_as_out_of_date()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        session.Use("\\\\?\\usb#other", 1280, 720, Full, Default);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
    }

    [Fact]
    public void A_video_file_is_calibrated_by_its_path_like_a_camera()
    {
        var session = NewSession();
        session.Use(@"C:\clips\practice.mp4", 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        Assert.Equal(ArCalibrationState.Ready, NewSession().Also(s => s.Use(@"C:\clips\practice.mp4", 1280, 720, Full, Default)).State);
        Assert.Equal(ArCalibrationState.NotCalibrated, NewSession().Also(s => s.Use(@"C:\clips\other.mp4", 1280, 720, Full, Default)).State);
    }

    // ---- views -------------------------------------------------------------------------------------------

    [Fact]
    public void Turning_the_camera_to_another_view_never_uses_the_corners_marked_at_the_first_one()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        Assert.Equal(ArCalibrationState.Ready, session.State);

        // The camera has moved: the view changes, and the key area marked at 45° says nothing about the overhead one.
        session.Use(Cam, 1280, 720, Full, ArCalibrationProfiles.Overhead90.Id);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        Assert.Null(session.Current);
        Assert.Equal(ArCalibrationProfiles.Overhead90, session.Snapshot.Profile);

        // Marking the overhead view leaves the first view's corners exactly where they were.
        Assert.Equal(ArSaveResult.Saved, session.Save(Moved(88, 30, -40), out _));
        Assert.Equal(Moved(88, 30, -40), session.Current!.Corners);
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(Corners(88), session.Current!.Corners);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.Equal(Corners(88), NewSession().Also(s => s.Use(Cam, 1280, 720, Full, Default)).Current!.Corners);
        Assert.Equal(Moved(88, 30, -40), NewSession().Also(s => s.Use(Cam, 1280, 720, Full, ArCalibrationProfiles.Overhead90.Id)).Current!.Corners);
    }

    [Fact]
    public void A_view_marked_at_another_picture_size_is_out_of_date_for_that_view_only()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        session.Use(Cam, 1920, 1080, Full, Default);
        Assert.Equal(ArCalibrationState.Outdated, session.State);                          // marked, at another size
        session.Use(Cam, 1920, 1080, Full, ArCalibrationProfiles.Tilt45.Id);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);                     // never marked at any size
        Assert.Null(session.Current);
    }

    [Fact]
    public void Forgetting_a_view_forgets_that_view_alone()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Full, ArCalibrationProfiles.Tilt45.Id);
        session.Save(Moved(88, 12, 8), out _);
        Assert.True(session.Reset());
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);                     // the 45° view is gone
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArCalibrationState.Ready, session.State);                             // the other view was not touched
        Assert.Equal(Corners(88), session.Current!.Corners);
        Assert.True(session.Reset());
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
    }

    [Fact]
    public void Corners_that_are_not_a_key_area_are_refused_and_change_nothing()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        var before = session.Snapshot;
        var corners = Corners(88);
        var reversed = new[] { corners[0], corners[1], corners[3], corners[2] };       // a bow tie: no way round makes it a keyboard
        Assert.Equal(ArSaveResult.BadCorners, session.Save(reversed, out var problem));
        Assert.NotEqual(CalibrationProblem.None, problem);
        Assert.Equal(ArSaveResult.BadCorners, session.Save([(0, 0), (1, 1)], out _));
        Assert.Same(before, session.Snapshot);                                    // not even a new version
        Assert.Equal(Corners(88), NewSession().Also(s => s.Use(Cam, 1280, 720, Full, Default)).Current!.Corners);
    }

    [Fact]
    public void Saving_again_replaces_the_calibration()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        session.Save(Corners(88), out _);
        var moved = Corners(88).Select(p => (p.X + 6, p.Y + 4)).ToArray();
        Assert.Equal(ArSaveResult.Saved, session.Save(moved, out _));
        Assert.Equal(moved, session.Current!.Corners);
        Assert.Equal(moved, NewSession().Also(s => s.Use(Cam, 1280, 720, Full, Default)).Current!.Corners);
    }

    [Fact]
    public void Reset_forgets_this_calibration_only()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default); session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Sixty, Default); session.Save(Corners(61), out _);
        Assert.True(session.Reset());
        Assert.Equal(ArCalibrationState.Outdated, session.State);                  // the 88-key one is still there
        Assert.Null(session.Current);
        Assert.False(session.Reset());                                             // nothing left to forget
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.True(session.Reset());
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        Assert.Equal(ArCalibrationState.NotCalibrated, NewSession().Also(s => s.Use(Cam, 1280, 720, Full, Default)).State);
    }

    [Fact]
    public void Closing_the_camera_leaves_nothing_to_draw_with_and_nothing_to_save_to()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default); session.Save(Corners(88), out _);
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
        session.Use(Cam, 1280, 720, Full, Default); seen.Add(session.Snapshot.Version);
        session.Save(Corners(88), out _); seen.Add(session.Snapshot.Version);
        session.Use(Cam, 1280, 720, Full, ArCalibrationProfiles.Tilt45.Id); seen.Add(session.Snapshot.Version);
        session.Use(Cam, 1920, 1080, Full, ArCalibrationProfiles.Tilt45.Id); seen.Add(session.Snapshot.Version);
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
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArCalibrationState.NotCalibrated, session.State);
        Assert.Equal(ArSaveResult.Saved, session.Save(Corners(88), out _));        // and it can be written over
        Assert.Equal(ArCalibrationState.Ready, NewSession().Also(s => s.Use(Cam, 1280, 720, Full, Default)).State);
    }

    [Fact]
    public void When_the_file_cannot_be_written_the_calibration_still_works_for_now()
    {
        File.WriteAllText(_dir, "a file where the folder should be");              // CreateDirectory cannot succeed
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default);
        Assert.Equal(ArSaveResult.NotWritten, session.Save(Corners(88), out _));
        Assert.Equal(ArCalibrationState.Ready, session.State);
        Assert.NotNull(session.Current);
    }

    [Fact]
    public async Task A_thread_drawing_while_the_window_changes_cameras_never_sees_a_mixed_state()
    {
        var session = NewSession();
        session.Use(Cam, 1280, 720, Full, Default); session.Save(Corners(88), out _);
        session.Use(Cam, 1280, 720, Sixty, Default); session.Save(Corners(61), out _);
        session.Use(Cam, 1280, 720, Full, ArCalibrationProfiles.Overhead90.Id); session.Save(Moved(88, 9, -9), out _);
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
                    if (!ArCalibrationProfiles.IsKnown(s.Profile.Id)) lock (bad) bad.Add($"an unknown view {s.Profile.Id}");
                }
                else if (s.Calibration is not null) lock (bad) bad.Add("a calibration that is not ready");
            }
        });
        for (var i = 0; i < 3000; i++)
        {
            var view = i % 3 == 0 ? Default : i % 3 == 1 ? ArCalibrationProfiles.Tilt45.Id : ArCalibrationProfiles.Overhead90.Id;
            session.Use(Cam, 1280, 720, i % 2 == 0 ? Full : Sixty, view);
            if (i % 7 == 0) session.Use(Cam, 1920, 1080, Full, view);
            if (i % 11 == 0) session.Clear();
            if (i % 13 == 0) session.Use(Cam, 1280, 720, Full, view);                  // same things again: must not publish
        }
        stop.Cancel();
        await reader;
        Assert.Empty(bad);
    }
}

internal static class SessionTestExtensions
{
    /// <summary>Runs an action on the value and gives the value back, so a check can read as one line.</summary>
    internal static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
