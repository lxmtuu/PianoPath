using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PianoPath;

/// <summary>Serializable, user-editable live-stage and note rendering controls.</summary>
internal sealed class PianoVisualSettings
{
    // ---- Layers -------------------------------------------------------------------------------------
    public bool ShowBackground { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
    public bool ShowEmbers { get; set; } = true;
    public bool ShowHalo { get; set; } = true;
    public bool ShowFlame { get; set; } = true;
    public bool ShowKeys { get; set; } = true;
    public bool ShowWatermark { get; set; } = false;
    public bool ShowCounter { get; set; } = false;
    public bool ShowFps { get; set; } = false;
    public bool AnimateKeys { get; set; } = true;
    public bool Notes3D { get; set; } = true;
    public bool BackgroundGuide { get; set; }
    public bool BackgroundGradient { get; set; }
    public bool ShowStars { get; set; }
    public bool ShowWisps { get; set; } = false;
    public bool ShowImpactRings { get; set; } = true;
    public bool ShowLightBeams { get; set; } = true;
    public bool ShowNoteLabels { get; set; } = false;
    public bool ShowKeyFelt { get; set; } = false;
    public bool ShowKeyShadow { get; set; } = true;
    public bool ShowDust { get; set; } = true;
    public bool ShowGrid { get; set; } = false;
    public bool ShowNoteShadow { get; set; } = true;
    public bool ShowKeyReflection { get; set; } = true;
    public bool ShowFallboard { get; set; } = true;
    public int BackgroundAppearanceVersion { get; set; }

    // ---- Style / preset -----------------------------------------------------------------------------
    /// <summary>Name of the preset the current values were derived from (informational only).</summary>
    public string PresetName { get; set; } = "Neon Violet";
    /// <summary>True once any value was edited after the preset was applied.</summary>
    public bool PresetModified { get; set; }

    // ---- Colors -------------------------------------------------------------------------------------
    /// <summary>Gradient, PerHand, PerTrack, RainbowPitch or RainbowTime.</summary>
    public string ColorMode { get; set; } = "Gradient";
    public string Palette { get; set; } = "Spectrum";
    public string NoteColorStart { get; set; } = "#43E6FF";
    public string NoteColorMid { get; set; } = "#7C6CFF";
    public string NoteColorEnd { get; set; } = "#D95EFF";
    /// <summary>Three-stop gradients read richer than a plain two-stop ramp; turn it off for a hard start-to-end blend.</summary>
    public bool ShowMidStop { get; set; } = true;
    public string LeftHandColor { get; set; } = "#3FA9FF";
    public string RightHandColor { get; set; } = "#FF6FD8";
    public string HaloColor { get; set; } = "#C66EFF";
    /// <summary>Halo (single color), Rainbow (pitch rainbow through the halo color) or Note (color of the loudest sounding note).</summary>
    public string HaloTintMode { get; set; } = "Halo";
    /// <summary>Thickness of the crisp core line.</summary>
    public double HaloThickness { get; set; } = 45;
    /// <summary>Brightness of the line and its colored bleed.</summary>
    public double HaloIntensity { get; set; } = 70;
    /// <summary>How far the soft glow reaches above and below the line.</summary>
    public double HaloGlowSize { get; set; } = 55;
    /// <summary>Extra brightness while keys sound, so the line breathes with the music.</summary>
    public double HaloPulse { get; set; } = 45;
    public string PressedKeyColor { get; set; } = "#F782FF";
    public string KeyFeltColor { get; set; } = "#C41C4A";
    public string BackgroundColor { get; set; } = "#000000";
    public List<string> TrackColors { get; set; } = ["#43E6FF", "#FF6FD8", "#FFD166", "#7CFF6B", "#FF7A59", "#8C7BFF", "#5CF2E8", "#FF4D8D"];
    public double HandSplitPitch { get; set; } = 60;
    public double RainbowSpeed { get; set; } = 30;
    /// <summary>Global hue rotation in degrees applied to every note, glow and light.</summary>
    public double HueShift { get; set; }
    /// <summary>Selective saturation: lifts muted colors more than already vivid ones.</summary>
    public double Vibrance { get; set; } = 45;
    /// <summary>Warm (+) or cool (-) grade applied on top of the color controls.</summary>
    public double ColorTemperature { get; set; }

    // ---- Note shape ---------------------------------------------------------------------------------
    /// <summary>Solid, Neon (hollow glowing outline), Glass or Fire (burning texture).</summary>
    public string NoteStyle { get; set; } = "Neon";
    public double NoteWidth { get; set; } = 80;
    public double NoteMinLength { get; set; } = 16;
    public double NoteGap { get; set; } = 2;
    public double NoteHeadGlow { get; set; } = 40;
    public double NoteTexture { get; set; } = 60;
    public double NoteTint { get; set; } = 78;
    public double NoteGlow { get; set; } = 86;
    public double NoteEdge { get; set; } = 88;
    public double NoteRefraction { get; set; } = 35;
    public double NoteRoundness { get; set; } = 70;
    public double NoteEdgeWidth { get; set; } = 55;
    public double NoteFallSpeed { get; set; } = 550;
    /// <summary>Depth shading that turns a flat bar into a lit 3D block.</summary>
    public double NoteDepth { get; set; } = 58;
    /// <summary>Glossy specular streak across the bar face.</summary>
    public double NoteSpecular { get; set; } = 55;
    /// <summary>Bright rim on both long edges, as if lit from behind.</summary>
    public double NoteRimLight { get; set; } = 42;
    /// <summary>Comet trail left behind a falling note.</summary>
    public double NoteTrail { get; set; } = 34;
    /// <summary>Opacity of the drop shadow under every note.</summary>
    public double NoteShadowStrength { get; set; } = 58;
    /// <summary>How far the shadow is offset from the note (pixels, down-right).</summary>
    public double NoteShadowDistance { get; set; } = 16;
    /// <summary>Softness of the shadow.</summary>
    public double NoteShadowBlur { get; set; } = 60;

    // ---- Particles: sparks --------------------------------------------------------------------------
    public double EmitterSize { get; set; } = 24;
    public double Spiral { get; set; } = 30;
    public double ParticleSpeed { get; set; } = 100;
    public double ParticleAmount { get; set; } = 24;
    public double ParticleVelocity { get; set; } = 150;
    public double ParticleRandomness { get; set; } = 38;
    public double ParticleSpread { get; set; } = 72;
    public double ParticleResponse { get; set; } = 55;
    public double ParticleLife { get; set; } = .65;
    public double ParticleLifeRandomness { get; set; } = 45;
    public double ParticleSize { get; set; } = 2.2;
    public double ParticleSizeRandomness { get; set; } = 80;
    public double ParticleGlow { get; set; } = 85;
    public double Gravity { get; set; } = 290;
    public double Drag { get; set; } = 26;
    public double VectorField { get; set; } = 36;
    public double FieldScale { get; set; } = 100;
    public double EvolutionSpeed { get; set; } = 100;
    public double PhysicsTimeFactor { get; set; } = 100;

    // ---- Particles: wisps, flames, rings ------------------------------------------------------------
    public double WispAmount { get; set; } = 45;
    public double WispSpeed { get; set; } = 170;
    public double WispHeight { get; set; } = 55;
    public double WispWidth { get; set; } = 40;
    public double WispTurbulence { get; set; } = 55;
    public double WispGlow { get; set; } = 80;
    public double FlameIntensity { get; set; } = 70;
    public double FlameHeight { get; set; } = 60;
    /// <summary>Warm (classic fire) or Note (flame takes the note color).</summary>
    public string FlameColorMode { get; set; } = "Warm";
    public double RingSize { get; set; } = 50;

    // ---- Keyboard -----------------------------------------------------------------------------------
    /// <summary>Classic, Studio (3D) or Glass.</summary>
    public string KeyboardStyle { get; set; } = "Studio";
    public double KeyboardScale { get; set; } = 100;
    /// <summary>Note (pressed keys take the note color) or Fixed (PressedKeyColor).</summary>
    public string PressedKeyColorMode { get; set; } = "Note";
    /// <summary>None, C (octave labels) or All.</summary>
    public string KeyLabels { get; set; } = "C";
    public double KeyLighting { get; set; } = 32;
    public double KeyGlowRadius { get; set; } = 50;
    public double KeyOverhang { get; set; } = 18;
    public double KeyPressDepth { get; set; } = 40;
    /// <summary>Glossy sheen and reflection strength on the key faces.</summary>
    public double KeyGloss { get; set; } = 55;
    /// <summary>Beveled edges plus ambient occlusion in the key gaps.</summary>
    public double KeyBevel { get; set; } = 60;
    /// <summary>Contact shadow a black key casts on the white keys beneath it.</summary>
    public double KeyContactShadow { get; set; } = 55;

    // ---- Background & camera ------------------------------------------------------------------------
    /// <summary>Solid, Image or ChromaGreen (pure green stage for OBS chroma keying).</summary>
    public string BackgroundMode { get; set; } = "Solid";
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundDim { get; set; } = 30;
    public double Vignette { get; set; } = 25;
    public double StarDensity { get; set; } = 50;
    public double HorizonGlow { get; set; } = 35;
    public double BeamIntensity { get; set; } = 45;
    public double CameraParallax { get; set; } = 24;
    public double CameraZoom { get; set; } = 100;
    public double CameraOffset { get; set; } = 50;
    public double Saturation { get; set; } = 100;
    public double Contrast { get; set; } = 100;
    public double BloomIntensity { get; set; } = 65;
    public double BloomSize { get; set; } = 62;
    /// <summary>How bright a pixel must be before it feeds the bloom pass.</summary>
    public double BloomThreshold { get; set; } = 45;
    /// <summary>Horizontal lens streaks smeared out of the brightest pixels.</summary>
    public double AnamorphicStreaks { get; set; } = 22;
    /// <summary>Color fringing at the edges of bright shapes.</summary>
    public double ChromaticAberration { get; set; } = 16;
    /// <summary>Animated film grain over the whole stage.</summary>
    public double FilmGrain { get; set; } = 10;
    /// <summary>CRT-style horizontal scanlines.</summary>
    public double Scanlines { get; set; }
    /// <summary>Cinematic letterbox bars, as a percentage of the stage height.</summary>
    public double CinematicBars { get; set; }
    /// <summary>Floating dust motes drifting through the light.</summary>
    public double DustDensity { get; set; } = 45;
    /// <summary>Perspective grid receding behind the keyboard.</summary>
    public double GridIntensity { get; set; } = 30;

    // ---- Recording ----------------------------------------------------------------------------------
    /// <summary>Window, 720p or 1080p.</summary>
    public string RecordingResolution { get; set; } = "Window";
    public double RecordingFrameRate { get; set; } = 30;

    internal static readonly string[] ColorModes = ["Gradient", "PerHand", "PerTrack", "RainbowPitch", "RainbowTime"];
    internal static readonly string[] Palettes = ["Spectrum", "Aurora", "Violet", "Sunset", "Cyberpunk", "Candy", "Emerald", "Ocean", "Fire", "Mono", "Custom"];
    internal static readonly string[] HaloTintModes = ["Halo", "Rainbow", "Note"];
    internal static readonly string[] NoteStyles = ["Solid", "Neon", "Glass", "Fire"];
    internal static readonly string[] FlameColorModes = ["Warm", "Note"];
    internal static readonly string[] KeyboardStyles = ["Classic", "Studio", "Glass"];
    internal static readonly string[] PressedKeyColorModes = ["Note", "Fixed"];
    internal static readonly string[] KeyLabelModes = ["None", "C", "All"];
    internal static readonly string[] BackgroundModes = ["Solid", "Image", "ChromaGreen"];
    internal static readonly string[] RecordingResolutions = ["Window", "720p", "1080p"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    internal static PianoVisualSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<PianoVisualSettings>(json, JsonOptions) ?? new PianoVisualSettings();
        settings.ApplyMigrations();
        settings.Clamp();
        return settings;
    }

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deep copy used by the preset system so a preset can be applied without sharing list instances.</summary>
    internal PianoVisualSettings Clone() => FromJson(ToJson());

    /// <summary>Copies every value from <paramref name="source"/> into this instance, so the renderer keeps its existing reference.</summary>
    internal void CopyFrom(PianoVisualSettings source, bool keepBackgroundImage = true)
    {
        var image = BackgroundImagePath;
        foreach (var property in typeof(PianoVisualSettings).GetProperties())
        {
            if (!property.CanWrite || !property.CanRead) continue;
            var value = property.GetValue(source);
            property.SetValue(this, value is List<string> list ? new List<string>(list) : value);
        }
        if (keepBackgroundImage) BackgroundImagePath = image;
        ApplyMigrations(); Clamp();
    }

    internal void Clamp()
    {
        NoteTint = Math.Clamp(NoteTint, 0, 100); NoteGlow = Math.Clamp(NoteGlow, 0, 200); NoteEdge = Math.Clamp(NoteEdge, 0, 200);
        NoteRefraction = Math.Clamp(NoteRefraction, 0, 100); NoteRoundness = Math.Clamp(NoteRoundness, 0, 100); NoteEdgeWidth = Math.Clamp(NoteEdgeWidth, 0, 100);
        NoteFallSpeed = Math.Clamp(NoteFallSpeed, 100, 1000); EmitterSize = Math.Clamp(EmitterSize, 0, 100); Spiral = Math.Clamp(Spiral, 0, 100);
        NoteWidth = Math.Clamp(NoteWidth, 30, 100); NoteMinLength = Math.Clamp(NoteMinLength, 4, 60); NoteGap = Math.Clamp(NoteGap, 0, 12);
        NoteHeadGlow = Math.Clamp(NoteHeadGlow, 0, 100); NoteTexture = Math.Clamp(NoteTexture, 0, 100);
        ParticleSpeed = Math.Clamp(ParticleSpeed, 0, 300); ParticleAmount = Math.Clamp(ParticleAmount, 0, 120); ParticleVelocity = Math.Clamp(ParticleVelocity, 0, 800);
        ParticleRandomness = Math.Clamp(ParticleRandomness, 0, 100); ParticleSpread = Math.Clamp(ParticleSpread, 0, 100); ParticleResponse = Math.Clamp(ParticleResponse, 0, 100);
        ParticleLife = Math.Clamp(ParticleLife, .05, 3); ParticleLifeRandomness = Math.Clamp(ParticleLifeRandomness, 0, 100); ParticleSize = Math.Clamp(ParticleSize, .2, 16);
        ParticleSizeRandomness = Math.Clamp(ParticleSizeRandomness, 0, 100); ParticleGlow = Math.Clamp(ParticleGlow, 0, 200); Gravity = Math.Clamp(Gravity, -600, 1200);
        Drag = Math.Clamp(Drag, 0, 100); VectorField = Math.Clamp(VectorField, 0, 1000); FieldScale = Math.Clamp(FieldScale, 10, 300); EvolutionSpeed = Math.Clamp(EvolutionSpeed, 0, 400);
        PhysicsTimeFactor = Math.Clamp(PhysicsTimeFactor, 10, 300);
        NoteDepth = Math.Clamp(NoteDepth, 0, 100); NoteSpecular = Math.Clamp(NoteSpecular, 0, 100); NoteRimLight = Math.Clamp(NoteRimLight, 0, 100);
        NoteTrail = Math.Clamp(NoteTrail, 0, 100); NoteShadowStrength = Math.Clamp(NoteShadowStrength, 0, 100); NoteShadowBlur = Math.Clamp(NoteShadowBlur, 0, 100);
        NoteShadowDistance = Math.Clamp(NoteShadowDistance, 0, 60);
        HueShift = Math.Clamp(HueShift, -180, 180); Vibrance = Math.Clamp(Vibrance, 0, 100); ColorTemperature = Math.Clamp(ColorTemperature, -100, 100);
        HaloThickness = Math.Clamp(HaloThickness, 0, 100); HaloIntensity = Math.Clamp(HaloIntensity, 0, 100); HaloGlowSize = Math.Clamp(HaloGlowSize, 0, 100); HaloPulse = Math.Clamp(HaloPulse, 0, 100);
        KeyGloss = Math.Clamp(KeyGloss, 0, 100); KeyBevel = Math.Clamp(KeyBevel, 0, 100); KeyContactShadow = Math.Clamp(KeyContactShadow, 0, 100);
        BloomThreshold = Math.Clamp(BloomThreshold, 0, 100); AnamorphicStreaks = Math.Clamp(AnamorphicStreaks, 0, 100); ChromaticAberration = Math.Clamp(ChromaticAberration, 0, 100);
        FilmGrain = Math.Clamp(FilmGrain, 0, 100); Scanlines = Math.Clamp(Scanlines, 0, 100); CinematicBars = Math.Clamp(CinematicBars, 0, 25);
        DustDensity = Math.Clamp(DustDensity, 0, 100); GridIntensity = Math.Clamp(GridIntensity, 0, 100);
        WispAmount = Math.Clamp(WispAmount, 0, 150); WispSpeed = Math.Clamp(WispSpeed, 20, 600); WispHeight = Math.Clamp(WispHeight, 5, 100);
        WispWidth = Math.Clamp(WispWidth, 0, 100); WispTurbulence = Math.Clamp(WispTurbulence, 0, 100); WispGlow = Math.Clamp(WispGlow, 0, 200);
        FlameIntensity = Math.Clamp(FlameIntensity, 0, 100); FlameHeight = Math.Clamp(FlameHeight, 0, 100); RingSize = Math.Clamp(RingSize, 0, 100);
        KeyboardScale = Math.Clamp(KeyboardScale, 60, 140); KeyLighting = Math.Clamp(KeyLighting, 0, 100); KeyGlowRadius = Math.Clamp(KeyGlowRadius, 0, 100);
        KeyOverhang = Math.Clamp(KeyOverhang, 0, 100); KeyPressDepth = Math.Clamp(KeyPressDepth, 0, 100);
        HandSplitPitch = Math.Clamp(Math.Round(HandSplitPitch), 21, 108); RainbowSpeed = Math.Clamp(RainbowSpeed, 0, 100);
        CameraParallax = Math.Clamp(CameraParallax, 0, 100); CameraZoom = Math.Clamp(CameraZoom, 65, 150);
        CameraOffset = Math.Clamp(CameraOffset, 0, 100); BackgroundDim = Math.Clamp(BackgroundDim, 0, 100); Saturation = Math.Clamp(Saturation, 0, 200);
        Contrast = Math.Clamp(Contrast, 0, 200); BloomIntensity = Math.Clamp(BloomIntensity, 0, 150); BloomSize = Math.Clamp(BloomSize, 0, 150);
        Vignette = Math.Clamp(Vignette, 0, 100); StarDensity = Math.Clamp(StarDensity, 0, 100); HorizonGlow = Math.Clamp(HorizonGlow, 0, 100); BeamIntensity = Math.Clamp(BeamIntensity, 0, 100);
        RecordingFrameRate = Math.Clamp(Math.Round(RecordingFrameRate), 15, 60);
        if (!Palettes.Contains(Palette)) Palette = "Spectrum";
        if (!HaloTintModes.Contains(HaloTintMode)) HaloTintMode = "Halo";
        if (!ColorModes.Contains(ColorMode)) ColorMode = "Gradient";
        if (!NoteStyles.Contains(NoteStyle)) NoteStyle = "Neon";
        if (!FlameColorModes.Contains(FlameColorMode)) FlameColorMode = "Warm";
        if (!KeyboardStyles.Contains(KeyboardStyle)) KeyboardStyle = "Studio";
        if (!PressedKeyColorModes.Contains(PressedKeyColorMode)) PressedKeyColorMode = "Note";
        if (!KeyLabelModes.Contains(KeyLabels)) KeyLabels = "C";
        if (!BackgroundModes.Contains(BackgroundMode)) BackgroundMode = "Solid";
        if (!RecordingResolutions.Contains(RecordingResolution)) RecordingResolution = "Window";
        TrackColors ??= [];
        var defaults = new List<string> { "#43E6FF", "#FF6FD8", "#FFD166", "#7CFF6B", "#FF7A59", "#8C7BFF", "#5CF2E8", "#FF4D8D" };
        for (var i = 0; i < 8; i++) if (TrackColors.Count <= i) TrackColors.Add(defaults[i]); else if (string.IsNullOrWhiteSpace(TrackColors[i])) TrackColors[i] = defaults[i];
        if (TrackColors.Count > 8) TrackColors.RemoveRange(8, TrackColors.Count - 8);
        PresetName = string.IsNullOrWhiteSpace(PresetName) ? "Custom" : PresetName.Trim();
        if (PresetName.Length > 40) PresetName = PresetName[..40];
    }

    internal void ApplyMigrations()
    {
        if (BackgroundAppearanceVersion < 1)
        {
            // Previous releases enabled decorative purple sky layers by default. Keep the
            // user's image path and all other preferences, but make the default stage black.
            BackgroundGradient = false;
            BackgroundGuide = false;
            ShowStars = false;
            BackgroundAppearanceVersion = 1;
        }
        if (BackgroundAppearanceVersion < 2)
        {
            // Version 2 introduced the background mode; a selected image keeps working.
            if (!string.IsNullOrWhiteSpace(BackgroundImagePath)) BackgroundMode = "Image";
            BackgroundAppearanceVersion = 2;
        }
    }
}

internal static class PianoVisualSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal static string SettingsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keyflow");
    internal static string SettingsPath => Path.Combine(SettingsDirectory, "visual-settings.json");

    internal static PianoVisualSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath)) return PianoVisualSettings.FromJson(File.ReadAllText(SettingsPath));
            var settings = new PianoVisualSettings(); settings.ApplyMigrations(); return settings;
        }
        catch { return new PianoVisualSettings(); }
    }

    internal static void Save(PianoVisualSettings settings)
    {
        settings.ApplyMigrations(); settings.Clamp();
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, true);
    }
}
