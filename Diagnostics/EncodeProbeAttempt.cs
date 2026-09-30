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

    /// <summary>
    /// One case on its own thread, with three seconds to finish. A case that does not finish is a result, not a
    /// reason to lose the run: its last note is quoted, the stuck thread is left to its fate as a background
    /// thread, and the next case gets its turn. Three seconds is many times what any of these calls takes on a
    /// machine whose media stack works at all.
    /// </summary>
    private static bool Case(Mf.MediaObjectCase kind)
    {
        var seen = new List<string>();
        var result = 0;
        void Note(string line) { lock (seen) seen.Add(line); Say(line); }
        var thread = new Thread(() =>
        {
            try { result = Mf.MediaObjectCaseRun(kind, Note); }
            catch { result = Mf.MF_E_OUT_OF_MEMORY; }
        }) { IsBackground = true, Name = "case-" + kind };
        thread.Start();
        if (thread.Join(3000))
        {
            Say($"PLUMBING case {kind}: every call came back ({Mf.Describe(result)}).");
            return false;
        }
        string? stopped;
        lock (seen) stopped = seen.Count > 0 ? seen[^1] : null;
        Say($"PLUMBING case {kind}: a call never came back within three seconds; the last one it reached was: {stopped ?? "<it said nothing at all>"}");
        return true;
    }

    /// <summary>The probe itself, once the trace file is open. See <see cref="Run"/>.</summary>
    private static int Attempt(string path)
    {
        // The media objects a take is made of, one set of calls at a time, each under its own watchdog: no file,
        // no encoder, no writer, and no way for one call that never comes back to hide the rest of the answers.
        // Between them these cases say which part of the object works — a stamp after the buffer, a stamp before
        // it, a stamp with no buffer, an attribute write, a read, and a media type for comparison.
        var blocked = false;
        foreach (var kind in new[]
        {
            Mf.MediaObjectCase.TimeAfterBuffer, Mf.MediaObjectCase.TimeBeforeBuffer, Mf.MediaObjectCase.TimeAlone,
            Mf.MediaObjectCase.AttributeOnSample, Mf.MediaObjectCase.ReadTime, Mf.MediaObjectCase.AttributeOnMediaType,
        })
        {
            if (Case(kind)) blocked = true;
        }
        Say(blocked
            ? "NOTE MP4 encoder: at least one case above never came back; the AVI attempt follows anyway, since it is the writing this probe exists to explain."
            : "NOTE MP4 encoder: every media-object case came back, so the objects themselves are not the trouble.");

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
