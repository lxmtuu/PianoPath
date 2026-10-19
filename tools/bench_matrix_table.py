#!/usr/bin/env python3
"""Turn `--bench-detail`'s JSON into N9's nine-row table.

    PianoPath.exe --bench-detail --bench=240 --bench-out=notes-matrix.json
    python3 tools/bench_matrix_table.py notes-matrix.json

Eighteen scenes are the measurement (three window sizes, three note counts, effects
off and on); this prints the nine rows the plan's table has, one pair of p95 figures
per row, and says on stderr which machine drew them. It reads only the fields
`FrameBenchRun.ToJson` writes, so it cannot drift into measuring something else.

Two rules, both deliberate:

* A missing cell is an error, not a blank. Eighteen scenes are what the run asks for;
  if the app stopped after the first few, a table with holes in it looks like a
  measurement of the holes.
* `fps ở fx` is 1000 / p95, which is what the plan's column means: the frame rate a
  reader would see if every frame took as long as the slowest one in twenty. p95 is
  not an average, so this number is lower than a mean-based fps on purpose.

Reported, not judged: nothing here takes a threshold. The eighteen cells carry
`P95LimitMs = 0` on purpose, because what "too slow" means depends on the graphics
card, and a build runner has none.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

SIZES = ("720p", "1080p", "1440p")
COUNTS = (1, 20, 60)
SCENE = re.compile(r"^notes-(\d+)p-(\d+)-(plain|fx)$")
HEADER = "| Cỡ | Nốt | plain p95 (ms) | fx p95 (ms) | fps ở fx |\n|---|---|---|---|---|\n"
EXPECTED = 18


class MatrixError(ValueError):
    """The file is not a `--bench-detail` run this script can read."""


def _number(value) -> float:
    """A finite, positive number from JSON, or a refusal — booleans are ints in Python."""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise MatrixError(f"expected a number, found {value!r}")
    number = float(value)
    if number != number or number <= 0 or number == float("inf"):
        raise MatrixError(f"expected a finite positive number, found {value!r}")
    return number


def parse(run) -> dict:
    """The eighteen cells of one run, keyed by (height, count, effects)."""
    if not isinstance(run, dict):
        raise MatrixError("the file is not a JSON object")
    scenes = run.get("scenes")
    if not isinstance(scenes, list):
        raise MatrixError("the file has no 'scenes' array, so it is not a --bench output")

    cells = {}
    for scene in scenes:
        if not isinstance(scene, dict):
            raise MatrixError(f"a scene is {type(scene).__name__}, not an object")
        name = scene.get("scene")
        match = SCENE.match(name) if isinstance(name, str) else None
        if match is None:
            continue
        height, count, variant = int(match.group(1)), int(match.group(2)), match.group(3)
        key = (height, count, variant == "fx")
        if key in cells:
            raise MatrixError(f"the run measured {name} twice, so the two figures cannot be told apart")
        stats = scene.get("stats")
        if not isinstance(stats, dict):
            raise MatrixError(f"{name} has no 'stats' object")
        try:
            p95 = _number(stats.get("p95Ms"))
        except MatrixError as error:
            raise MatrixError(f"{name} has no usable p95 ({error})") from None
        cells[key] = {
            "p95": p95,
            "width": scene.get("width"),
            "height": scene.get("height"),
            "frames": scene.get("frames"),
            "description": scene.get("description", ""),
        }

    missing = [
        f"notes-{height}p-{count:02d}-{variant}"
        for height in (720, 1080, 1440)
        for count in COUNTS
        for variant in ("plain", "fx")
        if (height, count, variant == "fx") not in cells
    ]
    if missing:
        raise MatrixError(
            f"the run is missing {len(missing)} of {EXPECTED} cells ({', '.join(missing)}) — "
            "the app stopped before measuring them, so a full table would be a guess")
    if len(cells) != EXPECTED:
        raise MatrixError(f"expected {EXPECTED} cells, read {len(cells)}")
    return cells


def _sizes(cells) -> list:
    """The three sizes in the order the plan lists them, refusing anything else."""
    heights = sorted({height for (height, _, _) in cells})
    if heights != [720, 1080, 1440]:
        raise MatrixError(f"expected the 720/1080/1440 rows, found {heights}")
    return [(f"{height}p", height) for height in heights]


def table(cells, decimal: str = ",") -> str:
    """Nine Markdown rows: one per size and note count, plain and effects side by side.

    The default decimal mark is the comma the plan's other tables use; `--dot` writes
    the point JSON and every other tool in the repository use, for a reader who wants
    to copy the numbers back into a script.
    """
    def number(value: float, places: int) -> str:
        return f"{value:.{places}f}".replace(".", decimal)

    rows = [HEADER]
    for label, height in _sizes(cells):
        for count in COUNTS:
            plain = cells[(height, count, False)]
            effects = cells[(height, count, True)]
            fps = 1000.0 / effects["p95"]
            rows.append(
                f"| {label} | {count} | {number(plain['p95'], 2)} | {number(effects['p95'], 2)} | {number(fps, 1)} |\n")
    return "".join(rows)


def describe(run) -> str:
    """The line that has to travel with the table: which machine drew it, and whether it had a card."""
    if not isinstance(run, dict):
        return ""
    adapter = run.get("adapter") or "an unnamed adapter"
    software = run.get("softwareAdapter")
    warp = run.get("warp")
    taken = run.get("takenUtc") or "an unrecorded time"
    kind = ("a software rasterizer (WARP)" if software else "a graphics card")
    if warp and not software:
        kind += " (WARP forced)"
    return (f"{SIZES[0]}-{SIZES[-1]} matrix from {adapter} — {kind}, drawn {taken}, "
            f"version {run.get('appVersion') or 'unrecorded'}. p95 per cell; "
            "reported, not judged.")


def render(run, decimal: str = ",") -> str:
    return table(parse(run), decimal)


def _self_test() -> int:
    """Synthetic checks that need no Windows machine: the shapes and the refusals."""
    def run_of(p95=lambda height, count, effects: 2.0 + height / 1000 + count / 100 + (0.5 if effects else 0)):
        return {"adapter": "contract", "softwareAdapter": False, "warp": False, "takenUtc": "2026-10-20T00:00:00Z",
                "appVersion": "contract", "scenes": [
                    {"scene": f"notes-{height}p-{count:02d}-{'fx' if effects else 'plain'}",
                     "width": height * 16 // 9, "height": height, "frames": 240,
                     "stats": {"p95Ms": p95(height, count, effects)}}
                    for height in (720, 1080, 1440) for count in (1, 20, 60) for effects in (False, True)]}

    good = run_of()
    cells = parse(good)
    if len(cells) != EXPECTED:
        return _fail("self-test: eighteen synthetic cells were not read as eighteen")
    text = table(cells)
    lines = text.strip().splitlines()
    if len(lines) != 11:  # header, delimiter row, nine data rows
        return _fail(f"self-test: expected 11 table lines, wrote {len(lines)}")
    first = lines[2].split("|")
    if [cell.strip() for cell in first[1:4]] != ["720p", "1", "2,73"]:
        return _fail(f"self-test: the first row reads {first!r}")
    # 1000 / p95, in the plan's decimal comma; the last row is the slowest, so its fps must be the smallest.
    if lines[-1].split("|")[5].strip() != f"{1000 / cells[(1440, 60, True)]['p95']:.1f}".replace(".", ","):
        return _fail("self-test: the fx column is not 1000 / p95")
    if "graphics card" not in describe(good):
        return _fail("self-test: a hardware run was not described as one")

    dotted = table(cells, decimal=".")
    if dotted.strip().splitlines()[2].split("|")[3].strip() != "2.73":
        return _fail("self-test: --dot did not switch the decimal mark back to a point")

    for label, broken, expected in (
        ("a missing cell", {**good, "scenes": good["scenes"][:-1]}, "missing 1 of 18"),
        ("a null p95", {**good, "scenes": [{**scene, "stats": {"p95Ms": None}} for scene in good["scenes"]]}, "no usable p95"),
        ("a duplicated cell", {**good, "scenes": good["scenes"] + good["scenes"][:1]}, "twice"),
        ("no scenes", {"scenes": "none"}, "no 'scenes' array"),
    ):
        try:
            parse(broken)
        except MatrixError as error:
            if expected not in str(error):
                return _fail(f"self-test: {label} refused for the wrong reason: {error}")
        else:
            return _fail(f"self-test: {label} was accepted")

    soft = describe({**good, "softwareAdapter": True})
    if "software rasterizer" not in soft or "not judged" not in soft:
        return _fail("self-test: a software run's description lost its warning")
    print("bench matrix table: self-test passed (18 cells, 9 rows, 4 refusals)")
    return 0


def _fail(message: str) -> int:
    print(message, file=sys.stderr)
    return 1


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Print N9's nine-row table from a --bench-detail run.")
    parser.add_argument("json", nargs="?", help="the file --bench-out wrote (or - for stdin)")
    parser.add_argument("--out", help="write the table here instead of stdout")
    parser.add_argument("--self-test", action="store_true", help="check the parser against synthetic runs; no Windows needed")
    parser.add_argument("--no-description", action="store_true", help="print only the table, without the machine line")
    parser.add_argument("--dot", action="store_true", help="write decimal points instead of the plan's decimal commas")
    args = parser.parse_args(argv)

    if args.self_test:
        return _self_test()
    if not args.json:
        parser.error("give the --bench-out file (or --self-test)")

    try:
        text = sys.stdin.read() if args.json == "-" else Path(args.json).read_text(encoding="utf-8")
        run = json.loads(text)
        table_text = render(run, decimal="." if args.dot else ",")
    except OSError as error:
        return _fail(f"bench matrix table: {error}")
    except json.JSONDecodeError as error:
        return _fail(f"bench matrix table: {args.json} is not JSON ({error})")
    except MatrixError as error:
        return _fail(f"bench matrix table: {error}")

    if args.out:
        Path(args.out).write_text(table_text, encoding="utf-8")
        print(f"bench matrix table: wrote {args.out}", file=sys.stderr)
    else:
        sys.stdout.write(table_text)
    if not args.no_description and (description := describe(run)):
        print(description, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
