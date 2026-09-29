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
            "Keyflow · Piano Performance & Concert VFX Studio\n\nA professional real-time MIDI piano visualizer: ray-traced keyboard shading, thermal sparks & embers, acoustic resonance waves, flames, and a full concert stage designer.\n\nInterface themes: Concert Grand (Steinway ebony & champagne gold), Concert Noir (obsidian slate with silvery platinum) and Velvet Gold (mahogany velvet & burnished brass).\n\nThemes and stage effects are driven by the shared vsync clock for fluid 60+ FPS motion.\nSoundFont: Bundled Yamaha Disklavier Grand Piano (88 Keys).\nShading model: Cook-Torrance GGX + ACES filmic tone mapping.\n\n© 2026 Yami · Neyu · Keyflow — released under the MIT license.\nContributor: Jin",
            "About Keyflow Concert Grand", MessageBoxButton.OK, MessageBoxImage.Information);
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
        RefreshPlayThemeChips();
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

    /// <summary>Opens the pre-flight performance dialog; also the target of <c>--play-dialog</c> captures.</summary>
    internal void OpenPlayDialog()
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
        SyncPlayInlineControls();
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

    // =====================================================================================
    // Play dialog: inline options under each layer row
    // =====================================================================================

    private sealed record InlineControl(string Label, string Property);

    /// <summary>The controls a layer row unfolds in place, plus the dock page its "More settings" link opens.</summary>
    private sealed record InlineSection(string DockPage, InlineControl[] Choices, InlineControl[] Sliders, InlineControl[] Toggles);

    private static readonly Dictionary<string, InlineSection> PlayInlineSections = new()
    {
        ["Camera"] = new(SettingsPages.Camera, [],
            [new("Parallax", nameof(PianoVisualSettings.CameraParallax)), new("Zoom", nameof(PianoVisualSettings.CameraZoom)), new("Saturation", nameof(PianoVisualSettings.Saturation)), new("Bloom", nameof(PianoVisualSettings.BloomIntensity))], []),
        ["Background"] = new(SettingsPages.Background, [],
            [new("Vignette", nameof(PianoVisualSettings.Vignette)), new("Horizon glow", nameof(PianoVisualSettings.HorizonGlow)), new("Light beams", nameof(PianoVisualSettings.BeamIntensity))],
            [new("Stars", nameof(PianoVisualSettings.ShowStars)), new("Guide lanes", nameof(PianoVisualSettings.BackgroundGuide))]),
        ["Notes"] = new(SettingsPages.Notes,
            [new("Style", nameof(PianoVisualSettings.NoteStyle)), new("Direction", nameof(PianoVisualSettings.NoteDirection))],
            [new("Width", nameof(PianoVisualSettings.NoteWidth)), new("Glow", nameof(PianoVisualSettings.NoteGlow)), new("Opacity", nameof(PianoVisualSettings.NoteTint))],
            [new("3D shading", nameof(PianoVisualSettings.Notes3D))]),
        ["Embers"] = new(SettingsPages.Particles, [],
            [new("Amount", nameof(PianoVisualSettings.ParticleAmount)), new("Velocity", nameof(PianoVisualSettings.ParticleVelocity)), new("Glow", nameof(PianoVisualSettings.ParticleGlow))],
            [new("Wisps", nameof(PianoVisualSettings.ShowWisps))]),
        ["Halo"] = new(SettingsPages.Background, [], [new("Intensity", nameof(PianoVisualSettings.HaloIntensity))], []),
        ["Flame"] = new(SettingsPages.Particles, [],
            [new("Intensity", nameof(PianoVisualSettings.FlameIntensity)), new("Height", nameof(PianoVisualSettings.FlameHeight))],
            [new("Impact rings", nameof(PianoVisualSettings.ShowImpactRings))]),
        ["Keys"] = new(SettingsPages.Keyboard,
            [new("Style", nameof(PianoVisualSettings.KeyboardStyle)), new("Shading", nameof(PianoVisualSettings.ShadingQuality))],
            [new("Height", nameof(PianoVisualSettings.KeyboardScale)), new("Light intensity", nameof(PianoVisualSettings.KeyLighting))],
            [new("Animate pressed keys", nameof(PianoVisualSettings.AnimateKeys))]),
    };

    private readonly Dictionary<string, StackPanel> _playInlinePanels = [];
    private readonly Dictionary<string, Button> _playInlineChevrons = [];
    private readonly List<(Slider Slider, TextBlock Value)> _playInlineSliders = [];
    private readonly List<ComboBox> _playInlineChoices = [];
    private WrapPanel? _playThemeChips;

    /// <summary>Unfolds (or folds) the options of one layer row right under it; only one row stays open at a time.</summary>
    private void PlayDialogExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string key, Parent: Grid row } chevron) return;
        var created = !_playInlinePanels.TryGetValue(key, out var panel);
        if (created)
        {
            panel = BuildPlayInlinePanel(key);
            _playInlinePanels[key] = panel;
            _playInlineChevrons[key] = chevron;
            LayerRowsHost.Children.Insert(LayerRowsHost.Children.IndexOf(row) + 1, panel);
        }
        var open = created || panel!.Visibility != Visibility.Visible;
        foreach (var (otherKey, other) in _playInlinePanels)
        {
            var isThis = otherKey == key;
            other.Visibility = isThis && open ? Visibility.Visible : Visibility.Collapsed;
            SetChevronOpen(_playInlineChevrons[otherKey], isThis && open);
        }
        if (open) ChromeMotion.FadeIn(panel!, 160);
    }

    private static void SetChevronOpen(Button chevron, bool open)
    {
        chevron.RenderTransformOrigin = new Point(.5, .5);
        chevron.RenderTransform = new RotateTransform(open ? 180 : 0);
    }

    private StackPanel BuildPlayInlinePanel(string key)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 0, 0, 8) };
        var dockPage = SettingsPages.Theme;
        if (key == "Theme")
        {
            _playThemeChips = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            panel.Children.Add(_playThemeChips);
            RefreshPlayThemeChips();
        }
        else if (PlayInlineSections.TryGetValue(key, out var section))
        {
            dockPage = section.DockPage;
            foreach (var choice in section.Choices) panel.Children.Add(BuildInlineChoice(choice));
            foreach (var slider in section.Sliders) panel.Children.Add(BuildInlineSlider(slider));
            foreach (var toggle in section.Toggles) panel.Children.Add(BuildInlineToggle(toggle));
        }
        var more = new Button { Content = "More settings…", DataContext = dockPage, Style = (Style)FindResource("MiniButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        more.Click += PlayDialogDeepLink_Click;
        panel.Children.Add(more);
        return panel;
    }

    private FrameworkElement BuildInlineSlider(InlineControl spec)
    {
        var (min, max) = _sliderRanges[spec.Property];
        var current = (double)Prop(spec.Property).GetValue(_visualSettings)!;
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var value = new TextBlock { Text = FormatSetting(spec.Property, current), Style = (Style)FindResource("MutedTextStyle"), HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(current, min, max), Tag = spec.Property };
        slider.ValueChanged += PlayInlineSlider_Changed;
        Grid.SetColumn(value, 1); Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 2);
        grid.Children.Add(new TextBlock { Text = spec.Label, Style = (Style)FindResource("LabelTextStyle") });
        grid.Children.Add(value); grid.Children.Add(slider);
        _playInlineSliders.Add((slider, value));
        return grid;
    }

    private FrameworkElement BuildInlineChoice(InlineControl spec)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dock = _visualChoices[spec.Property];
        var combo = new ComboBox { Tag = spec.Property, Width = 180, Height = 28, DisplayMemberPath = dock.DisplayMemberPath, SelectedValuePath = dock.SelectedValuePath, ItemsSource = dock.ItemsSource, SelectedValue = dock.SelectedValue };
        combo.SelectionChanged += PlayInlineChoice_Changed;
        Grid.SetColumn(combo, 1);
        grid.Children.Add(new TextBlock { Text = spec.Label, Style = (Style)FindResource("LabelTextStyle"), VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(combo);
        _playInlineChoices.Add(combo);
        return grid;
    }

    /// <summary>Inline switches join the shared toggle list, so <see cref="VisualToggle_Changed"/> keeps them and the dock in step.</summary>
    private CheckBox BuildInlineToggle(InlineControl spec)
    {
        var check = new CheckBox { Content = spec.Label, Tag = spec.Property, IsChecked = (bool)Prop(spec.Property).GetValue(_visualSettings)!, Margin = new Thickness(0, 4, 0, 4) };
        check.Checked += VisualToggle_Changed; check.Unchecked += VisualToggle_Changed;
        _visualToggleList.Add(check);
        return check;
    }

    // The inline controls hand every change to the matching dock control, whose handler stores,
    // applies and schedules the save — one code path for both surfaces.
    private void PlayInlineSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings || sender is not Slider { Tag: string property } slider) return;
        if (_visualSliders.TryGetValue(property, out var dock)) dock.Value = slider.Value;
        foreach (var (candidate, label) in _playInlineSliders)
            if (ReferenceEquals(candidate, slider)) label.Text = FormatSetting(property, (double)Prop(property).GetValue(_visualSettings)!);
    }

    private void PlayInlineChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingVisualSettings || sender is not ComboBox { Tag: string property, SelectedValue: string value }) return;
        if (_visualChoices.TryGetValue(property, out var dock)) dock.SelectedValue = value;
    }

    /// <summary>Pulls the current settings into the inline controls (dialog opened, preset applied, reset).</summary>
    private void SyncPlayInlineControls()
    {
        var wasLoading = _loadingVisualSettings;
        _loadingVisualSettings = true;
        try
        {
            foreach (var (slider, label) in _playInlineSliders)
            {
                var property = (string)slider.Tag;
                var value = (double)Prop(property).GetValue(_visualSettings)!;
                slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
                label.Text = FormatSetting(property, value);
            }
            foreach (var combo in _playInlineChoices) combo.SelectedValue = (string)Prop((string)combo.Tag).GetValue(_visualSettings)!;
            RefreshPlayThemeChips();
        }
        finally { _loadingVisualSettings = wasLoading; }
    }

    private void RefreshPlayThemeChips()
    {
        if (_playThemeChips is null) return;
        _playThemeChips.Children.Clear();
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
            _playThemeChips.Children.Add(chip);
        }
    }
}
