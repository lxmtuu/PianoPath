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
    /// <summary>Note names printed inside the bars when there is room (Style → SHAPE &amp; STYLE).</summary>
    public bool ShowNoteLabels { get; init; }
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
    /// <summary>Rainbow body hue speed in degrees per second (the software stage's 20 + intensity × 0.8).</summary>
    public float RainbowHueSpeed { get; init; } = 76;
    /// <summary>Trail drawn behind travelling notes: None, Glow, Sparkles, Speed Lines, Blur, Ribbon, Rainbow or Stream.</summary>
    public string FallingTrail { get; init; } = "None";
    public float FallingTrailIntensity { get; init; } = .7f;
    public float FallingTrailLength { get; init; } = .55f;
    public bool FallingGhost { get; init; }
    public float FallingGhostAmount { get; init; } = .4f;
    /// <summary>A light sheen sweeping along every travelling bar (Style → GLOW &amp; EDGES).</summary>
    public bool NoteShimmer { get; init; }
    public float NoteShimmerAmount { get; init; } = .45f;
    /// <summary>Bright pulses of the halo colour travelling along the hit line (Style → HIT LINE).</summary>
    public bool HaloPulse { get; init; }
    public float HaloPulseIntensity { get; init; } = .5f;
    public string HaloPulseStyle { get; init; } = "Pulse";
    public float HaloPulseSpeed { get; init; } = 1.0f;
    /// <summary>Occasional meteors crossing the sky behind the notes (Style → ATMOSPHERE).</summary>
    public bool ShootingStars { get; init; }
    public float ShootingStarsAmount { get; init; } = .5f;
    /// <summary>A glow gathering where a note is about to land (Style → IMPACT; off for chroma keying).</summary>
    public bool NoteLandingGlow { get; init; } = true;
    public float NoteLandingGlowAmount { get; init; } = .45f;
    public bool HoldBar { get; init; }
    public float HoldBarIntensity { get; init; } = .6f;
    public bool HoldBreath { get; init; }
    public float HoldBreathRate { get; init; } = .35f;
    public bool HoldVibration { get; init; }
    public float HoldVibrationAmount { get; init; } = .4f;
    public bool HoldElectricArc { get; init; }
    public float HoldArcIntensity { get; init; } = .7f;

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
    /// <summary>The raw 0-100 ring size slider, for the formulas shared with the software stage.</summary>
    public float RingSizeRaw { get; init; } = 50;
    /// <summary>0 = Flash, 1 = Lightning, 2 = Plasma.</summary>
    public int ImpactFlashStyle { get; init; }
    /// <summary>What the note becomes on impact: None, Shatter, Melt, Absorb, Bounce or Morph.</summary>
    public string ImpactMorph { get; init; } = "None";
    public float ImpactMorphIntensity { get; init; } = .7f;
    /// <summary>Zone Split: hits below this pitch erupt embers, hits at or above it splash.</summary>
    public int ZoneSplitPitch { get; init; } = 60;
    /// <summary>Release phase: Fade, Float Up, Dissolve, Smoke, Snap Back or Echo Rings.</summary>
    public string ReleaseEffect { get; init; } = "Fade";
    public float ReleaseIntensity { get; init; } = .7f;
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
    /// <summary>Twists the burst direction over time (0..1), as the software stage's Spiral slider.</summary>
    public float Spiral { get; init; } = .3f;
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
    /// <summary>The fallboard's soft shadow across the top of the keys.</summary>
    public bool ShowKeyShadow { get; init; } = true;
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
    /// <summary>Note names engraved on the white keys: 0 = none, 1 = only the Cs, 2 = every white key.</summary>
    public int KeyLabels { get; init; } = 1;
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
    public string BackgroundMotion { get; init; } = "None";
    public float BackgroundMotionAmount { get; init; } = .45f;
    public float BackgroundMotionSpeed { get; init; } = 1.0f;
    public bool ShowStars { get; init; }
    public float StarDensity { get; init; } = .5f;
    public bool Chroma { get; init; }
    public float BackgroundDim { get; init; } = .3f;
    public float HorizonGlow { get; init; } = .35f;
    public bool ShowLightBeams { get; init; }
    public bool BackgroundGuide { get; init; }
    public bool ShowPetals { get; init; }
    public float PetalAmount { get; init; } = 55;

    // ---- ambient layers (behind the notes); speed is the software stage's .25 + slider × 1.75 ----
    public string AmbientEnergy { get; init; } = "None";
    public float AmbientEnergyAmount { get; init; } = .6f;
    public float AmbientEnergySpeed { get; init; } = 1.125f;
    public string AmbientNature { get; init; } = "None";
    public float AmbientNatureAmount { get; init; } = .6f;
    public float AmbientNatureSpeed { get; init; } = 1.125f;
    public string AmbientLight { get; init; } = "None";
    public float AmbientLightAmount { get; init; } = .6f;
    public float AmbientLightSpeed { get; init; } = 1.125f;
    public string AmbientCosmic { get; init; } = "None";
    public float AmbientCosmicAmount { get; init; } = .6f;
    public float AmbientCosmicSpeed { get; init; } = 1.125f;
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
    public bool PedalGlow { get; init; }
    /// <summary>Audio reactive: note onsets pump the note glow and the hit line (0..1 amount).</summary>
    public bool AudioReactive { get; init; }
    public float AudioReactiveAmount { get; init; }
    public float PedalGlowIntensity { get; init; }

    // ---- colours (sRGB 0..1) ----
    public Vector3 BackgroundColor { get; init; }
    public Vector3 HaloColor { get; init; } = new(.78f, .43f, 1f);
    public Vector3 PressedKeyColor { get; init; } = new(.97f, .51f, 1f);
    public Vector3 KeyFeltColor { get; init; } = new(.77f, .11f, .29f);
    public Vector3 PetalColor { get; init; } = new(1f, .7f, .81f);
    public Vector3 BackgroundMotionColor { get; init; } = new(.48f, .36f, 1f);
    public Vector3 AmbientLightColor { get; init; } = new(.48f, .36f, 1f);
    /// <summary>Note colour per pitch (0..127) and track slot (0..7), sRGB, already graded by saturation/contrast on the stage.</summary>
    public Vector3[] NoteColors { get; init; } = new Vector3[128 * TrackSlots];

    internal Vector3 NoteColor(int pitch, int track) => NoteColors[Math.Clamp(pitch, 0, 127) * TrackSlots + ((track % TrackSlots) + TrackSlots) % TrackSlots];

    internal static Vector3 ToVector(WpfColor color) => new(color.R / 255f, color.G / 255f, color.B / 255f);

    internal static WpfColor ParseWpf(string? hex, WpfColor fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!; }
        catch { return fallback; }
    }

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
    internal static GpuLook From(PianoVisualSettings s, Func<int, int, WpfColor> noteColor, double keyboardFraction, Func<WpfColor, WpfColor>? adjustColor = null)
    {
        var grade = adjustColor ?? (c => c);
        var colors = new Vector3[128 * TrackSlots];
        for (var pitch = 0; pitch < 128; pitch++)
            for (var track = 0; track < TrackSlots; track++)
                colors[pitch * TrackSlots + track] = ToVector(noteColor(pitch, track));
        static float P(double value) => (float)(value / 100);
        static float Speed(double value) => (float)(.25 + value / 100 * 1.75);
        return new GpuLook
        {
            ShowNotes = s.ShowNotes,
            NoteStyle = Math.Max(0, Array.IndexOf(PianoVisualSettings.NoteStyles, s.NoteStyle)),
            NoteWidth = P(s.NoteWidth), NoteMinLength = (float)s.NoteMinLength, NoteGap = (float)Math.Min(s.NoteGap, 12),
            NoteRoundness = P(s.NoteRoundness), NoteEdgeWidth = P(s.NoteEdgeWidth), NoteGlow = P(s.NoteGlow), NoteTint = P(s.NoteTint),
            NoteEdge = P(s.NoteEdge), NoteHeadGlow = P(s.NoteHeadGlow), NoteRefraction = P(s.NoteRefraction), NoteTexture = P(s.NoteTexture),
            Notes3D = s.Notes3D, ShowNoteLabels = s.ShowNoteLabels,
            SongFallSpeed = (float)(258 * s.NoteFallSpeed / 550), LiveFallSpeed = (float)s.NoteFallSpeed,
            Rising = s.NoteDirection == "Up",
            VelocityColor = s.VelocityColor, VelocityColorAmount = P(s.VelocityColorAmount),
            HoldColorCycle = s.HoldColorCycle, HoldColorCycleSpeed = (float)s.HoldColorCycleSpeed,
            FallingPulse = s.FallingPulse, FallingPulseRate = P(s.FallingPulseRate),
            RainbowTrail = s.FallingTrail == "Rainbow", RainbowHueSpeed = (float)(20 + s.FallingTrailIntensity * .8),
            FallingTrail = s.FallingTrail, FallingTrailIntensity = P(s.FallingTrailIntensity), FallingTrailLength = P(s.FallingTrailLength),
            FallingGhost = s.FallingGhost, FallingGhostAmount = P(s.FallingGhostAmount),
            NoteShimmer = s.NoteShimmer, NoteShimmerAmount = P(s.NoteShimmerAmount),
            HaloPulse = s.HaloPulse, HaloPulseIntensity = P(s.HaloPulseIntensity), HaloPulseStyle = s.HaloPulseStyle, HaloPulseSpeed = Speed(s.HaloPulseSpeed),
            ShootingStars = s.ShootingStars, ShootingStarsAmount = P(s.ShootingStarsAmount),
            NoteLandingGlow = s.NoteLandingGlow, NoteLandingGlowAmount = P(s.NoteLandingGlowAmount),
            HoldBar = s.HoldBar, HoldBarIntensity = P(s.HoldBarIntensity), HoldBreath = s.HoldBreath, HoldBreathRate = P(s.HoldBreathRate),
            HoldVibration = s.HoldVibration, HoldVibrationAmount = P(s.HoldVibrationAmount),
            HoldElectricArc = s.HoldElectricArc, HoldArcIntensity = P(s.HoldArcIntensity),

            ShowEmbers = s.ShowEmbers, ShowWisps = s.ShowWisps, ShowFlame = s.ShowFlame && s.FlameIntensity > 0,
            ShowImpactRings = s.ShowImpactRings, ShowImpactFlash = s.ShowImpactFlash,
            ImpactBurst = s.ZoneSplit ? "Zone" : s.ImpactBurst, ImpactWave = s.ImpactWave, ImpactWaveIntensity = P(s.ImpactWaveIntensity),
            ImpactFlashIntensity = P(s.ImpactFlashIntensity), RingSize = P(s.RingSize), RingSizeRaw = (float)s.RingSize,
            ImpactFlashStyle = s.ImpactFlashStyle switch { "Lightning" => 1, "Plasma" => 2, _ => 0 },
            ImpactMorph = s.ImpactMorph, ImpactMorphIntensity = P(s.ImpactMorphIntensity), ZoneSplitPitch = (int)Math.Round(s.ZoneSplitPitch),
            ReleaseEffect = s.ReleaseEffect, ReleaseIntensity = P(s.ReleaseIntensity),
            ParticleAmount = (float)s.ParticleAmount, ParticleResponse = (float)s.ParticleResponse, ParticleVelocity = (float)s.ParticleVelocity,
            ParticleSpeed = P(s.ParticleSpeed), ParticleRandomness = P(s.ParticleRandomness), ParticleSpread = P(s.ParticleSpread),
            ParticleLife = (float)s.ParticleLife, ParticleLifeRandomness = P(s.ParticleLifeRandomness), ParticleSize = (float)s.ParticleSize,
            ParticleSizeRandomness = P(s.ParticleSizeRandomness), ParticleGlow = P(s.ParticleGlow), EmitterSize = P(s.EmitterSize), Spiral = P(s.Spiral),
            Gravity = (float)s.Gravity, Drag = P(s.Drag), VectorField = P(s.VectorField), FieldScale = (float)Math.Max(1, s.FieldScale),
            EvolutionSpeed = P(s.EvolutionSpeed), PhysicsTimeFactor = P(s.PhysicsTimeFactor),
            WispAmount = (float)s.WispAmount, WispSpeed = (float)s.WispSpeed, WispHeight = P(s.WispHeight), WispWidth = P(s.WispWidth),
            WispTurbulence = P(s.WispTurbulence), WispGlow = P(s.WispGlow),
            FlameIntensity = P(s.FlameIntensity), FlameHeight = P(s.FlameHeight), FlameNoteColor = s.FlameColorMode == "Note",

            ShowKeys = s.ShowKeys, AnimateKeys = s.AnimateKeys, ShowKeyFelt = s.ShowKeyFelt, ShowKeyShadow = s.ShowKeyShadow, ShowHalo = s.ShowHalo, HaloIntensity = P(s.HaloIntensity),
            KeyboardStyle = Math.Max(0, Array.IndexOf(PianoVisualSettings.KeyboardStyles, s.KeyboardStyle)),
            KeyboardFraction = (float)keyboardFraction, KeyOverhang = P(s.KeyOverhang), KeyPressDepth = P(s.KeyPressDepth),
            KeyLighting = P(s.KeyLighting), KeyGlowRadius = P(s.KeyGlowRadius), PressedKeyFixed = s.PressedKeyColorMode == "Fixed",
            KeyLabels = Math.Max(0, Array.IndexOf(PianoVisualSettings.KeyLabelModes, s.KeyLabels)),
            ShaderKeyLight = P(s.ShaderKeyLight), ShaderShadows = P(s.ShaderShadows), ShaderAmbientOcclusion = P(s.ShaderAmbientOcclusion),
            ShaderGloss = P(s.ShaderGloss), ShaderRimLight = P(s.ShaderRimLight), ShaderEmissive = P(s.ShaderEmissive),
            ShaderExposure = P(s.ShaderExposure), ShaderCameraTilt = P(s.ShaderCameraTilt), ShaderFilmic = s.ShaderFilmic,

            ShowBackground = s.ShowBackground, BackgroundGradient = s.BackgroundGradient, BackgroundMotion = s.BackgroundMotion,
            BackgroundMotionAmount = P(s.BackgroundMotionAmount), BackgroundMotionSpeed = Speed(s.BackgroundMotionSpeed), ShowStars = s.ShowStars, StarDensity = P(s.StarDensity),
            Chroma = s.BackgroundMode == "ChromaGreen", BackgroundDim = P(s.BackgroundDim),
            HorizonGlow = P(s.HorizonGlow), ShowLightBeams = s.ShowLightBeams, BeamIntensity = P(s.BeamIntensity),
            BackgroundGuide = s.ShowBackground && s.BackgroundGuide, ShowPetals = s.ShowPetals, PetalAmount = (float)s.PetalAmount,
            AmbientEnergy = s.AmbientEnergy, AmbientEnergyAmount = P(s.AmbientEnergyAmount), AmbientEnergySpeed = Speed(s.AmbientEnergySpeed),
            AmbientNature = s.AmbientNature, AmbientNatureAmount = P(s.AmbientNatureAmount), AmbientNatureSpeed = Speed(s.AmbientNatureSpeed),
            AmbientLight = s.AmbientLight, AmbientLightAmount = P(s.AmbientLightAmount), AmbientLightSpeed = Speed(s.AmbientLightSpeed),
            AmbientCosmic = s.AmbientCosmic, AmbientCosmicAmount = P(s.AmbientCosmicAmount), AmbientCosmicSpeed = Speed(s.AmbientCosmicSpeed),
            Vignette = P(s.Vignette), Saturation = P(s.Saturation), Contrast = P(s.Contrast),
            BloomIntensity = P(s.BloomIntensity), BloomSize = P(s.BloomSize),
            CameraZoom = P(s.CameraZoom), CameraOffset = P(s.CameraOffset), CameraParallax = P(s.CameraParallax),
            TempoSync = s.TempoSync, TempoSyncAmount = P(s.TempoSyncAmount), PedalGlow = s.PedalGlow, PedalGlowIntensity = P(s.PedalGlowIntensity),
            AudioReactive = s.AudioReactive, AudioReactiveAmount = P(s.AudioReactiveAmount),

            BackgroundColor = ParseHex(s.BackgroundColor, Vector3.Zero),
            HaloColor = ParseHex(s.HaloColor, new Vector3(.78f, .43f, 1f)),
            PressedKeyColor = ParseHex(s.PressedKeyColor, new Vector3(.97f, .51f, 1f)),
            KeyFeltColor = ParseHex(s.KeyFeltColor, new Vector3(.77f, .11f, .29f)),
            // Atmospheric tints use the same colour grade as the rest of the stage.
            PetalColor = ToVector(grade(ParseWpf(s.PetalColor, WpfColor.FromRgb(255, 179, 207)))),
            BackgroundMotionColor = ToVector(grade(ParseWpf(s.BackgroundMotionColor, WpfColor.FromRgb(123, 92, 255)))),
            AmbientLightColor = ToVector(grade(ParseWpf(s.AmbientLightColor, WpfColor.FromRgb(123, 92, 255)))),
            NoteColors = colors
        };
    }
}
