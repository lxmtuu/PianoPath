using Xunit;

namespace PianoPath.Tests;

public sealed class EffectContractTests
{
    [Fact]
    public void Pilot_has_unique_typed_ids_and_resolves_only_exact_legacy_values()
    {
        var contracts = EffectContractCatalog.NoteMaterialPilot;

        Assert.True(EffectContractCatalog.IsPilotWellFormed);
        Assert.Equal(new[] { "note.material.gem", "note.material.crystal-outline" }, contracts.Select(contract => contract.Id));
        Assert.Equal("note.material.gem", EffectContractCatalog.FindByLegacyAlias("Gem")?.Id);
        Assert.Equal("note.material.crystal-outline", EffectContractCatalog.FindByLegacyAlias("CrystalOutline")?.Id);
        Assert.Null(EffectContractCatalog.FindByLegacyAlias("gem"));
        Assert.Null(EffectContractCatalog.FindByLegacyAlias("Neon"));
        Assert.Null(EffectContractCatalog.FindById("note.material.streak"));
    }

    [Fact]
    public void Pilot_keeps_the_existing_gpu_material_ids_and_render_lifecycle()
    {
        var gem = EffectContractCatalog.FindById("note.material.gem")!;
        var crystal = EffectContractCatalog.FindById("note.material.crystal-outline")!;

        Assert.Equal("Gem", Assert.Single(gem.LegacyAliases));
        Assert.Equal("CrystalOutline", Assert.Single(crystal.LegacyAliases));
        Assert.Equal(4, gem.GpuStyleId);
        Assert.Equal(5, crystal.GpuStyleId);

        foreach (var contract in new[] { gem, crystal })
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
        Assert.All(new[] { gem, crystal }, contract =>
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
            var expectedP95 = contract.Id == "note.material.gem" ? 0.02688 : 0.0268;
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

        var gem = EffectContractCatalog.FindById("note.material.gem")!;
        var crystal = EffectContractCatalog.FindById("note.material.crystal-outline")!;
        Assert.Contains(gem.Parameters, parameter => parameter.SettingName == "Notes3D" && parameter.Kind == EffectParameterKind.Boolean);
        Assert.DoesNotContain(crystal.Parameters, parameter => parameter.SettingName == "Notes3D");

        Assert.All(gem.Parameters, parameter =>
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
}
