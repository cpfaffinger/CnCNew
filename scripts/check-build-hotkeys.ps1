<#
.SYNOPSIS
  Regression check for the hot keys of the command bar (builder's construction menu, production buttons): replays key presses through the real client
  (headless smoke test with --input-script, docs/GAME-HUD.md section 17) in the scenes exit-<faction>-all and asserts on the client's report.

.DESCRIPTION
  Runs the Release client once per faction (America, China, GLA), each bounded in time. Per faction: select the war factory, the barracks and the command
  centre and press the hot key of a unit button each (four units are queued), then select the builder, open a placement by key and cancel it, open another
  one by key and place the building. Among the keys are S (also bound to STOP by CommandMap.ini) and, for America and China, F (also fast forward): before
  the fix of 7 October 2026 a key with a meta action on its key-down never reached the button.
  Exit code 0 when every check passes, 1 otherwise. The mouse setup does not matter (-Mouse classic gives the same result).

.EXAMPLE
  .\scripts\check-build-hotkeys.ps1
  .\scripts\check-build-hotkeys.ps1 -Faction GLA -NoBuild
  .\scripts\check-build-hotkeys.ps1 -Mouse classic
  .\scripts\check-build-hotkeys.ps1 -NoBuild -TickRate 60
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [ValidateSet('all', 'America', 'China', 'GLA')]
    [string]$Faction = 'all',
    [ValidateSet('', 'alternate', 'classic')]
    [string]$Mouse = '',
    # Simulation rate (30, 60 or 120 Hz); 0 = the client's default. Frame numbers of the input scripts and --frames count original frames (1/30 s) at every rate.
    [int]$TickRate = 0,
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
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
$work = Join-Path ([IO.Path]::GetTempPath()) ("cncnew-hotkey-check-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$failures = 0

# Per faction: the units the keys queue (in the order of the script) and the two buildings of the builder (placement opened and cancelled; placed).
$plan = [ordered]@{
    'America' = @{ Units = @('AmericaVehicleSentryDrone', 'AmericaTankCrusader', 'AmericaInfantryRanger', 'AmericaVehicleDozer'); Opened = 'AmericaAirfield'; Placed = 'AmericaPowerPlant' }
    'China'   = @{ Units = @('ChinaVehicleListeningOutpost', 'ChinaTankBattleMaster', 'ChinaInfantryRedguard', 'ChinaVehicleDozer'); Opened = 'ChinaAirfield'; Placed = 'ChinaPowerPlant' }
    'GLA'     = @{ Units = @('GLATankScorpion', 'GLAVehicleTechnical', 'GLAInfantrySaboteur', 'GLAInfantryWorker'); Opened = 'GLATunnelNetwork'; Placed = 'GLABarracks' }
}

foreach ($name in $plan.Keys) {
    if ($Faction -ne 'all' -and $Faction -ne $name) { continue }
    $entry = $plan[$name]
    $lower = $name.ToLowerInvariant()
    $out = Join-Path $work "$lower.txt"
    $all = @('--smoke-test', '--content-dir', $ContentDir, '--size', '1280x720', '--frames', '320', '--scenario', "exit-$lower-all", '--input-script', (Join-Path $repo "scripts\input\hotkeys-$lower.txt"))
    if ($Mouse) { $all += @('--mouse', $Mouse) }
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
        $expect = @{ unresolved = 0; commandsRejected = 0; invalidOrders = 0; sitesCreated = 1; commandsAccepted = $entry.Units.Count + 1 }
        foreach ($key in $expect.Keys) {
            if ($summary -notmatch "\b$key=(\d+)") { $problems += "summary has no '$key'"; continue }
            if ([int]$Matches[1] -ne $expect[$key]) { $problems += "$key=$($Matches[1]), expected $($expect[$key])" }
        }
    }
    foreach ($unit in $entry.Units) {
        if (-not ($lines | Where-Object { $_ -like "*command QueueProductionCommand accepted*Template = $unit*" })) { $problems += "the key of $unit queued nothing" }
    }
    if (-not ($lines | Where-Object { $_ -like "*command mode None -> Placement (DozerConstruct $($entry.Opened))*" })) { $problems += "the key of $($entry.Opened) opened no placement" }
    if (-not ($lines | Where-Object { $_ -like "*command mode None -> Placement (DozerConstruct $($entry.Placed))*" })) { $problems += "the key of $($entry.Placed) opened no placement" }
    if (-not ($lines | Where-Object { $_ -like "*construction site created: $($entry.Placed)#*" })) { $problems += "$($entry.Placed) was not placed" }

    if ($problems.Count -eq 0) { Write-Host "PASS $name  ($summary)" }
    else {
        $failures++
        Write-Host "FAIL $name : $($problems -join '; ')" -ForegroundColor Red
        $lines | Where-Object { $_ -like 'input-script*' } | ForEach-Object { Write-Host "    $_" }
    }
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed; client output in $work" -ForegroundColor Red
    exit 1
}
Write-Host "All hot key checks passed (client output in $work)."
exit 0
