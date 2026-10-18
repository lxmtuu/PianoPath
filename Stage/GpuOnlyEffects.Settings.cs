namespace PianoPath;

internal static partial class GpuOnlyEffects
{
    /// <summary>
    /// The same list for a whole look. This half needs <see cref="PianoVisualSettings"/> and therefore the WPF
    /// settings type, so it lives apart from the pure half the Linux test project links.
    /// </summary>
    internal static IReadOnlyList<string> Omitted(PianoVisualSettings settings) =>
        Omitted(settings.NoteShimmer, settings.NoteLandingGlow, settings.ShootingStars);
}
