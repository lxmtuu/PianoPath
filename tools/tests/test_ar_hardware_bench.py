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
        # The written shape: one file and one note per scenario, built from the named list.
        self.assertIn('"{0}": {{ "file": "{1}", "notes": "{2}" }}', self.script)
        self.assertIn("foreach ($name in $Scenarios)", self.script)

    def test_it_can_fill_the_record_in_by_asking_instead_of_hand_editing_json(self):
        # Section 4 by hand means editing JSON in Notepad, where one missing quote turns the whole
        # record unreadable. -FillWebcam asks the questions and copies the pictures itself.
        self.assertIn("[switch]$FillWebcam", self.script)
        self.assertIn("Edit-WebcamRecord -Path $webcamPath -Folder $evidencePath", self.script)
        self.assertIn("Copy-Item -LiteralPath $shot -Destination $target -Force", self.script)
        self.assertIn("$shot.Trim('\"')", self.script)

    def test_a_note_typed_at_a_prompt_cannot_break_the_json(self):
        self.assertIn("function ConvertTo-JsonText", self.script)
        self.assertIn("$Value.Replace(", self.script)
        self.assertIn("ConvertTo-JsonText -Value $Files[$name]", self.script)

    def test_the_owner_sign_off_has_a_command_of_its_own(self):
        # The one decision the machine cannot make had nowhere to live but Notepad, and JSON edited by
        # hand is one missing quote away from "could not be read as JSON".
        self.assertIn("[switch]$Approve", self.script)
        self.assertIn("pwsh tools/ar_hardware_bench.ps1 -Approve", self.script)
        self.assertIn("function Approve-Record", self.script)

    def test_the_sign_off_is_refused_until_the_other_two_conditions_hold(self):
        # Section 6 lists the approval third of three: it is a signature on the two tier reports and
        # the five scenarios, not a way around them. An owner saying "I approve" over a folder with no
        # pictures spends the one decision only they can make on a record that proves nothing.
        self.assertIn("refused: the owner signs a complete record, and this one is not:", self.script)
        self.assertIn("nothing was signed - fix the lines above, then run -Approve again", self.script)
        # The refusal is the validator's own verdict, not a second list that can drift out of step.
        self.assertIn("--evidence $Folder --summary $summaryPath", self.script)
        self.assertIn("$summary.problems", self.script)
        # And it is a throw: signing is never something the script does on the way past a failure.
        self.assertIn("throw 'nothing was signed", self.script)

    def test_signing_takes_a_second_yes_and_never_happens_on_the_way_past(self):
        self.assertIn("sign it? (yes to approve, anything else leaves the record as it is)", self.script)
        self.assertIn("not signed - the record is unchanged", self.script)
        self.assertIn("-ArtApproved $true -OwnerApproved $true", self.script)
        # The approval is about the record; nothing about it touches the binding.
        self.assertIn("ArProjectedParameterBindings stays Unverified until the binding PR lands", self.script)

    def test_approving_measures_nothing_and_rewrites_no_evidence(self):
        # -Approve runs the validator and writes two booleans. A run that also benched two tiers or
        # re-templated webcam.json would destroy the evidence it is being asked to sign.
        for guard in (
            "if (-not $ValidateOnly -and -not $FillWebcam -and -not $Approve) {",
            "if (-not $SkipProbe -and -not $ValidateOnly -and -not $WebcamOnly -and -not $FillWebcam -and -not $Approve) {",
            "if (-not $ValidateOnly -and -not $WebcamOnly -and -not $FillWebcam -and -not $Approve) {",
            "if (-not $BenchOnly -and -not $Approve) {",
        ):
            self.assertIn(guard, self.script)

    def test_the_record_path_belongs_to_the_folder_not_to_one_section(self):
        # -Approve reached the sign-off holding an empty $webcamPath and reported "no webcam.json at "
        # for a record that was in the folder all along: the path was assigned inside the very section
        # that signing skips. Any mode that needs the record has to be able to find it.
        before_adapter = self.script.split("# ---------------------------------------------------------------- adapter")[0]
        self.assertIn("$webcamPath = Join-Path $evidencePath 'webcam.json'", before_adapter)

    def test_a_note_cannot_be_a_yes_or_no_answer(self):
        # Asked what a picture shows, an operator with no picture to describe falls back on "yes" -
        # and five of those read like five results. A note is a description, not a verdict.
        self.assertIn("[switch]$Sentence", self.script)
        self.assertIn("that answers a yes-or-no question; this one needs what the picture shows, in one sentence",
                      self.script)
        self.assertIn("Read-Filled -Prompt \"What does '$name' show - or how did it fail?\" -Current $currentNotes -Sentence",
                      self.script)

    def test_a_record_with_not_one_picture_in_it_is_not_written(self):
        # Filling the record before section 4 produced invented notes; the run looked like it had
        # recorded something. A record that attaches no picture describes nothing, and an operator who
        # pasted real paths has real pictures by then - so only the invented kind is stopped.
        self.assertIn("none of the five pictures is in the folder yet, so this record describes nothing:", self.script)
        self.assertIn("shoot the five scenarios (section 4), then run -FillWebcam again", self.script)
        self.assertIn("a record with no picture is not evidence", self.script)
        self.assertIn("throw 'nothing was written - a record with no picture is not evidence'", self.script)

    def test_a_scenario_that_has_not_been_shot_yet_says_so_at_the_prompt(self):
        # Keeping the target file name is the right answer before a picture exists, but it must not
        # look like the answer: the operator would reach the end of the run believing it was recorded.
        self.assertIn("no picture at $current yet", self.script)
        self.assertIn("the validator keeps asking until the file is there", self.script)

    def test_the_record_is_read_back_and_only_written_on_purpose(self):
        # An answer pasted from the wrong place looks exactly like a real answer once it is in the
        # file: the operator's name ended up holding the label of the next question. Reading the whole
        # record back and requiring a yes is the last point at which that is still catchable.
        self.assertIn("About to write this record:", self.script)
        self.assertIn("write it? (yes to write, anything else throws it away)", self.script)
        self.assertIn("nothing was written - run -FillWebcam again", self.script)

    def test_the_prompt_never_offers_enter_when_there_is_nothing_to_keep(self):
        # A field with no value yet plus a hint saying "press Enter to keep it" loops forever and
        # looks like a hang: the operator pressed Enter three times before asking what was wrong. The
        # hint has to depend on whether keeping is actually possible.
        self.assertIn("$hasCurrent = -not ([string]::IsNullOrWhiteSpace($Current) -or $Current -match 'FILL-ME')", self.script)
        self.assertIn("there is nothing to keep yet", self.script)
        self.assertIn("this one cannot stay empty", self.script)

    def test_filling_the_record_in_never_answers_the_two_approvals(self):
        # art_approved and owner_approved are the owner's; the fill-in passes them straight through.
        self.assertIn("-ArtApproved ([bool]$record.art_approved)", self.script)
        self.assertIn("-OwnerApproved ([bool]$record.owner_approved)", self.script)

    def test_an_unmeasured_delay_can_be_typed_as_zero_but_never_passes(self):
        # There is no "skip" key at a prompt, so an operator who has not run MEASURE DELAY yet had to
        # either stop or invent a number. 0 is the honest answer: it is accepted here and still
        # refused by the validator and named by Get-RemainingWork until it is measured.
        self.assertIn("if ($answer -eq '0')", self.script)
        self.assertIn("recorded as not measured yet", self.script)
        self.assertIn("this one must be a number greater than 0", self.script)

    def test_a_kept_delay_of_zero_is_refused_because_it_means_nobody_measured(self):
        self.assertIn("$delayAnswer = Read-Filled", self.script)
        self.assertIn("this one must be a number greater than 0", self.script)

    def test_unverified_machine_fields_are_placeholders_a_human_must_replace(self):
        self.assertIn("FILL-ME", self.script)
        self.assertIn("'webcam.json still has FILL-ME placeholders", self.script)
        self.assertIn("measured_delay_ms is still 0", self.script)
        self.assertIn("owner_approved is not true", self.script)

    def test_it_names_the_fields_that_are_still_placeholders(self):
        # After the first run most machine fields are filled from WMI, so a message that lists every
        # possible placeholder sends the operator hunting through a file that is nearly done.
        self.assertIn("webcam.json still has FILL-ME placeholders: ' + ($placeholders -join ', ')", self.script)
        self.assertIn("$record.machine.$key", self.script)
        self.assertIn("$record.camera.$key", self.script)

    def test_it_refuses_an_evidence_folder_on_a_drive_that_does_not_exist(self):
        # The first run on a machine with no D: died inside New-Item with "Cannot find drive", which
        # names no alternative and says nothing about evidence. The check names the drives it does have.
        self.assertIn("[System.IO.Path]::GetPathRoot($EvidenceDir)", self.script)
        self.assertIn("this machine has no such drive", self.script)
        self.assertIn("Get-PSDrive -PSProvider FileSystem", self.script)
        self.assertIn("Pass -EvidenceDir '", self.script)

    def test_the_default_evidence_folder_falls_back_instead_of_dying_on_a_missing_d_drive(self):
        self.assertIn("$defaultRoot = 'D:\\keyflow-signoff'", self.script)
        self.assertIn("Join-Path $HOME 'keyflow-signoff'", self.script)
        self.assertIn("no D: drive on this machine", self.script)

    def test_it_waits_for_the_gui_executable_and_reads_a_real_exit_code(self):
        # A WinExe invoked with '&' can return before the process ends, which leaves $LASTEXITCODE
        # unset: the first probe run on real hardware reported "the probe exited  - no adapter, no
        # measurement", with the exit code missing and the report still unwritten.
        self.assertIn("Start-Process -FilePath $ExePath", self.script)
        self.assertIn("-Wait -PassThru", self.script)
        self.assertIn("return [int]$process.ExitCode", self.script)
        self.assertNotIn("& $exePath", self.script)
        # The one place $LASTEXITCODE may still appear is after the python validator, which is a
        # console application and therefore does get waited for.
        self.assertLessEqual(self.script.count("$LASTEXITCODE"), 1)

    def test_a_run_that_writes_no_report_says_so_and_shows_what_the_app_left(self):
        self.assertIn("but wrote no report at", self.script)
        self.assertIn("the app wrote nothing before it ended", self.script)
        self.assertIn("[System.IO.Path]::ChangeExtension($LogPath, '.err.log')", self.script)

    def test_the_script_is_ascii_so_any_windows_code_page_reads_it_the_same(self):
        non_ascii = sorted({character for character in self.script if ord(character) > 127})
        self.assertEqual([], non_ascii)

    def test_the_runbook_points_at_it(self):
        self.assertIn("tools/ar_hardware_bench.ps1", self.runbook)

    def test_the_two_checks_that_carry_no_picture_have_a_home_in_the_record(self):
        # Section 4 demands three checks beyond the five scenarios. Two of them produce no photo,
        # so webcam.json is the only place they can exist: without a field the operator can skip
        # them and ar_hardware_signoff.py still prints PASS, and without a prompt nobody is ever
        # asked. Both the writer and the remaining-work report have to carry them.
        for key in ("obs_toggle_notes", "same_name_pair_notes"):
            self.assertIn(f'"{key}"', self.script)
            self.assertIn(f"camera.{key} is still empty", self.script)
            self.assertIn(f"$record.camera.{key}", self.script)
            self.assertIn(key, self.runbook)
        # Written once, in one place: template, prompt and pass-through all use the same parameters.
        self.assertIn("-ObsToggleNotes $obsToggle", self.script)
        self.assertIn("-SameNamePairNotes $sameNamePair", self.script)
        self.assertIn("-ObsToggleNotes 'FILL-ME", self.script)
        self.assertIn("-SameNamePairNotes 'FILL-ME", self.script)
        # Both are asked as free text, because a check that failed has to say how it failed.
        self.assertIn("while the app was open", self.script)
        self.assertIn("the one Windows calls (2)", self.script)


if __name__ == "__main__":
    unittest.main()
