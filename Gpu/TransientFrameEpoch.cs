namespace PianoPath;

/// <summary>Clear/seek boundary shared by feed snapshots and event queues. Generation reads are
/// lock-free; composite snapshot/clear decisions use Gate (the feed's existing short state lock).
/// This is a soft-clear epoch, not a session clock/RNG reset or an audio-sounding model.</summary>
internal sealed class TransientFrameEpoch
{
    internal object Gate { get; } = new();
    private long _generation;
    private bool _pendingClear;
    internal long Generation => Volatile.Read(ref _generation);
    internal bool IsCurrent(long stamp) => stamp == Generation;

    internal long Advance()
    {
        lock (Gate)
        {
            var next = Interlocked.Increment(ref _generation);
            _pendingClear = true;
            return next;
        }
    }

    /// <summary>Coalesce clears before a render frame. Consuming the flag does not make old stamps valid.</summary>
    internal bool TakeClearRequest()
    {
        lock (Gate)
        {
            var pending = _pendingClear;
            _pendingClear = false;
            return pending;
        }
    }
}
