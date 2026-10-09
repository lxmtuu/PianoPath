namespace PianoPath;

/// <summary>A growable array of instances that is refilled every frame without allocating.</summary>
internal sealed class GpuInstanceList<T> where T : struct
{
    private T[] _items;
    internal GpuInstanceList(int capacity) => _items = new T[capacity];
    internal int Count { get; private set; }
    internal void Clear() => Count = 0;
    internal void Add(in T item)
    {
        if (Count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
        _items[Count++] = item;
    }

    /// <summary>Deterministically thins one contiguous range and compacts the following instances without touching them.</summary>
    internal void RetainFraction(int start, int length, float fraction)
    {
        start = Math.Clamp(start, 0, Count);
        length = Math.Clamp(length, 0, Count - start);
        fraction = Math.Clamp(fraction, 0, 1);
        var retained = Math.Clamp((int)MathF.Round(length * fraction, MidpointRounding.AwayFromZero), 0, length);
        if (retained >= length) return;

        if (retained > 0)
        {
            for (var i = 0; i < retained; i++)
            {
                var sourceOffset = (int)(((long)(2 * i + 1) * length) / (2L * retained));
                _items[start + i] = _items[start + Math.Min(length - 1, sourceOffset)];
            }
        }

        var tailStart = start + length;
        var tailLength = Count - tailStart;
        Array.Copy(_items, tailStart, _items, start + retained, tailLength);
        Count -= length - retained;
    }

    internal ReadOnlySpan<T> Span => _items.AsSpan(0, Count);

    /// <summary>The same instances, writable: for a pass that rewrites them in place (AR-S4 cuts notes to the picture) before they are uploaded.</summary>
    internal Span<T> Writable => _items.AsSpan(0, Count);

    /// <summary>Keeps the first <paramref name="count"/> instances and drops the rest; the in-place counterpart of <see cref="Writable"/>.</summary>
    internal void Keep(int count) => Count = Math.Clamp(count, 0, Count);
}
