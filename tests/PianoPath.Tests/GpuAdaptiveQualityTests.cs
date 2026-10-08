using Xunit;

namespace PianoPath.Tests;

public sealed class GpuAdaptiveQualityTests
{
    [Fact]
    public void StartsAtFullQualityAndUnlimitedUsesTheProductBudget()
    {
        var controller = new GpuAdaptiveQualityController();

        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
        Assert.Equal(new GpuAdaptiveQualityProfile(6, 1f), controller.Profile);
        Assert.Equal(8.0, GpuAdaptiveQualityController.BudgetForTargetFps(0), 6);
        Assert.Equal(1000.0 / 60, GpuAdaptiveQualityController.BudgetForTargetFps(60), 6);
        Assert.Equal(1000.0 / 144, GpuAdaptiveQualityController.BudgetForTargetFps(144), 6);
    }

    [Fact]
    public void VSyncBudgetUsesDisplayRefreshAndTheSlowerConfiguredCap()
    {
        Assert.Equal(1000.0 / 60, GpuAdaptiveQualityController.BudgetForOutput(144, true, true, 60), 6);
        Assert.Equal(1000.0 / 120, GpuAdaptiveQualityController.BudgetForOutput(144, true, true, 120), 6);
        Assert.Equal(1000.0 / 60, GpuAdaptiveQualityController.BudgetForOutput(60, true, true, 144), 6);
        Assert.Equal(1000.0 / 144, GpuAdaptiveQualityController.BudgetForOutput(144, false, true, 60), 6);
        Assert.Equal(1000.0 / 144, GpuAdaptiveQualityController.BudgetForOutput(144, true, false, 60), 6);
        Assert.Equal(8.0, GpuAdaptiveQualityController.BudgetForOutput(0, true, true, 144), 6);
    }

    [Fact]
    public void ProfilesReduceBloomBeforeAmbientAndNeverAddParticleOrNoteGeometryOverrides()
    {
        Assert.Equal(new GpuAdaptiveQualityProfile(6, 1f), GpuAdaptiveQualityProfile.For(GpuAdaptiveQualityTier.Full));
        Assert.Equal(new GpuAdaptiveQualityProfile(4, 1f), GpuAdaptiveQualityProfile.For(GpuAdaptiveQualityTier.BloomReduced));
        Assert.Equal(new GpuAdaptiveQualityProfile(2, 1f), GpuAdaptiveQualityProfile.For(GpuAdaptiveQualityTier.BloomMinimal));
        Assert.Equal(new GpuAdaptiveQualityProfile(2, .6f), GpuAdaptiveQualityProfile.For(GpuAdaptiveQualityTier.AmbientReduced));
        Assert.Equal(new GpuAdaptiveQualityProfile(2, .3f), GpuAdaptiveQualityProfile.For(GpuAdaptiveQualityTier.AmbientMinimal));
        Assert.Equal(GpuAdaptiveQualityProfile.For(GpuAdaptiveQualityTier.Full), GpuAdaptiveQualityProfile.For((GpuAdaptiveQualityTier)999));
    }

    [Fact]
    public void RequiresThreeConsecutiveHotP95WindowsAndMovesOnlyOneTierAtATime()
    {
        var controller = WithBudget(10);

        AddWindow(controller, 12);
        AddWindow(controller, 12);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);

        AddWindow(controller, 12);
        Assert.Equal(GpuAdaptiveQualityTier.BloomReduced, controller.Tier);

        AddWindow(controller, 12);
        AddWindow(controller, 12);
        Assert.Equal(GpuAdaptiveQualityTier.BloomReduced, controller.Tier);
        AddWindow(controller, 12);
        Assert.Equal(GpuAdaptiveQualityTier.BloomMinimal, controller.Tier);

        AddWindow(controller, 12);
        AddWindow(controller, 12);
        AddWindow(controller, 12);
        Assert.Equal(GpuAdaptiveQualityTier.AmbientReduced, controller.Tier);
    }

    [Fact]
    public void OneSlowFrameDoesNotCauseAQualityChange()
    {
        var controller = WithBudget(10);

        controller.Observe(100);
        for (var i = 1; i < GpuAdaptiveQualityController.SamplesPerEvaluation; i++) controller.Observe(5);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
        Assert.Equal(5, controller.LastP95Milliseconds, 6);

        AddWindow(controller, 5);
        AddWindow(controller, 5);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
    }

    [Fact]
    public void InvalidOrMissingTimestampsAreNotCountedAsFastFrames()
    {
        var controller = WithBudget(10);

        for (var i = 0; i < GpuAdaptiveQualityController.SamplesPerEvaluation - 1; i++) controller.Observe(12);
        controller.Observe(double.NaN);
        controller.Observe(double.PositiveInfinity);
        controller.Observe(-1);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);

        controller.Observe(12);
        AddWindow(controller, 12);
        AddWindow(controller, 12);
        Assert.Equal(GpuAdaptiveQualityTier.BloomReduced, controller.Tier);
    }

    [Fact]
    public void RestoresOneTierAfterEightConsecutiveCoolWindows()
    {
        var controller = WithBudget(10);
        AddHotWindows(controller, 6); // Full -> BloomReduced -> BloomMinimal.
        Assert.Equal(GpuAdaptiveQualityTier.BloomMinimal, controller.Tier);

        AddCoolWindows(controller, GpuAdaptiveQualityController.CoolWindowsToRestore - 1);
        Assert.Equal(GpuAdaptiveQualityTier.BloomMinimal, controller.Tier);
        AddWindow(controller, 6.4);
        Assert.Equal(GpuAdaptiveQualityTier.BloomReduced, controller.Tier);

        AddCoolWindows(controller, GpuAdaptiveQualityController.CoolWindowsToRestore);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
    }

    [Fact]
    public void IntermediateLoadBreaksBothHysteresisRuns()
    {
        var controller = WithBudget(10);
        AddHotWindows(controller, 2);
        AddWindow(controller, 8); // Between the 90% reduction and 75% recovery thresholds.
        AddHotWindows(controller, 2);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);

        AddCoolWindows(controller, 7);
        AddWindow(controller, 8);
        AddWindow(controller, 6.4);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
    }

    [Fact]
    public void ChangingTheTargetDiscardsAPartialWindowAndResetsHysteresisCounters()
    {
        var controller = WithBudget(10);
        AddHotWindows(controller, 2);
        var generation = controller.SampleGeneration;
        controller.SetFrameBudgetMs(20);
        Assert.Equal(20, controller.FrameBudgetMs, 6);
        Assert.Equal(generation + 1, controller.SampleGeneration);

        AddWindow(controller, 20);
        AddWindow(controller, 20);
        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
        AddWindow(controller, 20);
        Assert.Equal(GpuAdaptiveQualityTier.BloomReduced, controller.Tier);
        Assert.Equal(generation + 2, controller.SampleGeneration);
    }

    [Fact]
    public void ResetReturnsToFullQualityAndClearsTheLastMeasurement()
    {
        var controller = WithBudget(10);
        AddHotWindows(controller, 3);
        Assert.Equal(GpuAdaptiveQualityTier.BloomReduced, controller.Tier);

        controller.ResetToFull();

        Assert.Equal(GpuAdaptiveQualityTier.Full, controller.Tier);
        Assert.Equal(0, controller.LastP95Milliseconds, 6);
    }

    private static GpuAdaptiveQualityController WithBudget(double milliseconds)
    {
        var controller = new GpuAdaptiveQualityController();
        controller.SetFrameBudgetMs(milliseconds);
        return controller;
    }

    private static void AddWindow(GpuAdaptiveQualityController controller, double milliseconds)
    {
        for (var i = 0; i < GpuAdaptiveQualityController.SamplesPerEvaluation; i++) controller.Observe(milliseconds);
    }

    private static void AddHotWindows(GpuAdaptiveQualityController controller, int count)
    {
        for (var i = 0; i < count; i++) AddWindow(controller, 12);
    }

    private static void AddCoolWindows(GpuAdaptiveQualityController controller, int count)
    {
        for (var i = 0; i < count; i++) AddWindow(controller, 6.4);
    }
}
