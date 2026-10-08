using Xunit;

namespace PianoPath.Tests;

/// <summary>The calibrations kept on disk: round trips, the keys that tell one apart — camera, view, picture size, keyboard size — and surviving a damaged file.</summary>
public sealed class ArCalibrationStoreTests : IDisposable
{
    private const int W = 1280, H = 720;
    private const string Default = ArCalibrationProfiles.LegacyId;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ar-" + Guid.NewGuid().ToString("N"));

    private static (double X, double Y)[] Sample(int keys = 88) => SyntheticKeyboardFrame.Corners[keys].ToArray();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void A_calibration_saved_by_one_run_is_found_by_the_next_exactly()
    {
        var corners = new[] { (70.125, 292.5), (1214.0, 268.25), (1236.75, 421.0), (52.0, 449.875) };
        Assert.True(new ArCalibrationStore(_dir).Set("usb#cam1", Default, W, H, 88, corners));
        var again = new ArCalibrationStore(_dir);
        Assert.Equal(corners, again.Get("usb#cam1", Default, W, H, 88));
        Assert.Equal(1, again.Count);
    }

    [Fact]
    public void A_calibration_is_for_one_camera_one_view_one_picture_size_and_one_keyboard_size()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set("cam-a", Default, W, H, 88, Sample());
        Assert.NotNull(store.Get("cam-a", Default, W, H, 88));
        Assert.Null(store.Get("cam-b", Default, W, H, 88));
        Assert.Null(store.Get("cam-a", ArCalibrationProfiles.Tilt45.Id, W, H, 88));
        Assert.Null(store.Get("cam-a", Default, 1920, 1080, 88));
        Assert.Null(store.Get("cam-a", Default, W, H, 61));
        Assert.Null(store.Get("", Default, W, H, 88));
    }

    [Fact]
    public void Two_views_of_one_camera_keep_their_own_key_areas()
    {
        var store = new ArCalibrationStore(_dir);
        var overhead = Sample();
        var tilted = overhead.Select(p => (p.X + 18, p.Y - 24)).ToArray();
        Assert.True(store.Set("cam", Default, W, H, 88, overhead));
        Assert.True(store.Set("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88, tilted));
        Assert.Equal(2, store.Count);
        Assert.Equal(overhead, store.Get("cam", Default, W, H, 88));
        Assert.Equal(tilted, store.Get("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88));
        Assert.Null(store.Get("cam", ArCalibrationProfiles.Tilt45.Id, W, H, 88));      // a third view was never marked
        Assert.True(store.Remove("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88));
        var reread = new ArCalibrationStore(_dir);
        Assert.Equal(1, reread.Count);
        Assert.NotNull(reread.Get("cam", Default, W, H, 88));                          // forgetting one view keeps the other
    }

    [Fact]
    public void The_view_an_id_names_is_read_without_regard_to_case_and_the_default_view_takes_anything_unknown()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.True(store.Set("cam", "TILT45", W, H, 88, Sample()));
        Assert.NotNull(store.Get("cam", "tilt45", W, H, 88));
        Assert.NotNull(store.Get("cam", "Tilt45", W, H, 88));
        Assert.Null(store.Get("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88));
        // An id the app does not know cannot name a key area of its own: it is read as, and written to, the default view.
        Assert.Null(store.Get("cam", "some-future-view", W, H, 88));                    // this camera was marked at 45°, nothing at the default view
        Assert.True(store.Set("cam2", "some-future-view", W, H, 88, Sample()));
        Assert.NotNull(store.Get("cam2", Default, W, H, 88));
        Assert.Null(store.Get("cam2", ArCalibrationProfiles.Tilt45.Id, W, H, 88));
        Assert.Contains("\"Profile\": \"default\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void The_camera_link_is_compared_without_regard_to_case()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set(@"\\?\usb#vid_046d&pid_0825", Default, W, H, 88, Sample());
        Assert.NotNull(store.Get(@"\\?\USB#VID_046D&PID_0825", Default, W, H, 88));
    }

    [Fact]
    public void A_camera_with_no_link_and_a_video_file_have_their_own_entries()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.True(store.Set("", Default, W, H, 88, Sample()));
        Assert.True(store.Set(@"C:\clips\take1.mp4", Default, W, H, 88, Sample()));
        Assert.Equal(2, store.Count);
        Assert.NotNull(store.Get("", Default, W, H, 88));
    }

    [Fact]
    public void Setting_again_replaces_and_does_not_pile_up()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set("cam", Default, W, H, 88, Sample());
        var moved = Sample(); moved[0] = (80, 292);
        store.Set("cam", Default, W, H, 88, moved);
        Assert.Equal(1, store.Count);
        Assert.Equal(moved, new ArCalibrationStore(_dir).Get("cam", Default, W, H, 88));
    }

    [Fact]
    public void A_camera_view_calibrated_at_another_size_is_told_apart_from_one_never_calibrated()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.False(store.HasOtherSize("cam", Default, W, H, 88));                    // never calibrated
        store.Set("cam", Default, W, H, 88, Sample());
        Assert.False(store.HasOtherSize("cam", Default, W, H, 88));                    // calibrated at exactly this
        Assert.True(store.HasOtherSize("cam", Default, 1920, 1080, 88));               // calibrated, but at 720p
        Assert.True(store.HasOtherSize("CAM", Default, W, H, 61));                     // calibrated, but for 88 keys
        Assert.False(store.HasOtherSize("other", Default, 1920, 1080, 88));
        // Another view of the same camera is a view nothing is known about, not an out-of-date one: it must ask for
        // keys to be marked rather than say the old marks were the wrong size.
        Assert.False(store.HasOtherSize("cam", ArCalibrationProfiles.Tilt45.Id, W, H, 88));
    }

    [Fact]
    public void Corners_that_would_not_pass_the_check_are_not_saved()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.False(store.Set("cam", Default, W, H, 88, new (double, double)[] { (100, 100), (500, 100), (900, 100), (100, 300) }));
        Assert.False(store.Set("cam", Default, W, H, 88, Sample().Take(3).ToArray()));
        Assert.Equal(0, store.Count);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Reset_forgets_one_calibration_for_good_and_says_whether_there_was_one()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set("cam", Default, W, H, 88, Sample());
        store.Set("cam", Default, W, H, 61, Sample(61));
        Assert.True(store.Remove("cam", Default, W, H, 88));
        Assert.False(store.Remove("cam", Default, W, H, 88));
        var again = new ArCalibrationStore(_dir);
        Assert.Null(again.Get("cam", Default, W, H, 88));
        Assert.NotNull(again.Get("cam", Default, W, H, 61));
    }

    [Fact]
    public void A_file_that_is_not_json_or_not_the_right_shape_is_ignored_and_then_replaced()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ArCalibrationStore.FileName);
        foreach (var junk in new[] { "not json at all", "", "[1,2,3]", "{\"entries\": 5}", "{\"entries\": [null, 7, \"x\"]}", "\0\0\0" })
        {
            File.WriteAllText(path, junk);
            var store = new ArCalibrationStore(_dir);
            Assert.Equal(0, store.Count);
            Assert.Null(store.Get("cam", Default, W, H, 88));
            Assert.True(store.Set("cam", Default, W, H, 88, Sample()));
            Assert.NotNull(new ArCalibrationStore(_dir).Get("cam", Default, W, H, 88));
        }
    }

    [Fact]
    public void A_file_written_before_views_existed_reads_as_the_default_view()
    {
        // Version 1 of the file: the same entries without a Profile field. Those corners belong to the view the app
        // starts on, so the person who had already marked their keys goes on playing without marking them again.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ArCalibrationStore.FileName),
            "{\"Version\":1,\"Entries\":[{\"CameraLink\":\"cam\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268,1236,421,52,449]}]}");
        var store = new ArCalibrationStore(_dir);
        Assert.Equal(1, store.Count);
        Assert.Equal(Sample(), store.Get("cam", Default, W, H, 88));
        // Saving a second view writes both, under their own ids, and the file says it is version 2 now.
        Assert.True(store.Set("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88, Sample()));
        var text = File.ReadAllText(store.FilePath);
        Assert.Contains("\"Version\": 2", text);
        Assert.Contains("\"Profile\": \"default\"", text);
        Assert.Contains("\"Profile\": \"overhead90\"", text);
    }

    [Fact]
    public void Entries_that_cannot_be_trusted_are_dropped_and_the_good_ones_beside_them_kept()
    {
        Directory.CreateDirectory(_dir);
        const string good = "{\"CameraLink\":\"cam\",\"Profile\":\"tilt45\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268,1236,421,52,449]}";
        var entries = new[]
        {
            good,
            "{\"CameraLink\":\"short\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268]}",
            "{\"CameraLink\":\"line\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[100,100,500,100,900,100,100,300]}",
            "{\"CameraLink\":\"nosize\",\"ImageWidth\":0,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268,1236,421,52,449]}",
            "{\"CameraLink\":\"nokeys\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":0,\"Corners\":[70,292,1214,268,1236,421,52,449]}",
            "{\"CameraLink\":\"far\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268,1236,421,52,90000]}",
            "{\"CameraLink\":\"nullcorners\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":null}",
        };
        File.WriteAllText(Path.Combine(_dir, ArCalibrationStore.FileName), "{\"Version\":2,\"Entries\":[" + string.Join(",", entries) + "]}");
        var store = new ArCalibrationStore(_dir);
        Assert.Equal(1, store.Count);
        Assert.Equal(Sample(), store.Get("cam", ArCalibrationProfiles.Tilt45.Id, W, H, 88));
    }

    [Fact]
    public void An_entry_for_a_view_this_app_does_not_know_is_dropped_rather_than_read_as_another_view()
    {
        Directory.CreateDirectory(_dir);
        var known = "{\"CameraLink\":\"cam\",\"Profile\":\"default\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268,1236,421,52,449]}";
        var unknown = "{\"CameraLink\":\"cam\",\"Profile\":\"from-a-newer-keyflow\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[10,20,30,40,50,60,70,80]}";
        File.WriteAllText(Path.Combine(_dir, ArCalibrationStore.FileName), "{\"Version\":2,\"Entries\":[" + known + "," + unknown + "]}");
        var store = new ArCalibrationStore(_dir);
        Assert.Equal(1, store.Count);
        Assert.Equal(Sample(), store.Get("cam", Default, W, H, 88));
        // The unknown id cannot be asked for, and its corners are not hiding behind the default view.
        Assert.False(ArCalibrationProfiles.IsKnown("from-a-newer-keyflow"));
    }

    [Fact]
    public void Only_the_newest_entries_are_kept_when_there_are_too_many()
    {
        var store = new ArCalibrationStore(_dir);
        var total = ArCalibrationStore.MaxEntries + 6;
        for (var i = 0; i < total; i++) Assert.True(store.Set("cam" + i, Default, W, H, 88, Sample()));
        Assert.Equal(ArCalibrationStore.MaxEntries, store.Count);
        Assert.Null(store.Get("cam0", Default, W, H, 88));
        Assert.Null(store.Get("cam5", Default, W, H, 88));
        Assert.NotNull(store.Get("cam6", Default, W, H, 88));
        Assert.NotNull(store.Get("cam" + (total - 1), Default, W, H, 88));
        Assert.Equal(ArCalibrationStore.MaxEntries, new ArCalibrationStore(_dir).Count);
    }

    [Fact]
    public void Saving_leaves_no_temporary_file_and_creates_the_folder_when_it_is_not_there()
    {
        var nested = Path.Combine(_dir, "a", "b");
        var store = new ArCalibrationStore(nested);
        Assert.True(store.Set("cam", Default, W, H, 88, Sample()));
        Assert.True(File.Exists(store.FilePath));
        Assert.Empty(Directory.GetFiles(nested, "*.tmp"));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_is_reported_by_the_return_value_and_not_by_an_exception()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "file-in-the-way");
        File.WriteAllText(blocker, "x");
        var store = new ArCalibrationStore(blocker);                  // the "folder" is a file
        Assert.Equal(0, store.Count);
        Assert.False(store.Set("cam", Default, W, H, 88, Sample()));
        Assert.False(store.Remove("nothing", Default, W, H, 88));
    }

    [Fact]
    public async Task Several_threads_saving_at_once_leave_a_whole_readable_file_with_every_entry()
    {
        var store = new ArCalibrationStore(_dir);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 10; i++) Assert.True(store.Set($"cam-{t}-{i}", t % 2 == 0 ? Default : ArCalibrationProfiles.Tilt45.Id, W, H, 88, Sample()));
        })));
        Assert.Equal(40, store.Count);
        var reread = new ArCalibrationStore(_dir);
        Assert.Equal(40, reread.Count);
        for (var t = 0; t < 4; t++) for (var i = 0; i < 10; i++) Assert.NotNull(reread.Get($"cam-{t}-{i}", t % 2 == 0 ? Default : ArCalibrationProfiles.Tilt45.Id, W, H, 88));
    }

    [Fact]
    public void What_is_stored_builds_the_same_calibration_that_was_made_from_the_clicks()
    {
        var store = new ArCalibrationStore(_dir);
        var live = ArCalibration.Create(KeyboardLayout.Standard88, Sample(), W, H, out _)!;
        store.Set("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88, live.Corners);
        var loaded = ArCalibration.Create(KeyboardLayout.Standard88, new ArCalibrationStore(_dir).Get("cam", ArCalibrationProfiles.Overhead90.Id, W, H, 88)!, W, H, out var problem)!;
        Assert.Equal(CalibrationProblem.None, problem);
        for (var pitch = 21; pitch <= 108; pitch++) Assert.Equal(live.KeyTopCentre(pitch), loaded.KeyTopCentre(pitch));
    }
}
