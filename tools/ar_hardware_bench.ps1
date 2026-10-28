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
    pwsh tools/ar_hardware_bench.ps1 -ValidateOnly         # re-check a folder after section 4
    pwsh tools/ar_hardware_bench.ps1 -WebcamOnly -Force    # rewrite webcam.json (never without -Force)
    pwsh tools/ar_hardware_bench.ps1 -FillWebcam           # answer the section 4 questions instead of editing JSON
    pwsh tools/ar_hardware_bench.ps1 -Approve              # sign the record; refused if any check fails

    Section 4 itself is still a human job: five webcam scenarios, OBS Virtual Camera, MEASURE DELAY.
#>
[CmdletBinding()]
param(
    # The Release build to measure. It must be the build of the commit being signed off (section 2).
    [string]$Exe = '',

    # Folder bench-Lite.json, bench-Ultra.json, webcam.json and shots/ go into. It is deliberately
    # outside the repository: evidence photos and videos are not committed (section 2). Defaults to
    # D:\keyflow-signoff\<today>, or <home>\keyflow-signoff\<today> when this machine has no D: drive.
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

    # Ask the questions section 4 needs and fill webcam.json in, instead of opening it in Notepad.
    # Nothing is benchmarked, and the two approvals are passed through untouched.
    [switch]$FillWebcam,

    # Allow webcam.json to be rewritten. The file holds the operator's notes and both approvals, so
    # overwriting it by accident would destroy section 4's work.
    [switch]$Force,

    # Record the owner's sign-off. The validator has to have nothing left to say first: section 6
    # makes the approval the third of three conditions, a signature on the other two, not a way
    # around them. Nothing is benchmarked and no picture is touched.
    [switch]$Approve
)

$ErrorActionPreference = 'Stop'

$Tiers = @('Lite', 'Ultra')
$Scenarios = @('overhead', 'fisheye', 'low_light', 'fast_motion', 'hand_occlusion')
# Where each scenario's picture belongs. Written out name by name so the record a fresh run writes is
# readable in one place, and so grepping the script for a shot name finds it.
$DefaultShotFiles = @{
    overhead       = 'shots/overhead.png'
    fisheye        = 'shots/fisheye.png'
    low_light      = 'shots/low_light.png'
    fast_motion    = 'shots/fast_motion.png'
    hand_occlusion = 'shots/hand_occlusion.png'
}

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
    # writes its reason beside the report as <report>.startup-error.txt, so read that file first,
    # then the two log files Invoke-BenchRun wrote.
    param([string]$ReportPath, [string]$LogPath)
    $startupError = [System.IO.Path]::ChangeExtension($ReportPath, '.startup-error.txt')
    if (Test-Path -LiteralPath $startupError) {
        Write-Host (Get-Content -LiteralPath $startupError -Raw) -ForegroundColor Yellow
    }
    foreach ($path in @($LogPath, [System.IO.Path]::ChangeExtension($LogPath, '.err.log'))) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $text = (Get-Content -LiteralPath $path -Raw)
        if ([string]::IsNullOrWhiteSpace($text)) {
            Write-Host "   ($(Split-Path -Leaf $path) is empty: the app wrote nothing before it ended)"
        }
        else {
            Write-Host $text
        }
    }
}

function Invoke-BenchRun {
    # The executable is a WinExe (PianoPath.csproj: OutputType WinExe). Calling it with '&' can return
    # before the process has finished, which leaves the automatic exit-code variable unset - that is how
    # the first real run reported "the probe exited  - no adapter, no measurement", with no number in
    # it. Start-Process -Wait -PassThru always waits and always reports a real exit code, so every
    # failure message below has a number to name. Standard output and error go to two different files,
    # because Start-Process refuses to redirect both to the same one.
    param([string]$ExePath, [string[]]$Arguments, [string]$LogPath)
    $errorLog = [System.IO.Path]::ChangeExtension($LogPath, '.err.log')
    $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $LogPath -RedirectStandardError $errorLog
    return [int]$process.ExitCode
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

function ConvertTo-JsonText {
    # A note typed at a prompt can hold a quote or a backslash, either of which would break the record
    # the validator then reads as JSON. Escape the two characters that matter and nothing else.
    param([string]$Value)
    if ($null -eq $Value) { return '' }
    return $Value.Replace('\', '\\').Replace('"', '\"')
}

function Write-WebcamRecord {
    # One writer for the whole record, so the fresh template and the -FillWebcam edit cannot drift
    # apart. The machine fields come from this machine; everything a human decides arrives as a
    # parameter; the two approvals are written back exactly as they were given.
    param(
        [string]$Path, [string]$Operator, [string]$GpuName, [string]$GpuDriver, [string]$OsName,
        [string]$Device, [string]$Mode, [bool]$Obs, [string]$ObsToggleNotes, [string]$SameNamePairNotes,
        [hashtable]$Files, [hashtable]$Notes,
        [double]$Delay, [bool]$ArtApproved, [bool]$OwnerApproved)

    $scenarioLines = @()
    foreach ($name in $Scenarios) {
        $file = ConvertTo-JsonText -Value $Files[$name]
        $note = ConvertTo-JsonText -Value $Notes[$name]
        $scenarioLines += ('    "{0}": {{ "file": "{1}", "notes": "{2}" }}' -f $name, $file, $note)
    }
    $scenarioBlock = $scenarioLines -join ",`n"
    $operatorText = ConvertTo-JsonText -Value $Operator
    $gpuText = ConvertTo-JsonText -Value $GpuName
    $driverText = ConvertTo-JsonText -Value $GpuDriver
    $osText = ConvertTo-JsonText -Value $OsName
    $deviceText = ConvertTo-JsonText -Value $Device
    $modeText = ConvertTo-JsonText -Value $Mode
    $obsToggleText = ConvertTo-JsonText -Value $ObsToggleNotes
    $sameNamePairText = ConvertTo-JsonText -Value $SameNamePairNotes
    $obsText = 'false'
    if ($Obs) { $obsText = 'true' }
    $artText = 'false'
    if ($ArtApproved) { $artText = 'true' }
    $ownerText = 'false'
    if ($OwnerApproved) { $ownerText = 'true' }
    $delayText = $Delay.ToString([System.Globalization.CultureInfo]::InvariantCulture)

    $json = @"
{
  "operator": "$operatorText",
  "machine": {
    "gpu": "$gpuText",
    "driver": "$driverText",
    "os": "$osText"
  },
  "camera": {
    "device": "$deviceText",
    "mode": "$modeText",
    "obs_virtual_camera": $obsText,
    "obs_toggle_notes": "$obsToggleText",
    "same_name_pair_notes": "$sameNamePairText"
  },
  "scenarios": {
$scenarioBlock
  },
  "measured_delay_ms": $delayText,
  "art_approved": $artText,
  "owner_approved": $ownerText
}
"@
    # UTF-8 without a BOM: tools/ar_hardware_signoff.py reads the file as utf-8, and a BOM would make
    # it the first character of the JSON. The usual cmdlet writes one on Windows PowerShell 5.1, so
    # the bytes are written directly instead.
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json, $encoding)
}

function Read-Filled {
    # Each question shows what is already there, because filling the record in after a partial
    # attempt must not ask the operator to retype what they wrote last time. Empty is only accepted
    # when the current value is usable, and a number is only accepted when it is greater than zero -
    # the validator refuses a delay of 0, because 0 means nobody ran MEASURE DELAY.
    param([string]$Prompt, [string]$Current, [switch]$Number)
    while ($true) {
        # Saying "press Enter to keep it" when there is nothing to keep puts the operator in a loop
        # that looks like a hang: Enter cannot satisfy a field that has no value yet. The hint has to
        # depend on whether keeping is actually possible.
        $hasCurrent = -not ([string]::IsNullOrWhiteSpace($Current) -or $Current -match 'FILL-ME')
        $shown = $Current
        if (-not $hasCurrent) { $shown = '(nothing yet)' }
        Write-Host "  $Prompt"
        Write-Host "    current: $shown" -ForegroundColor DarkGray
        $hint = '    type the answer now, or press Enter to keep the one above'
        if (-not $hasCurrent) { $hint = '    type the answer (Enter alone will not do: there is nothing to keep yet)' }
        $answer = (Read-Host $hint).Trim()
        if ([string]::IsNullOrWhiteSpace($answer)) {
            if ([string]::IsNullOrWhiteSpace($Current) -or $Current -match 'FILL-ME') {
                Write-Host '    this one cannot stay empty' -ForegroundColor Yellow
                continue
            }
            if ($Number) {
                $kept = 0.0
                $keptOk = [double]::TryParse($Current, [System.Globalization.NumberStyles]::Float,
                    [System.Globalization.CultureInfo]::InvariantCulture, [ref]$kept)
                if (-not ($keptOk -and $kept -gt 0)) {
                    Write-Host '    this one must be a number greater than 0' -ForegroundColor Yellow
                    continue
                }
            }
            return $Current
        }
        if ($Number) {
            # 0 means "not measured yet", and it is the one number that may be typed honestly before
            # MEASURE DELAY has been run: the validator keeps refusing it and Get-RemainingWork keeps
            # naming it, whereas any other number would be a measurement nobody made.
            if ($answer -eq '0') {
                Write-Host '    recorded as not measured yet; the validator will keep asking for it' -ForegroundColor Yellow
                return '0'
            }
            $parsed = 0.0
            $parsedOk = [double]::TryParse($answer, [System.Globalization.NumberStyles]::Float,
                [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)
            if (-not ($parsedOk -and $parsed -gt 0)) {
                Write-Host '    type a number greater than 0: the ms MEASURE DELAY reported' -ForegroundColor Yellow
                continue
            }
        }
        return $answer
    }
}

function Edit-WebcamRecord {
    # Section 4 written by hand means editing JSON in Notepad, where one missing quote turns the whole
    # record into "could not be read as JSON". This asks the questions the record needs, copies each
    # picture into shots/ itself, and never touches the two approvals: art_approved and
    # owner_approved are the owner's decision, not the machine's.
    param([string]$Path, [string]$Folder)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "no webcam.json at $Path - run the script once without -FillWebcam so it can write the record first"
    }
    try { $record = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "webcam.json is not readable JSON ($_)" }
    $shotsDir = Join-Path $Folder 'shots'
    if (-not (Test-Path -LiteralPath $shotsDir)) {
        New-Item -ItemType Directory -Path $shotsDir -Force | Out-Null
    }

    Write-Step 'Webcam record: answer each question; Enter keeps what is already there'
    $operator = Read-Filled -Prompt 'Who is running this session (operator)?' -Current $record.operator
    $mode = Read-Filled -Prompt 'Camera mode the app reports (e.g. 1280x720@60 MJPG)?' -Current $record.camera.mode
    $device = Read-Filled -Prompt 'Which camera did you shoot the five scenarios with?' -Current $record.camera.device
    $obs = ((Read-Filled -Prompt 'Did you test OBS Virtual Camera (true/false)?' -Current ([string]$record.camera.obs_virtual_camera)) -match '^(true|t|yes|y)$')
    # The last two questions of section 4 carry no picture, so the record is their only home. Both
    # are asked as free text on purpose: a check that failed has to say how, not just stay false.
    $obsToggle = Read-Filled -Prompt 'What happened when OBS Virtual Camera was switched on and off while the app was open?' `
        -Current ([string]$record.camera.obs_toggle_notes)
    $sameNamePair = Read-Filled -Prompt 'What happened with the second camera of the same name (the one Windows calls (2))?' `
        -Current ([string]$record.camera.same_name_pair_notes)
    $delayAnswer = Read-Filled -Prompt 'Delay in ms that MEASURE DELAY reported?' -Current ([string]$record.measured_delay_ms) -Number

    $files = @{}
    $notes = @{}
    foreach ($name in $Scenarios) {
        Write-Host ''
        Write-Host "  Scenario '$name'" -ForegroundColor Cyan
        $current = $DefaultShotFiles[$name]
        $currentNotes = ''
        $entry = $record.scenarios.$name
        if ($null -ne $entry) {
            if ($entry.file) { $current = $entry.file }
            if ($entry.notes) { $currentNotes = $entry.notes }
        }
        Write-Host "    it belongs at: $current"
        $shot = (Read-Host '    path to the picture you took (Enter to keep the current one)').Trim()
        if (-not ([string]::IsNullOrWhiteSpace($shot))) {
            # A path pasted from Explorer's "Copy as path" arrives wrapped in quotes.
            $shot = $shot.Trim('"').Trim()
            if (-not (Test-Path -LiteralPath $shot)) {
                Write-Host "    nothing at $shot - keeping $current" -ForegroundColor Yellow
            }
            else {
                $extension = [System.IO.Path]::GetExtension($shot)
                if ([string]::IsNullOrWhiteSpace($extension)) { $extension = '.png' }
                $target = Join-Path $shotsDir ($name + $extension)
                Copy-Item -LiteralPath $shot -Destination $target -Force
                $current = 'shots/' + $name + $extension
                Write-Host "    copied to $current"
            }
        }
        $files[$name] = $current
        if (-not (Test-Path -LiteralPath (Join-Path $Folder $current))) {
            # Keeping the target name is the right answer for a scenario that has not been shot yet,
            # but it must not look like the answer: without this the run ends, the notes are written
            # and the missing picture only turns up in the validator's list.
            Write-Host "    no picture at $current yet - the validator keeps asking until the file is there" -ForegroundColor Yellow
        }
        $notes[$name] = Read-Filled -Prompt "What does '$name' show - or how did it fail?" -Current $currentNotes
    }

    # An answer pasted from the wrong place - the label of the next question, a line of instructions -
    # looks exactly like a real answer once it is in the file, and the validator cannot tell them
    # apart either. Everything is read back once, and writing it takes a second yes.
    Write-Host ''
    Write-Host '  About to write this record:' -ForegroundColor Cyan
    Write-Host "    operator          : $operator"
    Write-Host "    camera.device     : $device"
    Write-Host "    camera.mode       : $mode"
    Write-Host "    OBS tested        : $obs"
    Write-Host "    OBS toggle notes  : $obsToggle"
    Write-Host "    same-name pair    : $sameNamePair"
    Write-Host "    measured delay ms : $delayAnswer"
    foreach ($name in $Scenarios) {
        Write-Host ("    {0,-17}: {1}" -f $name, $files[$name])
        Write-Host ("    {0,-17}  notes: {1}" -f '', $notes[$name])
    }
    $confirm = (Read-Host '  write it? (yes to write, anything else throws it away)').Trim()
    if ($confirm -notmatch '^(yes|y)$') {
        Write-Host '  nothing was written - run -FillWebcam again' -ForegroundColor Yellow
        return
    }

    $delay = [double]::Parse($delayAnswer, [System.Globalization.CultureInfo]::InvariantCulture)
    Write-WebcamRecord -Path $Path -Operator $operator -GpuName $record.machine.gpu -GpuDriver $record.machine.driver `
        -OsName $record.machine.os -Device $device -Mode $mode -Obs $obs -ObsToggleNotes $obsToggle `
        -SameNamePairNotes $sameNamePair -Files $files -Notes $notes -Delay $delay `
        -ArtApproved ([bool]$record.art_approved) -OwnerApproved ([bool]$record.owner_approved)
    Write-Note ("wrote $Path - the two approvals were left as they were")
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
    try {
        $record = $raw | ConvertFrom-Json
    }
    catch {
        $work.Add("webcam.json is not readable JSON ($_)")
        return @($work)
    }
    # Name the fields that are still placeholders instead of listing every field the template has:
    # the machine fields come from this machine, so after the first run only a couple are left.
    $placeholders = New-Object System.Collections.Generic.List[string]
    if ($record.operator -match 'FILL-ME') { $placeholders.Add('operator') }
    foreach ($key in @('gpu', 'driver', 'os')) {
        if ($record.machine.$key -match 'FILL-ME') { $placeholders.Add("machine.$key") }
    }
    foreach ($key in @('device', 'mode')) {
        if ($record.camera.$key -match 'FILL-ME') { $placeholders.Add("camera.$key") }
    }
    if ($placeholders.Count -gt 0) {
        $work.Add('webcam.json still has FILL-ME placeholders: ' + ($placeholders -join ', '))
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
    # Section 4's two picture-less checks are not scenarios, so nothing else in this script would
    # notice that they were never written down. They are listed here as their own remaining work.
    if ([string]::IsNullOrWhiteSpace([string]$record.camera.obs_toggle_notes) -or $record.camera.obs_toggle_notes -match 'FILL-ME') {
        $work.Add('camera.obs_toggle_notes is still empty - say what happened when OBS Virtual Camera was switched while the app was open')
    }
    if ([string]::IsNullOrWhiteSpace([string]$record.camera.same_name_pair_notes) -or $record.camera.same_name_pair_notes -match 'FILL-ME') {
        $work.Add('camera.same_name_pair_notes is still empty - say what happened with the second same-name camera, the one Windows calls (2)')
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
    # The runbook suggests D:\keyflow-signoff\<date>. Most machines have no D: at all - the first run
    # of this script died on "Cannot find drive. A drive with the name 'D' does not exist" - so the
    # default falls back to the user's own profile folder, which exists everywhere and is still
    # outside the repository. Pass -EvidenceDir to put it anywhere else.
    $defaultRoot = 'D:\keyflow-signoff'
    if (-not (Test-Path -LiteralPath ([System.IO.Path]::GetPathRoot($defaultRoot)))) {
        $defaultRoot = Join-Path $HOME 'keyflow-signoff'
        Write-Warning "no D: drive on this machine, so the evidence folder defaults to $defaultRoot"
    }
    $EvidenceDir = Join-Path $defaultRoot (Get-Date -Format 'yyyy-MM-dd')
}
# A typed -EvidenceDir on a drive that does not exist is an operator mistake, not something to fix
# silently: New-Item would fail three lines further down with a message that names no alternative.
$evidenceRoot = [System.IO.Path]::GetPathRoot($EvidenceDir)
if ($evidenceRoot -and -not (Test-Path -LiteralPath $evidenceRoot)) {
    $drives = @(Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Name + ':' } | Sort-Object)
    $firstRoot = (Get-PSDrive -PSProvider FileSystem | Select-Object -First 1).Root
    $suggested = Join-Path (Join-Path $firstRoot 'keyflow-signoff') (Get-Date -Format 'yyyy-MM-dd')
    throw "the evidence folder starts with $evidenceRoot, and this machine has no such drive (it has: $($drives -join ' ')). Pass -EvidenceDir '$suggested' or another folder on a drive that exists"
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
Write-Note ("evidence folder: $evidencePath")
$rootPath = (Resolve-Path -LiteralPath $repoRoot).Path
if ($evidencePath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Warning "the evidence folder is inside the repository ($evidencePath); section 2 keeps photos and videos out of it"
}

# ---------------------------------------------------------------- adapter
if (-not $ValidateOnly -and -not $FillWebcam -and -not $Approve) {
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
if (-not $SkipProbe -and -not $ValidateOnly -and -not $WebcamOnly -and -not $FillWebcam -and -not $Approve) {
    Write-Step 'Probe: 20 frames, to prove the adapter is a graphics card before a long run'
    $probeDir = Join-Path $env:TEMP ('keyflow-probe-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
    $probeJson = Join-Path $probeDir 'probe.json'
    $probeLog = Join-Path $probeDir 'probe.log'
    $probeCode = Invoke-BenchRun -ExePath $exePath -LogPath $probeLog -Arguments @(
        '--bench=20', '--bench-ar', "--bench-adapter=$adapterName", "--bench-out=$probeJson")
    if ($probeCode -eq 3) {
        Write-Warning 'the 20-frame probe was over budget; the adapter check still holds, so it continues'
    }
    elseif ($probeCode -ne 0) {
        Write-RunFailure -ReportPath $probeJson -LogPath $probeLog
        throw "the probe exited $probeCode - no adapter, no measurement. See $probeLog"
    }
    if (-not (Test-Path -LiteralPath $probeJson)) {
        Write-RunFailure -ReportPath $probeJson -LogPath $probeLog
        throw "the probe exited $probeCode but wrote no report at $probeJson; see $probeLog"
    }
    $probe = Get-BenchReport -Path $probeJson
    if ($probe.warp -ne $false -or $probe.softwareAdapter -ne $false) {
        throw "the probe ran on a software rasterizer (warp=$($probe.warp), softwareAdapter=$($probe.softwareAdapter), adapter=$($probe.adapter)); WARP is not a graphics card, so pick the other adapter with -Adapter"
    }
    Write-Note ("report says: adapter=$($probe.adapter) warp=$($probe.warp) softwareAdapter=$($probe.softwareAdapter)")
}

# ---------------------------------------------------------------- two tiers
if (-not $ValidateOnly -and -not $WebcamOnly -and -not $FillWebcam -and -not $Approve) {
    foreach ($tier in $Tiers) {
        Write-Step "Tier $tier - $Frames frames per scene (default 8 ms / heavy 16 ms / ar 16 ms)"
        $report = Join-Path $evidencePath "bench-$tier.json"
        $log = Join-Path $evidencePath "bench-$tier.log"
        $code = Invoke-BenchRun -ExePath $exePath -LogPath $log -Arguments @(
            "--bench=$Frames", '--bench-ar', "--bench-tier=$tier", "--bench-adapter=$adapterName", "--bench-out=$report")
        if ($code -eq 3) {
            Write-RunFailure -ReportPath $report -LogPath $log
            throw "tier $tier is outside the 8/16/16 ms budgets: report it instead of tuning the threshold (section 3), and stop before the webcam scenarios"
        }
        if ($code -ne 0) {
            Write-RunFailure -ReportPath $report -LogPath $log
            throw "tier $tier exited $code (2 = adapter or device, 4 = bad tier name); see $log"
        }
        if (-not (Test-Path -LiteralPath $report)) {
            Write-RunFailure -ReportPath $report -LogPath $log
            throw "tier $tier exited $code but wrote no report at $report; see $log"
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
if (-not $BenchOnly -and -not $Approve) {
    $shotsDir = Join-Path $evidencePath 'shots'
    if (-not (Test-Path -LiteralPath $shotsDir)) {
        New-Item -ItemType Directory -Path $shotsDir -Force | Out-Null
    }
    $webcamPath = Join-Path $evidencePath 'webcam.json'
    if ($FillWebcam) {
        Edit-WebcamRecord -Path $webcamPath -Folder $evidencePath
    }
    elseif ((Test-Path -LiteralPath $webcamPath) -and -not $Force) {
        Write-Note "webcam.json exists; keeping the operator's notes and approvals (use -Force to rewrite it, or -FillWebcam to answer the questions instead)"
    }
    else {
        Write-Step 'Webcam record'
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
        $osName = 'FILL-ME: Windows version, e.g. Windows 11 23H2'
        if ($os) { $osName = '{0} (build {1})' -f $os.Caption.Trim(), $os.BuildNumber }
        if ([string]::IsNullOrWhiteSpace($adapterName)) { $adapterName = 'FILL-ME: adapter name' }
        if ([string]::IsNullOrWhiteSpace($adapterDriver)) { $adapterDriver = 'FILL-ME: driver version' }
        $cameras = @(Get-CameraName)
        $device = 'FILL-ME: camera name, e.g. Logitech C920 / OBS Virtual Camera'
        if ($cameras.Count -eq 1) { $device = $cameras[0] }
        elseif ($cameras.Count -gt 1) { $device = 'FILL-ME: which camera? ' + ($cameras -join ' / ') }
        $blankNotes = @{}
        foreach ($name in $Scenarios) { $blankNotes[$name] = '' }
        Write-WebcamRecord -Path $webcamPath -Operator 'FILL-ME: name of the person who ran this' `
            -GpuName $adapterName -GpuDriver $adapterDriver -OsName $osName -Device $device `
            -Mode 'FILL-ME: the mode the app picked, e.g. 1280x720@60 MJPG' -Obs $false `
            -ObsToggleNotes 'FILL-ME: what happened when OBS Virtual Camera was switched while the app was open' `
            -SameNamePairNotes 'FILL-ME: what happened with the second same-name camera, the one Windows calls (2)' `
            -Files $DefaultShotFiles -Notes $blankNotes -Delay 0 -ArtApproved $false -OwnerApproved $false
        Write-Note ("wrote $webcamPath - machine fields came from this machine, the rest are FILL-ME")
        if ($cameras.Count -gt 0) { Write-Note ('cameras found: ' + ($cameras -join ' | ')) }
    }
}

# ---------------------------------------------------------------- approve
function Approve-Record {
    # The sign-off is the one thing the machine cannot supply, and the one thing that is worthless on
    # its own: an owner who says "I approve" over an empty evidence folder has spent the one decision
    # only they can make on a record that proves nothing. Section 6 lists it third of three, so the
    # other two are checked here and the approval is refused until they hold.
    param([string]$Path, [string]$Folder)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "no webcam.json at $Path - the approval belongs to a record; run the script once without -Approve so it can write one"
    }
    try { $record = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "webcam.json is not readable JSON ($_)" }

    $python = Get-Python
    $validator = Join-Path $PSScriptRoot 'ar_hardware_signoff.py'
    $summaryPath = Join-Path $Folder '.approve-check.json'
    Write-Step 'Checking the record before it can be signed'
    & $python $validator --evidence $Folder --summary $summaryPath | Out-Null
    $unmet = @()
    if (Test-Path -LiteralPath $summaryPath) {
        try {
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            foreach ($problem in @($summary.problems)) { $unmet += $problem }
        }
        catch { $unmet += 'the validator summary could not be read' }
        Remove-Item -LiteralPath $summaryPath -Force
    }
    else { $unmet += 'the validator produced no summary; run -ValidateOnly and read its output' }

    if ($unmet.Count -gt 0) {
        Write-Host ''
        Write-Host '  refused: the owner signs a complete record, and this one is not:' -ForegroundColor Red
        foreach ($problem in $unmet) { Write-Host "    - $problem" -ForegroundColor Red }
        throw 'nothing was signed - fix the lines above, then run -Approve again'
    }

    Write-Step 'Sign-off: both approvals are about to be written'
    Write-Host "  operator              : $($record.operator)"
    Write-Host "  art_approved (now)    : $($record.art_approved)"
    Write-Host "  owner_approved (now)  : $($record.owner_approved)"
    Write-Host "  measured_delay_ms     : $($record.measured_delay_ms)"
    foreach ($name in $Scenarios) {
        Write-Host ("  {0,-22}: {1}" -f $name, $record.scenarios.$name.file)
    }
    $confirm = (Read-Host '  sign it? (yes to approve, anything else leaves the record as it is)').Trim()
    if ($confirm -notmatch '^(yes|y)$') {
        Write-Host '  not signed - the record is unchanged' -ForegroundColor Yellow
        return
    }

    $files = @{}
    $notes = @{}
    foreach ($name in $Scenarios) {
        $entry = $record.scenarios.$name
        if ($null -ne $entry -and $entry.file) { $files[$name] = $entry.file }
        else { $files[$name] = $DefaultShotFiles[$name] }
        if ($null -ne $entry -and $entry.notes) { $notes[$name] = $entry.notes }
        else { $notes[$name] = '' }
    }
    Write-WebcamRecord -Path $Path -Operator $record.operator -GpuName $record.machine.gpu `
        -GpuDriver $record.machine.driver -OsName $record.machine.os -Device $record.camera.device `
        -Mode $record.camera.mode -Obs ([bool]$record.camera.obs_virtual_camera) `
        -ObsToggleNotes $record.camera.obs_toggle_notes `
        -SameNamePairNotes $record.camera.same_name_pair_notes -Files $files -Notes $notes `
        -Delay ([double]$record.measured_delay_ms) -ArtApproved $true -OwnerApproved $true
    Write-Host ("  signed by $($record.operator) - ArProjectedParameterBindings stays Unverified until the binding PR lands") -ForegroundColor Green
}

if ($Approve) {
    Approve-Record -Path $webcamPath -Folder $evidencePath
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
