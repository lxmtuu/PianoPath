using Xunit;

namespace PianoPath.Tests;

/// <summary>The calibrations kept on disk: round trips, the keys that tell one apart, and surviving a damaged file.</summary>
public sealed class ArCalibrationStoreTests : IDisposable
{
    private const int W = 1280, H = 720;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ar-" + Guid.NewGuid().ToString("N"));

    private static (double X, double Y)[] Sample(int keys = 88) => SyntheticKeyboardFrame.Corners[keys].ToArray();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void A_calibration_saved_by_one_run_is_found_by_the_next_exactly()
    {
        var corners = new[] { (70.125, 292.5), (1214.0, 268.25), (1236.75, 421.0), (52.0, 449.875) };
        Assert.True(new ArCalibrationStore(_dir).Set("usb#cam1", W, H, 88, corners));
        var again = new ArCalibrationStore(_dir);
        Assert.Equal(corners, again.Get("usb#cam1", W, H, 88));
        Assert.Equal(1, again.Count);
    }

    [Fact]
    public void A_calibration_is_for_one_camera_one_picture_size_and_one_keyboard_size()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set("cam-a", W, H, 88, Sample());
        Assert.NotNull(store.Get("cam-a", W, H, 88));
        Assert.Null(store.Get("cam-b", W, H, 88));
        Assert.Null(store.Get("cam-a", 1920, 1080, 88));
        Assert.Null(store.Get("cam-a", W, H, 61));
        Assert.Null(store.Get("", W, H, 88));
    }

    [Fact]
    public void The_camera_link_is_compared_without_regard_to_case()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set(@"\\?\usb#vid_046d&pid_0825", W, H, 88, Sample());
        Assert.NotNull(store.Get(@"\\?\USB#VID_046D&PID_0825", W, H, 88));
    }

    [Fact]
    public void A_camera_with_no_link_and_a_video_file_have_their_own_entries()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.True(store.Set("", W, H, 88, Sample()));
        Assert.True(store.Set(@"C:\clips\take1.mp4", W, H, 88, Sample()));
        Assert.Equal(2, store.Count);
        Assert.NotNull(store.Get("", W, H, 88));
    }

    [Fact]
    public void Setting_again_replaces_and_does_not_pile_up()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set("cam", W, H, 88, Sample());
        var moved = Sample(); moved[0] = (80, 292);
        store.Set("cam", W, H, 88, moved);
        Assert.Equal(1, store.Count);
        Assert.Equal(moved, new ArCalibrationStore(_dir).Get("cam", W, H, 88));
    }

    [Fact]
    public void A_camera_calibrated_at_another_size_is_told_apart_from_one_never_calibrated()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.False(store.HasOtherSize("cam", W, H, 88));            // never calibrated
        store.Set("cam", W, H, 88, Sample());
        Assert.False(store.HasOtherSize("cam", W, H, 88));            // calibrated at exactly this
        Assert.True(store.HasOtherSize("cam", 1920, 1080, 88));       // calibrated, but at 720p
        Assert.True(store.HasOtherSize("CAM", W, H, 61));             // calibrated, but for 88 keys
        Assert.False(store.HasOtherSize("other", 1920, 1080, 88));
    }

    [Fact]
    public void Corners_that_would_not_pass_the_check_are_not_saved()
    {
        var store = new ArCalibrationStore(_dir);
        Assert.False(store.Set("cam", W, H, 88, new (double, double)[] { (100, 100), (500, 100), (900, 100), (100, 300) }));
        Assert.False(store.Set("cam", W, H, 88, Sample().Take(3).ToArray()));
        Assert.Equal(0, store.Count);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Reset_forgets_one_calibration_for_good_and_says_whether_there_was_one()
    {
        var store = new ArCalibrationStore(_dir);
        store.Set("cam", W, H, 88, Sample());
        store.Set("cam", W, H, 61, Sample(61));
        Assert.True(store.Remove("cam", W, H, 88));
        Assert.False(store.Remove("cam", W, H, 88));
        var again = new ArCalibrationStore(_dir);
        Assert.Null(again.Get("cam", W, H, 88));
        Assert.NotNull(again.Get("cam", W, H, 61));
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
            Assert.Null(store.Get("cam", W, H, 88));
            Assert.True(store.Set("cam", W, H, 88, Sample()));
            Assert.NotNull(new ArCalibrationStore(_dir).Get("cam", W, H, 88));
        }
    }

    [Fact]
    public void Entries_that_cannot_be_trusted_are_dropped_and_the_good_ones_beside_them_kept()
    {
        Directory.CreateDirectory(_dir);
        const string good = "{\"CameraLink\":\"cam\",\"ImageWidth\":1280,\"ImageHeight\":720,\"KeyCount\":88,\"Corners\":[70,292,1214,268,1236,421,52,449]}";
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
        File.WriteAllText(Path.Combine(_dir, ArCalibrationStore.FileName), "{\"Version\":1,\"Entries\":[" + string.Join(",", entries) + "]}");
        var store = new ArCalibrationStore(_dir);
        Assert.Equal(1, store.Count);
        Assert.Equal(Sample(), store.Get("cam", W, H, 88));
    }

    [Fact]
    public void Only_the_newest_entries_are_kept_when_there_are_too_many()
    {
        var store = new ArCalibrationStore(_dir);
        var total = ArCalibrationStore.MaxEntries + 6;
        for (var i = 0; i < total; i++) Assert.True(store.Set("cam" + i, W, H, 88, Sample()));
        Assert.Equal(ArCalibrationStore.MaxEntries, store.Count);
        Assert.Null(store.Get("cam0", W, H, 88));
        Assert.Null(store.Get("cam5", W, H, 88));
        Assert.NotNull(store.Get("cam6", W, H, 88));
        Assert.NotNull(store.Get("cam" + (total - 1), W, H, 88));
        Assert.Equal(ArCalibrationStore.MaxEntries, new ArCalibrationStore(_dir).Count);
    }

    [Fact]
    public void Saving_leaves_no_temporary_file_and_creates_the_folder_when_it_is_not_there()
    {
        var nested = Path.Combine(_dir, "a", "b");
        var store = new ArCalibrationStore(nested);
        Assert.True(store.Set("cam", W, H, 88, Sample()));
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
        Assert.False(store.Set("cam", W, H, 88, Sample()));
        Assert.False(store.Remove("nothing", W, H, 88));
    }

    [Fact]
    public async Task Several_threads_saving_at_once_leave_a_whole_readable_file_with_every_entry()
    {
        var store = new ArCalibrationStore(_dir);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 10; i++) Assert.True(store.Set($"cam-{t}-{i}", W, H, 88, Sample()));
        })));
        Assert.Equal(40, store.Count);
        var reread = new ArCalibrationStore(_dir);
        Assert.Equal(40, reread.Count);
        for (var t = 0; t < 4; t++) for (var i = 0; i < 10; i++) Assert.NotNull(reread.Get($"cam-{t}-{i}", W, H, 88));
    }

    [Fact]
    public void What_is_stored_builds_the_same_calibration_that_was_made_from_the_clicks()
    {
        var store = new ArCalibrationStore(_dir);
        var live = ArCalibration.Create(KeyboardLayout.Standard88, Sample(), W, H, out _)!;
        store.Set("cam", W, H, 88, live.Corners);
        var loaded = ArCalibration.Create(KeyboardLayout.Standard88, new ArCalibrationStore(_dir).Get("cam", W, H, 88)!, W, H, out var problem)!;
        Assert.Equal(CalibrationProblem.None, problem);
        for (var pitch = 21; pitch <= 108; pitch++) Assert.Equal(live.KeyTopCentre(pitch), loaded.KeyTopCentre(pitch));
    }
}
