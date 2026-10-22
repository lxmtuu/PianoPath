namespace PianoPath;

/// <summary>
/// W1: the coherent flow a held wisp or flame is carried by. One law, three readers — the engine's particle
/// integrator, the software stage's, and the review that measures them.
///
/// <para>The prototype (<c>docs/prototypes/particle-motion-lab/lab.js</c>, <c>MODEL.FLOW</c>) measured what the
/// shipped model does to a held stream: p10–p90 width 1.87 keys at one third of the climb against 0.49 for this
/// one, and local spread 0.47 against 0.29 — the old stream is a column of motes each waving on its own phase,
/// this one bends as a body (docs/PARTICLE-MOTION-REVIEW.md §5.1). Four parts, in this order: a wind the particle
/// is entrained by, its own eddy, a widening that grows with age, and a slower, narrower birth for the sustained
/// emitter.</para>
///
/// <para>Every particle arrives here with the same numbers the lab used: the field in the settings' own units
/// (0..1000), the field's scale in pixels, the evolution speed normalised to 0..1, and a per-particle seed in
/// 0..1. The caller converts its angle-like <c>Phase</c> by dividing by <see cref="Turn"/>, so the two renderers
/// feed identical inputs even though one stores floats and the other doubles.</para>
///
/// <para>Zero flow means the caller never calls this at all: <see cref="Step"/> returns immediately, and the
/// engines keep the exact arithmetic their accepted captures pinned. That is what lets the feature ship without
/// re-baselining <c>wisps-v3</c>/<c>flames-v3</c> on the day it lands.</para>
/// </summary>
internal static class ParticleFlow
{
    /// <summary>One full turn in radians; the per-particle seed is the phase divided by this.</summary>
    internal const double Turn = 6.283185307179586;
    /// <summary>Seconds for the wind to take a particle from its own velocity to the field's (row 2).</summary>
    internal const double EntrainSeconds = .45;
    /// <summary>The wind bends the rise; it does not replace it — only this share of the vertical velocity is entrained.</summary>
    internal const double VerticalShare = .12;
    /// <summary>Sideways acceleration at the slider position the measurement used, px/s² (row 4).</summary>
    internal const double DiffusionRate = 46;
    /// <summary>The slider position the 46 px/s² was measured at; the amount is expressed against it.</summary>
    internal const double DiffusionReference = 55;

    /// <summary>
    /// A divergence-free wind, in px/s: the stream function is sampled at the particle's position, so neighbours
    /// travel together instead of each mote getting its own random push. The small upward term is the lift that
    /// keeps a bent stream climbing rather than flattening into the keys.
    /// </summary>
    internal static (double X, double Y) Wind(double x, double y, double time, double field, double fieldScale, double evolutionSpeed)
    {
        var s = Math.Max(30, fieldScale * 2.1);
        var kx = 1 / s;
        var ky = 1 / (s * 1.6);
        var w = time * (.22 + evolutionSpeed * .33);
        var a = kx * x + w;
        var b = ky * y - w * .6;
        var amp = 88 * (field / 36) * (s / 1.6);
        return (-amp * ky * Math.Sin(a) * Math.Sin(b), -amp * kx * Math.Cos(a) * Math.Cos(b) - 26);
    }

    /// <summary>
    /// The particle's own eddy, in px/s². Neighbours share it because the field is sampled in stage space; the
    /// seed only offsets the phase, so a pair born together twists together instead of scattering.
    /// </summary>
    internal static (double X, double Y) Curl(double x, double y, double time, double seed)
    {
        const double scale = 34;
        var kx = 1 / scale;
        var ky = 1 / (scale * 1.25);
        var a = kx * x + time * .9 + seed * Turn;
        var b = ky * y - time * .66 + seed * 2.4;
        const double amplitude = 520;
        return (-amplitude * ky * Math.Sin(a) * Math.Sin(b), -amplitude * kx * Math.Cos(a) * Math.Cos(b));
    }

    /// <summary>
    /// The sideways kick that widens the stream as it ages, in px/s, before <c>dt</c> is applied: zero-mean over
    /// the seed, linear in the amount, and expressed against the reference so a caller can pass 0..100.
    /// </summary>
    internal static double Diffusion(double seed, double amount)
        => (seed - .5) * DiffusionRate * (amount / DiffusionReference);

    /// <summary>
    /// One step of the flow: entrain towards the wind, add the eddy, diffuse, then move. Entrainment is
    /// exponential rather than a hard blend so the result does not depend on the frame rate — the same
    /// particle at 30 Hz and 144 Hz follows the same path.
    /// </summary>
    /// <param name="seed">Per-particle seed (the caller divides its phase by <see cref="Turn"/>).</param>
    /// <param name="flow">The flow slider in 0..1. Zero returns without touching the particle.</param>
    internal static void Step(ref double x, ref double y, ref double vx, ref double vy,
        double seed, double time, double dt, double field, double fieldScale, double evolutionSpeed, double flow)
    {
        if (!(flow > 0) || !(dt > 0)) return;
        // The seed is folded into its own turn rather than trusted to arrive inside one: the engine's wisp phase
        // carries the birth time (strand·π + t·1.7), so an unfolded seed would grow with the session and the
        // diffusion's zero-mean term would turn into a slow, one-way drift.
        seed -= Math.Floor(seed);
        var wind = Wind(x, y, time, field, fieldScale, evolutionSpeed);
        var k = 1 - Math.Exp(-dt / EntrainSeconds);
        vx += (wind.X - vx) * k;
        vy += (wind.Y - vy) * k * VerticalShare;
        var curl = Curl(x, y, time, seed);
        vx += curl.X * dt;
        vy += curl.Y * dt;
        vx += Diffusion(seed, flow * 100) * dt;
        x += vx * dt;
        y += vy * dt;
    }
}
