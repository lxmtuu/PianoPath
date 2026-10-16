using System.Numerics;
namespace PianoPath;

/// <summary>Review-only release pigments, separated from shared primary/morph shapes by CPU provenance.</summary>
internal static class ReleaseParticleAppearance
{
    internal const byte FloatRoute=3,DissolveRoute=4,SmokeRoute=5;
    internal static bool Eligible(bool enabled,int shape,bool wisp,int origin,int route)
        =>enabled && !wisp && origin==1 && ((route is 3 or 4 && shape==5)||(route==5 && shape==3));
    internal static float BodyKind(int route)=>route==3?40:route==4?43:46;
    internal static float StageKind(int route,int component,bool front)=>BodyKind(route)+component+(front?7:0);
    internal static bool TryColor(int route,Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
    {
        if(!SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color))return false;
        var fade=1-age/life;var energy=Math.Clamp(glow*gain,0,1);
        if(route==FloatRoute)color.W=MathF.Pow(fade,.8f)*energy;
        else if(route==SmokeRoute){color.X*=.32f;color.Y*=.32f;color.Z*=.32f;color.W=MathF.Pow(fade,1.25f)*energy*.35f;}
        else if(route!=DissolveRoute){color=default;return false;}
        return true;
    }
    internal static Vector4 Halo(Vector4 body,int route)=>new(body.X,body.Y,body.Z,body.W*(route==3?.12f:.08f));
    internal static Vector4 Glint(Vector4 body,int route)=>new(body.X,body.Y,body.Z,body.W*(route==3?.22f:.10f));
}
