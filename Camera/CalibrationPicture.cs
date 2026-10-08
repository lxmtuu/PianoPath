namespace PianoPath;

/// <summary>
/// A still picture from the camera for the calibration window: exactly <c>Width × Height × 4</c> bytes of top-down BGRA, always
/// the picture as the camera delivered it — never turned round — because the corners are kept for that picture and the overlay
/// turns it round at display time.
/// </summary>
internal sealed class CalibrationPicture
{
    internal CalibrationPicture(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("The pixels do not match the size of the picture.", nameof(bgra));
        Bgra = bgra; Width = width; Height = height;
    }

    internal byte[] Bgra { get; }
    internal int Width { get; }
    internal int Height { get; }

    /// <summary>
    /// Takes a copy of the newest frame of the ring without disturbing whoever draws from it. <paramref name="framesAreMirrored"/> says
    /// whether the camera thread turned the frames round on their way into the ring (it does when the overlay is not the one
    /// drawing them), in which case they are turned back. Null when the ring has no frame.
    /// </summary>
    internal static CalibrationPicture? Capture(CameraFrameRing ring, bool framesAreMirrored)
    {
        if (!ring.TryPeek(out var frame)) return null;
        if (framesAreMirrored) FlipRows(frame.Pixels, frame.Width, frame.Height);
        return new CalibrationPicture(frame.Pixels, frame.Width, frame.Height);
    }

    /// <summary>Turns a BGRA picture round left to right, in place.</summary>
    internal static void FlipRows(byte[] bgra, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (int left = 0, right = width - 1; left < right; left++, right--)
                for (var c = 0; c < 4; c++)
                    (bgra[row + left * 4 + c], bgra[row + right * 4 + c]) = (bgra[row + right * 4 + c], bgra[row + left * 4 + c]);
        }
    }
}

/// <summary>What stands in the way of opening the calibration window, and whether a result may still be saved.</summary>
internal enum CalibrationLaunch
{
    Ready,
    /// <summary>No camera or video is open, so there is no picture size to mark corners for.</summary>
    NoCamera,
    /// <summary>The camera is open but has not delivered a frame yet.</summary>
    NoPicture,
    /// <summary>The frame is not the size the overlay reads, so corners marked on it would not fit.</summary>
    SizeMismatch,
}

internal static class CalibrationLaunchRules
{
    /// <summary>Whether the window can open on this picture for the session as it stands.</summary>
    internal static CalibrationLaunch Check(ArCalibrationSnapshot snapshot, CalibrationPicture? picture)
    {
        if (snapshot.State == ArCalibrationState.NoSource) return CalibrationLaunch.NoCamera;
        if (picture is null) return CalibrationLaunch.NoPicture;
        return picture.Width == snapshot.ImageWidth && picture.Height == snapshot.ImageHeight ? CalibrationLaunch.Ready : CalibrationLaunch.SizeMismatch;
    }

    /// <summary>
    /// True when the session is still about the camera, the view of it, the picture size and the keyboard it was when the
    /// window opened. The window is modal but the camera is not: it can be unplugged or reopened at another size behind it,
    /// and corners marked for the old picture — or for the view the camera stood at then — must not be saved for the new one.
    /// Resetting or saving in between changes only the calibration, not what is open.
    /// </summary>
    internal static bool SameSource(ArCalibrationSnapshot before, ArCalibrationSnapshot after) =>
        after.State != ArCalibrationState.NoSource && string.Equals(before.Source, after.Source, StringComparison.OrdinalIgnoreCase)
        && string.Equals(before.Profile.Id, after.Profile.Id, StringComparison.OrdinalIgnoreCase)
        && before.ImageWidth == after.ImageWidth && before.ImageHeight == after.ImageHeight && before.KeyCount == after.KeyCount;
}
