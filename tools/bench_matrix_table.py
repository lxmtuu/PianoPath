#!/usr/bin/env python3
"""Turn N9's two measurements into the nine-row table the plan has.

    # the GPU half: eighteen scenes, p95 per cell
    PianoPath.exe --bench-detail --bench=240 --bench-out=notes-matrix.json
    python3 tools/bench_matrix_table.py notes-matrix.json

    # the WPF half: the same eighteen cells, median draw time, already measured by
    # every `--verify` run and written into its log
    python3 tools/bench_matrix_table.py --from-log keyflow-verification.log

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
WPF_HEADER = "| Cỡ | Nốt | plain median (ms) | fx median (ms) | fps ở fx |\n|---|---|---|---|---|\n"
# One row of the line `VerifyWpfNoteDensity` prints: `720p·1: 12.34 / 15.67 ms`.
WPF_ROW = re.compile(r"(\d+)p·(\d+):\s*([0-9]+[.,][0-9]+)\s*/\s*([0-9]+[.,][0-9]+)\s*ms")
WPF_MARKER = "WPF note density (N9)"
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


def parse_log(text: str) -> dict:
    """N9's WPF cells, read back from the line `VerifyWpfNoteDensity` prints.

    That line is the measurement: every `--verify` run draws the eighteen cells and
    reports the median of eight renders per cell. Reading it back is what makes the
    plan's table fillable from a CI artifact a person can download, without a second
    copy of the numbers living in a JSON file nobody uploads.

    The line is a sentence, so the shape is checked, not guessed: one marker line,
    eighteen rows in it, and no cell twice. Anything else is refused, because a
    half-read table is indistinguishable from a measurement.
    """
    lines = [line for line in text.splitlines() if WPF_MARKER in line]
    if not lines:
        raise MatrixError(f"no '{WPF_MARKER}' line in the file — it is not a --verify log, "
                          "or the verification suite did not reach that check")
    if len(lines) > 1:
        raise MatrixError(f"{len(lines)} '{WPF_MARKER}' lines; a log with two runs in it cannot be "
                          "told apart cell by cell")

    cells = {}
    for match in WPF_ROW.finditer(lines[0]):
        height, count = int(match.group(1)), int(match.group(2))
        plain, effects = float(match.group(3).replace(",", ".")), float(match.group(4).replace(",", "."))
        if plain <= 0 or effects <= 0:
            raise MatrixError(f"{height}p/{count}: a draw time of zero means the cell was not drawn")
        for value, is_fx in ((plain, False), (effects, True)):
            key = (height, count, is_fx)
            if key in cells:
                raise MatrixError(f"{height}p/{count} appears twice in the line")
            cells[key] = {"p95": value, "width": None, "height": height, "frames": 8, "description": ""}

    expected = {(height, count, is_fx)
                for height in (720, 1080, 1440) for count in COUNTS for is_fx in (False, True)}
    missing = sorted(f"{height}p/{count}/{'fx' if is_fx else 'plain'}" for (height, count, is_fx) in expected - set(cells))
    extra = sorted(f"{height}p/{count}/{'fx' if is_fx else 'plain'}" for (height, count, is_fx) in set(cells) - expected)
    if missing or extra:
        raise MatrixError("the WPF line is not the eighteen-cell matrix"
                          + (f"; missing {', '.join(missing)}" if missing else "")
                          + (f"; unexpected {', '.join(extra)}" if extra else ""))
    return cells


def _sizes(cells) -> list:
    """The three sizes in the order the plan lists them, refusing anything else."""
    heights = sorted({height for (height, _, _) in cells})
    if heights != [720, 1080, 1440]:
        raise MatrixError(f"expected the 720/1080/1440 rows, found {heights}")
    return [(f"{height}p", height) for height in heights]


def table(cells, decimal: str = ",", header: str = HEADER) -> str:
    """Nine Markdown rows: one per size and note count, plain and effects side by side.

    The default decimal mark is the comma the plan's other tables use; `--dot` writes
    the point JSON and every other tool in the repository use, for a reader who wants
    to copy the numbers back into a script.
    """
    def number(value: float, places: int) -> str:
        return f"{value:.{places}f}".replace(".", decimal)

    rows = [header]
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


def describe_log() -> str:
    """The warning that has to travel with the WPF table."""
    return ("the software stage on the machine that ran --verify — a build runner, so these are "
            "relative numbers between cells, not what a graphics card or a user's screen does. "
            "Reported, not judged.")


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

    # The WPF half: the sentence --verify writes, read back the same way.
    wpf_rows = [f"{height}p·{count}: {2 + height / 1000:.2f} / {3 + height / 1000:.2f} ms"
                for height in (720, 1080, 1440) for count in (1, 20, 60)]
    tail = (". Every cell drew its bars and the effects added pixels; "
            "the numbers are reported for the plan's table, not judged.")
    wpf = ("PASS WPF note density (N9): median draw time of the software roll per cell, plain / fx — "
           + ", ".join(wpf_rows) + tail)
    wpf_cells = parse_log(wpf)
    if len(wpf_cells) != EXPECTED:
        return _fail("self-test: the WPF line did not read back as eighteen measurements")
    if table(wpf_cells, header=WPF_HEADER).strip().splitlines()[2].split("|")[4].strip() != "3,72":
        return _fail("self-test: the WPF table's fx column does not read back")
    for label, broken, expected in (
        ("no marker line", "NOTE something else", "no 'WPF note density (N9)' line"),
        ("two runs in one log", wpf + "\n" + wpf, "2 'WPF note density (N9)' lines"),
        ("a short line", wpf.replace(", ".join(wpf_rows), ", ".join(wpf_rows[:-1])), "missing 1440p/60"),
        ("a cell twice", wpf.replace("720p·20:", "720p·1:"), "appears twice in the line"),
    ):
        try:
            parse_log(broken)
        except MatrixError as error:
            if expected not in str(error):
                return _fail(f"self-test: WPF {label} refused for the wrong reason: {error}")
        else:
            return _fail(f"self-test: WPF {label} was accepted")

    soft = describe({**good, "softwareAdapter": True})
    if "software rasterizer" not in soft or "not judged" not in soft:
        return _fail("self-test: a software run's description lost its warning")
    print("bench matrix table: self-test passed (18 GPU cells, 18 WPF cells, 9 rows, 8 refusals)")
    return 0


def _fail(message: str) -> int:
    print(message, file=sys.stderr)
    return 1


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Print N9's nine-row table from a --bench-detail run.")
    parser.add_argument("json", nargs="?", help="the file --bench-out wrote (or - for stdin)")
    parser.add_argument("--from-log", metavar="LOG", help="read N9's WPF half from a --verify log instead of the GPU JSON")
    parser.add_argument("--out", help="write the table here instead of stdout")
    parser.add_argument("--self-test", action="store_true", help="check the parser against synthetic runs; no Windows needed")
    parser.add_argument("--no-description", action="store_true", help="print only the table, without the machine line")
    parser.add_argument("--dot", action="store_true", help="write decimal points instead of the plan's decimal commas")
    args = parser.parse_args(argv)

    if args.self_test:
        return _self_test()
    if not args.json and not args.from_log:
        parser.error("give the --bench-out file, --from-log, or --self-test")
    if args.json and args.from_log:
        parser.error("--from-log and a JSON file are two different measurements; give one")

    decimal = "." if args.dot else ","
    description = ""
    try:
        if args.from_log:
            log = sys.stdin.read() if args.from_log == "-" else Path(args.from_log).read_text(encoding="utf-8", errors="replace")
            table_text = table(parse_log(log), decimal=decimal, header=WPF_HEADER)
            description = describe_log()
        else:
            text = sys.stdin.read() if args.json == "-" else Path(args.json).read_text(encoding="utf-8")
            run = json.loads(text)
            table_text = render(run, decimal=decimal)
            description = describe(run)
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
    if not args.no_description and description:
        print(description, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
