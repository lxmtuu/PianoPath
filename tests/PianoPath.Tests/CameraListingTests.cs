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
        Assert.Equal(new[] { "Camera 1", "Camera 2" }, rows.Skip(1).Select(row => row.Name).ToArray());
    }

    [Fact]
    public void NoCamerasLeavesJustAutomaticWithNothingToPick()
    {
        var rows = CameraListing.Build([], [], "", "");
        Assert.Single(rows);
        Assert.Null(rows[0].Picks);
        Assert.True(rows[0].Selected);
    }
}
