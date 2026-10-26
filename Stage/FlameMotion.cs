namespace PianoPath;

/// <summary>
/// The law of the held-key flame, shared by the software stage.
///
/// <para>The engine draws a flame as a stream of motes: roughly seventy per second per sounding key at full
/// intensity, climbing out of the key line at 50–200 px/s, living a third to two thirds of a second and cooling
/// from white through yellow and orange to deep red. The software stage drew the same setting as one ellipse
/// plume pinned to the key top, so a machine without a GPU showed a completely different effect — and a
/// motionless one, which is what "the flame is stuck to the key" meant.</para>
/// <para>The numbers below are the engine's, from <c>GpuStageSimulation</c>'s flame spawner. They are written
/// down once here, and <c>check_sources</c> fails if the engine's copy changes without this one, since the two
/// have to stay the same effect. Only the placement differs: the engine shares one 24k particle pool between
/// every family, while the software stage keeps a smaller cap of its own because it draws on the UI thread.</para>
/// </summary>
internal static class FlameMotion
{
    /// <summary>
    /// Most flame motes the software stage keeps alive at once. A full-width chord at maximum intensity would
    /// ask for thousands (the engine's pool would too, and it caps there the same way); this is the point where
    /// the fallback stays smooth on an integrated machine while a normal chord still reads as fire.
    /// </summary>
    internal const int MaxMotes = 900;

    /// <summary>
    /// Motes born per second for one key: seventy at full intensity and full heat, scaled by both, exactly as
    /// the engine's <c>70 * FlameIntensity * heat</c>.
    /// </summary>
    internal static double EmissionRate(double intensityPercent, double heat) => 70 * (intensityPercent / 100) * heat;

    /// <summary>
    /// Upward speed in px/s. <paramref name="heightPercent"/> stretches the reach of the flame, and
    /// <paramref name="jitter"/> (0..1, from the particle random) scatters the speed by ±40% so the stream does
    /// not look like a comb.
    /// </summary>
    internal static double RiseSpeed(double heightPercent, double jitter) => -(50 + 150 * (heightPercent / 100)) * (.6 + .6 * jitter);

    /// <summary>How long one mote lives, in seconds: 0.22 s plus a height-dependent part jittered ±40%.</summary>
    internal static double LifeSeconds(double heightPercent, double jitter) => .22 + (heightPercent / 100) * .45 * (.6 + .6 * jitter);

    /// <summary>Mote diameter in DIPs: about a third to two thirds of a key lane.</summary>
    internal static double Size(double lane, double jitter) => lane * (.35 + .3 * jitter);

    /// <summary>
    /// Sideways acceleration in px/s²: a slow curl around the rising axis, so the stream flickers instead of
    /// standing straight up.
    /// </summary>
    internal static double Sway(double age, double phase) => Math.Sin(age * 11 + phase) * 40;

    /// <summary>
    /// Upward acceleration in px/s² for the W2 candidate: hot gas keeps climbing instead of coasting at its
    /// birth speed. The engine applies it only when the flame motion candidate is on, so the shipped plume is
    /// still the constant-rise one that the accepted captures pin.
    /// </summary>
    internal const float V3Buoyancy = 30f;

    /// <summary>Lateral drag in 1/s for the W2 candidate: the flicker saturates instead of wandering away.</summary>
    internal const float V3Drag = 1.7f;

    /// <summary>Vertical drag in 1/s for the W2 candidate: the climb slows as the mote cools.</summary>
    internal const float V3Lift = .9f;

    /// <summary>Fractional growth in 1/s for the W2 candidate: a flame spreads as it cools (~1.57× a life).</summary>
    internal const float V3Swell = .9f;

    /// <summary>
    /// One step of the W2 flame candidate in the engine's own units, so the review clips and any future
    /// software adoption share one law instead of two copies of four numbers.
    ///
    /// <para>Birth, life, emission and the cooling ramp are untouched: only the path changes. Buoyancy makes
    /// the mote accelerate upward, drag keeps the flicker from accumulating, and the size swells with age, so
    /// the stream reads as fire rising off the key instead of a rigid column. Everything is a pure function of
    /// the current state, so the motion review stays deterministic.</para>
    /// </summary>
    internal static void StepV3(ref float x, ref float y, ref float vx, ref float vy, ref float size,
        float age, float phase, float dt)
    {
        vx += (float)Sway(age, phase) * dt;
        vy -= V3Buoyancy * dt;
        vx *= MathF.Exp(-V3Drag * dt);
        vy *= MathF.Exp(-V3Lift * dt);
        size *= 1 + V3Swell * dt;
        x += vx * dt;
        y += vy * dt;
    }

    /// <summary>
    /// The cooling ramp: white core to yellow to orange to deep red, indexed by the fraction of the mote's life
    /// it has lived. The engine interpolates the same four stops in linear space before its shader converts;
    /// the software renderer blends in sRGB, so the stops are written here in the space WPF actually draws.
    /// </summary>
    internal static (double R, double G, double B) Ramp(double ageFraction)
    {
        var t = Math.Clamp(ageFraction, 0, 1);
        if (t < .25) return Mix((1, .95, .8), (1, .78, .3), t / .25);
        if (t < .6) return Mix((1, .78, .3), (1, .38, .08), (t - .25) / .35);
        return Mix((1, .38, .08), (.45, .03, .01), (t - .6) / .4);
    }

    private static (double R, double G, double B) Mix((double R, double G, double B) from, (double R, double G, double B) to, double t) =>
        (from.R + (to.R - from.R) * t, from.G + (to.G - from.G) * t, from.B + (to.B - from.B) * t);
}
