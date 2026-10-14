using System.Collections.Concurrent;
using System.Numerics;
using Xunit;

namespace PianoPath.Tests;

public sealed class TransientFrameEpochTests
{
    [Fact]
    public void ClearInvalidatesOldSnapshotsEvenAfterItsFlagWasConsumed()
    {
        var epoch = new TransientFrameEpoch();
        var before = epoch.Generation;
        Assert.True(epoch.IsCurrent(before));
        Assert.Equal(1, epoch.Advance());
        Assert.True(epoch.TakeClearRequest());
        Assert.False(epoch.TakeClearRequest());
        Assert.False(epoch.IsCurrent(before));
        Assert.True(epoch.IsCurrent(1));
    }

    [Fact]
    public void MultipleClearsCoalesceButEveryStampIsInvalidated()
    {
        var epoch = new TransientFrameEpoch();
        for (var i = 0; i < 100; i++) Assert.Equal(i + 1, epoch.Advance());
        Assert.True(epoch.TakeClearRequest());
        Assert.False(epoch.TakeClearRequest());
        for (var i = 0; i < 100; i++) Assert.False(epoch.IsCurrent(i));
        Assert.True(epoch.IsCurrent(100));
    }

    [Fact]
    public void ParallelClearsHaveUniqueMonotonicGenerations()
    {
        var epoch = new TransientFrameEpoch();
        var stamps = new ConcurrentBag<long>();
        Parallel.For(0, 1000, _ => stamps.Add(epoch.Advance()));
        Assert.Equal(1000, epoch.Generation);
        Assert.Equal(Enumerable.Range(1, 1000).Select(i => (long)i), stamps.Order());
        Assert.True(epoch.TakeClearRequest());
        Assert.False(epoch.TakeClearRequest());
    }

    [Fact]
    public async Task SharedGateMakesSnapshotAndClearDecisionOneBoundary()
    {
        var epoch = new TransientFrameEpoch();
        using var started = new ManualResetEventSlim();
        Task<long> worker;
        lock (epoch.Gate)
        {
            worker = Task.Run(() => { started.Set(); return epoch.Advance(); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, epoch.Generation);
            Assert.False(worker.IsCompleted); // writer cannot publish through a caller's snapshot boundary
            Assert.False(epoch.TakeClearRequest()); // the shared lock is reentrant
        }
        Assert.Equal(1, await worker.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(epoch.TakeClearRequest());
    }

    [Theory]
    [InlineData(.05)] [InlineData(.15)] [InlineData(.3)]
    public void DelayedKeyHistoryCannotSampleBeforeClear(double delay)
    {
        var history = new ArKeyHistory(2);
        var lit = new[] { new Vector4(1, 0, 0, 1), Vector4.Zero };
        var dark = new Vector4[2]; var output = new Vector4[2];
        history.Record(1, lit, generation: 0);
        history.Record(1.05, lit, generation: 0);
        history.Record(1.1, dark, generation: 1);
        history.Sample(1.1 - delay, output);
        Assert.Equal(1, history.Count);
        Assert.All(output, key => Assert.Equal(Vector4.Zero, key));
        history.Sample(1.2, output);
        Assert.All(output, key => Assert.Equal(Vector4.Zero, key));
    }

    [Fact]
    public void NewEpochStartsItsOwnInterpolationWithoutRewindingWallClock()
    {
        var history = new ArKeyHistory(1); var output = new Vector4[1];
        history.Record(1, [new Vector4(1, 0, 0, 1)], 0);
        history.Record(2, [Vector4.Zero], 1);
        history.Record(2.1, [new Vector4(0, 1, 0, 1)], 1);
        history.Sample(2.05, output);
        Assert.Equal(2, history.Count);
        Assert.Equal(0, output[0].X);
        Assert.Equal(1, output[0].Y);
        Assert.InRange(output[0].W, .49999f, .50001f);
    }

    [Fact]
    public void InvalidTimeDoesNotStartAnotherHistoryEpoch()
    {
        var history = new ArKeyHistory(1); var output = new Vector4[1];
        history.Record(1, [Vector4.One], 0);
        history.Record(double.NaN, [Vector4.Zero], 1);
        history.Sample(1, output);
        Assert.Equal(Vector4.One, output[0]);
        history.Record(2, [Vector4.Zero], 1);
        history.Sample(1, output);
        Assert.Equal(Vector4.Zero, output[0]);
    }
}
