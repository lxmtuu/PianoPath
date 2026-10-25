"""Synthetic contract tests for tools/value_pilot_contract.py — no .NET, no Windows, no GPU.

The guard only reads text. These tests build a tiny repository that satisfies it, then break one
promise at a time: a hard-coded particle counter, a copied witness table, an unknown family,
an unhandled setting, a stale cell count, a document that still quotes the old frame count and a
workflow that drops the pilot's own fail-fast step. The CI annotations that motivated this guard
are described in docs/AR-VALUE-MATRIX-PLAN.md.
"""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import value_pilot_contract as v

FAMILIES = [
    ("particles.wisps", "ShowWisps", "WispAmount", "[15, 75, 150]", "ArParticleFxCount"),
    ("particles.flames", "ShowFlame", "FlameIntensity", "[20, 60, 100]", "ArParticleFxCount"),
    ("atmosphere.beams", "ShowLightBeams", "BeamIntensity", "[30, 65, 100]", "ArBeamCount"),
    ("particles.sparks", "ShowEmbers", "ParticleAmount", "[20, 70, 120]", "ArParticleFxCount"),
    ("falling.shimmer", "NoteShimmer", "NoteShimmerAmount", "[50, 70, 100]", "ArNoteShimmerQuadCount"),
]


def fixture(families=FAMILIES, cells=20):
    rows = "\n".join(
        f'        new("{fam}", Stage.Held, "{switch}", EffectParameterKind.Boolean, "off",\n'
        f'            "{value}", EffectParameterKind.Percentage, {nums}, [], ""),'
        for fam, switch, value, nums, _ in families)
    cases = "\n".join(
        f'            case "{name}": break;'
        for _, switch, value, _, _ in families for name in (switch, value))
    ids = [fam for fam, *_ in families]

    review = f"""// a comment must never be read as a driven row:
// new("ghost.family", Stage.Held, "NoSwitch", EffectParameterKind.Boolean, "off", "NoValue", ...)
internal static class ArValuePilotReview
{{
    private const string Url = @"C://evidence//ar-value-pilot";
    private static readonly PilotFamily[] Driven =
    [
{rows}
    ];

    private static void Apply(PianoVisualSettings settings, DrivenSetting setting)
    {{
        switch (setting.Name)
        {{
{cases}
        }}
    }}

    private static int Probe(GpuStageRenderer renderer, string family) => {v.DELEGATION};
}}
"""
    witness_rows = "\n".join(f'        "{fam}" => renderer.{counter},'
                             for fam, _, _, _, counter in families)
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
    suite = f"""        Assert(cells == {cells}, "The AR value pilot must shoot five families at four rows each.");
        Results.Add($"PASS AR value pilot (N17V): {{cells}} setting frames ...");
"""
    workflow = """      - name: AR value pilot (N17V)
        run: |
          Start-Process -ArgumentList '--verify','--verify-ar-value-pilot-only'
          if ($_ -like 'PASS AR value pilot (N17V)*') { "::notice title=N17V AR value pilot::$_" }
"""
    listed = "/".join(f"`{fam}`" for fam in ids)
    readme_vi = (f"| `--verify-ar-value-pilot-only` | pilot N17V — {listed}. "
                 f"**{cells} khung**, không đổi binding. |")
    readme_en = (f"| `--verify-ar-value-pilot-only` | N17V value pilot — {listed}. "
                 f"**{cells} frames**, no binding change. |")
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
        self.assertEqual(5, len(v.DRIVEN.findall(
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
        families = FAMILIES + [("hold.bar", "HoldBar", "HoldBarIntensity", "[60, 80, 100]", "ArLiveBarCount")]
        files = fixture(families=families, cells=24)
        # Give hold.bar a witness that is deliberately absent from the shared table.
        files["witness"] = files["witness"].replace(
            '        "hold.bar" => renderer.ArLiveBarCount,\n', "")
        problems = v.check(files)
        self.assertTrue(any("no counter for it" in p and "hold.bar" in p for p in problems), problems)

    def test_an_unhandled_setting_is_refused(self):
        review = fixture()["review"].replace('            case "NoteShimmer": break;\n', "")
        problems = errors_with(review=review)
        self.assertTrue(any("has no case for it" in p and "NoteShimmer" in p for p in problems), problems)

    def test_a_stale_cell_count_is_refused(self):
        problems = errors_with(suite=fixture()["suite"].replace("cells == 20", "cells == 16"))
        self.assertTrue(any("pins the 20 N17V cells" in p for p in problems), problems)

    def test_a_missing_pass_line_is_refused(self):
        suite = fixture()["suite"].replace('PASS AR value pilot (N17V)', 'PASS value pilot')
        problems = errors_with(suite=suite)
        self.assertTrue(any("no longer prints the N17V PASS line" in p for p in problems), problems)

    def test_a_readme_still_quoting_sixteen_frames_is_refused(self):
        problems = errors_with(readme_vi=fixture()["readme_vi"].replace("20 khung", "16 khung"))
        self.assertTrue(any("20 khung" in p for p in problems), problems)

    def test_an_english_readme_that_drops_a_family_is_refused(self):
        problems = errors_with(readme_en=fixture()["readme_en"].replace("`falling.shimmer`", "`shimmer`"))
        self.assertTrue(any("does not name 'falling.shimmer'" in p for p in problems), problems)

    def test_a_plan_still_quoting_sixteen_frames_is_refused(self):
        problems = errors_with(plan=fixture()["plan"].replace("20 khung", "16 khung"))
        self.assertTrue(any("does not say '20 khung'" in p for p in problems), problems)

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

    def test_an_empty_file_set_is_refused(self):
        problems = v.check({"review": ""})
        self.assertTrue(any("empty or missing" in p for p in problems), problems)


if __name__ == "__main__":
    unittest.main()
