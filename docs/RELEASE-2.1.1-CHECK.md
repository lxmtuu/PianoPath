# Keyflow 2.1.1 · Pre-merge publication correction

Date: 2026-10-16. This is a pre-merge evidence snapshot, not a final release certificate.

## Why 2.1.1

The full 2.1 feature set was merged by PR #67 at `828117f80c7a6e4bbf5b2993840112881213a8ac`. The 2.1.0 source release was mistakenly published before assets were uploaded. GitHub made it immutable, so it has no downloadable build. Its tag is not moved, deleted or reused. Immutability is not disabled. Version 2.1.1 is the first downloadable release of this feature set.

The correction changes version pins, release workflow/static guards and documents only; no application C#/XAML/shaders/models/presets change relative to the fully verified 2.1 feature set.

## Verification

- Full feature CI [38867237126](https://github.com/lxmtuu/PianoPath/actions/runs/38867237126): SUCCESS, including Linux tests, Windows native/WPF checks, art/evidence validators, frame budget, installer and refreshed previews. See the historical [2.1.0 check](RELEASE-2.1.0-CHECK.md).
- **2.1.1 release preflight [38875303681](https://github.com/lxmtuu/PianoPath/actions/runs/38875303681): SUCCESS**, source `607a4c5`. Actual 2.1.1 project version; all release packaging/native gates and isolated public-tree staging executed. No tag/release/public-repository write in preflight mode.
- **1,357 Windows tests passed**, zero failed/not executed; actual published self-contained x64 executable **7,911 assertions passed, one skipped check group**.
- Three portable ZIPs, x64 installer and SHA256 list produced. [Preflight artifact 12035855602](https://github.com/lxmtuu/PianoPath/actions/runs/38875303681/artifacts/12035855602): 181,195,270 bytes. Preflight assets are not the final immutable release assets.
- Compile lane [38875303695](https://github.com/lxmtuu/PianoPath/actions/runs/38875303695): SUCCESS; native claims come from the release preflight, not this lane.
- Final local source/migration/inventory/whitespace checks and Python test suite must remain green before merge.

## Correct publication contract

1. Merge the correction to main through a PR; do not push main directly.
2. Create `refs/tags/v2.1.1` through GitHub's Git refs API at the exact verified merge SHA. Do **not** publish an empty release as a trigger.
3. A single tag-creation event runs `release.yml`; branch pushes with the preflight marker still support private preflight packaging. Do not subscribe simultaneously to tag push and release published.
4. `softprops/action-gh-release@v3` uploads standard-release assets to a draft before publication. The public publisher explicitly uses `--draft`, uploads the complete files, then uses `--draft=false`. It refuses to overwrite an existing immutable public release.
5. After the actual tag run finishes, verify its portable tests/native suite, exact tag target, complete source/public asset names/sizes/SHA-256 digests, latest-release state and document-only public tree. Record the result in the correction PR/release; this pre-merge snapshot alone is not proof of publication.

Hardware/ARM/real-camera/audio/MIDI/DPI/manual-visual limitations and unsigned/SmartScreen guidance remain as in the 2.1 check. The software renderer remains classic and the legacy camera-file half-rate policy remains.

## Tagged-check follow-up

The initial unpublished 2.1.1 tag run `38876574673` correctly stopped before any source release was created: `OfflineFrameQueueTests.WriterFailureIsObservedAfterWakeup` timed out waiting for a thread-pool worker to start. Both queue handshake tests now use dedicated LongRunning workers, retaining all queue capacity/order, failure propagation, timeout and closed-recorder assertions. Production queue/encoder/application code is unchanged. The new tag will target the merged test-harness correction; no immutable or published tag is changed.
