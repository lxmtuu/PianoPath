namespace PianoPath;

/// <summary>
/// Writes one short MP4 take and says what it was doing, step by step.
///
/// <para>
/// This is the only place in the app that runs the encoders, and they are native code: a machine whose media
/// stack dies inside one of those calls takes the process down with it — there is no exception to catch and no
/// verdict left to write. The verification run therefore has a child process do the writing (see
/// <see cref="VerificationSuite"/>), so whatever happens here costs the run a note instead of its verdict, and
/// the lines below say which call this process stopped at.
/// </para>
/// </summary>
internal static class Mp4TakeAttempt
{
    /// <summary>The take the verification writes: small enough for any encoder, long enough to close a file.</summary>
    internal const int Width = 64, Height = 48, FrameRate = 15, Frames = 15;

    /// <summary>What this process is doing. The parent copies these into the verification log.</summary>
    private static void Say(string line)
    {
        try { Console.Out.WriteLine(line); Console.Out.Flush(); } catch { }
    }

    /// <summary>
    /// Encodes the take. Returns 0 when the file was written, 1 when one of the recorder's own promises did not
    /// hold — a real failure, named as such — and 2 when this machine cannot write an MP4 at all, which the
    /// caller reports as a skipped check rather than as a fault of the app.
    /// </summary>
    internal static int Run(string path)
    {
        Say($"NOTE MP4 encoder: opening a {Width}×{Height} take at {FrameRate} fps, asking for the audio stream as well.");
        Mp4Recorder recorder;
        try { recorder = new Mp4Recorder(path, Width, Height, FrameRate, withAudio: true); }
        catch (Exception ex)
        {
            Say("NOTE MP4 encoder: this machine's media stack cannot write an MP4 (" + ex.Message + ").");
            return 2;
        }
        using (recorder)
        {
            if (recorder.FrameBytes != Width * 4 * Height || !recorder.HasAlpha || recorder.IsNearSizeLimit || recorder.OutputPath != path)
            {
                Say($"FAIL MP4 take: the recorder promised {Width * 4 * Height} bytes a frame, alpha, no size ceiling and the path it was handed, and gave {recorder.FrameBytes}, {recorder.HasAlpha}, {recorder.IsNearSizeLimit}, {recorder.OutputPath}.");
                return 1;
            }
            try
            {
                recorder.WriteFrame(new byte[16]);
                Say("FAIL MP4 take: a frame of the wrong size was accepted instead of being refused.");
                return 1;
            }
            catch (ArgumentException) { }

            var audio = recorder.HasAudio;
            Say(audio
                ? "NOTE MP4 encoder: open with the audio stream; writing fifteen frames and a second of audio."
                : "NOTE MP4 encoder: open without the audio stream — this machine has no AAC encoder; writing fifteen frames.");
            var frame = new byte[recorder.FrameBytes];
            for (var index = 0; index < Frames; index++)
            {
                for (var pixel = 0; pixel < frame.Length; pixel += 4)
                {
                    frame[pixel] = (byte)(20 + index * 12); frame[pixel + 1] = 90; frame[pixel + 2] = (byte)(200 - index * 8); frame[pixel + 3] = 255;
                }
                recorder.WriteFrame(frame);
                if (index == 0) Say("NOTE MP4 encoder: the encoder took the first frame; writing the other fourteen.");
            }
            if (audio) recorder.AppendSilence(44100);
            var seconds = recorder.Seconds;
            var sound = recorder.AudioDropped ? "dropped" : "kept";
            if (recorder.FrameCount != Frames || audio != recorder.AudioDropped || (audio && Math.Abs(seconds - 1) > .001))
            {
                Say($"FAIL MP4 take: the recorder counted {recorder.FrameCount} frames and {seconds:0.###} s of audio, and said the sound was {sound}.");
                return 1;
            }
            Say("NOTE MP4 encoder: every frame is written; closing the file.");
        }
        Say("NOTE MP4 encoder: the writer closed the file and the take is on disk.");
        return 0;
    }
}
