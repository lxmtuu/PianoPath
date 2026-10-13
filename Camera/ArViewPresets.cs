namespace PianoPath;

/// <summary>
/// The AR look retuned per camera view (roadmap §14.7 "Thêm 5"): the catalogue's paper-thin heights
/// (<see cref="ArElementHeights"/>) and the visibility of the tall, stand-up effects, both keyed by the view the
/// camera is at (<see cref="ArCalibrationProfiles"/>), never by a new setting.
///
/// <para>
/// The reason it is a view and not a knob: what reaches the lens depends on the angle. On a leaned camera (the 45°
/// view, the free mark, the legacy calibration) a flat-born flare or pulse hides behind the fronts of the keys, so
/// the catalogue lifts it just past them; straight overhead (the 90° view) nothing stands in front of anything, the
/// same lift would float a light visibly over the wood, and the tall effects — hold arcs and light beams, quads that
/// stand on the keys and rise out of the plane — squash to a hairline of no area and read as noise, so the overhead
/// view simply hides them. Which numbers a view uses is therefore a function of the view the computer already knows
/// (<see cref="ArCalibrationProfile.Id"/>, machine-local like the calibration itself): presets and share codes carry
/// no look change, exactly as the view id itself travels nowhere.
/// </para>
/// </summary>
internal sealed record ArViewPreset(
    /// <summary>The impact flare's own lift above the struck key, in centimetres.</summary>
    double ImpactGlowCentimetres,
    /// <summary>The floor a travelling pulse is guaranteed above the key plane, in centimetres.</summary>
    double PulseCentimetres,
    /// <summary>Whether the hold arcs and light beams are drawn for this view (quad families that stand on the keys).</summary>
    bool TallEffectsVisible)
{
    /// <summary>The impact flare's lift in keyboard-depth units, ready for a projected quad centre.</summary>
    internal double ImpactGlow => ArElementHeights.ToKeyboardUnits(ImpactGlowCentimetres);

    /// <summary>The pulse floor in keyboard-depth units, ready for <see cref="ArElementHeights.Lifted(ArVec3, double)"/>.</summary>
    internal double PulseMinimum => ArElementHeights.ToKeyboardUnits(PulseCentimetres);
}

/// <summary>The preset table behind <see cref="ArViewPresets.ForProfile(string?)"/>: one row per view.</summary>
internal static class ArViewPresets
{
    /// <summary>
    /// The leaned camera: the catalogue's plain numbers (impact flare 0.2 cm, pulses at least 0.3 cm over the
    /// plane) plus the full effect set — hold arcs and light beams read beautifully against the diagonal keys.
    /// </summary>
    internal static readonly ArViewPreset Leaned = new(ImpactGlowCentimetres: .2, PulseCentimetres: .3, TallEffectsVisible: true);

    /// <summary>
    /// The 90° overhead camera: the key plane is the picture, so the lifts shrink to a paper ﬁlm (0.05 cm / 0.1 cm —
    /// sub-pixel, only z-order by another name) and the stand-up quads stay home.
    /// </summary>
    internal static readonly ArViewPreset Overhead = new(ImpactGlowCentimetres: .05, PulseCentimetres: .1, TallEffectsVisible: false);

    /// <summary>
    /// The preset of a view id, falling back to the leaned camera for <see cref="ArCalibrationProfiles.LegacyId"/>, an
    /// empty id, and anything the app does not know — matching how the calibration store normalizes the same ids, so an
    /// unmarked or unnamed angle keeps the look calibrations were always drawn with.
    /// </summary>
    internal static ArViewPreset ForProfile(string? profileId) =>
        string.Equals(ArCalibrationProfiles.ById(profileId)?.Id, ArCalibrationProfiles.Overhead90.Id, StringComparison.OrdinalIgnoreCase)
            ? Overhead
            : Leaned;
}
