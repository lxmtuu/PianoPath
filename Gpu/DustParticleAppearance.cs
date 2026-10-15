using System.Numerics;
namespace PianoPath;

/// <summary>Review-only dust material. No physics, RNG, geometry, particle selection or extra layers.</summary>
internal static class DustParticleAppearance
{
    internal const float Kind=29, V2Kind=30;
    internal static bool Eligible(bool enabled,int shape,bool wisp)=>enabled && shape==3 && !wisp;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color,bool v2=false)
    {
        if(!SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color))return false;
        // Intrinsic, single lifetime envelope; soft dust is deliberately weaker than sparks.
        color.W*=.35f;
        if(v2 && glow>0 && glow<1)
        {
            // Concave birth-glow response recovers weak held dust. Full-glow burst and zero remain
            // on the exact V1 path; neither the lifetime curve nor the Gaussian footprint changes.
            // Stable polynomial form of 1-(1-glow)^3, including very small positive glow.
            var response=glow*(3-glow*(3-glow));
            color.W=(1-age/life)*Math.Clamp(response*gain,0,1)*.35f;
        }
        return true;
    }
}
