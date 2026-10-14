using System.Collections.Concurrent;
using System.Diagnostics;

namespace PianoPath;

/// <summary>One note hit the render thread turns into light: burst, flash, ring and a key press.</summary>
internal readonly record struct GpuHit(int Pitch, float Strength, long Timestamp, long Generation);

/// <summary>A live key event from the player (press or release) with the moment it happened.</summary>
internal readonly record struct GpuLiveEvent(int Pitch, bool Down, float Strength, long Timestamp, long Generation);

/// <summary>What the render thread reads at the start of one frame.</summary>
internal sealed class GpuFrameInput
{
    /// <summary>Transient epoch at Capture. Manufactured diagnostic inputs start in epoch zero.</summary>
    public long Generation;
    public IReadOnlyList<NoteEvent> Notes = [];
    public double MaxNoteDuration;
    public double Position;
    public bool Playing;
    public readonly bool[] Pressed = new bool[128];
    public GpuLook Look = new();
    public float BeatPulse;
    public bool Sustain;
    /// <summary>Continuous sustain pedal level 0..1 (0 whenever <see cref="Sustain"/> is false); the AR damper strip lifts by it, so half-pedalling shows wherever the host reports it.</summary>
    public float SustainLevel;
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
    private readonly TransientFrameEpoch _epoch = new();
    private readonly object _gate;
    internal GpuStageFeed() => _gate = _epoch.Gate;
    private readonly ConcurrentQueue<GpuHit> _hits = new();
    private readonly ConcurrentQueue<GpuLiveEvent> _live = new();
    private readonly bool[] _pressed = new bool[128];
    private IReadOnlyList<NoteEvent> _notes = [];
    private double _maxNoteDuration;
    private double _position, _rate;
    private long _positionStamp;
    private bool _playing, _fixedPosition, _fixedBeatPulse;
    private bool _awaitingState; // clear cannot re-arm the old song between clear and the next owner publication
    private GpuLook _look = new();
    private float _beatPulse;
    private bool _sustain;
    private double _sustainLevel;
    private long _beatStamp;
    private float _pointerX = .5f, _pointerY = .5f;
    private double _stageHeightDip = 720;


    internal static long Now => Stopwatch.GetTimestamp();
    internal static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

    /// <summary>The look the next frame renders with; replaced whole, never mutated.</summary>
    internal GpuLook Look => Volatile.Read(ref _look);

    internal void SetLook(GpuLook look) => Volatile.Write(ref _look, look);

    private GpuBackgroundImage? _background;
    /// <summary>The decoded background picture (null for none); swapped whole, uploaded by the render thread when its version changes.</summary>
    internal GpuBackgroundImage? Background { get => Volatile.Read(ref _background); set => Volatile.Write(ref _background, value); }

    private GpuCameraInput? _camera;
    /// <summary>The camera layer (null for none); swapped whole. While it is set the render thread takes the frames from its ring.</summary>
    internal GpuCameraInput? Camera { get => Volatile.Read(ref _camera); set => Volatile.Write(ref _camera, value); }

    private GpuArInput? _ar;
    /// <summary>The AR layer (null for none); swapped whole. It draws only over a camera picture the calibration was made on.</summary>
    internal GpuArInput? Ar { get => Volatile.Read(ref _ar); set => Volatile.Write(ref _ar, value); }

    private GpuRecordingTap? _recording;
    /// <summary>The active GPU recording request (null when REC is off or records the software stage).</summary>
    internal GpuRecordingTap? Recording { get => Volatile.Read(ref _recording); set => Volatile.Write(ref _recording, value); }

    private GpuAlphaOutputTap? _arAlphaOutput;
    /// <summary>The optional combined transparent AR-effects output.</summary>
    internal GpuAlphaOutputTap? ArAlphaOutput { get => Volatile.Read(ref _arAlphaOutput); set => Volatile.Write(ref _arAlphaOutput, value); }

    private GpuAlphaLayerPairTap? _arAlphaLayersOutput;
    /// <summary>The optional geometry/bloom pair, rendered and published atomically from one scene frame.</summary>
    internal GpuAlphaLayerPairTap? ArAlphaLayersOutput { get => Volatile.Read(ref _arAlphaLayersOutput); set => Volatile.Write(ref _arAlphaLayersOutput, value); }

    private GpuBackgroundImage? _labelAtlas;
    /// <summary>The glyph atlas (note names, Matrix Rain glyphs); built once on the UI thread, uploaded once by the render thread.</summary>
    internal GpuBackgroundImage? LabelAtlas { get => Volatile.Read(ref _labelAtlas); set => Volatile.Write(ref _labelAtlas, value); }
    private GpuBackgroundImage? _watermark;
    /// <summary>The «KEYFLOW» strip for the GPU watermark (sprite kind 16); built once on the UI thread.</summary>
    internal GpuBackgroundImage? Watermark { get => Volatile.Read(ref _watermark); set => Volatile.Write(ref _watermark, value); }

    /// <summary>Sustain pedal state (drives Pedal Glow). Releasing also drops the stored continuous level.</summary>
    internal void SetSustain(bool down)
    {
        lock (_gate) { _sustain = down; if (!down) _sustainLevel = 0; }
    }

    /// <summary>Sustain pedal state together with its continuous level 0..1; the AR damper strip lifts by it, so
    /// half-pedalling shows wherever the host measures it (0..1, not a boolean; the level reads 0 when released).</summary>
    internal void SetSustainLevel(bool down, double level)
    {
        lock (_gate) { _sustain = down; _sustainLevel = down ? ArDampers.ClampLevel(level) : 0; }
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
            _awaitingState = false;
            _fixedPosition = false;
            _fixedBeatPulse = false;
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

    /// <summary>Posts an exact timeline sample for offline capture, bypassing wall-clock playback extrapolation.</summary>
    internal void SetDeterministicState(IReadOnlyList<NoteEvent> notes, double position, bool playing, IReadOnlySet<int> pressed)
    {
        SetState(notes, position, playing, pressed);
        lock (_gate)
        {
            _fixedPosition = true;
            _positionStamp = 0;
            _rate = 0;
        }
    }

    /// <summary>Sets an already-decayed beat envelope for offline capture; no Stopwatch time enters the frame.</summary>
    internal void SetDeterministicBeatPulse(double strength)
    {
        lock (_gate)
        {
            _beatPulse = Math.Clamp((float)strength, 0, 1.2f);
            _beatStamp = 0;
            _fixedBeatPulse = true;
        }
    }

    internal void Impact(int pitch, double strength) => _hits.Enqueue(new GpuHit(pitch, (float)strength, Now, _epoch.Generation));
    internal void LiveNote(int pitch, bool down, double strength) => _live.Enqueue(new GpuLiveEvent(pitch, down, (float)strength, Now, _epoch.Generation));

    internal void PulseBeat(double strength)
    {
        lock (_gate) { _fixedBeatPulse = false; _beatPulse = Math.Max(_beatPulse * BeatDecay(), (float)strength); _beatStamp = Now; }
    }

    private float BeatDecay() => _beatStamp == 0 ? 0 : (float)Math.Exp(-6 * Seconds(Now - _beatStamp));

    /// <summary>Drops transients for stop/seek/song change, not the song/pedal owners' steady state.
    /// Queue clearing precedes epoch publication: a producer tagged with the NEW epoch is never wiped.
    /// A producer delayed across the boundary may enqueue the old epoch; dequeue filters it.
    /// Song-active emission waits for the next SetState publication, avoiding the clear→seek-state gap.
    /// Post-clear Note On queues remain deliverable even while that snapshot is pending.</summary>
    internal void ClearTransient()
    {
        lock (_gate)
        {
            _hits.Clear();
            _live.Clear();
            Array.Clear(_pressed);
            _awaitingState = true;
            _epoch.Advance();
        }
    }

    /// <summary>Consume the coalesced clear flag. Old captured generations remain invalid afterward.</summary>
    internal bool TakeClearRequest() => _epoch.TakeClearRequest();

    internal bool TryDequeueHit(out GpuHit hit) => TryDequeueHit(_epoch.Generation, out hit);
    internal bool TryDequeueLive(out GpuLiveEvent live) => TryDequeueLive(_epoch.Generation, out live);

    /// <summary>The currently stepping frame may not steal events posted for a later clear epoch.
    /// Lock only the short dequeue, never physics/render work; UI producers keep lock-free pushes.</summary>
    internal bool TryDequeueHit(long generation, out GpuHit hit)
    {
        lock (_gate)
        {
            if (_epoch.IsCurrent(generation))
                while (_hits.TryDequeue(out hit))
                    if (hit.Generation == generation) return true;
            hit = default;
            return false;
        }
    }

    internal bool TryDequeueLive(long generation, out GpuLiveEvent live)
    {
        lock (_gate)
        {
            if (_epoch.IsCurrent(generation))
                while (_live.TryDequeue(out live))
                    if (live.Generation == generation) return true;
            live = default;
            return false;
        }
    }

    /// <summary>Linearise frame acceptance with clear. If clear landed after Capture, refresh the
    /// WHOLE input before simulation, including song position/playing, keys, pedal and look.
    /// A clear after this boundary is handled next frame; its new events cannot be consumed by this one.</summary>
    internal bool PrepareFrame(GpuFrameInput input)
    {
        lock (_gate)
        {
            if (!_epoch.IsCurrent(input.Generation)) CaptureLocked(input, Now);
            return _epoch.TakeClearRequest();
        }
    }

    /// <summary>Fills a frame snapshot and its epoch under the same lock as transient clear.</summary>
    internal void Capture(GpuFrameInput input, long now)
    {
        lock (_gate) CaptureLocked(input, now);
    }

    private void CaptureLocked(GpuFrameInput input, long now)
    {
        input.Generation = _epoch.Generation;
        input.Notes = _notes;
        input.MaxNoteDuration = _maxNoteDuration;
        var ahead = _fixedPosition ? 0 : Math.Clamp(Seconds(now - _positionStamp), 0, MaxExtrapolation);
        input.Position = _playing && !_fixedPosition ? _position + ahead * _rate : _position;
        input.Playing = _playing && !_awaitingState;
        Array.Copy(_pressed, input.Pressed, 128);
        input.BeatPulse = _fixedBeatPulse ? _beatPulse : _beatPulse * BeatDecay();
        input.Sustain = _sustain;
        input.SustainLevel = (float)_sustainLevel;
        input.PointerX = _pointerX; input.PointerY = _pointerY;
        input.StageHeightDip = _stageHeightDip;
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
