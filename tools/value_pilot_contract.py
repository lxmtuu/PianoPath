#!/usr/bin/env python3
"""Keep the N17V value pilot honest about what it shoots and what witnesses it.

Twelve attempts to widen the pilot failed with ``<family> low drew no quads`` because
``ArValuePilotReview.Probe`` returned the *particle* counter for every family. N17 already owns a
witness table that says which of the AR layer's counters belongs to which family; the pilots now
delegate to it. This check is the guard that stops a local copy, a hard-coded counter, a stale
``Assert(cells == ...)``, an unhandled setting, a README/plan that still quotes the old frame
count, a workflow that drops the pilot's own fail-fast step, a lost ``off↔mid`` gate (which
would let a neutral slider value be captioned as a third picture), a choice switch with the wrong
off value, a release scene that no longer lifts the key, a repeated (family, setting) column, or
a coverage block that stops naming the settings each column did not shoot, from coming back.

Pure text in, list of problems out: no .NET, no Windows, no GPU. ``check_sources.py`` calls it on
the real repository; ``tools/tests/test_value_pilot_contract.py`` calls it on synthetic files.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

FILES = {
    "review": ROOT / "Diagnostics" / "ArValuePilotReview.cs",
    "witness": ROOT / "Diagnostics" / "ArEffectFamilyReview.cs",
    "lifecycle": ROOT / "Diagnostics" / "ArLifecycleReview.cs",
    "suite": ROOT / "Diagnostics" / "VerificationSuite.cs",
    "readme_vi": ROOT / "README.md",
    "readme_en": ROOT / "README.en.md",
    "plan": ROOT / "docs" / "AR-VALUE-MATRIX-PLAN.md",
    "workflow": ROOT / ".github" / "workflows" / "build.yml",
}

DELEGATION = "ArEffectFamilyReview.Probe(renderer, family)"
DRIVEN = re.compile(
    r'new\(\s*"([a-z][a-z.\-]+)"\s*,\s*Stage\.\w+\s*,\s*"([^"]+)"\s*,'
    r'\s*EffectParameterKind\.\w+\s*,\s*"[^"]*"\s*,\s*"([^"]+)"\s*,', re.S)
WITNESS = re.compile(r'"([a-z][a-z\-]*\.[a-z\-]+)"\s*=>\s*renderer\.')
CASES = re.compile(r'case\s+"([^"]+)"\s*:')
CHOICE_SWITCH = re.compile(
    r'"([a-z][a-z.\-]+)"\s*,\s*Stage\.\w+\s*,\s*"([^"]+)"\s*,'
    r'\s*EffectParameterKind\.Choice\s*,\s*"([^"]+)"\s*,', re.S)
# The value the renderer itself treats as "this family is not here". A choice switch that picks a
# different string would draw the family in its own off row.
CHOICE_OFF_VALUES = {
    "FallingTrail": "None", "ImpactWave": "None", "ImpactMorph": "None", "ReleaseEffect": "Fade",
}


def strip_cs(text: str) -> str:
    """Blank C# comments, keep string literals (and their ``//``) exactly where they were."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and text[i + 1:i + 2] == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and text[i + 1:i + 2] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("\n" * text[i:j].count("\n"))
            i = j
            continue
        if c == "@" and text[i + 1:i + 2] == '"':
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if text[j + 1:j + 2] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            out.append(text[i:j])
            i = j
            continue
        if c == '"' or c == "'":
            quote, j = c, i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == quote:
                    j += 1
                    break
                j += 1
            out.append(text[i:j])
            i = j
            continue
        out.append(c)
        i += 1
    return "".join(out)


def _readme_row(text: str, key: str) -> str:
    for line in text.splitlines():
        if key in line:
            return line
    return ""


def check(files: dict) -> list:
    """Return every way the pilot, its witness, the suite and the documents disagree."""
    errors = []
    missing = [name for name in FILES if not files.get(name)]
    if missing:
        return [f"the N17V contract needs {', '.join(sorted(missing))} but it is empty or missing"]

    review = strip_cs(files["review"])
    witness = strip_cs(files["witness"])
    lifecycle = strip_cs(files["lifecycle"])
    suite = strip_cs(files["suite"])

    block = re.search(r"Driven\s*=\s*\[(.*?)\];", review, re.S)
    driven = DRIVEN.findall(block.group(1)) if block else []
    if not driven:
        return errors + ["Diagnostics/ArValuePilotReview.cs no longer declares a Driven family table; "
                         "the value pilot would shoot nothing while the suite still asserts a cell count"]
    ids = [row[0] for row in driven]
    # Round C: one column drives ONE setting, so a family may legitimately own several columns.
    # What may not repeat is the (family, setting) pair: that would overwrite the first column's
    # pictures and let one setting be captioned twice as two independent pieces of evidence.
    columns = [(row[0], row[2]) for row in driven]
    if len(set(columns)) != len(columns):
        errors.append(f"Diagnostics/ArValuePilotReview.cs drives a (family, setting) twice: {columns}; "
                      "the second column would overwrite the first one's pictures")

    if "internal static int Probe" not in witness:
        errors.append("Diagnostics/ArEffectFamilyReview.cs no longer exposes an internal Probe; "
                      "the lifecycle and value pilots cannot delegate to its witness table")
    if "_ => throw" not in witness:
        errors.append("Diagnostics/ArEffectFamilyReview.cs no longer refuses an unknown family; "
                      "a family without a witness would silently read zero quads")

    witnesses = set(WITNESS.findall(witness))
    for family in sorted(set(ids) - witnesses):
        errors.append(f"the value pilot drives '{family}' but N17's witness table has no counter for it; "
                      "teach Probe() about it before the page claims to have shot it")

    for name, text in (("ArValuePilotReview.cs", review), ("ArLifecycleReview.cs", lifecycle)):
        if "private static int Probe" not in text:
            errors.append(f"Diagnostics/{name} lost its Probe method; the pilot would not check that "
                          "anything was drawn")
        if DELEGATION not in text:
            errors.append(f"Diagnostics/{name} no longer delegates to {DELEGATION}; a local witness "
                          "copy is how a family gets measured with the wrong counter")
    if "=> renderer.ArParticleFxCount;" in review:
        errors.append("Diagnostics/ArValuePilotReview.cs still returns the particle counter for every "
                      "family; a non-particle family would fail 'low drew no quads' without drawing less")
    if "off↔mid" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs no longer refuses a mid frame identical to off; "
                      "a neutral slider value would be captioned as a third, independent picture")
    if '"hold.bar" => renderer.' in lifecycle:
        errors.append("Diagnostics/ArLifecycleReview.cs still keeps its own witness switch; "
                      "two tables is how they drift apart")

    # Round B: four families use a Choice as their own on/off. Their off value must be the one the
    # renderer honours, their on value and shot values must be applied by name, and the release
    # family needs its own scene (a key lifted at frame 12, captured 0.2 s later).
    choice_switches = CHOICE_SWITCH.findall(block.group(1))
    for family, setting, off_value in choice_switches:
        expected = CHOICE_OFF_VALUES.get(setting)
        if expected is None:
            errors.append(f"value pilot uses '{setting}' as a choice switch for '{family}' without "
                          "declaring its renderer off value in the guard")
        elif off_value != expected:
            errors.append(f"value pilot switches '{family}' off with '{off_value}' but the renderer "
                          f"treats '{expected}' as off; its off row would still draw the family")
    if "SwitchOnValue" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs lost the on value of a choice switch; "
                      "a Choice-driven family could not be turned on")
    if "setting.Choice" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs no longer applies a DrivenSetting's Choice; "
                      "Choice-driven low/mid/far rows would all shoot the same look")
    if "Stage.Release" not in review or "ReleaseLiftFrame" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs lost the Release scene (note-off at frame 12); "
                      "release.fx would be shot while the key is still held and draw nothing")
    if "ShowImpactRings" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs no longer pins ShowImpactRings for "
                      "impact.wave; the wave rides on the ring pass and would emit no quads")

    # Round C: a column that shoots one of a family's declared settings must say what it did NOT
    # shoot. Reading the contract catalogue (rather than a copied list) is the only way a new
    # setting cannot silently disappear from the coverage statement.
    # Round D: collect every weak column in one run instead of throwing at the first one, and
    # write the list into the manifest so a red run still says exactly which settings to drop.
    if "void Gate(" not in review or "failures.Add(" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs lost the gate collector; one CI run would "
                      "report only the first weak column again")
    if "gateFailures" not in review:
        errors.append("Diagnostics/ArValuePilotReview.cs no longer writes gateFailures into the "
                      "manifest; a red run would not say which columns to drop")

    for token, message in (
        ("EffectContractCatalog.AllFamilies", "no longer reads the effect contract catalogue; "
         "unshot settings would be a hand-written list that drifts from the product"),
        ("Unshot", "no longer records unshot percentages; a column shooting one setting could "
         "be mistaken for the whole family"),
        ("Pinned", "no longer records pinned settings; the reader cannot tell which values the "
         "scene had to hold"),
        ("coverage = Coverage()", "no longer writes the coverage block into manifest.json"),
        ("-{fam.ValueName}", "no longer keys each column's capture folder by its driven setting; "
         "two columns of one family would overwrite each other's pictures"),
    ):
        if token not in review:
            errors.append("Diagnostics/ArValuePilotReview.cs " + message)

    applied = set(CASES.findall(review))
    for family, switch, value in driven:
        for setting in (switch, value):
            if setting not in applied:
                errors.append(f"Diagnostics/ArValuePilotReview.cs drives '{family}' through "
                              f"'{setting}' but Apply() has no case for it; the shot would use whatever "
                              "the settings class happens to hold")

    cells = len(driven) * 4
    if f"Assert(cells == {cells}" not in suite:
        errors.append(f"Diagnostics/VerificationSuite.cs no longer pins the {cells} N17V cells "
                      f"({len(driven)} families x 4 rows); the page would pass with columns missing")
    if "AR value pilot (N17V)" not in suite:
        errors.append("Diagnostics/VerificationSuite.cs no longer prints the N17V PASS line; "
                      "CI's notice annotation would have nothing to quote")

    for name, marker in (("readme_vi", f"{cells} khung"), ("readme_en", f"{cells} frames")):
        row = _readme_row(files[name], "--verify-ar-value-pilot-only")
        if not row:
            errors.append(f"{name} lost the --verify-ar-value-pilot-only row")
            continue
        if marker not in row:
            errors.append(f"{name} does not say '{marker}' in the N17V row; the reader would be told "
                          "the old frame count")
        for family in ids:
            if family not in row:
                errors.append(f"{name} does not name '{family}' in the N17V row; the documented "
                              "page and the page the code shoots would be different pages")

    plan = files["plan"]
    if f"{cells} khung" not in plan:
        errors.append(f"docs/AR-VALUE-MATRIX-PLAN.md does not say '{cells} khung'; the plan would "
                      "still describe a pilot the code no longer shoots")
    for family in ids:
        if family not in plan:
            errors.append(f"docs/AR-VALUE-MATRIX-PLAN.md does not name '{family}'; the plan must "
                          "list the families its status line counts")

    workflow = files["workflow"]
    if "--verify-ar-value-pilot-only" not in workflow or "- name: AR value pilot (N17V)" not in workflow:
        errors.append(".github/workflows/build.yml no longer runs --verify-ar-value-pilot-only as its own "
                      "fail-fast step; a new family's pixel gate would only be checked an hour into the "
                      "full suite")
    if "-like 'PASS AR value pilot (N17V)*'" not in workflow:
        errors.append(".github/workflows/build.yml no longer turns the N17V PASS line into a notice "
                      "annotation; the measured per-family deltas would only exist in an artifact")
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
        print(f"{len(errors)} N17V contract problem(s):")
        for error in errors:
            print("  " + error)
        return 1
    print("checked that the N17V pilot delegates its witness, drives handled settings, "
          "declares pinned/unshot coverage from the contract catalogue, scopes each column's "
          "captures to its setting, pins its cell count, agrees with both READMEs and the plan, "
          "and keeps its fail-fast CI step")
    return 0


if __name__ == "__main__":
    sys.exit(main())
