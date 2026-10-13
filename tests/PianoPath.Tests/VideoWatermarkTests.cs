using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks where the export watermark lands (Video/ArVideoWatermark.cs, roadmap §14.10 AR‑S7): centred above
/// the keys at the software stage's own distance, sized from the strip's real aspect, only ever shrinking
/// on a narrow scene, and absent only when there is no picture to draw.
/// </summary>
public class VideoWatermarkTests
{
    [Fact]
    public void AStandardSceneCentresTheStripAboveTheKeys()
    {
        var mark = ArVideoWatermark.Place(1280, 720, 560, 110, 22);
        Assert.NotNull(mark);
        Assert.Equal(640, mark!.Value.CenterX);
        Assert.Equal(22 / 2f, mark.Value.HalfHeight);
        Assert.Equal(560 - ArVideoWatermark.AboveKeys - 11, mark.Value.CenterY);   // 531 DIPs
        Assert.Equal(5f, mark.Value.Aspect);
    }

    [Fact]
    public void ThePlacementKeepsTheSoftwareStagesOwnDistanceFromTheHitLine()
    {
        var mark = ArVideoWatermark.Place(120, 112, 88, 110, 22)!.Value;
        // A small export frame keeps the strip whole: it shrinks instead of sliding or clipping
        // (a 110×22 strip is 55 DIPs wide; the margins of a 120-DIP scene only allow 52).
        var halfWidth = mark.HalfHeight * mark.Aspect;
        Assert.True(mark.CenterX - halfWidth >= ArVideoWatermark.MarginX && mark.CenterX + halfWidth <= 120 - ArVideoWatermark.MarginX,
            $"The strip must keep its side margins (half-width {halfWidth}).");
        Assert.True(mark.HalfHeight < 11, $"On a narrow scene the strip shrinks ({mark.HalfHeight}).");
    }

    [Fact]
    public void AwkwardHitLinesStillKeepTheStripInsideTheFrame()
    {
        var mark = ArVideoWatermark.Place(640, 360, 120, 90, 30)!.Value;
        Assert.True(mark.CenterY - mark.HalfHeight >= 0 && mark.CenterY + mark.HalfHeight <= 360,
            $"The strip must stay inside the scene ({mark.CenterY} ± {mark.HalfHeight}).");
    }

    [Theory]
    [InlineData(0, 22)]
    [InlineData(-4, 22)]
    [InlineData(110, 0)]
    public void NoPictureNoPlacement(int width, int height)
    {
        Assert.Null(ArVideoWatermark.Place(1280, 720, 560, width, height));
    }

    [Fact]
    public void TheStripMatchesTheRealGlyphsProportions()
    {
        // A wider line stays wider: the placement never stretches text.
        var word = ArVideoWatermark.Place(1280, 720, 560, 220, 40)!.Value;
        Assert.Equal(220 / 40f, word.Aspect);
    }
}
