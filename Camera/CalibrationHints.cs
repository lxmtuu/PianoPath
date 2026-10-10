namespace PianoPath;

/// <summary>
/// What the calibration window says, as keys of the string table (the window looks them up). Kept apart from the window so a
/// test can check that every step and every problem has a sentence, and that each sentence is in both languages.
/// </summary>
internal static class CalibrationHints
{
    /// <summary>The sentence for each corner to click, in the order of <see cref="CalibrationEditor.CornerNames"/>.</summary>
    private static readonly string[] CornerSteps =
    [
        "Step 1 of 4: click the back-left corner of the keys: the lowest key, at the far edge where the black keys start.",
        "Step 2 of 4: click the back-right corner: the highest key, at the same far edge.",
        "Step 3 of 4: click the front-right corner: the highest key, at the near edge, closest to you.",
        "Step 4 of 4: click the front-left corner: the lowest key, at the near edge, closest to you.",
    ];

    private const string AdjustStep = "The grid shows the keys. Use the Unity-style gizmo or sliders to move, rotate and uniformly scale the keyboard in 3D; drag corners for fine tuning. Aim the separate note-travel arrow on the preview, or use the tilt and azimuth controls. Tab and the arrow keys nudge one selected corner. Then press Done.";

    internal const string RotationTitle = "3D keyboard pose";
    internal const string RotationTip = "These controls rotate the keyboard surface in 3D around its centre. Pitch, yaw and roll are independent of note travel; camera FOV adjusts the perspective estimate. The key corners remain the calibration anchor.";
    internal const string RotationOutOfBounds = "That 3D pose would move a corner too far outside the picture or behind the camera.";
    internal const string CameraFovTitle = "Camera FOV";
    internal const string CameraFovTip = "Estimated horizontal camera field of view. Adjust it if perspective depth looks wrong; it is saved with this calibration.";
    internal const string PitchTitle = "Pitch";
    internal const string YawTitle = "Yaw";
    internal const string RollTitle = "Roll";
    internal const string TransformTitle = "Object transform";
    internal const string TransformTip = "Move, rotate and uniformly scale the complete virtual keyboard in 3D. Translation uses keyboard-local units; scale preserves the key proportions. These controls never change note travel.";
    internal const string ScaleTitle = "Scale";
    internal const string ToolTitle = "Gizmo";
    internal const string ToolTip = "Choose corner editing or a Unity-style Move, Rotate, Scale or note-direction gizmo. Drag its handles on the camera preview.";
    internal const string NoteDirectionTitle = "Note travel";
    internal const string NoteDirectionTip = "Note travel is independent of the keyboard transform. Drag its yellow preview arrow to aim within the keyboard plane, or set its 3D tilt and azimuth. The resulting direction is saved with stage settings.";
    internal const string TiltTitle = "Tilt";
    internal const string AzimuthTitle = "Azimuth";
    internal const string ScreenUpTitle = "Aim screen-up";
    internal const string ScreenUpTip = "Set note travel to the 3D direction in the keyboard plane that projects straight up in this camera preview.";
    internal const string KeyDepthTitle = "Along key depth";
    internal const string KeyDepthTip = "Set note travel along the keyboard's back-to-front depth axis, perpendicular to the row of keys.";

    /// <summary>The line above the picture for the step the wizard is at.</summary>
    internal static string ForStep(CalibrationWizard wizard) => wizard.Step switch
    {
        WizardStep.PlaceCorner => CornerSteps[Math.Clamp(wizard.Points.Count, 0, CornerSteps.Length - 1)],
        WizardStep.Adjust => AdjustStep,
        _ => ForProblem(wizard.Problem),
    };

    /// <summary>The sentence for a verdict of <see cref="CalibrationQuad.Check"/> other than "fine".</summary>
    internal static string ForProblem(CalibrationProblem problem) => problem switch
    {
        CalibrationProblem.NotFinite => "A corner has no position in the picture. Press Start over.",
        CalibrationProblem.OutsideImage => "A corner is too far outside the picture. Drag it back toward the keys.",
        CalibrationProblem.WrongOrder => "The corners are in the wrong order. Press Start over and click them as asked.",
        CalibrationProblem.NotConvex => "The outline folds over itself. Check that each corner sits on the matching corner of the keys.",
        CalibrationProblem.Degenerate => "Two corners are on top of each other, or three are in a line. Drag them apart.",
        CalibrationProblem.TooSmall => "The marked area is too small to be a keyboard. Mark the whole row of keys.",
        CalibrationProblem.TooThin => "The marked area is too shallow. Mark from the far edge of the keys to the near edge.",
        CalibrationProblem.TooSkewed => "One long edge is far longer than the other. A camera looking more straight down at the keys works better.",
        _ => "Four corners are needed.",
    };

    private static readonly string[] Names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    /// <summary>The name of a key in the usual way, middle C being C4: what the window writes under the lowest and the highest key of the grid.</summary>
    internal static string KeyName(int pitch) => $"{Names[((pitch % 12) + 12) % 12]}{(int)Math.Floor(pitch / 12.0) - 1}";

    /// <summary>The label of the orientation choice, and what it is for.</summary>
    internal const string OrientationTitle = "Keyboard in the picture";
    internal const string OrientationTip = "Choose how the keyboard looks in the picture. A camera above the keys, behind you, sees the low notes on the left and the far edge at the top. A camera on the far side of the keyboard, looking at you, sees it the other way round.";

    /// <summary>The words for one choice of orientation.</summary>
    internal static string ForOrientation(KeyboardOrientation orientation) => orientation switch
    {
        KeyboardOrientation.LowRight => "Low notes on the right, far edge at the top",
        KeyboardOrientation.FarBottom => "Low notes on the left, far edge at the bottom",
        KeyboardOrientation.LowRightFarBottom => "Low notes on the right, far edge at the bottom",
        _ => "Low notes on the left, far edge at the top",
    };

    internal static string ForTool(CalibrationTool tool) => tool switch
    {
        CalibrationTool.Move => "Move",
        CalibrationTool.Rotate => "Rotate",
        CalibrationTool.Scale => "Scale",
        CalibrationTool.NoteDirection => "Note direction",
        _ => "Corner points",
    };

    /// <summary>Every sentence the window can show, for the check that they all exist in the string table.</summary>
    internal static IEnumerable<string> All() =>
    [
        .. CornerSteps, AdjustStep, OrientationTitle, OrientationTip, RotationTitle, RotationTip, RotationOutOfBounds,
        CameraFovTitle, CameraFovTip, PitchTitle, YawTitle, RollTitle, TransformTitle, TransformTip, ScaleTitle,
        ToolTitle, ToolTip, NoteDirectionTitle, NoteDirectionTip, TiltTitle, AzimuthTitle, ScreenUpTitle, ScreenUpTip,
        KeyDepthTitle, KeyDepthTip,
        .. Enum.GetValues<CalibrationTool>().Select(ForTool),
        .. Enum.GetValues<CalibrationProblem>().Where(p => p != CalibrationProblem.None).Select(ForProblem),
        .. Enum.GetValues<KeyboardOrientation>().Select(ForOrientation),
    ];
}
