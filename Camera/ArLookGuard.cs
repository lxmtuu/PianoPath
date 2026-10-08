namespace PianoPath;

/// <summary>
/// The guard between a look and the AR overlay (AR-S3, the KeySight rule that a style the AR cannot draw
/// is left out rather than drawn wrong — roadmap §14.12). The overlay projects flat quads: it has no PBR,
/// no facets, no refraction, so a note style that needs them keeps its flat fallback instead of claiming
/// a shape the projection cannot give it. Pure: which styles are ported is a fact about the code, and the
/// guard says it without a GPU or a camera.
/// </summary>
internal static class ArLookGuard
{
    /// <summary>The note styles the app ships (mirrors <c>PianoVisualSettings.NoteStyles</c>, kept here so this file needs no WPF-leaning types).</summary>
    internal static readonly string[] NoteStyles = ["Solid", "Neon", "Glass", "Fire", "Gem", "CrystalOutline"];

    /// <summary>The note styles with no AR variant: the overlay draws them flat.</summary>
    internal static readonly string[] UnportedNoteStyles = ["Gem", "CrystalOutline"];

    /// <summary>The style the overlay draws a note with when the look's own style has no AR variant.</summary>
    internal const string ArFallbackStyle = "Neon";

    /// <summary>Whether a note style has an AR variant to honour.</summary>
    internal static bool IsPortedToAr(string? style) => style is not null && !UnportedNoteStyles.Contains(style);

    /// <summary>The style to draw in AR for a look's style: its own when ported, the flat fallback otherwise.</summary>
    internal static string StyleForAr(string? style) => IsPortedToAr(style) ? style! : ArFallbackStyle;

    /// <summary>The style name for the index the GPU look holds (mirrors the lookup on the settings side).</summary>
    internal static string StyleName(int noteStyleIndex)
    {
        var index = Math.Clamp(noteStyleIndex, 0, NoteStyles.Length - 1);
        return NoteStyles[index];
    }

    /// <summary>The style index the AR layer uses for a look: the look's own when ported, the flat fallback otherwise.</summary>
    internal static int StyleIndexForAr(int noteStyleIndex) => Array.IndexOf(NoteStyles, StyleForAr(StyleName(noteStyleIndex)));
}
