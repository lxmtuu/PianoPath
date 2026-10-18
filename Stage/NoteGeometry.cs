namespace PianoPath;

/// <summary>
/// Note geometry shared by the software stage, the GPU stage and their tests. Kept pure so the
/// arithmetic can be checked without WPF or a device.
/// </summary>
internal static class NoteGeometry
{
    /// <summary>A note never grows longer than this, however long the key is held.</summary>
    internal const double MaxNoteSeconds = 20;

    /// <summary>How much of a black key a note may fill; the two percent keeps its neighbours apart.</summary>
    internal const double BlackKeyFill = .98;

    /// <summary>A black key's width in DIPs for a keyboard of this width and white-key count.</summary>
    internal static double BlackKeyWidth(double keyboardWidth, int whiteCount)
        => keyboardWidth / whiteCount * KeyboardLayout.BlackWidthShare;

    /// <summary>
    /// A note's width in DIPs: a share of the average key lane, but never wider than
    /// <see cref="BlackKeyFill"/> of a black key. The lane keeps every pitch the same width; the cap
    /// stops the widest setting from spilling onto the keys next to a black one.
    /// </summary>
    internal static double Width(double lane, double noteWidthFraction, double blackKeyWidth)
        => Math.Min(lane * noteWidthFraction, blackKeyWidth * BlackKeyFill);

    /// <summary>
    /// A note's height in DIPs: its sounding time at the current speed, minus the gap between
    /// consecutive notes, floored at the readable minimum and capped at <see cref="MaxNoteSeconds"/>.
    /// It deliberately does not depend on the window height: a four-second note is a four-second bar
    /// on every screen, and only the part the viewport can show differs.
    /// </summary>
    internal static double Height(double duration, double speed, double gap, double minLength)
    {
        if (!double.IsFinite(duration) || duration <= 0) return minLength;
        return Math.Max(Math.Min(duration, MaxNoteSeconds) * speed - gap, minLength);
    }
}
