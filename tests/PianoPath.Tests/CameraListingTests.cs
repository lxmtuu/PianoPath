using Xunit;

namespace PianoPath.Tests;

public sealed class CameraListingTests
{
    private static readonly string[] Names = ["Integrated IR Camera", "Integrated Camera", "OBS Virtual Camera"];
    private static readonly string[] Links = ["ir", "web", "obs"];

    [Fact]
    public void AutomaticComesFirstThenEveryCameraByItsOwnNameInTheDriversOrder()
    {
        var rows = CameraListing.Build(Names, Links, "", "");
        Assert.Equal(4, rows.Count);
        Assert.True(rows[0].IsAutomatic);
        Assert.Equal(new[] { "Integrated IR Camera", "Integrated Camera", "OBS Virtual Camera" }, rows.Skip(1).Select(row => row.Name).ToArray());
        Assert.Equal(new[] { "ir", "web", "obs" }, rows.Skip(1).Select(row => row.Link).ToArray());
    }

    [Fact]
    public void AutomaticSaysWhichCameraItPicksNotTheInfraredOneListedFirst()
        => Assert.Equal("Integrated Camera", CameraListing.Build(Names, Links, "", "")[0].Picks);

    [Fact]
    public void EachCameraIsMarkedWithItsKind()
    {
        var kinds = CameraListing.Build(Names, Links, "", "").Skip(1).Select(row => row.Kind).ToArray();
        Assert.Equal(new[] { CameraKind.Infrared, CameraKind.Colour, CameraKind.Virtual }, kinds);
    }

    [Fact]
    public void WithNoStoredPickAutomaticIsTheSelectedLineAndNoneOtherIs()
    {
        var rows = CameraListing.Build(Names, Links, "web", "");
        Assert.Equal(new[] { true, false, false, false }, rows.Select(row => row.Selected).ToArray());
        Assert.Equal(new[] { true, false, true, false }, rows.Select(row => row.InUse).ToArray());   // automatic and the webcam it opened
    }

    [Fact]
    public void AStoredPickIsTheSelectedLineEvenWhenAnotherCameraIsRunning()
    {
        var rows = CameraListing.Build(Names, Links, "web", "OBS");   // the link is compared ignoring case
        Assert.Equal(new[] { false, false, false, true }, rows.Select(row => row.Selected).ToArray());
        Assert.False(rows[0].InUse);
        Assert.True(rows[2].InUse);
    }

    [Fact]
    public void AnUnpluggedStoredCameraLeavesNothingSelected()
        => Assert.DoesNotContain(CameraListing.Build(Names, Links, "", "gone"), row => row.Selected);

    [Fact]
    public void NothingRunningMarksNothingInUse()
        => Assert.DoesNotContain(CameraListing.Build(Names, Links, "", "web"), row => row.InUse);

    [Fact]
    public void ACameraWithNoLinkCannotBePickedSoItIsLeftOut()
    {
        var rows = CameraListing.Build(["A", "B"], ["", "b"], "", "");
        Assert.Equal(new[] { "", "b" }, rows.Select(row => row.Link).ToArray());
        Assert.Equal("B", rows[0].Picks);
    }

    [Fact]
    public void ACameraWithNoNameStillGetsALineToClick()
    {
        var rows = CameraListing.Build(["", " "], ["x", "y"], "", "");
        Assert.Equal(new[] { "Unnamed camera", "Unnamed camera (2)" }, rows.Skip(1).Select(row => row.Name).ToArray());
    }

    [Fact]
    public void TwoCamerasWithTheSameNameAreToldApartInTheList()
    {
        var rows = CameraListing.Build(["HD Webcam", "HD Webcam"], ["a", "b"], "", "");
        Assert.Equal(new[] { "HD Webcam", "HD Webcam (2)" }, rows.Skip(1).Select(row => row.Name).ToArray());
        Assert.Equal("HD Webcam", rows[0].Picks);
    }

    [Fact]
    public void EachLineCarriesTheHardwareOfItsCamera()
    {
        var rows = CameraListing.Build(["A", "B"], ["", "b"], "", "", hardware: ["USB · VID_1&PID_2", "USB · VID_3&PID_4"]);
        Assert.Equal("", rows[0].Hardware);
        Assert.Equal("USB · VID_3&PID_4", rows[1].Hardware);   // the camera without a link is left out, so "b" keeps ITS hardware
    }

    [Fact]
    public void NoCamerasLeavesJustAutomaticWithNothingToPick()
    {
        var rows = CameraListing.Build([], [], "", "");
        Assert.Single(rows);
        Assert.Null(rows[0].Picks);
        Assert.True(rows[0].Selected);
    }

    [Fact]
    public void ACameraThatStartsAppearsInTheChangesAndOneThatStopsLeaves()
    {
        (string, string)[] webcam = [("web", "Integrated Camera")];
        (string, string)[] both = [("web", "Integrated Camera"), ("obs", "OBS Virtual Camera")];
        var started = CameraListing.Changes(webcam, both);
        Assert.Equal(new[] { "OBS Virtual Camera" }, started.Added.ToArray());
        Assert.Empty(started.Removed);
        var stopped = CameraListing.Changes(both, webcam);
        Assert.Empty(stopped.Added);
        Assert.Equal(new[] { "OBS Virtual Camera" }, stopped.Removed.ToArray());
    }

    [Fact]
    public void NoChangeAndLinkCaseAreNotChanges()
    {
        (string, string)[] a = [("WEB", "Cam")];
        (string, string)[] b = [("web", "Cam")];
        var same = CameraListing.Changes(a, b);
        Assert.Empty(same.Added);
        Assert.Empty(same.Removed);
    }
}
