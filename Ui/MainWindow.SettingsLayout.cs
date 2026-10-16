using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PianoPath;

/// <summary>Approved P6 layout. Moves existing elements once; never clones bindings or settings.</summary>
public partial class MainWindow
{
    private readonly Dictionary<Border, (string Origin, string Title)> _settingsCardOrigins = [];
    private readonly List<(Expander Element, List<SettingRow> Rows)> _settingsAdvancedGroups = [];
    private readonly Dictionary<Expander, bool> _settingsExpansionBeforeSearch = [];
    private bool _settingsArContext;

    private static readonly HashSet<string> CommonSettings = new(StringComparer.Ordinal)
    {
        "ShowSheet", "ShowWatermark", "ShowCounter", "ShowFps", "ShowNotes", "ColorMode", "Palette", "NoteColorStart", "NoteColorEnd", "LeftHandColor", "RightHandColor",
        "TrackColors", "NoteStyle", "NoteWidth", "NoteFallSpeed", "NoteDirection", "NoteSatin", "ShowEmbers",
        "ImpactBurst", "ParticleAmount", "ParticleSize", "ParticleGlow", "ShowImpactRings", "ImpactWave", "RingSize",
        "ShowImpactFlash", "ImpactMorph", "UpgradedParticleEffects", "ShowWisps", "WispAmount", "WispHeight",
        "WispGlow", "ShowFlame", "FlameIntensity", "FlameHeight", "FlameColorMode", "ReleaseEffect", "ReleaseIntensity",
        "HoldBar", "HoldBreath", "ShowKeys", "KeyboardSize", "ShowBackground", "BackgroundMode", "BackgroundColor",
        "BackgroundDim", "BloomIntensity", "ShaderKeyLight", "ArKeyLight", "ArEnabled", "ArMode", "ArLivePressTrails",
        "ArNotesInPerspective", "ArViewProfileId", "ArOpacity", "ArDelay", "CameraMirror", "ShowCameraOverlay",
        "CameraSourceLink", "RecordingFormat", "RecordingResolution", "RecordingFrameRate", "RecordAudio",
        "ShellTheme", "ReducedMotion", "ReduceFlashes", "HighContrast", "GpuFrameRate", "GpuVSync"
    };

    private Panel ApprovedCardHost(string origin, string title) => origin switch
    {
        nameof(ThemeSettingsHost) => GeneralSettingsHost,
        nameof(ParticleSettingsHost) => title switch
        {
            "PARTICLE APPEARANCE" => StyleSettingsHost,
            "WISPS" or "FLAMES" => HoldSettingsHost,
            _ => PressSettingsHost
        },
        nameof(NoteSettingsHost) => title switch
        {
            "IMPACT · WAVE & FLASH" => PressSettingsHost,
            "HOLD FX" => HoldSettingsHost,
            "RELEASE FX" => ReleaseSettingsHost,
            "SMART MODULATORS" => LightingSettingsHost,
            _ => NoteSettingsHost
        },
        nameof(KeyboardSettingsHost) => title is "KEY LIGHTING" or "RAY-TRACED SHADING" ? LightingSettingsHost : StageDesignSettingsHost,
        nameof(SceneSettingsHost) => title == "HIT LINE" ? LightingSettingsHost : StageDesignSettingsHost,
        nameof(CameraSettingsHost) => title == "POST FX" ? LightingSettingsHost : CameraSettingsHost,
        nameof(StyleSettingsHost) => StyleSettingsHost,
        nameof(RecordingSettingsHost) => RecordingSettingsHost,
        nameof(GeneralSettingsHost) => GeneralSettingsHost,
        _ => throw new InvalidOperationException($"Unmapped settings host: {origin}")
    };

    private void ArrangeApprovedSettingsLayout()
    {
        // Each card keeps its children, events, tags, choice IDs, custom hosts and title/deep-link key.
        foreach (var card in _settingCards.ToArray())
        {
            var (origin, title) = _settingsCardOrigins[card];
            var destination = ApprovedCardHost(origin, title);
            if (card.Parent is Panel parent && !ReferenceEquals(parent, destination))
            {
                parent.Children.Remove(card);
                destination.Children.Add(card);
            }
            foreach (var row in _settingRows.Where(row => ReferenceEquals(row.Card, card))) row.Page = destination;
        }

        // A setting now has one detailed owner. Old layer shortcuts become navigation, not a second value.
        foreach (var duplicates in _settingRows.Where(row => row.Property is not null).GroupBy(row => row.Property).Where(group => group.Count() > 1).ToArray())
        {
            var owner = duplicates.Last();
            foreach (var alias in duplicates.Where(row => !ReferenceEquals(row, owner)).ToArray())
            {
                if (alias.Element.Parent is Panel parent) parent.Children.Remove(alias.Element);
                if (alias.Property is { } property && _visualToggles.TryGetValue(property, out var live))
                    _visualToggleList.RemoveAll(check => Equals(check.Tag, property) && !ReferenceEquals(check, live));
                _settingRows.Remove(alias);
            }
        }
        MoveSettingsProperty(nameof(PianoVisualSettings.ShowNotes), (Panel)_cardsByTitle["SHAPE & STYLE"].Child);
        MoveSettingsProperty(nameof(PianoVisualSettings.ShowBackground), (Panel)_cardsByTitle["BACKGROUND"].Child);
        var beams = Card(LightingSettingsHost, "LIGHT BEAMS", "Shared light-beam controls, kept together instead of split across layer and background pages.");
        MoveSettingsProperty(nameof(PianoVisualSettings.ShowLightBeams), beams);
        MoveSettingsProperty(nameof(PianoVisualSettings.BeamIntensity), beams);
        var arLight = Card(LightingSettingsHost, "AR KEY LIGHTING", "Key tint on the real keyboard. Camera alignment and live-note timing stay on Camera & AR.");
        MoveSettingsProperty(nameof(PianoVisualSettings.ArKeyLight), arLight);
        // Theme is prominent within Application, rather than below profile/update actions.
        var themeCards = _settingCards.Where(card => _settingsCardOrigins[card].Origin == nameof(ThemeSettingsHost)).ToArray();
        for (var i = themeCards.Length - 1; i >= 0; i--) { GeneralSettingsHost.Children.Remove(themeCards[i]); GeneralSettingsHost.Children.Insert(0, themeCards[i]); }

        RegisterBusinessCard(AudioBusinessCard, SoundSettingsHost, "PIANO", "EFFECTS", "OUTPUT & LATENCY", "Piano sound", "Audio output", "Target latency", "Master piano level", "reverb resonance Salamander ASIO impulse audio output sound latency gain");
        RegisterBusinessCard(MidiBusinessCard, SoundSettingsHost, "DEVICES", "MIDI input", "MIDI output", "SONG TRACKS", "METRONOME", "midi devices track solo mute color refresh");
        RegisterBusinessCard(PracticeBusinessCard, SessionSettingsHost, "PRACTICE MODE", "Playback tempo", "Auto practice tempo", "Misses before slowing down", "LOOP RANGE", "SHORTCUTS", "practice loop tempo hands follow wait");
        RegisterBusinessCard(HistoryBusinessCard, SessionSettingsHost, "PRACTICE HISTORY", "PRACTICE CHART", "GHOST OF THIS SONG", "history chart ghost best latest export clear");
        // Static preset/share-code controls are searchable, without reparenting their named elements.
        var overviewBody = (Panel)StyleSettingsHost.Parent;
        foreach (var element in overviewBody.Children.OfType<FrameworkElement>().Where(element => !ReferenceEquals(element, StyleSettingsHost)).ToArray())
            _settingsOverviewElements.Add(element);

        foreach (var row in _settingRows.ToArray()) AddSettingsScopeBadge(row);
        foreach (var card in _settingCards.ToArray()) AddAdvancedSettingsGroup(card);
        // Reflection-based diagnostics and keyboard traversal use the same order as the actual visual tree.
        var ordered = new List<SettingRow>();
        foreach (var page in SettingsPages.Order.Select((_, index) => SettingsPageHost(index)).Where(page => page is not null))
            CollectSettingsRows(page!, ordered);
        _settingRows.Clear(); _settingRows.AddRange(ordered);
        SetSettingsContext(_visualSettings.ArEnabled);
    }

    private readonly List<FrameworkElement> _settingsOverviewElements = [];

    private void MoveSettingsProperty(string property, Panel destination)
    {
        var row = _settingRows.Single(row => row.Property == property);
        if (row.Element.Parent is Panel previous) previous.Children.Remove(row.Element);
        // Below the heading and description, before the card's existing controls.
        destination.Children.Insert(Math.Min(2, destination.Children.Count), row.Element);
        row.Card = (Border)destination.Parent;
        row.Page = (Panel)row.Card.Parent;
    }

    private void RegisterBusinessCard(Border card, Panel page, params string[] keys)
    {
        _settingCards.Add(card);
        _settingRows.Add(new SettingRow { Element = (FrameworkElement)card.Child, Card = card, Page = page, SearchKeys = keys });
    }

    private static void CollectSettingsRows(DependencyObject node, List<SettingRow> target, IEnumerable<SettingRow> rows)
    {
        var row = rows.FirstOrDefault(row => ReferenceEquals(row.Element, node));
        if (row is not null) { target.Add(row); return; }
        if (node is Panel panel) foreach (UIElement panelChild in panel.Children) CollectSettingsRows(panelChild, target, rows);
        else if (node is Decorator decorator && decorator.Child is { } decoratorChild) CollectSettingsRows(decoratorChild, target, rows);
        else if (node is ContentControl content && content.Content is DependencyObject value) CollectSettingsRows(value, target, rows);
    }

    private void CollectSettingsRows(DependencyObject node, List<SettingRow> target) => CollectSettingsRows(node, target, _settingRows);

    private void AddAdvancedSettingsGroup(Border card)
    {
        if (card.Child is not Panel body) return;
        var rows = _settingRows.Where(row => ReferenceEquals(row.Card, card) && row.Property is not null && !CommonSettings.Contains(row.Property)).ToList();
        if (rows.Count == 0) return;
        var content = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var advanced = new Expander { Content = content, Margin = new Thickness(0, 10, 0, 0), Foreground = (Brush)FindResource("MutedTextBrush"), IsExpanded = false };
        Loc.Set(advanced, "Advanced", HeaderedContentControl.HeaderProperty);
        Loc.Set(advanced, "Advanced", AutomationProperties.NameProperty);
        foreach (var row in rows) { body.Children.Remove(row.Element); content.Children.Add(row.Element); }
        body.Children.Add(advanced);
        _settingsAdvancedGroups.Add((advanced, rows));
    }

    private void AddSettingsScopeBadge(SettingRow row)
    {
        if (row.Property is not { } property) return;
        var scope = property.StartsWith("Ar", StringComparison.Ordinal) ? "AR only"
            : property is "ShowKeys" or "KeyboardStyle" or "KeyboardScale" or "KeyOverhang" or "KeyLabels" or "ShowKeyShadow" or "ShowKeyFelt" or "KeyFeltColor" or "ShaderCameraTilt" or "ShaderKeyLight" or "ShaderShadows" or "ShaderAmbientOcclusion" or "ShaderGloss" ? "Stage only"
            : ReferenceEquals(row.Page, NoteSettingsHost) || ReferenceEquals(row.Page, PressSettingsHost) || ReferenceEquals(row.Page, HoldSettingsHost) || ReferenceEquals(row.Page, ReleaseSettingsHost) || property == nameof(PianoVisualSettings.UpgradedParticleEffects) ? "Shared" : null;
        if (scope is null || row.Element.Parent is not Panel parent) return;
        var original = row.Element; var index = parent.Children.IndexOf(original); parent.Children.RemoveAt(index);
        var wrapper = new StackPanel();
        var badge = new TextBlock { FontSize = 8.5, Foreground = (Brush)FindResource("DimTextBrush"), Margin = new Thickness(0, 5, 0, 0) };
        Loc.Set(badge, scope); wrapper.Children.Add(badge); wrapper.Children.Add(original);
        parent.Children.Insert(index, wrapper); row.Element = wrapper;
        // Context badges are informational. Do not disable a live editor or change an app/device mode.
    }

    private void RefreshSettingsLayoutSearch(bool searching)
    {
        foreach (var (advanced, rows) in _settingsAdvancedGroups)
        {
            advanced.Visibility = rows.Any(row => row.Element.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
            if (searching)
            {
                _settingsExpansionBeforeSearch.TryAdd(advanced, advanced.IsExpanded);
                if (advanced.Visibility == Visibility.Visible) advanced.IsExpanded = true;
            }
            else if (_settingsExpansionBeforeSearch.Remove(advanced, out var wasExpanded)) advanced.IsExpanded = wasExpanded;
        }
        var overviewMatch = !searching || new[] { "PRESETS", "SHARE CODE", "Apply preset", "Apply code", "preset import export save delete reset share code" }.Any(key => SettingsPhraseMatches(key, SettingsSearchBox.Text));
        foreach (var element in _settingsOverviewElements) element.Visibility = overviewMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsStageContext_Click(object sender, RoutedEventArgs e) => SetSettingsContext(false);
    private void SettingsArContext_Click(object sender, RoutedEventArgs e) => SetSettingsContext(true);

    private void SetSettingsContext(bool ar)
    {
        _settingsArContext = ar;
        SettingsStageContextButton.IsChecked = !ar; SettingsArContextButton.IsChecked = ar;
        Loc.Set(SettingsStageContextButton, "Settings context only. Shared values, camera sources and calibration are unchanged.", FrameworkElement.ToolTipProperty);
        Loc.Set(SettingsArContextButton, "Settings context only. Shared values, camera sources and calibration are unchanged.", FrameworkElement.ToolTipProperty);
        // Selection context is session-only. Never call ApplyVisualSettings, MarkModified, CopyFrom or a camera handler here.
    }

    private static string NormalizeSettingsSearch(string value)
    {
        var text = new System.Text.StringBuilder();
        foreach (var character in value.Normalize(System.Text.NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) text.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        return text.ToString();
    }

    private static bool SettingsPhraseMatches(string phrase, string query)
    {
        var variants = NormalizeSettingsSearch(phrase + " " + (Loc.Known(phrase) ? Loc.T(phrase) : "") + " " + (StringsVietnamese.Table.TryGetValue(phrase, out var vietnamese) ? vietnamese : ""));
        return SplitQuery(NormalizeSettingsSearch(query)).All(token => variants.Contains(token, StringComparison.Ordinal));
    }
}
