using PianoPath;
using Xunit;

namespace PianoPath.Tests;

public sealed class AsioBufferSizingTests
{
    [Fact]
    public void Power_of_two_driver_uses_largest_block_at_or_below_requested_latency()
    {
        Assert.Equal(128, AsioBufferSizing.SelectFrames(64, 2048, 512, -1, 132));
        Assert.Equal(256, AsioBufferSizing.SelectFrames(64, 2048, 512, -1, 500));
        Assert.Equal(64, AsioBufferSizing.SelectFrames(64, 2048, 512, -1, 16));
    }

    [Fact]
    public void Positive_granularity_is_anchored_at_minimum_and_clamped_to_driver_range()
    {
        Assert.Equal(192, AsioBufferSizing.SelectFrames(64, 2048, 512, 64, 220));
        Assert.Equal(64, AsioBufferSizing.SelectFrames(64, 2048, 512, 64, 64));
        Assert.Equal(2048, AsioBufferSizing.SelectFrames(64, 2048, 512, 64, 4096));
        Assert.Equal(256, AsioBufferSizing.SelectFrames(256, 1024, 512, 128, 32));
    }

    [Fact]
    public void Fixed_and_inconsistent_legacy_ranges_are_handled_safely()
    {
        Assert.Equal(128, AsioBufferSizing.SelectFrames(128, 128, 128, 0, 64));
        Assert.Equal(256, AsioBufferSizing.SelectFrames(64, 512, 256, 0, 192));
        Assert.Equal(80, AsioBufferSizing.SelectFrames(0, 0, 80, 0, 10));
    }
}
