"""Synthetic contract tests for tools/value_pilot_contract.py — no .NET, no Windows, no GPU.

The guard only reads text. These tests build a tiny repository that satisfies it, then break one
promise at a time: a hard-coded particle counter, a copied witness table, an unknown family,
an unhandled setting, a stale cell count, a document that still quotes the old frame count, a
workflow that drops the pilot's own fail-fast step, a choice switch with the wrong off value, or a
release scene that no longer lifts the key. The CI annotations that motivated this guard are
described in docs/AR-VALUE-MATRIX-PLAN.md.
"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import value_pilot_contract as v

# family, stage, switch, switch kind, off value, on value, value, value kind,
# numbers or choices literal, witness counter, pins
FAMILIES = [
    ("particles.wisps", "Held", "ShowWisps", "Boolean", "off", "", "WispAmount", "Percentage", "[15, 75, 150]", "ArParticleFxCount", []),
    ("particles.flames", "Held", "ShowFlame", "Boolean", "off", "", "FlameIntensity", "Percentage", "[20, 60, 100]", "ArParticleFxCount", []),
    ("atmosphere.beams", "Held", "ShowLightBeams", "Boolean", "off", "", "BeamIntensity", "Percentage", "[30, 65, 100]", "ArBeamCount", []),
    ("hold.bar", "Held", "HoldBar", "Boolean", "off", "", "HoldBarIntensity", "Percentage", "[25, 60, 100]", "ArLiveBarCount", []),
    ("hold.breath", "Held", "HoldBreath", "Boolean", "off", "", "HoldBreathRate", "Percentage", "[25, 75, 100]", "ArLiveBarCount", []),
    ("hold.vibration", "Held", "HoldVibration", "Boolean", "off", "", "HoldVibrationAmount", "Percentage", "[25, 60, 100]", "ArLiveBarCount", []),
    ("hold.color-cycle", "Held", "HoldColorCycle", "Boolean", "off", "", "HoldColorCycleSpeed", "Percentage", "[25, 60, 100]", "ArLiveBarCount", []),
    ("hold.arc", "Held", "HoldElectricArc", "Boolean", "off", "", "HoldArcIntensity", "Percentage", "[25, 60, 100]", "ArHoldArcCount", []),
    ("falling.pulse", "Approach", "FallingPulse", "Boolean", "off", "", "FallingPulseRate", "Percentage", "[25, 75, 100]", "ArShapeCount", []),
    ("falling.shimmer", "Approach", "NoteShimmer", "Boolean", "off", "", "NoteShimmerAmount", "Percentage", "[50, 70, 100]", "ArNoteShimmerQuadCount", []),
    ("particles.sparks", "Impact", "ShowEmbers", "Boolean", "off", "", "ParticleAmount", "Percentage", "[20, 70, 120]", "ArParticleFxCount", []),
    ("impact.flash", "Impact", "ShowImpactFlash", "Boolean", "off", "", "ImpactFlashIntensity", "Percentage", "[25, 60, 100]", "ArFlashCount", []),
    ("falling.trail", "Approach", "FallingTrail", "Choice", "None", "Glow", "FallingTrailLength", "Percentage", "[25, 60, 100]", "ArNoteTrailQuadCount", []),
    ("impact.burst", "Impact", "ShowEmbers", "Boolean", "off", "", "ImpactBurst", "Choice", '["Embers", "Fireworks", "Dust"]', "ArParticleFxCount", []),
    ("impact.wave", "Impact", "ImpactWave", "Choice", "None", "Ring", "ImpactWave", "Choice", '["Ring", "Shockwave", "Ripple"]', "ArPulseCount", ["ShowImpactRings"]),
    ("impact.morph", "Impact", "ImpactMorph", "Choice", "None", "Shatter", "ImpactMorph", "Choice", '["Shatter", "Melt", "Bounce"]', "ArParticleFxCount", []),
    ("release.fx", "Release", "ReleaseEffect", "Choice", "Fade", "Float Up", "ReleaseEffect", "Choice", '["Float Up", "Dissolve", "Smoke"]', "ArReleaseEffectCount", []),
    # Round C: one family can own two columns, each driving a different declared setting.
    ("falling.trail", "Approach", "FallingTrail", "Choice", "None", "Glow", "FallingTrail", "Choice", '["Glow", "Blur", "Ribbon"]', "ArNoteTrailQuadCount", []),
    ("impact.flash", "Impact", "ShowImpactFlash", "Boolean", "off", "", "ImpactFlashStyle", "Choice", '["Flash", "Lightning", "Plasma"]', "ArFlashCount", []),
    # Round D: one column for every remaining declared percentage.
    ("particles.wisps", "Held", "ShowWisps", "Boolean", "off", "", "WispSpeed", "Percentage", "[100, 300, 600]", "ArParticleFxCount", []),
    ("particles.wisps", "Held", "ShowWisps", "Boolean", "off", "", "WispTurbulence", "Percentage", "[25, 60, 100]", "ArParticleFxCount", []),
    ("particles.wisps", "Held", "ShowWisps", "Boolean", "off", "", "WispGlow", "Percentage", "[50, 125, 200]", "ArParticleFxCount", []),
    ("particles.flames", "Held", "ShowFlame", "Boolean", "off", "", "FlameHeight", "Percentage", "[25, 60, 100]", "ArParticleFxCount", []),
    ("particles.sparks", "Impact", "ShowEmbers", "Boolean", "off", "", "ParticleSpeed", "Percentage", "[75, 175, 300]", "ArParticleFxCount", []),
    ("particles.sparks", "Impact", "ShowEmbers", "Boolean", "off", "", "ParticleSize", "Percentage", "[2, 8, 16]", "ArParticleFxCount", []),
    ("particles.sparks", "Impact", "ShowEmbers", "Boolean", "off", "", "ParticleGlow", "Percentage", "[50, 125, 200]", "ArParticleFxCount", []),
    ("particles.sparks", "Impact", "ShowEmbers", "Boolean", "off", "", "ParticleLife", "Percentage", "[0.5, 1.5, 3]", "ArParticleFxCount", []),
    ("falling.trail", "Approach", "FallingTrail", "Choice", "None", "Glow", "FallingTrailIntensity", "Percentage", "[25, 60, 100]", "ArNoteTrailQuadCount", []),
    ("impact.wave", "Impact", "ImpactWave", "Choice", "None", "Ring", "ImpactWaveIntensity", "Percentage", "[30, 90, 150]", "ArPulseCount", ["ShowImpactRings"]),
    ("impact.morph", "Impact", "ImpactMorph", "Choice", "None", "Shatter", "ImpactMorphIntensity", "Percentage", "[15, 65, 100]", "ArParticleFxCount", []),
    ("release.fx", "Release", "ReleaseEffect", "Choice", "Fade", "Float Up", "ReleaseIntensity", "Percentage", "[25, 60, 100]", "ArReleaseEffectCount", []),
    ("falling.trail", "Approach", "FallingGhost", "Boolean", "off", "", "FallingGhostAmount", "Percentage", "[30, 65, 100]", "ArNoteTrailQuadCount", []),
]


def fixture(families=FAMILIES, cells=128):
    rows = []
    for fam, stage, switch, switch_kind, off, on, value, value_kind, values, _, pins in families:
        numbers = values if value_kind == "Percentage" else "[]"
        choices = values if value_kind == "Choice" else "[]"
        pins_literal = "[" + ", ".join(f'"{p}"' for p in pins) + "]" if pins else "null"
        rows.append(
            f'        new("{fam}", Stage.{stage}, "{switch}", EffectParameterKind.{switch_kind}, "{off}",\n'
            f'            "{value}", EffectParameterKind.{value_kind}, {numbers}, {choices}, "", "{on}", {pins_literal}),')
    rows = "\n".join(rows)
    applied = set()
    for _, _, switch, _, _, _, value, _, _, _, pins in families:
        applied.add(switch)
        applied.add(value)
        applied.update(pins)
    cases = "\n".join(f'            case "{name}": break;' for name in sorted(applied))
    ids = [f[0] for f in families]

    review = f"""// a comment must never be read as a driven row:
// new("ghost.family", Stage.Held, "NoSwitch", EffectParameterKind.Boolean, "off", "NoValue", ...)
internal static class ArValuePilotReview
{{
    private const string Url = @"C://evidence//ar-value-pilot";
    private static readonly PilotFamily[] Driven =
    [
{rows}
    ];

    private static PianoVisualSettings Settings(PilotFamily fam, int? valueIndex, bool isOff)
    {{
        foreach (var pin in fam.Pins ?? []) Apply(s, new DrivenSetting(pin, EffectParameterKind.Boolean, 1, ""));
        if (fam.SwitchKind == EffectParameterKind.Boolean)
            Apply(s, new DrivenSetting(fam.SwitchName, EffectParameterKind.Boolean, isOff ? 0 : 1, ""));
        else
            Apply(s, new DrivenSetting(fam.SwitchName, EffectParameterKind.Choice, 0,
                isOff ? fam.SwitchOffValue : fam.SwitchOnValue));
        if (fam.ValueKind == EffectParameterKind.Choice)
            Apply(s, new DrivenSetting(fam.ValueName, EffectParameterKind.Choice, 0, setting.Choice));
    }}

    private static void Apply(PianoVisualSettings settings, DrivenSetting setting)
    {{
        switch (setting.Name)
        {{
{cases}
        }}
    }}

    private static int Probe(GpuStageRenderer renderer, string family) => {v.DELEGATION};
    private static void Gate(int offVsMid) {{ if (offVsMid < 12) throw new InvalidDataException("off↔mid collapsed"); }}
    private static int ReleaseLiftFrame => 12;
    private static string Path(PilotFamily fam) => Slug($"{{fam.Id}}-{{fam.ValueName}}");
    private static object[] Coverage()
    {{
        var all = EffectContractCatalog.AllFamilies;
        var perFamily = Driven.Where(candidate => candidate.Id == fam.Id);
        return Driven.Select(fam => new {{ Pinned = all.Single(c => c.Id == fam.Id), Unshot = new string[0] }}).ToArray();
    }}
    private static void WriteManifest() {{ var manifest = new {{ coverage = Coverage(), gateFailures, fullyCovered = Coverage() }}; }}
    private static void VerifyStageValuePilot() {{ Assert(cells == 24, "stage"); }}
    private static int Gate(PilotFamily fam, string name, int changed) {{ return failures.Add($"{{fam.Id}}/{{fam.ValueName}} {{name}}"); }}
}}
"""
    witness_rows = "\n".join(
        f'        "{f[0]}" => renderer.{f[9]}' + (' + renderer.ArGhostNoteQuadCount' if f[0] == 'falling.trail' else '') + ','
        for f in dict((x[0], x) for x in families).values())
    witness = f"""internal static class ArEffectFamilyReview
{{
    internal static int Probe(GpuStageRenderer renderer, string family) => family switch
    {{
{witness_rows}
        _ => throw new InvalidDataException("no witness"),
    }};
}}
"""
    lifecycle = f"""internal static class ArLifecycleReview
{{
    private static int Probe(GpuStageRenderer renderer, string family) => {v.DELEGATION};
}}
"""
    suite = f"""        Assert(cells == {cells}, "The AR value pilot must shoot thirty-two columns over all seventeen families.");
        Assert(cells == 24, "stage");
        if (args.Contains("--verify-stage-value-pilot-only")) {{ VerifyStageValuePilot(); }}
        VerifyStageValuePilot(reviewOutput!);
        Results.Add($"PASS AR value pilot (N17V): {{cells}} setting frames ...");
"""
    workflow = """      - name: AR value pilot (N17V)
        run: |
          Start-Process -ArgumentList '--verify','--verify-ar-value-pilot-only'
          if ($_ -like 'PASS AR value pilot (N17V)*') { "::notice title=N17V AR value pilot::$_" }
      - name: Stage value pilot (N17V-S)
        run: |
          Start-Process -ArgumentList '--verify','--verify-stage-value-pilot-only'
          if ($_ -like 'PASS stage value pilot (N17V-S)*') { "::notice::$_" }
"""
    listed = "/".join(f"`{fam}`" for fam in ids)
    readme_vi = (f"| `--verify-ar-value-pilot-only` | pilot N17V — {listed}. "
                 f"**{cells} khung**, không đổi binding. |\n"
                 "| `--verify-stage-value-pilot-only` | pilot N17V-S trên Live Stage. |")
    readme_en = (f"| `--verify-ar-value-pilot-only` | N17V value pilot — {listed}. "
                 f"**{cells} frames**, no binding change. |\n"
                 "| `--verify-stage-value-pilot-only` | N17V-S stage value pilot. |")
    plan = f"> Trạng thái: pilot {len(families)} family / **{cells} khung**: " + ", ".join(ids) + "."
    return {"review": review, "witness": witness, "lifecycle": lifecycle, "suite": suite,
            "readme_vi": readme_vi, "readme_en": readme_en, "plan": plan, "workflow": workflow}


def errors_with(**changes):
    files = fixture()
    files.update(changes)
    return v.check(files)


class ValuePilotContractTests(unittest.TestCase):
    def test_the_fixture_is_clean(self):
        self.assertEqual([], v.check(fixture()))

    def test_comments_and_verbatim_strings_are_not_read_as_code(self):
        self.assertEqual(len(FAMILIES), len(v.DRIVEN.findall(
            v.strip_cs(fixture()["review"]).split("Driven =")[1])))

    def test_a_flat_particle_counter_is_refused(self):
        review = fixture()["review"].replace(v.DELEGATION, "renderer.ArParticleFxCount")
        problems = errors_with(review=review)
        self.assertTrue(any("particle counter" in p for p in problems), problems)
        self.assertTrue(any("no longer delegates" in p for p in problems), problems)

    def test_a_copied_witness_table_in_the_lifecycle_pilot_is_refused(self):
        lifecycle = """internal static class ArLifecycleReview
{
    private static int Probe(GpuStageRenderer renderer, string family) => family switch
    {
        "hold.bar" => renderer.ArLiveBarCount,
        _ => throw new InvalidDataException("no witness"),
    };
}
"""
        problems = errors_with(lifecycle=lifecycle)
        self.assertTrue(any("own witness switch" in p for p in problems), problems)

    def test_a_private_witness_table_is_refused(self):
        problems = errors_with(witness=fixture()["witness"].replace(
            "internal static int Probe", "private static int Probe"))
        self.assertTrue(any("no longer exposes an internal Probe" in p for p in problems), problems)

    def test_a_family_without_a_witness_is_refused(self):
        extra = ("hold.shadow", "Held", "HoldShadow", "Boolean", "off", "", "HoldShadowIntensity",
                 "Percentage", "[25, 60, 100]", "ArShadowCount", [])
        files = fixture(families=FAMILIES + [extra], cells=132)
        files["witness"] = files["witness"].replace(
            '        "hold.shadow" => renderer.ArShadowCount,\n', "")
        problems = v.check(files)
        self.assertTrue(any("no counter for it" in p and "hold.shadow" in p for p in problems), problems)

    def test_an_unhandled_setting_is_refused(self):
        review = fixture()["review"].replace('            case "NoteShimmer": break;\n', "")
        problems = errors_with(review=review)
        self.assertTrue(any("has no case for it" in p and "NoteShimmer" in p for p in problems), problems)

    def test_a_choice_switch_with_the_wrong_off_value_is_refused(self):
        review = fixture()["review"].replace(
            '"ImpactMorph", EffectParameterKind.Choice, "None"',
            '"ImpactMorph", EffectParameterKind.Choice, "Fade"')
        problems = errors_with(review=review)
        self.assertTrue(any("treats 'None' as off" in p for p in problems), problems)

    def test_an_undeclared_choice_switch_is_refused(self):
        review = fixture()["review"].replace(
            '"FallingTrail", EffectParameterKind.Choice',
            '"FallingTrailSomethingElse", EffectParameterKind.Choice')
        problems = errors_with(review=review)
        self.assertTrue(any("without declaring its renderer off value" in p for p in problems), problems)

    def test_a_lost_choice_on_value_is_refused(self):
        problems = errors_with(review=fixture()["review"].replace("SwitchOnValue", "SwitchOn"))
        self.assertTrue(any("lost the on value of a choice switch" in p for p in problems), problems)

    def test_a_pilot_that_ignores_the_choice_string_is_refused(self):
        problems = errors_with(review=fixture()["review"].replace("setting.Choice", "setting.Number.ToString()"))
        self.assertTrue(any("no longer applies a DrivenSetting's Choice" in p for p in problems), problems)

    def test_a_lost_release_scene_is_refused(self):
        problems = errors_with(review=fixture()["review"].replace("Stage.Release", "Stage.Held"))
        self.assertTrue(any("lost the Release scene" in p for p in problems), problems)

    def test_a_lost_impact_wave_pin_is_refused(self):
        problems = errors_with(review=fixture()["review"].replace("ShowImpactRings", "ShowSomethingElse"))
        self.assertTrue(any("no longer pins ShowImpactRings" in p for p in problems), problems)

    def test_a_stale_cell_count_is_refused(self):
        problems = errors_with(suite=fixture()["suite"].replace("cells == 128", "cells == 124"))
        self.assertTrue(any("pins the 128 N17V cells" in p for p in problems), problems)

    def test_a_missing_pass_line_is_refused(self):
        suite = fixture()["suite"].replace('PASS AR value pilot (N17V)', 'PASS value pilot')
        problems = errors_with(suite=suite)
        self.assertTrue(any("no longer prints the N17V PASS line" in p for p in problems), problems)

    def test_a_readme_still_quoting_onetwentyfour_frames_is_refused(self):
        problems = errors_with(readme_vi=fixture()["readme_vi"].replace("128 khung", "124 khung"))
        self.assertTrue(any("128 khung" in p for p in problems), problems)

    def test_an_english_readme_that_drops_a_family_is_refused(self):
        problems = errors_with(readme_en=fixture()["readme_en"].replace("`release.fx`", "`release`"))
        self.assertTrue(any("does not name 'release.fx'" in p for p in problems), problems)

    def test_a_plan_still_quoting_onetwentyfour_frames_is_refused(self):
        problems = errors_with(plan=fixture()["plan"].replace("128 khung", "124 khung"))
        self.assertTrue(any("does not say '128 khung'" in p for p in problems), problems)

    def test_a_missing_readme_row_is_refused(self):
        problems = errors_with(readme_vi="| another switch | nothing here |")
        self.assertTrue(any("lost the --verify-ar-value-pilot-only row" in p for p in problems), problems)

    def test_a_missing_fail_fast_step_is_refused(self):
        workflow = fixture()["workflow"].replace("--verify-ar-value-pilot-only", "--verify-ar-effect-only")
        problems = errors_with(workflow=workflow)
        self.assertTrue(any("fail-fast step" in p for p in problems), problems)

    def test_a_missing_n17v_notice_is_refused(self):
        workflow = fixture()["workflow"].replace(
            "-like 'PASS AR value pilot (N17V)*'", "-like 'PASS value pilot*'")
        problems = errors_with(workflow=workflow)
        self.assertTrue(any("notice annotation" in p for p in problems), problems)

    def test_a_missing_off_vs_mid_gate_is_refused(self):
        review = fixture()["review"].replace("off↔mid collapsed", "mid collapsed")
        problems = errors_with(review=review)
        self.assertTrue(any("no longer refuses a mid frame" in p for p in problems), problems)

    def test_a_second_column_for_one_family_is_allowed(self):
        # Both shapes are legal: one column per family (the round-B shape) and one column per
        # setting (the round-C shape, where a family with several settings owns several columns).
        seen, single = set(), []
        for f in FAMILIES:
            if f[0] not in seen:
                seen.add(f[0])
                single.append(f)
        self.assertEqual([], v.check(fixture(families=single, cells=68)))
        self.assertEqual([], v.check(fixture()))

    def test_a_repeated_family_and_setting_pair_is_refused(self):
        doubled = list(FAMILIES) + [FAMILIES[-1]]
        problems = v.check(fixture(families=doubled, cells=132))
        self.assertTrue(any("drives a (family, setting) twice" in p for p in problems), problems)

    def test_a_pilot_without_coverage_is_refused(self):
        review = fixture()["review"].replace("coverage = Coverage()", "coverage = new object[0]")
        problems = errors_with(review=review)
        self.assertTrue(any("coverage block" in p for p in problems), problems)

    def test_a_pilot_without_unshot_is_refused(self):
        problems = errors_with(review=fixture()["review"].replace("Unshot", "ShotNames"))
        self.assertTrue(any("unshot percentages" in p for p in problems), problems)

    def test_a_pilot_without_the_catalogue_is_refused(self):
        review = fixture()["review"].replace("EffectContractCatalog.AllFamilies", "Driven")
        problems = errors_with(review=review)
        self.assertTrue(any("effect contract catalogue" in p for p in problems), problems)

    def test_a_pilot_without_a_setting_scoped_capture_folder_is_refused(self):
        review = fixture()["review"].replace("-{fam.ValueName}", "")
        problems = errors_with(review=review)
        self.assertTrue(any("overwrite each other's pictures" in p for p in problems), problems)

    def test_a_run_without_the_gate_collector_is_refused(self):
        review = fixture()["review"].replace('return failures.Add(', 'return 0; //')
        problems = errors_with(review=review)
        self.assertTrue(any("gate collector" in p for p in problems), problems)

    def test_a_manifest_without_gate_failures_is_refused(self):
        review = fixture()["review"].replace("gateFailures", "notes")
        problems = errors_with(review=review)
        self.assertTrue(any("gateFailures" in p for p in problems), problems)

    def test_a_missing_stage_pilot_flag_is_refused(self):
        suite = fixture()["suite"].replace("--verify-stage-value-pilot-only", "--verify-something-else")
        problems = errors_with(suite=suite)
        self.assertTrue(any("isolated --verify-stage-value-pilot-only" in p for p in problems), problems)

    def test_a_stage_pilot_missing_from_full_verify_is_refused(self):
        suite = fixture()["suite"].replace("VerifyStageValuePilot(reviewOutput!);", "")
        problems = errors_with(suite=suite)
        self.assertTrue(any("full --verify chain" in p for p in problems), problems)

    def test_a_stale_stage_cell_count_is_refused(self):
        suite = fixture()["suite"].replace("Assert(cells == 24", "Assert(cells == 20")
        problems = errors_with(suite=suite)
        self.assertTrue(any("24 stage value pilot cells" in p for p in problems), problems)

    def test_a_missing_stage_fail_fast_step_is_refused(self):
        workflow = fixture()["workflow"].replace("Stage value pilot (N17V-S)", "Something else")
        problems = errors_with(workflow=workflow)
        self.assertTrue(any("stage value pilot fail-fast step" in p for p in problems), problems)

    def test_a_missing_stage_notice_is_refused(self):
        workflow = fixture()["workflow"].replace("-like 'PASS stage value pilot (N17V-S)*'", "-like 'PASS*'")
        problems = errors_with(workflow=workflow)
        self.assertTrue(any("stage pilot PASS line into a notice" in p for p in problems), problems)

    def test_a_manifest_without_fully_covered_is_refused(self):
        review = fixture()["review"].replace("fullyCovered = Coverage()", "ignored = Coverage()")
        problems = errors_with(review=review)
        self.assertTrue(any("fullyCovered" in p for p in problems), problems)

    def test_a_witness_without_ghost_quads_is_refused(self):
        witness = fixture()["witness"].replace(" + renderer.ArGhostNoteQuadCount", "")
        problems = errors_with(witness=witness)
        self.assertTrue(any("ghost quads" in p for p in problems), problems)

    def test_coverage_that_is_not_per_family_is_refused(self):
        review = fixture()["review"].replace(
            "Driven.Where(candidate => candidate.Id == fam.Id)", "new[] { fam }")
        problems = errors_with(review=review)
        self.assertTrue(any("coverage per family" in p for p in problems), problems)

    def test_an_empty_file_set_is_refused(self):
        problems = v.check({"review": ""})
        self.assertTrue(any("empty or missing" in p for p in problems), problems)


if __name__ == "__main__":
    unittest.main()
