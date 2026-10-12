using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PianoPath;

/// <summary>
/// The KEYBOARD ALIGNMENT card's EXPORT CALIBRATION / IMPORT CALIBRATION buttons (§14.7 "Thêm 3"). A backup is one
/// JSON file holding every saved calibration of the camera in use — every view, picture size and keyboard size —
/// plus a snapshot picture of that camera saved beside the file, so a person browsing old backups can see which
/// camera the file belongs to (the picture is for people only; restoring never looks at it). Restoring runs every
/// row through the same checks a fresh calibration passes; rows that fail are counted and skipped, never trusted.
/// No setting is touched, so presets and share codes are unaffected, like the calibrations themselves.
/// </summary>
public partial class MainWindow
{
    /// <summary>Saves every calibration of the camera in use to one file, with a picture of the camera beside it.</summary>
    private void ExportArCalibrationBackup(object sender, RoutedEventArgs e)
    {
        if (_arSource.Length == 0) { ShowMessage(Loc.T("Open the camera or a video on the Camera page first — the backup belongs to the camera in use."), "AR calibration backup", MessageBoxImage.Information); return; }
        var rows = ArCalibration.Store.ExportRows(_arSource);
        if (rows.Count == 0) { ShowMessage(Loc.T("Nothing is saved for this camera yet — mark the keys with Calibrate first."), "AR calibration backup", MessageBoxImage.Information); return; }
        var name = ArBackupCameraName();
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Export the AR calibration backup"),
            Filter = Loc.T("Keyflow calibration backup (*.json)|*.json"),
            FileName = "keyflow-ar-calibration-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json",
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // The newest live frame turned the way the calibrator marks corners on: the camera's own picture, mirrored
            // back when the camera thread mirrored it for display.
            var picture = CalibrationPicture.Capture(_cameraFrames, _visualSettings.CameraMirror && !_arRawFrames);
            File.WriteAllText(dlg.FileName,
                ArCalibrationArchive.ToJson(new ArCalibrationArchiveBundle(_arSource, name, AppInfo.Version, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm\" UTC\""),
                    picture is not null, picture?.Width ?? 0, picture?.Height ?? 0, 0, rows)));
            if (picture is not null)
                WriteArBackupPng(Path.ChangeExtension(dlg.FileName, ".png"), picture.Bgra, picture.Width, picture.Height);
            ShowMessage(Loc.F(picture is not null
                    ? "Exported {0} saved calibrations for {1}; a snapshot picture sits beside the file."
                    : "Exported {0} saved calibrations for {1} (no camera picture to archive).", rows.Count, name),
                "AR calibration backup", MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            ShowMessage(Loc.F("That file could not be used: {0}", ex.Message), "AR calibration backup", MessageBoxImage.Exclamation);
        }
    }

    /// <summary>Puts a backup's calibrations back, every row checked the way a fresh calibration is checked.</summary>
    private void ImportArCalibrationBackup(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Restore an AR calibration backup"),
            Filter = Loc.T("Keyflow calibration backup (*.json)|*.json"),
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (!ArCalibrationArchive.TryRead(File.ReadAllText(dlg.FileName), out var bundle, out var error) || bundle is null)
            {
                ShowMessage(Loc.F("That file could not be used: {0}", error), "AR calibration backup", MessageBoxImage.Exclamation);
                return;
            }
            var (restored, skipped) = ArCalibration.Store.ImportRows(
                bundle.CameraLink.Length > 0 ? bundle.CameraLink : _arSource, bundle.Rows);
            skipped += bundle.SkippedRows;
            // The same refresh the end of a calibration does: the overlay re-reads the store and draws the restored grid.
            if (_arSource.Length > 0)
                ArCalibration.Use(_arSource, _arWidth, _arHeight, _visualSettings.KeyboardLayoutChoice, _visualSettings.ArViewProfileId);
            ShowMessage(Loc.F("Restored {0} calibrations ({1} rows skipped: they failed the same checks a fresh calibration passes).",
                    restored, skipped), "AR calibration backup", MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(Loc.F("That file could not be used: {0}", ex.Message), "AR calibration backup", MessageBoxImage.Exclamation);
        }
    }

    /// <summary>A name a person recognises for the camera in use: the list name, or the video's file name.</summary>
    private string ArBackupCameraName()
    {
        var fromList = _cameraDevices.FirstOrDefault(device => string.Equals(device.Link, _arSource, StringComparison.OrdinalIgnoreCase))?.Name;
        if (!string.IsNullOrWhiteSpace(fromList)) return fromList;
        if (!string.IsNullOrWhiteSpace(_visualSettings.CameraVideoPath)) return Path.GetFileName(_visualSettings.CameraVideoPath);
        return _arSource.Length > 0 ? _arSource : Loc.T("this camera");
    }

    /// <summary>Writes the snapshot as a PNG so it sits next to the JSON in the same folder for people to see.</summary>
    private static void WriteArBackupPng(string path, byte[] bgra, int width, int height)
    {
        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, width * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
