namespace PianoPath;

/// <summary>
/// The sideways wander of a wisp mote, in one place.
///
/// <para>The same formula used to exist four times — twice inside <c>GpuStageSimulation</c> (the classic sprite
/// path and the held-particle art path), once in <c>GpuArLayer</c> for the projected camera quads, and once in
/// <c>ArParticleAlignmentReview</c>, the diagnostic that measures AR alignment. The diagnostic's copy is the
/// dangerous one: it checked the product against its own arithmetic, so it could keep passing while the
/// renderer drifted.</para>
///
/// <para>The software stage made a fourth, different copy: it added <c>sin(age·(3+t·7))·(8+t·46)</c> to the
/// particle's <em>velocity</em> every tick, which the integrator then damped. Same settings, same seed, and
/// the two renderers drew a wisp 10.6 px apart after 0.8 s, with the amplitude 4.9 px against 16.4 px and
/// the frequency 6.85 rad/s against 5.75 (docs/PARTICLE-MOTION-REVIEW.md §4.3). Nothing compared them, so
/// nothing went red.</para>
///
/// <para>The motion therefore leaves the simulation: it is an offset the renderer adds at draw time, so the
/// simulation state AR reads is the state the stage drew, and a slow-motion frame cannot change where a wisp
/// is. Turbulence and width arrive normalised to 0..1, exactly as <c>GpuLook</c> and the settings model
/// deliver them.</para>
/// </summary>
internal static class WispMotion
{
    /// <summary>Peak sideways reach in stage pixels: turbulence widens it, a wider wisp sways farther.</summary>
    internal static float Amplitude(float turbulence, float width)
        => (5 + T(turbulence) * 26) * (.45f + W(width));

    /// <summary>How fast the mote crosses its own span, in radians per second.</summary>
    internal static float AngularFrequency(float turbulence) => 3 + T(turbulence) * 5;

    /// <summary>
    /// A mote is born on its path, not 16 px off it: the sway fades in over the first ~0.6 s, so a key
    /// pressed for a moment leaves a straight column instead of a sideways hook.
    /// </summary>
    internal static float Envelope(float ageSeconds) => Math.Min(1, Math.Max(0, ageSeconds) * 1.6f);

    /// <summary>
    /// The lateral offset to draw a wisp at, given its simulated age and phase. Zero at birth, bounded by
    /// <see cref="Amplitude"/>, and identical for the stage, the AR quads and the review diagnostic.
    /// </summary>
    /// <remarks>
    /// The sway is multiplied as <c>sin × (amplitude × envelope)</c>, the grouping the two GPU call sites
    /// already used. Floating-point multiplication is not associative, so regrouping it would move a mote by a
    /// fraction of a pixel and change the hashes of the accepted WARP evidence — a refactor that alters
    /// pixels is not a refactor. The AR projection reads the same curve through
    /// <see cref="OffsetPrecise"/>, because that path always evaluated the sine in double.
    /// </remarks>
    internal static float Offset(float turbulence, float width, float ageSeconds, float phase)
        => MathF.Sin(Math.Max(0, ageSeconds) * AngularFrequency(turbulence) + phase)
            * (Amplitude(turbulence, width) * Envelope(ageSeconds));

    /// <summary>
    /// The same offset at the precision the AR projection works in. <c>GpuArLayer</c> places camera quads from
    /// doubles (<c>unit</c>, the pose ratio), and it has always taken <c>Math.Sin</c> of the float argument, so
    /// the field returns a double rather than rounding through float first. Every bit matters here: these
    /// frames are part of the accepted WARP evidence, and a pure refactor must leave them identical.
    /// </summary>
    internal static double OffsetPrecise(float turbulence, float width, float ageSeconds, float phase)
        => Math.Sin(Math.Max(0, ageSeconds) * AngularFrequency(turbulence) + phase)
            * (Amplitude(turbulence, width) * Envelope(ageSeconds));

    private static float T(float turbulence) => Math.Clamp(turbulence, 0, 1);
    private static float W(float width) => Math.Clamp(width, 0, 1);
}
