using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
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
        public required string SearchText;
        public required Panel Page;
        public required Border Card;
        public string? Property;
        public Func<bool>? VisibleWhen;
    }

    private readonly List<SettingRow> _settingRows = [];
    private readonly List<Border> _settingCards = [];
    private readonly Dictionary<string, TextBox> _visualValueBoxes = [];
    private readonly Dictionary<string, (double Min, double Max)> _sliderRanges = [];
    private readonly Dictionary<string, ComboBox> _visualChoices = [];
    private readonly Dictionary<string, CheckBox> _visualToggles = [];
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

    // =====================================================================================================
    // Page construction
    // =====================================================================================================

    private void BuildVisualSettingsControls()
    {
        _loadingVisualSettings = true;
        BuildStylePage(); BuildThemePage(); BuildNotesPage(); BuildParticlesPage(); BuildKeyboardPage(); BuildBackgroundPage(); BuildCameraPage(); BuildRecordingPage();
        _loadingVisualSettings = false;
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
        var body = Card(StyleSettingsHost, "LAYERS", "Quick switches for every layer of the stage. Detailed controls live on the other pages.");
        Toggle(body, "Falling notes", nameof(PianoVisualSettings.ShowNotes), "Draw the piano-roll bars for MIDI playback and live playing.");
        Toggle(body, "Sparks", nameof(PianoVisualSettings.ShowEmbers), "Particle burst when a note reaches the keyboard.");
        Toggle(body, "Wisps", nameof(PianoVisualSettings.ShowWisps), "Smoke-like plasma streams rising from held keys (Embers style).");
        Toggle(body, "Flames", nameof(PianoVisualSettings.ShowFlame), "Fire bursts at the impact point.");
        Toggle(body, "Impact rings", nameof(PianoVisualSettings.ShowImpactRings), "Expanding shock ring when a note hits the key line.");
        Toggle(body, "Light beams", nameof(PianoVisualSettings.ShowLightBeams), "Soft columns of light above every sounding key.");
        Toggle(body, "Hit line halo", nameof(PianoVisualSettings.ShowHalo), "Glowing line where the notes meet the keys.");
        Toggle(body, "Piano keys", nameof(PianoVisualSettings.ShowKeys), "Show the 88-key keyboard.");
        Toggle(body, "Background layers", nameof(PianoVisualSettings.ShowBackground), "Image, gradient, stars and guide lanes.");
        Toggle(body, "Keyflow watermark", nameof(PianoVisualSettings.ShowWatermark), "Small logo above the keyboard.");
        Toggle(body, "Key counter", nameof(PianoVisualSettings.ShowCounter), "Show how many keys are held.");
        Toggle(body, "FPS & particle HUD", nameof(PianoVisualSettings.ShowFps), "Performance overlay in the top-right corner.");
    }

    /// <summary>
    /// The Theme page: the look of the application shell (independent from the piano stage itself),
    /// how much the chrome animates, and the two concert layers that can be added to the stage —
    /// blossom petals and sweeping spotlights.
    /// </summary>
    private void BuildThemePage()
    {
        var shell = Card(ThemeSettingsHost, "INTERFACE THEME", "The chrome around the stage: surfaces, accents and the animated acoustic backdrop. Applying a stage preset also switches the theme that belongs to it.");
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        themeChipHost = chips;
        Register(shell, chips, "interface theme shell concert grand noir obsidian velvet gold backdrop", nameof(PianoVisualSettings.ShellTheme));
        themeBlurbLabel = new TextBlock { Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 2, 0, 8) };
        Register(shell, themeBlurbLabel, "theme description");
        Choice(shell, "Motion", nameof(PianoVisualSettings.ChromeMotion), "How much the interface moves: animated acoustic backdrop, panel transitions and button response. Off keeps everything still.",
            ("Off", "Off"), ("Calm", "Calm"), ("Full", "Full"));
        SliderRow(shell, "Backdrop density", nameof(PianoVisualSettings.BackdropDensity), 0, 200, "Density of the floating concert dust motes and acoustic waves in the backdrop.");

        var concert = Card(ThemeSettingsHost, "STAGE ATMOSPHERE LAYERS", "Optional recital layers: floating acoustic motes and soft ambient stage illumination.");
        Toggle(concert, "Acoustic motes", nameof(PianoVisualSettings.ShowPetals), "Floating ambient particles drift through the concert space; the colour below tints them.");
        SliderRow(concert, "Mote amount", nameof(PianoVisualSettings.PetalAmount), 0, 150, "Density of floating concert particles in the air.").VisibleWhen = () => _visualSettings.ShowPetals;
        ColorRow(concert, "Mote color", nameof(PianoVisualSettings.PetalColor), "Colour of the floating ambient particles.").VisibleWhen = () => _visualSettings.ShowPetals;
        Toggle(concert, "Stage illumination", nameof(PianoVisualSettings.ShowSpotlights), "Soft atmospheric stage illumination across the concert keybed.");
        SliderRow(concert, "Illumination intensity", nameof(PianoVisualSettings.SpotlightIntensity), 0, 100, "Brightness of the concert illumination.").VisibleWhen = () => _visualSettings.ShowSpotlights;

        var looks = Card(ThemeSettingsHost, "QUICK LOOKS", "One click applies a complete concert look: stage preset plus matching interface theme.");
        ButtonRow(looks,
            ("Concert Grand", (_, _) => ApplyBuiltInPreset("Neon Violet")),
            ("Concert Gold", (_, _) => ApplyBuiltInPreset("Concert Gold")),
            ("Moonlight", (_, _) => ApplyBuiltInPreset("Moonlight Sonata")));
        Note(looks, "Every preset can be edited afterwards; the pages next to this one keep the piano roll, keyboard and camera in sync with the new theme.");
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
            themeChipHost.Children.Add(chip);
        }
        if (themeBlurbLabel is not null) themeBlurbLabel.Text = ShellThemeManager.Current.Blurb;
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

        var flames = Card(ParticleSettingsHost, "FLAMES & RINGS", "Fire at the impact point and shock rings.");
        Toggle(flames, "Enable flames", nameof(PianoVisualSettings.ShowFlame), "Fire bursts while a key sounds.");
        SliderRow(flames, "Flame intensity", nameof(PianoVisualSettings.FlameIntensity), 0, 100, "Brightness and size of the fire.");
        SliderRow(flames, "Flame height", nameof(PianoVisualSettings.FlameHeight), 0, 100, "How tall the flames reach.");
        Choice(flames, "Flame color", nameof(PianoVisualSettings.FlameColorMode), "Classic warm fire or the color of the note.", ("Warm", "Warm fire"), ("Note", "Note color"));
        Toggle(flames, "Enable impact rings", nameof(PianoVisualSettings.ShowImpactRings), "Expanding ring on every hit.");
        SliderRow(flames, "Ring size", nameof(PianoVisualSettings.RingSize), 0, 100, "Final radius of the ring.");
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
        var shadingOn = () => _visualSettings.ShadingQuality != "Off";
        Choice(shader, "Shading engine", nameof(PianoVisualSettings.ShadingQuality),
            "Off draws the flat vector keys. Fast, Balanced and Cinematic trade bake time for shadow and occlusion samples.",
            ("Off", "Off · flat keys"), ("Fast", "Fast"), ("Balanced", "Balanced"), ("Cinematic", "Cinematic"));
        SliderRow(shader, "Camera tilt", nameof(PianoVisualSettings.ShaderCameraTilt), 0, 100, "Low camera exaggerates the perspective and lengthens the black key shadows; high camera flattens the bed.").VisibleWhen = shadingOn;
        SliderRow(shader, "Key light", nameof(PianoVisualSettings.ShaderKeyLight), 0, 200, "Intensity of the softbox above the keyboard.").VisibleWhen = shadingOn;
        SliderRow(shader, "Shadow strength", nameof(PianoVisualSettings.ShaderShadows), 0, 100, "How dark the shadows are; also widens the penumbra.").VisibleWhen = shadingOn;
        SliderRow(shader, "Contact occlusion", nameof(PianoVisualSettings.ShaderAmbientOcclusion), 0, 100, "Ambient light lost in the gaps between keys and under the fallboard.").VisibleWhen = shadingOn;
        SliderRow(shader, "Gloss", nameof(PianoVisualSettings.ShaderGloss), 0, 100, "Polish of the ivory and ebony; higher means tighter highlights.").VisibleWhen = shadingOn;
        SliderRow(shader, "Rim light", nameof(PianoVisualSettings.ShaderRimLight), 0, 150, "Accent light rising from behind the fallboard, tinted by the hit-line color.").VisibleWhen = shadingOn;
        SliderRow(shader, "Key emission", nameof(PianoVisualSettings.ShaderEmissive), 0, 200, "How strongly a sounding key glows and lights the bed around it.").VisibleWhen = shadingOn;
        SliderRow(shader, "Exposure", nameof(PianoVisualSettings.ShaderExposure), 20, 250, "Applied before the filmic tonemapper.").VisibleWhen = shadingOn;
        Toggle(shader, "ACES filmic tonemapper", nameof(PianoVisualSettings.ShaderFilmic), "Unreal's default filmic curve; off clips highlights linearly instead.").VisibleWhen = shadingOn;
        Note(shader, "The keyboard is baked once and cached, then only the sounding keys are re-shaded, so the shader stays inside the frame budget. Green-screen recording always uses the flat keys.").VisibleWhen = shadingOn;
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
        SliderRow(atmosphere, "Vignette", nameof(PianoVisualSettings.Vignette), 0, 100, "Darkens the corners for a cinematic frame.");
        SliderRow(atmosphere, "Horizon glow", nameof(PianoVisualSettings.HorizonGlow), 0, 100, "Colored glow rising from the keyboard line.");
        SliderRow(atmosphere, "Light beam intensity", nameof(PianoVisualSettings.BeamIntensity), 0, 100, "Brightness of the columns above sounding keys.");

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
    }

    private void BuildRecordingPage()
    {
        var output = Card(RecordingSettingsHost, "OUTPUT", "Applied when the next recording starts.");
        Choice(output, "Resolution", nameof(PianoVisualSettings.RecordingResolution), "Match the window, or render to a fixed 16:9 size.", ("Window", "Match window"), ("720p", "1280 × 720"), ("1080p", "1920 × 1080"));
        SliderRow(output, "Frame rate", nameof(PianoVisualSettings.RecordingFrameRate), 15, 60, "Frames per second of the AVI file.");
    }

    // =====================================================================================================
    // Row builders
    // =====================================================================================================

    private Panel Card(Panel page, string title, string subtitle)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 10, 0, 0), Background = (Brush)FindResource("ControlBrush"), BorderBrush = (Brush)FindResource("ControlBorderBrush"), BorderThickness = new Thickness(1) };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("EyebrowTextStyle") });
        body.Children.Add(new TextBlock { Text = subtitle, Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 2, 0, 6) });
        card.Child = body; page.Children.Add(card); _settingCards.Add(card);
        return body;
    }

    private SettingRow Register(Panel body, FrameworkElement element, string searchText, string? property = null)
    {
        body.Children.Add(element);
        var card = (Border)((FrameworkElement)body).Parent;
        var page = (Panel)card.Parent;
        var row = new SettingRow { Element = element, SearchText = searchText.ToLowerInvariant(), Page = page, Card = card, Property = property };
        _settingRows.Add(row);
        return row;
    }

    private SettingRow Toggle(Panel body, string label, string property, string tooltip)
    {
        var check = new CheckBox { Content = label, Tag = property, IsChecked = (bool)Prop(property).GetValue(_visualSettings)!, Margin = new Thickness(0, 6, 0, 6), ToolTip = tooltip, HorizontalAlignment = HorizontalAlignment.Stretch };
        check.Checked += VisualToggle_Changed; check.Unchecked += VisualToggle_Changed;
        _visualToggles[property] = check; _visualToggleList.Add(check);
        return Register(body, check, label + " " + tooltip, property);
    }

    private SettingRow SliderRow(Panel body, string label, string property, double minimum, double maximum, string tooltip)
    {
        var prop = Prop(property);
        var current = (double)prop.GetValue(_visualSettings)!;
        var row = new Grid { Margin = new Thickness(0, 4, 0, 6), ToolTip = tooltip };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Text = label, Style = (Style)FindResource("LabelTextStyle") };
        var box = new TextBox { Text = FormatSetting(property, current), Tag = property, Width = 74, Height = 24, Padding = new Thickness(6, 2, 6, 2), FontSize = 10.5, TextAlignment = TextAlignment.Right, ToolTip = "Type a value and press Enter" };
        box.LostFocus += VisualValueBox_Commit; box.KeyDown += VisualValueBox_KeyDown;
        var reset = new Button { Content = "↺", Tag = property, Style = (Style)FindResource("MiniButtonStyle"), Margin = new Thickness(4, 0, 0, 0), ToolTip = $"Reset to {FormatSetting(property, (double)prop.GetValue(DefaultVisualSettings)!)}" };
        reset.Click += VisualReset_Click;
        var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(current, minimum, maximum), Tag = property, Margin = new Thickness(0, 2, 0, 0) };
        slider.ValueChanged += VisualSlider_ValueChanged;
        Grid.SetColumn(box, 1); Grid.SetColumn(reset, 2); Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 3);
        row.Children.Add(text); row.Children.Add(box); row.Children.Add(reset); row.Children.Add(slider);
        _visualSliders[property] = slider; _visualValueBoxes[property] = box; _sliderRanges[property] = (minimum, maximum);
        return Register(body, row, label + " " + tooltip, property);
    }

    private SettingRow Choice(Panel body, string label, string property, string tooltip, params (string Value, string Caption)[] options)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 6), ToolTip = tooltip };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, Style = (Style)FindResource("LabelTextStyle") };
        var combo = new ComboBox { Tag = property, Width = 210, Height = 30, DisplayMemberPath = "Caption", SelectedValuePath = "Value" };
        combo.ItemsSource = options.Select(o => new ChoiceOption(o.Value, o.Caption)).ToList();
        combo.SelectedValue = (string)Prop(property).GetValue(_visualSettings)!;
        combo.SelectionChanged += VisualChoice_Changed;
        Grid.SetColumn(combo, 1); row.Children.Add(text); row.Children.Add(combo);
        _visualChoices[property] = combo;
        return Register(body, row, label + " " + tooltip + " " + string.Join(' ', options.Select(o => o.Caption)), property);
    }

    private SettingRow ColorRow(Panel body, string label, string property, string tooltip)
    {
        var current = (string)Prop(property).GetValue(_visualSettings)!;
        var row = new Grid { Margin = new Thickness(0, 5, 0, 6), ToolTip = tooltip };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { Text = label, Style = (Style)FindResource("LabelTextStyle") };
        var swatch = new Button { Tag = property, Width = 30, Height = 26, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Open color picker", BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) };
        swatch.Click += VisualColorButton_Click; SetColorSwatch(swatch, current);
        var box = new TextBox { Text = current, Tag = property, Width = 92, Height = 26, FontSize = 10.5, CharacterCasing = CharacterCasing.Upper, MaxLength = 9 };
        box.LostFocus += VisualColor_LostFocus; box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { VisualColor_LostFocus(s, e); e.Handled = true; } };
        Grid.SetColumn(swatch, 1); Grid.SetColumn(box, 2);
        row.Children.Add(text); row.Children.Add(swatch); row.Children.Add(box);
        _visualColorInputs[property] = box; _visualColorButtons[property] = swatch;
        return Register(body, row, label + " " + tooltip + " color", property);
    }

    private SettingRow TrackPaletteRow(Panel body)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        stack.Children.Add(new TextBlock { Text = "Track colors (tracks 9+ repeat the palette)", Style = (Style)FindResource("LabelTextStyle"), Margin = new Thickness(0, 0, 0, 6) });
        var wrap = new WrapPanel();
        _trackPaletteSwatches.Clear();
        for (var i = 0; i < 8; i++)
        {
            var swatch = new Button { Tag = i, Width = 40, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6), Content = (i + 1).ToString(), FontSize = 10, Foreground = Brushes.White, ToolTip = $"Color of track {i + 1}" };
            swatch.Click += TrackPaletteButton_Click; SetColorSwatch(swatch, _visualSettings.TrackColors[i]);
            wrap.Children.Add(swatch); _trackPaletteSwatches.Add(swatch);
        }
        stack.Children.Add(wrap);
        return Register(body, stack, "track colors palette per track", nameof(PianoVisualSettings.TrackColors));
    }

    private SettingRow ButtonRow(Panel body, params (string Text, RoutedEventHandler Click)[] buttons)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        foreach (var (text, click) in buttons)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 4) }; button.Click += click; wrap.Children.Add(button);
        }
        return Register(body, wrap, string.Join(' ', buttons.Select(b => b.Text)));
    }

    private SettingRow Note(Panel body, string text)
        => Register(body, new TextBlock { Text = text, Style = (Style)FindResource("MutedTextStyle"), Margin = new Thickness(0, 4, 0, 4) }, text);

    private sealed record ChoiceOption(string Value, string Caption);
    private static System.Reflection.PropertyInfo Prop(string property) => typeof(PianoVisualSettings).GetProperty(property) ?? throw new InvalidOperationException($"Unknown visual setting '{property}'.");

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
        MarkModified(); RefreshDependentRows();
        var label = check.Content as string ?? check.Tag as string ?? "Setting";
        ApplyVisualSettings($"{label} {(check.IsChecked == true ? "on" : "off")}");
    }

    private void VisualSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _loadingVisualSettings || sender is not Slider slider || slider.Tag is not string property) return;
        Prop(property).SetValue(_visualSettings, slider.Value);
        if (_visualValueBoxes.TryGetValue(property, out var box) && !box.IsKeyboardFocused) box.Text = FormatSetting(property, slider.Value);
        MarkModified();
        if (property is nameof(PianoVisualSettings.RecordingFrameRate)) UpdateRecordingInfo();
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
        MarkModified(); RefreshDependentRows(); RebuildTrackList();
        if (property is nameof(PianoVisualSettings.RecordingResolution)) UpdateRecordingInfo();
        var what = property switch { nameof(PianoVisualSettings.NoteStyle) => "Note style", nameof(PianoVisualSettings.NoteDirection) => "Note direction", nameof(PianoVisualSettings.ColorMode) => "Color mode", nameof(PianoVisualSettings.KeyboardStyle) => "Keyboard style", nameof(PianoVisualSettings.BackgroundMode) => "Background mode", _ => "Setting" };
        ApplyVisualSettings(what + " updated");
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
        MarkModified();
        ApplyVisualSettings(property == nameof(PianoVisualSettings.HaloColor) ? "Halo color applied" : "Color applied");
    }

    private void TrackPaletteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        var picker = new ColorPickerWindow(_visualSettings.TrackColors[index]) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedHex is not { } selected) return;
        _visualSettings.TrackColors[index] = selected;
        SetColorSwatch(_trackPaletteSwatches[index], selected);
        RebuildTrackList(); MarkModified();
        ApplyVisualSettings($"Track {index + 1} color applied");
    }

    private void MarkModified()
    {
        if (_loadingVisualSettings) return;
        _visualSettings.PresetModified = true;
        UpdatePresetLabels();
    }

    private void RefreshDependentRows()
    {
        var query = SettingsSearchBox?.Text.Trim().ToLowerInvariant() ?? "";
        foreach (var row in _settingRows)
        {
            var visible = (row.VisibleWhen?.Invoke() ?? true) && (query.Length == 0 || row.SearchText.Contains(query, StringComparison.Ordinal));
            row.Element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var card in _settingCards)
        {
            var anyVisible = _settingRows.Any(r => ReferenceEquals(r.Card, card) && r.Element.Visibility == Visibility.Visible);
            card.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SettingsSearchHint is not null) SettingsSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
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
    }

    private void UpdatePresetLabels()
    {
        var name = string.IsNullOrWhiteSpace(_visualSettings.PresetName) ? "Custom" : _visualSettings.PresetName;
        if (PresetNameLabel is not null) PresetNameLabel.Text = $"Preset · {name}{(_visualSettings.PresetModified ? " · modified" : "")}";
        if (HeaderPresetLabel is not null) HeaderPresetLabel.Text = _visualSettings.PresetModified ? name + " *" : name;
    }

    private void ApplyVisualSettings(string status, bool reloadBackground = false)
    {
        _visualSettings.Clamp();
        Stage.SetVisualSettings(_visualSettings, reloadBackground);
        ApplyChromeTheme();
        if (reloadBackground && Stage.BackgroundLoadError is { } error)
        {
            SettingsSaveLabel.Text = "Background image failed to load";
            MessageBox.Show(this, $"Keyflow could not load this image. Choose a PNG, JPEG, BMP, GIF or TIFF file.\n\n{error}", "Background image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else SettingsSaveLabel.Text = status;
        _settingsSaveTimer.Stop(); _settingsSaveTimer.Start();
    }

    private void SaveVisualSettings_Click(object sender, RoutedEventArgs e) => SaveVisualSettings();
    private void SaveVisualSettings()
    {
        try { PianoVisualSettingsStore.Save(_visualSettings); SettingsSaveLabel.Text = "Saved to this computer"; }
        catch (Exception ex) { SettingsSaveLabel.Text = "Save failed"; if (!_closing) MessageBox.Show(this, ex.Message, "Stage settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ChooseStageBackground(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*", Title = "Choose a piano visualizer background", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        _visualSettings.BackgroundImagePath = dialog.FileName; _visualSettings.BackgroundMode = "Image"; _visualSettings.ShowBackground = true;
        RefreshSettingControls(); MarkModified();
        ApplyVisualSettings("Background image applied", reloadBackground: true);
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
    }

    private void StyleQuick_Click(object sender, RoutedEventArgs e) { SettingsTabs.SelectedIndex = 0; OpenSettingsPanel(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void ResetPage_Click(object sender, RoutedEventArgs e)
    {
        var page = SettingsPageHost(SettingsTabs.SelectedIndex);
        if (page is null) { SettingsSaveLabel.Text = "This page has no visual settings to reset"; return; }
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
            _presets.Clear(); _presets.AddRange(VisualPresets.BuiltIn); _presets.AddRange(VisualPresetStore.Default.LoadUserPresets());
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
        text.Children.Add(new TextBlock { Text = preset.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        text.Children.Add(new TextBlock { Text = preset.Description, Style = (Style)FindResource("MutedTextStyle"), FontSize = 9.5, Margin = new Thickness(0, 2, 0, 0) });
        var kind = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center, Background = (Brush)FindResource(preset.BuiltIn ? "AccentSoftBrush" : "ControlHoverBrush"), Margin = new Thickness(10, 0, 0, 0) };
        kind.Child = new TextBlock { Text = preset.BuiltIn ? "BUILT-IN" : "USER", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("MutedTextBrush") };
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

    /// <summary>Embers-style miniature: a tiny keyboard with the preset's note palette falling onto it.</summary>
    private ImageSource PresetThumbnail(VisualPreset preset)
    {
        var s = preset.Settings;
        var key = $"{preset.Name}|{s.ColorMode}|{s.NoteColorStart}|{s.NoteColorEnd}|{s.HaloColor}|{s.NoteStyle}";
        if (_presetThumbs.TryGetValue(key, out var cached)) return cached;
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
        PresetDescriptionLabel.Text = preset is null ? "Select a preset to preview its description." : preset.Description + (preset.BuiltIn ? "" : $"  ·  {preset.FilePath}");
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
        _visualSettings.CopyFrom(preset.Settings, keepBackgroundImage: true);
        _visualSettings.PresetName = preset.Name; _visualSettings.PresetModified = false;
        if (_visualSettings.BackgroundMode == "Image" && string.IsNullOrWhiteSpace(_visualSettings.BackgroundImagePath)) _visualSettings.BackgroundMode = "Solid";
        RefreshSettingControls();
        ApplyVisualSettings($"Preset “{preset.Name}” applied");
    }

    private void SavePresetAs_Click(object sender, RoutedEventArgs e)
    {
        var suggested = _visualSettings.PresetModified || VisualPresets.FindBuiltIn(_visualSettings.PresetName) is not null ? "My " + _visualSettings.PresetName : _visualSettings.PresetName;
        var prompt = new TextPromptWindow("Save preset", "Name for this look. Existing user presets with the same name are replaced.", suggested) { Owner = this };
        if (prompt.ShowDialog() != true || prompt.Result is not { } name) return;
        try
        {
            if (VisualPresets.FindBuiltIn(name) is not null) { MessageBox.Show(this, $"“{name}” is a built-in preset. Choose another name.", "Save preset", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var saved = VisualPresetStore.Default.Save(name, _visualSettings);
            _visualSettings.PresetName = saved.Name; _visualSettings.PresetModified = false;
            LoadPresetList(saved.Name); UpdatePresetLabels();
            ApplyVisualSettings($"Preset “{saved.Name}” saved");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save preset", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPreset is not { BuiltIn: false } preset) return;
        if (MessageBox.Show(this, $"Delete the preset “{preset.Name}”?", "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        VisualPresetStore.Default.Delete(preset);
        LoadPresetList(VisualPresets.DefaultPresetName);
        SettingsSaveLabel.Text = $"Preset “{preset.Name}” deleted";
    }

    private void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Keyflow preset (*.json)|*.json|All files (*.*)|*.*", Title = "Import a Keyflow preset" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = VisualPresetStore.Import(dialog.FileName);
            var saved = VisualPresetStore.Default.Save(imported.Name, imported.Settings);
            LoadPresetList(saved.Name);
            ApplyPreset(saved);
        }
        catch (Exception ex) { MessageBox.Show(this, $"This file is not a valid Keyflow preset.\n{ex.Message}", "Import preset", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Keyflow preset (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = VisualPresetStore.SanitizeName(_visualSettings.PresetName) + ".json", Title = "Export the current look" };
        if (dialog.ShowDialog(this) != true) return;
        try { VisualPresetStore.Export(_visualSettings, dialog.FileName); SettingsSaveLabel.Text = "Preset exported"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export preset", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
            TrackListHost.Children.Add(new TextBlock { Text = "Open a MIDI file to see its tracks here.", Style = (Style)FindResource("MutedTextStyle") });
            return;
        }
        foreach (var track in tracks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var paletteIndex = ((track % 8) + 8) % 8;
            var swatch = new Button { Tag = paletteIndex, Width = 26, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0, 0, 10, 0), ToolTip = _visualSettings.ColorMode == "PerTrack" ? "Change the color of this track" : "Track color (used by the “Per MIDI track” color mode)" };
            SetColorSwatch(swatch, _visualSettings.TrackColors[paletteIndex]); swatch.Click += TrackPaletteButton_Click;
            var count = _allNotes.Count(n => n.Track == track);
            var name = _trackNames.TryGetValue(track, out var trackName) ? $"Track {track + 1} · {trackName}" : $"Track {track + 1}";
            var check = new CheckBox { Content = $"{name}   ({count} notes)", Tag = track, IsChecked = !_mutedTracks.Contains(track), ToolTip = "Audible and visible when on; muted tracks are hidden from the stage and the score.", HorizontalAlignment = HorizontalAlignment.Stretch };
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
        RecordingInfoLabel.Text = $"Next recording: {width} × {height} @ {_visualSettings.RecordingFrameRate:0} fps · AVI (MJPEG when a codec is installed, raw BGR otherwise) · audio is not captured.";
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
}
