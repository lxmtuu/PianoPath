namespace PianoPath;

/// <summary>
/// The visual effects only the GPU stage draws. A take that falls back to the software stage drops them,
/// so the recording line and the run log name them before a frame is written instead of losing them
/// silently; the toggles themselves keep working, because the same preset can still be shown live on the
/// GPU stage and only lose these three in a transparent take.
/// </summary>
internal static partial class GpuOnlyEffects
{
    /// <summary>
    /// The localization keys of the GPU-only effects a look has switched on, in interface order.
    /// An empty list means a software-rendered take loses nothing visible. The three switches are passed
    /// one by one so this half stays pure: the test project links this file without the WPF settings type.
    /// </summary>
    internal static IReadOnlyList<string> Omitted(bool shimmer, bool landingGlow, bool shootingStars)
    {
        var missing = new List<string>(3);
        if (shimmer) missing.Add("Note shimmer");
        if (landingGlow) missing.Add("Landing glow");
        if (shootingStars) missing.Add("Shooting stars");
        return missing;
    }
}
