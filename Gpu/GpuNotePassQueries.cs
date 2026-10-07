using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace PianoPath;

/// <summary>
/// A diagnostic-only hand-off from the benchmark thread to the D3D11 render thread. The query objects and
/// immediate context stay on the render thread; this object only publishes the current run's raw samples.
/// </summary>
internal sealed class GpuNotePassSampleBuffer
{
    private GpuNotePassSampleRun? _current;
    private string? _initializationError;

    internal string? InitializationError => Volatile.Read(ref _initializationError);
    internal GpuNotePassSampleRun? Current => Volatile.Read(ref _current);

    internal GpuNotePassSampleRun Start(int requestedSamples)
    {
        if (requestedSamples <= 0) throw new ArgumentOutOfRangeException(nameof(requestedSamples));
        if (Current is { IsComplete: false }) throw new InvalidOperationException("The previous note-pass query batch has not drained yet.");
        var run = new GpuNotePassSampleRun(requestedSamples);
        Volatile.Write(ref _current, run);
        return run;
    }

    internal void SetInitializationError(string message) =>
        Interlocked.CompareExchange(ref _initializationError, message, null);
}

/// <summary>One run of asynchronous note-draw timestamp queries.</summary>
internal sealed class GpuNotePassSampleRun(int requestedSamples)
{
    private readonly GpuNotePassSample[] _samples = new GpuNotePassSample[requestedSamples];
    private int _issued, _pending, _valid, _disjoint, _busyFrames, _queryErrors;

    internal int RequestedSamples { get; } = requestedSamples;
    internal int Issued => Volatile.Read(ref _issued);
    internal int Pending => Volatile.Read(ref _pending);
    internal int ValidCount => Volatile.Read(ref _valid);
    internal int DisjointCount => Volatile.Read(ref _disjoint);
    internal int BusyFrames => Volatile.Read(ref _busyFrames);
    internal int QueryErrors => Volatile.Read(ref _queryErrors);
    internal bool IsComplete => Issued >= RequestedSamples && Pending == 0;

    /// <summary>Called only after a free query slot has been selected.</summary>
    internal bool TryReserve()
    {
        if (Issued >= RequestedSamples) return false;
        Interlocked.Increment(ref _issued);
        Interlocked.Increment(ref _pending);
        return true;
    }

    internal void RecordBusyFrame() => Interlocked.Increment(ref _busyFrames);

    internal void CompleteValid(GpuNotePassSample sample)
    {
        var index = Volatile.Read(ref _valid);
        if ((uint)index < (uint)_samples.Length)
        {
            _samples[index] = sample;
            Volatile.Write(ref _valid, index + 1);
        }
        Interlocked.Decrement(ref _pending);
    }

    internal void CompleteDisjoint()
    {
        Interlocked.Increment(ref _disjoint);
        Interlocked.Decrement(ref _pending);
    }

    internal void CompleteQueryError()
    {
        Interlocked.Increment(ref _queryErrors);
        Interlocked.Decrement(ref _pending);
    }

    internal GpuNotePassSample[] SnapshotSamples()
    {
        var count = Math.Clamp(Volatile.Read(ref _valid), 0, _samples.Length);
        return _samples.AsSpan(0, count).ToArray();
    }
}

/// <summary>A GPU timestamp-query result for one note DrawInstanced call.</summary>
internal readonly record struct GpuNotePassSample(
    double Milliseconds,
    int DrawInstances,
    ulong TimestampStart,
    ulong TimestampEnd,
    ulong FrequencyHz);

/// <summary>
/// D3D11 timestamp-query ring used only by the explicit material benchmark. Each disjoint query brackets one
/// note draw, and results are polled with DONOTFLUSH; an unready slot is deferred rather than waited on.
/// </summary>
internal sealed class GpuNotePassQueryProfiler : IDisposable
{
    private const int SlotCount = 8;
    // D3D11_ASYNC_GETDATA_DONOTFLUSH is native flag 0x1; keep polling non-blocking and do not rely on the generated enum member spelling.
    private const AsyncGetDataFlags DoNotFlush = (AsyncGetDataFlags)1;
    private readonly ID3D11DeviceContext _context;
    private readonly GpuNotePassSampleBuffer _samples;
    private readonly QuerySlot?[] _slots = new QuerySlot?[SlotCount];
    private int _nextSlot;

    internal GpuNotePassQueryProfiler(ID3D11Device device, ID3D11DeviceContext context, GpuNotePassSampleBuffer samples)
    {
        _context = context;
        _samples = samples;
        try
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = new QuerySlot();
                _slots[i] = slot;
                slot.Disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
                slot.Start = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
                slot.End = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Poll once per rendered frame; returns immediately if the GPU has not completed a slot yet.</summary>
    internal void CollectReady()
    {
        foreach (var slot in _slots)
        {
            if (slot is not { Pending: true } || slot.Disjoint is null || slot.Start is null || slot.End is null) continue;
            if (!_context.IsDataAvailable(slot.Disjoint, DoNotFlush)) continue;
            if (!_context.GetData(slot.Disjoint, DoNotFlush, out TimestampDisjointData disjoint)
                || !_context.GetData(slot.Start, DoNotFlush, out ulong start)
                || !_context.GetData(slot.End, DoNotFlush, out ulong end))
                continue;

            var run = slot.Run;
            if (run is not null)
            {
                if (disjoint.Disjoint != 0 || disjoint.Frequency == 0 || end < start)
                    run.CompleteDisjoint();
                else
                    run.CompleteValid(new GpuNotePassSample(
                        (end - start) * 1000.0 / disjoint.Frequency,
                        slot.DrawInstances, start, end, disjoint.Frequency));
            }
            slot.Pending = false;
            slot.Run = null;
        }
    }

    /// <summary>Starts a timestamp window immediately before the note draw. Returns -1 when no sample is requested.</summary>
    internal int BeginDraw(int drawInstances)
    {
        var run = _samples.Current;
        if (run is null || run.Issued >= run.RequestedSamples) return -1;

        for (var offset = 0; offset < _slots.Length; offset++)
        {
            var index = (_nextSlot + offset) % _slots.Length;
            var slot = _slots[index]!;
            if (slot.Pending || slot.Recording) continue;
            if (!run.TryReserve()) return -1;
            slot.Run = run;
            slot.DrawInstances = drawInstances;
            slot.Recording = true;
            try
            {
                _context.Begin(slot.Disjoint!);
                _context.End(slot.Start!);
                _nextSlot = (index + 1) % _slots.Length;
                return index;
            }
            catch
            {
                slot.Recording = false;
                slot.Run = null;
                run.CompleteQueryError();
                throw;
            }
        }

        run.RecordBusyFrame();
        return -1;
    }

    /// <summary>Ends the timestamp window immediately after the note draw.</summary>
    internal void EndDraw(int slotIndex)
    {
        if ((uint)slotIndex >= (uint)_slots.Length) return;
        var slot = _slots[slotIndex]!;
        if (!slot.Recording) return;
        _context.End(slot.End!);
        _context.End(slot.Disjoint!);
        slot.Recording = false;
        slot.Pending = true;
    }

    public void Dispose()
    {
        foreach (var slot in _slots)
        {
            slot?.Disjoint?.Dispose();
            slot?.Start?.Dispose();
            slot?.End?.Dispose();
        }
        Array.Clear(_slots, 0, _slots.Length);
    }

    private sealed class QuerySlot
    {
        internal ID3D11Query? Disjoint;
        internal ID3D11Query? Start;
        internal ID3D11Query? End;
        internal GpuNotePassSampleRun? Run;
        internal int DrawInstances;
        internal bool Recording;
        internal bool Pending;
    }

    // D3D11_QUERY_DATA_TIMESTAMP_DISJOINT is UINT64 + Win32 BOOL (a 32-bit int), padded to 16 bytes.
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
    private struct TimestampDisjointData
    {
        internal ulong Frequency;
        internal int Disjoint;
    }
}
