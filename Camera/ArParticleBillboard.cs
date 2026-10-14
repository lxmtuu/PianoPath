namespace PianoPath;

/// <summary>Camera-facing particle surface at a keyboard-local point, not screen-space simulation.
/// Across follows the keyboard, while the second axis faces the ray instead of going edge-on overhead.</summary>
internal static class ArParticleBillboard
{
    internal static bool TryFacing(ArCameraPose pose, ArVec3 center, out ArVec3 axisX, out ArVec3 axisY)
    {
        axisX = axisY = default;
        var point = pose.ToCamera(center);
        if (!point.IsFinite || point.Z <= ArCameraPose.NearPlane) return false;
        var ray = point.Normalized;
        var across = pose.Right - ray * ArVec3.Dot(pose.Right, ray);
        if (across.Length < 1e-8)
        {
            var fallback = Math.Abs(ray.X) < .9 ? new ArVec3(1, 0, 0) : new ArVec3(0, 1, 0);
            across = fallback - ray * ArVec3.Dot(fallback, ray);
        }
        across = across.Normalized;
        var up = ArVec3.Cross(across, ray).Normalized;
        ArVec3 Local(ArVec3 camera) => new(ArVec3.Dot(camera, pose.Right),
            ArVec3.Dot(camera, pose.Down), ArVec3.Dot(camera, pose.Normal));
        axisX = Local(across).Normalized; axisY = Local(up).Normalized;
        return axisX.IsFinite && axisY.IsFinite && axisX.Length > .99 && axisY.Length > .99;
    }
}
