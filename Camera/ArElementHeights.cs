namespace PianoPath;

/// <summary>
/// The tiny heights of the AR effects catalogue (roadmap §14.2): how far above the real key surface an element
/// floats, in centimetres, converted to the keyboard-depth units the 3D projection uses (<c>-Z</c> is up out of the
/// key plane; one unit == one key depth == <see cref="ArCameraPose.KeyboardDepthMillimetres"/>). On a leaned camera
/// (the 45°/free views) a light that lies exactly in the key plane visually hides behind the key fronts, so the
/// catalogue gives the flat-born elements a paper-thin lift — a hover the player reads as "on the keys", never as
/// "in the air".
///
/// <para>
/// Only strike-born elements get a number here, and only the ones that start glued to the plane: the impact flare
/// lifts <see cref="ImpactGlowCentimetres"/>, the travelling pulse is guaranteed at least
/// <see cref="PulseCentimetres"/> above the plane. Elements that already start up out of the plane (flashes, arcs)
/// keep their own heights, and light that must stay glued by design — key tint, the note-light spill, the damper
/// strip, the HUD — keeps zero. All arithmetic is pure so the xUnit project locks the table on any machine; the
/// values stay below one pixel on a typical 1080p camera on purpose, a readability decision rather than a visual
/// change, which is why the WARP screenshot check does not gate them.
/// </para>
/// </summary>
internal static class ArElementHeights
{
    /// <summary>Impact flare: the catalogue's "fast flash on press" hovers 0.2 cm over the struck key.</summary>
    internal const double ImpactGlowCentimetres = .2;

    /// <summary>Travelling pulse: never closer to the plane than 0.3 cm, so it never z-fights the key fronts.</summary>
    internal const double PulseCentimetres = .3;

    /// <summary>Centimetres to keyboard-depth units: 150 mm of key depth makes exactly one unit.</summary>
    internal static double ToKeyboardUnits(double centimetres) => centimetres / (ArCameraPose.KeyboardDepthMillimetres / 10);

    /// <summary>The impact flare's own lift, ready for a projected quad centre.</summary>
    internal static double ImpactGlow => ToKeyboardUnits(ImpactGlowCentimetres);

    /// <summary>The smallest lift a pulse ring may carry, in keyboard-depth units.</summary>
    internal static double PulseMinimum => ToKeyboardUnits(PulseCentimetres);

    /// <summary>
    /// Guarantees a point at least <paramref name="minimumUnits"/> up out of the key plane: anything already higher
    /// (steeper negative Z) passes through untouched, so stage-born rings that rise keep their rise.
    /// </summary>
    internal static ArVec3 Lifted(ArVec3 point, double minimumUnits) =>
        point.Z > -minimumUnits ? point with { Z = -minimumUnits } : point;
}
