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

    [Theory]
    [InlineData(GpuAdaptiveQualityTier.Full, 0)]
    [InlineData(GpuAdaptiveQualityTier.BloomReduced, 1)]
    [InlineData(GpuAdaptiveQualityTier.BloomMinimal, 2)]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, 3)]
    [InlineData(GpuAdaptiveQualityTier.AmbientMinimal, 6)]
    public void EachAdaptiveTierCutsExactlySoManyFamilies(GpuAdaptiveQualityTier tier, int expected)
    {
        Assert.Equal(expected, ArQualityBudget.CutCountFor(tier));
        Assert.True(expected <= ArQualityBudget.CutOrder.Length, "never deeper than the table itself");
    }

    [Theory]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.Particles, true)]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.Pulses, true)]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.NoteLights, true)]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.LightBars, false)]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.NoteStyle, false)]
    [InlineData(GpuAdaptiveQualityTier.AmbientReduced, ArBudgetCut.ImpactLights, false)]
    [InlineData(GpuAdaptiveQualityTier.AmbientMinimal, ArBudgetCut.ImpactLights, true)]
    [InlineData(GpuAdaptiveQualityTier.AmbientMinimal, ArBudgetCut.NoteStyle, true)]
    [InlineData(GpuAdaptiveQualityTier.Full, ArBudgetCut.Particles, false)]
    public void CutsHappenOnlyAtOrPastTheirStep(GpuAdaptiveQualityTier tier, ArBudgetCut cut, bool expected)
    {
        Assert.Equal(expected, ArQualityBudget.IsCut(tier, cut));
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
