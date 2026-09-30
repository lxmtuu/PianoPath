namespace PianoPath;

/// <summary>
/// Where a recording's audio goes, so the recording session never has to care which format is running: a WAV
/// file written beside the video (<see cref="WavWriter"/>) or the audio stream of the MP4 the video itself is
/// going into (<see cref="Mp4Recorder"/>).
///
/// <para>
/// The three members are exactly what the engine's tap and the recording clock need, and they are written from
/// two threads — the audio thread appends the blocks the synthesiser renders, the recording clock appends
/// silence when the machine has no sound device — so whoever implements this is responsible for taking a lock
/// around its own state.
/// </para>
/// </summary>
internal interface IAudioTrack : IDisposable
{
    /// <summary>
    /// Appends interleaved 16-bit samples; <paramref name="count"/> is the number of <c>short</c> values, not
    /// frames. A count that would leave half a frame is trimmed, since a partial frame is not a frame.
    /// </summary>
    void Append(short[] samples, int count);

    /// <summary>Appends a stretch of silence, to keep the track in step with the video clock.</summary>
    void AppendSilence(long frames);

    /// <summary>Seconds of audio held so far.</summary>
    double Seconds { get; }
}
