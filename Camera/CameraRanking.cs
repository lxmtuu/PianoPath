namespace PianoPath;

/// <summary>
/// Which of the cameras on a machine the overlay should try first. Windows lists every capture device the same way, and
/// a laptop with Windows Hello lists its infrared face-login camera next to the webcam — often FIRST. Opening that one
/// gives a grey, tiny picture or nothing at all, so "the first camera" is a poor default. This puts the cameras a person
/// can use ahead of the ones they cannot, keeping the driver's order inside each group.
/// </summary>
internal static class CameraRanking
{
    private static readonly string[] InfraredWords = ["infrared", "infra-red", "windows hello", "face auth", "face recogn", "depth", "ir camera", "ir cam", "ir sensor", "(ir)", "ir-camera"];
    private static readonly string[] VirtualWords = ["virtual", "obs-camera", "snap camera", "manycam", "xsplit", "splitcam", "vcam", "nvidia broadcast", "streamlabs"];

    /// <summary>A camera that only sees infrared light (face login, depth sensors): it cannot show the keyboard.</summary>
    internal static bool IsInfrared(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var text = name.Trim().ToLowerInvariant();
        if (InfraredWords.Any(word => text.Contains(word, StringComparison.Ordinal))) return true;
        // A bare "IR" word: "Integrated IR Camera", "HP IR Camera", "IR" — but not "Fire", "Sirius" or "Air".
        return text.Split(new[] { ' ', '-', '_', '(', ')', '[', ']', ',', '.' }, StringSplitOptions.RemoveEmptyEntries).Contains("ir");
    }

    /// <summary>A software camera (OBS Virtual Camera, Snap Camera…): it shows another program's picture, often black.</summary>
    internal static bool IsVirtual(string name)
        => !string.IsNullOrWhiteSpace(name) && VirtualWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>0 = a physical colour camera, 1 = a virtual one, 2 = an infrared one.</summary>
    internal static int Class(string name) => IsInfrared(name) ? 2 : IsVirtual(name) ? 1 : 0;

    /// <summary>
    /// The order to try the cameras in, as indexes into <paramref name="names"/>: a camera whose index is
    /// <paramref name="preferred"/> (the one the user picked, or -1) comes first, then the physical cameras, then the
    /// virtual ones, then the infrared ones — each group in the order the driver listed them. Every index appears once.
    /// </summary>
    internal static IReadOnlyList<int> Order(IReadOnlyList<string> names, int preferred = -1)
    {
        var order = new List<int>(names.Count);
        if (preferred >= 0 && preferred < names.Count) order.Add(preferred);
        order.AddRange(Enumerable.Range(0, names.Count)
            .Where(index => index != preferred)
            .OrderBy(index => Class(names[index]))   // OrderBy is stable: the driver's order survives inside a group
            .ToList());
        return order;
    }

    /// <summary>The index of the camera whose link matches, ignoring case, or -1 when none does (or the link is empty).</summary>
    internal static int Find(IReadOnlyList<string> links, string link)
    {
        if (string.IsNullOrEmpty(link)) return -1;
        for (var index = 0; index < links.Count; index++)
            if (links[index].Length > 0 && string.Equals(links[index], link, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }

    /// <summary>
    /// Whether a change of the camera list calls for reopening the overlay: nothing is running and a camera is there now; the
    /// running camera is gone; or the camera the user picked has come back while another one is running.
    /// </summary>
    internal static bool ShouldReopen(IReadOnlyList<string> links, string runningLink, bool running, string preferredLink)
    {
        if (!running) return links.Count > 0;
        if (runningLink.Length > 0 && Find(links, runningLink) < 0) return true;
        return preferredLink.Length > 0 && Find(links, preferredLink) >= 0 && !string.Equals(preferredLink, runningLink, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A key that changes exactly when the set of cameras does (a camera plugged in or removed).</summary>
    internal static string Signature(IReadOnlyList<string> links) => string.Join("|", links.Select(link => link.ToLowerInvariant()).Order(StringComparer.Ordinal));
}
