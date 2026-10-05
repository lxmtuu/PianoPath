using Xunit;

namespace PianoPath.Tests;

public sealed class CameraRankingTests
{
    [Theory]
    [InlineData("Integrated IR Camera", true)]
    [InlineData("HP IR Camera", true)]
    [InlineData("IR", true)]
    [InlineData("Integrated Camera (IR)", true)]
    [InlineData("Intel(R) RealSense(TM) Depth Camera 435", true)]
    [InlineData("Windows Hello Face Software Device", true)]
    [InlineData("USB Infrared Camera", true)]
    [InlineData("Integrated Camera", false)]
    [InlineData("Logitech C920 HD Pro Webcam", false)]
    [InlineData("HD Webcam", false)]
    [InlineData("Fire TV Camera", false)]
    [InlineData("Sirius Cam", false)]
    [InlineData("Air Cam", false)]
    [InlineData("", false)]
    public void InfraredNamesAreRecognisedAndOrdinaryOnesAreNot(string name, bool infrared)
        => Assert.Equal(infrared, CameraRanking.IsInfrared(name));

    [Theory]
    [InlineData("OBS Virtual Camera", true)]
    [InlineData("Snap Camera", true)]
    [InlineData("NVIDIA Broadcast", true)]
    [InlineData("Logitech C920 HD Pro Webcam", false)]
    public void VirtualCamerasAreRecognised(string name, bool isVirtual) => Assert.Equal(isVirtual, CameraRanking.IsVirtual(name));

    [Fact]
    public void AnInfraredCameraListedFirstIsTriedLast()
    {
        // The usual Windows Hello laptop: the IR camera comes first in the driver's list.
        var names = new[] { "Integrated IR Camera", "Integrated Camera" };
        Assert.Equal(new[] { 1, 0 }, CameraRanking.Order(names).ToArray());
    }

    [Fact]
    public void PhysicalThenVirtualThenInfraredKeepingTheDriversOrderInsideAGroup()
    {
        var names = new[] { "IR Camera", "OBS Virtual Camera", "Webcam B", "Webcam A", "Snap Camera" };
        Assert.Equal(new[] { 2, 3, 1, 4, 0 }, CameraRanking.Order(names).ToArray());
    }

    [Fact]
    public void ThePickedCameraGoesFirstEvenWhenItIsInfrared()
    {
        var names = new[] { "Integrated Camera", "Integrated IR Camera" };
        Assert.Equal(new[] { 1, 0 }, CameraRanking.Order(names, preferred: 1).ToArray());
        Assert.Equal(new[] { 0, 1 }, CameraRanking.Order(names, preferred: 0).ToArray());
    }

    [Fact]
    public void EveryCameraAppearsExactlyOnceAndAnOutOfRangePreferenceIsIgnored()
    {
        var names = new[] { "A", "B IR", "C", "D virtual" };
        foreach (var preferred in new[] { -1, 0, 1, 2, 3, 4, 99 })
            Assert.Equal(new[] { 0, 1, 2, 3 }, CameraRanking.Order(names, preferred).Order().ToArray());
        Assert.Equal(new[] { 0, 2, 3, 1 }, CameraRanking.Order(names, 99).ToArray());
        Assert.Empty(CameraRanking.Order([]));
    }

    [Fact]
    public void FindMatchesLinksIgnoringCaseAndNeverMatchesAnEmptyLink()
    {
        var links = new[] { "", "\\\\?\\USB#VID_046D", "\\\\?\\usb#vid_1234" };
        Assert.Equal(1, CameraRanking.Find(links, "\\\\?\\usb#vid_046d"));
        Assert.Equal(2, CameraRanking.Find(links, "\\\\?\\USB#VID_1234"));
        Assert.Equal(-1, CameraRanking.Find(links, ""));
        Assert.Equal(-1, CameraRanking.Find(links, "other"));
    }

    [Fact]
    public void NothingRunningReopensAsSoonAsACameraAppears()
    {
        Assert.True(CameraRanking.ShouldReopen(["a"], "", running: false, preferredLink: ""));
        Assert.False(CameraRanking.ShouldReopen([], "", running: false, preferredLink: ""));
    }

    [Fact]
    public void ARunningCameraThatWasUnpluggedReopensOnAnother()
    {
        Assert.True(CameraRanking.ShouldReopen(["b"], "a", running: true, preferredLink: ""));
        Assert.False(CameraRanking.ShouldReopen(["a", "b"], "a", running: true, preferredLink: ""));
    }

    [Fact]
    public void ThePickedCameraComingBackTakesOverFromTheFallback()
    {
        Assert.True(CameraRanking.ShouldReopen(["a", "b"], "a", running: true, preferredLink: "b"));
        Assert.False(CameraRanking.ShouldReopen(["a"], "a", running: true, preferredLink: "b"));   // not back yet
        Assert.False(CameraRanking.ShouldReopen(["a", "b"], "b", running: true, preferredLink: "B"));
    }

    [Fact]
    public void TheSignatureChangesWithTheSetOfCamerasButNotWithTheirOrderOrCase()
    {
        Assert.Equal(CameraRanking.Signature(["A", "b"]), CameraRanking.Signature(["B", "a"]));
        Assert.NotEqual(CameraRanking.Signature(["a"]), CameraRanking.Signature(["a", "b"]));
        Assert.Equal("", CameraRanking.Signature([]));
    }
}
