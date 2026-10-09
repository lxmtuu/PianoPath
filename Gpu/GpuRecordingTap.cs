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
    internal bool TryRead(Action<byte[]> copy) => TryRead(copy, out _);

    /// <summary>Copies one complete published frame and returns its serial under the same lock.</summary>
    internal bool TryRead(Action<byte[]> copy, out long serial)
    {
        lock (_gate)
        {
            serial = Interlocked.Read(ref _serial);
            if (serial == 0) return false;
            copy(_front);
            return true;
        }
    }
}

internal enum GpuAlphaLayerKind : byte { Geometry, Glow }

/// <summary>
/// One atomically published geometry/glow frame pair. Both windows read the same shared serial, and the renderer
/// swaps both front buffers together only after both GPU readbacks for one frame are complete.
/// </summary>
internal sealed class GpuAlphaLayerPairTap
{
    private readonly object _gate = new();
    private byte[] _geometryFront, _geometryBack, _glowFront, _glowBack;
    private long _serial;

    internal GpuAlphaLayerPairTap(int width, int height, int frameRate)
    {
        Width = Math.Clamp(width, 16, 7680);
        Height = Math.Clamp(height, 16, 4320);
        FrameRate = Math.Clamp(frameRate, 1, 240);
        var bytes = checked(Width * Height * 4);
        _geometryFront = new byte[bytes]; _geometryBack = new byte[bytes];
        _glowFront = new byte[bytes]; _glowBack = new byte[bytes];
        Geometry = new GpuAlphaLayerOutputTap(this, GpuAlphaLayerKind.Geometry);
        Glow = new GpuAlphaLayerOutputTap(this, GpuAlphaLayerKind.Glow);
    }

    internal int Width { get; }
    internal int Height { get; }
    internal int FrameRate { get; }
    internal long Serial => Interlocked.Read(ref _serial);
    internal GpuAlphaLayerOutputTap Geometry { get; }
    internal GpuAlphaLayerOutputTap Glow { get; }

    internal byte[] GeometryBackBuffer(int width, int height) =>
        width == Width && height == Height ? _geometryBack : throw new InvalidOperationException("The GPU geometry frame has the wrong size.");

    internal byte[] GlowBackBuffer(int width, int height) =>
        width == Width && height == Height ? _glowBack : throw new InvalidOperationException("The GPU glow frame has the wrong size.");

    internal void Publish()
    {
        lock (_gate)
        {
            (_geometryFront, _geometryBack) = (_geometryBack, _geometryFront);
            (_glowFront, _glowBack) = (_glowBack, _glowFront);
            Interlocked.Increment(ref _serial);
        }
    }

    internal bool TryRead(GpuAlphaLayerKind layer, Action<byte[]> copy, out long serial)
    {
        lock (_gate)
        {
            serial = Interlocked.Read(ref _serial);
            if (serial == 0) return false;
            copy(layer == GpuAlphaLayerKind.Geometry ? _geometryFront : _glowFront);
            return true;
        }
    }

    /// <summary>Copies both layers under one lock, so their WPF windows can refresh from the same frame serial.</summary>
    internal bool TryReadPair(Action<byte[]>? copyGeometry, Action<byte[]>? copyGlow, out long serial)
    {
        lock (_gate)
        {
            serial = Interlocked.Read(ref _serial);
            if (serial == 0) return false;
            copyGeometry?.Invoke(_geometryFront);
            copyGlow?.Invoke(_glowFront);
            return true;
        }
    }
}

/// <summary>A window-facing view of one channel from a shared, atomically published geometry/glow pair.</summary>
internal sealed class GpuAlphaLayerOutputTap(GpuAlphaLayerPairTap pair, GpuAlphaLayerKind layer)
{
    internal int Width => pair.Width;
    internal int Height => pair.Height;
    internal int FrameRate => pair.FrameRate;
    internal long Serial => pair.Serial;
    internal string OutputName => layer == GpuAlphaLayerKind.Geometry ? "Geometry" : "Glow";
    internal bool TryRead(Action<byte[]> copy) => pair.TryRead(layer, copy, out _);
    internal bool TryRead(Action<byte[]> copy, out long serial) => pair.TryRead(layer, copy, out serial);
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
