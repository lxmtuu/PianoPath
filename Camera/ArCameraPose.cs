namespace PianoPath;

/// <summary>A double-precision vector in the camera's physical keyboard frame, where one unit is one key depth.</summary>
internal readonly record struct ArPoseRotation(double PitchDegrees = 0, double YawDegrees = 0, double RollDegrees = 0)
{
    internal const double MinDegrees = -180, MaxDegrees = 180;
    internal bool IsValid => double.IsFinite(PitchDegrees) && double.IsFinite(YawDegrees) && double.IsFinite(RollDegrees)
        && PitchDegrees is >= MinDegrees and <= MaxDegrees && YawDegrees is >= MinDegrees and <= MaxDegrees && RollDegrees is >= MinDegrees and <= MaxDegrees;
    internal ArPoseRotation Normalised() => new(
        double.IsFinite(PitchDegrees) ? Math.Clamp(PitchDegrees, MinDegrees, MaxDegrees) : 0,
        double.IsFinite(YawDegrees) ? Math.Clamp(YawDegrees, MinDegrees, MaxDegrees) : 0,
        double.IsFinite(RollDegrees) ? Math.Clamp(RollDegrees, MinDegrees, MaxDegrees) : 0);
}

/// <summary>A Unity-style keyboard-object transform, measured in key-depth units around the calibrated keyboard centre.</summary>
internal readonly record struct ArPoseTransform(double PositionX = 0, double PositionY = 0, double PositionZ = 0,
    double PitchDegrees = 0, double YawDegrees = 0, double RollDegrees = 0, double UniformScale = 1)
{
    internal const double PositionLimit = 3;
    internal const double MinScale = .25, MaxScale = 4;
    internal static ArPoseTransform Identity => new(0, 0, 0, 0, 0, 0, 1);
    internal ArVec3 Position => new(PositionX, PositionY, PositionZ);
    internal ArPoseRotation Rotation => new(PitchDegrees, YawDegrees, RollDegrees);
    internal bool IsValid => Position.IsFinite && Math.Abs(PositionX) <= PositionLimit && Math.Abs(PositionY) <= PositionLimit
        && Math.Abs(PositionZ) <= PositionLimit && Rotation.IsValid && double.IsFinite(UniformScale)
        && UniformScale is >= MinScale and <= MaxScale;
    internal ArPoseTransform Normalised() => new(
        double.IsFinite(PositionX) ? Math.Clamp(PositionX, -PositionLimit, PositionLimit) : 0,
        double.IsFinite(PositionY) ? Math.Clamp(PositionY, -PositionLimit, PositionLimit) : 0,
        double.IsFinite(PositionZ) ? Math.Clamp(PositionZ, -PositionLimit, PositionLimit) : 0,
        Rotation.Normalised().PitchDegrees, Rotation.Normalised().YawDegrees, Rotation.Normalised().RollDegrees,
        double.IsFinite(UniformScale) ? Math.Clamp(UniformScale, MinScale, MaxScale) : 1);
    internal bool IsIdentity => Math.Abs(PositionX) < 1e-10 && Math.Abs(PositionY) < 1e-10 && Math.Abs(PositionZ) < 1e-10
        && Math.Abs(PitchDegrees) < 1e-10 && Math.Abs(YawDegrees) < 1e-10 && Math.Abs(RollDegrees) < 1e-10
        && Math.Abs(UniformScale - 1) < 1e-10;
}

internal readonly record struct ArVec3(double X, double Y, double Z)
{
    internal double LengthSquared => X * X + Y * Y + Z * Z;
    internal double Length => Math.Sqrt(LengthSquared);
    internal bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    internal ArVec3 Normalized => Length > 1e-12 && double.IsFinite(Length) ? this / Length : default;
    internal static double Dot(ArVec3 a, ArVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    internal static ArVec3 Cross(ArVec3 a, ArVec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static ArVec3 operator +(ArVec3 a, ArVec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static ArVec3 operator -(ArVec3 a, ArVec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static ArVec3 operator *(ArVec3 a, double scalar) => new(a.X * scalar, a.Y * scalar, a.Z * scalar);
    public static ArVec3 operator *(double scalar, ArVec3 a) => a * scalar;
    public static ArVec3 operator /(ArVec3 a, double scalar) => new(a.X / scalar, a.Y / scalar, a.Z / scalar);
}

/// <summary>
/// A pinhole-camera pose recovered from the calibrated keyboard homography and a horizontal field of view.
/// The keyboard is a metric plane: X is across the keys, Y is one key depth from back to front, and Z is normal to
/// the keys. Its width in key-depth units comes from the known key layout. The small 2D correction homography makes
/// the pose land exactly on the four clicked corners even when the user's FOV estimate is not the camera's true FOV;
/// off-plane points then use the estimated 3D pose rather than extending the keyboard homography to its horizon.
/// </summary>
internal readonly struct ArCameraPose
{
    internal const double DefaultFovDegrees = 70;
    internal const double MinFovDegrees = 20;
    internal const double MaxFovDegrees = 140;
    internal const double KeyboardDepthMillimetres = 150;
    internal const double WhiteKeyWidthMillimetres = 23.5;
    internal const double NearPlane = .025;

    private readonly Homography _imageCorrection;

    private ArCameraPose(double fx, double fy, double cx, double cy, double keyboardWidthDepthRatio,
        ArVec3 right, ArVec3 down, ArVec3 normal, ArVec3 translation, double fovDegrees, Homography imageCorrection)
    {
        Fx = fx; Fy = fy; Cx = cx; Cy = cy; KeyboardWidthDepthRatio = keyboardWidthDepthRatio;
        Right = right; Down = down; Normal = normal; Translation = translation;
        FovDegrees = fovDegrees; _imageCorrection = imageCorrection;
    }

    internal double Fx { get; }
    internal double Fy { get; }
    internal double Cx { get; }
    internal double Cy { get; }
    internal double KeyboardWidthDepthRatio { get; }
    internal double FovDegrees { get; }
    /// <summary>Camera-space unit vectors along the keyboard's across, back-to-front and normal axes.</summary>
    internal ArVec3 Right { get; }
    internal ArVec3 Down { get; }
    internal ArVec3 Normal { get; }
    /// <summary>Camera-space location of the keyboard's back-left corner.</summary>
    internal ArVec3 Translation { get; }

    internal static double NormalizeFov(double degrees) =>
        double.IsFinite(degrees) ? Math.Clamp(degrees, MinFovDegrees, MaxFovDegrees) : DefaultFovDegrees;

    internal static double WidthDepthRatio(KeyboardLayout layout) =>
        layout.WhiteCount * WhiteKeyWidthMillimetres / KeyboardDepthMillimetres;

    /// <summary>
    /// Decomposes a calibrated plane-to-image homography into camera axes using the supplied horizontal FOV.
    /// A correction homography is retained so the estimated pose always agrees with the clicked key area at Z = 0.
    /// </summary>
    internal static bool TryCreate(Homography toImage, IReadOnlyList<(double X, double Y)> corners, KeyboardLayout layout,
        int imageWidth, int imageHeight, double fovDegrees, out ArCameraPose pose)
    {
        pose = default;
        if (corners.Count != 4 || imageWidth <= 0 || imageHeight <= 0) return false;
        var fov = NormalizeFov(fovDegrees);
        var ratio = WidthDepthRatio(layout);
        if (!(ratio > 0) || !double.IsFinite(ratio)) return false;

        var focal = imageWidth / (2 * Math.Tan(fov * Math.PI / 360));
        if (!(focal > 0) || !double.IsFinite(focal)) return false;
        var fx = focal; var fy = focal;
        var cx = imageWidth * .5; var cy = imageHeight * .5;

        // H = K [rX * keyboardWidth, rY, t]. The calibration's horizontal coordinate is 0..1,
        // so divide its first column by the physical keyboard-width/key-depth ratio before decomposition.
        ArVec3 InverseK(double x, double y, double z) => new((x - cx * z) / fx, (y - cy * z) / fy, z);
        var xAxis = InverseK(toImage[0, 0] / ratio, toImage[1, 0] / ratio, toImage[2, 0] / ratio);
        var yAxis = InverseK(toImage[0, 1], toImage[1, 1], toImage[2, 1]);
        var translationColumn = InverseK(toImage[0, 2], toImage[1, 2], toImage[2, 2]);
        var xLength = xAxis.Length; var yLength = yAxis.Length;
        if (!(xLength > 1e-12) || !(yLength > 1e-12) || !double.IsFinite(xLength) || !double.IsFinite(yLength)) return false;
        var scale = 2 / (xLength + yLength);
        var right = xAxis.Normalized;
        // A hand-marked quad and an approximate FOV need not decompose to perfectly perpendicular axes. Gram-Schmidt
        // gives the nearest stable camera basis; the correction homography below preserves the exact 2D calibration.
        var downCandidate = yAxis - right * ArVec3.Dot(right, yAxis);
        var down = downCandidate.Normalized;
        var normal = ArVec3.Cross(right, down).Normalized;
        var translation = translationColumn * scale;
        if (!right.IsFinite || !down.IsFinite || !normal.IsFinite || !translation.IsFinite
            || right.Length < .9 || down.Length < .9 || normal.Length < .9 || translation.Z <= NearPlane) return false;

        var raw = new (double X, double Y)[4];
        var plane = new[] { (0.0, 0.0), (ratio, 0.0), (ratio, 1.0), (0.0, 1.0) };
        for (var i = 0; i < plane.Length; i++)
        {
            var camera = right * plane[i].Item1 + down * plane[i].Item2 + translation;
            if (!TryProjectRaw(camera, fx, fy, cx, cy, out raw[i])) return false;
        }
        var correction = Homography.FromPoints(raw, corners);
        if (correction is null) return false;
        pose = new ArCameraPose(fx, fy, cx, cy, ratio, right, down, normal, translation, fov, correction.Value);
        return true;
    }

    /// <summary>The point in camera coordinates for a keyboard-local point in physical key-depth units.</summary>
    internal ArVec3 ToCamera(ArVec3 keyboardPoint) =>
        Right * keyboardPoint.X + Down * keyboardPoint.Y + Normal * keyboardPoint.Z + Translation;

    /// <summary>A direction in camera coordinates; translation does not apply.</summary>
    internal ArVec3 DirectionToCamera(ArVec3 keyboardDirection) =>
        Right * keyboardDirection.X + Down * keyboardDirection.Y + Normal * keyboardDirection.Z;

    /// <summary>Projects a camera-space point through the estimated pinhole camera and the exact-calibration correction.</summary>
    internal bool TryProject(ArVec3 cameraPoint, out (double X, double Y) imagePoint)
    {
        imagePoint = default;
        if (!TryProjectRaw(cameraPoint, Fx, Fy, Cx, Cy, out var raw)) return false;
        imagePoint = _imageCorrection.Apply(raw.X, raw.Y);
        return double.IsFinite(imagePoint.X) && double.IsFinite(imagePoint.Y)
            && Math.Abs(imagePoint.X) <= ArProjection.MaxCoordinate && Math.Abs(imagePoint.Y) <= ArProjection.MaxCoordinate;
    }

    /// <summary>Projects a keyboard-local point to the output, preserving the calibration's exact on-plane image.</summary>
    internal bool TryProject(ArVec3 keyboardPoint, ArView view, out ArCorner corner)
    {
        corner = default;
        var camera = ToCamera(keyboardPoint);
        if (!(camera.Z > NearPlane) || !TryProject(camera, out var image)) return false;
        var (x, y) = view.ToOutput(image.X, image.Y);
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > ArProjection.MaxCoordinate || Math.Abs(y) > ArProjection.MaxCoordinate) return false;
        corner = new ArCorner(x, y, camera.Z);
        return true;
    }

    /// <summary>The horizontal and vertical pixel derivatives at a camera-space point, for screen-facing note geometry.</summary>
    internal bool TryProjectDelta(ArVec3 cameraPoint, ArVec3 cameraDelta, ArView view, out (double X, double Y) outputDelta)
    {
        outputDelta = default;
        const double epsilon = 1e-3;
        if (!TryProject(cameraPoint, out var origin) || !TryProject(cameraPoint + cameraDelta * epsilon, out var moved)) return false;
        var (x0, y0) = view.ToOutput(origin.X, origin.Y);
        var (x1, y1) = view.ToOutput(moved.X, moved.Y);
        outputDelta = ((x1 - x0) / epsilon, (y1 - y0) / epsilon);
        return double.IsFinite(outputDelta.X) && double.IsFinite(outputDelta.Y);
    }

    /// <summary>
    /// Finds a direction in the keyboard plane whose local camera projection follows a requested output-pixel direction.
    /// This powers the calibration preview's "drag to aim" control without changing the keyboard object's pose.
    /// </summary>
    internal bool TryDirectionForScreenDelta(ArVec3 keyboardPoint, (double X, double Y) screenDelta, ArView view, out ArVec3 direction)
    {
        direction = default;
        if (!double.IsFinite(screenDelta.X) || !double.IsFinite(screenDelta.Y) || screenDelta.X * screenDelta.X + screenDelta.Y * screenDelta.Y < 1e-12)
            return false;
        var cameraPoint = ToCamera(keyboardPoint);
        if (!TryProjectDelta(cameraPoint, DirectionToCamera(new ArVec3(1, 0, 0)), view, out var across)
            || !TryProjectDelta(cameraPoint, DirectionToCamera(new ArVec3(0, 1, 0)), view, out var depth)) return false;
        var determinant = across.X * depth.Y - depth.X * across.Y;
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-8) return false;
        var x = (screenDelta.X * depth.Y - depth.X * screenDelta.Y) / determinant;
        var y = (across.X * screenDelta.Y - screenDelta.X * across.Y) / determinant;
        direction = new ArVec3(x, y, 0).Normalized;
        return direction.Length > .9 && direction.IsFinite;
    }

    /// <summary>Moves, uniformly scales and rotates the plane using a full local 3D transform, then projects its four corners back to the picture.</summary>
    internal static bool TryTransformCorners(KeyboardLayout layout, IReadOnlyList<(double X, double Y)> corners,
        int imageWidth, int imageHeight, double fovDegrees, ArPoseTransform transform,
        out (double X, double Y)[] transformed)
    {
        transformed = [];
        if (corners.Count != 4) return false;
        if (transform == default) transform = ArPoseTransform.Identity;
        if (!transform.IsValid) return false;
        if (transform.IsIdentity)
        {
            transformed = corners.ToArray();
            return true;
        }
        var homography = Homography.FromPoints([(0, 0), (1, 0), (1, 1), (0, 1)], corners);
        if (homography is null || !TryCreate(homography.Value, corners, layout, imageWidth, imageHeight, fovDegrees, out var pose)) return false;
        var ratio = pose.KeyboardWidthDepthRatio;
        var centre = new ArVec3(ratio * .5, .5, 0);
        var angles = (Pitch: transform.PitchDegrees * Math.PI / 180, Yaw: transform.YawDegrees * Math.PI / 180,
            Roll: transform.RollDegrees * Math.PI / 180);
        var uv = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) };
        transformed = new (double X, double Y)[4];
        for (var i = 0; i < uv.Length; i++)
        {
            var local = new ArVec3(uv[i].Item1 * ratio, uv[i].Item2, 0);
            local = centre + transform.Position + Rotate((local - centre) * transform.UniformScale, angles.Pitch, angles.Yaw, angles.Roll);
            var camera = pose.ToCamera(local);
            if (!(camera.Z > NearPlane) || !pose.TryProject(camera, out transformed[i])) { transformed = []; return false; }
        }
        if (CalibrationQuad.CheckAnyWinding(transformed, imageWidth, imageHeight) != CalibrationProblem.None) { transformed = []; return false; }
        return true;
    }

    /// <summary>Compatibility helper for rotation-only callers.</summary>
    internal static bool TryRotateCorners(KeyboardLayout layout, IReadOnlyList<(double X, double Y)> corners,
        int imageWidth, int imageHeight, double fovDegrees, double pitchDegrees, double yawDegrees, double rollDegrees,
        out (double X, double Y)[] rotated) =>
        TryTransformCorners(layout, corners, imageWidth, imageHeight, fovDegrees,
            new ArPoseTransform(0, 0, 0, pitchDegrees, yawDegrees, rollDegrees, 1), out rotated);

    private static ArVec3 Rotate(ArVec3 p, double pitch, double yaw, double roll)
    {
        var cp = Math.Cos(pitch); var sp = Math.Sin(pitch);
        p = new ArVec3(p.X, cp * p.Y - sp * p.Z, sp * p.Y + cp * p.Z);
        var cy = Math.Cos(yaw); var sy = Math.Sin(yaw);
        p = new ArVec3(cy * p.X + sy * p.Z, p.Y, -sy * p.X + cy * p.Z);
        var cr = Math.Cos(roll); var sr = Math.Sin(roll);
        return new ArVec3(cr * p.X - sr * p.Y, sr * p.X + cr * p.Y, p.Z);
    }

    private static bool TryProjectRaw(ArVec3 cameraPoint, double fx, double fy, double cx, double cy, out (double X, double Y) imagePoint)
    {
        imagePoint = default;
        if (!cameraPoint.IsFinite || !(cameraPoint.Z > NearPlane)) return false;
        var x = fx * cameraPoint.X / cameraPoint.Z + cx;
        var y = fy * cameraPoint.Y / cameraPoint.Z + cy;
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > ArProjection.MaxCoordinate || Math.Abs(y) > ArProjection.MaxCoordinate) return false;
        imagePoint = (x, y);
        return true;
    }
}
