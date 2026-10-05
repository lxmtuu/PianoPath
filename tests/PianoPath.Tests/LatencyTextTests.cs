using Xunit;

namespace PianoPath.Tests;

public sealed class LatencyTextTests
{
    private static LatencyReport Report(LatencyOutcome outcome, double delay = 0, double held = 0, int suggested = 0, bool clamped = false) =>
        new(new LatencyEstimate(outcome, delay, outcome == LatencyOutcome.Measured ? 1 : 0, 20), held, suggested, clamped);

    [Fact]
    public void A_run_in_progress_says_how_many_of_how_many_and_never_more_than_the_wanted_number()
    {
        var text = LatencyText.Collecting(7);
        Assert.Contains("7", text);
        Assert.Contains(LatencyMeasurement.TargetPresses.ToString(), text);
        Assert.DoesNotContain((LatencyMeasurement.TargetPresses + 9).ToString(), LatencyText.Collecting(LatencyMeasurement.TargetPresses + 9));
    }

    [Fact]
    public void A_measured_delay_is_told_with_the_cameras_share_the_apps_share_and_the_setting()
    {
        var text = LatencyText.Result(Report(LatencyOutcome.Measured, delay: .083, held: 22, suggested: 105));
        Assert.Contains("83", text);
        Assert.Contains("22", text);
        Assert.Contains("105", text);
        var clamped = LatencyText.Result(Report(LatencyOutcome.Measured, delay: .35, held: 15, suggested: 300, clamped: true));
        Assert.Contains("350", clamped);
        Assert.Contains("300", clamped);
        Assert.NotEqual(text, clamped);
    }

    [Fact]
    public void Every_way_a_run_can_fail_has_its_own_advice()
    {
        var texts = new[] { LatencyOutcome.NotEnoughNotes, LatencyOutcome.NotEnoughPicture, LatencyOutcome.NoMotion, LatencyOutcome.Unclear }
            .Select(outcome => LatencyText.Result(Report(outcome))).ToArray();
        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(texts.Length, texts.Distinct().Count());
        Assert.Contains(LatencyMeasurement.TargetPresses.ToString(), texts[0]);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(.0834, 83)]
    [InlineData(.4, 400)]
    [InlineData(-1.0, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Seconds_are_whole_milliseconds_and_nothing_wild(double seconds, int expected) => Assert.Equal(expected, LatencyText.Rounded(seconds));
}
