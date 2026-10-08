namespace PianoPath;

/// <summary>
/// One way a camera can look at the keyboard: a calibration profile. The four corners marked on the picture mean
/// nothing once the camera is turned to another angle, so every view keeps its own key area under its own id.
///
/// <para>
/// A view is not a look: which view the overlay uses belongs to this computer's camera, exactly as the marked key area
/// does, so applying a preset or pasting a share code never changes it (see <c>PianoVisualSettings.CopyFrom</c> and
/// <c>ClearMachineLocal</c>).
/// </para>
/// </summary>
/// <param name="Id">The stable name written to the settings and to <c>ar-calibration.json</c>; never shown to anyone.</param>
/// <param name="Name">The English name, which is a key of the string tables; the picker prints its translation.</param>
internal sealed record ArCalibrationProfile(string Id, string Name);

/// <summary>
/// The views the app knows. A fixed catalogue rather than a list the person adds to, because the id is written into
/// the calibration file: a view the app does not know can only be an entry it has to refuse (see
/// <see cref="ArCalibrationStore"/>), and a fixed catalogue is the one shape where that cannot happen by accident.
/// </summary>
internal static class ArCalibrationProfiles
{
    /// <summary>
    /// The view every calibration marked before views existed belongs to. It is also the one the app starts on, so a
    /// person who had already marked their keys keeps playing with those corners and nothing asks them to mark again.
    /// </summary>
    internal const string LegacyId = "default";

    /// <summary>The key area marked before the app knew about views, or a camera whose angle was never named.</summary>
    internal static readonly ArCalibrationProfile Legacy = new(LegacyId, "Default");

    /// <summary>The camera stands above and behind the player, looking down at the keys at about 45°.</summary>
    internal static readonly ArCalibrationProfile Tilt45 = new("tilt45", "45°");

    /// <summary>The camera hangs straight above the keys, looking down at 90°.</summary>
    internal static readonly ArCalibrationProfile Overhead90 = new("overhead90", "90° overhead");

    /// <summary>Every view, in the order the picker shows them.</summary>
    internal static readonly ArCalibrationProfile[] All = [Legacy, Tilt45, Overhead90];

    /// <summary>The view with this id, or null when the app does not know it. An id is compared without regard to case, because it comes out of a text file.</summary>
    internal static ArCalibrationProfile? ById(string? id) =>
        All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The view a stored id names; <see cref="Legacy"/> for anything else, including an empty id.</summary>
    internal static ArCalibrationProfile Normalize(string? id) => ById(id) ?? Legacy;

    /// <summary>Whether a stored id names a view the app knows.</summary>
    internal static bool IsKnown(string? id) => ById(id) is not null;
}
