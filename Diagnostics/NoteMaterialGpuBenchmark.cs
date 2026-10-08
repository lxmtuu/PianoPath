using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// A diagnostic GPU-only A/B study for a static, one-note fixture. Unlike <c>--bench</c>, this reads D3D11
/// timestamp queries placed around the note DrawInstanced call; it does not treat whole-frame p95 as a material cost.
/// </summary>
internal static class NoteMaterialGpuBenchmark
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const double PositionSeconds = 1.5;
    private const double NoteStartSeconds = .6;
    private const double NoteDurationSeconds = 2.0;
    private const int MidiPitch = 60;
    internal const int CounterbalancedRoundCount = 3;
    private const int ExitProbeUnavailable = 4;

    private static readonly (string Id, string Style)[] Materials =
    [
        ("Solid", "Solid"),
        ("Gem", "Gem"),
        ("CrystalOutline", "CrystalOutline")
    ];

    private static readonly string[][] CounterbalancedOrders =
    [
        ["Solid", "Gem", "CrystalOutline"],
        ["Gem", "CrystalOutline", "Solid"],
        ["CrystalOutline", "Solid", "Gem"]
    ];

    internal static IReadOnlyList<NoteMaterialSamplingRound> BuildSamplingPlan(int requestedSamples)
    {
        if (requestedSamples < CounterbalancedOrders.Length)
            throw new ArgumentOutOfRangeException(nameof(requestedSamples), "At least one sample per material in each counterbalanced round is required.");

        var samplesPerRound = requestedSamples / CounterbalancedOrders.Length;
        var remainder = requestedSamples % CounterbalancedOrders.Length;
        return CounterbalancedOrders.Select((order, index) => new NoteMaterialSamplingRound(
            index + 1,
            samplesPerRound + (index < remainder ? 1 : 0),
            order)).ToArray();
    }

    /// <summary>Runs the probe. Exit 0 means every scheduled batch drained with at least one valid sample per material; it is not a budget pass.</summary>
    internal static int Run(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception exception) when (exception is IOException or PlatformNotSupportedException) { }

        var frames = ParseFrameCount(args);
        var samplingPlan = BuildSamplingPlan(frames);
        var output = Value(args, "--bench-out=") is { Length: > 0 } path
            ? path
            : Path.Combine(Path.GetTempPath(), "keyflow-note-materials.json");
        var feed = new GpuStageFeed();
        var sampleBuffer = new GpuNotePassSampleBuffer();
        using var loop = new GpuRenderLoop(feed, notePassSamples: sampleBuffer, enableAdaptiveQuality: false);
        if (!loop.WaitUntilStarted(TimeSpan.FromSeconds(60)))
        {
            Console.WriteLine("FAIL cost probe: the Direct3D render loop did not start within 60 s; no GPU timestamp sample was collected");
            return FrameBenchmark.ExitNoDevice;
        }
        if (sampleBuffer.InitializationError is { } queryError)
        {
            Console.WriteLine($"FAIL cost probe: D3D11 timestamp-query initialization failed ({queryError}); no fallback to whole-frame timing was used");
            return ExitProbeUnavailable;
        }
        if (loop.Error is { } loopError)
        {
            Console.WriteLine($"FAIL cost probe: the GPU render loop could not start ({loopError}); no sample was collected");
            return FrameBenchmark.ExitNoDevice;
        }

        feed.LabelAtlas = PianoStage.GpuGlyphAtlas;
        loop.TargetFps = 0;
        loop.SetEmbedded(true, Width, Height);
        var notes = new List<NoteEvent> { new() { Pitch = MidiPitch, Start = NoteStartSeconds, Duration = NoteDurationSeconds, Track = 0, Velocity = 90 } };
        var pressed = new HashSet<int>();
        var accumulators = Materials.ToDictionary(material => material.Id, material => new MaterialAccumulator(material.Id, frames));
        var samplingStopped = false;
        var stopReason = "";

        foreach (var round in samplingPlan)
        {
            foreach (var materialId in round.Order)
            {
                var material = Materials.Single(candidate => candidate.Id == materialId);
                var accumulator = accumulators[material.Id];
                var settings = CreateFixtureSettings(material.Style);
                feed.SetLook(GpuLook.From(settings, (_, _) => Color.FromRgb(64, 176, 250), .205));
                feed.SetStageHeight(Height);
                feed.ClearTransient();
                feed.SetDeterministicState(notes, PositionSeconds, true, pressed);
                feed.SetDeterministicBeatPulse(0);

                var warmup = WarmUp(loop);
                accumulator.AddWarmup(warmup);
                if (!warmup.Complete || loop.Error is not null)
                {
                    var why = loop.Error ?? "the 24-frame / 1.5-second warm-up did not finish";
                    accumulator.AddBatch(round, 0, 0, 0, 0, 0, [], false, false, "warmup-incomplete", why);
                    accumulator.Fail("warmup-incomplete", why);
                    stopReason = why;
                    samplingStopped = true;
                    break;
                }

                var sampleRun = sampleBuffer.Start(round.SamplesPerMaterial);
                var frameStart = loop.FramesRendered;
                var samplingClock = Stopwatch.StartNew();
                while (!sampleRun.IsComplete && loop.Error is null && samplingClock.Elapsed.TotalSeconds < FrameBudget.SceneTimeoutSeconds)
                    Thread.Sleep(4); // the D3D query result itself is polled asynchronously by the render thread, never busy-waited here

                var renderedFrames = (int)Math.Clamp(loop.FramesRendered - frameStart, 0, int.MaxValue);
                var samples = sampleRun.SnapshotSamples();
                var queryBatchDrained = sampleRun.IsComplete && loop.Error is null;
                var complete = queryBatchDrained && sampleRun.QueryErrors == 0;
                var fixtureValid = sampleRun.Issued > 0 && samples.All(sample => sample.DrawInstances == 1) && loop.ParticleCount == 0;
                var status = sampleRun.QueryErrors > 0 ? "query-errors"
                    : !queryBatchDrained ? "incomplete"
                    : samples.Length == 0 ? "no-valid-timestamp-samples"
                    : !fixtureValid ? "fixture-mismatch"
                    : sampleRun.DisjointCount > 0 ? "complete-with-disjoint-exclusions"
                    : "complete";
                var reason = loop.Error ?? (sampleRun.QueryErrors > 0
                    ? $"{sampleRun.QueryErrors} D3D11 query result(s) could not be read"
                    : queryBatchDrained ? ""
                    : $"query batch timed out after {samplingClock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
                accumulator.AddBatch(round, sampleRun.Issued, sampleRun.DisjointCount, sampleRun.BusyFrames,
                    sampleRun.QueryErrors, renderedFrames, samples, complete, fixtureValid, status, reason);

                if (!complete)
                {
                    accumulator.Fail(status, reason);
                    stopReason = string.IsNullOrWhiteSpace(reason) ? status : reason;
                    samplingStopped = true;
                    break;
                }
            }
            if (samplingStopped) break;
        }

        if (samplingStopped)
        {
            foreach (var round in samplingPlan)
                foreach (var materialId in round.Order)
                    if (!accumulators[materialId].HasRound(round.Round))
                        accumulators[materialId].AddNotRun(round, stopReason);
        }

        var results = Materials.Select(material => accumulators[material.Id].Build(samplingPlan.Count)).ToArray();
        loop.SetEmbedded(false, 0, 0);
        var software = FrameBudget.IsSoftwareAdapter(loop.IsWarp, loop.AdapterName);
        var report = new NoteMaterialGpuRun
        {
            TakenUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            AppVersion = AppInfo.Version,
            Adapter = loop.AdapterName,
            Backend = loop.IsWarp ? "Direct3D 11 / WARP" : software ? "Direct3D 11 / recognized software adapter" : "Direct3D 11 / non-WARP adapter",
            Warp = loop.IsWarp,
            SoftwareAdapter = software,
            ShaderCompileMs = loop.ShaderCompileMilliseconds,
            Width = Width,
            Height = Height,
            RequestedSamples = frames,
            SamplingPlan = samplingPlan,
            Fixture = "One static MIDI note (pitch 60), fixed playhead at 1.5 s, duration 2.0 s, 2D depth; Solid control versus Gem and Crystal Outline. Hold vibration/breath/color cycle, falling pulse/trail/rainbow/shimmer, landing glow, particles, keys, background, ambient layers, bloom and camera/AR are disabled.",
            MeasurementScope = "D3D11 timestamp + timestamp-disjoint queries bracket only the note DrawInstanced call in GpuStageRenderer. This is the GPU-clock interval between markers around that draw, not whole-frame p95; it excludes other draw passes, bloom/composite and readback. A GPU timestamp interval can include queue idle if the CPU has not submitted the draw yet; the static warmed fixture minimizes that gap but cannot redefine the API's scope.",
            Interpretation = software
                ? "Diagnostic-only: WARP/software-adapter timings validate the query path and report schema, not physical GPU performance."
                : "Non-WARP adapter GPU-pass measurements are recorded for manual review; the adapter identity is reported, and this diagnostic does not set a budget or update EffectCostAssessment.",
            Materials = results
        };

        foreach (var line in report.LogLines()) Console.WriteLine(line);
        Write(report, output);
        var allMeasured = results.Length == Materials.Length
            && results.All(result => result.Complete && result.FixtureValid && result.Stats.Count > 0 && result.QueryErrors == 0);
        return allMeasured ? 0 : ExitProbeUnavailable;
    }

    private static int ParseFrameCount(string[] args)
    {
        var argument = args.FirstOrDefault(value => value.StartsWith("--bench-note-materials=", StringComparison.Ordinal));
        return argument is not null
            && int.TryParse(argument["--bench-note-materials=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked)
            && asked > 0
                ? Math.Clamp(asked, 20, 5000)
                : FrameBudget.DefaultFrames;
    }

    private static PianoVisualSettings CreateFixtureSettings(string style) => new()
    {
        ShowNotes = true,
        NoteStyle = style,
        NoteWidth = 100,
        NoteFallSpeed = 550,
        NoteRoundness = 30,
        NoteEdgeWidth = 48,
        NoteGlow = 0,
        NoteTint = 78,
        NoteEdge = 88,
        NoteHeadGlow = 0,
        NoteRefraction = 100,
        NoteTexture = 60,
        Notes3D = false,
        ShowNoteLabels = false,
        FallingPulse = false,
        FallingTrail = "None",
        FallingGhost = false,
        NoteShimmer = false,
        NoteLandingGlow = false,
        HoldBar = false,
        HoldBreath = false,
        HoldVibration = false,
        HoldColorCycle = false,
        HoldElectricArc = false,
        HaloPulse = false,
        ShootingStars = false,
        ShowEmbers = false,
        ShowWisps = false,
        ShowFlame = false,
        FlameIntensity = 0,
        ShowImpactRings = false,
        ShowImpactFlash = false,
        ShowKeys = false,
        AnimateKeys = false,
        ShowKeyFelt = false,
        ShowKeyShadow = false,
        ShowHalo = false,
        ShowBackground = false,
        BackgroundGradient = false,
        BackgroundMotion = "None",
        BackgroundGuide = false,
        ShowStars = false,
        ShowLightBeams = false,
        ShowPetals = false,
        AmbientEnergy = "None",
        AmbientNature = "None",
        AmbientLight = "None",
        AmbientCosmic = "None",
        BloomIntensity = 0,
        Vignette = 0,
        HorizonGlow = 0,
        BeamIntensity = 0,
        PedalGlow = false,
        TempoSync = false,
        AudioReactive = false
    };

    private static WarmupResult WarmUp(GpuRenderLoop loop)
    {
        var start = loop.FramesRendered;
        var clock = Stopwatch.StartNew();
        while ((loop.FramesRendered - start < FrameBudget.DefaultWarmupFrames || clock.Elapsed.TotalSeconds < FrameBudget.WarmupSeconds)
            && loop.Error is null && clock.Elapsed.TotalSeconds < FrameBudget.SceneTimeoutSeconds)
            Thread.Sleep(4);
        var elapsed = clock.Elapsed.TotalMilliseconds;
        var frames = (int)Math.Clamp(loop.FramesRendered - start, 0, int.MaxValue);
        return new WarmupResult(frames, elapsed,
            frames >= FrameBudget.DefaultWarmupFrames && elapsed >= FrameBudget.WarmupSeconds && loop.Error is null);
    }

    private static void Write(NoteMaterialGpuRun report, string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(full, report.ToJson());
            Console.WriteLine($"NOTE cost probe: JSON report written to {full}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"NOTE cost probe: report could not be written to {path} ({exception.Message})");
        }
    }

    private static string? Value(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private sealed class MaterialAccumulator(string material, int requestedSamples)
    {
        private readonly List<GpuNotePassSample> _samples = [];
        private readonly List<NoteMaterialBatchSummary> _batches = [];
        private int _issuedSamples, _disjointSamples, _querySlotsBusyFrames, _queryErrors, _renderFrames, _warmupFrames;
        private double _warmupMilliseconds;
        private bool _hasIssuedDraw;
        private bool _allFixturesValid = true;
        private string _failureStatus = "";
        private string _failureReason = "";

        internal void AddWarmup(WarmupResult warmup)
        {
            _warmupFrames += warmup.Frames;
            _warmupMilliseconds += warmup.Milliseconds;
        }

        internal bool HasRound(int round) => _batches.Any(batch => batch.Round == round);

        internal void AddBatch(NoteMaterialSamplingRound round, int issuedSamples, int disjointSamples,
            int querySlotsBusyFrames, int queryErrors, int renderFrames, GpuNotePassSample[] samples,
            bool complete, bool fixtureValid, string status, string reason)
        {
            var offset = _samples.Count;
            _samples.AddRange(samples);
            _issuedSamples += issuedSamples;
            _disjointSamples += disjointSamples;
            _querySlotsBusyFrames += querySlotsBusyFrames;
            _queryErrors += queryErrors;
            _renderFrames += renderFrames;
            if (issuedSamples > 0)
            {
                _hasIssuedDraw = true;
                _allFixturesValid &= fixtureValid;
            }
            _batches.Add(new NoteMaterialBatchSummary(round.Round, offset, round.SamplesPerMaterial,
                issuedSamples, samples.Length, disjointSamples, querySlotsBusyFrames, queryErrors,
                complete, status, reason));
        }

        internal void AddNotRun(NoteMaterialSamplingRound round, string reason)
        {
            var offset = _samples.Count;
            _batches.Add(new NoteMaterialBatchSummary(round.Round, offset, round.SamplesPerMaterial,
                0, 0, 0, 0, 0, false, "not-run", reason));
            var hasPriorData = _batches.Any(batch => batch.Round != round.Round && (batch.IssuedSamples > 0 || batch.ValidSamples > 0));
            Fail(hasPriorData ? "incomplete" : "skipped", reason);
        }

        internal void Fail(string status, string reason)
        {
            if (_failureStatus.Length != 0) return;
            _failureStatus = status;
            _failureReason = reason;
        }

        internal NoteMaterialMeasurement Build(int expectedRounds)
        {
            var samples = _samples.ToArray();
            var stats = FrameStats.From(samples.Select(sample => sample.Milliseconds).ToArray());
            var batches = _batches.OrderBy(batch => batch.Round).ToArray();
            var complete = _failureStatus.Length == 0 && batches.Length == expectedRounds && batches.All(batch => batch.Complete);
            var fixtureValid = _hasIssuedDraw && _allFixturesValid;
            var status = !complete ? (_failureStatus.Length == 0 ? "incomplete" : _failureStatus)
                : stats.Count == 0 ? "no-valid-timestamp-samples"
                : !fixtureValid ? "fixture-mismatch"
                : _queryErrors > 0 ? "query-errors"
                : _disjointSamples > 0 ? "complete-with-disjoint-exclusions"
                : "complete";
            return new NoteMaterialMeasurement(material, "2D", requestedSamples, _issuedSamples, _disjointSamples,
                _querySlotsBusyFrames, _queryErrors, _renderFrames, _warmupFrames, _warmupMilliseconds,
                samples, stats, fixtureValid, complete, status, _failureReason)
            {
                Batches = batches
            };
        }
    }

    private readonly record struct WarmupResult(int Frames, double Milliseconds, bool Complete);
}

internal sealed record NoteMaterialSamplingRound(int Round, int SamplesPerMaterial, IReadOnlyList<string> Order);

internal sealed record NoteMaterialBatchSummary(
    int Round,
    int SampleOffset,
    int RequestedSamples,
    int IssuedSamples,
    int ValidSamples,
    int DisjointSamples,
    int QuerySlotsBusyFrames,
    int QueryErrors,
    bool Complete,
    string Status,
    string Reason);

internal sealed class NoteMaterialMeasurement(
    string material,
    string depth,
    int requestedSamples,
    int issuedSamples,
    int disjointSamples,
    int querySlotsBusyFrames,
    int queryErrors,
    int renderFramesDuringSampling,
    int warmupFrames,
    double warmupMilliseconds,
    GpuNotePassSample[] samples,
    FrameStats stats,
    bool fixtureValid,
    bool complete,
    string status,
    string reason)
{
    internal string Material { get; } = material;
    internal string Depth { get; } = depth;
    internal int RequestedSamples { get; } = requestedSamples;
    internal int IssuedSamples { get; } = issuedSamples;
    internal int DisjointSamples { get; } = disjointSamples;
    internal int QuerySlotsBusyFrames { get; } = querySlotsBusyFrames;
    internal int QueryErrors { get; } = queryErrors;
    internal int RenderFramesDuringSampling { get; } = renderFramesDuringSampling;
    internal int WarmupFrames { get; } = warmupFrames;
    internal double WarmupMilliseconds { get; } = warmupMilliseconds;
    internal GpuNotePassSample[] Samples { get; } = samples;
    internal FrameStats Stats { get; } = stats;
    internal bool FixtureValid { get; } = fixtureValid;
    internal bool Complete { get; } = complete;
    internal string Status { get; } = status;
    internal string Reason { get; } = reason;
    internal IReadOnlyList<NoteMaterialBatchSummary> Batches { get; init; } = [];
}

internal sealed class NoteMaterialGpuRun
{
    internal string TakenUtc { get; init; } = "";
    internal string AppVersion { get; init; } = "";
    internal string Adapter { get; init; } = "";
    internal string Backend { get; init; } = "";
    internal bool Warp { get; init; }
    internal bool SoftwareAdapter { get; init; }
    internal double ShaderCompileMs { get; init; }
    internal int Width { get; init; }
    internal int Height { get; init; }
    internal int RequestedSamples { get; init; }
    internal string Fixture { get; init; } = "";
    internal string MeasurementScope { get; init; } = "";
    internal string Interpretation { get; init; } = "";
    internal IReadOnlyList<NoteMaterialSamplingRound> SamplingPlan { get; init; } = [];
    internal IReadOnlyList<NoteMaterialMeasurement> Materials { get; init; } = [];

    internal IEnumerable<string> LogLines()
    {
        var mode = SoftwareAdapter ? "diagnostic-only software/WARP" : "non-WARP GPU timestamps; manual review required";
        var adapter = string.IsNullOrWhiteSpace(Adapter) ? "unknown adapter" : Adapter;
        yield return $"NOTE cost probe: {mode}; adapter={adapter}; API=Direct3D 11; size={Width}×{Height}; warm-up=at least {FrameBudget.DefaultWarmupFrames} frames / {FrameBudget.WarmupSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s per material per round; counterbalanced rounds={SamplingPlan.Count}; scope=note DrawInstanced only; requested samples/material={RequestedSamples}";
        foreach (var round in SamplingPlan)
            yield return $"NOTE cost probe: round {round.Round}: {string.Join(" → ", round.Order)}; {round.SamplesPerMaterial} samples/material";
        foreach (var result in Materials)
        {
            var suffix = result.Stats.Count > 0
                ? $"p95={result.Stats.P95Ms.ToString("0.000000", CultureInfo.InvariantCulture)} ms (valid {result.Stats.Count}/{result.RequestedSamples}, disjoint {result.DisjointSamples}, instances {MinInstances(result)}/{MaxInstances(result)})"
                : $"no valid GPU timestamp samples ({result.Status})";
            var completion = result.Complete ? "query batch complete" : result.Reason;
            yield return $"NOTE cost probe: {result.Material}/{result.Depth}: {suffix}; {completion}";
        }
        yield return "NOTE cost probe: this report is not whole-frame p95 and does not write or update EffectCostAssessment.";
    }

    private static int MinInstances(NoteMaterialMeasurement result) => result.Samples.Length == 0 ? 0 : result.Samples.Min(sample => sample.DrawInstances);
    private static int MaxInstances(NoteMaterialMeasurement result) => result.Samples.Length == 0 ? 0 : result.Samples.Max(sample => sample.DrawInstances);

    internal string ToJson()
    {
        var baseline = Materials.FirstOrDefault(result => result.Material == "Solid" && result.Complete && result.FixtureValid && result.QueryErrors == 0 && result.Stats.Count > 0);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", 1);
            json.WriteString("measurement", "d3d11-note-pass-timestamp");
            json.WriteString("takenUtc", TakenUtc);
            json.WriteString("appVersion", AppVersion);
            json.WriteString("adapter", Adapter);
            json.WriteString("backend", Backend);
            json.WriteBoolean("warp", Warp);
            json.WriteBoolean("softwareAdapter", SoftwareAdapter);
            json.WriteNumber("shaderCompileMs", Round(ShaderCompileMs));
            json.WriteNumber("width", Width);
            json.WriteNumber("height", Height);
            json.WriteNumber("requestedSamplesPerMaterial", RequestedSamples);
            json.WriteString("fixture", Fixture);
            json.WriteString("measurementScope", MeasurementScope);
            json.WriteString("interpretation", Interpretation);
            json.WriteStartObject("warmupProtocol");
            json.WriteNumber("minimumFrames", FrameBudget.DefaultWarmupFrames);
            json.WriteNumber("minimumMilliseconds", Round(FrameBudget.WarmupSeconds * 1000));
            json.WriteString("perMaterial", "warm up after setting the static fixture; each counterbalanced material batch samples only after both minima are met");
            json.WriteEndObject();
            json.WriteStartObject("samplingProtocol");
            json.WriteString("strategy", SamplingPlan.Count == NoteMaterialGpuBenchmark.CounterbalancedRoundCount ? "three-round-cyclic-counterbalanced" : "verification-smoke");
            json.WriteString("sampleArrays", "each material's raw arrays concatenate valid samples in ascending round order; batch sampleOffset indexes those arrays");
            json.WriteStartArray("rounds");
            foreach (var round in SamplingPlan)
            {
                json.WriteStartObject();
                json.WriteNumber("round", round.Round);
                json.WriteNumber("samplesPerMaterial", round.SamplesPerMaterial);
                json.WriteStartArray("order");
                foreach (var material in round.Order) json.WriteStringValue(material);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteStartArray("materials");
            foreach (var result in Materials)
            {
                json.WriteStartObject();
                json.WriteString("material", result.Material);
                json.WriteString("depth", result.Depth);
                json.WriteString("status", result.Status);
                if (!string.IsNullOrWhiteSpace(result.Reason)) json.WriteString("reason", result.Reason);
                json.WriteBoolean("complete", result.Complete);
                json.WriteBoolean("fixtureValid", result.FixtureValid);
                json.WriteNumber("requestedSamples", result.RequestedSamples);
                json.WriteNumber("issuedSamples", result.IssuedSamples);
                json.WriteNumber("validSamples", result.Stats.Count);
                json.WriteNumber("disjointSamplesExcluded", result.DisjointSamples);
                json.WriteNumber("queryErrors", result.QueryErrors);
                json.WriteNumber("querySlotsBusyFrames", result.QuerySlotsBusyFrames);
                json.WriteNumber("renderFramesDuringSampling", result.RenderFramesDuringSampling);
                json.WriteNumber("warmupFrames", result.WarmupFrames);
                json.WriteNumber("warmupMs", Round(result.WarmupMilliseconds));
                json.WriteNumber("drawInstancesMin", MinInstances(result));
                json.WriteNumber("drawInstancesMax", MaxInstances(result));
                json.WriteStartArray("batches");
                foreach (var batch in result.Batches)
                {
                    json.WriteStartObject();
                    json.WriteNumber("round", batch.Round);
                    json.WriteNumber("sampleOffset", batch.SampleOffset);
                    json.WriteNumber("requestedSamples", batch.RequestedSamples);
                    json.WriteNumber("issuedSamples", batch.IssuedSamples);
                    json.WriteNumber("validSamples", batch.ValidSamples);
                    json.WriteNumber("disjointSamplesExcluded", batch.DisjointSamples);
                    json.WriteNumber("querySlotsBusyFrames", batch.QuerySlotsBusyFrames);
                    json.WriteNumber("queryErrors", batch.QueryErrors);
                    json.WriteBoolean("complete", batch.Complete);
                    json.WriteString("status", batch.Status);
                    if (!string.IsNullOrWhiteSpace(batch.Reason)) json.WriteString("reason", batch.Reason);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartObject("gpuPassMs");
                json.WriteNumber("mean", Round(result.Stats.MeanMs));
                json.WriteNumber("min", Round(result.Stats.MinMs));
                json.WriteNumber("median", Round(result.Stats.MedianMs));
                json.WriteNumber("p95", Round(result.Stats.P95Ms));
                json.WriteNumber("p99", Round(result.Stats.P99Ms));
                json.WriteNumber("max", Round(result.Stats.MaxMs));
                json.WriteEndObject();
                if (baseline is not null && result.Material != "Solid" && result.Complete && result.FixtureValid && result.QueryErrors == 0 && result.Stats.Count > 0)
                {
                    var delta = result.Stats.P95Ms - baseline.Stats.P95Ms;
                    json.WriteNumber("p95DeltaFromSolidMs", Round(delta));
                    if (baseline.Stats.P95Ms > 0)
                        json.WriteNumber("p95ChangeFromSolidPercent", Round(delta / baseline.Stats.P95Ms * 100));
                }
                json.WriteStartArray("samplesGpuMs");
                foreach (var sample in result.Samples) json.WriteNumberValue(Round(sample.Milliseconds));
                json.WriteEndArray();
                json.WriteStartArray("sampleClock");
                foreach (var sample in result.Samples)
                {
                    json.WriteStartObject();
                    json.WriteNumber("startTicks", sample.TimestampStart);
                    json.WriteNumber("endTicks", sample.TimestampEnd);
                    json.WriteNumber("frequencyHz", sample.FrequencyHz);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("sampleDrawInstances");
                foreach (var sample in result.Samples) json.WriteNumberValue(sample.DrawInstances);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static double Round(double value) => Math.Round(value, 6);
}
