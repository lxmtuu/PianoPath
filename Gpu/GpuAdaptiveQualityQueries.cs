using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace PianoPath;

/// <summary>
/// Non-blocking whole-stage GPU timestamp ring. One query brackets the scene, bloom and composite draws;
/// readback, Present/VSync, sleeping and CPU frame time are outside this measurement.
/// </summary>
internal sealed class GpuAdaptiveQualityQueryProfiler : IDisposable
{
    private const int SlotCount = 16;
    // D3D11_ASYNC_GETDATA_DONOTFLUSH is native flag 0x1; never wait for a GPU result on the render thread.
    private const AsyncGetDataFlags DoNotFlush = (AsyncGetDataFlags)1;
    private readonly ID3D11DeviceContext _context;
    private readonly GpuAdaptiveQualityController _quality;
    private readonly QuerySlot?[] _slots = new QuerySlot?[SlotCount];
    private int _nextSlot;

    internal GpuAdaptiveQualityQueryProfiler(ID3D11Device device, ID3D11DeviceContext context, GpuAdaptiveQualityController quality)
    {
        _context = context;
        _quality = quality;
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

    /// <summary>Polls pending slots; an unavailable result stays pending and never stalls the immediate context.</summary>
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

            if (slot.SampleGeneration == _quality.SampleGeneration && disjoint.Disjoint == 0 && disjoint.Frequency > 0 && end >= start)
                _quality.Observe((end - start) * 1000.0 / disjoint.Frequency);
            slot.Pending = false;
        }
    }

    /// <summary>Starts one whole-stage timestamp window; returns -1 if every slot is still in flight.</summary>
    internal int BeginFrame()
    {
        for (var offset = 0; offset < _slots.Length; offset++)
        {
            var index = (_nextSlot + offset) % _slots.Length;
            var slot = _slots[index]!;
            if (slot.Pending || slot.Recording) continue;
            slot.SampleGeneration = _quality.SampleGeneration;
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
                throw;
            }
        }
        return -1;
    }

    /// <summary>Ends the timestamp window after the final composite draw.</summary>
    internal void EndFrame(int slotIndex)
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
        internal int SampleGeneration;
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
