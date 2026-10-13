using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the view-tuned AR table (Camera/ArViewPresets.cs, roadmap §14.7 "Thêm 5"): the leaned camera keeps the
/// catalogue heights the looks were built with, the 90° overhead view goes paper-thin and hides stand-up quads,
/// and every id the table does not know — legacy, empty, foreign — falls back to the leaned row it always had.
/// </summary>
public class ArViewPresetTests
{
    [Fact]
    public void OverheadAndLeanedCamerasGetDifferentNumbers()
    {
        var leaned = ArViewPresets.Leaned;
        var overhead = ArViewPresets.Overhead;
        Assert.Equal(.2, leaned.ImpactGlowCentimetres, 9);
        Assert.Equal(.3, leaned.PulseCentimetres, 9);
        Assert.True(leaned.TallEffectsVisible);
        Assert.Equal(.05, overhead.ImpactGlowCentimetres, 9);
        Assert.Equal(.1, overhead.PulseCentimetres, 9);
        Assert.False(overhead.TallEffectsVisible);
        // And the lifts come out in keyboard-depth units, strictly thinner overhead but never zero:
        Assert.True(overhead.ImpactGlow > 0 && overhead.PulseMinimum > 0);
        Assert.True(overhead.ImpactGlow < leaned.ImpactGlow);
        Assert.True(overhead.PulseMinimum < leaned.PulseMinimum);
        Assert.Equal(leaned.ImpactGlowCentimetres / 15, leaned.ImpactGlow, 9);       // 150 mm of key depth = 1 unit
        Assert.Equal(overhead.PulseCentimetres / 15, overhead.PulseMinimum, 9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("tilt45")]
    [InlineData("unknown-view")]
    [InlineData("OVERHEAD91")]
    public void UnknownAndLeanedViewsGetTheLeanedRow(string? id)
    {
        Assert.Same(ArViewPresets.Leaned, ArViewPresets.ForProfile(id));
    }

    [Theory]
    [InlineData("overhead90")]
    [InlineData("Overhead90")]
    [InlineData("OVERHEAD90")]
    public void TheOverheadViewGetsItsOwnRow(string id)
    {
        Assert.Same(ArViewPresets.Overhead, ArViewPresets.ForProfile(id));
    }

    [Fact]
    public void OverheadPulseStillLiftsAFlatPointPastThePlane()
    {
        // The Lifted guarantee works against the view's own floor: a point lying on the plane rises to it.
        var flat = new ArVec3(0, 0, 0);
        var liftedLeaned = ArElementHeights.Lifted(flat, ArViewPresets.Leaned.PulseMinimum);
        var liftedOverhead = ArElementHeights.Lifted(flat, ArViewPresets.Overhead.PulseMinimum);
        Assert.Equal(-ArViewPresets.Leaned.PulseMinimum, liftedLeaned.Z, 12);
        Assert.Equal(-ArViewPresets.Overhead.PulseMinimum, liftedOverhead.Z, 12);
        Assert.True(liftedLeaned.Z < liftedOverhead.Z, "the leaned floor is the deeper one");
    }
}
