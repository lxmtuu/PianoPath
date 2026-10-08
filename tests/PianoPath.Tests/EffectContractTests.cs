using Xunit;

namespace PianoPath.Tests;

public sealed class EffectContractTests
{
    [Fact]
    public void Pilot_has_unique_typed_ids_and_resolves_only_exact_legacy_values()
    {
        var contracts = EffectContractCatalog.NoteMaterialPilot;

        Assert.True(EffectContractCatalog.IsPilotWellFormed);
        Assert.Equal(new[] { "note.material.solid", "note.material.gem", "note.material.crystal-outline" }, contracts.Select(contract => contract.Id));
        Assert.Equal("note.material.solid", EffectContractCatalog.FindByLegacyAlias("Solid")?.Id);
        Assert.Equal("note.material.gem", EffectContractCatalog.FindByLegacyAlias("Gem")?.Id);
        Assert.Equal("note.material.crystal-outline", EffectContractCatalog.FindByLegacyAlias("CrystalOutline")?.Id);
        Assert.Null(EffectContractCatalog.FindByLegacyAlias("gem"));
        Assert.Null(EffectContractCatalog.FindByLegacyAlias("Neon"));
        Assert.Null(EffectContractCatalog.FindById("note.material.streak"));
    }

    [Fact]
    public void Pilot_keeps_the_existing_gpu_material_ids_and_render_lifecycle()
    {
        var solid = EffectContractCatalog.FindById("note.material.solid")!;
        var gem = EffectContractCatalog.FindById("note.material.gem")!;
        var crystal = EffectContractCatalog.FindById("note.material.crystal-outline")!;

        Assert.Equal("Solid", Assert.Single(solid.LegacyAliases));
        Assert.Equal("Gem", Assert.Single(gem.LegacyAliases));
        Assert.Equal("CrystalOutline", Assert.Single(crystal.LegacyAliases));
        Assert.Equal(0, solid.GpuStyleId);
        Assert.Equal(4, gem.GpuStyleId);
        Assert.Equal(5, crystal.GpuStyleId);
        Assert.Equal(EffectShapeCue.OpaqueBodyAndRim, solid.Accessibility.NonColorCue);

        foreach (var contract in new[] { solid, gem, crystal })
        {
            Assert.Equal(EffectPhase.NoteMaterial, contract.Phase);
            Assert.Equal(EffectTrigger.NoteBodyRendered, contract.Trigger);
            Assert.Equal(EffectLifecycle.WhileNoteVisible, contract.Lifecycle);
            Assert.Equal(EffectAnchor.NoteBody, contract.Anchor);
            Assert.Equal(EffectClock.None, contract.Clock);
            Assert.Equal(EffectSeedPolicy.None, contract.SeedPolicy);
            Assert.True(contract.IsWellFormed);
        }

        Assert.Equal(EffectShapeCue.FacetPlanes, gem.Accessibility.NonColorCue);
        Assert.Equal(EffectShapeCue.AngularContourAndFacets, crystal.Accessibility.NonColorCue);
        Assert.All(new[] { solid, gem, crystal }, contract =>
        {
            Assert.Equal(EffectFlashRisk.None, contract.Accessibility.FlashRisk);
            Assert.Equal(EffectReducedMotionBehavior.NoIndependentAnimation, contract.Accessibility.ReducedMotionBehavior);
        });
    }

    [Fact]
    public void Capability_states_do_not_overclaim_recording_or_ar_material_support()
    {
        Assert.Equal("ar.note.flat-quad", EffectContractCatalog.ArFlatQuadFallbackId);
        foreach (var contract in EffectContractCatalog.NoteMaterialPilot)
        {
            Assert.Equal(EffectBackendState.Native, contract.Capabilities.WpfStage.State);
            Assert.Equal(EffectBackendState.Native, contract.Capabilities.GpuStage.State);
            Assert.Equal(EffectBackendState.Unverified, contract.Capabilities.Recording.State);
            Assert.Equal(EffectBackendState.Fallback, contract.Capabilities.ArOverlay.State);
            Assert.Equal("ar.note.flat-quad", contract.Capabilities.ArOverlay.FallbackId);

            foreach (var cost in new[] { contract.Cost.WpfStage, contract.Cost.Recording, contract.Cost.ArOverlay })
            {
                Assert.Equal(EffectCostState.Unmeasured, cost.State);
                Assert.Null(cost.P95Milliseconds);
                Assert.Null(cost.EvidenceId);
                Assert.True(cost.IsWellFormed);
            }

            // The GPU-stage entry is the manual-review record of the isolated physical-adapter probe:
            // a note-pass p95 with an evidence pointer, not a budget and not a whole-frame number.
            var gpuCost = contract.Cost.GpuStage;
            Assert.Equal(EffectCostState.Measured, gpuCost.State);
            Assert.Contains("NOTE-MATERIAL-GPU-BENCHMARK", gpuCost.EvidenceId);
            Assert.Contains("AMD Radeon(TM) Graphics", gpuCost.EvidenceId);
            var expectedP95 = contract.Id == "note.material.gem" ? 0.02688
                : contract.Id == "note.material.solid" ? 0.02356
                : 0.0268;
            Assert.Equal(expectedP95, gpuCost.P95Milliseconds!.Value, 10);
            Assert.True(gpuCost.IsWellFormed);
        }
    }

    [Fact]
    public void Material_parameters_preserve_clamps_and_per_renderer_bindings()
    {
        var expectedRanges = new Dictionary<string, (double Minimum, double Maximum)>(StringComparer.Ordinal)
        {
            ["NoteTint"] = (0, 100),
            ["NoteGlow"] = (0, 200),
            ["NoteEdge"] = (0, 200),
            ["NoteEdgeWidth"] = (0, 100),
            ["NoteRefraction"] = (0, 100),
        };

        foreach (var contract in EffectContractCatalog.NoteMaterialPilot)
        {
            var numeric = contract.Parameters.Where(parameter => parameter.Kind == EffectParameterKind.Percentage).ToDictionary(parameter => parameter.SettingName, StringComparer.Ordinal);
            Assert.Equal(expectedRanges.Keys.OrderBy(key => key, StringComparer.Ordinal), numeric.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.All(numeric.Values, parameter => Assert.True(parameter.IsWellFormed));
            foreach (var (setting, (minimum, maximum)) in expectedRanges)
            {
                Assert.Equal(minimum, numeric[setting].Minimum!.Value);
                Assert.Equal(maximum, numeric[setting].Maximum!.Value);
            }
        }

        var solid = EffectContractCatalog.FindById("note.material.solid")!;
        var gem = EffectContractCatalog.FindById("note.material.gem")!;
        var crystal = EffectContractCatalog.FindById("note.material.crystal-outline")!;
        Assert.Contains(solid.Parameters, parameter => parameter.SettingName == "Notes3D" && parameter.Kind == EffectParameterKind.Boolean);
        Assert.Contains(gem.Parameters, parameter => parameter.SettingName == "Notes3D" && parameter.Kind == EffectParameterKind.Boolean);
        Assert.DoesNotContain(crystal.Parameters, parameter => parameter.SettingName == "Notes3D");

        // Solid and Gem consume every look setting on both stage renderers; the evidence lives in the
        // WPF default branch and the GPU Solid/Gem shader branches.
        Assert.All(solid.Parameters.Concat(gem.Parameters), parameter =>
        {
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.WpfStage);
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.GpuStage);
            Assert.Equal(EffectParameterBindingState.Unverified, parameter.Bindings.Recording);
            Assert.Equal(EffectParameterBindingState.Ignored, parameter.Bindings.ArOverlay);
        });
        Assert.All(crystal.Parameters.Where(parameter => parameter.SettingName != "NoteRefraction"), parameter =>
        {
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.WpfStage);
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.GpuStage);
            Assert.Equal(EffectParameterBindingState.Unverified, parameter.Bindings.Recording);
            Assert.Equal(EffectParameterBindingState.Ignored, parameter.Bindings.ArOverlay);
        });
        var crystalRefraction = crystal.Parameters.Single(parameter => parameter.SettingName == "NoteRefraction");
        Assert.Equal(EffectParameterBindingState.Applied, crystalRefraction.Bindings.WpfStage);
        Assert.Equal(EffectParameterBindingState.Ignored, crystalRefraction.Bindings.GpuStage);
        Assert.Equal(EffectParameterBindingState.Unverified, crystalRefraction.Bindings.Recording);
        Assert.Equal(EffectParameterBindingState.Ignored, crystalRefraction.Bindings.ArOverlay);
    }

    [Fact]
    public void Impact_fx_pilot_is_well_formed_with_conservative_flash_classification()
    {
        Assert.True(EffectContractCatalog.IsImpactPilotWellFormed);
        var wave = EffectContractCatalog.ImpactFxPilot.Single(contract => contract.Id == "impact.wave");
        var flash = EffectContractCatalog.ImpactFxPilot.Single(contract => contract.Id == "impact.flash");
        var burst = EffectContractCatalog.ImpactFxPilot.Single(contract => contract.Id == "impact.burst");
        var morph = EffectContractCatalog.ImpactFxPilot.Single(contract => contract.Id == "impact.morph");
        Assert.Equal(EffectFlashRisk.None, wave.Accessibility.FlashRisk);
        Assert.Equal(EffectFlashRisk.Local, flash.Accessibility.FlashRisk);
        Assert.Equal(EffectFlashRisk.Unverified, burst.Accessibility.FlashRisk);
        Assert.Equal(EffectFlashRisk.None, morph.Accessibility.FlashRisk);

        // Reduce Flashes scales with each family's declared risk and stays off otherwise.
        Assert.Equal(FlashSuppression.NoChange, AccessibilityContractResolver.Resolve(wave.Accessibility, false, true, false).Flash);
        Assert.NotEqual(FlashSuppression.NoChange, AccessibilityContractResolver.Resolve(flash.Accessibility, false, true, false).Flash);
        Assert.Equal(FlashSuppression.Suppress, AccessibilityContractResolver.Resolve(burst.Accessibility, false, true, false).Flash);
        Assert.Equal(FlashSuppression.NoChange, AccessibilityContractResolver.Resolve(morph.Accessibility, false, false, false).Flash);

        // No isolated probe measured any impact family yet: no cost may be fabricated.
        Assert.All(EffectContractCatalog.ImpactFxPilot, contract => Assert.All(
            new[] { contract.Cost.WpfStage, contract.Cost.GpuStage, contract.Cost.Recording, contract.Cost.ArOverlay },
            cost => Assert.Equal(EffectCostState.Unmeasured, cost.State)));
    }

    [Fact]
    public void Falling_fx_pilot_classifies_motion_honestly_and_names_the_gpu_only_shimmer()
    {
        Assert.True(EffectContractCatalog.IsFallingPilotWellFormed);
        var trail = EffectContractCatalog.FallingFxPilot.Single(contract => contract.Id == "falling.trail");
        var pulse = EffectContractCatalog.FallingFxPilot.Single(contract => contract.Id == "falling.pulse");
        var shimmer = EffectContractCatalog.FallingFxPilot.Single(contract => contract.Id == "falling.shimmer");

        Assert.Equal(EffectReducedMotionBehavior.NoIndependentAnimation, trail.Accessibility.ReducedMotionBehavior);
        Assert.Equal(EffectReducedMotionBehavior.IntrinsicAnimation, pulse.Accessibility.ReducedMotionBehavior);
        Assert.Equal(EffectReducedMotionBehavior.IntrinsicAnimation, shimmer.Accessibility.ReducedMotionBehavior);

        // Reduced Motion freezes the self-animated families and leaves the travel-derived trail alone.
        Assert.Equal(MotionSuppression.FreezeIntrinsicAnimation, AccessibilityContractResolver.Resolve(pulse.Accessibility, true, false, false).Motion);
        Assert.Equal(MotionSuppression.FreezeIntrinsicAnimation, AccessibilityContractResolver.Resolve(shimmer.Accessibility, true, false, false).Motion);
        Assert.Equal(MotionSuppression.NoChange, AccessibilityContractResolver.Resolve(trail.Accessibility, true, false, false).Motion);

        // The shimmer is the GPU-only member: WPF never reads NoteShimmer, so no capability claim may say Native there.
        Assert.Equal(EffectBackendState.Unsupported, shimmer.Capabilities.WpfStage.State);
        Assert.Null(shimmer.Capabilities.WpfStage.FallbackId);
        Assert.Equal(EffectBackendState.Native, shimmer.Capabilities.GpuStage.State);
        Assert.Equal(EffectBackendState.Native, trail.Capabilities.WpfStage.State);
        Assert.Equal(EffectBackendState.Native, pulse.Capabilities.WpfStage.State);
        Assert.All(shimmer.Parameters, parameter =>
        {
            Assert.Equal(EffectParameterBindingState.Ignored, parameter.Bindings.WpfStage);
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.GpuStage);
        });

        Assert.All(EffectContractCatalog.FallingFxPilot, contract => Assert.All(
            new[] { contract.Cost.WpfStage, contract.Cost.GpuStage, contract.Cost.Recording, contract.Cost.ArOverlay },
            cost => Assert.Equal(EffectCostState.Unmeasured, cost.State)));
    }

    [Fact]
    public void Hold_fx_pilot_separates_the_duration_bar_from_the_self_animated_families()
    {
        Assert.True(EffectContractCatalog.IsHoldPilotWellFormed);
        var bar = EffectContractCatalog.HoldFxPilot.Single(contract => contract.Id == "hold.bar");
        var arc = EffectContractCatalog.HoldFxPilot.Single(contract => contract.Id == "hold.arc");
        var breath = EffectContractCatalog.HoldFxPilot.Single(contract => contract.Id == "hold.breath");

        // The bar derives its motion from the held note's remaining duration; the rest own their clock.
        Assert.Equal(EffectReducedMotionBehavior.NoIndependentAnimation, bar.Accessibility.ReducedMotionBehavior);
        Assert.Equal(EffectClock.SongPosition, bar.Clock);
        Assert.Equal(EffectReducedMotionBehavior.IntrinsicAnimation, breath.Accessibility.ReducedMotionBehavior);
        Assert.Equal(EffectReducedMotionBehavior.IntrinsicAnimation, arc.Accessibility.ReducedMotionBehavior);
        Assert.Equal(MotionSuppression.NoChange, AccessibilityContractResolver.Resolve(bar.Accessibility, true, false, false).Motion);
        Assert.Equal(MotionSuppression.FreezeIntrinsicAnimation, AccessibilityContractResolver.Resolve(breath.Accessibility, true, false, false).Motion);

        // The flickering arc has no strobe analysis yet; breathing carries no flash.
        Assert.Equal(EffectFlashRisk.Unverified, arc.Accessibility.FlashRisk);
        Assert.Equal(FlashSuppression.Suppress, AccessibilityContractResolver.Resolve(arc.Accessibility, false, true, false).Flash);
        Assert.Equal(EffectFlashRisk.None, breath.Accessibility.FlashRisk);
        Assert.Equal(FlashSuppression.NoChange, AccessibilityContractResolver.Resolve(breath.Accessibility, false, true, false).Flash);

        // Every hold family lives exactly as long as the key stays down, on both stage renderers.
        Assert.All(EffectContractCatalog.HoldFxPilot, contract =>
        {
            Assert.Equal(EffectLifecycle.WhileNoteHeld, contract.Lifecycle);
            Assert.Equal(EffectBackendState.Native, contract.Capabilities.WpfStage.State);
            Assert.Equal(EffectBackendState.Native, contract.Capabilities.GpuStage.State);
        });
    }

    [Fact]
    public void All_families_union_covers_every_pilot_exactly_once()
    {
        Assert.Equal(24, EffectContractCatalog.AllFamilies.Count);
        Assert.Equal(
            EffectContractCatalog.ImpactFxPilot.Concat(EffectContractCatalog.FallingFxPilot)
                .Concat(EffectContractCatalog.HoldFxPilot).Concat(EffectContractCatalog.AmbientFxPilot)
                .Concat(EffectContractCatalog.ParticlesFxPilot).Concat(EffectContractCatalog.ReleaseFxPilot)
                .Select(family => family.Id).OrderBy(id => id, StringComparer.Ordinal),
            EffectContractCatalog.AllFamilies.Select(family => family.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Ar_overlay_support_is_limited_to_the_captured_ambient_layers()
    {
        var nativeInAr = new HashSet<string>(StringComparer.Ordinal)
        {
            "ambient.energy", "ambient.nature", "ambient.light", "ambient.cosmic",
            "atmosphere.shooting-stars", "atmosphere.petals",
        };
        Assert.Equal(6, EffectContractCatalog.AllFamilies.Count(family => family.Capabilities.ArOverlay.State == EffectBackendState.Native));
        foreach (var family in EffectContractCatalog.AllFamilies)
        {
            Assert.Equal(nativeInAr.Contains(family.Id) ? EffectBackendState.Native : EffectBackendState.Unsupported,
                family.Capabilities.ArOverlay.State);
            Assert.All(family.Parameters, parameter => Assert.Equal(
                nativeInAr.Contains(family.Id) ? EffectParameterBindingState.Applied : EffectParameterBindingState.Ignored,
                parameter.Bindings.ArOverlay));
            Assert.Equal(EffectBackendState.Unverified, family.Capabilities.Recording.State);
        }
        var shootingStars = EffectContractCatalog.AllFamilies.Single(family => family.Id == "atmosphere.shooting-stars");
        Assert.Equal(EffectBackendState.Unsupported, shootingStars.Capabilities.WpfStage.State);
        Assert.All(shootingStars.Parameters, parameter =>
        {
            Assert.Equal(EffectParameterBindingState.Ignored, parameter.Bindings.WpfStage);
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.GpuStage);
            Assert.Equal(EffectParameterBindingState.Applied, parameter.Bindings.ArOverlay);
        });
        Assert.Equal(150d, EffectContractCatalog.AllFamilies.Single(family => family.Id == "atmosphere.petals")
            .Parameters.Single(parameter => parameter.SettingName == "PetalAmount").Maximum!.Value);
        Assert.All(EffectContractCatalog.NoteMaterialPilot, material =>
        {
            Assert.Equal(EffectBackendState.Fallback, material.Capabilities.ArOverlay.State);
            Assert.Equal("ar.note.flat-quad", material.Capabilities.ArOverlay.FallbackId);
        });
    }

    [Fact]
    public void Ambient_pilot_freezes_every_layer_and_names_the_gpu_only_meteors()
    {
        Assert.True(EffectContractCatalog.IsAmbientPilotWellFormed);
        var stars = EffectContractCatalog.AmbientFxPilot.Single(contract => contract.Id == "atmosphere.stars");
        var meteors = EffectContractCatalog.AmbientFxPilot.Single(contract => contract.Id == "atmosphere.shooting-stars");
        var energy = EffectContractCatalog.AmbientFxPilot.Single(contract => contract.Id == "ambient.energy");

        // The whole sky animates on its own clock, so Reduced Motion freezes all of it.
        Assert.All(EffectContractCatalog.AmbientFxPilot, contract =>
            Assert.Equal(EffectReducedMotionBehavior.IntrinsicAnimation, contract.Accessibility.ReducedMotionBehavior));
        Assert.All(EffectContractCatalog.AmbientFxPilot, contract =>
            Assert.Equal(MotionSuppression.FreezeIntrinsicAnimation, AccessibilityContractResolver.Resolve(contract.Accessibility, true, false, false).Motion));

        // Meteors are the second GPU-only member; the star twinkle is un-analysed flash risk.
        Assert.Equal(EffectBackendState.Unsupported, meteors.Capabilities.WpfStage.State);
        Assert.Equal(EffectBackendState.Native, meteors.Capabilities.GpuStage.State);
        Assert.Equal(EffectFlashRisk.Unverified, stars.Accessibility.FlashRisk);
        Assert.Equal(FlashSuppression.Suppress, AccessibilityContractResolver.Resolve(stars.Accessibility, false, true, false).Flash);
        Assert.Equal(EffectFlashRisk.None, energy.Accessibility.FlashRisk);
        Assert.Equal(FlashSuppression.NoChange, AccessibilityContractResolver.Resolve(energy.Accessibility, false, true, false).Flash);
    }
}
