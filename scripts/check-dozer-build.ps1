<#
.SYNOPSIS
  Regression check for "the dozer cannot build": replays select dozer -> construct button -> ground click through the real client
  (headless smoke test with --input-script, see docs/GAME-HUD.md section 17) and asserts on the client's exit report.

.DESCRIPTION
  Runs the Release client five times, each bounded in time:
    mission GC_ChemGeneral and skirmish Alpine Assault, each at 1280x720 and 1920x1080: the building must be placed and finished;
    mission GC_ChemGeneral during its opening cinematic: clicks must be dropped and the script must hand the input back.
  -Quick runs only the mission at 1280x720 and the cinematic check.
  Exit code 0 when every check passes, 1 otherwise.

.EXAMPLE
  .\scripts\check-dozer-build.ps1
  .\scripts\check-dozer-build.ps1 -Quick -NoBuild
  .\scripts\check-dozer-build.ps1 -NoBuild -TickRate 60
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [switch]$Quick,
    # Simulation rate (30, 60 or 120 Hz); 0 = the client's default. Frame numbers of the input scripts and --frames count original frames (1/30 s) at every rate.
    [int]$TickRate = 0,
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 420
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}
$exe = Get-ChildItem (Join-Path $repo 'src\CnCNew.Client.Stride\bin\Release') -Recurse -Filter 'CnCNew.Client.Stride.exe' | Select-Object -First 1
if (-not $exe) { throw 'Release client not found; run without -NoBuild.' }

# The client's console output of each run; left in the temp directory for a look after a failure.
$work = Join-Path ([IO.Path]::GetTempPath()) ("cncnew-dozer-check-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$failures = 0

function Invoke-Replay([string]$Name, [string[]]$GameArgs, [string]$Size, [int]$Frames, [string]$Script, [hashtable]$Expect, [string[]]$ExpectLines, [int[]]$LockedFrames = @()) {
    $out = Join-Path $work "$Name.txt"
    $all = @('--smoke-test', '--content-dir', $ContentDir, '--size', $Size, '--frames', "$Frames", '--input-script', (Join-Path $repo "scripts\input\$Script")) + $GameArgs
    if ($TickRate -gt 0) { $all += @('--tick-rate', "$TickRate") }
    $quoted = $all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath $exe.FullName -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    $problems = @()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $problems += "timed out after $TimeoutSeconds s"
    }
    elseif ($process.ExitCode -ne 0) { $problems += "exit code $($process.ExitCode)" }
    $lines = if (Test-Path $out) { Get-Content $out } else { @() }
    $summary = $lines | Where-Object { $_ -like 'input-script summary:*' } | Select-Object -Last 1
    if (-not $summary) { $problems += 'no input-script summary in the output' }
    else {
        foreach ($key in $Expect.Keys) {
            if ($summary -notmatch "\b$key=(\d+)") { $problems += "summary has no '$key'"; continue }
            $value = [int]$Matches[1]
            $want = $Expect[$key]
            $ok = if ($want -is [string] -and $want.StartsWith('>=')) { $value -ge [int]$want.Substring(2) } else { $value -eq [int]$want }
            if (-not $ok) { $problems += "$key=$value, expected $want" }
        }
    }
    foreach ($pattern in $ExpectLines) {
        if (-not ($lines | Where-Object { $_ -like $pattern })) { $problems += "no output line like '$pattern'" }
    }
    if ($LockedFrames.Count -eq 2) {
        # The client counts the input lock in simulation ticks and names its rate; the bounds are original frames (1/30 s), the same game time at every rate.
        $rateLine = $lines | Where-Object { $_ -cmatch '^session: simulation rate (\d+) Hz' } | Select-Object -Last 1
        $lockLine = $lines | Where-Object { $_ -cmatch '^mission presentation: input locked for (\d+) ticks' } | Select-Object -Last 1
        if (-not $rateLine -or -not $lockLine) { $problems += 'no simulation rate or no input lock count in the output' }
        else {
            $null = $rateLine -cmatch 'rate (\d+) Hz'; $rate = [int]$Matches[1]
            $null = $lockLine -cmatch 'for (\d+) ticks'; $locked = [long]$Matches[1] * 30 / $rate
            if ($locked -lt $LockedFrames[0] -or $locked -gt $LockedFrames[1]) { $problems += "input locked for $locked original frames, expected $($LockedFrames[0]) to $($LockedFrames[1])" }
        }
    }
    if ($problems.Count -eq 0) { Write-Host "PASS $Name  ($summary)" }
    else {
        $script:failures++
        Write-Host "FAIL $Name : $($problems -join '; ')" -ForegroundColor Red
        $lines | Where-Object { $_ -like 'input-script*' -or $_ -like 'mission presentation*' } | ForEach-Object { Write-Host "    $_" }
    }
}

$built = @{ unresolved = 0; commandsAccepted = '>=1'; commandsRejected = 0; sitesCreated = 1; sitesCompleted = 1 }
$mission = @('--mission', 'Maps/GC_ChemGeneral/GC_ChemGeneral.map')
$match = @('--match', 'Maps/Alpine Assault/Alpine Assault.map')
Invoke-Replay 'mission-1280x720' $mission '1280x720' 1400 'dozer-build.txt' $built @('mission presentation:*locked at exit False*')
Invoke-Replay 'mission-intro-clicks' $mission '1280x720' 800 'clicks-during-intro.txt' @{ unresolved = 0; selectionChanges = 0; commandsAccepted = 0; sitesCreated = 0 } @('mission presentation: input locked for * ticks, locked at exit False, letterbox at exit False*', 'input-script: at exit*command mode None*') @(700, 799)
if (-not $Quick) {
    Invoke-Replay 'mission-1920x1080' $mission '1920x1080' 1400 'dozer-build.txt' $built @()
    Invoke-Replay 'match-1280x720' $match '1280x720' 1400 'dozer-build.txt' $built @()
    Invoke-Replay 'match-1920x1080' $match '1920x1080' 1400 'dozer-build.txt' $built @()
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed; client output in $work" -ForegroundColor Red
    exit 1
}
Write-Host "All dozer build checks passed (client output in $work)."
exit 0
