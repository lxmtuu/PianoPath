using System.Numerics;

namespace PianoPath;

/// <summary>Review-only liquid surface. Does not own emission, geometry or eligibility.</summary>
internal static class SplashParticleAppearance
{
    internal const float HaloKind=22, BodyKind=23, GlintKind=24, ArKind=25;
    internal static bool Eligible(bool enabled,int shape,bool wisp) => enabled && shape==5 && !wisp;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
    {
        if(!SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color))return false;
        // Keep Splash's existing slower envelope, but encode it once, in alpha, in both outputs.
        color.W=MathF.Pow(1-age/life,.8f)*Math.Clamp(glow*gain,0,1);
        return true;
    }
}
