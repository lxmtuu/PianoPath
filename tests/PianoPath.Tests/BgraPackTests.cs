using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the pixel packing of the render-to-video pipeline (Video/BgraPack.cs): the bottom-up, stride-aligned
/// BGR rows an AVI frame is written from, and the 2×2 box average that downsamples the output.
/// </summary>
public class BgraPackTests
{
    [Fact]
    public void AnAviFrameRunsBottomUpWithZeroPaddedRows()
    {
        // A 2×2 BGRA picture: blue top-left, red top-right, green bottom-left, white bottom-right.
        var bgra = new byte[]
        {
            255, 0, 0, 255, /* blue  */  0, 0, 255, 255, /* red */
            0, 255, 0, 255, /* green */  255, 255, 255, 255,
        };
        var frame = BgraPack.ToBottomUpBgr(bgra, 2, 2);
        var stride = BgraPack.AviStride(2);                  // 2 px × 3 = 6 bytes, padded to 8
        Assert.Equal(8, stride);
        Assert.Equal(stride * 2, frame.Length);
        // First stored row = the source's last row (bottom-up):
        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 255, 0, 0 }, frame[..stride]);
        // Second stored row = the source's first row:
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255, 0, 0 }, frame[stride..]);
        Assert.Equal(0, frame[stride + 6]);                  // padding stays zero, so two runs match byte for byte
    }

    [Fact]
    public void ABoxAveragesEveryTwoByTwoPatchIncludingAlpha()
    {
        // 2×2 of quarter values must land on the exact rounded mean: (40+60+80+100+2)/4 = 70.
        var bgra = new byte[]
        {
            40, 40, 40, 100,   60, 60, 60, 200,
            80, 80, 80, 150,   100, 100, 100, 250,
        };
        var half = BgraPack.Box2(bgra, 2, 2);
        Assert.Equal(4, half.Length);
        Assert.Equal(new byte[] { 70, 70, 70, 175 }, half);  // alpha (100+200+150+250+2)/4 = 175
        // A solid frame boxes into itself: a camera picture stays recognisable at half size.
        var solid = Enumerable.Repeat((byte)92, 4 * 4 * 4).ToArray();
        Assert.Equal(solid.Length / 4, BgraPack.Box2(solid, 4, 4).Length);
        Assert.All(BgraPack.Box2(solid, 4, 4), b => Assert.Equal(92, b));
    }

    [Fact]
    public void PackingRefusesFramesItCannotWalk()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BgraPack.Box2(new byte[12], 3, 2)); // 3 px wide: no even split
        Assert.Throws<ArgumentOutOfRangeException>(() => BgraPack.Box2(new byte[8], 1, 2));  // too small to box
        Assert.Throws<ArgumentOutOfRangeException>(() => BgraPack.ToBottomUpBgr(new byte[4], 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => BgraPack.ToBottomUpBgr(Array.Empty<byte>(), 1, 1));
    }
}
