namespace PianoPath;

/// <summary>
/// How many particles the sustained emitters birth, in births per second per sounding key.
///
/// <para>Both renderers have always agreed on <em>what</em> a held key emits — embers that follow the selected
/// burst family, two wisp strands, a flame — but not on <em>how much</em> of it. The engine births
/// <c>ParticleAmount × 1.3</c> embers and <c>WispAmount × 4</c> motes a second per held key (four, because the
/// wisps come in two strands that twist around each other). The software stage had no held-ember emitter at
/// all and birthed wisps at <c>WispAmount</c> — a quarter of them. Same preset, then: a stream of embers and
/// smoke on a GPU machine, a bare key with a thin trickle of motes without one.</para>
///
/// <para><see cref="EffectBudget"/> already draws the line this belongs on: a ceiling is a frame-time budget
/// (<c>WpfParticleCap</c> is 9.2× smaller than the GPU's on purpose), but the visible behaviour must agree.
/// So the rate lives here, once, and each renderer drops births at its own ceiling instead of quietly emitting
/// less in the first place.</para>
///
/// <para>One detail is deliberate: the engine multiplies its <c>float</c> look values
/// (<c>look.WispAmount * 4f</c>) while this helper takes the settings' <c>double</c>. For an integer amount
/// such as the default 45 the two agree exactly; for a fractional amount they can differ in the last few bits,
/// which is far below one birth per hour and nowhere near a visible difference. The engine keeps its own
/// expression so the frames pinned by the particle evidence stay byte-identical.</para>
/// </summary>
internal static class HeldEmission
{
    /// <summary>Wisp motes per second for one held key, at <paramref name="amount"/> on the density slider.</summary>
    internal static double Wisps(double amount) => amount * 4;

    /// <summary>Ember-streak particles per second for one held key, at <paramref name="amount"/> on the particle slider.</summary>
    internal static double Sparks(double amount) => amount * 1.3;
}
