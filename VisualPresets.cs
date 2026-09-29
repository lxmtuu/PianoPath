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
        s.HaloColor = "#C66EFF"; s.HaloIntensity = 95; s.NoteGlow = 110; s.NoteEdge = 120; s.NoteEdgeWidth = 62; s.NoteTint = 30; s.NoteHeadGlow = 55;
        s.ShowFlame = true; s.FlameIntensity = 75; s.FlameHeight = 60; s.FlameColorMode = "Warm"; s.ShowWisps = false; s.ShowImpactRings = true;
        s.KeyboardStyle = "Studio"; s.PressedKeyColorMode = "Note"; s.KeyGlowRadius = 55; s.BloomIntensity = 80; s.Vignette = 30; s.HorizonGlow = 35;
        s.ShadingQuality = "Balanced"; s.ShaderCameraTilt = 48; s.ShaderGloss = 72; s.ShaderShadows = 78; s.ShaderEmissive = 85;
        return s;
    }

    internal static PianoVisualSettings Inferno()
    {
        var s = Base("Inferno");
        s.NoteStyle = "Fire"; s.ColorMode = "Gradient"; s.Palette = "Fire"; s.NoteColorStart = "#FF3B12"; s.NoteColorEnd = "#FFB02E";
        s.HaloColor = "#FF4A1C"; s.HaloIntensity = 135; s.PressedKeyColor = "#FF3A1A"; s.PressedKeyColorMode = "Fixed"; s.NoteTexture = 75; s.NoteGlow = 130; s.NoteEdge = 110;
        s.NoteEdgeWidth = 40; s.NoteTint = 95; s.NoteHeadGlow = 70; s.NoteRoundness = 45; s.ParticleAmount = 48; s.ParticleVelocity = 260; s.ParticleSpread = 85;
        s.ParticleLife = .8; s.ParticleSize = 2.6; s.Gravity = 420; s.ShowFlame = true; s.FlameIntensity = 100; s.FlameHeight = 85; s.FlameColorMode = "Warm";
        s.ShowImpactRings = true; s.RingSize = 60; s.KeyboardStyle = "Studio"; s.KeyGlowRadius = 90; s.KeyLighting = 55; s.ShowKeyFelt = true; s.KeyFeltColor = "#FF2E3A";
        s.HorizonGlow = 80; s.BeamIntensity = 55; s.BloomIntensity = 110; s.BloomSize = 90; s.Vignette = 45; s.Saturation = 115;
        s.ShadingQuality = "Cinematic"; s.ShaderCameraTilt = 34; s.ShaderKeyLight = 78; s.ShaderGloss = 66; s.ShaderShadows = 88; s.ShaderEmissive = 120; s.ShaderRimLight = 84;
        return s;
    }

    internal static PianoVisualSettings AuroraRainbow()
    {
        var s = Base("Aurora Rainbow");
        s.NoteStyle = "Solid"; s.ColorMode = "RainbowPitch"; s.Palette = "Spectrum"; s.HaloColor = "#8CFFE9"; s.NoteGlow = 70; s.NoteEdge = 60; s.NoteEdgeWidth = 30;
        s.NoteTint = 88; s.NoteRoundness = 60; s.NoteHeadGlow = 35; s.ShowWisps = true; s.WispAmount = 70; s.WispHeight = 70; s.WispTurbulence = 65; s.WispGlow = 110;
        s.ShowFlame = false; s.ParticleAmount = 10; s.ParticleVelocity = 90; s.Gravity = 60; s.ShowImpactRings = false; s.ShowLightBeams = true; s.BeamIntensity = 70;
        s.KeyboardStyle = "Glass"; s.PressedKeyColorMode = "Note"; s.KeyGlowRadius = 70; s.BloomIntensity = 70; s.Vignette = 35; s.HorizonGlow = 25;
        s.ShadingQuality = "Balanced"; s.ShaderCameraTilt = 56; s.ShaderGloss = 88; s.ShaderShadows = 62; s.ShaderEmissive = 70;
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
        s.ShadingQuality = "Cinematic"; s.ShaderCameraTilt = 62; s.ShaderGloss = 94; s.ShaderShadows = 58; s.ShaderExposure = 112; s.ShaderEmissive = 55;
        return s;
    }

    internal static PianoVisualSettings TwoHands()
    {
        var s = Base("Two Hands");
        s.NoteStyle = "Glass"; s.ColorMode = "PerHand"; s.LeftHandColor = "#3FA9FF"; s.RightHandColor = "#FF6FD8"; s.HaloColor = "#B58CFF"; s.HandSplitPitch = 60;
        s.NoteGlow = 60; s.NoteEdge = 80; s.NoteEdgeWidth = 30; s.NoteTint = 85; s.ShowNoteLabels = true; s.KeyLabels = "C"; s.ParticleAmount = 12; s.ShowFlame = false;
        s.ShowImpactRings = true; s.KeyboardStyle = "Studio"; s.PressedKeyColorMode = "Note"; s.BloomIntensity = 50; s.Vignette = 20;
        s.ShadingQuality = "Balanced"; s.ShaderCameraTilt = 44; s.ShaderEmissive = 95;
        return s;
    }

    internal static PianoVisualSettings ClassicRoll()
    {
        var s = Base("Classic Roll");
        s.NoteStyle = "Solid"; s.ColorMode = "PerTrack"; s.NoteGlow = 20; s.NoteEdge = 40; s.NoteEdgeWidth = 20; s.NoteTint = 100; s.NoteRoundness = 25; s.Notes3D = false;
        s.NoteHeadGlow = 0; s.ShowEmbers = false; s.ShowFlame = false; s.ShowWisps = false; s.ShowImpactRings = false; s.ShowLightBeams = false; s.ShowHalo = true;
        s.HaloColor = "#FFFFFF"; s.HaloIntensity = 45; s.KeyboardStyle = "Classic"; s.KeyLighting = 20; s.KeyGlowRadius = 0; s.BloomIntensity = 0; s.Vignette = 0; s.HorizonGlow = 0;
        s.ShadingQuality = "Fast"; s.ShaderCameraTilt = 30; s.ShaderKeyLight = 104; s.ShaderGloss = 48; s.ShaderEmissive = 45;
        return s;
    }

    internal static PianoVisualSettings GreenScreen()
    {
        var s = Base("Green Screen");
        s.BackgroundMode = "ChromaGreen"; s.NoteStyle = "Neon"; s.ColorMode = "Gradient"; s.Palette = "Violet"; s.NoteColorStart = "#7B5CFF"; s.NoteColorEnd = "#F05CFF";
        s.ShowHalo = false; s.ShowLightBeams = false; s.HorizonGlow = 0; s.Vignette = 0; s.ShowStars = false; s.BackgroundGradient = false; s.BackgroundGuide = false;
        s.BloomIntensity = 40; s.KeyGlowRadius = 0; s.ShowFlame = true; s.ShowImpactRings = false; s.KeyboardStyle = "Studio";
        s.ShadingQuality = "Off";
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
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
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
