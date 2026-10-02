# Private source, public releases · Mã nguồn riêng tư, bản phát hành công khai

## Mô hình / Model

- **Nguồn riêng tư / Private source:** `lxmtuu/PianoPath` keeps the application source, CI, issues, and development history private. Only authorized collaborators can clone it.
- **Phát hành công khai / Public releases:** `lxmtuu/PianoPath-Releases` is the public download page. It contains release tags/notes and compiled packages only — never a mirror of the source tree or its commit history.
- **Luồng phát hành / Release flow:** a `vX.Y.Z` tag in the private source builds and verifies the packages, then a separate, isolated job uploads them to the public repository. The public release tag points to that repository's small default-branch README, not to a private commit. GitHub-generated notes are disabled so private PR titles, commit messages, and links cannot leak.

Public download URL: <https://github.com/lxmtuu/PianoPath-Releases/releases/latest>.

## Thiết lập một lần / One-time setup

1. Create `lxmtuu/PianoPath-Releases` as a **public** GitHub repository. Initialize its default branch with a short README (do not use the private source as its template, and do not push the source tree to it). The release workflow checks that the target is public and initialized.
2. In the **private** `lxmtuu/PianoPath` repository, open **Settings → Secrets and variables → Actions → Variables → New repository variable**:
   - Name: `PUBLIC_RELEASES_REPOSITORY`
   - Value: `lxmtuu/PianoPath-Releases`
3. Create a fine-grained personal access token restricted to `lxmtuu/PianoPath-Releases`, with **Contents: Read and write** (Metadata read access is required by GitHub). In the private source repository, open **Settings → Secrets and variables → Actions → Secrets → New repository secret**:
   - Name: `PUBLIC_RELEASES_TOKEN`
   - Value: the token (never commit it or put it in a workflow file).
4. Keep the source repository's default `GITHUB_TOKEN` permission read-only for release publishing. The `package` job uses only that read access. The cross-repository token is exposed only to the small `publish-public-release` job, after package building is finished.

The release job fails early with a clear message if the variable/secret is absent, the token cannot read the target, or the target is private, archived, or has no default branch. The token must be allowed to create releases and upload assets in the public target repository.

## Phát hành / Releasing

### Migrate the current release once

The private source already has tag `v1.0.0` and a private release. After setting up the public target and secret, open **Actions → release → Run workflow**, select the private source's default branch, and enter `v1.0.0` in `release_tag`. The workflow checks out that exact private tag, rebuilds its packages, and creates a separate public `v1.0.0` release. The old private release remains private. This manual path is also useful for re-publishing an existing tag.

### Future versions

1. Bump `<Version>` in `PianoPath.csproj` and update the newest bilingual changelog entry and README examples. `tools/check_sources.py` checks that these versions agree.
2. From the matching source commit, create and push the tag:

   ```powershell
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

3. The `release` workflow builds the three ZIP packages, the x64 installer, and `SHA256SUMS.txt`. Afterward, its isolated release job creates the same-version release under `PianoPath-Releases` and attaches only those files plus bilingual notes copied from the versioned changelog entry.

A manual workflow run with `release_tag` blank remains build-only and exposes packages as a temporary Actions artifact. `release_tag` must match both `vMAJOR.MINOR.PATCH` and `<Version>` in the checked-out source tag.

## Quyền riêng tư và giấy phép / Privacy and licensing

- No source files, private Git tags, private commit history, workflow logs, or generated notes from private PRs are copied to the public repository.
- Each package already carries `LICENSE.txt` (MIT) and `Assets/ATTRIBUTION.txt` for FreePats' YDP Grand Piano (CC BY 3.0). Keep both in every public distribution.
- Release assets are not code-signed. `SHA256SUMS.txt` checks that a downloaded file matches the uploaded release asset; it is not a code signature.
- The current `lxmtuu/PianoPath` repository is already private. This workflow does not change repository visibility; it adds a separate public distribution surface.
