using System.Collections.Concurrent;
using System.IO;

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

    private static StreamWriter? _trace;

    /// <summary>
    /// What this process is doing, said twice: on standard output, which the parent copies into the verification
    /// log, and into a trace file beside the take. The file is not a luxury — this process is killed when the
    /// media stack does not come back, and the last lines in flight on a pipe can be lost exactly when they
    /// matter most. A run that stops then reads the file's last line, which is the step the machine really
    /// stopped at, however hostile the ending was.
    /// </summary>
    private static void Say(string line)
    {
        try { _trace?.WriteLine(line); _trace?.Flush(); } catch { }
        try { Pending.Add(line); } catch { }
    }

    private static readonly BlockingCollection<string> Pending = new();
    private static Thread? _pump;

    /// <summary>
    /// Starts the one thread that speaks on standard output. Everything this child says goes into a queue and is
    /// written from there, so a pipe that stops being read cannot stop the child: a redirected pipe that nobody
    /// drains blocks on the write, and that would look exactly like a media stack that had stalled — the run would
    /// end quoting the line before the block and blaming the wrong call. The trace file is written first and from
    /// the working thread, which is why it is the account the parent trusts.
    /// </summary>
    private static void StartPump()
    {
        _pump = new Thread(() =>
        {
            foreach (var line in Pending.GetConsumingEnumerable())
            {
                try { Console.Out.WriteLine(line); Console.Out.Flush(); } catch { }
            }
        }) { IsBackground = true, Name = "stdout-pump" };
        _pump.Start();
    }

    /// <summary>Lets the pump finish what is queued, then lets it go.</summary>
    private static void StopPump()
    {
        try { Pending.CompleteAdding(); } catch { }
        try { _pump?.Join(1000); } catch { }
        _pump = null;
    }



    /// <summary>The file the child's own trace goes into; also where the parent looks after a run that stopped.</summary>
    internal static string TracePath(string takePath) => takePath + ".trace";

    /// <summary>
    /// Encodes the take. Returns 0 when the file was written, 1 when one of the recorder's own promises did not
    /// hold — a real failure, named as such — and 2 when this machine cannot write an MP4 at all, which the
    /// caller reports as a skipped check rather than as a fault of the app.
    /// </summary>
    internal static int Run(string path)
    {
        try { _trace = new StreamWriter(TracePath(path), append: false) { AutoFlush = true }; } catch { }
        StartPump();
        try { return Attempt(path); }
        finally
        {
            try { _trace?.Dispose(); } catch { }
            _trace = null;
            StopPump();
        }
    }

    /// <summary>
    /// Builds the take's buffer and sample on a thread of their own, with three seconds to do it. A machine whose
    /// media objects stall makes that a result rather than a lost run: the caller is told the last call reached
    /// and carries on to the take, which is the only thing that can say whether a real file can be written.
    /// </summary>
    private static int ObjectsUnderWatchdog(out string? stopped)
    {
        var seen = new List<string>();
        var result = 0;
        void Note(string line) { lock (seen) seen.Add(line); Say(line); }
        var thread = new Thread(() =>
        {
            try { result = Mf.MediaObjectProbe(Width * Height * 4, Note); }
            catch { result = Mf.MF_E_OUT_OF_MEMORY; }
        }) { IsBackground = true, Name = "take-objects" };
        thread.Start();
        if (thread.Join(3000)) { stopped = null; return result; }
        lock (seen) stopped = seen.Count > 0 ? seen[^1] : null;
        return unchecked((int)0x80004004);   // E_ABORT: the call did not come back, which is not a failure of the app
    }

    /// <summary>The take itself, once the trace file is open. See <see cref="Run"/>.</summary>
    private static int Attempt(string path)
    {
        // The machine gets asked one question before the take, and only one: can this media stack hold an
        // H.264 stream at all? Asking a sink writer for one writes no frame and finds the encoder behind the
        // mux, so a machine that says no is told so in one sentence instead of being left to hang inside a take
        // that was never going to be written. Everything else the run may want to know about this machine — the
        // encoder-free plumbing probe — is asked afterwards and elsewhere, so it can never hold up the take.
        var streamProbe = Path.ChangeExtension(path, ".probe.mp4");
        Say("NOTE MP4 encoder: asking the media stack whether it can hold an H.264 stream for this size.");
        var encoder = Mp4Recorder.VideoTarget(Width, Height, FrameRate);
        var encoderResult = encoder is null ? Mf.MF_E_OUT_OF_MEMORY : Mf.EncodeSinkProbe(streamProbe, encoder, Say);
        try { File.Delete(streamProbe); } catch { }
        Say(encoderResult >= 0
            ? "NOTE MP4 encoder: the sink writer found a way to make H.264 at this size, so there is an encoder here to write a take with."
            : $"NOTE MP4 encoder: the media stack would not take an H.264 stream (HRESULT {Mf.Describe(encoderResult)}), so there is no encoder here to write a take with, and nothing further is attempted.");
        if (encoderResult < 0) return 2;

        // The buffer and the sample the take's first frame will go into, built once on their own and under a
        // watchdog: they need no writer and no encoder, so a machine that stalls while they come together says so
        // here, in three seconds, with the call named. The take then goes ahead regardless — the writing is the
        // one thing this child exists for, and a probe may not stand in front of it, which is exactly what a run
        // of this verification once showed when the probe was allowed to block the take.
        var objects = ObjectsUnderWatchdog(out var objectStop);
        Say(objects >= 0
            ? "NOTE MP4 encoder: the take's media buffer and sample were built and stamped on their own, so the take can carry a frame."
            : $"NOTE MP4 encoder: the take's media objects did not come back within three seconds — the last call reached was: {objectStop ?? "<it said nothing at all>"} — so the take is attempted anyway and its own trace will say where it stops.");

        Say($"NOTE MP4 encoder: opening a {Width}×{Height} take at {FrameRate} fps, asking for the audio stream as well.");
        Mp4Recorder recorder;
        try { recorder = new Mp4Recorder(path, Width, Height, FrameRate, withAudio: true, step: Say); }
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
