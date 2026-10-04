using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PianoPath;

/// <summary>
/// Moves recording writes off the real-time audio callback. Samples are copied into pooled blocks and a
/// background consumer calls the writer; a bounded backlog means a slow disk/encoder can lose recording
/// frames, but it can never stall live piano playback.
/// </summary>
internal sealed class AudioTapDispatcher : IDisposable
{
    private const int MaxQueuedBlocks = 512;
    private readonly ConcurrentQueue<TapBlock> _queue = new();
    private readonly AutoResetEvent _available = new(false);
    private readonly ManualResetEventSlim _drained = new(true);
    private readonly Thread _worker;
    private Action<short[], int>? _receiver;
    private int _queuedBlocks, _activeProducers, _disposed;
    private long _droppedFrames;

    private readonly record struct TapBlock(short[] Samples, int Count, Action<short[], int> Receiver);

    internal AudioTapDispatcher()
    {
        _worker = new Thread(Pump) { IsBackground = true, Name = "Keyflow recording audio writer", Priority = ThreadPriority.Normal };
        _worker.Start();
    }

    internal bool HasTap => Volatile.Read(ref _receiver) is not null;
    internal long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    internal void SetTap(Action<short[], int>? receiver)
    {
        if (Volatile.Read(ref _disposed) != 0 && receiver is not null) return;
        Volatile.Write(ref _receiver, receiver);
    }

    /// <summary>Copies one rendered block without waiting for the recording writer or allocating a sample array.</summary>
    internal bool Enqueue(short[] samples, int count)
    {
        if (samples is null || count <= 0 || Volatile.Read(ref _disposed) != 0) return false;
        var producerCount = Interlocked.Increment(ref _activeProducers);
        // Pair the initial fast-path check with a second check after registration. Dispose can then either
        // see us in Flush(), or we return without touching wait handles it may already have closed.
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Decrement(ref _activeProducers);
            return false;
        }
        if (producerCount == 1) _drained.Reset();
        var counted = false;
        short[]? copy = null;
        try
        {
            var receiver = Volatile.Read(ref _receiver);
            if (receiver is null || Volatile.Read(ref _disposed) != 0) return false;

            count = Math.Min(count, samples.Length);
            count -= count & 1; // the tap is always interleaved stereo PCM
            if (count == 0) return false;

            var queued = Interlocked.Increment(ref _queuedBlocks);
            counted = true;
            if (queued > MaxQueuedBlocks)
            {
                Interlocked.Decrement(ref _queuedBlocks);
                counted = false;
                Interlocked.Add(ref _droppedFrames, count / 2);
                return false;
            }

            copy = ArrayPool<short>.Shared.Rent(count);
            Array.Copy(samples, copy, count);
            _queue.Enqueue(new TapBlock(copy, count, receiver));
            copy = null; // ownership passed to the worker
            _available.Set();
            return true;
        }
        catch
        {
            if (counted) Interlocked.Decrement(ref _queuedBlocks);
            if (count > 0) Interlocked.Add(ref _droppedFrames, count / 2);
            return false;
        }
        finally
        {
            if (copy is not null) ArrayPool<short>.Shared.Return(copy);
            if (Interlocked.Decrement(ref _activeProducers) == 0 && Volatile.Read(ref _queuedBlocks) == 0)
                _drained.Set();
        }
    }

    /// <summary>Waits until queued samples and writers that entered before a detach are finished.</summary>
    internal bool Flush(TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (Volatile.Read(ref _queuedBlocks) != 0 || Volatile.Read(ref _activeProducers) != 0)
        {
            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero || !_drained.Wait(remaining)) return false;
        }
        return true;
    }

    private void Pump()
    {
        while (Volatile.Read(ref _disposed) == 0 || !_queue.IsEmpty || Volatile.Read(ref _activeProducers) != 0)
        {
            if (!_queue.TryDequeue(out var block))
            {
                _available.WaitOne(100);
                continue;
            }

            try { block.Receiver(block.Samples, block.Count); }
            catch { Interlocked.Add(ref _droppedFrames, block.Count / 2); }
            finally
            {
                ArrayPool<short>.Shared.Return(block.Samples);
                if (Interlocked.Decrement(ref _queuedBlocks) == 0 && Volatile.Read(ref _activeProducers) == 0)
                    _drained.Set();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _receiver, null);
        _available.Set();
        Flush(TimeSpan.FromSeconds(10));
        _available.Set();
        if (_worker.Join(TimeSpan.FromSeconds(2)))
        {
            _available.Dispose();
            _drained.Dispose();
        }
        // A wedged external encoder must not make app shutdown wait forever. In that unusual case leave the
        // two tiny wait handles alive with the background worker rather than dispose objects it may still use.
    }
}
