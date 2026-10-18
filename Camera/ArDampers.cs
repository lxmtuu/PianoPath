namespace PianoPath;

/// <summary>
/// The damper strip of the AR overlay (roadmap §14.3, the <see cref="ArElement.Dampers"/> element): a soft glowing
/// line laid along the back edge of the keys, lifted by the sustain level. In the words of the catalogue it "moves
/// up with damping 0..1 (sustain 0..1, not a boolean)": the strip is always a thin band past the key back edge — in
/// the picture, the fallboard side — and the pedal reaching down widens it and brightens it. Its colour is the
/// look's halo colour, the same family the stage's Pedal Glow grows with, so the strip needs no knob of its own.
///
/// <para>
/// Since D2 the strip also carries one <em>cell</em> and one <em>pin</em> per key: the cell is the key's square of
/// light at the strip's depth, tinted 22% toward the key's own colour, and the pin is the damper head standing on
/// it. A key that is down lifts its own damper to 1 whatever the pedal does; otherwise the pedal lifts every key
/// together, and each key follows through the same 30/40 ms smoother that gives the comb its release. The GPU packs
/// them as <c>Misc.z = 12</c> quads next to the strip, so they are scene elements and never count as note quads.
/// </para>
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

    /// <summary>Time constant for a pedal moving down: about 121 ms from 10% to 90%.</summary>
    internal const double AttackSeconds = .055;

    /// <summary>Time constant for a pedal coming up: a little slower, because the real pedal has weight.</summary>
    internal const double ReleaseSeconds = .075;

    /// <summary>
    /// One exponential smoother for every pedal-driven light. <paramref name="dt"/> is the rendered frame's
    /// real time (not the particle time factor); a missing or non-positive step keeps the previous value.
    /// </summary>
    internal static double Smooth(double previous, double target, double dt)
        => Smooth(previous, target, dt, AttackSeconds, ReleaseSeconds);

    private static double Smooth(double previous, double target, double dt, double attack, double release)
    {
        var p = ClampLevel(previous);
        if (!(dt > 0) || !double.IsFinite(dt)) return p;
        var t = ClampLevel(target);
        var tau = t > p ? attack : release;
        return p + (t - p) * (1 - Math.Exp(-dt / tau));
    }

    /// <summary>How much of the strip's extra depth the glowing core travels across.</summary>
    internal const double CoreTravel = .55;

    /// <summary>
    /// The core's uv.y for a pedal lift: 1 on the key edge with the pedal up, moving toward the fallboard by
    /// <see cref="CoreTravel"/> of the strip's extra depth, so it never leaves the lit band. Kept here rather
    /// than in the shader so the prototype, the test and the shader share one number.
    /// </summary>
    internal static double CorePositionFor(double lift)
    {
        var l = Math.Clamp(lift, 0, 1);
        return 1 - CoreTravel * (LiftDepth * l) / (BaseDepth + LiftDepth * l);
    }

    /// <summary>Cell width as a share of a white key; a black key's cell is scaled to its narrower span.</summary>
    internal const double CellWhiteShare = .70;
    internal const double CellBlackShare = .46;

    /// <summary>Damper ticks: they answer a press in 30 ms and fall back in 40 ms.</summary>
    internal const double TickAttackSeconds = .030;
    internal const double TickReleaseSeconds = .040;

    /// <summary>How tall a fully lifted tick is on a 1080p take, in output pixels, and how wide it is.</summary>
    internal const double TickHeight = 15;
    internal const double TickWidth = 2.4;

    /// <summary>
    /// A pin's height for the strip's projected depth, in output pixels: one band deep at most and never taller
    /// than <see cref="TickHeight"/>. A fixed 15 px pin is right at 1080p and enormous in a 320x180 take, where
    /// the whole keyboard is barely forty pixels deep; tying it to the projection keeps the same shape at every size.
    /// </summary>
    internal static double PinHeight(double bandPixels) =>
        double.IsFinite(bandPixels) ? Math.Clamp(bandPixels, 2, TickHeight) : TickHeight;

    /// <summary>A pin's width for the cell's projected width: 2.4 px on a 1080p cell, never under one pixel.</summary>
    internal static double PinWidth(double cellPixels) =>
        double.IsFinite(cellPixels) ? Math.Clamp(cellPixels * .10, 1, TickWidth) : TickWidth;

    /// <summary>The per-key cell's body and rim opacity for a damper value 0..1.</summary>
    internal static double CellBodyAlpha(double value) => .05 + .42 * ClampLevel(value);
    internal static double CellRimAlpha(double value) => .16 + .54 * ClampLevel(value);
    internal static double TickAlpha(double value) => .18 + .58 * ClampLevel(value);

    /// <summary>The cell's width for a key span; black keys keep SeeMusic's narrower cell.</summary>
    internal static double CellWidth(double keyWidth, bool black)
        => keyWidth * (black ? CellBlackShare / KeyboardLayout.BlackWidthShare : CellWhiteShare);

    /// <summary>
    /// How far one key's damper is lifted: a key that is down is fully lifted whatever the pedal does, otherwise
    /// the pedal lifts every key together. The step into the smoother happens per key so the cells answer a chord
    /// key by key instead of the whole board at once.
    /// </summary>
    internal static double TickTarget(bool held, double sustainLift) => held ? 1 : ClampLevel(sustainLift);

    /// <summary>A key's damper tick fades in and out faster than the bar itself.</summary>
    internal static double SmoothTick(double previous, double target, double dt)
        => Smooth(previous, target, dt, TickAttackSeconds, TickReleaseSeconds);

    /// <summary>The composite pass's bloom threshold, shared by the renderer and the strip's ignition curve.</summary>
    internal const double BloomThreshold = 1.8;

    /// <summary>How much hotter the core burns at full lift than at rest (D3).</summary>
    internal const double CoreIgnition = 1.4;

    /// <summary>
    /// The strip core's bloom ignition for a lift 0..1 (D3). It is exactly 1 with the pedal up, so the D1 look is
    /// untouched, and reaches <c>1 + CoreIgnition</c> at full lift. With the strip's gains that puts the core's
    /// brightest channel at 0.48 at rest and 2.57 at full lift, so it enters the prefilter's soft knee (1.44) at
    /// lift 0.65 and crosses the 1.8 threshold at 0.74 (raw CC64 53, about 42% of travel): the light wakes up
    /// through the half-pedal band instead of popping on.
    /// </summary>
    internal static double Ignition(double lift)
    {
        var t = Math.Clamp((ClampLevel(lift) - .35) / .60, 0, 1);
        return 1 + CoreIgnition * t * t * (3 - 2 * t);
    }

    /// <summary>Idle breath: ±2% of the damper light at 0.7 Hz, only while the pedal holds something up.</summary>
    internal const double BreathAmount = .02;
    internal const double BreathHertz = .7;

    /// <summary>
    /// A deterministic alpha multiplier for the damper light (D3): 1 with the pedal up, and a slow ±2% sway once
    /// the pedal lifts, so a held pedal feels alive without becoming a strobe. <paramref name="reducedMotion"/>
    /// answers the accessibility toggle, where the sway is off entirely.
    /// </summary>
    internal static double Breath(double lift, double seconds, bool reducedMotion = false)
    {
        if (reducedMotion || !double.IsFinite(seconds)) return 1;
        var engage = Math.Clamp((ClampLevel(lift) - .02) / .25, 0, 1);
        if (engage <= 0) return 1;
        return 1 + BreathAmount * engage * Math.Sin(seconds * 2 * Math.PI * BreathHertz);
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
