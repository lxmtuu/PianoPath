using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The arithmetic behind the perf gate (<c>--bench</c>): the percentiles a frame budget is judged by, the
/// budgets themselves, which machines they apply to, the report CI reads back and the relative comparison
/// CI makes with it.
///
/// These live here rather than only in <c>VerifyFrameBudget</c> because a gate whose arithmetic is wrong
/// fails builds that did nothing wrong, and none of it needs Windows: the file it tests is the pure half of
/// the gate, which is exactly why it is a file of its own.
/// </summary>
public class FrameBudgetTests
{
    private static List<double> Range(int from, int to)
    {
        var values = new List<double>();
        for (var i = from; i <= to; i++) values.Add(i);
        return values;
    }

    // ---------------------------------------------------------------------------------------------
    // percentiles
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Percentiles_take_the_nearest_rank_of_the_sorted_frames()
    {
        var stats = FrameStats.From(Range(1, 100));

        Assert.Equal(100, stats.Count);
        Assert.Equal(1, stats.MinMs);
        Assert.Equal(100, stats.MaxMs);
        Assert.Equal(50.5, stats.MeanMs, 6);
        Assert.Equal(50, stats.MedianMs, 6);
        Assert.Equal(95, stats.P95Ms, 6);
        Assert.Equal(99, stats.P99Ms, 6);
    }

    [Fact]
    public void A_percentile_of_an_empty_or_single_frame_list_never_divides_by_anything()
    {
        Assert.Equal(0, FrameStats.From([]).Count);
        Assert.Equal(0, FrameStats.From([]).P95Ms, 6);
        Assert.Equal(0, FrameStats.From([]).MedianMs, 6);
        Assert.Equal(0, FrameStats.Percentile(new double[0], .95), 6);

        var one = FrameStats.From([4.5]);
        Assert.Equal(1, one.Count);
        Assert.Equal(4.5, one.MedianMs, 6);
        Assert.Equal(4.5, one.P95Ms, 6);
        Assert.Equal(4.5, one.P99Ms, 6);
        Assert.Equal(4.5, one.MaxMs, 6);
    }

    [Fact]
    public void The_samples_arrive_in_frame_order_so_the_distribution_sorts_its_own_copy()
    {
        var frames = new List<double> { 7, 1, 4 };

        var stats = FrameStats.From(frames);

        Assert.Equal(7, stats.P95Ms, 6);
        Assert.Equal(1, stats.MinMs, 6);
        Assert.Equal(7, stats.MaxMs, 6);
        // The report carries the samples in the order the loop drew them; a distribution that reordered the
        // caller's list would quietly rewrite the evidence.
        Assert.Equal(new double[] { 7, 1, 4 }, frames);
    }

    [Fact]
    public void A_frame_time_that_is_not_a_finite_non_negative_number_is_a_broken_measurement()
    {
        Assert.Equal(0, FrameStats.From([double.NaN, -3, double.PositiveInfinity]).Count);

        var kept = FrameStats.From([3, double.NaN, -1, double.PositiveInfinity, 5]);
        Assert.Equal(2, kept.Count);
        Assert.Equal(3, kept.MinMs, 6);
        Assert.Equal(5, kept.MaxMs, 6);
    }

    [Fact]
    public void A_distribution_whose_numbers_cannot_come_from_one_sorted_list_is_said_to_be_unordered()
    {
        Assert.True(FrameStats.From(Range(1, 100)).Ordered);
        Assert.True(FrameStats.From([1, 2, 3]).Ordered);
        Assert.True(FrameStats.From([]).Ordered);

        var impossible = new FrameStats { Count = 3, MinMs = 5, MedianMs = 4, P95Ms = 6, P99Ms = 7, MaxMs = 8, MeanMs = 6 };
        Assert.False(impossible.Ordered);
    }

    [Fact]
    public void The_frame_rate_a_p95_implies_is_the_number_a_report_reads_as()
    {
        Assert.Equal(100, FrameStats.From([10]).FpsAtP95, 6);
        Assert.Equal(0, FrameStats.From([]).FpsAtP95, 6);
    }

    // ---------------------------------------------------------------------------------------------
    // the budget table
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_gate_budgets_two_looks_at_one_size_each()
    {
        Assert.Equal(2, FrameBudget.All.Count);
        Assert.All(FrameBudget.All, budget =>
        {
            Assert.Equal(1920, budget.Width);
            Assert.Equal(1080, budget.Height);
            Assert.True(budget.P95LimitMs > 0);
            Assert.True(budget.Description.Length > 20);
        });
        Assert.Equal(2, FrameBudget.All.Select(budget => budget.Scene).Distinct().Count());
    }

    [Fact]
    public void The_two_budgets_the_roadmap_set_are_still_the_two_the_gate_applies()
    {
        Assert.Equal(8, FrameBudget.DefaultLook.P95LimitMs, 6);
        Assert.Equal(16, FrameBudget.HeavyLook.P95LimitMs, 6);
        // The default scene is a first run's own settings, not a preset; the heavy one names the busiest look
        // the app ships. (Whether that preset really exists is a Windows-side check: it needs the preset shelf.)
        Assert.True(string.IsNullOrWhiteSpace(FrameBudget.DefaultLook.Preset));
        Assert.Equal("Galaxy Voyage", FrameBudget.HeavyLook.Preset);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData(" heavy ")]
    public void A_scene_is_found_by_its_id_whatever_the_case(string name)
    {
        Assert.NotNull(FrameBudget.Find(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cinematic")]
    [InlineData("classic roll")]
    public void A_name_that_is_not_a_scene_does_not_resolve_to_one(string? name)
    {
        // The budgets the roadmap started with were named after a preset and a keyboard shading level; neither
        // is a scene any more, and neither may quietly become one.
        Assert.Null(FrameBudget.Find(name));
    }

    // ---------------------------------------------------------------------------------------------
    // which machine a budget applies to
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, "NVIDIA GeForce RTX 4070")]
    [InlineData(false, "Microsoft Basic Render Driver")]
    [InlineData(false, "Microsoft Basic Render Driver (WARP)")]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    [InlineData(false, null)]
    public void A_software_rasterizer_is_recognised_whatever_the_adapter_claims(bool warp, string? adapter)
    {
        Assert.True(FrameBudget.IsSoftwareAdapter(warp, adapter));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070")]
    [InlineData("AMD Radeon RX 7900 XTX")]
    [InlineData("Intel(R) Iris(R) Xe Graphics")]
    public void A_real_graphics_card_is_the_machine_the_budgets_were_written_for(string adapter)
    {
        Assert.False(FrameBudget.IsSoftwareAdapter(false, adapter));
    }

    [Fact]
    public void On_a_software_rasterizer_the_budget_is_never_applied()
    {
        var (verdict, note) = FrameBudget.DefaultLook.Judge(true, 400, "Microsoft Basic Render Driver");

        Assert.Equal(FrameVerdict.NotApplicable, verdict);
        Assert.Contains("software rasterizer", note, StringComparison.Ordinal);
        Assert.Contains("8.00 ms", note, StringComparison.Ordinal);
    }

    [Fact]
    public void On_a_graphics_card_the_p95_budget_is_a_hard_gate_whose_edge_passes()
    {
        const string card = "NVIDIA GeForce RTX 4070";

        Assert.Equal(FrameVerdict.Pass, FrameBudget.DefaultLook.Judge(false, 7.9, card).Verdict);
        Assert.Equal(FrameVerdict.Pass, FrameBudget.DefaultLook.Judge(false, 8, card).Verdict);
        Assert.Equal(FrameVerdict.Fail, FrameBudget.DefaultLook.Judge(false, 8.1, card).Verdict);
        // The same frame time passes the heavier scene's budget: a limit belongs to its scene, not to the app.
        Assert.Equal(FrameVerdict.Pass, FrameBudget.HeavyLook.Judge(false, 8.1, card).Verdict);
        Assert.Equal(FrameVerdict.Fail, FrameBudget.HeavyLook.Judge(false, 16.5, card).Verdict);
    }

    [Fact]
    public void A_scene_without_a_limit_only_measures()
    {
        var unbudgeted = FrameBudget.DefaultLook with { P95LimitMs = 0 };

        Assert.Equal(FrameVerdict.NotApplicable, unbudgeted.Judge(false, 999, "NVIDIA GeForce RTX 4070").Verdict);
        Assert.Contains("no budget", unbudgeted.Judge(false, 999, "NVIDIA GeForce RTX 4070").Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Milliseconds_are_printed_with_a_dot_in_any_regional_format()
    {
        // A Windows set to Vietnamese once failed a dozen verification checks over a decimal comma.
        Assert.Equal("8.00", FrameBudget.Ms(8));
        Assert.Equal("63.70", FrameBudget.Ms(63.7));
    }

    // ---------------------------------------------------------------------------------------------
    // the report CI reads back
    // ---------------------------------------------------------------------------------------------

    private static FrameBenchReport Scene(string id, double p95, int width = 1920, int height = 1080)
    {
        // 100 ascending samples whose 95th is exactly p95, so a comparison is judged by the number it names.
        var samples = new List<double>();
        for (var i = 1; i <= 100; i++) samples.Add(p95 * i / 95);
        // The verdict and its sentence come from the gate itself, so the printed lines below are the lines the
        // app would print rather than something a fixture invented.
        var (verdict, note) = FrameBudget.DefaultLook.Judge(true, p95, "Microsoft Basic Render Driver");
        return new FrameBenchReport
        {
            Scene = id, Preset = "", Description = id, Width = width, Height = height, Frames = 100, WarmupFrames = 24,
            WarmupMs = 1500, Particles = 321, BudgetP95LimitMs = FrameBudget.DefaultLook.P95LimitMs, Verdict = verdict, VerdictNote = note,
            Stats = FrameStats.From(samples), Samples = samples
        };
    }

    private static FrameBenchRun Run(bool software, params FrameBenchReport[] scenes) => new()
    {
        TakenUtc = "2026-10-01T00:00:00.0000000Z",
        AppVersion = "0.4.0",
        Adapter = software ? "Microsoft Basic Render Driver" : "NVIDIA GeForce RTX 4070",
        Warp = software,
        SoftwareAdapter = software,
        ShaderCompileMs = 1234.5,
        Scenes = scenes
    };

    [Fact]
    public void A_report_survives_the_trip_through_json()
    {
        var run = Run(true, Scene("default", 60), Scene("heavy", 120));

        Assert.True(FrameBenchRun.TryParse(run.ToJson(), out var reread));
        Assert.Equal(run.Adapter, reread.Adapter);
        Assert.True(reread.SoftwareAdapter);
        Assert.True(reread.Warp);
        Assert.Equal(1234.5, reread.ShaderCompileMs, 3);
        Assert.Equal(2, reread.Scenes.Count);

        var heavy = reread.Scene("heavy");
        Assert.NotNull(heavy);
        Assert.True(heavy!.Measured);
        Assert.Equal(1920, heavy.Width);
        Assert.Equal(1080, heavy.Height);
        Assert.Equal(100, heavy.Frames);
        Assert.Equal(100, heavy.Samples.Count);
        Assert.Equal(run.Scene("heavy")!.Stats.P95Ms, heavy.Stats.P95Ms, 3);
        Assert.Equal(321, heavy.Particles);
        Assert.Null(reread.Scene("cinematic"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"schema\":1}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"scenes\":[]}")]
    [InlineData("{\"scenes\":[{\"scene\":\"default\"}]}")]
    public void A_baseline_that_is_missing_truncated_or_somebody_elses_file_is_simply_no_baseline(string? json)
    {
        // The run then has nothing to compare with. Crashing instead would turn a stale cache into a red build.
        Assert.False(FrameBenchRun.TryParse(json, out _));
    }

    [Fact]
    public void A_run_whose_budget_was_exceeded_says_so()
    {
        var failing = new FrameBenchReport { Scene = "default", Width = 1920, Height = 1080, Verdict = FrameVerdict.Fail, Stats = FrameStats.From([9]) };

        Assert.True(failing.Failed);
        Assert.True(Run(false, failing).Failed);
        Assert.False(Run(true, Scene("default", 60)).Failed);
        Assert.False(new FrameBenchRun().Measured);
    }

    [Fact]
    public void Every_line_the_gate_prints_carries_one_of_the_suites_own_prefixes()
    {
        var lines = Run(true, Scene("default", 60)).LogLines(null);

        Assert.Equal(4, lines.Count); // the machine, then the measurement, the budget and the comparison
        Assert.All(lines, line => Assert.Matches("^(NOTE|PASS|WARN|FAIL) perf: ", line));
        Assert.Contains(lines, line => line.Contains("p95 60.00 ms", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("60,00", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("NOTE perf: the frame was drawn by ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_with_no_frames_prints_a_failure_rather_than_a_fast_number()
    {
        var empty = new FrameBenchRun { Scenes = [new FrameBenchReport { Scene = "default", Width = 1920, Height = 1080 }] };

        Assert.Contains(empty.LogLines(null), line => line.StartsWith("FAIL perf: the scene 'default' measured no frames", StringComparison.Ordinal));
        Assert.Contains(new FrameBenchRun().LogLines(null), line => line.StartsWith("FAIL perf: no scene was measured", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    // the relative gate: the only thing a WARP runner can judge absolutely
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_run_compared_with_the_report_it_just_wrote_is_steady()
    {
        var run = Run(true, Scene("default", 60));

        Assert.True(FrameBenchRun.TryParse(run.ToJson(), out var reread));
        var (trend, delta, _) = reread.Compare(run, reread.Scene("default")!);

        Assert.Equal(FrameTrend.Steady, trend);
        Assert.Equal(0, delta, 3);
    }

    [Fact]
    public void Half_again_as_slow_as_the_previous_run_is_a_regression()
    {
        var baseline = Run(true, Scene("default", 60));
        var slower = Run(true, Scene("default", 96));

        var (trend, delta, line) = slower.Compare(baseline, slower.Scene("default")!);

        Assert.Equal(FrameTrend.Regression, trend);
        Assert.Equal(60, delta, 3);
        Assert.Contains("regression", line, StringComparison.Ordinal);
        Assert.Contains(slower.LogLines(baseline), line => line.StartsWith("WARN perf: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_run_that_holds_or_beats_the_previous_p95_is_not_a_regression()
    {
        var baseline = Run(true, Scene("default", 60));
        var held = Run(true, Scene("default", 62));
        var quicker = Run(true, Scene("default", 50));

        Assert.Equal(FrameTrend.Steady, held.Compare(baseline, held.Scene("default")!).Trend);
        Assert.Equal(FrameTrend.Faster, quicker.Compare(baseline, quicker.Scene("default")!).Trend);
        Assert.DoesNotContain(quicker.LogLines(baseline), line => line.StartsWith("WARN", StringComparison.Ordinal));
    }

    [Fact]
    public void A_first_run_has_nothing_to_compare_with()
    {
        var run = Run(true, Scene("default", 60));

        var (trend, _, line) = run.Compare(null, run.Scene("default")!);

        Assert.Equal(FrameTrend.Unknown, trend);
        Assert.Contains("no previous measurement", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_different_machines_or_two_different_sizes_are_not_comparable()
    {
        // The percentage would describe the two machines rather than the code between the two commits.
        var baseline = Run(true, Scene("default", 60));

        Assert.Equal(FrameTrend.Unknown, baseline.Compare(Run(false, Scene("default", 60)), Scene("default", 60)).Trend);
        Assert.Equal(FrameTrend.Unknown, Run(false, Scene("default", 60)).Compare(baseline, Scene("default", 60)).Trend);
        // The size lives in the scene being compared, so both halves of this pair have to be spelled out:
        // a 4K run compared with a 4K scene is comparable, and only 4K against 1080p is not.
        var fourK = Run(true, Scene("default", 60, 3840, 2160));
        Assert.Equal(FrameTrend.Unknown, baseline.Compare(fourK, baseline.Scene("default")!).Trend);
        Assert.Equal(FrameTrend.Unknown, fourK.Compare(baseline, fourK.Scene("default")!).Trend);
    }

    [Fact]
    public void A_scene_the_previous_run_did_not_measure_is_not_compared_either()
    {
        var baseline = Run(true, Scene("default", 60));

        Assert.Equal(FrameTrend.Unknown, baseline.Compare(Run(true, Scene("heavy", 10)), Scene("default", 60)).Trend);
        Assert.Equal(FrameTrend.Unknown, baseline.Compare(new FrameBenchRun { SoftwareAdapter = true }, Scene("default", 60)).Trend);
    }
}
