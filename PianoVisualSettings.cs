using System.IO;
using System.Text.Json;

namespace PianoPath;

/// <summary>Serializable, user-editable live-stage and note rendering controls.</summary>
internal sealed class PianoVisualSettings
{
    public bool ShowBackground { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
    public bool ShowEmbers { get; set; } = true;
    public bool ShowHalo { get; set; } = true;
    public bool ShowFlame { get; set; } = true;
    public bool ShowKeys { get; set; } = true;
    public bool ShowWatermark { get; set; } = false;
    public bool ShowCounter { get; set; } = false;
    public bool AnimateKeys { get; set; } = true;
    public bool Notes3D { get; set; } = true;
    public bool BackgroundGuide { get; set; }
    public bool BackgroundGradient { get; set; }
    public bool ShowStars { get; set; }
    public int BackgroundAppearanceVersion { get; set; }
    public string Palette { get; set; } = "Spectrum";
    public string NoteColorStart { get; set; } = "#43E6FF";
    public string NoteColorEnd { get; set; } = "#D95EFF";
    public string HaloColor { get; set; } = "#C66EFF";
    public string BackgroundImagePath { get; set; } = "";
    public double NoteTint { get; set; } = 78;
    public double NoteGlow { get; set; } = 86;
    public double NoteEdge { get; set; } = 88;
    public double NoteRefraction { get; set; } = 35;
    public double NoteRoundness { get; set; } = 70;
    public double NoteEdgeWidth { get; set; } = 55;
    public double NoteFallSpeed { get; set; } = 550;
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
    public double CameraParallax { get; set; } = 24;
    public double CameraZoom { get; set; } = 100;
    public double CameraOffset { get; set; } = 50;
    public double BackgroundDim { get; set; } = 30;
    public double Saturation { get; set; } = 100;
    public double Contrast { get; set; } = 100;
    public double BloomIntensity { get; set; } = 65;
    public double BloomSize { get; set; } = 62;
    public double KeyLighting { get; set; } = 32;
    public double KeyOverhang { get; set; } = 18;

    internal static PianoVisualSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<PianoVisualSettings>(json) ?? new PianoVisualSettings();
        settings.ApplyMigrations();
        settings.Clamp();
        return settings;
    }

    internal string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    internal void Clamp()
    {
        NoteTint = Math.Clamp(NoteTint, 0, 100); NoteGlow = Math.Clamp(NoteGlow, 0, 200); NoteEdge = Math.Clamp(NoteEdge, 0, 200);
        NoteRefraction = Math.Clamp(NoteRefraction, 0, 100); NoteRoundness = Math.Clamp(NoteRoundness, 0, 100); NoteEdgeWidth = Math.Clamp(NoteEdgeWidth, 0, 100);
        NoteFallSpeed = Math.Clamp(NoteFallSpeed, 100, 1000); EmitterSize = Math.Clamp(EmitterSize, 0, 100); Spiral = Math.Clamp(Spiral, 0, 100);
        ParticleSpeed = Math.Clamp(ParticleSpeed, 0, 300); ParticleAmount = Math.Clamp(ParticleAmount, 0, 120); ParticleVelocity = Math.Clamp(ParticleVelocity, 0, 800);
        ParticleRandomness = Math.Clamp(ParticleRandomness, 0, 100); ParticleSpread = Math.Clamp(ParticleSpread, 0, 100); ParticleResponse = Math.Clamp(ParticleResponse, 0, 100);
        ParticleLife = Math.Clamp(ParticleLife, .05, 3); ParticleLifeRandomness = Math.Clamp(ParticleLifeRandomness, 0, 100); ParticleSize = Math.Clamp(ParticleSize, .2, 16);
        ParticleSizeRandomness = Math.Clamp(ParticleSizeRandomness, 0, 100); ParticleGlow = Math.Clamp(ParticleGlow, 0, 200); Gravity = Math.Clamp(Gravity, -600, 1200);
        Drag = Math.Clamp(Drag, 0, 100); VectorField = Math.Clamp(VectorField, 0, 1000); FieldScale = Math.Clamp(FieldScale, 10, 300); EvolutionSpeed = Math.Clamp(EvolutionSpeed, 0, 400);
        PhysicsTimeFactor = Math.Clamp(PhysicsTimeFactor, 10, 300); CameraParallax = Math.Clamp(CameraParallax, 0, 100); CameraZoom = Math.Clamp(CameraZoom, 65, 150);
        CameraOffset = Math.Clamp(CameraOffset, 0, 100); BackgroundDim = Math.Clamp(BackgroundDim, 0, 100); Saturation = Math.Clamp(Saturation, 0, 200);
        Contrast = Math.Clamp(Contrast, 0, 200); BloomIntensity = Math.Clamp(BloomIntensity, 0, 150); BloomSize = Math.Clamp(BloomSize, 0, 150);
        KeyLighting = Math.Clamp(KeyLighting, 0, 100); KeyOverhang = Math.Clamp(KeyOverhang, 0, 100);
        if (Palette is not ("Spectrum" or "Aurora" or "Fire" or "Ocean" or "Violet" or "Custom")) Palette = "Spectrum";
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
    }
}

internal static class PianoVisualSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    internal static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keyflow", "visual-settings.json");

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
