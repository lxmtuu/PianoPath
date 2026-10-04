using System.Threading;
using PianoPath;
using Xunit;

namespace PianoPath.Tests;

public sealed class AudioTapDispatcherTests
{
    [Fact]
    public void Tap_copies_samples_and_calls_the_writer_off_the_producer_thread()
    {
        using var dispatcher = new AudioTapDispatcher();
        using var received = new ManualResetEventSlim(false);
        var source = new short[] { 1, 2, 3, 4 };
        short[]? result = null;
        var writerThread = Environment.CurrentManagedThreadId;
        var callbackThread = writerThread;
        dispatcher.SetTap((samples, count) =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            result = samples.Take(count).ToArray();
            received.Set();
        });

        Assert.True(dispatcher.Enqueue(source, source.Length));
        Array.Fill(source, (short)99);
        Assert.True(received.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(dispatcher.Flush(TimeSpan.FromSeconds(2)));

        Assert.Equal(new short[] { 1, 2, 3, 4 }, result);
        Assert.NotEqual(writerThread, callbackThread);
    }

    [Fact]
    public void Detaching_tap_prevents_new_blocks_and_flush_waits_for_prior_work()
    {
        using var dispatcher = new AudioTapDispatcher();
        var calls = 0;
        dispatcher.SetTap((_, _) => Interlocked.Increment(ref calls));
        Assert.True(dispatcher.Enqueue(new short[8], 8));
        dispatcher.SetTap(null);
        Assert.False(dispatcher.Enqueue(new short[8], 8));
        Assert.True(dispatcher.Flush(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, calls);
    }
}
