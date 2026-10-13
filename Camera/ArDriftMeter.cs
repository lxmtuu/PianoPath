namespace PianoPath;

/// <summary>What one drift measurement found: offsets are in picture pixels (480p pictures read coarser than 1080p).</summary>
/// <param name="OffsetX">Best horizontal shift of the current key picture against the captured reference.</param>
/// <param name="OffsetY">Best vertical shift of the current key picture against the captured reference.</param>
/// <param name="Residual">Trimmed brightness mismatch at that best shift (0 = identical; exposure changes are levelled first).</param>
/// <param name="ZeroShiftResidual">The same mismatch at zero shift — the improvement over it is what makes a shift mean something.</param>
/// <param name="Usable">False when the picture, the region or the match is too poor to say anything (never raises an alarm).</param>
internal readonly record struct ArDriftEstimate(double OffsetX, double OffsetY, double Residual, double ZeroShiftResidual, bool Usable)
{
    /// <summary>Length of the measured shift.</summary>
    internal double Pixels => Math.Sqrt(OffsetX * OffsetX + OffsetY * OffsetY);

    /// <summary>True when the match is good, shifted is meaningfully better than not, and the shift clears <paramref name="thresholdPixels"/>.</summary>
    internal bool IsDrift(double thresholdPixels) =>
        Usable && Residual <= ArDriftMeter.ResidualGate && ZeroShiftResidual - Residual >= ArDriftMeter.ImprovementGate && Pixels >= thresholdPixels;
}

/// <summary>
/// Pure picture shift measurement between two samplings of the key area: the grid that <see cref="MotionEnergy.Sample"/>
/// writes for the saved key region becomes the reference; each new frame is gridded for the same region grown by a
/// search margin, and a coarse-then-refined scan finds the integer cell shift whose trimmed brightness difference is
/// smallest. Part of "Thêm 2 — đo trôi" (AR roadmap §14.7): when the keys have wandered past a threshold for a few
/// readings the app nags to re-mark the corners; it never moves the calibration on its own.
/// </summary>
internal static class ArDriftMeter
{
    /// <summary>Best match must be at least this clean to mean anything at all (0..1; below it a lighting change looks like a move).</summary>
    internal const double ResidualGate = .12;

    /// <summary>How much the shifted match must beat the not-shifted one before a shift is believed.</summary>
    internal const double ImprovementGate = .035;

    /// <summary>Consecutive drifted readings before a watch raises the alarm (about six seconds at the usual two-second tick).</summary>
    internal const int FlagHoldReadings = 3;

    /// <summary>Search window half-size in pixels, tied to the picture width and kept a multiple of the sampling stride.</summary>
    internal static int MaxShiftPixels(int imageWidth) =>
        Math.Clamp((int)Math.Round(imageWidth / 64.0) / MotionEnergy.Step * MotionEnergy.Step, 12, 24);

    /// <summary>How far the keys may wander before the watch cares: about half a centimetre of a 1280-pixels-wide frame.</summary>
    internal static double ThresholdPixels(int imageWidth) => Math.Clamp(imageWidth * 0.005, 5, 10);

    /// <summary>The region grown by the search margin on every side, fitted to the picture.</summary>
    internal static PixelRegion Expand(PixelRegion region, int marginPixels, int imageWidth, int imageHeight) =>
        new PixelRegion(region.Left - marginPixels, region.Top - marginPixels, region.Right + marginPixels, region.Bottom + marginPixels)
            .Within(imageWidth, imageHeight);

    /// <summary>
    /// Grids <paramref name="picture"/> for <paramref name="region"/> into <paramref name="into"/> (brightening every
    /// <see cref="MotionEnergy.Step"/>th pixel), or returns false without writing when anything does not fit.
    /// </summary>
    internal static bool SampleGrid(byte[] picture, int imageWidth, int imageHeight, PixelRegion region, byte[] into) =>
        MotionEnergy.Sample(picture, imageWidth, imageHeight, region, into);

    /// <summary>
    /// Finds the shift of <paramref name="current"/> against <paramref name="reference"/> in pixels.
    /// Both grids come from <see cref="SampleGrid"/>; the current grid's region is the reference region
    /// <see cref="Expand"/>ed, so every valid shift lands inside it. Cells march with the sampling stride.
    /// </summary>
    internal static ArDriftEstimate Estimate(byte[] reference, PixelRegion referenceRegion, byte[] current, PixelRegion currentRegion, int imageWidth)
    {
        var step = MotionEnergy.Step;
        if (referenceRegion.IsEmpty || currentRegion.IsEmpty || reference.Length == 0 || current.Length == 0) return default;
        var refW = (referenceRegion.Width + step - 1) / step;
        var refH = (referenceRegion.Height + step - 1) / step;
        var curW = (currentRegion.Width + step - 1) / step;
        var curH = (currentRegion.Height + step - 1) / step;
        if (reference.Length < refW * refH || current.Length < curW * curH) return default;
        // the reference top-left cell sits at this cell in the current grid; a margin of zero collapses the search to one answer
        var homeX = (referenceRegion.Left - currentRegion.Left) / step;
        var homeY = (referenceRegion.Top - currentRegion.Top) / step;
        var marginX = Math.Min(homeX, curW - refW - homeX);
        var marginY = Math.Min(homeY, curH - refH - homeY);
        if (marginX < 0 || marginY < 0) return default;

        // exposure is levelled on the means over the inner cells so a sunlit change does not masquerade as a move
        long refSum = 0, curSum = 0;
        var cells = refW * refH;
        for (var y = 0; y < refH; y++)
            for (var x = 0; x < refW; x++)
            {
                refSum += reference[y * refW + x];
                curSum += current[(y + homeY) * curW + x + homeX];
            }
        var bias = (double)(refSum - curSum) / cells;

        double ShiftScore(int cellDx, int cellDy, byte[] scratch)
        {
            var n = 0;
            for (var y = 0; y < refH; y++)
            {
                var crow = (y + homeY + cellDy) * curW + homeX + cellDx;
                var rrow = y * refW;
                for (var x = 0; x < refW; x++)
                    scratch[n++] = (byte)Math.Min(255, (int)Math.Abs(current[crow + x] + bias - reference[rrow + x]));
            }
            Array.Sort(scratch, 0, n);
            double sum = 0;
            var keep = Math.Max(1, (int)(n * .9));      // drop the noisiest decile only: hands and shadows live there, but a real move spreads small diffs over the whole field and must not be trimmed away
            for (var i = 0; i < keep; i++) sum += scratch[i];
            return sum / keep / 255.0;
        }

        var scratch = new byte[cells];
        var zero = ShiftScore(0, 0, scratch);
        var bestDx = 0; var bestDy = 0; var best = zero;
        for (var pass = 0; pass < 2; pass++)
        {
            var stride = pass == 0 ? 2 : 1;
            var radius = pass == 0 ? Math.Max(marginX, marginY) : 1;
            var cx = bestDx; var cy = bestDy;
            for (var dy = cy - radius; dy <= cy + radius; dy += stride)
                for (var dx = cx - radius; dx <= cx + radius; dx += stride)
                    if (Math.Abs(dx) <= marginX && Math.Abs(dy) <= marginY)
                    {
                        var score = ShiftScore(dx, dy, scratch);
                        if (score < best - 1e-6) { best = score; bestDx = dx; bestDy = dy; }
                    }
        }
        return new(bestDx * (double)step, bestDy * (double)step, best, zero, Usable: best <= ResidualGate);
    }
}
