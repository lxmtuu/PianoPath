namespace PianoPath;

/// <summary>
/// A note's travel direction in keyboard-local 3D: zero tilt is the default back-to-keys direction (+Y),
/// positive tilt turns toward the player-side surface normal (-Z), and azimuth rotates that turn toward across-key motion (+X).
/// </summary>
internal readonly record struct ArNoteDirection(double TiltDegrees = 0, double AzimuthDegrees = 0)
{
    internal static readonly ArNoteDirection Default = new();
    internal const double TiltMin = 0, TiltMax = 180, AzimuthMin = -180, AzimuthMax = 180;

    internal ArNoteDirection Normalised() => new(
        double.IsFinite(TiltDegrees) ? Math.Clamp(TiltDegrees, TiltMin, TiltMax) : 0,
        double.IsFinite(AzimuthDegrees) ? Math.Clamp(AzimuthDegrees, AzimuthMin, AzimuthMax) : 0);

    /// <summary>Unit vector in physical keyboard coordinates; its components are measured in keyboard-depth units.</summary>
    internal ArVec3 Vector
    {
        get
        {
            var d = Normalised();
            var tilt = d.TiltDegrees * Math.PI / 180;
            var azimuth = d.AzimuthDegrees * Math.PI / 180;
            var sinTilt = Math.Sin(tilt);
            return new ArVec3(sinTilt * Math.Sin(azimuth), Math.Cos(tilt), -sinTilt * Math.Cos(azimuth)).Normalized;
        }
    }

    internal static ArNoteDirection FromVector(ArVec3 vector)
    {
        vector = vector.Normalized;
        if (!vector.IsFinite || vector.Length < .9) return Default;
        var tilt = Math.Acos(Math.Clamp(vector.Y, -1, 1)) * 180 / Math.PI;
        var azimuth = Math.Sin(tilt * Math.PI / 180) < 1e-8 ? 0
            : Math.Atan2(vector.X, -vector.Z) * 180 / Math.PI;
        return new ArNoteDirection(tilt, azimuth).Normalised();
    }
}

/// <summary>A GPU-ready note patch and the pixel-space half-size used by the shared note material.</summary>
internal readonly record struct Ar3DNoteShape(ArPatch Patch, int Pitch, int NoteIndex, bool Black, bool Sounding, double Fade,
    double HalfWidthPixels, double HalfLengthPixels, double SceneY, ArVec3 Target, ArVec3 Travel,
    double HeadDistance, double TailDistance, double HalfWidthWorld);

/// <summary>A 3D-projected live key bar, still independent of the key's own tint.</summary>
internal readonly record struct Ar3DLiveBar(ArPatch Patch, int Pitch, double Fade, bool Held, float Strength,
    double HalfWidthPixels, double HalfLengthPixels, double SceneY, ArVec3 Target, ArVec3 Travel,
    double HeadDistance, double TailDistance, double HalfWidthWorld);

/// <summary>A travelling, velocity-colored AR note emitted by one MIDI Note On.</summary>
internal readonly record struct Ar3DLiveMidiNote(ArPatch Patch, int Pitch, double Fade, bool Held, float Strength,
    double HalfWidthPixels, double HalfLengthPixels, double SceneY, ArVec3 Target, ArVec3 Travel,
    double HeadDistance, double TailDistance, double HalfWidthWorld);

/// <summary>
/// Makes notes and optional live bars in actual keyboard-local 3D, projected through the calibration's camera pose.
/// This intentionally does not extend a planar homography beyond its horizon. The note centre follows the selected
/// 3D vector; each bar is a screen-facing quad at that 3D depth, so even a direction that points nearly at the lens
/// cannot collapse an otherwise visible note edge-on.
/// </summary>
internal static class Ar3DNoteProjection
{
    /// <summary>Fills notes to draw at <paramref name="position"/> seconds, clipped by reach and by the next same-key note.</summary>
    internal static void Build(ArCalibration calibration, ArView view, IReadOnlyList<NoteEvent> notes, double position,
        double maxNoteDuration, ArNoteStyle style, ArNoteDirection direction, double minAspect, List<Ar3DNoteShape> into,
        PianoSimulation? simulation = null)
    {
        into.Clear();
        if (calibration.CameraPose is not { } pose || !style.IsUsable || !double.IsFinite(position) || !view.Valid || notes.Count == 0) return;
        direction = direction.Normalised();
        var travel = direction.Vector;
        var until = position + style.Reach / style.FallSpeed;
        var first = NoteTimeline.FirstIndexAtOrAfter(notes, position - 1 - Math.Max(0, maxNoteDuration));
        var nextSame = NextOfSamePitch(notes);
        var layout = calibration.Layout;

        // White notes first, then black notes over them, as the piano roll and keyboard do.
        for (var pass = 0; pass < 2; pass++)
        {
            var wantBlack = pass == 1;
            for (var index = first; index < notes.Count; index++)
            {
                var note = notes[index];
                if (note.Start > until) break;
                if (!layout.Contains(note.Pitch) || KeyboardLayout.IsBlack(note.Pitch) != wantBlack || note.End < position) continue;

                var velocity = simulation?.ActivityForVelocity(note.Velocity) ?? Math.Clamp(note.Velocity / 127.0, 0, 1);
                var (left, right) = layout.Span(note.Pitch);
                var noteWidth = (right - left) * pose.KeyboardWidthDepthRatio * style.WidthShareFor(velocity);
                var halfWidth = noteWidth * .5;
                if (!(halfWidth > 0) || !double.IsFinite(halfWidth)) continue;

                // Signed distance is positive before onset and negative while the note is sounding. The leading edge
                // stops at the key plane; its tail shrinks with the remaining duration, then the short-note minimum grows it.
                var signedHead = (note.Start - position) * style.FallSpeed;
                var headDistance = Math.Max(0, signedHead);
                var naturalLength = Math.Max(note.Duration * style.FallSpeed - style.Gap, style.MinLength);
                var minLength = Math.Max(0, minAspect) * noteWidth;
                var tailDistance = Math.Max(signedHead + naturalLength, headDistance + minLength);
                if (nextSame[index] >= 0)
                {
                    var nextHead = (notes[nextSame[index]].Start - position) * style.FallSpeed;
                    tailDistance = Math.Min(tailDistance, Math.Max(headDistance, nextHead));
                }
                tailDistance = Math.Min(tailDistance, style.Reach);
                if (headDistance >= style.Reach || !(tailDistance > headDistance + 1e-7)) continue;

                var target = new ArVec3(layout.CenterX(note.Pitch) * pose.KeyboardWidthDepthRatio, 0, 0);
                if (!TryPatch(pose, view, target, travel, headDistance, tailDistance, halfWidth, out var patch, out var halfWidthPx, out var halfLengthPx, out var sceneY)) continue;
                var fadeRate = Math.Clamp(style.FadeGraph.Sample(velocity), .1, 2);
                var fade = Math.Clamp((style.Reach - headDistance) * fadeRate / (style.Reach * .25), 0, 1);
                into.Add(new Ar3DNoteShape(patch, note.Pitch, index, wantBlack, note.Start <= position && note.End > position,
                    fade, halfWidthPx, halfLengthPx, sceneY, target, travel, headDistance, tailDistance, halfWidth));
            }
        }
    }

    /// <summary>Fills one 3D bar for every held key or recently released key, respecting the independent trail toggle at the caller.</summary>
    internal static void BuildLive(ArCalibration calibration, ArView view, IReadOnlyList<ArLivePress> presses, ArNoteStyle style,
        ArNoteDirection direction, List<Ar3DLiveBar> into, PianoSimulation? simulation = null)
    {
        into.Clear();
        if (calibration.CameraPose is not { } pose || !style.IsUsable || !view.Valid || presses.Count == 0) return;
        direction = direction.Normalised();
        var travel = direction.Vector;
        var layout = calibration.Layout;

        for (var pass = 0; pass < 2; pass++)
        {
            var wantBlack = pass == 1;
            foreach (var press in presses)
            {
                if (!layout.Contains(press.Pitch) || KeyboardLayout.IsBlack(press.Pitch) != wantBlack) continue;
                if (!double.IsFinite(press.Age) || !double.IsFinite(press.HeldSeconds) || press.HeldSeconds <= 0) continue;
                var gone = 1.0;
                var releaseDistance = 0.0;
                var releaseFraction = 0.0;                      // how far the release has travelled, for the fade graph's backward walk
                if (!press.Down)
                {
                    var since = press.Age - press.HeldSeconds;
                    if (!(since >= 0) || !double.IsFinite(since)) continue;
                    // A released bar keeps travelling along its selected 3D direction. Its lifetime follows the
                    // same physical reach/speed as song notes rather than the legacy flat overlay's 350 ms fade.
                    releaseDistance = since * style.FallSpeed;
                    if (!(releaseDistance < style.Reach)) continue;
                    releaseFraction = Math.Clamp(releaseDistance / style.Reach, 0, 1);
                    gone = .65 * Math.Clamp(1 - releaseDistance / style.Reach, 0, 1);
                }
                var length = Math.Min(style.Reach, press.HeldSeconds * style.FallSpeed);
                if (length < style.MinLength) continue;
                var velocity = simulation?.ActivityForVelocity(press.Strength * 127) ?? Math.Clamp(press.Strength, 0, 1);
                var (left, right) = layout.Span(press.Pitch);
                var halfWidth = (right - left) * pose.KeyboardWidthDepthRatio * style.WidthShareFor(velocity) * .5;
                if (!(halfWidth > 0) || !double.IsFinite(halfWidth)) continue;
                var target = new ArVec3(layout.CenterX(press.Pitch) * pose.KeyboardWidthDepthRatio, 0, 0);
                // One world unit is one key depth in this frame, so the style's coverage fraction places the bar's
                // leading edge that far past the key's back edge without touching the pose.
                var headDistance = releaseDistance - style.LiveBarCoverFraction;
                var tailDistance = Math.Min(style.Reach, releaseDistance + length);
                if (!(tailDistance > headDistance + 1e-7)) continue;
                if (!TryPatch(pose, view, target, travel, headDistance, tailDistance, halfWidth, out var patch, out var halfWidthPx, out var halfLengthPx, out var sceneY)) continue;
                var fade = ArLiveFade.Opacity(style.LiveFade, length / style.Reach, press.Down, releaseFraction) * gone * style.FadeGraph.Sample(velocity);
                into.Add(new Ar3DLiveBar(patch, press.Pitch, fade, press.Down, press.Strength,
                    halfWidthPx, halfLengthPx, sceneY, target, travel, headDistance, tailDistance, halfWidth));
            }
        }
    }

    /// <summary>Fills one calibrated travelling note per live MIDI onset; Note Off freezes the body's held duration, not its motion.</summary>
    internal static void BuildRisingMidiNotes(ArCalibration calibration, ArView view, IReadOnlyList<ArLiveMidiNote> notes,
        ArNoteStyle style, ArNoteDirection direction, List<Ar3DLiveMidiNote> into, PianoSimulation? simulation = null)
    {
        into.Clear();
        if (calibration.CameraPose is not { } pose || !style.IsUsable || !view.Valid || notes.Count == 0) return;
        direction = direction.Normalised();
        var travel = direction.Vector;
        var layout = calibration.Layout;
        var maxLength = Math.Min(style.Reach, Math.Max(style.MinLength, style.Reach * .25));

        // Keep MIDI onset order within each colour pass, matching the song-note white-then-black draw order.
        for (var pass = 0; pass < 2; pass++)
        {
            var wantBlack = pass == 1;
            foreach (var note in notes)
            {
                if (!layout.Contains(note.Pitch) || KeyboardLayout.IsBlack(note.Pitch) != wantBlack
                    || !double.IsFinite(note.Age) || note.Age < 0 || !double.IsFinite(note.HeldSeconds) || note.HeldSeconds < 0) continue;
                var headDistance = note.Age * style.FallSpeed;
                if (!(headDistance < style.Reach)) continue;
                var velocity = simulation?.ActivityForVelocity(note.Strength * 127) ?? Math.Clamp(note.Strength, 0, 1);
                var (left, right) = layout.Span(note.Pitch);
                var noteWidth = (right - left) * pose.KeyboardWidthDepthRatio * style.WidthShareFor(velocity);
                var halfWidth = noteWidth * .5;
                if (!(halfWidth > 0) || !double.IsFinite(halfWidth)) continue;

                // Each Note On starts a distinct object at the key and advances immediately. Its body grows to the
                // physical hold duration, then freezes at Note Off while the leading edge continues to the AR reach.
                var length = Math.Min(maxLength, Math.Max(style.MinLength, note.HeldSeconds * style.FallSpeed));
                var tailDistance = Math.Min(style.Reach, headDistance + length);
                if (!(tailDistance > headDistance + 1e-7)) continue;
                var target = new ArVec3(layout.CenterX(note.Pitch) * pose.KeyboardWidthDepthRatio, 0, 0);
                if (!TryPatch(pose, view, target, travel, headDistance, tailDistance, halfWidth,
                    out var patch, out var halfWidthPx, out var halfLengthPx, out var sceneY)) continue;
                var fadeOut = Math.Clamp((style.Reach - headDistance) / (style.Reach * .25), 0, 1);
                var fadeRate = Math.Clamp(style.FadeGraph.Sample(velocity), .1, 2);
                var fadeIn = Math.Clamp(note.Age * fadeRate / .045, .25, 1);
                var fade = fadeOut * fadeIn;
                into.Add(new Ar3DLiveMidiNote(patch, note.Pitch, fade, note.Down, note.Strength,
                    halfWidthPx, halfLengthPx, sceneY, target, travel, headDistance, tailDistance, halfWidth));
            }
        }
    }

    /// <summary>Builds a camera-facing projected quad whose centre still follows the selected 3D motion vector.</summary>
    internal static bool TryPatch(ArCameraPose pose, ArView view, ArVec3 target, ArVec3 travel,
        double headDistance, double tailDistance, double halfWidthWorld, out ArPatch patch,
        out double halfWidthPixels, out double halfLengthPixels, out double sceneY)
    {
        patch = default; halfWidthPixels = halfLengthPixels = sceneY = 0;
        if (!view.Valid || !target.IsFinite || !travel.IsFinite || !(tailDistance > headDistance) || !(halfWidthWorld > 0)) return false;
        travel = travel.Normalized;
        if (travel.Length < .9) return false;
        var targetCamera = pose.ToCamera(target);
        var travelCamera = pose.DirectionToCamera(travel);
        if (!(targetCamera.Z > ArCameraPose.NearPlane) || !travelCamera.IsFinite) return false;

        // When an edited direction points toward and through the lens, keep the visible part up to the near plane rather
        // than feeding negative clip-w to the GPU. The default and the whole back-to-keys hemisphere recede safely.
        if (travelCamera.Z > 1e-12)
        {
            var farthestVisible = (targetCamera.Z - ArCameraPose.NearPlane * 1.02) / travelCamera.Z;
            tailDistance = Math.Min(tailDistance, farthestVisible);
            if (headDistance >= farthestVisible)
            {
                if (!(farthestVisible > 0)) return false;
                headDistance = Math.Max(0, farthestVisible - Math.Min(.02, farthestVisible * .02));
                tailDistance = farthestVisible;
            }
            if (!(tailDistance > headDistance + 1e-7)) return false;
        }

        var centreDistance = (headDistance + tailDistance) * .5;
        var halfLengthWorld = (tailDistance - headDistance) * .5;
        var centreCamera = targetCamera - travelCamera * centreDistance;
        if (!(centreCamera.Z > ArCameraPose.NearPlane) || !pose.TryProject(centreCamera, out var imageCentre)) return false;
        var centre = view.ToOutput(imageCentre.X, imageCentre.Y);
        if (!double.IsFinite(centre.X) || !double.IsFinite(centre.Y)) return false;

        var hasTravelAxis = pose.TryProjectDelta(centreCamera, travelCamera, view, out var along);
        var pathScale = hasTravelAxis ? Math.Sqrt(along.X * along.X + along.Y * along.Y) : 0;
        if (!(pathScale > 1e-5) || !double.IsFinite(pathScale))
        {
            // A vector aimed almost exactly into the camera has no stable 2D tangent. Keep the real 3D centre/depth,
            // but orient and size its billboard using the visible back-to-front keyboard axis instead of hiding it.
            if (!pose.TryProjectDelta(centreCamera, pose.Down, view, out along)) along = (0, 1);
            pathScale = Math.Sqrt(along.X * along.X + along.Y * along.Y);
            if (!(pathScale > 1e-5) || !double.IsFinite(pathScale)) { along = (0, 1); pathScale = 1; }
        }
        var axis = (X: along.X / pathScale, Y: along.Y / pathScale);

        var widthScale = pose.TryProjectDelta(centreCamera, pose.Right, view, out var across)
            ? Math.Sqrt(across.X * across.X + across.Y * across.Y) : 0;
        if (!(widthScale > 1e-5) || !double.IsFinite(widthScale)) widthScale = pose.Fx / centreCamera.Z * view.Scale;
        var depthScale = pose.TryProjectDelta(centreCamera, pose.Down, view, out var depth)
            ? Math.Sqrt(depth.X * depth.X + depth.Y * depth.Y) : 0;
        var normalScale = pose.TryProjectDelta(centreCamera, pose.Normal, view, out var normal)
            ? Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y) : 0;
        var fallbackScale = Math.Max(depthScale, normalScale);
        if (!(fallbackScale > 1e-5) || !double.IsFinite(fallbackScale)) fallbackScale = Math.Max(.05, widthScale / pose.KeyboardWidthDepthRatio);
        if (pathScale < fallbackScale * .025) pathScale = fallbackScale;

        halfWidthPixels = Math.Clamp(halfWidthWorld * widthScale, .5, ArProjection.MaxCoordinate * .01);
        halfLengthPixels = Math.Clamp(halfLengthWorld * pathScale, .5, ArProjection.MaxCoordinate * .01);
        if (!double.IsFinite(halfWidthPixels) || !double.IsFinite(halfLengthPixels)) return false;
        var side = (X: -axis.Y, Y: axis.X);
        if (across.X * side.X + across.Y * side.Y < 0) side = (-side.X, -side.Y);

        var tail = (X: centre.X - axis.X * halfLengthPixels, Y: centre.Y - axis.Y * halfLengthPixels);
        var head = (X: centre.X + axis.X * halfLengthPixels, Y: centre.Y + axis.Y * halfLengthPixels);
        var backLeft = new ArCorner(tail.X - side.X * halfWidthPixels, tail.Y - side.Y * halfWidthPixels, centreCamera.Z);
        var backRight = new ArCorner(tail.X + side.X * halfWidthPixels, tail.Y + side.Y * halfWidthPixels, centreCamera.Z);
        var frontRight = new ArCorner(head.X + side.X * halfWidthPixels, head.Y + side.Y * halfWidthPixels, centreCamera.Z);
        var frontLeft = new ArCorner(head.X - side.X * halfWidthPixels, head.Y - side.Y * halfWidthPixels, centreCamera.Z);
        patch = new ArPatch(backLeft, backRight, frontRight, frontLeft);
        sceneY = centre.Y / Math.Max(view.Scale, 1e-6);
        return patch.Area > .01 && double.IsFinite(patch.Area) && Math.Abs(centre.X) < ArProjection.MaxCoordinate && Math.Abs(centre.Y) < ArProjection.MaxCoordinate;
    }

    /// <summary>
    /// Projects a keyboard-local camera-facing billboard from all four 3D corners. Unlike screen-space sprites, its
    /// centre and size follow the saved pose/FOV and arbitrary camera orientation; callers choose the billboard axes.
    /// </summary>
    internal static bool TryProjectBillboard(ArCameraPose pose, ArView view, ArVec3 centre, ArVec3 axisX, ArVec3 axisY,
        double halfX, double halfY, out ArPatch patch)
    {
        patch = default;
        if (!view.Valid || !centre.IsFinite || !axisX.IsFinite || !axisY.IsFinite
            || !(halfX > 0) || !(halfY > 0) || !double.IsFinite(halfX) || !double.IsFinite(halfY)) return false;
        axisX = axisX.Normalized; axisY = axisY.Normalized;
        if (axisX.Length < .9 || axisY.Length < .9) return false;
        if (!pose.TryProject(centre - axisX * halfX - axisY * halfY, view, out var backLeft)
            || !pose.TryProject(centre + axisX * halfX - axisY * halfY, view, out var backRight)
            || !pose.TryProject(centre + axisX * halfX + axisY * halfY, view, out var frontRight)
            || !pose.TryProject(centre - axisX * halfX + axisY * halfY, view, out var frontLeft)) return false;
        patch = new ArPatch(backLeft, backRight, frontRight, frontLeft);
        return patch.Area > .01 && double.IsFinite(patch.Area);
    }

    private static int[] NextOfSamePitch(IReadOnlyList<NoteEvent> notes)
    {
        var next = new int[notes.Count];
        Span<int> last = stackalloc int[128];
        last.Fill(-1);
        for (var i = notes.Count - 1; i >= 0; i--)
        {
            var pitch = notes[i].Pitch;
            next[i] = pitch is >= 0 and < 128 ? last[pitch] : -1;
            if (pitch is >= 0 and < 128) last[pitch] = i;
        }
        return next;
    }
}
