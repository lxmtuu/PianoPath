namespace PianoPath;

internal static partial class ArOmittedEffects
{
    /// <summary>
    /// The same list for a whole look. Needs <see cref="PianoVisualSettings"/> and therefore the WPF
    /// settings type, so it lives apart from the pure half the Linux test project links — the same split
    /// <c>GpuOnlyEffects</c> uses. The thresholds mirror the renderer's own: it normalizes these percentages
    /// to 0..1 and skips anything at or under .01, so a value of 1% or less is already "off" there and must
    /// not be reported as something the overlay dropped.
    /// </summary>
    internal static IReadOnlyList<string> Omitted(PianoVisualSettings settings) =>
        Omitted(
            projected: settings.ArNotesProjected,
            trail: settings.FallingTrail != "None" && settings.FallingTrailIntensity > 1,
            ghosts: settings.FallingGhost && settings.FallingGhostAmount > 1,
            landingGlow: settings.NoteLandingGlow && settings.NoteLandingGlowAmount > 1,
            labels: settings.ShowNoteLabels,
            // The star field belongs to the stage background pass, so it is absent whenever the camera is
            // the background — which is the only time this list is shown.
            stars: settings.ShowBackground && settings.ShowStars && settings.StarDensity > 0);
}
