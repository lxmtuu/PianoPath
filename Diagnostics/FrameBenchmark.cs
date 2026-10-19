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
        // --bench-adapter=<name> pins the run to one GPU by a substring of its DXGI description ("RTX 4050",
        // "Radeon"). Hybrid laptops otherwise measure whichever adapter Windows assigned to the executable,
        // and a hardware gate that silently measured the wrong GPU would be worse than no gate at all:
        // a name that matches none or several adapters fails the run instead of falling back.
        var adapterRequest = Value(args, "--bench-adapter=");
        // --bench-ar adds the AR overlay scene to the run (and is a run of its own, with that one scene, when --bench is not there).
        var withAr = args.Contains("--bench-ar");
        var gate = args.Contains("--bench") || args.Any(argument => argument.StartsWith("--bench=", StringComparison.Ordinal));
        // --bench-detail adds N9's eighteen note-density scenes: a matrix, not a gate, so it is never in the CI
        // run. Alone it measures only that matrix; beside --bench it runs after the budgeted scenes.
        var withDetail = args.Contains("--bench-detail");
        IReadOnlyList<FrameBudget> budgets;
        if (withDetail && !gate) budgets = FrameBudget.Detail;
        else if (withDetail) budgets = withAr ? [.. FrameBudget.AllWithAr, .. FrameBudget.Detail] : [.. FrameBudget.All, .. FrameBudget.Detail];
        else budgets = withAr ? (gate ? FrameBudget.AllWithAr : [FrameBudget.ArLook]) : FrameBudget.All;

        var scenes = new List<FrameBenchReport>();
        var notes = MainWindow.CreateDemoSong();
        var feed = new GpuStageFeed();
        using (var loop = new GpuRenderLoop(feed, adapterRequest: adapterRequest, enableAdaptiveQuality: false))
        {
            if (!loop.WaitUntilStarted(TimeSpan.FromSeconds(60)) || loop.Error is not null)
            {
                var why = loop.Error ?? "it reported no device within 60 s";
                Console.WriteLine($"FAIL perf: the GPU render loop did not start ({why}), so no frame was measured");
                // This exe is GUI-subsystem: at a terminal the user sees only the exit code, no console
                // lines. A start-up failure (an --bench-adapter name matching nothing, a device that
                // refuses to start) therefore also leaves its reason beside the report path.
                try { File.WriteAllText(Path.ChangeExtension(output, ".startup-error.txt"), $"The GPU render loop did not start: {why}{Environment.NewLine}"); }
                catch (IOException) { }
                return ExitNoDevice;
            }
            // The glyph atlas is drawn with WPF's text stack, which needs this (STA) thread; the render thread
            // only uploads it. The real stage always sets it, so the measured scene has it too.
            feed.LabelAtlas = PianoStage.GpuGlyphAtlas;
            var software = FrameBudget.IsSoftwareAdapter(loop.IsWarp, loop.AdapterName);
            foreach (var budget in budgets) scenes.Add(RunScene(loop, feed, budget, notes, frames, software));
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
        using var loop = new GpuRenderLoop(feed, forceWarp: true, enableAdaptiveQuality: false);
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
            ? VisualPresets.CreateDefaultSettings()
            : VisualPresets.FindBuiltIn(budget.Preset)?.Settings.Clone() ?? VisualPresets.CreateDefaultSettings();
        // A detail cell measures the note layer alone: ambient, impacts and particles are switched off in every
        // cell, so the difference between "plain" and "fx" is the trail, the ghosts and the landing glow.
        if (budget.NoteCount > 0) ApplyNoteIsolation(settings, budget.NoteEffects);
        var sceneNotes = budget.NoteCount > 0 ? DensityNotes(budget, settings) : notes;
        // Note colours are data the stage uploads as a table either way, so the bench uses a fixed ramp
        // instead of reaching into the WPF stage for the user's palette: the frame cost does not depend on it.
        feed.SetLook(GpuLook.From(settings, (pitch, track) => System.Windows.Media.Color.FromRgb((byte)(pitch * 2), (byte)(40 + track * 60), 200), .205, null, overCamera: budget.Camera));
        feed.SetStageHeight(budget.Height);
        feed.ClearTransient();
        loop.SetEmbedded(true, budget.Width, budget.Height);
        loop.TargetFps = 0; // the loop still does not pace itself while sampling; this keeps it unpaced before and after
        // A density scene starts its playhead one visible window into the song: the window is exactly how much
        // music the stage shows above the keys, so the first measured frame already has all N bars on screen
        // instead of filling up from one while the warm-up runs.
        // A density cell presses nothing: its bars are the song's, and a press would add live trails and impacts
        // on top of the layer being measured.
        var transport = new BenchTransport(sceneNotes, budget.NoteCount > 0 ? VisibleSeconds(budget, settings) : 0, press: budget.NoteCount == 0);
        using var camera = budget.Camera ? new BenchCamera() : null;
        if (camera is not null)
        {
            // what the overlay shows: the camera's picture, and the notes and lit keys on the keyboard marked in it
            feed.Camera = new GpuCameraInput(camera.Ring, PictureFit.Fit, false, 1f);
            feed.Ar = new GpuArInput(camera.Calibration, ArNoteStyle.Default, 1f);
        }

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
        var cameraAge = loop.CameraAge;
        feed.Camera = null; feed.Ar = null;
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
            CameraFrames = camera is null ? 0 : cameraAge.Frames,
            CameraAgeMeanMs = camera is null ? 0 : cameraAge.AverageMilliseconds,
            CameraAgeWorstMs = camera is null ? 0 : cameraAge.WorstMilliseconds,
            BudgetP95LimitMs = budget.P95LimitMs,
            Verdict = verdict,
            VerdictNote = note,
            Stats = stats,
            Samples = samples
        };
    }

    /// <summary>
    /// Seconds of song the stage shows above the keys: the fall height divided by the song's fall speed, with
    /// the keyboard taking the same share of the frame the layout gives it in every scene.
    /// </summary>
    private static double VisibleSeconds(FrameBudget budget, PianoVisualSettings settings)
    {
        var speed = Math.Max(1, 258 * settings.NoteFallSpeed / 550);
        return Math.Max(1, budget.Height * (1 - .205)) / speed;
    }

    /// <summary>
    /// N simultaneous bars, repeated far past any measurement window. A bar is visible for
    /// <see cref="VisibleSeconds"/> seconds, so bars spaced that window apart over N slices fill it exactly;
    /// pitches walk upward from C3 so every bar is a different key. The notes are long enough to read as
    /// bars and short enough not to overlap the next one on the same key.
    /// </summary>
    private static IReadOnlyList<NoteEvent> DensityNotes(FrameBudget budget, PianoVisualSettings settings)
    {
        var count = Math.Max(1, budget.NoteCount);
        var spacing = Math.Max(.02, VisibleSeconds(budget, settings) / count);
        var duration = Math.Max(.05, Math.Min(spacing * .9, 4));
        var cycle = count * spacing;
        var notes = new List<NoteEvent>(count * 12);
        for (var repeat = 0; repeat < 12; repeat++)
            for (var i = 0; i < count; i++)
                notes.Add(new NoteEvent { Pitch = 48 + i, Start = repeat * cycle + i * spacing, Duration = duration, Velocity = 90 });
        return notes;
    }

    /// <summary>
    /// Turns everything except the note layer off, then sets the three note effects for the cell. The toggles
    /// are the preset's own, so a "plain" cell is the same look with its decorations removed, not a new look.
    /// </summary>
    private static void ApplyNoteIsolation(PianoVisualSettings settings, bool effects)
    {
        settings.ParticleAmount = 0;
        settings.ShowImpactFlash = false;
        settings.ShowImpactRings = false;
        settings.ImpactMorph = "None";
        settings.ReleaseEffect = "None";
        settings.BackgroundMotion = "None";
        settings.ShowStars = false; settings.ShowPetals = false; settings.ShowEmbers = false;
        settings.ShowFlame = false; settings.ShowWisps = false; settings.ShootingStars = false;
        settings.NoteShimmer = false;
        settings.HoldBreath = false; settings.HaloPulse = false; settings.FallingPulse = false; settings.HoldColorCycle = false;
        settings.FallingTrail = effects ? "Glow" : "None";
        settings.FallingTrailIntensity = effects ? 70 : 0;
        settings.FallingGhost = effects;
        settings.FallingGhostAmount = effects ? 60 : 0;
        settings.NoteLandingGlow = effects;
    }

    /// <summary>
    /// Plays the demo song into the feed on wall-clock time, the way the UI thread does: the render thread
    /// only extrapolates the playhead for up to 50 ms between updates, so a bench that set the position once
    /// would measure an empty stage. Notes crossing the playhead fire an impact and hold their key, which is
    /// what puts bursts, rings, flashes and trails into the measured frames. <paramref name="startAt"/> puts the
    /// playhead inside a song that has already started, which is how a density scene opens at full count.
    /// </summary>
    private sealed class BenchTransport(IReadOnlyList<NoteEvent> notes, double startAt = 0, bool press = true)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly HashSet<int> _pressed = new();
        private readonly List<(double At, int Pitch)> _releases = [];
        private readonly double _length = notes.Count == 0 ? 4 : Math.Max(4, notes.Max(note => note.End) + 1);
        private int _next;

        internal void Advance(GpuStageFeed feed)
        {
            var time = _clock.Elapsed.TotalSeconds + startAt;
            if (time >= _length) { time %= _length; Restart(); }
            while (_next < notes.Count && notes[_next].Start <= time)
            {
                var note = notes[_next++];
                if (!press) continue;
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

    /// <summary>
    /// A camera for the AR scene: a 1280×720 picture published into a ring sixty times a second by its own thread, copied into the
    /// ring's buffer each time as a real reader copies a frame out of the driver, and the calibration of the keyboard "in" it.
    /// </summary>
    private sealed class BenchCamera : IDisposable
    {
        internal const int Width = 1280, Height = 720, Fps = 60;
        private readonly Thread _thread;
        private volatile bool _stop;

        internal CameraFrameRing Ring { get; } = new();
        internal ArCalibration Calibration { get; }

        internal BenchCamera()
        {
            Calibration = ArCalibration.Create(KeyboardLayout.Standard88, [(70, 292), (1214, 268), (1236, 421), (52, 449)], Width, Height, out var problem)
                ?? throw new InvalidOperationException($"the bench keyboard did not make a calibration ({problem})");
            var picture = new byte[Width * Height * 4];
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var i = (y * Width + x) * 4;
                    picture[i] = (byte)(70 + y / 8); picture[i + 1] = (byte)(60 + x / 16); picture[i + 2] = 80; picture[i + 3] = 255;
                }
            _thread = new Thread(() =>
            {
                var interval = Stopwatch.Frequency / Fps;
                var next = Stopwatch.GetTimestamp();
                while (!_stop)
                {
                    var buffer = Ring.BeginWrite(picture.Length, out var slot);
                    Buffer.BlockCopy(picture, 0, buffer, 0, picture.Length);
                    Ring.Publish(slot, Width, Height, Stopwatch.GetTimestamp());
                    next += interval;
                    var late = Stopwatch.GetTimestamp() - next;
                    if (late > interval * 4) next = Stopwatch.GetTimestamp();      // a stall is not caught up with in a burst
                    while (!_stop && Stopwatch.GetTimestamp() < next) Thread.Sleep(1);
                }
            }) { IsBackground = true, Name = "bench camera" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join(TimeSpan.FromSeconds(2));
            Ring.Clear();
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
