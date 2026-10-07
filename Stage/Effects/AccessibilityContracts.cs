namespace PianoPath;

/// <summary>
/// The global accessibility toggles the contract layer plans for. Persistence, localization and UI wiring
/// land in later Phase 2 slices; this type only names the stable set so metadata, tests and the resolver agree.
/// </summary>
internal enum AccessibilityToggle
{
    ReducedMotion,
    ReduceFlashes,
    HighContrast,
}

/// <summary>What Reduce Flashes does to one effect's own flash output.</summary>
internal enum FlashSuppression
{
    NoChange,
    CapLocalIntensity,
    Suppress,
}

/// <summary>What Reduced Motion does to one effect's intrinsic animation; it never rewrites the parent note's motion.</summary>
internal enum MotionSuppression
{
    NoChange,
    FreezeIntrinsicAnimation,
}

/// <summary>
/// One toggle's stable identity for the future persisted schema. Defaults are off so today's looks are the
/// baseline; enabling a toggle is always an explicit user choice applied over every preset.
/// </summary>
internal sealed record AccessibilityToggleContract(
    AccessibilityToggle Toggle,
    string PersistedId,
    bool DefaultEnabled)
{
    internal bool IsWellFormed => !string.IsNullOrWhiteSpace(PersistedId);
}

/// <summary>
/// The settings channels a global toggle overrides while enabled, so the renderer keeps consuming
/// <c>PianoVisualSettings</c> exactly as today and the policy lives in one reviewed place.
/// </summary>
internal enum AccessibilityChannel
{
    /// <summary>The impact flash strobe channel (ShowImpactFlash and its intensity).</summary>
    ImpactFlash,
    /// <summary>Intrinsic animation channels: background motion, halo pulse, falling pulse, held-key breathing.</summary>
    IndependentMotion,
    /// <summary>The note outline channel: a bright, wide edge so shape carries information that color alone would have to.</summary>
    NoteShapeCue,
}

/// <summary>One toggle's override policy; an empty channel list means metadata-only until renderer work exists.</summary>
internal sealed record AccessibilityTogglePolicy(
    AccessibilityToggle Toggle,
    IReadOnlyList<AccessibilityChannel> Channels)
{
    internal bool IsWellFormed => Channels is not null;
}

/// <summary>
/// The resolved per-effect behavior under the global accessibility state. Pure metadata: renderers and the
/// settings UI will consume it in later slices, so the safety semantics get reviewed before any pixel changes.
/// </summary>
internal sealed record AccessibilityResolution(
    FlashSuppression Flash,
    MotionSuppression Motion,
    bool NeedsNonColorCueWork)
{
    internal bool IsWellFormed =>
        (Flash is FlashSuppression.NoChange or FlashSuppression.CapLocalIntensity or FlashSuppression.Suppress)
        && (Motion is MotionSuppression.NoChange or MotionSuppression.FreezeIntrinsicAnimation);
}

/// <summary>
/// Resolves one effect's accessibility behavior from its intrinsic contract plus the global toggles.
/// Conservative by design: where the evidence is <c>Unverified</c>, the safe side of a flash-safety or
/// motion contract is to suppress/freeze until a review says otherwise, and high contrast flags effects
/// whose shape would vanish without hue so later renderer work can close the gap.
/// </summary>
internal static class AccessibilityContractResolver
{
    internal static IReadOnlyList<AccessibilityToggleContract> Toggles { get; } = Array.AsReadOnly(new[]
    {
        new AccessibilityToggleContract(AccessibilityToggle.ReducedMotion, "a11y.reduced-motion", false),
        new AccessibilityToggleContract(AccessibilityToggle.ReduceFlashes, "a11y.reduce-flashes", false),
        new AccessibilityToggleContract(AccessibilityToggle.HighContrast, "a11y.high-contrast", false),
    });

    /// <summary>
    /// Which settings channels each toggle overrides. High Contrast rides the existing note-edge channel:
    /// both renderers already draw the outline, so the shape cue needs no new renderer work.
    /// </summary>
    internal static IReadOnlyList<AccessibilityTogglePolicy> Policies { get; } = Array.AsReadOnly(new[]
    {
        new AccessibilityTogglePolicy(AccessibilityToggle.ReducedMotion,
            Array.AsReadOnly(new[] { AccessibilityChannel.IndependentMotion })),
        new AccessibilityTogglePolicy(AccessibilityToggle.ReduceFlashes,
            Array.AsReadOnly(new[] { AccessibilityChannel.ImpactFlash })),
        new AccessibilityTogglePolicy(AccessibilityToggle.HighContrast,
            Array.AsReadOnly(new[] { AccessibilityChannel.NoteShapeCue })),
    });

    internal static bool IsCatalogWellFormed =>
        Toggles.All(toggle => toggle.IsWellFormed)
        && Toggles.Select(toggle => toggle.PersistedId).Distinct(StringComparer.Ordinal).Count() == Toggles.Count
        && Toggles.All(toggle => !toggle.DefaultEnabled)
        && Toggles.Select(toggle => toggle.Toggle).Distinct().Count() == Toggles.Count
        && Policies.All(policy => policy.IsWellFormed)
        && Policies.Select(policy => policy.Toggle).Distinct().Count() == Toggles.Count;

    internal static AccessibilityResolution Resolve(
        EffectAccessibilityContract effect,
        bool reducedMotion,
        bool reduceFlashes,
        bool highContrast)
    {
        var flash = !reduceFlashes
            ? FlashSuppression.NoChange
            : effect.FlashRisk switch
            {
                EffectFlashRisk.None => FlashSuppression.NoChange,
                EffectFlashRisk.Local => FlashSuppression.CapLocalIntensity,
                EffectFlashRisk.FullFrame => FlashSuppression.Suppress,
                EffectFlashRisk.Unverified => FlashSuppression.Suppress,
                _ => FlashSuppression.Suppress,
            };
        var motion = !reducedMotion
            ? MotionSuppression.NoChange
            : effect.ReducedMotionBehavior switch
            {
                EffectReducedMotionBehavior.NoIndependentAnimation => MotionSuppression.NoChange,
                EffectReducedMotionBehavior.Unverified => MotionSuppression.FreezeIntrinsicAnimation,
                _ => MotionSuppression.FreezeIntrinsicAnimation,
            };
        var needsCueWork = highContrast && effect.NonColorCue == EffectShapeCue.None;
        return new AccessibilityResolution(flash, motion, needsCueWork);
    }
}
