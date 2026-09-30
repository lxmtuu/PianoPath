using System.Numerics;
using WpfColor = System.Windows.Media.Color;

namespace PianoPath;

/// <summary>
/// An immutable, render-thread-safe picture of the look: every <see cref="PianoVisualSettings"/> value
/// the GPU stage reads, already converted to numbers, plus the note colour table.
/// </summary>
/// <remarks>
/// The settings object is mutated by the UI thread while the user drags a slider, so the render thread
/// must never read it directly. <see cref="PianoStage.SetVisualSettings"/> builds a new look on the UI
/// thread and publishes the reference; the render thread picks it up at the start of its next frame.
/// The colour table is produced by the stage's own <c>NoteColor</c>, so the GPU and the software
/// renderer can never disagree about which colour a note is.
/// </remarks>
internal sealed class GpuLook
{
    internal const int TrackSlots = 8;

    // ---- note roll ----
    public bool ShowNotes { get; init; } = true;
    public int NoteStyle { get; init; } = 1;
    public float NoteWidth { get; init; } = .8f;
    public float NoteMinLength { get; init; } = 16;
    public float NoteGap { get; init; } = 2;
    public float NoteRoundness { get; init; } = .7f;
    public float NoteEdgeWidth { get; init; } = .55f;
    public float NoteGlow { get; init; } = 1;
    public float NoteTint { get; init; } = .78f;
    public float NoteEdge { get; init; } = .88f;
    public float NoteHeadGlow { get; init; } = .4f;
    public float NoteRefraction { get; init; } = .35f;
    public float NoteTexture { get; init; } = .6f;
    public bool Notes3D { get; init; } = true;
    /// <summary>Song note speed in DIPs per second, identical to the software stage.</summary>
    public float SongFallSpeed { get; init; } = 258;
    /// <summary>Live trail speed in DIPs per second (the software stage uses NoteFallSpeed directly).</summary>
    public float LiveFallSpeed { get; init; } = 550;
    public bool Rising { get; init; }
    public bool VelocityColor { get; init; }
    public float VelocityColorAmount { get; init; }
    public bool HoldColorCycle { get; init; }
    public float HoldColorCycleSpeed { get; init; }
    public bool FallingPulse { get; init; }
    public float FallingPulseRate { get; init; }
    public bool RainbowTrail { get; init; }

    // ---- particles ----
    public bool ShowEmbers { get; init; } = true;
    public bool ShowWisps { get; init; }
    public bool ShowFlame { get; init; } = true;
    public bool ShowImpactRings { get; init; } = true;
    public bool ShowImpactFlash { get; init; } = true;
    public string ImpactBurst { get; init; } = "Embers";
    public string ImpactWave { get; init; } = "Ring";
    public float ImpactWaveIntensity { get; init; } = 1;
    public float ImpactFlashIntensity { get; init; } = .8f;
    public float RingSize { get; init; } = .5f;
    public float ParticleAmount { get; init; } = 34;
    public float ParticleResponse { get; init; } = 55;
    public float ParticleVelocity { get; init; } = 210;
    public float ParticleSpeed { get; init; } = 1;
    public float ParticleRandomness { get; init; } = .38f;
    public float ParticleSpread { get; init; } = .72f;
    public float ParticleLife { get; init; } = .9f;
    public float ParticleLifeRandomness { get; init; } = .45f;
    public float ParticleSize { get; init; } = 1.6f;
    public float ParticleSizeRandomness { get; init; } = .8f;
    public float ParticleGlow { get; init; } = .85f;
    public float EmitterSize { get; init; } = .24f;
    public float Gravity { get; init; } = 290;
    public float Drag { get; init; } = .26f;
    public float VectorField { get; init; } = .36f;
    public float FieldScale { get; init; } = 100;
    public float EvolutionSpeed { get; init; } = 1;
    public float PhysicsTimeFactor { get; init; } = 1;
    public float WispAmount { get; init; } = 45;
    public float WispSpeed { get; init; } = 170;
    public float WispHeight { get; init; } = .55f;
    public float WispWidth { get; init; } = .4f;
    public float WispTurbulence { get; init; } = .55f;
    public float WispGlow { get; init; } = .8f;
    public float FlameIntensity { get; init; } = .7f;
    public float FlameHeight { get; init; } = .6f;
    public bool FlameNoteColor { get; init; }

    // ---- keyboard ----
    public bool ShowKeys { get; init; } = true;
    public bool AnimateKeys { get; init; } = true;
    public bool ShowKeyFelt { get; init; }
    public bool ShowHalo { get; init; } = true;
    public float HaloIntensity { get; init; } = .9f;
    public int KeyboardStyle { get; init; } = 1;
    /// <summary>Keyboard height as a fraction of the stage height (the software stage's formula, evaluated on the UI thread).</summary>
    public float KeyboardFraction { get; init; } = .205f;
    public float KeyOverhang { get; init; } = .18f;
    public float KeyPressDepth { get; init; } = .4f;
    public float KeyLighting { get; init; } = .32f;
    public float KeyGlowRadius { get; init; } = .5f;
    public bool PressedKeyFixed { get; init; }
    public float ShaderKeyLight { get; init; } = .92f;
    public float ShaderShadows { get; init; } = .78f;
    public float ShaderAmbientOcclusion { get; init; } = .7f;
    public float ShaderGloss { get; init; } = .72f;
    public float ShaderRimLight { get; init; } = .7f;
    public float ShaderEmissive { get; init; } = .95f;
    public float ShaderExposure { get; init; } = 1.05f;
    public float ShaderCameraTilt { get; init; } = .48f;
    public bool ShaderFilmic { get; init; } = true;

    // ---- scene ----
    public bool ShowBackground { get; init; } = true;
    public bool BackgroundGradient { get; init; }
    public bool ShowStars { get; init; }
    public float StarDensity { get; init; } = .5f;
    public bool Chroma { get; init; }
    public float BackgroundDim { get; init; } = .3f;
    public float HorizonGlow { get; init; } = .35f;
    public bool ShowLightBeams { get; init; }
    public float BeamIntensity { get; init; } = .45f;
    public float Vignette { get; init; } = .25f;
    public float Saturation { get; init; } = 1;
    public float Contrast { get; init; } = 1;
    public float BloomIntensity { get; init; } = .8f;
    public float BloomSize { get; init; } = .7f;
    public float CameraZoom { get; init; } = 1;
    public float CameraOffset { get; init; } = .5f;
    public float CameraParallax { get; init; } = .24f;
    public bool TempoSync { get; init; }
    public float TempoSyncAmount { get; init; }

    // ---- colours (sRGB 0..1) ----
    public Vector3 BackgroundColor { get; init; }
    public Vector3 HaloColor { get; init; } = new(.78f, .43f, 1f);
    public Vector3 PressedKeyColor { get; init; } = new(.97f, .51f, 1f);
    public Vector3 KeyFeltColor { get; init; } = new(.77f, .11f, .29f);
    /// <summary>Note colour per pitch (0..127) and track slot (0..7), sRGB, already graded by saturation/contrast on the stage.</summary>
    public Vector3[] NoteColors { get; init; } = new Vector3[128 * TrackSlots];

    internal Vector3 NoteColor(int pitch, int track) => NoteColors[Math.Clamp(pitch, 0, 127) * TrackSlots + ((track % TrackSlots) + TrackSlots) % TrackSlots];

    internal static Vector3 ToVector(WpfColor color) => new(color.R / 255f, color.G / 255f, color.B / 255f);

    internal static Vector3 ParseHex(string? hex, Vector3 fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            return ToVector(color);
        }
        catch { return fallback; }
    }

    /// <summary>
    /// Builds the look from the settings. <paramref name="noteColor"/> is the stage's own colour function
    /// (note colour with every modulator and the saturation/contrast grade applied).
    /// </summary>
    internal static GpuLook From(PianoVisualSettings s, Func<int, int, WpfColor> noteColor, double keyboardFraction)
    {
        var colors = new Vector3[128 * TrackSlots];
        for (var pitch = 0; pitch < 128; pitch++)
            for (var track = 0; track < TrackSlots; track++)
                colors[pitch * TrackSlots + track] = ToVector(noteColor(pitch, track));
        static float P(double value) => (float)(value / 100);
        return new GpuLook
        {
            ShowNotes = s.ShowNotes,
            NoteStyle = Math.Max(0, Array.IndexOf(PianoVisualSettings.NoteStyles, s.NoteStyle)),
            NoteWidth = P(s.NoteWidth), NoteMinLength = (float)s.NoteMinLength, NoteGap = (float)Math.Min(s.NoteGap, 12),
            NoteRoundness = P(s.NoteRoundness), NoteEdgeWidth = P(s.NoteEdgeWidth), NoteGlow = P(s.NoteGlow), NoteTint = P(s.NoteTint),
            NoteEdge = P(s.NoteEdge), NoteHeadGlow = P(s.NoteHeadGlow), NoteRefraction = P(s.NoteRefraction), NoteTexture = P(s.NoteTexture),
            Notes3D = s.Notes3D,
            SongFallSpeed = (float)(258 * s.NoteFallSpeed / 550), LiveFallSpeed = (float)s.NoteFallSpeed,
            Rising = s.NoteDirection == "Up",
            VelocityColor = s.VelocityColor, VelocityColorAmount = P(s.VelocityColorAmount),
            HoldColorCycle = s.HoldColorCycle, HoldColorCycleSpeed = (float)s.HoldColorCycleSpeed,
            FallingPulse = s.FallingPulse, FallingPulseRate = P(s.FallingPulseRate),
            RainbowTrail = s.FallingTrail == "Rainbow",

            ShowEmbers = s.ShowEmbers, ShowWisps = s.ShowWisps, ShowFlame = s.ShowFlame && s.FlameIntensity > 0,
            ShowImpactRings = s.ShowImpactRings, ShowImpactFlash = s.ShowImpactFlash,
            ImpactBurst = s.ZoneSplit ? "Zone" : s.ImpactBurst, ImpactWave = s.ImpactWave, ImpactWaveIntensity = P(s.ImpactWaveIntensity),
            ImpactFlashIntensity = P(s.ImpactFlashIntensity), RingSize = P(s.RingSize),
            ParticleAmount = (float)s.ParticleAmount, ParticleResponse = (float)s.ParticleResponse, ParticleVelocity = (float)s.ParticleVelocity,
            ParticleSpeed = P(s.ParticleSpeed), ParticleRandomness = P(s.ParticleRandomness), ParticleSpread = P(s.ParticleSpread),
            ParticleLife = (float)s.ParticleLife, ParticleLifeRandomness = P(s.ParticleLifeRandomness), ParticleSize = (float)s.ParticleSize,
            ParticleSizeRandomness = P(s.ParticleSizeRandomness), ParticleGlow = P(s.ParticleGlow), EmitterSize = P(s.EmitterSize),
            Gravity = (float)s.Gravity, Drag = P(s.Drag), VectorField = P(s.VectorField), FieldScale = (float)Math.Max(1, s.FieldScale),
            EvolutionSpeed = P(s.EvolutionSpeed), PhysicsTimeFactor = P(s.PhysicsTimeFactor),
            WispAmount = (float)s.WispAmount, WispSpeed = (float)s.WispSpeed, WispHeight = P(s.WispHeight), WispWidth = P(s.WispWidth),
            WispTurbulence = P(s.WispTurbulence), WispGlow = P(s.WispGlow),
            FlameIntensity = P(s.FlameIntensity), FlameHeight = P(s.FlameHeight), FlameNoteColor = s.FlameColorMode == "Note",

            ShowKeys = s.ShowKeys, AnimateKeys = s.AnimateKeys, ShowKeyFelt = s.ShowKeyFelt, ShowHalo = s.ShowHalo, HaloIntensity = P(s.HaloIntensity),
            KeyboardStyle = Math.Max(0, Array.IndexOf(PianoVisualSettings.KeyboardStyles, s.KeyboardStyle)),
            KeyboardFraction = (float)keyboardFraction, KeyOverhang = P(s.KeyOverhang), KeyPressDepth = P(s.KeyPressDepth),
            KeyLighting = P(s.KeyLighting), KeyGlowRadius = P(s.KeyGlowRadius), PressedKeyFixed = s.PressedKeyColorMode == "Fixed",
            ShaderKeyLight = P(s.ShaderKeyLight), ShaderShadows = P(s.ShaderShadows), ShaderAmbientOcclusion = P(s.ShaderAmbientOcclusion),
            ShaderGloss = P(s.ShaderGloss), ShaderRimLight = P(s.ShaderRimLight), ShaderEmissive = P(s.ShaderEmissive),
            ShaderExposure = P(s.ShaderExposure), ShaderCameraTilt = P(s.ShaderCameraTilt), ShaderFilmic = s.ShaderFilmic,

            ShowBackground = s.ShowBackground, BackgroundGradient = s.BackgroundGradient, ShowStars = s.ShowStars, StarDensity = P(s.StarDensity),
            Chroma = s.BackgroundMode == "ChromaGreen", BackgroundDim = P(s.BackgroundDim),
            HorizonGlow = P(s.HorizonGlow), ShowLightBeams = s.ShowLightBeams, BeamIntensity = P(s.BeamIntensity),
            Vignette = P(s.Vignette), Saturation = P(s.Saturation), Contrast = P(s.Contrast),
            BloomIntensity = P(s.BloomIntensity), BloomSize = P(s.BloomSize),
            CameraZoom = P(s.CameraZoom), CameraOffset = P(s.CameraOffset), CameraParallax = P(s.CameraParallax),
            TempoSync = s.TempoSync, TempoSyncAmount = P(s.TempoSyncAmount),

            BackgroundColor = ParseHex(s.BackgroundColor, Vector3.Zero),
            HaloColor = ParseHex(s.HaloColor, new Vector3(.78f, .43f, 1f)),
            PressedKeyColor = ParseHex(s.PressedKeyColor, new Vector3(.97f, .51f, 1f)),
            KeyFeltColor = ParseHex(s.KeyFeltColor, new Vector3(.77f, .11f, .29f)),
            NoteColors = colors
        };
    }
}
