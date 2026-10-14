using System.Numerics;
using Xunit;

namespace PianoPath.Tests;

public sealed class PrismFlowPaletteTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void SamplingEveryStopPreservesItsSrgbPigment(int index) =>
        Near(PrismFlowPalette.StopColor(index), PrismFlowPalette.Sample(PrismFlowPalette.StopPosition(index)));

    [Fact]
    public void SegmentMidpointIsLinearLightNotSrgbAverage()
    {
        var a = PrismFlowPalette.StopColor(1);
        var b = PrismFlowPalette.StopColor(2);
        var actual = PrismFlowPalette.Sample(.33);
        static float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
        var expectedRed = (Linear(a.X) + Linear(b.X)) / 2;
        Assert.InRange(MathF.Abs(Linear(actual.X) - expectedRed), 0, .00001f);
        Assert.True(actual.X > (a.X + b.X) / 2 + .05f);
    }

    [Fact]
    public void FixedRangeClampsWithoutLookingAtSongOrKeyboardLayout()
    {
        Near(PrismFlowPalette.StopColor(0), PrismFlowPalette.ForPitch(21, 36, 84));
        Near(PrismFlowPalette.StopColor(5), PrismFlowPalette.ForPitch(108, 36, 84));
        Near(PrismFlowPalette.Sample(.5), PrismFlowPalette.ForPitch(60, 36, 84));
    }

    [Theory]
    [InlineData(60, 60)] [InlineData(80, 40)] [InlineData(-1, 108)] [InlineData(21, 128)]
    public void InvalidMidiRangesAreRejected(int low, int high) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PrismFlowPalette.ForPitch(60, low, high));

    [Fact]
    public void InvalidPositionsHaveDeterministicEndpoints()
    {
        Near(PrismFlowPalette.StopColor(0), PrismFlowPalette.Sample(double.NaN));
        Near(PrismFlowPalette.StopColor(0), PrismFlowPalette.Sample(double.NegativeInfinity));
        Near(PrismFlowPalette.StopColor(5), PrismFlowPalette.Sample(double.PositiveInfinity));
    }

    [Fact]
    public void AllIntermediateColorsStayInGamutAndContinuous()
    {
        var previous = PrismFlowPalette.Sample(0);
        for (var i = 0; i <= 10000; i++)
        {
            var current = PrismFlowPalette.Sample(i / 10000.0);
            Assert.InRange(current.X, 0, 1.000001f);
            Assert.InRange(current.Y, 0, 1.000001f);
            Assert.InRange(current.Z, 0, 1.000001f);
            Assert.True(Vector3.Distance(previous, current) < .004f);
            previous = current;
        }
    }

    private static void Near(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < .00001f,
        $"Expected {expected}, got {actual}");
}
