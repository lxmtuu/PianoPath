using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PianoPath;

/// <summary>Settings dock: declarative page builder, presets, search, per-track controls and recording options.</summary>
public partial class MainWindow
{
    private sealed class SettingRow
    {
        public required FrameworkElement Element;
        /// <summary>
        /// English source strings this row answers to. The search box matches a row in any bundled
        /// language, so a Vietnamese user can type either "tốc độ" or "speed" and find the same
        /// slider — the keys stay English, the match runs through <see cref="Loc.T"/>.
        /// </summary>
        public required string[] SearchKeys;
        public required Panel Page;
        public required Border Card;
        public string? Property;
        public Func<bool>? VisibleWhen;
        /// <summary>
        /// The label element this row paints its caption into, so the search can highlight the word it
        /// matched (see <see cref="SetCaption"/>). Rows without a caption (a button row) leave it null.
        /// </summary>
        public (DependencyObject Target, DependencyProperty Property, string Key)? Caption;

        /// <summary>Notes which label element holds the row caption; returns the row so builders can chain.</summary>
        public SettingRow WithCaption(DependencyObject target, DependencyProperty property, string key)
        {
            Caption = (target, property, key);
            return this;
        }
    }

    private readonly List<SettingRow> _settingRows = [];
    private readonly List<Border> _settingCards = [];
    private readonly Dictionary<string, TextBox> _visualValueBoxes = [];
    private readonly Dictionary<string, (double Min, double Max)> _sliderRanges = [];
    private readonly Dictionary<string, ComboBox> _visualChoices = [];
    /// <summary>Raw (value, caption) pairs of every picker, so a language switch can rebuild its items without losing the English captions.</summary>
    private readonly Dictionary<string, (string Value, string Caption)[]> _visualChoiceOptions = [];
    private readonly Dictionary<string, CheckBox> _visualToggles = [];
    /// <summary>The camera rows live on the Camera & FX page; the status line and the list are rebuilt on demand.</summary>
    private TextBlock? _cameraStatus;
    private TextBlock? _handStatus;
    private IReadOnlyList<CameraInfo> _cameraDevices = [];
    private string? _cameraListError;
    /// <summary>Every generated switch; a layer such as sparks appears both on the Style page and on its own page.</summary>
    private readonly List<CheckBox> _visualToggleList = [];
    private readonly List<Button> _trackPaletteSwatches = [];
    /// <summary>Chip host of the Theme page and its caption, filled by <see cref="RefreshThemeChips"/>.</summary>
    private WrapPanel? themeChipHost;
    private TextBlock? themeBlurbLabel;
    private readonly HashSet<int> _mutedTracks = [];
    private readonly List<VisualPreset> _presets = [];
    private bool _presetListLoading;
    private static readonly Regex NoteNamePattern = new(@"^\s*([A-Ga-g])\s*([#♯bB]?)\s*(-?\d)\s*$", RegexOptions.Compiled);
    private static readonly PianoVisualSettings DefaultVisualSettings = VisualPresets.NeonViolet();

    /// <summary>
    /// Extra words a row answers to besides its own captions. The search box runs over these, the row's
    /// search keys and the name of the setting itself, so people find a slider by the word they already
    /// know ("tempo" for the fall speed, "brightness" for a glow amount, "fps" for the recorder) without
    /// anyone renaming a label or inventing a fake row. A query may mix several words in any order, and
    /// translated captions match too, through <see cref="Loc"/> — see <see cref="MatchesSearch"/>.
    /// </summary>
    private static readonly Dictionary<string, string> SearchSynonyms = new(StringComparer.Ordinal)
    {
        [nameof(PianoVisualSettings.NoteFallSpeed)] = "tempo speed velocity scroll fast slow fps",
        [nameof(PianoVisualSettings.NoteGlow)] = "brightness halo shine luminance",
        [nameof(PianoVisualSettings.NoteTint)] = "opacity alpha transparency",
        [nameof(PianoVisualSettings.NoteWidth)] = "thickness bar lane size",
        [nameof(PianoVisualSettings.NoteRoundness)] = "corner radius curvature",
        [nameof(PianoVisualSettings.NoteEdge)] = "outline stroke border brightness",
        [nameof(PianoVisualSettings.NoteEdgeWidth)] = "outline stroke thickness",
        [nameof(PianoVisualSettings.NoteMinLength)] = "short duration staccato",
        [nameof(PianoVisualSettings.NoteRefraction)] = "highlight sheen gloss",
        [nameof(PianoVisualSettings.HandSplitPitch)] = "split middle c hand division",
        [nameof(PianoVisualSettings.ZoneSplitPitch)] = "split zone bass treble",
        [nameof(PianoVisualSettings.RecordingFrameRate)] = "fps frames rate video",
        [nameof(PianoVisualSettings.RecordingResolution)] = "size 720p 1080p window video",
        [nameof(PianoVisualSettings.BackgroundDim)] = "darken dim opacity",
        [nameof(PianoVisualSettings.ParticleAmount)] = "count quantity density sparks",
        [nameof(PianoVisualSettings.ParticleLife)] = "duration seconds",
        [nameof(PianoVisualSettings.ParticleGlow)] = "brightness laser",
        [nameof(PianoVisualSettings.Gravity)] = "falling weight force",
        [nameof(PianoVisualSettings.Drag)] = "friction air resistance",
        [nameof(PianoVisualSettings.KeyLighting)] = "light brightness led",
        [nameof(PianoVisualSettings.KeyPressDepth)] = "travel sink press",
        [nameof(PianoVisualSettings.ShadingQuality)] = "shader render raytrace quality",
        [nameof(PianoVisualSettings.CameraZoom)] = "scale framing size",
        [nameof(PianoVisualSettings.CameraParallax)] = "shake drift motion 3d",
        [nameof(PianoVisualSettings.BloomIntensity)] = "glow shine",
        [nameof(PianoVisualSettings.BloomSize)] = "glow spread radius",
        [nameof(PianoVisualSettings.Contrast)] = "grading curve",
        [nameof(PianoVisualSettings.Saturation)] = "vibrance colour color grading",
        [nameof(PianoVisualSettings.Vignette)] = "dark corners edge",
        [nameof(PianoVisualSettings.HaloIntensity)] = "hit line brightness",
        [nameof(PianoVisualSettings.TempoSync)] = "beat metronome pulse",
        [nameof(PianoVisualSettings.PedalGlow)] = "sustain damper glow",
        [nameof(PianoVisualSettings.VelocityColor)] = "dynamics velocity colour",
        [nameof(PianoVisualSettings.ShowPetals)] = "dust motes ambient floating",
        [nameof(PianoVisualSettings.ShowEmbers)] = "sparks fire particles",
        [nameof(PianoVisualSettings.ShowWisps)] = "plasma smoke trails",
        [nameof(PianoVisualSettings.ShowFlame)] = "fire burning",
        [nameof(PianoVisualSettings.ShowImpactRings)] = "wave ring shockwave bounce",
        [nameof(PianoVisualSettings.ShowLightBeams)] = "beams columns rays light",
        [nameof(PianoVisualSettings.ShowHalo)] = "hit line glowing",
        [nameof(PianoVisualSettings.ShowKeys)] = "keyboard piano keys",
        [nameof(PianoVisualSettings.ShowBackground)] = "backdrop scene image",
        [nameof(PianoVisualSettings.ShowWatermark)] = "logo keyflow brand",
        [nameof(PianoVisualSettings.ShowCounter)] = "count keys held hud",
        [nameof(PianoVisualSettings.ShowFps)] = "performance overlay stats",
        [nameof(PianoVisualSettings.ShowNoteLabels)] = "names letters on bars",
        [nameof(PianoVisualSettings.Language)] = "language english vietnamese follow windows",
        [nameof(PianoVisualSettings.ShellTheme)] = "theme shell colour palette skin",
        [nameof(PianoVisualSettings.ChromeMotion)] = "animation transitions easing",
        [nameof(PianoVisualSettings.BackdropDensity)] = "motes dust density backdrop",
    };

    // =====================================================================================================
    // Page construction
    // =====================================================================================================

    private void BuildVisualSettingsControls()
    {
        _loadingVisualSettings = true;
        BuildStylePage(); BuildThemePage(); BuildNotesPage(); BuildParticlesPage(); BuildKeyboardPage(); BuildBackgroundPage(); BuildCameraPage(); BuildRecordingPage(); BuildGeneralPage();
        _loadingVisualSettings = false;
        // Pickers hold translated captions, so they are repainted from their raw option list whenever
        // the language changes (the selected values themselves never move).
        Loc.OnChanged(RefreshChoiceCaptions);
        Loc.OnChanged(RefreshLanguageChips);
        // The theme chips are generated, so they have to be filled once the pages exist; the menu
        // picker shares the same list of themes.
        RefreshThemeChips(); RefreshMenuThemeChips();
        LoadPresetList();
        RefreshDependentRows();
        UpdateRecordingInfo();
        UpdatePresetLabels();
    }

    private void BuildStylePage()
    {
        var body = Card(StyleSettingsHost, "LAYERS", "Quick switches for every layer of the stage. The › button beside a layer opens the card with its detailed controls.");
        LayerToggle(body, "Falling notes", nameof(PianoVisualSettings.ShowNotes), "Draw the piano-roll bars for MIDI playback and live playing.", "SHAPE & STYLE");
        Toggle(body, "Sheet music", nameof(PianoVisualSettings.ShowSheet), "Grand staff above the roll, following the playhead: MusicXML and MIDI notes written on the staff their hand split puts them on.");
        LayerToggle(body, "Sparks", nameof(PianoVisualSettings.ShowEmbers), "Particle burst when a note reaches the keyboard.", "SPARKS · EMITTER");
        LayerToggle(body, "Wisps", nameof(PianoVisualSettings.ShowWisps), "Smoke-like plasma streams rising from held keys (Embers style).", "WISPS");
        LayerToggle(body, "Flames", nameof(PianoVisualSettings.ShowFlame), "Fire bursts at the impact point.", "FLAMES");
        LayerToggle(body, "Impact rings", nameof(PianoVisualSettings.ShowImpactRings), "Expanding shock ring when a note hits the key line.", "IMPACT · WAVE & FLASH");
        LayerToggle(body, "Light beams", nameof(PianoVisualSettings.ShowLightBeams), "Soft columns of light above every sounding key.", "ATMOSPHERE");
        LayerToggle(body, "Hit line halo", nameof(PianoVisualSettings.ShowHalo), "Glowing line where the notes meet the keys.", "HIT LINE");
        LayerToggle(body, "Piano keys", nameof(PianoVisualSettings.ShowKeys), "Show the 88-key keyboard.", "KEYBOARD");
        LayerToggle(body, "Background layers", nameof(PianoVisualSettings.ShowBackground), "Image, gradient, stars and guide lanes.", "BACKGROUND");
        Toggle(body, "Keyflow watermark", nameof(PianoVisualSettings.ShowWatermark), "Small logo above the keyboard.");
        Toggle(body, "Key counter", nameof(PianoVisualSettings.ShowCounter), "Show how many keys are held.");
        Toggle(body, "FPS & particle HUD", nameof(PianoVisualSettings.ShowFps), "Performance overlay in the top-right corner.");

        // the three places most looks are tuned in, one click away from the switches
        var more = Card(StyleSettingsHost, "GO TO", "The note's journey, the sky behind it and the engine that draws it all.");
        ButtonRow(more,
            ("NOTE EFFECTS ›", (_, _) => JumpToCard("FALLING FX")),
            ("AMBIENT LAYERS ›", (_, _) => JumpToCard("AMBIENT LAYERS")),
            ("GRAPHICS ENGINE ›", (_, _) => JumpToCard("GRAPHICS ENGINE")));
    }

    /// <summary>
    /// The Theme page: the look of the application shell (independent from the piano stage itself),
    /// how much the chrome animates, and the ambient mote layer that can float over the stage.
    /// </summary>
    private void BuildThemePage()
    {
        var shell = Card(ThemeSettingsHost, "INTERFACE THEME", "The chrome around the stage: surfaces, accents and the animated acoustic backdrop. Applying a stage preset also switches the theme that belongs to it.");
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        themeChipHost = chips;
        Register(shell, chips, nameof(PianoVisualSettings.ShellTheme), "interface theme shell concert grand noir obsidian velvet gold backdrop");
        themeBlurbLabel = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 2, 0, 8) };
        Register(shell, themeBlurbLabel, null, "theme description");
        Choice(shell, "Motion", nameof(PianoVisualSettings.ChromeMotion), "How much the interface moves: animated acoustic backdrop, panel transitions and button response. Off keeps everything still.",
            ("Off", "Off"), ("Calm", "Calm"), ("Full", "Full"));
        SliderRow(shell, "Backdrop density", nameof(PianoVisualSettings.BackdropDensity), 0, 200, "Density of the floating concert dust motes and acoustic waves in the backdrop.");

        var custom = Card(ThemeSettingsHost, "USER THEMES", "Themes you made: pick five colours and a backdrop family, and the rest of the chrome follows. They live in the settings folder as small JSON files and show up in both theme chip rows.");
        ButtonRow(custom,
            ("Create theme", CreateTheme_Click),
            ("Edit theme", EditTheme_Click),
            ("Delete theme", DeleteTheme_Click));
        Note(custom, "Only themes you made can be edited or deleted; the three built-in looks stay as they are. A theme named after a built-in one is refused, and saving over one of your own names updates it.");

        var looks = Card(ThemeSettingsHost, "QUICK LOOKS", "One click applies a complete concert look: stage preset plus matching interface theme.");
        ButtonRow(looks,
            ("Concert Grand", (_, _) => ApplyBuiltInPreset("Neon Violet")),
            ("Concert Gold", (_, _) => ApplyBuiltInPreset("Concert Gold")),
            ("Moonlight", (_, _) => ApplyBuiltInPreset("Moonlight Sonata")));
        Note(looks, "Every preset can be edited afterwards; the pages next to this one keep the piano roll, keyboard and camera in sync with the new theme.");
    }

    /// <summary>Applies a built-in preset named on the command line; spaces and case do not matter.</summary>
    internal void PreviewPreset(string name)
    {
        static string Key(string value) => value.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        var preset = VisualPresets.BuiltIn.FirstOrDefault(candidate => Key(candidate.Name) == Key(name));
        if (preset is not null) ApplyBuiltInPreset(preset.Name);
    }

    /// <summary>Applies a built-in preset by name (used by the Theme page quick looks).</summary>
    private void ApplyBuiltInPreset(string name)
    {
        if (VisualPresets.FindBuiltIn(name) is not { } preset) return;
        ApplyPreset(preset);
        LoadPresetList(preset.Name);
        RefreshMenuStageLook();
    }

    /// <summary>Rebuilds the theme chips of the Theme page; the active theme is marked.</summary>
    private void RefreshThemeChips()
    {
        if (themeChipHost is null) return;
        themeChipHost.Children.Clear();
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
            // The chip prints the localised theme name; the id it stores stays English.
            Loc.Bind(chip, () => Loc.F(active ? "✦  {0}" : "{0}", Loc.T(theme.Name)));
            Loc.Set(chip, theme.Blurb, FrameworkElement.ToolTipProperty);
            chip.Click += ThemeChip_Click;
            themeChipHost.Children.Add(chip);
        }
        if (themeBlurbLabel is not null) Loc.Set(themeBlurbLabel, ShellThemeManager.Current.Blurb);
    }

    /// <summary>The generated host of a page in tab-strip order, or null for pages built directly in XAML.</summary>
    private Panel? SettingsPageHost(int index)
    {
        if (index < 0 || index >= SettingsPages.Order.Length) return null;
        return SettingsPages.Order[index] switch
        {
            SettingsPages.Style => StyleSettingsHost,
            SettingsPages.Theme => ThemeSettingsHost,
            SettingsPages.Notes => NoteSettingsHost,
            SettingsPages.Particles => ParticleSettingsHost,
            SettingsPages.Keyboard => KeyboardSettingsHost,
            SettingsPages.Background => SceneSettingsHost,
            SettingsPages.Camera => CameraSettingsHost,
            SettingsPages.Recording => RecordingSettingsHost,
            SettingsPages.General => GeneralSettingsHost,
            _ => null
        };
    }

    private void BuildNotesPage()
    {
        var color = Card(NoteSettingsHost, "COLOR", "How each note picks its color.");
        Choice(color, "Color mode", nameof(PianoVisualSettings.ColorMode), "Gradient across the keyboard, one color per hand, per MIDI track, or animated rainbows.",
            ("Gradient", "Gradient by pitch"), ("PerHand", "Left / right hand"), ("PerTrack", "Per MIDI track"), ("RainbowPitch", "Rainbow by pitch"), ("RainbowTime", "Rainbow cycling in time"));
        Choice(color, "Palette", nameof(PianoVisualSettings.Palette), "Built-in gradient families. Editing the start/end colors switches to Custom.",
            ("Spectrum", "Spectrum"), ("Aurora", "Aurora"), ("Fire", "Fire"), ("Ocean", "Ocean"), ("Violet", "Violet"), ("Custom", "Custom"))
            .VisibleWhen = () => _visualSettings.ColorMode == "Gradient";
        ColorRow(color, "Gradient start (low keys)", nameof(PianoVisualSettings.NoteColorStart), "Color of the lowest notes.").VisibleWhen = () => _visualSettings.ColorMode == "Gradient";
        ColorRow(color, "Gradient end (high keys)", nameof(PianoVisualSettings.NoteColorEnd), "Color of the highest notes.").VisibleWhen = () => _visualSettings.ColorMode == "Gradient";
        ColorRow(color, "Left hand", nameof(PianoVisualSettings.LeftHandColor), "Notes below the split point.").VisibleWhen = () => _visualSettings.ColorMode == "PerHand";
        ColorRow(color, "Right hand", nameof(PianoVisualSettings.RightHandColor), "Notes at or above the split point.").VisibleWhen = () => _visualSettings.ColorMode == "PerHand";
        SliderRow(color, "Hand split point", nameof(PianoVisualSettings.HandSplitPitch), 21, 108, "MIDI note where the right hand begins (C4 = 60). Type a note name such as C4 or F#3.").VisibleWhen = () => _visualSettings.ColorMode == "PerHand";
        Toggle(color, "Infer hand split from the song", nameof(PianoVisualSettings.InferHandSplit), "When a MIDI file opens, take the split point from how its notes are spread over the keyboard and remember it for that song.");
        TrackPaletteRow(color).VisibleWhen = () => _visualSettings.ColorMode == "PerTrack";
        SliderRow(color, "Rainbow speed", nameof(PianoVisualSettings.RainbowSpeed), 0, 100, "How fast the hue cycles.").VisibleWhen = () => _visualSettings.ColorMode == "RainbowTime";

        var shape = Card(NoteSettingsHost, "SHAPE & STYLE", "Silhouette of the falling bars.");
        Choice(shape, "Note style", nameof(PianoVisualSettings.NoteStyle), "Solid bars, hollow neon tubes, glossy glass or burning fire notes.",
            ("Solid", "Solid"), ("Neon", "Neon outline"), ("Glass", "Glass"), ("Fire", "Fire / burning"));
        SliderRow(shape, "Note width", nameof(PianoVisualSettings.NoteWidth), 30, 100, "Width of a note relative to its key lane.");
        SliderRow(shape, "Corner roundness", nameof(PianoVisualSettings.NoteRoundness), 0, 100, "Rounded corners of every bar.");
        SliderRow(shape, "Minimum length", nameof(PianoVisualSettings.NoteMinLength), 4, 60, "Very short notes are stretched to at least this many pixels.");
        SliderRow(shape, "Gap between notes", nameof(PianoVisualSettings.NoteGap), 0, 12, "Space carved between consecutive notes of the same key.");
        SliderRow(shape, "Fire texture", nameof(PianoVisualSettings.NoteTexture), 0, 100, "Strength of the animated ember holes in the Fire style.").VisibleWhen = () => _visualSettings.NoteStyle == "Fire";
        Toggle(shape, "3D shading", nameof(PianoVisualSettings.Notes3D), "Inner shadow and highlight on solid and glass notes.");
        Toggle(shape, "Note names on bars", nameof(PianoVisualSettings.ShowNoteLabels), "Print the note name inside each bar when there is room.");

        var glow = Card(NoteSettingsHost, "GLOW & EDGES", "Bloom, outline and highlights.");
        SliderRow(glow, "Tint / opacity", nameof(PianoVisualSettings.NoteTint), 0, 100, "Fill opacity of the bars.");
        SliderRow(glow, "Bloom / glow", nameof(PianoVisualSettings.NoteGlow), 0, 200, "Soft halo around every note.");
        SliderRow(glow, "Edge brightness", nameof(PianoVisualSettings.NoteEdge), 0, 200, "Brightness of the outline stroke.");
        SliderRow(glow, "Edge width", nameof(PianoVisualSettings.NoteEdgeWidth), 0, 100, "Thickness of the outline (the tube in Neon style).");
        SliderRow(glow, "Leading-edge glow", nameof(PianoVisualSettings.NoteHeadGlow), 0, 100, "Bright cap on the edge that leads (bottom while falling, top while rising), stronger while the note sounds.");
        SliderRow(glow, "Light refraction", nameof(PianoVisualSettings.NoteRefraction), 0, 100, "Thin white highlight along the left edge.");

        var motion = Card(NoteSettingsHost, "MOTION", "Speed and travel of the piano roll.");
        SliderRow(motion, "Fall speed", nameof(PianoVisualSettings.NoteFallSpeed), 100, 1000, "Pixels per second for live trails; MIDI notes scale with it.");
        Choice(motion, "Direction", nameof(PianoVisualSettings.NoteDirection), "Down: notes fall onto the keys and sink below the hit line. Up: notes are born at the keys on onset and rise out of the top of the stage.",
            ("Down", "Fall down"), ("Up", "Rise up"));
        Note(motion, "Only a physically held key extends its visual note. Pedals sustain the audio without stretching the bar after key release.");

        var falling = Card(NoteSettingsHost, "FALLING FX", "Trails, echoes and pulsing while notes travel.");
        Choice(falling, "Trail", nameof(PianoVisualSettings.FallingTrail), "Light dragging behind every falling note.",
            ("None", "None"), ("Glow", "Glow"), ("Sparkles", "Sparkles"), ("Speed Lines", "Speed lines"), ("Blur", "Motion blur"), ("Ribbon", "Ribbon"), ("Rainbow", "Rainbow"), ("Stream", "Particle stream"));
        SliderRow(falling, "Trail intensity", nameof(PianoVisualSettings.FallingTrailIntensity), 0, 100, "Brightness of the trail.").VisibleWhen = () => _visualSettings.FallingTrail != "None";
        SliderRow(falling, "Trail length", nameof(PianoVisualSettings.FallingTrailLength), 0, 100, "How far the trail reaches behind the note.").VisibleWhen = () => _visualSettings.FallingTrail != "None";
        Toggle(falling, "Pulsing", nameof(PianoVisualSettings.FallingPulse), "Notes breathe bright and dim while falling.");
        SliderRow(falling, "Pulse rate", nameof(PianoVisualSettings.FallingPulseRate), 0, 100, "How fast the notes pulse.").VisibleWhen = () => _visualSettings.FallingPulse;
        Toggle(falling, "Ghost echoes", nameof(PianoVisualSettings.FallingGhost), "Faint echo copies lead each note.");
        SliderRow(falling, "Ghost amount", nameof(PianoVisualSettings.FallingGhostAmount), 0, 100, "Visibility and number of the echoes.").VisibleWhen = () => _visualSettings.FallingGhost;

        var impact = Card(NoteSettingsHost, "IMPACT · WAVE & FLASH", "The first half second after a note lands on the keys. Size and brightness follow the hit strength.");
        Toggle(impact, "Enable impact wave", nameof(PianoVisualSettings.ShowImpactRings), "Expanding wave on every hit.");
        Choice(impact, "Wave style", nameof(PianoVisualSettings.ImpactWave), "Hollow acoustic ring, a filled shockwave blast or flat water ripples.",
            ("Ring", "Ring"), ("Shockwave", "Shockwave"), ("Ripple", "Ripple"), ("None", "None")).VisibleWhen = () => _visualSettings.ShowImpactRings;
        SliderRow(impact, "Wave size", nameof(PianoVisualSettings.RingSize), 0, 100, "Final radius of the wave.").VisibleWhen = () => _visualSettings.ShowImpactRings && _visualSettings.ImpactWave != "None";
        SliderRow(impact, "Wave intensity", nameof(PianoVisualSettings.ImpactWaveIntensity), 0, 150, "Brightness of the wave.").VisibleWhen = () => _visualSettings.ShowImpactRings && _visualSettings.ImpactWave != "None";
        Choice(impact, "Note morph", nameof(PianoVisualSettings.ImpactMorph), "What the note itself becomes when it lands.",
            ("None", "None"), ("Shatter", "Shatter"), ("Melt", "Melt"), ("Absorb", "Absorb"), ("Bounce", "Bounce"), ("Morph", "Star morph")).VisibleWhen = () => _visualSettings.ShowImpactRings;
        SliderRow(impact, "Morph intensity", nameof(PianoVisualSettings.ImpactMorphIntensity), 0, 100, "Strength of the morph.").VisibleWhen = () => _visualSettings.ShowImpactRings && _visualSettings.ImpactMorph != "None";
        Toggle(impact, "Impact flash", nameof(PianoVisualSettings.ShowImpactFlash), "White-hot flare at the hit point, fading in about 180 ms.");
        Choice(impact, "Flash style", nameof(PianoVisualSettings.ImpactFlashStyle), "A white-hot flare, a lightning strike or a plasma ball.",
            ("Flash", "Flash"), ("Lightning", "Lightning"), ("Plasma", "Plasma")).VisibleWhen = () => _visualSettings.ShowImpactFlash;
        SliderRow(impact, "Flash intensity", nameof(PianoVisualSettings.ImpactFlashIntensity), 0, 100, "Brightness of the hit flash.").VisibleWhen = () => _visualSettings.ShowImpactFlash;
        Note(impact, "The particle burst of the impact (its amount, style and physics) is tuned on the Particles page.");

        var hold = Card(NoteSettingsHost, "HOLD FX", "What sounding notes and held keys do while the key stays down.");
        Toggle(hold, "Hold bar highlight", nameof(PianoVisualSettings.HoldBar), "The sounding bar burns brighter with a hot outline.");
        SliderRow(hold, "Hold bar intensity", nameof(PianoVisualSettings.HoldBarIntensity), 0, 100, "Strength of the highlight.").VisibleWhen = () => _visualSettings.HoldBar;
        Toggle(hold, "Breathing glow", nameof(PianoVisualSettings.HoldBreath), "Held keys and notes breathe bright and dim.");
        SliderRow(hold, "Breath rate", nameof(PianoVisualSettings.HoldBreathRate), 0, 100, "How fast the glow breathes.").VisibleWhen = () => _visualSettings.HoldBreath;
        Toggle(hold, "Vibration", nameof(PianoVisualSettings.HoldVibration), "Held notes tremble subtly.");
        SliderRow(hold, "Vibration amount", nameof(PianoVisualSettings.HoldVibrationAmount), 0, 100, "Strength of the tremble.").VisibleWhen = () => _visualSettings.HoldVibration;
        Toggle(hold, "Color cycle", nameof(PianoVisualSettings.HoldColorCycle), "Held notes keep shifting hue.");
        SliderRow(hold, "Cycle speed", nameof(PianoVisualSettings.HoldColorCycleSpeed), 0, 100, "How fast the hue cycles.").VisibleWhen = () => _visualSettings.HoldColorCycle;
        Toggle(hold, "Electric arc", nameof(PianoVisualSettings.HoldElectricArc), "Crackling arcs chain simultaneously held keys.");
        SliderRow(hold, "Arc intensity", nameof(PianoVisualSettings.HoldArcIntensity), 0, 100, "Brightness of the arcs.").VisibleWhen = () => _visualSettings.HoldElectricArc;

        var release = Card(NoteSettingsHost, "RELEASE FX", "What happens at the key when a note ends.");
        Choice(release, "Release effect", nameof(PianoVisualSettings.ReleaseEffect), "The farewell of every note: fade, float, dissolve, smoke, snap or echo.",
            ("Fade", "Fade out"), ("Float Up", "Float up"), ("Dissolve", "Dissolve"), ("Smoke", "Smoke puff"), ("Snap Back", "Snap back"), ("Echo Rings", "Echo rings"));
        SliderRow(release, "Release intensity", nameof(PianoVisualSettings.ReleaseIntensity), 0, 100, "Strength of the release effect.").VisibleWhen = () => _visualSettings.ReleaseEffect != "Fade";

        var smart = Card(NoteSettingsHost, "SMART MODULATORS", "Music data that scales the effects above: they never draw anything themselves.");
        Toggle(smart, "Velocity color", nameof(PianoVisualSettings.VelocityColor), "Soft hits cool blue, hard hits hot red.");
        SliderRow(smart, "Velocity color amount", nameof(PianoVisualSettings.VelocityColorAmount), 0, 100, "How strongly velocity recolors notes and bursts.").VisibleWhen = () => _visualSettings.VelocityColor;
        Toggle(smart, "Octave color", nameof(PianoVisualSettings.OctaveColor), "Each octave owns a slice of the rainbow.");
        SliderRow(smart, "Octave blend", nameof(PianoVisualSettings.OctaveColorBlend), 0, 100, "How strongly the octave hue takes over.").VisibleWhen = () => _visualSettings.OctaveColor;
        Toggle(smart, "Zone split", nameof(PianoVisualSettings.ZoneSplit), "Bass zone erupts fire, treble zone splashes ice.");
        SliderRow(smart, "Split point", nameof(PianoVisualSettings.ZoneSplitPitch), 21, 108, "MIDI note where the treble zone begins (C4 = 60).").VisibleWhen = () => _visualSettings.ZoneSplit;
        SliderRow(smart, "Zone amount", nameof(PianoVisualSettings.ZoneSplitAmount), 0, 100, "Strength of the zone tint.").VisibleWhen = () => _visualSettings.ZoneSplit;
        Toggle(smart, "Pedal glow", nameof(PianoVisualSettings.PedalGlow), "Keys glow brighter while the sustain pedal is down.");
        SliderRow(smart, "Pedal glow intensity", nameof(PianoVisualSettings.PedalGlowIntensity), 0, 100, "How much the pedal brightens the keys.").VisibleWhen = () => _visualSettings.PedalGlow;
        Toggle(smart, "Tempo sync", nameof(PianoVisualSettings.TempoSync), "Glow pulses on every beat of the MIDI tempo map.");
        SliderRow(smart, "Tempo sync amount", nameof(PianoVisualSettings.TempoSyncAmount), 0, 100, "Strength of the beat pulse.").VisibleWhen = () => _visualSettings.TempoSync;
        Toggle(smart, "Audio reactive", nameof(PianoVisualSettings.AudioReactive), "Glow follows the musical energy of note onsets.");
        SliderRow(smart, "Audio reactive amount", nameof(PianoVisualSettings.AudioReactiveAmount), 0, 100, "How strongly onsets pump the glow.").VisibleWhen = () => _visualSettings.AudioReactive;
    }

    private void BuildParticlesPage()
    {
        var sparks = Card(ParticleSettingsHost, "SPARKS · EMITTER", "Burst when a note reaches the keyboard.");
        Toggle(sparks, "Enable sparks", nameof(PianoVisualSettings.ShowEmbers), "Turn the spark burst on or off.");
        SliderRow(sparks, "Amount", nameof(PianoVisualSettings.ParticleAmount), 0, 120, "Particles per impact.");
        SliderRow(sparks, "Velocity", nameof(PianoVisualSettings.ParticleVelocity), 0, 800, "Initial speed.");
        SliderRow(sparks, "Velocity randomness", nameof(PianoVisualSettings.ParticleRandomness), 0, 100, "Variation of the initial speed.");
        SliderRow(sparks, "Spread", nameof(PianoVisualSettings.ParticleSpread), 0, 100, "Angle of the burst cone.");
        SliderRow(sparks, "Note response", nameof(PianoVisualSettings.ParticleResponse), 0, 100, "How strongly the amount follows the note strength.");
        SliderRow(sparks, "Emitter size", nameof(PianoVisualSettings.EmitterSize), 0, 100, "Width of the spawn area on the key.");
        SliderRow(sparks, "Spiral", nameof(PianoVisualSettings.Spiral), 0, 100, "Twists the burst direction over time.");
        SliderRow(sparks, "Speed", nameof(PianoVisualSettings.ParticleSpeed), 0, 300, "Overall speed multiplier.");
        Choice(sparks, "Burst style", nameof(PianoVisualSettings.ImpactBurst), "Look of the particle explosion: embers, water splash, fireworks, confetti or dust.",
            ("Embers", "Embers"), ("Splash", "Splash"), ("Fireworks", "Fireworks"), ("Confetti", "Confetti"), ("Dust", "Dust"));

        var physics = Card(ParticleSettingsHost, "SPARKS · PHYSICS", "Lifetime, size and forces.");
        SliderRow(physics, "Lifetime", nameof(PianoVisualSettings.ParticleLife), .05, 3, "Seconds a spark stays alive.");
        SliderRow(physics, "Lifetime randomness", nameof(PianoVisualSettings.ParticleLifeRandomness), 0, 100, "Variation of the lifetime.");
        SliderRow(physics, "Size", nameof(PianoVisualSettings.ParticleSize), .2, 16, "Radius in pixels.");
        SliderRow(physics, "Size randomness", nameof(PianoVisualSettings.ParticleSizeRandomness), 0, 100, "Variation of the size.");
        SliderRow(physics, "Glow", nameof(PianoVisualSettings.ParticleGlow), 0, 200, "Brightness of every spark.");
        SliderRow(physics, "Gravity", nameof(PianoVisualSettings.Gravity), -600, 1200, "Negative values make sparks float upward.");
        SliderRow(physics, "Drag", nameof(PianoVisualSettings.Drag), 0, 100, "Air resistance.");
        SliderRow(physics, "Vector field", nameof(PianoVisualSettings.VectorField), 0, 1000, "Sideways wind that varies with height.");
        SliderRow(physics, "Field scale", nameof(PianoVisualSettings.FieldScale), 10, 300, "Size of the wind pattern.");
        SliderRow(physics, "Evolution speed", nameof(PianoVisualSettings.EvolutionSpeed), 0, 400, "How fast the wind pattern changes.");
        SliderRow(physics, "Physics time factor", nameof(PianoVisualSettings.PhysicsTimeFactor), 10, 300, "Slow-motion or fast-forward for all particles.");

        var wisps = Card(ParticleSettingsHost, "WISPS", "Plasma streams that rise from every held key.");
        Toggle(wisps, "Enable wisps", nameof(PianoVisualSettings.ShowWisps), "Smoke-like streams above sounding keys.");
        SliderRow(wisps, "Density", nameof(PianoVisualSettings.WispAmount), 0, 150, "Particles per second per key.");
        SliderRow(wisps, "Rise speed", nameof(PianoVisualSettings.WispSpeed), 20, 600, "Upward speed in pixels per second.");
        SliderRow(wisps, "Height", nameof(PianoVisualSettings.WispHeight), 5, 100, "How far the stream reaches before fading.");
        SliderRow(wisps, "Width", nameof(PianoVisualSettings.WispWidth), 0, 100, "Spread of the stream at its base.");
        SliderRow(wisps, "Turbulence", nameof(PianoVisualSettings.WispTurbulence), 0, 100, "Sideways waving of the stream.");
        SliderRow(wisps, "Glow", nameof(PianoVisualSettings.WispGlow), 0, 200, "Brightness of the wisps.");

        var flames = Card(ParticleSettingsHost, "FLAMES", "Fire at the impact point while a key sounds.");
        Toggle(flames, "Enable flames", nameof(PianoVisualSettings.ShowFlame), "Fire bursts while a key sounds.");
        SliderRow(flames, "Flame intensity", nameof(PianoVisualSettings.FlameIntensity), 0, 100, "Brightness and size of the fire.");
        SliderRow(flames, "Flame height", nameof(PianoVisualSettings.FlameHeight), 0, 100, "How tall the flames reach.");
        Choice(flames, "Flame color", nameof(PianoVisualSettings.FlameColorMode), "Classic warm fire or the color of the note.", ("Warm", "Warm fire"), ("Note", "Note color"));
    }

    private void BuildKeyboardPage()
    {
        var look = Card(KeyboardSettingsHost, "KEYBOARD", "Appearance of the 88 keys.");
        Toggle(look, "Show keyboard", nameof(PianoVisualSettings.ShowKeys), "Hide the keys for a pure piano-roll look.");
        Choice(look, "Style", nameof(PianoVisualSettings.KeyboardStyle), "Flat classic keys, a studio grand with depth, or translucent glass keys.", ("Classic", "Classic"), ("Studio", "Studio 3D"), ("Glass", "Glass"));
        SliderRow(look, "Keyboard height", nameof(PianoVisualSettings.KeyboardScale), 60, 140, "Scale of the keyboard area.");
        SliderRow(look, "Black key length", nameof(PianoVisualSettings.KeyOverhang), 0, 100, "How far the black keys reach down.");
        Choice(look, "Key labels", nameof(PianoVisualSettings.KeyLabels), "Note names printed on the white keys.", ("None", "None"), ("C", "Only C keys"), ("All", "All white keys"));
        Toggle(look, "Lid shadow", nameof(PianoVisualSettings.ShowKeyShadow), "Soft shadow falling on the top of the keys.");
        Toggle(look, "Red felt strip", nameof(PianoVisualSettings.ShowKeyFelt), "Colored felt line above the keys like a real grand.");
        ColorRow(look, "Felt color", nameof(PianoVisualSettings.KeyFeltColor), "Color of the felt strip.").VisibleWhen = () => _visualSettings.ShowKeyFelt;

        var light = Card(KeyboardSettingsHost, "KEY LIGHTING", "How pressed keys light up.");
        Toggle(light, "Animate pressed keys", nameof(PianoVisualSettings.AnimateKeys), "Light and press animation for sounding keys.");
        Choice(light, "Pressed key color", nameof(PianoVisualSettings.PressedKeyColorMode), "Follow the note color or use one fixed color.", ("Note", "Follow note color"), ("Fixed", "Fixed color"));
        ColorRow(light, "Fixed color", nameof(PianoVisualSettings.PressedKeyColor), "Color of every pressed key.").VisibleWhen = () => _visualSettings.PressedKeyColorMode == "Fixed";
        SliderRow(light, "Light intensity", nameof(PianoVisualSettings.KeyLighting), 0, 100, "Brightness of the glow around lit keys.");
        SliderRow(light, "Glow radius", nameof(PianoVisualSettings.KeyGlowRadius), 0, 100, "How far the light bleeds over the keyboard.");
        SliderRow(light, "Press depth", nameof(PianoVisualSettings.KeyPressDepth), 0, 100, "How much a key sinks when pressed.");

        var shader = Card(KeyboardSettingsHost, "RAY-TRACED SHADING",
            "Every pixel of the keyboard is shaded with a real light transport model: a GGX specular lobe, a softbox with true penumbra shadows, contact occlusion in the gaps, colored lights from every sounding key and an ACES filmic tonemapper.");
        // the GPU engine always draws lit 3D keys, and it reads these same sliders
        var gpuKeys = () => _visualSettings.RenderBackend == "Gpu";
        var shadingOn = () => _visualSettings.ShadingQuality != "Off" || gpuKeys();
        Choice(shader, "Shading engine", nameof(PianoVisualSettings.ShadingQuality),
            "Off draws the flat vector keys. Fast, Balanced and Cinematic trade bake time for shadow and occlusion samples.",
            ("Off", "Off · flat keys"), ("Fast", "Fast"), ("Balanced", "Balanced"), ("Cinematic", "Cinematic"))
            .VisibleWhen = () => !gpuKeys();
        Note(shader, "The GPU engine is on: it renders the keys in 3D with real-time lights and shadows every frame, and the sliders below shape them. The shading engine picker applies to the software renderer only.").VisibleWhen = gpuKeys;
        SliderRow(shader, "Camera tilt", nameof(PianoVisualSettings.ShaderCameraTilt), 0, 100, "Low camera exaggerates the perspective and lengthens the black key shadows; high camera flattens the bed.").VisibleWhen = shadingOn;
        SliderRow(shader, "Key light", nameof(PianoVisualSettings.ShaderKeyLight), 0, 200, "Intensity of the softbox above the keyboard.").VisibleWhen = shadingOn;
        SliderRow(shader, "Shadow strength", nameof(PianoVisualSettings.ShaderShadows), 0, 100, "How dark the shadows are; also widens the penumbra.").VisibleWhen = shadingOn;
        SliderRow(shader, "Contact occlusion", nameof(PianoVisualSettings.ShaderAmbientOcclusion), 0, 100, "Ambient light lost in the gaps between keys and under the fallboard.").VisibleWhen = shadingOn;
        SliderRow(shader, "Gloss", nameof(PianoVisualSettings.ShaderGloss), 0, 100, "Polish of the ivory and ebony; higher means tighter highlights.").VisibleWhen = shadingOn;
        SliderRow(shader, "Rim light", nameof(PianoVisualSettings.ShaderRimLight), 0, 150, "Accent light rising from behind the fallboard, tinted by the hit-line color.").VisibleWhen = shadingOn;
        SliderRow(shader, "Key emission", nameof(PianoVisualSettings.ShaderEmissive), 0, 200, "How strongly a sounding key glows and lights the bed around it.").VisibleWhen = shadingOn;
        SliderRow(shader, "Exposure", nameof(PianoVisualSettings.ShaderExposure), 20, 250, "Applied before the filmic tonemapper.").VisibleWhen = shadingOn;
        Toggle(shader, "ACES filmic tonemapper", nameof(PianoVisualSettings.ShaderFilmic), "Unreal's default filmic curve; off clips highlights linearly instead.").VisibleWhen = shadingOn;
        Note(shader, "The keyboard is baked once and cached, then only the sounding keys are re-shaded, so the shader stays inside the frame budget. Green-screen recording always uses the flat keys.").VisibleWhen = () => _visualSettings.ShadingQuality != "Off" && !gpuKeys();
    }

    private void BuildBackgroundPage()
    {
        var background = Card(SceneSettingsHost, "BACKGROUND", "What sits behind the notes.");
        Choice(background, "Mode", nameof(PianoVisualSettings.BackgroundMode), "Solid color, your own image, or a pure green stage for chroma keying in OBS.", ("Solid", "Solid color"), ("Image", "Image"), ("ChromaGreen", "Green screen (chroma key)"));
        ColorRow(background, "Background color", nameof(PianoVisualSettings.BackgroundColor), "Base color of the stage.").VisibleWhen = () => _visualSettings.BackgroundMode == "Solid";
        var imageButtons = ButtonRow(background,
            ("CHOOSE IMAGE…", ChooseStageBackground),
            ("CLEAR IMAGE", (_, _) => { _visualSettings.BackgroundImagePath = ""; if (_visualSettings.BackgroundMode == "Image") _visualSettings.BackgroundMode = "Solid"; RefreshSettingControls(); ApplyVisualSettings("Background image removed"); }));
        imageButtons.VisibleWhen = () => _visualSettings.BackgroundMode == "Image";
        SliderRow(background, "Image dim", nameof(PianoVisualSettings.BackgroundDim), 0, 100, "Darkens the image so the notes stay readable.").VisibleWhen = () => _visualSettings.BackgroundMode == "Image";
        Note(background, "Green screen mode disables vignette, beams and decorative layers so the key can be pulled cleanly.").VisibleWhen = () => _visualSettings.BackgroundMode == "ChromaGreen";

        var atmosphere = Card(SceneSettingsHost, "ATMOSPHERE", "Decorative layers drawn behind the notes.");
        Toggle(atmosphere, "Purple aura gradient", nameof(PianoVisualSettings.BackgroundGradient), "Soft radial glow at the top of the stage.");
        Toggle(atmosphere, "Stars", nameof(PianoVisualSettings.ShowStars), "Twinkling star field.");
        SliderRow(atmosphere, "Star density", nameof(PianoVisualSettings.StarDensity), 0, 100, "How many stars are visible.").VisibleWhen = () => _visualSettings.ShowStars;
        Toggle(atmosphere, "Guide lanes", nameof(PianoVisualSettings.BackgroundGuide), "Faint vertical lines for every key.");
        Toggle(atmosphere, "Acoustic motes", nameof(PianoVisualSettings.ShowPetals), "Floating ambient particles drift through the concert space; the colour below tints them.");
        SliderRow(atmosphere, "Mote amount", nameof(PianoVisualSettings.PetalAmount), 0, 150, "Density of floating concert particles in the air.").VisibleWhen = () => _visualSettings.ShowPetals;
        ColorRow(atmosphere, "Mote color", nameof(PianoVisualSettings.PetalColor), "Colour of the floating ambient particles.").VisibleWhen = () => _visualSettings.ShowPetals;
        SliderRow(atmosphere, "Vignette", nameof(PianoVisualSettings.Vignette), 0, 100, "Darkens the corners for a cinematic frame.");
        SliderRow(atmosphere, "Horizon glow", nameof(PianoVisualSettings.HorizonGlow), 0, 100, "Colored glow rising from the keyboard line.");
        SliderRow(atmosphere, "Light beam intensity", nameof(PianoVisualSettings.BeamIntensity), 0, 100, "Brightness of the columns above sounding keys.");

        var ambient = Card(SceneSettingsHost, "AMBIENT LAYERS", "Four independent stage-wide layers behind the notes.");
        Choice(ambient, "Energy layer", nameof(PianoVisualSettings.AmbientEnergy), "Lightning storms, lasers, confetti rain or fireworks.",
            ("None", "None"), ("Lightning Storm", "Lightning storm"), ("Laser Beams", "Laser beams"), ("Confetti Rain", "Confetti rain"), ("Fireworks", "Fireworks"));
        SliderRow(ambient, "Energy amount", nameof(PianoVisualSettings.AmbientEnergyAmount), 0, 100, "How much fills the sky.").VisibleWhen = () => _visualSettings.AmbientEnergy != "None";
        SliderRow(ambient, "Energy speed", nameof(PianoVisualSettings.AmbientEnergySpeed), 0, 100, "How fast it moves.").VisibleWhen = () => _visualSettings.AmbientEnergy != "None";
        Choice(ambient, "Nature layer", nameof(PianoVisualSettings.AmbientNature), "Rain, snow, smoke, leaves, butterflies, dust or aurora.",
            ("None", "None"), ("Rain", "Rain"), ("Snow", "Snow"), ("Smoke", "Smoke"), ("Leaves", "Leaves"), ("Butterflies", "Butterflies"), ("Dust", "Dust"), ("Aurora", "Aurora"));
        SliderRow(ambient, "Nature amount", nameof(PianoVisualSettings.AmbientNatureAmount), 0, 100, "How much fills the air.").VisibleWhen = () => _visualSettings.AmbientNature != "None";
        SliderRow(ambient, "Nature speed", nameof(PianoVisualSettings.AmbientNatureSpeed), 0, 100, "How fast it drifts.").VisibleWhen = () => _visualSettings.AmbientNature != "None";
        Choice(ambient, "Light layer", nameof(PianoVisualSettings.AmbientLight), "Gradient waves, a crystal prism or color splashes.",
            ("None", "None"), ("Gradient Wave", "Gradient wave"), ("Prism", "Prism"), ("Color Splash", "Color splash"));
        SliderRow(ambient, "Light amount", nameof(PianoVisualSettings.AmbientLightAmount), 0, 100, "How strong the light is.").VisibleWhen = () => _visualSettings.AmbientLight != "None";
        SliderRow(ambient, "Light speed", nameof(PianoVisualSettings.AmbientLightSpeed), 0, 100, "How fast it shifts.").VisibleWhen = () => _visualSettings.AmbientLight != "None";
        ColorRow(ambient, "Light tint", nameof(PianoVisualSettings.AmbientLightColor), "Tint of the light layer.").VisibleWhen = () => _visualSettings.AmbientLight != "None";
        Choice(ambient, "Cosmic layer", nameof(PianoVisualSettings.AmbientCosmic), "Galaxy, black hole, matrix rain, geometric shapes or fractals.",
            ("None", "None"), ("Galaxy", "Galaxy"), ("Black Hole", "Black hole"), ("Matrix Rain", "Matrix rain"), ("Geometric", "Geometric shapes"), ("Fractal", "Fractal"));
        SliderRow(ambient, "Cosmic amount", nameof(PianoVisualSettings.AmbientCosmicAmount), 0, 100, "How dense the cosmos is.").VisibleWhen = () => _visualSettings.AmbientCosmic != "None";
        SliderRow(ambient, "Cosmic speed", nameof(PianoVisualSettings.AmbientCosmicSpeed), 0, 100, "How fast it turns.").VisibleWhen = () => _visualSettings.AmbientCosmic != "None";

        var halo = Card(SceneSettingsHost, "HIT LINE", "The line where notes meet the keys.");
        Toggle(halo, "Show halo line", nameof(PianoVisualSettings.ShowHalo), "Glowing line across the stage at key height.");
        ColorRow(halo, "Halo color", nameof(PianoVisualSettings.HaloColor), "Also tints the horizon glow and the keyboard rim light.");
        SliderRow(halo, "Halo intensity", nameof(PianoVisualSettings.HaloIntensity), 0, 200, "Brightness and photon emission of the hit line.");
    }

    private void BuildCameraPage()
    {
        var camera = Card(CameraSettingsHost, "CAMERA", "Framing and subtle motion.");
        SliderRow(camera, "Parallax", nameof(PianoVisualSettings.CameraParallax), 0, 100, "The stage drifts slightly with the mouse.");
        SliderRow(camera, "Zoom", nameof(PianoVisualSettings.CameraZoom), 65, 150, "Scale of the whole stage.");
        SliderRow(camera, "Horizontal framing", nameof(PianoVisualSettings.CameraOffset), 0, 100, "Where the zoomed stage is anchored.");

        var post = Card(CameraSettingsHost, "POST FX", "Color grading applied to notes, particles and lights.");
        SliderRow(post, "Saturation", nameof(PianoVisualSettings.Saturation), 0, 200, "Color intensity.");
        SliderRow(post, "Contrast", nameof(PianoVisualSettings.Contrast), 0, 200, "Difference between bright and dark tones.");
        SliderRow(post, "Bloom intensity", nameof(PianoVisualSettings.BloomIntensity), 0, 150, "Global glow strength.");
        SliderRow(post, "Bloom size", nameof(PianoVisualSettings.BloomSize), 0, 150, "How far the glow spreads.");

        var overlay = Card(CameraSettingsHost, "WEBCAM OVERLAY", "A live camera or a video file drawn over the stage, keyed against pure green. Use a green screen behind you (or the Green Screen preset on the stage) and the background of the picture disappears.");
        Toggle(overlay, "Camera overlay", nameof(PianoVisualSettings.ShowCameraOverlay), "Draw the source below over the stage as a picture-in-picture.");
        ButtonRow(overlay, ("CHOOSE VIDEO…", ChooseCameraVideo_Click), ("USE CAMERA", UseCameraOverlay_Click), ("REFRESH CAMERAS", RefreshCameraOverlay_Click));
        Choice(overlay, "Camera", nameof(PianoVisualSettings.CameraSourceLink), "Which camera the overlay opens; the list is read from the machine when the page opens and when REFRESH is pressed.",
            CameraChoices());
        Choice(overlay, "Corner", nameof(PianoVisualSettings.CameraCorner), "Which corner of the stage the picture sits in.",
            CameraOverlay.Corners.Select(corner => (corner, corner)).ToArray());
        SliderRow(overlay, "Size", nameof(PianoVisualSettings.CameraSize), 15, 60, "Width of the picture as a percentage of the stage width; the height follows the camera's own shape.");
        SliderRow(overlay, "Opacity", nameof(PianoVisualSettings.CameraOpacity), 20, 100, "How solid the picture is over the stage.");
        Toggle(overlay, "Mirror", nameof(PianoVisualSettings.CameraMirror), "Flip the picture, the way a camera pointed at the player should look.");
        SliderRow(overlay, "Key tolerance", nameof(PianoVisualSettings.CameraKeyTolerance), 0, 100, "How much of the picture counts as the green key colour and is made see-through. Zero keeps the whole picture.");
        _cameraStatus = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        Register(overlay, _cameraStatus, null, "camera overlay status green screen chroma key");

        // Hand tracking is its own card: it reads the same camera but paints on the keyboard, and it works with
        // the picture hidden, which is the point of having it separate from the overlay.
        var hands = Card(CameraSettingsHost, "HAND TRACKING", "Follow the hand the camera sees and mark the key it is over, with the fingers it holds up. No model and no download: the shape of a hand is read from the picture, so it wants a plain background and reasonable light.");
        Toggle(hands, "Hand tracking", nameof(PianoVisualSettings.ShowHandTracking), "Mark the key under the hand the camera sees. The camera opens for this even when the overlay picture is off.");
        SliderRow(hands, "Skin sensitivity", nameof(PianoVisualSettings.HandTrackingSensitivity), 0, 100, "How much of the picture counts as skin. A warm light or a dark room asks for more; a background the same colour as a hand asks for less.");
        _handStatus = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        Register(hands, _handStatus, null, "hand tracking status fingers key camera");
        SyncCameraOverlay();
        RefreshCameraOverlayStatus(announce: false);
        RefreshHandStatus();
    }

    /// <summary>Rows of the camera picker built from the machine's own list, refreshed on demand.</summary>
    private (string Value, string Caption)[] CameraChoices()
    {
        var devices = CameraFrameReader.Devices(out _cameraListError);
        _cameraDevices = devices;
        return [("", Loc.T("First camera")), .. devices.Where(device => device.Link.Length > 0).Select(device => (device.Link, device.Name))];
    }

    /// <summary>
    /// Rebuilds the camera picker from the machine's own list. The stored link stays in the settings even when
    /// that camera is currently unplugged, so plugging it back in restores the choice; the overlay opens the
    /// first camera meanwhile and the status line says so.
    /// </summary>
    private void RefreshCameraChoices()
    {
        _visualChoiceOptions[nameof(PianoVisualSettings.CameraSourceLink)] = CameraChoices();
        RefreshChoiceCaptions();
    }

    private void RefreshCameraOverlay_Click(object sender, RoutedEventArgs e)
    {
        RefreshCameraChoices();
        RefreshCameraOverlayStatus(announce: true);
        ApplyVisualSettings("Camera list refreshed");
    }

    private void UseCameraOverlay_Click(object sender, RoutedEventArgs e)
    {
        _visualSettings.CameraVideoPath = "";
        MarkModified(nameof(PianoVisualSettings.CameraVideoPath));
        RefreshCameraOverlayStatus(announce: true);
        ApplyVisualSettings("Camera overlay source: the live camera");
    }

    private void ChooseCameraVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Loc.T("Video files (*.avi;*.mp4;*.wmv;*.mov;*.mkv)|*.avi;*.mp4;*.wmv;*.mov;*.mkv|All files (*.*)|*.*"),
            Title = Loc.T("Choose a video to overlay"),
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;
        _visualSettings.CameraVideoPath = dialog.FileName;
        MarkModified(nameof(PianoVisualSettings.CameraVideoPath));
        RefreshCameraOverlayStatus(announce: true);
        ApplyVisualSettings("Camera overlay source: {0}", false, Path.GetFileName(dialog.FileName));
    }

    /// <summary>
    /// The line under the camera rows: what the overlay will open and why it cannot, read from the same
    /// enumeration and the same reader the pump uses, so it can never claim something the stage does not do.
    /// </summary>
    private void RefreshCameraOverlayStatus(bool announce)
    {
        if (_cameraStatus is null) return;
        var live = string.IsNullOrWhiteSpace(_visualSettings.CameraVideoPath);
        var text = !_visualSettings.ShowCameraOverlay
            ? Loc.T("The overlay is off. Switch Camera overlay on to put the picture over the stage.")
            // What the reader is really doing wins over what the settings say it should do.
            : CameraStatus.Length > 0
                ? Stage.HasCameraFrame ? CameraStatus : Loc.F("{0} Waiting for the first frame.", CameraStatus)
                : !CameraFrameReader.Available
                    ? Loc.F("The camera cannot run here: {0}", CameraFrameReader.StartupError ?? Loc.T("the media stack is unavailable"))
                    : !live
                        ? Loc.F("Overlaying the video file {0}, looping it.", Path.GetFileName(_visualSettings.CameraVideoPath))
                        : _cameraListError is not null
                            ? Loc.F("The camera list could not be read: {0}", _cameraListError)
                            : _cameraDevices.Count == 0
                                ? Loc.T("No camera was found on this machine. Choose a video file to overlay instead.")
                                : Loc.F("{0} camera(s) found. The overlay opens the one picked above.", _cameraDevices.Count);
        _cameraStatus.Text = text;
        if (announce) Loc.Set(SettingsSaveLabel, text);
    }

    /// <summary>
    /// The line under the hand rows: what the tracker is seeing in the newest frame — the key, the fingers, how
    /// much of the picture is hand — or why there is nothing to show. It reads the window's own status, so the
    /// dock and the stage can never disagree about the same frame.
    /// </summary>
    private void RefreshHandStatus()
    {
        if (_handStatus is null) return;
        var text = !_visualSettings.ShowHandTracking
            ? Loc.T("Hand tracking is off. Switch it on to follow the hand the camera sees and mark its key.")
            : CameraStatus.Length == 0
                ? Loc.T("The camera is not running, so there is nothing to follow.")
                : HandStatus.Length > 0 ? HandStatus : Loc.T("Hand: waiting for the first frame.");
        if (_handStatus.Text == text) return;
        _handStatus.Text = text;
    }

    private void BuildRecordingPage()
    {
        var output = Card(RecordingSettingsHost, "OUTPUT", "Applied when the next recording starts.");
        Choice(output, "Format", nameof(PianoVisualSettings.RecordingFormat), "What REC writes: one AVI video file, a folder of 32-bit PNG frames whose alpha channel lets you layer the piano over your own footage, or an MP4 with the take's own audio written inside it as it records.",
            (RecordingFormatIds.Avi, "AVI video"), (RecordingFormatIds.PngSequence, "PNG sequence (32-bit alpha)"), (RecordingFormatIds.Mp4, "MP4 (H.264 + AAC)"));
        Choice(output, "Resolution", nameof(PianoVisualSettings.RecordingResolution), "Match the window, or render to a fixed 16:9 size.", ("Window", "Match window"), ("720p", "1280 × 720"), ("1080p", "1920 × 1080"));
        SliderRow(output, "Frame rate", nameof(PianoVisualSettings.RecordingFrameRate), 15, 60, "Frames per second of the recording.");
        Toggle(output, "Record audio", nameof(PianoVisualSettings.RecordAudio),
            "Write the piano's own audio — the exact blocks the engine renders. An AVI or a PNG sequence gets a 16-bit stereo WAV beside it to mux with the ffmpeg line below the button; an MP4 takes the same samples straight into the file as AAC. A machine with no SoundFont records video only.");
        Toggle(output, "Transparent background", nameof(PianoVisualSettings.RecordingTransparent),
            "PNG sequence only: skip the fills that would hide the alpha channel — the background colour, the image, the gradient and the vignette. Every layer your look enables is still drawn, so turn the Background layer off for a piano-only export.").VisibleWhen = () => _visualSettings.RecordingFormat == RecordingFormatIds.PngSequence;
    }

    /// <summary>
    /// The General page: preferences that belong to the application rather than to the look of the
    /// stage. The language picker is the first entry; anything app-wide (startup behaviour, update
    /// channel, notation style) belongs here too.
    /// </summary>
    private void BuildGeneralPage()
    {
        var language = Card(GeneralSettingsHost, "LANGUAGE", "The language of every menu, dock page, dialog and status line. A language applies immediately — playback, the open MIDI file and your settings are untouched.");
        _languageChipHost = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        Register(language, _languageChipHost, null, "language interface english vietnamese tiếng việt");
        var blurb = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 2, 0, 8) };
        _languageBlurb = blurb;
        Register(language, blurb, null, "language follows windows");
        RefreshLanguageChips();
        Note(language, "Keyflow stores the language id, not the translated text: settings files, presets, theme ids and MIDI files all keep the same English identifiers, so a file written in one language opens unchanged in another.");

        var graphics = Card(GeneralSettingsHost, "GRAPHICS ENGINE", "Software draws the stage with WPF on the interface thread. GPU renders it with Direct3D 11 on a thread of its own: HDR bloom, lit 3D keys with shadows, tens of thousands of particles and up to 240 frames per second, without ever holding up MIDI input.");
        Choice(graphics, "Renderer", nameof(PianoVisualSettings.RenderBackend), "Which engine draws the stage. If Direct3D 11 cannot start, Keyflow stays on the software engine and says why.",
            ("Software", "Software (WPF)"), ("Gpu", "GPU (Direct3D 11)"));
        Choice(graphics, "GPU frame rate", nameof(PianoVisualSettings.GpuFrameRate), "Frames per second the GPU render thread aims for. Match your display (60, 120, 144 or 240 Hz); Unlimited renders as fast as the graphics card allows.",
            ("60", "60 FPS"), ("120", "120 FPS"), ("144", "144 FPS"), ("240", "240 FPS"), ("Unlimited", "Unlimited"));
        Toggle(graphics, "VSync in the GPU stage window", nameof(PianoVisualSettings.GpuVSync), "Present on the display's refresh. Turn it off for the lowest latency; the picture may tear.");
        ButtonRow(graphics, ("OPEN GPU STAGE WINDOW", OpenGpuStage_Click));
        Note(graphics, "The GPU stage window runs at the full frame rate on any monitor and suits a projector or OBS window capture. F11 toggles full screen, Esc leaves it. Graphics settings belong to this computer: applying a preset keeps them.");
        _gpuStatusLabel = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 2, 0, 4), TextWrapping = TextWrapping.Wrap };
        Register(graphics, _gpuStatusLabel, null, "gpu direct3d graphics engine renderer status adapter");
        RefreshGpuStatus();

        var profile = Card(GeneralSettingsHost, "SETTINGS PROFILE", "One file with the whole setup: the stage settings, the interface language and the face of the shell. Keep it beside your presets, hand it to another machine, or drop it onto the window.");
        ButtonRow(profile, ("EXPORT PROFILE…", ExportProfile_Click), ("IMPORT PROFILE…", ImportProfile_Click));
        Note(profile, "A profile is plain JSON: dropping one on the window applies it, a dropped MIDI file opens the song and a dropped image becomes the stage background.");
    }

    /// <summary>
    /// One language chip: the "follow Windows" row is a table key and translates, while a real language
    /// prints its own name the way its own speakers write it (<c>Tiếng Việt · English</c>) in every
    /// interface language — that text is composed, not translated, so it is bound rather than looked up.
    /// </summary>
    private Button LanguageChip(string id, string caption)
    {
        var chip = new Button { Tag = id, DataContext = id, Style = (Style)FindResource("ThemeChipStyle"), Opacity = IsActiveLanguage(id) ? 1 : .72 };
        if (id.Length == 0) Loc.Set(chip, caption); else Loc.Bind(chip, () => caption);
        chip.Click += LanguageChip_Click;
        return chip;
    }

    /// <summary>Rebuilds the language chips of the General page and the startup menu.</summary>
    private void RefreshLanguageChips()
    {
        if (_languageChipHost is not null)
        {
            _languageChipHost.Children.Clear();
            foreach (var (id, caption) in LanguageChoices()) _languageChipHost.Children.Add(LanguageChip(id, caption));
        }
        if (_languageBlurb is not null)
            Loc.Bind(_languageBlurb, () => Loc.F("Active language: {0} · selected from {1}", Loc.Current.Display, Loc.StoredId.Length == 0 ? Loc.T("the Windows display language") : Loc.T("this settings page")));
        RefreshMenuLanguageChips();
    }

    /// <summary>
    /// The picker entries: "follow Windows" first, then every bundled language by its native name.
    /// The id of the automatic entry is empty, which is exactly what the settings file stores.
    /// </summary>
    private static IEnumerable<(string Id, string Caption)> LanguageChoices()
    {
        yield return ("", "Follow Windows");
        foreach (var language in Languages.All) yield return (language.Id, language.Display);
    }

    private static bool IsActiveLanguage(string id) =>
        id.Length == 0 ? Loc.StoredId.Length == 0 : string.Equals(id, Loc.Current.Id, StringComparison.OrdinalIgnoreCase);

    private void LanguageChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: string id }) return;
        if (IsActiveLanguage(id)) { ChromeMotion.Pulse((UIElement)sender); return; }
        ChromeMotion.Pulse((UIElement)sender);
        ApplyLanguage(id);
    }

    // =====================================================================================================
    // Row builders
    // =====================================================================================================

    private Panel Card(Panel page, string title, string subtitle)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 10, 0, 0), Background = (Brush)FindResource("ControlBrush"), BorderBrush = (Brush)FindResource("ControlBorderBrush"), BorderThickness = new Thickness(1) };
        var body = new StackPanel();
        var heading = new TextBlock { Style = (Style)FindResource("EyebrowTextStyle") };
        var caption = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 2, 0, 6) };
        Loc.Set(heading, title); Loc.Set(caption, subtitle);
        body.Children.Add(heading);
        body.Children.Add(caption);
        card.Child = body; page.Children.Add(card); _settingCards.Add(card); _cardsByTitle[title] = card;
        return body;
    }

    private readonly Dictionary<string, Border> _cardsByTitle = [];

    /// <summary>
    /// Opens the page that holds the card titled <paramref name="title"/> (its English key), scrolls the
    /// card into view and pulses it so the eye lands on it.
    /// </summary>
    private void JumpToCard(string title)
    {
        if (!_cardsByTitle.TryGetValue(title, out var card) || card.Parent is not Panel page) return;
        for (var i = 0; i < SettingsPages.Order.Length; i++)
            if (ReferenceEquals(SettingsPageHost(i), page)) { SettingsTabs.SelectedIndex = i; break; }
        Dispatcher.BeginInvoke(new Action(() => { card.BringIntoView(); ChromeMotion.Pulse(card); }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A layer switch with a › button that opens the card holding the layer's detailed controls.</summary>
    private SettingRow LayerToggle(Panel body, string label, string property, string tooltip, string detailCard)
    {
        var row = Toggle(body, label, property, tooltip);
        var check = row.Element;
        var index = body.Children.IndexOf(check);
        body.Children.RemoveAt(index);
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var jump = new Button { Content = "›", Style = (Style)FindResource("MiniButtonStyle"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Loc.Set(jump, "Open the detailed controls for this layer", FrameworkElement.ToolTipProperty);
        Loc.Set(jump, "Open the detailed controls for this layer", AutomationProperties.NameProperty);
        jump.Click += (_, _) => JumpToCard(detailCard);
        Grid.SetColumn(jump, 1);
        line.Children.Add(check); line.Children.Add(jump);
        body.Children.Insert(index, line);
        row.Element = line;
        return row;
    }

    private SettingRow Register(Panel body, FrameworkElement element, string? property = null, params string[] searchKeys)
    {
        body.Children.Add(element);
        var card = (Border)((FrameworkElement)body).Parent;
        var page = (Panel)card.Parent;
        var row = new SettingRow { Element = element, SearchKeys = searchKeys, Page = page, Card = card, Property = property };
        _settingRows.Add(row);
        return row;
    }

    /// <summary>One line of the generated catalogue: a switch, a slider, a picker or a colour.</summary>
    /// <remarks>
    /// Label and tooltip are English source strings handed to <see cref="Loc"/>, which prints the
    /// active language now and repaints the element after a switch — the dock is never rebuilt for a
    /// language change, so scroll position, open controls and typed values all survive.
    /// </remarks>
    private SettingRow Toggle(Panel body, string label, string property, string tooltip)
    {
        var check = new CheckBox { Tag = property, IsChecked = (bool)Prop(property).GetValue(_visualSettings)!, Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
        Loc.Set(check, label, ContentControl.ContentProperty);
        Loc.Set(check, tooltip, FrameworkElement.ToolTipProperty);
        Loc.Set(check, label, AutomationProperties.NameProperty);
        check.Checked += VisualToggle_Changed; check.Unchecked += VisualToggle_Changed;
        _visualToggles[property] = check; _visualToggleList.Add(check);
        return Register(body, check, property, label, tooltip).WithCaption(check, ContentControl.ContentProperty, label);
    }

    private SettingRow SliderRow(Panel body, string label, string property, double minimum, double maximum, string tooltip)
    {
        var prop = Prop(property);
        var current = (double)prop.GetValue(_visualSettings)!;
        var row = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        Loc.Set(row, tooltip, FrameworkElement.ToolTipProperty);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Style = (Style)FindResource("LabelTextStyle") };
        Loc.Set(text, label);
        var box = new TextBox { Text = FormatSetting(property, current), Tag = property, Width = 74, Height = 24, Padding = new Thickness(6, 2, 6, 2), FontSize = 10.5, TextAlignment = TextAlignment.Right };
        Loc.Set(box, "Type a value and press Enter", FrameworkElement.ToolTipProperty);
        Loc.Set(box, label, AutomationProperties.NameProperty);
        box.LostFocus += VisualValueBox_Commit; box.KeyDown += VisualValueBox_KeyDown;
        var reset = new Button { Content = "↺", Tag = property, Style = (Style)FindResource("MiniButtonStyle"), Margin = new Thickness(4, 0, 0, 0) };
        var resetValue = FormatSetting(property, (double)prop.GetValue(DefaultVisualSettings)!);
        Loc.Format(reset, "Reset to {0}", resetValue);
        reset.Click += VisualReset_Click;
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(current, minimum, maximum), Tag = property, Margin = new Thickness(0, 2, 0, 0) };
        Loc.Set(slider, label, AutomationProperties.NameProperty);
        slider.ValueChanged += VisualSlider_ValueChanged;
        Grid.SetColumn(box, 1); Grid.SetColumn(reset, 2); Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 3);
        row.Children.Add(text); row.Children.Add(box); row.Children.Add(reset); row.Children.Add(slider);
        _visualSliders[property] = slider; _visualValueBoxes[property] = box; _sliderRanges[property] = (minimum, maximum);
        return Register(body, row, property, label, tooltip).WithCaption(text, TextBlock.TextProperty, label);
    }

    private SettingRow Choice(Panel body, string label, string property, string tooltip, params (string Value, string Caption)[] options)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 6) };
        Loc.Set(row, tooltip, FrameworkElement.ToolTipProperty);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Style = (Style)FindResource("LabelTextStyle") };
        Loc.Set(text, label);
        var combo = new ComboBox { Tag = property, Width = 210, Height = 30, DisplayMemberPath = "Caption", SelectedValuePath = "Value" };
        Loc.Set(combo, label, AutomationProperties.NameProperty);
        // The caption is display only; the stored value stays the English identifier, so a saved
        // preset keeps working after the interface language changes.
        combo.ItemsSource = options.Select(o => new ChoiceOption(o.Value, Loc.T(o.Caption))).ToList();
        combo.SelectedValue = (string)Prop(property).GetValue(_visualSettings)!;
        combo.SelectionChanged += VisualChoice_Changed;
        Grid.SetColumn(combo, 1); row.Children.Add(text); row.Children.Add(combo);
        _visualChoices[property] = combo; _visualChoiceOptions[property] = options;
        return Register(body, row, property, [label, tooltip, .. options.Select(o => o.Caption)]).WithCaption(text, TextBlock.TextProperty, label);
    }

    /// <summary>
    /// Repaints every picker after a language switch or a changed list (the camera picker), keeping the
    /// selected value. The English captions come from the recorded options, never from the painted items.
    /// </summary>
    private void RefreshChoiceCaptions()
    {
        foreach (var (property, combo) in _visualChoices)
        {
            // The declared type keeps the tuple names on both branches of the choice below.
            (string Value, string Caption)[]? options = _visualChoiceOptions.TryGetValue(property, out var recorded)
                ? recorded
                : combo.ItemsSource is IEnumerable<ChoiceOption> painted
                    ? painted.Select(option => (option.Value, option.Caption)).ToArray()
                    : null;
            if (options is null) continue;
            var selected = combo.SelectedValue;
            var loading = _loadingVisualSettings;
            _loadingVisualSettings = true;
            try { combo.ItemsSource = options.Select(o => new ChoiceOption(o.Value, Loc.T(o.Caption))).ToList(); combo.SelectedValue = selected; }
            finally { _loadingVisualSettings = loading; }
        }
    }

    private SettingRow ColorRow(Panel body, string label, string property, string tooltip)
    {
        var current = (string)Prop(property).GetValue(_visualSettings)!;
        var row = new Grid { Margin = new Thickness(0, 5, 0, 6) };
        Loc.Set(row, tooltip, FrameworkElement.ToolTipProperty);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Style = (Style)FindResource("LabelTextStyle") };
        Loc.Set(text, label);
        var swatch = new Button { Tag = property, Width = 30, Height = 26, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) };
        Loc.Set(swatch, "Open color picker", FrameworkElement.ToolTipProperty);
        Loc.Set(swatch, label, AutomationProperties.NameProperty);
        swatch.Click += VisualColorButton_Click; SetColorSwatch(swatch, current);
        var box = new TextBox { Text = current, Tag = property, Width = 92, Height = 26, Padding = new Thickness(6, 2, 6, 2), FontSize = 10.5, CharacterCasing = CharacterCasing.Upper, MaxLength = 9 };
        Loc.Set(box, label, AutomationProperties.NameProperty);
        box.LostFocus += VisualColor_LostFocus; box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { VisualColor_LostFocus(s, e); e.Handled = true; } };
        Grid.SetColumn(swatch, 1); Grid.SetColumn(box, 2);
        row.Children.Add(text); row.Children.Add(swatch); row.Children.Add(box);
        _visualColorInputs[property] = box; _visualColorButtons[property] = swatch;
        return Register(body, row, property, label, tooltip, "color").WithCaption(text, TextBlock.TextProperty, label);
    }

    private SettingRow TrackPaletteRow(Panel body)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        var caption = new TextBlock { Style = (Style)FindResource("LabelTextStyle"), Margin = new Thickness(0, 0, 0, 6) };
        Loc.Set(caption, "Track colors (tracks 9+ repeat the palette)");
        stack.Children.Add(caption);
        var wrap = new WrapPanel();
        _trackPaletteSwatches.Clear();
        for (var i = 0; i < 8; i++)
        {
            var swatch = new Button { Tag = i, Width = 40, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6), Content = (i + 1).ToString(), FontSize = 10, Foreground = Brushes.White };
            // The number stays the visible content; the sentence is the tooltip and the screen-reader
            // name (the older two-argument Format replaced the digit with the whole sentence).
            Loc.Format(swatch, "Color of track {0}", FrameworkElement.ToolTipProperty, i + 1);
            Loc.Format(swatch, "Color of track {0}", AutomationProperties.NameProperty, i + 1);
            swatch.Click += TrackPaletteButton_Click; SetColorSwatch(swatch, _visualSettings.TrackColors[i]);
            wrap.Children.Add(swatch); _trackPaletteSwatches.Add(swatch);
        }
        stack.Children.Add(wrap);
        return Register(body, stack, nameof(PianoVisualSettings.TrackColors), "track colors palette per track");
    }

    private SettingRow ButtonRow(Panel body, params (string Text, RoutedEventHandler Click)[] buttons)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        foreach (var (text, click) in buttons)
        {
            var button = new Button { Margin = new Thickness(0, 0, 8, 4) };
            Loc.Set(button, text);
            button.Click += click; wrap.Children.Add(button);
        }
        return Register(body, wrap, null, [.. buttons.Select(b => b.Text)]);
    }

    private SettingRow Note(Panel body, string text)
    {
        var label = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 4, 0, 4) };
        Loc.Set(label, text);
        return Register(body, label, null, text);
    }

    private sealed record ChoiceOption(string Value, string Caption);
    private static System.Reflection.PropertyInfo Prop(string property) => typeof(PianoVisualSettings).GetProperty(property) ?? throw new InvalidOperationException($"Unknown visual setting '{property}'.");

    /// <summary>Warning or notice dialog with a localized body and title (dialogs are never snapshotted two ways).</summary>
    private void ShowMessage(string message, string titleKey, MessageBoxImage icon = MessageBoxImage.Warning)
    {
        if (SuppressErrorDialogs || _closing) return;
        MessageBox.Show(this, message, Loc.T(titleKey), MessageBoxButton.OK, icon);
    }

    /// <summary>Yes/no confirmation whose texts follow the interface language.</summary>
    private bool Confirm(string message, string titleKey)
    {
        if (SuppressErrorDialogs || _closing) return false;
        return MessageBox.Show(this, message, Loc.T(titleKey), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    internal static string FormatSetting(string property, double value) => property switch
    {
        nameof(PianoVisualSettings.ParticleLife) => $"{value:0.00} s",
        nameof(PianoVisualSettings.ParticleSize) => $"{value:0.0} px",
        nameof(PianoVisualSettings.NoteMinLength) or nameof(PianoVisualSettings.NoteGap) => $"{value:0} px",
        nameof(PianoVisualSettings.NoteFallSpeed) or nameof(PianoVisualSettings.WispSpeed) => $"{value:0} px/s",
        nameof(PianoVisualSettings.ParticleAmount) or nameof(PianoVisualSettings.ParticleVelocity) or nameof(PianoVisualSettings.Gravity) or nameof(PianoVisualSettings.VectorField) or nameof(PianoVisualSettings.WispAmount) => $"{value:0}",
        nameof(PianoVisualSettings.HandSplitPitch) => $"{NoteLabel((int)Math.Round(value))} ({value:0})",
        nameof(PianoVisualSettings.RecordingFrameRate) => $"{value:0} fps",
        _ => $"{value:0}%"
    };

    /// <summary>Parses a typed value: plain numbers with optional units, or note names for the hand split.</summary>
    internal static bool TryParseSettingValue(string property, string text, out double value)
    {
        value = 0;
        if (property == nameof(PianoVisualSettings.HandSplitPitch) && NoteNamePattern.Match(text) is { Success: true } note)
        {
            var letter = char.ToUpperInvariant(note.Groups[1].Value[0]); var accidental = note.Groups[2].Value; var octave = int.Parse(note.Groups[3].Value, CultureInfo.InvariantCulture);
            var semitone = letter switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11 };
            if (accidental is "#" or "♯") semitone++; else if (accidental is "b" or "B") semitone--;
            value = (octave + 1) * 12 + semitone; return true;
        }
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c is '-' or '.' or ',').ToArray()).Replace(',', '.');
        if (cleaned.Length == 0 || !double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = parsed; return true;
    }

    private static void SetColorSwatch(Button button, string value)
    {
        try { button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!); }
        catch { button.Background = new SolidColorBrush(Color.FromRgb(198, 110, 255)); }
    }

    // =====================================================================================================
    // Change handlers
    // =====================================================================================================

    private void VisualToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingVisualSettings || sender is not CheckBox check || check.Tag is not string property) return;
        var value = check.IsChecked == true;
        Prop(property).SetValue(_visualSettings, value);
        _loadingVisualSettings = true;
        try
        {
            foreach (var other in _visualToggleList) if (!ReferenceEquals(other, check) && Equals(other.Tag, property)) other.IsChecked = value;
            SyncPlayDialogToggle(property, value);
        }
        finally { _loadingVisualSettings = false; }
        MarkModified(property); RefreshDependentRows();
        // The recording rows describe what the next take will be, so they are written again when one changes.
        if (property is nameof(PianoVisualSettings.RecordAudio) or nameof(PianoVisualSettings.RecordingFormat)
            or nameof(PianoVisualSettings.RecordingResolution) or nameof(PianoVisualSettings.RecordingFrameRate)
            or nameof(PianoVisualSettings.RecordingTransparent)) UpdateRecordingInfo();
        var label = check.Content as string ?? check.Tag as string ?? Loc.T("Setting");
        ApplyVisualSettings(check.IsChecked == true ? "{0} on" : "{0} off", false, label);
    }

    private void VisualSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings || sender is not Slider slider || slider.Tag is not string property) return;
        Prop(property).SetValue(_visualSettings, slider.Value);
        if (_visualValueBoxes.TryGetValue(property, out var box) && !box.IsKeyboardFocused) box.Text = FormatSetting(property, slider.Value);
        MarkModified(property);
        if (property is nameof(PianoVisualSettings.RecordingFrameRate)) UpdateRecordingInfo();
        if (property is nameof(PianoVisualSettings.RecordingFormat)) UpdateRecordingInfo();
        if (property is nameof(PianoVisualSettings.HandSplitPitch) && ModeCombo.SelectedIndex is 2 or 3) { ApplyTrackFilter(); UpdateSongUi(); }
        if (property is nameof(PianoVisualSettings.NoteFallSpeed) && PlaySpeedSlider is not null)
        {
            PlaySpeedSlider.Value = Math.Clamp(_visualSettings.NoteFallSpeed, PlaySpeedSlider.Minimum, PlaySpeedSlider.Maximum);
            if (PlaySpeedLabel is not null) PlaySpeedLabel.Text = ((int)_visualSettings.NoteFallSpeed).ToString();
        }
        ApplyVisualSettings("Visual changes apply live");
    }

    private void VisualValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        VisualValueBox_Commit(box, e); e.Handled = true;
        Keyboard.ClearFocus(); Stage.Focus();
    }

    private void VisualValueBox_Commit(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string property || !_visualSliders.TryGetValue(property, out var slider)) return;
        if (TryParseSettingValue(property, box.Text, out var value))
        {
            var (min, max) = _sliderRanges[property];
            slider.Value = Math.Clamp(value, min, max);
        }
        box.Text = FormatSetting(property, slider.Value);
    }

    private void VisualReset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string property } || !_visualSliders.TryGetValue(property, out var slider)) return;
        slider.Value = (double)Prop(property).GetValue(DefaultVisualSettings)!;
    }

    private void VisualChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingVisualSettings || sender is not ComboBox { Tag: string property, SelectedValue: string value }) return;
        Prop(property).SetValue(_visualSettings, value);
        if (property == nameof(PianoVisualSettings.BackgroundMode) && value == "Image" && string.IsNullOrWhiteSpace(_visualSettings.BackgroundImagePath)) ChooseStageBackground(sender, e);
        MarkModified(property); RefreshDependentRows(); RebuildTrackList();
        if (property is nameof(PianoVisualSettings.RecordingResolution)) UpdateRecordingInfo();
        if (property is nameof(PianoVisualSettings.RecordingTransparent)) UpdateRecordingInfo();
        var what = property switch
        {
            nameof(PianoVisualSettings.NoteStyle) => "Note style updated",
            nameof(PianoVisualSettings.NoteDirection) => "Note direction updated",
            nameof(PianoVisualSettings.ColorMode) => "Color mode updated",
            nameof(PianoVisualSettings.KeyboardStyle) => "Keyboard style updated",
            nameof(PianoVisualSettings.BackgroundMode) => "Background mode updated",
            _ => "Setting updated"
        };
        ApplyVisualSettings(what);
    }

    private void VisualColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string property }) return;
        var input = _visualColorInputs[property];
        var picker = new ColorPickerWindow(input.Text) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } selected) return;
        input.Text = selected;
        CommitColor(property, selected);
    }

    private void VisualColor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string property) return;
        try
        {
            _ = ColorConverter.ConvertFromString(box.Text) ?? throw new FormatException();
            CommitColor(property, box.Text);
        }
        catch { box.Text = (string)Prop(property).GetValue(_visualSettings)!; }
    }

    private void CommitColor(string property, string value)
    {
        Prop(property).SetValue(_visualSettings, value);
        if (_visualColorButtons.TryGetValue(property, out var swatch)) SetColorSwatch(swatch, value);
        if (property == nameof(PianoVisualSettings.HaloColor) && PlayDialogHaloColorDot is not null)
            PlayDialogHaloColorDot.Background = new SolidColorBrush(SafeColor(value));
        if (property is nameof(PianoVisualSettings.NoteColorStart) or nameof(PianoVisualSettings.NoteColorEnd))
        {
            _visualSettings.Palette = "Custom";
            if (_visualChoices.TryGetValue(nameof(PianoVisualSettings.Palette), out var palette)) { _loadingVisualSettings = true; palette.SelectedValue = "Custom"; _loadingVisualSettings = false; }
        }
        MarkModified(property);
        ApplyVisualSettings(property == nameof(PianoVisualSettings.HaloColor) ? "Halo color applied" : "Color applied");
    }

    private void TrackPaletteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        var picker = new ColorPickerWindow(_visualSettings.TrackColors[index]) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } selected) return;
        _visualSettings.TrackColors[index] = selected;
        SetColorSwatch(_trackPaletteSwatches[index], selected);
        RebuildTrackList(); MarkModified(nameof(PianoVisualSettings.TrackColors));
        ApplyVisualSettings("Track {0} color applied", false, index + 1);
    }

    private void MarkModified(string? property = null)
    {
        if (_loadingVisualSettings) return;
        _visualSettings.PresetModified = true;
        UpdatePresetLabels();
        RecordHistory(property);
    }

    private void RefreshDependentRows()
    {
        var text = SettingsSearchBox?.Text.Trim() ?? "";
        var query = text.ToLowerInvariant();
        foreach (var row in _settingRows)
        {
            var visible = (row.VisibleWhen?.Invoke() ?? true) && (query.Length == 0 || MatchesSearch(row, query));
            row.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            // Repaint the caption: the word the query matched is shown in the accent colour, and the
            // plain localized label comes back when the search box is emptied. Loc.T inside SetCaption
            // keeps the caption in step with the interface language.
            if (row.Caption is { } caption) SetCaption(caption, visible && text.Length > 0 ? FirstTokenIn(Loc.T(caption.Key), text) : null);
        }
        foreach (var card in _settingCards)
        {
            var anyVisible = _settingRows.Any(r => ReferenceEquals(r.Card, card) && r.Element.Visibility == Visibility.Visible);
            card.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SettingsSearchHint is not null) SettingsSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A row matches when every word of the query is found in one of the phrases it answers to — its
    /// search keys (in English or in the active translation), the synonyms of its setting and the name
    /// of the setting itself. Words may be typed in any order and with either language's vocabulary:
    /// "speed fall" and "tốc độ rơi" both find the same slider.
    /// </summary>
    private static bool MatchesSearch(SettingRow row, string query)
    {
        var tokens = SplitQuery(query);
        if (tokens.Length == 0) return true;
        var phrases = SearchPhrases(row).ToList();
        foreach (var token in tokens)
        {
            var found = false;
            foreach (var phrase in phrases)
            {
                if (phrase.Contains(token, StringComparison.OrdinalIgnoreCase)
                    || Loc.Known(phrase) && Loc.T(phrase).Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>The first query word that actually appears in the caption, or null when the row matched on a synonym or on its setting name.</summary>
    private static string? FirstTokenIn(string caption, string query)
    {
        foreach (var token in SplitQuery(query))
            if (token.Length > 0 && caption.Contains(token, StringComparison.CurrentCultureIgnoreCase)) return token;
        return null;
    }

    /// <summary>
    /// Repaints one row caption: the plain localized string, or the same string with the matched part in
    /// the accent colour. A language switch repaints the plain label through <see cref="Loc"/>, and the
    /// next keystroke in the search box brings the highlight straight back.
    /// </summary>
    private void SetCaption((DependencyObject Target, DependencyProperty Property, string Key) caption, string? match)
    {
        var text = Loc.T(caption.Key);
        var index = match is null ? -1 : text.IndexOf(match, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
        {
            // The plain label. A TextBlock is repainted through its inline collection so that a label
            // which *was* highlighted always comes back plain (writing the same Text value again would
            // not raise a change notification and the highlighted runs would stay on screen).
            if (caption.Target is TextBlock plain) { plain.Inlines.Clear(); plain.Inlines.Add(new Run(text)); }
            else caption.Target.SetValue(caption.Property, text);
            return;
        }
        var accent = (Brush)FindResource("Accent2Brush");
        var piece = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (index > 0) piece.Inlines.Add(new Run(text[..index]));
        piece.Inlines.Add(new Run(text.Substring(index, match!.Length)) { Foreground = accent, FontWeight = FontWeights.SemiBold });
        if (index + match.Length < text.Length) piece.Inlines.Add(new Run(text[(index + match.Length)..]));
        if (caption.Target is TextBlock block) { block.Inlines.Clear(); foreach (var inline in piece.Inlines.ToList()) block.Inlines.Add(inline); }
        else caption.Target.SetValue(caption.Property, piece);
    }

    private static string[] SplitQuery(string query) => query.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Everything a row answers to: its search keys, the synonyms of its setting and its property name.</summary>
    private static IEnumerable<string> SearchPhrases(SettingRow row)
    {
        foreach (var key in row.SearchKeys) yield return key;
        if (row.Property is not { } property) yield break;
        if (SearchSynonyms.TryGetValue(property, out var synonyms)) yield return synonyms;
        // The property name itself is the last safety net: "NoteFallSpeed" answers to "fall" and "speed"
        // even before anybody writes a synonym for a newly added row.
        yield return property;
    }

    /// <summary>Pushes every value of <see cref="_visualSettings"/> back into the generated controls (after presets, resets or imports).</summary>
    private void RefreshSettingControls()
    {
        _loadingVisualSettings = true;
        try
        {
            foreach (var (property, slider) in _visualSliders)
            {
                var value = (double)Prop(property).GetValue(_visualSettings)!;
                slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
                if (_visualValueBoxes.TryGetValue(property, out var box)) box.Text = FormatSetting(property, value);
            }
            foreach (var check in _visualToggleList) if (check.Tag is string toggleProperty) check.IsChecked = (bool)Prop(toggleProperty).GetValue(_visualSettings)!;
            foreach (var (property, combo) in _visualChoices) combo.SelectedValue = (string)Prop(property).GetValue(_visualSettings)!;
            foreach (var (property, box) in _visualColorInputs)
            {
                var value = (string)Prop(property).GetValue(_visualSettings)!;
                box.Text = value; if (_visualColorButtons.TryGetValue(property, out var swatch)) SetColorSwatch(swatch, value);
            }
            for (var i = 0; i < _trackPaletteSwatches.Count && i < _visualSettings.TrackColors.Count; i++) SetColorSwatch(_trackPaletteSwatches[i], _visualSettings.TrackColors[i]);
            if (themeChipHost is not null) { RefreshThemeChips(); RefreshMenuThemeChips(); }
            if (AutoPracticeTempoCheck is not null)
            {
                AutoPracticeTempoCheck.IsChecked = _visualSettings.PracticeAutoTempo;
                AutoPracticeMissSlider.IsEnabled = _visualSettings.PracticeAutoTempo;
                AutoPracticeMissSlider.Value = Math.Clamp(_visualSettings.PracticeMissThreshold, AutoPracticeMissSlider.Minimum, AutoPracticeMissSlider.Maximum);
                if (AutoPracticeMissLabel is not null) AutoPracticeMissLabel.Text = ((int)Math.Round(AutoPracticeMissSlider.Value)).ToString();
            }
            SyncAllPlayDialogControls();
        }
        finally { _loadingVisualSettings = false; }
        RefreshDependentRows(); RebuildTrackList(); UpdateRecordingInfo(); UpdatePresetLabels();
    }

    private void SyncPlayDialogToggle(string property, bool value)
    {
        switch (property)
        {
            case nameof(PianoVisualSettings.ShowBackground): if (LayerBackgroundToggle is not null) LayerBackgroundToggle.IsChecked = value; break;
            case nameof(PianoVisualSettings.ShowNotes): if (LayerNotesToggle is not null) LayerNotesToggle.IsChecked = value; break;
            case nameof(PianoVisualSettings.ShowEmbers): if (LayerEmbersToggle is not null) LayerEmbersToggle.IsChecked = value; break;
            case nameof(PianoVisualSettings.ShowHalo): if (LayerHaloToggle is not null) LayerHaloToggle.IsChecked = value; break;
            case nameof(PianoVisualSettings.ShowFlame): if (LayerFlameToggle is not null) LayerFlameToggle.IsChecked = value; break;
            case nameof(PianoVisualSettings.ShowKeys): if (LayerKeysToggle is not null) LayerKeysToggle.IsChecked = value; break;
            default:
                if (ExtrasSubPanel is not null)
                {
                    foreach (var child in ExtrasSubPanel.Children.OfType<CheckBox>())
                        if (Equals(child.Tag, property)) child.IsChecked = value;
                }
                break;
        }
    }

    private void SyncAllPlayDialogControls()
    {
        if (LayerBackgroundToggle is not null) LayerBackgroundToggle.IsChecked = _visualSettings.ShowBackground;
        if (LayerNotesToggle is not null) LayerNotesToggle.IsChecked = _visualSettings.ShowNotes;
        if (LayerEmbersToggle is not null) LayerEmbersToggle.IsChecked = _visualSettings.ShowEmbers;
        if (LayerHaloToggle is not null) LayerHaloToggle.IsChecked = _visualSettings.ShowHalo;
        if (LayerFlameToggle is not null) LayerFlameToggle.IsChecked = _visualSettings.ShowFlame;
        if (LayerKeysToggle is not null) LayerKeysToggle.IsChecked = _visualSettings.ShowKeys;
        if (ExtrasSubPanel is not null)
        {
            foreach (var child in ExtrasSubPanel.Children.OfType<CheckBox>())
                if (child.Tag is string prop) child.IsChecked = (bool)Prop(prop).GetValue(_visualSettings)!;
        }
        if (PlaySpeedSlider is not null) PlaySpeedSlider.Value = Math.Clamp(_visualSettings.NoteFallSpeed, PlaySpeedSlider.Minimum, PlaySpeedSlider.Maximum);
        if (PlaySpeedLabel is not null) PlaySpeedLabel.Text = ((int)_visualSettings.NoteFallSpeed).ToString();
        SyncPlayInlineControls();
    }

    private void UpdatePresetLabels()
    {
        var name = string.IsNullOrWhiteSpace(_visualSettings.PresetName) ? Loc.T("Custom") : VisualPresets.DisplayName(_visualSettings.PresetName);
        if (PresetNameLabel is not null) Loc.Format(PresetNameLabel, _visualSettings.PresetModified ? "Preset · {0} · modified" : "Preset · {0}", name);
        if (HeaderPresetLabel is not null) Loc.Format(HeaderPresetLabel, _visualSettings.PresetModified ? "{0} *" : "{0}", name);
    }

    /// <summary>
    /// Applies the current settings and prints one line of feedback. The message is a template plus
    /// arguments, so it is re-rendered in the new language if the user switches afterwards.
    /// </summary>
    private void ApplyVisualSettings(string statusTemplate, bool reloadBackground = false, params object?[] statusArguments)
    {
        _visualSettings.Clamp();
        Stage.SetVisualSettings(_visualSettings, reloadBackground);
        ApplyRenderBackend();
        SyncCameraOverlay();
        ApplyChromeTheme();
        RefreshHandStatus();
        if (reloadBackground && Stage.BackgroundLoadError is { } error)
        {
            Loc.Set(SettingsSaveLabel, "Background image failed to load");
            ShowMessage(Loc.F("Keyflow could not load this image. Choose a PNG, JPEG, BMP, GIF or TIFF file.\n\n{0}", error), "Background image", MessageBoxImage.Warning);
        }
        else Loc.Format(SettingsSaveLabel, statusTemplate, statusArguments);
        _settingsSaveTimer.Stop(); _settingsSaveTimer.Start();
    }

    private void SaveVisualSettings_Click(object sender, RoutedEventArgs e) => SaveVisualSettings();
    private void SaveVisualSettings()
    {
        try { PianoVisualSettingsStore.Save(_visualSettings); Loc.Set(SettingsSaveLabel, "Saved to this computer"); }
        catch (Exception ex) { Loc.Set(SettingsSaveLabel, "Save failed"); if (!_closing) ShowMessage(ex.Message, "Stage settings", MessageBoxImage.Warning); }
    }

    private void ChooseStageBackground(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*"), Title = Loc.T("Choose a piano visualizer background"), CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        _visualSettings.BackgroundImagePath = dialog.FileName; _visualSettings.BackgroundMode = "Image"; _visualSettings.ShowBackground = true;
        RefreshSettingControls(); MarkModified(nameof(PianoVisualSettings.BackgroundImagePath));
        ApplyVisualSettings("Background image applied", reloadBackground: true);
    }

    /// <summary>
    /// Preview one picture behind the keys for this run only. <c>--background-image=&lt;path&gt;</c> uses it so
    /// the documented screenshots can show the feature without shipping anyone's artwork. The store is
    /// deliberately untouched: no auto-save timer and no "modified" flag, because the picture is not a
    /// setting the user chose. A bad path is likewise not escalated into a dialog - a preview run has
    /// nobody to click it - the stage just keeps the solid colour and records <c>BackgroundLoadError</c>.
    /// </summary>
    public void PreviewBackgroundImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _visualSettings.BackgroundImagePath = Path.GetFullPath(path.Trim());
        _visualSettings.BackgroundMode = "Image";
        _visualSettings.ShowBackground = true;
        _visualSettings.Clamp();
        RefreshSettingControls();
        Stage.SetVisualSettings(_visualSettings, reloadBackground: true);
    }

    // =====================================================================================================
    // Search, navigation and window chrome
    // =====================================================================================================

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        RefreshDependentRows();
        var query = SettingsSearchBox.Text.Trim();
        if (query.Length == 0) return;
        // Jump to the first page that has a match when the current page shows none.
        var pageCount = SettingsPages.Order.Length;
        bool HasMatch(int index) { var page = SettingsPageHost(index); return page is not null && _settingRows.Any(r => ReferenceEquals(r.Page, page) && r.Element.Visibility == Visibility.Visible); }
        var currentIndex = SettingsTabs.SelectedIndex;
        if (currentIndex >= 0 && currentIndex < pageCount && HasMatch(currentIndex)) return;
        for (var i = 0; i < pageCount; i++) if (HasMatch(i)) { SettingsTabs.SelectedIndex = i; return; }
    }

    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selector.SelectionChanged bubbles from combo boxes and lists inside the pages; only react to the tab strip itself.
        if (!ReferenceEquals(e.OriginalSource, SettingsTabs)) return;
        _lastPointerActivity = DateTime.UtcNow;
        // The navigation strip holds thirteen rows in a scrollable column, so arriving at a page from
        // anywhere else — the header chip, a search hit, the General page at the bottom, --settings-tab —
        // has to bring that row into view. Otherwise the dock shows a page whose own entry is off screen.
        // One layout pass later, because the item is still being measured when SelectionChanged fires.
        if (SettingsTabs.SelectedItem is FrameworkElement row)
            row.Dispatcher.BeginInvoke(new Action(row.BringIntoView), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void StyleQuick_Click(object sender, RoutedEventArgs e) { SettingsTabs.SelectedIndex = 0; OpenSettingsPanel(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ResetPage_Click(object sender, RoutedEventArgs e)
    {
        var page = SettingsPageHost(SettingsTabs.SelectedIndex);
        if (page is null) { Loc.Set(SettingsSaveLabel, "This page has no visual settings to reset"); return; }
        var source = BasePresetSettings();
        foreach (var row in _settingRows)
        {
            if (!ReferenceEquals(row.Page, page) || row.Property is null) continue;
            if (row.Property == nameof(PianoVisualSettings.TrackColors)) { _visualSettings.TrackColors = new List<string>(source.TrackColors); continue; }
            var prop = Prop(row.Property); prop.SetValue(_visualSettings, prop.GetValue(source));
        }
        RefreshSettingControls();
        ApplyVisualSettings("Page reset to the active preset");
    }

    /// <summary>The preset the current look was derived from, or the default look when it is unknown.</summary>
    private PianoVisualSettings BasePresetSettings()
    {
        var preset = _presets.FirstOrDefault(p => string.Equals(p.Name, _visualSettings.PresetName, StringComparison.OrdinalIgnoreCase));
        return preset?.Settings ?? DefaultVisualSettings;
    }

    // =====================================================================================================
    // Presets
    // =====================================================================================================

    private void LoadPresetList(string? selectName = null)
    {
        _presetListLoading = true;
        try
        {
            _presets.Clear();
            _presets.AddRange(VisualPresets.BuiltIn);
            _presets.AddRange(CommunityPresets.All);
            _presets.AddRange(VisualPresetStore.Default.LoadUserPresets());
            PresetList.Items.Clear();
            foreach (var preset in _presets) PresetList.Items.Add(new ListBoxItem { Content = PresetListContent(preset), Tag = preset });
            var wanted = selectName ?? _visualSettings.PresetName;
            var index = _presets.FindIndex(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
            PresetList.SelectedIndex = index;
        }
        finally { _presetListLoading = false; }
        UpdatePresetSelectionUi();
    }

    private FrameworkElement PresetListContent(VisualPreset preset)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var strip = new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        strip.Child = new Image { Source = PresetThumbnail(preset), Width = 96, Height = 56, Stretch = Stretch.Fill };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") };
        Loc.Bind(name, () => VisualPresets.DisplayName(preset.Name));
        var description = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), FontSize = 9.5, Margin = new Thickness(0, 2, 0, 0) };
        Loc.Bind(description, () => preset.BuiltIn ? Loc.T(preset.Description) : preset.Description);
        text.Children.Add(name);
        text.Children.Add(description);
        var kind = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center, Background = (Brush)FindResource(preset.BuiltIn ? "AccentSoftBrush" : "ControlHoverBrush"), Margin = new Thickness(10, 0, 0, 0) };
        var kindLabel = new TextBlock { FontSize = 8, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("MutedTextBrush") };
        Loc.Set(kindLabel, preset.Community ? "COMMUNITY" : preset.BuiltIn ? "BUILT-IN" : "USER");
        kind.Child = kindLabel;
        Grid.SetColumn(text, 1); Grid.SetColumn(kind, 2);
        grid.Children.Add(strip); grid.Children.Add(text); grid.Children.Add(kind);
        return grid;
    }

    private static IEnumerable<string> PresetSwatches(PianoVisualSettings s) => s.ColorMode switch
    {
        "PerHand" => [s.LeftHandColor, s.RightHandColor, s.HaloColor],
        "PerTrack" => [s.TrackColors[0], s.TrackColors[1], s.TrackColors[2]],
        "RainbowPitch" or "RainbowTime" => ["#FF4D4D", "#FFD166", "#4FE3B0", "#4DA3FF", "#B46BFF"],
        _ => [s.NoteColorStart, s.NoteColorEnd, s.HaloColor]
    };

    private readonly Dictionary<string, ImageSource> _presetThumbs = [];

    /// <summary>
    /// The picture of a preset: the render that was stored with the look when it was saved or imported,
    /// and only when the file carries none, the embers-style miniature drawn from its settings.
    /// </summary>
    private ImageSource PresetThumbnail(VisualPreset preset)
    {
        var s = preset.Settings;
        var key = $"{preset.Name}|{s.ColorMode}|{s.NoteColorStart}|{s.NoteColorEnd}|{s.HaloColor}|{s.NoteStyle}";
        if (_presetThumbs.TryGetValue(key, out var cached)) return cached;
        if (PianoPath.PresetThumbnail.Decode(preset.Thumbnail) is { } stored) { _presetThumbs[key] = stored; return stored; }
        var w = 96; var h = 56;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 10, 15)), null, new Rect(0, 0, w, h));
            var swatches = PresetSwatches(s).Select(TryColor).ToList();
            if (swatches.Count == 0) swatches.Add(Color.FromRgb(148, 148, 148));
            var halo = swatches[^1];
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, halo.R, halo.G, halo.B)), null, new Rect(0, h * .55 - 2, w, 2));
            const int whites = 12; var ww = (double)w / whites; var keyTop = h * .55;
            for (var i = 0; i < whites; i++)
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(224, 226, 233)), null, new Rect(i * ww + .5, keyTop, ww - 1, h - keyTop - 1));
            for (var i = 0; i < whites - 1; i++)
            {
                var step = i % 7; if (step == 2 || step == 6) continue;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 24, 32)), null, new Rect((i + 1) * ww - ww * .17, keyTop, ww * .34, (h - keyTop) * .62));
            }
            double[] xs = [1.15, 3.1, 5.35, 7.2, 9.05, 10.55];
            double[] tops = [.08, .22, .12, .32, .18, .04];
            for (var i = 0; i < xs.Length; i++)
            {
                var c = swatches[i % swatches.Count];
                dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(xs[i] * ww + 1, tops[i] * h, ww - 2, keyTop - tops[i] * h - 3));
            }
        }
        var bmp = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        _presetThumbs[key] = bmp;
        return bmp;
    }

    private static Color TryColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return Color.FromRgb(148, 148, 148); }
    }

    private VisualPreset? SelectedPreset => (PresetList.SelectedItem as ListBoxItem)?.Tag as VisualPreset;

    private void UpdatePresetSelectionUi()
    {
        var preset = SelectedPreset;
        if (preset is null) Loc.Set(PresetDescriptionLabel, "Select a preset to preview its description.");
        else Loc.Bind(PresetDescriptionLabel, () => (preset.BuiltIn ? Loc.T(preset.Description) : preset.Description) + (preset.FilePath is { Length: > 0 } ? $"  ·  {preset.FilePath}" : ""));
        DeletePresetButton.IsEnabled = preset is { BuiltIn: false };
        ApplyPresetButton.IsEnabled = preset is not null;
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_presetListLoading || !ReferenceEquals(e.OriginalSource, PresetList)) return;
        UpdatePresetSelectionUi();
    }

    private void PresetList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (SelectedPreset is not null) ApplyPreset_Click(sender, e); }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is not { } preset) return;
        ApplyPreset(preset);
    }

    private void ApplyPreset(VisualPreset preset)
    {
        CommitHistory();
        _visualSettings.CopyFrom(preset.Settings, keepBackgroundImage: true);
        _visualSettings.PresetName = preset.Name; _visualSettings.PresetModified = false;
        if (_visualSettings.BackgroundMode == "Image" && string.IsNullOrWhiteSpace(_visualSettings.BackgroundImagePath)) _visualSettings.BackgroundMode = "Solid";
        RefreshSettingControls();
        ApplyVisualSettings("Preset “{0}” applied", false, VisualPresets.DisplayName(preset.Name));
    }

    private void SavePresetAs_Click(object sender, RoutedEventArgs e)
    {
        var suggested = _visualSettings.PresetModified || VisualPresets.FindBuiltIn(_visualSettings.PresetName) is not null ? "My " + _visualSettings.PresetName : _visualSettings.PresetName;
        var prompt = new TextPromptWindow(Loc.T("Save preset"), Loc.T("Name for this look. Existing user presets with the same name are replaced."), suggested) { Owner = this };
        if (prompt.ShowDialog() != true || prompt.Result is not { } name) return;
        try
        {
            if (CommunityPresets.NameConflict(name) is { } conflict) { ShowMessage(Loc.F(conflict, name), "Save preset", MessageBoxImage.Information); return; }
            var saved = VisualPresetStore.Default.Save(name, _visualSettings, PianoPath.PresetThumbnail.Encode(_visualSettings));
            _visualSettings.PresetName = saved.Name; _visualSettings.PresetModified = false;
            LoadPresetList(saved.Name); UpdatePresetLabels();
            ApplyVisualSettings("Preset “{0}” saved", false, VisualPresets.DisplayName(saved.Name));
        }
        catch (Exception ex) { ShowMessage(ex.Message, "Save preset", MessageBoxImage.Warning); }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is not { BuiltIn: false } preset) return;
        if (!Confirm(Loc.F("Delete the preset “{0}”?", VisualPresets.DisplayName(preset.Name)), "Delete preset")) return;
        VisualPresetStore.Default.Delete(preset);
        LoadPresetList(VisualPresets.DefaultPresetName);
        Loc.Format(SettingsSaveLabel, "Preset “{0}” deleted", VisualPresets.DisplayName(preset.Name));
    }

    private void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("Keyflow preset (*.json)|*.json|All files (*.*)|*.*"), Title = Loc.T("Import a Keyflow preset") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = VisualPresetStore.Import(dialog.FileName);
            // The picture and the description travel with the file, so importing one keeps both.
            var saved = VisualPresetStore.Default.Save(imported.Name, imported.Settings, imported.Thumbnail);
            if (!string.IsNullOrWhiteSpace(imported.Description)) saved = saved with { Description = imported.Description };
            LoadPresetList(saved.Name);
            ApplyPreset(saved);
        }
        catch (Exception ex) { ShowMessage(Loc.F("This file is not a valid Keyflow preset.\n{0}", ex.Message), "Import preset", MessageBoxImage.Warning); }
    }

    private void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Keyflow preset (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = VisualPresetStore.SanitizeName(_visualSettings.PresetName) + ".json", Title = "Export the current look" };
        if (dialog.ShowDialog(this) != true) return;
        try { VisualPresetStore.Export(_visualSettings, dialog.FileName, PianoPath.PresetThumbnail.Encode(_visualSettings)); Loc.Set(SettingsSaveLabel, "Preset exported"); }
        catch (Exception ex) { ShowMessage(ex.Message, "Export preset", MessageBoxImage.Warning); }
    }

    // =================================================================================================
    // Themes the user made (see Theme/UserShellTheme.cs and Ui/ThemeStudioWindow.cs)
    // =================================================================================================

    private void CreateTheme_Click(object sender, RoutedEventArgs e)
    {
        var seed = UserShellThemes.FromTheme(ShellThemeManager.Current, Loc.T("My theme"));
        var studio = new ThemeStudioWindow(seed, "Create theme") { Owner = this };
        if (studio.ShowDialog() != true || studio.Result is not { } theme) return;
        SaveUserTheme(theme);
    }

    private void EditTheme_Click(object sender, RoutedEventArgs e)
    {
        var current = ShellThemeManager.Current;
        if (!UserShellThemes.IsUserTheme(current.Id)) { Loc.Set(SettingsSaveLabel, "Only themes you made can be edited or deleted."); return; }
        var studio = new ThemeStudioWindow(UserShellThemes.FromTheme(current, current.Name), "Edit theme") { Owner = this };
        if (studio.ShowDialog() != true || studio.Result is not { } theme) return;
        // Renaming writes a new file, so the old one has to go or the picker would show the theme twice.
        if (!string.Equals(theme.Name, current.Name, StringComparison.OrdinalIgnoreCase)) UserThemeStore.Default.Delete(current.Id);
        SaveUserTheme(theme);
    }

    private void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        var current = ShellThemeManager.Current;
        if (!UserShellThemes.IsUserTheme(current.Id)) { Loc.Set(SettingsSaveLabel, "Only themes you made can be edited or deleted."); return; }
        if (!UserThemeStore.Default.Delete(current.Id)) { Loc.Set(SettingsSaveLabel, "Only themes you made can be edited or deleted."); return; }
        _visualSettings.ShellTheme = ShellThemes.DefaultId;
        MarkModified();
        ApplyVisualSettings("Theme “{0}” deleted", false, current.Name);
        RefreshThemeChips(); RefreshMenuThemeChips();
    }

    /// <summary>Saves a theme the user made, selects it and repaints both chip rows.</summary>
    private void SaveUserTheme(UserShellTheme theme)
    {
        if (UserThemeStore.NameConflict(theme.Name) is { } conflict)
        {
            ShowMessage(Loc.F(conflict, theme.Name), "Create theme", MessageBoxImage.Information);
            return;
        }
        ShellTheme saved;
        try { saved = UserThemeStore.Default.Save(theme); }
        catch (Exception ex) { ShowMessage(ex.Message, "Create theme", MessageBoxImage.Warning); return; }
        _visualSettings.ShellTheme = saved.Id;
        MarkModified();
        ApplyVisualSettings("Theme “{0}” saved", false, saved.Name);
        RefreshThemeChips(); RefreshMenuThemeChips();
    }

    private void ResetVisualSettings_Click(object sender, RoutedEventArgs e)
    {
        var preset = VisualPresets.FindBuiltIn(VisualPresets.DefaultPresetName)!;
        ApplyPreset(preset);
        LoadPresetList(preset.Name);
    }

    // =====================================================================================================
    // Per-track list (MIDI page) and recording info
    // =====================================================================================================

    private void RebuildTrackList()
    {
        if (TrackListHost is null) return;
        TrackListHost.Children.Clear();
        var tracks = _allNotes.Select(n => n.Track).Distinct().Order().ToList();
        if (tracks.Count == 0)
        {
            var empty = new TextBlock { Style = (Style)FindResource("MutedTextStyle") };
            Loc.Set(empty, "Open a MIDI file to see its tracks here.");
            TrackListHost.Children.Add(empty);
            return;
        }
        foreach (var track in tracks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var paletteIndex = ((track % 8) + 8) % 8;
            var swatch = new Button { Tag = paletteIndex, Width = 26, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 0) };
            // Both calls repeat the condition on purpose: tools/check_sources.py reads the literals of
            // the argument, and a variable holding the key would hide them from that check.
            Loc.Set(swatch, _visualSettings.ColorMode == "PerTrack" ? "Change the color of this track" : "Track color (used by the “Per MIDI track” color mode)", FrameworkElement.ToolTipProperty);
            Loc.Set(swatch, _visualSettings.ColorMode == "PerTrack" ? "Change the color of this track" : "Track color (used by the “Per MIDI track” color mode)", AutomationProperties.NameProperty);
            SetColorSwatch(swatch, _visualSettings.TrackColors[paletteIndex]); swatch.Click += TrackPaletteButton_Click;
            var count = _allNotes.Count(n => n.Track == track);
            var label = Loc.F("Track {0}", track + 1);
            var name = _trackNames.TryGetValue(track, out var trackName) ? $"{label} · {trackName}" : label;
            var check = new CheckBox { Tag = track, IsChecked = !_mutedTracks.Contains(track), HorizontalAlignment = HorizontalAlignment.Stretch };
            Loc.Format(check, "{0}   ({1} notes)", name, count);
            Loc.Set(check, "Audible and visible when on; muted tracks are hidden from the stage and the score.", FrameworkElement.ToolTipProperty);
            check.Checked += TrackMute_Changed; check.Unchecked += TrackMute_Changed;
            Grid.SetColumn(check, 1);
            row.Children.Add(swatch); row.Children.Add(check);
            TrackListHost.Children.Add(row);
        }
    }

    private void TrackMute_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || sender is not CheckBox { Tag: int track } check) return;
        if (check.IsChecked == true) _mutedTracks.Remove(track); else _mutedTracks.Add(track);
        ApplyTrackFilter(); UpdateSongUi(); UpdatePlaybackLabel(); ResetScore(); UpdateStage(); UpdateTime();
    }

    private void UpdateRecordingInfo()
    {
        if (RecordingInfoLabel is null) return;
        var (width, height) = RecordingSize();
        var sequence = _visualSettings.RecordingFormat == RecordingFormatIds.PngSequence;
        var mp4 = _visualSettings.RecordingFormat == RecordingFormatIds.Mp4;
        var format = sequence
            ? (_visualSettings.RecordingTransparent ? Loc.T("PNG frames (32-bit alpha)") : Loc.T("PNG frames (opaque)"))
            : mp4
                ? Loc.T("MP4 (H.264 + AAC)")
                : Loc.T("AVI (MJPEG when a codec is installed, raw BGR otherwise)");
        var audio = _visualSettings.RecordAudio
            ? _audio.HasSoundFont
                ? mp4
                    ? Loc.T("audio is written inside the file as AAC")
                    : Loc.T("audio is written to a WAV beside it (mux it with the ffmpeg line below)")
                : Loc.T("no SoundFont is loaded, so this recording will have no audio")
            : Loc.T("audio is not captured");
        // which engine renders the take: the GPU stage renders it at exactly this size when it is on screen
        var gpuTake = _gpuLoop is { Error: null } && (Stage.UsesGpuFrame || _gpuWindow is not null) && !(sequence && _visualSettings.RecordingTransparent);
        var engine = gpuTake
            ? Loc.T("Frames are rendered by the GPU stage at exactly this size.")
            : sequence && _visualSettings.RecordingTransparent && _gpuLoop is not null
                ? Loc.T("Transparent frames are drawn by the software stage (the GPU frame is opaque).")
                : Loc.T("Frames are drawn by the software stage.");
        Loc.Format(RecordingInfoLabel, "Next recording: {0} × {1} @ {2:0} fps · {3} · {4}. {5}", width, height, _visualSettings.RecordingFrameRate, format, audio, engine);
    }

    private (int Width, int Height) RecordingSize()
    {
        switch (_visualSettings.RecordingResolution)
        {
            case "720p": return (1280, 720);
            case "1080p": return (1920, 1080);
        }
        var ratio = Stage.ActualHeight / Math.Max(1, Stage.ActualWidth);
        var width = Math.Max(640, Math.Min(1920, (int)Stage.ActualWidth)) & ~1;
        var height = Math.Max(360, (int)(width * (double.IsFinite(ratio) && ratio > 0 ? ratio : 9.0 / 16))) & ~1;
        return (width, height);
    }

    // =====================================================================================================
    // History: undo / redo over everything the dock can change
    // =====================================================================================================

    /// <summary>How many distinct states the history keeps. A continuous drag counts as one state.</summary>
    private const int HistoryDepth = 32;
    private readonly List<string> _undoHistory = [];
    private readonly List<string> _redoHistory = [];
    /// <summary>JSON of the last committed state: what the next undo returns to.</summary>
    private string _historyBaseline = "";
    /// <summary>True while a change has happened whose starting state is not on the undo stack yet.</summary>
    private bool _historyPending;
    private bool _restoringHistory;

    /// <summary>
    /// Records that the settings changed. The state <em>before</em> the change is not known here — the
    /// handlers mutate first — so the history point is committed by <see cref="CommitHistory"/>, which
    /// the settings-save timer calls once the user stops moving things. That single hook is what makes
    /// a slow slider drag one undo step instead of two hundred, without special-casing any control.
    /// </summary>
    private void RecordHistory(string? property)
    {
        if (_loadingVisualSettings || _restoringHistory) return;
        _historyPending = true;
    }

    /// <summary>
    /// Commits the change in progress: the state it started from enters the undo stack and the current
    /// state becomes the new baseline. Called when the controls settle (the save timer), and before
    /// undo, redo, preset changes and profile exports so those never see a half-committed history.
    /// </summary>
    private void CommitHistory()
    {
        if (_restoringHistory || !_historyPending) return;
        if (_historyBaseline.Length == 0) _historyBaseline = _visualSettings.ToJson();
        var current = _visualSettings.ToJson();
        _historyPending = false;
        if (current == _historyBaseline) return;
        if (_historyBaseline.Length > 0)
        {
            _undoHistory.Add(_historyBaseline);
            while (_undoHistory.Count > HistoryDepth) _undoHistory.RemoveAt(0);
            _redoHistory.Clear();
        }
        _historyBaseline = current;
    }

    /// <summary>Ctrl+Z: restores the state before the change in progress (a settled drag is one change).</summary>
    private void UndoVisualSettings()
    {
        if (_restoringHistory) return;
        CommitHistory();
        if (_undoHistory.Count == 0) { Loc.Set(SettingsSaveLabel, "Nothing to undo"); return; }
        var json = _undoHistory[^1];
        _undoHistory.RemoveAt(_undoHistory.Count - 1);
        _redoHistory.Add(_visualSettings.ToJson());
        while (_redoHistory.Count > HistoryDepth) _redoHistory.RemoveAt(0);
        RestoreHistory(json, "Undo");
    }

    /// <summary>Ctrl+Shift+Z (or Ctrl+Y): replays a change that was undone.</summary>
    private void RedoVisualSettings()
    {
        if (_restoringHistory) return;
        CommitHistory();
        if (_redoHistory.Count == 0) { Loc.Set(SettingsSaveLabel, "Nothing to redo"); return; }
        var json = _redoHistory[^1];
        _redoHistory.RemoveAt(_redoHistory.Count - 1);
        _undoHistory.Add(_visualSettings.ToJson());
        while (_undoHistory.Count > HistoryDepth) _undoHistory.RemoveAt(0);
        RestoreHistory(json, "Redo");
    }

    /// <summary>
    /// Swaps a snapshot into the live settings: every control, the stage, the chrome and the settings
    /// file follow, so stepping back looks exactly like the user having set the values again. The
    /// interface language is not part of a snapshot (see <see cref="PianoVisualSettings.CopyFrom"/>).
    /// </summary>
    private void RestoreHistory(string json, string statusKey)
    {
        _restoringHistory = true;
        try
        {
            _visualSettings.CopyFrom(PianoVisualSettings.FromJson(json), keepBackgroundImage: false);
            RefreshSettingControls();
            _historyBaseline = _visualSettings.ToJson(); _historyPending = false;
        }
        finally { _restoringHistory = false; }
        ApplyVisualSettings(statusKey, reloadBackground: true);
    }

    /// <summary>
    /// Drops the history and takes the current settings as the starting point. Called once the window
    /// has loaded its settings, and again after a profile replaces them: a fresh setup is a fresh start,
    /// not an undo step away from whatever the previous file held.
    /// </summary>
    private void StartHistory()
    {
        _historyBaseline = _visualSettings.ToJson();
        _historyPending = false;
        _undoHistory.Clear();
        _redoHistory.Clear();
    }

    // =====================================================================================================
    // Settings profile: one file carrying the stage settings, the interface language and the shell theme
    // =====================================================================================================

    /// <summary>Writes the whole setup to <paramref name="path"/>; returns false and reports when the file cannot be written.</summary>
    internal bool ExportProfile(string path)
    {
        try
        {
            File.WriteAllText(path, SettingsProfile.Capture(_visualSettings, ShellThemeManager.Current.Id).ToJson(), Encoding.UTF8);
            Loc.Format(SettingsSaveLabel, "Profile “{0}” saved", Path.GetFileName(path));
            return true;
        }
        catch (Exception ex)
        {
            Loc.Set(SettingsSaveLabel, "Could not write the profile");
            if (!_closing) ShowMessage(ex.Message, "Settings profile", MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Reads a profile from <paramref name="path"/> and applies it; returns false when the file is not a Keyflow profile.</summary>
    internal bool ImportProfile(string path)
    {
        SettingsProfile? profile = null;
        try { profile = SettingsProfile.FromJson(File.ReadAllText(path)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (profile is null) { Loc.Set(SettingsSaveLabel, "This file is not a Keyflow profile"); return false; }
        ApplyProfile(profile, Path.GetFileName(path));
        return true;
    }

    /// <summary>
    /// Applies a profile: the shell theme first (the chrome should already wear the profile's palette
    /// when the settings repaint the stage), then the stage settings, then the language. A language id
    /// that no bundled table answers to falls back to English rather than to a half-translated window.
    /// </summary>
    private void ApplyProfile(SettingsProfile profile, string fileName)
    {
        CommitHistory();
        _visualSettings.ShellTheme = ShellThemeManager.Apply(profile.ShellTheme).Id;
        _visualSettings.CopyFrom(profile.Visual, keepBackgroundImage: false);
        RefreshSettingControls();
        ApplyLanguage(Languages.Find(profile.Language).Id);
        StartHistory();
        ApplyVisualSettings("Profile “{0}” applied", true, fileName);
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = SettingsProfile.SuggestedName,
            Filter = Loc.T("Keyflow profile (*.json)|*.json|All files (*.*)|*.*"),
            Title = Loc.T("Save the current setup as a profile"),
        };
        if (dialog.ShowDialog(this) == true) ExportProfile(dialog.FileName);
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Loc.T("Keyflow profile (*.json)|*.json|All files (*.*)|*.*"),
            Title = Loc.T("Open a Keyflow profile"),
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true) ImportProfile(dialog.FileName);
    }

    /// <summary>
    /// Files dropped on the window: a Keyflow profile replaces the whole setup, a MIDI file opens as a
    /// song, and an image becomes the stage background. One gesture instead of a dialog plus a page hunt.
    /// </summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return;
        var path = files[0];
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ImportProfile(path);
        else if (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase)) OpenMidiFile(path);
        else if (IsMusicXml(path)) OpenSongFile(path);
        else ApplyDroppedBackground(path);
        e.Handled = true;
    }

    /// <summary>A dropped image becomes the stage background, through the same state the background row writes.</summary>
    private void ApplyDroppedBackground(string path)
    {
        _visualSettings.BackgroundImagePath = path;
        _visualSettings.BackgroundMode = "Image";
        _visualSettings.ShowBackground = true;
        RefreshSettingControls();
        MarkModified(nameof(PianoVisualSettings.BackgroundImagePath));
        ApplyVisualSettings("Background image applied", reloadBackground: true);
    }
}
