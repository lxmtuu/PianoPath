using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The fade-over-lifetime of a live press bar (AR‑6.2): nothing changes without the graph, a held key walks it
/// forward with the bar's stretch, and letting the key go walks it backwards over the release. The two builders
/// multiply it with their own release dim; these facts pin the factor itself.
/// </summary>
public sealed class ArLiveFadeTests
{
    private static readonly GraphVariable SoftToFull = new(Graph: true, Start: 0, Stop: 1, Min: .2, Max: 1);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(.25, .75)]
    [InlineData(.5, .5)]
    [InlineData(.92, .08)]                            // the faint floor a held key never falls under
    [InlineData(1, .08)]
    public void Without_a_graph_the_legacy_length_fade_is_what_it_always_was(double heldFraction, double expected)
    {
        Assert.Equal(expected, ArLiveFade.Opacity(null, heldFraction, true, 0), 9);
    }

    [Fact]
    public void The_legacy_path_ignores_the_release_fraction_because_the_release_dim_handles_it()
    {
        Assert.Equal(ArLiveFade.Opacity(null, .5, false, 0), ArLiveFade.Opacity(null, .5, false, .75), 12);
    }

    [Fact]
    public void A_flat_value_is_that_value_whatever_the_bar_does()
    {
        var flat = new GraphVariable(Graph: false, Min: 0, Max: .7);
        Assert.Equal(.7, ArLiveFade.Opacity(flat, 0, true, 0), 9);
        Assert.Equal(.7, ArLiveFade.Opacity(flat, .7, true, 0), 9);
        Assert.Equal(.7, ArLiveFade.Opacity(flat, .7, false, .35), 9);
    }

    [Fact]
    public void While_the_key_is_held_the_graph_walks_forward_with_the_stretch()
    {
        Assert.Equal(.2, ArLiveFade.Opacity(SoftToFull, 0, true, 0), 9);
        Assert.Equal(.6, ArLiveFade.Opacity(SoftToFull, .5, true, 0), 9);
        Assert.Equal(1, ArLiveFade.Opacity(SoftToFull, 1, true, 0), 9);
    }

    [Fact]
    public void The_release_walks_the_same_graph_backwards_from_where_the_finger_left_it()
    {
        // Let go at half stretch: the walk stands at .5 when the finger leaves and reaches 0 when the release completes.
        Assert.Equal(.6, ArLiveFade.Opacity(SoftToFull, .5, false, 0), 9);
        Assert.Equal(.4, ArLiveFade.Opacity(SoftToFull, .5, false, .5), 9);
        Assert.Equal(.2, ArLiveFade.Opacity(SoftToFull, .5, false, 1), 9);
    }

    [Fact]
    public void A_let_go_at_full_stretch_fades_back_to_the_graphs_start_not_to_zero_solidity()
    {
        // The graph's own values are the ends of the walk; the builder's release dim still takes the bar away.
        Assert.Equal(1, ArLiveFade.Opacity(SoftToFull, 1, false, 0), 9);
        Assert.Equal(.2, ArLiveFade.Opacity(SoftToFull, 1, false, 1), 9);
    }

    [Fact]
    public void A_graph_falling_over_lifetime_is_just_the_same_walk_with_its_ends_swapped()
    {
        var dimmerAsItGrows = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: 1, Max: .2);
        Assert.Equal(1, ArLiveFade.Opacity(dimmerAsItGrows, 0, true, 0), 9);
        Assert.Equal(.2, ArLiveFade.Opacity(dimmerAsItGrows, 1, true, 0), 9);
        Assert.Equal(1, ArLiveFade.Opacity(dimmerAsItGrows, 1, false, 1), 9);   // released, the bar brightens back
    }

    [Fact]
    public void Numbers_that_are_not_numbers_read_as_their_walks_start_and_the_answer_stays_in_range()
    {
        Assert.Equal(1, ArLiveFade.Opacity(null, double.NaN, true, 0), 9);
        Assert.Equal(.08, ArLiveFade.Opacity(null, 2, true, 0), 9);
        Assert.Equal(.2, ArLiveFade.Opacity(SoftToFull, double.NaN, true, 0), 9);
        // A release age that is not a number reads as "just let go": the walk stands at the held spot.
        Assert.Equal(.6, ArLiveFade.Opacity(SoftToFull, .5, false, double.NaN), 9);
        Assert.Equal(.6, ArLiveFade.Opacity(SoftToFull, .5, false, double.PositiveInfinity), 9);
        var junk = new GraphVariable(Graph: true, Start: double.NaN, Stop: 1, Min: 0, Max: 2);
        Assert.Equal(1, ArLiveFade.Opacity(junk, .5, true, 0), 9);                 // a graph that makes no sense is a flat full
    }
}
