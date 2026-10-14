using PianoPath;
using Xunit;

namespace PianoPath.Tests;

/// <summary>
/// The update check's decision, without a network: how a tag reads, when a release counts as newer, and
/// that a reply can only ever lead to one of three answers, with a page of Keyflow's own repository.
/// </summary>
public sealed class UpdateReleaseTests
{
    [Theory]
    [InlineData("v2.0.1", 2, 0, 1, 0)]
    [InlineData("2.0", 2, 0, 0, 0)]
    [InlineData("V1.0.0-final", 1, 0, 0, 0)]
    [InlineData("1.2.3+build7", 1, 2, 3, 0)]
    public void Tags_read_to_their_number(string tag, int major, int minor, int build, int revision)
    {
        Assert.Equal(new System.Version(major, minor, build, revision), UpdateRelease.ParseVersion(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("2.x.1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.0.0")]
    public void Text_that_is_not_a_version_reads_as_nothing(string text)
    {
        Assert.Null(UpdateRelease.ParseVersion(text));
    }

    [Theory]
    [InlineData("2.0.1", "v2.0.2", true)]
    [InlineData("2.0.1", "v2.1.0", true)]
    [InlineData("2.0.1", "v3.0.0", true)]
    [InlineData("2.0.1", "v2.0.1", false)]
    [InlineData("2.0.1", "v2.0.0", false)]
    [InlineData("2.0.1", "v2.0", false)]
    [InlineData("2.0.10", "v2.0.9", false)]
    [InlineData("2.0.1", "garbage", false)]
    public void A_release_counts_as_newer_only_when_strictly_higher(string current, string latest, bool expected)
    {
        Assert.Equal(expected, UpdateRelease.IsNewer(current, latest));
    }

    [Fact]
    public void A_newer_release_is_reported_available_with_its_own_page()
    {
        const string json = """
{"tag_name":"v2.0.2","html_url":"https://github.com/lxmtuu/KeyFlow/releases/tag/v2.0.2","draft":false}
""";
        var result = UpdateRelease.Interpret(json, "2.0.1");

        Assert.Equal(UpdateState.Available, result.State);
        Assert.Equal("2.0.2", result.Latest);
        Assert.Equal("https://github.com/lxmtuu/KeyFlow/releases/tag/v2.0.2", result.Url);
    }

    [Fact]
    public void The_current_release_reads_as_up_to_date()
    {
        var result = UpdateRelease.Interpret("""{"tag_name":"v2.0.1","html_url":"https://github.com/lxmtuu/KeyFlow/releases/tag/v2.0.1"}""", "2.0.1");

        Assert.Equal(UpdateState.UpToDate, result.State);
        Assert.Equal("2.0.1", result.Latest);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"html_url":"https://github.com/lxmtuu/KeyFlow/releases/tag/v9.9.9"}""")]
    [InlineData("""{"tag_name":42}""")]
    [InlineData("""{"tag_name":"nightly"}""")]
    public void A_reply_that_is_not_a_release_is_a_failure_never_a_false_up_to_date(string json)
    {
        var result = UpdateRelease.Interpret(json, "2.0.1");

        Assert.Equal(UpdateState.Failed, result.State);
        Assert.Equal(UpdateRelease.ReleasesPage, result.Url);
    }

    [Theory]
    [InlineData("https://evil.example.com/setup.exe")]
    [InlineData("https://github.com/someone-else/KeyFlow/releases/tag/v9.9.9")]
    [InlineData("https://github.com/lxmtuu/KeyFlow/releasesX")]
    [InlineData("javascript:alert(1)")]
    public void A_page_outside_the_repository_is_replaced_by_its_releases_page(string url)
    {
        var json = $"{{\"tag_name\":\"v9.9.9\",\"html_url\":\"{url}\"}}";
        var result = UpdateRelease.Interpret(json, "2.0.1");

        Assert.Equal(UpdateState.Available, result.State);
        Assert.Equal(UpdateRelease.ReleasesPage, result.Url);
    }
}
