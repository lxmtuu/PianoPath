namespace PianoPath;

/// <summary>
/// The visual effects only the GPU stage draws. A take that falls back to the software stage drops them,
/// so the recording line and the run log name them before a frame is written instead of losing them
/// silently; the toggles themselves keep working, because the same preset can still be shown live on the
/// GPU stage and only lose these three in a transparent take.
/// </summary>
internal static class GpuOnlyEffects
{
    /// <summary>
    /// The localization keys of the GPU-only effects this preset has switched on, in interface order.
    /// An empty list means a software-rendered take loses nothing visible.
    /// </summary>
    internal static IReadOnlyList<string> Omitted(PianoVisualSettings settings)
    {
        var missing = new List<string>(3);
        if (settings.NoteShimmer) missing.Add("Note shimmer");
        if (settings.NoteLandingGlow) missing.Add("Landing glow");
        if (settings.ShootingStars) missing.Add("Shooting stars");
        return missing;
    }
}
