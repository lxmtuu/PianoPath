using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace PianoPath;

/// <summary>
/// <c>--bench</c>: the perf gate. Runs the real GPU stage — the same <see cref="GpuRenderLoop"/> the main
/// window starts, at the size the product ships at — and writes one JSON report of how long every frame
/// took (<see cref="FrameBenchRun"/>), so a frame budget can be judged by a number instead of by feel.
/// </summary>
/// <remarks>
/// <para><b>What is measured.</b> Each sampled frame is one turn of the loop with the embedded output on:
/// capture the feed, step the simulation, draw the scene and its bloom pyramid, copy the frame out and
/// publish it to the WPF stage. That is exactly what the main window does every frame, and the copy out is
/// what makes the number honest on a real graphics card: without a read-back the loop would only be
/// measuring how fast it can <em>submit</em> work, which a card queues up and finishes later.</para>
/// <para><b>What is not measured.</b> Pacing. The loop normally sleeps to the target frame rate, and a frame
/// that ends with a sleep would report the rate instead of the cost, so the loop skips pacing while sampling
/// (see <see cref="GpuRenderLoop.StartFrameSampling"/>).</para>
/// <para><b>Why the look is not the user's.</b> A gate that depends on whatever somebody saved in
/// <c>visual-settings.json</c> would move for reasons no commit caused. Each scene therefore builds its own
/// settings: a fresh <see cref="PianoVisualSettings"/> for the <c>default</c> scene and the named built-in
/// preset for <c>heavy</c> (see <see cref="FrameBudget"/>).</para>
/// </remarks>
internal static class FrameBenchmark
{
    /// <summary>Exit code when no frame could be measured at all (no Direct3D device, or the render loop stopped): nothing measured, nothing judged.</summary>
    internal const int ExitNoDevice = 2;
    /// <summary>Exit code when a budget that applies to this machine was exceeded.</summary>
    internal const int ExitOverBudget = 3;

    /// <summary>Runs the gate and returns the process exit code.</summary>
    internal static int Run(string[] args)
    {
        // The lines this prints carry ×, · and — . Written to a redirected stdout (which is how CI reads
        // them) or to a terminal on a legacy code page, each of those came out as '?': the first CI run of
        // this gate printed "1920?1080". The gate therefore says which encoding it writes in.
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
        {
            // No console to configure. The numbers still print, in whatever encoding the host picked.
        }
        var frames = FrameBudget.DefaultFrames;
        var framesArgument = args.FirstOrDefault(argument => argument.StartsWith("--bench=", StringComparison.Ordinal));
        if (framesArgument is not null
            && int.TryParse(framesArgument["--bench=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked)
            && asked > 0)
            // A floor, because p95 of five frames is one frame; a ceiling, because a gate should not take an hour.
            frames = Math.Clamp(asked, 20, 5000);
        var output = Value(args, "--bench-out=") is { Length: > 0 } path
            ? path
            : Path.Combine(Path.GetTempPath(), "keyflow-bench.json");
        var previous = Read(Value(args, "--bench-baseline="));

        var scenes = new List<FrameBenchReport>();
        var notes = MainWindow.CreateDemoSong();
        var feed = new GpuStageFeed();
        using (var loop = new GpuRenderLoop(feed))
        {
            if (!loop.WaitUntilStarted(TimeSpan.FromSeconds(60)) || loop.Error is not null)
            {
                var why = loop.Error ?? "it reported no device within 60 s";
                Console.WriteLine($"FAIL perf: the GPU render loop did not start ({why}), so no frame was measured");
                return ExitNoDevice;
            }
            // The glyph atlas is drawn with WPF's text stack, which needs this (STA) thread; the render thread
            // only uploads it. The real stage always sets it, so the measured scene has it too.
            feed.LabelAtlas = PianoStage.GpuGlyphAtlas;
            var software = FrameBudget.IsSoftwareAdapter(loop.IsWarp, loop.AdapterName);
            foreach (var budget in FrameBudget.All) scenes.Add(RunScene(loop, feed, budget, notes, frames, software));
            var run = new FrameBenchRun
            {
                TakenUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                AppVersion = AppInfo.Version,
                Adapter = loop.AdapterName,
                Warp = loop.IsWarp,
                SoftwareAdapter = software,
                ShaderCompileMs = loop.ShaderCompileMilliseconds,
                Scenes = scenes
            };
            foreach (var line in run.LogLines(previous)) Console.WriteLine(line);
            Write(run, output);
            if (!run.Measured) return ExitNoDevice;
            return run.Failed ? ExitOverBudget : 0;
        }
    }

    /// <summary>
    /// Measures one scene on a private loop, for the verification suite: the same code path <c>--bench</c>
    /// takes, at whatever size the check can afford. Returns null when this machine has no Direct3D device,
    /// which the suite reports as a SKIP rather than a failure.
    /// </summary>
    internal static FrameBenchReport? Measure(FrameBudget budget, int frames)
    {
        var feed = new GpuStageFeed();
        using var loop = new GpuRenderLoop(feed, forceWarp: true);
        if (!loop.WaitUntilStarted(TimeSpan.FromSeconds(60)) || loop.Error is not null) return null;
        feed.LabelAtlas = PianoStage.GpuGlyphAtlas;
        var software = FrameBudget.IsSoftwareAdapter(loop.IsWarp, loop.AdapterName);
        return RunScene(loop, feed, budget, MainWindow.CreateDemoSong(), frames, software);
    }

    /// <summary>
    /// Measures one scene on an already running loop. The loop's device is shared by every scene on purpose:
    /// the first frame on a brand-new software device costs seconds while WARP generates code for the shaders,
    /// and paying that once per scene would measure the driver's compiler more than the stage.
    /// </summary>
    private static FrameBenchReport RunScene(GpuRenderLoop loop, GpuStageFeed feed, FrameBudget budget, IReadOnlyList<NoteEvent> notes, int frames, bool software)
    {
        var settings = string.IsNullOrWhiteSpace(budget.Preset)
            ? new PianoVisualSettings()
            : VisualPresets.FindBuiltIn(budget.Preset)?.Settings.Clone() ?? new PianoVisualSettings();
        // Note colours are data the stage uploads as a table either way, so the bench uses a fixed ramp
        // instead of reaching into the WPF stage for the user's palette: the frame cost does not depend on it.
        feed.SetLook(GpuLook.From(settings, (pitch, track) => System.Windows.Media.Color.FromRgb((byte)(pitch * 2), (byte)(40 + track * 60), 200), .205));
        feed.SetStageHeight(budget.Height);
        feed.ClearTransient();
        loop.SetEmbedded(true, budget.Width, budget.Height);
        loop.TargetFps = 0; // the loop still does not pace itself while sampling; this keeps it unpaced before and after
        var transport = new BenchTransport(notes);

        // ---- warm-up: shaders, the atlas, the texture ring and the particle pools all settle here ----
        var drawnBefore = loop.FramesRendered;
        var warm = Stopwatch.StartNew();
        transport.Advance(feed);
        while (loop.FramesRendered - drawnBefore < FrameBudget.DefaultWarmupFrames || warm.Elapsed.TotalSeconds < FrameBudget.WarmupSeconds)
        {
            if (loop.Error is not null || warm.Elapsed.TotalSeconds > FrameBudget.SceneTimeoutSeconds) break;
            Thread.Sleep(4);
            transport.Advance(feed);
        }
        var warmupMs = warm.Elapsed.TotalMilliseconds;
        var warmupFrames = (int)(loop.FramesRendered - drawnBefore);

        // ---- measure ----
        loop.StartFrameSampling(frames);
        var clock = Stopwatch.StartNew();
        while (loop.SampledFrames < frames && loop.Error is null && clock.Elapsed.TotalSeconds < FrameBudget.SceneTimeoutSeconds)
        {
            Thread.Sleep(4);
            transport.Advance(feed);
        }
        var samples = loop.FrameSamples;
        loop.SetEmbedded(false, 0, 0);

        var stats = FrameStats.From(samples);
        // A scene that measured nothing must not be scored: p95 of no frames is zero, and zero is inside every
        // budget, so an unmeasured scene would otherwise report a pass on a machine with a graphics card.
        var (verdict, note) = stats.Count == 0
            ? (FrameVerdict.Fail, $"the scene '{budget.Scene}' measured no frames, so its budget cannot be judged")
            : budget.Judge(software, stats.P95Ms, loop.AdapterName);
        return new FrameBenchReport
        {
            Scene = budget.Scene,
            Preset = string.IsNullOrWhiteSpace(budget.Preset) ? VisualPresets.DefaultPresetName : budget.Preset,
            Description = budget.Description,
            Width = budget.Width,
            Height = budget.Height,
            Frames = stats.Count,
            WarmupFrames = warmupFrames,
            WarmupMs = warmupMs,
            Particles = loop.ParticleCount,
            BudgetP95LimitMs = budget.P95LimitMs,
            Verdict = verdict,
            VerdictNote = note,
            Stats = stats,
            Samples = samples
        };
    }

    /// <summary>
    /// Plays the demo song into the feed on wall-clock time, the way the UI thread does: the render thread
    /// only extrapolates the playhead for up to 50 ms between updates, so a bench that set the position once
    /// would measure an empty stage. Notes crossing the playhead fire an impact and hold their key, which is
    /// what puts bursts, rings, flashes and trails into the measured frames.
    /// </summary>
    private sealed class BenchTransport(IReadOnlyList<NoteEvent> notes)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly HashSet<int> _pressed = new();
        private readonly List<(double At, int Pitch)> _releases = [];
        private readonly double _length = notes.Count == 0 ? 4 : Math.Max(4, notes.Max(note => note.End) + 1);
        private int _next;

        internal void Advance(GpuStageFeed feed)
        {
            var time = _clock.Elapsed.TotalSeconds;
            if (time >= _length) { time %= _length; Restart(); }
            while (_next < notes.Count && notes[_next].Start <= time)
            {
                var note = notes[_next++];
                feed.Impact(note.Pitch, .9);
                feed.LiveNote(note.Pitch, true, .9);
                _pressed.Add(note.Pitch);
                _releases.Add((note.End, note.Pitch));
            }
            for (var i = _releases.Count - 1; i >= 0; i--)
            {
                if (_releases[i].At > time) continue;
                feed.LiveNote(_releases[i].Pitch, false, 0);
                _pressed.Remove(_releases[i].Pitch);
                _releases.RemoveAt(i);
            }
            feed.SetState(notes, time, true, _pressed);
        }

        private void Restart()
        {
            _next = 0;
            _pressed.Clear();
            _releases.Clear();
        }
    }

    private static FrameBenchRun? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!File.Exists(path)) { Console.WriteLine($"NOTE perf: no previous report at {path}, so this run has nothing to compare with"); return null; }
            if (FrameBenchRun.TryParse(File.ReadAllText(path), out var previous)) return previous;
            Console.WriteLine($"NOTE perf: the previous report at {path} could not be read as a Keyflow bench report this build understands (truncated, or written under another schema), so this run has nothing to compare with");
            return null;
        }
        catch (IOException exception)
        {
            Console.WriteLine($"NOTE perf: the previous report at {path} could not be opened ({exception.Message}), so this run has nothing to compare with");
            return null;
        }
    }

    private static void Write(FrameBenchRun run, string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(full, run.ToJson());
            Console.WriteLine($"NOTE perf: report written to {full}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The numbers were printed above; losing the file must not look like a failed frame budget.
            Console.WriteLine($"NOTE perf: the report could not be written to {path} ({exception.Message})");
        }
    }

    private static string? Value(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
