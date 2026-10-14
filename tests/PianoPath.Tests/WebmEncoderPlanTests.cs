using System.IO;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The WebM take's command lines and ffmpeg search, checked without ever starting a process: the video
/// pass must describe the take exactly (size, rate, raw BGRA in, VP9/yuv420p out), the mux pass must copy
/// the picture untouched and Opus-encode the track, and the search must prefer the executable beside the
/// app over anything on PATH.
/// </summary>
public sealed class WebmEncoderPlanTests
{
    [Fact]
    public void Video_pass_feeds_raw_bgra_and_asks_for_vp9()
    {
        var args = WebmEncoderPlan.VideoPassArguments(1280, 720, 30, "take.video.webm", alpha: false);
        Assert.Equal("pipe:0", args[Array.IndexOf(args, "-i") + 1]);
        Assert.Equal("bgra", args[Array.IndexOf(args, "-pix_fmt") + 1]);
        Assert.Equal("1280x720", args[Array.IndexOf(args, "-s:v") + 1]);
        Assert.Equal("libvpx-vp9", args[Array.IndexOf(args, "-c:v") + 1]);
        Assert.Contains("yuv420p", args);
        Assert.Contains("-an", args);
        Assert.Equal("take.video.webm", args[^1]);
    }

    [Fact]
    public void Alpha_pass_keeps_the_alpha_channel_and_disables_alt_ref()
    {
        var args = WebmEncoderPlan.VideoPassArguments(1280, 720, 30, "take.video.webm", alpha: true);
        Assert.Equal("yuva420p", args[Array.LastIndexOf(args, "-pix_fmt") + 1]);
        Assert.Equal("0", args[Array.IndexOf(args, "-auto-alt-ref") + 1]);
        Assert.DoesNotContain("yuv420p", args);
    }

    [Fact]
    public void Bitrate_follows_the_mp4_rule_floor_and_ceiling()
    {
        Assert.Equal(2_000_000, WebmEncoderPlan.BitrateFor(320, 240, 15));
        Assert.Equal(24_000_000, WebmEncoderPlan.BitrateFor(1920, 1080, 60));
        Assert.InRange(WebmEncoderPlan.BitrateFor(1280, 720, 30), 2_000_000, 24_000_000);
    }

    [Fact]
    public void Mux_pass_copies_the_picture_and_Opus_encodes_the_track()
    {
        var withAudio = WebmEncoderPlan.MuxPassArguments("take.video.webm", "take.audio.wav", "take.webm");
        Assert.Equal("copy", withAudio[Array.IndexOf(withAudio, "-c:v") + 1]);
        Assert.Equal("libopus", withAudio[Array.IndexOf(withAudio, "-c:a") + 1]);
        Assert.Equal("take.webm", withAudio[^1]);

        var silent = WebmEncoderPlan.MuxPassArguments("take.video.webm", null, "take.webm");
        Assert.DoesNotContain("-c:a", silent);
        Assert.Equal("copy", silent[Array.IndexOf(silent, "-c:v") + 1]);
    }

    [Fact]
    public void Search_prefers_the_app_folder_then_path_then_gives_up()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "keyflow-ffmpeg-" + Guid.NewGuid().ToString("N")));
        try
        {
            var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var onPath = Path.Combine(root.FullName, "pathbin");
            var beside = Path.Combine(root.FullName, "app");
            Directory.CreateDirectory(onPath); Directory.CreateDirectory(beside);
            Assert.Null(WebmEncoderPlan.FindFfmpeg(beside, onPath));

            File.WriteAllText(Path.Combine(onPath, name), "");
            Assert.Equal(Path.Combine(onPath, name), WebmEncoderPlan.FindFfmpeg(beside, onPath));

            File.WriteAllText(Path.Combine(beside, name), "");
            Assert.Equal(Path.Combine(beside, name), WebmEncoderPlan.FindFfmpeg(beside, onPath));
        }
        finally { try { root.Delete(recursive: true); } catch { } }
    }
}
