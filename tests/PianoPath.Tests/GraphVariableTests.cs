using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// AR-S2: the graph parameter of the redesigned overlay (docs/AR-OVERLAY-ROADMAP.md §14.5). A parameter
/// is flat or a curve over an input that always runs 0..1 — nothing else walks it.
/// </summary>
public class GraphVariableTests
{
    private static void Near(double expected, double actual, double tolerance = 1e-9) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void A_flat_parameter_is_always_its_max()
    {
        var flat = new GraphVariable(Graph: false, Min: .2, Max: .7);
        Near(.7, flat.Sample(0));
        Near(.7, flat.Sample(.5));
        Near(.7, flat.Sample(1));
        Near(1, GraphVariable.Flat.Sample(.3)); // the default is a flat 1
        Near(1, GraphVariable.Flat.Sample(0));
    }

    [Fact]
    public void A_graph_clamps_outside_its_window()
    {
        var graph = new GraphVariable(Graph: true, Start: .25, Stop: .75, Min: .2, Max: .8);
        Near(.2, graph.Sample(0));       // before the window
        Near(.2, graph.Sample(.25));    // at its start
        Near(.8, graph.Sample(.75));    // at its end
        Near(.8, graph.Sample(1));       // past it
        Near(.8, graph.Sample(5));
    }

    [Fact]
    public void A_graph_walks_straight_between_its_ends()
    {
        var graph = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .2, Max: .8);
        Near(.5, graph.Sample(.5));
        Near(.2, graph.Sample(0));
        Near(.8, graph.Sample(1));
        var halfWindow = new GraphVariable(Graph: true, Start: .5, Stop: 1, Min: 0, Max: 1);
        Near(0, halfWindow.Sample(.5));
        Near(.5, halfWindow.Sample(.75));
        Near(1, halfWindow.Sample(1));
    }

    [Fact]
    public void A_negative_curve_starts_slow_and_ends_fast_a_positive_curve_the_other_way()
    {
        var slowIn = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: 0, Max: 1, Curve: -1);
        var fastIn = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: 0, Max: 1, Curve: 1);
        Assert.True(slowIn.Sample(.25) < .25, "A -1 curve should sit under the straight line early on.");
        Assert.True(slowIn.Sample(.75) - slowIn.Sample(.5) > slowIn.Sample(.5) - slowIn.Sample(0),
            "A -1 curve should cover more ground in its second half than in its first: slow in, fast out.");
        Assert.True(fastIn.Sample(.25) > .25, "A +1 curve should sit over the straight line early on.");
        Assert.True(fastIn.Sample(.5) - fastIn.Sample(0) > fastIn.Sample(.75) - fastIn.Sample(.5),
            "A +1 curve should cover more ground in its first half than in its second: fast in, slow out.");
        Near(0, slowIn.Sample(0)); Near(1, slowIn.Sample(1));   // the ends never move
        Near(0, fastIn.Sample(0)); Near(1, fastIn.Sample(1));
        Near(.5, new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: 0, Max: 1, Curve: 0).Sample(.5));
        Near(.25, slowIn.Sample(.5), 1e-9);                    // -1 squares the walk
        Near(.5, fastIn.Sample(.25), 1e-9);                    // +1 square-roots it
    }

    [Fact]
    public void An_empty_window_is_a_flat_max_never_a_divide_by_zero()
    {
        var empty = new GraphVariable(Graph: true, Start: .5, Stop: .5, Min: .1, Max: .9);
        Near(.9, empty.Sample(0));
        Near(.9, empty.Sample(.5));
        Near(.9, empty.Sample(1));
        var backwards = new GraphVariable(Graph: true, Start: 1, Stop: 0, Min: .1, Max: .9);
        Near(.9, backwards.Sample(.5));
    }

    [Fact]
    public void Degenerate_inputs_sample_safely()
    {
        var graph = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .2, Max: .8);
        Near(.2, graph.Sample(double.NaN));       // a NaN input reads as zero
        Near(.2, graph.Sample(double.NegativeInfinity));
        Near(.8, graph.Sample(double.PositiveInfinity));
        var nan = new GraphVariable(Graph: true, Start: double.NaN, Stop: double.NaN, Min: double.NaN, Max: double.NaN);
        Assert.True(double.IsFinite(nan.Sample(.5)), "A graph of NaNs should still sample a finite number.");
        Near(1, nan.Normalised().Sample(.5));     // normalising turns it into a flat 1
        var flatNan = new GraphVariable(Graph: false, Max: double.NaN);
        Near(0, flatNan.Sample(.5));              // a flat NaN max reads as zero, not NaN
    }

    [Fact]
    public void Normalised_clamps_the_curve_and_repairs_numbers_that_make_no_sense()
    {
        var wild = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: 0, Max: 1, Curve: 5);
        Near(1, wild.Normalised().Curve);
        var broken = new GraphVariable(Graph: true, Start: double.NaN, Stop: 1, Min: 0, Max: 1);
        Assert.Equal(GraphVariable.Flat with { Max = 1 }, broken.Normalised());
    }
}
