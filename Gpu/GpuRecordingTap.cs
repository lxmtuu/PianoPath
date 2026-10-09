namespace PianoPath;

/// <summary>
/// A fixed-size GPU frame mailbox. The render thread writes into the back buffer and swaps it only after the
/// readback is complete; a UI consumer copies the newest published frame without ever stalling the GPU thread.
/// </summary>
internal class GpuFrameTap
{
    private readonly object _gate = new();
    private byte[] _front, _back;
    private long _serial;

    protected GpuFrameTap(int width, int height, int frameRate)
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
    internal byte[] BackBuffer(int width, int height) => width == Width && height == Height ? _back : throw new InvalidOperationException("The GPU frame has the wrong size.");

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

/// <summary>A GPU-rendered take at the exact size and rate requested by the recorder.</summary>
internal sealed class GpuRecordingTap(int width, int height, int frameRate) : GpuFrameTap(width, height, frameRate)
{
    /// <summary>How long the camera's pictures waited in the app before they were first drawn into this take.</summary>
    internal CameraAgeMeter CameraAge { get; } = new();
}

/// <summary>
/// A premultiplied BGRA AR-effects frame for the borderless transparent output window (e.g. OBS window capture).
/// Unlike recording frames, this mailbox contains no camera image or opaque stage background.
/// </summary>
internal sealed class GpuAlphaOutputTap(int width, int height, int frameRate) : GpuFrameTap(width, height, frameRate)
{
}
