using System.Numerics;

namespace PianoPath;

/// <summary>Review-only spark materials. Never owns physics, geometry, packing or RNG.</summary>
internal static class SparkParticleAppearance
{
    internal const float StreakKind = 18, DotKind = 19, StreakV2Kind = 20, DotV2Kind = 21;
    internal static float StageKind(bool candidate, int shape, bool wisp, float legacyKind, bool v2 = false)
        => candidate && !wisp && shape is 0 or 1 ? (shape == 1 ? (v2 ? StreakV2Kind : StreakKind) : (v2 ? DotV2Kind : DotKind)) : legacyKind;
    internal static float ArKind(bool candidate, int shape, bool wisp, bool v2 = false)
        => StageKind(candidate, shape, wisp, 2, v2);

    /// <summary>One intrinsic linear-light colour/opacity for both outputs. Fade lives only in alpha.
    /// Hue is retained, black is not promoted to a luminous colour, and RGB is bounded before premultiplication.</summary>
    internal static bool TryColor(Vector3 birthColor, float age, float life, float glow, float particleGlow, out Vector4 color)
    {
        color = default;
        if (!Finite(birthColor.X) || !Finite(birthColor.Y) || !Finite(birthColor.Z)
            || !float.IsFinite(age) || !float.IsFinite(life) || age < 0 || life <= 0 || age >= life
            || !float.IsFinite(glow) || glow < 0 || !float.IsFinite(particleGlow) || particleGlow < 0) return false;
        var linear = new Vector3(Linear(birthColor.X), Linear(birthColor.Y), Linear(birthColor.Z));
        var peak = Math.Max(linear.X, Math.Max(linear.Y, linear.Z));
        var tint = linear * (.95f / Math.Max(.15f, peak));
        var alpha = (1 - age / life) * Math.Clamp(glow * particleGlow, 0, 1);
        color = new(tint, alpha);
        return true;
    }
    private static bool Finite(float c) => float.IsFinite(c) && c >= 0 && c <= 1;
    private static float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
}
