using System.Windows;

namespace PianoPath;

/// <summary>
/// One labelled group of dock pages, shown as a section header in the navigation column.
/// </summary>
/// <param name="Label">Section caption printed above the first page of the group.</param>
/// <param name="Pages">Page names of the group, in tab-strip order.</param>
internal sealed record SettingsSection(string Label, string[] Pages);

/// <summary>
/// The catalogue of the settings dock: every page, its section and its position.
///
/// Page numbers used to be magic integers spread over the window, the play dialog and the command
/// line parser — every inserted page had to be tracked down in five files. The dock now refers to a
/// page by name and this class owns the order <em>and</em> the grouping, so adding a page is one line
/// here plus the matching <c>TabItem</c> in <c>MainWindow.xaml</c> (carrying the same section label
/// through the <see cref="SectionProperty"/> attached property).
///
/// <see cref="Order"/> is derived from <see cref="Sections"/>, so a page can never be listed in
/// <see cref="Order"/> without belonging to a section — <c>tools/check_sources.py</c> additionally
/// proves that the XAML tab strip and this catalogue agree, and <c>VerificationSuite</c> asserts it
/// at runtime.
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

    /// <summary>Caption of the first group; also the fallback section for an unlabelled page.</summary>
    internal const string DesignSection = "STAGE DESIGN";
    internal const string SoundSection = "SOUND & INPUT";
    internal const string SessionSection = "SESSION";

    /// <summary>
    /// The navigation groups, in display order: what the stage looks like, what it sounds like and
    /// what the current practice session does. Grouping the eleven pages by intent is what keeps the
    /// dock readable as it grows.
    /// </summary>
    internal static readonly SettingsSection[] Sections =
    [
        new(DesignSection, [Style, Theme, Notes, Particles, Keyboard, Background, Camera]),
        new(SoundSection, [Audio, Midi]),
        new(SessionSection, [Practice, Recording]),
    ];

    /// <summary>Tab-strip order; the index in this array is the <c>SettingsTabs.SelectedIndex</c>.</summary>
    internal static readonly string[] Order = [.. Sections.SelectMany(section => section.Pages)];

    internal static int IndexOf(string? name) => Array.IndexOf(Order, name);

    /// <summary>The section a page belongs to, or <c>null</c> when the name is unknown.</summary>
    internal static SettingsSection? SectionOf(string? name) =>
        name is null ? null : Sections.FirstOrDefault(section => section.Pages.Contains(name));

    // =================================================================================================
    // The section label is an attached property so the navigation template can print the group header
    // on the first page of every section without a second, hand-maintained list in XAML.
    // =================================================================================================

    internal static readonly DependencyProperty SectionProperty = DependencyProperty.RegisterAttached(
        "Section", typeof(string), typeof(SettingsPages), new FrameworkPropertyMetadata(null));

    internal static void SetSection(DependencyObject element, string? value) => element.SetValue(SectionProperty, value);

    internal static string? GetSection(DependencyObject element) => (string?)element.GetValue(SectionProperty);
}
