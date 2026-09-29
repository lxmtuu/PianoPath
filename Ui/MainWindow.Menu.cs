using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Concert shell: the startup main menu (Play / Design / Settings / About / Exit, with a live theme
/// picker and a stage-look read-out) and the pre-flight Play dialog with per-hand style cards and
/// quick layer switches. Every quick switch mirrors a real stage setting, so the dock and the dialog
/// never disagree.
/// </summary>
public partial class MainWindow
{
    private readonly List<Button> _menuThemeChips = [];

    internal void ShowStartupMenu()
    {
        SetChromeVisible(false);
        MainMenuVersionLabel.Text = $"Keyflow {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.4"}";
        RefreshMenuThemeChips();
        RefreshMenuStageLook();
        MainMenuOverlay.Visibility = Visibility.Visible;
        MenuBackdrop.Configure(ShellThemeManager.Current, _visualSettings.ChromeMotion, _visualSettings.BackdropDensity);
        // Choreography: the two primary actions arrive first, then the links and the side card.
        ChromeMotion.FadeIn(MainMenuOverlay, 260);
        ChromeMotion.Cascade([MenuBrand, MenuPlayButton, MenuDesignButton], 60, 18);
        ChromeMotion.Cascade([.. MenuLinkStack.Children.OfType<FrameworkElement>(), MenuSidePanel], 45, 12);
    }

    private void HideStartupMenu() => MainMenuOverlay.Visibility = Visibility.Collapsed;

    private void MainMenu_Click(object sender, RoutedEventArgs e)
    {
        Stop();
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        CloseSettingsPanel();
        ShowStartupMenu();
    }

    private void MainMenuPlay_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        OpenPlayDialog();
    }

    private void MainMenuDesign_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SetChromeVisible(true);
        OpenSettingsPanel();
    }

    private void MainMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SetChromeVisible(true);
        OpenSettingsPanel();
        SettingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Audio);
    }

    private void MainMenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Keyflow · Piano VFX Studio\n\nA real-time MIDI piano visualizer: ray-traced keyboard shading, particle embers, flames, halos, blossom petals and a full stage designer.\n\nInterface themes: Sakura Nocturne (indigo night, blossom pink and gold), Concert Noir (violet and cyan studio) and Velvet Gold (burgundy velvet with brass light).\n\nThemes are drawn by the shared frame clock, so every panel animates in step with the monitor.\nSoundFont: FreePats YDP Grand Piano (CC BY 3.0).\nShading model: Cook-Torrance GGX + ACES filmic, in the spirit of Unreal Engine.",
            "About Keyflow", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MainMenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // =====================================================================================
    // Menu chrome: theme chips, stage-look read-out and preset shuffle
    // =====================================================================================

    /// <summary>Rebuilds the theme chips of the main menu; the active theme is marked and highlighted.</summary>
    private void RefreshMenuThemeChips()
    {
        if (MenuThemeHost is null) return;
        MenuThemeHost.Children.Clear();
        _menuThemeChips.Clear();
        foreach (var theme in ShellThemes.All)
        {
            var active = string.Equals(theme.Id, ShellThemeManager.Current.Id, StringComparison.OrdinalIgnoreCase);
            var chip = new Button
            {
                Content = active ? "✦  " + theme.Name : theme.Name,
                Tag = ThemeOrb(theme),
                DataContext = theme.Id,
                Style = (Style)FindResource("ThemeChipStyle"),
                ToolTip = theme.Blurb,
                Opacity = active ? 1 : .72
            };
            chip.Click += ThemeChip_Click;
            MenuThemeHost.Children.Add(chip);
            _menuThemeChips.Add(chip);
        }
        if (MenuThemeBlurb is not null) MenuThemeBlurb.Text = ShellThemeManager.Current.Blurb;
    }

    /// <summary>A small palette sphere for a theme chip: the accent turning into its companion colour.</summary>
    /// <summary>Frozen corner-to-corner gradients of each theme's two accents, shared by both chip rows.</summary>
    private static readonly Dictionary<string, Brush> ThemeOrbs = [];

    private static Brush ThemeOrb(ShellTheme theme)
    {
        if (ThemeOrbs.TryGetValue(theme.Id, out var cached)) return cached;
        var gradient = new LinearGradientBrush(theme.Accent, theme.AccentAlt, new Point(0, 0), new Point(1, 1));
        if (gradient.CanFreeze) gradient.Freeze();
        ThemeOrbs[theme.Id] = gradient;
        return gradient;
    }

    private void ThemeChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string id }) return;
        var theme = ShellThemes.Find(id);
        if (string.Equals(theme.Id, _visualSettings.ShellTheme, StringComparison.OrdinalIgnoreCase))
        {
            ChromeMotion.Pulse((UIElement)sender);   // already active: just acknowledge the click
            return;
        }
        _visualSettings.ShellTheme = theme.Id;
        MarkModified();
        // ApplyVisualSettings already publishes the theme, so the chip click rebuilds every surface
        // (menu, dock and play dialog) through one path.
        ApplyVisualSettings($"Interface theme · {theme.Name}");
        RefreshMenuThemeChips();
        ChromeMotion.Pulse((UIElement)sender);
    }

    /// <summary>The stage look named under the theme picker, so the menu always says what will play.</summary>
    private void RefreshMenuStageLook()
    {
        if (MenuPresetLabel is null) return;
        var preset = VisualPresets.FindBuiltIn(_visualSettings.PresetName);
        var name = string.IsNullOrWhiteSpace(_visualSettings.PresetName) ? "Custom" : _visualSettings.PresetName;
        MenuPresetLabel.Text = preset is null
            ? $"“{name}” — your own look{(_visualSettings.PresetModified ? ", edited since it was applied" : "")}."
            : $"“{name}” — {preset.Description}";
    }

    /// <summary>Cycles the built-in stage presets, the quickest way to feel the difference.</summary>
    private void MenuPresetShuffle_Click(object sender, RoutedEventArgs e)
    {
        if (VisualPresets.BuiltIn.Count == 0) return;
        var index = 0;
        for (var i = 0; i < VisualPresets.BuiltIn.Count; i++)
            if (string.Equals(VisualPresets.BuiltIn[i].Name, _visualSettings.PresetName, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        var next = VisualPresets.BuiltIn[(index + 1) % VisualPresets.BuiltIn.Count];
        ApplyPreset(next);
        LoadPresetList(next.Name);
        RefreshMenuStageLook();
        RefreshMenuThemeChips();
        SettingsSaveLabel.Text = $"Preset “{next.Name}” applied";
    }

    // =====================================================================================
    // Play dialog
    // =====================================================================================

    private void OpenPlayDialog()
    {
        _loadingVisualSettings = true;
        try
        {
            LayerBackgroundToggle.IsChecked = _visualSettings.ShowBackground;
            LayerNotesToggle.IsChecked = _visualSettings.ShowNotes;
            LayerEmbersToggle.IsChecked = _visualSettings.ShowEmbers;
            LayerHaloToggle.IsChecked = _visualSettings.ShowHalo;
            LayerFlameToggle.IsChecked = _visualSettings.ShowFlame;
            LayerKeysToggle.IsChecked = _visualSettings.ShowKeys;
            foreach (var child in ExtrasSubPanel.Children.OfType<CheckBox>())
                if (child.Tag is string property) child.IsChecked = (bool)Prop(property).GetValue(_visualSettings)!;
            PlaySpeedSlider.Value = Math.Clamp(_visualSettings.NoteFallSpeed, PlaySpeedSlider.Minimum, PlaySpeedSlider.Maximum);
            PlaySpeedLabel.Text = ((int)_visualSettings.NoteFallSpeed).ToString();
        }
        finally { _loadingVisualSettings = false; }

        var left = SafeColor(_visualSettings.ColorMode == "PerHand" ? _visualSettings.LeftHandColor : _visualSettings.NoteColorStart);
        var right = SafeColor(_visualSettings.ColorMode == "PerHand" ? _visualSettings.RightHandColor : _visualSettings.NoteColorEnd);
        LeftStyleCard.BorderBrush = new SolidColorBrush(left);
        RightStyleCard.BorderBrush = new SolidColorBrush(right);
        var presetDisplay = _visualSettings.PresetModified ? _visualSettings.PresetName + " *" : _visualSettings.PresetName;
        LeftStyleLabel.Text = presetDisplay;
        RightStyleLabel.Text = presetDisplay;
        if (PlayDialogHaloColorDot is not null)
            PlayDialogHaloColorDot.Background = new SolidColorBrush(SafeColor(_visualSettings.HaloColor));
        if (PlayDialogThemeOrb is not null)
            PlayDialogThemeOrb.Background = new SolidColorBrush(ShellThemeManager.Current.Accent);
        RefreshThemeChips();
        PlayDialogOverlay.Visibility = Visibility.Visible;
        ChromeMotion.FadeIn(PlayDialogOverlay, 200);
        ChromeMotion.PopIn(PlayDialogCard);
    }

    private static Color SafeColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Color.FromRgb(139, 92, 246); }
    }

    private void PlayDialogClose_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
    }

    private void PlayDialogOpenMidi_Click(object sender, RoutedEventArgs e)
    {
        var before = SongTitle.Text;
        OpenMidi_Click(sender, e);
        if (SongTitle.Text != before) PlayDialogMidiButton.Content = SongTitle.Text;
    }

    private void PlayDialogLive_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
        _lastPointerActivity = DateTime.UtcNow;
        Stop();
    }

    private void PlayDialogPlay_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
        _lastPointerActivity = DateTime.UtcNow;
        if (SongDuration() > 0) StartPlayback();
        else OpenMidi_Click(sender, e);
    }

    private void PlayDialogHaloColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(_visualSettings.HaloColor) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } hex) return;
        _visualSettings.HaloColor = hex;
        if (PlayDialogHaloColorDot is not null)
            PlayDialogHaloColorDot.Background = new SolidColorBrush(SafeColor(hex));
        if (_visualColorInputs.TryGetValue(nameof(PianoVisualSettings.HaloColor), out var input))
            input.Text = hex;
        if (_visualColorButtons.TryGetValue(nameof(PianoVisualSettings.HaloColor), out var button))
            SetColorSwatch(button, hex);
        MarkModified();
        ApplyVisualSettings("Halo color applied");
    }

    private void PlayDialogStyleCard_Click(object sender, MouseButtonEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        OpenSettingsPanel();
        SettingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Notes);
    }

    /// <summary>Opens the design dock on the page named by the chevron's DataContext.</summary>
    private void PlayDialogDeepLink_Click(object sender, RoutedEventArgs e)
    {
        var page = sender is FrameworkElement { DataContext: string name } ? SettingsPages.IndexOf(name) : -1;
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        OpenSettingsPanel();
        if (page >= 0 && page < SettingsTabs.Items.Count) SettingsTabs.SelectedIndex = page;
    }

    private void PlayDialogExtras_Click(object sender, RoutedEventArgs e) =>
        ExtrasSubPanel.Visibility = ExtrasSubPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void PlaySpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings) return;
        _visualSettings.NoteFallSpeed = Math.Clamp(PlaySpeedSlider.Value, 100, 1000);
        PlaySpeedLabel.Text = ((int)_visualSettings.NoteFallSpeed).ToString();
        if (_visualSliders.TryGetValue(nameof(PianoVisualSettings.NoteFallSpeed), out var dockSlider) && Math.Abs(dockSlider.Value - _visualSettings.NoteFallSpeed) > .01)
        {
            _loadingVisualSettings = true;
            try { dockSlider.Value = _visualSettings.NoteFallSpeed; if (_visualValueBoxes.TryGetValue(nameof(PianoVisualSettings.NoteFallSpeed), out var box)) box.Text = ((int)_visualSettings.NoteFallSpeed).ToString(); }
            finally { _loadingVisualSettings = false; }
        }
        MarkModified();
        ApplyVisualSettings($"Fall speed {(int)_visualSettings.NoteFallSpeed}");
    }

    private void PlaySpeedReset_Click(object sender, RoutedEventArgs e) => PlaySpeedSlider.Value = DefaultVisualSettings.NoteFallSpeed;
}
