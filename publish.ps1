<#
.SYNOPSIS
    Builds a distributable Keyflow (PianoPath.exe) with `dotnet publish`.

.DESCRIPTION
    Wraps the publish commands documented in README.md so a release is one command:

        .\publish.ps1                         # self-contained single-file, win-x64, .\publish\win-x64
        .\publish.ps1 -Mode FrameworkDependent # small build, needs the .NET 10 Desktop Runtime on the target PC
        .\publish.ps1 -Runtime win-arm64      # Windows on ARM
        .\publish.ps1 -Zip                    # also creates Keyflow-<version>-<runtime>[-fd].zip
        .\publish.ps1 -Clean                  # deletes bin/, obj/ and the target folder first

    The script refuses to publish while Assets\ConcertGrand.sf2 is still a Git LFS pointer
    (a 134-byte text file) unless -AllowLfsPointer is given, because the resulting build
    would start without its piano sound.

.NOTES
    Requires the .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.
    Trimming (-p:PublishTrimmed) and Native AOT are not supported by WPF and are intentionally not used.
#>
[CmdletBinding()]
param(
    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string] $Mode = 'SelfContained',

    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [string] $Configuration = 'Release',

    # Output folder; defaults to .\publish\<runtime>[-fd]
    [string] $Output,

    [switch] $Zip,
    [switch] $Clean,
    [switch] $NoReadyToRun,
    [switch] $AllowLfsPointer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'PianoPath.csproj'
$soundFont = Join-Path $root 'Assets\ConcertGrand.sf2'

function Write-Step([string] $text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------------------------
# 1. Tooling checks
# ---------------------------------------------------------------------------------------------
Write-Step 'Checking the .NET SDK'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'dotnet was not found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and reopen the terminal.' }
$sdkVersion = (& dotnet --version).Trim()
Write-Host "dotnet SDK $sdkVersion"
if ([version]($sdkVersion -replace '-.*$', '') -lt [version]'10.0.0') {
    throw "Keyflow targets net10.0-windows and needs the .NET 10 SDK; found $sdkVersion."
}

Write-Step 'Checking the bundled SoundFont'
if (-not (Test-Path $soundFont)) { throw "Missing $soundFont. Run 'git lfs pull' (after 'git lfs install')." }
$soundFontSize = (Get-Item $soundFont).Length
if ($soundFontSize -lt 1MB) {
    $message = "Assets\ConcertGrand.sf2 is only $soundFontSize bytes - it is a Git LFS pointer, not the real ~113 MiB SoundFont. Run 'git lfs install' then 'git lfs pull'."
    if ($AllowLfsPointer) { Write-Warning $message } else { throw $message }
} else {
    Write-Host ('SoundFont OK ({0:N1} MiB)' -f ($soundFontSize / 1MB))
}

# ---------------------------------------------------------------------------------------------
# 2. Publish
# ---------------------------------------------------------------------------------------------
$selfContained = $Mode -eq 'SelfContained'
$suffix = if ($selfContained) { '' } else { '-fd' }
if (-not $Output) { $Output = Join-Path $root "publish\$Runtime$suffix" }

if ($Clean) {
    Write-Step 'Cleaning previous build output'
    foreach ($dir in @((Join-Path $root 'bin'), (Join-Path $root 'obj'), $Output)) {
        if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    }
}

$versionNode = Select-Xml -Path $project -XPath '/Project/PropertyGroup/Version' | Select-Object -First 1
$version = if ($versionNode) { $versionNode.Node.InnerText.Trim() } else { '0.0.0' }

$arguments = @(
    'publish', $project,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--output', $Output,
    '--self-contained', $selfContained.ToString().ToLowerInvariant(),
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:SatelliteResourceLanguages=en'
)
if ($selfContained -and -not $NoReadyToRun) { $arguments += '-p:PublishReadyToRun=true' }

Write-Step "Publishing Keyflow $version ($Mode, $Runtime, $Configuration)"
Write-Host "dotnet $($arguments -join ' ')" -ForegroundColor DarkGray
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# ---------------------------------------------------------------------------------------------
# 3. Summary (+ optional zip)
# ---------------------------------------------------------------------------------------------
$exe = Join-Path $Output 'PianoPath.exe'
if (-not (Test-Path $exe)) { throw "Publish finished but $exe was not produced." }
$total = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum

Write-Step 'Done'
Write-Host ("Executable : {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host ("Folder     : {0} ({1:N1} MB total)" -f $Output, ($total / 1MB))
if (-not $selfContained) {
    Write-Host 'Target PCs need the .NET 10 Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
}

if ($Zip) {
    $zipName = "Keyflow-$version-$Runtime$suffix.zip"
    $zipPath = Join-Path (Split-Path -Parent $Output) $zipName
    Write-Step "Creating $zipName"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host ("Archive    : {0} ({1:N1} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB))
}
