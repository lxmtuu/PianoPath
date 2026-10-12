using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks roadmap §14.8's AR drop rule (Camera/ArQualityBudget.cs): the six cuts come in the sentence's exact order,
/// each adaptive tier cuts exactly so many of them, the rules stay reversible and state-free, and the three things
/// that ARE the overlay — keys, notes, keypresses — exist outside every cut, so no over-budget frame can lose them.
/// </summary>
public class ArQualityBudgetTests
{
    [Fact]
    public void TheCutListIsTheRoadmapSentenceWordForWord()
    {
        Assert.Equal(
            [ArBudgetCut.Particles, ArBudgetCut.Pulses, ArBudgetCut.NoteLights,
             ArBudgetCut.LightBars, ArBudgetCut.NoteStyle, ArBudgetCut.ImpactLights],
            ArQualityBudget.CutOrder);
    }

    // tier values: Full = 0, BloomReduced = 1, BloomMinimal = 2, AmbientReduced = 3, AmbientMinimal = 4 (enum order)
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 6)]
    public void EachAdaptiveTierCutsExactlySoManyFamilies(int tierValue, int expected)
    {
        var tier = (GpuAdaptiveQualityTier)tierValue;
        Assert.Equal(expected, ArQualityBudget.CutCountFor(tier));
        Assert.True(expected <= ArQualityBudget.CutOrder.Length, "never deeper than the table itself");
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(3, 1, true)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    [InlineData(3, 4, false)]
    [InlineData(3, 5, false)]
    [InlineData(4, 5, true)]
    [InlineData(4, 4, true)]
    [InlineData(0, 0, false)]
    public void CutsHappenOnlyAtOrPastTheirStep(int tierValue, int cutValue, bool expected)
    {
        Assert.Equal(expected, ArQualityBudget.IsCut((GpuAdaptiveQualityTier)tierValue, (ArBudgetCut)cutValue));
    }

    [Fact]
    public void TheOverlayItsThreeEssencesStandOutsideEveryCut()
    {
        foreach (var tier in System.Enum.GetValues<GpuAdaptiveQualityTier>())
        {
            // Deep-cut frames still allow the keys whenever any tier would: nothing in the table touches them.
            Assert.Equal(ArElementCatalog.CanDraw(ArQualityTier.Lite, ArElement.PianoKeys),
                ArQualityBudget.CanDraw(ArQualityTier.Lite, tier, ArElement.PianoKeys));
            Assert.Equal(ArElementCatalog.CanDraw(ArQualityTier.Lite, ArElement.NoteObjects),
                ArQualityBudget.CanDraw(ArQualityTier.Lite, tier, ArElement.NoteObjects));
            Assert.Equal(ArElementCatalog.CanDraw(ArQualityTier.Lite, ArElement.Keypresses),
                ArQualityBudget.CanDraw(ArQualityTier.Lite, tier, ArElement.Keypresses));
        }
    }

    [Fact]
    public void ElementGatesStayConsistentWithTheCatalogueFloor()
    {
        // Below the catalogue's own floor nothing is restored by a healthy budget: Lite never had particles, so
        // a Full-budget Lite frame still draws none; Ultra at Full draws them all.
        Assert.False(ArQualityBudget.CanDraw(ArQualityTier.Lite, GpuAdaptiveQualityTier.Full, ArElement.Particles));
        Assert.True(ArQualityBudget.CanDraw(ArQualityTier.Ultra, GpuAdaptiveQualityTier.Full, ArElement.Particles));
        Assert.True(ArQualityBudget.CanDraw(ArQualityTier.Ultra, GpuAdaptiveQualityTier.Full, ArElement.ImpactLights));
        Assert.True(ArQualityBudget.CanDraw(ArQualityTier.Standard, GpuAdaptiveQualityTier.Full, ArElement.ImpactLights));
        // And a deep cut hides the catalogue-visible families even at Ultra.
        Assert.False(ArQualityBudget.CanDraw(ArQualityTier.Ultra, GpuAdaptiveQualityTier.AmbientMinimal, ArElement.Particles));
        Assert.False(ArQualityBudget.CanDraw(ArQualityTier.Ultra, GpuAdaptiveQualityTier.AmbientMinimal, ArElement.ImpactLights));
        Assert.False(ArQualityBudget.CanDraw(ArQualityTier.Ultra, GpuAdaptiveQualityTier.AmbientMinimal, ArElement.Pulses));
    }
}
