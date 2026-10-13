using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PianoPath;

/// <summary>
/// The "Thêm 3" buttons of the KEYBOARD ALIGNMENT card (roadmap §14.7): EXPORT CALIBRATION writes every calibration
/// saved for the camera in use (all views, picture sizes and keyboard sizes) to one JSON document, with a snapshot of
/// the camera's current frame beside it for the archive a person keeps (restoring never needs the picture); IMPORT
/// CALIBRATION reads such a file back under its own camera link — every row passes the same checks a fresh calibration
/// passes, so what the restore puts down is exactly what the app would have accepted from clicking the corners today.
/// </summary>
public partial class MainWindow
{
    private void ExportArCalibrationBackup_Click(object sender, RoutedEventArgs e)
    {
        List<ArCalibrationArchiveRow> rows = ArCalibration.Store.ExportRows(_arSource)
            .Where(row => _arWidth <= 0 || (row.ImageWidth == _arWidth && row.ImageHeight == _arHeight))
            .Where(row => row.KeyCount == _visualSettings.KeyboardLayoutChoice.KeyCount)
            .ToList();
        if (rows.Count == 0)
        { ShowMessage(Loc.T("Nothing is saved for this camera yet — mark the keys with Calibrate first."), "AR calibration backup", MessageBoxImage.Information); return; }
        var cameraName = ArBackupCameraName();
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Export the AR calibration backup"),
            Filter = Loc.T("Keyflow calibration backup (*.json)|*.json"),
            FileName = "keyflow-calibration-" + MakeSlug(cameraName) + ".json",
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var pict = CalibrationPicture.Capture(_cameraFrames, _visualSettings.CameraMirror && !_arRawFrames);
            File.WriteAllText(dlg.FileName, ArCalibrationArchive.ToJson(new ArCalibrationArchiveBundle(
                _arSource, cameraName, AppInfo.Version, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                pict is not null, _arWidth, _arHeight, 0, rows)));
            if (pict is not null) WriteBackupPicture(Path.ChangeExtension(dlg.FileName, ".png"), pict);
            ShowMessage(Loc.F(pict is not null
                    ? "Exported {0} saved calibrations for {1}. A snapshot picture sits beside the file."
                    : "Exported {0} saved calibrations for {1} (no camera picture was ready to archive).",
                rows.Count, cameraName), "AR calibration backup", MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { ShowMessage(Loc.F("That file could not be used: {0}", ex.Message), "AR calibration backup", MessageBoxImage.Warning); }
    }

    private void ImportArCalibrationBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Restore an AR calibration backup"),
            Filter = Loc.T("Keyflow calibration backup (*.json)|*.json"),
            DefaultExt = ".json",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (!ArCalibrationArchive.TryRead(File.ReadAllText(dlg.FileName), out var bundle, out var error))
            { ShowMessage(Loc.F("That file could not be used: {0}", error), "AR calibration backup", MessageBoxImage.Warning); return; }
            var link = bundle!.CameraLink.Length > 0 ? bundle.CameraLink : _arSource;
            if (link.Length == 0)
            { ShowMessage(Loc.T("Open the camera or a video on the Camera page first — the backup belongs to the camera in use."), "AR calibration backup", MessageBoxImage.Information); return; }
            var (restored, skipped) = ArCalibration.Store.ImportRows(link, bundle.Rows);
            if (_arSource.Length > 0)      // reload whatever is open: restored corners for THIS camera appear at once
                ArCalibration.Use(_arSource, _arWidth, _arHeight, _visualSettings.KeyboardLayoutChoice, _visualSettings.ArViewProfileId);
            UpdateArFeed();
            ShowMessage(Loc.F("Restored {0} calibrations for camera {1} ({2} rows skipped: they failed the same checks a fresh calibration passes).",
                restored, bundle.CameraName.Length > 0 ? bundle.CameraName : link, skipped), "AR calibration backup", MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { ShowMessage(Loc.F("That file could not be used: {0}", ex.Message), "AR calibration backup", MessageBoxImage.Warning); }
    }

    /// <summary>The human name of the camera in use, as the settings picker shows it (the link itself when the picker has none).</summary>
    private string ArBackupCameraName()
    {
        if (_visualChoiceOptions.TryGetValue(nameof(PianoVisualSettings.CameraSourceLink), out var options))
            foreach (var (value, caption) in options)
                if (string.Equals(value, _arSource, StringComparison.OrdinalIgnoreCase)) return caption;
        return _arSource;
    }

    /// <summary>Letters, digits and dashes of a name, for the backup file's default name.</summary>
    private static string MakeSlug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length == 0 ? "camera" : slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }

    /// <summary>The snapshot archived next to the JSON: the raw camera frame as a PNG a person can open.</summary>
    private static void WriteBackupPicture(string path, CalibrationPicture picture)
    {
        var source = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, picture.Bgra, picture.Width * 4);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
