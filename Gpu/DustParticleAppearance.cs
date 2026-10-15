using System.Numerics;
namespace PianoPath;

/// <summary>Review-only dust material. No physics, RNG, geometry, particle selection or extra layers.</summary>
internal static class DustParticleAppearance
{
    internal const float Kind=29;
    internal static bool Eligible(bool enabled,int shape,bool wisp)=>enabled && shape==3 && !wisp;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
    {
        if(!SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color))return false;
        // Intrinsic, single lifetime envelope; soft dust is deliberately weaker than sparks.
        color.W*=.35f;return true;
    }
}
