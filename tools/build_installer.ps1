#!/usr/bin/env pwsh
<#
.SYNOPSIS
Compiles installer\Keyflow.iss with the Inno Setup compiler this machine has.

.DESCRIPTION
Used by both workflows and by anyone cutting a release by hand:

    pwsh tools/build_installer.ps1                 # the real publish\win-x64 build
    pwsh tools/build_installer.ps1 -Stub           # a stub folder — what build.yml runs every push

Inno Setup is deliberately forgiving about wizard text: a message name it does not recognize is only
a *warning* (the line is dropped and the English wording is shipped), and so is a message a partial
translation leaves out. tools/check_sources.py is what keeps installer\Languages\Vietnamese.isl
honest, and this script holds the compiler to the same promise — every warning that is not the
documented "falls back to Default.isl" notice of the partial Vietnamese translation fails the build.

The compiler also only checks that every Source: file exists, which is why -Stub can compile the
installer on every push without the 113 MiB SoundFont.
#>
[CmdletBinding()]
param(
    # Folder the script's Source: patterns resolve against, as typed from the current directory (the
    # default matches installer\Keyflow.iss's own ..\publish\win-x64 when this runs from the repo root).
    # It is passed to the compiler as an absolute /DSourceDir, so the folder that is checked is the
    # folder that is compiled.
    [string]$SourceDir = 'publish/win-x64',

    # Where ISCC writes Keyflow-Setup-<version>.exe. Defaults to a temporary folder so a check never
    # leaves files in the checkout; pass installer\Output to keep the installer.
    [string]$OutputDir = (Join-Path ([IO.Path]::GetTempPath()) 'keyflow-installer'),

    # Value for the script's /DAppVersion, which also names the output file. Empty = the fallback in
    # installer\Keyflow.iss (keep it in sync with <Version> in PianoPath.csproj).
    [string]$Version = '',

    # Write placeholder files into $SourceDir so the installer can be compiled before the application
    # is published.
    [switch]$Stub
)

$ErrorActionPreference = 'Stop'

# Chocolatey refuses to replace an installed package with an older one, so a pinned version cannot be
# installed over the Inno Setup the Windows images already ship. Use the preinstalled compiler when
# there is one and only then fall back to Chocolatey.
$candidates = @(
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 7\ISCC.exe'
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host 'ISCC.exe is not installed; asking Chocolatey for Inno Setup'
    choco install innosetup -y --no-progress
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'ISCC.exe was not found and Chocolatey did not install one' }

$compiler = Split-Path -Parent $iscc
if (-not (Test-Path (Join-Path $compiler 'ISPP.dll'))) {
    throw "$compiler has no ISPP.dll next to it, so the script's #ifndef directives cannot be processed (install the full Inno Setup package)"
}
Write-Host "compiling with $iscc"

# The Source: patterns in installer\Keyflow.iss resolve against the folder the script lives in, while this
# parameter is whatever the caller typed — so it is resolved once, here, and handed to the compiler as an
# absolute /DSourceDir: the folder that is checked for files, filled with stubs and compiled is then the
# same one, whichever way the caller named it. (Without that pass-through the compiler would quietly build
# the default ..\publish\win-x64 while the checks ran somewhere else.)
$sourceFull = [IO.Path]::GetFullPath($SourceDir)

if ($Stub) {
    New-Item -ItemType Directory -Force -Path (Join-Path $sourceFull 'Assets') | Out-Null
    foreach ($file in 'PianoPath.exe', 'Assets/ConcertGrand.sf2', 'Assets/ATTRIBUTION.txt') {
        Set-Content -Path (Join-Path $sourceFull $file) -Value 'stub for the installer check'
    }
}
if (-not (Test-Path $sourceFull)) { throw "$sourceFull does not exist — run .\publish.ps1 first" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
# What the output folder already holds, so the check below can tell a freshly written installer from one
# an earlier run left behind (a release folder is not emptied between builds).
$before = @{}
Get-ChildItem -Path $OutputDir -Filter 'Keyflow-Setup-*.exe' -ErrorAction SilentlyContinue |
    ForEach-Object { $before[$_.Name] = $_.LastWriteTimeUtc }

$arguments = @("/O$OutputDir", "/DSourceDir=$sourceFull")
if ($Version) { $arguments += "/DAppVersion=$Version" }
$arguments += 'installer\Keyflow.iss'

$output = & $iscc @arguments 2>&1
$code = $LASTEXITCODE
$output | ForEach-Object { "$_" }
if ($code -ne 0) { throw "ISCC failed with exit code $code" }

# The Vietnamese file is partial on purpose: the rest of the wizard falls back to Default.isl and
# Inno Setup says so, once per message. Anything else the compiler warns about was ignored, which is
# exactly how a typo would turn into English text in a Vietnamese dialog.
$expected = 'has not been defined for the "vietnamese" language'
$warnings = @($output | Where-Object { "$_" -match '^Warning: ' })
$unexpected = @($warnings | Where-Object { "$_" -notmatch [regex]::Escape($expected) })
foreach ($warning in $unexpected) { Write-Host "::error title=Inno Setup warning::$warning" }
if ($unexpected.Count) { throw "$($unexpected.Count) unexpected Inno Setup warning(s)" }

$setup = Get-ChildItem -Path $OutputDir -Filter 'Keyflow-Setup-*.exe' |
    Where-Object { -not $before.ContainsKey($_.Name) -or $before[$_.Name] -ne $_.LastWriteTimeUtc } |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $setup) { throw "ISCC reported success but wrote no new Keyflow-Setup-*.exe into $OutputDir" }
Write-Host "built $($setup.Name) ($([math]::Round($setup.Length / 1KB)) KB); $($warnings.Count) message(s) keep the English Default.isl text of the partial Vietnamese translation"
$setup.FullName
