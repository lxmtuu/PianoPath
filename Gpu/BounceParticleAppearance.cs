using System.Numerics;
namespace PianoPath;

/// <summary>Review-only rounded Bounce stroke. Explicit provenance excludes primary Ember/Firework streaks.</summary>
internal static class BounceParticleAppearance
{
    internal const byte Route=2;
    internal const float Kind=39;
    internal static Vector2 Orientation(float vx,float vy)
    {
        var speed=MathF.Sqrt(vx*vx+vy*vy);
        return speed>1e-3f?new(vx/speed,vy/speed):new(0,-1);
    }
    internal static void BillboardAxes(ArVec3 right,ArVec3 up,float vx,float vy,out ArVec3 x,out ArVec3 y)
    {
        var d=Orientation(vx,vy);x=right*d.X-up*d.Y;y=right*-d.Y-up*d.X;
    }
    internal static bool Eligible(bool enabled,int shape,bool wisp,int origin,int route)
        =>enabled && shape==1 && !wisp && origin==0 && route==Route;
    internal static bool TryColor(Vector3 birth,float age,float life,float glow,float gain,out Vector4 color)
        =>SparkParticleAppearance.TryColor(birth,age,life,glow,gain,out color);
}
