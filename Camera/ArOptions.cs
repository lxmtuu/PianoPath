namespace PianoPath;

/// <summary>The two ways the overlay is used, stored by these names.</summary>
internal static class ArModes
{
    /// <summary>The notes to come fall onto the real keys, and the keys light as they are played.</summary>
    internal const string Learn = "Learn";
    /// <summary>Free play: only the lit keys, no notes to come. For filming and streaming.</summary>
    internal const string Perform = "Perform";

    internal static readonly string[] All = [Learn, Perform];

    /// <summary>The mode a stored text names; Learn for anything the app does not know.</summary>
    internal static string Normalize(string? mode) => All.Contains(mode) ? mode! : Learn;
}

/// <summary>
/// What the player chose for the overlay, in the units the settings page shows, and what that means for drawing it.
/// A plain value with no settings class behind it so the arithmetic is tested on any machine; the settings class holds
/// the same six numbers and hands them over through <c>PianoVisualSettings.ArOptionsValue</c>.
/// </summary>
/// <param name="Enabled">The overlay is on: the camera is the background and the notes and keys are drawn over it.</param>
/// <param name="Mode">One of <see cref="ArModes.All"/>.</param>
/// <param name="Opacity">How solid the notes and keys are, percent.</param>
/// <param name="Lookahead">How many seconds before it sounds a note starts to show.</param>
/// <param name="KeyLight">How strongly a played key is tinted, percent.</param>
/// <param name="Delay">How late the camera's picture is, milliseconds: what the overlay is held back by so that it shows what the picture shows.</param>
/// <param name="NoteWidthVelocity">How a note's width follows the strike's velocity (a <see cref="GraphVariable"/>); null or flat keeps every note the same width.</param>
/// <param name="NoteFadeVelocity">How a note's fade-in follows the strike's velocity (a <see cref="GraphVariable"/>); null or flat keeps the fade the same for every note.</param>
internal readonly record struct ArOptions(bool Enabled, string Mode, double Opacity, double Lookahead, double KeyLight, double Delay = 0,
    GraphVariable? NoteWidthVelocity = null, GraphVariable? NoteFadeVelocity = null)
{
    internal const double OpacityMin = 10, OpacityMax = 100, OpacityDefault = 100;
    internal const double LookaheadMin = 1, LookaheadMax = 6, LookaheadDefault = 3;
    internal const double KeyLightMin = 0, KeyLightMax = 100, KeyLightDefault = 60;
    internal const double DelayMin = 0, DelayMax = 300, DelayDefault = 0;

    internal static readonly ArOptions Default = new(false, ArModes.Learn, OpacityDefault, LookaheadDefault, KeyLightDefault);

    /// <summary>The same choices pulled into range: a number that is not a number takes its default, an unknown mode becomes Learn.</summary>
    internal ArOptions Normalised() => new(Enabled, ArModes.Normalize(Mode),
        Fit(Opacity, OpacityMin, OpacityMax, OpacityDefault), Fit(Lookahead, LookaheadMin, LookaheadMax, LookaheadDefault),
        Fit(KeyLight, KeyLightMin, KeyLightMax, KeyLightDefault), Fit(Delay, DelayMin, DelayMax, DelayDefault));

    internal static double Fit(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    /// <summary>Learn draws the notes to come; Perform leaves them out and keeps the lit keys.</summary>
    internal bool ShowsNotes => ArModes.Normalize(Mode) == ArModes.Learn;

    /// <summary>The layer's opacity, 0..1.</summary>
    internal float LayerOpacity => (float)(Normalised().Opacity / 100);

    /// <summary>How strongly a played key is tinted, 0..1.</summary>
    internal float KeyLightStrength => (float)(Normalised().KeyLight / 100);

    /// <summary>
    /// The camera delay in seconds. A camera shows the keyboard some tens of milliseconds late, so a key lit the moment it is played
    /// would glow before the finger in the picture has reached it; the overlay is drawn as the world was this long ago instead,
    /// which is what the picture shows. The sound is never held back.
    /// </summary>
    internal double DelaySeconds => Normalised().Delay / 1000;

    /// <summary>The falling notes' shape: the default one, reaching as far above the keys as the lookahead takes them to fall, with the width and fade graphs of this choice.</summary>
    internal ArNoteStyle NoteStyle => ArNoteStyle.Default with
    {
        Reach = Normalised().Lookahead * ArNoteStyle.Default.FallSpeed,
        WidthVelocity = (Normalised().NoteWidthVelocity ?? GraphVariable.Flat).Normalised(),
        FadeVelocity = (Normalised().NoteFadeVelocity ?? GraphVariable.Flat).Normalised()
    };
}
