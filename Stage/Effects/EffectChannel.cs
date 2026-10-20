namespace PianoPath;

/// <summary>
/// Which setting owns a spark, ring or flash, so one channel's effect can never hide behind another
/// channel's visibility toggle.
///
/// <para>
/// The software stage has three switches that both spawn effects and hide them: Embers (bursts and the
/// held spark emitter), Impact rings (the wave) and Impact flash. The note morph and the release effect
/// are not covered by any of them: they are their own row in the settings, they can be chosen on their
/// own, and switching the sparks off must not silently delete a Shatter the user asked for. Tagging each
/// effect with the channel that drew it lets one particle list serve all of them. The numbers are the GPU
/// stage's ring channels (<see cref="KeyRingChannelGain"/>) so both renderers agree on who owns an effect
/// and on which knob drives its brightness.
/// </para>
/// </summary>
internal static class EffectChannel
{
    /// <summary>Spawned by a visibility switch: hidden when that switch is off.</summary>
    internal const byte Layer = 0;

    /// <summary>Impact morph (Shatter, Melt, Absorb, Bounce, Morph): draws even with every layer off.</summary>
    internal const byte Morph = 1;

    /// <summary>Release effect (Float Up, Dissolve, Smoke, Snap Back, Echo Rings): draws on its own.</summary>
    internal const byte Release = 2;

    /// <summary>True when the effect draws even while the layer that spawns a sibling effect is off.</summary>
    internal static bool OwnsItsDraw(byte channel) => channel != Layer;
}
