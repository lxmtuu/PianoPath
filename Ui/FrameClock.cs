using System.Diagnostics;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// The single frame driver of the application.
///
/// Everything that animates on screen — the piano stage, the animated chrome backdrop and the
/// on-screen transitions — asks this clock for frames instead of running its own timer. That gives
/// three properties a <see cref="System.Windows.Threading.DispatcherTimer"/> cannot provide:
///
/// * <b>VSync alignment.</b> Frames are delivered from <see cref="CompositionTarget.Rendering"/>, so
///   the animation is produced in step with the monitor refresh instead of drifting against it
///   (the classic cause of "almost smooth" note motion).
/// * <b>Duplicate-frame rejection.</b> WPF raises <c>Rendering</c> more than once for the same
///   composition frame; forwarding those would advance the simulation by zero seconds and stall
///   every particle for a frame. Only a strictly newer composition time is a real frame.
/// * <b>Automatic idle.</b> Frames are requested (<see cref="Acquire"/>) and released
///   (<see cref="Release"/>); when nobody needs them the clock unhooks completely, so an idle stage
///   costs no CPU and lets the laptop sleep.
/// </summary>
internal sealed class FrameClock
{
    /// <summary>Longest accepted frame delta. A stall (window drag, debugger, sleep) must not teleport particles.</summary>
    private const double MaxDelta = .1;

    internal static FrameClock Shared { get; } = new();

    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private double _lastFrameSeconds = -1;
    private double _delta, _fps;
    private int _demands;
    private bool _hooked;

    private FrameClock() { }

    /// <summary>
    /// Switches the clock onto a fixed frame step and starts counting frames, for renders that have to
    /// come out byte-identical from one run to the next — the README previews.
    ///
    /// Every animator in the app integrates the delta this clock hands out, so on a wall-clock step the
    /// picture depends on how fast the machine happened to draw: the same window screenshotted twice
    /// lands the petals, wisps and arcs at different phases and the two PNGs differ. Pinning the step
    /// makes the picture a function of the frame count alone, and <see cref="FrameCount"/> is what the
    /// capture waits for, so "the same interface" really does produce the same bytes.
    /// </summary>
    internal void UseFixedStep(double seconds)
    {
        _fixedDelta = seconds;
        FrameCount = 0;
        _lastFrameSeconds = -1;
    }

    /// <summary>
    /// Frames delivered since the last <see cref="Acquire"/> (or since <see cref="UseFixedStep"/>).
    /// Counted on real composition frames, so it advances at the machine's own pace while the
    /// simulation it drives does not.
    /// </summary>
    internal long FrameCount { get; private set; }

    private double? _fixedDelta;

    /// <summary>Raised once per real composition frame with the seconds elapsed since the previous one.</summary>
    internal event Action<double>? Tick;

    /// <summary>Seconds between the two most recent frames, already clamped by <see cref="MaxDelta"/>.</summary>
    internal double DeltaSeconds => _delta;

    /// <summary>Smoothed frames per second, used by the on-stage HUD.</summary>
    internal double Fps => _fps;

    /// <summary>True while at least one consumer needs frames.</summary>
    internal bool IsRunning => _hooked;

    /// <summary>Monotonic seconds since the process started; the shared time source for every animator.</summary>
    internal double Time => _watch.Elapsed.TotalSeconds;

    /// <summary>Registers a consumer. Pair every call with <see cref="Release"/>, usually from a timer-driven state change.</summary>
    internal void Acquire()
    {
        _demands++;
        if (_hooked) return;
        _hooked = true;
        _lastFrameSeconds = -1;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Unregisters a consumer; the clock detaches from the compositor once the last one leaves.</summary>
    internal void Release()
    {
        _demands = Math.Max(0, _demands - 1);
        if (_demands > 0 || !_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : _watch.Elapsed.TotalSeconds;
        // A repeated composition time is not a new frame. Skipping it keeps the motion smooth; feeding
        // it forward would double-report the previous frame as a zero-length one.
        if (_lastFrameSeconds >= 0 && now <= _lastFrameSeconds) return;
        _delta = _fixedDelta ?? (_lastFrameSeconds < 0 ? 1.0 / 60 : Math.Clamp(now - _lastFrameSeconds, 0, MaxDelta));
        _lastFrameSeconds = now;
        FrameCount++;
        if (_delta > 0) _fps += (1 / _delta - _fps) * .08;
        Tick?.Invoke(_delta);
    }
}
