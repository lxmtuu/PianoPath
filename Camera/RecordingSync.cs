namespace PianoPath;

/// <summary>
/// Keeping the sound and the picture of a take together when the picture comes from a camera.
///
/// <para>
/// A camera's picture shows the world as it was a camera-delay ago (the <c>Camera delay</c> setting is that delay: what the camera
/// takes to deliver a picture plus what the app holds it). The AR layer is drawn for that same older world (see <see cref="ArKeyHistory"/>),
/// so every frame of the take is a consistent picture of one moment — but the engine's sound is of the present. Stamped as they
/// are made, the finger touches the key <c>delay</c> after the note sounds. The fix is to move the sound, not the picture: the
/// audio track starts <c>delay</c> later, which puts the sound of a press at the same stamp as the frame that shows the press.
/// </para>
/// <para>
/// A frame cannot be stamped before zero, so the video keeps its own clock and the audio is the one that waits; the file is then
/// a little longer in sound than in picture, by the delay.
/// </para>
/// </summary>
internal static class RecordingSync
{
    /// <summary>The sample rate of every audio track the app writes.</summary>
    internal const int SampleRate = 44100;

    /// <summary>The longest delay the track will wait for: the setting's own largest, which nobody should need more than.</summary>
    internal const double MaxLeadInSeconds = ArOptions.DelayMax / 1000;

    /// <summary>
    /// How long the audio waits before it begins: the camera's delay when the camera is part of this take, nothing otherwise
    /// (a take without the camera has no picture to wait for). Never negative, never more than <see cref="MaxLeadInSeconds"/>, and
    /// nothing for a delay that is not a number.
    /// </summary>
    internal static double LeadInSeconds(double delaySeconds, bool cameraInTake) =>
        cameraInTake && double.IsFinite(delaySeconds) ? Math.Clamp(delaySeconds, 0, MaxLeadInSeconds) : 0;

    /// <summary>The same wait in whole sample frames, rounded to the nearest, which is the finest the track can place it.</summary>
    internal static long LeadInFrames(double delaySeconds, bool cameraInTake) =>
        (long)Math.Round(LeadInSeconds(delaySeconds, cameraInTake) * SampleRate);

    /// <summary>
    /// Writes the wait into a track that has just been opened and returns it in seconds, as the track holds it (a whole number of
    /// frames, so it can differ from the asked-for delay by half a sample frame).
    /// </summary>
    internal static double Begin(IAudioTrack track, double delaySeconds, bool cameraInTake)
    {
        var frames = LeadInFrames(delaySeconds, cameraInTake);
        if (frames > 0) track.AppendSilence(frames);
        return frames / (double)SampleRate;
    }

    /// <summary>
    /// How many sample frames of silence a take that has no sound device must still be given, to keep its track as long as the
    /// recording clock says plus the wait: what ought to be there less what has been written. Never negative.
    /// </summary>
    internal static long SilenceDue(double elapsedSeconds, double leadInSeconds, double writtenSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || !double.IsFinite(writtenSeconds)) return 0;
        var wanted = (long)((Math.Max(0, elapsedSeconds) + Math.Max(0, leadInSeconds)) * SampleRate);
        return Math.Max(0, wanted - (long)(Math.Max(0, writtenSeconds) * SampleRate));
    }

    /// <summary>
    /// How far the sound of a press is from the picture of it, in seconds, in a finished take (positive: the sound comes after the
    /// picture). The picture shows the press <paramref name="cameraDelaySeconds"/> after it happened, the sound was held back by
    /// <paramref name="leadInSeconds"/>; the press itself cancels out.
    /// </summary>
    internal static double Skew(double cameraDelaySeconds, double leadInSeconds) => leadInSeconds - cameraDelaySeconds;
}
