using System.Text.RegularExpressions;

namespace PianoPath;

/// <summary>
/// Working out what to call a camera. Media Foundation hands out a "friendly name" for each device, but a driver can leave
/// it empty or give a stand-in like "Camera 1" — which tells nobody which camera is which. So the name is looked for in
/// more places (the device's own registry entry, found through the symbolic link Windows gives every capture device),
/// and when nothing better exists the camera is named after its hardware (its USB vendor and product ids) instead of a
/// number. Everything here works on text only, so it can be tested without a camera.
/// </summary>
internal static partial class CameraNames
{
    /// <summary>The start of the identity given to a camera that has no symbolic link: the prefix, then its name.</summary>
    internal const string NameLinkPrefix = "name:";

    /// <summary>"Camera", "Camera 2", "Webcam 1": a stand-in, not a name.</summary>
    internal static bool IsGeneric(string? name)
        => string.IsNullOrWhiteSpace(name) || GenericName().IsMatch(name.Trim());

    [GeneratedRegex(@"^(camera|webcam|video device|capture device|device)\s*#?\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericName();

    /// <summary>
    /// The device-instance path a capture device's symbolic link points at — the key under
    /// <c>HKLM\SYSTEM\CurrentControlSet\Enum</c> — or null when the link has no such shape.
    /// <c>\\?\usb#vid_046d&amp;pid_0825&amp;mi_00#6&amp;2d0b#{e532…}\global</c> gives <c>usb\vid_046d&amp;pid_0825&amp;mi_00\6&amp;2d0b</c>.
    /// </summary>
    internal static string? InstancePath(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        var text = link.Trim();
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal)) text = text[4..];
        var cut = text.LastIndexOf("#{", StringComparison.Ordinal);
        if (cut > 0) text = text[..cut];
        var parts = text.Split('#', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 ? string.Join('\\', parts.Take(3)) : null;
    }

    /// <summary>The bus and the vendor/product ids of the hardware behind a link ("USB · VID_046D&amp;PID_0825"), or "" when the link says none.</summary>
    internal static string Hardware(string? link)
    {
        var path = InstancePath(link);
        if (path is null) return "";
        var parts = path.Split('\\');
        var bus = parts[0].ToUpperInvariant();
        var ids = parts[1].ToUpperInvariant().Split('&').Where(part => part.StartsWith("VID_", StringComparison.Ordinal) || part.StartsWith("PID_", StringComparison.Ordinal)).ToList();
        return ids.Count == 0 ? bus : $"{bus} · {string.Join('&', ids)}";
    }

    /// <summary>
    /// A driver's description as the registry keeps it — <c>@oem12.inf,%usbvideo.devicedesc%;USB Video Device</c> — reduced to the
    /// readable part after the last semicolon. A value that is only an unresolved reference gives "".
    /// </summary>
    internal static string CleanDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = raw.Trim();
        var semicolon = text.LastIndexOf(';');
        if (semicolon >= 0) return text[(semicolon + 1)..].Trim();
        return text.StartsWith('@') ? "" : text;
    }

    /// <summary>
    /// The name to show: the first of the candidates that is a real name (not blank, not "Camera 1"), in the order given; with none
    /// of those, a name built from the hardware ("USB camera (VID_046D&amp;PID_0825)"); with no hardware either, "Unnamed camera".
    /// A stand-in the driver did give is still better than nothing when the hardware says nothing.
    /// </summary>
    internal static string Resolve(string? link, params string?[] candidates)
    {
        foreach (var candidate in candidates)
            if (!IsGeneric(candidate)) return candidate!.Trim();
        var hardware = Hardware(link);
        if (hardware.Length > 0)
        {
            var ids = hardware.Contains(" · ") ? hardware[(hardware.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : "";
            var bus = hardware.Contains(" · ") ? hardware[..hardware.IndexOf(" · ", StringComparison.Ordinal)] : hardware;
            return ids.Length > 0 ? $"{bus} camera ({ids})" : $"{bus} camera";
        }
        var standIn = candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        return standIn?.Trim() ?? "Unnamed camera";
    }

    /// <summary>
    /// Two cameras with the same name (two of the same webcam) must stay tellable apart: the second gets " (2)", the third " (3)".
    /// A name that is already unique is left alone.
    /// </summary>
    internal static IReadOnlyList<string> Disambiguate(IReadOnlyList<string> names)
    {
        var total = names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            seen[name] = seen.GetValueOrDefault(name) + 1;
            result.Add(total[name] > 1 && seen[name] > 1 ? $"{name} ({seen[name]})" : name);
        }
        return result;
    }
}
