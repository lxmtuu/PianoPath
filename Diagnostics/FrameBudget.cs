using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PianoPath;

/// <summary>What a measured frame time means next to the budget that was written for it.</summary>
internal enum FrameVerdict
{
    /// <summary>The p95 frame time is inside the budget.</summary>
    Pass,
    /// <summary>The p95 frame time is outside the budget, on a machine the budget was written for.</summary>
    Fail,
    /// <summary>No judgement: the frame was drawn by a software rasterizer, or the scene carries no budget.</summary>
    NotApplicable
}

/// <summary>How this run's frame time compares with the previous measurement of the same scene.</summary>
internal enum FrameTrend { Unknown, Faster, Steady, Regression }

/// <summary>
/// The distribution of one run's per-frame times, in milliseconds.
/// </summary>
/// <remarks>
/// <para>Percentiles use the <b>nearest-rank</b> method on a sorted copy: p95 of 100 frames is the 95th
/// fastest one, not an interpolation between two of them. That is the honest reading of "95% of frames were
/// at least this fast", it assumes nothing about the shape of the distribution, and a reader can check it by
/// hand against the sample list the report carries.</para>
/// <para>This file is deliberately free of WPF, Direct3D and everything else the app needs, so
/// <c>tests/PianoPath.Tests</c> links it and checks the arithmetic on any machine.</para>
/// </remarks>
internal readonly struct FrameStats
{
    internal int Count { get; init; }
    internal double MeanMs { get; init; }
    internal double MinMs { get; init; }
    internal double MedianMs { get; init; }
    internal double P95Ms { get; init; }
    internal double P99Ms { get; init; }
    internal double MaxMs { get; init; }

    /// <summary>The distribution of <paramref name="samples"/>; an empty or unusable list gives an empty distribution.</summary>
    internal static FrameStats From(IReadOnlyList<double> samples)
    {
        // A frame time that is not a finite, non-negative number is a broken measurement, not a fast frame.
        var values = new List<double>(samples?.Count ?? 0);
        if (samples is not null) foreach (var value in samples) if (double.IsFinite(value) && value >= 0) values.Add(value);
        if (values.Count == 0) return default;
        values.Sort();
        var sum = 0.0;
        foreach (var value in values) sum += value;
        var sorted = new ReadOnlySpan<double>(values.ToArray());
        return new FrameStats
        {
            Count = values.Count,
            MeanMs = sum / values.Count,
            MinMs = values[0],
            MedianMs = Percentile(sorted, .5),
            P95Ms = Percentile(sorted, .95),
            P99Ms = Percentile(sorted, .99),
            MaxMs = values[^1]
        };
    }

    /// <summary>The nearest-rank percentile of <paramref name="values"/> (sorted inside; the caller's order is untouched).</summary>
    internal static double Percentile(ReadOnlySpan<double> values, double quantile)
    {
        if (values.IsEmpty) return 0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var rank = (int)Math.Ceiling(Math.Clamp(quantile, 0, 1) * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>True when the five numbers can come from one sorted sample list — the first thing a reader of a report checks.</summary>
    internal bool Ordered => Count <= 0 || (MinMs <= MedianMs && MedianMs <= P95Ms && P95Ms <= P99Ms && P99Ms <= MaxMs && MeanMs >= MinMs && MeanMs <= MaxMs);

    /// <summary>The frame rate this distribution's p95 corresponds to, for the line the report prints.</summary>
    internal double FpsAtP95 => P95Ms > 0 ? 1000 / P95Ms : 0;
}

/// <summary>
/// One frame budget: a scene, the size it is measured at, and the p95 frame time that scene may cost.
/// </summary>
/// <remarks>
/// <para><b>Why these two scenes.</b> The budgets the roadmap started with were written for the WPF stage and
/// mixed two different kinds of thing up ("<i>Classic Roll</i>" is a preset, "<i>Cinematic</i>" is a keyboard
/// <c>ShadingQuality</c>). The stage every run draws now is the Direct3D 11 engine, whose cost is driven by how
/// much it composites — ambient shapes, sprites, trails and the bloom pyramid — at the size it composites at.
/// So a budget is now <i>one look at one size</i>: the look a first run gets and the busiest built-in look,
/// both at 1080p, keeping the two numbers the roadmap had (8 ms and 16 ms) with something measurable behind
/// each of them.</para>
/// <para><b>Why p95 and not the mean.</b> A stage that drops one frame in twenty while a burst lands is what a
/// player feels, and a mean hides exactly that. p99 is reported too, but the gate is p95: over a couple of
/// hundred frames p99 is one or two samples and moves with whatever else the machine was doing.</para>
/// </remarks>
internal sealed record FrameBudget(string Scene, int Width, int Height, double P95LimitMs, string Preset, string Description)
{
    /// <summary>Frames measured per scene when the command line does not say.</summary>
    internal const int DefaultFrames = 120;
    /// <summary>
    /// Frames drawn before measuring: shaders, the glyph atlas and the particle pools settle here, and the
    /// first frame on a brand-new software device costs seconds while WARP generates code for every shader.
    /// </summary>
    internal const int DefaultWarmupFrames = 24;
    /// <summary>Wall-clock seconds the warm-up runs for at least, so a machine that draws fast still measures a scene that is already running.</summary>
    internal const double WarmupSeconds = 1.5;
    /// <summary>Overall ceiling for one scene, so a machine that cannot draw it still reports something.</summary>
    internal const double SceneTimeoutSeconds = 60;
    /// <summary>How much slower than the previous run of the same scene counts as a regression (see <see cref="FrameBenchRun.Compare"/>).</summary>
    internal const double RegressionFactor = 1.5;

    /// <summary>The look a fresh install gets, at the size the product is designed for.</summary>
    internal static readonly FrameBudget DefaultLook = new("default", 1920, 1080, 8, "",
        "the look a first run gets: a song playing, notes, halos, impact bursts and the bloom pyramid at 1080p");

    /// <summary>The busiest built-in look: an ambient galaxy, stars, shooting stars, trails and firework bursts.</summary>
    internal static readonly FrameBudget HeavyLook = new("heavy", 1920, 1080, 16, "Galaxy Voyage",
        "the busiest built-in look (Galaxy Voyage): ambient galaxy, stars, shooting stars, rainbow trails and firework bursts at 1080p");

    /// <summary>Every budgeted scene, in the order <c>--bench</c> measures them.</summary>
    internal static IReadOnlyList<FrameBudget> All { get; } = [DefaultLook, HeavyLook];

    /// <summary>Finds a scene by id (<c>default</c>, <c>heavy</c>), or null when the name is not one of them.</summary>
    internal static FrameBudget? Find(string? scene) =>
        string.IsNullOrWhiteSpace(scene) ? null : All.FirstOrDefault(budget => string.Equals(budget.Scene, scene.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether <paramref name="adapterName"/> names a software rasterizer. Windows presents its software
    /// adapter as a <i>hardware</i> one on a machine with no graphics card, so the loop's own "is WARP" answer
    /// is not enough: without this, a CI runner would be judged against budgets written for a real card and
    /// would fail every run. An adapter nobody can name counts as software too — a skipped budget says so in
    /// the report, while a budget applied to the wrong machine fails a build that did nothing wrong.
    /// </summary>
    internal static bool IsSoftwareAdapter(bool loopSaysWarp, string? adapterName)
    {
        if (loopSaysWarp) return true;
        if (string.IsNullOrWhiteSpace(adapterName)) return true;
        foreach (var marker in new[] { "warp", "basic render", "basic display", "software", "microsoft basic" })
            if (adapterName.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The verdict for a measured p95 frame time, and the sentence the report prints next to it.</summary>
    internal (FrameVerdict Verdict, string Note) Judge(bool softwareAdapter, double p95Ms, string adapterName)
    {
        if (P95LimitMs <= 0) return (FrameVerdict.NotApplicable, $"no budget is written for the scene '{Scene}', so this run only measures");
        var limit = Ms(P95LimitMs);
        if (softwareAdapter)
            return (FrameVerdict.NotApplicable,
                $"the frame was drawn by {Describe(adapterName)}, a software rasterizer, so the p95 budget of {limit} ms is not applied — " +
                "the number is still comparable with the previous run of this scene on the same kind of adapter");
        return p95Ms <= P95LimitMs
            ? (FrameVerdict.Pass, $"p95 {Ms(p95Ms)} ms is inside the {limit} ms budget of the scene '{Scene}' on {Describe(adapterName)}")
            : (FrameVerdict.Fail, $"p95 {Ms(p95Ms)} ms is outside the {limit} ms budget of the scene '{Scene}' on {Describe(adapterName)}");
    }

    private static string Describe(string? adapterName) => string.IsNullOrWhiteSpace(adapterName) ? "an adapter that reported no name" : adapterName;

    /// <summary>One millisecond count, always with a dot: these numbers are read by machines and by people in any regional format.</summary>
    internal static string Ms(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>What one scene of a <c>--bench</c> run measured.</summary>
internal sealed class FrameBenchReport
{
    internal string Scene { get; init; } = "";
    internal string Preset { get; init; } = "";
    internal string Description { get; init; } = "";
    internal int Width { get; init; }
    internal int Height { get; init; }
    internal int Frames { get; init; }
    internal int WarmupFrames { get; init; }
    internal double WarmupMs { get; init; }
    internal int Particles { get; init; }
    internal double BudgetP95LimitMs { get; init; }
    internal FrameVerdict Verdict { get; init; }
    internal string VerdictNote { get; init; } = "";
    internal FrameStats Stats { get; init; }
    internal IReadOnlyList<double> Samples { get; init; } = [];

    /// <summary>True when this scene measured anything at all; a scene with no frames says the run failed, not that the stage is fast.</summary>
    internal bool Measured => Stats.Count > 0 && Width > 0 && Height > 0;
    /// <summary>True when a budget that applies to this machine was exceeded.</summary>
    internal bool Failed => Verdict == FrameVerdict.Fail;
}

/// <summary>
/// One <c>--bench</c> run: the machine that drew the frames and every scene it measured. The JSON of this
/// record is the gate's only durable output — CI reads it, compares it with the previous run's copy and
/// publishes it as an artifact, because a finished job's log cannot be read back later.
/// </summary>
internal sealed class FrameBenchRun
{
    /// <summary>Bumped when a field changes meaning, so a reader can tell an old report from a new one.</summary>
    internal const int Schema = 1;

    internal string TakenUtc { get; init; } = "";
    internal string AppVersion { get; init; } = "";
    internal string Adapter { get; init; } = "";
    internal bool Warp { get; init; }
    internal bool SoftwareAdapter { get; init; }
    internal double ShaderCompileMs { get; init; }
    internal IReadOnlyList<FrameBenchReport> Scenes { get; init; } = [];

    /// <summary>True when any scene on a machine the budgets apply to went over its limit.</summary>
    internal bool Failed => Scenes.Any(scene => scene.Failed);
    /// <summary>True when the run measured at least one scene.</summary>
    internal bool Measured => Scenes.Any(scene => scene.Measured);

    internal FrameBenchReport? Scene(string name) => Scenes.FirstOrDefault(scene => string.Equals(scene.Scene, name, StringComparison.Ordinal));

    /// <summary>
    /// How each scene of this run compares with the same scene of <paramref name="previous"/>, in percent of
    /// that run's p95. This is the whole of CI's gate: frames per second on a runner says nothing absolute
    /// (the runners have no graphics card, so the stage is drawn by WARP), but the same kind of machine
    /// drawing the same scene slower than last time is a regression in the code between the two commits.
    /// The third element is the sentence on its own, with no <c>NOTE</c>/<c>WARN perf: </c> in front of it:
    /// <see cref="LogLines"/> is what decides which category a line gets, so the two cannot drift into
    /// printing <c>NOTE perf: perf: …</c> — which is exactly what this gate's first CI run printed.
    /// </summary>
    internal (FrameTrend Trend, double DeltaPercent, string Line) Compare(FrameBenchRun? previous, FrameBenchReport scene)
    {
        if (!scene.Measured) return (FrameTrend.Unknown, 0, $"the scene '{scene.Scene}' measured no frames, so there is nothing to compare");
        // The null test comes first and on its own, so every use below is provably not null: an "is not null"
        // buried inside a larger condition leaves the compiler unsure and costs the build a CS8602 warning.
        if (previous is null) return (FrameTrend.Unknown, 0, $"the scene '{scene.Scene}' has no previous measurement to compare with");
        var before = previous.Scene(scene.Scene);
        if (before is null || !before.Measured)
            return (FrameTrend.Unknown, 0, $"the scene '{scene.Scene}' has no previous measurement to compare with");
        // Comparing a WARP number with a graphics-card one, or 1080p with 4K, would produce a percentage that
        // describes the two machines rather than the code between them.
        if (previous.SoftwareAdapter != SoftwareAdapter || before.Width != scene.Width || before.Height != scene.Height)
            return (FrameTrend.Unknown, 0,
                $"the scene '{scene.Scene}' was measured at {before.Width}×{before.Height} on {(previous.SoftwareAdapter ? "a software rasterizer" : "a graphics card")} last time " +
                $"and at {scene.Width}×{scene.Height} on {(SoftwareAdapter ? "a software rasterizer" : "a graphics card")} this time, so the two runs are not comparable");
        var then = before.Stats.P95Ms;
        var now = scene.Stats.P95Ms;
        if (then <= 0) return (FrameTrend.Unknown, 0, $"the previous report for the scene '{scene.Scene}' has no usable p95, so there is nothing to compare");
        var delta = (now - then) / then * 100;
        var shape = $"previous run p95 {FrameBudget.Ms(then)} ms → this run {FrameBudget.Ms(now)} ms ({Sign(delta)}{Percent(delta)}%)";
        if (delta >= (FrameBudget.RegressionFactor - 1) * 100)
            return (FrameTrend.Regression, delta, SoftwareAdapter
                // Measured, not assumed: the same code gave this scene p95 307.21 ms and then 788.32 ms on two
                // runners, one commit apart, and that commit changed one string. A software rasterizer varies by
                // more than the threshold, so on one the movement is a number to read rather than a verdict.
                ? $"the scene '{scene.Scene}' moved on the same kind of adapter — {shape}; a software rasterizer's own frame time varies by more than this between two runs of the same code, so the number is reported, not judged"
                : $"the scene '{scene.Scene}' got slower on the same kind of adapter — {shape}; a jump past {Percent((FrameBudget.RegressionFactor - 1) * 100)}% " +
                  "is read as a regression in the code between the two commits");
        // Two sentences, not one: the gate's second CI run read a 62.8% drop and still called it "held its
        // frame time", because Faster and Steady shared a line. A number and a sentence that disagree about
        // each other are worse than either on its own.
        if (delta <= -5)
            return (FrameTrend.Faster, delta,
                $"the scene '{scene.Scene}' got quicker on the same kind of adapter — {shape}");
        return (FrameTrend.Steady, delta,
            $"the scene '{scene.Scene}' held its frame time on the same kind of adapter — {shape}");
    }

    /// <summary>
    /// The lines the run prints: the machine, then three lines per scene (the measurement, the budget, the
    /// comparison). They are built here rather than in the caller so the numbers in the sentences and the
    /// numbers in the JSON cannot drift apart, and so the wording can be checked on any machine.
    /// </summary>
    internal IReadOnlyList<string> LogLines(FrameBenchRun? previous)
    {
        var lines = new List<string>
        {
            $"NOTE perf: Keyflow {AppVersion} · {(SoftwareAdapter ? "software rasterizer" : "graphics card")} {Adapter} · shaders compiled in {FrameBudget.Ms(ShaderCompileMs)} ms"
        };
        if (Scenes.Count == 0) lines.Add("FAIL perf: no scene was measured");
        foreach (var scene in Scenes)
        {
            if (!scene.Measured)
            {
                lines.Add($"FAIL perf: the scene '{scene.Scene}' measured no frames, so there is nothing to judge");
                continue;
            }
            var fps = scene.Stats.FpsAtP95.ToString("0", CultureInfo.InvariantCulture);
            lines.Add($"NOTE perf: {scene.Scene} {scene.Width}×{scene.Height} · {scene.Stats.Count} frames after {scene.WarmupFrames} warm-up frames · " +
                      $"mean {FrameBudget.Ms(scene.Stats.MeanMs)} ms, median {FrameBudget.Ms(scene.Stats.MedianMs)} ms, " +
                      $"p95 {FrameBudget.Ms(scene.Stats.P95Ms)} ms (~{fps} fps), p99 {FrameBudget.Ms(scene.Stats.P99Ms)} ms, max {FrameBudget.Ms(scene.Stats.MaxMs)} ms · {scene.Particles} particles");
            lines.Add(scene.Verdict switch
            {
                FrameVerdict.Pass => $"PASS perf: {scene.VerdictNote}",
                FrameVerdict.Fail => $"FAIL perf: {scene.VerdictNote}",
                _ => $"NOTE perf: {scene.VerdictNote}"
            });
            var (trend, _, line) = Compare(previous, scene);
            // A WARN is a claim about the code between two commits, and on a software rasterizer that claim is
            // false more often than not, so only a machine the budgets were written for gets the warning.
            lines.Add(trend == FrameTrend.Regression && !SoftwareAdapter ? $"WARN perf: {line}" : $"NOTE perf: {line}");
        }
        return lines;
    }

    private static string Sign(double value) => value >= 0 ? "+" : "";
    private static string Percent(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The run as JSON, written field by field: the shape is a contract with CI, and a serializer reflecting over internals would leave that contract invisible.</summary>
    internal string ToJson()
    {
        using var stream = new System.IO.MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", Schema);
            json.WriteString("takenUtc", TakenUtc);
            json.WriteString("appVersion", AppVersion);
            json.WriteString("adapter", Adapter);
            json.WriteBoolean("warp", Warp);
            json.WriteBoolean("softwareAdapter", SoftwareAdapter);
            json.WriteNumber("shaderCompileMs", Math.Round(ShaderCompileMs, 2));
            json.WriteStartArray("scenes");
            foreach (var scene in Scenes)
            {
                json.WriteStartObject();
                json.WriteString("scene", scene.Scene);
                json.WriteString("preset", scene.Preset);
                json.WriteString("description", scene.Description);
                json.WriteNumber("width", scene.Width);
                json.WriteNumber("height", scene.Height);
                json.WriteNumber("frames", scene.Frames);
                json.WriteNumber("warmupFrames", scene.WarmupFrames);
                json.WriteNumber("warmupMs", Math.Round(scene.WarmupMs, 2));
                json.WriteNumber("particles", scene.Particles);
                json.WriteNumber("budgetP95LimitMs", scene.BudgetP95LimitMs);
                json.WriteString("verdict", scene.Verdict.ToString());
                json.WriteString("verdictNote", scene.VerdictNote);
                json.WriteStartObject("stats");
                json.WriteNumber("count", scene.Stats.Count);
                json.WriteNumber("meanMs", Math.Round(scene.Stats.MeanMs, 3));
                json.WriteNumber("minMs", Math.Round(scene.Stats.MinMs, 3));
                json.WriteNumber("medianMs", Math.Round(scene.Stats.MedianMs, 3));
                json.WriteNumber("p95Ms", Math.Round(scene.Stats.P95Ms, 3));
                json.WriteNumber("p99Ms", Math.Round(scene.Stats.P99Ms, 3));
                json.WriteNumber("maxMs", Math.Round(scene.Stats.MaxMs, 3));
                json.WriteEndObject();
                json.WriteStartArray("frameMs");
                foreach (var sample in scene.Samples) json.WriteNumberValue(Math.Round(sample, 3));
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Reads back a run this app wrote. A baseline that cannot be read is not an error — the run then simply
    /// has nothing to compare with — so this answers false instead of throwing on a truncated or foreign file.
    /// </summary>
    internal static bool TryParse(string? json, out FrameBenchRun run)
    {
        run = new FrameBenchRun();
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array) return false;
            var measured = new List<FrameBenchReport>();
            foreach (var element in scenes.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var statsElement = element.TryGetProperty("stats", out var found) && found.ValueKind == JsonValueKind.Object ? found : default;
                var samples = new List<double>();
                if (element.TryGetProperty("frameMs", out var frames) && frames.ValueKind == JsonValueKind.Array)
                    foreach (var item in frames.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var value)) samples.Add(value);
                measured.Add(new FrameBenchReport
                {
                    Scene = Text(element, "scene"),
                    Preset = Text(element, "preset"),
                    Description = Text(element, "description"),
                    Width = (int)Number(element, "width"),
                    Height = (int)Number(element, "height"),
                    Frames = (int)Number(element, "frames"),
                    WarmupFrames = (int)Number(element, "warmupFrames"),
                    WarmupMs = Number(element, "warmupMs"),
                    Particles = (int)Number(element, "particles"),
                    BudgetP95LimitMs = Number(element, "budgetP95LimitMs"),
                    Verdict = Enum.TryParse<FrameVerdict>(Text(element, "verdict"), out var verdict) ? verdict : FrameVerdict.NotApplicable,
                    VerdictNote = Text(element, "verdictNote"),
                    Stats = statsElement.ValueKind == JsonValueKind.Undefined
                        ? FrameStats.From(samples)
                        : new FrameStats
                        {
                            Count = (int)Number(statsElement, "count"),
                            MeanMs = Number(statsElement, "meanMs"),
                            MinMs = Number(statsElement, "minMs"),
                            MedianMs = Number(statsElement, "medianMs"),
                            P95Ms = Number(statsElement, "p95Ms"),
                            P99Ms = Number(statsElement, "p99Ms"),
                            MaxMs = Number(statsElement, "maxMs")
                        },
                    Samples = samples
                });
            }
            run = new FrameBenchRun
            {
                TakenUtc = Text(root, "takenUtc"),
                AppVersion = Text(root, "appVersion"),
                Adapter = Text(root, "adapter"),
                Warp = Flag(root, "warp"),
                SoftwareAdapter = Flag(root, "softwareAdapter"),
                ShaderCompileMs = Number(root, "shaderCompileMs"),
                Scenes = measured
            };
            return run.Measured;
        }
        catch (JsonException) { return false; }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;
}
