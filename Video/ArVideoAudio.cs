namespace PianoPath;

/// <summary>One note switch on one tick of a render-to-video take: press at this velocity, or release.</summary>
internal readonly record struct ArVideoNoteEvent(int Pitch, int Velocity, bool On);

/// <summary>
/// The voice half of an offline take (roadmap §14.10 AR‑S7, voice part): the piano to play, the note
/// events each tick fires, and the WAV track the mixed blocks land in. The synthesizer is the app's own
/// <see cref="IPianoSynthesizer"/> — a device-free renderer, so the take sounds the same on every machine
/// and twice over.
/// </summary>
internal sealed record ArVideoAudio(IPianoSynthesizer Synth, Func<int, IReadOnlyList<ArVideoNoteEvent>> EventsForTick, WavWriter Out);

/// <summary>
/// Mixes the take's piano track at frame pace (roadmap §14.10 AR‑S7, voice part): for every tick, fire
/// that tick's note events and render exactly the samples that tick covers, so the track ends exactly
/// where the video ends. Rounding happens at cumulative tick ends — never once per tick — so a take at
/// 13 fps keeps its 44.1 kHz line sample-exact across the uneven split.
/// </summary>
internal static class ArVideoMixdown
{
    /// <summary>The track always lands at the recording convention: stereo 16-bit, 44.1 kHz.</summary>
    internal const int SampleRate = 44100;
    private const int ChunkFrames = 4096;

    /// <summary>Samples covered by ticks 0..<paramref name="tick"/> inclusive; the take is this long after that tick.</summary>
    internal static long FramesThrough(int tick, int frameRate) =>
        (long)Math.Round((tick + 1) * (SampleRate / (double)frameRate), MidpointRounding.AwayFromZero);

    /// <summary>Renders the whole voice take; returns the sample frames written (== the take's seconds, in samples).</summary>
    internal static long MixTake(ArVideoAudio audio, ArVideoRenderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(profile);
        var floats = new float[ChunkFrames * 2];
        var pcm = new short[ChunkFrames * 2];
        long done = 0;
        for (var tick = 0; tick < profile.Frames; tick++) done = MixTick(audio, profile.FrameRate, tick, done, floats, pcm);
        return done;
    }

    /// <summary>
    /// Fires one tick's note events and renders exactly the samples that tick covers; <paramref name="doneBefore"/>
    /// is how many samples of the take are already down, so the split lands exactly on the tick's picture.
    /// The video half of the engine calls this once per output frame; a buffer pair of at least
    /// ChunkFrames × 2 is reused across ticks.
    /// </summary>
    internal static long MixTick(ArVideoAudio audio, int frameRate, int tick, long doneBefore, float[] floats, short[] pcm)
    {
        foreach (var note in audio.EventsForTick(tick) ?? [])
        {
            if (note.On) audio.Synth.NoteOn(0, note.Pitch, note.Velocity);
            else audio.Synth.NoteOff(0, note.Pitch);
        }
        var done = doneBefore;
        var target = FramesThrough(tick, frameRate);
        while (done < target)
        {
            var step = (int)Math.Min(ChunkFrames, target - done);
            audio.Synth.Render(floats, step);
            ToPcm16(floats, pcm, step);
            audio.Out.Append(pcm, step * 2);
            done += step;
        }
        return done;
    }

    /// <summary>Signed 16-bit interleaved from interleaved floats; the same mapping the live recording tap converts with.</summary>
    internal static void ToPcm16(float[] source, short[] target, int frames)
    {
        for (var i = 0; i < frames * 2; i++)
            target[i] = (short)Math.Clamp((int)Math.Round(source[i] * 32767f), -32768, 32767);
    }
}
