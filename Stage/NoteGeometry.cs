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

    /// <summary>
    /// Share of the fall height over which a note fades in at the top of the roll. A bar that is just
    /// entering is a thin sliver at 5–8% of the roll; without the ramp it appears at full opacity and
    /// reads as a pop. The GPU shader uses the same fraction of <c>ScreenTime.w</c>.
    /// </summary>
    internal const double EnterFadeShare = .06;

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

    /// <summary>
    /// How many DIPs below the top of the roll the entrance ramp covers. The floor keeps a very short
    /// stage from collapsing the ramp to nothing; the shader has the same one-DIP floor.
    /// </summary>
    internal static double EnterFadePixels(double hitY) => Math.Max(1, hitY * EnterFadeShare);

    /// <summary>
    /// The share of the entrance ramp a bar whose top sits at <paramref name="sceneY"/> has reached, 0..1.
    /// The note body is a gradient inside <c>PsNote</c>; this copy is for the sprites that belong to a note
    /// (its trail, label and shimmer), which a different shader draws and which would otherwise pop in at
    /// full opacity while the body they hang on was still fading.
    /// </summary>
    internal static double EnterFade(double sceneY, double hitY)
    {
        if (!double.IsFinite(sceneY) || !double.IsFinite(hitY)) return 1;
        return Math.Clamp(sceneY / EnterFadePixels(hitY), 0, 1);
    }
}
