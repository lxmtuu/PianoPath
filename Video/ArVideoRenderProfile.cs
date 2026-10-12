namespace PianoPath;

/// <summary>
/// The numbers a render-to-video run of the AR overlay steps to (roadmap §14.10 AR‑S7): what the source
/// picture is, what size and rate the export becomes, how long it lasts, and how far 2×2-box-downsampled
/// the output is from what the GPU draws. Pure arithmetic, no device — the portable tests lock every
/// clamp and the engine merely follows it.
///
/// <para>
/// Three rules keep a profile honest on every writer. Both output dimensions are even, because the AVI
/// container wants that; the output frame rate matches the recorders' ceiling (60), so a planned take
/// never fails inside the writer; and the render size is never bigger than <c>maxSide</c>, so an
/// over-large camera cannot burn GPU time into pixels that are boxed away.
/// </para>
/// </summary>
internal sealed record ArVideoRenderProfile
{
    /// <summary>Smallest side the renderer will still draw; below this the probes read nothing.</summary>
    internal const int MinRenderSide = 32;
    /// <summary>Smallest side an output frame may carry.</summary>
    internal const int MinOutputSide = 16;
    /// <summary>Highest planned rate on the source; both writers accept at most 60.</summary>
    internal const int MaxRate = 60;
    /// <summary>Longest render side without an explicit cap.</summary>
    internal const int DefaultMaxRenderSide = 1920;
    /// <summary>Deterministic simulation seed of every offline sweep — two runs ask for byte-identical frames.</summary>
    internal const int Seed = 20261013;

    internal required int SourceWidth { get; init; }
    internal required int SourceHeight { get; init; }
    /// <summary>What the GPU draws per output frame; <= the source, <= maxSide on the long side.</summary>
    internal required int RenderWidth { get; init; }
    internal required int RenderHeight { get; init; }
    /// <summary>Frames per second of the take and of the writer; 1..60.</summary>
    internal required int FrameRate { get; init; }
    /// <summary>Output frames the take writes, at least one.</summary>
    internal required int Frames { get; init; }
    /// <summary>1 keeps the render size, 2 halves it with one box pass, 4 quarters it with two.</summary>
    internal required int Downsample { get; init; }
    /// <summary>Seconds one output frame advances the scene; the fixed time step of the sweep.</summary>
    internal double FrameSeconds => 1.0 / FrameRate;
    /// <summary>Seconds the take covers (Frames ticks, including the final settle frame).</summary>
    internal double Seconds => Frames / (double)FrameRate;
    /// <summary>What the writer receives: the render size divided by <see cref="Downsample"/>.</summary>
    internal int Width => RenderWidth / Downsample;
    internal int Height => RenderHeight / Downsample;

    /// <summary>
    /// Plans a take from the source picture's size and the song-or-session length. Throws for a source that
    /// cannot be drawn at all or a length that is not positive; clamps everything a bit off rather than
    /// refusing, exactly so a user-edited cursor cannot kill the export.
    /// </summary>
    internal static ArVideoRenderProfile Plan(int sourceWidth, int sourceHeight, double lengthSeconds,
        int frameRate = 30, int downsample = 1, int maxSide = DefaultMaxRenderSide)
    {
        if (sourceWidth < MinRenderSide || sourceHeight < MinRenderSide)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), $"A render-to-video source must be at least {MinRenderSide}×{MinRenderSide}.");
        if (!(lengthSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(lengthSeconds), "A render-to-video take needs a positive length.");
        if (downsample is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(downsample), "The downsample is 1, 2 or 4 (each step is a 2×2 box).");
        var rate = Math.Clamp(frameRate, 1, MaxRate);
        var cap = Math.Clamp(maxSide, MinRenderSide, 8192);
        var scale = Math.Min(1.0, cap / (double)Math.Max(sourceWidth, sourceHeight));
        var renderWidth = EvenFloor(sourceWidth * scale, MinRenderSide);
        var renderHeight = EvenFloor(sourceHeight * scale, MinRenderSide);
        // The bigger the downsample, the smaller the output; keep at least the floor of dot-pixels.
        var outWidth = Math.Max(MinOutputSide, renderWidth / downsample) & ~1;
        var outHeight = Math.Max(MinOutputSide, renderHeight / downsample) & ~1;
        // Box passes divide by two exactly, so the render size is the output grown back by the downsample.
        renderWidth = outWidth * downsample;
        renderHeight = outHeight * downsample;
        var frames = Math.Max(1, (int)Math.Ceiling(lengthSeconds * rate));
        return new ArVideoRenderProfile
        {
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            RenderWidth = renderWidth,
            RenderHeight = renderHeight,
            FrameRate = rate,
            Frames = frames,
            Downsample = downsample,
        };
    }

    /// <summary>A recorder is only usable when it carries exactly this plan's output geometry and rate.</summary>
    internal void MustFit(IFrameRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        if (recorder.Width != Width || recorder.Height != Height)
            throw new ArgumentException($"A {RenderWidth}×{RenderHeight}→{Width}×{Height} plan cannot write into a {recorder.Width}×{recorder.Height} recorder.", nameof(recorder));
        if (recorder.FrameRate != FrameRate)
            throw new ArgumentException($"A {FrameRate} fps plan needs a {FrameRate} fps recorder, not {recorder.FrameRate}.", nameof(recorder));
    }

    private static int EvenFloor(double value, int minimum) => Math.Max(minimum, (int)Math.Floor(value) & ~1);
}
