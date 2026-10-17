using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>
/// Draws the camera picture and the four corners the person is marking on it, and passes the mouse and keys to the
/// <see cref="CalibrationWizard"/>, which decides everything. Nothing here is a rule: it paints what the wizard reports (the picture
/// in its box, turned round when the stage turns it round, the outline, the grid of keys the corners would give, a magnifier on the
/// corner being moved) and forwards input in its own coordinates.
/// </summary>
internal sealed class CalibrationSurface : FrameworkElement
{
    private const double MagnifierBox = 168;
    private static readonly Brush Backdrop = Freeze(new SolidColorBrush(Color.FromRgb(8, 7, 12)));
    private static readonly Brush WhiteKeyFill = Freeze(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)));
    private static readonly Brush BlackKeyFill = Freeze(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
    private static readonly Pen WhiteKeyPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), 0.8));
    private static readonly Pen BlackKeyPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(220, 67, 230, 255)), 0.8));
    private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(67, 230, 255)));
    private static readonly Brush ProblemBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 83, 113)));
    private static readonly Pen Halo = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 4.2));

    private readonly bool _mirror;
    private BitmapSource _bitmap;
    private CalibrationTool _tool = CalibrationTool.Corners;
    private ArNoteDirection _noteDirection = ArNoteDirection.Default;
    private DragKind _dragKind;
    private char _dragAxis;
    private Point _dragOrigin, _dragStart;
    private Vector _dragScreenAxis;
    private double _dragPixelsPerUnit, _dragPreviousRingAngle;
    private ArPoseTransform _dragStartTransform;

    private readonly record struct RotationRing(ArVec3 Centre, ArVec3 First, ArVec3 Second, double Radius);
    private enum DragKind { None, Move, Rotate, Scale, NoteDirection }

    internal CalibrationSurface(CalibrationPicture picture, CalibrationWizard wizard, bool mirror, ArNoteDirection noteDirection = default)
    {
        Wizard = wizard; _mirror = mirror; _bitmap = ToBitmap(picture); Picture = picture;
        _noteDirection = noteDirection.Normalised();
        Focusable = true; Cursor = Cursors.Cross; ClipToBounds = true;
    }

    internal CalibrationWizard Wizard { get; }
    internal CalibrationTool Tool => _tool;
    internal ArNoteDirection NoteDirection => _noteDirection;

    internal event Action<ArNoteDirection>? NoteDirectionChanged;

    internal void SetTool(CalibrationTool tool) { _tool = tool; Cursor = tool == CalibrationTool.Corners ? Cursors.Cross : Cursors.SizeAll; Refresh(); }

    internal void SetNoteDirection(ArNoteDirection direction, bool notify = false)
    {
        _noteDirection = direction.Normalised();
        if (notify) NoteDirectionChanged?.Invoke(_noteDirection);
        Refresh();
    }

    internal bool SetNoteDirectionToScreenUp()
    {
        if (!TryGetPoseContext(out _, out _, out var pose, out var centre)
            || !pose.TryDirectionForScreenDelta(centre, (0, 1), Wizard.View, out var direction)) return false;
        SetNoteDirection(ArNoteDirection.FromVector(direction), notify: true);
        return true;
    }

    /// <summary>Test seam for the same projected rotation ring used for drawing and hit testing.</summary>
    internal bool TryRotationRingPoint(char axis, double degrees, out Point point)
    {
        point = default;
        if (!TryGetPoseContext(out _, out var basePose, out _, out _)
            || !TryBuildRotationRing(basePose, Wizard.PoseTransform, axis, out var ring)) return false;
        return TryRotationRingPoint(basePose, ring, degrees * Math.PI / 180, Wizard.View, out point);
    }

    /// <summary>The picture on show, as the camera delivered it.</summary>
    internal CalibrationPicture Picture { get; private set; }

    /// <summary>Raised after anything that changed what the wizard says, so the window can refresh its text and buttons.</summary>
    internal event Action? Changed;

    /// <summary>Shows another picture of the same size, keeping the corners placed so far.</summary>
    internal void SetPicture(CalibrationPicture picture) { _bitmap = ToBitmap(picture); Picture = picture; InvalidateVisual(); }

    private static BitmapSource ToBitmap(CalibrationPicture picture)
    {
        var bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgr32, null, picture.Bgra, picture.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Freeze(Brush brush) { brush.Freeze(); return brush; }
    private static Pen FreezePen(Pen pen) { pen.Freeze(); return pen; }

    // ---- input ------------------------------------------------------------------------------------------

    internal bool HandlePress(Point position)
    {
        if (Wizard.CanFinish && _tool != CalibrationTool.Corners && TryBeginToolDrag(position))
        {
            Refresh();
            return true;
        }
        var taken = Wizard.PointerDown(position.X, position.Y);
        Refresh();
        return taken;
    }

    internal void HandleMove(Point position)
    {
        var moved = _dragKind != DragKind.None ? UpdateToolDrag(position) : Wizard.PointerMove(position.X, position.Y);
        if (moved) Refresh();
    }

    internal void HandleRelease()
    {
        _dragKind = DragKind.None;
        Wizard.PointerUp();
        Refresh();
    }

    /// <summary>The keys of the window: arrows nudge, Tab changes corner, Backspace takes one back, Delete starts over. True when the key was used.</summary>
    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return false;
        var large = (modifiers & ModifierKeys.Shift) != 0;
        switch (key)
        {
            case Key.Left: Wizard.Nudge(-1, 0, large); break;
            case Key.Right: Wizard.Nudge(1, 0, large); break;
            case Key.Up: Wizard.Nudge(0, -1, large); break;
            case Key.Down: Wizard.Nudge(0, 1, large); break;
            case Key.Tab when Wizard.Points.Count > 0: Wizard.SelectNext(backwards: large); break;
            case Key.Back: Wizard.Undo(); break;
            case Key.Delete: Wizard.Clear(); break;
            default: return false;
        }
        Refresh();
        return true;
    }

    internal void Refresh() { Changed?.Invoke(); InvalidateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (HandlePress(e.GetPosition(this))) CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) HandleMove(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        HandleRelease();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => HandleRelease();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Wizard.SetViewport(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height, _mirror);
        Changed?.Invoke();
    }

    // ---- drawing ----------------------------------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var view = Wizard.View;
        if (!view.Valid) return;
        DrawPicture(dc, new Rect(view.Left, view.Top, view.Width, view.Height), _mirror, nearest: false);
        DrawKeys(dc);
        DrawOutline(dc);
        DrawNoteDirection(dc);
        DrawTransformGizmo(dc);
        DrawMagnifier(dc);
    }

    /// <summary>The picture in a rectangle, turned round about the rectangle's middle when the stage turns it round.</summary>
    private void DrawPicture(DrawingContext dc, Rect rect, bool mirror, bool nearest, double? flipAbout = null)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, nearest ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        using (var inner = group.Open()) inner.DrawImage(_bitmap, rect);
        if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, flipAbout ?? rect.X + rect.Width / 2, 0));
        dc.DrawDrawing(group);
        if (mirror) dc.Pop();
    }

    private void DrawKeys(DrawingContext dc)
    {
        var keys = Wizard.PreviewKeys();
        if (keys.Count == 0) return;
        var whites = new StreamGeometry(); var blacks = new StreamGeometry();
        using (var white = whites.Open())
        using (var black = blacks.Open())
            foreach (var key in keys)
            {
                var context = key.Black ? black : white;
                context.BeginFigure(new Point(key.Corners[0].X, key.Corners[0].Y), true, true);
                context.PolyLineTo([new Point(key.Corners[1].X, key.Corners[1].Y), new Point(key.Corners[2].X, key.Corners[2].Y), new Point(key.Corners[3].X, key.Corners[3].Y)], true, false);
            }
        whites.Freeze(); blacks.Freeze();
        dc.DrawGeometry(WhiteKeyFill, WhiteKeyPen, whites);
        dc.DrawGeometry(BlackKeyFill, BlackKeyPen, blacks);
        // The lowest and the highest key are named at the near edge, so a grid that runs the wrong way round shows itself.
        var ends = keys.Where(key => !key.Black).ToList();
        if (ends.Count > 0) { DrawKeyName(dc, ends[0]); DrawKeyName(dc, ends[^1]); }
    }

    private void DrawKeyName(DrawingContext dc, WizardKey key)
    {
        var text = Text(CalibrationHints.KeyName(key.Pitch), 12, Accent);
        var x = Math.Clamp((key.Corners[2].X + key.Corners[3].X) / 2 - text.Width / 2, 2, Math.Max(2, ActualWidth - text.Width - 2));
        // outside the near edge: below it when the keys' near edge is the lower one, above it when the picture shows them the other way up
        var nearIsLower = key.Corners[2].Y + key.Corners[3].Y >= key.Corners[0].Y + key.Corners[1].Y;
        var y = Math.Clamp(nearIsLower ? Math.Max(key.Corners[2].Y, key.Corners[3].Y) + 4 : Math.Min(key.Corners[2].Y, key.Corners[3].Y) - text.Height - 4, 2, Math.Max(2, ActualHeight - text.Height - 2));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), null, new Rect(x - 3, y - 1, text.Width + 6, text.Height + 2));
        dc.DrawText(text, new Point(x, y));
    }

    private void DrawOutline(DrawingContext dc)
    {
        var count = Wizard.Points.Count;
        if (count == 0) return;
        var brush = Wizard.Step == WizardStep.Fix ? ProblemBrush : Accent;
        var pen = new Pen(brush, 1.6);
        var points = Enumerable.Range(0, count).Select(i => Wizard.PointOnScreen(i)).Select(p => new Point(p.X, p.Y)).ToArray();
        for (var i = 1; i < count; i++) { dc.DrawLine(Halo, points[i - 1], points[i]); dc.DrawLine(pen, points[i - 1], points[i]); }
        if (count == 4) { dc.DrawLine(Halo, points[3], points[0]); dc.DrawLine(pen, points[3], points[0]); }
        for (var i = 0; i < count; i++)
        {
            var selected = i == Wizard.Selected;
            dc.DrawEllipse(selected ? brush : null, Halo, points[i], selected ? 6 : 5, selected ? 6 : 5);
            dc.DrawEllipse(selected ? brush : null, pen, points[i], selected ? 6 : 5, selected ? 6 : 5);
            var label = Text((i + 1).ToString(CultureInfo.InvariantCulture), 12, Brushes.White);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), null, new Rect(points[i].X + 8, points[i].Y - 21, label.Width + 6, label.Height + 2));
            dc.DrawText(label, new Point(points[i].X + 11, points[i].Y - 20));
        }
    }

    private static readonly Brush AxisX = Freeze(new SolidColorBrush(Color.FromRgb(255, 92, 106)));
    private static readonly Brush AxisY = Freeze(new SolidColorBrush(Color.FromRgb(92, 235, 145)));
    private static readonly Brush AxisZ = Freeze(new SolidColorBrush(Color.FromRgb(91, 164, 255)));
    private static readonly Brush NoteArrowBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 218, 77)));

    private bool TryGetPoseContext(out ArCalibration calibration, out ArCameraPose basePose,
        out ArCameraPose currentPose, out ArVec3 centre)
    {
        calibration = null!; basePose = default; currentPose = default; centre = default;
        if (Wizard.CurrentCalibration is not { } current || current.CameraPose is not { } pose) return false;
        var baseCalibration = ArCalibration.Create(Wizard.Layout, current.PoseBaseCorners, Wizard.ImageWidth, Wizard.ImageHeight,
            out _, current.CameraFovDegrees);
        if (baseCalibration?.CameraPose is not { } originPose) return false;
        calibration = current; currentPose = pose; basePose = originPose;
        centre = new ArVec3(pose.KeyboardWidthDepthRatio * .5, .5, 0);
        return true;
    }

    private static bool TryPoint(ArCameraPose pose, ArVec3 keyboardPoint, ArView view, out Point point)
    {
        point = default;
        if (!pose.TryProject(keyboardPoint, view, out var projected)) return false;
        point = new Point(projected.X, projected.Y);
        return true;
    }

    private static bool TryAxis(ArCameraPose currentPose, ArCameraPose axisPose, ArVec3 centre,
        ArVec3 localAxis, ArView view, out Vector screenDerivative)
    {
        var cameraPoint = currentPose.ToCamera(centre);
        var cameraAxis = axisPose.DirectionToCamera(localAxis);
        if (!currentPose.TryProjectDelta(cameraPoint, cameraAxis, view, out var projected)) { screenDerivative = default; return false; }
        screenDerivative = new Vector(projected.X, projected.Y);
        return screenDerivative.Length > 1e-6 && double.IsFinite(screenDerivative.X) && double.IsFinite(screenDerivative.Y);
    }

    private void DrawNoteDirection(DrawingContext dc)
    {
        if (!TryGetPoseContext(out _, out _, out var pose, out var centre)) return;
        if (!TryPoint(pose, centre, Wizard.View, out var origin)) return;
        var cameraPoint = pose.ToCamera(centre);
        var cameraDirection = pose.DirectionToCamera(_noteDirection.Vector);
        if (!pose.TryProjectDelta(cameraPoint, cameraDirection, Wizard.View, out var delta)) return;
        var vector = new Vector(delta.X, delta.Y);
        if (vector.Length < 1e-6) return;
        vector.Normalize();
        var length = _tool == CalibrationTool.NoteDirection ? 76 : 56;
        DrawArrow(dc, origin, origin + vector * length, NoteArrowBrush, _tool == CalibrationTool.NoteDirection ? 3.5 : 2.6);
        dc.DrawEllipse(NoteArrowBrush, Halo, origin, 5, 5);
        if (_tool == CalibrationTool.NoteDirection)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(36, 255, 218, 77)), new Pen(NoteArrowBrush, 1.2), origin, 86, 86);
        }
    }

    private void DrawTransformGizmo(DrawingContext dc)
    {
        if (_tool is CalibrationTool.Corners or CalibrationTool.NoteDirection) return;
        if (!TryGetPoseContext(out _, out var basePose, out var currentPose, out var centre)
            || !TryPoint(currentPose, centre, Wizard.View, out var origin)) return;
        switch (_tool)
        {
            case CalibrationTool.Move:
                DrawMoveAxis(dc, origin, currentPose, basePose, centre, new ArVec3(1, 0, 0), AxisX, "X");
                DrawMoveAxis(dc, origin, currentPose, basePose, centre, new ArVec3(0, 1, 0), AxisY, "Y");
                DrawMoveAxis(dc, origin, currentPose, basePose, centre, new ArVec3(0, 0, 1), AxisZ, "Z");
                break;
            case CalibrationTool.Rotate:
                var transform = Wizard.PoseTransform;
                DrawRotationRing(dc, basePose, transform, 'X', AxisX);
                DrawRotationRing(dc, basePose, transform, 'Y', AxisY);
                DrawRotationRing(dc, basePose, transform, 'Z', AxisZ);
                break;
            case CalibrationTool.Scale:
                if (TryAxis(currentPose, currentPose, centre, new ArVec3(1, 1, 0).Normalized, Wizard.View, out var scaleAxis))
                {
                    scaleAxis.Normalize();
                    var end = origin + scaleAxis * 78;
                    DrawArrow(dc, origin, end, AxisZ, 3.2);
                    dc.DrawEllipse(AxisZ, Halo, end, 8, 8);
                    DrawText(dc, "1×", end + new Vector(8, -16), 12, Brushes.White);
                }
                break;
        }
    }

    private void DrawMoveAxis(DrawingContext dc, Point origin, ArCameraPose currentPose, ArCameraPose basePose,
        ArVec3 centre, ArVec3 axis, Brush brush, string label)
    {
        if (!TryAxis(currentPose, basePose, centre, axis, Wizard.View, out var derivative)) return;
        derivative.Normalize();
        var end = origin + derivative * 78;
        DrawArrow(dc, origin, end, brush, 3.2);
        dc.DrawEllipse(brush, Halo, end, 7, 7);
        DrawText(dc, label, end + new Vector(8, -16), 12, Brushes.White);
    }

    private void DrawRotationRing(DrawingContext dc, ArCameraPose basePose, ArPoseTransform transform, char axis, Brush brush)
    {
        if (!TryBuildRotationRing(basePose, transform, axis, out var ring)) return;
        const int segments = 128;
        var pen = new Pen(brush, 2.2);
        Point? previous = null, firstPoint = null, lastPoint = null;
        for (var i = 0; i <= segments; i++)
        {
            var angle = Math.Tau * i / segments;
            if (!TryRotationRingPoint(basePose, ring, angle, Wizard.View, out var point))
            {
                previous = null;
                continue;
            }
            firstPoint ??= point;
            if (previous is { } from) dc.DrawLine(pen, from, point);
            previous = point; lastPoint = point;
        }
        if (firstPoint is { } start && lastPoint is { } end && (start - end).Length < 8) dc.DrawLine(pen, end, start);
    }

    private static bool TryBuildRotationRing(ArCameraPose basePose, ArPoseTransform transform, char axis, out RotationRing ring)
    {
        ring = default;
        var normal = transform.RotationAxis(axis);
        if (!normal.IsFinite || normal.Length < .9) return false;
        var reference = Math.Abs(normal.Z) < .85 ? new ArVec3(0, 0, 1) : new ArVec3(0, 1, 0);
        var first = ArVec3.Cross(reference, normal).Normalized;
        var second = ArVec3.Cross(normal, first).Normalized;
        if (first.Length < .9 || second.Length < .9) return false;
        var centre = new ArVec3(basePose.KeyboardWidthDepthRatio * .5, .5, 0) + transform.Position;
        ring = new RotationRing(centre, first, second, .38 * transform.UniformScale);
        return true;
    }

    private static bool TryRotationRingPoint(ArCameraPose pose, RotationRing ring, double angle, ArView view, out Point point)
    {
        var local = ring.Centre + (ring.First * Math.Cos(angle) + ring.Second * Math.Sin(angle)) * ring.Radius;
        return TryPoint(pose, local, view, out point);
    }

    /// <summary>Finds the closest point on the projected (perspective ellipse) ring and its 3D angular parameter.</summary>
    private static bool TryClosestRotationRingAngle(Point target, ArCameraPose pose, RotationRing ring, ArView view,
        out double angle, out double distance)
    {
        const int segments = 192;
        angle = 0; distance = double.PositiveInfinity;
        var found = false;
        for (var i = 0; i < segments; i++)
        {
            var a0 = Math.Tau * i / segments; var a1 = Math.Tau * (i + 1) / segments;
            if (!TryRotationRingPoint(pose, ring, a0, view, out var p0)
                || !TryRotationRingPoint(pose, ring, a1, view, out var p1)) continue;
            var segment = p1 - p0;
            var lengthSquared = segment.LengthSquared;
            if (lengthSquared < 1e-8) continue;
            var fraction = Math.Clamp(Vector.Multiply(target - p0, segment) / lengthSquared, 0, 1);
            var closest = p0 + segment * fraction;
            var candidateDistance = (target - closest).Length;
            if (candidateDistance >= distance) continue;
            distance = candidateDistance;
            angle = a0 + (a1 - a0) * fraction;
            found = true;
        }
        angle = WrapRadians(angle);
        return found;
    }

    private static double WrapRadians(double angle)
    {
        while (angle > Math.PI) angle -= Math.Tau;
        while (angle < -Math.PI) angle += Math.Tau;
        return angle;
    }

    private static void DrawArrow(DrawingContext dc, Point from, Point to, Brush brush, double thickness)
    {
        var vector = to - from;
        if (vector.Length < 2) return;
        vector.Normalize();
        var side = new Vector(-vector.Y, vector.X);
        var pen = new Pen(brush, thickness);
        dc.DrawLine(Halo, from, to);
        dc.DrawLine(pen, from, to);
        dc.DrawLine(pen, to, to - vector * 11 + side * 5);
        dc.DrawLine(pen, to, to - vector * 11 - side * 5);
    }

    private void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush) =>
        dc.DrawText(Text(text, size, brush), point);

    private bool TryBeginToolDrag(Point position)
    {
        if (!TryGetPoseContext(out var calibration, out var basePose, out var currentPose, out var centre)
            || !TryPoint(currentPose, centre, Wizard.View, out var origin)) return false;
        if (NearCorner(position)) return false;
        if (_tool == CalibrationTool.NoteDirection)
        {
            var (imageX, imageY) = Wizard.View.ToImage(position.X, position.Y);
            if (!calibration.Contains(imageX, imageY) && Distance(position, GetNoteTip(currentPose, centre)) > 18) return false;
            _dragKind = DragKind.NoteDirection; _dragOrigin = origin; _dragStart = position;
            if ((position - origin).Length < 5) return true;
            if (UpdateNoteDirection(position)) return true;
            _dragKind = DragKind.None;
            return false;
        }
        if (_tool == CalibrationTool.Move)
        {
            var candidates = new[]
            {
                ('X', new ArVec3(1, 0, 0)), ('Y', new ArVec3(0, 1, 0)), ('Z', new ArVec3(0, 0, 1)),
            };
            var bestDistance = 11.0;
            foreach (var (axis, localAxis) in candidates)
            {
                if (!TryAxis(currentPose, basePose, centre, localAxis, Wizard.View, out var derivative)) continue;
                var pixels = derivative.Length; derivative.Normalize();
                var delta = position - origin;
                var along = Vector.Multiply(delta, derivative);
                var across = (delta - derivative * along).Length;
                if (along is < 16 or > 86 || across >= bestDistance) continue;
                bestDistance = across; _dragAxis = axis; _dragScreenAxis = derivative; _dragPixelsPerUnit = pixels;
            }
            if (bestDistance >= 11) return false;
            BeginDrag(DragKind.Move, position, origin);
            return true;
        }
        if (_tool == CalibrationTool.Scale)
        {
            if (!TryAxis(currentPose, currentPose, centre, new ArVec3(1, 1, 0).Normalized, Wizard.View, out var derivative)) return false;
            derivative.Normalize();
            var end = origin + derivative * 78;
            if (Distance(position, end) > 15) return false;
            _dragAxis = 'S'; _dragScreenAxis = derivative; _dragPixelsPerUnit = 1;
            BeginDrag(DragKind.Scale, position, origin);
            return true;
        }
        if (_tool == CalibrationTool.Rotate)
        {
            var bestDistance = 14.0;
            var bestAngle = 0.0;
            foreach (var axis in new[] { 'X', 'Y', 'Z' })
            {
                if (!TryBuildRotationRing(basePose, Wizard.PoseTransform, axis, out var ring)
                    || !TryClosestRotationRingAngle(position, basePose, ring, Wizard.View, out var angle, out var distance)
                    || distance >= bestDistance) continue;
                bestDistance = distance; bestAngle = angle; _dragAxis = axis;
            }
            if (bestDistance >= 14) return false;
            _dragPreviousRingAngle = bestAngle;
            BeginDrag(DragKind.Rotate, position, origin);
            return true;
        }
        return false;
    }

    private void BeginDrag(DragKind kind, Point start, Point origin)
    {
        _dragKind = kind; _dragStart = start; _dragOrigin = origin; _dragStartTransform = Wizard.PoseTransform;
    }

    private bool UpdateToolDrag(Point position) => _dragKind switch
    {
        DragKind.Move => UpdateMove(position),
        DragKind.Rotate => UpdateRotation(position),
        DragKind.Scale => UpdateScale(position),
        DragKind.NoteDirection => UpdateNoteDirection(position),
        _ => false,
    };

    private bool UpdateMove(Point position)
    {
        if (_dragPixelsPerUnit <= 1e-6) return false;
        var amount = Vector.Multiply(position - _dragStart, _dragScreenAxis) / _dragPixelsPerUnit;
        var transform = _dragAxis switch
        {
            'X' => _dragStartTransform with { PositionX = Math.Clamp(_dragStartTransform.PositionX + amount, -ArPoseTransform.PositionLimit, ArPoseTransform.PositionLimit) },
            'Y' => _dragStartTransform with { PositionY = Math.Clamp(_dragStartTransform.PositionY + amount, -ArPoseTransform.PositionLimit, ArPoseTransform.PositionLimit) },
            _ => _dragStartTransform with { PositionZ = Math.Clamp(_dragStartTransform.PositionZ + amount, -ArPoseTransform.PositionLimit, ArPoseTransform.PositionLimit) },
        };
        return Wizard.SetObjectTransform(transform, Wizard.CameraFovDegrees);
    }

    private bool UpdateRotation(Point position)
    {
        if (!TryGetPoseContext(out _, out var basePose, out _, out _)
            || !TryBuildRotationRing(basePose, Wizard.PoseTransform, _dragAxis, out var ring)
            || !TryClosestRotationRingAngle(position, basePose, ring, Wizard.View, out var angle, out var distance)
            || distance > 42) return false;
        var deltaDegrees = WrapRadians(angle - _dragPreviousRingAngle) * 180 / Math.PI;
        if (Math.Abs(deltaDegrees) < .001) return false;
        var transform = Wizard.PoseTransform;
        double WrapDegrees(double value)
        {
            while (value > 180) value -= 360;
            while (value < -180) value += 360;
            return value;
        }
        transform = _dragAxis switch
        {
            'X' => transform with { PitchDegrees = WrapDegrees(transform.PitchDegrees + deltaDegrees) },
            'Y' => transform with { YawDegrees = WrapDegrees(transform.YawDegrees + deltaDegrees) },
            _ => transform with { RollDegrees = WrapDegrees(transform.RollDegrees + deltaDegrees) },
        };
        if (!Wizard.SetObjectTransform(transform, Wizard.CameraFovDegrees)) return false;
        _dragPreviousRingAngle = angle;
        return true;
    }

    private bool UpdateScale(Point position)
    {
        var pixels = Vector.Multiply(position - _dragStart, _dragScreenAxis);
        var scale = Math.Clamp(_dragStartTransform.UniformScale * Math.Exp(pixels / 100), ArPoseTransform.MinScale, ArPoseTransform.MaxScale);
        return Wizard.SetObjectTransform(_dragStartTransform with { UniformScale = scale }, Wizard.CameraFovDegrees);
    }

    private bool UpdateNoteDirection(Point position)
    {
        if (!TryGetPoseContext(out _, out _, out var pose, out var centre)) return false;
        var delta = position - _dragOrigin;
        if (delta.Length < 5 || !pose.TryDirectionForScreenDelta(centre, (delta.X, delta.Y), Wizard.View, out var direction)) return false;
        _noteDirection = ArNoteDirection.FromVector(direction);
        NoteDirectionChanged?.Invoke(_noteDirection);
        return true;
    }

    private Point GetNoteTip(ArCameraPose pose, ArVec3 centre)
    {
        if (!TryPoint(pose, centre, Wizard.View, out var origin)) return new Point(double.PositiveInfinity, double.PositiveInfinity);
        if (!pose.TryProjectDelta(pose.ToCamera(centre), pose.DirectionToCamera(_noteDirection.Vector), Wizard.View, out var delta)) return origin;
        var vector = new Vector(delta.X, delta.Y);
        if (vector.Length < 1e-6) return origin;
        vector.Normalize();
        return origin + vector * 56;
    }

    private bool NearCorner(Point position)
    {
        for (var i = 0; i < Wizard.Points.Count; i++)
        {
            var point = Wizard.PointOnScreen(i);
            if (Distance(position, new Point(point.X, point.Y)) <= CalibrationWizard.GrabRadius + 3) return true;
        }
        return false;
    }

    private static double Distance(Point a, Point b) => (a - b).Length;

    /// <summary>A zoomed view of the picture around the corner being moved, in the top corner that is farthest from it.</summary>
    private void DrawMagnifier(DrawingContext dc)
    {
        if (Wizard.Selected < 0 || Wizard.Selected >= Wizard.Points.Count) return;
        var source = Wizard.MagnifierSource(Wizard.Selected);
        var corner = Wizard.PointOnScreen(Wizard.Selected);
        var leftBox = new Rect(12, 12, MagnifierBox, MagnifierBox);
        var box = leftBox;
        if (corner.X < leftBox.Right + 48 && corner.Y < leftBox.Bottom + 48) box = new Rect(Math.Max(12, ActualWidth - MagnifierBox - 12), 12, MagnifierBox, MagnifierBox);
        var zoom = MagnifierBox / source.Size;
        dc.DrawRectangle(Backdrop, null, box);
        dc.PushClip(new RectangleGeometry(box));
        DrawPicture(dc, new Rect(box.X - source.X * zoom, box.Y - source.Y * zoom, Wizard.ImageWidth * zoom, Wizard.ImageHeight * zoom), _mirror, nearest: true, flipAbout: box.X + box.Width / 2);
        var middle = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        foreach (var pen in new[] { Halo, new Pen(Brushes.White, 1) })
        {
            dc.DrawLine(pen, new Point(middle.X - 22, middle.Y), new Point(middle.X - 6, middle.Y));
            dc.DrawLine(pen, new Point(middle.X + 6, middle.Y), new Point(middle.X + 22, middle.Y));
            dc.DrawLine(pen, new Point(middle.X, middle.Y - 22), new Point(middle.X, middle.Y - 6));
            dc.DrawLine(pen, new Point(middle.X, middle.Y + 6), new Point(middle.X, middle.Y + 22));
        }
        dc.Pop();
        dc.DrawRectangle(null, new Pen(Accent, 1.5), box);
    }

    private FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

}

/// <summary>
/// The window in which the person marks the four corners of the keys on a still picture from the camera: the picture, a line that
/// says what to do next, and the buttons. Returns the calibration when Done is pressed; the caller saves it.
/// </summary>
internal sealed class CalibrationWindow : Window
{
    private readonly CalibrationSurface _surface;
    private readonly TextBlock _hint, _note, _tips;
    private readonly Button _done, _undo, _startOver, _screenUp, _keyDepth;
    private readonly Button _resetOrientation, _resetFov, _resetPitch, _resetYaw, _resetRoll;
    private readonly Button _resetPositionX, _resetPositionY, _resetPositionZ, _resetScale, _resetTool;
    private readonly Button _resetNoteTilt, _resetNoteAzimuth;
    private readonly List<Button> _poseResetButtons = [];
    private readonly ComboBox _orientation, _toolBox;
    private readonly Slider _pitchSlider, _yawSlider, _rollSlider, _fovSlider;
    private readonly Slider _positionXSlider, _positionYSlider, _positionZSlider, _scaleSlider, _noteTiltSlider, _noteAzimuthSlider;
    private readonly TextBlock _pitchText, _yawText, _rollText, _fovText;
    private readonly TextBlock _positionXText, _positionYText, _positionZText, _scaleText, _noteTiltText, _noteAzimuthText;
    private bool _syncPoseControls;

    private sealed record OrientationChoice(KeyboardOrientation Value, string Caption);
    private sealed record ToolChoice(CalibrationTool Value, string Caption);
    private readonly Func<CalibrationPicture?>? _retake;
    private static readonly Brush NormalText = new SolidColorBrush(Color.FromRgb(231, 218, 244)), FixText = new SolidColorBrush(Color.FromRgb(255, 140, 160));

    /// <param name="picture">A still of the camera, as it delivers it.</param>
    /// <param name="layout">The keyboard being marked.</param>
    /// <param name="mirror">The stage shows the picture turned round, so the window does too.</param>
    /// <param name="retake">Gives a newer picture of the camera, or null when it has none.</param>
    /// <param name="orientation">How the keyboard looks in the picture to start with; when not given, as upright in the picture the camera delivers.</param>
    /// <param name="profile">The view of the camera the keys are being marked for, named in the title and the line above the buttons so nobody marks the keys for the angle the camera is not at.</param>
    /// <param name="initialCalibration">The full saved calibration, including FOV and editable 3D pose, when fine-tuning it.</param>
    internal CalibrationWindow(CalibrationPicture picture, KeyboardLayout layout, bool mirror, Func<CalibrationPicture?>? retake = null, KeyboardOrientation? orientation = null, ArCalibrationProfile? profile = null,
        ArCalibration? initialCalibration = null, ArNoteDirection noteDirection = default)
    {
        _retake = retake;
        Title = profile is null ? Loc.T("Mark the keys") : Loc.F("Mark the keys for the {0} view", Loc.T(profile.Name));
        Width = Math.Min(1120, Math.Max(640, SystemParameters.WorkArea.Width - 60));
        Height = Math.Min(780, Math.Max(480, SystemParameters.WorkArea.Height - 60));
        MinWidth = 640; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; UseLayoutRounding = true;
        Background = new SolidColorBrush(Color.FromRgb(18, 16, 24)); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI");

        var wizard = new CalibrationWizard(picture.Width, picture.Height, layout,
            initialCalibration?.CameraFovDegrees ?? ArCameraPose.DefaultFovDegrees);
        wizard.SetViewport(0, 0, mirror);                        // the picture is shown turned round or not from the start, whatever the orientation means for it
        var start = orientation ?? KeyboardOrientations.Default(mirror);
        wizard.SetOrientation(start);
        if (initialCalibration is not null) wizard.LoadCalibration(initialCalibration);
        _surface = new CalibrationSurface(picture, wizard, mirror, noteDirection);
        _hint = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, MinHeight = 38, Margin = new Thickness(16, 12, 16, 8), Foreground = new SolidColorBrush(Color.FromRgb(231, 218, 244)) };
        _note = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(255, 190, 120)), Margin = new Thickness(0, 2, 0, 0) };
        // roadmap §14.7 "Thêm 4": the two set-up tips nobody remembers on their first calibration, said once, plainly.
        _tips = new TextBlock
        {
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(158, 148, 170)),
            Margin = new Thickness(16, 0, 16, 6),
            Text = Loc.T("Hang the camera over your shoulder, looking down the keys: the low notes on one side, the far key edge along the top of the picture. A camera square overhead reads every key, a camera across the keys hides the far ones.") + "\n"
                 + Loc.T("Avoid a very wide lens (phone ultra-wide, fisheye): the rows of keys bend toward the picture edges, and corners marked on bent rows never stay on the real keys.") + "  "
                 + Loc.T("Keep hands out of the picture while marking: the camera's exposure walks as a hand waves, and the saved frame must see keys, not fingers."),
        };

        var info = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)) };
        info.Text = profile is null
            ? Loc.F("{0}-key keyboard · picture {1} × {2}", layout.KeyCount, picture.Width, picture.Height)
            : Loc.F("{0}-key keyboard · picture {1} × {2} · {3} view", layout.KeyCount, picture.Width, picture.Height, Loc.T(profile.Name));
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(info); left.Children.Add(_note);

        var newPicture = MakeButton(Loc.T("New picture"), false); newPicture.Click += (_, _) => Retake();
        _startOver = MakeButton(Loc.T("Start over"), false); _startOver.Click += (_, _) => { _surface.Wizard.Clear(); SyncPoseControls(); _surface.Refresh(); };
        _undo = MakeButton(Loc.T("Undo"), false); _undo.Click += (_, _) => { _surface.Wizard.Undo(); _surface.Refresh(); };
        var cancel = MakeButton(Loc.T("Cancel"), false); cancel.IsCancel = true;
        _done = MakeButton(Loc.T("Done"), true); _done.IsDefault = true; _done.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { newPicture, _startOver, _undo, cancel, _done }) { button.Margin = new Thickness(8, 0, 0, 0); buttons.Children.Add(button); }

        var bar = new DockPanel { Margin = new Thickness(16, 10, 16, 14), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons); bar.Children.Add(left);

        _orientation = new ComboBox { Width = 330, Height = 28, DisplayMemberPath = nameof(OrientationChoice.Caption), SelectedValuePath = nameof(OrientationChoice.Value), ToolTip = Loc.T(CalibrationHints.OrientationTip) };
        foreach (var choice in Enum.GetValues<KeyboardOrientation>()) _orientation.Items.Add(new OrientationChoice(choice, Loc.T(CalibrationHints.ForOrientation(choice))));
        _orientation.SelectedValue = start;
        _orientation.SelectionChanged += (_, _) =>
        {
            if (_orientation.SelectedValue is KeyboardOrientation chosen) { Wizard.SetOrientation(chosen); _surface.Refresh(); }
        };
        _orientation.DropDownClosed += (_, _) => _surface.Focus();          // the arrow keys and Tab are for the corners again as soon as the choice is made
        _resetOrientation = MakeResetButton(Loc.T(CalibrationHints.OrientationTitle), Loc.T(CalibrationHints.ForOrientation(start)),
            () => _orientation.SelectedValue = start);
        var orientationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8) };
        orientationRow.Children.Add(new TextBlock { Text = Loc.T(CalibrationHints.OrientationTitle), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)) });
        orientationRow.Children.Add(_orientation);
        _resetOrientation.Margin = new Thickness(4, 0, 0, 0); orientationRow.Children.Add(_resetOrientation);

        var fovRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center };
        fovRow.Children.Add(PoseLabel(Loc.T(CalibrationHints.CameraFovTitle), 88, Loc.T(CalibrationHints.CameraFovTip)));
        _fovSlider = PoseSlider(ArCameraPose.MinFovDegrees, ArCameraPose.MaxFovDegrees, wizard.CameraFovDegrees, 390, 2,
            Loc.T(CalibrationHints.CameraFovTip));
        _fovText = PoseValue(wizard.CameraFovDegrees.ToString("0.0", CultureInfo.CurrentCulture) + "°", 56);
        fovRow.Children.Add(_fovSlider); fovRow.Children.Add(_fovText);
        _resetFov = MakeResetButton(Loc.T(CalibrationHints.CameraFovTitle), DegreesValue(ArCameraPose.DefaultFovDegrees),
            () => _fovSlider.Value = ArCameraPose.DefaultFovDegrees, poseDependent: true);
        _resetFov.Margin = new Thickness(4, 0, 0, 0); fovRow.Children.Add(_resetFov);

        var poseRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8), VerticalAlignment = VerticalAlignment.Center };
        poseRow.Children.Add(PoseLabel(Loc.T(CalibrationHints.RotationTitle), 116, Loc.T(CalibrationHints.RotationTip)));
        _pitchSlider = PoseSlider(-180, 180, wizard.PitchDegrees, 196, 15, Loc.T(CalibrationHints.RotationTip));
        _yawSlider = PoseSlider(-180, 180, wizard.YawDegrees, 196, 15, Loc.T(CalibrationHints.RotationTip));
        _rollSlider = PoseSlider(-180, 180, wizard.RollDegrees, 196, 15, Loc.T(CalibrationHints.RotationTip));
        _pitchText = PoseValue("0.0°", 48); _yawText = PoseValue("0.0°", 48); _rollText = PoseValue("0.0°", 48);
        _resetPitch = AddPoseControl(poseRow, Loc.T(CalibrationHints.PitchTitle), _pitchSlider, _pitchText, DegreesValue(0),
            () => _pitchSlider.Value = 0, poseDependent: true);
        _resetYaw = AddPoseControl(poseRow, Loc.T(CalibrationHints.YawTitle), _yawSlider, _yawText, DegreesValue(0),
            () => _yawSlider.Value = 0, poseDependent: true);
        _resetRoll = AddPoseControl(poseRow, Loc.T(CalibrationHints.RollTitle), _rollSlider, _rollText, DegreesValue(0),
            () => _rollSlider.Value = 0, poseDependent: true);
        _pitchSlider.ValueChanged += PoseControl_ValueChanged;
        _yawSlider.ValueChanged += PoseControl_ValueChanged;
        _rollSlider.ValueChanged += PoseControl_ValueChanged;
        _fovSlider.ValueChanged += PoseControl_ValueChanged;

        var transformRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center };
        transformRow.Children.Add(PoseLabel(Loc.T(CalibrationHints.TransformTitle), 124, Loc.T(CalibrationHints.TransformTip)));
        var currentTransform = wizard.PoseTransform;
        _positionXSlider = PoseSlider(-ArPoseTransform.PositionLimit, ArPoseTransform.PositionLimit, currentTransform.PositionX, 108, .25, Loc.T(CalibrationHints.TransformTip));
        _positionYSlider = PoseSlider(-ArPoseTransform.PositionLimit, ArPoseTransform.PositionLimit, currentTransform.PositionY, 108, .25, Loc.T(CalibrationHints.TransformTip));
        _positionZSlider = PoseSlider(-ArPoseTransform.PositionLimit, ArPoseTransform.PositionLimit, currentTransform.PositionZ, 108, .25, Loc.T(CalibrationHints.TransformTip));
        _scaleSlider = PoseSlider(ArPoseTransform.MinScale, ArPoseTransform.MaxScale, currentTransform.UniformScale, 132, .25, Loc.T(CalibrationHints.TransformTip));
        _positionXText = PoseValue("0.00", 42); _positionYText = PoseValue("0.00", 42); _positionZText = PoseValue("0.00", 42); _scaleText = PoseValue("1.00×", 48);
        _resetPositionX = AddPoseControl(transformRow, "X", _positionXSlider, _positionXText, ScalarValue(0),
            () => _positionXSlider.Value = 0, poseDependent: true, tooltip: Loc.T(CalibrationHints.TransformTip));
        _resetPositionY = AddPoseControl(transformRow, "Y", _positionYSlider, _positionYText, ScalarValue(0),
            () => _positionYSlider.Value = 0, poseDependent: true, tooltip: Loc.T(CalibrationHints.TransformTip));
        _resetPositionZ = AddPoseControl(transformRow, "Z", _positionZSlider, _positionZText, ScalarValue(0),
            () => _positionZSlider.Value = 0, poseDependent: true, tooltip: Loc.T(CalibrationHints.TransformTip));
        _resetScale = AddPoseControl(transformRow, Loc.T(CalibrationHints.ScaleTitle), _scaleSlider, _scaleText, ScalarValue(1) + "×",
            () => _scaleSlider.Value = 1, poseDependent: true, tooltip: Loc.T(CalibrationHints.TransformTip));
        foreach (var slider in new[] { _positionXSlider, _positionYSlider, _positionZSlider, _scaleSlider }) slider.ValueChanged += ObjectControl_ValueChanged;

        var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center };
        toolRow.Children.Add(PoseLabel(Loc.T(CalibrationHints.ToolTitle), 70, Loc.T(CalibrationHints.ToolTip)));
        _toolBox = new ComboBox { Width = 190, Height = 28, DisplayMemberPath = nameof(ToolChoice.Caption), SelectedValuePath = nameof(ToolChoice.Value), ToolTip = Loc.T(CalibrationHints.ToolTip) };
        foreach (var tool in Enum.GetValues<CalibrationTool>()) _toolBox.Items.Add(new ToolChoice(tool, Loc.T(CalibrationHints.ForTool(tool))));
        _toolBox.SelectedValue = CalibrationTool.Corners;
        _toolBox.SelectionChanged += (_, _) => { if (_toolBox.SelectedValue is CalibrationTool tool) _surface.SetTool(tool); };
        _toolBox.DropDownClosed += (_, _) => _surface.Focus();
        toolRow.Children.Add(_toolBox);
        _resetTool = MakeResetButton(Loc.T(CalibrationHints.ToolTitle), Loc.T(CalibrationHints.ForTool(CalibrationTool.Corners)),
            () => _toolBox.SelectedValue = CalibrationTool.Corners, poseDependent: true);
        _resetTool.Margin = new Thickness(4, 0, 0, 0); toolRow.Children.Add(_resetTool);
        _screenUp = MakeButton(Loc.T(CalibrationHints.ScreenUpTitle), false);
        _screenUp.ToolTip = Loc.T(CalibrationHints.ScreenUpTip);
        _screenUp.Click += (_, _) =>
        {
            if (!_surface.SetNoteDirectionToScreenUp()) _note.Text = Loc.T(CalibrationHints.RotationOutOfBounds);
            else _note.Text = "";
            _surface.Focus();
        };
        _screenUp.Margin = new Thickness(10, 0, 0, 0); toolRow.Children.Add(_screenUp);
        _keyDepth = MakeButton(Loc.T(CalibrationHints.KeyDepthTitle), false);
        _keyDepth.ToolTip = Loc.T(CalibrationHints.KeyDepthTip);
        _keyDepth.Click += (_, _) => { _surface.SetNoteDirection(ArNoteDirection.Default, notify: true); _surface.Focus(); };
        _keyDepth.Margin = new Thickness(8, 0, 0, 0); toolRow.Children.Add(_keyDepth);

        var initialDirection = noteDirection.Normalised();
        var noteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 8), VerticalAlignment = VerticalAlignment.Center };
        noteRow.Children.Add(PoseLabel(Loc.T(CalibrationHints.NoteDirectionTitle), 95, Loc.T(CalibrationHints.NoteDirectionTip)));
        _noteTiltSlider = PoseSlider(ArNoteDirection.TiltMin, ArNoteDirection.TiltMax, initialDirection.TiltDegrees, 154, 10, Loc.T(CalibrationHints.NoteDirectionTip));
        _noteAzimuthSlider = PoseSlider(ArNoteDirection.AzimuthMin, ArNoteDirection.AzimuthMax, initialDirection.AzimuthDegrees, 154, 10, Loc.T(CalibrationHints.NoteDirectionTip));
        _noteTiltText = PoseValue(SignedDegrees(initialDirection.TiltDegrees), 48); _noteAzimuthText = PoseValue(SignedDegrees(initialDirection.AzimuthDegrees), 48);
        _resetNoteTilt = AddPoseControl(noteRow, Loc.T(CalibrationHints.TiltTitle), _noteTiltSlider, _noteTiltText, DegreesValue(ArNoteDirection.Default.TiltDegrees),
            () => _noteTiltSlider.Value = ArNoteDirection.Default.TiltDegrees, tooltip: Loc.T(CalibrationHints.NoteDirectionTip));
        _resetNoteAzimuth = AddPoseControl(noteRow, Loc.T(CalibrationHints.AzimuthTitle), _noteAzimuthSlider, _noteAzimuthText, DegreesValue(ArNoteDirection.Default.AzimuthDegrees),
            () => _noteAzimuthSlider.Value = ArNoteDirection.Default.AzimuthDegrees, tooltip: Loc.T(CalibrationHints.NoteDirectionTip));
        _noteTiltSlider.ValueChanged += NoteDirectionControl_ValueChanged;
        _noteAzimuthSlider.ValueChanged += NoteDirectionControl_ValueChanged;

        var root = new DockPanel();
        DockPanel.SetDock(_hint, Dock.Top); root.Children.Add(_hint);
        DockPanel.SetDock(_tips, Dock.Top); root.Children.Add(_tips);
        DockPanel.SetDock(orientationRow, Dock.Top); root.Children.Add(orientationRow);
        DockPanel.SetDock(fovRow, Dock.Top); root.Children.Add(fovRow);
        DockPanel.SetDock(poseRow, Dock.Top); root.Children.Add(poseRow);
        DockPanel.SetDock(transformRow, Dock.Top); root.Children.Add(transformRow);
        DockPanel.SetDock(toolRow, Dock.Top); root.Children.Add(toolRow);
        DockPanel.SetDock(noteRow, Dock.Top); root.Children.Add(noteRow);
        DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        root.Children.Add(new Border { Margin = new Thickness(16, 0, 16, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(79, 65, 97)), BorderThickness = new Thickness(1), Child = _surface });
        Content = root;

        _surface.Changed += RefreshText;
        _surface.NoteDirectionChanged += Surface_NoteDirectionChanged;
        RefreshText();
        UpdateFramingNote(picture);
        Loaded += (_, _) => _surface.Focus();
    }

    internal CalibrationSurface Surface => _surface;
    internal CalibrationWizard Wizard => _surface.Wizard;
    internal Button DoneButton => _done;

    /// <summary>The choice of how the keyboard looks in the picture.</summary>
    internal ComboBox OrientationBox => _orientation;

    /// <summary>The 3D pitch, yaw and roll controls; the roll alias keeps the old automation hook but is not a 2D image rotation.</summary>
    internal Slider RotationSlider => _rollSlider;
    internal Slider PitchSlider => _pitchSlider;
    internal Slider YawSlider => _yawSlider;
    internal Slider RollSlider => _rollSlider;
    internal Slider CameraFovSlider => _fovSlider;
    internal Button ResetOrientationButton => _resetOrientation;
    internal Button ResetFovButton => _resetFov;
    internal Button ResetPitchButton => _resetPitch;
    internal Button ResetYawButton => _resetYaw;
    internal Button ResetRollButton => _resetRoll;
    internal Button ResetPositionXButton => _resetPositionX;
    internal Button ResetPositionYButton => _resetPositionY;
    internal Button ResetPositionZButton => _resetPositionZ;
    internal Button ResetScaleButton => _resetScale;
    internal Button ResetToolButton => _resetTool;
    internal Button ResetNoteTiltButton => _resetNoteTilt;
    internal Button ResetNoteAzimuthButton => _resetNoteAzimuth;
    internal Slider PositionXSlider => _positionXSlider;
    internal Slider PositionYSlider => _positionYSlider;
    internal Slider PositionZSlider => _positionZSlider;
    internal Slider UniformScaleSlider => _scaleSlider;
    internal Slider NoteTiltSlider => _noteTiltSlider;
    internal Slider NoteAzimuthSlider => _noteAzimuthSlider;
    internal ComboBox ToolBox => _toolBox;
    internal Button ScreenUpButton => _screenUp;
    internal Button KeyDepthButton => _keyDepth;
    internal Button StartOverButton => _startOver;
    internal ArNoteDirection NoteDirection => _surface.NoteDirection;

    /// <summary>The calibration the person marked; null until Done is pressed.</summary>
    internal ArCalibration? Result { get; private set; }

    /// <summary>What the window says under the picture about the last New picture, or empty.</summary>
    internal string NoteText => _note.Text;

    /// <summary>The line above the picture as it reads now.</summary>
    internal string HintText => _hint.Text;

    private void PoseControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncPoseControls) return;
        if (!Wizard.Set3DPose(_pitchSlider.Value, _yawSlider.Value, _rollSlider.Value, _fovSlider.Value))
        {
            _note.Text = Loc.T(CalibrationHints.RotationOutOfBounds);
            SyncPoseControls();
            return;
        }
        _note.Text = "";
        SyncPoseControls();
        _surface.Refresh();
    }

    private void ObjectControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncPoseControls) return;
        var transform = Wizard.PoseTransform with
        {
            PositionX = _positionXSlider.Value, PositionY = _positionYSlider.Value, PositionZ = _positionZSlider.Value,
            UniformScale = _scaleSlider.Value,
        };
        if (!Wizard.SetObjectTransform(transform, _fovSlider.Value))
        {
            _note.Text = Loc.T(CalibrationHints.RotationOutOfBounds);
            SyncPoseControls();
            return;
        }
        _note.Text = "";
        SyncPoseControls();
        _surface.Refresh();
    }

    private void NoteDirectionControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncPoseControls) return;
        _surface.SetNoteDirection(new ArNoteDirection(_noteTiltSlider.Value, _noteAzimuthSlider.Value));
        SyncNoteDirectionControls();
    }

    private void Surface_NoteDirectionChanged(ArNoteDirection direction)
    {
        _syncPoseControls = true;
        _noteTiltSlider.Value = direction.TiltDegrees; _noteAzimuthSlider.Value = direction.AzimuthDegrees;
        _noteTiltText.Text = SignedDegrees(direction.TiltDegrees); _noteAzimuthText.Text = SignedDegrees(direction.AzimuthDegrees);
        _syncPoseControls = false;
    }

    private void SyncPoseControls()
    {
        _syncPoseControls = true;
        var transform = Wizard.PoseTransform;
        _pitchSlider.Value = transform.PitchDegrees; _yawSlider.Value = transform.YawDegrees; _rollSlider.Value = transform.RollDegrees;
        _fovSlider.Value = Wizard.CameraFovDegrees;
        _positionXSlider.Value = transform.PositionX; _positionYSlider.Value = transform.PositionY; _positionZSlider.Value = transform.PositionZ;
        _scaleSlider.Value = transform.UniformScale;
        _pitchText.Text = SignedDegrees(transform.PitchDegrees); _yawText.Text = SignedDegrees(transform.YawDegrees);
        _rollText.Text = SignedDegrees(transform.RollDegrees);
        _fovText.Text = Wizard.CameraFovDegrees.ToString("0.0", CultureInfo.CurrentCulture) + "°";
        _positionXText.Text = transform.PositionX.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
        _positionYText.Text = transform.PositionY.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
        _positionZText.Text = transform.PositionZ.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
        _scaleText.Text = transform.UniformScale.ToString("0.00", CultureInfo.CurrentCulture) + "×";
        SyncNoteDirectionControls();
        _syncPoseControls = false;
    }

    private void SyncNoteDirectionControls()
    {
        var direction = _surface.NoteDirection;
        _noteTiltSlider.Value = direction.TiltDegrees; _noteAzimuthSlider.Value = direction.AzimuthDegrees;
        _noteTiltText.Text = SignedDegrees(direction.TiltDegrees); _noteAzimuthText.Text = SignedDegrees(direction.AzimuthDegrees);
    }

    private static string SignedDegrees(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + "°";
    private static string DegreesValue(double value) => value.ToString("0.0", CultureInfo.CurrentCulture) + "°";
    private static string ScalarValue(double value) => value.ToString("0.00", CultureInfo.CurrentCulture);

    private void RefreshText()
    {
        SyncPoseControls();
        var text = Loc.T(CalibrationHints.ForStep(Wizard));
        if (_hint.Text != text) _hint.Text = text;
        _hint.Foreground = Wizard.Step == WizardStep.Fix ? FixText : NormalText;
        _done.IsEnabled = Wizard.CanFinish;
        _undo.IsEnabled = Wizard.Points.Count > 0 && !Wizard.IsDragging;
        _startOver.IsEnabled = Wizard.Points.Count > 0;
        var poseEnabled = Wizard.CanFinish;
        _pitchSlider.IsEnabled = _yawSlider.IsEnabled = _rollSlider.IsEnabled = _fovSlider.IsEnabled = poseEnabled;
        _positionXSlider.IsEnabled = _positionYSlider.IsEnabled = _positionZSlider.IsEnabled = _scaleSlider.IsEnabled = poseEnabled;
        _toolBox.IsEnabled = _screenUp.IsEnabled = _keyDepth.IsEnabled = poseEnabled;
        foreach (var resetButton in _poseResetButtons) resetButton.IsEnabled = poseEnabled;
        _resetTool.IsEnabled = poseEnabled;
        _noteTiltSlider.IsEnabled = _noteAzimuthSlider.IsEnabled = true;
        _resetNoteTilt.IsEnabled = _resetNoteAzimuth.IsEnabled = true;
        if (_note.Text.Length > 0 && Wizard.IsDragging) _note.Text = "";
    }

    /// <summary>New picture: a fresher still of the camera, keeping the corners placed so far.</summary>
    internal void Retake()
    {
        var picture = _retake?.Invoke();
        if (picture is null) { _note.Text = Loc.T("The camera has not delivered a picture yet. Try again in a moment."); return; }
        if (picture.Width != Wizard.ImageWidth || picture.Height != Wizard.ImageHeight)
        {
            _note.Text = Loc.T("The new picture has a different size. Close this window and mark the keys again.");
            return;
        }
        _surface.SetPicture(picture);
        UpdateFramingNote(picture);
    }

    /// <summary>Live framing advice (roadmap §14.7 "Thêm 4"): skin-coloured movement inside the marked keys once the corners are down, anywhere in the picture before — one read of the still the window shows, never a timer.</summary>
    private void UpdateFramingNote(CalibrationPicture picture)
    {
        var (handsInTheWay, _) = CalibrationAdvice.CheckPicture(picture.Bgra, picture.Width, picture.Height,
            Wizard.Points.Count == 4 ? Wizard.Points : null);
        _note.Text = handsInTheWay && !Wizard.IsDragging ? Loc.T("Something skin-coloured moves inside the picture (hands?). Keep hands outside the key area while calibrating.") : "";
    }

    /// <summary>Done: takes the calibration out of the wizard and closes. Does nothing while the corners cannot be used.</summary>
    internal void Accept()
    {
        if (Result is not null || !Wizard.CanFinish) return;
        var result = Wizard.Finish(out _);
        if (result is null) return;
        Result = result;
        try { DialogResult = true; }
        catch (InvalidOperationException) { Close(); }      // not shown as a dialog (a check drives the window without one)
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Accept(); e.Handled = true; return; }
        if (_surface.HandleKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private static TextBlock PoseLabel(string text, double width, string tooltip) => new()
    {
        Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center, ToolTip = tooltip,
        Foreground = new SolidColorBrush(Color.FromRgb(178, 168, 190)), FontSize = 12, Margin = new Thickness(0, 0, 8, 0)
    };

    private static Slider PoseSlider(double minimum, double maximum, double value, double width, double tick, string tooltip) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value, Width = width, Height = 24, IsMoveToPointEnabled = true,
        TickFrequency = tick, TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
        IsSnapToTickEnabled = false, IsEnabled = false, ToolTip = tooltip
    };

    private static TextBlock PoseValue(string text, double width) => new()
    {
        Text = text, Width = width, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(231, 218, 244)), FontSize = 11
    };

    private Button AddPoseControl(StackPanel row, string title, Slider slider, TextBlock value, string defaultValue,
        Action reset, bool poseDependent = false, string? tooltip = null)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        group.Children.Add(PoseLabel(title, 42, tooltip ?? Loc.T(CalibrationHints.RotationTip)));
        group.Children.Add(slider);
        group.Children.Add(value);
        var resetButton = MakeResetButton(title, defaultValue, reset, poseDependent);
        resetButton.Margin = new Thickness(4, 0, 0, 0);
        group.Children.Add(resetButton);
        row.Children.Add(group);
        return resetButton;
    }

    private Button MakeResetButton(string label, string defaultValue, Action reset, bool poseDependent = false)
    {
        var button = new Button { Tag = label, Style = (Style)FindResource("MiniButtonStyle"), Margin = new Thickness(4, 0, 0, 0) };
        var muted = new SolidColorBrush(Color.FromRgb(0xA5, 0xAC, 0xB9));
        button.Content = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource("IconReset"), Fill = muted, Stroke = muted, StrokeThickness = 1.4,
            StrokeLineJoin = PenLineJoin.Round, Width = 11, Height = 11, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Loc.Format(button, "Reset to {0}", FrameworkElement.ToolTipProperty, defaultValue);
        Loc.Format(button, "Reset to {0}", AutomationProperties.NameProperty, defaultValue);
        button.Click += (_, _) =>
        {
            _note.Text = "";
            reset();
            _surface.Focus();
        };
        if (poseDependent) _poseResetButtons.Add(button);
        return button;
    }

    private static Button MakeButton(string text, bool primary) => new()
    {
        Content = text, MinWidth = 92, Height = 30, Padding = new Thickness(12, 0, 12, 0), Foreground = Brushes.White,
        Background = new SolidColorBrush(primary ? Color.FromRgb(125, 61, 174) : Color.FromRgb(43, 38, 51)),
        BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(184, 119, 229) : Color.FromRgb(80, 71, 91)), BorderThickness = new Thickness(1),
    };
}
