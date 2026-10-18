namespace PianoPath;

/// <summary>
/// The damper strip of the AR overlay (roadmap §14.3, the <see cref="ArElement.Dampers"/> element): a soft glowing
/// line laid along the back edge of the keys, lifted by the sustain level. In the words of the catalogue it "moves
/// up with damping 0..1 (sustain 0..1, not a boolean)": the strip is always a thin band past the key back edge — in
/// the picture, the fallboard side — and the pedal reaching down widens it and brightens it. Its colour is the
/// look's halo colour, the same family the stage's Pedal Glow grows with, so the strip needs no knob of its own.
///
/// <para>
/// All geometry is keyboard-plane space: the strip runs the full key-area width <c>u</c> 0..1 and reaches
/// <see cref="DepthFor(double)"/> key-depth units past <c>v = 0</c> (negative v is behind the keys, over the piano
/// body). On a 90° overhead camera exactly the whole band is in view, where it reads as the glowing strike line of
/// the odyssey-style top-down look; on a leaned camera it compresses with perspective and stays glued to the keys.
/// All arithmetic is pure so the xUnit project checks it on any machine; the GPU draws it as a <c>Misc.z = 11</c>
/// quad in <c>PsArQuad</c> (Gpu/StageShaders.hlsl) and bloom turns its HDR core into the halo.
/// </para>
/// </summary>
internal static class ArDampers
{
    /// <summary>Where the strip's colour comes from: the look's halo colour, or the player's own pick.</summary>
    internal static readonly string[] ColorModes = ["Halo", "Custom"];

    internal const string DefaultColorMode = "Halo";

    /// <summary>The strip's depth past the key back edge with the pedal up, in key-depth units (.06 ≈ 9 mm).</summary>
    internal const double BaseDepth = .06;

    /// <summary>How much deeper the strip reaches at full sustain, added to <see cref="BaseDepth"/> (.10 ≈ 15 mm).</summary>
    internal const double LiftDepth = .10;

    /// <summary>The strip's opacity with the pedal up, a 0..1 share later multiplied by the layer's own opacity.</summary>
    internal const double AlphaBase = .18;

    /// <summary>The extra opacity full sustain adds on top of <see cref="AlphaBase"/>.</summary>
    internal const double AlphaLift = .32;

    /// <summary>Pedal level as a real 0..1; out-of-range and NaN values mean a released pedal.</summary>
    internal static double ClampLevel(double sustain) =>
        double.IsFinite(sustain) ? Math.Clamp(sustain, 0, 1) : 0;

    /// <summary>
    /// The damper's visible lift for a raw pedal 0..1: nothing happens through the first quarter of travel,
    /// the felt catches between a quarter and a half, and the last half is already fully lifted — the same
    /// shape the audio's own damper uses, so light half-pedalling neither lights up nor silently does nothing.
    /// </summary>
    internal static double LiftFromPedal(double sustain)
    {
        var t = Math.Clamp((ClampLevel(sustain) - .25) / .25, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>How far the strip reaches past the key back edge for a pedal lift 0..1.</summary>
    internal static double DepthFor(double lift) => BaseDepth + LiftDepth * Math.Clamp(lift, 0, 1);

    /// <summary>The strip's opacity for a pedal lift 0..1, before the layer's own opacity applies.</summary>
    internal static double AlphaFor(double lift) => AlphaBase + AlphaLift * Math.Clamp(lift, 0, 1);

    /// <summary>
    /// Builds the strip's quad for a pedal lift: its back edge sits <see cref="DepthFor(double)"/> past the keys,
    /// its front edge is the key back line itself. False when the calibration cannot project that band — a horizon
    /// cut from too close a perspective simply leaves the strip out instead of tearing it.
    /// </summary>
    internal static bool TryPatch(ArCalibration calibration, ArView view, double lift, out ArPatch patch) =>
        ArProjection.TryProject(calibration, view, 0, 1, -DepthFor(lift), 0, out patch, out _, out _);
}
