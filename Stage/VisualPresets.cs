using System.IO;

namespace PianoPath;

/// <summary>A named look for the stage. Built-in presets are generated in code; user presets are JSON files.</summary>
/// <param name="Thumbnail">Base64 PNG of the look as it was rendered when the preset was saved, when the
/// file carries one. A preset without a picture is drawn from its own settings instead.</param>
internal sealed record VisualPreset(string Name, string Description, bool BuiltIn, PianoVisualSettings Settings, string? FilePath = null, string? Thumbnail = null, bool Community = false);

/// <summary>Curated stage looks plus a JSON store for user-made presets.</summary>
internal static class VisualPresets
{
    /// <summary>The shipped first-run/reset look. Existing user settings are never rewritten to this preset.</summary>
    internal const string DefaultPresetName = "Concert Gold";

    internal static IReadOnlyList<VisualPreset> BuiltIn { get; } =
    [
        new(DefaultPresetName, "A restrained recital look: warm brass and sea-glass hands, a satin keyboard and one soft stage light.", true, ConcertGold()),
        new("Neon Violet", "Violet-to-cyan luminous notes, concise trails and tightly bounded impact glow for streams.", true, NeonViolet()),
        new("Two Hands", "Opaque blue and rose hand colors, octave labels and minimal motion for learning.", true, TwoHands()),
        new("Classic Roll", "Quiet, solid track colors with no particles or animated background for clear long-form viewing.", true, ClassicRoll()),
        new("Green Screen", "Clean chroma green with opaque contrasting notes and no decorative light spill.", true, GreenScreen()),
        new("Inferno", "Molten amber-red notes, sparse embers and a slow, low-density ember haze.", true, Inferno()),
        new("Ice Crystal", "Cool glass notes, precise cyan edges and a restrained snowfall with limited refraction.", true, IceCrystal()),
        new("Sakura Nocturne", "Rose and ivory notes, a few drifting petals and quiet indigo ambience.", true, SakuraNocturne()),
        new("Moonlight Sonata", "Silver-blue glass bars, a sparse starfield and soft moonlit keyboard lighting.", true, MoonlightSonata()),
        new("Aurora Rainbow", "Stable rainbow-per-pitch notes beneath a slow aurora, without rapidly cycling note colors.", true, AuroraRainbow()),
        new("Ocean Depths", "Teal glass notes, sparse rain, localized key ripples and a slow deep-water flow.", true, OceanDepths()),
        new("Galaxy Voyage", "Violet-to-cyan meteoric trails and a low-density nebula with sparse impact accents.", true, GalaxyVoyage()),
        new("Electric Storm", "Cyan-white electric notes, short speed lines, local lightning and restrained held-key arcs.", true, ElectricStorm()),
        new("Retro Arcade", "Square-edged retro bars, a limited track palette, a quiet geometric grid and crisp bounces.", true, RetroArcade()),
    ];

    internal static VisualPreset? FindBuiltIn(string name) => BuiltIn.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A fresh copy of the complete first-run look, including the preset name and shell theme.</summary>
    internal static PianoVisualSettings CreateDefaultSettings() => FindBuiltIn(DefaultPresetName)?.Settings.Clone() ?? new PianoVisualSettings();

    /// <summary>
    /// The caption to print for a preset name. A built-in name is translated when the language has an
    /// entry for it; a name the user typed is returned untouched, because it is their own text.
    /// The <em>stored</em> name is always the English one — see <see cref="VisualPreset.Name"/>.
    /// </summary>
    internal static string DisplayName(string name) =>
        BuiltIn.Any(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)) ? Loc.T(name) : name;

    /// <summary>
    /// The shared baseline deliberately disables ornamental layers. Every curated look opts into only the
    /// material, lifecycle effects and atmosphere that define it; this prevents a new model default from
    /// silently making all presets brighter or busier.
    /// </summary>
    private static PianoVisualSettings Base(string name)
    {
        var s = new PianoVisualSettings { PresetName = name };
        s.ApplyMigrations();
        s.BackgroundAppearanceVersion = 2;
        s.ShowBackground = true; s.ShowNotes = true; s.ShowKeys = true; s.ShowKeyShadow = true;
        s.BackgroundMode = "Solid"; s.BackgroundColor = "#080B12"; s.BackgroundGradient = false; s.BackgroundGuide = false; s.BackgroundDim = 28;
        s.BackgroundMotion = "None"; s.BackgroundMotionAmount = 0; s.BackgroundMotionSpeed = 18; s.BackgroundMotionColor = "#8A9AAD";
        s.ShowEmbers = false; s.ShowHalo = true; s.ShowFlame = false; s.ShowWisps = false; s.ShowImpactRings = false; s.ShowLightBeams = false;
        s.ShowStars = false; s.ShootingStars = false; s.ShowPetals = false; s.ShowNoteLabels = false; s.ShowKeyFelt = false;
        s.PetalAmount = 0; s.StarDensity = 0; s.ShootingStarsAmount = 0;
        s.NoteStyle = "Solid"; s.ColorMode = "Gradient"; s.Palette = "Custom";
        s.NoteColorStart = "#63D7DE"; s.NoteColorEnd = "#F3C56E";
        s.LeftHandColor = "#63D7DE"; s.RightHandColor = "#F3C56E"; s.HandSplitPitch = 60;
        s.NoteWidth = 84; s.NoteMinLength = 12; s.NoteGap = 2; s.NoteFallSpeed = 550;
        s.NoteTint = 86; s.NoteGlow = 44; s.NoteEdge = 68; s.NoteEdgeWidth = 30; s.NoteRoundness = 38; s.NoteHeadGlow = 22; s.NoteTexture = 8; s.NoteRefraction = 12; s.Notes3D = true;
        s.HaloColor = "#F3C56E"; s.HaloIntensity = 44; s.HaloPulse = false; s.HaloPulseStyle = "Pulse"; s.HaloPulseIntensity = 16; s.HaloPulseSpeed = 20;
        s.NoteLandingGlow = false; s.NoteLandingGlowAmount = 18;
        s.PressedKeyColor = "#F3C56E"; s.PressedKeyColorMode = "Note"; s.KeyFeltColor = "#4C382D";
        s.KeyboardStyle = "Studio"; s.KeyLighting = 30; s.KeyGlowRadius = 32; s.KeyOverhang = 18; s.KeyPressDepth = 40; s.KeyLabels = "C";
        s.FallingTrail = "None"; s.FallingTrailIntensity = 0; s.FallingTrailLength = 30; s.FallingPulse = false; s.FallingGhost = false; s.NoteShimmer = false;
        s.ImpactBurst = "Embers"; s.ImpactWave = "None"; s.ImpactWaveIntensity = 30; s.RingSize = 36;
        s.ShowImpactFlash = false; s.ImpactFlashStyle = "Flash"; s.ImpactFlashIntensity = 18; s.ImpactMorph = "None"; s.ImpactMorphIntensity = 24;
        s.ParticleAmount = 8; s.ParticleVelocity = 150; s.ParticleSpeed = 100; s.ParticleResponse = 42; s.ParticleSpread = 38; s.ParticleLife = .6;
        s.ParticleLifeRandomness = 25; s.ParticleSize = 1.1; s.ParticleSizeRandomness = 35; s.ParticleGlow = 48; s.ParticleRandomness = 28; s.Gravity = 220; s.Drag = 34; s.EmitterSize = 12; s.Spiral = 10;
        s.ShowFlame = false; s.FlameIntensity = 0; s.FlameHeight = 30; s.FlameColorMode = "Warm";
        s.WispAmount = 0; s.WispSpeed = 120; s.WispHeight = 40; s.WispWidth = 30; s.WispTurbulence = 25; s.WispGlow = 30;
        s.HoldBar = false; s.HoldBarIntensity = 24; s.HoldBreath = false; s.HoldBreathRate = 24; s.HoldVibration = false; s.HoldVibrationAmount = 15;
        s.HoldColorCycle = false; s.HoldElectricArc = false; s.HoldArcIntensity = 20;
        s.ReleaseEffect = "Fade"; s.ReleaseIntensity = 32;
        s.AmbientEnergy = "None"; s.AmbientEnergyAmount = 0; s.AmbientEnergySpeed = 18;
        s.AmbientNature = "None"; s.AmbientNatureAmount = 0; s.AmbientNatureSpeed = 18;
        s.AmbientLight = "None"; s.AmbientLightAmount = 0; s.AmbientLightSpeed = 18; s.AmbientLightColor = "#F3C56E";
        s.AmbientCosmic = "None"; s.AmbientCosmicAmount = 0; s.AmbientCosmicSpeed = 18;
        s.VelocityColor = false; s.OctaveColor = false; s.PedalGlow = false; s.TempoSync = false; s.AudioReactive = false; s.ZoneSplit = false;
        s.BloomIntensity = 34; s.BloomSize = 48; s.Vignette = 24; s.HorizonGlow = 18; s.BeamIntensity = 0; s.Saturation = 100; s.Contrast = 100;
        s.ShadingQuality = "Balanced"; s.ShaderKeyLight = 76; s.ShaderShadows = 62; s.ShaderAmbientOcclusion = 48; s.ShaderGloss = 60;
        s.ShaderRimLight = 42; s.ShaderEmissive = 60; s.ShaderExposure = 100; s.ShaderCameraTilt = 40; s.ShaderFilmic = true;
        s.ChromeMotion = "Calm"; s.BackdropDensity = 24; s.ShellTheme = ShellThemes.ConcertNoirId;
        return s;
    }

    /// <summary>The signature stage look: two-hand brass/sea-glass notes, satin keyboard and one soft recital light.</summary>
    internal static PianoVisualSettings ConcertGold()
    {
        var s = Base(DefaultPresetName);
        s.ShellTheme = ShellThemes.VelvetGoldId;
        s.NoteStyle = "Solid"; s.ColorMode = "PerHand"; s.LeftHandColor = "#63D7DE"; s.RightHandColor = "#F3C56E"; s.HandSplitPitch = 60;
        s.NoteGlow = 42; s.NoteEdge = 74; s.NoteEdgeWidth = 26; s.NoteTint = 84; s.NoteHeadGlow = 24; s.NoteRoundness = 36;
        s.HaloColor = "#E8BD71"; s.HaloIntensity = 46; s.ShowHalo = true;
        s.FallingTrail = "Glow"; s.FallingTrailIntensity = 24; s.FallingTrailLength = 28;
        s.ShowEmbers = true; s.ImpactBurst = "Dust"; s.ParticleAmount = 5; s.ParticleVelocity = 82; s.ParticleSpread = 24; s.ParticleLife = .42; s.ParticleSize = .85; s.ParticleGlow = 24;
        s.ImpactWave = "Ring"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 32; s.RingSize = 36;
        s.HoldBar = true; s.HoldBarIntensity = 24; s.PedalGlow = true; s.PedalGlowIntensity = 18; s.ReleaseEffect = "Fade"; s.ReleaseIntensity = 30;
        s.NoteLandingGlow = true; s.NoteLandingGlowAmount = 20;
        s.AmbientLight = "Spotlights"; s.AmbientLightAmount = 16; s.AmbientLightSpeed = 18; s.AmbientLightColor = "#E8BD71";
        s.KeyboardStyle = "Studio"; s.ShowKeyFelt = true; s.KeyFeltColor = "#4E3529"; s.KeyLighting = 34; s.KeyGlowRadius = 38;
        s.BloomIntensity = 36; s.BloomSize = 48; s.Vignette = 28; s.HorizonGlow = 22; s.Saturation = 100;
        s.ShadingQuality = "Balanced"; s.ShaderKeyLight = 78; s.ShaderGloss = 62; s.ShaderShadows = 66; s.ShaderAmbientOcclusion = 52; s.ShaderEmissive = 60; s.ShaderRimLight = 44; s.ShaderCameraTilt = 38;
        return s;
    }

    /// <summary>Stream/creator look: violet material, cyan-violet note edge and sparse local energy.</summary>
    internal static PianoVisualSettings NeonViolet()
    {
        var s = Base("Neon Violet");
        s.ShellTheme = ShellThemes.ConcertNoirId;
        s.NoteStyle = "Neon"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#786DF1"; s.NoteColorEnd = "#54D8E8";
        s.HaloColor = "#9B8CFF"; s.HaloIntensity = 56; s.NoteGlow = 62; s.NoteEdge = 82; s.NoteEdgeWidth = 38; s.NoteTint = 42; s.NoteHeadGlow = 30;
        s.BackgroundMotion = "Nebula"; s.BackgroundMotionAmount = 16; s.BackgroundMotionSpeed = 14; s.BackgroundMotionColor = "#8073D9";
        s.FallingTrail = "Glow"; s.FallingTrailIntensity = 34; s.FallingTrailLength = 34; s.NoteShimmer = true; s.NoteShimmerAmount = 18;
        s.ShowEmbers = true; s.ImpactBurst = "Embers"; s.ParticleAmount = 10; s.ParticleVelocity = 150; s.ParticleGlow = 46;
        s.ImpactWave = "Shockwave"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 36; s.RingSize = 42;
        s.ShowImpactFlash = true; s.ImpactFlashStyle = "Plasma"; s.ImpactFlashIntensity = 20;
        s.HoldBar = true; s.HoldBarIntensity = 22; s.ReleaseEffect = "Echo Rings"; s.ReleaseIntensity = 22;
        s.HaloPulse = true; s.HaloPulseStyle = "Pulse"; s.HaloPulseIntensity = 18; s.HaloPulseSpeed = 22;
        s.BloomIntensity = 48; s.BloomSize = 55; s.Vignette = 28; s.Saturation = 104;
        s.ShaderEmissive = 72; s.ShaderRimLight = 62; s.ShaderGloss = 68;
        return s;
    }

    /// <summary>Learning look: high-contrast opaque bars, stable hand split and useful labels without decorative motion.</summary>
    internal static PianoVisualSettings TwoHands()
    {
        var s = Base("Two Hands");
        s.ShellTheme = ShellThemes.ConcertGrandId;
        s.NoteStyle = "Solid"; s.ColorMode = "PerHand"; s.LeftHandColor = "#54B7E5"; s.RightHandColor = "#E88FAA"; s.HandSplitPitch = 60;
        s.NoteGlow = 8; s.NoteEdge = 58; s.NoteEdgeWidth = 24; s.NoteHeadGlow = 0; s.NoteTint = 100; s.NoteRoundness = 24;
        s.ShowNoteLabels = true; s.KeyLabels = "C"; s.NoteLandingGlow = true; s.NoteLandingGlowAmount = 28;
        s.ShowEmbers = false; s.ShowImpactRings = false; s.ImpactWave = "None"; s.ShowImpactFlash = false; s.HaloIntensity = 26;
        s.KeyboardStyle = "Classic"; s.KeyLighting = 22; s.KeyGlowRadius = 16; s.ShowKeyFelt = false;
        s.BloomIntensity = 8; s.Vignette = 12; s.HorizonGlow = 0; s.ShadingQuality = "Fast"; s.ShaderEmissive = 35;
        return s;
    }

    /// <summary>Low-cost piano roll with stable, muted track colors and no decorative effect layers.</summary>
    internal static PianoVisualSettings ClassicRoll()
    {
        var s = Base("Classic Roll");
        s.NoteStyle = "Solid"; s.ColorMode = "PerTrack";
        s.TrackColors = ["#58B7C5", "#D38DAA", "#D6AE66", "#85B493", "#D78070", "#9A91CE", "#64AAA1", "#CF7F9A"];
        s.NoteTint = 100; s.NoteGlow = 0; s.NoteEdge = 28; s.NoteEdgeWidth = 18; s.NoteRoundness = 20; s.Notes3D = false;
        s.ShowEmbers = false; s.ShowHalo = true; s.HaloColor = "#D5DCE6"; s.HaloIntensity = 24;
        s.ShowImpactRings = false; s.ShowImpactFlash = false; s.NoteLandingGlow = false; s.ReleaseEffect = "Fade"; s.ReleaseIntensity = 24;
        s.KeyboardStyle = "Classic"; s.KeyLighting = 16; s.KeyGlowRadius = 0; s.ShowKeyFelt = false;
        s.BloomIntensity = 0; s.Vignette = 0; s.HorizonGlow = 0; s.ShadingQuality = "Fast"; s.ShaderCameraTilt = 30; s.ShaderEmissive = 32;
        return s;
    }

    /// <summary>Production chroma-key look: pure green background and opaque notes, with no bloom or colored spill.</summary>
    internal static PianoVisualSettings GreenScreen()
    {
        var s = Base("Green Screen");
        s.BackgroundMode = "ChromaGreen"; s.BackgroundColor = "#00FF00"; s.BackgroundGradient = false; s.BackgroundMotion = "None"; s.BackgroundMotionAmount = 0;
        s.NoteStyle = "Solid"; s.ColorMode = "PerHand"; s.LeftHandColor = "#42D4E8"; s.RightHandColor = "#F05DB6"; s.NoteTint = 100;
        s.NoteGlow = 0; s.NoteEdge = 42; s.NoteEdgeWidth = 22; s.NoteHeadGlow = 0; s.NoteLandingGlow = false;
        s.ShowEmbers = false; s.ShowHalo = false; s.ShowFlame = false; s.ShowWisps = false; s.ShowImpactRings = false; s.ShowLightBeams = false;
        s.ShowImpactFlash = false; s.ShowStars = false; s.ShootingStars = false; s.ShowPetals = false;
        s.AmbientEnergy = "None"; s.AmbientNature = "None"; s.AmbientLight = "None"; s.AmbientCosmic = "None";
        s.BloomIntensity = 0; s.Vignette = 0; s.HorizonGlow = 0; s.BeamIntensity = 0; s.Saturation = 100;
        s.KeyboardStyle = "Studio"; s.KeyLighting = 0; s.KeyGlowRadius = 0; s.ShowKeyFelt = false;
        s.ShadingQuality = "Off"; s.ShaderEmissive = 0; s.ShaderShadows = 0; s.ShaderAmbientOcclusion = 0;
        return s;
    }

    /// <summary>Warm ember recipe: the flame material is kept, while particles and bloom remain sparse.</summary>
    internal static PianoVisualSettings Inferno()
    {
        var s = Base("Inferno");
        s.ShellTheme = ShellThemes.VelvetGoldId;
        s.NoteStyle = "Fire"; s.ColorMode = "Gradient"; s.Palette = "Fire"; s.NoteColorStart = "#F25738"; s.NoteColorEnd = "#F6B65B";
        s.HaloColor = "#F58A4B"; s.HaloIntensity = 52; s.NoteGlow = 64; s.NoteEdge = 76; s.NoteEdgeWidth = 30; s.NoteTint = 78; s.NoteTexture = 42; s.NoteHeadGlow = 30;
        s.BackgroundMotion = "Ember Haze"; s.BackgroundMotionAmount = 18; s.BackgroundMotionSpeed = 16; s.BackgroundMotionColor = "#C85A36";
        s.ShowEmbers = true; s.ShowFlame = true; s.FlameIntensity = 22; s.FlameHeight = 32; s.FlameColorMode = "Warm";
        s.ImpactBurst = "Embers"; s.ParticleAmount = 10; s.ParticleVelocity = 140; s.ParticleSpread = 36; s.ParticleGlow = 58;
        s.ImpactWave = "Shockwave"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 42; s.RingSize = 40;
        s.ShowImpactFlash = true; s.ImpactFlashStyle = "Flash"; s.ImpactFlashIntensity = 16;
        s.HoldBar = true; s.HoldBarIntensity = 24; s.ReleaseEffect = "Smoke"; s.ReleaseIntensity = 24;
        s.AmbientEnergy = "Fireworks"; s.AmbientEnergyAmount = 7; s.AmbientEnergySpeed = 18;
        s.BloomIntensity = 42; s.BloomSize = 52; s.HorizonGlow = 20; s.Saturation = 106;
        s.ShadingQuality = "Balanced"; s.ShaderKeyLight = 82; s.ShaderGloss = 56; s.ShaderShadows = 70; s.ShaderEmissive = 68;
        return s;
    }

    /// <summary>Cold glass with one restrained snow layer and a readable blue-white face.</summary>
    internal static PianoVisualSettings IceCrystal()
    {
        var s = Base("Ice Crystal");
        s.NoteStyle = "Glass"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#72C6DF"; s.NoteColorEnd = "#E2F3F7";
        s.HaloColor = "#8BD9E8"; s.HaloIntensity = 46; s.NoteGlow = 42; s.NoteEdge = 78; s.NoteEdgeWidth = 30; s.NoteTint = 78; s.NoteRefraction = 32; s.NoteRoundness = 30; s.NoteHeadGlow = 20;
        s.BackgroundMotion = "Prism"; s.BackgroundMotionAmount = 10; s.BackgroundMotionSpeed = 12; s.BackgroundMotionColor = "#77C9DD";
        s.HaloPulse = true; s.HaloPulseStyle = "Ripple"; s.HaloPulseIntensity = 10; s.HaloPulseSpeed = 14;
        s.FallingTrail = "Sparkles"; s.FallingTrailIntensity = 18; s.FallingTrailLength = 26;
        s.ShowEmbers = true; s.ImpactBurst = "Splash"; s.ParticleAmount = 7; s.ParticleVelocity = 105; s.ParticleSize = 1; s.ParticleGlow = 38;
        s.ImpactWave = "Ripple"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 32; s.RingSize = 34;
        s.ImpactMorph = "Absorb"; s.ImpactMorphIntensity = 30; s.HoldVibration = true; s.HoldVibrationAmount = 10; s.ReleaseEffect = "Dissolve"; s.ReleaseIntensity = 24;
        s.AmbientNature = "Snow"; s.AmbientNatureAmount = 10; s.AmbientNatureSpeed = 16;
        s.KeyboardStyle = "Glass"; s.KeyGlowRadius = 28; s.KeyLighting = 26;
        s.BloomIntensity = 34; s.BloomSize = 46; s.Vignette = 20; s.Saturation = 94;
        s.ShaderGloss = 74; s.ShaderEmissive = 50; s.ShaderRimLight = 48;
        return s;
    }

    /// <summary>Soft rainbow by pitch and a slow aurora; pitch color stays stable while a note is held.</summary>
    internal static PianoVisualSettings AuroraRainbow()
    {
        var s = Base("Aurora Rainbow");
        s.NoteStyle = "Solid"; s.ColorMode = "RainbowPitch"; s.Palette = "Spectrum"; s.RainbowSpeed = 20;
        s.NoteGlow = 34; s.NoteEdge = 48; s.NoteEdgeWidth = 22; s.NoteTint = 92; s.NoteHeadGlow = 16;
        s.HaloColor = "#76D8D0"; s.HaloIntensity = 34;
        s.BackgroundMotion = "Aurora"; s.BackgroundMotionAmount = 20; s.BackgroundMotionSpeed = 14; s.BackgroundMotionColor = "#65D4C4";
        s.FallingTrail = "Sparkles"; s.FallingTrailIntensity = 16; s.FallingTrailLength = 22;
        s.HaloPulse = true; s.HaloPulseStyle = "Spectrum"; s.HaloPulseIntensity = 12; s.HaloPulseSpeed = 18;
        s.ShowEmbers = false; s.ShowImpactRings = true; s.ImpactWave = "Ring"; s.ImpactWaveIntensity = 22; s.RingSize = 30;
        s.BloomIntensity = 30; s.BloomSize = 42; s.Saturation = 92; s.ShaderEmissive = 48;
        return s;
    }

    /// <summary>Rose-and-ivory nocturne with sparse petals and petal-like impact accents.</summary>
    internal static PianoVisualSettings SakuraNocturne()
    {
        var s = Base("Sakura Nocturne");
        s.ShellTheme = ShellThemes.ConcertGrandId;
        s.NoteStyle = "Glass"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#E887A7"; s.NoteColorEnd = "#F4D4AD";
        s.HaloColor = "#D99CB2"; s.HaloIntensity = 40; s.NoteGlow = 42; s.NoteEdge = 70; s.NoteEdgeWidth = 28; s.NoteTint = 68; s.NoteRefraction = 20; s.NoteRoundness = 48;
        s.BackgroundMotion = "Nebula"; s.BackgroundMotionAmount = 10; s.BackgroundMotionSpeed = 12; s.BackgroundMotionColor = "#685178";
        s.ShowPetals = true; s.PetalAmount = 14; s.PetalColor = "#E9A4B8";
        s.FallingTrail = "Ribbon"; s.FallingTrailIntensity = 22; s.FallingTrailLength = 26;
        s.ShowEmbers = true; s.ImpactBurst = "Confetti"; s.ParticleAmount = 5; s.ParticleVelocity = 95; s.ParticleGlow = 22;
        s.ImpactWave = "Ring"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 24; s.RingSize = 30;
        s.HoldBreath = true; s.HoldBreathRate = 18; s.ReleaseEffect = "Float Up"; s.ReleaseIntensity = 22;
        s.BloomIntensity = 36; s.BloomSize = 46; s.Vignette = 24; s.HorizonGlow = 18; s.Saturation = 98;
        s.KeyboardStyle = "Glass"; s.KeyGlowRadius = 30; s.ShaderGloss = 66; s.ShaderEmissive = 52;
        return s;
    }

    /// <summary>Quiet silver-blue glass and a small starfield for slow nocturnes.</summary>
    internal static PianoVisualSettings MoonlightSonata()
    {
        var s = Base("Moonlight Sonata");
        s.ShellTheme = ShellThemes.ConcertNoirId;
        s.NoteStyle = "Glass"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#7897C7"; s.NoteColorEnd = "#D9E6F4";
        s.HaloColor = "#A9C2E0"; s.HaloIntensity = 38; s.NoteGlow = 36; s.NoteEdge = 72; s.NoteEdgeWidth = 26; s.NoteTint = 82; s.NoteRefraction = 22; s.NoteRoundness = 42;
        s.BackgroundMotion = "Aurora"; s.BackgroundMotionAmount = 8; s.BackgroundMotionSpeed = 10; s.BackgroundMotionColor = "#7898BD";
        s.ShowStars = true; s.StarDensity = 18;
        s.FallingTrail = "Glow"; s.FallingTrailIntensity = 16; s.FallingTrailLength = 24;
        s.ShowEmbers = false; s.ImpactWave = "Ring"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 20; s.RingSize = 28;
        s.ReleaseEffect = "Fade"; s.ReleaseIntensity = 26;
        s.KeyboardStyle = "Glass"; s.KeyGlowRadius = 28; s.KeyLighting = 22;
        s.BloomIntensity = 28; s.BloomSize = 42; s.Vignette = 30; s.BackgroundDim = 34; s.Saturation = 86;
        s.ShadingQuality = "Balanced"; s.ShaderGloss = 72; s.ShaderExposure = 100; s.ShaderEmissive = 46; s.ShaderRimLight = 38;
        return s;
    }

    /// <summary>Stable pitch identity with a restrained rainbow trail and a single cosmic background layer.</summary>
    internal static PianoVisualSettings GalaxyVoyage()
    {
        var s = Base("Galaxy Voyage");
        s.NoteStyle = "Neon"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#7774D9"; s.NoteColorEnd = "#75C9DD";
        s.HaloColor = "#8C85E2"; s.HaloIntensity = 48; s.NoteGlow = 52; s.NoteEdge = 78; s.NoteEdgeWidth = 32; s.NoteTint = 40; s.NoteHeadGlow = 24; s.NoteRoundness = 48;
        s.AmbientCosmic = "Galaxy"; s.AmbientCosmicAmount = 16; s.AmbientCosmicSpeed = 16;
        s.FallingTrail = "Rainbow"; s.FallingTrailIntensity = 24; s.FallingTrailLength = 28;
        s.ShowEmbers = true; s.ImpactBurst = "Fireworks"; s.ParticleAmount = 7; s.ParticleVelocity = 130; s.ParticleGlow = 48;
        s.ImpactWave = "Shockwave"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 34; s.RingSize = 38;
        s.ShowImpactFlash = true; s.ImpactFlashStyle = "Plasma"; s.ImpactFlashIntensity = 18;
        s.HoldBreath = true; s.HoldBreathRate = 18; s.ReleaseEffect = "Dissolve"; s.ReleaseIntensity = 22;
        s.BloomIntensity = 44; s.BloomSize = 52; s.Vignette = 28; s.Saturation = 100;
        s.ShadingQuality = "Balanced"; s.ShaderGloss = 68; s.ShaderEmissive = 65; s.ShaderRimLight = 54;
        return s;
    }

    /// <summary>Electric cyan-white bars with local lightning and quiet held-key arcs.</summary>
    internal static PianoVisualSettings ElectricStorm()
    {
        var s = Base("Electric Storm");
        s.NoteStyle = "Neon"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#49B8D7"; s.NoteColorEnd = "#DDF7FA";
        s.HaloColor = "#71D6E8"; s.HaloIntensity = 50; s.NoteGlow = 54; s.NoteEdge = 82; s.NoteEdgeWidth = 34; s.NoteTint = 34; s.NoteHeadGlow = 24;
        s.FallingTrail = "Speed Lines"; s.FallingTrailIntensity = 24; s.FallingTrailLength = 25;
        s.ShowEmbers = true; s.ImpactBurst = "Embers"; s.ParticleAmount = 7; s.ParticleVelocity = 160; s.ParticleGlow = 50;
        s.ImpactWave = "Shockwave"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 34; s.RingSize = 36;
        s.ShowImpactFlash = true; s.ImpactFlashStyle = "Lightning"; s.ImpactFlashIntensity = 22;
        s.HoldElectricArc = true; s.HoldArcIntensity = 22; s.ReleaseEffect = "Snap Back"; s.ReleaseIntensity = 22;
        s.AmbientEnergy = "Lightning Storm"; s.AmbientEnergyAmount = 8; s.AmbientEnergySpeed = 22;
        s.TempoSync = true; s.TempoSyncAmount = 18;
        s.HaloPulse = true; s.HaloPulseStyle = "Electric Arc"; s.HaloPulseIntensity = 16; s.HaloPulseSpeed = 24;
        s.BloomIntensity = 42; s.BloomSize = 50; s.Vignette = 26; s.Saturation = 102; s.ShaderEmissive = 66;
        return s;
    }

    /// <summary>Teal glass and locally anchored water effects over a slow ocean-flow background.</summary>
    internal static PianoVisualSettings OceanDepths()
    {
        var s = Base("Ocean Depths");
        s.NoteStyle = "Glass"; s.ColorMode = "Gradient"; s.Palette = "Custom"; s.NoteColorStart = "#3EAEB5"; s.NoteColorEnd = "#A5E1DE";
        s.HaloColor = "#5BC7C7"; s.HaloIntensity = 42; s.NoteGlow = 40; s.NoteEdge = 74; s.NoteEdgeWidth = 28; s.NoteTint = 76; s.NoteRefraction = 28; s.NoteRoundness = 48;
        s.BackgroundMotion = "Ocean Flow"; s.BackgroundMotionAmount = 14; s.BackgroundMotionSpeed = 12; s.BackgroundMotionColor = "#247F90";
        s.FallingTrail = "Glow"; s.FallingTrailIntensity = 20; s.FallingTrailLength = 26;
        s.ShowEmbers = true; s.ImpactBurst = "Splash"; s.ParticleAmount = 7; s.ParticleVelocity = 115; s.ParticleGlow = 36;
        s.ImpactWave = "Ripple"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 34; s.RingSize = 38;
        s.ReleaseEffect = "Float Up"; s.ReleaseIntensity = 22;
        s.AmbientNature = "Rain"; s.AmbientNatureAmount = 8; s.AmbientNatureSpeed = 18;
        s.KeyboardStyle = "Glass"; s.KeyGlowRadius = 30; s.KeyLighting = 26;
        s.BloomIntensity = 36; s.BloomSize = 46; s.Vignette = 26; s.Saturation = 96;
        s.ShaderGloss = 72; s.ShaderEmissive = 52; s.ShaderRimLight = 44;
        return s;
    }

    /// <summary>Square bars, a compact track palette and low-intensity arcade feedback.</summary>
    internal static PianoVisualSettings RetroArcade()
    {
        var s = Base("Retro Arcade");
        s.ShellTheme = ShellThemes.ConcertGrandId;
        s.NoteStyle = "Solid"; s.Notes3D = false; s.ColorMode = "PerTrack";
        s.TrackColors = ["#56D5D2", "#F2769A", "#FFD166", "#8CDB75", "#B49AFF", "#FF9A64", "#67B3FF", "#F2E66F"];
        s.NoteRoundness = 0; s.NoteGlow = 14; s.NoteEdge = 62; s.NoteEdgeWidth = 24; s.NoteTint = 100; s.NoteHeadGlow = 0;
        s.BackgroundMotion = "Retro Grid"; s.BackgroundMotionAmount = 18; s.BackgroundMotionSpeed = 24; s.BackgroundMotionColor = "#498F9C";
        s.FallingGhost = true; s.FallingGhostAmount = 14;
        s.ShowEmbers = true; s.ImpactBurst = "Confetti"; s.ParticleAmount = 8; s.ParticleVelocity = 110; s.ParticleGlow = 24;
        s.ImpactWave = "Ring"; s.ShowImpactRings = true; s.ImpactWaveIntensity = 24; s.RingSize = 32;
        s.ImpactMorph = "Bounce"; s.ImpactMorphIntensity = 30; s.ReleaseEffect = "Snap Back"; s.ReleaseIntensity = 24;
        s.HaloPulse = true; s.HaloPulseStyle = "Sweep"; s.HaloPulseIntensity = 14; s.HaloPulseSpeed = 22;
        s.BloomIntensity = 18; s.BloomSize = 34; s.Vignette = 20; s.Saturation = 104;
        s.KeyboardStyle = "Classic"; s.KeyGlowRadius = 18; s.KeyLighting = 20; s.ShadingQuality = "Fast"; s.ShaderEmissive = 40;
        return s;
    }
}

/// <summary>Reads and writes user presets as individual JSON files.</summary>
internal sealed class VisualPresetStore(string directory)
{
    internal string Directory { get; } = directory;

    private static VisualPresetStore? _default;

    /// <summary>The store inside the current settings folder; rebuilt when that folder is redirected.</summary>
    internal static VisualPresetStore Default => _default ??= new(Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "presets"));

    internal static void InvalidateDefault() => _default = null;

    internal IReadOnlyList<VisualPreset> LoadUserPresets()
    {
        var presets = new List<VisualPreset>();
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return presets;
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var stored = ReadPresetFile(File.ReadAllText(file));
                    var name = Path.GetFileNameWithoutExtension(file);
                    stored.Settings.PresetName = name;
                    presets.Add(new VisualPreset(name, Loc.F("User preset · {0}", Path.GetFileName(file)), false, stored.Settings, file, stored.Thumbnail));
                }
                catch { /* a corrupt file should not hide the remaining presets */ }
            }
        }
        catch { }
        return presets;
    }

    internal VisualPreset Save(string name, PianoVisualSettings settings, string? thumbnail = null)
    {
        var safe = SanitizeName(name);
        System.IO.Directory.CreateDirectory(Directory);
        var copy = settings.Clone();
        copy.PresetName = safe;
        copy.ClearMachineLocal();
        var path = Path.Combine(Directory, safe + ".json");
        var temp = path + ".tmp";
        var picture = thumbnail ?? ""; // the file always carries the field, empty when there is no picture
        File.WriteAllText(temp, WritePresetFile(copy, picture));
        File.Move(temp, path, true);
        return new VisualPreset(safe, Loc.F("User preset · {0}", safe + ".json"), false, copy, path, picture);
    }

    internal bool Delete(VisualPreset preset)
    {
        if (preset.BuiltIn || preset.FilePath is null) return false;
        try { if (File.Exists(preset.FilePath)) File.Delete(preset.FilePath); return true; } catch { return false; }
    }

    internal static void Export(PianoVisualSettings settings, string path, string? thumbnail = null, string description = "")
    {
        var copy = settings.Clone();
        copy.ClearMachineLocal();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        File.WriteAllText(path, WritePresetFile(copy, thumbnail, description));
    }

    internal static VisualPreset Import(string path)
    {
        var stored = ReadPresetFile(File.ReadAllText(path));
        var name = SanitizeName(string.IsNullOrWhiteSpace(stored.Settings.PresetName) || stored.Settings.PresetName == "Custom"
            ? Path.GetFileNameWithoutExtension(path) : stored.Settings.PresetName);
        stored.Settings.PresetName = name;
        // A preset that travels with its own description keeps it, so an exported shelf look still reads
        // like itself on the other machine.
        var description = string.IsNullOrWhiteSpace(stored.Description) ? Loc.F("Imported · {0}", Path.GetFileName(path)) : stored.Description;
        return new VisualPreset(name, description, false, stored.Settings, null, stored.Thumbnail);
    }

    // =================================================================================================
    // The preset file: a small envelope so a look can travel with the picture that was rendered for it.
    // Files written before the envelope existed are plain settings JSON and still load, and a file with
    // no picture at all is simply a preset that gets drawn from its settings.
    // =================================================================================================

    private sealed record PresetFile(int Version, string Thumbnail, string Description, PianoVisualSettings Settings);

    internal static string WritePresetFile(PianoVisualSettings settings, string? thumbnail, string description = "") =>
        System.Text.Json.JsonSerializer.Serialize(new PresetFile(1, thumbnail ?? "", description ?? "", settings),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    /// <summary>Reads a preset file: the envelope, or the bare settings JSON older versions wrote.</summary>
    internal static PresetFileContent ReadPresetFile(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            && document.RootElement.TryGetProperty("Settings", out var inner)
            && inner.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var settings = PianoVisualSettings.FromJson(inner.GetRawText());
            var thumbnail = document.RootElement.TryGetProperty("Thumbnail", out var picture) && picture.ValueKind == System.Text.Json.JsonValueKind.String
                ? picture.GetString() ?? ""
                : "";
            var description = document.RootElement.TryGetProperty("Description", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String
                ? text.GetString() ?? ""
                : "";
            return new PresetFileContent(settings, thumbnail, description);
        }
        return new PresetFileContent(PianoVisualSettings.FromJson(json), "", "");
    }

    /// <summary>What a preset file holds: the look, the picture rendered for it and the human description.</summary>
    internal sealed record PresetFileContent(PianoVisualSettings Settings, string Thumbnail, string Description);

    internal static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? "").Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "My preset";
        return cleaned.Length > 40 ? cleaned[..40].Trim() : cleaned;
    }
}
