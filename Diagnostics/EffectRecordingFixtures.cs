namespace PianoPath;

internal enum EffectRecordingScene
{
    Quiet,
    FallingNote,
    HeldNotes,
    SongNoteRelease,
}

internal enum EffectRecordingEvent
{
    None,
    Impact,
}

/// <summary>
/// One effect-family recording fixture. It changes only the settings needed to expose that family; shared
/// per-setting recording bindings deliberately remain a separate verification task.
/// </summary>
internal sealed record EffectRecordingCaptureFixture(
    string FamilyId,
    EffectRecordingScene Scene,
    Action<PianoVisualSettings> Enable,
    EffectRecordingEvent Event = EffectRecordingEvent.None,
    int WpfAdvanceSteps = 6,
    int GpuSettleFrames = 2,
    int GpuSampleFrames = 8,
    int MinimumChangedPixels = 8);

/// <summary>
/// Deterministic, one-family-at-a-time inputs for the actual recording outputs. The GPU-only families are
/// still represented: their WPF stage capability remains Unsupported, while their GPU recording capture is
/// checked through <see cref="GpuRecordingTap"/>.
/// </summary>
internal static class EffectRecordingFixtures
{
    internal const int Width = 640;
    internal const int Height = 360;

    private static readonly IReadOnlySet<int> NoKeys = new HashSet<int>();
    private static readonly IReadOnlySet<int> HeldPitches = new HashSet<int> { 60, 64 };
    private static readonly IReadOnlyList<NoteEvent> NoNotes = Array.Empty<NoteEvent>();
    private static readonly IReadOnlyList<NoteEvent> FallingNotes = Array.AsReadOnly(new[]
    {
        new NoteEvent { Pitch = 60, Start = .25, Duration = .7 },
        new NoteEvent { Pitch = 64, Start = .25, Duration = .7 },
    });
    private static readonly IReadOnlyList<NoteEvent> HeldNotes = Array.AsReadOnly(new[]
    {
        new NoteEvent { Pitch = 60, Start = .1, Duration = 4 },
        new NoteEvent { Pitch = 64, Start = .1, Duration = 4 },
    });
    private static readonly IReadOnlyList<NoteEvent> ReleaseNotes = Array.AsReadOnly(new[]
    {
        new NoteEvent { Pitch = 60, Start = .05, Duration = .05 },
    });

    internal static IReadOnlyList<EffectRecordingCaptureFixture> All { get; } = Array.AsReadOnly(new[]
    {
        new EffectRecordingCaptureFixture(
            "impact.wave", EffectRecordingScene.Quiet,
            settings => { settings.ShowImpactRings = true; settings.ImpactWave = "Shockwave"; settings.ImpactWaveIntensity = 100; settings.RingSize = 100; },
            EffectRecordingEvent.Impact, WpfAdvanceSteps: 1, GpuSettleFrames: 1, GpuSampleFrames: 4),
        new EffectRecordingCaptureFixture(
            "impact.flash", EffectRecordingScene.Quiet,
            settings => { settings.ShowImpactFlash = true; settings.ImpactFlashStyle = "Lightning"; settings.ImpactFlashIntensity = 100; },
            EffectRecordingEvent.Impact, WpfAdvanceSteps: 1, GpuSettleFrames: 1, GpuSampleFrames: 4),
        new EffectRecordingCaptureFixture(
            "impact.burst", EffectRecordingScene.Quiet,
            settings => { settings.ShowEmbers = true; settings.ImpactBurst = "Confetti"; settings.ParticleAmount = 120; },
            EffectRecordingEvent.Impact, WpfAdvanceSteps: 1, GpuSettleFrames: 1, GpuSampleFrames: 5),
        new EffectRecordingCaptureFixture(
            "impact.morph", EffectRecordingScene.Quiet,
            // WPF stores the shards in the shared particle list, so the particle draw layer must be on; amount 0 keeps the burst channel quiet.
            settings => { settings.ShowEmbers = true; settings.ImpactMorph = "Shatter"; settings.ImpactMorphIntensity = 100; },
            EffectRecordingEvent.Impact, WpfAdvanceSteps: 1, GpuSettleFrames: 1, GpuSampleFrames: 4),
        new EffectRecordingCaptureFixture(
            "falling.trail", EffectRecordingScene.FallingNote,
            settings => { settings.FallingTrail = "Sparkles"; settings.FallingTrailIntensity = 100; settings.FallingTrailLength = 100; settings.FallingGhost = true; settings.FallingGhostAmount = 80; },
            WpfAdvanceSteps: 4, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "falling.pulse", EffectRecordingScene.FallingNote,
            settings => { settings.FallingPulse = true; settings.FallingPulseRate = 100; },
            WpfAdvanceSteps: 10, GpuSettleFrames: 2, GpuSampleFrames: 18),
        new EffectRecordingCaptureFixture(
            "falling.shimmer", EffectRecordingScene.FallingNote,
            settings => { settings.NoteShimmer = true; settings.NoteShimmerAmount = 100; },
            WpfAdvanceSteps: 6, GpuSettleFrames: 2, GpuSampleFrames: 12),
        new EffectRecordingCaptureFixture(
            "hold.bar", EffectRecordingScene.HeldNotes,
            settings => { settings.HoldBar = true; settings.HoldBarIntensity = 100; },
            WpfAdvanceSteps: 4, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "hold.breath", EffectRecordingScene.HeldNotes,
            settings => { settings.HoldBreath = true; settings.HoldBreathRate = 100; },
            WpfAdvanceSteps: 10, GpuSettleFrames: 2, GpuSampleFrames: 18),
        new EffectRecordingCaptureFixture(
            "hold.vibration", EffectRecordingScene.HeldNotes,
            settings => { settings.HoldVibration = true; settings.HoldVibrationAmount = 100; },
            WpfAdvanceSteps: 4, GpuSettleFrames: 2, GpuSampleFrames: 18),
        new EffectRecordingCaptureFixture(
            "hold.color-cycle", EffectRecordingScene.HeldNotes,
            settings => { settings.HoldColorCycle = true; settings.HoldColorCycleSpeed = 100; },
            WpfAdvanceSteps: 10, GpuSettleFrames: 2, GpuSampleFrames: 18),
        new EffectRecordingCaptureFixture(
            "hold.arc", EffectRecordingScene.HeldNotes,
            settings => { settings.HoldElectricArc = true; settings.HoldArcIntensity = 100; },
            WpfAdvanceSteps: 4, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "ambient.energy", EffectRecordingScene.Quiet,
            settings => { settings.AmbientEnergy = "Confetti Rain"; settings.AmbientEnergyAmount = 100; settings.AmbientEnergySpeed = 65; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "ambient.nature", EffectRecordingScene.Quiet,
            settings => { settings.AmbientNature = "Snow"; settings.AmbientNatureAmount = 100; settings.AmbientNatureSpeed = 65; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "ambient.light", EffectRecordingScene.Quiet,
            settings => { settings.AmbientLight = "Prism"; settings.AmbientLightAmount = 100; settings.AmbientLightSpeed = 65; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "ambient.cosmic", EffectRecordingScene.Quiet,
            settings => { settings.AmbientCosmic = "Galaxy"; settings.AmbientCosmicAmount = 100; settings.AmbientCosmicSpeed = 65; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "atmosphere.stars", EffectRecordingScene.Quiet,
            settings => { settings.ShowStars = true; settings.StarDensity = 100; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "atmosphere.shooting-stars", EffectRecordingScene.Quiet,
            settings => { settings.ShootingStars = true; settings.ShootingStarsAmount = 100; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 0, GpuSampleFrames: 360, MinimumChangedPixels: 4),
        new EffectRecordingCaptureFixture(
            "atmosphere.petals", EffectRecordingScene.Quiet,
            settings => { settings.ShowPetals = true; settings.PetalAmount = 100; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "atmosphere.beams", EffectRecordingScene.HeldNotes,
            settings => { settings.ShowLightBeams = true; settings.BeamIntensity = 100; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 2, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "particles.sparks", EffectRecordingScene.Quiet,
            settings => { settings.ShowEmbers = true; settings.ImpactBurst = "Embers"; settings.ParticleAmount = 120; settings.ParticleLife = 3; },
            EffectRecordingEvent.Impact, WpfAdvanceSteps: 1, GpuSettleFrames: 1, GpuSampleFrames: 8),
        new EffectRecordingCaptureFixture(
            "particles.wisps", EffectRecordingScene.HeldNotes,
            settings => { settings.ShowWisps = true; settings.WispAmount = 150; settings.WispSpeed = 300; settings.WispGlow = 150; },
            WpfAdvanceSteps: 6, GpuSettleFrames: 2, GpuSampleFrames: 12),
        new EffectRecordingCaptureFixture(
            "particles.flames", EffectRecordingScene.HeldNotes,
            settings => { settings.ShowFlame = true; settings.FlameIntensity = 100; settings.FlameHeight = 100; },
            WpfAdvanceSteps: 6, GpuSettleFrames: 2, GpuSampleFrames: 12),
        new EffectRecordingCaptureFixture(
            "release.fx", EffectRecordingScene.SongNoteRelease,
            settings => { settings.ReleaseEffect = "Echo Rings"; settings.ReleaseIntensity = 100; settings.ShowImpactRings = true; settings.ImpactWaveIntensity = 100; settings.RingSize = 100; },
            WpfAdvanceSteps: 2, GpuSettleFrames: 0, GpuSampleFrames: 5),
    });

    internal static PianoVisualSettings NeutralLook() => new()
    {
        BackgroundMode = "Solid",
        // SetVisualSettings clones through FromJson. The v0 migration would otherwise
        // turn off ShowStars after fixture.Enable.
        BackgroundAppearanceVersion = 2,
        BackgroundGradient = false,
        BackgroundMotion = "None",
        BackgroundMotionAmount = 0,
        ShowBackground = true,
        ShowNotes = true,
        ShowKeys = false,
        ShowHalo = false,
        ShowWatermark = false,
        ShowCounter = false,
        ShowFps = false,
        ShowSheet = false,
        ShowNoteLabels = false,
        ShowStars = false,
        StarDensity = 0,
        ShootingStars = false,
        ShowPetals = false,
        PetalAmount = 0,
        ShowLightBeams = false,
        BeamIntensity = 0,
        HorizonGlow = 0,
        Vignette = 0,
        ShowEmbers = false,
        ShowWisps = false,
        ShowFlame = false,
        ShowImpactRings = false,
        ShowImpactFlash = false,
        ImpactWave = "None",
        ImpactWaveIntensity = 0,
        RingSize = 0,
        ImpactFlashIntensity = 0,
        ImpactMorph = "None",
        ImpactMorphIntensity = 0,
        ImpactBurst = "Embers",
        ParticleAmount = 0,
        FallingTrail = "None",
        FallingTrailIntensity = 0,
        FallingTrailLength = 0,
        FallingGhost = false,
        FallingGhostAmount = 0,
        FallingPulse = false,
        FallingPulseRate = 0,
        NoteShimmer = false,
        NoteShimmerAmount = 0,
        NoteLandingGlow = false,
        HoldBar = false,
        HoldBarIntensity = 0,
        HoldBreath = false,
        HoldVibration = false,
        HoldColorCycle = false,
        HoldElectricArc = false,
        AmbientEnergy = "None",
        AmbientEnergyAmount = 0,
        AmbientNature = "None",
        AmbientNatureAmount = 0,
        AmbientLight = "None",
        AmbientLightAmount = 0,
        AmbientCosmic = "None",
        AmbientCosmicAmount = 0,
        ReleaseEffect = "Fade",
        ReleaseIntensity = 100,
        ReducedMotion = false,
        ReduceFlashes = false,
        HighContrast = false,
        PhysicsTimeFactor = 100,
        NoteStyle = "Solid",
        Notes3D = false,
        NoteWidth = 100,
        NoteTint = 100,
        // HoldBreath modulates the WPF glow envelope; keep a strong shared test-note glow in both paired frames.
        NoteGlow = 150,
        NoteHeadGlow = 0,
        BloomIntensity = 100,
        BloomSize = 60,
        NoteEdge = 80,
        NoteEdgeWidth = 40,
        NoteRefraction = 0,
        NoteFallSpeed = 550,
    };

    internal static (IReadOnlyList<NoteEvent> Notes, double Position, bool Playing, IReadOnlySet<int> Pressed) SceneState(EffectRecordingScene scene) => scene switch
    {
        EffectRecordingScene.Quiet => (NoNotes, 0, false, NoKeys),
        EffectRecordingScene.FallingNote => (FallingNotes, .1, true, NoKeys),
        EffectRecordingScene.HeldNotes => (HeldNotes, 1, true, HeldPitches),
        EffectRecordingScene.SongNoteRelease => (ReleaseNotes, 0, true, NoKeys),
        _ => throw new ArgumentOutOfRangeException(nameof(scene)),
    };
}
