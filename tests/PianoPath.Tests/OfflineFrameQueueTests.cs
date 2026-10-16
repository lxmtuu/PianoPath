using Xunit;

namespace PianoPath.Tests;

public sealed class OfflineFrameQueueTests
{
    [Fact]
    public async Task FullQueueWaitsThenRetainsEveryImageInOrder()
    {
        var gate = new object(); var queue = new Queue<int>(); var read = new List<int>();
        using var full = new ManualResetEventSlim();
        // The test blocks its reader on a real event; a pool task can starve behind other
        // parallel tests on small CI runners. Keep deadlines/order checks, use a dedicated writer.
        var writer = Task.Factory.StartNew(() =>
        {
            for (var i=0;i<12;i++) lock(gate)
            {
                OfflineFrameQueue.WaitForSpace(gate, () => queue.Count, 4, () => { }, TimeSpan.FromSeconds(5));
                queue.Enqueue(i); if (queue.Count==4) full.Set(); Monitor.PulseAll(gate);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(full.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(writer.IsCompleted);
        lock(gate) Assert.Equal(4,queue.Count);
        for (var i=0;i<12;i++) lock(gate)
        {
            OfflineFrameQueue.WaitForSpace(gate, () => queue.Count==0 ? 1 : 0, 1, () => { }, TimeSpan.FromSeconds(5));
            read.Add(queue.Dequeue()); Monitor.PulseAll(gate);
        }
        await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Enumerable.Range(0,12),read);
    }

    [Fact]
    public async Task WriterFailureIsObservedAfterWakeup()
    {
        var gate=new object(); bool failed=false;
        using var waiting=new ManualResetEventSlim();
        // A dedicated worker must reach the queue before the test sends the encoder failure.
        // Do not make the producer/consumer handshake depend on thread-pool availability.
        var task=Task.Factory.StartNew(() =>
        {
            lock(gate) Assert.Throws<IOException>(() => OfflineFrameQueue.WaitForSpace(gate,()=>4,4,()=>
            { waiting.Set(); if (failed) throw new IOException("encoder failed"); },TimeSpan.FromSeconds(5)));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
        lock(gate) { failed=true; Monitor.PulseAll(gate); }
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WedgedEncoderHasBoundedWait()
    {
        var gate=new object();
        lock(gate) Assert.Throws<TimeoutException>(() => OfflineFrameQueue.WaitForSpace(gate,()=>4,4,()=>{},TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void ClosedRecorderIsRejectedEvenWithSpace()
    {
        var gate=new object();
        lock(gate) Assert.Throws<InvalidOperationException>(() => OfflineFrameQueue.WaitForSpace(gate,()=>0,4,
            ()=>throw new InvalidOperationException("closed")));
    }
}
