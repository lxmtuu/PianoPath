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

    private const string AdjustStep = "The grid shows where the keys would be. Drag a corner, or use Rotate key grid to turn all four together, then drag corners for fine tuning. Tab and the arrow keys also nudge a selected corner (Shift for bigger steps). Then press Done.";

    internal const string RotationTitle = "Rotate key grid";
    internal const string RotationTip = "After the four corners are placed, rotate the whole keyboard grid around its centre. Positive values turn clockwise. The note path follows this calibration, so notes still meet the correct keys.";
    internal const string RotationOutOfBounds = "That rotation would move a corner too far outside the picture.";

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

    /// <summary>Every sentence the window can show, for the check that they all exist in the string table.</summary>
    internal static IEnumerable<string> All() =>
    [
        .. CornerSteps, AdjustStep, OrientationTitle, OrientationTip, RotationTitle, RotationTip, RotationOutOfBounds,
        .. Enum.GetValues<CalibrationProblem>().Where(p => p != CalibrationProblem.None).Select(ForProblem),
        .. Enum.GetValues<KeyboardOrientation>().Select(ForOrientation),
    ];
}
