namespace PianoPath;

/// <summary>
/// Minimum note scaling (AR-S4 remainder, the Keysight "minimum note scaling" and "new note truncates existing note"):
/// a note always keeps at least <c>minAspect × width</c> of length, even when the key is released almost at once, and it
/// grows from its head, the edge that meets the keys. A note of the same pitch that follows cuts that growth short, so two
/// quick notes stay two notes. Zero leaves every note at its own length.
/// </summary>
internal static class ArNoteMinimum
{
    /// <summary>
    /// The extent of one note after the minimum is applied, in scene units with y running down the scene.
    /// <paramref name="headAtBottom"/> is true for falling notes (the head is the lower edge; the note grows upward) and false
    /// for rising ones. <paramref name="limit"/> is the edge of the next note of the same pitch on the side the note grows
    /// towards, or NaN when there is none. A note already long enough, or one the limit leaves no room to grow, is unchanged.
    /// </summary>
    internal static (double Top, double Bottom) Extend(double top, double bottom, double width, double minAspect, bool headAtBottom, double limit)
    {
        if (!(minAspect > 0) || !(width > 0) || !(bottom > top) || !double.IsFinite(width)) return (top, bottom);
        var need = minAspect * width;
        if (bottom - top >= need) return (top, bottom);
        if (headAtBottom)
        {
            var grown = bottom - need;
            if (!double.IsNaN(limit)) grown = Math.Max(grown, limit);
            return (Math.Min(top, grown), bottom);
        }
        else
        {
            var grown = top + need;
            if (!double.IsNaN(limit)) grown = Math.Min(grown, limit);
            return (top, Math.Max(bottom, grown));
        }
    }
}
