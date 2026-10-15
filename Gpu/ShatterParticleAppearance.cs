using System.Numerics;
namespace PianoPath;

/// <summary>Review-only Shatter facet/glint material; never owns simulation, geometry or selection.</summary>
internal static class ShatterParticleAppearance
{
    internal const float BodyKind=31, GlintKind=32;
    internal static bool Eligible(bool enabled,int shape,bool wisp,int origin)=>enabled && shape==6 && !wisp && origin==0;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
        =>SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color);
    internal static Vector4 Glint(Vector4 body)=>new(body.X,body.Y,body.Z,body.W*.45f);
}
