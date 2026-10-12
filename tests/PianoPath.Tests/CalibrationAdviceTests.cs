using System;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the calibration window's live framing advice (Camera/CalibrationAdvice.cs): a real skin tone inside the marked
/// keys must fire the warning, white keys / black keys / piano wood must never count as skin, and the quad form of the
/// check watches only what is inside the four corners.
/// </summary>
public class CalibrationAdviceTests
{
    private const int W = 320, H = 180;

    /// <summary>A quiet stage: white keys along the bottom, dark wood above, no hands.</summary>
    private static byte[] KeyboardPicture()
    {
        var px = new byte[W * H * 4];
        var rng = new Random(4);
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                var lum = y > 120 ? (200 + rng.Next(30)) : (34 + rng.Next(18));   // keys bright, wood dark
                if (y > 120 && x % 40 > 32) lum = 24;                            // black keys
                px[i] = px[i + 1] = px[i + 2] = (byte)lum; px[i + 3] = 255;
            }
        return px;
    }

    /// <summary>Paints a fist-sized skin blob (a believable human tone) at the centre of a rect.</summary>
    private static void PaintHand(byte[] px, int cx, int cy, int radius, byte r = 226, byte g = 174, byte b = 140)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= W || y >= H || (x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
                var i = (y * W + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
            }
    }

    [Fact]
    public void AQuietKeyboardRaisesNoHandWarning()
    {
        var (warn, share) = CalibrationAdvice.CheckPicture(KeyboardPicture(), W, H, null);
        Assert.False(warn, $"a bright-key picture must not read as skin (share {share:0.000})");
        Assert.True(share < CalibrationAdvice.SkinWarningFraction);
    }

    [Fact]
    public void AHandInFrameFiresWhenNoCornersAreDown()
    {
        var px = KeyboardPicture();
        PaintHand(px, W / 2, H / 2, 42);
        var (warn, share) = CalibrationAdvice.CheckPicture(px, W, H, null);
        Assert.True(warn, $"a palm in frame must fire (share {share:0.000})");
    }

    [Fact]
    public void WithCornersDownOnlySkinInsideTheQuadCounts()
    {
        (double X, double Y)[] quad = [(40, 100), (280, 100), (290, 170), (30, 170)];
        var inside = KeyboardPicture();
        PaintHand(inside, 160, 135, 30);                              // inside the quad: the warning must fire
        var (warnInside, _) = CalibrationAdvice.CheckPicture(inside, W, H, quad);
        Assert.True(warnInside);
        var outside = KeyboardPicture();
        PaintHand(outside, 30, 30, 30);                               // a waving arm beside the keys: nobody's business
        var (warnOutside, shareOutside) = CalibrationAdvice.CheckPicture(outside, W, H, quad);
        Assert.False(warnOutside, $"skin outside the quad is not the key area (share {shareOutside:0.000})");
    }

    [Fact]
    public void PianoWoodAndSkinAreTellableApart()
    {
        var px = KeyboardPicture();
        // paint the upper half a dark walnut case — the lobe plus the brightness floor must keep it silent
        for (var y = 0; y < 120; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                px[i] = 30; px[i + 1] = 40; px[i + 2] = 60; px[i + 3] = 255;
            }
        var (warn, share) = CalibrationAdvice.CheckPicture(px, W, H, null);
        Assert.False(warn, $"dark walnut must stay under the alarm (share {share:0.000})");
        // and one acknowledged human tone passes the lobe while walnut does not
        Assert.True(CalibrationAdvice.IsSkin(new byte[] { 140, 174, 226, 255 }, 0));
        Assert.False(CalibrationAdvice.IsSkin(new byte[] { 30, 40, 60, 255 }, 0));
    }
}
