using Xunit;

namespace PianoPath.Tests;

public sealed class CameraNamesTests
{
    private const string Link = @"\\?\usb#vid_046d&pid_0825&mi_00#6&2d0b1d5f&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global";

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData(null, true)]
    [InlineData("Camera", true)]
    [InlineData("Camera 1", true)]
    [InlineData("camera 2", true)]
    [InlineData("Webcam 1", true)]
    [InlineData("Camera #3", true)]
    [InlineData("Integrated Camera", false)]
    [InlineData("Logitech C920 HD Pro Webcam", false)]
    [InlineData("USB Video Device", false)]
    [InlineData("OBS Virtual Camera", false)]
    public void AStandInIsNotAName(string? name, bool generic) => Assert.Equal(generic, CameraNames.IsGeneric(name));

    [Fact]
    public void TheInstancePathIsTheLinkWithoutItsPrefixAndInterfaceGuid()
        => Assert.Equal(@"usb\vid_046d&pid_0825&mi_00\6&2d0b1d5f&0&0000", CameraNames.InstancePath(Link));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData(@"\\?\usb#only")]
    public void ALinkWithoutTheUsualShapeHasNoInstancePath(string? link) => Assert.Null(CameraNames.InstancePath(link));

    [Fact]
    public void TheHardwareTextNamesTheBusAndTheVendorAndProductIds()
    {
        Assert.Equal("USB · VID_046D&PID_0825", CameraNames.Hardware(Link));
        Assert.Equal("SWD", CameraNames.Hardware(@"\\?\swd#vcam#obs-virtual-cam#{e5323777-f976-4f5b-9b55-b94699c46e44}\global"));
        Assert.Equal("", CameraNames.Hardware("nonsense"));
    }

    [Theory]
    [InlineData("@oem12.inf,%usbvideo.devicedesc%;USB Video Device", "USB Video Device")]
    [InlineData("@oem12.inf,%name%;HD Webcam;extra", "extra")]
    [InlineData("Plain Name", "Plain Name")]
    [InlineData("@oem12.inf,%unresolved%", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ADescriptionIsReducedToItsReadablePart(string? raw, string expected) => Assert.Equal(expected, CameraNames.CleanDescription(raw));

    [Fact]
    public void TheFirstRealNameWinsInTheOrderGiven()
    {
        Assert.Equal("Logitech C920", CameraNames.Resolve(Link, "Logitech C920", "Other"));
        Assert.Equal("HD Pro Webcam C920", CameraNames.Resolve(Link, "Camera 1", "", "HD Pro Webcam C920", "USB Video Device"));
        Assert.Equal("USB Video Device", CameraNames.Resolve(Link, null, "Camera 2", "USB Video Device"));
    }

    [Fact]
    public void WithNoRealNameTheCameraIsNamedAfterItsHardwareNotANumber()
    {
        Assert.Equal("USB camera (VID_046D&PID_0825)", CameraNames.Resolve(Link, "Camera 1", null, ""));
        Assert.Equal("SWD camera", CameraNames.Resolve(@"\\?\swd#vcam#obs-virtual-cam#{e5323777-f976-4f5b-9b55-b94699c46e44}\global", "Camera 2"));
    }

    [Fact]
    public void WithNothingAtAllTheStandInTheDriverGaveIsKeptElseUnnamed()
    {
        Assert.Equal("Camera 1", CameraNames.Resolve("nonsense", "Camera 1", ""));
        Assert.Equal("Unnamed camera", CameraNames.Resolve("nonsense", null, " "));
    }

    [Fact]
    public void TwoCamerasWithTheSameNameStayTellableApart()
    {
        Assert.Equal(new[] { "HD Webcam", "HD Webcam (2)", "Other", "HD Webcam (3)" }, CameraNames.Disambiguate(["HD Webcam", "HD Webcam", "Other", "HD Webcam"]).ToArray());
        Assert.Equal(new[] { "A", "B" }, CameraNames.Disambiguate(["A", "B"]).ToArray());
    }
}
