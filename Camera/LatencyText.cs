namespace PianoPath;

/// <summary>The words about measuring the camera's delay, for the AR card's status line.</summary>
internal static class LatencyText
{
    /// <summary>While a run is going: what to do and how far it has got.</summary>
    internal static string Collecting(int presses) =>
        Loc.F("Measuring the camera delay: press keys on your keyboard, one at a time, with your hands in view of the camera. {0} of {1}. Press MEASURE DELAY again to stop.",
            Math.Min(presses, LatencyMeasurement.TargetPresses), LatencyMeasurement.TargetPresses);

    /// <summary>What a finished run found, and what was done or what to try again.</summary>
    internal static string Result(LatencyReport report) => report.Estimate.Outcome switch
    {
        LatencyOutcome.Measured when report.Clamped => Loc.F("Camera delay measured: the picture arrives {0} ms after the key, and the app holds it {1} ms. That is more than the setting holds, so the camera delay is now {2} ms, its largest.",
            Rounded(report.Estimate.DelaySeconds), Rounded(report.AppAgeMilliseconds / 1000), report.SuggestedMilliseconds),
        LatencyOutcome.Measured => Loc.F("Camera delay measured: the picture arrives {0} ms after the key, and the app holds it {1} ms. The camera delay is now {2} ms.",
            Rounded(report.Estimate.DelaySeconds), Rounded(report.AppAgeMilliseconds / 1000), report.SuggestedMilliseconds),
        LatencyOutcome.NotEnoughNotes => Loc.F("Too few keys were pressed to measure. Press about {0} keys, one at a time, and try again.", LatencyMeasurement.TargetPresses),
        LatencyOutcome.NotEnoughPicture => Loc.T("The camera's picture did not come in for long enough to measure. Check the camera and try again."),
        LatencyOutcome.NoMotion => Loc.T("The picture did not move when the keys were pressed. Make sure the camera sees your hands and the keys, and that the light is steady, then try again."),
        _ => Loc.T("The picture did not follow the keys clearly enough to tell. Press the keys slowly and one at a time, with your hands in view and nothing else moving, and try again."),
    };

    /// <summary>Seconds as whole milliseconds.</summary>
    internal static int Rounded(double seconds) => double.IsFinite(seconds) ? (int)Math.Round(Math.Max(0, seconds) * 1000) : 0;
}
