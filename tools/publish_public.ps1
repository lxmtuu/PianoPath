<#
.SYNOPSIS
    Publishes one release's documents (and optionally its packages) to the public release repository.

.DESCRIPTION
    The source repository is private; the packages and their documentation are published to a public
    repository. release.yml does that automatically when it holds a token, and this script is the same
    step by hand — for a maintainer whose `gh` is already logged in and who would rather not keep a
    second token in the repository's secrets.

        pwsh tools/publish_public.ps1 -Tag v1.0.0                       # documents only
        pwsh tools/publish_public.ps1 -Tag v1.0.0 -Packages publish     # + the packages of that release
        pwsh tools/publish_public.ps1 -Tag v1.0.0 -DryRun               # print what it would do

    It builds the branch from the tag, not from the working tree: `git worktree` checks out exactly the
    tagged commit, the generated documents under `docs/release/` are copied in (with `docs/previews/`,
    which CI renders), the commit is created inside that worktree, and only then is it pushed to the
    public repository. Nothing is committed in the source repository, and no credential is stored
    anywhere: `gh auth setup-git` hands git the token `gh` already holds.

    The commit is created as a root commit, so the public repository carries the files that are meant to
    be public and none of the source repository's history. Re-publishing a tag therefore replaces the
    branch rather than adding to it — on purpose, since the branch is generated content and the public
    README says a hand edit there is overwritten.

.PARAMETER Tag
    The release tag to publish, e.g. v1.0.0. It must exist in this repository and already be pushed.

.PARAMETER Repository
    The public release repository, owner/name.

.PARAMETER Branch
    The branch to write in the public repository. Defaults to release/public-<tag>.

.PARAMETER Packages
    A folder holding the packages of this release (Keyflow-*.zip, Keyflow-Setup-*.exe, SHA256SUMS.txt).
    When given, they are attached to a GitHub Release of the same tag in the public repository — created
    if it does not exist, appended to if it does.

.PARAMETER Remote
    The remote in this repository whose tags are checked, default `origin`.

.PARAMETER UpdateMain
    Also write the branch to `main` of the public repository. Normally main is only moved when this tag is
    the newest release of this repository (the front page should show the current page, and re-publishing
    an old tag must not push it backwards); this forces it.

.EXAMPLE
    pwsh tools/publish_public.ps1 -Tag v1.0.0 -Packages publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [string] $Repository = 'lxmtuu/PianoPath-Releases',

    [string] $Branch,

    [string] $Packages,

    [string] $Remote = 'origin',

    [switch] $UpdateMain,

    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Native commands report failure through $LASTEXITCODE. PowerShell 7.3 and later can be told to turn a
# native command's non-zero exit into a terminating error instead, and a shell that has that switch on
# would kill this script at the first `gh auth status` that is merely reporting information.
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Write-Step([string] $text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not (Test-Path (Join-Path $root 'PianoPath.csproj'))) {
    throw "Run this from a checkout of the source repository: $root does not hold PianoPath.csproj"
}
if (-not $Branch) { $Branch = "release/public-$Tag" }

# ------------------------------------------------------------------------------------------------
# 1. Tools, tag and version
# ------------------------------------------------------------------------------------------------
Write-Step 'Checking git and the GitHub CLI'
$gh = Get-Command gh -ErrorAction SilentlyContinue
if (-not $gh) { throw 'the GitHub CLI (gh) was not found: https://cli.github.com' }
& gh auth status 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'gh is not logged in; run `gh auth login` first' }

Write-Step "Checking the tag $Tag"
Push-Location $root
try {
    $commit = (& git rev-parse --verify "refs/tags/$Tag^{commit}" 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "tag $Tag does not exist in this repository" }
    $commit = "$commit".Trim()
    $identityLine = "$(& git log -1 --format='%an <%ae>' $commit)".Trim()
    $name, $email = 'Keyflow release', 'actions@users.noreply.github.com'
    if ($identityLine -match '^(.*) <([^>]*)>$') { $name = $Matches[1].Trim(); $email = $Matches[2].Trim() }
    $pushed = (& git ls-remote --tags $Remote "refs/tags/$Tag")
    if (-not $pushed) { throw "tag $Tag is not on $Remote yet; push it first (git push $Remote $Tag)" }

    $versionNode = Select-Xml -Path (Join-Path $root 'PianoPath.csproj') -XPath '/Project/PropertyGroup/Version' | Select-Object -First 1
    $version = if ($versionNode) { $versionNode.Node.InnerText.Trim() } else { '' }
    if (-not $version) { throw 'PianoPath.csproj has no <Version> to check the tag against' }
    if ($Tag -ne "v$version") {
        Write-Host "note: tag $Tag does not look like v$version (the <Version> in PianoPath.csproj)" -ForegroundColor Yellow
    }
    Write-Host "tag $Tag -> $commit (Keyflow $version, committed as $name <$email>)"
}
finally { Pop-Location }

# ------------------------------------------------------------------------------------------------
# 2. Staging worktree: the tagged tree, plus the generated documents
# ------------------------------------------------------------------------------------------------
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("keyflow-public-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
Write-Step "Checking out $Tag into $staging"
& git -C $root worktree add --detach $staging $commit | Out-Host
if ($LASTEXITCODE -ne 0) { throw "git worktree add failed for $commit" }

try {
    $source = Join-Path $root 'docs\release'
    if (-not (Test-Path $source)) {
        throw 'docs/release is missing; run `python3 tools/make_public_docs.py` and commit the result first'
    }

    # The branch holds the documents and nothing else. The tree is emptied first so a file that used to be
    # generated and no longer is disappears, instead of lingering on the public branch for ever.
    Write-Step 'Building the release branch content'
    Get-ChildItem -Path $staging -Force |
        Where-Object { $_.Name -ne '.git' } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
    Copy-Item -Path (Join-Path $source '*') -Destination $staging -Recurse -Force
    $previews = Join-Path $root 'docs\previews'
    if (Test-Path $previews) {
        Copy-Item -Path $previews -Destination (Join-Path $staging 'docs\previews') -Recurse -Force
        Write-Host ('previews  : {0} file(s)' -f (Get-ChildItem (Join-Path $staging 'docs\previews') -Recurse -File).Count)
    } else {
        Write-Warning 'docs/previews is missing, so the README pictures will not resolve on the public page'
    }

    # Nothing that is source code may travel: the public repository is public, and a source file landing
    # there is an unintended publication. The same list is asserted by tools/check_sources.py.
    $forbidden = @('.cs', '.xaml', '.csproj', '.sln', '.iss', '.ps1', '.py', '.hlsl', '.sf2', '.yml')
    $sourceFiles = @(Get-ChildItem $staging -Recurse -File -Force | Where-Object { $forbidden -contains $_.Extension })
    if ($sourceFiles.Count -gt 0) {
        throw ('the staged branch would publish source file(s): ' +
               (($sourceFiles | ForEach-Object { $_.FullName }) -join ', '))
    }

    Write-Step 'Creating the commit'
    & git -C $staging add -A | Out-Host
    $tree = "$(& git -C $staging write-tree)".Trim()
    if ($LASTEXITCODE -ne 0 -or -not $tree) { throw 'git write-tree failed' }
    # No -p parent: the public branch carries this release's files and none of the private history.
    $message = "Keyflow $version ($Tag) — release documents"
    # Quoting kept out of the string: a nested double quote inside `$( )` inside a double-quoted string is
    # exactly the kind of thing that parses on one PowerShell and not on another.
    $newCommit = & git -C $staging -c "user.name=$name" -c "user.email=$email" commit-tree $tree -m $message
    $newCommit = "$newCommit".Trim()
    if ($LASTEXITCODE -ne 0 -or -not $newCommit) { throw 'git commit-tree failed' }
    Write-Host "commit    : $newCommit"
    Get-ChildItem $staging -File | ForEach-Object { Write-Host ("            {0}" -f $_.Name) }

    # The repository this checkout belongs to: the one repository whose main this script must never write.
    $sourceSlug = ''
    $origin = "$(& git -C $root remote get-url $Remote)".Trim()
    if ($origin) { $sourceSlug = ($origin -replace '^.*github\.com[:/]', '') -replace '\.git$', '' }

    # main carries the newest release, so the public repository's front page shows the current page. An
    # older tag re-published (a fix to its notes, say) must not push it backwards, so this asks which
    # release is the newest — a question this repository answers only to the maintainer's gh, which is the
    # login this script publishes with anyway.
    $updateMain = $UpdateMain.IsPresent
    if (-not $updateMain -and $sourceSlug) {
        $latestOutput = & gh api "repos/$sourceSlug/releases/latest" --jq .tag_name 2>$null
        if ($LASTEXITCODE -eq 0 -and $latestOutput) { $updateMain = ("$latestOutput".Trim() -eq $Tag) }
    }

    # A repository may only have its main replaced by documents if it is a repository that exists to hold
    # documents; this one holds the product. On 2026-10-03 this script was pointed at the source repository
    # (-Repository lxmtuu/PianoPath) and did exactly that: main spent half an hour holding docs/release and
    # the sources had to be restored from a branch. Hence the refusals below. tools/check_sources.py fails
    # the static check if the first one is edited away.
    if ($sourceSlug -and $Repository -ieq $sourceSlug) {
        if ($updateMain) {
            throw (@(
                "refusing to push the release documents to refs/heads/main of $Repository: that is the source"
                'repository, whose main is the product and not the documentation. Publish to the public release'
                'repository instead (docs/PRIVATE-SOURCE-PUBLIC-RELEASES.md), or drop -UpdateMain to write only'
                "the $Branch branch."
            ) -join ' ')
        }
        if ($Branch -in @('main', 'master')) {
            throw "refusing to write the release documents to $Branch of the source repository $Repository"
        }
        Write-Warning ("the target repository is this one ($Repository): the documents go to the $Branch " +
                       'branch, and its main is never written.')
    }

    if ($DryRun) {
        Write-Step 'Dry run: nothing was pushed'
        Write-Host ("would push {0} to https://github.com/{1}.git HEAD:refs/heads/{2}" -f $newCommit, $Repository, $Branch)
        if ($updateMain) { Write-Host ("would also push it to refs/heads/main of {0}" -f $Repository) }
        return
    }

    Write-Step "Pushing to https://github.com/$Repository ($Branch$(if ($updateMain) { ' and main' }))"
    & gh auth setup-git | Out-Host
    # The branch is generated content, so re-publishing the same tag replaces it: hence the force.
    & git -C $staging push --force "https://github.com/$Repository.git" "${newCommit}:refs/heads/$Branch" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "the push to $Repository failed (does the repository exist, and does gh hold write access to it?)"
    }
    if ($updateMain) {
        & git -C $staging push --force "https://github.com/$Repository.git" "${newCommit}:refs/heads/main" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "the push of main to $Repository failed" }
    }

    # ------------------------------------------------------------------------------------------------
    # 3. Optional: attach the packages of this release to a GitHub Release in the public repository
    # ------------------------------------------------------------------------------------------------
    if ($Packages) {
        if (-not (Test-Path $Packages)) { throw "the -Packages folder '$Packages' does not exist" }
        $files = @(Get-ChildItem $Packages -File | Where-Object {
            $_.Name -like 'Keyflow-*.zip' -or $_.Name -like 'Keyflow-Setup-*.exe' -or $_.Name -eq 'SHA256SUMS.txt'
        })
        if ($files.Count -eq 0) { throw "no Keyflow-*.zip, Keyflow-Setup-*.exe or SHA256SUMS.txt found in '$Packages'" }
        $paths = @($files | ForEach-Object { $_.FullName })

        Write-Step "Attaching $($files.Count) file(s) to the $Tag release on $Repository"
        & gh release view $Tag --repo $Repository 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & gh release upload $Tag @paths --repo $Repository --clobber | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'gh release upload failed' }
        } else {
            $notes = @(
                "**Keyflow $version** for Windows 10/11 (64-bit). Pick one package: the installer, or a ZIP",
                "you unzip and run `PianoPath.exe` from. Keep `Assets\` next to the .exe, and keep",
                "`LICENSE.txt` and `Assets\ATTRIBUTION.txt` that ship inside every package.",
                '',
                'The packages are **not code-signed**, so SmartScreen shows "Windows protected your PC" on',
                'the first launch — choose More info → Run anyway. `SHA256SUMS.txt` covers every file above.',
                '',
                "Documentation: https://github.com/$Repository/blob/$Branch/README.en.md · " +
                "https://github.com/$Repository/blob/$Branch/README.md",
                "What changed: https://github.com/$Repository/blob/$Branch/CHANGELOG.en.md"
            ) -join "`n"
            & gh release create $Tag @paths --repo $Repository --title "Keyflow $version" --notes $notes | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'gh release create failed' }
        }
    }

    Write-Step 'Done'
    Write-Host ("public page : https://github.com/{0}/tree/{1}" -f $Repository, $Branch)
    Write-Host ("release page: https://github.com/{0}/releases/tag/{1}" -f $Repository, $Tag)
}
finally {
    Write-Step 'Removing the staging checkout'
    & git -C $root worktree remove --force $staging 2>$null | Out-Host
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
