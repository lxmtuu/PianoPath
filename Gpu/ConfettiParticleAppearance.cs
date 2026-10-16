using System.Numerics;
namespace PianoPath;

/// <summary>Review-only paper surface and material orientation. No simulation or quad ownership.</summary>
internal static class ConfettiParticleAppearance
{
    internal const float Kind=28;
    internal static bool Eligible(bool enabled,int shape,bool wisp)=>enabled && shape==2 && !wisp;
    internal static Vector2 Orientation(float age,float phase)
    {
        var angle=age*7+phase;return new(MathF.Cos(angle),MathF.Sin(angle));
    }
    internal static bool TryColor(Vector3 birth,float age,float life,out Vector4 color)
    {
        if(!SparkParticleAppearance.TryColor(birth,age,life,1,1,out color))return false;
        // Preserve Confetti's own legacy opacity envelope (it does not use ParticleGlow).
        color.W=Math.Min(1,(1-age/life)*1.5f);return true;
    }
}
