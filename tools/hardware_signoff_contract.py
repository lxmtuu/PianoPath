#!/usr/bin/env python3
"""Keep the AR hardware sign-off kit honest: code, reports, docs, script and CI say the same thing.

The value matrix is green on WARP, but WARP is a software rasterizer and the bench camera is synthetic.
``--bench-tier`` exists so a maintainer can produce *two comparable* reports on a real GPU; without a
guard, a future edit could drop the tier from the JSON, let a WARP report through, lose the webcam
scenarios, or flip ``ArProjectedParameterBindings`` from ``Unverified`` to ``Applied`` while the
evidence is still missing. This module reads text only — no .NET, no Windows, no GPU — and is wired
into ``tools/check_sources.py`` so the fast lane fails in seconds instead of after a Windows build.

Pure text in, list of problems out. ``tools/tests/test_hardware_signoff_contract.py`` feeds it synthetic
files that break one promise at a time.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

FILES = {
    "bench": ROOT / "Diagnostics" / "FrameBenchmark.cs",
    "budget": ROOT / "Diagnostics" / "FrameBudget.cs",
    "app": ROOT / "App.xaml.cs",
    "portable_tests": ROOT / "tests" / "PianoPath.Tests" / "FrameBudgetTests.cs",
    "readme_vi": ROOT / "README.md",
    "readme_en": ROOT / "README.en.md",
    "plan": ROOT / "docs" / "AR-VALUE-MATRIX-PLAN.md",
    "runbook": ROOT / "docs" / "AR-HARDWARE-SIGNOFF.md",
    "script": ROOT / "tools" / "ar_hardware_signoff.py",
    "workflow": ROOT / ".github" / "workflows" / "build.yml",
    "bindings": ROOT / "Stage" / "Effects" / "EffectContracts.cs",
    "check_sources": ROOT / "tools" / "check_sources.py",
    "self": ROOT / "tools" / "hardware_signoff_contract.py",
}

TIERS = ("Lite", "Ultra")
SCENARIOS = ("overhead", "fisheye", "low_light", "fast_motion", "hand_occlusion")


def check(files: dict) -> list:
    """Return every way the switch, its evidence contract and the documents disagree."""
    missing = [name for name in FILES if not files.get(name)]
    if missing:
        return [f"the hardware sign-off contract needs {', '.join(sorted(missing))} but it is empty or missing"]
    errors = []
    bench = files["bench"]
    budget = files["budget"]
    app = files["app"]
    tests = files["portable_tests"]
    script = files["script"]
    workflow = files["workflow"]
    runbook = files["runbook"]
    plan = files["plan"]
    bindings = files["bindings"]

    # 1. The switch is a real, validated input, not a string that is parsed and ignored.
    if '"--bench-tier="' not in bench:
        errors.append("FrameBenchmark.cs no longer parses --bench-tier=; the two-tier report has no reproducible switch")
    if "ArQualityTiers.All" not in bench or "ExitBadTier" not in bench:
        errors.append("FrameBenchmark.cs must validate --bench-tier against ArQualityTiers.All and fail with ExitBadTier")
    for token, why in (
        ("settings.ArQuality = ArQualityTiers.NameOf(tier)", "the scene's settings would still be Standard"),
        ("Quality: tier", "the AR overlay would still be drawn at Standard"),
        ("ArTier = ArQualityTiers.NameOf(tier)", "the report would not say which tier it measured"),
        ("frames, software, tier)", "the tier would not reach the measured scene"),
        ("ArQualityTier.Standard)", "the verification suite's private loop would have no tier"),
        ('args.Contains("--bench-ar") || !string.IsNullOrWhiteSpace(tierRequest)',
         "a tier request without the AR scene would describe nothing"),
    ):
        if token not in bench:
            errors.append(f"FrameBenchmark.cs lost {token!r}: {why}")
    if '"--bench-tier="' not in app:
        errors.append("App.xaml.cs no longer dispatches --bench-tier= to FrameBenchmark; the flag would open the window instead")

    # 2. The tier travels through the JSON at both levels, survives a legacy report, and blocks a
    #    cross-tier relative comparison. Schema stays 1 so CI's cached baseline still reads.
    if not re.search(r"internal const int Schema = 1;", budget):
        errors.append("FrameBudget.cs bumped the report schema; an additive arTier field must not invalidate cached baselines")
    if budget.count('json.WriteString("arTier"') != 2:
        errors.append("FrameBudget.cs must write arTier at both the run and the scene level")
    if 'Text(root, "arTier")' not in budget or 'Text(element, "arTier")' not in budget:
        errors.append("FrameBudget.cs must read arTier back at both levels; a round-trip that loses the tier is not evidence")
    if 'previous.ArTier' not in budget:
        errors.append("FrameBudget.cs no longer refuses to compare a baseline measured at a different tier")
    if "AR tier {ArTier}" not in budget:
        errors.append("FrameBudget.cs no longer names the tier in the machine line of the log")

    # 3. The portable tests pin the round-trip and the comparison guard.
    for name in ("The_tier_travels_through_the_json", "A_lite_baseline_is_not_compared_with_an_ultra_run",
                 "The_machine_line_names_the_tier"):
        if name not in tests:
            errors.append(f"FrameBudgetTests.cs lost {name}; the tier contract is only a comment without it")

    # 4. Both READMEs stand on their own and point at the runbook.
    for name, heading in (("readme_vi", "Tham số dòng lệnh"), ("readme_en", "Command line switches")):
        text = files[name]
        table = re.search(rf"^### {re.escape(heading)}$(.*?)^### ", text, re.M | re.S)
        row = next((line for line in (table.group(1).splitlines() if table else []) if "--bench-tier=" in line), "")
        if not row:
            errors.append(f"{name} lost the --bench-tier row in its CLI table")
            continue
        for token in ("Lite", "Ultra", "arTier", "AR-HARDWARE-SIGNOFF.md"):
            if token not in row:
                errors.append(f"{name}'s --bench-tier row does not mention {token!r}")
        if "WARP" not in row:
            errors.append(f"{name}'s --bench-tier row must say WARP is not physical-GPU evidence")

    # 5. The runbook is a procedure, not a claim of victory.
    if "CHƯA DUYỆT" not in runbook:
        errors.append("docs/AR-HARDWARE-SIGNOFF.md lost its explicit CHƯA DUYỆT status")
    for token in ("--bench-tier=Lite", "--bench-tier=Ultra", "--bench-adapter", "warp: false",
                  "8/16/16", "webcam.json", "OBS Virtual Camera", "Unverified", "owner_approved",
                  "ar_hardware_signoff.py --evidence", "--check"):
        if token not in runbook:
            errors.append(f"docs/AR-HARDWARE-SIGNOFF.md does not name {token!r}; the procedure is incomplete")
    for scenario in SCENARIOS:
        if f"`{scenario}`" not in runbook:
            errors.append(f"docs/AR-HARDWARE-SIGNOFF.md lost the '{scenario}' webcam scenario")
    if "CHƯA DUYỆT" in runbook and "40364410157" not in runbook:
        errors.append("docs/AR-HARDWARE-SIGNOFF.md must cite the WARP run it is still waiting on hardware for")

    # 6. The validator checks the whole evidence folder and cannot be used to declare art approval.
    for token in ("--check", "--evidence", '"warp"', "softwareAdapter", "arTier", "different adapters",
                  "two separate runs", "BUDGETS", "PENDING owner approval"):
        if token not in script:
            errors.append(f"tools/ar_hardware_signoff.py lost {token!r}; it would accept incomplete evidence")
    for scenario in SCENARIOS:
        if f'"{scenario}"' not in script:
            errors.append(f"tools/ar_hardware_signoff.py lost the '{scenario}' scenario")
    if "EffectContracts.cs" in script.split('"""')[-1]:
        errors.append("tools/ar_hardware_signoff.py must never open EffectContracts.cs; flipping a binding is a human PR")

    # 7. CI has its own fail-fast step for the kit, on the Linux fast lane, with a readable notice.
    step = re.search(r"^      - name: AR hardware sign-off kit \(N18-HW\)\n(.*?)(?=^      - name: |\Z)",
                     workflow, re.M | re.S)
    if not step:
        errors.append("build.yml lost the AR hardware sign-off kit (N18-HW) fail-fast step")
    else:
        body = step.group(1)
        if "ar_hardware_signoff.py --check" not in body:
            errors.append("build.yml's N18-HW step must run the validator's synthetic --check")
        if "hardware_signoff_contract.py" not in body:
            errors.append("build.yml's N18-HW step must run the contract itself")
        # The prefix lives in a variable: check_sources.py's N17 guard searches the whole workflow for
        # the first literal "-like 'PASS ...*'" and would otherwise mistake this step's notice for N17's.
        if "$pass = 'PASS AR hardware sign-off kit (N18-HW)*'" not in body or "-like $pass" not in body:
            errors.append("build.yml's N18-HW step must turn the kit's PASS line into a notice without stealing N17's literal prefix")

    # 8. The plan links the finished WARP matrix to the unfinished hardware gate.
    for token in ("N18-HW", "--bench-tier", "AR-HARDWARE-SIGNOFF.md", "128 khung"):
        if token not in plan:
            errors.append(f"docs/AR-VALUE-MATRIX-PLAN.md does not name {token!r}; plan and code drifted apart")

    # 9. The binding is the whole point: it stays Unverified until a human signs the evidence.
    binding = re.search(r"ArProjectedParameterBindings\s*=\s*new\((.*?)\);", bindings, re.S)
    if not binding:
        errors.append("Stage/Effects/EffectContracts.cs lost ArProjectedParameterBindings")
    else:
        states = re.findall(r"EffectParameterBindingState\.(\w+)", binding.group(1))
        if len(states) != 4 or states[2] != "Unverified" or states[3] != "Unverified":
            errors.append(f"ArProjectedParameterBindings is {states}; Recording and ArOverlay must stay Unverified "
                          "until a human signs the two-tier, real-GPU, real-webcam evidence")

    # 10. A guard nobody calls is a comment. check_sources.py must wire it, and this module must name
    #     the file that calls it, so removing the wire is visible from either side.
    if "scan_ar_hardware_signoff_contract()" not in files["check_sources"] or "import hardware_signoff_contract" not in files["check_sources"]:
        errors.append("tools/check_sources.py no longer wires hardware_signoff_contract into the fast lane")
    if "check_sources.py" not in files["self"]:
        errors.append("hardware_signoff_contract.py must name check_sources.py so the wiring check has a target")
    return errors


def collect(root: Path = ROOT) -> dict:
    """Read the real repository into the dictionary ``check`` expects."""
    return {
        name: (root / path.relative_to(ROOT)).read_text(encoding="utf-8")
        if (root / path.relative_to(ROOT)).exists() else ""
        for name, path in FILES.items()
    }


def main() -> int:
    errors = check(collect())
    if errors:
        print(f"{len(errors)} AR hardware sign-off contract problem(s):")
        for error in errors:
            print("  " + error)
        return 1
    print("PASS AR hardware sign-off contract (N18-HW): the tier reaches the settings, the AR input and the JSON, "
          "legacy baselines still read, the validator refuses WARP, the runbook and plan stay unapproved, "
          "the binding stays Unverified, and CI checks the kit in the fast lane")
    return 0


if __name__ == "__main__":
    sys.exit(main())
