"""Synthetic contract tests for tools/bench_matrix_table.py — no GPU, no Windows.

The table is what the plan's N9 rows are filled from, so the checks are about
reading a `--bench-detail` run exactly: eighteen named scenes, one p95 each, and a
refusal instead of a blank when the run is short. Real frame times are not
something a test can invent, and nothing here pretends otherwise.
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import bench_matrix_table as b


def run_of(p95=lambda height, count, effects: 2.0 + height / 1000 + count / 100 + (0.5 if effects else 0)):
    return {
        "schema": 1, "takenUtc": "2026-10-20T00:00:00Z", "appVersion": "1.0.0",
        "adapter": "contract adapter", "warp": False, "softwareAdapter": False,
        "scenes": [
            {"scene": f"notes-{height}p-{count:02d}-{'fx' if effects else 'plain'}",
             "preset": "Concert Gold", "width": height * 16 // 9, "height": height,
             "frames": 240, "warmupFrames": 24, "warmupMs": 12.5, "particles": 0,
             "budgetP95LimitMs": 0, "verdict": "NotApplicable", "verdictNote": "",
             "stats": {"count": 240, "p95Ms": p95(height, count, effects)}}
            for height in (720, 1080, 1440) for count in (1, 20, 60) for effects in (False, True)],
    }


class BenchMatrixTableTests(unittest.TestCase):
    def test_eighteen_scenes_become_nine_rows(self):
        cells = b.parse(run_of())
        self.assertEqual(18, len(cells))
        lines = b.table(cells).strip().splitlines()
        self.assertEqual(11, len(lines))  # header + delimiter + nine rows
        self.assertEqual("| 720p | 1 | 2,73 | 3,23 | 309,6 |", lines[2])
        self.assertTrue(lines[-1].startswith("| 1440p | 60 |"), lines[-1])

    def test_fps_column_is_1000_over_p95(self):
        cells = b.parse(run_of(lambda h, c, fx: 4.0 if fx else 1.0))
        row = b.table(cells).strip().splitlines()[2].split("|")
        self.assertEqual(["720p", "1", "1,00", "4,00", "250,0"], [cell.strip() for cell in row[1:6]])
        dotted = b.table(cells, decimal=".").strip().splitlines()[2].split("|")
        self.assertEqual(["720p", "1", "1.00", "4.00", "250.0"], [cell.strip() for cell in dotted[1:6]])

    def test_a_short_run_is_refused_not_padded(self):
        run = run_of()
        run["scenes"] = run["scenes"][:-1]
        with self.assertRaises(b.MatrixError) as caught:
            b.parse(run)
        self.assertIn("missing 1 of 18", str(caught.exception))
        self.assertIn("notes-1440p-60-fx", str(caught.exception))

    def test_a_cell_without_a_p95_is_refused(self):
        run = run_of()
        run["scenes"][3]["stats"]["p95Ms"] = None
        with self.assertRaises(b.MatrixError) as caught:
            b.parse(run)
        self.assertIn("no usable p95", str(caught.exception))

    def test_the_same_cell_twice_is_refused(self):
        run = run_of()
        run["scenes"].append(json.loads(json.dumps(run["scenes"][0])))
        with self.assertRaises(b.MatrixError) as caught:
            b.parse(run)
        self.assertIn("twice", str(caught.exception))

    def test_a_file_that_is_not_a_bench_run_is_refused(self):
        for broken in ({}, {"scenes": None}, {"scenes": [1, 2]}, []):
            with self.assertRaises(b.MatrixError):
                b.parse(broken)

    def test_other_scenes_are_ignored_and_sizes_must_be_the_three(self):
        run = run_of()
        run["scenes"].append({"scene": "default", "stats": {"p95Ms": 1.0}, "width": 1920, "height": 1080})
        self.assertEqual(18, len(b.parse(run)))
        run["scenes"] = [scene for scene in run["scenes"] if "1440p" not in scene["scene"]]
        with self.assertRaises(b.MatrixError):
            b.parse(run)

    def test_description_names_the_machine_and_warns_on_software(self):
        self.assertIn("graphics card", b.describe(run_of()))
        self.assertIn("contract adapter", b.describe(run_of()))
        self.assertIn("software rasterizer", b.describe({**run_of(), "softwareAdapter": True}))
        self.assertIn("WARP forced", b.describe({**run_of(), "warp": True}))
        self.assertEqual("", b.describe(None))

    def test_cli_writes_the_table_and_refuses_a_broken_file(self):
        with tempfile.TemporaryDirectory() as directory:
            good = Path(directory) / "notes-matrix.json"
            good.write_text(json.dumps(run_of()), encoding="utf-8")
            out = Path(directory) / "table.md"
            self.assertEqual(0, b.main([str(good), "--out", str(out), "--no-description", "--dot"]))
            self.assertIn("| 720p | 1 | 2.73 |", out.read_text(encoding="utf-8"))
            bad = Path(directory) / "short.json"
            bad.write_text(json.dumps({"scenes": run_of()["scenes"][:2]}), encoding="utf-8")
            self.assertEqual(1, b.main([str(bad)]))


if __name__ == "__main__":
    unittest.main()
