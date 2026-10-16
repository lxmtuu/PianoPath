using Xunit;

namespace PianoPath.Tests;

public sealed class KeyParticleEnsembleTests
{
    [Fact]
    public void MatrixCoversShippingSelectorsWithoutPretendingTheyAreAllAccepted()
    {
        Assert.Equal(1120,KeyParticleEnsemblePlan.CaptureCount);
        Assert.Equal(new[]{"Embers","Splash","Fireworks","Confetti","Dust"},KeyParticleEnsemblePlan.Recipes.Select(r=>r.Burst));
        Assert.Equal(2,KeyParticleEnsemblePlan.Recipes.Select(r=>r.Held).Distinct().Count());
        Assert.Equal(3,KeyParticleEnsemblePlan.Recipes.Select(r=>r.Morph).Distinct().Count());
        Assert.Equal(3,KeyParticleEnsemblePlan.Recipes.Select(r=>r.Release).Distinct().Count());
        Assert.Equal(new[]{"off","classic","mixed","front-control"},KeyParticleEnsemblePlan.Variants);
    }
    [Fact]
    public void PhysicalReleaseDoesNotWaitForPedal()
    {
        Assert.Equal(new[]{60},KeyParticleEnsemblePlan.PressedAt(0));
        Assert.Empty(KeyParticleEnsemblePlan.PressedAt(12));
        Assert.Equal(8,KeyParticleEnsemblePlan.PressedAt(18).Count);
        Assert.Contains(60,KeyParticleEnsemblePlan.PressedAt(33));Assert.Contains(61,KeyParticleEnsemblePlan.PressedAt(33));
        Assert.Empty(KeyParticleEnsemblePlan.PressedAt(36));Assert.True(KeyParticleEnsemblePlan.PedalAt(36));
        Assert.True(KeyParticleEnsemblePlan.PedalAt(53));Assert.False(KeyParticleEnsemblePlan.PedalAt(54));
        Assert.Equal(9,KeyParticleEnsemblePlan.Notes.Count);
        Assert.All(KeyParticleEnsemblePlan.Notes,n=>Assert.True(n.Off>n.On && n.Strength>0 && n.Strength<=1));
    }
    [Fact]
    public void CheckpointsStraddleActualMusicalEvents()
    {
        Assert.Equal(new[]{2,10,20,33,38,56,149},KeyParticleEnsemblePlan.Checkpoints.Select(c=>c.Frame));
        Assert.True(KeyParticleEnsemblePlan.PedalAt(38));Assert.Empty(KeyParticleEnsemblePlan.PressedAt(38));
        Assert.False(KeyParticleEnsemblePlan.PedalAt(149));Assert.Empty(KeyParticleEnsemblePlan.PressedAt(149));
        Assert.Throws<ArgumentOutOfRangeException>(()=>KeyParticleEnsemblePlan.PressedAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>KeyParticleEnsemblePlan.PedalAt(-1));
    }
}
