using Xunit;

namespace PianoPath.Tests;

public sealed class GpuInstanceListTests
{
    [Fact]
    public void RetainFractionDeterministicallyThinsTheChosenRangeAndPreservesFollowingInstances()
    {
        var instances = Create(0, 1, 10, 11, 12, 13, 14, 15, 16, 17, 90, 91);

        instances.RetainFraction(start: 2, length: 8, fraction: .5f);

        Assert.Equal(new[] { 0, 1, 11, 13, 15, 17, 90, 91 }, instances.Span.ToArray());
    }

    [Fact]
    public void ZeroAmbientDensityRemovesOnlyTheRangeAndLeavesGuideAndNoteTrailInstances()
    {
        var instances = Create(1, 2, 3, 4, 5, 6);

        instances.RetainFraction(start: 2, length: 3, fraction: 0);

        Assert.Equal(new[] { 1, 2, 6 }, instances.Span.ToArray());
    }

    [Fact]
    public void FullOrClampedDensityLeavesInstancesUntouched()
    {
        var instances = Create(1, 2, 3, 4);

        instances.RetainFraction(start: 1, length: 2, fraction: 1);
        instances.RetainFraction(start: 1, length: 2, fraction: 2);

        Assert.Equal(new[] { 1, 2, 3, 4 }, instances.Span.ToArray());
    }

    [Fact]
    public void EmptyAndClampedRangesAreSafe()
    {
        var instances = Create(1, 2, 3);

        instances.RetainFraction(start: 99, length: 10, fraction: 0);
        instances.RetainFraction(start: 1, length: -2, fraction: 0);

        Assert.Equal(new[] { 1, 2, 3 }, instances.Span.ToArray());
    }

    private static GpuInstanceList<int> Create(params int[] values)
    {
        var instances = new GpuInstanceList<int>(values.Length);
        foreach (var value in values) instances.Add(value);
        return instances;
    }
}
