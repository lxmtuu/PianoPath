using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianoPath;

/// <summary>
/// Concert shell: the startup menu (Play / Design / Quick Adjust / Settings / About / Exit, with a live
/// theme picker and stage-look read-out), a shared contextual quick-adjust surface, and the pre-flight
/// Play dialog with per-hand style cards and quick layer switches. Navigation has explicit return
/// surfaces, and every quick control writes through the same live settings path as the Design dock.
/// </summary>
public partial class MainWindow
{
    private enum NavigationSurface { Stage, MainMenu, PlayDialog }

    private NavigationSurface _playDialogReturnSurface = NavigationSurface.Stage;
    private NavigationSurface _settingsReturnSurface = NavigationSurface.Stage;
    private NavigationSurface _quickAdjustReturnSurface = NavigationSurface.Stage;
    private readonly List<Button> _menuThemeChips = [];
    private readonly Dictionary<string, ComboBox> _quickAdjustChoices = [];
    private readonly Dictionary<string, Border> _quickHandColorDots = [];
    private FrameworkElement? _quickHandSplitRow;
    private bool _quickAdjustControlsBuilt;

    private sealed record HandColorPreset(string Name, string Hex);

    /// <summary>Per-hand color looks used by the Play dialog. They intentionally change only one hand's color.</summary>
    private static readonly HandColorPreset[] HandColorPresets =
    [
        new("Ocean Blue", "#3FA9FF"),
        new("Rose Neon", "#FF6FD8"),
        new("Aurora Teal", "#49E2C2"),
        new("Violet Glow", "#8C69FF"),
        new("Sunset Amber", "#FFB05A"),
        new("Ice Crystal", "#9BE8FF"),
        new("Concert Gold", "#F3C05E"),
        new("Electric Storm", "#7183FF"),
    ];

    private bool _handPresetTargetsRight;

    internal void ShowStartupMenu()
    {
        SetChromeVisible(false);
        Loc.Format(MainMenuVersionLabel, "Keyflow {0} · Concert Grand Edition", AppInfo.Version);
        RefreshMenuThemeChips();
        RefreshMenuStageLook();
        MainMenuOverlay.Visibility = Visibility.Visible;
        // A locked chrome (automated captures) keeps the backdrop parked on its one static frame, exactly as
        // ConfigureBackdrops does. Configuring it with the setting alone started it moving again right after
        // DisableChromeMotion had stopped it, so the menu screenshot differed from run to run: the one
        // picture whose chrome, and not only the GPU stage, was not reproducible.
        MenuBackdrop.Configure(ShellThemeManager.Current, _chromeMotionLocked ? "Off" : _visualSettings.ChromeMotion, _visualSettings.BackdropDensity);
        MenuPlayButton.Focus();
        // Choreography: the two primary actions arrive first, then the links and the side card.
        ChromeMotion.FadeIn(MainMenuOverlay, 260);
        ChromeMotion.Cascade([MenuBrand, MenuPlayButton, MenuQuickAdjustButton], 60, 18);
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
        OpenSettingsPanelFor(NavigationSurface.MainMenu);
    }

    private void MainMenuQuickAdjust_Click(object sender, RoutedEventArgs e) => OpenQuickAdjust(NavigationSurface.MainMenu);

    private void MainMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        HideStartupMenu();
        SettingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Audio);
        OpenSettingsPanelFor(NavigationSurface.MainMenu);
    }

    private void MainMenuAbout_Click(object sender, RoutedEventArgs e) => ShowMessage(
        Loc.F("Keyflow · Piano Performance & Concert VFX Studio\n\nA professional real-time MIDI piano visualizer: ray-traced keyboard shading, thermal sparks & embers, acoustic resonance waves, flames, and a full concert stage designer.\n\nInterface themes: Concert Grand (Steinway ebony & champagne gold), Concert Noir (obsidian slate with silvery platinum) and Velvet Gold (mahogany velvet & burnished brass).\n\nThemes and stage effects are driven by the shared vsync clock for fluid 60+ FPS motion.\nSoundFont: Bundled Yamaha Disklavier Grand Piano (88 Keys).\nShading model: Cook-Torrance GGX + ACES filmic tone mapping.\nKeyflow {0} · shipped languages: English and Tiếng Việt.\n\n© 2026 Yami · Neyu · Keyflow — released under the MIT license.\nContributor: Jin", AppInfo.Version),
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
        HandPresetPanel.Visibility = Visibility.Collapsed;
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

        RefreshHandStyleCards();
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
        (PlayDialogMidiButton.Visibility == Visibility.Visible ? PlayDialogMidiButton : PlayDialogPrimaryButton).Focus();
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
        PlayDialogMidiButton.Visibility = hasLoadedSong ? Visibility.Visible : Visibility.Collapsed;
        if (!hasLoadedSong) return;

        PlayDialogLoadedSongTitle.Text = _songLabel;
        var noteCount = _allNotes.Count;
        var trackCount = _allNotes.Select(note => note.Track).Distinct().Count();
        var duration = TimeSpan.FromSeconds(_allNotes.Max(note => note.End)).ToString("m\\:ss");
        var details = _playing ? "Playing · {0} notes · {1} tracks · {2}" : "Ready · {0} notes · {1} tracks · {2}";
        Loc.Bind(PlayDialogLoadedSongDetails, () => Loc.F(details, noteCount, trackCount, duration));
    }

    private void PlayDialogOpen_Click(object sender, RoutedEventArgs e) => OpenPlayDialog();

    private void RefreshHandStyleCards()
    {
        if (LeftStyleCard is null || RightStyleCard is null) return;
        var left = SafeColor(_visualSettings.ColorMode == "PerHand" ? _visualSettings.LeftHandColor : _visualSettings.NoteColorStart);
        var right = SafeColor(_visualSettings.ColorMode == "PerHand" ? _visualSettings.RightHandColor : _visualSettings.NoteColorEnd);
        LeftStyleCard.BorderBrush = new SolidColorBrush(left);
        RightStyleCard.BorderBrush = new SolidColorBrush(right);
        Loc.Bind(LeftStyleLabel, () => HandStyleCaption(rightHand: false));
        Loc.Bind(RightStyleLabel, () => HandStyleCaption(rightHand: true));
    }

    private string HandStyleCaption(bool rightHand)
    {
        if (_visualSettings.ColorMode != "PerHand")
            return VisualPresets.DisplayName(_visualSettings.PresetName) + (_visualSettings.PresetModified ? " *" : "");

        var hex = rightHand ? _visualSettings.RightHandColor : _visualSettings.LeftHandColor;
        var preset = HandColorPresets.FirstOrDefault(item => string.Equals(item.Hex, hex, StringComparison.OrdinalIgnoreCase));
        return preset is null ? Loc.F("Custom · {0}", hex.ToUpperInvariant()) : Loc.T(preset.Name);
    }

    private void PlayDialogStyleCard_Click(object sender, RoutedEventArgs e) =>
        ShowHandPresetPicker(ReferenceEquals(sender, RightStyleCard));

    private void ShowHandPresetPicker(bool rightHand)
    {
        _handPresetTargetsRight = rightHand;
        Loc.Set(HandPresetTitle, rightHand ? "Right hand presets" : "Left hand presets");
        Loc.Set(HandPresetHint, "Choose a color preset. Only the selected hand will change.");
        RefreshHandPresetChoices();
        HandPresetPanel.Visibility = Visibility.Visible;
        HandPresetPanel.BringIntoView();
        ChromeMotion.FadeIn(HandPresetPanel, 150);
        FocusHandPresetChoice();
    }

    private void RefreshHandPresetChoices()
    {
        if (HandPresetChoicesHost is null) return;
        HandPresetChoicesHost.Children.Clear();
        var current = _handPresetTargetsRight ? _visualSettings.RightHandColor : _visualSettings.LeftHandColor;
        foreach (var preset in HandColorPresets)
        {
            var selected = string.Equals(current, preset.Hex, StringComparison.OrdinalIgnoreCase);
            var color = new SolidColorBrush(SafeColor(preset.Hex));
            var button = new Button
            {
                DataContext = preset,
                Tag = preset.Hex,
                Style = (Style)FindResource("GhostButtonStyle"),
                MinWidth = 126,
                MinHeight = 32,
                Margin = new Thickness(3),
                Padding = new Thickness(8, 5, 8, 5),
                BorderThickness = new Thickness(1.5),
                BorderBrush = color,
                Background = (Brush)FindResource(selected ? "AccentSoftBrush" : "ControlBrush"),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = color,
                BorderBrush = (Brush)FindResource("TextBrush"),
                BorderThickness = new Thickness(.5),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            });
            var label = new TextBlock { Style = (Style)FindResource("LabelTextStyle"), VerticalAlignment = VerticalAlignment.Center };
            Loc.Set(label, preset.Name);
            Loc.Set(button, preset.Name, AutomationProperties.NameProperty);
            Loc.Set(button, preset.Name, FrameworkElement.ToolTipProperty);
            content.Children.Add(label);
            button.Content = content;
            button.Click += PlayDialogHandPreset_Click;
            HandPresetChoicesHost.Children.Add(button);
        }
        if (HandPresetPanel.Visibility == Visibility.Visible) FocusHandPresetChoice();
    }

    private void FocusHandPresetChoice()
    {
        var current = _handPresetTargetsRight ? _visualSettings.RightHandColor : _visualSettings.LeftHandColor;
        var selected = HandPresetChoicesHost.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, current));
        (selected ?? HandPresetChoicesHost.Children.OfType<Button>().FirstOrDefault())?.Focus();
    }

    private void PlayDialogHandPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HandColorPreset preset }) return;
        ApplyHandColorPreset(preset.Hex);
    }

    private void ApplyHandColorPreset(string hex)
    {
        var property = _handPresetTargetsRight
            ? nameof(PianoVisualSettings.RightHandColor)
            : nameof(PianoVisualSettings.LeftHandColor);
        Prop(property).SetValue(_visualSettings, hex);

        // A hand color only affects notes in PerHand mode. Selecting one turns that mode on while
        // preserving the other hand, note style, effects, theme and every unrelated setting.
        if (_visualSettings.ColorMode != "PerHand") _visualSettings.ColorMode = "PerHand";
        RefreshSettingControls();
        MarkModified(property);
        RefreshHandStyleCards();
        RefreshHandPresetChoices();
        ApplyVisualSettings("Color applied");
    }

    private void PlayDialogHandPresetCustom_Click(object sender, RoutedEventArgs e)
    {
        var current = _handPresetTargetsRight ? _visualSettings.RightHandColor : _visualSettings.LeftHandColor;
        var picker = new ColorPickerWindow(current) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedHex is { } hex) ApplyHandColorPreset(hex);
    }

    private void HideHandPresetPanel(bool restoreFocus)
    {
        if (HandPresetPanel is null || HandPresetPanel.Visibility != Visibility.Visible) return;
        HandPresetPanel.Visibility = Visibility.Collapsed;
        if (restoreFocus) ( _handPresetTargetsRight ? RightStyleCard : LeftStyleCard ).Focus();
    }

    private void PlayDialogHandPresetClose_Click(object sender, RoutedEventArgs e) => HideHandPresetPanel(restoreFocus: true);

    private void PlayDialogHandPresetAdvanced_Click(object sender, RoutedEventArgs e)
    {
        HideHandPresetPanel(restoreFocus: false);
        OpenSettingsFromPlayDialog(SettingsPages.Notes);
    }

    private void PlayDialogBack_Click(object sender, RoutedEventArgs e)
    {
        HideHandPresetPanel(restoreFocus: false);
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
        HideHandPresetPanel(restoreFocus: false);
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        _lastPointerActivity = DateTime.UtcNow;
        SetChromeVisible(true);
        Stage.Focus();
    }

    private void PlayDialogOpenMidi_Click(object sender, RoutedEventArgs e) => OpenMidi_Click(sender, e);

    private void PlayDialogQuickAdjust_Click(object sender, RoutedEventArgs e) => OpenQuickAdjust(NavigationSurface.PlayDialog);

    private void OpenQuickAdjust(NavigationSurface returnSurface)
    {
        _quickAdjustReturnSurface = returnSurface;
        if (SettingsPanel.Visibility == Visibility.Visible || _settingsHiddenByIdle) CloseSettingsPanel();
        if (returnSurface == NavigationSurface.MainMenu) HideStartupMenu();
        else if (returnSurface == NavigationSurface.PlayDialog) PlayDialogOverlay.Visibility = Visibility.Collapsed;

        BuildQuickAdjustControls();
        SyncPlayInlineControls();
        SyncQuickAdjustState();
        SetChromeVisible(true);
        QuickAdjustOverlay.Visibility = Visibility.Visible;
        _lastPointerActivity = DateTime.UtcNow;
        ChromeMotion.FadeIn(QuickAdjustOverlay, 180);
        ChromeMotion.PopIn(QuickAdjustCard);
        _quickAdjustChoices.GetValueOrDefault(nameof(PianoVisualSettings.NoteStyle))?.Focus();
    }

    private void CloseQuickAdjust()
    {
        if (QuickAdjustOverlay.Visibility != Visibility.Visible) return;
        var destination = _quickAdjustReturnSurface;
        QuickAdjustOverlay.Visibility = Visibility.Collapsed;
        _lastPointerActivity = DateTime.UtcNow;
        switch (destination)
        {
            case NavigationSurface.MainMenu:
                ShowStartupMenu();
                break;
            case NavigationSurface.PlayDialog:
                ShowPlayDialog();
                break;
            default:
                SetChromeVisible(true);
                Stage.Focus();
                break;
        }
    }

    private void QuickAdjustClose_Click(object sender, RoutedEventArgs e) => CloseQuickAdjust();

    private void QuickAdjustAdvanced_Click(object sender, RoutedEventArgs e)
    {
        var destination = _quickAdjustReturnSurface;
        QuickAdjustOverlay.Visibility = Visibility.Collapsed;
        SettingsTabs.SelectedIndex = SettingsPages.IndexOf(SettingsPages.Style);
        OpenSettingsPanelFor(destination);
    }

    private void QuickAdjustOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, QuickAdjustOverlay)) CloseQuickAdjust();
    }

    private void QuickAdjustCard_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private Border QuickAdjustGroup(string title, out StackPanel body)
    {
        body = new StackPanel();
        var heading = new TextBlock { Style = (Style)FindResource("EyebrowTextStyle"), Margin = new Thickness(0, 0, 0, 6) };
        Loc.Set(heading, title);
        body.Children.Add(heading);
        return new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(11),
            Background = (Brush)FindResource("ControlBrush"),
            BorderBrush = (Brush)FindResource("ControlBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = body
        };
    }

    private void BuildQuickAdjustControls()
    {
        if (_quickAdjustControlsBuilt) return;
        _quickAdjustControlsBuilt = true;

        var notesCard = QuickAdjustGroup("Notes", out var notesBody);
        foreach (var spec in new[]
        {
            new InlineControl("Note style", nameof(PianoVisualSettings.NoteStyle)),
            new InlineControl("Color mode", nameof(PianoVisualSettings.ColorMode)),
        })
        {
            var row = BuildInlineChoice(spec);
            if (row is Grid grid && grid.Children.OfType<ComboBox>().FirstOrDefault() is { } combo)
                _quickAdjustChoices[spec.Property] = combo;
            notesBody.Children.Add(row);
        }
        notesBody.Children.Add(BuildInlineSlider(new InlineControl("Fall speed", nameof(PianoVisualSettings.NoteFallSpeed))));
        notesBody.Children.Add(BuildInlineSlider(new InlineControl("Glow", nameof(PianoVisualSettings.NoteGlow))));
        var splitRow = BuildInlineSlider(new InlineControl("Hand split point", nameof(PianoVisualSettings.HandSplitPitch)));
        _quickHandSplitRow = splitRow;
        notesBody.Children.Add(splitRow);
        QuickAdjustControlsHost.Children.Add(notesCard);

        foreach (var spec in new[]
        {
            new InlineControl("Background", nameof(PianoVisualSettings.ShowBackground)),
            new InlineControl("Notes", nameof(PianoVisualSettings.ShowNotes)),
            new InlineControl("Embers", nameof(PianoVisualSettings.ShowEmbers)),
            new InlineControl("Halo", nameof(PianoVisualSettings.ShowHalo)),
            new InlineControl("Flame", nameof(PianoVisualSettings.ShowFlame)),
            new InlineControl("Keys", nameof(PianoVisualSettings.ShowKeys)),
        })
        {
            var toggle = BuildInlineToggle(spec);
            toggle.MinWidth = 150;
            QuickAdjustLayerHost.Children.Add(toggle);
        }

        QuickAdjustHandColorsHost.Children.Add(BuildQuickHandColorButton(rightHand: false));
        QuickAdjustHandColorsHost.Children.Add(BuildQuickHandColorButton(rightHand: true));
        SyncQuickAdjustState();
    }

    private Button BuildQuickHandColorButton(bool rightHand)
    {
        var property = rightHand ? nameof(PianoVisualSettings.RightHandColor) : nameof(PianoVisualSettings.LeftHandColor);
        var labelKey = rightHand ? "Right Hand" : "Left Hand";
        var dot = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(SafeColor((string)Prop(property).GetValue(_visualSettings)!)),
            BorderBrush = (Brush)FindResource("TextBrush"),
            BorderThickness = new Thickness(.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _quickHandColorDots[property] = dot;
        var label = new TextBlock { Style = (Style)FindResource("LabelTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        Loc.Set(label, labelKey);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(dot);
        content.Children.Add(label);
        var button = new Button
        {
            Tag = property,
            Content = content,
            Style = (Style)FindResource("GhostButtonStyle"),
            MinWidth = 142,
            Margin = new Thickness(3),
            Padding = new Thickness(10, 7, 10, 7)
        };
        Loc.Set(button, labelKey, AutomationProperties.NameProperty);
        Loc.Set(button, "Choose any color for this hand", FrameworkElement.ToolTipProperty);
        button.Click += QuickHandColor_Click;
        return button;
    }

    private void QuickHandColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string property }) return;
        var rightHand = property == nameof(PianoVisualSettings.RightHandColor);
        var current = (string)Prop(property).GetValue(_visualSettings)!;
        var picker = new ColorPickerWindow(current) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } hex) return;
        _handPresetTargetsRight = rightHand;
        ApplyHandColorPreset(hex);
    }

    private void SyncQuickAdjustState()
    {
        if (!_quickAdjustControlsBuilt) return;
        var perHand = _visualSettings.ColorMode == "PerHand";
        QuickAdjustHandColorsCard.Visibility = perHand ? Visibility.Visible : Visibility.Collapsed;
        if (_quickHandSplitRow is not null) _quickHandSplitRow.Visibility = perHand ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (property, dot) in _quickHandColorDots)
            dot.Background = new SolidColorBrush(SafeColor((string)Prop(property).GetValue(_visualSettings)!));
    }

    private void OpenSettingsFromPlayDialog(string page)
    {
        PlayDialogOverlay.Visibility = Visibility.Collapsed;
        SettingsTabs.SelectedIndex = Math.Max(0, SettingsPages.IndexOf(page));
        OpenSettingsPanelFor(NavigationSurface.PlayDialog);
    }

    private void PlayDialogLive_Click(object sender, RoutedEventArgs e)
    {
        HideHandPresetPanel(restoreFocus: false);
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
        HideHandPresetPanel(restoreFocus: false);
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
    private void SyncPlayInlineControls(bool refreshThemeChips = true)
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
            foreach (var combo in _playInlineChoices)
            {
                if (combo.Tag is not string property) continue;
                if (_visualChoices.TryGetValue(property, out var source))
                {
                    if (!ReferenceEquals(combo.ItemsSource, source.ItemsSource)) combo.ItemsSource = source.ItemsSource;
                    if (combo.DisplayMemberPath != source.DisplayMemberPath) combo.DisplayMemberPath = source.DisplayMemberPath;
                    if (combo.SelectedValuePath != source.SelectedValuePath) combo.SelectedValuePath = source.SelectedValuePath;
                }
                combo.SelectedValue = (string)Prop(property).GetValue(_visualSettings)!;
            }
            if (refreshThemeChips) RefreshPlayThemeChips();
        }
        finally { _loadingVisualSettings = wasLoading; }
        SyncQuickAdjustState();
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
