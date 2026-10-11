namespace PianoPath;

/// <summary>
/// The camera delay of whichever camera is open (AR‑3.5): when the overlay moves from one source to another, the
/// slider's value is filed under the one being left, and the one being opened gets its own delay back if it was ever
/// tuned on it. The window owns one and calls it on its own thread; nothing here touches a camera or a window, so the
/// change of hands between cameras is stated by tests instead of discovered on a webcam.
/// </summary>
internal sealed class ArDelaySession
{
    private readonly ArDelayStore _store;
    private string _activeSource = "";

    internal ArDelaySession(ArDelayStore store) => _store = store;

    /// <summary>The source the delay belongs to right now — the camera's link or the video file's path, exactly as the reader reports it; empty when nothing is open.</summary>
    internal string ActiveSource => _activeSource;

    /// <summary>
    /// Says which source is open now: files the current delay under the one being left, and returns the delay saved
    /// for the one being opened — or null when it has none, which leaves the slider where it is. Calling it again for
    /// the same source does nothing, so the window can call it whenever the camera is re-applied.
    /// </summary>
    internal double? SwitchTo(string? source, double currentDelayMs)
    {
        var next = source ?? "";
        if (string.Equals(_activeSource, next, StringComparison.OrdinalIgnoreCase)) return null;
        if (_activeSource.Length > 0) _store.Set(_activeSource, currentDelayMs);
        _activeSource = next;
        return next.Length > 0 ? _store.Get(next) : null;
    }

    /// <summary>
    /// Files the delay under the camera that is open: a drag of the Camera delay slider, and a measured delay the person
    /// accepts, both belong to it. Nothing open means there is no camera to file the value under, and it stays what it
    /// has always been — the machine-wide setting.
    /// </summary>
    internal void Remember(double delayMs)
    {
        if (_activeSource.Length == 0) return;
        _store.Set(_activeSource, delayMs);
    }
}
