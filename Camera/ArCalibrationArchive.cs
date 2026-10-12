using System.Text.Json;

namespace PianoPath;

/// <summary>
/// One saved calibration as the archive carries it — the same identity and the same field layout as an
/// <c>ar-calibration.json</c> entry (camera link + view + picture size + keyboard size, corners as eight doubles:
/// final back-left, back-right, front-right, front-left, and the pose's unrotated baseline the same way).
/// </summary>
internal sealed record ArCalibrationArchiveRow(
    string Profile, int ImageWidth, int ImageHeight, int KeyCount, double CameraFovDegrees,
    double PositionX, double PositionY, double PositionZ,
    double PitchDegrees, double YawDegrees, double RollDegrees, double UniformScale,
    double[] Corners, double[] PoseBaseCorners);

/// <summary>
/// What one backup file holds: who the camera is, when the backup was written, whether a snapshot picture sits
/// beside it (for the human archive; restores never need it), and every saved key area of that camera — all views,
/// picture sizes and keyboard sizes — after each row passed the same checks the live store applies.
/// <paramref name="SkippedRows"/> counts rows the checks rejected (never a ground to refuse the file).
/// </summary>
internal sealed record ArCalibrationArchiveBundle(
    string CameraLink, string CameraName, string AppVersion, string SavedUtc,
    bool HasPicture, int PictureWidth, int PictureHeight, int SkippedRows,
    IReadOnlyList<ArCalibrationArchiveRow> Rows);

/// <summary>
/// The portable calibration backup (AR roadmap §14.7 "Thêm 3 — xuất/nhập bản sao căn chỉnh"): one JSON document
/// (<c>FormatId</c>/<c>FormatVersion</c>) plus a snapshot PNG written next to it by the window. Everything about
/// the document is pure so xUnit and <c>--verify</c> can hold the round trip and the validation without a disk,
/// a camera or WPF's PNG encoders.
/// </summary>
internal static class ArCalibrationArchive
{
    internal const string FormatId = "keyflow-ar-calibration";
    internal const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class Envelope
    {
        public string? Format { get; set; }
        public int FormatVersion { get; set; }
        public string? CameraLink { get; set; }
        public string? CameraName { get; set; }
        public string? AppVersion { get; set; }
        public string? SavedUtc { get; set; }
        public bool HasPicture { get; set; }
        public int PictureWidth { get; set; }
        public int PictureHeight { get; set; }
        public ArCalibrationArchiveRow[]? Calibrations { get; set; }
    }

    /// <summary>The archive's JSON view of four points: flat x0,y0,x1,y1,x2,y2,x3,y3, like the store itself writes them.</summary>
    internal static double[] Flat(IReadOnlyList<(double X, double Y)> points) =>
        points.Count != 4 ? [] : [points[0].X, points[0].Y, points[1].X, points[1].Y, points[2].X, points[2].Y, points[3].X, points[3].Y];

    /// <summary>The four points inside a flat eight-double array, or [] when the array is not one.</summary>
    internal static (double X, double Y)[] Points(double[]? flat) => flat is { Length: 8 } f && f.All(double.IsFinite)
        ? [(f[0], f[1]), (f[2], f[3]), (f[4], f[5]), (f[6], f[7])]
        : [];

    /// <summary>
    /// Applies the same acceptance rules the live store's own load path applies (winding of corners and baseline,
    /// finite numbers, normalized FOV, a sane transform — an insane one falls back to identity with the corners as
    /// their own baseline), so a file restored later cannot smuggle in an area the app would not draw.
    /// </summary>
    internal static bool TrySanitizeRow(ArCalibrationArchiveRow raw, out ArCalibrationArchiveRow row)
    {
        row = raw;
        var corners = Points(raw.Corners);
        if (raw.ImageWidth <= 0 || raw.ImageHeight <= 0 || raw.KeyCount <= 0 || corners.Length != 4) return false;
        if (CalibrationQuad.CheckAnyWinding(corners, raw.ImageWidth, raw.ImageHeight) != CalibrationProblem.None) return false;
        var transform = new ArPoseTransform(raw.PositionX, raw.PositionY, raw.PositionZ, raw.PitchDegrees, raw.YawDegrees, raw.RollDegrees, raw.UniformScale);
        var baselineValid = Points(raw.PoseBaseCorners) is { Length: 4 } baseline
            && CalibrationQuad.CheckAnyWinding(baseline, raw.ImageWidth, raw.ImageHeight) == CalibrationProblem.None;
        // The store combines these the opposite way on the way OUT of a fresh calibration (an invalid baseline means
        // "no pose baseline", not "discard the pose"): keeping a good pose while the baseline falls back to the
        // corners is what Set() does too, so a restore behaves like the original save.
        if (!transform.IsValid) transform = ArPoseTransform.Identity;
        row = raw with
        {
            Profile = ArCalibrationProfiles.Normalize(raw.Profile).Id,
            CameraFovDegrees = ArCameraPose.NormalizeFov(raw.CameraFovDegrees),
            PositionX = transform.PositionX, PositionY = transform.PositionY, PositionZ = transform.PositionZ,
            PitchDegrees = transform.PitchDegrees, YawDegrees = transform.YawDegrees, RollDegrees = transform.RollDegrees,
            UniformScale = transform.UniformScale,
            Corners = (double[])raw.Corners.Clone(),
            PoseBaseCorners = baselineValid ? (double[])raw.PoseBaseCorners!.Clone() : (double[])raw.Corners.Clone(),
        };
        return true;
    }

    /// <summary>Serializes the backup: one indented JSON document whose every row already passed <see cref="TrySanitizeRow"/>.</summary>
    internal static string ToJson(ArCalibrationArchiveBundle bundle) => JsonSerializer.Serialize(new Envelope
    {
        Format = FormatId,
        FormatVersion = FormatVersion,
        CameraLink = bundle.CameraLink,
        CameraName = bundle.CameraName,
        AppVersion = bundle.AppVersion,
        SavedUtc = bundle.SavedUtc,
        HasPicture = bundle.HasPicture,
        PictureWidth = bundle.PictureWidth,
        PictureHeight = bundle.PictureHeight,
        Calibrations = bundle.Rows.ToArray(),
    }, Options);

    /// <summary>
    /// Reads a backup. False with <paramref name="error"/> when the text is not this archive at all; otherwise true
    /// with every row that survived <see cref="TrySanitizeRow"/> in <c>Rows</c> and the count of rejected rows in
    /// <c>SkippedRows</c>. A newer <c>FormatVersion</c> refuses, so a downgraded app does not silently lose fields.
    /// </summary>
    internal static bool TryRead(string json, out ArCalibrationArchiveBundle? bundle, out string error)
    {
        bundle = null; error = "";
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(json, ReadOptions); }
        catch (JsonException) { error = "The file is not readable JSON."; return false; }
        if (envelope is null || !string.Equals(envelope.Format, FormatId, StringComparison.Ordinal))
        { error = "That file is not a Keyflow calibration backup."; return false; }
        if (envelope.FormatVersion > FormatVersion)
        { error = "The backup was written by a newer Keyflow."; return false; }
        if (envelope.Calibrations is null || envelope.Calibrations.Length == 0)
        { error = "The backup holds no calibrations."; return false; }
        var rows = new List<ArCalibrationArchiveRow>();
        var skipped = 0;
        foreach (var raw in envelope.Calibrations ?? [])
            if (TrySanitizeRow(raw, out var row)) rows.Add(row);
            else skipped++;
        bundle = new ArCalibrationArchiveBundle(
            envelope.CameraLink ?? "", envelope.CameraName ?? "", envelope.AppVersion ?? "", envelope.SavedUtc ?? "",
            envelope.HasPicture, envelope.PictureWidth, envelope.PictureHeight, skipped, rows);
        return true;
    }
}
