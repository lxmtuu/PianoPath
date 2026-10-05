using System.Diagnostics;
using System.Windows;

namespace PianoPath;

/// <summary>Measuring how late the camera's picture is, from the player's own key presses.</summary>
public partial class MainWindow
{
    private readonly LatencyMeasurement _latency = new(Stopwatch.Frequency);
    /// <summary>The words about the last run, shown on the AR card until the next one or until the camera changes.</summary>
    private string _latencyText = "";
    private int _latencyCalibrationVersion;

    internal bool MeasuringArDelay => _latency.State == MeasurementState.Collecting;

    private void MeasureArDelay_Click(object sender, RoutedEventArgs e) => MeasureArDelay();

    /// <summary>
    /// The button: starts a run, or ends the run that is going with what it has. A run needs the overlay drawing on marked keys (so the
    /// picture of the keys is known and the app's own share of the delay is being counted), a live camera, and a MIDI keyboard to
    /// time the presses on.
    /// </summary>
    internal void MeasureArDelay()
    {
        if (MeasuringArDelay) { FinishLatency(_latency.Finish(CurrentCameraAge())); return; }
        var calibration = _arPlan.ArLayer ? _arCalibration?.Current : null;
        if (calibration is null) { ShowMessage(Loc.T("Measuring the delay needs the AR overlay on and the keys marked, so the picture of the keys can be watched."), "Measure the camera delay", MessageBoxImage.Information); return; }
        if (_camera is not { IsLive: true }) { ShowMessage(Loc.T("The delay can only be measured on a live camera, not on a video file."), "Measure the camera delay", MessageBoxImage.Information); return; }
        if (!_midi.InputOpen) { ShowMessage(Loc.T("Connect a MIDI keyboard first: the measurement times your key presses against the camera's picture."), "Measure the camera delay", MessageBoxImage.Information); return; }

        // The frames are the picture as the camera delivers it, which is the picture the corners were marked on, unless the camera thread
        // is mirroring them (no overlay is drawing then, so this is only for safety).
        var corners = calibration.Corners;
        if (_visualSettings.CameraMirror && !_arRawFrames) corners = CalibrationQuad.Reflect(corners, calibration.ImageWidth, calibration.ImageHeight, flipX: true, flipY: false);
        var region = MotionEnergy.KeysRegion(corners, calibration.ImageWidth, calibration.ImageHeight);
        if (region.IsEmpty) return;
        _latencyCalibrationVersion = _arCalibration?.Snapshot.Version ?? 0;
        _latencyText = "";
        _latency.Start(region, Stopwatch.GetTimestamp());
        _cameraFrames.FrameTap = _latency.AddFrame;
        RefreshArStatus();
    }

    private CameraAge CurrentCameraAge() => _gpuLoop?.CameraAge ?? default;

    /// <summary>
    /// Called from the camera timer: finishes a run that has enough or has waited too long, and gives up on one whose camera or key area
    /// has changed under it.
    /// </summary>
    private void PollLatency()
    {
        if (!MeasuringArDelay) return;
        if ((_arCalibration?.Snapshot.Version ?? 0) != _latencyCalibrationVersion || !_arPlan.ArLayer)
        {
            CancelLatency(Loc.T("The measurement stopped because the camera or the marked keys changed. Start it again."));
            return;
        }
        if (_latency.Poll(Stopwatch.GetTimestamp(), CurrentCameraAge()) is { } report) FinishLatency(report);
        else RefreshArStatus();
    }

    private void CancelLatency(string message)
    {
        _cameraFrames.FrameTap = null;
        _latency.Cancel();
        _latencyText = message;
        RefreshArStatus();
    }

    /// <summary>Sets the camera delay from a measured run and says what happened; a run that found nothing changes no setting.</summary>
    private void FinishLatency(LatencyReport? report)
    {
        _cameraFrames.FrameTap = null;
        if (report is null) { RefreshArStatus(); return; }
        _latencyText = LatencyText.Result(report);
        if (report.Estimate.IsMeasured) SetArDelay(report.SuggestedMilliseconds);
        RefreshArStatus();
    }

    /// <summary>Moves the Camera delay slider, which does what a drag of it does (settings, history, the engine).</summary>
    internal void SetArDelay(int milliseconds)
    {
        if (_visualSliders.TryGetValue(nameof(PianoVisualSettings.ArDelay), out var slider)) slider.Value = Math.Clamp(milliseconds, slider.Minimum, slider.Maximum);
        else { _visualSettings.ArDelay = milliseconds; ApplyVisualSettings("Camera delay set to {0} ms", false, milliseconds); }
        UpdateArFeed();
    }
}
