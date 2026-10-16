using System.Numerics;

namespace PianoPath;

/// <summary>Intrinsic surface of the experimental held wisps/flames, in scene units.
/// Neither output may advance physics, consume RNG or resolve a second lifetime envelope.</summary>
internal readonly record struct HeldParticleSurface(float HalfWidth, float HalfHeight, Vector4 Color,
    float Progress, float Shape, float Phase, bool BaseAnchored);

internal static class HeldParticleAppearance
{
    internal static bool TryResolve(bool wisp, float size, float age, float life, float phase, float glow,
        Vector3 birthColor, float wispWidth, bool flameNoteColor, out HeldParticleSurface surface, bool wispV3ForReview = false, bool flameV3ForReview = false)
    {
        surface = default;
        if (!float.IsFinite(size) || size <= 0 || !float.IsFinite(age) || age < 0
            || !float.IsFinite(life) || life <= 0 || age >= life || !float.IsFinite(phase)
            || !float.IsFinite(glow) || glow <= 0 || !Finite(birthColor)) return false;
        var u = Math.Clamp(age / life, 0, 1); var fade = 1 - u;
        Vector3 color; float alpha, halfX, halfY;
        if (wisp)
        {
            var width = float.IsFinite(wispWidth) ? Math.Clamp(wispWidth, 0, 1) : .4f;
            halfX = size * (1.2f + width * .8f); halfY = size * 4.8f;
            color = Linear(birthColor) * (.6f + glow * 1.6f);
            alpha = Math.Clamp(glow, 0, 1) * Math.Min(1, u * 6) * fade;
        }
        else
        {
            var fire = u < .25f ? Vector3.Lerp(new(1f, .95f, .8f), new(1f, .78f, .3f), u / .25f)
                : u < .6f ? Vector3.Lerp(new(1f, .78f, .3f), new(1f, .38f, .08f), (u - .25f) / .35f)
                : Vector3.Lerp(new(1f, .38f, .08f), new(.45f, .03f, .01f), (u - .6f) / .4f);
            if (flameNoteColor) fire = Vector3.Lerp(fire, birthColor, .6f);
            color = Linear(fire) * (1.3f * glow);
            var swelling = .6f + MathF.Sin(Math.Min(1, u * 1.4f) * MathF.PI) * .7f;
            halfX = size * swelling * .7f; halfY = size * swelling * 1.8f;
            alpha = fade;
        }
        if (!float.IsFinite(halfX) || !float.IsFinite(halfY) || !Finite(color)) return false;
        surface = new(halfX, halfY, new Vector4(color, alpha), u, wisp ? (wispV3ForReview ? 8 : 7) : (flameV3ForReview ? 6 : 4), phase, !wisp);
        return true;
    }

    private static bool Finite(Vector3 c) => float.IsFinite(c.X) && float.IsFinite(c.Y) && float.IsFinite(c.Z);
    private static Vector3 Linear(Vector3 c) => new(Component(c.X), Component(c.Y), Component(c.Z));
    private static float Component(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
}
