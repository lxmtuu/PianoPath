using System.Net.Http;

namespace PianoPath;

/// <summary>
/// The one network request Keyflow ever makes on its own, and only when the person has turned on
/// "check for updates" (off by default). It asks GitHub's public releases API for the newest release
/// and reads back a version number and a page address. Nothing is downloaded or installed, and no
/// account, identifier or usage data is sent beyond the ordinary request itself.
/// </summary>
internal static class UpdateCheck
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Keyflow/" + AppInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// Asks GitHub for the newest release. Never throws: no network, a timeout, a refused request or a
    /// cancelled check all come back as <see cref="UpdateState.Failed"/>, because the product works the
    /// same without the check.
    /// </summary>
    internal static async Task<UpdateCheckResult> QueryAsync(CancellationToken cancellation = default)
    {
        try
        {
            using var response = await Http.GetAsync(UpdateRelease.LatestReleaseApi, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return UpdateRelease.Failure;
            var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            return UpdateRelease.Interpret(json, AppInfo.Version);
        }
        catch (HttpRequestException)
        {
            return UpdateRelease.Failure;
        }
        catch (TaskCanceledException)
        {
            return UpdateRelease.Failure;
        }
    }
}
