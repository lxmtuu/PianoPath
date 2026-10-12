namespace PianoPath;

/// <summary>What a finished offline take left behind, so the caller can report it instead of measuring twice.</summary>
internal sealed record ArVideoTake(int FramesWritten, double Seconds, string OutputPath, int Width, int Height, int Downsample);

/// <summary>
/// Renders the AR-composited scene offline at a fixed time step (roadmap §14.10 AR‑S7, frame part): one
/// sweep of <see cref="ArVideoRenderProfile.Frames"/> ticks, each drawing the feed the caller scripts for
/// that tick, downsampling the readback with profile boxes and handing the frame to the recorder the
/// caller chose. Live-mode waits and clock pacing stay out of the export — every output frame advances
/// the scene by exactly `1 / FrameRate`, deterministically, so a second take of the same script writes a
/// byte-identical file and a parallel live-shaped render can be compared pixel for pixel.
///
/// <para>
/// The engine builds no pixels of its own: the caller's feed factory delivers each tick's state (camera
/// picture published into the ring, pressed keys, impacts, beat pulse), exactly like a live loop would
/// have received them at that moment. Audio and the watermark belong to the S7 voice/UI half and are
/// deliberately not here yet.
/// </para>
/// </summary>
internal static class ArRenderToVideo
{
    /// <summary>
    /// Renders the whole take. The recorder is borrowed, not owned: the caller opens and disposes it, and
    /// <see cref="ArVideoRenderProfile.MustFit"/> guards the geometry first. <paramref name="onFrame"/>
    /// (verification hook) sees each downsampled BGRA frame before it is packed for the writer.
    /// </summary>
    internal static ArVideoTake Run(GpuStageRenderer renderer, Func<int, GpuStageFeed> feedForFrame,
        ArVideoRenderProfile profile, IFrameRecorder recorder, Action<int, byte[]>? onFrame = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(feedForFrame);
        ArgumentNullException.ThrowIfNull(profile);
        profile.MustFit(recorder);
        var written = 0;
        for (var i = 0; i < profile.Frames; i++)
        {
            var feed = feedForFrame(i) ?? throw new InvalidOperationException($"The take script produced no feed for frame {i + 1} of {profile.Frames}.");
            // One output frame is exactly one fixed time step: a fresh simulation over the tick's feed,
            // seeded identically every sweep so nothing but the script can differ between two takes.
            var bgra = GpuRenderLoop.RenderFrames(renderer, feed, profile.RenderWidth, profile.RenderHeight,
                0, profile.FrameSeconds, false, out _, null, ArVideoRenderProfile.Seed);
            if (profile.Downsample >= 2) bgra = BgraPack.Box2(bgra, profile.RenderWidth, profile.RenderHeight);
            if (profile.Downsample == 4) bgra = BgraPack.Box2(bgra, profile.RenderWidth / 2, profile.RenderHeight / 2);
            onFrame?.Invoke(i, bgra);
            recorder.WriteFrame(recorder.HasAlpha ? bgra : BgraPack.ToBottomUpBgr(bgra, profile.Width, profile.Height));
            written++;
        }
        return new ArVideoTake(written, profile.Seconds, recorder.OutputPath, profile.Width, profile.Height, profile.Downsample);
    }
}
