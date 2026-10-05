namespace PianoPath;

/// <summary>A rectangle of the picture in pixels: <c>Left</c> and <c>Top</c> are in, <c>Right</c> and <c>Bottom</c> are the first pixels out.</summary>
internal readonly record struct PixelRegion(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
    internal bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The region fitted inside a picture of this size.</summary>
    internal PixelRegion Within(int imageWidth, int imageHeight) =>
        new(Math.Clamp(Left, 0, imageWidth), Math.Clamp(Top, 0, imageHeight), Math.Clamp(Right, 0, imageWidth), Math.Clamp(Bottom, 0, imageHeight));
}

/// <summary>How much a picture moves, as one number per frame, for <see cref="LatencyProbe"/>.</summary>
internal static class MotionEnergy
{
    /// <summary>Every <c>Step</c>th pixel in each direction is looked at: enough to see a hand, cheap enough for every frame.</summary>
    internal const int Step = 3;

    /// <summary>
    /// Where hands and keys move: the box around the four corners of the keys, a fifth of its height higher (fingers come from above) and
    /// a tenth lower, fitted to the picture. Empty when the corners are not usable.
    /// </summary>
    internal static PixelRegion KeysRegion(IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight)
    {
        if (corners.Count != 4 || imageWidth <= 0 || imageHeight <= 0 || corners.Any(c => !double.IsFinite(c.X) || !double.IsFinite(c.Y))) return new(0, 0, 0, 0);
        double left = corners.Min(c => c.X), right = corners.Max(c => c.X), top = corners.Min(c => c.Y), bottom = corners.Max(c => c.Y);
        var height = bottom - top;
        return new PixelRegion((int)Math.Floor(left), (int)Math.Floor(top - height * 0.2), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom + height * 0.1)).Within(imageWidth, imageHeight);
    }

    /// <summary>How many values <see cref="Sample"/> writes for this region.</summary>
    internal static int SampleCount(PixelRegion region) => region.IsEmpty ? 0 : ((region.Width + Step - 1) / Step) * ((region.Height + Step - 1) / Step);

    /// <summary>
    /// Writes the brightness of every sampled pixel of the region into <paramref name="into"/> (at least <see cref="SampleCount"/> long).
    /// False for a picture that is too small for its size or a region outside it; nothing is written then.
    /// </summary>
    internal static bool Sample(byte[] bgra, int imageWidth, int imageHeight, PixelRegion region, byte[] into)
    {
        region = region.Within(imageWidth, imageHeight);
        if (region.IsEmpty || imageWidth <= 0 || imageHeight <= 0 || bgra.Length < (long)imageWidth * imageHeight * 4 || into.Length < SampleCount(region)) return false;
        var n = 0;
        for (var y = region.Top; y < region.Bottom; y += Step)
        {
            var row = y * imageWidth * 4;
            for (var x = region.Left; x < region.Right; x += Step)
            {
                var i = row + x * 4;
                into[n++] = (byte)((bgra[i] * 29 + bgra[i + 1] * 150 + bgra[i + 2] * 77) >> 8);
            }
        }
        return true;
    }

    /// <summary>The average change of brightness between two samplings of the same region, 0 (the same) to 1 (black against white).</summary>
    internal static double Between(byte[] previous, byte[] current, int count)
    {
        if (count <= 0 || previous.Length < count || current.Length < count) return 0;
        long sum = 0;
        for (var i = 0; i < count; i++) sum += Math.Abs(current[i] - previous[i]);
        return sum / (255.0 * count);
    }
}

/// <summary>
/// Collects what <see cref="LatencyProbe"/> needs while the player presses keys: how much the picture moves with each camera frame, and
/// when each note was played, both on the <see cref="System.Diagnostics.Stopwatch"/> clock. Frames come from one thread and notes
/// from another.
/// </summary>
internal sealed class LatencyRecorder
{
    /// <summary>A measurement takes about half a minute; this is more than a camera makes in ten.</summary>
    internal const int MaxSamples = 36_000, MaxNotes = 2_000;

    private readonly object _gate = new();
    private readonly double _ticksPerSecond;
    private readonly List<MotionSample> _motion = [];
    private readonly List<double> _notes = [];
    private PixelRegion _region;
    private int _width, _height;
    private byte[] _previous = [], _current = [];
    private bool _hasPrevious;
    private long _lastSequence;

    /// <param name="ticksPerSecond">Ticks of the clock the times are on (<see cref="System.Diagnostics.Stopwatch.Frequency"/>).</param>
    internal LatencyRecorder(double ticksPerSecond) => _ticksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : 1;

    /// <summary>Where in the picture movement is looked for; set again whenever the camera or the calibration changes.</summary>
    internal void SetRegion(PixelRegion region)
    {
        lock (_gate) { _region = region; _hasPrevious = false; }
    }

    internal int Frames { get { lock (_gate) return _motion.Count; } }
    internal int Notes { get { lock (_gate) return _notes.Count; } }

    /// <summary>Counts one camera frame. A frame already counted (same sequence), a frame of another size than the last, or one when there is no region says nothing.</summary>
    internal bool AddFrame(CameraFrameView frame)
    {
        lock (_gate)
        {
            if (frame.Sequence == _lastSequence || _motion.Count >= MaxSamples) return false;
            _lastSequence = frame.Sequence;
            if (frame.Width != _width || frame.Height != _height) { _width = frame.Width; _height = frame.Height; _hasPrevious = false; }
            var region = _region.Within(frame.Width, frame.Height);
            var count = MotionEnergy.SampleCount(region);
            if (count == 0) return false;
            if (_current.Length < count) { _current = new byte[count]; _previous = new byte[count]; _hasPrevious = false; }
            if (!MotionEnergy.Sample(frame.Pixels, frame.Width, frame.Height, region, _current)) { _hasPrevious = false; return false; }
            var counted = false;
            if (_hasPrevious)
            {
                _motion.Add(new MotionSample(frame.CaptureTicks / _ticksPerSecond, MotionEnergy.Between(_previous, _current, count)));
                counted = true;
            }
            (_previous, _current) = (_current, _previous);
            _hasPrevious = true;
            return counted;
        }
    }

    /// <summary>A key was pressed at this time. A key within a few milliseconds of the one before it joins it as a chord and is not counted again.</summary>
    internal void NoteOn(long ticks)
    {
        var time = ticks / _ticksPerSecond;
        // the keys of a chord are one press
        lock (_gate) if (_notes.Count < MaxNotes && (_notes.Count == 0 || time - _notes[^1] >= LatencyProbe.ChordWindow || time < _notes[^1])) _notes.Add(time);
    }

    internal LatencyEstimate Estimate()
    {
        MotionSample[] motion; double[] notes;
        lock (_gate) { motion = [.. _motion]; notes = [.. _notes]; }
        return LatencyProbe.Estimate(motion, notes);
    }

    internal void Reset()
    {
        lock (_gate) { _motion.Clear(); _notes.Clear(); _hasPrevious = false; _lastSequence = 0; }
    }
}

/// <summary>Where a delay measurement is.</summary>
internal enum MeasurementState { Idle, Collecting, Finished }

/// <summary>What a finished measurement found and what to do about it.</summary>
/// <param name="Estimate">What <see cref="LatencyProbe"/> made of it.</param>
/// <param name="AppAgeMilliseconds">How long the app itself held each picture while this was measured (0 when it was not counted).</param>
/// <param name="SuggestedMilliseconds">The camera delay to set so the overlay matches the picture on the screen: what the camera took
/// to deliver the picture plus what the app held it for, rounded; 0 unless <see cref="LatencyEstimate.IsMeasured"/>.</param>
/// <param name="Clamped">The suggestion was more than the setting can hold and was cut to its largest.</param>
internal sealed record LatencyReport(LatencyEstimate Estimate, double AppAgeMilliseconds, int SuggestedMilliseconds, bool Clamped);

/// <summary>
/// One run of "press about twenty keys while the camera watches": starts, takes the frames and the key presses, says when it has
/// enough (or has waited too long), and turns the answer into a setting. Frames arrive on the camera's thread, presses on the MIDI
/// thread and <see cref="Poll"/> comes from the interface; everything shares one clock, <see cref="System.Diagnostics.Stopwatch"/> ticks.
/// </summary>
internal sealed class LatencyMeasurement
{
    /// <summary>Presses wanted. More is surer; this is about as many as anyone will play without losing patience.</summary>
    internal const int TargetPresses = 20;
    /// <summary>It gives up after this long, with whatever it has.</summary>
    internal const double TimeoutSeconds = 90;
    /// <summary>After the last wanted press it waits this long so the picture of that press has arrived too.</summary>
    internal const double SettleSeconds = 0.6;

    private readonly object _gate = new();
    private readonly double _ticksPerSecond;
    private readonly LatencyRecorder _recorder;
    private long _startedTicks, _lastPressTicks;
    private volatile MeasurementState _state;
    private LatencyReport? _report;

    internal LatencyMeasurement(double ticksPerSecond)
    {
        _ticksPerSecond = ticksPerSecond > 0 ? ticksPerSecond : 1;
        _recorder = new LatencyRecorder(_ticksPerSecond);
    }

    internal MeasurementState State => _state;

    /// <summary>Key presses counted so far (a chord is one).</summary>
    internal int Presses => _recorder.Notes;

    /// <summary>The report of the last run that finished, until the next one starts.</summary>
    internal LatencyReport? Report { get { lock (_gate) return _report; } }

    /// <summary>Begins again, forgetting whatever came before. <paramref name="region"/> is where in the picture movement is looked for.</summary>
    internal void Start(PixelRegion region, long nowTicks)
    {
        lock (_gate)
        {
            _recorder.Reset();
            _recorder.SetRegion(region);
            _report = null;
            _startedTicks = nowTicks; _lastPressTicks = nowTicks;
            _state = MeasurementState.Collecting;
        }
    }

    /// <summary>Stops without a result.</summary>
    internal void Cancel()
    {
        lock (_gate) { _state = MeasurementState.Idle; _report = null; _recorder.Reset(); }
    }

    /// <summary>A camera frame; ignored unless a run is going.</summary>
    internal void AddFrame(CameraFrameView frame)
    {
        if (_state == MeasurementState.Collecting) _recorder.AddFrame(frame);
    }

    /// <summary>A key was pressed; ignored unless a run is going.</summary>
    internal void NoteOn(long ticks)
    {
        if (_state != MeasurementState.Collecting) return;
        _recorder.NoteOn(ticks);
        lock (_gate) _lastPressTicks = Math.Max(_lastPressTicks, ticks);
    }

    /// <summary>
    /// Call now and then while a run is going. Finishes it, and returns the report, once the wanted presses have been played and the
    /// picture has caught up with the last of them, or the time is up; null until then.
    /// </summary>
    internal LatencyReport? Poll(long nowTicks, CameraAge age)
    {
        lock (_gate)
        {
            if (_state != MeasurementState.Collecting) return null;
            var waited = (nowTicks - _startedTicks) / _ticksPerSecond;
            var settled = _recorder.Notes >= TargetPresses && (nowTicks - _lastPressTicks) / _ticksPerSecond >= SettleSeconds;
            return waited >= TimeoutSeconds || settled ? Complete(age) : null;
        }
    }

    /// <summary>Ends the run now with whatever it has (the player pressed STOP).</summary>
    internal LatencyReport? Finish(CameraAge age)
    {
        lock (_gate) return _state == MeasurementState.Collecting ? Complete(age) : null;
    }

    private LatencyReport Complete(CameraAge age)
    {
        var estimate = _recorder.Estimate();
        var held = age.Frames > 0 && double.IsFinite(age.AverageMilliseconds) ? Math.Max(0, age.AverageMilliseconds) : 0;
        var suggested = 0; var clamped = false;
        if (estimate.IsMeasured)
        {
            var wanted = (int)Math.Round(estimate.DelaySeconds * 1000 + held);
            suggested = Math.Clamp(wanted, 0, (int)ArOptions.DelayMax);
            clamped = wanted > ArOptions.DelayMax;
        }
        _state = MeasurementState.Finished;
        return _report = new LatencyReport(estimate, held, suggested, clamped);
    }
}
