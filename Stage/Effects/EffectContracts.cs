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
        EffectParameterKind.Boolean => Minimum is null && Maximum is null,
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
        && (ReducedMotionBehavior is EffectReducedMotionBehavior.NoIndependentAnimation or EffectReducedMotionBehavior.Unverified)
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
    /// settings serialization, UI filtering or renderer dispatch. WPF and GPU are marked Native because their
    /// direct note-material render checks exercise these three styles; recording stays Unverified until a
    /// material-specific captured-frame check exists. AR names its actual flat-quad fallback instead of claiming
    /// native facet/material support.
    /// </summary>
internal static class EffectContractCatalog
{
    internal const string ArFlatQuadFallbackId = "ar.note.flat-quad";

    private static readonly EffectBackendCapability Native = new(EffectBackendState.Native);
    private static readonly EffectBackendCapability Unverified = new(EffectBackendState.Unverified);
    private static readonly EffectBackendCapability ArFlatFallback = new(EffectBackendState.Fallback, ArFlatQuadFallbackId);
    private static readonly EffectParameterBindingProfile NativeParameterBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Ignored);
    private static readonly EffectParameterBindingProfile WpfOnlyRefractionBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Ignored,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Ignored);
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
        Percentage("NoteTint", 0, 100),
        Percentage("NoteGlow", 0, 200),
        Percentage("NoteEdge", 0, 200),
        Percentage("NoteEdgeWidth", 0, 100),
        Percentage("NoteRefraction", 0, 100),
        Boolean("Notes3D"),
    });

    private static readonly IReadOnlyList<EffectParameterContract> GemParameters = Array.AsReadOnly(new[]
    {
        Percentage("NoteTint", 0, 100),
        Percentage("NoteGlow", 0, 200),
        Percentage("NoteEdge", 0, 200),
        Percentage("NoteEdgeWidth", 0, 100),
        Percentage("NoteRefraction", 0, 100),
        Boolean("Notes3D"),
    });

    private static readonly IReadOnlyList<EffectParameterContract> CrystalOutlineParameters = Array.AsReadOnly(new[]
    {
        Percentage("NoteTint", 0, 100),
        Percentage("NoteGlow", 0, 200),
        Percentage("NoteEdge", 0, 200),
        Percentage("NoteEdgeWidth", 0, 100),
        // WPF paints the crystal's refraction fringe; the current GPU CrystalOutline branch does not consume it.
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
            new EffectCapabilityProfile(Native, Native, Unverified, ArFlatFallback),
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
            new EffectCapabilityProfile(Native, Native, Unverified, ArFlatFallback),
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
            new EffectCapabilityProfile(Native, Native, Unverified, ArFlatFallback),
            CrystalOutlineCosts,
            new EffectAccessibilityContract(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.AngularContourAndFacets)),
    });

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

    private static EffectParameterContract Boolean(string settingName) =>
        new(settingName, EffectParameterKind.Boolean, null, null, NativeParameterBindings);
}
