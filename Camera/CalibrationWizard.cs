namespace PianoPath;

/// <summary>What the wizard asks of the person next.</summary>
internal enum WizardStep
{
    /// <summary>Click the next corner of the keys; <see cref="CalibrationWizard.NextCorner"/> says which.</summary>
    PlaceCorner,
    /// <summary>All four are placed and good: drag or nudge to improve, or finish.</summary>
    Adjust,
    /// <summary>All four are placed but cannot be used; <see cref="CalibrationWizard.Problem"/> says why.</summary>
    Fix,
}

internal enum CalibrationTool { Corners, Move, Rotate, Scale, NoteDirection }

/// <summary>
/// How the keyboard looks in the picture the person sees. A camera above the keys, behind the player, sees the low notes on the left
/// and the far edge of the keys (where the black keys start) at the top. A camera on the far side of the keyboard, looking at the player,
/// sees it turned round: the low notes on the right and the far edge at the bottom.
/// </summary>
public enum KeyboardOrientation
{
    /// <summary>Low notes on the left, far edge at the top.</summary>
    Upright,
    /// <summary>Low notes on the right, far edge at the top.</summary>
    LowRight,
    /// <summary>Low notes on the left, far edge at the bottom.</summary>
    FarBottom,
    /// <summary>Low notes on the right, far edge at the bottom.</summary>
    LowRightFarBottom,
}

internal static class KeyboardOrientations
{
    internal static bool LowOnRight(this KeyboardOrientation orientation) => orientation is KeyboardOrientation.LowRight or KeyboardOrientation.LowRightFarBottom;
    internal static bool FarAtBottom(this KeyboardOrientation orientation) => orientation is KeyboardOrientation.FarBottom or KeyboardOrientation.LowRightFarBottom;

    /// <summary>
    /// What to start from when nothing is known: the keyboard upright in the picture as the camera delivers it, which on the
    /// screen is the other way round when the picture is shown mirrored.
    /// </summary>
    internal static KeyboardOrientation Default(bool mirrored) => mirrored ? KeyboardOrientation.LowRight : KeyboardOrientation.Upright;

    /// <summary>
    /// The orientation a calibration's corners (back-left, back-right, front-right, front-left, in the picture as delivered) show
    /// on the screen, where the picture is shown turned round left to right when <paramref name="mirrored"/>. How the window
    /// starts when the camera already has a calibration, so it opens on the choice that made it.
    /// </summary>
    internal static KeyboardOrientation Of(IReadOnlyList<(double X, double Y)> corners, int imageWidth, bool mirrored)
    {
        if (corners.Count != 4) return KeyboardOrientation.Upright;
        double X(int i) => mirrored ? imageWidth - corners[i].X : corners[i].X;
        var lowOnRight = (X(0) + X(3)) / 2 > (X(1) + X(2)) / 2;
        var farAtBottom = (corners[0].Y + corners[1].Y) / 2 > (corners[2].Y + corners[3].Y) / 2;
        return (lowOnRight, farAtBottom) switch
        {
            (false, false) => KeyboardOrientation.Upright,
            (true, false) => KeyboardOrientation.LowRight,
            (false, true) => KeyboardOrientation.FarBottom,
            _ => KeyboardOrientation.LowRightFarBottom,
        };
    }
}

/// <summary>A key of the preview grid, in window coordinates.</summary>
/// <param name="Pitch">The key.</param>
/// <param name="Black">True for a black key; these come after every white one so they are drawn on top.</param>
/// <param name="Corners">Back-left, back-right, front-right, front-left.</param>
internal readonly record struct WizardKey(int Pitch, bool Black, (double X, double Y)[] Corners);

/// <summary>
/// Everything the calibration window does that is not drawing: the picture is shown in a box of some size (whole picture with bars,
/// turned round when mirrored), the person clicks and drags corners on it, nudges them with the arrow keys, and sees the keys
/// the four corners would give. The window feeds this its mouse and key events in its own coordinates and draws what it reports.
/// The corners are kept in picture pixels as the camera delivers them, so mirroring only changes what the person sees.
/// </summary>
internal sealed class CalibrationWizard
{
    /// <summary>How near, in window pixels, a press has to be to a corner to pick it up instead of placing a new one.</summary>
    internal const double GrabRadius = 14;

    /// <summary>The side, in picture pixels, of the square the magnifier shows around a corner.</summary>
    internal const double MagnifierSize = 36;

    /// <summary>How far an arrow key moves a corner, in picture pixels, and with Shift held.</summary>
    internal const double NudgeStep = 1, NudgeStepLarge = 10;

    private readonly CalibrationEditor _editor;
    private ArView _view;
    private KeyboardOrientation _orientation = KeyboardOrientation.Upright;
    private int _dragging = -1;
    private (double X, double Y) _grabOffset;

    internal CalibrationWizard(int imageWidth, int imageHeight, KeyboardLayout layout, double cameraFovDegrees = ArCameraPose.DefaultFovDegrees)
    {
        Layout = layout;
        CameraFovDegrees = ArCameraPose.NormalizeFov(cameraFovDegrees);
        _editor = new CalibrationEditor(imageWidth, imageHeight, layout);
        _view = new ArView(imageWidth, imageHeight, 0, 0, PictureFit.Fit, false);
    }

    internal KeyboardLayout Layout { get; }
    internal double CameraFovDegrees { get; private set; }
    internal ArPoseTransform PoseTransform => _editor.PoseTransform;
    internal double PitchDegrees => PoseTransform.PitchDegrees;
    internal double YawDegrees => PoseTransform.YawDegrees;
    internal double RollDegrees => PoseTransform.RollDegrees;
    internal ArCalibration? CurrentCalibration => CanFinish
        ? _editor.Finish(Layout, CameraFovDegrees, PoseTransform, out _)
        : null;
    internal int ImageWidth => _editor.ImageWidth;
    internal int ImageHeight => _editor.ImageHeight;

    /// <summary>The corners placed so far, in picture pixels.</summary>
    internal IReadOnlyList<(double X, double Y)> Points => _editor.Points;

    /// <summary>True when all four corners have been placed.</summary>
    internal bool IsComplete => _editor.IsComplete;

    /// <summary>The ordered corners the overlay could use, or null while they are invalid.</summary>
    internal IReadOnlyList<(double X, double Y)>? UsableCorners => _editor.UsableCorners;

    /// <summary>The corner being moved or last touched, or -1.</summary>
    internal int Selected => _editor.Selected;

    /// <summary>How the picture sits in the window now.</summary>
    internal ArView View => _view;

    /// <summary>True while a corner is held by the mouse.</summary>
    internal bool IsDragging => _dragging >= 0;

    /// <summary>Tells the wizard how big the picture's box is and whether the picture is shown turned round.</summary>
    internal void SetViewport(double width, double height, bool mirror)
    {
        _view = new ArView(ImageWidth, ImageHeight, width, height, PictureFit.Fit, mirror);
        ApplyOrientation();
    }

    /// <summary>How the keyboard looks in the picture on the screen; the corners already placed are judged again for it.</summary>
    internal KeyboardOrientation Orientation => _orientation;

    /// <summary>Says how the keyboard looks on the screen. The corners stay where they are; what changes is how they are put in order and judged.</summary>
    internal void SetOrientation(KeyboardOrientation orientation)
    {
        if (_orientation != orientation) ResetPoseTransform();
        _orientation = orientation;
        ApplyOrientation();
    }

    /// <summary>Loads a saved set of corners into the adjustment step, preserving its keyboard-coordinate order.</summary>
    internal bool LoadCorners(IReadOnlyList<(double X, double Y)> corners)
    {
        ResetPoseTransform();
        return _editor.LoadCorners(corners);
    }

    /// <summary>Restores the saved final pose, FOV, complete transform and its untransformed baseline.</summary>
    internal bool LoadCalibration(ArCalibration calibration)
    {
        if (calibration.ImageWidth != ImageWidth || calibration.ImageHeight != ImageHeight || calibration.Layout.KeyCount != Layout.KeyCount)
            return false;
        var fov = ArCameraPose.NormalizeFov(calibration.CameraFovDegrees);
        if (!_editor.LoadCalibration(calibration.Corners, calibration.PoseBaseCorners, calibration.Transform, fov)) return false;
        CameraFovDegrees = fov;
        return true;
    }

    /// <summary>Sets a complete absolute object transform, so input order never accumulates screen-space drift.</summary>
    internal bool SetObjectTransform(ArPoseTransform transform, double fovDegrees)
    {
        var fov = ArCameraPose.NormalizeFov(fovDegrees);
        if (!transform.IsValid || !_editor.SetPoseTransform(transform, fov)) return false;
        CameraFovDegrees = fov;
        return true;
    }

    /// <summary>Sets absolute local-object pitch, yaw and roll, preserving its independent position and scale.</summary>
    internal bool Set3DPose(double pitchDegrees, double yawDegrees, double rollDegrees, double fovDegrees) =>
        SetObjectTransform(PoseTransform with { PitchDegrees = pitchDegrees, YawDegrees = yawDegrees, RollDegrees = rollDegrees }, fovDegrees);

    /// <summary>Rotates the complete keyboard object about its surface normal (the Z/roll axis).</summary>
    internal bool RotateGrid(double degrees) => Set3DPose(PitchDegrees, YawDegrees, degrees, CameraFovDegrees);

    /// <summary>The orientation is as seen on the screen and the editor works on the picture as delivered, which the screen shows turned round left to right when mirrored.</summary>
    private void ApplyOrientation() => _editor.SetFlips(_orientation.LowOnRight() != _view.Mirror, _orientation.FarAtBottom());

    private void ResetPoseTransform() => _editor.ResetPoseTransform(CameraFovDegrees);

    // ---- what to say ------------------------------------------------------------------------------------

    internal WizardStep Step => !_editor.IsComplete ? WizardStep.PlaceCorner : _editor.CanFinish ? WizardStep.Adjust : WizardStep.Fix;

    /// <summary>The name of the corner to click next (one of <see cref="CalibrationEditor.CornerNames"/>), or null when all four are placed.</summary>
    internal string? NextCorner => _editor.IsComplete ? null : CalibrationEditor.CornerNames[_editor.Points.Count];

    /// <summary>What is wrong with the four corners; <see cref="CalibrationProblem.None"/> while they are fine or not all placed.</summary>
    internal CalibrationProblem Problem => _editor.IsComplete && !_editor.CanFinish ? _editor.Problem : CalibrationProblem.None;

    /// <summary>True when Done may be pressed.</summary>
    internal bool CanFinish => _editor.CanFinish;

    // ---- mouse ------------------------------------------------------------------------------------------

    /// <summary>
    /// A press at a window position: it picks up the corner within <see cref="GrabRadius"/> of it, or places the next corner
    /// there. True when it did one of those (so the window should capture the mouse and follow the drag).
    /// </summary>
    internal bool PointerDown(double x, double y)
    {
        if (!_view.Valid || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        _dragging = -1;
        var (ix, iy) = _view.ToImage(x, y);
        var hit = _editor.HitTest(ix, iy, GrabRadius / _view.Scale);
        if (hit >= 0)
        {
            _dragging = hit;
            _grabOffset = (_editor.Points[hit].X - ix, _editor.Points[hit].Y - iy);    // the corner does not jump to the pointer
            _editor.Move(hit, _editor.Points[hit].X, _editor.Points[hit].Y);            // select it
            return true;
        }
        if (_editor.Place(ix, iy)) { _dragging = _editor.Points.Count - 1; _grabOffset = (0, 0); return true; }
        return false;
    }

    /// <summary>The pointer moved: the held corner follows it. True when a corner moved.</summary>
    internal bool PointerMove(double x, double y)
    {
        if (_dragging < 0 || !_view.Valid || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        var (ix, iy) = _view.ToImage(x, y);
        var before = _editor.Points[_dragging];
        var moved = _editor.Move(_dragging, ix + _grabOffset.X, iy + _grabOffset.Y);
        if (moved && _editor.Points[_dragging] != before) ResetPoseTransform();
        return moved;
    }

    /// <summary>The button was released.</summary>
    internal void PointerUp() => _dragging = -1;

    // ---- keys -------------------------------------------------------------------------------------------

    /// <summary>
    /// An arrow key: moves the selected corner one picture pixel (ten with <paramref name="large"/>) the way the arrow points
    /// on the screen, which for a mirrored picture is the other way in the picture. True when a corner moved.
    /// </summary>
    internal bool Nudge(int right, int down, bool large)
    {
        if (_editor.Selected < 0 || _dragging >= 0) return false;
        var step = large ? NudgeStepLarge : NudgeStep;
        var before = _editor.Points[_editor.Selected];
        var moved = _editor.Nudge(_editor.Selected, (_view.Mirror ? -right : right) * step, down * step);
        if (moved && _editor.Points[_editor.Selected] != before) ResetPoseTransform();
        return moved;
    }

    /// <summary>Selects the next (or previous) placed corner, as Tab does. True when there is a corner to select.</summary>
    internal bool SelectNext(bool backwards)
    {
        var count = _editor.Points.Count;
        if (count == 0 || _dragging >= 0) return false;
        var index = _editor.Selected < 0 ? (backwards ? count - 1 : 0) : ((_editor.Selected + (backwards ? count - 1 : 1)) % count);
        return _editor.Move(index, _editor.Points[index].X, _editor.Points[index].Y);
    }

    /// <summary>Takes the last corner back (Backspace). False when there is none or one is being dragged.</summary>
    internal bool Undo()
    {
        if (_dragging >= 0 || !_editor.Undo()) return false;
        ResetPoseTransform();
        return true;
    }

    /// <summary>Starts again.</summary>
    internal void Clear() { _dragging = -1; _editor.Clear(); ResetPoseTransform(); }

    // ---- what to draw -----------------------------------------------------------------------------------

    /// <summary>Where a placed corner is in the window.</summary>
    internal (double X, double Y) PointOnScreen(int index) => _view.ToOutput(_editor.Points[index].X, _editor.Points[index].Y);

    /// <summary>
    /// The square of the picture the magnifier shows for a corner, in picture pixels (it may reach beyond the picture, which
    /// the window draws as empty): <see cref="MagnifierSize"/> across, centred on the corner.
    /// </summary>
    internal (double X, double Y, double Size) MagnifierSource(int index) =>
        (_editor.Points[index].X - MagnifierSize / 2, _editor.Points[index].Y - MagnifierSize / 2, MagnifierSize);

    /// <summary>
    /// The keys the four corners would give, in window coordinates, white keys first; empty until the corners can be used. The
    /// window draws them over the picture while a corner is dragged, which is how a corner that is a little off shows itself.
    /// </summary>
    internal IReadOnlyList<WizardKey> PreviewKeys()
    {
        if (!_view.Valid || _editor.UsableCorners is not { } corners) return [];
        var calibration = ArCalibration.Create(Layout, corners, ImageWidth, ImageHeight, out _);
        if (calibration is null) return [];
        var keys = new List<WizardKey>(Layout.KeyCount);
        for (var pass = 0; pass < 2; pass++)
            for (var pitch = Layout.FirstPitch; pitch <= Layout.LastPitch; pitch++)
            {
                var black = KeyboardLayout.IsBlack(pitch);
                if (black != (pass == 1)) continue;
                keys.Add(new WizardKey(pitch, black, [.. calibration.KeyPolygon(pitch).Select(p => _view.ToOutput(p.X, p.Y))]));
            }
        return keys;
    }

    /// <summary>
    /// Finishes: the calibration for the keyboard, with the corners put in order when they only needed that; null — with the
    /// reason — when they are not good enough.
    /// </summary>
    internal ArCalibration? Finish(out CalibrationProblem problem) =>
        _editor.Finish(Layout, CameraFovDegrees, PoseTransform, out problem);
}
