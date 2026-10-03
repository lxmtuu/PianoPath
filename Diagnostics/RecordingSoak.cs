using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace PianoPath;

/// <summary>
/// <c>--soak-record=avi|mp4</c>: records the real window for a while while a pattern of chords is played, and
/// writes what it cost — the longest the UI thread went without answering, how far the process grew, how big
/// the file came out and how long closing it took. It drives the same path the REC button does
/// (<c>RecordVideo_Click</c>, the record timer, the GPU recording tap); only the save dialog is answered
/// by <see cref="MainWindow.DiagnosticRecordTarget"/>. Options: <c>--soak-seconds=</c>, <c>--soak-out=</c>
/// (the report file) and <c>--soak-file=</c> (the take).
/// </summary>
internal static class RecordingSoak
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Start(MainWindow window, string[] args, App app)
    {
        string Option(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith(name, StringComparison.Ordinal))?[name.Length..] ?? fallback;
        var format = Option("--soak-record=", "avi").ToLowerInvariant();
        var seconds = int.TryParse(Option("--soak-seconds=", "20"), out var s) ? s : 20;
        var report = Option("--soak-out=", Path.Combine(Path.GetTempPath(), "keyflow-soak.txt"));
        var take = Option("--soak-file=", Path.Combine(Path.GetTempPath(), "keyflow-soak." + format));
        // The AVI fallback to MP4 renames the take, so the report looks for whichever file ended up there.
        var lines = new List<string>();
        void Say(string text) { lines.Add(text); try { File.WriteAllLines(report, lines); } catch { } }
        void Finish(int code) { Say($"exit {code}"); app.Shutdown(code); }

        var begun = Stopwatch.StartNew();
        var waiter = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        waiter.Tick += (_, _) =>
        {
            if (!window.GpuStageActive && begun.Elapsed < TimeSpan.FromSeconds(25)) return;
            waiter.Stop();
            try { Run(window, format, seconds, take, Option("--soak-resolution=", "1080p"), Option("--soak-window=", ""), Say, Finish); }
            catch (Exception ex) { Say("FAIL " + ex); Finish(1); }
        };
        window.ContentRendered += (_, _) => waiter.Start();
    }

    /// <summary>The size of the take, or of the MP4 it was redirected to when the AVI had no codec.</summary>
    private static long SizeOf(string take)
    {
        foreach (var candidate in new[] { take, Path.ChangeExtension(take, ".mp4") })
        {
            try { if (File.Exists(candidate)) return new FileInfo(candidate).Length; } catch { }
        }
        return 0;
    }

    /// <summary>The width and height the MP4 declares for its video (read from the avc1 sample entry), or null when there is no such entry.</summary>
    private static (int Width, int Height)? DeclaredSize(string take)
    {
        foreach (var candidate in new[] { take, Path.ChangeExtension(take, ".mp4") })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var bytes = File.ReadAllBytes(candidate);
                var at = bytes.AsSpan().IndexOf("avc1"u8);
                if (at < 0 || at + 32 > bytes.Length) continue;
                return ((bytes[at + 28] << 8) | bytes[at + 29], (bytes[at + 30] << 8) | bytes[at + 31]);
            }
            catch { }
        }
        return null;
    }

    private static void Run(MainWindow window, string format, int seconds, string take, string resolution, string windowSize, Action<string> say, Action<int> finish)
    {
        // --soak-window=<width>x<height> resizes the window first, so the "match window" size of a take can be
        // tried on shapes a person might leave it in (narrow, tall, odd-sized).
        var parts = windowSize.Split('x');
        if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
        {
            window.WindowState = WindowState.Normal; window.Width = w; window.Height = h;
            window.UpdateLayout();
        }
        var visual = (PianoVisualSettings)typeof(MainWindow).GetField("_visualSettings", Private)!.GetValue(window)!;
        visual.RecordingFormat = format == "mp4" ? RecordingFormatIds.Mp4 : RecordingFormatIds.Avi;
        visual.RecordingResolution = resolution;
        visual.RecordingFrameRate = 60;
        visual.RecordAudio = false;
        try { File.Delete(take); } catch { }
        window.DiagnosticRecordTarget = take;
        window.SuppressErrorDialogs = true;
        say($"soak: {format} {resolution} 60 fps for {seconds} s; GPU stage active = {window.GpuStageActive}; window {window.ActualWidth:0}x{window.ActualHeight:0}");

        var press = typeof(MainWindow).GetMethod("PressNote", Private)!;
        var release = typeof(MainWindow).GetMethod("ReleaseNote", Private)!;
        var (askedWidth, askedHeight) = ((int, int))typeof(MainWindow).GetMethod("RecordingSize", Private)!.Invoke(window, null)!;
        typeof(MainWindow).GetMethod("RecordVideo_Click", Private)!.Invoke(window, [window, new RoutedEventArgs()]);
        if (!window.RecordingFromGpu) say("NOTE the take is not fed by the GPU tap (the software stage is drawing it)");

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var startMb = process.PrivateMemorySize64 / 1048576.0;
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed; double longest = 0; var stalls = 0; var ticks = 0;
        var held = new Queue<(TimeSpan At, int Pitch)>();
        var next = TimeSpan.Zero; var step = 0;
        int[][] chords = [[48, 52, 55], [50, 53, 57], [52, 55, 59], [53, 57, 60], [55, 59, 62], [57, 60, 64], [59, 62, 65], [60, 64, 67]];
        double peakMb = startMb; var lastSample = TimeSpan.Zero;

        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            var now = clock.Elapsed;
            var gap = (now - last).TotalMilliseconds; last = now; ticks++;
            if (gap > longest) longest = gap;
            if (gap > 100) stalls++;
            while (held.Count > 0 && held.Peek().At <= now) release.Invoke(window, [held.Dequeue().Pitch]);
            if (now >= next)
            {
                foreach (var pitch in chords[step++ % chords.Length]) { press.Invoke(window, [pitch, 100]); held.Enqueue((now + TimeSpan.FromMilliseconds(260), pitch)); }
                next = now + TimeSpan.FromMilliseconds(180);
            }
            if (now - lastSample >= TimeSpan.FromSeconds(2))
            {
                lastSample = now; process.Refresh();
                var mb = process.PrivateMemorySize64 / 1048576.0; if (mb > peakMb) peakMb = mb;
                var size = SizeOf(take);
                say($"  t={now.TotalSeconds,4:0} s  private {mb,7:0} MB  file {size / 1048576.0,7:0.0} MB  longest UI gap so far {longest,5:0} ms  (>100 ms: {stalls})");
            }
            if (now.TotalSeconds < seconds) return;
            timer.Stop();
            while (held.Count > 0) release.Invoke(window, [held.Dequeue().Pitch]);
            var recorder = typeof(MainWindow).GetField("_videoRecorder", Private)!.GetValue(window);
            var stop = Stopwatch.StartNew();
            typeof(MainWindow).GetMethod("StopVideoRecording", Private)!.Invoke(window, [false, null]);
            stop.Stop();
            process.Refresh();
            if (recorder is Mp4Recorder mp4) say($"RESULT mp4: FinalizeFile answered {(mp4.FinalizeResult is { } r ? "0x" + r.ToString("X8") : "never called")}");
            var final = SizeOf(take);
            say($"RESULT {format}: longest UI gap {longest:0} ms, {stalls} gaps over 100 ms, {ticks} UI ticks in {seconds} s (an idle UI would tick ~{seconds * 125})");
            say($"RESULT {format}: private memory {startMb:0} -> peak {peakMb:0} -> end {process.PrivateMemorySize64 / 1048576.0:0} MB; closing the take took {stop.ElapsedMilliseconds} ms");
            say($"RESULT {format}: file {final / 1048576.0:0.0} MB for {seconds} s ({final / 1048576.0 / seconds:0.0} MB/s)");
            var declared = DeclaredSize(take);
            var sizeOk = declared is null || declared == (askedWidth, askedHeight);
            say($"RESULT {format}: the file declares {(declared is { } d ? $"{d.Width}x{d.Height}" : "no H.264 size (not an MP4)")}; the take was asked for {askedWidth}x{askedHeight}{(sizeOk ? "" : "  <-- FRAME SIZE MISMATCH")}");
            finish(sizeOk ? 0 : 1);
        };
        timer.Start();
    }
}
