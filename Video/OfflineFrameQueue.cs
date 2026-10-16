using System.Diagnostics;

namespace PianoPath;

/// <summary>Bounded backpressure for offline exports. Caller owns gate; worker pulses it on dequeue/error/close.
/// Live recording may coalesce; an offline sweep must retain every input image, not just its timeline slot.</summary>
internal static class OfflineFrameQueue
{
    internal static void WaitForSpace(object gate, Func<int> count, int capacity, Action ensureWritable,
        TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        var elapsed = Stopwatch.StartNew();
        ensureWritable();
        while (count() >= capacity)
        {
            var remaining = limit - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException("The offline video encoder did not free its frame queue within the time limit.");
            Monitor.Wait(gate, remaining);
            ensureWritable();
        }
    }
}
