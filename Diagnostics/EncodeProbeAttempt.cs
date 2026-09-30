using System.Collections.Concurrent;
using System.IO;

namespace PianoPath;

/// <summary>
/// Writes three pictures into an AVI through the take's own sample step, with no encoder involved anywhere,
/// and says whether it finished. It is the answer to the question a failed take leaves open: an AVI written
/// uncompressed needs no codec at all, so a machine that cannot finish this one has trouble in the media
/// stack's file handling or in this side's sample plumbing rather than in its encoders.
///
/// <para>
/// The verification starts this as a child process of its own, and only when a run produced no take: the same
/// native calls can hang, and a diagnostic that hangs must never be in front of the thing it is meant to
/// explain. See <c>VerificationSuite.VerifyMp4Take</c>.
/// </para>
/// </summary>
internal static class EncodeProbeAttempt
{
    /// <summary>Three pictures of the take's own size: enough to write a real file, few enough to be quick.</summary>
    internal const int Width = 64, Height = 48, FrameRate = 15, Frames = 3;

    private static StreamWriter? _trace;

    /// <summary>
    /// What this process is doing, said twice: on standard output for the parent's log, and into a trace file
    /// beside the AVI. The file outlives the process even when it is killed, which is the only way to read the
    /// step a stalled media stack really stopped at (see <see cref="Mp4TakeAttempt"/>).
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



    /// <summary>The file this child's trace goes into; also where the parent looks after a run that stopped.</summary>
    internal static string TracePath(string probePath) => probePath + ".trace";

    /// <summary>
    /// Returns 0 when the AVI really was written and 2 when this machine did not manage it — the caller turns
    /// that into a skipped check, since a machine that cannot write an uncompressed AVI is a fact about the
    /// machine rather than a fault of the app.
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

    /// <summary>The probe itself, once the trace file is open. See <see cref="Run"/>.</summary>
    private static int Attempt(string path)
    {
        // The media objects a take is made of, built on their own first: no file, no encoder, no writer, so a
        // machine that stalls while they are handed over is named here rather than after a take has timed out.
        var objects = Mf.MediaObjectProbe(Width * Height * 4, Say);
        Say(objects >= 0
            ? "NOTE MP4 encoder: a media buffer and a sample were built and stamped on their own, so the media objects themselves are not the trouble."
            : $"NOTE MP4 encoder: the media objects stopped being built ({Mf.Describe(objects)}).");
        if (objects < 0) return 2;

        Say($"NOTE MP4 encoder: writing {Frames} pictures into an uncompressed AVI through the same sample plumbing.");
        var result = Mf.EncodeAviProbe(path, new byte[Width * Height * 4], Width, Height, FrameRate, Frames, Say);
        var bytes = 0L;
        try { if (File.Exists(path)) bytes = new FileInfo(path).Length; } catch { }
        Say(result >= 0 && bytes > 0
            ? $"NOTE MP4 encoder: the sample plumbing works — the AVI probe wrote {bytes} bytes of uncompressed video (HRESULT {Mf.Describe(result)})."
            : $"NOTE MP4 encoder: the sample plumbing did not finish an AVI probe ({bytes} bytes, HRESULT {Mf.Describe(result)}).");
        return result >= 0 && bytes > 0 ? 0 : 2;
    }
}
