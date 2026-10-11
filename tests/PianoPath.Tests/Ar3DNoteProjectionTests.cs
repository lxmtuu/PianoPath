using Xunit;

namespace PianoPath.Tests;

/// <summary>AR song notes and live bars are projected from a calibrated 3D camera pose, independently of note travel direction.</summary>
public class Ar3DNoteProjectionTests
{
    private static readonly (double X, double Y)[] Corners = [(70, 292), (1214, 268), (1236, 421), (52, 449)];
    private static readonly ArView View = new(1280, 720, 1280, 720, PictureFit.Fit, false);
    private static readonly ArNoteStyle Style = ArNoteStyle.Default with { Reach = 4.5 };
    private static readonly ArCalibration Calibration = MakeCalibration(70);

    private static ArCalibration MakeCalibration(double fov) =>
        ArCalibration.Create(KeyboardLayout.Standard88, Corners, 1280, 720, out var problem, fov)
        ?? throw new InvalidOperationException($"the sample corners were refused: {problem}");

    private static NoteEvent Note(int pitch, double start, double duration) => new() { Pitch = pitch, Start = start, Duration = duration };

    private static List<Ar3DNoteShape> Build(IReadOnlyList<NoteEvent> notes, double position = 10,
        ArNoteDirection direction = default, double minAspect = 0, double maxDuration = 3, ArNoteStyle? style = null)
    {
        var into = new List<Ar3DNoteShape> { default };
        Ar3DNoteProjection.Build(Calibration, View, notes, position, maxDuration, style ?? Style, direction, minAspect, into);
        return into;
    }

    private static void Near(ArVec3 expected, ArVec3 actual, double tolerance = 1e-12)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, tolerance);
        Assert.InRange(Math.Abs(expected.Z - actual.Z), 0, tolerance);
    }

    private static void AssertFinite(ArPatch patch)
    {
        Assert.True(patch.Area > .01 && double.IsFinite(patch.Area));
        for (var i = 0; i < 4; i++)
        {
            Assert.True(double.IsFinite(patch[i].X) && double.IsFinite(patch[i].Y));
            Assert.True(double.IsFinite(patch[i].W) && patch[i].W > ArCameraPose.NearPlane);
        }
    }

    private static (double X, double Y) Centre(ArPatch patch) =>
        ((patch.BackLeft.X + patch.BackRight.X + patch.FrontRight.X + patch.FrontLeft.X) * .25,
         (patch.BackLeft.Y + patch.BackRight.Y + patch.FrontRight.Y + patch.FrontLeft.Y) * .25);

    private static double Distance(double x, double y) => Math.Sqrt(x * x + y * y);

    private static List<Ar3DLiveBar> BuildLive(IReadOnlyList<ArLivePress> presses,
        ArNoteDirection direction = default, ArNoteStyle? style = null)
    {
        var into = new List<Ar3DLiveBar>();
        Ar3DNoteProjection.BuildLive(Calibration, View, presses, style ?? Style, direction, into);
        return into;
    }

    private static List<Ar3DLiveMidiNote> BuildRisingMidi(IReadOnlyList<ArLiveMidiNote> notes,
        ArNoteDirection direction = default, ArNoteStyle? style = null)
    {
        var into = new List<Ar3DLiveMidiNote>();
        Ar3DNoteProjection.BuildRisingMidiNotes(Calibration, View, notes, style ?? Style, direction, into);
        return into;
    }

    [Fact]
    public void Travel_direction_is_a_unit_vector_with_the_back_to_keys_axis_as_its_default()
    {
        Near(new ArVec3(0, 1, 0), ArNoteDirection.Default.Vector);
        Near(new ArVec3(0, 0, -1), new ArNoteDirection(90, 0).Vector);
        Near(new ArVec3(1, 0, 0), new ArNoteDirection(90, 90).Vector);
        Near(new ArVec3(0, -1, 0), new ArNoteDirection(180, 0).Vector);
        foreach (var direction in new[] { new ArNoteDirection(0, 0), new ArNoteDirection(43, -137), new ArNoteDirection(90, 90), new ArNoteDirection(180, 180) })
            Assert.InRange(direction.Vector.Length, .999999999, 1.000000001);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(24, -18, 12, false)]
    [InlineData(-37, 61, -28, true)]
    [InlineData(35, 48, 32, true)]
    public void Screen_up_direction_is_solved_as_full_3d_for_an_arbitrary_transformed_keyboard_pose(double pitch, double yaw, double roll, bool mirror)
    {
        // Keep the transformed quad inside the frame even at these deliberately large rotations.
        var baseCorners = Corners.Select(point => (640 + (point.X - 640) * .55, 360 + (point.Y - 360) * .55)).ToArray();
        var transform = new ArPoseTransform(.08, -.04, .02, pitch, yaw, roll, 1.12);
        Assert.True(ArCameraPose.TryTransformCorners(KeyboardLayout.Standard88, baseCorners, 1280, 720, 70, transform, out var finalCorners));
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, finalCorners, 1280, 720, out _, 70, transform, baseCorners);
        Assert.NotNull(calibration?.CameraPose);
        var pose = calibration!.CameraPose!.Value;
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, mirror);
        var centre = new ArVec3(pose.KeyboardWidthDepthRatio * .5, .5, 0);
        Assert.True(pose.TryDirectionForScreenDelta(centre, (0, -1), view, out var localDirection));
        var direction = ArNoteDirection.FromVector(localDirection);
        Assert.InRange((direction.Vector - localDirection).Length, 0, 1e-9);
        Assert.True(pose.TryProjectDelta(pose.ToCamera(centre), pose.DirectionToCamera(direction.Vector), view, out var projected));
        var length = Math.Sqrt(projected.X * projected.X + projected.Y * projected.Y);
        Assert.True(length > 1e-6);
        Assert.InRange(Math.Abs(projected.X / length), 0, 1e-3);
        Assert.InRange(projected.Y / length, -1.001, -.999);
    }

    [Fact]
    public void Every_note_filter_is_observable_and_valid_notes_produce_finite_projected_geometry()
    {
        Assert.NotNull(Calibration.CameraPose);
        var notes = new[]
        {
            Note(60, 8, 1),                 // ended before the playhead
            Note(60, 10.1, 2),              // long held note, still sounding
            Note(60, 10.6, .1),             // same-key repeat clips the first tail instead of hiding either note
            Note(61, 10.2, .3),              // black key: drawn after white notes
            Note(127, 10.25, .3),            // pitch outside this keyboard
            Note(64, 14.1, .2),              // beyond the lookahead/reach
        };
        var shapes = Build(notes, maxDuration: 2);
        Assert.Equal(new[] { 60, 60, 61 }, shapes.Select(shape => shape.Pitch));
        Assert.Equal(new[] { 1, 2, 3 }, shapes.Select(shape => shape.NoteIndex));
        Assert.Equal(new[] { false, false, true }, shapes.Select(shape => shape.Black));
        Assert.All(shapes, shape => AssertFinite(shape.Patch));
        Assert.All(shapes, shape => Assert.True(shape.HalfWidthPixels > 0 && shape.HalfLengthPixels > 0 && double.IsFinite(shape.SceneY)
            && shape.Target.IsFinite && shape.Travel.IsFinite && shape.HalfWidthWorld > 0
            && shape.HeadDistance >= 0 && shape.TailDistance > shape.HeadDistance));
        Assert.True(shapes[0].HalfLengthPixels > shapes[1].HalfLengthPixels,
            "The next same-pitch onset must shorten the older note without removing both notes.");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 0)]
    [InlineData(90, 0)]
    [InlineData(90, 90)]
    [InlineData(90, -90)]
    [InlineData(135, 180)]
    [InlineData(180, 0)]
    public void Notes_remain_projectable_for_independent_3d_travel_directions(double tilt, double azimuth)
    {
        var shape = Assert.Single(Build([Note(60, 10.2, .2)], direction: new ArNoteDirection(tilt, azimuth)));
        Assert.Equal(60, shape.Pitch);
        AssertFinite(shape.Patch);
        Assert.True(shape.HalfWidthPixels > 0 && shape.HalfLengthPixels > 0);
    }

    [Fact]
    public void Minimum_length_extends_short_notes_in_key_width_units_and_never_mutates_the_song()
    {
        var note = Note(60, 10.08, .02);
        var natural = Assert.Single(Build([note]));
        var extended = Assert.Single(Build([note], minAspect: 2));
        Assert.True(extended.HalfLengthPixels > natural.HalfLengthPixels * 2,
            $"the 200% minimum should enlarge a staccato note (half-length {natural.HalfLengthPixels:0.##} -> {extended.HalfLengthPixels:0.##} px)");
        Assert.Equal(10.08, note.Start);
        Assert.Equal(.02, note.Duration);
    }

    [Fact]
    public void A_live_bar_uses_the_same_3d_pose_and_independent_direction_as_song_notes()
    {
        var presses = new[] { new ArLivePress(60, Age: .8, HeldSeconds: .8, Down: true, Strength: .9f) };
        var bars = new List<Ar3DLiveBar>();
        Ar3DNoteProjection.BuildLive(Calibration, View, presses, Style, new ArNoteDirection(68, 37), bars);
        var bar = Assert.Single(bars);
        Assert.Equal(60, bar.Pitch);
        Assert.True(bar.Held);
        Assert.InRange(bar.Strength, .899f, .901f);
        AssertFinite(bar.Patch);
        Assert.True(bar.HalfWidthPixels > 0 && bar.HalfLengthPixels > 0);
        Assert.True(bar.Target.IsFinite && bar.Travel.IsFinite && bar.HalfWidthWorld > 0
            && bar.HeadDistance == 0 && bar.TailDistance > bar.HeadDistance);
    }

    [Fact]
    public void Each_midi_note_on_emits_a_separate_3d_note_that_keeps_travelling_after_note_off()
    {
        var direction = new ArNoteDirection(68, 37);
        var held = new ArLiveMidiNote(60, Age: .22, HeldSeconds: .22, Down: true, Strength: .9f);
        var released = new ArLiveMidiNote(60, Age: .52, HeldSeconds: .22, Down: false, Strength: .9f);
        var heldShape = Assert.Single(BuildRisingMidi([held], direction));
        var releasedShape = Assert.Single(BuildRisingMidi([released], direction));
        Assert.True(heldShape.Held);
        Assert.False(releasedShape.Held);
        Assert.Equal(60, releasedShape.Pitch);
        Assert.InRange(releasedShape.Strength, .899f, .901f);
        AssertFinite(heldShape.Patch);
        AssertFinite(releasedShape.Patch);
        Assert.True(releasedShape.HalfWidthPixels > 0 && releasedShape.HalfLengthPixels > 0);
        Assert.True(releasedShape.Target.IsFinite && releasedShape.Travel.IsFinite && releasedShape.HalfWidthWorld > 0
            && releasedShape.HeadDistance > heldShape.HeadDistance && releasedShape.TailDistance > releasedShape.HeadDistance);

        var heldCentre = Centre(heldShape.Patch);
        var releasedCentre = Centre(releasedShape.Patch);
        Assert.True(Distance(releasedCentre.X - heldCentre.X, releasedCentre.Y - heldCentre.Y) > 5,
            "Note Off freezes the note body but must not freeze its 3D travel centre.");

        var repeatedSamePitch = BuildRisingMidi([
            new ArLiveMidiNote(60, .12, .12, false, .4f),
            new ArLiveMidiNote(60, .3, .3, true, .95f)
        ], direction);
        Assert.Equal(2, repeatedSamePitch.Count);
        Assert.All(repeatedSamePitch, shape => Assert.Equal(60, shape.Pitch));
        Assert.Empty(BuildRisingMidi([new ArLiveMidiNote(60, Style.Reach / Style.FallSpeed, .2, false, .9f)], direction));
    }

    [Fact]
    public void Live_bar_and_Perform_note_reaches_are_fixed_and_do_not_follow_Learn_lookahead()
    {
        var shortLookahead = Style with { Reach = 1.5 };
        var longLookahead = Style with { Reach = 9 };
        var shortLivePress = ArOptions.LivePressNoteStyle(shortLookahead);
        var longLivePress = ArOptions.LivePressNoteStyle(longLookahead);
        var shortRisingMidi = ArOptions.RisingMidiNoteStyle(shortLookahead);
        var longRisingMidi = ArOptions.RisingMidiNoteStyle(longLookahead);

        Assert.Equal(3d, ArOptions.LivePressReachSeconds);
        Assert.Equal(3d, ArOptions.RisingMidiNoteReachSeconds);
        Assert.Equal(ArOptions.RisingMidiNoteReachSeconds * Style.FallSpeed, shortRisingMidi.Reach);
        Assert.Equal(shortRisingMidi.Reach, longRisingMidi.Reach);
        Assert.Equal(ArOptions.LivePressReachSeconds * Style.FallSpeed, shortLivePress.Reach);
        Assert.Equal(shortLivePress.Reach, longLivePress.Reach);
        Assert.Equal(Style.WidthGraph, longRisingMidi.WidthGraph);
        Assert.Equal(Style.FadeGraph, longRisingMidi.FadeGraph);
    }

    [Fact]
    public void Song_note_fade_velocity_graph_controls_the_same_reach_fade_used_by_the_projected_note()
    {
        var style = Style with
        {
            FadeVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .5, Max: 1)
        };
        var start = 10 + (style.Reach - .6) / style.FallSpeed;
        var notes = new[]
        {
            new NoteEvent { Pitch = 60, Start = start, Duration = .4, Velocity = 20 },
            new NoteEvent { Pitch = 64, Start = start, Duration = .4, Velocity = 110 }
        };
        var shapes = Build(notes, style: style);

        Assert.Equal(2, shapes.Count);
        Assert.True(shapes[1].Fade > shapes[0].Fade,
            "A harder projected song-note strike should use the faster end of the same AR fade graph.");
    }

    [Fact]
    public void Rising_midi_notes_use_the_same_velocity_width_and_fade_graphs_as_ar_song_notes()
    {
        var style = Style with
        {
            WidthVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .4, Max: 1),
            FadeVelocity = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .5, Max: 1)
        };
        var softAndHard = BuildRisingMidi([
            new ArLiveMidiNote(60, .02, .02, true, .2f),
            new ArLiveMidiNote(60, .02, .02, true, .9f)
        ], style: style);
        Assert.Equal(2, softAndHard.Count);
        Assert.True(softAndHard[1].HalfWidthPixels > softAndHard[0].HalfWidthPixels,
            "A hard MIDI onset should use the hard end of the AR note-width graph.");
        Assert.True(softAndHard[1].Fade > softAndHard[0].Fade,
            "A hard MIDI onset should fade in faster, through the same AR fade graph as song notes.");

        var flat = BuildRisingMidi([
            new ArLiveMidiNote(60, .02, .02, true, .2f),
            new ArLiveMidiNote(60, .02, .02, true, .9f)
        ]);
        Assert.Equal(flat[0].HalfWidthPixels, flat[1].HalfWidthPixels, 9);
        Assert.Equal(flat[0].Fade, flat[1].Fade, 9);
    }

    [Fact]
    public void A_released_live_bar_keeps_moving_along_its_calibrated_3d_path_until_it_reaches_its_limit()
    {
        var pose = Calibration.CameraPose!.Value;
        var heldSeconds = .8;
        var held = Assert.Single(BuildLive([new ArLivePress(60, heldSeconds, heldSeconds, true, .9f)]));
        var ageAfterRelease = heldSeconds + .3;
        var released = Assert.Single(BuildLive([new ArLivePress(60, ageAfterRelease, heldSeconds, false, .9f)]));

        Assert.True(released.Fade < held.Fade);
        var heldCentre = Centre(held.Patch);
        var releasedCentre = Centre(released.Patch);
        var movedPixels = Distance(releasedCentre.X - heldCentre.X, releasedCentre.Y - heldCentre.Y);
        Assert.True(movedPixels > 5, $"The bar must keep travelling after note-off, not only fade in place ({movedPixels:0.0}px moved).");

        // Verify the centre against the selected keyboard-local travel vector transformed by the calibrated camera pose.
        var target = new ArVec3(Calibration.Layout.CenterX(60) * pose.KeyboardWidthDepthRatio, 0, 0);
        var travelCamera = pose.DirectionToCamera(ArNoteDirection.Default.Vector);
        var heldDistance = heldSeconds * Style.FallSpeed * .5;
        var releasedDistance = (ageAfterRelease - heldSeconds) * Style.FallSpeed + heldDistance;
        Assert.True(pose.TryProject(pose.ToCamera(target) - travelCamera * heldDistance, out var heldImage));
        Assert.True(pose.TryProject(pose.ToCamera(target) - travelCamera * releasedDistance, out var releasedImage));
        var expectedHeld = View.ToOutput(heldImage.X, heldImage.Y);
        var expectedReleased = View.ToOutput(releasedImage.X, releasedImage.Y);
        Assert.InRange(Distance(heldCentre.X - expectedHeld.X, heldCentre.Y - expectedHeld.Y), 0, 1.0);
        Assert.InRange(Distance(releasedCentre.X - expectedReleased.X, releasedCentre.Y - expectedReleased.Y), 0, 1.0);

        var afterReach = heldSeconds + Style.Reach / Style.FallSpeed;
        Assert.Empty(BuildLive([new ArLivePress(60, afterReach, heldSeconds, false, .9f)]));
    }

    [Fact]
    public void A_lifetime_fade_walks_the_graph_with_hold_and_release_in_3d_too()
    {
        var live = new GraphVariable(Graph: true, Start: 0, Stop: 1, Min: .2, Max: 1);
        var style = Style with { LiveFade = live };
        var f = .5 * Style.FallSpeed / Style.Reach;
        var held = Assert.Single(BuildLive([new ArLivePress(60, .5, .5, true, .9f)], style: style));
        var released = Assert.Single(BuildLive([new ArLivePress(60, .8, .5, false, .9f)], style: style));
        // held: the graph stands at the bar's stretch; released: it walked back by how far the bar has travelled toward its reach
        var stretchBack = .3 * Style.FallSpeed / Style.Reach;
        Assert.Equal(.2 + .8 * f, held.Fade, 9);
        Assert.Equal((.2 + .8 * f * (1 - stretchBack)) * (.65 * .9), released.Fade, 9);
        Assert.True(released.Fade < held.Fade);
    }

    [Fact]
    public void Elevated_billboards_follow_the_saved_3d_pose_and_project_all_four_corners()
    {
        var baseline = Corners.Select(point => (640 + (point.X - 640) * .62, 360 + (point.Y - 360) * .62)).ToArray();
        var transform = new ArPoseTransform(.045, -.025, .015, 29, -23, 17, 1.08);
        Assert.True(ArCameraPose.TryTransformCorners(KeyboardLayout.Standard88, baseline, 1280, 720, 70, transform, out var movedCorners));
        var calibration = ArCalibration.Create(KeyboardLayout.Standard88, movedCorners, 1280, 720, out var problem,
            70, transform, baseline) ?? throw new InvalidOperationException($"transformed corners were refused: {problem}");
        var pose = calibration.CameraPose!.Value;
        var view = new ArView(1280, 720, 1280, 720, PictureFit.Fit, true);
        var key = new ArVec3(pose.KeyboardWidthDepthRatio * .5, .12, 0);
        Assert.True(pose.TryProject(key, view, out var onPlane));
        Assert.True(pose.TryProject(key + new ArVec3(0, 0, -.42), view, out var raised));
        Assert.True(Math.Abs(onPlane.X - raised.X) + Math.Abs(onPlane.Y - raised.Y) > .1,
            "A spark raised along the keyboard normal must use the actual transformed pose, not the flat key homography.");
        Assert.True(Ar3DNoteProjection.TryProjectBillboard(pose, view, key + new ArVec3(0, 0, -.3),
            new ArVec3(1, 0, 0), new ArVec3(0, 0, 1), .018, .03, out var particle));
        AssertFinite(particle);
        Assert.True(Ar3DNoteProjection.TryProjectBillboard(pose, view, key,
            new ArVec3(1, 0, 0), new ArVec3(0, 1, 0), .08, .12, out var pulse));
        AssertFinite(pulse);
        Assert.True(particle.Area > .01 && pulse.Area > particle.Area,
            "The particle billboard and the wider key-plane pulse should retain distinct camera-projected extents.");
    }

    [Fact]
    public void Fov_changes_the_off_plane_3d_projection_but_keeps_the_clicked_keyboard_plane_fixed()
    {
        var narrowFov = MakeCalibration(35).CameraPose!.Value;
        var wideFov = MakeCalibration(110).CameraPose!.Value;
        var ratio = narrowFov.KeyboardWidthDepthRatio;
        Assert.True(narrowFov.TryProject(new ArVec3(0, 0, 0), View, out var firstCorner));
        Assert.True(wideFov.TryProject(new ArVec3(0, 0, 0), View, out var sameCorner));
        Assert.InRange(Math.Abs(firstCorner.X - sameCorner.X), 0, 1e-6);
        Assert.InRange(Math.Abs(firstCorner.Y - sameCorner.Y), 0, 1e-6);
        Assert.True(narrowFov.TryProject(new ArVec3(ratio * .5, -.35, .25), View, out var nearPoint));
        Assert.True(wideFov.TryProject(new ArVec3(ratio * .5, -.35, .25), View, out var farPoint));
        Assert.True(Math.Abs(nearPoint.X - farPoint.X) + Math.Abs(nearPoint.Y - farPoint.Y) > .01,
            "the FOV is part of the saved pose and must affect geometry away from the calibrated plane");
    }
}
