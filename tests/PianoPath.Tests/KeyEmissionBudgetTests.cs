using Xunit;

namespace PianoPath.Tests;

public sealed class KeyEmissionBudgetTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)] [InlineData(144)]
    public void EqualElapsedTimeHasEqualBirthScheduleTotals(int fps)
    {
        double credit = 0;
        var born = 0;
        for (var frame = 0; frame < fps * 2; frame++)
        {
            born += KeyEmissionBudget.Take(ref credit, 26, 1.0 / fps, 24000);
            Assert.InRange(credit, 0, Math.BitDecrement(1d));
        }
        Assert.Equal(52, born);
        Assert.InRange(credit, 0, 1e-8);
    }

    [Fact]
    public void CapacityDropsWholeBirthsRatherThanAccumulatingDebt()
    {
        double credit = 0;
        for (var i = 0; i < 10000; i++)
        {
            Assert.Equal(0, KeyEmissionBudget.Take(ref credit, 156, .05, 0));
            Assert.InRange(credit, 0, Math.BitDecrement(1d));
        }
        Assert.InRange(KeyEmissionBudget.Take(ref credit, 156, .01, 24000), 1, 2);
    }

    [Fact]
    public void PartialCapacityStillDiscardsUnservedBirths()
    {
        double credit = .5;
        Assert.Equal(2, KeyEmissionBudget.Take(ref credit, 100, .1, 2));
        Assert.Equal(.5, credit, 8);
        Assert.Equal(1, KeyEmissionBudget.Take(ref credit, 100, .01, 24000));
    }

    [Fact]
    public void ReleaseOrToggleOffStartsANewFractionalEpoch()
    {
        double credit = .99;
        Assert.Equal(0, KeyEmissionBudget.Take(ref credit, 20, .01, 24000, enabled: false));
        Assert.Equal(0, credit);
        Assert.Equal(0, KeyEmissionBudget.Take(ref credit, 20, .01, 24000));
        Assert.Equal(.2, credit, 8);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidOrDisabledRateClearsCredit(double rate)
    {
        double credit = .9;
        Assert.Equal(0, KeyEmissionBudget.Take(ref credit, rate, .1, 24000));
        Assert.Equal(0, credit);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidOrZeroDtCannotCreateBirthsOrAgeCredit(double seconds)
    {
        double credit = .5;
        Assert.Equal(0, KeyEmissionBudget.Take(ref credit, 100, seconds, 24000));
        Assert.Equal(.5, credit);
    }

    [Theory]
    [InlineData(-100)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(50000)]
    public void StaleOrCorruptCreditCannotBecomeADeferredBurst(double stale)
    {
        double credit = stale;
        Assert.InRange(KeyEmissionBudget.Take(ref credit, 20, .01, 24000), 0, 1);
        Assert.InRange(credit, 0, Math.BitDecrement(1d));
    }

    [Fact]
    public void OverflowAndNegativeCapacityAreBounded()
    {
        double credit = 0;
        Assert.Equal(0, KeyEmissionBudget.Take(ref credit, double.MaxValue, 2, 24000));
        Assert.Equal(0, credit);
        Assert.Equal(0, KeyEmissionBudget.Take(ref credit, 100, .1, -100));
        Assert.Equal(0, credit);
    }

    [Fact]
    public void DifferentKeysHaveIndependentCredit()
    {
        double white = .9, black = .1;
        Assert.Equal(1, KeyEmissionBudget.Take(ref white, 20, .01, 24000));
        Assert.Equal(0, KeyEmissionBudget.Take(ref black, 20, .01, 24000));
        Assert.Equal(.1, white, 8);
        Assert.Equal(.3, black, 8);
    }
}
