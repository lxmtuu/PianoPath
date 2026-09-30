using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Interface language: the picker on the General page, the chip on the startup menu, and the
/// re-translation of every surface that is already on screen.
///
/// <para>
/// Switching language never restarts the app and never touches what is stored: the settings file,
/// the presets, the theme ids and the MIDI files all keep their English identifiers, and the new
/// language is written to <see cref="PianoVisualSettings.Language"/> only so the next launch opens
/// the same way. Labels registered through <see cref="Loc"/> repaint by themselves; the surfaces
/// below are the ones that also have to <em>recompute</em> something (a score line, a preset
/// description, the dock navigation captions) before they can be repainted.
/// </para>
/// </summary>
public partial class MainWindow
{
    private WrapPanel? _languageChipHost;
    private TextBlock? _languageBlurb;

    /// <summary>
    /// Applies a language id and repaints the interface. An empty id means "follow Windows": the
    /// language is resolved from the system on every launch until the user picks one explicitly.
    /// </summary>
    internal void ApplyLanguage(string id)
    {
        _visualSettings.Language = id;
        Loc.Apply(id);
        RetranslateSurfaces();
        ApplyVisualSettings("Language switched to {0}", false, Loc.Current.Display);
    }

    /// <summary>
    /// Rebuilds everything whose text is composed at runtime — score lines, preset descriptions,
    /// device and track pickers, the navigation captions of the dock — after
    /// <see cref="Loc.Refresh"/> has already repainted every label it tracks.
    /// </summary>
    private void RetranslateSurfaces()
    {
        Loc.LocalizeTree(this);
        Loc.Refresh();

        RefreshLanguageChips();
        RefreshThemeChips();
        RefreshMenuThemeChips();
        RefreshPlayThemeChips();
        RefreshMenuStageLook();
        RefreshSectionHeaders();
        RefreshShortcutCard();
        RefreshChoiceCaptions();
        RefreshDeviceCaptions();

        LoadPresetList();
        RefreshRecentSongs();
        RefreshLibrarySongs();
        RefreshPracticeHistory();
        RefreshSettingControls();
        UpdateSoundFontUi();
        UpdatePlaybackLabel();
        PopulateTracks(preserve: true);
        UpdateStats();
        UpdateTime();
        UpdateLoopLabel();
        UpdateRecordingInfo();
        SyncPlayInlineControls();
    }

    // =================================================================================================
    // The chip row on the startup menu: the first thing a new user sees, so it is also the quickest
    // way to run Keyflow in their own language without knowing where the dock hides its pages.
    // =================================================================================================

    private void RefreshMenuLanguageChips()
    {
        if (MenuLanguageHost is null) return;
        MenuLanguageHost.Children.Clear();
        foreach (var (id, caption) in LanguageChoices()) MenuLanguageHost.Children.Add(LanguageChip(id, caption));
    }

    // =================================================================================================
    // Two surfaces whose text is painted by a data template rather than by a bound element
    // =================================================================================================

    /// <summary>
    /// Prints the group captions of the dock navigation (STAGE DESIGN, SOUND &amp; INPUT, …). They
    /// live in the item template, bound to the attached property of <see cref="SettingsPages"/>, so
    /// they are repainted here instead of through <see cref="Loc"/>.
    /// </summary>
    private void RefreshSectionHeaders()
    {
        foreach (var tab in SettingsTabs.Items.OfType<TabItem>())
        {
            if (SettingsPages.GetSection(tab) is not { } section) continue;
            foreach (var label in Descendants<TextBlock>(tab, "SectionLabel")) label.Text = Loc.Page(section);
        }
    }

    /// <summary>
    /// Re-captions the device pickers (the two placeholder rows are translated; real device names come
    /// from Windows) without closing and reopening the MIDI port.
    /// </summary>
    private void RefreshDeviceCaptions()
    {
        RefreshDeviceCaption(InputDeviceCombo, "Computer keyboard only");
        RefreshDeviceCaption(OutputDeviceCombo, "No MIDI output");
    }

    private void RefreshDeviceCaption(ComboBox combo, string placeholder)
    {
        if (combo?.ItemsSource is not IEnumerable<DeviceOption> items) return;
        var selected = combo.SelectedIndex;
        var rebuilt = items.Select(item => item.Id == placeholder ? DeviceOption.Placeholder(placeholder) : item).ToList();
        _suppressDevices = true;
        try { combo.ItemsSource = rebuilt; combo.SelectedIndex = selected; }
        finally { _suppressDevices = false; }
    }

    /// <summary>Depth-first search of the visual tree, optionally by the name a template gave a part.</summary>
    private static IEnumerable<T> Descendants<T>(DependencyObject root, string? name = null) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && (name is null || string.Equals(element.Name, name, StringComparison.Ordinal))) yield return element;
            foreach (var nested in Descendants<T>(child, name)) yield return nested;
        }
    }
}
