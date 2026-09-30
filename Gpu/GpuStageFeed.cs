using System.Collections.Concurrent;
using System.Diagnostics;

namespace PianoPath;

/// <summary>One note hit the render thread turns into light: burst, flash, ring and a key press.</summary>
internal readonly record struct GpuHit(int Pitch, float Strength, long Timestamp);

/// <summary>A live key event from the player (press or release) with the moment it happened.</summary>
internal readonly record struct GpuLiveEvent(int Pitch, bool Down, float Strength, long Timestamp);

/// <summary>What the render thread reads at the start of one frame.</summary>
internal sealed class GpuFrameInput
{
    public IReadOnlyList<NoteEvent> Notes = [];
    public double MaxNoteDuration;
    public double Position;
    public bool Playing;
    public readonly bool[] Pressed = new bool[128];
    public GpuLook Look = new();
    public float BeatPulse;
    public bool Sustain;
    public float PointerX = .5f, PointerY = .5f;
    public double StageHeightDip = 720;
}

/// <summary>
/// The only bridge between the WPF thread and the Direct3D render thread.
/// </summary>
/// <remarks>
/// <para>The UI thread <em>posts</em> (song position, pressed keys, hits, live notes, the look) and never
/// waits: every call is a short lock or a lock-free queue push, so a slow or stalled GPU can never hold
/// up MIDI input, audio scheduling or the dispatcher. The render thread <em>pulls</em> a consistent
/// snapshot once per frame (<see cref="Capture"/>).</para>
/// <para>Between two UI updates the render thread extrapolates the playhead from the last position,
/// its timestamp and the measured playback rate, so a 144 Hz or 240 Hz output scrolls smoothly even
/// though the song clock only advances at the WPF composition rate. The extrapolation is capped at
/// 50 ms: a stall, a seek or a pause cannot make the roll run away.</para>
/// </remarks>
internal sealed class GpuStageFeed
{
    private const double MaxExtrapolation = .05;
    private readonly object _gate = new();
    private readonly ConcurrentQueue<GpuHit> _hits = new();
    private readonly ConcurrentQueue<GpuLiveEvent> _live = new();
    private readonly bool[] _pressed = new bool[128];
    private IReadOnlyList<NoteEvent> _notes = [];
    private double _maxNoteDuration;
    private double _position, _rate;
    private long _positionStamp;
    private bool _playing;
    private GpuLook _look = new();
    private float _beatPulse;
    private bool _sustain;
    private long _beatStamp;
    private float _pointerX = .5f, _pointerY = .5f;
    private double _stageHeightDip = 720;
    private int _clearRequests;

    internal static long Now => Stopwatch.GetTimestamp();
    internal static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

    /// <summary>The look the next frame renders with; replaced whole, never mutated.</summary>
    internal GpuLook Look => Volatile.Read(ref _look);

    internal void SetLook(GpuLook look) => Volatile.Write(ref _look, look);

    private GpuBackgroundImage? _background;
    /// <summary>The decoded background picture (null for none); swapped whole, uploaded by the render thread when its version changes.</summary>
    internal GpuBackgroundImage? Background { get => Volatile.Read(ref _background); set => Volatile.Write(ref _background, value); }

    private GpuRecordingTap? _recording;
    /// <summary>The active GPU recording request (null when REC is off or records the software stage).</summary>
    internal GpuRecordingTap? Recording { get => Volatile.Read(ref _recording); set => Volatile.Write(ref _recording, value); }

    private GpuBackgroundImage? _labelAtlas;
    /// <summary>The glyph atlas (note names, Matrix Rain glyphs); built once on the UI thread, uploaded once by the render thread.</summary>
    internal GpuBackgroundImage? LabelAtlas { get => Volatile.Read(ref _labelAtlas); set => Volatile.Write(ref _labelAtlas, value); }

    /// <summary>Sustain pedal state (drives Pedal Glow).</summary>
    internal void SetSustain(bool down)
    {
        lock (_gate) _sustain = down;
    }

    internal void SetStageHeight(double dips)
    {
        if (dips < 1) return;
        lock (_gate) _stageHeightDip = dips;
    }

    internal void SetPointer(double x, double y)
    {
        lock (_gate) { _pointerX = (float)x; _pointerY = (float)y; }
    }

    /// <summary>Song state from <see cref="PianoStage.SetState"/>.</summary>
    internal void SetState(IReadOnlyList<NoteEvent> notes, double position, bool playing, IReadOnlySet<int> pressed)
    {
        var now = Now;
        lock (_gate)
        {
            if (!ReferenceEquals(notes, _notes))
            {
                _notes = notes;
                var longest = 0.0;
                foreach (var note in notes) if (note.Duration > longest) longest = note.Duration;
                _maxNoteDuration = longest;
                _rate = 0;
            }
            else if (playing && _playing && _positionStamp != 0)
            {
                var dt = Seconds(now - _positionStamp);
                if (dt > .002)
                {
                    var measured = (position - _position) / dt;
                    // A seek, a loop jump or a stall is not a rate: keep the previous estimate.
                    if (measured >= 0 && measured <= 4) _rate += (measured - _rate) * .35;
                }
            }
            if (!playing) _rate = 0;
            _position = position; _positionStamp = now; _playing = playing;
            Array.Clear(_pressed);
            foreach (var pitch in pressed) if (pitch is >= 0 and < 128) _pressed[pitch] = true;
        }
    }

    internal void Impact(int pitch, double strength) => _hits.Enqueue(new GpuHit(pitch, (float)strength, Now));
    internal void LiveNote(int pitch, bool down, double strength) => _live.Enqueue(new GpuLiveEvent(pitch, down, (float)strength, Now));

    internal void PulseBeat(double strength)
    {
        lock (_gate) { _beatPulse = Math.Max(_beatPulse * BeatDecay(), (float)strength); _beatStamp = Now; }
    }

    private float BeatDecay() => _beatStamp == 0 ? 0 : (float)Math.Exp(-6 * Seconds(Now - _beatStamp));

    /// <summary>Drops every transient effect (stop, seek, song change).</summary>
    internal void ClearTransient()
    {
        Interlocked.Increment(ref _clearRequests);
        lock (_gate) Array.Clear(_pressed);
    }

    /// <summary>True once per <see cref="ClearTransient"/> call; read by the render thread.</summary>
    internal bool TakeClearRequest()
    {
        if (Volatile.Read(ref _clearRequests) == 0) return false;
        Interlocked.Exchange(ref _clearRequests, 0);
        return true;
    }

    internal bool TryDequeueHit(out GpuHit hit) => _hits.TryDequeue(out hit);
    internal bool TryDequeueLive(out GpuLiveEvent live) => _live.TryDequeue(out live);

    /// <summary>Fills <paramref name="input"/> with the state for a frame presented at <paramref name="now"/>.</summary>
    internal void Capture(GpuFrameInput input, long now)
    {
        lock (_gate)
        {
            input.Notes = _notes;
            input.MaxNoteDuration = _maxNoteDuration;
            var ahead = Math.Clamp(Seconds(now - _positionStamp), 0, MaxExtrapolation);
            input.Position = _playing ? _position + ahead * _rate : _position;
            input.Playing = _playing;
            Array.Copy(_pressed, input.Pressed, 128);
            input.BeatPulse = _beatPulse * BeatDecay();
            input.Sustain = _sustain;
            input.PointerX = _pointerX; input.PointerY = _pointerY;
            input.StageHeightDip = _stageHeightDip;
        }
        input.Look = Look;
    }

    // ------------------------------------------------------------------------------------------
    // Frames coming back from the render thread (the embedded presenter)
    // ------------------------------------------------------------------------------------------

    private readonly object _frameGate = new();
    private byte[] _front = [], _back = [];
    private int _frontWidth, _frontHeight, _backWidth, _backHeight;
    private long _frameSerial;

    /// <summary>Monotonic number of the newest finished frame; the UI copies a frame only when it changes.</summary>
    internal long FrameSerial => Interlocked.Read(ref _frameSerial);

    /// <summary>Render thread: a buffer to write the next frame into (BGRA, tightly packed).</summary>
    internal byte[] BackBuffer(int width, int height)
    {
        var size = width * height * 4;
        if (_back.Length != size) _back = new byte[size];
        _backWidth = width; _backHeight = height;
        return _back;
    }

    /// <summary>Render thread: publishes the back buffer as the newest frame.</summary>
    internal void PublishBackBuffer()
    {
        lock (_frameGate)
        {
            (_front, _back) = (_back, _front);
            (_frontWidth, _backWidth) = (_backWidth, _frontWidth);
            (_frontHeight, _backHeight) = (_backHeight, _frontHeight);
            Interlocked.Increment(ref _frameSerial);
        }
    }

    /// <summary>UI thread: runs <paramref name="copy"/> over the newest frame while the render thread cannot swap it.</summary>
    internal bool ReadFrontBuffer(Action<byte[], int, int> copy)
    {
        lock (_frameGate)
        {
            if (_frontWidth <= 0 || _frontHeight <= 0 || _front.Length < _frontWidth * _frontHeight * 4) return false;
            copy(_front, _frontWidth, _frontHeight);
            return true;
        }
    }
}
