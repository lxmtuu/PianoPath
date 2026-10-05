namespace PianoPath;

/// <summary>
/// Decides how long a reader that is not a live camera should wait before it hands out the next frame.
///
/// <para>
/// A live camera needs no pacing: asking it for a frame returns when it has one, so the loop runs at the camera's
/// own rate — 30, 60 or whatever the mode is. A video file would be read as fast as the disk allows, so it is
/// held to the times written in it: a frame stamped 0.5 s after the first is shown 0.5 s after the first. A file
/// with no usable times falls back to a steady <see cref="FallbackSeconds"/>.
/// </para>
/// </summary>
internal sealed class FramePacer
{
    /// <summary>The wait between frames of a file that carries no times: 30 fps, the pace the overlay used to run at.</summary>
    internal const double FallbackSeconds = 1.0 / 30;

    /// <summary>The longest wait asked for in one go, so a corrupt time cannot freeze the overlay.</summary>
    internal const double MaxWaitSeconds = 0.25;

    private bool _started;
    private long _firstSample, _lastSample;
    private double _firstNow;

    /// <summary>Starts again, as for a file that was rewound.</summary>
    internal void Reset() => _started = false;

    /// <summary>
    /// Seconds to wait before showing a frame stamped <paramref name="sampleTime"/> (100 ns units, as Media Foundation
    /// writes them), when <paramref name="nowSeconds"/> is the clock reading as it is handed over.
    /// </summary>
    internal double WaitSeconds(long sampleTime, double nowSeconds)
    {
        if (!_started)
        {
            _started = true;
            _firstSample = _lastSample = sampleTime; _firstNow = nowSeconds;
            return 0;
        }
        if (sampleTime <= _lastSample)
        {
            _lastSample = sampleTime;      // times that do not rise tell nothing: keep the steady pace
            return FallbackSeconds;
        }
        _lastSample = sampleTime;
        var due = (sampleTime - _firstSample) / 1e7;
        var wait = due - (nowSeconds - _firstNow);
        if (wait < 0)
        {
            // Behind: show it now, and start counting again so one stall is not paid back as a burst of frames.
            _firstSample = sampleTime; _firstNow = nowSeconds;
            return 0;
        }
        return Math.Min(wait, MaxWaitSeconds);
    }
}
