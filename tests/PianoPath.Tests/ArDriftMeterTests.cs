using System;
using System.Collections.Generic;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the pure picture-shift measurement behind the drift watch (Camera/ArDriftMeter.cs): a moved
/// picture must be measured to within a cell, a lighting change and a passing hand must never raise the alarm.
/// Frames are painted synthetically — a striped trapezoid standing in for the row of keys at a known offset —
/// so no camera and no PNG decoder is involved.
/// </summary>
public class ArDriftMeterTests
{
    private const int W = 640, H = 360;

    // corner order follows the calibration: back-left, back-right, front-right, front-left (the synthetic 88-key quad halved)
    private static readonly (double X, double Y)[] Corners = [(35, 146), (607, 134), (618, 211), (26, 224)];

    /// <summary>Paints a BGRA frame of a striped trapezoid (the keys) moved by <paramref name="shiftX"/>/<paramref name="shiftY"/> pixels.</summary>
    private static byte[] Paint(double shiftX, double shiftY, double brightness = 1, double blobFraction = 0, int seed = 7)
    {
        var px = new byte[W * H * 4];
        var rng = new Random(seed);
        for (var y = 0; y < H; y++)
        {
            // the trapezoid's left/right border at this row, interpolated between the back and the front edge
            double tBack = Corners[0].Y, tFront = Corners[3].Y;
            if (y < tBack + shiftY - 1 || y > tFront + shiftY + 1) continue;
            double tv = Math.Clamp((y - (tBack + shiftY)) / (tFront - tBack), 0, 1);
            var xa = Corners[0].X + (Corners[3].X - Corners[0].X) * tv + shiftX;
            var xb = Corners[1].X + (Corners[2].X - Corners[1].X) * tv + shiftX;
            for (var x = (int)Math.Ceiling(xa); x < Math.Min(W, xb); x++)
            {
                var u = (x - xa) / (xb - xa);
                var lum = (int)Math.Floor(u * 26) % 2 == 0 ? 232 : 56;   // 45 px bands: unique across the search window, so a move cannot alias onto the next stripe
                if (u > .3 && u < .72 && tv < .6) lum = Math.Min(lum, 44);   // a dark middle band: the black-key half
                lum = (int)Math.Clamp(lum * brightness + 6 + (rng.NextDouble() - .5) * 6, 0, 255);
                var i = (y * W + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)lum; px[i + 3] = 255;
            }
        }
        if (blobFraction > 0)
        {
            // a roaming dark hand covering a share of the picture: robust to it means the trimmed mean ignores it
            var bw = (int)(W * blobFraction * 2);
            for (var y = 180; y < Math.Min(H, 180 + bw / 2); y++)
                for (var x = 300; x < Math.Min(W, 300 + bw); x++)
                {
                    var i = (y * W + x) * 4;
                    px[i] = px[i + 1] = px[i + 2] = (byte)Math.Min((int)px[i], 36);
                }
        }
        return px;
    }

    private static (byte[] Ref, PixelRegion Region, int Margin) Reference(byte[] picture)
    {
        var region = MotionEnergy.KeysRegion(Corners, W, H);
        var margin = ArDriftMeter.MaxShiftPixels(W);
        var into = new byte[MotionEnergy.SampleCount(region)];
        Assert.True(ArDriftMeter.SampleGrid(picture, W, H, region, into));
        return (into, region, margin);
    }

    private static ArDriftEstimate Measure((byte[] Ref, PixelRegion Region, int Margin) reference, double shiftX, double shiftY, double brightness = 1, double blob = 0)
    {
        var expanded = ArDriftMeter.Expand(reference.Region, reference.Margin, W, H);
        var picture = Paint(shiftX, shiftY, brightness, blob, seed: 9);
        var grid = new byte[MotionEnergy.SampleCount(expanded)];
        Assert.True(ArDriftMeter.SampleGrid(picture, W, H, expanded, grid));
        return ArDriftMeter.Estimate(reference.Ref, reference.Region, grid, expanded, W);
    }

    [Fact]
    public void UnmovedPictureReadsNoDrift()
    {
        var reference = Reference(Paint(0, 0, seed: 9));
        var estimate = Measure(reference, 0, 0);
        Assert.True(estimate.Usable, $"The reference picture should match itself cleanly (residual {estimate.Residual:0.###}).");
        Assert.False(estimate.IsDrift(ArDriftMeter.ThresholdPixels(W)), "A still picture must never raise the alarm.");
        Assert.True(estimate.Pixels <= 1.5, $"No shift should be found (got {estimate.OffsetX:0.#}, {estimate.OffsetY:0.#}).");
    }

    [Fact]
    public void MovedPictureReadsTheShiftInPixels()
    {
        var reference = Reference(Paint(0, 0, seed: 9));
        var estimate = Measure(reference, 9, 3);
        Assert.True(estimate.Usable && estimate.IsDrift(ArDriftMeter.ThresholdPixels(W)),
            $"A 9-pixel move must be flagged (got {estimate.OffsetX:0.#}, {estimate.OffsetY:0.#}, residual {estimate.Residual:0.###}).");
        Assert.True(Math.Abs(estimate.OffsetX - 9) <= 3 && Math.Abs(estimate.OffsetY - 3) <= 3,
            $"The measured shift should land within a sampling cell of the truth ({estimate.OffsetX:0.#}, {estimate.OffsetY:0.#}).");
    }

    [Fact]
    public void BrighterOrDarkerPictureIsNotDrift()
    {
        var reference = Reference(Paint(0, 0, seed: 9));
        var bright = Measure(reference, 0, 0, brightness: 1.18);
        var dim = Measure(reference, 0, 0, brightness: .82);
        Assert.True(bright.Usable, $"Exposure is levelled, so brightness alone must not fool the match ({bright.Residual:0.###}).");
        Assert.False(bright.IsDrift(ArDriftMeter.ThresholdPixels(W)) || dim.IsDrift(ArDriftMeter.ThresholdPixels(W)),
            "A lighting change must not look like a moved tripod.");
    }

    [Fact]
    public void AHandSweepingThroughIsNotDrift()
    {
        var reference = Reference(Paint(0, 0, seed: 9));
        var estimate = Measure(reference, 0, 0, blob: .12);
        Assert.False(estimate.IsDrift(ArDriftMeter.ThresholdPixels(W)),
            $"Playing hands must not raise the alarm (residual {estimate.Residual:0.###}, shift {estimate.Pixels:0.#} px).");
    }

    [Fact]
    public void AShiftBelowTheThresholdStaysQuietButIsStillMeasured()
    {
        var reference = Reference(Paint(0, 0, seed: 9));
        var estimate = Measure(reference, 3, 0);
        Assert.False(estimate.IsDrift(ArDriftMeter.ThresholdPixels(W)),
            $"A 3-pixel creep is below the watch's threshold ({estimate.Pixels:0.#} px).");
        Assert.True(estimate.ZeroShiftResidual > estimate.Residual, "Even a small true shift should beat zero on the matcher, so the alarm only waits on the threshold.");
    }

    [Fact]
    public void EmptyRegionsAndMismatchedGridsAreSimplyNotUsable()
    {
        Assert.False(default(ArDriftEstimate).Usable);
        var empty = ArDriftMeter.Estimate([], new PixelRegion(0, 0, 0, 0), [], new PixelRegion(0, 0, 0, 0), W);
        Assert.False(empty.Usable);
        Assert.Empty(new byte[MotionEnergy.SampleCount(default(PixelRegion))]);
    }
}
