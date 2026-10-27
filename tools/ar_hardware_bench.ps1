#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs section 3 of docs/AR-HARDWARE-SIGNOFF.md in one command.

.DESCRIPTION
    The runbook asks for a Release build, an adapter name that matches exactly one GPU, two separate
    120-frame runs (--bench-tier=Lite and --bench-tier=Ultra) and an immediate check that each report
    really came from a graphics card (warp: false, softwareAdapter: false). Done by hand on a hybrid
    laptop it fails in two ways that cost an afternoon: a --bench-adapter that matches nothing - or
    several adapters - exits 2 without a visible reason, because the executable is a WinExe
    (PianoPath.csproj: OutputType WinExe) and a terminal may show nothing but the exit code. So this
    script pins the adapter with a 20-frame probe first, then runs both tiers, then validates the
    folder with tools/ar_hardware_signoff.py and writes the webcam.json the runbook needs for
    section 4.

    It only reads and writes evidence files. It never opens Stage/Effects/EffectContracts.cs, and the
    PASS it prints is the validator's own line, not an approval: ArProjectedParameterBindings stays
    Unverified until the owner signs (section 6).

    pwsh tools/ar_hardware_bench.ps1                        # probe, Lite + Ultra, then validate
    pwsh tools/ar_hardware_bench.ps1 -Adapter 'RTX 4050'    # pin the discrete GPU by name
    pwsh tools/ar_hardware_bench.ps1 -EvidenceDir D:\keyflow-signoff\2026-10-27
    pwsh tools/ar_hardware_bench.ps1 -ValidateOnly          # re-check a folder after section 4
    pwsh tools/ar_hardware_bench.ps1 -WebcamOnly -Force     # rewrite webcam.json (never without -Force)

    Section 4 itself is still a human job: five webcam scenarios, OBS Virtual Camera, MEASURE DELAY.
#>
[CmdletBinding()]
param(
    # The Release build to measure. It must be the build of the commit being signed off (section 2).
    [string]$Exe = '',

    # Folder bench-Lite.json, bench-Ultra.json, webcam.json and shots/ go into. It is deliberately
    # outside the repository: evidence photos and videos are not committed (section 2).
    [string]$EvidenceDir = '',

    # Substring of the adapter to measure on. Required in practice on a hybrid laptop, and resolved
    # against this machine's own adapter list before a single frame is drawn.
    [string]$Adapter = '',

    # Frames per scene. The runbook's 120 is the default; the probe always uses 20.
    [int]$Frames = 120,

    # Skip the 20-frame adapter probe (use it once the adapter name is known to work).
    [switch]$SkipProbe,

    # Measure, but do not run the validator.
    [switch]$BenchOnly,

    # Only run the validator over an existing folder.
    [switch]$ValidateOnly,

    # Only (re)write webcam.json. Refuses to overwrite a signed or annotated record without -Force.
    [switch]$WebcamOnly,

    # Allow webcam.json to be rewritten. The file holds the operator's notes and both approvals, so
    # overwriting it by accident would destroy section 4's work.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$Tiers = @('Lite', 'Ultra')
$Scenarios = @('overhead', 'fisheye', 'low_light', 'fast_motion', 'hand_occlusion')

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "== $Message" -ForegroundColor Cyan
}

function Write-Note {
    param([string]$Message)
    Write-Host "   $Message"
}

function Get-BenchReport {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "the app wrote no report at $Path (exit code alone is not enough: see the log beside it)"
    }
    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    }
    catch {
        throw "$Path is not readable JSON ($_)"
    }
}

function Write-RunFailure {
    # The executable is GUI-subsystem, so its console lines may never reach the terminal. It still
    # writes its reason beside the report as <report>.startup-error.txt, so read that file first.
    param([string]$ReportPath, [string]$LogPath)
    $startupError = [System.IO.Path]::ChangeExtension($ReportPath, '.startup-error.txt')
    if (Test-Path -LiteralPath $startupError) {
        Write-Host (Get-Content -LiteralPath $startupError -Raw) -ForegroundColor Yellow
    }
    if (Test-Path -LiteralPath $LogPath) {
        Write-Host (Get-Content -LiteralPath $LogPath -Tail 15)
    }
}

function Write-SceneLines {
    param($Report)
    foreach ($scene in $Report.scenes) {
        Write-Note ('  {0,-8} p95 {1,7} ms of {2,2} ms budget - {3}' -f $scene.scene, $scene.stats.p95Ms,
            $scene.budgetP95LimitMs, $scene.verdict)
    }
}

function Get-CameraName {
    $found = New-Object System.Collections.Generic.List[string]
    $pnpCommand = Get-Command Get-PnpDevice -ErrorAction SilentlyContinue
    if ($pnpCommand) {
        $devices = @(Get-PnpDevice -Class Camera -Status OK -ErrorAction SilentlyContinue)
        foreach ($device in $devices) {
            if ($device.FriendlyName) { $found.Add($device.FriendlyName.Trim()) }
        }
    }
    if ($found.Count -eq 0) {
        $entities = @(Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -and $_.Name -match 'camera|webcam' -and $_.Name -notmatch 'button' })
        foreach ($entity in $entities) { $found.Add($entity.Name.Trim()) }
    }
    return @($found | Sort-Object -Unique)
}

function Write-WebcamRecord {
    # Auto-filled from this machine where it can be (GPU, driver, Windows build, camera names), so the
    # record never ships a plausible-looking number nobody verified. Everything else is a FILL-ME that
    # Get-RemainingWork keeps naming until a human replaces it.
    param([string]$Path, [string]$GpuName, [string]$GpuDriver, [string]$OsName, [string[]]$Cameras)

    $device = 'FILL-ME: camera name, e.g. Logitech C920 / OBS Virtual Camera'
    if ($Cameras.Count -eq 1) { $device = $Cameras[0] }
    elseif ($Cameras.Count -gt 1) { $device = 'FILL-ME: which camera? ' + ($Cameras -join ' / ') }

    $json = @"
{
  "operator": "FILL-ME: name of the person who ran this",
  "machine": {
    "gpu": "$GpuName",
    "driver": "$GpuDriver",
    "os": "$OsName"
  },
  "camera": {
    "device": "$device",
    "mode": "FILL-ME: the mode the app picked, e.g. 1280x720@60 MJPG",
    "obs_virtual_camera": false
  },
  "scenarios": {
    "overhead":       { "file": "shots/overhead.png",       "notes": "" },
    "fisheye":        { "file": "shots/fisheye.png",        "notes": "" },
    "low_light":      { "file": "shots/low_light.png",      "notes": "" },
    "fast_motion":    { "file": "shots/fast_motion.png",    "notes": "" },
    "hand_occlusion": { "file": "shots/hand_occlusion.png", "notes": "" }
  },
  "measured_delay_ms": 0,
  "art_approved": false,
  "owner_approved": false
}
"@
    # UTF-8 without a BOM: tools/ar_hardware_signoff.py reads the file as utf-8, and a BOM would make
    # it the first character of the JSON. The usual cmdlet writes one on Windows PowerShell 5.1, so
    # the bytes are written directly instead.
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json, $encoding)
}

function Get-RemainingWork {
    # What section 6 still needs. Mirrors the conditions in tools/ar_hardware_signoff.py so the
    # operator is told what to finish rather than only that something failed.
    param([string]$Folder)
    $work = New-Object System.Collections.Generic.List[string]
    $webcamPath = Join-Path $Folder 'webcam.json'
    if (-not (Test-Path -LiteralPath $webcamPath)) {
        $work.Add('webcam.json is missing')
        return @($work)
    }
    $raw = Get-Content -LiteralPath $webcamPath -Raw
    if ($raw -match 'FILL-ME') {
        $work.Add('webcam.json still has FILL-ME placeholders (operator, camera device, camera mode)')
    }
    try {
        $record = $raw | ConvertFrom-Json
    }
    catch {
        $work.Add("webcam.json is not readable JSON ($_)")
        return @($work)
    }
    foreach ($name in $Scenarios) {
        $entry = $record.scenarios.$name
        if ($null -eq $entry) {
            $work.Add("webcam.json has no '$name' scenario")
            continue
        }
        if (-not $entry.file) {
            $work.Add("scenario '$name' has no file")
        }
        elseif (-not (Test-Path -LiteralPath (Join-Path $Folder $entry.file))) {
            $work.Add("scenario '$name' points at $($entry.file), which is not in the evidence folder")
        }
        if (-not $entry.notes) {
            $work.Add("scenario '$name' still needs written notes - a failed scenario needs its result too")
        }
    }
    if (-not $record.measured_delay_ms -or $record.measured_delay_ms -le 0) {
        $work.Add('measured_delay_ms is still 0 - run MEASURE DELAY in the app and put the number here')
    }
    if ($record.art_approved -ne $true) {
        $work.Add('art_approved is not true - the owner reviews the shots first')
    }
    if ($record.owner_approved -ne $true) {
        $work.Add('owner_approved is not true - ArProjectedParameterBindings stays Unverified until it is')
    }
    return @($work)
}

function Get-Python {
    foreach ($name in @('python', 'py', 'python3')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    throw 'no python on PATH, and tools/ar_hardware_signoff.py needs one'
}

# ---------------------------------------------------------------- paths
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Exe)) {
    $Exe = [System.IO.Path]::Combine($repoRoot, 'bin', 'Release', 'net10.0-windows', 'PianoPath.exe')
}
if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
    $EvidenceDir = Join-Path 'D:\keyflow-signoff' (Get-Date -Format 'yyyy-MM-dd')
}
if (-not (Test-Path -LiteralPath $Exe)) {
    throw "no build at $Exe - run 'dotnet build -c Release' from the commit being signed off first (section 2)"
}
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$evidencePath = $EvidenceDir
try {
    $evidencePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidenceDir)
}
catch {
    # A drive that does not exist yet has no resolved form. New-Item creates it below or fails loudly.
}
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
$rootPath = (Resolve-Path -LiteralPath $repoRoot).Path
if ($evidencePath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "the evidence folder is inside the repository ($evidencePath); section 2 keeps photos and videos out of it"
}

# ---------------------------------------------------------------- adapter
if (-not $ValidateOnly) {
    Write-Step 'Adapter'
    $controllers = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -and $_.Name -notmatch 'Basic Render Driver|Basic Display' })
    $names = @($controllers | ForEach-Object { $_.Name.Trim() })
    if ($names.Count -eq 0) {
        throw 'this machine reports no display adapter, so the run cannot be pinned to a graphics card'
    }
    Write-Note ('WMI reports: ' + ($names -join ' | '))
    if ([string]::IsNullOrWhiteSpace($Adapter)) {
        $preferred = @($names | Where-Object { $_ -match 'NVIDIA|GeForce|RTX' })
        if ($preferred.Count -eq 1) {
            $Adapter = $preferred[0]
        }
        else {
            throw "pass -Adapter '<a substring of one adapter>': WMI reports $($names.Count) adapters and a hybrid laptop must not be benchmarked on whichever one Windows picked"
        }
    }
    $matched = @($names | Where-Object { $_ -like "*$Adapter*" })
    if ($matched.Count -eq 0) {
        throw "-Adapter '$Adapter' matches none of: $($names -join ' | ')"
    }
    if ($matched.Count -gt 1) {
        throw "-Adapter '$Adapter' matches $($matched.Count) adapters ($($matched -join ' | ')); it must match exactly one"
    }
    $adapterName = $matched[0]
    $adapterDriver = ($controllers | Where-Object { $_.Name.Trim() -eq $adapterName } |
        Select-Object -First 1).DriverVersion
    if (-not $adapterDriver) { $adapterDriver = 'FILL-ME: driver version' }
    Write-Note ("measuring on: $adapterName (driver $adapterDriver)")
}

# ---------------------------------------------------------------- probe
if (-not $SkipProbe -and -not $ValidateOnly -and -not $WebcamOnly) {
    Write-Step 'Probe: 20 frames, to prove the adapter is a graphics card before a long run'
    $probeDir = Join-Path $env:TEMP ('keyflow-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
    $probeJson = Join-Path $probeDir 'probe.json'
    $probeLog = Join-Path $probeDir 'probe.log'
    & $exePath --bench=20 --bench-ar "--bench-adapter=$adapterName" "--bench-out=$probeJson" *> $probeLog
    $probeCode = $LASTEXITCODE
    if ($probeCode -eq 3) {
        Write-Warning 'the 20-frame probe was over budget; the adapter check still holds, so it continues'
    }
    elseif ($probeCode -ne 0) {
        Write-RunFailure -ReportPath $probeJson -LogPath $probeLog
        throw "the probe exited $probeCode - no adapter, no measurement. See $probeLog"
    }
    $probe = Get-BenchReport -Path $probeJson
    if ($probe.warp -ne $false -or $probe.softwareAdapter -ne $false) {
        throw "the probe ran on a software rasterizer (warp=$($probe.warp), softwareAdapter=$($probe.softwareAdapter), adapter=$($probe.adapter)); WARP is not a graphics card, so pick the other adapter with -Adapter"
    }
    Write-Note ("report says: adapter=$($probe.adapter) warp=$($probe.warp) softwareAdapter=$($probe.softwareAdapter)")
}

# ---------------------------------------------------------------- two tiers
if (-not $ValidateOnly -and -not $WebcamOnly) {
    foreach ($tier in $Tiers) {
        Write-Step "Tier $tier - $Frames frames per scene (default 8 ms / heavy 16 ms / ar 16 ms)"
        $report = Join-Path $evidencePath "bench-$tier.json"
        $log = Join-Path $evidencePath "bench-$tier.log"
        & $exePath "--bench=$Frames" --bench-ar "--bench-tier=$tier" "--bench-adapter=$adapterName" "--bench-out=$report" *> $log
        $code = $LASTEXITCODE
        if ($code -eq 3) {
            Write-RunFailure -ReportPath $report -LogPath $log
            throw "tier $tier is outside the 8/16/16 ms budgets: report it instead of tuning the threshold (section 3), and stop before the webcam scenarios"
        }
        if ($code -ne 0) {
            Write-RunFailure -ReportPath $report -LogPath $log
            throw "tier $tier exited $code (2 = adapter or device, 4 = bad tier name); see $log"
        }
        $doc = Get-BenchReport -Path $report
        if ($doc.warp -ne $false) { throw "bench-$tier.json has warp=$($doc.warp); a software rasterizer is not hardware evidence" }
        if ($doc.softwareAdapter -ne $false) { throw "bench-$tier.json has softwareAdapter=$($doc.softwareAdapter); the absolute budgets do not apply" }
        if ($doc.arTier -ne $tier) { throw "bench-$tier.json says arTier '$($doc.arTier)'; the switch did not reach the report" }
        Write-Note ("adapter=$($doc.adapter) arTier=$($doc.arTier)")
        Write-SceneLines -Report $doc
    }
}

# ---------------------------------------------------------------- webcam record
if (-not $BenchOnly) {
    $shotsDir = Join-Path $evidencePath 'shots'
    if (-not (Test-Path -LiteralPath $shotsDir)) {
        New-Item -ItemType Directory -Path $shotsDir -Force | Out-Null
    }
    $webcamPath = Join-Path $evidencePath 'webcam.json'
    if ((Test-Path -LiteralPath $webcamPath) -and -not $Force) {
        Write-Note "webcam.json exists; keeping the operator's notes and approvals (use -Force to rewrite it)"
    }
    else {
        Write-Step 'Webcam record'
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
        $osName = 'FILL-ME: Windows version, e.g. Windows 11 23H2'
        if ($os) { $osName = '{0} (build {1})' -f $os.Caption.Trim(), $os.BuildNumber }
        if ([string]::IsNullOrWhiteSpace($adapterName)) { $adapterName = 'FILL-ME: adapter name' }
        if ([string]::IsNullOrWhiteSpace($adapterDriver)) { $adapterDriver = 'FILL-ME: driver version' }
        $cameras = @(Get-CameraName)
        Write-WebcamRecord -Path $webcamPath -GpuName $adapterName -GpuDriver $adapterDriver -OsName $osName -Cameras $cameras
        Write-Note ("wrote $webcamPath - machine fields came from this machine, the rest are FILL-ME")
        if ($cameras.Count -gt 0) { Write-Note ('cameras found: ' + ($cameras -join ' | ')) }
    }
}

# ---------------------------------------------------------------- validate
if (-not $BenchOnly -and -not $WebcamOnly) {
    Write-Step 'Validate'
    $python = Get-Python
    $validator = Join-Path $PSScriptRoot 'ar_hardware_signoff.py'
    & $python $validator --evidence $evidencePath
    $validatorCode = $LASTEXITCODE
    $work = @(Get-RemainingWork -Folder $evidencePath)
    if ($work.Count -gt 0) {
        Write-Host ''
        Write-Host 'Still to do before the binding can be flipped (section 6):' -ForegroundColor Yellow
        foreach ($item in $work) { Write-Host "  - $item" }
    }
    elseif ($validatorCode -ne 0) {
        throw "the validator failed on a folder that looks complete; read its FAIL lines above"
    }
}
