#!/usr/bin/env python3
"""Read the evidence of a real-GPU, two-tier AR sign-off and print what is still missing.

This CLI reads ``bench-Lite.json`` and ``bench-Ultra.json`` (the reports ``--bench-tier`` writes),
plus the human ``webcam.json`` record, and prints one PASS or FAIL line per condition. It writes an
optional JSON summary when asked. It does not draw, does not launch a GUI, does not assert anything
about art quality, and never edits ``Stage/Effects/EffectContracts.cs``: flipping a binding is a human
decision described in ``docs/AR-HARDWARE-SIGNOFF.md``. ``--check`` runs the same validator against
synthetic evidence in a temporary folder, so CI can prove the gate still works without a graphics card.
"""
import argparse
import json
import sys
import tempfile
from pathlib import Path

TIERS = ("Lite", "Ultra")
# The budgets FrameBudget.All writes. Pinned here so a hand-edited report cannot loosen its own gate.
BUDGETS = {"default": 8.0, "heavy": 16.0, "ar": 16.0}
SCENARIOS = ("overhead", "fisheye", "low_light", "fast_motion", "hand_occlusion")
PASS = "PASS AR hardware sign-off kit (N18-HW)"


def _load(path):
    """Return (document, error). A missing or unreadable file is a condition, not an exception."""
    if not path.is_file():
        return None, f"{path.name} is missing"
    try:
        return json.loads(path.read_text(encoding="utf-8")), None
    except (OSError, ValueError) as error:
        return None, f"{path.name} could not be read as JSON ({error})"


def _scene(report, name):
    return next((scene for scene in report.get("scenes", []) if scene.get("scene") == name), None)


def _report_errors(report, tier):
    """Every way one tier's report can fail to be physical-GPU evidence in its own right."""
    errors = []
    if report.get("schema") != 1:
        errors.append(f"{tier}: schema is {report.get('schema')!r}, expected 1")
    if report.get("warp") is not False:
        errors.append(f"{tier}: warp is {report.get('warp')!r}; WARP is a software rasterizer, not a graphics card")
    if report.get("softwareAdapter") is not False:
        errors.append(f"{tier}: softwareAdapter is {report.get('softwareAdapter')!r}, so the absolute budgets do not apply")
    if report.get("arTier") != tier:
        errors.append(f"{tier}: the report's arTier is {report.get('arTier')!r}; the switch did not reach the report")
    adapter = report.get("adapter")
    if not isinstance(adapter, str) or not adapter.strip():
        errors.append(f"{tier}: the report has no adapter name")
    if not report.get("takenUtc"):
        errors.append(f"{tier}: the report has no takenUtc timestamp")
    scenes = report.get("scenes")
    if not isinstance(scenes, list):
        return errors + [f"{tier}: the report has no scenes array"]
    for name, budget in BUDGETS.items():
        scene = _scene(report, name)
        if scene is None:
            errors.append(f"{tier}: scene '{name}' was not measured")
            continue
        if scene.get("arTier") != tier:
            errors.append(f"{tier}: scene '{name}' says arTier {scene.get('arTier')!r}, not {tier}")
        stats = scene.get("stats") or {}
        if not (isinstance(stats.get("count"), int) and stats["count"] > 0
                and scene.get("width", 0) > 0 and scene.get("height", 0) > 0):
            errors.append(f"{tier}: scene '{name}' measured no frames")
        if scene.get("budgetP95LimitMs") != budget:
            errors.append(f"{tier}: scene '{name}' carries budget {scene.get('budgetP95LimitMs')!r} ms, expected {budget} ms")
        p95 = stats.get("p95Ms")
        if not isinstance(p95, (int, float)) or p95 <= 0 or p95 > budget:
            errors.append(f"{tier}: scene '{name}' p95 {p95!r} ms is outside 0 < p95 <= {budget} ms")
        if scene.get("verdict") == "Fail":
            errors.append(f"{tier}: scene '{name}' was judged Fail by the app itself")
        if name == "ar" and not (scene.get("camera") or {}).get("frames", 0) > 0:
            errors.append(f"{tier}: scene 'ar' drew no camera pictures, so the overlay was not measured")
    return errors


def _webcam_errors(root, record):
    errors = []
    if not isinstance(record.get("operator"), str) or not record["operator"].strip():
        errors.append("webcam.json has no operator")
    machine = record.get("machine") or {}
    for key in ("gpu", "driver", "os"):
        if not isinstance(machine.get(key), str) or not machine[key].strip():
            errors.append(f"webcam.json machine.{key} is empty")
    camera = record.get("camera") or {}
    for key in ("device", "mode"):
        if not isinstance(camera.get(key), str) or not camera[key].strip():
            errors.append(f"webcam.json camera.{key} is empty")
    if not isinstance(camera.get("obs_virtual_camera"), bool):
        errors.append("webcam.json camera.obs_virtual_camera must be true or false")
    # Section 4 asks for two more checks than it has scenarios, and neither one carries a picture:
    # switching OBS Virtual Camera while the app is open, and the second camera Windows enumerates
    # under the same name with a '(2)' suffix. Both are recorded as a written result instead of a
    # pass/fail flag, because a check that failed still has to say so - only silence is refused.
    for key, what in (("obs_toggle_notes", "switching OBS Virtual Camera while the app was open"),
                      ("same_name_pair_notes", "the second same-name camera (the '(2)' device)")):
        if not isinstance(camera.get(key), str) or not camera[key].strip():
            errors.append(f"webcam.json camera.{key} has no written result for {what}; section 4 records it either way")
    delay = record.get("measured_delay_ms")
    if not isinstance(delay, (int, float)) or isinstance(delay, bool) or delay <= 0:
        errors.append("webcam.json measured_delay_ms must be the positive number MEASURE DELAY reported")
    scenarios = record.get("scenarios") or {}
    shared = {}
    noted = {}
    for name in SCENARIOS:
        entry = scenarios.get(name)
        if not isinstance(entry, dict):
            errors.append(f"webcam.json has no '{name}' scenario")
            continue
        shot = entry.get("file")
        if not isinstance(shot, str) or not shot.strip():
            errors.append(f"scenario '{name}' has no file")
        elif not (root / shot).is_file():
            errors.append(f"scenario '{name}' points at {shot}, which is not in the evidence folder")
        elif (root / shot).stat().st_size == 0:
            # A name in the record is not a picture. A zero-byte file passes every other condition
            # this gate has, and nothing would notice until somebody opened the folder by hand.
            errors.append(f"scenario '{name}' points at {shot}, which is empty; the picture itself has to be in the folder")
        else:
            shared.setdefault(shot, []).append(name)
        note = entry.get("notes")
        if not isinstance(note, str) or not note.strip():
            errors.append(f"scenario '{name}' has no notes; a failed scenario still needs a written result")
        else:
            noted.setdefault(note.strip().casefold(), []).append(name)
    for shot, names in shared.items():
        # Five scenarios exist because five different things can go wrong. One picture doing the work
        # of five proves only that a picture was taken once.
        if len(names) > 1:
            joined = ", ".join(f"'{n}'" for n in names)
            errors.append(f"{joined} all point at {shot}; each scenario needs its own picture, "
                          "taken at its own moment")
    for note_text, names in noted.items():
        # Two scenarios cannot have had the same thing happen to them, so one note written five times
        # is a placeholder that satisfies every other condition this gate has - which is exactly how
        # the prompt's own wording ended up in a record that printed no failures at all.
        if len(names) > 1:
            joined = ", ".join(f"'{n}'" for n in names)
            errors.append(f"{joined} carry the same note; five scenarios cannot have one result - "
                          "write what each picture shows, or how each one failed")
    for key in ("art_approved", "owner_approved"):
        if not isinstance(record.get(key), bool):
            errors.append(f"webcam.json {key} must be true or false")
    return errors


def validate(root):
    """Return (problems, facts). facts is the printable summary of a complete evidence folder."""
    root = Path(root)
    errors, reports, facts = [], {}, {"root": str(root), "p95": {}}
    for tier in TIERS:
        report, problem = _load(root / f"bench-{tier}.json")
        if problem:
            errors.append(problem)
            continue
        reports[tier] = report
        errors.extend(_report_errors(report, tier))
        facts["p95"][tier] = {name: (_scene(report, name) or {}).get("stats", {}).get("p95Ms")
                              for name in BUDGETS}
    if len(reports) == 2:
        if reports["Lite"].get("adapter") != reports["Ultra"].get("adapter"):
            errors.append("the two tiers were measured on different adapters, so they are not a pair")
        if reports["Lite"].get("takenUtc") == reports["Ultra"].get("takenUtc"):
            errors.append("the two tiers share one takenUtc; they must be two separate runs")
        facts["adapter"] = reports["Lite"].get("adapter")
    record, problem = _load(root / "webcam.json")
    if problem:
        errors.append(problem)
    else:
        errors.extend(_webcam_errors(root, record))
        facts["art_approved"] = record.get("art_approved") is True
        facts["owner_approved"] = record.get("owner_approved") is True
    return errors, facts


def _synthetic(root):
    """A complete, deliberately fake evidence folder: two WARP-free reports and five empty shots."""
    shots = root / "shots"
    shots.mkdir(parents=True, exist_ok=True)
    for name in SCENARIOS:
        (shots / f"{name}.png").write_bytes(b"not a real picture")
    for index, tier in enumerate(TIERS):
        scenes = []
        for name, budget in BUDGETS.items():
            scene = {
                "scene": name, "arTier": tier, "width": 1920, "height": 1080,
                "budgetP95LimitMs": budget, "verdict": "Pass",
                "stats": {"count": 120, "p95Ms": min(budget - 1, 6.5 + index)},
                "frameMs": [6.0] * 120,
            }
            if name == "ar":
                scene["camera"] = {"frames": 412, "ageMeanMs": 3.25, "ageWorstMs": 17.5}
            scenes.append(scene)
        (root / f"bench-{tier}.json").write_text(json.dumps({
            "schema": 1, "takenUtc": f"2026-10-27T0{index}:00:00.0000000Z",
            "appVersion": "synthetic", "adapter": "NVIDIA GeForce RTX 4050",
            "warp": False, "softwareAdapter": False, "shaderCompileMs": 12.5,
            "arTier": tier, "scenes": scenes,
        }, indent=2), encoding="utf-8")
    (root / "webcam.json").write_text(json.dumps({
        "operator": "synthetic", "machine": {"gpu": "RTX 4050", "driver": "560.94", "os": "Windows 11"},
        "camera": {"device": "C920 / OBS", "mode": "1280x720@60 MJPG", "obs_virtual_camera": True,
                  "obs_toggle_notes": "switched off and on twice with the app open; the overlay kept drawing",
                  "same_name_pair_notes": "the (2) device opened a black frame, so the first one was kept"},
        # Each scenario gets its own note: five identical ones are refused, and the fixture must be
        # able to pass the gate it is used to test.
        "scenarios": {name: {"file": f"shots/{name}.png", "notes": f"synthetic result for {name}"}
                      for name in SCENARIOS},
        "measured_delay_ms": 85, "art_approved": False, "owner_approved": False,
    }, indent=2), encoding="utf-8")


def self_check():
    """Break one promise at a time on synthetic evidence; print the result of every case."""
    cases = []

    def case(label, mutate, expected):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            _synthetic(root)
            mutate(root)
            problems, _ = validate(root)
            hit = any(expected in problem for problem in problems)
            cases.append((label, hit, problems))

    case("a complete folder passes", lambda root: None, "__never__")
    # The first case is special: it expects NO problems at all.
    label, _, problems = cases.pop(0)
    ok = not problems
    print(f"  {'PASS' if ok else 'FAIL'} self-check: {label}" + (f" ({problems})" if problems else ""))
    failures = [] if ok else [label]

    case("WARP is refused", lambda root: _patch(root, "bench-Lite.json", warp=True), "WARP is a software rasterizer")
    case("software adapter is refused", lambda root: _patch(root, "bench-Ultra.json", softwareAdapter=True), "softwareAdapter")
    case("a missing tier is named", lambda root: (root / "bench-Ultra.json").unlink(), "bench-Ultra.json is missing")
    case("a swapped arTier is caught", lambda root: _patch(root, "bench-Lite.json", arTier="Ultra"), "arTier")
    case("an over-budget p95 is caught", lambda root: _scene_patch(root, "bench-Ultra.json", "default", 99), "p95")
    case("a loosened budget is caught", lambda root: _budget_patch(root, "bench-Lite.json", "heavy", 99), "expected 16.0 ms")
    case("a camera-less ar scene is caught", lambda root: _scene_patch(root, "bench-Lite.json", "ar", None, camera=False), "drew no camera pictures")
    case("two adapters are refused", lambda root: _patch(root, "bench-Ultra.json", adapter="another card"), "different adapters")
    case("one run copied twice is refused", lambda root: _patch(root, "bench-Ultra.json", takenUtc="2026-10-27T00:00:00.0000000Z"), "two separate runs")
    case("a missing webcam record is caught", lambda root: (root / "webcam.json").unlink(), "webcam.json is missing")
    case("a missing scenario shot is caught", lambda root: (root / "shots/fisheye.png").unlink(), "fisheye")
    case("an empty picture is caught", lambda root: (root / "shots/overhead.png").write_bytes(b""), "is empty")
    case("one picture reused for every scenario is caught",
         lambda root: _scene_file_patch(root, "fisheye", "shots/overhead.png"),
         "each scenario needs its own picture")
    case("one note copied into every scenario is caught",
         lambda root: _all_notes_patch(root, "the same sentence everywhere"),
         "carry the same note")
    case("an unmeasured delay is caught", lambda root: _webcam_patch(root, measured_delay_ms=0), "measured_delay_ms")
    case("an untold OBS toggle is caught", lambda root: _camera_patch(root, obs_toggle_notes="  "),
         "camera.obs_toggle_notes")
    case("an untold same-name pair is caught", lambda root: _camera_patch(root, same_name_pair_notes=""),
         "camera.same_name_pair_notes")
    for label, hit, problems in cases:
        print(f"  {'PASS' if hit else 'FAIL'} self-check: {label}" + ("" if hit else f" ({problems})"))
        if not hit:
            failures.append(label)
    if failures:
        print(f"FAIL {PASS}: {len(failures)} self-check(s) failed")
        return 1
    print(f"{PASS}: {len(cases) + 1} synthetic cases pass; this is not hardware evidence")
    return 0


def _patch(root, name, **fields):
    path = root / name
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc.update(fields)
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def _webcam_patch(root, **fields):
    _patch(root, "webcam.json", **fields)


def _scene_file_patch(root, name, file):
    """Point one scenario at another scenario's picture, as a folder with one shot reused would."""
    path = root / "webcam.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc["scenarios"][name]["file"] = file
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def _all_notes_patch(root, note):
    """Write one sentence into every scenario's note, as a record filled in by copy and paste would."""
    path = root / "webcam.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    for name in SCENARIOS:
        doc["scenarios"][name]["notes"] = note
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def _camera_patch(root, **fields):
    path = root / "webcam.json"
    doc = json.loads(path.read_text(encoding="utf-8"))
    doc["camera"].update(fields)
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def _scene_patch(root, name, scene, p95=None, camera=False):
    path = root / name
    doc = json.loads(path.read_text(encoding="utf-8"))
    entry = _scene(doc, scene)
    if p95 is not None:
        entry["stats"]["p95Ms"] = p95
    if camera is False:
        entry.pop("camera", None)
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def _budget_patch(root, name, scene, budget):
    path = root / name
    doc = json.loads(path.read_text(encoding="utf-8"))
    _scene(doc, scene)["budgetP95LimitMs"] = budget
    path.write_text(json.dumps(doc, indent=2), encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, help="folder holding bench-Lite.json, bench-Ultra.json and webcam.json")
    parser.add_argument("--summary", type=Path, help="optional path for the JSON summary")
    parser.add_argument("--check", action="store_true", help="run the synthetic self-check (no GPU, no evidence needed)")
    args = parser.parse_args(argv)
    if args.check:
        return self_check()
    if args.evidence is None:
        parser.error("pass --evidence <folder> or --check")
    problems, facts = validate(args.evidence)
    if args.summary:
        args.summary.write_text(json.dumps({"ok": not problems, "problems": problems, "facts": facts}, indent=2), encoding="utf-8")
    for problem in problems:
        print(f"FAIL AR hardware evidence (N18-HW): {problem}")
    if problems:
        print(f"FAIL AR hardware evidence (N18-HW): {len(problems)} condition(s) unmet; no sign-off")
        return 1
    print(f"PASS AR hardware evidence (N18-HW): Lite and Ultra on {facts['adapter']}, budgets 8/16/16 ms, 5 webcam scenarios attached")
    for tier in TIERS:
        print(f"  {tier}: " + ", ".join(f"{name} p95 {facts['p95'][tier][name]} ms" for name in BUDGETS))
    print("PENDING owner approval: art_approved=%s, owner_approved=%s; ArProjectedParameterBindings stays Unverified"
          % (facts.get("art_approved"), facts.get("owner_approved")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
