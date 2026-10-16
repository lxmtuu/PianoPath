using System.Numerics;

namespace PianoPath;

/// <summary>
/// What the keys' glow looked like in the last moments, so the AR layer can light the keys the way they were lit a camera-delay ago.
/// The stage simulation gives the glow of every key for now (linear rgb and glow 0..1, indexed by pitch); this keeps a short
/// run of those and answers for an earlier time, blending the two pictures it falls between. Written and read by the render thread.
/// </summary>
internal sealed class ArKeyHistory
{
    /// <summary>How far back the history reaches, seconds; the camera delay never goes beyond 0.3.</summary>
    internal const double Reach = 0.75;
    /// <summary>When two neighbouring pictures are further apart than this the loop was not drawing in between (idle, paused), and the later one is used as it is.</summary>
    internal const double MaxGap = 0.2;

    private readonly Queue<(double Time, Vector4[] Keys)> _frames = new();
    private readonly Stack<Vector4[]> _spare = new();
    private readonly int _size;
    private long? _generation;
    private double _newest = double.NegativeInfinity;

    internal ArKeyHistory(int keyCount = 128) => _size = keyCount;

    /// <summary>How many pictures are held.</summary>
    internal int Count => _frames.Count;

    /// <summary>Remembers glow at a steady time. A clock rewind OR a new transient epoch starts
    /// a fresh history, so camera delay cannot sample key light from before stop/seek/clear.</summary>
    internal void Record(double time, IReadOnlyList<Vector4> keys, long generation = 0)
    {
        if (!double.IsFinite(time)) return;
        if (time < _newest || _generation != generation) Clear();
        _generation = generation;
        while (_frames.Count > 0 && time - _frames.Peek().Time > Reach) _spare.Push(_frames.Dequeue().Keys);
        var copy = _spare.Count > 0 ? _spare.Pop() : new Vector4[_size];
        for (var i = 0; i < _size; i++) copy[i] = i < keys.Count ? keys[i] : Vector4.Zero;
        _frames.Enqueue((time, copy));
        _newest = time;
    }

    internal void Clear()
    {
        while (_frames.Count > 0) _spare.Push(_frames.Dequeue().Keys);
        _newest = double.NegativeInfinity;
        _generation = null;
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the glow at <paramref name="time"/>. Before the first picture nothing was lit (what happened
    /// then is not known and a key left glowing would be worse than one left dark); after the last, the last. In between the glow is
    /// blended, and the colour is taken from whichever side has the key lit, so a key coming on does not start from a made-up colour.
    /// </summary>
    internal void Sample(double time, Vector4[] into)
    {
        Array.Clear(into, 0, Math.Min(into.Length, _size));
        if (_frames.Count == 0 || !double.IsFinite(time)) return;
        (double Time, Vector4[] Keys)? before = null;
        foreach (var frame in _frames)
        {
            if (frame.Time <= time) { before = frame; continue; }
            // the first picture after `time`
            if (before is not { } earlier) return;                               // time is before everything held: dark
            var span = frame.Time - earlier.Time;
            var t = time <= earlier.Time ? 0.0 : span > MaxGap ? 1.0 : (time - earlier.Time) / span;      // exactly at a picture: that picture
            for (var i = 0; i < _size && i < into.Length; i++) into[i] = Blend(earlier.Keys[i], frame.Keys[i], (float)t);
            return;
        }
        var last = before!.Value.Keys;                                           // time is at or after the newest picture
        for (var i = 0; i < _size && i < into.Length; i++) into[i] = last[i];
    }

    private static Vector4 Blend(Vector4 a, Vector4 b, float t)
    {
        var glow = a.W + (b.W - a.W) * t;
        var rgb = a.W <= 0 ? new Vector3(b.X, b.Y, b.Z) : b.W <= 0 ? new Vector3(a.X, a.Y, a.Z)
            : Vector3.Lerp(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z), t);
        return new Vector4(rgb, glow);
    }
}
