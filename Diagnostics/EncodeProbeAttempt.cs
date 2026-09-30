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

    /// <summary>What this process is doing. The parent copies these into the verification log.</summary>
    private static void Say(string line)
    {
        try { Console.Out.WriteLine(line); Console.Out.Flush(); } catch { }
    }

    /// <summary>
    /// Returns 0 when the AVI really was written and 2 when this machine did not manage it — the caller turns
    /// that into a skipped check, since a machine that cannot write an uncompressed AVI is a fact about the
    /// machine rather than a fault of the app.
    /// </summary>
    internal static int Run(string path)
    {
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
