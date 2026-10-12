namespace PianoPath;

/// <summary>
/// The shared, dependency-free light curves of the stage lighting: the impact envelope, the velocity dynamics of
/// the flash and the gentle breath of a held key. They live in their own file so the portable xUnit project can
/// link them in and lock their shapes, mirroring what <c>--verify</c> samples from the live simulation.
/// </summary>
internal static class StageLightCurves
{
    /// <summary>Brightness response of an impact: soft hits stay quiet, hard hits open up — a wider dynamic band than the old linear mix.</summary>
    internal static float ImpactDynamic(float strength) { var s = Math.Clamp(strength, 0, 1); return .42f + 1.18f * s * s; }

    /// <summary>Size response of an impact flare: quiet hits stay a spark, hard hits bloom wider.</summary>
    internal static float ImpactSizeBoost(float strength) { var s = Math.Clamp(strength, 0, 1); return 1.35f + 2.6f * s * s; }

    /// <summary>Strength the Lightning/Plasma flashes inherit: weighted toward the note's strength but never black.</summary>
    internal static float FlashStyleStrength(float strength) { var s = Math.Clamp(strength, 0, 1); return .35f + .85f * s * s; }

    /// <summary>Flash envelope: a fast attack (about 55 ms) and a smooth quadratic tail, so impacts bloom in and settle instead of popping on and clipping off.</summary>
    internal static float FlashEnvelope(float phase) { var u = Math.Clamp(phase, 0, 1); var attack = Math.Min(1, u / .055f); var d = 1f - u; return attack * d * d; }

    /// <summary>Breathing amplitude of a held key's light: silent at first, smoothstepping up over ~1.25 s so short taps stay crisp.</summary>
    internal static float HoldBreathAmp(double heldSeconds) { var t = Math.Clamp(((float)heldSeconds - .35f) / .9f, 0, 1); return t * t * (3 - 2 * t) * .075f; }

    /// <summary>The held-key light multiplier: gentle phase-offset sway per key, always within ~±7.5% of the steady glow.</summary>
    internal static float KeyBreathMultiplier(double heldSeconds, double timeSeconds, int pitch)
        => 1 + HoldBreathAmp(heldSeconds) * MathF.Sin((float)(timeSeconds * (MathF.Tau / 1.85f) + pitch * .55f));
}
