namespace PianoPath;

/// <summary>What a finished offline take left behind, so the caller can report it instead of measuring twice.</summary>
internal sealed record ArVideoTake(int FramesWritten, double Seconds, string OutputPath, int Width, int Height, int Downsample, double AudioSeconds = 0);

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
/// have received them at that moment. With <paramref name="audio"/> set, the tick's note events fire with
/// its frame and the piano blocks mix into the take's WAV at exactly the same pace — the track ends where
/// the video ends, sample-exact. The GPU-side watermark and the export UI belong to the rest of AR‑S7.
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
        ArVideoRenderProfile profile, IFrameRecorder recorder, Action<int, byte[]>? onFrame = null,
        ArVideoAudio? audio = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(feedForFrame);
        ArgumentNullException.ThrowIfNull(profile);
        profile.MustFit(recorder);
        var written = 0;
        using var sequence = new GpuOfflineFrameSequence(renderer,profile.RenderWidth,profile.RenderHeight,ArVideoRenderProfile.Seed);
        float[]? floats = null; short[]? pcm = null; long mixed = 0;
        if (audio is not null) { floats = new float[4096 * 2]; pcm = new short[4096 * 2]; }
        for (var i = 0; i < profile.Frames; i++)
        {
            // The tick's sound covers exactly the span of the tick's frame — events on it land with its picture.
            if (audio is not null) mixed = ArVideoMixdown.MixTick(audio, profile.FrameRate, i, mixed, floats!, pcm!);
            var feed = feedForFrame(i) ?? throw new InvalidOperationException($"The take script produced no feed for frame {i + 1} of {profile.Frames}.");
            // Seed once per take, not once per frame: particle ages, emitter credit, key envelopes,
            // release history and RNG all continue across ticks, just as they do in the live loop.
            var bgra = sequence.RenderFrame(feed,profile.FrameSeconds);
            if (profile.Downsample >= 2) bgra = BgraPack.Box2(bgra, profile.RenderWidth, profile.RenderHeight);
            if (profile.Downsample == 4) bgra = BgraPack.Box2(bgra, profile.RenderWidth / 2, profile.RenderHeight / 2);
            onFrame?.Invoke(i, bgra);
            recorder.WriteOfflineFrame(recorder.HasAlpha ? bgra : BgraPack.ToBottomUpBgr(bgra, profile.Width, profile.Height));
            written++;
        }
        return new ArVideoTake(written, profile.Seconds, recorder.OutputPath, profile.Width, profile.Height, profile.Downsample,
            audio is null ? 0 : mixed / (double)ArVideoMixdown.SampleRate);
    }
}
