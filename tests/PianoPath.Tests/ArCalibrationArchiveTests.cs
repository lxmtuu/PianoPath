using System;
using System.Linq;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the portable calibration backup (Camera/ArCalibrationArchive.cs): a backup must carry every field faithfully,
/// refuse what is not a backup in plain words, skip rows that would never pass the live checks, and round-trip through
/// the real store on disk under the backup's own camera link.
/// </summary>
public class ArCalibrationArchiveTests
{
    private static readonly double[] Corners88 = [70, 292, 1214, 268, 1236, 421, 52, 449];
    private static readonly double[] Base88 = [68, 290, 1212, 266, 1234, 419, 50, 447];

    private static ArCalibrationArchiveRow Row(string profile = "tilt45", int keyCount = 88, double fov = 71.5) => new(
        profile, 1280, 720, keyCount, fov,
        PositionX: .5, PositionY: -.25, PositionZ: 1.2,
        PitchDegrees: 3, YawDegrees: -2, RollDegrees: 1, UniformScale: 1.04,
        Corners: Corners88, PoseBaseCorners: Base88);

    private static ArCalibrationArchiveBundle Bundle(params ArCalibrationArchiveRow[] rows) => new(
        "\\\\?\\USB#VID_1234", "Streamcam", "2.0.1", "2026-10-12T20:00:00Z",
        true, 1280, 720, rows);

    [Fact]
    public void ABackupRoundTripsEveryField()
    {
        var json = ArCalibrationArchive.ToJson(Bundle(Row(), Row("overhead90", 61, 130)));
        Assert.True(ArCalibrationArchive.TryRead(json, out var bundle, out var err), err);
        Assert.Equal("\\\\?\\USB#VID_1234", bundle!.CameraLink);
        Assert.Equal("Streamcam", bundle.CameraName);
        Assert.True(bundle.HasPicture && bundle.PictureWidth == 1280 && bundle.PictureHeight == 720);
        Assert.Equal(2, bundle.Rows.Count);
        var overhead = bundle.Rows.Single(r => r.Profile == "overhead90");
        Assert.Equal(61, overhead.KeyCount);
        Assert.Equal(ArCameraPose.NormalizeFov(130), overhead.CameraFovDegrees, 6);   // out of range, clamped by the same rule the live store applies
        Assert.Equal(1.04, overhead.UniformScale, 9);
        Assert.Equal(Corners88, overhead.Corners);
        Assert.Equal(Base88, overhead.PoseBaseCorners);
    }

    [Fact]
    public void FilesThatAreNotTheBackupAreRefusedInWords()
    {
        Assert.False(ArCalibrationArchive.TryRead("{ }", out var bundle, out var err));
        Assert.Null(bundle);
        Assert.Equal("That file is not a Keyflow calibration backup.", err);
        var newer = """{ "format": "keyflow-ar-calibration", "formatVersion": 99, "calibrations": [] }""";
        Assert.False(ArCalibrationArchive.TryRead(newer, out _, out err));
        Assert.Equal("The backup was written by a newer Keyflow.", err);
        var noRows = """{ "format": "keyflow-ar-calibration", "formatVersion": 1 }""";
        Assert.False(ArCalibrationArchive.TryRead(noRows, out _, out err));
        Assert.Equal("The backup holds no calibrations.", err);
    }

    [Fact]
    public void RowsThatWouldNeverDrawAreSkippedNotRefused()
    {
        var crossed = Row() with { Corners = [100, 100, 900, 100, 100, 600, 900, 600] };   // folded over itself
        var broken = Row() with { PoseBaseCorners = [10, 10, 11, 10, 10, 11] };            // a baseline that is not a quad
        var json = ArCalibrationArchive.ToJson(Bundle(crossed, broken, Row("overhead90")));
        Assert.True(ArCalibrationArchive.TryRead(json, out var bundle, out _), "the file as a whole is fine");
        var rows = bundle!.Rows;
        Assert.Equal(2, rows.Count(r => r is not null));
        Assert.Single(rows.Where(r => r.Profile == ArCalibrationArchiveRowFromSanitize("overhead90")));
        // the crossed quad is dropped entirely; the broken baseline falls back to the corners, like the store's own load path does
        var fallen = rows.Single(r => r.Profile == "tilt45");                                    // the broken baseline row survives as tilt45
        Assert.Equal(Corners88, fallen.PoseBaseCorners);
    }

    private static string ArCalibrationArchiveRowFromSanitize(string id) => ArCalibrationProfiles.Normalize(id).Id;

    [Fact]
    public void TheLiveStoreExportsAndRestoresItsOwnRows()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "keyflow-backup-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var store = new ArCalibrationStore(dir);
            (double X, double Y)[] corners = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
            Assert.True(store.Set("\\\\cam-a", "tilt45", 1280, 720, 88, corners, 71.5, positionX: .5, uniformScale: 1.04));
            Assert.True(store.Set("\\\\cam-b", "tilt45", 1280, 720, 88, corners));
            var rows = store.ExportRows("\\\\cam-a");
            Assert.Single(rows);
            var bundle = new ArCalibrationArchiveBundle("\\\\cam-a", "A", "1.0", "now", false, 1280, 720, 0, rows);
            var rt = ArCalibrationArchive.TryRead(ArCalibrationArchive.ToJson(bundle), out var back, out _);
            Assert.True(rt);
            var restored = new ArCalibrationStore(dir);
            var (done, skipped) = restored.ImportRows(back!.CameraLink, back.Rows);
            Assert.Equal(1, done); Assert.Equal(0, skipped);
            var cal = restored.GetCalibration("\\\\cam-a", "tilt45", 1280, 720, 88);
            Assert.NotNull(cal);
            Assert.Equal(71.5, cal!.CameraFovDegrees, 6);
            Assert.Equal(1.04, cal.Transform.UniformScale, 9);
            Assert.Equal(.5, cal.Transform.PositionX, 9);
            Assert.Equal(corners, cal.Corners);
            // and the other camera stayed untouched: restoring cam-a never wrote cam-b's keys
            Assert.Equal(2, restored.Count);
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
    }
}
