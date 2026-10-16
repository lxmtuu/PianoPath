# Keyflow 2.1.0 · Final release check

Pre-merge evidence snapshot, 2026-10-16. Scope: the complete feature branch since `a52ca16947df6b689c594c7b747a95fbd694618e`, not only the last Settings changes.

## Changes being released

- Approved opt-in press/held/release particle materials, shared GPU Stage/AR rendering, the approved AR role/note-group fair allocation of the fixed 192 cap, and persistent offline AR particle simulation with the planned scene clock.
- The approved twelve-page WPF Settings layout, one editor per property, legacy routes, Advanced/search/deep-link handling, preserved business controls and preset/machine-local boundaries.
- Version 2.1.0, bilingual changelogs/README navigation and regenerated public documents.
- Safe public-document staging using an isolated Git index, no destructive orphan checkout or auxiliary private documentation branch. Source-main protection remains in place. Fixed the existing invalid PowerShell `Set-Content -Append` calls and added a static regression guard.
- Release-published events support `gh release create` after merging without requiring a local tag push. Native/source checks fail before expensive capture/package work; none of the release's test or published-executable gates is removed.

## Final-check corrections

- The concurrent epoch test now uses a dedicated worker rather than blocking on a thread-pool worker under the snapshot lock. Its generation, locking, clear-request and deadline assertions remain intact.
- Language round-trip assertions expect Overview, the actual first page, in both directions rather than the legacy Style caption.
- Compact Settings accessibility checks exercise both collapsed and expanded Advanced states, restoring the original state. Only actually visible rows are measured; the expanded pass checks the previously hidden controls too.
- Live-bar coverage remains directly revealable by its existing MORE/LESS button rather than sitting behind an additional closed Advanced section.
- Chrome does not count construction/initial layout as user idle time: the idle clock starts at the first rendered frame. Functional failure messages now include the measured viewport/visibility state.

## Verified

- `python3 tools/check_sources.py`, `check_settings_layout_migration.py`, `check_settings_layout_inventory.py`, and `git diff --check`: PASS.
- `python3 -m unittest discover -s tools/tests`: 150 tests, no failures, 30 conditional skips.
- Full source run [38867237126](https://github.com/lxmtuu/PianoPath/actions/runs/38867237126), application inputs at `e8d8b49`: **SUCCESS**: static, Linux portable tests, full Windows native/WPF verification, native art/evidence validation, frame-budget gate, installer validation, refreshed README/material/preset previews and distributable build. Only the separate opt-in app-build/key-ensemble/ar-budget jobs were skipped.
- Release preflight [38867895710](https://github.com/lxmtuu/PianoPath/actions/runs/38867895710), source `e236fdf`: **SUCCESS**. Windows portable tests: **1,357 passed, zero failed/not executed**. Actual published self-contained x64 executable: **7,911 assertions passed, one skipped check group**. Isolated public-tree staging, all three ZIPs, installer and SHA256 list PASS.
- Preflight artifact: [Keyflow-packages / 12032103506](https://github.com/lxmtuu/PianoPath/actions/runs/38867895710/artifacts/12032103506), 181,206,005 bytes. This is pre-merge evidence, not the final tagged release; final release asset hashes must be checked separately.
- Compile/package lane [38867895552](https://github.com/lxmtuu/PianoPath/actions/runs/38867895552): SUCCESS. No native claims are based solely on this lane.

The later changes between the full-source run and the release preflight concern release workflow/static-check reporting, not the application's C#/XAML/shaders/models. An earlier Windows test invocation failed without diagnostic test names; the new TRX annotations were added so subsequent failures are queryable. Do not describe that earlier failure as a diagnosed product defect or claim that a passing rerun explains its cause.

## Release status

Pre-merge gates are complete. This is a historical check snapshot, not a final publication certificate. The subsequent PR/merge and actual tagged assets are recorded on GitHub: [source release](https://github.com/lxmtuu/PianoPath/releases/tag/v2.1.0) and [public release](https://github.com/lxmtuu/KeyFlow/releases/tag/v2.1.0). The publishing run must pass its own tag/version, portable-test and published-executable gates; the final source/public assets must be compared after uploading.

## Limits

CI is not manual UI acceptance on every DPI/theme/display, real-camera/calibration or MIDI/audio-device certification, hardware GPU/FPS certification, or Windows ARM execution. ARM is cross-published, not hardware-tested. The software renderer keeps classic particle effects. The legacy half-rate camera-file policy remains; the export fix is not a universal A/V-sync claim. Builds are unsigned; users may see SmartScreen. Salamander is downloaded separately on request and is not bundled.
