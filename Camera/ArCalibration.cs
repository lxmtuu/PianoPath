using System.IO;
using System.Text.Json;

namespace PianoPath;

/// <summary>Why four clicked points do not make a key area the overlay can work with; <see cref="None"/> when they do.</summary>
internal enum CalibrationProblem
{
    None,
    /// <summary>Fewer or more than four points.</summary>
    NotFourPoints,
    /// <summary>A coordinate is not a number, or the picture has no size.</summary>
    NotFinite,
    /// <summary>A point lies far outside the picture.</summary>
    OutsideImage,
    /// <summary>The points go round the wrong way (or the picture was mirrored): they should run back-left, back-right, front-right, front-left.</summary>
    WrongOrder,
    /// <summary>The outline folds over itself, or a corner is a reflex angle.</summary>
    NotConvex,
    /// <summary>Three points on one line, or two on top of each other.</summary>
    Degenerate,
    /// <summary>The area is a sliver of the picture, or too short to be a keyboard.</summary>
    TooSmall,
    /// <summary>The keys would be a hair's breadth deep.</summary>
    TooThin,
    /// <summary>One long edge is many times longer than the other: not a keyboard seen by a camera.</summary>
    TooSkewed,
}

/// <summary>
/// Judging and tidying the four corners a person clicks on the camera picture. Pure arithmetic, so the wizard's
/// refusals are stated by tests instead of discovered on a camera.
///
/// <para>
/// The corners are the key area's: back-left, back-right, front-right, front-left, where the back is the end of the
/// keys the black keys stand at and the front is the end nearest the player. In picture coordinates (y grows
/// downward) that is clockwise for a camera above the keys looking down.
/// </para>
/// </summary>
internal static class CalibrationQuad
{
    /// <summary>The smallest share of the picture's area the key area may cover (a real one covers a fifth or more).</summary>
    internal const double MinAreaShare = 0.01;

    /// <summary>The key area's longer end edge must reach at least this share of the picture's width.</summary>
    internal const double MinWidthShare = 0.15;

    /// <summary>Both side edges must reach at least this share of the picture's height, or the keys have no depth.</summary>
    internal const double MinDepthShare = 0.02;

    /// <summary>The longer end edge may be at most this many times the shorter (strong perspective is 2 or 3).</summary>
    internal const double MaxEdgeRatio = 6;

    /// <summary>A point may lie this share of the picture's size beyond its edge: a keyboard can run off the frame.</summary>
    internal const double OutsideShare = 0.25;

    /// <summary>The first problem with these corners in this picture, or <see cref="CalibrationProblem.None"/>.</summary>
    internal static CalibrationProblem Check(IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight)
    {
        if (corners.Count != 4) return CalibrationProblem.NotFourPoints;
        if (imageWidth <= 0 || imageHeight <= 0) return CalibrationProblem.NotFinite;
        foreach (var (x, y) in corners)
            if (!double.IsFinite(x) || !double.IsFinite(y)) return CalibrationProblem.NotFinite;

        foreach (var (x, y) in corners)
            if (x < -OutsideShare * imageWidth || x > (1 + OutsideShare) * imageWidth ||
                y < -OutsideShare * imageHeight || y > (1 + OutsideShare) * imageHeight)
                return CalibrationProblem.OutsideImage;

        // Every corner must turn the same way. A zero turn is three points on a line (or a repeated point).
        int positive = 0, negative = 0;
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i]; var b = corners[(i + 1) % 4]; var c = corners[(i + 2) % 4];
            double abx = b.X - a.X, aby = b.Y - a.Y, bcx = c.X - b.X, bcy = c.Y - b.Y;
            var cross = abx * bcy - aby * bcx;
            var scale = Math.Sqrt(abx * abx + aby * aby) * Math.Sqrt(bcx * bcx + bcy * bcy);
            if (!(Math.Abs(cross) > 1e-6 * scale)) return CalibrationProblem.Degenerate;
            if (cross > 0) positive++; else negative++;
        }
        if (positive == 0) return CalibrationProblem.WrongOrder;   // all four turn the other way: the same shape, named backwards
        if (negative > 0) return CalibrationProblem.NotConvex;

        var area = Area(corners);
        if (area < MinAreaShare * imageWidth * imageHeight) return CalibrationProblem.TooSmall;
        var back = Distance(corners[0], corners[1]); var front = Distance(corners[3], corners[2]);
        var left = Distance(corners[0], corners[3]); var right = Distance(corners[1], corners[2]);
        if (Math.Max(back, front) < MinWidthShare * imageWidth) return CalibrationProblem.TooSmall;
        if (Math.Min(left, right) < MinDepthShare * imageHeight) return CalibrationProblem.TooThin;
        if (Math.Max(back, front) > MaxEdgeRatio * Math.Min(back, front)) return CalibrationProblem.TooSkewed;
        if (Math.Max(left, right) > MaxEdgeRatio * Math.Min(left, right)) return CalibrationProblem.TooSkewed;
        return CalibrationProblem.None;
    }

    /// <summary>
    /// <see cref="Check"/> for corners that may go round either way. A camera that looks at the keyboard from the far side, or whose
    /// picture arrives turned round, sees it as a mirror image: back-left, back-right, front-right, front-left then turn the other
    /// way in the picture, and that is a real keyboard, not a mistake. Everything else is judged as <see cref="Check"/> does, on the same
    /// corners turned round so that they run the usual way.
    /// </summary>
    internal static CalibrationProblem CheckAnyWinding(IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight)
    {
        var problem = Check(corners, imageWidth, imageHeight);
        return problem == CalibrationProblem.WrongOrder ? Check(Reflect(corners, imageWidth, imageHeight, flipX: true, flipY: false), imageWidth, imageHeight) : problem;
    }

    /// <summary>The points turned round left to right (<paramref name="flipX"/>) and/or top to bottom (<paramref name="flipY"/>) in a picture of this size. Doing it twice gives the points back.</summary>
    internal static (double X, double Y)[] Reflect(IReadOnlyList<(double X, double Y)> points, int imageWidth, int imageHeight, bool flipX, bool flipY) =>
        [.. points.Select(p => (flipX ? imageWidth - p.X : p.X, flipY ? imageHeight - p.Y : p.Y))];

    /// <summary>
    /// The four points put into the order the overlay wants — back-left, back-right, front-right, front-left —
    /// judged by where they are in the picture (top-left is the one with the smallest x + y, and so on). Null when
    /// that does not name four different points, as when the keyboard is turned a quarter round.
    /// </summary>
    internal static (double X, double Y)[]? Order(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count != 4) return null;
        int topLeft = 0, bottomRight = 0, topRight = 0, bottomLeft = 0;
        for (var i = 0; i < 4; i++)
        {
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y)) return null;
            if (points[i].X + points[i].Y < points[topLeft].X + points[topLeft].Y) topLeft = i;
            if (points[i].X + points[i].Y > points[bottomRight].X + points[bottomRight].Y) bottomRight = i;
            if (points[i].X - points[i].Y > points[topRight].X - points[topRight].Y) topRight = i;
            if (points[i].X - points[i].Y < points[bottomLeft].X - points[bottomLeft].Y) bottomLeft = i;
        }
        if (new HashSet<int> { topLeft, topRight, bottomRight, bottomLeft }.Count != 4) return null;
        return [points[topLeft], points[topRight], points[bottomRight], points[bottomLeft]];
    }

    /// <summary>The polygon's area by the shoelace rule, positive when the points run clockwise in picture coordinates.</summary>
    internal static double Area(IReadOnlyList<(double X, double Y)> polygon)
    {
        double sum = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>
/// A keyboard found in a camera picture: the key area's four corners, the map between keyboard coordinates and
/// picture pixels that they define, and everything the overlay needs to know about keys through it — where a key's
/// top is, the polygon it covers, which key is under a pixel.
///
/// <para>
/// Keyboard coordinates: <c>u</c> runs across the keys 0..1 over the layout's key area, <c>v</c> from the back of the
/// keys 0 to their front 1 — the same ones <c>tools/make_ar_sample.py</c> draws with.
/// </para>
/// </summary>
internal sealed record ArStoredCalibration((double X, double Y)[] Corners, (double X, double Y)[] PoseBaseCorners,
    double CameraFovDegrees, ArPoseTransform Transform)
{
    internal ArStoredCalibration((double X, double Y)[] corners, (double X, double Y)[] poseBaseCorners,
        double cameraFovDegrees, ArPoseRotation rotation)
        : this(corners, poseBaseCorners, cameraFovDegrees,
            new ArPoseTransform(0, 0, 0, rotation.PitchDegrees, rotation.YawDegrees, rotation.RollDegrees, 1)) { }

    internal ArPoseRotation Rotation => Transform.Rotation;
}

internal sealed class ArCalibration
{
    /// <summary>How far down the key area the black keys reach: 95 mm of a 150 mm white key.</summary>
    internal const double BlackLengthShare = 95.0 / 150.0;

    private ArCalibration(KeyboardLayout layout, (double X, double Y)[] corners, (double X, double Y)[] poseBaseCorners,
        int width, int height, Homography toImage, Homography toKeyboard, double cameraFovDegrees, ArPoseTransform transform, ArCameraPose? cameraPose)
    {
        Layout = layout; Corners = corners; PoseBaseCorners = poseBaseCorners; ImageWidth = width; ImageHeight = height;
        ToImage = toImage; ToKeyboard = toKeyboard; CameraFovDegrees = cameraFovDegrees; Transform = transform; CameraPose = cameraPose;
    }

    internal KeyboardLayout Layout { get; }

    /// <summary>Back-left, back-right, front-right, front-left, in picture pixels, after applying the saved 3D object transform.</summary>
    internal (double X, double Y)[] Corners { get; }

    /// <summary>The untransformed four-corner baseline from which the saved absolute 3D object transform is applied.</summary>
    internal (double X, double Y)[] PoseBaseCorners { get; }

    internal ArPoseTransform Transform { get; }
    internal ArPoseRotation Rotation => Transform.Rotation;
    internal double PitchDegrees => Transform.PitchDegrees;
    internal double YawDegrees => Transform.YawDegrees;
    internal double RollDegrees => Transform.RollDegrees;

    internal int ImageWidth { get; }

    internal int ImageHeight { get; }

    /// <summary>The horizontal camera field of view used to estimate a 3D pose from this plane homography.</summary>
    internal double CameraFovDegrees { get; }

    /// <summary>The approximate pinhole-camera pose; null only if the homography cannot be decomposed safely.</summary>
    internal ArCameraPose? CameraPose { get; }

    /// <summary>Keyboard coordinates to picture pixels.</summary>
    internal Homography ToImage { get; }

    /// <summary>Picture pixels to keyboard coordinates.</summary>
    internal Homography ToKeyboard { get; }

    /// <summary>
    /// Builds the calibration for <paramref name="layout"/> from four corners, or null with the reason in
    /// <paramref name="problem"/>: the corners may not be used when <see cref="CalibrationQuad.CheckAnyWinding"/> finds
    /// anything wrong or when they define no usable map.
    /// </summary>
    internal static ArCalibration? Create(KeyboardLayout layout, IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight,
        out CalibrationProblem problem, double cameraFovDegrees = ArCameraPose.DefaultFovDegrees,
        ArPoseRotation rotation = default, IReadOnlyList<(double X, double Y)>? poseBaseCorners = null) =>
        Create(layout, corners, imageWidth, imageHeight, out problem, cameraFovDegrees,
            new ArPoseTransform(0, 0, 0, rotation.PitchDegrees, rotation.YawDegrees, rotation.RollDegrees, 1), poseBaseCorners);

    internal static ArCalibration? Create(KeyboardLayout layout, IReadOnlyList<(double X, double Y)> corners, int imageWidth, int imageHeight,
        out CalibrationProblem problem, double cameraFovDegrees, ArPoseTransform transform,
        IReadOnlyList<(double X, double Y)>? poseBaseCorners = null)
    {
        problem = CalibrationQuad.CheckAnyWinding(corners, imageWidth, imageHeight);
        if (problem != CalibrationProblem.None) return null;
        var copy = corners.ToArray();
        var toImage = Homography.FromPoints([(0, 0), (1, 0), (1, 1), (0, 1)], copy);
        var toKeyboard = toImage?.Inverse();
        if (toImage is null || toKeyboard is null) { problem = CalibrationProblem.Degenerate; return null; }
        var fov = ArCameraPose.NormalizeFov(cameraFovDegrees);
        var pose = ArCameraPose.TryCreate(toImage.Value, copy, layout, imageWidth, imageHeight, fov, out var solved) ? solved : (ArCameraPose?)null;
        var baseValid = poseBaseCorners is { Count: 4 }
            && CalibrationQuad.CheckAnyWinding(poseBaseCorners, imageWidth, imageHeight) == CalibrationProblem.None;
        var baseCorners = baseValid ? poseBaseCorners!.ToArray() : copy.ToArray();
        var savedTransform = baseValid && transform.IsValid ? transform : ArPoseTransform.Identity;
        return new ArCalibration(layout, copy, baseCorners, imageWidth, imageHeight, toImage.Value, toKeyboard.Value, fov, savedTransform, pose);
    }

    /// <summary>The picture pixel of a point of the keyboard.</summary>
    internal (double X, double Y) ToPicture(double u, double v) => ToImage.Apply(u, v);

    /// <summary>The keyboard coordinates of a picture pixel; NaN for a pixel on the horizon of the map.</summary>
    internal (double U, double V) ToKeys(double x, double y) => ToKeyboard.Apply(x, y);

    /// <summary>
    /// Where the middle of a key's top edge is in the picture: a quarter of the way down a black key, four fifths down
    /// a white one (<c>tools/make_ar_sample.py</c> takes the same points).
    /// </summary>
    internal (double X, double Y) KeyTopCentre(int pitch) =>
        ToPicture(Layout.CenterX(pitch), KeyboardLayout.IsBlack(pitch) ? 0.25 : 0.8);

    /// <summary>
    /// The picture polygon a key covers, as four corners (back-left, back-right, front-right, front-left): a white
    /// key over the whole depth of the keys, a black one over the back <see cref="BlackLengthShare"/> of it. A black
    /// key is drawn over the white ones, which is why a white polygon is the whole key and not the part left visible.
    /// </summary>
    internal (double X, double Y)[] KeyPolygon(int pitch)
    {
        double left, right, depth;
        if (KeyboardLayout.IsBlack(pitch)) { (left, right) = Layout.Span(pitch); depth = BlackLengthShare; }
        else { var centre = Layout.CenterX(pitch); left = centre - Layout.WhiteWidth / 2; right = centre + Layout.WhiteWidth / 2; depth = 1; }
        return [ToPicture(left, 0), ToPicture(right, 0), ToPicture(right, depth), ToPicture(left, depth)];
    }

    /// <summary>True when the pixel is inside the key area.</summary>
    internal bool Contains(double x, double y)
    {
        var (u, v) = ToKeys(x, y);
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }

    /// <summary>The key under a picture pixel, or null when the pixel is outside the key area.</summary>
    internal int? PitchAt(double x, double y)
    {
        var (u, v) = ToKeys(x, y);
        if (!(u >= 0 && u <= 1 && v >= 0 && v <= 1)) return null;   // also false for NaN
        return Layout.PitchAt(u, inBlackRow: v < BlackLengthShare);
    }
}

/// <summary>
/// The model behind the four-point calibration wizard: the points placed so far, the one picked up, and whether
/// they are good enough to finish. The window draws it and feeds it clicks, drags and arrow keys; every decision —
/// which point a click picks, where a point may go, when Done is allowed — is made here.
/// </summary>
internal sealed class CalibrationEditor
{
    private readonly List<(double X, double Y)> _points = [];
    private bool _preservePointOrder;
    private readonly KeyboardLayout _layout;
    private (double X, double Y)[]? _poseBaseCorners;
    private ArPoseTransform _poseTransform = ArPoseTransform.Identity;

    internal CalibrationEditor(int imageWidth, int imageHeight, KeyboardLayout? layout = null)
    {
        ImageWidth = imageWidth; ImageHeight = imageHeight; _layout = layout ?? KeyboardLayout.Standard88;
    }

    internal int ImageWidth { get; }

    internal int ImageHeight { get; }

    /// <summary>The points placed so far, in the order they were placed.</summary>
    internal IReadOnlyList<(double X, double Y)> Points => _points;

    internal bool IsComplete => _points.Count == 4;

    /// <summary>The point being moved by the arrow keys, or -1.</summary>
    internal int Selected { get; private set; } = -1;

    /// <summary>The names of the corners in the order they are asked for.</summary>
    internal static readonly string[] CornerNames = ["back-left", "back-right", "front-right", "front-left"];

    internal ArPoseTransform PoseTransform => _poseTransform;

    /// <summary>
    /// What is wrong with the points as they stand. <see cref="CalibrationProblem.NotFourPoints"/> until all four are
    /// placed; after that the verdict of <see cref="CalibrationQuad.Check"/>, so a drag that makes the outline fold
    /// shows it at once, while the person is still dragging.
    /// </summary>
    internal CalibrationProblem Problem => _preservePointOrder
        ? CalibrationQuad.CheckAnyWinding(_points, ImageWidth, ImageHeight)
        : CalibrationQuad.Check(Seen(_points), ImageWidth, ImageHeight);

    /// <summary>True when the keyboard shows left to right turned round in the picture as delivered (low notes on the right).</summary>
    internal bool FlipX { get; private set; }

    /// <summary>True when the keyboard shows top to bottom turned round in the picture as delivered (the far edge at the bottom).</summary>
    internal bool FlipY { get; private set; }

    /// <summary>
    /// Says how the keyboard sits in the picture as delivered: the points are judged and put in order as if the picture were turned
    /// round that way, which for a keyboard that is upside down in the picture is the way it looks right.
    /// </summary>
    internal void SetFlips(bool flipX, bool flipY)
    {
        var flipsChanged = FlipX != flipX || FlipY != flipY;
        if (_preservePointOrder && _points.Count == 4 && flipsChanged)
        {
            var current = _points.ToArray();
            int source = 0;
            for (var i = 0; i < 4; i++)
            {
                source = i;
                if (FlipX != flipX) source = source switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
                if (FlipY != flipY) source = source switch { 0 => 3, 1 => 2, 2 => 1, _ => 0 };
                _points[i] = current[source];
            }
        }
        FlipX = flipX; FlipY = flipY;
        if (_points.Count == 4 && flipsChanged) RememberPoseBase();
    }

    /// <summary>The points as they would be if the keyboard sat upright in the picture.</summary>
    private (double X, double Y)[] Seen(IReadOnlyList<(double X, double Y)> points) => CalibrationQuad.Reflect(points, ImageWidth, ImageHeight, FlipX, FlipY);

    /// <summary>
    /// The points as the overlay can use them. A picture with the keyboard upright — low notes on the left, the player at
    /// the bottom — is the usual one, so when <see cref="CalibrationQuad.Order"/> can name the four corners by where they
    /// are in the picture and the result passes <see cref="CalibrationQuad.Check"/>, that order is used: it repairs a
    /// start at the wrong corner, a turn the wrong way round, and a click order that would have turned the keyboard
    /// upside down. Corners that cannot be named that way (a keyboard seen at a steep angle) are taken as clicked when
    /// they pass the check; otherwise null.
    /// </summary>
    private (double X, double Y)[]? Usable()
    {
        var points = _points.ToArray();
        if (_preservePointOrder)
            return CalibrationQuad.CheckAnyWinding(points, ImageWidth, ImageHeight) == CalibrationProblem.None ? points : null;
        var upright = Seen(points);
        if (CalibrationQuad.Order(upright) is { } ordered && CalibrationQuad.Check(ordered, ImageWidth, ImageHeight) == CalibrationProblem.None)
            return CalibrationQuad.Reflect(ordered, ImageWidth, ImageHeight, FlipX, FlipY);
        return CalibrationQuad.Check(upright, ImageWidth, ImageHeight) == CalibrationProblem.None ? points : null;
    }

    /// <summary>The four corners as the overlay would use them (put in order when they only need that), or null when they are not usable yet.</summary>
    internal IReadOnlyList<(double X, double Y)>? UsableCorners => Usable();

    /// <summary>Done is allowed when the points are fine, or only need putting in order.</summary>
    internal bool CanFinish => Usable() is not null;

    /// <summary>Places the next point. False when all four are already placed. The point is held inside the allowed margin of the picture.</summary>
    internal bool Place(double x, double y)
    {
        if (IsComplete || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        _points.Add(Hold(x, y));
        Selected = _points.Count - 1;
        if (IsComplete) RememberPoseBase();
        return true;
    }

    /// <summary>Moves a placed point to a position; false for an index that is not a placed point or a position that is not a number.</summary>
    internal bool Move(int index, double x, double y)
    {
        if (index < 0 || index >= _points.Count || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        var next = Hold(x, y);
        var changed = _points[index] != next;
        _points[index] = next;
        Selected = index;
        if (changed && IsComplete) RememberPoseBase();
        return true;
    }

    /// <summary>Loads a saved, already ordered key area and its original 3D object-transform baseline.</summary>
    internal bool LoadCorners(IReadOnlyList<(double X, double Y)> corners,
        IReadOnlyList<(double X, double Y)>? poseBaseCorners = null)
    {
        if (CalibrationQuad.CheckAnyWinding(corners, ImageWidth, ImageHeight) != CalibrationProblem.None) return false;
        _points.Clear();
        _points.AddRange(corners);
        Selected = _points.Count - 1;
        _preservePointOrder = true;
        _poseBaseCorners = poseBaseCorners is { Count: 4 }
            && CalibrationQuad.CheckAnyWinding(poseBaseCorners, ImageWidth, ImageHeight) == CalibrationProblem.None
            ? poseBaseCorners.ToArray() : corners.ToArray();
        _poseTransform = ArPoseTransform.Identity;
        return true;
    }

    internal bool LoadCalibration(IReadOnlyList<(double X, double Y)> corners,
        IReadOnlyList<(double X, double Y)> poseBaseCorners, ArPoseTransform transform, double fovDegrees)
    {
        if (!LoadCorners(poseBaseCorners)) return false;
        return SetPoseTransform(transform, fovDegrees) || LoadCorners(corners, poseBaseCorners);
    }

    /// <summary>Sets a complete absolute object transform from its last user-marked baseline, so input order cannot accumulate drift.</summary>
    internal bool SetPoseTransform(ArPoseTransform transform, double fovDegrees)
    {
        if (!IsComplete || !transform.IsValid) return false;
        var source = _poseBaseCorners ?? (_preservePointOrder ? _points.ToArray() : Usable());
        if (source is null || !ArCameraPose.TryTransformCorners(_layout, source, ImageWidth, ImageHeight, fovDegrees, transform, out var transformed)) return false;
        _points.Clear();
        _points.AddRange(transformed);
        _preservePointOrder = true;
        _poseTransform = transform;
        return true;
    }

    /// <summary>Compatibility helper for rotation-only controls.</summary>
    internal bool SetPoseRotation(double pitchDegrees, double yawDegrees, double rollDegrees, double fovDegrees) =>
        SetPoseTransform(_poseTransform with { PitchDegrees = pitchDegrees, YawDegrees = yawDegrees, RollDegrees = rollDegrees }, fovDegrees);

    internal bool ResetPoseTransform(double fovDegrees)
    {
        if (!IsComplete) { _poseTransform = ArPoseTransform.Identity; return true; }
        return SetPoseTransform(ArPoseTransform.Identity, fovDegrees);
    }

    private void RememberPoseBase()
    {
        var source = _preservePointOrder ? _points.ToArray() : Usable();
        _poseBaseCorners = source?.ToArray();
        _poseTransform = ArPoseTransform.Identity;
    }

    /// <summary>Moves a placed point by a number of pixels, as the arrow keys do.</summary>
    internal bool Nudge(int index, double dx, double dy) =>
        index >= 0 && index < _points.Count && Move(index, _points[index].X + dx, _points[index].Y + dy);

    /// <summary>
    /// The placed point nearest to a position and within <paramref name="radius"/> pixels of it, or -1: what a mouse press
    /// picks up. The nearest wins when two are in reach, and the later-placed one when they tie.
    /// </summary>
    internal int HitTest(double x, double y, double radius)
    {
        var best = -1; var bestDistance = double.MaxValue;
        for (var i = 0; i < _points.Count; i++)
        {
            var distance = Math.Sqrt((_points[i].X - x) * (_points[i].X - x) + (_points[i].Y - y) * (_points[i].Y - y));
            if (distance <= radius && distance <= bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>Takes the last point back; false when there is none.</summary>
    internal bool Undo()
    {
        if (_points.Count == 0) return false;
        _points.RemoveAt(_points.Count - 1);
        Selected = _points.Count - 1;
        _poseBaseCorners = null;
        _poseTransform = ArPoseTransform.Identity;
        if (IsComplete) RememberPoseBase();
        return true;
    }

    /// <summary>Starts again.</summary>
    internal void Clear() { _points.Clear(); Selected = -1; _preservePointOrder = false; _poseBaseCorners = null; _poseTransform = ArPoseTransform.Identity; }

    /// <summary>
    /// Finishes: puts the points in order if they only need that, and builds the calibration for
    /// <paramref name="layout"/>. Null — with the reason — when the points are not good enough; the editor is not
    /// changed then.
    /// </summary>
    internal ArCalibration? Finish(KeyboardLayout layout, double cameraFovDegrees, ArPoseTransform transform, out CalibrationProblem problem)
    {
        if (Usable() is not { } points) { problem = Problem; return null; }       // judged the strict way: Create alone would take corners that run the other way
        var baseCorners = _poseBaseCorners ?? points;
        var result = ArCalibration.Create(layout, points, ImageWidth, ImageHeight, out problem, cameraFovDegrees, transform, baseCorners);
        if (result is not null) { _points.Clear(); _points.AddRange(points); }
        return result;
    }

    internal ArCalibration? Finish(KeyboardLayout layout, double cameraFovDegrees, ArPoseRotation rotation, out CalibrationProblem problem) =>
        Finish(layout, cameraFovDegrees, _poseTransform with
        { PitchDegrees = rotation.PitchDegrees, YawDegrees = rotation.YawDegrees, RollDegrees = rotation.RollDegrees }, out problem);

    internal ArCalibration? Finish(KeyboardLayout layout, double cameraFovDegrees, out CalibrationProblem problem) =>
        Finish(layout, cameraFovDegrees, _poseTransform, out problem);

    internal ArCalibration? Finish(KeyboardLayout layout, out CalibrationProblem problem) =>
        Finish(layout, ArCameraPose.DefaultFovDegrees, _poseTransform, out problem);

    private (double X, double Y) Hold(double x, double y) => (
        Math.Clamp(x, -CalibrationQuad.OutsideShare * ImageWidth, (1 + CalibrationQuad.OutsideShare) * ImageWidth),
        Math.Clamp(y, -CalibrationQuad.OutsideShare * ImageHeight, (1 + CalibrationQuad.OutsideShare) * ImageHeight));
}

/// <summary>
/// The calibrations kept on this computer, one file in the settings folder. Each is for one camera, one view of that
/// camera, one picture size and one keyboard size — turning the camera to another angle, changing its resolution or the
/// keyboard size means the old four corners no longer mean anything — and a settings preset or a share code never
/// carries one.
///
/// <para>
/// A file that cannot be read, or an entry that would not pass <see cref="CalibrationQuad.CheckAnyWinding"/>, is dropped
/// instead of trusted: the worst a damaged file can do is ask for a new calibration. So is an entry that names a view the
/// app does not know, which is what keeps corners marked for one angle from ever being read as another angle's. Saving
/// writes a temporary file and moves it into place, so a crash cannot leave half a file.
/// </para>
/// </summary>
internal sealed class ArCalibrationStore
{
    /// <summary>The most calibrations kept; the least recently saved are forgotten beyond it.</summary>
    internal const int MaxEntries = 64;

    internal const string FileName = "ar-calibration.json";

    /// <summary>
    /// The version this app writes. Version 2 adds the camera view, version 3 adds the estimated horizontal FOV, and
    /// version 4 stores the 3D rotation and its unrotated baseline, and version 5 adds local translation and uniform scale.
    /// Versions 1–2 use 70° FOV; version 3 retains its saved FOV. Pre-v4 files load at identity rotation, and v1–v4
    /// entries default to zero translation and unit scale.
    /// </summary>
    internal const int FormatVersion = 5;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly string _path;
    private List<Entry> _entries = [];

    internal ArCalibrationStore(string directory)
    {
        _path = Path.Combine(directory, FileName);
        Reload();
    }

    internal string FilePath => _path;

    internal int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>The corners saved for this camera, view, picture size and keyboard size, or null when there are none.</summary>
    internal (double X, double Y)[]? Get(string cameraLink, string profileId, int imageWidth, int imageHeight, int keyCount)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => Matches(e, cameraLink, profileId, imageWidth, imageHeight, keyCount));
            return entry is null ? null : Points(entry);
        }
    }

    /// <summary>The saved camera FOV for this calibration, or the backward-compatible 70° default when it predates 3D pose data.</summary>
    internal double GetCameraFov(string cameraLink, string profileId, int imageWidth, int imageHeight, int keyCount)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => Matches(e, cameraLink, profileId, imageWidth, imageHeight, keyCount));
            return ArCameraPose.NormalizeFov(entry?.CameraFovDegrees ?? ArCameraPose.DefaultFovDegrees);
        }
    }

    /// <summary>Returns the complete stored calibration, including the original baseline for absolute 3D rotation controls.</summary>
    internal ArStoredCalibration? GetCalibration(string cameraLink, string profileId, int imageWidth, int imageHeight, int keyCount)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => Matches(e, cameraLink, profileId, imageWidth, imageHeight, keyCount));
            if (entry is null) return null;
            var corners = Points(entry);
            var baseline = Points(entry.PoseBaseCorners) is { Length: 4 } savedBase ? savedBase : corners.ToArray();
            var transform = Transform(entry);
            if (!transform.IsValid || baseline.Length != 4
                || CalibrationQuad.CheckAnyWinding(baseline, imageWidth, imageHeight) != CalibrationProblem.None)
            {
                transform = ArPoseTransform.Identity;
                baseline = corners.ToArray();
            }
            return new ArStoredCalibration(corners, baseline, ArCameraPose.NormalizeFov(entry.CameraFovDegrees), transform);
        }
    }

    /// <summary>
    /// True when this camera and view have a calibration at some other picture size or keyboard size but not at this one,
    /// which is what lets the app say "calibrate again" instead of "never calibrated".
    /// </summary>
    internal bool HasOtherSize(string cameraLink, string profileId, int imageWidth, int imageHeight, int keyCount)
    {
        var profile = ArCalibrationProfiles.Normalize(profileId).Id;
        lock (_gate)
            return !_entries.Any(e => Matches(e, cameraLink, profile, imageWidth, imageHeight, keyCount)) &&
                   _entries.Any(e => string.Equals(e.CameraLink, cameraLink, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Saves the final corners, FOV, full 3D object transform and its original baseline for this camera/view/size/layout.</summary>
    internal bool Set(string cameraLink, string profileId, int imageWidth, int imageHeight, int keyCount,
        IReadOnlyList<(double X, double Y)> corners, double cameraFovDegrees = ArCameraPose.DefaultFovDegrees,
        double pitchDegrees = 0, double yawDegrees = 0, double rollDegrees = 0,
        IReadOnlyList<(double X, double Y)>? poseBaseCorners = null,
        double positionX = 0, double positionY = 0, double positionZ = 0, double uniformScale = 1)
    {
        if (CalibrationQuad.CheckAnyWinding(corners, imageWidth, imageHeight) != CalibrationProblem.None) return false;
        var profile = ArCalibrationProfiles.Normalize(profileId).Id;
        var fov = ArCameraPose.NormalizeFov(cameraFovDegrees);
        var transform = new ArPoseTransform(positionX, positionY, positionZ, pitchDegrees, yawDegrees, rollDegrees, uniformScale);
        var baselineValid = poseBaseCorners is { Count: 4 }
            && CalibrationQuad.CheckAnyWinding(poseBaseCorners, imageWidth, imageHeight) == CalibrationProblem.None;
        // A bad pose means junk, junk is thrown away; a missing or bad BASELINE only means "this calibration did
        // not come from the QUALIFY dial" — the pose keeps its translation and scale and the corners replace the
        // baseline, so moving a calibration between cameras does not flatten what the user dialled in.
        if (!transform.IsValid) transform = ArPoseTransform.Identity;
        var baseline = baselineValid && poseBaseCorners is not null ? poseBaseCorners : corners;
        lock (_gate)
        {
            _entries.RemoveAll(e => Matches(e, cameraLink, profile, imageWidth, imageHeight, keyCount));
            _entries.Add(new Entry
            {
                CameraLink = cameraLink ?? "", Profile = profile, ImageWidth = imageWidth, ImageHeight = imageHeight, KeyCount = keyCount,
                CameraFovDegrees = fov, PosePitchDegrees = transform.PitchDegrees, PoseYawDegrees = transform.YawDegrees, PoseRollDegrees = transform.RollDegrees,
                PosePositionX = transform.PositionX, PosePositionY = transform.PositionY, PosePositionZ = transform.PositionZ, PoseUniformScale = transform.UniformScale,
                Corners = corners.SelectMany(p => new[] { p.X, p.Y }).ToArray(),
                PoseBaseCorners = baseline.SelectMany(p => new[] { p.X, p.Y }).ToArray(),
            });
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            return Write();
        }
    }

    /// <summary>Forgets the key area of this camera and view at this picture size and keyboard size ("Reset calibration"); true when there was one.</summary>
    internal bool Remove(string cameraLink, string profileId, int imageWidth, int imageHeight, int keyCount)
    {
        var profile = ArCalibrationProfiles.Normalize(profileId).Id;
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => Matches(e, cameraLink, profile, imageWidth, imageHeight, keyCount)) > 0;
            if (removed) Write();
            return removed;
        }
    }

    /// <summary>Every entry saved of one camera, verbatim, for the portable backup (§14.7 "Thêm 3").</summary>
    internal IReadOnlyList<ArCalibrationArchiveRow> ExportRows(string cameraLink)
    {
        lock (_gate) return _entries
            .Where(e => string.Equals(e.CameraLink, cameraLink, StringComparison.OrdinalIgnoreCase))
            .Select(e => new ArCalibrationArchiveRow(e.Profile ?? "", e.ImageWidth, e.ImageHeight, e.KeyCount, e.CameraFovDegrees,
                e.PosePositionX, e.PosePositionY, e.PosePositionZ, e.PosePitchDegrees, e.PoseYawDegrees, e.PoseRollDegrees, e.PoseUniformScale,
                (double[])(e.Corners ?? []).Clone(), (double[])(e.PoseBaseCorners ?? []).Clone()))
            .ToArray();
    }

    /// <summary>
    /// Puts a backup's rows back under the backup's own camera link. Every row goes through <see cref="Set"/> again,
    /// so the same checks run as if the corners had just been marked; rows that fail are counted and skipped.
    /// </summary>
    internal (int Restored, int Skipped) ImportRows(string cameraLink, IEnumerable<ArCalibrationArchiveRow> rows)
    {
        var restored = 0; var skipped = 0;
        foreach (var row in rows)
        {
            var corners = ArCalibrationArchive.Points(row.Corners);
            var baselinePoints = ArCalibrationArchive.Points(row.PoseBaseCorners);
            if (Set(cameraLink, row.Profile, row.ImageWidth, row.ImageHeight, row.KeyCount, corners, row.CameraFovDegrees,
                    row.PitchDegrees, row.YawDegrees, row.RollDegrees, baselinePoints.Length == 4 ? baselinePoints : null,
                    row.PositionX, row.PositionY, row.PositionZ, row.UniformScale)) restored++;
            else skipped++;
        }
        return (restored, skipped);
    }

    /// <summary>Reads the file again, dropping whatever cannot be trusted.</summary>
    internal void Reload()
    {
        lock (_gate)
        {
            _entries = [];
            try
            {
                if (!File.Exists(_path)) return;
                var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), Options);
                if (file?.Entries is null) return;
                foreach (var entry in file.Entries)
                {
                    if (entry is null || entry.Corners is not { Length: 8 } || entry.ImageWidth <= 0 || entry.ImageHeight <= 0 || entry.KeyCount <= 0) continue;
                    if (CalibrationQuad.CheckAnyWinding(Points(entry), entry.ImageWidth, entry.ImageHeight) != CalibrationProblem.None) continue;
                    // A file written before views existed has no profile: those corners are the ones the person has been
                    // playing with, so they belong to the view the app starts on rather than to nothing.
                    if (string.IsNullOrWhiteSpace(entry.Profile)) entry.Profile = ArCalibrationProfiles.LegacyId;
                    if (!ArCalibrationProfiles.IsKnown(entry.Profile)) continue;      // a view this app cannot use: ask for a new calibration instead of guessing
                    entry.Profile = ArCalibrationProfiles.Normalize(entry.Profile).Id;
                    entry.CameraLink ??= "";
                    entry.CameraFovDegrees = ArCameraPose.NormalizeFov(entry.CameraFovDegrees);
                    var transform = Transform(entry);
                    var baseCorners = Points(entry.PoseBaseCorners);
                    if (!transform.IsValid || baseCorners.Length != 4
                        || CalibrationQuad.CheckAnyWinding(baseCorners, entry.ImageWidth, entry.ImageHeight) != CalibrationProblem.None)
                    {
                        transform = ArPoseTransform.Identity;
                        baseCorners = Points(entry);
                    }
                    entry.PosePitchDegrees = transform.PitchDegrees; entry.PoseYawDegrees = transform.YawDegrees; entry.PoseRollDegrees = transform.RollDegrees;
                    entry.PosePositionX = transform.PositionX; entry.PosePositionY = transform.PositionY; entry.PosePositionZ = transform.PositionZ;
                    entry.PoseUniformScale = transform.UniformScale;
                    entry.PoseBaseCorners = baseCorners.SelectMany(p => new[] { p.X, p.Y }).ToArray();
                    _entries.RemoveAll(e => Matches(e, entry.CameraLink, entry.Profile, entry.ImageWidth, entry.ImageHeight, entry.KeyCount));
                    _entries.Add(entry);
                }
                if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
            catch { _entries = []; }
        }
    }

    private bool Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new FileShape { Version = FormatVersion, Entries = _entries }, Options));
            File.Move(temp, _path, true);
            return true;
        }
        catch { return false; }
    }

    private static bool Matches(Entry e, string link, string profile, int width, int height, int keys) =>
        string.Equals(e.CameraLink ?? "", link ?? "", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(e.Profile ?? "", ArCalibrationProfiles.Normalize(profile).Id, StringComparison.OrdinalIgnoreCase) &&
        e.ImageWidth == width && e.ImageHeight == height && e.KeyCount == keys;

    private static ArPoseTransform Transform(Entry e) => new(e.PosePositionX, e.PosePositionY, e.PosePositionZ,
        e.PosePitchDegrees, e.PoseYawDegrees, e.PoseRollDegrees, e.PoseUniformScale);

    private static (double X, double Y)[] Points(Entry e) => Points(e.Corners);

    private static (double X, double Y)[] Points(double[]? corners) => corners is { Length: 8 }
        ? [(corners[0], corners[1]), (corners[2], corners[3]), (corners[4], corners[5]), (corners[6], corners[7])]
        : [];

    private sealed class FileShape
    {
        public int Version { get; set; }
        public List<Entry>? Entries { get; set; }
    }

    private sealed class Entry
    {
        public string? CameraLink { get; set; }
        /// <summary>The view these corners were marked for, one of <see cref="ArCalibrationProfiles.All"/>; empty only in a file written before views existed.</summary>
        public string? Profile { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public int KeyCount { get; set; }
        /// <summary>Estimated horizontal camera field of view in degrees; absent on older entries, which use the 70° default.</summary>
        public double CameraFovDegrees { get; set; } = ArCameraPose.DefaultFovDegrees;
        /// <summary>The absolute object-space pitch, yaw and roll of the calibrated keyboard surface.</summary>
        public double PosePitchDegrees { get; set; }
        public double PoseYawDegrees { get; set; }
        public double PoseRollDegrees { get; set; }
        /// <summary>Local object-space translation; absent before v5 and therefore zero.</summary>
        public double PosePositionX { get; set; }
        public double PosePositionY { get; set; }
        public double PosePositionZ { get; set; }
        /// <summary>Uniform object scale; absent before v5 and therefore one.</summary>
        public double PoseUniformScale { get; set; } = 1;
        /// <summary>x0, y0, x1, y1, x2, y2, x3, y3: final back-left, back-right, front-right, front-left.</summary>
        public double[] Corners { get; set; } = [];
        /// <summary>The same four corners before applying the saved 3D object transform; absent in v1-v3 files.</summary>
        public double[] PoseBaseCorners { get; set; } = [];
    }
}
