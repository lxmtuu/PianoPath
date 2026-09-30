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
    # Folder the script's Source: patterns resolve against (installer\Keyflow.iss defaults to
    # ..\publish\win-x64, relative to the script).
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

if ($Stub) {
    New-Item -ItemType Directory -Force -Path (Join-Path $SourceDir 'Assets') | Out-Null
    foreach ($file in 'PianoPath.exe', 'Assets/ConcertGrand.sf2', 'Assets/ATTRIBUTION.txt') {
        Set-Content -Path (Join-Path $SourceDir $file) -Value 'stub for the installer check'
    }
}
if (-not (Test-Path $SourceDir)) { throw "$SourceDir does not exist — run .\publish.ps1 first" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$arguments = @("/O$OutputDir")
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

$setup = Get-ChildItem -Path $OutputDir -Filter 'Keyflow-Setup-*.exe' | Select-Object -First 1
if (-not $setup) { throw "ISCC reported success but wrote no Keyflow-Setup-*.exe into $OutputDir" }
Write-Host "built $($setup.Name) ($([math]::Round($setup.Length / 1KB)) KB); $($warnings.Count) message(s) keep the English Default.isl text of the partial Vietnamese translation"
$setup.FullName
