"""Keep ``tools/ar_hardware_bench.ps1`` the helper the runbook says it is.

The script runs section 3 of ``docs/AR-HARDWARE-SIGNOFF.md`` on a real machine, so it cannot be
exercised here: there is no Windows, no graphics card and no camera in CI. What can be pinned is
everything that would make it *quietly* dishonest — a run that skips a tier, accepts a software
rasterizer, overwrites the operator's signed record, or writes a ``webcam.json`` that only looks
filled in.

Reads text only. Run with the rest of the portable checks::

    python3 -m unittest discover -s tools/tests -v
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
SCRIPT = ROOT / "tools" / "ar_hardware_bench.ps1"
RUNBOOK = ROOT / "docs" / "AR-HARDWARE-SIGNOFF.md"

TIERS = ("Lite", "Ultra")
SCENARIOS = ("overhead", "fisheye", "low_light", "fast_motion", "hand_occlusion")


def _read(path):
    return path.read_text(encoding="utf-8") if path.is_file() else ""


class ArHardwareBenchScript(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.script = _read(SCRIPT)
        cls.runbook = _read(RUNBOOK)

    def test_the_script_exists(self):
        self.assertTrue(self.script.strip(), f"{SCRIPT.name} is missing or empty")

    def test_it_measures_both_tiers_and_names_the_reports_the_validator_reads(self):
        self.assertIn("$Tiers = @('Lite', 'Ultra')", self.script)
        self.assertIn('Join-Path $evidencePath "bench-$tier.json"', self.script)
        self.assertIn('"--bench-tier=$tier"', self.script)
        # The validator reads exactly two files; a helper that writes one report twice is not a pair.
        for tier in TIERS:
            self.assertIn(tier, self.script)

    def test_it_pins_the_run_to_one_adapter_before_measuring(self):
        self.assertIn('"--bench-adapter=$adapterName"', self.script)
        self.assertIn("it must match exactly one", self.script)
        self.assertIn("matches none of", self.script)

    def test_it_refuses_a_software_rasterizer_at_every_stage(self):
        self.assertIn("$probe.warp -ne $false", self.script)
        self.assertIn("$doc.warp -ne $false", self.script)
        self.assertIn("$doc.softwareAdapter -ne $false", self.script)

    def test_an_over_budget_tier_stops_the_run_instead_of_being_tuned(self):
        self.assertIn("$code -eq 3", self.script)
        self.assertIn("instead of tuning the threshold", self.script)

    def test_it_never_opens_the_binding_file_it_only_mentions_it_in_its_help(self):
        # Comment-based help ends at '#>'; a mention after that would be code, not documentation.
        body = self.script.split("#>", 1)[-1]
        self.assertNotIn("EffectContracts", body)
        self.assertNotIn("Applied", body)

    def test_the_webcam_record_survives_a_re_run(self):
        self.assertIn("-not $Force", self.script)
        self.assertIn("(Test-Path -LiteralPath $webcamPath) -and -not $Force", self.script)

    def test_the_record_is_written_without_a_byte_order_mark(self):
        # tools/ar_hardware_signoff.py reads webcam.json as utf-8; a BOM would be the first character
        # of the JSON, and Set-Content -Encoding utf8 writes one on Windows PowerShell 5.1. The comment
        # that explains this names the command, so only the code after the help block is searched.
        body = self.script.split("#>", 1)[-1]
        self.assertIn("New-Object System.Text.UTF8Encoding($false)", body)
        self.assertIn("[System.IO.File]::WriteAllText(", body)
        self.assertNotIn("Set-Content -Encoding utf8", body)

    def test_the_record_it_writes_covers_every_scenario(self):
        for scenario in SCENARIOS:
            self.assertIn(f'shots/{scenario}.png', self.script)
            self.assertIn(f'"{scenario}":', self.script)

    def test_unverified_machine_fields_are_placeholders_a_human_must_replace(self):
        self.assertIn("FILL-ME", self.script)
        self.assertIn("'webcam.json still has FILL-ME placeholders", self.script)
        self.assertIn("measured_delay_ms is still 0", self.script)
        self.assertIn("owner_approved is not true", self.script)

    def test_the_script_is_ascii_so_any_windows_code_page_reads_it_the_same(self):
        non_ascii = sorted({character for character in self.script if ord(character) > 127})
        self.assertEqual([], non_ascii)

    def test_the_runbook_points_at_it(self):
        self.assertIn("tools/ar_hardware_bench.ps1", self.runbook)


if __name__ == "__main__":
    unittest.main()
