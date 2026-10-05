namespace PianoPath;

/// <summary>What kind of camera a device is, from its name: the list shows it so nobody has to guess why a camera is not picked.</summary>
internal enum CameraKind { Colour, Virtual, Infrared }

/// <summary>
/// One line of the camera list in the settings. <paramref name="IsAutomatic"/> marks the first line, "Automatic", which has no
/// link of its own and says in <paramref name="Picks"/> which camera it would open.
/// </summary>
internal sealed record CameraRow(string Link, string Name, CameraKind Kind, bool IsAutomatic, bool InUse, bool Selected, string? Picks, string Hardware = "");

/// <summary>Builds the lines of the camera list from what the machine reports, what is running and what the user picked.</summary>
internal static class CameraListing
{
    /// <summary>
    /// "Automatic" first, then every camera that has a link (one that has none cannot be picked) in the driver's order, each
    /// with its name. <paramref name="runningLink"/> is the camera the overlay has open ("" when none) and
    /// <paramref name="storedLink"/> the user's pick ("" for automatic). Exactly one line is Selected unless the stored camera
    /// is unplugged, in which case none is.
    /// </summary>
    internal static IReadOnlyList<CameraRow> Build(IReadOnlyList<string> names, IReadOnlyList<string> links, string runningLink, string storedLink, IReadOnlyList<string>? hardware = null)
    {
        var count = Math.Min(names.Count, links.Count);
        var listed = Enumerable.Range(0, count).Where(index => links[index].Length > 0).ToList();
        // Every camera is called by its name; one the driver left unnamed is "Unnamed camera", never a bare number, and two alike stay apart.
        var listedNames = CameraNames.Disambiguate(listed.Select(index => NameOrFallback(names[index])).ToList()).ToList();
        var best = CameraRanking.Order(listedNames);
        var automatic = string.IsNullOrEmpty(storedLink);
        var rows = new List<CameraRow>
        {
            new("", "", CameraKind.Colour, IsAutomatic: true, InUse: automatic && runningLink.Length > 0, Selected: automatic,
                Picks: best.Count > 0 ? listedNames[best[0]] : null),
        };
        for (var position = 0; position < listed.Count; position++)
        {
            var link = links[listed[position]];
            var name = listedNames[position];
            var kind = CameraRanking.IsInfrared(name) ? CameraKind.Infrared : CameraRanking.IsVirtual(name) ? CameraKind.Virtual : CameraKind.Colour;
            var same = runningLink.Length > 0 && string.Equals(link, runningLink, StringComparison.OrdinalIgnoreCase);
            var chosen = !automatic && string.Equals(link, storedLink, StringComparison.OrdinalIgnoreCase);
            rows.Add(new CameraRow(link, name, kind, IsAutomatic: false, InUse: same, Selected: chosen, Picks: null,
                Hardware: hardware is not null && listed[position] < hardware.Count ? hardware[listed[position]] : ""));
        }
        return rows;
    }

    /// <summary>A camera whose driver gives no name at all still gets a line to click.</summary>
    private static string NameOrFallback(string name) => string.IsNullOrWhiteSpace(name) ? "Unnamed camera" : name.Trim();

    /// <summary>
    /// What changed between two readings of the machine's cameras, by link (ignoring case): the names of the cameras that
    /// appeared and of those that went. A virtual camera such as OBS shows up here the moment its program starts it.
    /// </summary>
    internal static (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) Changes(IReadOnlyList<(string Link, string Name)> before, IReadOnlyList<(string Link, string Name)> after)
    {
        var was = new HashSet<string>(before.Select(camera => camera.Link), StringComparer.OrdinalIgnoreCase);
        var now = new HashSet<string>(after.Select(camera => camera.Link), StringComparer.OrdinalIgnoreCase);
        return (after.Where(camera => !was.Contains(camera.Link)).Select(camera => camera.Name).ToList(),
                before.Where(camera => !now.Contains(camera.Link)).Select(camera => camera.Name).ToList());
    }
}
