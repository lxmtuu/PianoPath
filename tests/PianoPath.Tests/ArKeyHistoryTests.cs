using System.Numerics;
using Xunit;

namespace PianoPath.Tests;

public sealed class ArKeyHistoryTests
{
    private static Vector4[] Keys(params (int Pitch, float R, float G, float B, float Glow)[] lit)
    {
        var keys = new Vector4[128];
        foreach (var (pitch, r, g, b, glow) in lit) keys[pitch] = new Vector4(r, g, b, glow);
        return keys;
    }

    private static Vector4[] SampleAt(ArKeyHistory history, double time)
    {
        var into = new Vector4[128];
        history.Sample(time, into);
        return into;
    }

    [Fact]
    public void Nothing_held_answers_dark_for_every_time()
    {
        var history = new ArKeyHistory();
        Assert.All(SampleAt(history, 5), key => Assert.Equal(Vector4.Zero, key));
        history.Record(1, Keys());
        Assert.All(SampleAt(history, 0), key => Assert.Equal(Vector4.Zero, key));
        Assert.All(SampleAt(history, 1), key => Assert.Equal(Vector4.Zero, key));
        Assert.All(SampleAt(history, 9), key => Assert.Equal(Vector4.Zero, key));
    }

    [Fact]
    public void A_key_that_lights_at_one_time_is_seen_lit_a_delay_later_and_not_before()
    {
        var history = new ArKeyHistory();
        const double Frame = 1 / 60.0;
        for (var i = 0; i < 120; i++)
        {
            var time = 10 + i * Frame;
            history.Record(time, i < 30 ? Keys() : Keys((60, .5f, .25f, .75f, 1)));         // pressed at frame 30
            var pressedAt = 10 + 30 * Frame;
            var seen = SampleAt(history, time - .1)[60];
            // what the camera shows at `time` is the world 0.1 s earlier
            if (time - .1 < pressedAt - 1e-9) Assert.True(seen.W < 1e-6, $"frame {i}: lit too early ({seen.W})");
            else Assert.True(seen.W > .99, $"frame {i}: not lit yet ({seen.W})");
        }
    }

    [Fact]
    public void Between_two_pictures_the_glow_is_blended_by_time_and_the_colour_comes_from_the_lit_side()
    {
        var history = new ArKeyHistory();
        history.Record(1.00, Keys());
        history.Record(1.10, Keys((60, .2f, .4f, .8f, 1)));
        var half = SampleAt(history, 1.05)[60];
        Assert.Equal(.5f, half.W, 5);
        Assert.Equal(new Vector3(.2f, .4f, .8f), new Vector3(half.X, half.Y, half.Z));        // a key coming on does not start from black
        var quarter = SampleAt(history, 1.025)[60];
        Assert.Equal(.25f, quarter.W, 5);
        history.Record(1.20, Keys((60, .6f, .4f, .0f, .5f)));
        var between = SampleAt(history, 1.15)[60];
        Assert.Equal(.75f, between.W, 5);
        Assert.Equal(.4f, between.X, 5);                                                      // both lit: the colours are blended too
        history.Record(1.30, Keys());
        var fading = SampleAt(history, 1.25)[60];
        Assert.Equal(.25f, fading.W, 5);
        Assert.Equal(.6f, fading.X, 5);                                                       // a key going out keeps its own colour
    }

    [Fact]
    public void Asking_for_a_time_before_the_first_picture_is_dark_and_after_the_last_is_the_last()
    {
        var history = new ArKeyHistory();
        history.Record(5, Keys((64, 1, 1, 1, 1)));
        history.Record(5.1, Keys((64, 1, 1, 1, .8f)));
        Assert.Equal(0f, SampleAt(history, 4.9)[64].W);
        Assert.Equal(.8f, SampleAt(history, 5.1)[64].W, 5);
        Assert.Equal(.8f, SampleAt(history, 50)[64].W, 5);
    }

    [Fact]
    public void A_long_pause_between_two_pictures_is_not_blended()
    {
        var history = new ArKeyHistory();
        history.Record(1, Keys((60, 1, 0, 0, 1)));
        history.Record(1 + ArKeyHistory.MaxGap + .5, Keys());                                  // the loop was idle in between
        Assert.Equal(0f, SampleAt(history, 1.3)[60].W);                                        // not "half lit" in the middle of nothing
        Assert.Equal(1f, SampleAt(history, 1)[60].W);
    }

    [Fact]
    public void Old_pictures_are_dropped_and_their_memory_reused()
    {
        var history = new ArKeyHistory();
        for (var i = 0; i < 2000; i++) history.Record(i / 100.0, Keys((60, 1, 1, 1, i % 2)));
        Assert.InRange(history.Count, 1, (int)(ArKeyHistory.Reach * 100) + 2);
        Assert.Equal(1f, SampleAt(history, 19.99)[60].W + 0f, 5);                              // the newest is still there
        Assert.Equal(0f, SampleAt(history, 5)[60].W);                                          // far older than the reach: gone
    }

    [Fact]
    public void A_clock_that_goes_back_starts_the_history_again_and_wild_input_is_ignored()
    {
        var history = new ArKeyHistory();
        history.Record(100, Keys((60, 1, 1, 1, 1)));
        history.Record(1, Keys());                                                              // a new run
        Assert.Equal(1, history.Count);
        Assert.Equal(0f, SampleAt(history, 100)[60].W);
        history.Record(double.NaN, Keys((60, 1, 1, 1, 1)));
        history.Record(double.PositiveInfinity, Keys((60, 1, 1, 1, 1)));
        Assert.Equal(1, history.Count);
        Assert.All(SampleAt(history, double.NaN), key => Assert.Equal(Vector4.Zero, key));
        history.Record(1.5, [new Vector4(1, 1, 1, 1)]);                                          // fewer keys than the history holds: the rest are dark
        Assert.Equal(0f, SampleAt(history, 1.5)[100].W);
        history.Clear();
        Assert.Equal(0, history.Count);
    }
}
