namespace PianoPath;

/// <summary>What <see cref="CameraAgeMeter"/> has seen so far.</summary>
/// <param name="Frames">Camera frames counted.</param>
/// <param name="LastMilliseconds">The age of the newest one when it was first drawn.</param>
/// <param name="AverageMilliseconds">A running average (the newest frames weigh most).</param>
/// <param name="WorstMilliseconds">The oldest a frame was when first drawn.</param>
internal readonly record struct CameraAge(long Frames, double LastMilliseconds, double AverageMilliseconds, double WorstMilliseconds);

/// <summary>
/// How long a camera picture waited inside the app: from the moment the reader got the frame
/// (<see cref="CameraFrameView.CaptureTicks"/>) until the render thread first drew it. It counts only what the app does with a frame — the
/// camera's own exposure, USB and driver time come before that moment and are the business of the latency probe — so it is the part of
/// the delay that the app can be held to, for a stream or a call, whatever camera is plugged in. One frame is counted once, however many
/// times it is drawn. Written by the render thread, read from any.
/// </summary>
internal sealed class CameraAgeMeter
{
    private const double Smoothing = .1;
    private readonly object _gate = new();
    private long _sequence, _frames;
    private double _last, _average, _worst;

    /// <summary>
    /// Counts the frame now in the camera texture, the first time it is seen. Frames without a time (0), from the future, or already
    /// counted say nothing and are ignored. True when the frame was counted.
    /// </summary>
    internal bool Observe(long sequence, long captureTicks, long nowTicks, long ticksPerSecond)
    {
        if (sequence <= 0 || captureTicks <= 0 || ticksPerSecond <= 0 || nowTicks < captureTicks) return false;
        var milliseconds = (nowTicks - captureTicks) * 1000.0 / ticksPerSecond;
        lock (_gate)
        {
            if (sequence == _sequence) return false;
            _sequence = sequence;
            _last = milliseconds;
            _average = _frames == 0 ? milliseconds : _average + (milliseconds - _average) * Smoothing;
            _worst = Math.Max(_worst, milliseconds);
            _frames++;
            return true;
        }
    }

    /// <summary>
    /// Takes the frame already on show as seen without counting it: a take that starts now is about the pictures drawn from now on,
    /// not about one that was already waiting on the stage.
    /// </summary>
    internal void Skip(long sequence)
    {
        lock (_gate) _sequence = sequence;
    }

    /// <summary>Forgets everything: the camera went away or another took its place.</summary>
    internal void Reset()
    {
        lock (_gate) { _sequence = 0; _frames = 0; _last = _average = _worst = 0; }
    }

    internal CameraAge Snapshot { get { lock (_gate) return new CameraAge(_frames, _last, _average, _worst); } }
}
