using Xunit;

namespace PianoPath.Tests;

/// <summary>The per-camera delays kept on disk (AR‑3.5): round trips, one camera apart from another, the slider's range, and surviving a damaged file.</summary>
public sealed class ArDelayStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "keyflow-ardelay-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string Webcam = @"\\?\usb#vid_046d&pid_0825";
    private const string ObsVirtual = "obs-virtual-camera";

    [Fact]
    public void A_delay_saved_by_one_run_is_found_by_the_next_exactly()
    {
        Assert.True(new ArDelayStore(_dir).Set(Webcam, 120));
        var again = new ArDelayStore(_dir);
        Assert.Equal(120d, again.Get(Webcam));
        Assert.Equal(1, again.Count);
    }

    [Fact]
    public void Each_camera_keeps_its_own_delay_and_the_link_is_compared_without_regard_to_case()
    {
        var store = new ArDelayStore(_dir);
        Assert.True(store.Set(Webcam, 90));
        Assert.True(store.Set(ObsVirtual, 210));
        Assert.Equal(2, store.Count);
        Assert.Equal(90d, store.Get(Webcam.ToUpperInvariant()));
        Assert.Equal(210d, store.Get(ObsVirtual));
        Assert.Null(store.Get("some-other-camera"));
    }

    [Fact]
    public void An_empty_link_keeps_nothing_and_finds_nothing()
    {
        var store = new ArDelayStore(_dir);
        Assert.False(store.Set("", 120));
        Assert.False(store.Set("   ", 120));
        Assert.False(store.Set(null, 120));
        Assert.Equal(0, store.Count);
        Assert.Null(store.Get(""));
        Assert.Null(store.Get(null));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void A_saved_delay_is_held_to_the_sliders_range_and_a_number_that_is_not_a_number_becomes_the_default()
    {
        var store = new ArDelayStore(_dir);
        Assert.True(store.Set("over", 950));
        Assert.True(store.Set("under", -40));
        Assert.True(store.Set("nan", double.NaN));
        Assert.True(store.Set("inf", double.PositiveInfinity));
        Assert.Equal(ArOptions.DelayMax, store.Get("over"));
        Assert.Equal(ArOptions.DelayMin, store.Get("under"));
        Assert.Equal(ArOptions.DelayDefault, store.Get("nan"));
        Assert.Equal(ArOptions.DelayDefault, store.Get("inf"));
    }

    [Fact]
    public void Setting_again_replaces_and_does_not_pile_up()
    {
        var store = new ArDelayStore(_dir);
        store.Set(Webcam, 80);
        store.Set(Webcam, 130);
        Assert.Equal(1, store.Count);
        Assert.Equal(130d, new ArDelayStore(_dir).Get(Webcam));
    }

    [Fact]
    public void Remove_forgets_one_cameras_delay_and_says_whether_there_was_one()
    {
        var store = new ArDelayStore(_dir);
        store.Set(Webcam, 80);
        store.Set(ObsVirtual, 140);
        Assert.True(store.Remove(Webcam));
        Assert.False(store.Remove(Webcam));
        var again = new ArDelayStore(_dir);
        Assert.Null(again.Get(Webcam));
        Assert.Equal(140d, again.Get(ObsVirtual));
    }

    [Fact]
    public void A_file_that_is_not_json_or_not_the_right_shape_is_ignored_and_then_replaced()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, ArDelayStore.FileName);
        foreach (var junk in new[] { "not json at all", "", "[1,2,3]", "{\"entries\": 5}", "{\"entries\": [null, 7, \"x\"]}", "\0\0\0" })
        {
            File.WriteAllText(path, junk);
            var store = new ArDelayStore(_dir);
            Assert.Equal(0, store.Count);
            Assert.Null(store.Get(Webcam));
            Assert.True(store.Set(Webcam, 75));
            Assert.Equal(75d, new ArDelayStore(_dir).Get(Webcam));
        }
    }

    [Fact]
    public void Entries_that_cannot_be_trusted_are_dropped_and_the_good_ones_beside_them_kept()
    {
        Directory.CreateDirectory(_dir);
        var entries = new[]
        {
            "{\"CameraLink\":\"webcam\",\"DelayMs\":95}",
            "{\"CameraLink\":\"\",\"DelayMs\":95}",
            "{\"DelayMs\":95}",
            "{\"CameraLink\":null,\"DelayMs\":95}",
        };
        File.WriteAllText(Path.Combine(_dir, ArDelayStore.FileName), "{\"Version\":1,\"Entries\":[" + string.Join(",", entries) + "]}");
        var store = new ArDelayStore(_dir);
        Assert.Equal(1, store.Count);
        Assert.Equal(95d, store.Get("webcam"));
    }

    [Fact]
    public void An_out_of_range_or_null_delay_in_the_file_is_pulled_into_range_not_trusted_as_is()
    {
        Directory.CreateDirectory(_dir);
        var entries = new[]
        {
            "{\"CameraLink\":\"wide\",\"DelayMs\":100000}",
            "{\"CameraLink\":\"negative\",\"DelayMs\":-5}",
            "{\"CameraLink\":\"missing\"}",
        };
        File.WriteAllText(Path.Combine(_dir, ArDelayStore.FileName), "{\"Version\":1,\"Entries\":[" + string.Join(",", entries) + "]}");
        var store = new ArDelayStore(_dir);
        Assert.Equal(ArOptions.DelayMax, store.Get("wide"));
        Assert.Equal(ArOptions.DelayMin, store.Get("negative"));
        Assert.Equal(0d, store.Get("missing"));                        // a missing field reads as zero, which is in range
    }

    [Fact]
    public void Only_the_newest_entries_are_kept_when_there_are_too_many()
    {
        var store = new ArDelayStore(_dir);
        var total = ArDelayStore.MaxEntries + 6;
        for (var i = 0; i < total; i++) Assert.True(store.Set("cam" + i, 60 + i));
        Assert.Equal(ArDelayStore.MaxEntries, store.Count);
        Assert.Null(store.Get("cam0"));
        Assert.Null(store.Get("cam5"));
        Assert.Equal(66d, store.Get("cam6"));
        Assert.Equal(60d + total - 1, store.Get("cam" + (total - 1)));
        Assert.Equal(ArDelayStore.MaxEntries, new ArDelayStore(_dir).Count);
    }

    [Fact]
    public void Saving_creates_a_missing_folder_and_leaves_no_temporary_file()
    {
        var nested = Path.Combine(_dir, "a", "b");
        var store = new ArDelayStore(nested);
        Assert.True(store.Set(Webcam, 110));
        Assert.True(File.Exists(store.FilePath));
        Assert.Empty(Directory.GetFiles(nested, "*.tmp"));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_is_reported_by_the_return_value_and_not_by_an_exception()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "file-in-the-way");
        File.WriteAllText(blocker, "x");
        var store = new ArDelayStore(blocker);                       // the "folder" is a file
        Assert.Equal(0, store.Count);
        Assert.False(store.Set(Webcam, 80));
        // The file refused it, but the value itself is kept in memory for the session — the calibration store
        // degrades the same way, so one unwritable folder does not make the overlay forget the camera mid-run.
        Assert.Equal(80d, store.Get(Webcam));
        Assert.False(store.Remove("never-saved"));
    }

    [Fact]
    public async Task Several_threads_saving_at_once_leave_a_whole_readable_file_with_every_entry()
    {
        var store = new ArDelayStore(_dir);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 10; i++) Assert.True(store.Set($"cam-{t}-{i}", 50 + t * 10 + i));
        })));
        Assert.Equal(40, store.Count);
        var reread = new ArDelayStore(_dir);
        for (var t = 0; t < 4; t++) for (var i = 0; i < 10; i++) Assert.Equal(50d + t * 10 + i, reread.Get($"cam-{t}-{i}"));
    }
}
