using System.IO;
using System.Text.Json;

namespace PianoPath;

/// <summary>
/// The camera delays kept on this computer, one file in the settings folder, one entry per camera. A camera's
/// picture is late by its own amount — a USB webcam by some tens of milliseconds, a virtual camera or a
/// phone-as-webcam by much more — so one machine-wide number could only ever be right for one of them (AR‑3.5).
/// Which camera an entry belongs to is the same link the calibration file keys on, and a settings preset or a
/// share code never carries one: the delay describes this machine's hardware, not the look.
///
/// <para>
/// A file that cannot be read, or an entry without a usable camera link, is dropped instead of trusted: the worst a
/// damaged file can do is leave a camera at the delay it already showed. Saving writes a temporary file and moves it
/// into place, so a crash cannot leave half a file.
/// </para>
/// </summary>
internal sealed class ArDelayStore
{
    /// <summary>The most delays kept; the least recently written are forgotten beyond it.</summary>
    internal const int MaxEntries = 64;

    internal const string FileName = "ar-delay.json";

    /// <summary>The version this app writes. Version 1 entries are a camera link and its delay in milliseconds.</summary>
    internal const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly string _path;
    private List<Entry> _entries = [];

    internal ArDelayStore(string directory)
    {
        _path = Path.Combine(directory, FileName);
        Reload();
    }

    internal string FilePath => _path;

    internal int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>The delay saved for this camera, or null when it has none — never measured, and never tuned while it was open.</summary>
    internal double? Get(string? cameraLink)
    {
        if (string.IsNullOrWhiteSpace(cameraLink)) return null;
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => string.Equals(e.CameraLink, cameraLink, StringComparison.OrdinalIgnoreCase));
            return entry is null ? null : Clamp(entry.DelayMs);
        }
    }

    /// <summary>Keeps the delay for one camera; true when the file took it. An empty link names no camera, so nothing is kept.</summary>
    internal bool Set(string? cameraLink, double delayMs)
    {
        if (string.IsNullOrWhiteSpace(cameraLink)) return false;
        lock (_gate)
        {
            _entries.RemoveAll(e => string.Equals(e.CameraLink, cameraLink, StringComparison.OrdinalIgnoreCase));
            _entries.Add(new Entry { CameraLink = cameraLink.Trim(), DelayMs = Clamp(delayMs), SavedUtc = DateTime.UtcNow });
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            return Write();
        }
    }

    /// <summary>Forgets the delay of one camera; true when there was one.</summary>
    internal bool Remove(string? cameraLink)
    {
        if (string.IsNullOrWhiteSpace(cameraLink)) return false;
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => string.Equals(e.CameraLink, cameraLink, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Write();
            return removed;
        }
    }

    /// <summary>Reads the file again, dropping whatever cannot be trusted.</summary>
    internal void Reload()
    {
        lock (_gate)
        {
            _entries = [];
            try
            {
                if (!File.Exists(_path)) return;
                var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Options);
                if (file?.Entries is null) return;
                foreach (var entry in file.Entries)
                {
                    if (entry is null || string.IsNullOrWhiteSpace(entry.CameraLink) || !double.IsFinite(entry.DelayMs)) continue;
                    entry.CameraLink = entry.CameraLink.Trim();
                    entry.DelayMs = Clamp(entry.DelayMs);
                    _entries.RemoveAll(e => string.Equals(e.CameraLink, entry.CameraLink, StringComparison.OrdinalIgnoreCase));
                    _entries.Add(entry);
                }
                if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
            catch { _entries = []; }
        }
    }

    private bool Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape { Version = FormatVersion, Entries = _entries }, Options));
            File.Move(temp, _path, true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>The range of the Camera delay slider: a stored value outside it was written by another version, so it is pulled in, not trusted as is.</summary>
    private static double Clamp(double delayMs) => ArOptions.Fit(delayMs, ArOptions.DelayMin, ArOptions.DelayMax, ArOptions.DelayDefault);

    private sealed class FileShape
    {
        public int Version { get; set; }
        public List<Entry>? Entries { get; set; }
    }

    private sealed class Entry
    {
        public string? CameraLink { get; set; }
        public double DelayMs { get; set; }
        /// <summary>When the delay was written; it answers "how stale is this number" and never decides anything.</summary>
        public DateTime SavedUtc { get; set; }
    }
}
