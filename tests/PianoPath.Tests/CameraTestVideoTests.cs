using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the camera-video fixture recipe (Camera/CameraTestVideo.cs): each tick is one grey step brighter
/// than the last, the stripe sits on the left twelfth with its fixed boost, and every frame is opaque and
/// tightly packed — the properties the render-to-video verify probes for when the file plays through.
/// </summary>
public class CameraTestVideoTests
{
    [Fact]
    public void EveryTickIsOneGreyStepBrighter()
    {
        var first = CameraTestVideo.Picture(64, 36, 0);
        var second = CameraTestVideo.Picture(64, 36, 1);
        var mid = 40 * 4;                 // a pixel outside the stripe
        Assert.Equal(first[mid] + 8, second[mid]);
    }

    [Fact]
    public void TheStripeSitsOnTheLeftTwelfthWithItsBoost()
    {
        var frame = CameraTestVideo.Picture(64, 36, 3);
        var stripeX = (64 / 12 - 1) * 4;  // inside the stripe
        var plainX = 40 * 4;              // outside it
        Assert.Equal(frame[plainX] + CameraTestVideo.StripeBoost, frame[stripeX]);
    }

    [Fact]
    public void FramesAreOpaqueAndTightlyPacked()
    {
        var frame = CameraTestVideo.Picture(24, 12, 0);
        Assert.Equal(24 * 12 * 4, frame.Length);
        Assert.All(Enumerable.Range(0, 24 * 12), pixel => Assert.Equal(255, frame[pixel * 4 + 3]));
    }
}
