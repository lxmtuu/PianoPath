using System.IO;

namespace PianoPath;

/// <summary>
/// Watches the library folder so the list follows the disk instead of a button: a file added, edited, renamed
/// or deleted under the watched folder calls back once per change.
///
/// <para>
/// The callback runs on the thread the file-system watcher happens to use, so the window it belongs to only
/// raises a flag there and does the rescanning on its own clock — a scan must never run on a thread that draws.
/// The watcher is rebuilt whenever another folder is chosen, and every failure (a folder that does not exist,
/// a buffer that overflows) is treated as "something changed": a rescan is cheap and always safe.
/// </para>
/// </summary>
internal sealed class SongFolderWatcher : IDisposable
{
    private readonly Action _changed;
    private FileSystemWatcher? _watcher;

    internal SongFolderWatcher(Action changed) => _changed = changed;

    /// <summary>The folder being watched, or empty when nothing is.</summary>
    internal string WatchedFolder => _watcher?.Path ?? "";

    /// <summary>True while a folder is watched; the dock says whether the list is live.</summary>
    internal bool IsWatching => _watcher is { EnableRaisingEvents: true };

    /// <summary>Watches a folder instead of whatever was watched before; an empty or missing folder stops it.</summary>
    internal void Watch(string folder)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Error += (_, _) => Raise();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch
        {
            // A folder that cannot be watched is still a folder that can be scanned with RESCAN.
            _watcher = null;
        }
    }

    /// <summary>Stops watching; the folder stays where it was so the next launch does not have to be told again.</summary>
    internal void Stop()
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is null) return;
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnChanged; watcher.Changed -= OnChanged;
            watcher.Deleted -= OnChanged; watcher.Renamed -= OnChanged;
            watcher.Dispose();
        }
        catch
        {
            // Disposing a watcher that the system already dropped is not an error worth reporting.
        }
    }

    public void Dispose() => Stop();

    private void OnChanged(object sender, FileSystemEventArgs e) => Raise();

    private void Raise()
    {
        try { _changed(); }
        catch
        {
            // The window is gone or its timer is already ticking; either way there is nothing to do here.
        }
    }
}
