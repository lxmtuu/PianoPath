using System.Globalization;
using System.Text.Json;

namespace PianoPath;

/// <summary>What the optional update check found.</summary>
internal enum UpdateState
{
    /// <summary>GitHub could not be asked, or answered with something that is not a release.</summary>
    Failed,
    /// <summary>The newest published release is not newer than the running build.</summary>
    UpToDate,
    /// <summary>A newer release exists; <see cref="UpdateCheckResult.Url"/> is its page.</summary>
    Available,
}

/// <param name="State">What the check found.</param>
/// <param name="Latest">The newest release's version number, without a leading <c>v</c>; empty on failure.</param>
/// <param name="Url">The release page to open. Always a Keyflow release page, never an address taken blindly from the reply.</param>
internal sealed record UpdateCheckResult(UpdateState State, string Latest, string Url);

/// <summary>
/// The parts of the update check that need no network: reading a release reply and deciding whether
/// it is newer than the running build. Kept apart from <see cref="UpdateCheck"/> so the decision can be
/// tested on its own, and so a reply that is malformed or hostile can only ever produce one of three
/// answers.
/// </summary>
internal static class UpdateRelease
{
    /// <summary>The public repository that carries the release packages (see <c>docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md</c>).</summary>
    internal const string Repository = "lxmtuu/KeyFlow";
    internal const string ReleasesPage = "https://github.com/" + Repository + "/releases";
    internal const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>
    /// Reads the version out of a tag such as <c>v2.0.1</c>, <c>2.0</c> or <c>v1.0.0-final</c>. A
    /// pre-release or build suffix is dropped, so only the number is compared. Returns <c>null</c> when
    /// the text is not a dotted list of up to four numbers.
    /// </summary>
    internal static Version? ParseVersion(string text)
    {
        var core = text.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+']);
        if (cut >= 0) core = core[..cut];
        var parts = core.Split('.');
        if (parts.Length is 0 or > 4) return null;
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return null;
        }
        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>True only when both versions read and <paramref name="latest"/> is strictly higher.</summary>
    internal static bool IsNewer(string current, string latest)
    {
        var mine = ParseVersion(current);
        var theirs = ParseVersion(latest);
        return mine is not null && theirs is not null && theirs > mine;
    }

    /// <summary>
    /// The release page is only ever opened when it is a page of <see cref="Repository"/>; anything else
    /// in the reply is replaced by the repository's own releases page.
    /// </summary>
    internal static string TrustedPage(string? url) =>
        url is not null && url.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) ? url : ReleasesPage;

    /// <summary>
    /// Reads GitHub's <c>releases/latest</c> reply and compares it with <paramref name="currentVersion"/>.
    /// Malformed JSON or a missing tag is <see cref="UpdateState.Failed"/>, never a false "up to date".
    /// </summary>
    internal static UpdateCheckResult Interpret(string json, string currentVersion)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Failure;
            if (!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return Failure;
            var tagText = tag.GetString() ?? "";
            var latest = ParseVersion(tagText);
            if (latest is null) return Failure;

            string? page = null;
            if (root.TryGetProperty("html_url", out var htmlUrl) && htmlUrl.ValueKind == JsonValueKind.String) page = htmlUrl.GetString();
            var number = tagText.Trim().TrimStart('v', 'V');
            var state = IsNewer(currentVersion, tagText) ? UpdateState.Available : UpdateState.UpToDate;
            return new UpdateCheckResult(state, number, TrustedPage(page));
        }
        catch (JsonException)
        {
            return Failure;
        }
    }

    /// <summary>What a check that could not complete reports.</summary>
    internal static UpdateCheckResult Failure { get; } = new(UpdateState.Failed, "", ReleasesPage);
}
