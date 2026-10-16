namespace PianoPath;

/// <summary>Pure deterministic input plan. No renderer, musical ownership model or imported MIDI.</summary>
internal static class AuxParticleTimeline
{
    internal enum Channel { Held, Impact, Release }
    internal sealed record Route(string Id, Channel Source, string Effect, byte Shape, bool Wisp = false);
    internal readonly record struct Tick(bool Pressed, bool Pedal, bool Down, bool Up, bool Impact);
    internal const int Fps = 60, ReleaseFrame = 21;
    internal static IReadOnlyList<Route> Routes { get; } = Array.AsReadOnly(new[]
    {
        new Route("wisps", Channel.Held, "Wisps", 0, true),
        new Route("flames", Channel.Held, "Flames", 4),
        new Route("morph-shatter", Channel.Impact, "Shatter", 6),
        new Route("morph-melt", Channel.Impact, "Melt", 5),
        new Route("morph-bounce", Channel.Impact, "Bounce", 1),
        new Route("release-float-up", Channel.Release, "Float Up", 5),
        new Route("release-dissolve", Channel.Release, "Dissolve", 5),
        new Route("release-smoke", Channel.Release, "Smoke", 3)
    });
    internal static IReadOnlyList<string> Phases { get; } = Array.AsReadOnly(new[] { "off", "active", "pedal-tail", "expired" });
    internal static double SampleTime(Route route, string phase) => phase switch
    {
        "off" or "active" => route.Source switch { Channel.Held => .6, Channel.Impact => .05, _ => .4 },
        "pedal-tail" => .65,
        "expired" => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
    internal static Tick At(Route route, string phase, int frame)
    {
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        _ = SampleTime(route, phase); // reject unknown phases rather than silently changing the input
        var releases = route.Source == Channel.Release || phase is "pedal-tail" or "expired";
        var pressed = route.Source != Channel.Impact && (!releases || frame < ReleaseFrame);
        return new Tick(pressed, releases && frame >= ReleaseFrame,
            route.Source == Channel.Release && frame == 0,
            route.Source == Channel.Release && frame == ReleaseFrame,
            route.Source == Channel.Impact && frame == 0);
    }
}
