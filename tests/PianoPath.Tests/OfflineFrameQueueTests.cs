using Xunit;

namespace PianoPath.Tests;

public sealed class OfflineFrameQueueTests
{
    [Fact]
    public void FullQueueWaitsThenRetainsEveryImageInOrder()
    {
        var gate = new object(); var queue = new Queue<int>(); var read = new List<int>();
        using var full = new ManualResetEventSlim();
        var writer = Task.Run(() =>
        {
            for (var i=0;i<12;i++) lock(gate)
            {
                OfflineFrameQueue.WaitForSpace(gate, () => queue.Count, 4, () => { }, TimeSpan.FromSeconds(5));
                queue.Enqueue(i); if (queue.Count==4) full.Set(); Monitor.PulseAll(gate);
            }
        });
        Assert.True(full.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(writer.IsCompleted);
        lock(gate) Assert.Equal(4,queue.Count);
        for (var i=0;i<12;i++) lock(gate)
        {
            OfflineFrameQueue.WaitForSpace(gate, () => queue.Count==0 ? 1 : 0, 1, () => { }, TimeSpan.FromSeconds(5));
            read.Add(queue.Dequeue()); Monitor.PulseAll(gate);
        }
        Assert.True(writer.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(Enumerable.Range(0,12),read);
    }

    [Fact]
    public void WriterFailureIsObservedAfterWakeup()
    {
        var gate=new object(); bool failed=false;
        using var waiting=new ManualResetEventSlim();
        var task=Task.Run(() =>
        {
            lock(gate) Assert.Throws<IOException>(() => OfflineFrameQueue.WaitForSpace(gate,()=>4,4,()=>
            { waiting.Set(); if (failed) throw new IOException("encoder failed"); },TimeSpan.FromSeconds(5)));
        });
        Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
        lock(gate) { failed=true; Monitor.PulseAll(gate); }
        Assert.True(task.Wait(TimeSpan.FromSeconds(5)));
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
