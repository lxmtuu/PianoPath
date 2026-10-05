namespace PianoPath;

/// <summary>Where the overlay stands for the camera that is open.</summary>
internal enum ArCalibrationState
{
    /// <summary>No camera (or file) is open, so there is nothing to calibrate against.</summary>
    NoSource,
    /// <summary>This camera, at this picture size and keyboard size, has never been calibrated.</summary>
    NotCalibrated,
    /// <summary>The camera was calibrated at another picture size or keyboard size: the old corners mean nothing here.</summary>
    Outdated,
    /// <summary>A calibration is in force.</summary>
    Ready,
}

/// <summary>
/// One consistent picture of the session: what is open, and the calibration for it if there is one. Immutable, so the
/// drawing thread can read it while the window changes the session.
/// </summary>
/// <param name="Source">The camera's link or the video file's path, as the reader reports it; empty for <see cref="ArCalibrationState.NoSource"/>.</param>
/// <param name="ImageWidth">Width of the picture the camera delivers.</param>
/// <param name="ImageHeight">Height of the picture the camera delivers.</param>
/// <param name="KeyCount">Keys of the keyboard the calibration is for.</param>
/// <param name="Version">Counts every change, so a page can tell its text is stale.</param>
internal sealed record ArCalibrationSnapshot(ArCalibrationState State, ArCalibration? Calibration, string Source, int ImageWidth, int ImageHeight, int KeyCount, int Version);

/// <summary>What <see cref="ArCalibrationSession.Save"/> did.</summary>
internal enum ArSaveResult
{
    Saved,
    /// <summary>Nothing is open, so there is no picture the corners could belong to.</summary>
    NoSource,
    /// <summary>The corners are not a usable key area; the reason is in the problem.</summary>
    BadCorners,
    /// <summary>The calibration works for now but could not be written, so it will be asked for again next time.</summary>
    NotWritten,
}

/// <summary>
/// The calibration of whichever camera is open: finds the saved one when a camera, its picture size or the keyboard size
/// changes, keeps the answer ready for the drawing thread, and saves or forgets it when the player asks. The window owns
/// one and tells it what is open; nothing here touches a camera or a window.
///
/// <para>
/// Only what is open decides the calibration. The mirror switch does not: the corners are clicked on, and kept for, the
/// picture as the camera delivers it, and the overlay turns the picture round at display time like it does for the
/// camera layer.
/// </para>
/// </summary>
internal sealed class ArCalibrationSession
{
    private readonly Lock _gate = new();
    private readonly ArCalibrationStore _store;
    private volatile ArCalibrationSnapshot _snapshot = new(ArCalibrationState.NoSource, null, "", 0, 0, 0, 0);
    private KeyboardLayout? _layout;

    internal ArCalibrationSession(ArCalibrationStore store) => _store = store;

    /// <summary>The newest picture of the session; safe to read from any thread.</summary>
    internal ArCalibrationSnapshot Snapshot => _snapshot;

    internal ArCalibrationState State => _snapshot.State;

    /// <summary>The calibration in force, or null.</summary>
    internal ArCalibration? Current => _snapshot.Calibration;

    internal ArCalibrationStore Store => _store;

    /// <summary>
    /// Says what is open now: the camera (or file), the size of its picture and the keyboard in use. Calling it again with
    /// the same things does nothing, so the window can call it whenever any setting changes. True when something changed.
    /// </summary>
    internal bool Use(string source, int imageWidth, int imageHeight, KeyboardLayout layout)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(source) || imageWidth <= 0 || imageHeight <= 0) return Clear();
            var old = _snapshot;
            if (old.State != ArCalibrationState.NoSource && string.Equals(old.Source, source, StringComparison.OrdinalIgnoreCase)
                && old.ImageWidth == imageWidth && old.ImageHeight == imageHeight && old.KeyCount == layout.KeyCount)
            {
                _layout = layout;
                return false;
            }
            _layout = layout;
            Publish(source, imageWidth, imageHeight, layout);
            return true;
        }
    }

    /// <summary>The camera was closed. True when something was open.</summary>
    internal bool Clear()
    {
        lock (_gate)
        {
            _layout = null;
            if (_snapshot.State == ArCalibrationState.NoSource) return false;
            _snapshot = new ArCalibrationSnapshot(ArCalibrationState.NoSource, null, "", 0, 0, 0, _snapshot.Version + 1);
            return true;
        }
    }

    /// <summary>Takes four corners clicked on the picture as the calibration for what is open, and saves them.</summary>
    internal ArSaveResult Save(IReadOnlyList<(double X, double Y)> corners, out CalibrationProblem problem)
    {
        lock (_gate)
        {
            problem = CalibrationProblem.None;
            var old = _snapshot;
            if (old.State == ArCalibrationState.NoSource || _layout is null) return ArSaveResult.NoSource;
            var calibration = ArCalibration.Create(_layout, corners, old.ImageWidth, old.ImageHeight, out problem);
            if (calibration is null) return ArSaveResult.BadCorners;
            var written = _store.Set(old.Source, old.ImageWidth, old.ImageHeight, old.KeyCount, corners);
            _snapshot = new ArCalibrationSnapshot(ArCalibrationState.Ready, calibration, old.Source, old.ImageWidth, old.ImageHeight, old.KeyCount, old.Version + 1);
            return written ? ArSaveResult.Saved : ArSaveResult.NotWritten;
        }
    }

    /// <summary>Forgets the calibration for what is open ("Reset calibration"). True when there was one.</summary>
    internal bool Reset()
    {
        lock (_gate)
        {
            var old = _snapshot;
            if (old.State == ArCalibrationState.NoSource || _layout is null) return false;
            var had = _store.Remove(old.Source, old.ImageWidth, old.ImageHeight, old.KeyCount);
            Publish(old.Source, old.ImageWidth, old.ImageHeight, _layout);
            return had;
        }
    }

    private void Publish(string source, int width, int height, KeyboardLayout layout)
    {
        var corners = _store.Get(source, width, height, layout.KeyCount);
        var calibration = corners is null ? null : ArCalibration.Create(layout, corners, width, height, out _);
        var state = calibration is not null ? ArCalibrationState.Ready
            : _store.HasOtherSize(source, width, height, layout.KeyCount) ? ArCalibrationState.Outdated
            : ArCalibrationState.NotCalibrated;
        _snapshot = new ArCalibrationSnapshot(state, calibration, source, width, height, layout.KeyCount, _snapshot.Version + 1);
    }
}
