using System;

namespace PianoPath;

/// <summary>
/// The drift watch (roadmap §14.7 "Thêm 2 — đo trôi"): while the AR overlay draws with a calibration, the key
/// area's picture is gridded every few frames and compared against the reference sampled when this calibration was
/// first watched. Three clean readings with the keys beyond a small threshold put a sentence on the AR status line
/// asking for the corners to be marked again; the calibration itself is never touched. Pure measurement lives in
/// <see cref="ArDriftMeter"/> so xUnit and <c>--verify</c> can pin it without a camera.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan DriftMeasureInterval = TimeSpan.FromSeconds(2);

    private readonly object _driftGate = new();
    private byte[] _driftReference = [];          // brightness grid of the base key region, captured once per calibration version
    private byte[] _driftCurrent = [];            // brightness grid of the expanded region, refreshed by the frame tap
    private PixelRegion _driftRegion, _driftExpanded;
    private bool _driftArmed;
    private int _driftVersion = -1;
    private int _driftStreak;
    private string _driftText = "";
    private long _driftFrameSeq, _driftLastMeasuredSeq;
    private long _driftLastMeasureTicks;

    /// <summary>
    /// Called from the camera frame pump, right where <c>PollLatency</c> lives. The guard chain keeps the watch to
    /// exactly one situation: overlay drawing on a live, calibrated camera, with the delay measurement parked.
    /// </summary>
    private void PollDriftWatch()
    {
        var calibration = _arPlan.ArLayer ? _arCalibration?.Current : null;
        var active = calibration is not null && _visualSettings.ArDriftWatch && _camera is { IsLive: true } && !MeasuringArDelay;
        if (!active)
        {
            if (_driftArmed || _driftText.Length > 0) { DisarmDrift(); RefreshArStatus(); }
            return;
        }
        var version = _arCalibration?.Snapshot.Version ?? 0;
        if (version != _driftVersion) { _driftVersion = version; _driftText = ""; ArmDrift(calibration!); }
        // the latency probe owns the frame tap while it measures; the poll hands the tap back to the watch afterwards
        if (_cameraFrames.FrameTap != OnDriftFrame) _cameraFrames.FrameTap = OnDriftFrame;

        var seq = _driftFrameSeq;
        if (seq == _driftLastMeasuredSeq || seq == 0) return;
        var now = DateTime.UtcNow.Ticks;
        if (now - _driftLastMeasureTicks < DriftMeasureInterval.Ticks) return;
        byte[] grid;
        lock (_driftGate) { grid = (byte[])_driftCurrent.Clone(); _driftLastMeasuredSeq = seq; }
        _driftLastMeasureTicks = now;
        var calibrationFrame = calibration!;
        var width = calibrationFrame.ImageWidth;

        if (_driftReference.Length == 0)
        {
            CaptureDriftReference(grid);
            return;
        }
        var estimate = ArDriftMeter.Estimate(_driftReference, _driftRegion, grid, _driftExpanded, width);
        var threshold = ArDriftMeter.ThresholdPixels(width);
        if (estimate.IsDrift(threshold))
        {
            _driftStreak++;
            if (_driftStreak >= ArDriftMeter.FlagHoldReadings)
                _driftText = Loc.F("The keys have drifted about {0} px from the marked frame — open Calibrate and mark the corners again.", (int)Math.Round(estimate.Pixels));
        }
        else
        {
            _driftStreak = 0;
            _driftText = "";
        }
        RefreshArStatus();
    }

    /// <summary>Arms the watch for this calibration: base region from the marked corners, search margin from the picture width.</summary>
    private void ArmDrift(ArCalibration calibration)
    {
        var region = MotionEnergy.KeysRegion(calibration.Corners, calibration.ImageWidth, calibration.ImageHeight);
        var margin = ArDriftMeter.MaxShiftPixels(calibration.ImageWidth);
        var expanded = ArDriftMeter.Expand(region, margin, calibration.ImageWidth, calibration.ImageHeight);
        lock (_driftGate)
        {
            _driftRegion = region;
            _driftExpanded = expanded;
            _driftReference = [];
            _driftCurrent = expanded.IsEmpty ? [] : new byte[MotionEnergy.SampleCount(expanded)];
            _driftStreak = 0;
            _driftFrameSeq = 0;
            _driftLastMeasuredSeq = 0;
            _driftArmed = !region.IsEmpty && _driftCurrent.Length > 0;
        }
    }

    private void DisarmDrift()
    {
        lock (_driftGate)
        {
            _driftArmed = false;
            _driftReference = [];
            _driftCurrent = [];
            _driftStreak = 0;
            _driftFrameSeq = 0;
            _driftLastMeasuredSeq = 0;
        }
        _driftText = "";
    }

    /// <summary>The frame tap keeps every twelfth frame as the newest sampling; it runs on the camera thread and never holds a buffer.</summary>
    private void OnDriftFrame(CameraFrameView view)
    {
        if (!_driftArmed || view.Sequence % 12 != 0) return;
        lock (_driftGate)
        {
            if (!_driftArmed || _driftCurrent.Length == 0) return;
            if (ArDriftMeter.SampleGrid(view.Pixels, view.Width, view.Height, _driftExpanded, _driftCurrent))
                _driftFrameSeq = view.Sequence;
        }
    }

    /// <summary>The first sampling after (re-)arming becomes the reference: the base region's cells are lifted out of the expanded grid.</summary>
    private void CaptureDriftReference(byte[] expandedGrid)
    {
        var step = MotionEnergy.Step;
        var refW = (_driftRegion.Width + step - 1) / step;
        var refH = (_driftRegion.Height + step - 1) / step;
        var curW = (_driftExpanded.Width + step - 1) / step;
        var homeY = (_driftRegion.Top - _driftExpanded.Top) / step;
        var homeX = (_driftRegion.Left - _driftExpanded.Left) / step;
        var reference = new byte[refW * refH];
        for (var y = 0; y < refH; y++)
            Array.Copy(expandedGrid, ((homeY + y) * curW + homeX), reference, y * refW, refW);
        _driftReference = reference;
    }
}
