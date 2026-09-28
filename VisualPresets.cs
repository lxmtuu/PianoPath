using System.IO;

namespace PianoPath;

/// <summary>A named look for the stage. Built-in presets are generated in code; user presets are JSON files.</summary>
internal sealed record VisualPreset(string Name, string Description, bool BuiltIn, PianoVisualSettings Settings, string? FilePath = null);

/// <summary>Built-in looks inspired by popular MIDI visualizers plus a JSON store for user-made presets.</summary>
internal static class VisualPresets
{
    internal const string DefaultPresetName = "Neon Violet";

    internal static IReadOnlyList<VisualPreset> BuiltIn { get; } =
    [
        new(DefaultPresetName, "Hollow violet notes with a bright glowing outline, sparks and fire bursts on impact.", true, NeonViolet()),
        new("Inferno", "Burning orange notes with an ember texture, red-lit keys, warm bloom and heavy spark explosions.", true, Inferno()),
        new("Aurora Rainbow", "Rainbow colors per pitch, smoke wisps rising from every pressed key and soft light beams.", true, AuroraRainbow()),
        new("Ice Crystal", "Cool glass notes in cyan and white, crisp edges, gentle snow-like particles.", true, IceCrystal()),
        new("Two Hands", "Blue left hand / pink right hand split at middle C - ideal for tutorials and practice videos.", true, TwoHands()),
        new("Cinematic Bloom", "Letterboxed, grainy, anamorphic: heavy bloom, lens streaks, chromatic fringing and deep note shadows.", true, CinematicBloom()),
        new("Studio Grand", "Warm gold notes over a glossy studio grand: strong key gloss, black-key contact shadows and mirror reflections.", true, StudioGrand()),
        new("Classic Roll", "Clean solid piano-roll bars without particles; low GPU cost for long recordings.", true, ClassicRoll()),
        new("Green Screen", "Pure green stage with no decorative layers, ready for OBS chroma keying.", true, GreenScreen()),
    ];

    internal static VisualPreset? FindBuiltIn(string name) => BuiltIn.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static PianoVisualSettings Base(string name)
    {
        var s = new PianoVisualSettings { PresetName = name };
        s.ApplyMigrations();
        return s;
    }

    internal static PianoVisualSettings NeonViolet()
    {
        var s = Base(DefaultPresetName);
        s.NoteStyle = "Neon"; s.ColorMode = "Gradient"; s.Palette = "Violet"; s.NoteColorStart = "#7B5CFF"; s.NoteColorEnd = "#F05CFF";
        s.HaloColor = "#C66EFF"; s.NoteGlow = 110; s.NoteEdge = 120; s.NoteEdgeWidth = 62; s.NoteTint = 30; s.NoteHeadGlow = 55;
        s.ShowFlame = true; s.FlameIntensity = 75; s.FlameHeight = 60; s.FlameColorMode = "Warm"; s.ShowWisps = false; s.ShowImpactRings = true;
        s.KeyboardStyle = "Studio"; s.PressedKeyColorMode = "Note"; s.KeyGlowRadius = 55; s.BloomIntensity = 80; s.Vignette = 30; s.HorizonGlow = 35;
        s.HaloTintMode = "Halo"; s.HaloThickness = 50; s.HaloIntensity = 85; s.HaloGlowSize = 60; s.HaloPulse = 55;
        s.NoteColorMid = "#B85CFF"; s.NoteDepth = 55; s.NoteSpecular = 65; s.NoteRimLight = 55; s.NoteTrail = 40;
        s.NoteShadowStrength = 65; s.NoteShadowDistance = 18; s.NoteShadowBlur = 62; s.KeyGloss = 65; s.KeyBevel = 60; s.KeyContactShadow = 55;
        s.BloomThreshold = 40; s.AnamorphicStreaks = 28; s.ChromaticAberration = 16; s.FilmGrain = 8; s.Vibrance = 55;
        return s;
    }

    internal static PianoVisualSettings Inferno()
    {
        var s = Base("Inferno");
        s.NoteStyle = "Fire"; s.ColorMode = "Gradient"; s.Palette = "Fire"; s.NoteColorStart = "#FF3B12"; s.NoteColorEnd = "#FFB02E";
        s.HaloColor = "#FF4A1C"; s.PressedKeyColor = "#FF3A1A"; s.PressedKeyColorMode = "Fixed"; s.NoteTexture = 75; s.NoteGlow = 130; s.NoteEdge = 110;
        s.NoteEdgeWidth = 40; s.NoteTint = 95; s.NoteHeadGlow = 70; s.NoteRoundness = 45; s.ParticleAmount = 48; s.ParticleVelocity = 260; s.ParticleSpread = 85;
        s.ParticleLife = .8; s.ParticleSize = 2.6; s.Gravity = 420; s.ShowFlame = true; s.FlameIntensity = 100; s.FlameHeight = 85; s.FlameColorMode = "Warm";
        s.ShowImpactRings = true; s.RingSize = 60; s.KeyboardStyle = "Studio"; s.KeyGlowRadius = 90; s.KeyLighting = 55; s.ShowKeyFelt = true; s.KeyFeltColor = "#FF2E3A";
        s.HorizonGlow = 80; s.BeamIntensity = 55; s.BloomIntensity = 110; s.BloomSize = 90; s.Vignette = 45; s.Saturation = 115;
        s.HaloTintMode = "Note"; s.HaloThickness = 60; s.HaloIntensity = 95; s.HaloGlowSize = 75; s.HaloPulse = 80; s.NoteColorMid = "#FF7A2F";
        s.NoteShadowStrength = 72; s.NoteShadowDistance = 22; s.NoteShadowBlur = 70; s.AnamorphicStreaks = 48; s.ChromaticAberration = 26; s.FilmGrain = 14;
        s.BloomThreshold = 30; s.KeyGloss = 45; s.KeyBevel = 70; s.KeyContactShadow = 75; s.DustDensity = 60;
        return s;
    }

    internal static PianoVisualSettings AuroraRainbow()
    {
        var s = Base("Aurora Rainbow");
        s.NoteStyle = "Solid"; s.ColorMode = "RainbowPitch"; s.Palette = "Spectrum"; s.HaloColor = "#8CFFE9"; s.NoteGlow = 70; s.NoteEdge = 60; s.NoteEdgeWidth = 30;
        s.NoteTint = 88; s.NoteRoundness = 60; s.NoteHeadGlow = 35; s.ShowWisps = true; s.WispAmount = 70; s.WispHeight = 70; s.WispTurbulence = 65; s.WispGlow = 110;
        s.ShowFlame = false; s.ParticleAmount = 10; s.ParticleVelocity = 90; s.Gravity = 60; s.ShowImpactRings = false; s.ShowLightBeams = true; s.BeamIntensity = 70;
        s.KeyboardStyle = "Glass"; s.PressedKeyColorMode = "Note"; s.KeyGlowRadius = 70; s.BloomIntensity = 70; s.Vignette = 35; s.HorizonGlow = 25;
        s.HaloTintMode = "Rainbow"; s.HaloThickness = 55; s.HaloIntensity = 75; s.HaloGlowSize = 65; s.HaloPulse = 60; s.ShowGrid = true; s.GridIntensity = 35;
        s.ShowKeyReflection = true; s.KeyGloss = 70; s.NoteDepth = 40; s.NoteSpecular = 70; s.NoteRimLight = 45; s.NoteShadowStrength = 45; s.ChromaticAberration = 22;
        return s;
    }

    internal static PianoVisualSettings IceCrystal()
    {
        var s = Base("Ice Crystal");
        s.NoteStyle = "Glass"; s.ColorMode = "Gradient"; s.Palette = "Ocean"; s.NoteColorStart = "#7FE9FF"; s.NoteColorEnd = "#FFFFFF"; s.HaloColor = "#9BE8FF";
        s.NoteGlow = 60; s.NoteEdge = 90; s.NoteEdgeWidth = 35; s.NoteTint = 70; s.NoteRefraction = 65; s.NoteRoundness = 35; s.Notes3D = true; s.NoteHeadGlow = 30;
        s.ParticleAmount = 16; s.ParticleVelocity = 70; s.Gravity = 40; s.Drag = 40; s.ParticleLife = 1.4; s.ParticleSize = 1.8; s.ParticleGlow = 60; s.ShowFlame = false;
        s.ShowWisps = false; s.ShowImpactRings = true; s.RingSize = 35; s.KeyboardStyle = "Classic"; s.PressedKeyColorMode = "Note"; s.KeyGlowRadius = 40;
        s.BloomIntensity = 45; s.Vignette = 20; s.HorizonGlow = 20; s.Saturation = 85;
        s.ShowNoteShadow = false; s.NoteShadowStrength = 0; s.KeyGloss = 80; s.KeyBevel = 45; s.ShowFallboard = false; s.KeyContactShadow = 35;
        s.BloomThreshold = 62; s.FilmGrain = 0; s.DustDensity = 55; s.Vibrance = 35;
        return s;
    }

    internal static PianoVisualSettings TwoHands()
    {
        var s = Base("Two Hands");
        s.NoteStyle = "Glass"; s.ColorMode = "PerHand"; s.LeftHandColor = "#3FA9FF"; s.RightHandColor = "#FF6FD8"; s.HaloColor = "#B58CFF"; s.HandSplitPitch = 60;
        s.NoteGlow = 60; s.NoteEdge = 80; s.NoteEdgeWidth = 30; s.NoteTint = 85; s.ShowNoteLabels = true; s.KeyLabels = "C"; s.ParticleAmount = 12; s.ShowFlame = false;
        s.ShowImpactRings = true; s.KeyboardStyle = "Studio"; s.PressedKeyColorMode = "Note"; s.BloomIntensity = 50; s.Vignette = 20;
        return s;
    }

    internal static PianoVisualSettings CinematicBloom()
    {
        var s = Base("Cinematic Bloom");
        s.NoteStyle = "Glass"; s.ColorMode = "Gradient"; s.Palette = "Cyberpunk"; s.NoteColorStart = "#00F0FF"; s.NoteColorMid = "#7A6BFF"; s.NoteColorEnd = "#FF2BD6";
        s.NoteGlow = 95; s.NoteEdge = 70; s.NoteEdgeWidth = 26; s.NoteTint = 82; s.NoteHeadGlow = 60; s.NoteRefraction = 55; s.NoteRoundness = 40;
        s.BloomThreshold = 34; s.HaloTintMode = "Note"; s.HaloThickness = 58; s.HaloIntensity = 88; s.HaloGlowSize = 78; s.HaloPulse = 65;
        s.NoteShadowStrength = 62; s.NoteShadowDistance = 26; s.NoteShadowBlur = 76; s.NoteDepth = 62; s.NoteSpecular = 72; s.NoteRimLight = 58; s.NoteTrail = 52;
        s.ShowFlame = false; s.ShowImpactRings = true; s.RingSize = 45; s.ShowWisps = true; s.WispAmount = 40; s.WispHeight = 80; s.WispGlow = 90;
        s.KeyboardStyle = "Glass"; s.KeyGloss = 78; s.KeyBevel = 55; s.KeyContactShadow = 45; s.ShowKeyReflection = true; s.ShowFallboard = true;
        s.HorizonGlow = 65; s.BloomIntensity = 105; s.BloomSize = 105; s.AnamorphicStreaks = 42; s.ChromaticAberration = 30; s.FilmGrain = 26;
        s.CinematicBars = 9; s.Vignette = 55; s.Saturation = 112; s.Vibrance = 62; s.ColorTemperature = -12; s.DustDensity = 50;
        return s;
    }

    internal static PianoVisualSettings StudioGrand()
    {
        var s = Base("Studio Grand");
        s.NoteStyle = "Solid"; s.ColorMode = "Gradient"; s.Palette = "Sunset"; s.NoteColorStart = "#FFD37A"; s.NoteColorMid = "#FF9F5A"; s.NoteColorEnd = "#E96A4A";
        s.NoteGlow = 45; s.NoteEdge = 55; s.NoteEdgeWidth = 22; s.NoteTint = 96; s.NoteRoundness = 30; s.NoteHeadGlow = 30;
        s.NoteDepth = 68; s.NoteSpecular = 58; s.NoteRimLight = 40; s.NoteTrail = 22; s.NoteShadowStrength = 60; s.NoteShadowDistance = 14; s.NoteShadowBlur = 58;
        s.HaloColor = "#FFB071"; s.HaloTintMode = "Halo"; s.HaloThickness = 40; s.HaloIntensity = 60; s.HaloGlowSize = 45; s.HaloPulse = 35;
        s.ParticleAmount = 10; s.ParticleVelocity = 60; s.Gravity = 90; s.ShowFlame = false; s.ShowWisps = false; s.ShowImpactRings = true; s.RingSize = 28;
        s.KeyboardStyle = "Studio"; s.ShowKeyFelt = true; s.KeyFeltColor = "#B4323C"; s.KeyGloss = 82; s.KeyBevel = 72; s.KeyContactShadow = 82;
        s.ShowKeyReflection = true; s.ShowFallboard = true; s.KeyLabels = "C";
        s.BloomIntensity = 55; s.BloomSize = 70; s.BloomThreshold = 52; s.AnamorphicStreaks = 12; s.ChromaticAberration = 8; s.FilmGrain = 6;
        s.HorizonGlow = 30; s.Vignette = 38; s.Saturation = 105; s.Vibrance = 48; s.ColorTemperature = 14; s.DustDensity = 35;
        return s;
    }

    internal static PianoVisualSettings ClassicRoll()
    {
        var s = Base("Classic Roll");
        s.NoteStyle = "Solid"; s.ColorMode = "PerTrack"; s.NoteGlow = 20; s.NoteEdge = 40; s.NoteEdgeWidth = 20; s.NoteTint = 100; s.NoteRoundness = 25; s.Notes3D = false;
        s.NoteHeadGlow = 0; s.ShowEmbers = false; s.ShowFlame = false; s.ShowWisps = false; s.ShowImpactRings = false; s.ShowLightBeams = false; s.ShowHalo = true;
        s.HaloColor = "#FFFFFF"; s.KeyboardStyle = "Classic"; s.KeyLighting = 20; s.KeyGlowRadius = 0; s.BloomIntensity = 0; s.Vignette = 0; s.HorizonGlow = 0;
        s.ShowNoteShadow = false; s.NoteShadowStrength = 0; s.NoteTrail = 0; s.NoteDepth = 0; s.NoteSpecular = 0; s.NoteRimLight = 0;
        s.ShowKeyReflection = false; s.KeyGloss = 20; s.AnamorphicStreaks = 0; s.ChromaticAberration = 0; s.FilmGrain = 0; s.ShowDust = false;
        return s;
    }

    internal static PianoVisualSettings GreenScreen()
    {
        var s = Base("Green Screen");
        s.BackgroundMode = "ChromaGreen"; s.NoteStyle = "Neon"; s.ColorMode = "Gradient"; s.Palette = "Violet"; s.NoteColorStart = "#7B5CFF"; s.NoteColorEnd = "#F05CFF";
        s.ShowHalo = false; s.ShowLightBeams = false; s.HorizonGlow = 0; s.Vignette = 0; s.ShowStars = false; s.BackgroundGradient = false; s.BackgroundGuide = false;
        s.BloomIntensity = 40; s.KeyGlowRadius = 0; s.ShowFlame = true; s.ShowImpactRings = false; s.KeyboardStyle = "Studio";
        return s;
    }
}

/// <summary>Reads and writes user presets as individual JSON files.</summary>
internal sealed class VisualPresetStore(string directory)
{
    internal string Directory { get; } = directory;

    internal static VisualPresetStore Default { get; } = new(Path.Combine(PianoVisualSettingsStore.SettingsDirectory, "presets"));

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
                    var settings = PianoVisualSettings.FromJson(File.ReadAllText(file));
                    var name = Path.GetFileNameWithoutExtension(file);
                    settings.PresetName = name;
                    presets.Add(new VisualPreset(name, "User preset · " + Path.GetFileName(file), false, settings, file));
                }
                catch { /* a corrupt file should not hide the remaining presets */ }
            }
        }
        catch { }
        return presets;
    }

    internal VisualPreset Save(string name, PianoVisualSettings settings)
    {
        var safe = SanitizeName(name);
        System.IO.Directory.CreateDirectory(Directory);
        var copy = settings.Clone();
        copy.PresetName = safe;
        copy.BackgroundImagePath = "";
        var path = Path.Combine(Directory, safe + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, copy.ToJson());
        File.Move(temp, path, true);
        return new VisualPreset(safe, "User preset · " + safe + ".json", false, copy, path);
    }

    internal bool Delete(VisualPreset preset)
    {
        if (preset.BuiltIn || preset.FilePath is null) return false;
        try { if (File.Exists(preset.FilePath)) File.Delete(preset.FilePath); return true; } catch { return false; }
    }

    internal static void Export(PianoVisualSettings settings, string path)
    {
        var copy = settings.Clone();
        copy.BackgroundImagePath = "";
        File.WriteAllText(path, copy.ToJson());
    }

    internal static VisualPreset Import(string path)
    {
        var settings = PianoVisualSettings.FromJson(File.ReadAllText(path));
        var name = SanitizeName(string.IsNullOrWhiteSpace(settings.PresetName) || settings.PresetName == "Custom" ? Path.GetFileNameWithoutExtension(path) : settings.PresetName);
        settings.PresetName = name;
        return new VisualPreset(name, "Imported · " + Path.GetFileName(path), false, settings);
    }

    internal static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? "").Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "My preset";
        return cleaned.Length > 40 ? cleaned[..40].Trim() : cleaned;
    }
}
