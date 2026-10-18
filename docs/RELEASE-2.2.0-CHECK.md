# Keyflow 2.2.0 · Publication record

Date: 2026-10-18. Every number in this document was read back from GitHub's API after publication, not
taken from a preflight. The preflight that preceded the tag is recorded below as preparation evidence.

## Publication record (final)

Published and verified on 2026-10-18 after PR #75 ("Prepare Keyflow 2.2.0 release") was merged.

### Tag and the single release run

- `refs/tags/v2.2.0` resolves to `2c03fd095b3128a66b90d06934b6ef4e747cdc65`, which is PR #75's
  `merge_commit_sha` — the tag targets exactly the verified merge, not a later `main` commit. PR #75 was
  merged 12:10:49Z with a merge commit (head `062a158`, a `[skip ci]` evidence refresh on top of
  `e3b8d5f` "Prepare release 2.2.0").
- The tag-creation event ran `release.yml` once:
  [run 39126387869](https://github.com/lxmtuu/PianoPath/actions/runs/39126387869) (`create`, attempt 1,
  head `2c03fd0`): **SUCCESS**, 9 m 23 s (12:13:20Z → 12:22:43Z).
  - `Validate release tag`: passed against the 2.2.0 version pins.
  - `Test the tagged source`: **1,362 passed, 0 failed, 0 not executed** (2.1.1: 1,357).
  - `Smoke test the published executable`: **Verification passed: 7,918 assertions, 1 skipped check
    group(s)**.
  - `Hash the packages`: 4 packages hashed into `publish/SHA256SUMS.txt`.
  - `Attach packages to the GitHub Release`: 5 files (three ZIPs, installer, `SHA256SUMS.txt`) on the
    source release.
  - `Publish to the public release repository`: `release/public-v2.2.0` and `main` pushed to
    `lxmtuu/KeyFlow` as a document-only root commit, and the same 5 files attached to the public release
    (annotations: "published release/public-v2.2.0 and main to lxmtuu/KeyFlow", "5 package(s) attached to
    the v2.2.0 release on lxmtuu/KeyFlow").
- No empty release was published as a trigger. The source release's publish time (12:22:11Z) is the
  moment its packages were attached; the public release went through the draft → upload → publish
  sequence and reports 12:22:37Z. Both report `updated_at` equal to `published_at`, so nothing has been
  edited since publication.

### Assets — identical in both repositories

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| `Keyflow-2.2.0-win-arm64.zip` | 55,850,544 | `6a36b650338418f66046765722959dda5e65b55afab4ae3d8be566183ee50e24` |
| `Keyflow-2.2.0-win-x64-fd.zip` | 3,067,038 | `6964b9abecf2ce9739168d56b5093ee7f01cf3112e6fbef205f43d475d8fb9bc` |
| `Keyflow-2.2.0-win-x64.zip` | 71,317,580 | `eefb857ecc94afade357ab490c414c42f20590ed8bb61301356c838d5cbd52ce` |
| `Keyflow-Setup-2.2.0.exe` | 52,076,029 | `026cd546da9998c3993f0b3a89d60e61a660130666f66400080da602506f9a4a` |
| `SHA256SUMS.txt` | 375 | `6b26b7840b9e94007d2556cfc276483059b73c3e23271770e7abf097df1d0a1b` |

The five names, byte sizes and GitHub-recorded digests are the same in `lxmtuu/PianoPath` and
`lxmtuu/KeyFlow`, and the four package hashes are the ones the tagged run printed while hashing
(`::notice title=Package SHA256`). The list itself was checked against the assets: rebuilding
`SHA256SUMS.txt` from those four names and hashes (lowercase hash, two spaces, file name, CRLF, no BOM,
trailing newline, order arm64 → x64-fd → x64 → installer) reproduces 375 bytes hashing to `6b26b784…`,
the digest GitHub records for that file — so the list and the packages provably describe each other.

### Release state

- `releases/latest` is **v2.2.0 in both repositories**; neither release is a draft or a prerelease, and
  both are `immutable: true`, so their assets and notes are locked and a fix can only travel in a later
  version.
- v2.1.1 is untouched: `refs/tags/v2.1.1` is still `e4ea5e36e48babb7106885560b7409e951bb5701`; its
  immutable release keeps its assets and only the `latest` pointer moved. No tag was moved, deleted or
  reused and immutability was not disabled.
- Public tree: `main` = `release/public-v2.2.0` = `7375c20`, a root commit with 437 tree entries and no
  source history: six documents (`README.md`, `README.en.md`, `CHANGELOG.md`, `CHANGELOG.en.md`,
  `LICENSE`, `VERSION` = 2.2.0), `docs/LOCALIZATION.md`, the three-file slide deck
  (`docs/presentation/`) and 424 preview images (`docs/previews/`). No `*.cs`, `*.xaml`, `*.csproj`,
  `*.sln`, `*.iss`, `*.ps1`, `*.py`, `*.hlsl`, `*.sf2`, `*.yml`, `*.json` and no `.github/` (verified by
  walking the whole tree).

## Release preflight (before the tag)

- No packaging preflight existed for 2.2.0 when the release was prepared, so one ran first:
  [run 39123686734](https://github.com/lxmtuu/PianoPath/actions/runs/39123686734) (`push`, head
  `67f9d71`), **SUCCESS** in 7 m 52 s. Its tree was the exact merge GitHub later created for PR #75
  (`9d7a177` ⊕ `062a158` over `0be142f`), so the gates exercised the same source, version pins, installer
  and public-tree staging the tag would see. Results: 1,362 tests passed; smoke test 7,920 assertions, 1
  skipped check group; 4 packages hashed; artifact `Keyflow-packages` 181,281,766 B. The tag-only steps
  (`Validate release tag`, release notes links, `Attach packages…`, `Publish to the public…`) stayed
  skipped: no tag, no release and no public-repository write happened in preflight mode.
- `workflow_dispatch` was not available to the preparing session (the GitHub integration token lacks
  `actions:write` — HTTP 403), so the preflight used the documented `[release-preflight]` marker push on
  a session branch instead.
- Operational note for the next release: a *first* push creating a branch fires both `push` and `create`
  events, and both runs share the `release-<ref>` concurrency group with `cancel-in-progress` for
  non-tags — the `create` run (job skipped, branch ≠ tag) cancelled the preflight run
  ([39123560767](https://github.com/lxmtuu/PianoPath/actions/runs/39123560767) cancelled by
  [39123561318](https://github.com/lxmtuu/PianoPath/actions/runs/39123561318), skipped). The rerun API was
  also 403 for the session, so the preflight was retried with a second marker commit, which ran clean.
  Tag creation fires only `create` (the workflow's `push` trigger listens to `arena/**` branches only), so
  the tagged run never sees this race.

## CI after the merge

- `build` [run 39126217798](https://github.com/lxmtuu/PianoPath/actions/runs/39126217798) on the PR #75
  merge commit `2c03fd0`: at record time `static`, `test` and `keyboard-light` passed and the `build` job
  was still rendering previews; `ar-budget`, `app-build` and `key-ensemble` were skipped (each needs its
  marker in the commit message or a manual dispatch). Release evidence does not depend on this run: the
  packages, the sums and the public documents came from tagged run `39126387869`.

## Release notes body — the 2.1.1 cosmetic issue persists

- The assembled body again splits the checksum instruction across three lines —
  `` `Get-FileHash .\Keyflow- `` / `2.2.0` / `` -win-x64.zip -Algorithm SHA256` `` — instead of one code
  span. Everything else in the body matches the notes template (the changelog links point at
  `release/public-v2.2.0` in the public repository, as intended). Both releases are immutable and were
  never edited, so this cannot be corrected in place — it is cosmetic, it moves no asset and no digest.
  The notes template in `release.yml` still produces the split around the version; worth fixing there
  before the next cut.

## Open items and limits

- Manual acceptance is still outstanding and cannot be done from CI or a sandbox: the
  [private-source/public-release manual](PRIVATE-SOURCE-PUBLIC-RELEASES.md) §4 asks for a clean Windows
  machine run of `PianoPath.exe --verify --verify-log=%TEMP%\keyflow-verify.log` against an installed or
  unzipped package (exit code 0). CI verified the published executable, not a real installation on a
  third-party machine.
- Smoke-test assertion counts vary slightly between runs: preflight 7,920 vs the tagged run 7,918 (both
  passed with one skipped check group each). The exact 2-assertion difference is not established from the
  published evidence — the same pattern 2.1.1 recorded (7,911 → 7,909).
- AR bench notes vary with runner load (preflight: mean 6.91 ms, p95 13.77 ms; tagged run: mean 10.06 ms,
  p95 19.96 ms over 24 frames of 320×180). Both stayed NOTE-level and no budget gate ran (`ar-budget` is
  marker-gated).
- The preparing sandbox could not download run-log archives (the results-receiver host is outside its
  network allow-list) and could not use `workflow_dispatch` or rerun (token without `actions:write`).
  Every number above comes from the checks/annotations APIs and the release APIs; the `SHA256SUMS.txt`
  proof was made by reconstruction rather than by downloading the asset.

## What 2.2.0 contains

Feature summary and user-visible changes live in the release entry `## 2.2.0 — 2026-10-18` of both
changelogs (`CHANGELOG.en.md` / `CHANGELOG.md`), centered on the AR overlay work. The version is pinned in
one place (`<Version>` in `PianoPath.csproj`) and the static guards kept every copy aligned through the
release PR.
