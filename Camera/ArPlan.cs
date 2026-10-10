namespace PianoPath;

/// <summary>What the overlay is doing, in the order the player has to fix things.</summary>
internal enum ArStatus
{
    /// <summary>The switch is off.</summary>
    Off,
    /// <summary>The overlay is drawn by the Direct3D engine only; the software stage cannot do it.</summary>
    NeedsGpu,
    /// <summary>The stage has a green-screen background, which takes the camera's place.</summary>
    ChromaBackground,
    /// <summary>The camera is being opened, or could not be.</summary>
    NoCamera,
    /// <summary>The camera shows, but the key area has never been marked for this camera, picture size and keyboard size.</summary>
    NeedsCalibration,
    /// <summary>The key area was marked at another picture size or keyboard size.</summary>
    Outdated,
    /// <summary>The camera is the background and the notes and keys are drawn over it.</summary>
    Active,
}

/// <summary>
/// What to do with the stage for the overlay's switch, the engine, the camera and the calibration as they are right now.
/// Every combination has one answer here, so the window only carries it out.
/// </summary>
/// <param name="Status">Where the overlay stands.</param>
/// <param name="NeedsCamera">The overlay wants the camera open, whatever else does.</param>
/// <param name="RawFrames">The camera is read as it comes, not mirrored, and the engine takes the frames: the calibration is kept for the picture as delivered, and the engine turns it round when it draws.</param>
/// <param name="CameraLayer">The engine shows the camera as the background.</param>
/// <param name="ArLayer">The engine draws the notes and keys over the camera, on the calibrated key area.</param>
/// <param name="HideStageLayers">Uncalibrated stage-space notes, key bodies and sprites are not drawn over the camera; pitch-tagged Live Stage effects can still be projected by the calibrated AR layer.</param>
internal readonly record struct ArPlan(ArStatus Status, bool NeedsCamera, bool RawFrames, bool CameraLayer, bool ArLayer, bool HideStageLayers)
{
    internal static readonly ArPlan Off = new(ArStatus.Off, false, false, false, false, false);

    /// <param name="options">What the player chose.</param>
    /// <param name="gpuActive">The Direct3D engine is running and has no error.</param>
    /// <param name="sourceOpen">A camera or a video file is open (and so a calibration is being looked for).</param>
    /// <param name="snapshot">The calibration session now.</param>
    /// <param name="chromaBackground">The stage background is the green screen.</param>
    internal static ArPlan Decide(ArOptions options, bool gpuActive, bool sourceOpen, ArCalibrationSnapshot snapshot, bool chromaBackground)
    {
        if (!options.Enabled) return Off;
        if (!gpuActive) return Off with { Status = ArStatus.NeedsGpu };
        if (chromaBackground) return Off with { Status = ArStatus.ChromaBackground };
        if (!sourceOpen || snapshot.State == ArCalibrationState.NoSource)
            return new ArPlan(ArStatus.NoCamera, true, true, false, false, false);
        return snapshot.State switch
        {
            ArCalibrationState.Ready when snapshot.Calibration is not null => new ArPlan(ArStatus.Active, true, true, true, true, true),
            ArCalibrationState.Outdated => new ArPlan(ArStatus.Outdated, true, true, true, false, false),
            _ => new ArPlan(ArStatus.NeedsCalibration, true, true, true, false, false),
        };
    }
}
