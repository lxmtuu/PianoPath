using System.IO;

namespace PianoPath;

/// <summary>
/// Everything about turning a recording into a WebM that can be decided without touching a process: where
/// ffmpeg is looked for, the bitrate the VP9 encoder is asked for, and the two command lines a take runs —
/// one that encodes the picture while the user plays, one that muxes the finished piano track in when they
/// stop. The plan is plain code with no process and no window in it so the shape of both lines is checkable
/// anywhere; <see cref="WebmRecorder"/> is the side that runs them.
///
/// <para>
/// ffmpeg is deliberately not bundled: the installer would grow by tens of megabytes and take on another
/// licence for a format most users never pick. The app uses the ffmpeg the machine already has — beside the
/// executable first, then anywhere on <c>PATH</c> — and says in one sentence when there is none.
/// </para>
/// </summary>
internal static class WebmEncoderPlan
{
    /// <summary>The audio bitrate the Opus encoder is asked for: transparent for a sampled piano.</summary>
    internal const int AudioBitrate = 128_000;

    /// <summary>
    /// The bitrate the VP9 encoder is asked for: the same rule the H.264 take follows (seven per cent of
    /// the frame size times the frame rate, 2 Mbps floor, 24 Mbps ceiling), because a small stage would
    /// otherwise come out mush and a 1080p stage needs the headroom.
    /// </summary>
    internal static int BitrateFor(int width, int height, int frameRate) =>
        (int)Math.Clamp(width * (long)height * frameRate * 7 / 100, 2_000_000, 24_000_000);

    /// <summary>
    /// Where ffmpeg is looked for, in order: beside the executable, then each <c>PATH</c> entry. Null when
    /// the machine has none in either place. <paramref name="appDirectory"/> and <paramref name="pathVariable"/>
    /// are parameters (not read from the environment here) so the search itself is testable.
    /// </summary>
    internal static string? FindFfmpeg(string appDirectory, string? pathVariable)
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        if (!string.IsNullOrWhiteSpace(appDirectory))
        {
            var beside = Path.Combine(appDirectory, name);
            if (File.Exists(beside)) return beside;
        }
        if (string.IsNullOrWhiteSpace(pathVariable)) return null;
        foreach (var entry in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var candidate = Path.Combine(entry.Trim(), name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// The first pass, running while the take records: raw BGRA frames arrive on stdin at the take's size
    /// and rate and leave as a video-only WebM. <c>deadline realtime</c> with <c>cpu-used 5</c> is what lets
    /// libvpx keep up with a live capture on an ordinary machine; quality passes belong to offline renders,
    /// not to a clock that cannot wait.
    /// </summary>
    internal static string[] VideoPassArguments(int width, int height, int frameRate, string tempVideo, bool alpha)
    {
        // With an alpha channel the pixel format becomes yuva420p and alt-ref frames must go off: an
        // alt-ref frame is invisible, so a codec that hides frames would hide pieces of the alpha too.
        var pass = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgra",
            "-s:v", $"{width}x{height}", "-r", frameRate.ToString(),
            "-i", "pipe:0", "-an",
            "-c:v", "libvpx-vp9", "-pix_fmt", alpha ? "yuva420p" : "yuv420p",
            "-b:v", BitrateFor(width, height, frameRate).ToString(),
            "-deadline", "realtime", "-cpu-used", "5", "-row-mt", "1",
        };
        if (alpha) { pass.Add("-auto-alt-ref"); pass.Add("0"); }
        pass.Add("-f"); pass.Add("webm"); pass.Add(tempVideo);
        return pass.ToArray();
    }

    /// <summary>
    /// The second pass, run once when the take stops: the finished picture is copied untouched and the
    /// piano track beside it is encoded as Opus into the file the user asked for. Without a track
    /// (<paramref name="tempAudio"/> null) the pass is a plain remux of the picture.
    /// </summary>
    internal static string[] MuxPassArguments(string tempVideo, string? tempAudio, string output)
    {
        if (tempAudio is null)
            return ["-y", "-hide_banner", "-loglevel", "error", "-i", tempVideo, "-c:v", "copy", output];
        return
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", tempVideo, "-i", tempAudio,
            "-c:v", "copy", "-c:a", "libopus", "-b:a", AudioBitrate.ToString(),
            output,
        ];
    }
}
