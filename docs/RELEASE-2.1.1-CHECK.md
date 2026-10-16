# Keyflow 2.1.1 · Pre-merge publication correction

Date: 2026-10-16. The sections below are the pre-merge evidence snapshot; the **Publication record (final)** section is the post-publication verification the publication contract's step 5 asks for.

## Publication record (final)

Published and verified on 2026-10-16 after PR #69 was merged. Every number in this section was read back from GitHub's API after publication, not taken from a preflight.

### Tag and the single release run

- `refs/tags/v2.1.1` resolves to `e4ea5e36e48babb7106885560b7409e951bb5701`, which is PR #69's `merge_commit_sha` — the tag targets exactly the verified merge, not a later `main` commit.
- The tag-creation event ran `release.yml` once: [run 38878150599](https://github.com/lxmtuu/PianoPath/actions/runs/38878150599) (`create`, attempt 1, head `e4ea5e36`): **SUCCESS**.
  - `Test the tagged source`: **1,357 passed, 0 failed, 0 not executed**.
  - `Smoke test the published executable`: **Verification passed: 7,909 assertions, 1 skipped check group(s)**.
  - `Hash the packages`: 4 packages hashed into `publish/SHA256SUMS.txt`.
  - `Attach packages to the GitHub Release`: 5 files (three ZIPs, installer, `SHA256SUMS.txt`) on the source release.
  - `Publish to the public release repository`: `release/public-v2.1.1` and `main` pushed to `lxmtuu/KeyFlow` as a document-only root commit, and the same 5 files attached to the public release.
- No empty release was published as a trigger. The source release's publish time (09:15:19Z) is the moment its packages were attached; the public release was created as a draft at 09:15:32Z and published at 09:15:53Z. Both report `updated_at` equal to `published_at`, so nothing has been edited since publication.

### Assets — identical in both repositories

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| `Keyflow-2.1.1-win-arm64.zip` | 55,851,550 | `b36a3dec2b2cb2cc848cf1de2152589ee395142f0967d10c87433993f3162739` |
| `Keyflow-2.1.1-win-x64-fd.zip` | 3,066,245 | `abc67e0d6a45d9e7427bf82e44f775556ec74b8108b730087d3af20ec01c3dc5` |
| `Keyflow-2.1.1-win-x64.zip` | 71,271,587 | `d7a71f8d1dfcad103f7f966c561379ba18f9e95dcce6979ae43886f7378ff12d` |
| `Keyflow-Setup-2.1.1.exe` | 52,041,398 | `e5d55a7b9b64b298466dbb13f414da3ca86e3b87c38351e51e25b30f2d875f83` |
| `SHA256SUMS.txt` | 375 | `c23f49907fcd276c4478e542984427f5ed0034e78d704533c2be353d6f904ae0` |

The five names, byte sizes and GitHub-recorded digests are the same in `lxmtuu/PianoPath` and `lxmtuu/KeyFlow`, and the four package hashes are the ones the tagged run printed while hashing (`::notice title=Package SHA256`). The list itself was checked against the assets: rebuilding `SHA256SUMS.txt` from those four names and hashes (lowercase hash, two spaces, file name, CRLF, no BOM, trailing newline) reproduces 375 bytes hashing to `c23f4990…`, the digest GitHub records for that file — so the list and the packages provably describe each other.

### Release state

- `releases/latest` is **v2.1.1 in both repositories**; neither release is a draft or a prerelease, and both are `immutable: true`, so their assets and notes are locked and a fix can only travel in a later version.
- v2.1.0 is untouched: `refs/tags/v2.1.0` is still `828117f80c7a6e4bbf5b2993840112881213a8ac` (PR #67's merge), the source release is still a prerelease with zero assets and is not `latest`, and there is no v2.1.0 release in the public repository. No tag was moved, deleted or reused and immutability was not disabled.
- Public tree: `main` = `release/public-v2.1.1` = `f79c9a70cb2111892c409f475ecafcf24f9caf4b`, a root commit with 404 files and no source history: six documents (`README.md`, `README.en.md`, `CHANGELOG.md`, `CHANGELOG.en.md`, `LICENSE`, `VERSION` = 2.1.1), `docs/LOCALIZATION.md`, the three-file slide deck and 394 preview images. No `*.cs`, `*.xaml`, `*.csproj`, `*.sln`, `*.iss`, `*.ps1`, `*.py`, `*.hlsl`, `*.sf2`, `*.yml`, `*.json` and no `.github/`. Every blob is byte-identical to the document tree generated from the tag (`docs/release/*` for the root files, `docs/previews/*`, `docs/presentation/*`, `docs/LOCALIZATION.md`).

### CI after the merge

- `build` [run 38878121563](https://github.com/lxmtuu/PianoPath/actions/runs/38878121563) on the PR #69 merge commit: **failed**, 1 h 1 m 10 s (09:05:59Z → 10:07:09Z), with exactly one failing step — `Commit refreshed previews` in the `build` job (56 m 52 s, check run `116899579047`). `static` (28 s), `test` (52 s) and `keyboard-light` (2 m 18 s) passed; `ar-budget`, `app-build` and `key-ensemble` were skipped (each needs its marker in the commit message or a manual dispatch), and everything after the failed step, including `Publish a distributable build`, was skipped too. The five artifacts are intact (`keyflow-previews` 138,407,108 B, `phase1-visual-review` 1,466,205,382 B, `keyboard-key-light-native` 21,150,427 B, `keyflow-verification-log` 16,835 B, `keyflow-bench-report` 2,028 B). Release evidence does not depend on this run: the packages, the sums and the public documents came from tagged run `38878150599`.
- That failing step was the preview commit, and it lost a race that `main` cannot cancel. The run checked out `e4ea5e36` at 09:05:59Z; an earlier run of `main` was still rendering, rebased its refresh onto `e4ea5e36` and pushed the preview commit `0ff9d1bb` at 09:08:22Z; when this run finished rendering 57 minutes later its `git pull --rebase` had to replay the same `docs/evidence` manifests and `docs/previews` screenshots — files every run rewrites, with renders that are not byte-stable between runners — and stopped there. The step has been reworked on `arena/d9f17f72-pianopath`: a branch that moved while a build rendered now gets an `::warning` naming both commits, keeps the screenshots that belong to its newer tip, and stops the drift report from calling the branch stale (`landed=superseded` → an `::notice` instead). Conflicting pulls, replays and overwrites are gone; the GH013 answer for a pull-request-only branch and the red step for a real push failure (network, authentication) are unchanged. The race and the four outcomes — unchanged tip → commit and push, moved tip → warning, GH013 → warning, moved between fetch and push → non-fast-forward handled the same way — were reproduced in a throwaway git sandbox; no CI run has exercised the rework yet, and the next push to `main` is the first real test.
- `Report preview drift` after it is warning-only by design, so the missing refresh surfaces as an annotation rather than as a second failure.
- The last green `main` build remains [run 38873882220](https://github.com/lxmtuu/PianoPath/actions/runs/38873882220) (`828117f8`, PR #67's merge); the run for PR #68's merge `6983effc` was cancelled before it finished, and `38878121563` is the failure above.
- The only commit on `main` after the tag is the CI preview refresh `0ff9d1bbf4c8770b85b58f5ee872fc3170100b66`, which changes `docs/evidence` manifests and one `docs/previews` PNG and no source file, so tag and `main` differ in documents only.
- The `[release-preflight]` runs that preceded the tag (`38876959998` at `eb8860c`, `38876960073`) published nothing to the public repository: their check-run annotations carry no public-release notice, only packaging/artifact and test notices.

### Open items and limits

- Manual acceptance is still outstanding and cannot be done from CI or a sandbox: the [private-source/public-release manual](PRIVATE-SOURCE-PUBLIC-RELEASES.md) §4 asks for a clean Windows machine run of `PianoPath.exe --verify --verify-log=%TEMP%\keyflow-verify.log` against an installed or unzipped package (exit code 0). CI verified the published executable, not a real installation on a third-party machine.
- The public release notes render their checksum instruction as `` `Get-FileHash .\Keyflow- `` / `2.1.1` / `` -win-x64.zip -Algorithm SHA256` `` on three lines instead of one code span. Everything else in that body matches `release.yml`'s notes template. The release is immutable and was never edited (`updated_at == published_at`), so this cannot be corrected in place — it is cosmetic, it moves no asset or digest, and the assembled body is worth reading back when the next release is cut.
- The native count is 7,909 assertions in both final runs (the branch preflight at `eb8860c` and the tagged run); the earlier preflights reported 7,911. Both final runs passed with one skipped conditional check group each and no group was lost, so this is not a partial verification; the exact 2-assertion difference is not established from the published evidence.

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

The dedicated-worker correction passed release preflight [38876959998](https://github.com/lxmtuu/PianoPath/actions/runs/38876959998) at `eb8860c`: Windows portable tests and actual published-executable native gates, public staging, three ZIPs/installer/SHA256 all SUCCESS. Compile lane `38876960073` also SUCCESS. No production queue/encoder change or deadline/assertion removal.
