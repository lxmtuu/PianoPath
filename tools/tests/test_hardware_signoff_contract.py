#!/usr/bin/env python3
"""Synthetic contract tests for tools/hardware_signoff_contract.py — no .NET, no Windows, no GPU.

The guard only reads text. These tests build a tiny repository that satisfies it, then break one promise
at a time: a tier switch that never reaches the AR input, a JSON that loses ``arTier``, a bumped schema
that orphans cached baselines, a README row without the WARP caveat, a runbook that claims it is done,
a validator that forgets to refuse WARP, a CI step without its notice, or a binding flipped to Applied
without a human signature. None of these files is the product; they are the minimum text the guard
expects, so a regression in the real repository turns one of these cases red first.
"""
import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import hardware_signoff_contract as h

BENCH = '''
internal static class FrameBenchmark
{
    internal const int ExitBadTier = 4;
    internal static int Run(string[] args)
    {
        var tierRequest = Value(args, "--bench-tier=");
        var match = ArQualityTiers.All.FirstOrDefault(name => name == tierRequest);
        if (match is null) return ExitBadTier;
        var withAr = args.Contains("--bench-ar") || !string.IsNullOrWhiteSpace(tierRequest);
        foreach (var budget in budgets) scenes.Add(RunScene(loop, feed, budget, notes, frames, software, tier));
        var run = new FrameBenchRun { ArTier = ArQualityTiers.NameOf(tier) };
        feed.Ar = new GpuArInput(calibration, noteStyle, 1f, Quality: tier);
        settings.ArQuality = ArQualityTiers.NameOf(tier);
        return RunScene(loop, feed, budget, notes, frames, software, ArQualityTier.Standard);
    }
}
'''

BUDGET = '''
internal sealed class FrameBenchRun
{
    internal const int Schema = 1;
    internal string ArTier { get; init; } = "Standard";
    internal string ToJson()
    {
        json.WriteString("arTier", ArTier);
        json.WriteString("arTier", scene.ArTier);
        if (previous.ArTier != ArTier) return unknown;
        lines.Add($"NOTE perf: ... AR tier {ArTier} ...");
        ArTier = Text(root, "arTier") is { Length: > 0 } runTier ? runTier : "Standard";
        ArTier = Text(element, "arTier") is { Length: > 0 } sceneTier ? sceneTier : "Standard";
    }
}
'''

README_VI = '''
### Tham số dòng lệnh

| Tham số | Tác dụng |
| --- | --- |
| `--bench-tier=<Lite\\|Standard\\|Ultra> [--bench ...]` | Ghim tier, ghi `arTier`. WARP không phải GPU thật. Xem [sổ duyệt phần cứng](docs/AR-HARDWARE-SIGNOFF.md). |

### Kiểm thử
'''

README_EN = '''
### Command line switches

| Switch | Effect |
| --- | --- |
| `--bench-tier=<Lite\\|Standard\\|Ultra> [--bench ...]` | Pin the tier, writes `arTier`. WARP is not physical-GPU evidence. See [hardware sign-off runbook](docs/AR-HARDWARE-SIGNOFF.md). |

### Testing
'''

RUNBOOK = '''
# Sổ duyệt phần cứng AR (N18-HW)

> **Trạng thái: CHƯA DUYỆT.** Run [40364410157](https://example.invalid) vẫn là WARP.

`--bench-tier=Lite` và `--bench-tier=Ultra`, `--bench-adapter`, `warp: false`,
ngân sách 8/16/16 ms, `webcam.json`, OBS Virtual Camera, `Unverified`, `owner_approved`.
Chạy `tools/ar_hardware_signoff.py --evidence <dir>` rồi `--check`.
Hai kiểm tra không có ảnh ghi vào `camera.obs_toggle_notes` và `camera.same_name_pair_notes`.

| Kịch bản | Yêu cầu |
| --- | --- |
| `overhead` | nhìn từ trên |
| `fisheye` | ống kính rộng |
| `low_light` | thiếu sáng |
| `fast_motion` | tay chạy nhanh |
| `hand_occlusion` | tay che phím |
'''

SCRIPT = '''
"""doc"""
import json
TIERS = ("Lite", "Ultra")
BUDGETS = {"default": 8.0, "heavy": 16.0, "ar": 16.0}
SCENARIOS = ("overhead", "fisheye", "low_light", "fast_motion", "hand_occlusion")
def validate(root):
    if report.get("warp") is not False: pass
    if report.get("softwareAdapter") is not False: pass
    if report.get("arTier") != tier: pass
    errors.append("the two tiers were measured on different adapters")
    errors.append("the two tiers share one takenUtc; they must be two separate runs")
    if not isinstance(camera.get("obs_toggle_notes"), str): pass
    if not isinstance(camera.get("same_name_pair_notes"), str): pass
    print("PENDING owner approval")
def main(argv=None):
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--evidence", type=Path)
'''

WORKFLOW = '''
      - name: AR hardware sign-off kit (N18-HW)
        shell: pwsh
        run: |
          python3 tools/ar_hardware_signoff.py --check 2>&1 | Tee-Object -FilePath $log
          python3 tools/hardware_signoff_contract.py 2>&1 | Tee-Object -FilePath $log -Append
          $pass = 'PASS AR hardware sign-off kit (N18-HW)*'
          Get-Content $log | ForEach-Object {
            if ($_ -like 'FAIL*') { "::error title=N18-HW::$_" }
            elseif ($_ -like $pass) { "::notice title=N18-HW::$_" }
          }

      - name: Prism Flow baseline input checks
'''

BINDINGS = '''
    private static readonly EffectParameterBindingProfile ArProjectedParameterBindings = new(
        EffectParameterBindingState.Applied, EffectParameterBindingState.Applied,
        EffectParameterBindingState.Unverified, EffectParameterBindingState.Unverified);
'''

PLAN = "## N18-HW\n\n> `--bench-tier`, `docs/AR-HARDWARE-SIGNOFF.md`, ma trận 128 khung vẫn là WARP.\n"


def fixture():
    return {
        "bench": BENCH,
        "budget": BUDGET,
        "app": 'if (e.Args.Any(a => a.StartsWith("--bench-tier=", StringComparison.Ordinal))) { }',
        "portable_tests": ("The_tier_travels_through_the_json A_lite_baseline_is_not_compared_with_an_ultra_run "
                           "The_machine_line_names_the_tier"),
        "readme_vi": README_VI,
        "readme_en": README_EN,
        "plan": PLAN,
        "runbook": RUNBOOK,
        "script": SCRIPT,
        "workflow": WORKFLOW,
        "bindings": BINDINGS,
        "check_sources": "import hardware_signoff_contract\nerrors.extend(scan_ar_hardware_signoff_contract())",
        "self": "guard for check_sources.py",
    }


class HardwareSignoffContractTests(unittest.TestCase):
    def test_a_complete_fixture_passes(self):
        self.assertEqual([], h.check(fixture()))

    def test_every_file_is_required(self):
        for name in h.FILES:
            files = fixture()
            files[name] = ""
            self.assertTrue(h.check(files), f"removing {name} must be reported")

    def test_the_tier_must_reach_the_ar_input_and_the_report(self):
        for token in ("Quality: tier", "settings.ArQuality = ArQualityTiers.NameOf(tier)",
                      "ArTier = ArQualityTiers.NameOf(tier)", "frames, software, tier)"):
            files = fixture()
            files["bench"] = BENCH.replace(token, "")
            self.assertTrue(any("FrameBenchmark.cs" in error for error in h.check(files)), token)

    def test_an_unvalidated_switch_is_rejected(self):
        for token in ("ArQualityTiers.All", "ExitBadTier", '"--bench-tier="'):
            files = fixture()
            files["bench"] = BENCH.replace(token, "")
            self.assertTrue(h.check(files), token)
        files = fixture()
        files["app"] = files["app"].replace('"--bench-tier="', '"--other="')
        self.assertTrue(any("App.xaml.cs" in error for error in h.check(files)))

    def test_the_report_must_carry_the_tier_at_both_levels(self):
        files = fixture()
        files["budget"] = BUDGET.replace('json.WriteString("arTier", scene.ArTier);', "")
        self.assertTrue(any("both the run and the scene level" in error for error in h.check(files)))
        for token in ('Text(root, "arTier")', 'Text(element, "arTier")', "previous.ArTier", "AR tier {ArTier}"):
            files = fixture()
            files["budget"] = BUDGET.replace(token, "")
            self.assertTrue(h.check(files), token)

    def test_a_bumped_schema_orphans_cached_baselines(self):
        files = fixture()
        files["budget"] = BUDGET.replace("Schema = 1;", "Schema = 2;")
        self.assertTrue(any("schema" in error for error in h.check(files)))

    def test_portable_tests_are_part_of_the_contract(self):
        files = fixture()
        files["portable_tests"] = files["portable_tests"].replace("The_tier_travels_through_the_json", "")
        self.assertTrue(any("FrameBudgetTests.cs" in error for error in h.check(files)))

    def test_both_readmes_need_the_warp_caveat_and_the_runbook_link(self):
        for name in ("readme_vi", "readme_en"):
            for token in ("Lite", "Ultra", "arTier", "AR-HARDWARE-SIGNOFF.md", "WARP"):
                files = fixture()
                files[name] = files[name].replace(token, "", 1)
                self.assertTrue(h.check(files), f"{name} without {token}")
            files = fixture()
            files[name] = files[name].replace("`--bench-tier=", "`--bench-old=")
            self.assertTrue(any("lost the --bench-tier row" in error for error in h.check(files)))

    def test_the_runbook_must_stay_unapproved_and_keep_all_scenarios(self):
        files = fixture()
        files["runbook"] = RUNBOOK.replace("CHƯA DUYỆT", "DUYỆT XONG")
        self.assertTrue(any("CHƯA DUYỆT" in error for error in h.check(files)))
        for token in ("--bench-tier=Lite", "--bench-tier=Ultra", "--bench-adapter", "warp: false",
                      "8/16/16", "40364410157", "--check", "obs_toggle_notes", "same_name_pair_notes"):
            files = fixture()
            files["runbook"] = RUNBOOK.replace(token, "")
            self.assertTrue(h.check(files), token)
        for scenario in h.SCENARIOS:
            files = fixture()
            files["runbook"] = RUNBOOK.replace(f"`{scenario}`", "")
            self.assertTrue(any(scenario in error for error in h.check(files)), scenario)

    def test_the_validator_must_refuse_incomplete_evidence(self):
        for token in ('"warp"', "softwareAdapter", "arTier", "different adapters", "two separate runs",
                      "BUDGETS", "PENDING owner approval", "--check", "--evidence",
                      "obs_toggle_notes", "same_name_pair_notes"):
            files = fixture()
            files["script"] = SCRIPT.replace(token, "")
            self.assertTrue(h.check(files), token)
        for scenario in h.SCENARIOS:
            files = fixture()
            files["script"] = SCRIPT.replace(f'"{scenario}"', "")
            self.assertTrue(any(scenario in error for error in h.check(files)), scenario)
        files = fixture()
        files["script"] = SCRIPT + '\nPath("Stage/Effects/EffectContracts.cs").write_text("Applied")\n'
        self.assertTrue(any("must never open EffectContracts.cs" in error for error in h.check(files)))

    def test_ci_needs_its_own_fail_fast_step_and_notice(self):
        files = fixture()
        files["workflow"] = WORKFLOW.replace("AR hardware sign-off kit (N18-HW)", "Some other step")
        self.assertTrue(any("fail-fast step" in error for error in h.check(files)))
        for token in ("ar_hardware_signoff.py --check", "hardware_signoff_contract.py",
                      "$pass = 'PASS AR hardware sign-off kit (N18-HW)*'", "-like $pass"):
            files = fixture()
            files["workflow"] = WORKFLOW.replace(token, "")
            self.assertTrue(h.check(files), token)

    def test_the_plan_and_binding_are_part_of_the_contract(self):
        for token in ("N18-HW", "--bench-tier", "AR-HARDWARE-SIGNOFF.md", "128 khung"):
            files = fixture()
            files["plan"] = PLAN.replace(token, "")
            self.assertTrue(h.check(files), token)
        files = fixture()
        files["bindings"] = BINDINGS.replace("EffectParameterBindingState.Unverified, EffectParameterBindingState.Unverified",
                                            "EffectParameterBindingState.Applied, EffectParameterBindingState.Applied")
        self.assertTrue(any("must stay Unverified" in error for error in h.check(files)))

    def test_the_guard_must_be_wired_into_check_sources(self):
        files = fixture()
        files["check_sources"] = files["check_sources"].replace("scan_ar_hardware_signoff_contract()", "")
        self.assertTrue(any("no longer wires" in error for error in h.check(files)))
        files = fixture()
        files["self"] = files["self"].replace("check_sources.py", "elsewhere.py")
        self.assertTrue(any("wiring check has a target" in error for error in h.check(files)))


if __name__ == "__main__":
    unittest.main()
