namespace PianoPath;

/// <summary>Stage in which a visual effect participates. This first typed pilot covers note materials only.</summary>
internal enum EffectPhase
{
    NoteMaterial,
    Falling,
    Impact,
    Hold,
    Release,
    Ambient,
    Theme,
    Modulator,
}

/// <summary>Event or render condition that starts evaluating an effect.</summary>
internal enum EffectTrigger
{
    NoteBodyRendered,
    NoteOn,
    NoteHeld,
    NoteOff,
    Beat,
    AudioEnvelope,
    SceneTick,
}

/// <summary>How long an effect remains active after its trigger.</summary>
internal enum EffectLifecycle
{
    WhileNoteVisible,
    FixedDuration,
    WhileNoteHeld,
    Continuous,
}

/// <summary>Coordinate space or object to which an effect is attached.</summary>
internal enum EffectAnchor
{
    NoteBody,
    Key,
    HitPoint,
    Stage,
    Keyboard,
    View,
}

/// <summary>Time source an effect consumes. Note materials in the pilot do not animate independently.</summary>
internal enum EffectClock
{
    None,
    SongPosition,
    RenderElapsed,
    AudioTime,
}

/// <summary>How an effect's randomness is made repeatable.</summary>
internal enum EffectSeedPolicy
{
    None,
    PerNote,
    PerSession,
    PerFrame,
}

/// <summary>Evidence state for a renderer/backend, not a UI visibility flag.</summary>
internal enum EffectBackendState
{
    /// <summary>The backend has a native implementation; this is not a performance or all-device guarantee.</summary>
    Native,
    /// <summary>The backend substitutes a named, readable alternative rather than rendering this material natively.</summary>
    Fallback,
    /// <summary>The backend cannot render this effect or any declared fallback.</summary>
    Unsupported,
    /// <summary>Current source/tests are not enough to make a support claim.</summary>
    Unverified,
}

/// <summary>Type of a material-specific setting exposed by the legacy settings model.</summary>
internal enum EffectParameterKind
{
    Boolean,
    Percentage,
    /// <summary>A persisted choice string ("Ring", "Embers", …); the allowed values stay owned by the settings model.</summary>
    Choice,
}

/// <summary>Whether a backend consumes a setting for this particular effect.</summary>
internal enum EffectParameterBindingState
{
    Applied,
    Ignored,
    Unverified,
}

/// <summary>Risk of a flash emitted by the effect itself (not by other stage layers).</summary>
internal enum EffectFlashRisk
{
    None,
    Local,
    FullFrame,
    Unverified,
}

/// <summary>Intrinsic motion classification only; it does not suppress the parent note's movement.</summary>
internal enum EffectReducedMotionBehavior
{
    NoIndependentAnimation,
    /// <summary>The effect animates on its own clock (pulses, twinkles) even when the note sits still.</summary>
    IntrinsicAnimation,
    Unverified,
}

/// <summary>Visible shape information that remains available without relying on hue alone.</summary>
internal enum EffectShapeCue
{
    None,
    FacetPlanes,
    AngularContourAndFacets,
    /// <summary>The opaque body plus rim contour stays legible without hue (Solid).</summary>
    OpaqueBodyAndRim,
}

/// <summary>Whether an isolated, backend-specific cost measurement exists.</summary>
internal enum EffectCostState
{
    Unmeasured,
    Measured,
}

/// <summary>Per-output parameter use; a native effect can still ignore an input on one renderer.</summary>
internal sealed record EffectParameterBindingProfile(
    EffectParameterBindingState WpfStage,
    EffectParameterBindingState GpuStage,
    EffectParameterBindingState Recording,
    EffectParameterBindingState ArOverlay)
{
    internal bool IsWellFormed =>
        (WpfStage is EffectParameterBindingState.Applied or EffectParameterBindingState.Ignored or EffectParameterBindingState.Unverified)
        && (GpuStage is EffectParameterBindingState.Applied or EffectParameterBindingState.Ignored or EffectParameterBindingState.Unverified)
        && (Recording is EffectParameterBindingState.Applied or EffectParameterBindingState.Ignored or EffectParameterBindingState.Unverified)
        && (ArOverlay is EffectParameterBindingState.Applied or EffectParameterBindingState.Ignored or EffectParameterBindingState.Unverified);
}

/// <summary>A setting, its inclusive legacy clamp range, and the outputs that actually consume it; the range mirrors <c>PianoVisualSettings.Clamp()</c>.</summary>
internal sealed record EffectParameterContract(
    string SettingName,
    EffectParameterKind Kind,
    double? Minimum,
    double? Maximum,
    EffectParameterBindingProfile Bindings)
{
    internal bool IsWellFormed => Bindings is not null && Bindings.IsWellFormed && (Kind switch
    {
        EffectParameterKind.Boolean or EffectParameterKind.Choice => Minimum is null && Maximum is null,
        EffectParameterKind.Percentage => Minimum is double min && Maximum is double max
            && double.IsFinite(min) && double.IsFinite(max) && min <= max,
        _ => false,
    });
}

/// <summary>One backend's evidence-graded support state; only a fallback state may name a fallback ID.</summary>
internal sealed record EffectBackendCapability(EffectBackendState State, string? FallbackId = null)
{
    internal bool IsWellFormed => State switch
    {
        EffectBackendState.Fallback => !string.IsNullOrWhiteSpace(FallbackId),
        EffectBackendState.Native or EffectBackendState.Unsupported or EffectBackendState.Unverified => FallbackId is null,
        _ => false,
    };
}

/// <summary>Support is independent for each output; support on the stage never implies AR material support.</summary>
internal sealed record EffectCapabilityProfile(
    EffectBackendCapability WpfStage,
    EffectBackendCapability GpuStage,
    EffectBackendCapability Recording,
    EffectBackendCapability ArOverlay)
{
    internal bool IsWellFormed => WpfStage.IsWellFormed && GpuStage.IsWellFormed
        && Recording.IsWellFormed && ArOverlay.IsWellFormed;
}

/// <summary>Cost evidence for one backend. A measured value is the effect's isolated P95 cost in milliseconds.</summary>
internal sealed record EffectCostAssessment(
    EffectCostState State,
    double? P95Milliseconds = null,
    string? EvidenceId = null)
{
    internal bool IsWellFormed => State switch
    {
        EffectCostState.Unmeasured => P95Milliseconds is null && EvidenceId is null,
        EffectCostState.Measured => P95Milliseconds is double ms && double.IsFinite(ms) && ms >= 0
            && !string.IsNullOrWhiteSpace(EvidenceId),
        _ => false,
    };
}

/// <summary>Cost evidence is kept per output; a GPU-stage entry may cite the isolated physical-adapter
/// benchmark in <c>docs/NOTE-MATERIAL-GPU-BENCHMARK.md</c> — a note-pass number, never a whole-frame budget.</summary>
internal sealed record EffectCostProfile(
    EffectCostAssessment WpfStage,
    EffectCostAssessment GpuStage,
    EffectCostAssessment Recording,
    EffectCostAssessment ArOverlay)
{
    internal bool IsWellFormed => WpfStage.IsWellFormed && GpuStage.IsWellFormed
        && Recording.IsWellFormed && ArOverlay.IsWellFormed;
}

/// <summary>
/// Accessibility classification for this effect alone. Reduced-motion metadata describes intrinsic material
/// animation; it is not a claim that the application currently suppresses the note's parent movement.
/// </summary>
internal sealed record EffectAccessibilityContract(
    EffectFlashRisk FlashRisk,
    EffectReducedMotionBehavior ReducedMotionBehavior,
    EffectShapeCue NonColorCue)
{
    internal bool IsWellFormed =>
        (FlashRisk is EffectFlashRisk.None or EffectFlashRisk.Local or EffectFlashRisk.FullFrame or EffectFlashRisk.Unverified)
        && (ReducedMotionBehavior is EffectReducedMotionBehavior.NoIndependentAnimation or EffectReducedMotionBehavior.IntrinsicAnimation or EffectReducedMotionBehavior.Unverified)
        && (NonColorCue is EffectShapeCue.None or EffectShapeCue.FacetPlanes or EffectShapeCue.AngularContourAndFacets or EffectShapeCue.OpaqueBodyAndRim);
}

/// <summary>
/// Typed metadata for a visual effect. <see cref="LegacyAliases"/> are persisted values, not serialized IDs;
/// adding this contract does not migrate user JSON or change UI choices. Parameters list material-specific controls;
/// shared note geometry, palette and phase controls remain owned by their existing settings/contracts.
/// </summary>
internal sealed record EffectContract(
    string Id,
    IReadOnlyList<string> LegacyAliases,
    int GpuStyleId,
    EffectPhase Phase,
    EffectTrigger Trigger,
    EffectLifecycle Lifecycle,
    EffectAnchor Anchor,
    EffectClock Clock,
    EffectSeedPolicy SeedPolicy,
    IReadOnlyList<EffectParameterContract> Parameters,
    EffectCapabilityProfile Capabilities,
    EffectCostProfile Cost,
    EffectAccessibilityContract Accessibility)
{
    internal bool IsWellFormed => !string.IsNullOrWhiteSpace(Id)
        && LegacyAliases.Count > 0
        && LegacyAliases.All(alias => !string.IsNullOrWhiteSpace(alias))
        && LegacyAliases.Distinct(StringComparer.Ordinal).Count() == LegacyAliases.Count
        && GpuStyleId >= 0
        && Parameters.All(parameter => !string.IsNullOrWhiteSpace(parameter.SettingName) && parameter.IsWellFormed)
        && Parameters.Select(parameter => parameter.SettingName).Distinct(StringComparer.Ordinal).Count() == Parameters.Count
        && Capabilities.IsWellFormed
        && Cost.IsWellFormed
        && Accessibility is not null && Accessibility.IsWellFormed;
}

    /// <summary>
    /// The first runtime-contract pilot: Solid, Gem and CrystalOutline — the complete note-material family.
    /// This registry is deliberately separate from the descriptive <c>EffectCatalog</c> and does not drive
    /// settings serialization, UI filtering or renderer dispatch. WPF, GPU and Recording are Native after
    /// material-specific read-back checks in <c>VerifyPngSequenceRecorder</c>, <c>VerifyGpuNotePassQueries</c>
    /// and <c>VerifyGpuNoteMaterials</c> (WPF PNG files and GPU <c>GpuRecordingTap</c> frames). Individual
    /// parameter-to-recording bindings remain Unverified. AR passes known style IDs to the shared material
    /// shader on camera-facing quads, but AR material capability and parameter bindings remain Unverified
    /// until an AR-specific style/value matrix is reviewed.
    /// </summary>
internal static class EffectContractCatalog
{
    private static readonly EffectBackendCapability Native = new(EffectBackendState.Native);
    private static readonly EffectBackendCapability Unverified = new(EffectBackendState.Unverified);
    private static readonly EffectBackendCapability Unsupported = new(EffectBackendState.Unsupported);
    private static readonly EffectParameterBindingProfile NativeParameterBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Ignored);
    // AR uses the shared shader path, but no per-setting AR evidence currently supports Applied/Ignored claims.
    private static readonly EffectParameterBindingProfile ArMaterialParameterBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Unverified);
    private static readonly EffectParameterBindingProfile ArAmbientParameterBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Applied);
    // S5's calibrated 3D projection consumes these settings, and selected output variants are now read back
    // by WARP. Keep per-setting bindings Unverified until every declared value/range and lifecycle branch has coverage.
    private static readonly EffectParameterBindingProfile ArProjectedParameterBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Unverified);
    private static readonly EffectParameterBindingProfile GpuOnlyArAmbientParameterBindings = new(
        EffectParameterBindingState.Ignored, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Applied);
    private static readonly EffectParameterBindingProfile GpuOnlyParameterBindings = new(
        EffectParameterBindingState.Ignored, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Ignored);
    // The shimmer is GPU-only, but "GPU-only" is not "stage-only": AddSongNote feeds NoteShimmer to the AR
    // note quads on both the flat and the projected path (Gpu/GpuArLayer.cs AddNoteShimmer call).
    private static readonly EffectParameterBindingProfile GpuOnlyArNoteParameterBindings =
        GpuOnlyParameterBindings with { ArOverlay = EffectParameterBindingState.Unverified };
    private static readonly EffectParameterBindingProfile WpfOnlyRefractionBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Ignored,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Unverified);
    private static readonly EffectCostAssessment UnmeasuredCost = new(EffectCostState.Unmeasured);

    // Manual-review record of the physical-adapter probe (2026-10-07, AMD Radeon(TM) Graphics, iGPU):
    // isolated p95 around the note DrawInstanced only. The probe itself never writes these values;
    // WPF, recording and AR stay Unmeasured until their own evidence exists.
    private const string GpuNotePassCostEvidence =
        "docs/NOTE-MATERIAL-GPU-BENCHMARK.md, run 2026-10-07T12:50:58Z, adapter AMD Radeon(TM) Graphics";
    private static readonly EffectCostProfile GemCosts = new(
        UnmeasuredCost,
        new EffectCostAssessment(EffectCostState.Measured, 0.02688, GpuNotePassCostEvidence),
        UnmeasuredCost, UnmeasuredCost);
    private static readonly EffectCostProfile SolidCosts = new(
        UnmeasuredCost,
        new EffectCostAssessment(EffectCostState.Measured, 0.02356, GpuNotePassCostEvidence),
        UnmeasuredCost, UnmeasuredCost);
    private static readonly EffectCostProfile CrystalOutlineCosts = new(
        UnmeasuredCost,
        new EffectCostAssessment(EffectCostState.Measured, 0.0268, GpuNotePassCostEvidence),
        UnmeasuredCost, UnmeasuredCost);

    // Solid consumes all six look settings on both renderers: the WPF default branch draws tint/glow/edge/
    // edge-width/3D and the shared refraction fringe, and the GPU Solid branch reads the same uniforms.
    private static readonly IReadOnlyList<EffectParameterContract> SolidParameters = Array.AsReadOnly(new[]
    {
        Percentage("NoteTint", 0, 100, ArMaterialParameterBindings),
        Percentage("NoteGlow", 0, 200, ArMaterialParameterBindings),
        Percentage("NoteEdge", 0, 200, ArMaterialParameterBindings),
        Percentage("NoteEdgeWidth", 0, 100, ArMaterialParameterBindings),
        Percentage("NoteRefraction", 0, 100, ArMaterialParameterBindings),
        Boolean("Notes3D", ArMaterialParameterBindings),
    });

    private static readonly IReadOnlyList<EffectParameterContract> GemParameters = Array.AsReadOnly(new[]
    {
        Percentage("NoteTint", 0, 100, ArMaterialParameterBindings),
        Percentage("NoteGlow", 0, 200, ArMaterialParameterBindings),
        Percentage("NoteEdge", 0, 200, ArMaterialParameterBindings),
        Percentage("NoteEdgeWidth", 0, 100, ArMaterialParameterBindings),
        Percentage("NoteRefraction", 0, 100, ArMaterialParameterBindings),
        Boolean("Notes3D", ArMaterialParameterBindings),
    });

    private static readonly IReadOnlyList<EffectParameterContract> CrystalOutlineParameters = Array.AsReadOnly(new[]
    {
        Percentage("NoteTint", 0, 100, ArMaterialParameterBindings),
        Percentage("NoteGlow", 0, 200, ArMaterialParameterBindings),
        Percentage("NoteEdge", 0, 200, ArMaterialParameterBindings),
        Percentage("NoteEdgeWidth", 0, 100, ArMaterialParameterBindings),
        // Crystal Outline is the one family where the GPU ignores NoteRefraction: that branch reads NoteB.y for
        // its rim and NoteB.x for its tint, while WPF draws both a fringe and a sheen from the same setting.
        // Gem, Solid, Satin and Glass do read NoteB.w, so this profile belongs to the family, not to the parameter.
        Percentage("NoteRefraction", 0, 100, WpfOnlyRefractionBindings),
    });

    /// <summary>The intentionally narrow pilot. Add other families only after their own evidence review.</summary>
    internal static IReadOnlyList<EffectContract> NoteMaterialPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectContract(
            "note.material.solid",
            Array.AsReadOnly(new[] { "Solid" }),
            0,
            EffectPhase.NoteMaterial,
            EffectTrigger.NoteBodyRendered,
            EffectLifecycle.WhileNoteVisible,
            EffectAnchor.NoteBody,
            EffectClock.None,
            EffectSeedPolicy.None,
            SolidParameters,
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            SolidCosts,
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.OpaqueBodyAndRim)),
        new EffectContract(
            "note.material.gem",
            Array.AsReadOnly(new[] { "Gem" }),
            4,
            EffectPhase.NoteMaterial,
            EffectTrigger.NoteBodyRendered,
            EffectLifecycle.WhileNoteVisible,
            EffectAnchor.NoteBody,
            EffectClock.None,
            EffectSeedPolicy.None,
            GemParameters,
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            GemCosts,
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.FacetPlanes)),
        new EffectContract(
            "note.material.crystal-outline",
            Array.AsReadOnly(new[] { "CrystalOutline" }),
            5,
            EffectPhase.NoteMaterial,
            EffectTrigger.NoteBodyRendered,
            EffectLifecycle.WhileNoteVisible,
            EffectAnchor.NoteBody,
            EffectClock.None,
            EffectSeedPolicy.None,
            CrystalOutlineParameters,
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            CrystalOutlineCosts,
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.AngularContourAndFacets)),
    });

    /// <summary>
    /// Typed metadata for a non-material effect family. Unlike <see cref="EffectContract"/> it carries no
    /// persisted alias and no GPU style index: impact effects are selected by their own settings and never
    /// by a stored style string. Its Recording state is Native only after its own isolated GPU
    /// <see cref="GpuRecordingTap"/> frame is read back by <c>VerifyEffectFamilyGpuRecordingCaptures</c>;
    /// WPF-native families also pass actual PNG write/read capture fixtures. The two GPU-only families have
    /// no WPF capture claim. This is family-presence evidence only: each setting's Recording binding stays
    /// Unverified and recording cost stays Unmeasured.
    /// </summary>
    internal sealed record EffectFamilyContract(
        string Id,
        EffectPhase Phase,
        EffectTrigger Trigger,
        EffectLifecycle Lifecycle,
        EffectAnchor Anchor,
        EffectClock Clock,
        EffectSeedPolicy SeedPolicy,
        IReadOnlyList<EffectParameterContract> Parameters,
        EffectCapabilityProfile Capabilities,
        EffectCostProfile Cost,
        EffectAccessibilityContract Accessibility)
    {
        internal bool IsWellFormed => !string.IsNullOrWhiteSpace(Id)
            && Parameters.All(parameter => !string.IsNullOrWhiteSpace(parameter.SettingName) && parameter.IsWellFormed)
            && Parameters.Select(parameter => parameter.SettingName).Distinct(StringComparer.Ordinal).Count() == Parameters.Count
            && Capabilities.IsWellFormed
            && Cost.IsWellFormed
            && Accessibility is not null && Accessibility.IsWellFormed;
    }

    /// <summary>
    /// The second contract pilot: the four impact families that fire when a note lands. Both renderers draw
    /// all four (grep-verified in <c>PianoStage.cs</c> and the GPU simulation), hence Native there. Each
    /// family now also has its own GPU recording-tap read-back fixture (and WPF PNG write/read fixture), while
    /// parameter bindings remain Unverified. S5 preserves these pitch-tagged channels for calibrated 3D
    /// projection instead of drawing their stage-space sprites over camera; selected Standard/Ultra variants now
    /// have WARP pixel/counter coverage, but family and parameter capabilities remain Unverified until the full
    /// choice/value/lifecycle matrix is exercised. Costs
    /// are all Unmeasured — no isolated probe has measured any of them, and this catalog must not invent
    /// numbers. Flash classification is kept conservative: the flash is a localised bright pulse (Local), the
    /// spark burst flickers without a strobe-area analysis (Unverified, so safety resolution suppresses it),
    /// wave and morph carry no flash.
    /// </summary>
    internal static IReadOnlyList<EffectFamilyContract> ImpactFxPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectFamilyContract(
            "impact.wave",
            EffectPhase.Impact, EffectTrigger.NoteOn, EffectLifecycle.FixedDuration, EffectAnchor.HitPoint,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowImpactRings"),
                ArProjectedChoice("ImpactWave"),
                ArProjectedPercentage("ImpactWaveIntensity", 0, 150),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "impact.flash",
            EffectPhase.Impact, EffectTrigger.NoteOn, EffectLifecycle.FixedDuration, EffectAnchor.HitPoint,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowImpactFlash"),
                ArProjectedChoice("ImpactFlashStyle"),
                ArProjectedPercentage("ImpactFlashIntensity", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Local, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "impact.burst",
            EffectPhase.Impact, EffectTrigger.NoteOn, EffectLifecycle.FixedDuration, EffectAnchor.HitPoint,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowEmbers"),
                ArProjectedChoice("ImpactBurst"),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "impact.morph",
            EffectPhase.Impact, EffectTrigger.NoteOn, EffectLifecycle.FixedDuration, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedChoice("ImpactMorph"),
                ArProjectedPercentage("ImpactMorphIntensity", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
    });

    /// <summary>IDs unique, schemas and capability claims well-formed, before consumers opt in.</summary>
    internal static bool IsImpactPilotWellFormed =>
        ImpactFxPilot.Count == 4
        && ImpactFxPilot.All(contract => contract.IsWellFormed)
        && ImpactFxPilot.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == ImpactFxPilot.Count;

    /// <summary>
    /// The third contract pilot: what rides along while a note travels. Trail and pulse/ghost are drawn by
    /// both renderers; the shimmer is the one GPU-only member — the WPF stage never reads
    /// <c>NoteShimmer</c> (verified by source inspection), so it is Unsupported there rather than silently
    /// claimed. Motion classification is honest: trails and ghosts derive from the note's own travel, while
    /// the pulse and the shimmer twinkle on their own clock (IntrinsicAnimation).
    ///
    /// <para>The AR column used to say Unsupported for all three, which was true when the overlay was
    /// flat-only. It is not any more: <c>GpuArLayer.AddSongNote</c> applies <c>FallingPulse</c> and adds the
    /// shimmer on the flat and the projected path alike, so those two are <c>Unverified</c> — the code paths
    /// exist and the WARP checks in <c>VerifyArNoteProjection</c> touch them, but no per-value matrix does.
    /// Trail and ghosts are <c>Unverified</c> too, drawn only when the saved 3D pose is present: the flat
    /// overlay builds note bodies and nothing else, and that is what the AR card says out loud
    /// (<see cref="ArOmittedEffects"/>). Costs all Unmeasured.</para>
    /// </summary>
    internal static IReadOnlyList<EffectFamilyContract> FallingFxPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectFamilyContract(
            "falling.trail",
            EffectPhase.Falling, EffectTrigger.NoteBodyRendered, EffectLifecycle.WhileNoteVisible, EffectAnchor.NoteBody,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Choice("FallingTrail", ArProjectedParameterBindings),
                Percentage("FallingTrailIntensity", 0, 100, ArProjectedParameterBindings),
                Percentage("FallingTrailLength", 0, 100, ArProjectedParameterBindings),
                Boolean("FallingGhost", ArProjectedParameterBindings),
                Percentage("FallingGhostAmount", 0, 100, ArProjectedParameterBindings),
            }),
            // Projected notes only: the flat overlay draws note bodies, so trail and ghosts need the pose.
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "falling.pulse",
            EffectPhase.Falling, EffectTrigger.NoteBodyRendered, EffectLifecycle.WhileNoteVisible, EffectAnchor.NoteBody,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Boolean("FallingPulse", ArProjectedParameterBindings),
                Percentage("FallingPulseRate", 0, 100, ArProjectedParameterBindings),
            }),
            // AddSongNote applies the pulse to AR's own note quads on both the flat and the projected path.
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "falling.shimmer",
            EffectPhase.Falling, EffectTrigger.NoteBodyRendered, EffectLifecycle.WhileNoteVisible, EffectAnchor.NoteBody,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Boolean("NoteShimmer", GpuOnlyArNoteParameterBindings),
                Percentage("NoteShimmerAmount", 0, 100, GpuOnlyArNoteParameterBindings),
            }),
            // AddNoteShimmer is called from AddSongNote, so AR draws it on both paths as well.
            new EffectCapabilityProfile(Unsupported, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
    });

    /// <summary>IDs unique, schemas and capability claims well-formed, before consumers opt in.</summary>
    internal static bool IsFallingPilotWellFormed =>
        FallingFxPilot.Count == 3
        && FallingFxPilot.All(contract => contract.IsWellFormed)
        && FallingFxPilot.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == FallingFxPilot.Count;

    /// <summary>
    /// The fourth contract pilot: what sounding notes and held keys do while the key stays down. All five are
    /// drawn by both renderers. Four own their clock (breathing, vibration, hue cycling, arc flicker) and are
    /// IntrinsicAnimation; the hold bar merely mirrors the note's remaining duration, so it derives its motion
    /// from the song and stays NoIndependentAnimation. The electric arc flickers bright and has no strobe
    /// analysis yet: flash risk Unverified, safety resolution suppresses. The hue cycle is color-only and
    /// declares no shape cue. S5 projects the retained hold channels from the pitch-tagged simulation through
    /// the calibrated pose without drawing stage-space key/sprite pixels. VerifyArNoteProjection now isolates
    /// HoldBar, HoldBreath, HoldVibration and HoldColorCycle on projected live bars; the family/parameter states
    /// remain Unverified because those sampled settings do not cover every value or the full hold-arc/style matrix.
    /// Costs all Unmeasured.
    /// </summary>
    internal static IReadOnlyList<EffectFamilyContract> HoldFxPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectFamilyContract(
            "hold.bar",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.NoteBody,
            EffectClock.SongPosition, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("HoldBar"),
                ArProjectedPercentage("HoldBarIntensity", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "hold.breath",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("HoldBreath"),
                ArProjectedPercentage("HoldBreathRate", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "hold.vibration",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("HoldVibration"),
                ArProjectedPercentage("HoldVibrationAmount", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "hold.color-cycle",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("HoldColorCycle"),
                ArProjectedPercentage("HoldColorCycleSpeed", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "hold.arc",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("HoldElectricArc"),
                ArProjectedPercentage("HoldArcIntensity", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
    });

    /// <summary>IDs unique, schemas and capability claims well-formed, before consumers opt in.</summary>
    internal static bool IsHoldPilotWellFormed =>
        HoldFxPilot.Count == 5
        && HoldFxPilot.All(contract => contract.IsWellFormed)
        && HoldFxPilot.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == HoldFxPilot.Count;

    /// <summary>
    /// The fifth contract pilot: the stage-wide decorative sky. The four ambient layers, the star field, the
    /// petals and the light beams are drawn by both renderers; shooting stars are the second GPU-only member
    /// (the WPF stage has no meteor pass), so WPF stays Unsupported there. Every member drifts or twinkles on
    /// its own clock (IntrinsicAnimation) and freezes under Reduced Motion. The star twinkle has no strobe
    /// analysis yet (Unverified, resolves to suppression); the slow layers carry no flash. Over a camera,
    /// backdrop stars remain Unsupported, while key-anchored beams have a new S5 3D projection and stay
    /// Unverified after sampled WARP checks (the full beam setting/quality matrix is not covered); the four
    /// ambient slots, shooting stars and petals are Native only as screen-space camera composites. Their six WARP
    /// captured frames are checked by
    /// <c>VerifyArOverlayCapturedFrames</c>; this is not homography-projected or world-space AR. Costs all
    /// Unmeasured.
    /// </summary>
    internal static IReadOnlyList<EffectFamilyContract> AmbientFxPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectFamilyContract(
            "ambient.energy",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Choice("AmbientEnergy", ArAmbientParameterBindings),
                Percentage("AmbientEnergyAmount", 0, 100, ArAmbientParameterBindings),
                Percentage("AmbientEnergySpeed", 0, 100, ArAmbientParameterBindings),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Native),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "ambient.nature",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Choice("AmbientNature", ArAmbientParameterBindings),
                Percentage("AmbientNatureAmount", 0, 100, ArAmbientParameterBindings),
                Percentage("AmbientNatureSpeed", 0, 100, ArAmbientParameterBindings),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Native),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "ambient.light",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Choice("AmbientLight", ArAmbientParameterBindings),
                Percentage("AmbientLightAmount", 0, 100, ArAmbientParameterBindings),
                Percentage("AmbientLightSpeed", 0, 100, ArAmbientParameterBindings),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Native),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "ambient.cosmic",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Choice("AmbientCosmic", ArAmbientParameterBindings),
                Percentage("AmbientCosmicAmount", 0, 100, ArAmbientParameterBindings),
                Percentage("AmbientCosmicSpeed", 0, 100, ArAmbientParameterBindings),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Native),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "atmosphere.stars",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Boolean("ShowStars"),
                Percentage("StarDensity", 0, 100),
            }),
            // AR Unsupported is not a style choice: Stars() is called only from PsBackground, and the background
            // pass is skipped whenever the camera is the backdrop or the output is a chroma frame
            // (Gpu/GpuStageRenderer.cs, "if (!alphaOutput && !cameraActive)"). VerifyArOverlayCapturedFrames
            // already proves it: turning ShowStars on leaves the camera composite byte-identical.
            new EffectCapabilityProfile(Native, Native, Native, Unsupported),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "atmosphere.shooting-stars",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Boolean("ShootingStars", GpuOnlyArAmbientParameterBindings),
                Percentage("ShootingStarsAmount", 0, 100, GpuOnlyArAmbientParameterBindings),
            }),
            new EffectCapabilityProfile(Unsupported, Native, Native, Native),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "atmosphere.petals",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                Boolean("ShowPetals", GpuOnlyArAmbientParameterBindings),
                Percentage("PetalAmount", 0, 150, GpuOnlyArAmbientParameterBindings),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Native),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "atmosphere.beams",
            EffectPhase.Ambient, EffectTrigger.SceneTick, EffectLifecycle.Continuous, EffectAnchor.Stage,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowLightBeams"),
                ArProjectedPercentage("BeamIntensity", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
    });

    /// <summary>
    /// The sixth contract pilot: the particle systems. Sparks, wisps and flames all run their own physics
    /// on their own clock (IntrinsicAnimation) and are drawn by both renderers. Sparks and flames flicker
    /// bright with no strobe analysis yet, so their flash risk stays Unverified and resolves to
    /// suppression; the soft plasma wisps carry no flash. S5 retains pitch-tagged bursts/emitters and projects
    /// them through the calibrated pose; selected particle/release cases now pass WARP pixel/counter checks, but
    /// all three AR family and parameter states remain Unverified pending broader shape/setting/lifecycle coverage.
    /// Costs all Unmeasured.
    /// </summary>
    internal static IReadOnlyList<EffectFamilyContract> ParticlesFxPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectFamilyContract(
            "particles.sparks",
            EffectPhase.Impact, EffectTrigger.NoteOn, EffectLifecycle.FixedDuration, EffectAnchor.HitPoint,
            EffectClock.RenderElapsed, EffectSeedPolicy.PerNote,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowEmbers"),
                ArProjectedPercentage("ParticleAmount", 0, 120),
                ArProjectedPercentage("ParticleSpeed", 0, 300),
                ArProjectedPercentage("ParticleSize", .2, 16),
                ArProjectedPercentage("ParticleGlow", 0, 200),
                ArProjectedPercentage("ParticleLife", .05, 3),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "particles.wisps",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowWisps"),
                ArProjectedPercentage("WispAmount", 0, 150),
                ArProjectedPercentage("WispSpeed", 20, 600),
                ArProjectedPercentage("WispTurbulence", 0, 100),
                ArProjectedPercentage("WispGlow", 0, 200),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
        new EffectFamilyContract(
            "particles.flames",
            EffectPhase.Hold, EffectTrigger.NoteHeld, EffectLifecycle.WhileNoteHeld, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedBoolean("ShowFlame"),
                ArProjectedPercentage("FlameIntensity", 0, 100),
                ArProjectedPercentage("FlameHeight", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.IntrinsicAnimation, EffectShapeCue.None)),
    });

    /// <summary>IDs unique, schemas and capability claims well-formed, before consumers opt in.</summary>
    internal static bool IsParticlesPilotWellFormed =>
        ParticlesFxPilot.Count == 3
        && ParticlesFxPilot.All(contract => contract.IsWellFormed)
        && ParticlesFxPilot.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == ParticlesFxPilot.Count;

    /// <summary>
    /// The seventh contract pilot: what happens at the key when a note ends. A single family: the release
    /// effect is an event-driven decay with no idle animation and no flash, drawn by both stage renderers.
    /// S5 retains release events for calibrated 3D projection rather than drawing a stage-space sprite; Dissolve
    /// is now read back on WARP, but the family and parameter bindings remain Unverified until release styles,
    /// intensities and lifecycle variants are covered more broadly. Cost Unmeasured.
    /// </summary>
    internal static IReadOnlyList<EffectFamilyContract> ReleaseFxPilot { get; } = Array.AsReadOnly(new[]
    {
        new EffectFamilyContract(
            "release.fx",
            EffectPhase.Release, EffectTrigger.NoteOff, EffectLifecycle.FixedDuration, EffectAnchor.Key,
            EffectClock.RenderElapsed, EffectSeedPolicy.None,
            Array.AsReadOnly(new[]
            {
                ArProjectedChoice("ReleaseEffect"),
                ArProjectedPercentage("ReleaseIntensity", 0, 100),
            }),
            new EffectCapabilityProfile(Native, Native, Native, Unverified),
            new EffectCostProfile(UnmeasuredCost, UnmeasuredCost, UnmeasuredCost, UnmeasuredCost),
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None)),
    });

    /// <summary>IDs unique, schemas and capability claims well-formed, before consumers opt in.</summary>
    internal static bool IsReleasePilotWellFormed =>
        ReleaseFxPilot.Count == 1
        && ReleaseFxPilot.All(contract => contract.IsWellFormed)
        && ReleaseFxPilot.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == ReleaseFxPilot.Count;

    /// <summary>IDs unique, schemas and capability claims well-formed, before consumers opt in.</summary>
    internal static bool IsAmbientPilotWellFormed =>
        AmbientFxPilot.Count == 8
        && AmbientFxPilot.All(contract => contract.IsWellFormed)
        && AmbientFxPilot.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == AmbientFxPilot.Count;

    /// <summary>Every typed family across the pilots, for catalog-driven global toggles.</summary>
    internal static IReadOnlyList<EffectFamilyContract> AllFamilies { get; } =
        Array.AsReadOnly(ImpactFxPilot.Concat(FallingFxPilot).Concat(HoldFxPilot).Concat(AmbientFxPilot).Concat(ParticlesFxPilot).Concat(ReleaseFxPilot).ToArray());

    /// <summary>Checks IDs, aliases, parameter schemas and capability/fallback claims before consumers opt in.</summary>
    internal static bool IsPilotWellFormed
    {
        get
        {
            var contracts = NoteMaterialPilot;
            var aliases = contracts.SelectMany(contract => contract.LegacyAliases).ToArray();
            return contracts.Count == 3
                && contracts.All(contract => contract.IsWellFormed)
                && contracts.Select(contract => contract.Id).Distinct(StringComparer.Ordinal).Count() == contracts.Count
                && contracts.Select(contract => contract.GpuStyleId).Distinct().Count() == contracts.Count
                && aliases.Distinct(StringComparer.Ordinal).Count() == aliases.Length;
        }
    }

    internal static EffectContract? FindById(string? id) => id is null
        ? null
        : NoteMaterialPilot.FirstOrDefault(contract => string.Equals(contract.Id, id, StringComparison.Ordinal));

    /// <summary>Resolves only an exact persisted value; unknown/case-changed settings remain the settings model's concern.</summary>
    internal static EffectContract? FindByLegacyAlias(string? alias) => alias is null
        ? null
        : NoteMaterialPilot.FirstOrDefault(contract => contract.LegacyAliases.Any(value => string.Equals(value, alias, StringComparison.Ordinal)));

    private static EffectParameterContract Percentage(
        string settingName,
        double minimum,
        double maximum,
        EffectParameterBindingProfile? bindings = null) =>
        new(settingName, EffectParameterKind.Percentage, minimum, maximum, bindings ?? NativeParameterBindings);

    private static EffectParameterContract Boolean(string settingName, EffectParameterBindingProfile? bindings = null) =>
        new(settingName, EffectParameterKind.Boolean, null, null, bindings ?? NativeParameterBindings);

    private static EffectParameterContract Choice(string settingName, EffectParameterBindingProfile? bindings = null) =>
        new(settingName, EffectParameterKind.Choice, null, null, bindings ?? NativeParameterBindings);

    private static EffectParameterContract ArProjectedPercentage(string settingName, double minimum, double maximum) =>
        Percentage(settingName, minimum, maximum, ArProjectedParameterBindings);

    private static EffectParameterContract ArProjectedBoolean(string settingName) =>
        Boolean(settingName, ArProjectedParameterBindings);

    private static EffectParameterContract ArProjectedChoice(string settingName) =>
        Choice(settingName, ArProjectedParameterBindings);
}
