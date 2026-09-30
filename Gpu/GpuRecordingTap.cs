namespace PianoPath;

/// <summary>
/// A recording request handed to the GPU render thread: while one is set on
/// <see cref="GpuStageFeed.Recording"/>, the loop renders an extra off-screen frame at exactly the
/// recording size and rate (not a scaled copy of the preview), reads it back through its own staging
/// ring and publishes it here. The REC session on the UI thread copies the newest frame whenever the
/// recording clock asks for one, so a busy UI never slows the renderer and a slow disk never stalls it.
/// </summary>
internal sealed class GpuRecordingTap
{
    private readonly object _gate = new();
    private byte[] _front, _back;
    private long _serial;

    internal GpuRecordingTap(int width, int height, int frameRate)
    {
        Width = Math.Clamp(width, 16, 7680);
        Height = Math.Clamp(height, 16, 4320);
        FrameRate = Math.Clamp(frameRate, 1, 240);
        _front = new byte[Width * Height * 4];
        _back = new byte[Width * Height * 4];
    }

    internal int Width { get; }
    internal int Height { get; }
    internal int FrameRate { get; }

    /// <summary>Number of frames published so far; 0 until the first one is read back.</summary>
    internal long Serial => Interlocked.Read(ref _serial);

    /// <summary>Render thread: the buffer the next frame is read back into (BGRA, tightly packed).</summary>
    internal byte[] BackBuffer(int width, int height) => width == Width && height == Height ? _back : throw new InvalidOperationException("The GPU recording frame has the wrong size.");

    /// <summary>Render thread: publishes the back buffer as the newest frame.</summary>
    internal void Publish()
    {
        lock (_gate)
        {
            (_front, _back) = (_back, _front);
            Interlocked.Increment(ref _serial);
        }
    }

    /// <summary>UI thread: runs <paramref name="copy"/> over the newest frame; false until one exists.</summary>
    internal bool TryRead(Action<byte[]> copy)
    {
        lock (_gate)
        {
            if (Interlocked.Read(ref _serial) == 0) return false;
            copy(_front);
            return true;
        }
    }
}
