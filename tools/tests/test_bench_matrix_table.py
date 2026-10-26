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

    # ---- the WPF half, read back from the --verify log ----

    @staticmethod
    def wpf_line(rows=None):
        rows = rows if rows is not None else [
            f"{height}p·{count}: {2 + height / 1000:.2f} / {3 + height / 1000:.2f} ms"
            for height in (720, 1080, 1440) for count in (1, 20, 60)]
        return ("PASS WPF note density (N9): median draw time of the software roll per cell, "
                "plain / fx — " + ", ".join(rows)
                + ". Every cell drew its bars and the effects added pixels; "
                  "the numbers are reported for the plan's table, not judged.")

    def test_the_wpf_line_reads_back_as_eighteen_cells(self):
        cells = b.parse_log(self.wpf_line())
        self.assertEqual(18, len(cells))
        self.assertEqual(2.72, cells[(720, 1, False)]["p95"])
        self.assertEqual(3.72, cells[(720, 1, True)]["p95"])
        verified = "NOTE something\n" + self.wpf_line() + "\nPASS other\n"
        self.assertEqual(cells, b.parse_log(verified))

    def test_the_wpf_table_uses_medians_not_p95(self):
        text = b.table(b.parse_log(self.wpf_line()), header=b.WPF_HEADER).strip().splitlines()
        self.assertIn("median", text[0])
        self.assertNotIn("p95", text[0])
        self.assertEqual("| 1080p | 1 | 3,08 | 4,08 | 245,1 |", text[5])

    def test_a_log_without_the_line_is_refused(self):
        for broken in ("", "NOTE unrelated", "PASS Phase 1 visual review: 48 images"):
            with self.assertRaises(b.MatrixError) as caught:
                b.parse_log(broken)
            self.assertIn("not a --verify log", str(caught.exception))

    def test_a_log_with_two_runs_is_refused(self):
        with self.assertRaises(b.MatrixError) as caught:
            b.parse_log(self.wpf_line() + "\n" + self.wpf_line())
        self.assertIn("2", str(caught.exception))

    def test_a_ragged_wpf_line_is_refused(self):
        rows = [f"{height}p·{count}: 4.00 / 5.00 ms"
                for height in (720, 1080, 1440) for count in (1, 20, 60)]
        with self.assertRaises(b.MatrixError) as caught:
            b.parse_log(self.wpf_line(rows[:-1]))
        self.assertIn("1440p/60", str(caught.exception))
        with self.assertRaises(b.MatrixError) as caught:
            b.parse_log(self.wpf_line(rows + ["2160p·1: 4.00 / 5.00 ms"]))
        self.assertIn("2160p/1", str(caught.exception))

    def test_a_zero_draw_time_is_refused(self):
        rows = [f"{height}p·{count}: 4.00 / 5.00 ms"
                for height in (720, 1080, 1440) for count in (1, 20, 60)]
        rows[0] = "720p·1: 0.00 / 5.00 ms"
        with self.assertRaises(b.MatrixError) as caught:
            b.parse_log(self.wpf_line(rows))
        self.assertIn("was not drawn", str(caught.exception))

    def test_cli_reads_a_log_and_names_it_software(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "keyflow-verification.log"
            log.write_text("NOTE noise\n" + self.wpf_line() + "\n", encoding="utf-8")
            out = Path(directory) / "wpf.md"
            self.assertEqual(0, b.main(["--from-log", str(log), "--out", str(out)]))
            self.assertIn("| 720p | 1 | 2,72 | 3,72 |", out.read_text(encoding="utf-8"))

    def test_cli_refuses_two_sources_at_once(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "log.txt"
            log.write_text(self.wpf_line(), encoding="utf-8")
            matrix = Path(directory) / "m.json"
            matrix.write_text(json.dumps(run_of()), encoding="utf-8")
            with self.assertRaises(SystemExit):
                b.main([str(matrix), "--from-log", str(log)])

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
