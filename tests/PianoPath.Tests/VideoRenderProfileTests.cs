using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// Locks the render-to-video plan (Video/ArVideoRenderProfile.cs, roadmap §14.10 AR‑S7): even output
/// dimensions for the AVI container, the recorders' 60 fps ceiling, long-side capping against the
/// render cap, downsample mapping onto box passes and the frame count of a take.
/// </summary>
public class VideoRenderProfileTests
{
    [Fact]
    public void SongPositionsUseThePlannedTakeRateNotTheRequestedCameraRate()
    {
        var plan=ArVideoRenderProfile.Plan(640,360,2,frameRate:15);
        Assert.Equal(0,plan.PositionAt(0));
        Assert.Equal(1,plan.PositionAt(15),12);
        Assert.Equal(29/15.0,plan.PositionAt(29),12);
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan.PositionAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>plan.PositionAt(plan.Frames));
    }

    [Fact]
    public void APlanFitsTheOutputIntoTheCapWithEvenSides()
    {
        var plan = ArVideoRenderProfile.Plan(640, 360, lengthSeconds: 0.5, frameRate: 12, downsample: 1, maxSide: 200);
        Assert.Equal(640, plan.SourceWidth);
        Assert.Equal(360, plan.SourceHeight);
        Assert.Equal(200, plan.RenderWidth);
        Assert.Equal(112, plan.RenderHeight);
        Assert.Equal(200, plan.Width);
        Assert.Equal(112, plan.Height);
        Assert.True(plan.Width % 2 == 0 && plan.Height % 2 == 0, "The AVI container wants even sides.");
        Assert.Equal(12, plan.FrameRate);
        Assert.Equal(6, plan.Frames);                          // 0.5 s at 12 fps, ceiling gives the settle frame
        Assert.Equal(1d / 12, plan.FrameSeconds, 9);
        Assert.Equal(0.5, plan.Seconds, 9);
    }

    [Theory]
    [InlineData(2, 640, 360)]
    [InlineData(4, 320, 180)]
    public void DownsampleBoxesTheRenderDownToTheOutput(int ds, int ow, int oh)
    {
        // The render always draws up to the cap; the downsample only shrinks what the writer receives.
        var plan = ArVideoRenderProfile.Plan(1280, 720, 2, 30, ds, maxSide: 1280);
        Assert.Equal(1280, plan.RenderWidth);
        Assert.Equal(720, plan.RenderHeight);
        Assert.Equal(ow, plan.Width);
        Assert.Equal(oh, plan.Height);
        Assert.Equal(ds, plan.Downsample);
        Assert.Equal(60, plan.Frames);
    }

    [Fact]
    public void EveryKnobIsClampedIntoAWritableRange()
    {
        var plan = ArVideoRenderProfile.Plan(3840, 2160, 0.1, frameRate: 480, downsample: 1);
        Assert.Equal(ArVideoRenderProfile.MaxRate, plan.FrameRate);          // both writers only accept up to 60
        Assert.Equal(6, plan.Frames);                                        // 0.1 s at 60 fps
        var small = ArVideoRenderProfile.Plan(40, 34, 0.001, 30, 4);
        Assert.True(small.RenderWidth >= ArVideoRenderProfile.MinRenderSide && small.RenderHeight >= ArVideoRenderProfile.MinRenderSide);
        Assert.True(small.Width >= ArVideoRenderProfile.MinOutputSide - 2 && small.Width % 2 == 0);
        Assert.True(small.Frames >= 1);
    }

    [Fact]
    public void AnUnplannableTakeIsRefusedBeforeAnythingRenders()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArVideoRenderProfile.Plan(0, 360, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArVideoRenderProfile.Plan(640, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArVideoRenderProfile.Plan(640, 360, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArVideoRenderProfile.Plan(640, 360, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArVideoRenderProfile.Plan(640, 360, 1, downsample: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArVideoRenderProfile.Plan(640, 360, 1, downsample: 8));
    }

    [Fact]
    public void TheRendererIsCheckedAgainstThePlanBeforeATake()
    {
        var plan = ArVideoRenderProfile.Plan(1280, 720, 1, 24, 2, maxSide: 640);
        var fake = new FakeRecorder(320, 180, 24);
        plan.MustFit(fake);                                                  // exact fit: no complaint
        Assert.Throws<ArgumentException>(() => plan.MustFit(new FakeRecorder(640, 360, 24)));
        Assert.Throws<ArgumentException>(() => plan.MustFit(new FakeRecorder(320, 180, 30)));
    }

    /// <summary>Just enough of a recorder to let the plan check run in a portable test.</summary>
    private sealed class FakeRecorder(int width, int height, int frameRate) : IFrameRecorder
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public int FrameRate { get; } = frameRate;
        public int FrameCount { get; private set; }
        public long BytesWritten { get; private set; }
        public bool IsNearSizeLimit => false;
        public string OutputPath => "fake";
        public int FrameBytes => Width * Height * 4;
        public bool HasAlpha => true;
        public void WriteFrame(byte[] frame, int repeat = 1) { FrameCount += repeat; BytesWritten += frame.Length * repeat; }
        public void Dispose() { }
    }
}
