namespace PianoPath;

/// <summary>
/// Keeps the AR note-style lookup fail-closed for unknown/future settings. All six shipped style IDs are
/// passed to the shared GPU note-material shader, which shades camera-facing AR patches. This records a
/// shader route, not full material/parameter parity: the typed AR material capability stays Unverified
/// until its own style/value matrix is covered (see docs/AR-OVERLAY-ROADMAP.md).
/// </summary>
internal static class ArLookGuard
{
    /// <summary>The note styles shipped by <c>PianoVisualSettings</c>, in their stable GPU-index order.</summary>
    internal static readonly string[] NoteStyles = new[] { "Solid", "Neon", "Glass", "Fire", "Gem", "CrystalOutline" };

    /// <summary>Style IDs with an implemented branch in the shared shader used by the AR quad pass.</summary>
    internal static readonly string[] PortedNoteStyles = new[] { "Solid", "Neon", "Glass", "Fire", "Gem", "CrystalOutline" };

    /// <summary>The safe appearance used for unknown or not-yet-ported style IDs.</summary>
    internal const string ArFallbackStyle = "Neon";

    /// <summary>Whether a known style ID has a branch in the shared note-material shader used by AR quads.</summary>
    internal static bool IsPortedToAr(string? style) => style is not null
        && NoteStyles.Contains(style)
        && PortedNoteStyles.Contains(style);

    /// <summary>The AR style name: a known shader branch unchanged, otherwise the readable fallback.</summary>
    internal static string StyleForAr(string? style) => IsPortedToAr(style) ? style! : ArFallbackStyle;

    /// <summary>The style name for a valid GPU style index; clamps only when reading an existing look.</summary>
    internal static string StyleName(int noteStyleIndex)
    {
        var index = Math.Clamp(noteStyleIndex, 0, NoteStyles.Length - 1);
        return NoteStyles[index];
    }

    /// <summary>Preserves supported style indices; invalid indices fail closed to the Neon fallback.</summary>
    internal static int StyleIndexForAr(int noteStyleIndex)
    {
        if ((uint)noteStyleIndex >= (uint)NoteStyles.Length)
            return Array.IndexOf(NoteStyles, ArFallbackStyle);
        return Array.IndexOf(NoteStyles, StyleForAr(NoteStyles[noteStyleIndex]));
    }
}
