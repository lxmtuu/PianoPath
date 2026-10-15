using System.Numerics;
namespace PianoPath;

/// <summary>Review-only viscous Melt pigment. Route tag separates morph beads from primary Splash and releases.</summary>
internal static class MeltParticleAppearance
{
    internal const byte Route=1;
    internal const float BodyKind=33,HaloKind=34,GlintKind=35;
    internal const float FrontBodyKind=36,FrontHaloKind=37,FrontGlintKind=38;
    internal static bool Eligible(bool enabled,int shape,bool wisp,int origin,int route)
        =>enabled && shape==5 && !wisp && origin==0 && route==Route;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
        =>SplashParticleAppearance.TryColor(birth,age,life,glow,gain,out color);
    internal static Vector4 Halo(Vector4 body)=>new(body.X,body.Y,body.Z,body.W*.18f);
    internal static Vector4 Glint(Vector4 body)=>new(body.X,body.Y,body.Z,body.W*.30f);
}
