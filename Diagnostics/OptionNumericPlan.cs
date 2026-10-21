namespace PianoPath;

/// <summary>
/// N12(e): the numeric half of the option matrix. The N12(b) matrix proves every <em>listed</em> value of
/// eleven settings draws; it cannot see a slider, because a slider has no list. This plan names the numeric
/// axes that a note, a bar or an effect is sized or brightened by, and the three values each one is shot at.
///
/// <para>
/// The values are the product's own ranges, not invented ones: <see cref="Minimum"/> and
/// <see cref="Maximum"/> are the endpoints of the dock slider in <c>Ui/MainWindow.Settings.cs</c>, pinned there
/// by <c>tools/check_sources.py</c>. The claim the review makes is the one that catches a hidden floor or
/// clamp: <b>two different slider values on one axis never paint the same picture</b>. That is exactly the
/// failure the software stage had — its held bar floored <c>Note width</c> at 60, so 30% and 45% drew the same
/// bar — and a review that only compared each value with "off" would have passed it.
/// </para>
///
/// <para>
/// Each axis is arranged through a family of the discrete matrix (<see cref="Family"/> plus
/// <see cref="FamilyValue"/>), so the scene, the input and every other layer switch are the ones the flat
/// matrix already pins; only the numeric property is overridden.
/// </para>
/// </summary>
internal static class OptionNumericPlan
{
    /// <summary>
    /// One numeric axis. <paramref name="Scene"/> is the moment it is measured at: a falling note, a held bar,
    /// a live hit or a released key.
    /// </summary>
    internal sealed record Axis(string Id, string Setting, string Family, string FamilyValue,
        OptionMatrixPlan.Scene Scene, double Minimum, double Maximum, IReadOnlyList<double> Values)
    {
        /// <summary>The discrete family whose scene and switches arrange this axis.</summary>
        internal OptionMatrixPlan.Family Arrange => OptionMatrixPlan.FamilyById(Family);
        internal int Frames => OptionMatrixPlan.Frames(Scene);
        internal double Seconds => OptionMatrixPlan.Seconds(Scene);
    }

    /// <summary>
    /// Nine axes, 27 values. Two of them share <c>NoteWidth</c> on purpose: the falling note and the held bar
    /// are separate drawing paths on the software stage, and the bar is the one that had the floor.
    /// </summary>
    internal static IReadOnlyList<Axis> Axes { get; } = Array.AsReadOnly(new[]
    {
        // 30 is the bottom of the slider and 45 is a value the old 60 floor collapsed onto it (30 and 45 both
        // drew a 60% bar), so this pair is the regression test for that bug, not a sample of the range.
        new Axis("note-width", "NoteWidth", "note-style", "Solid", OptionMatrixPlan.Scene.Falling, 30, 100, [30d, 45d, 100d]),
        new Axis("bar-width", "NoteWidth", "note-style", "Solid", OptionMatrixPlan.Scene.Held, 30, 100, [30d, 45d, 100d]),
        new Axis("note-speed", "NoteFallSpeed", "note-style", "Solid", OptionMatrixPlan.Scene.Falling, 100, 1000, [125d, 325d, 550d]),
        new Axis("trail-intensity", "FallingTrailIntensity", "falling-trail", "Glow", OptionMatrixPlan.Scene.Falling, 0, 100, [0d, 50d, 100d]),
        new Axis("wave-intensity", "ImpactWaveIntensity", "impact-wave", "Ring", OptionMatrixPlan.Scene.Impact, 0, 150, [0d, 75d, 150d]),
        new Axis("wave-size", "RingSize", "impact-wave", "Ring", OptionMatrixPlan.Scene.Impact, 0, 100, [5d, 50d, 100d]),
        new Axis("burst-amount", "ParticleAmount", "impact-burst", "Embers", OptionMatrixPlan.Scene.Impact, 0, 120, [8d, 60d, 120d]),
        new Axis("flash-intensity", "ImpactFlashIntensity", "impact-flash", "Flash", OptionMatrixPlan.Scene.Impact, 0, 100, [0d, 50d, 100d]),
        new Axis("release-intensity", "ReleaseIntensity", "release", "Float Up", OptionMatrixPlan.Scene.Release, 0, 100, [0d, 50d, 100d])
    });

    /// <summary>One PNG per value per renderer.</summary>
    internal static int CaptureCount => Axes.Sum(axis => axis.Values.Count) * 2;

    /// <summary>A file-name-safe spelling of a slider value: 30 → "30", 12.5 → "12p5".</summary>
    internal static string Label(double value)
    {
        var text = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return text.Replace('.', 'p');
    }
}
