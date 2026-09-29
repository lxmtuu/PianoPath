using System.Windows.Controls;

namespace PianoPath;

/// <summary>
/// One entry of a device picker: an English identity plus the caption to show.
///
/// <para>
/// Device <em>names</em> come from Windows and are never translated, but the two placeholder rows
/// ("Computer keyboard only", "No MIDI output") must follow the interface language. Keeping the
/// identity apart from the caption means a language switch rebuilds only the display, while the
/// selected device — and everything the app compares it against — stays the same English string.
/// </para>
/// </summary>
/// <param name="Id">Stable identity: the placeholder key or the real device name from WinMM.</param>
/// <param name="Display">Caption printed by the combo box, in the active language.</param>
internal sealed record DeviceOption(string Id, string Display)
{
    /// <summary>A localized caption over a fixed English identity, used for the "no device" rows.</summary>
    internal static DeviceOption Placeholder(string key) => new(key, Loc.T(key));

    /// <summary>Caption of the current selection, for labels such as <c>Listening · Roland FP-30</c>.</summary>
    internal static string Name(ComboBox combo) => combo.SelectedItem is DeviceOption option ? option.Display : "";
}
