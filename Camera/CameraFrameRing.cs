namespace PianoPath;

/// <summary>One frame handed out by <see cref="CameraFrameRing"/>.</summary>
/// <param name="Pixels">Top-down BGRA, at least <c>Width × Height × 4</c> bytes; the taker owns it until the next take.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Sequence">1 for the first frame published, then 2, 3… so a gap shows how many frames were skipped.</param>
/// <param name="CaptureTicks"><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> at the moment the frame arrived from the camera.</param>
internal readonly record struct CameraFrameView(byte[] Pixels, int Width, int Height, long Sequence, long CaptureTicks);

/// <summary>
/// Three frame buffers passed from the thread that reads the camera to the thread that draws, with no copy and a
/// lock held only as long as it takes to swap a number.
///
/// <para>
/// The reader fills a buffer nobody else holds, then publishes it as the newest; the drawing thread takes the
/// newest and keeps it until it takes again. With three buffers the reader always has one that is neither the
/// newest nor the one being drawn, so it never overwrites a picture that is being looked at — the hole the single
/// shared buffer had — and a slow drawing thread only means frames are skipped, never torn.
/// </para>
/// </summary>
internal sealed class CameraFrameRing
{
    private const int Slots = 3;

    private readonly Lock _gate = new();
    private readonly byte[][] _buffers = [[], [], []];
    private readonly int[] _widths = new int[Slots], _heights = new int[Slots];
    private readonly long[] _sequences = new long[Slots], _ticks = new long[Slots];
    private int _newest = -1, _held = -1, _writing = -1;
    private long _published, _taken;

    /// <summary>Frames published so far.</summary>
    internal long Published { get { lock (_gate) return _published; } }

    /// <summary>
    /// Hands the reader a buffer of at least <paramref name="size"/> bytes that is neither the newest frame nor the one
    /// the drawing thread holds. Pass it back to <see cref="Publish"/> when it is filled.
    /// </summary>
    internal byte[] BeginWrite(int size, out int slot)
    {
        lock (_gate)
        {
            slot = 0;
            while (slot == _newest || slot == _held) slot++;
            _writing = slot;
            if (_buffers[slot].Length < size) _buffers[slot] = new byte[size];
            return _buffers[slot];
        }
    }

    /// <summary>Makes the buffer from <see cref="BeginWrite"/> the newest frame.</summary>
    internal void Publish(int slot, int width, int height, long captureTicks)
    {
        lock (_gate)
        {
            if (slot != _writing) return;
            _writing = -1;
            _widths[slot] = width; _heights[slot] = height; _ticks[slot] = captureTicks;
            _sequences[slot] = ++_published;
            _newest = slot;
        }
    }

    /// <summary>
    /// Takes the newest frame if it is one this has not handed out before. The frame stays the caller's — and safe to
    /// change, as the green key does — until the next call to this method.
    /// </summary>
    internal bool TryTake(out CameraFrameView frame)
    {
        lock (_gate)
        {
            if (_newest < 0 || _sequences[_newest] <= _taken) { frame = default; return false; }
            _held = _newest;
            _taken = _sequences[_held];
            frame = new CameraFrameView(_buffers[_held], _widths[_held], _heights[_held], _sequences[_held], _ticks[_held]);
            return true;
        }
    }

    /// <summary>Forgets every frame, so the next <see cref="TryTake"/> has nothing until something new is published.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _newest = -1; _held = -1; _writing = -1;
            _taken = _published;
        }
    }
}
