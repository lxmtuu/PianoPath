using System;
using System.Linq;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the portable calibration backup (Camera/ArCalibrationArchive.cs): a backup must round-trip every field,
/// the store's own sweep must re-check whatever comes back, and a file that is not this archive must be refused in
/// words, never guessed at.
/// </summary>
public class ArCalibrationArchiveTests
{
    private static readonly double[] Corners88 = [70, 292, 1214, 268, 1236, 421, 52, 449];

    private static ArCalibrationArchiveRow Row(string profile = "tilt45") => new(
        profile, 1280, 720, 88, 71.5,
        PositionX: .5, PositionY: -.25, PositionZ: 1.2,
        PitchDegrees: 3, YawDegrees: -2, RollDegrees: 1, UniformScale: 1.04,
        Corners: (double[])Corners88.Clone(),
        PoseBaseCorners: [68, 290, 1212, 266, 1234, 419, 50, 447]);

    private static ArCalibrationArchiveBundle Bundle(params ArCalibrationArchiveRow[] rows) => new(
        "\\\\?\\USB#VID_1234", "Streamcam", "2.0.1", "2026-10-12T10:30:00Z",
        HasPicture: true, PictureWidth: 1280, PictureHeight: 720, SkippedRows: 0, Rows: rows);

    [Fact]
    public void ABackupRoundTripsEveryField()
    {
        var json = ArCalibrationArchive.ToJson(Bundle(Row(), Row("overhead90")));
        Assert.True(ArCalibrationArchive.TryRead(json, out var bundle, out var error), error);
        Assert.NotNull(bundle);
        Assert.Equal("\\\\?\\USB#VID_1234", bundle!.CameraLink);
        Assert.True(bundle.HasPicture && bundle.PictureWidth == 1280 && bundle.SkippedRows == 0);
        Assert.Equal(2, bundle.Rows.Count);
        var restored = bundle.Rows.Single(row => row.Profile == "overhead90");
        Assert.Equal(71.5, restored.CameraFovDegrees, 6);
        Assert.Equal(.5, restored.PositionX, 9);
        Assert.Equal(1.04, restored.UniformScale, 9);
        Assert.Equal(3, restored.PitchDegrees, 9);
        Assert.Equal(Corners88, restored.Corners);
        Assert.Equal(68, restored.PoseBaseCorners[0], 9);
    }

    [Fact]
    public void AFileThatIsNotThisArchiveIsRefusedInWords()
    {
        Assert.False(ArCalibrationArchive.TryRead("{\"preset\": 1}", out _, out var error) && error.Length == 0);
        Assert.False(ArCalibrationArchive.TryRead("not json at all", out _, out error));
        Assert.True(error.Contains("JSON") || error.Contains("backup"), error);
    }

    [Fact]
    public void RowsThatWouldNeverDrawAreSkippedNotRestored()
    {
        var openQuad = Row() with { Corners = [100, 100, 900, 100, 100, 600, 900, 600] };   // self-crossing: unusable
        var brokenSize = Row() with { ImageWidth = 0 };
        var json = ArCalibrationArchive.ToJson(Bundle(openQuad, brokenSize, Row()));
        Assert.True(ArCalibrationArchive.TryRead(json, out var bundle, out _));
        Assert.Single(bundle!.Rows);
        Assert.Equal(2, bundle.SkippedRows);
    }

    [Fact]
    public void SanitizingKeepsTheStoresOwnRules()
    {
        // a baseline that fails its winding check falls back to the corners, exactly like the live store's load path
        var raw = Row() with { PoseBaseCorners = [10, 10, 11, 10, 10, 11, 11, 11] };
        Assert.True(ArCalibrationArchive.TrySanitizeRow(raw, out var row));
        Assert.Equal(raw.Corners, row.PoseBaseCorners);
        // a nonsense FOV is normalized, so a hand-edited backup cannot smuggle a black picture
        Assert.True(ArCalibrationArchive.TrySanitizeRow(Row() with { CameraFovDegrees = 500 }, out var clamped));
        Assert.Equal(ArCameraPose.NormalizeFov(500), clamped.CameraFovDegrees, 6);
    }

    [Fact]
    public void TheStoreExportsAndRestoresItsOwnRows()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "keyflow-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var store = new ArCalibrationStore(directory);
            Assert.True(store.Set("\\camA", "tilt45", 1280, 720, 88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], 70));
            Assert.True(store.Set("\\camB", "tilt45", 1280, 720, 88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], 70));
            Assert.Single(store.ExportRows("\\camA"));           // only the camera asked for
            var bundle = new ArCalibrationArchiveBundle("\\camA", "A", "2.0.1", "", false, 0, 0, 0, store.ExportRows("\\camA"));
            var imported = new ArCalibrationStore(directory);
            var (restored, skipped) = imported.ImportRows("\\camA", ArCalibrationArchive.TryRead(ArCalibrationArchive.ToJson(bundle), out var back, out _) ? back!.Rows : []);
            Assert.Equal((1, 0), (restored, skipped));
            Assert.NotNull(imported.GetCalibration("\\camA", "tilt45", 1280, 720, 88));
            Assert.Equal(70, imported.GetCameraFov("\\camA", "tilt45", 1280, 720, 88));
        }
        finally { try { System.IO.Directory.Delete(directory, true); } catch { } }
    }
}
