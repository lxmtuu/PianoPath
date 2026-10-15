namespace PianoPath;

/// <summary>Camera-facing particle surface at a keyboard-local point, not screen-space simulation.
/// Across follows the keyboard, while the second axis faces the ray instead of going edge-on overhead.</summary>
internal static class ArParticleBillboard
{
    /// <summary>Camera-facing tangent plane, with its long axis following the projected outward note path.
    /// The same near-ray fallback as Ar3DNoteProjection.TryPatch keeps overhead/into-lens cases finite.</summary>
    internal static bool TryAligned(ArCameraPose pose, ArView view, ArVec3 center, ArVec3 outward,
        out ArVec3 axisX, out ArVec3 axisY)
    {
        axisX = axisY = default;
        var point = pose.ToCamera(center);
        if (!view.Valid || !point.IsFinite || point.Z <= ArCameraPose.NearPlane || !outward.IsFinite || outward.Length < .9) return false;
        var hasAxis = pose.TryProjectDelta(point, pose.DirectionToCamera(outward * -1), view, out var along);
        var length = hasAxis ? Math.Sqrt(along.X * along.X + along.Y * along.Y) : 0;
        if (!(length > 1e-5) || !double.IsFinite(length))
        {
            if (!pose.TryProjectDelta(point, pose.Down, view, out along)) along = (0, 1);
            length = Math.Sqrt(along.X * along.X + along.Y * along.Y);
            if (!(length > 1e-5) || !double.IsFinite(length)) along = (0, 1);
        }
        if (!pose.TryDirectionForScreenDelta(center, (-along.X, -along.Y), view, out var local)) return false;
        var ray = point.Normalized;
        var up = pose.DirectionToCamera(local);
        up = (up - ray * ArVec3.Dot(up, ray)).Normalized;
        var across = ArVec3.Cross(ray, up).Normalized;
        ArVec3 Local(ArVec3 camera) => new(ArVec3.Dot(camera, pose.Right), ArVec3.Dot(camera, pose.Down), ArVec3.Dot(camera, pose.Normal));
        axisX = Local(across).Normalized; axisY = Local(up).Normalized;
        return axisX.IsFinite && axisY.IsFinite && axisX.Length > .99 && axisY.Length > .99;
    }

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

/// <summary>Rigidly maps the Stage particle's rise/lateral basis onto the OUTWARD note path.
/// Notes use target - travel * distance: using travel itself would send emitted particles the wrong way.
/// This transforms rendering only; no simulation state, random draws or emission budgets change.</summary>
internal readonly record struct ArParticleFrame(ArVec3 Across, ArVec3 Depth, ArVec3 Up)
{
    internal static readonly ArParticleFrame Legacy = new(new(1, 0, 0), new(0, 1, 0), new(0, 0, -1));

    internal static ArParticleFrame ForNotes(ArNoteDirection direction)
    {
        var up = direction.Vector * -1;
        var cosine = Math.Clamp(-up.Z, -1, 1);
        var cross = ArVec3.Cross(Legacy.Up, up);
        var sine = cross.Length;
        var axis = sine > 1e-12 ? cross / sine : Legacy.Across;
        // Axis-angle avoids the unstable 1/(1+cosine) form close to a 180° turn.
        ArVec3 Rotate(ArVec3 v) => v * cosine + ArVec3.Cross(axis, v) * sine
            + axis * (ArVec3.Dot(axis, v) * (1 - cosine));
        return new(Rotate(Legacy.Across).Normalized, Rotate(Legacy.Depth).Normalized, up);
    }

    internal ArVec3 Point(ArVec3 anchor, double lateral, double rise) => anchor + Across * lateral + Up * rise;
    internal ArVec3 Velocity(double vx, double vy) => Across * vx - Up * vy;
}
