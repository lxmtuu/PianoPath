using System.Numerics;
namespace PianoPath;

/// <summary>Review-only rounded Bounce stroke. Explicit provenance excludes primary Ember/Firework streaks.</summary>
internal static class BounceParticleAppearance
{
    internal const byte Route=2;
    internal const float Kind=39;
    internal static bool Eligible(bool enabled,int shape,bool wisp,int origin,int route)
        =>enabled && shape==1 && !wisp && origin==0 && route==Route;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
        =>SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color);
}
