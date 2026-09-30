using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Concert shell: the startup main menu (Play / Design / Settings / About / Exit, with a live theme
/// picker and a stage-look read-out) and the pre-flight Play dialog with per-hand style cards and
/// quick layer switches. Play, dock and menu navigation share an explicit return surface, and every
/// quick switch mirrors a real stage setting so the dock and dialog never disagree.
/// </summary>
public partial class MainWindow
{
    private enum NavigationSurface { Stage, MainMenu, PlayDialog }

    private NavigationSurface _playDialogReturnSurface = NavigationSurface.Stage;
    private NavigationSurface _settingsReturnSurface = NavigationSurface.Stage;
    private readonly List<Button> _menuThemeChips = [];

    internal void ShowStartupMenu()
    {
        SetChromeVisible(false);
        Loc.Format(MainMenuVersionLabel, "Keyflow {0} · Concert Grand Edition",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.4");
        RefreshMenuThemeChips();
        RefreshMenuStageLook();
        MainMenuOverlay.Visibility = Visibility.Visible;
        MenuBackdrop.Configure(ShellThemeManager.Current, _visualSettings.ChromeMotion, _visualSettings.BackdropDensity);
        MenuPlayButton.Focus();
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
        _playDialogReturnSurface = NavigationSurface.Stage;
        _settingsReturnSurface = NavigationSurface.Stage;
        UpdateSettingsReturnButton();
        CloseSettingsPanel();
        ShowStartupMenu();
    }

    private void MainMenuPlay_Click(object sender, RoutedEventArgs e) => OpenPlayDialog(returnToMenu: true);

    private void MainMenuDesign_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SettingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Style);
        OpenSettingsPanel(NavigationSurface.MainMenu);
    }

    private void MainMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SettingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Audio);
        OpenSettingsPanel(NavigationSurface.MainMenu);
    }

    private void MainMenuAbout_Click(object sender, RoutedEventArgs e) => ShowMessage(
        Loc.T("Keyflow · Piano Performance & Concert VFX Studio\n\nA professional real-time MIDI piano visualizer: ray-traced keyboard shading, thermal sparks & embers, acoustic resonance waves, flames, and a full concert stage designer.\n\nInterface themes: Concert Grand (Steinway ebony & champagne gold), Concert Noir (obsidian slate with silvery platinum) and Velvet Gold (mahogany velvet & burnished brass).\n\nThemes and stage effects are driven by the shared vsync clock for fluid 60+ FPS motion.\nSoundFont: Bundled Yamaha Disklavier Grand Piano (88 Keys).\nShading model: Cook-Torrance GGX + ACES filmic tone mapping.\nKeyflow 0.4.0 · shipped languages: English and Tiếng Việt.\n\n© 2026 Yami · Neyu · Keyflow — released under the MIT license.\nContributor: Jin"),
        "About Keyflow Concert Grand", MessageBoxImage.Information);

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
        foreach (var theme in ShellThemes.Everything)
        {
            var active = string.Equals(theme.Id, ShellThemeManager.Current.Id, StringComparison.OrdinalIgnoreCase);
            var chip = new Button
            {
                Tag = ThemeOrb(theme),
                DataContext = theme.Id,
                Style = (Style)FindResource("ThemeChipStyle"),
                Opacity = active ? 1 : .72
            };
            Loc.Bind(chip, () => Loc.F(active ? "✦  {0}" : "{0}", Loc.T(theme.Name)));
            Loc.Set(chip, theme.Blurb, FrameworkElement.ToolTipProperty);
            chip.Click += ThemeChip_Click;
            MenuThemeHost.Children.Add(chip);
            _menuThemeChips.Add(chip);
        }
        if (MenuThemeBlurb is not null) Loc.Set(MenuThemeBlurb, ShellThemeManager.Current.Blurb);
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
        ApplyVisualSettings("Interface theme · {0}", false, Loc.T(theme.Name));
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
        // A closure (rather than a template plus arguments) because the description and the
        // "edited" suffix are themselves translated text that has to follow a language switch.
        Loc.Bind(MenuPresetLabel, () => preset is null
            ? Loc.F("“{0}” — your own look{1}.", VisualPresets.DisplayName(name), _visualSettings.PresetModified ? Loc.T(", edited since it was applied") : "")
            : Loc.F("“{0}” — {1}", VisualPresets.DisplayName(name), Loc.T(preset.Description)));
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
        Loc.Format(SettingsSaveLabel, "Preset “{0}” applied", VisualPresets.DisplayName(next.Name));
    }

    // =====================================================================================
    // Play dialog
    // =====================================================================================

    /// <summary>Opens the pre-flight performance dialog; also the target of <c>--play-dialog</c> captures.</summary>
    internal void OpenPlayDialog(bool returnToMenu = false)
    {
        _playDialogReturnSurface = returnToMenu ? NavigationSurface.MainMenu : NavigationSurface.Stage;
        _settingsReturnSurface = NavigationSurface.Stage;
        UpdateSettingsReturnButton();
        HideStartupMenu();
        if (SettingsPanel.Visibility == Visibility.Visible) CloseSettingsPanel();
        ShowPlayDialog();
    }

    /// <summary>Re-shows the dialog without replacing the surface it should return to.</summary>
    private void ShowPlayDialog()
    {
        SetChromeVisible(true);
        RefreshPlayDialogNavigation();
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
        // The preset name is translated and the "edited" marker appended, both inside the renderer, so
        // the two hand cards follow a language switch instead of keeping the text of the old one.
        string PresetCaption() => VisualPresets.DisplayName(_visualSettings.PresetName)
            + (_visualSettings.PresetModified ? " *" : "");
        Loc.Bind(LeftStyleLabel, PresetCaption);
        Loc.Bind(RightStyleLabel, PresetCaption);
        if (PlayDialogHaloColorDot is not null)
            PlayDialogHaloColorDot.Background = new SolidColorBrush(SafeColor(_visualSettings.HaloColor));
        if (PlayDialogThemeOrb is not null)
            PlayDialogThemeOrb.Background = new SolidColorBrush(ShellThemeManager.Current.Accent);
        RefreshThemeChips();
        SyncPlayInlineControls();
        RefreshPlayDialogState();
        RefreshRecentSongs();
        RefreshLibrarySongs();
        if (SongFolderIndex.Folder.Length > 0) StartSongFolderWatch(SongFolderIndex.Folder);
        _lastPointerActivity = DateTime.UtcNow;
        PlayDialogOverlay.Visibility = Visibility.Visible;
        ChromeMotion.FadeIn(PlayDialogOverlay, 200);
        ChromeMotion.PopIn(PlayDialogCard);
        PlayDialogMidiButton.Focus();
    }

    private static Color SafeColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Color.FromRgb(139, 92, 246); }
    }

    private void RefreshPlayDialogNavigation()
    {
        if (PlayDialogBackButton is null) return;
        var destination = _playDialogReturnSurface == NavigationSurface.MainMenu
            ? "Back to the main menu"
            : "Back to the live stage";
        Loc.Set(PlayDialogBackButton, destination, FrameworkElement.ToolTipProperty);
        Loc.Set(PlayDialogBackButton, destination, AutomationProperties.NameProperty);
    }

    private void RefreshPlayDialogState()
    {
        if (PlayDialogPrimaryButton is null) return;
        var canPlay = SongDuration() > 0;
        var label = _playing ? "Return to playback" : canPlay ? "Start playback" : "Choose a MIDI file";
        Loc.Set(PlayDialogPrimaryLabel, label);
        Loc.Set(PlayDialogPrimaryButton, label, AutomationProperties.NameProperty);
        Loc.Set(PlayDialogPrimaryButton, label, FrameworkElement.ToolTipProperty);
        PlayDialogPrimaryIcon.Data = FindResource(canPlay || _playing ? "IconPlay" : "IconFolder") as Geometry;

        var hasLoadedSong = !string.IsNullOrWhiteSpace(_songPath) && _allNotes.Count > 0;
        PlayDialogLoadedSongPanel.Visibility = hasLoadedSong ? Visibility.Visible : Visibility.Collapsed;
        if (!hasLoadedSong) return;

        PlayDialogLoadedSongTitle.Text = _songLabel;
        var noteCount = _allNotes.Count;
        var trackCount = _allNotes.Select(note => note.Track).Distinct().Count();
        var duration = TimeSpan.FromSeconds(_allNotes.Max(note => note.End)).ToString("m\\:ss");
        var details = _playing ? "Playing · {0} notes · {1} tracks · {2}" : "Ready · {0} notes · {1} tracks · {2}";
        Loc.Bind(PlayDialogLoadedSongDetails, () => Loc.F(details, noteCount, trackCount, duration));
    }

    private void PlayDialogOpen_Click(object sender, RoutedEventArgs e) => OpenPlayDialog();

    private void PlayDialogBack_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        _lastPointerActivity = DateTime.UtcNow;
        if (_playDialogReturnSurface == NavigationSurface.MainMenu)
        {
            ShowStartupMenu();
            return;
        }
        SetChromeVisible(true);
        Stage.Focus();
    }

    private void PlayDialogClose_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        _lastPointerActivity = DateTime.UtcNow;
        SetChromeVisible(true);
        Stage.Focus();
    }

    private void PlayDialogOpenMidi_Click(object sender, RoutedEventArgs e) => OpenMidi_Click(sender, e);

    private void PlayDialogSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsFromPlayDialog(SettingsPages.Style);

    private void OpenSettingsFromPlayDialog(string page)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SettingsTabs.SelectedIndex = Math.Max(0, SettingsPages.IndexOf(page));
        OpenSettingsPanel(NavigationSurface.PlayDialog);
    }

    private void PlayDialogLive_Click(object sender, RoutedEventArgs e)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
        _lastPointerActivity = DateTime.UtcNow;
        Stop();
        Stage.Focus();
    }

    private void PlayDialogPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            PlayDialogClose_Click(sender, e);
            return;
        }
        if (SongDuration() <= 0)
        {
            // Keep the setup visible if the picker is cancelled; after a successful load the same
            // primary action becomes "Start playback" without making the user reopen this dialog.
            OpenMidi_Click(sender, e);
            RefreshPlayDialogState();
            return;
        }
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SetChromeVisible(true);
        _lastPointerActivity = DateTime.UtcNow;
        StartPlayback();
        Stage.Focus();
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

    private void PlayDialogStyleCard_Click(object sender, RoutedEventArgs e) => OpenSettingsFromPlayDialog(SettingsPages.Notes);

    /// <summary>Opens the design dock on the page named by the chevron's DataContext.</summary>
    private void PlayDialogDeepLink_Click(object sender, RoutedEventArgs e)
    {
        var page = sender is FrameworkElement { DataContext: string name } ? name : SettingsPages.Style;
        if (SettingsPages.IndexOf(page) < 0) page = SettingsPages.Style;
        OpenSettingsFromPlayDialog(page);
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
        ApplyVisualSettings("Fall speed {0}", false, (int)_visualSettings.NoteFallSpeed);
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
        var more = new Button { DataContext = dockPage, Style = (Style)FindResource("MiniButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        Loc.Set(more, "More settings…");
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
        Loc.Set(slider, spec.Label, AutomationProperties.NameProperty);
        slider.ValueChanged += PlayInlineSlider_Changed;
        Grid.SetColumn(value, 1); Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 2);
        var label = new TextBlock { Style = (Style)FindResource("LabelTextStyle") };
        Loc.Set(label, spec.Label);
        grid.Children.Add(label);
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
        Loc.Set(combo, spec.Label, AutomationProperties.NameProperty);
        combo.SelectionChanged += PlayInlineChoice_Changed;
        Grid.SetColumn(combo, 1);
        var label = new TextBlock { Style = (Style)FindResource("LabelTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        Loc.Set(label, spec.Label);
        grid.Children.Add(label);
        grid.Children.Add(combo);
        _playInlineChoices.Add(combo);
        return grid;
    }

    /// <summary>Inline switches join the shared toggle list, so <see cref="VisualToggle_Changed"/> keeps them and the dock in step.</summary>
    private CheckBox BuildInlineToggle(InlineControl spec)
    {
        var check = new CheckBox { Tag = spec.Property, IsChecked = (bool)Prop(spec.Property).GetValue(_visualSettings)!, Margin = new Thickness(0, 4, 0, 4) };
        Loc.Set(check, spec.Label, ContentControl.ContentProperty);
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
        foreach (var theme in ShellThemes.Everything)
        {
            var active = string.Equals(theme.Id, ShellThemeManager.Current.Id, StringComparison.OrdinalIgnoreCase);
            var chip = new Button
            {
                Tag = ThemeOrb(theme),
                DataContext = theme.Id,
                Style = (Style)FindResource("ThemeChipStyle"),
                Opacity = active ? 1 : .72
            };
            Loc.Bind(chip, () => Loc.F(active ? "✦  {0}" : "{0}", Loc.T(theme.Name)));
            Loc.Set(chip, theme.Blurb, FrameworkElement.ToolTipProperty);
            chip.Click += ThemeChip_Click;
            _playThemeChips.Children.Add(chip);
        }
    }
}
