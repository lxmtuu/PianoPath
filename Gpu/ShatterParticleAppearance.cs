using System.Numerics;
namespace PianoPath;

/// <summary>Review-only Shatter facet/glint material; never owns simulation, geometry or selection.</summary>
internal static class ShatterParticleAppearance
{
    internal static float Angle(float age,float phase)=>phase+age*7.3f;
    internal static Vector2 Orientation(float age,float phase)
    {
        var angle=Angle(age,phase);return new(MathF.Cos(angle),MathF.Sin(angle));
    }
    // Rotate Stage's right/down axes in the existing note-aligned camera tangent plane.
    internal static void BillboardAxes(ArVec3 right,ArVec3 up,float age,float phase,out ArVec3 x,out ArVec3 y)
    {
        var r=Orientation(age,phase);
        x=right*r.X-up*r.Y;y=right*-r.Y-up*r.X;
    }
    internal const float BodyKind=31, GlintKind=32;
    internal static bool Eligible(bool enabled,int shape,bool wisp,int origin)=>enabled && shape==6 && !wisp && origin==0;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
        =>SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color);
    internal static Vector4 Glint(Vector4 body)=>new(body.X,body.Y,body.Z,body.W*.45f);
}
