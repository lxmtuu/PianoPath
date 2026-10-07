using Xunit;

namespace PianoPath.Tests;

public sealed class AccessibilityContractTests
{
    private static EffectAccessibilityContract Of(
        EffectFlashRisk flash,
        EffectReducedMotionBehavior motion,
        EffectShapeCue cue) => new(flash, motion, cue);

    [Fact]
    public void Toggle_catalog_is_stable_off_by_default_and_unique()
    {
        Assert.True(AccessibilityContractResolver.IsCatalogWellFormed);
        Assert.Equal(
            new[] { "a11y.reduced-motion", "a11y.reduce-flashes", "a11y.high-contrast" },
            AccessibilityContractResolver.Toggles.Select(toggle => toggle.PersistedId));
        Assert.All(AccessibilityContractResolver.Toggles, toggle => Assert.False(toggle.DefaultEnabled));
    }

    [Fact]
    public void All_toggles_off_keeps_every_pilot_material_untouched()
    {
        foreach (var contract in EffectContractCatalog.NoteMaterialPilot)
        {
            var resolution = AccessibilityContractResolver.Resolve(contract.Accessibility, false, false, false);
            Assert.True(resolution.IsWellFormed);
            Assert.Equal(FlashSuppression.NoChange, resolution.Flash);
            Assert.Equal(MotionSuppression.NoChange, resolution.Motion);
            Assert.False(resolution.NeedsNonColorCueWork);
        }
    }

    [Fact]
    public void Reduce_flashes_scales_with_the_effect_own_flash_risk()
    {
        Assert.Equal(FlashSuppression.NoChange,
            AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), false, true, false).Flash);
        Assert.Equal(FlashSuppression.CapLocalIntensity,
            AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.Local, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), false, true, false).Flash);
        Assert.Equal(FlashSuppression.Suppress,
            AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.FullFrame, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), false, true, false).Flash);
        // Unverified flash evidence resolves to the safe side until a review says otherwise.
        Assert.Equal(FlashSuppression.Suppress,
            AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.Unverified, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), false, true, false).Flash);

        // The pilot materials declare no intrinsic flash, so Reduce Flashes leaves their look untouched.
        Assert.All(EffectContractCatalog.NoteMaterialPilot, contract =>
            Assert.Equal(FlashSuppression.NoChange,
                AccessibilityContractResolver.Resolve(contract.Accessibility, false, true, false).Flash));
    }

    [Fact]
    public void Reduced_motion_freezes_only_intrinsic_animation_and_never_touches_unverified_silently()
    {
        Assert.Equal(MotionSuppression.NoChange,
            AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), true, false, false).Motion);
        Assert.Equal(MotionSuppression.FreezeIntrinsicAnimation,
            AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.None, EffectReducedMotionBehavior.Unverified, EffectShapeCue.None), true, false, false).Motion);

        Assert.All(EffectContractCatalog.NoteMaterialPilot, contract =>
            Assert.Equal(MotionSuppression.NoChange,
                AccessibilityContractResolver.Resolve(contract.Accessibility, true, false, false).Motion));
    }

    [Fact]
    public void High_contrast_flags_effects_whose_shape_would_vanish_without_hue()
    {
        Assert.True(AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), false, false, true).NeedsNonColorCueWork);
        Assert.False(AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.OpaqueBodyAndRim), false, false, true).NeedsNonColorCueWork);
        Assert.False(AccessibilityContractResolver.Resolve(Of(EffectFlashRisk.None, EffectReducedMotionBehavior.NoIndependentAnimation, EffectShapeCue.None), false, false, false).NeedsNonColorCueWork);

        // Every pilot material carries a hue-independent shape cue, so high contrast needs no rework for them.
        Assert.All(EffectContractCatalog.NoteMaterialPilot, contract =>
            Assert.False(AccessibilityContractResolver.Resolve(contract.Accessibility, false, false, true).NeedsNonColorCueWork));
    }
}
