namespace PianoPath;

/// <summary>
/// The fade-over-lifetime of a live press bar (AR‑6.2 of docs/AR-OVERLAY-ROADMAP.md §14, the Keysight keypress
/// "fade over lifetime" graph that runs left→right while the key is held and right→left on release): an opted-in graph
/// follows how far the bar has stretched, then walks backwards after the finger leaves. Without a graph, held bars stay
/// fully visible regardless of how long the key is held, and the builders' release dim alone takes them away.
/// </summary>
internal static class ArLiveFade
{
    /// <summary>
    /// The opacity factor of one live bar, multiplied by the builders' own release dim and velocity fade.
    /// <paramref name="heldFraction"/> is how far the bar has stretched towards its full reach (0 at the moment the key
    /// went down, 1 at full stretch); <paramref name="releaseFraction"/> is how far the release has travelled through its
    /// life (only used when <paramref name="down"/> is false). An input that is not a number reads as zero. Without a
    /// graph the bar stays fully solid; each builder applies release dim separately.
    /// </summary>
    internal static double Opacity(GraphVariable? lifetime, double heldFraction, bool down, double releaseFraction)
    {
        var held = double.IsFinite(heldFraction) ? Math.Clamp(heldFraction, 0, 1) : 0;
        if (lifetime is not { } graph) return 1;
        graph = graph.Normalised();
        if (!graph.Graph) return Math.Clamp(graph.Max, 0, 1);
        // Held: the walk runs left→right with the bar's stretch. Released: it replays right→left from where the finger
        // left it, so the graph's start value is where a bar about to disappear lands.
        var walk = down ? held : held * (1 - (double.IsFinite(releaseFraction) ? Math.Clamp(releaseFraction, 0, 1) : 0));
        return Math.Clamp(graph.Sample(walk), 0, 1);
    }
}
