namespace PianoPath;

/// <summary>
/// The pages of the settings dock, in tab-strip order.
///
/// Page numbers used to be magic integers spread over the window, the play dialog and the command
/// line parser — every inserted page had to be tracked down in five files. The dock now refers to a
/// page by name and this class owns the order, so adding a page is a one-line change here plus the
/// matching <c>TabItem</c> in <c>MainWindow.xaml</c>.
/// </summary>
internal static class SettingsPages
{
    internal const string Style = "Style";
    internal const string Theme = "Theme";
    internal const string Notes = "Notes";
    internal const string Particles = "Particles";
    internal const string Keyboard = "Keyboard";
    internal const string Background = "Background";
    internal const string Camera = "Camera";
    internal const string Audio = "Audio";
    internal const string Midi = "MIDI";
    internal const string Practice = "Practice";
    internal const string Recording = "Recording";

    /// <summary>Tab-strip order; the index in this array is the <c>SettingsTabs.SelectedIndex</c>.</summary>
    internal static readonly string[] Order = [Style, Theme, Notes, Particles, Keyboard, Background, Camera, Audio, Midi, Practice, Recording];

    internal static int IndexOf(string? name) => Array.IndexOf(Order, name);
}
