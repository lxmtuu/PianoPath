using System.Numerics;
namespace PianoPath;

/// <summary>Review-only radial spark material. Never owns physics, geometry, RNG or eligibility.</summary>
internal static class FireworkParticleAppearance
{
    internal const float StreakKind=26, DotKind=27;
    internal static bool Eligible(bool enabled,int shape,bool wisp)=>enabled && shape is 0 or 1 && !wisp;
    internal static float StageKind(bool enabled,int shape,bool wisp,float legacy)
        =>Eligible(enabled,shape,wisp)?(shape==1?StreakKind:DotKind):legacy;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
        =>SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color);
}
