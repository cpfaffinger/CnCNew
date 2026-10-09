<#
.SYNOPSIS
  Performance profile of the simulation (docs/PERFORMANCE.md, docs/handoffs/HANDOFF-PHASE18.md point 5): runs the fixed list of measurement runs strictly one after another.

.DESCRIPTION
  Builds SimTool (Release), copies the binaries into the output directory (so that a later build does not disturb the running measurement) and starts
  one after another, never in parallel:
    - matches with `SimTool perf ... --replay`: small map with 2 players, medium map with 2 and 4 players, large map with 2, 4 and 8 players,
      30 game minutes each (passed to the tool as `--minutes 30`: 54,000 ticks at 30 Hz, 108,000 at 60 Hz) or -Ticks ticks;
    - two large missions with `SimTool perf-mission` (MD_USA05, MD_GLA05), 100 and 300 game seconds each (`--seconds`; the run names
      `mission-<map>-3000` and `-9000` state this game time in original frames of 1/30 s, the same names at every rate).
  The script does not name the rate itself: without -TickRate SimTool runs with its default (the product rate, `GameData.DefaultSessionRate`), and the
  summary reads the rate from the tool's `summary:` line. A comparison of two commits needs the same rate on both sides.
  All real-time quantities (catch-up speed, share of one core) apply to the rate of the run; nothing is extrapolated to another rate.
  Per run it produces <name>.txt (full output), <name>.csv (one line per 300 ticks) and <name>.json (machine-readable summary for
  `SimTool perf-compare` or scripts/perf-compare.ps1); at the end summary.md (a table from the `summary:` lines).
  The output lies outside the repository under -OutDir (default: %TEMP%\cncnew-perf).

  Run with pwsh (PowerShell 7) only. For a quiet measurement nothing else may run on the machine (see docs/PERFORMANCE.md);
  the script does not check that, it only writes the CPU load before every run into the table.

.EXAMPLE
  .\scripts\perf-profile.ps1
  .\scripts\perf-profile.ps1 -Ticks 6000 -Only 'large-*'
  .\scripts\perf-profile.ps1 -TickRate 60 -Only 'large-8p'
  .\scripts\perf-profile.ps1 -Only 'mission-*' -MissionTicks 3000
  .\scripts\perf-profile.ps1 -TickRate 30 -Label 'comparison at 30 Hz'
  .\scripts\perf-profile.ps1 -Label 'b432fb7 before the change' -OutDir $env:TEMP\perf-old
#>
[CmdletBinding()]
param(
    # Simulation rate of the runs (30, 60 or 120; passed to SimTool as --tick-rate); 0 = the tool's default (the product rate).
    [ValidateSet(0, 30, 60, 120)][int]$TickRate = 0,
    # Ticks per match (ticks of the run's rate). When omitted 30 game minutes.
    [int]$Ticks = 0,
    # Ticks per mission run (every mission runs once per value; ticks of the run's rate). When omitted 100 and 300 game seconds.
    [string[]]$MissionTicks = @(),
    # Only runs whose name matches one of the patterns (wildcards allowed), e.g. 'small-*', 'large-8p', 'mission-*'.
    [string[]]$Only = @('*'),
    [string]$ContentDir,
    [string]$OutDir,
    # Upper limit per run; a run that exceeds it is terminated and listed as TIMEOUT.
    [int]$TimeoutMinutes = 60,
    # Free text that is written into every <name>.json (usually the commit). When omitted the short hash of HEAD; the tool itself does not ask git.
    [string]$Label,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
# `pwsh -File` passes "a,b" through as one argument: lists are therefore split at commas here.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
# Default budgets are game time; the tool converts them into ticks with its rate (--minutes/--seconds). A value in ticks is passed through as --ticks.
$matchBudget = if ($Ticks -gt 0) { @('--ticks', "$Ticks") } else { @('--minutes', '30') }
$missionBudgets = if ($MissionTicks.Count -eq 0) {
    @(@{ Suffix = '3000'; Budget = @('--seconds', '100') }, @{ Suffix = '9000'; Budget = @('--seconds', '300') })
}
else {
    @($MissionTicks | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { @{ Suffix = "$([int]$_)"; Budget = @('--ticks', "$([int]$_)") } })
}
$rateArgs = if ($TickRate -gt 0) { @('--tick-rate', "$TickRate") } else { @() }
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-perf' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
if ($OutDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw "The output directory must lie outside the repository: $OutDir" }

# Maps (cell counts as reported by `SimTool perf`, see docs/PERFORMANCE.md).
$small = 'Maps/Scorched Earth/Scorched Earth.map'        # 2 start positions
$medium = 'Maps/Tournament Tundra/Tournament Tundra.map' # 4 start positions
$large = 'Maps/Twilight Flame/Twilight Flame.map'        # 8 start positions

$runs = @(
    @{ Name = 'small-2p';  Command = 'perf'; Map = $small;  Players = 2; Budget = $matchBudget }
    @{ Name = 'medium-2p'; Command = 'perf'; Map = $medium; Players = 2; Budget = $matchBudget }
    @{ Name = 'medium-4p'; Command = 'perf'; Map = $medium; Players = 4; Budget = $matchBudget }
    @{ Name = 'large-2p';  Command = 'perf'; Map = $large;  Players = 2; Budget = $matchBudget }
    @{ Name = 'large-4p';  Command = 'perf'; Map = $large;  Players = 4; Budget = $matchBudget }
    @{ Name = 'large-8p';  Command = 'perf'; Map = $large;  Players = 8; Budget = $matchBudget }
)
foreach ($mission in 'MD_USA05', 'MD_GLA05') {
    foreach ($budget in $missionBudgets) {
        $runs += @{ Name = "mission-$mission-$($budget.Suffix)"; Command = 'perf-mission'; Map = "Maps/$mission/$mission.map"; Players = 0; Budget = $budget.Budget }
    }
}
$runs = @($runs | Where-Object { $name = $_.Name; @($Only | Where-Object { $name -like $_ }).Count -gt 0 })
if ($runs.Count -eq 0) { throw "No run matches -Only $($Only -join ', ')." }

$project = Join-Path $repo 'tools\CnCNew.SimTool\CnCNew.SimTool.csproj'
$binDir = Join-Path $OutDir 'bin'
if (-not $NoBuild) {
    Write-Host '== Building SimTool (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
    $built = Get-ChildItem -LiteralPath (Join-Path $repo 'tools\CnCNew.SimTool\bin\Release') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $built) { throw 'Release output of SimTool not found.' }
    if (Test-Path -LiteralPath $binDir) { Remove-Item -LiteralPath $binDir -Recurse -Force }
    Copy-Item -LiteralPath $built.FullName -Destination $binDir -Recurse
}
$exe = Join-Path $binDir 'CnCNew.SimTool.exe'
if (-not $Label) { $Label = (git -C $repo rev-parse --short HEAD) }
if (-not (Test-Path -LiteralPath $exe)) { throw "SimTool not found: $exe (start without -NoBuild)." }

function Get-CpuLoad {
    try { [int](Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average } catch { -1 }
}

$rows = @()
$failed = 0
foreach ($run in $runs) {
    $log = Join-Path $OutDir "$($run.Name).txt"
    $errorLog = Join-Path $OutDir "$($run.Name).err.txt"
    $csv = Join-Path $OutDir "$($run.Name).csv"
    $json = Join-Path $OutDir "$($run.Name).json"
    # An old summary must not stay behind as the result of this run when it fails.
    if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
    $arguments = @($run.Command, "`"$ContentDir`"", '--map', "`"$($run.Map)`"") + $run.Budget + $rateArgs + @('--csv', "`"$csv`"", '--json', "`"$json`"", '--label', "`"$($Label -replace '"', "'")`"")
    if ($run.Command -eq 'perf') { $arguments += @('--players', $run.Players, '--replay') }
    $load = Get-CpuLoad
    Write-Host "== $($run.Name): $($run.Command) $($run.Map), $($run.Budget -join ' ') at $(if ($TickRate -gt 0) { "$TickRate Hz" } else { "the tool's default rate" }) (CPU load before $load %)" -ForegroundColor Cyan
    $started = Get-Date
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError $errorLog
    $status = 'ok'
    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Stop-Process -Id $process.Id -Force
        $status = 'TIMEOUT'
    }
    else {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { $status = "exit code $($process.ExitCode)" }
    }
    if ($status -ne 'ok') { $failed++ }
    $seconds = [int]((Get-Date) - $started).TotalSeconds
    $values = @{}
    $line = Get-Content -LiteralPath $log -ErrorAction Ignore | Where-Object { $_ -cmatch '^summary: ' } | Select-Object -Last 1
    if ($line) {
        foreach ($pair in $line.Substring(9).Split(';')) {
            $at = $pair.IndexOf('=')
            if ($at -gt 0) { $values[$pair.Substring(0, $at)] = $pair.Substring($at + 1) }
        }
    }
    Write-Host "   $status after $seconds s; mean $($values['mean_us']) us per tick, final hash $($values['hash']); output: $log"
    $rows += [pscustomobject]@{ Run = $run; Status = $status; Seconds = $seconds; Load = $load; Values = $values }
}

function Field($row, [string]$key) { if ($row.Values.ContainsKey($key)) { $row.Values[$key] } else { '-' } }

# The rate of the runs is in the tool's summary lines (tick_rate); the script names none of its own.
$measuredRates = @($rows | Where-Object { $_.Values.ContainsKey('tick_rate') } | ForEach-Object { $_.Values['tick_rate'] } | Sort-Object -Unique)
$rateText = if ($measuredRates.Count -eq 1) { "$($measuredRates[0]) Hz" } elseif ($measuredRates.Count -eq 0) { 'unknown (no run wrote a summary line)' } else { "mixed ($($measuredRates -join ', ') Hz)" }
$lines = @()
$lines += '# Performance profile'
$lines += ''
$lines += "Generated by ``scripts/perf-profile.ps1`` on $(Get-Date -Format 'yyyy-MM-dd HH:mm'); commit $(git -C $repo rev-parse --short HEAD). Simulation rate $rateText; times in microseconds per tick unless stated otherwise. Catch-up speed and share of one core apply to this rate."
$hardware = Get-Content -LiteralPath (Join-Path $OutDir "$($runs[0].Name).txt") -ErrorAction Ignore | Where-Object { $_ -cmatch '^Hardware: ' } | Select-Object -First 1
if ($hardware) { $lines += ''; $lines += $hardware }
$lines += ''
$lines += '| Run | Map | Cells | Players | Ticks | Status | Mean | Median | 95 % | 99 % | Max | World | AI / scripts | Bytes per tick | GC 0/1/2 | Objects min/max/end | Replay us | Catch-up | Share of one core | Snapshot bytes | Write ms | Read ms | Final hash | incremental = full | Load before | Duration s |'
$lines += '|---|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---:|---:|---|---|---:|---:|'
foreach ($row in $rows) {
    $session = if ($row.Run.Command -eq 'perf') { Field $row 'ai_mean_us' } else { Field $row 'scripts_mean_us' }
    $catchup = if ($row.Values.ContainsKey('catchup')) { "$($row.Values['catchup'])x" } elseif ($row.Values.ContainsKey('catchup_live')) { "$($row.Values['catchup_live'])x (live)" } else { '-' }
    $cells = @(
        $row.Run.Name, ([IO.Path]::GetFileNameWithoutExtension($row.Run.Map)), (Field $row 'cells'), (Field $row 'players'), (Field $row 'ticks'), $row.Status,
        (Field $row 'mean_us'), (Field $row 'p50_us'), (Field $row 'p95_us'), (Field $row 'p99_us'), (Field $row 'max_us'), (Field $row 'world_mean_us'), $session,
        (Field $row 'alloc_bytes_per_tick'), (Field $row 'gc'), "$(Field $row 'objects_min')/$(Field $row 'objects_max')/$(Field $row 'objects_end')",
        (Field $row 'replay_us'), $catchup, "$(Field $row 'core_percent') %", (Field $row 'snapshot_bytes'), (Field $row 'snapshot_write_ms'), (Field $row 'snapshot_restore_ms'),
        "``$(Field $row 'hash')``", (Field $row 'incremental_equals_full'), "$($row.Load) %", $row.Seconds)
    $lines += '| ' + ($cells -join ' | ') + ' |'
}
$lines += ''
$lines += '## Tick phases (mean in microseconds per tick)'
$lines += ''
# Order as in the tick (TickPhase); unknown phases of a newer tool are appended at the end.
$order = 'Commands', 'Timers', 'Updates', 'Acquisition', 'Turrets', 'FiringTrackers', 'Events', 'Sales', 'Destroys', 'DelayedDamage', 'Spawns', 'SpawnEvents', 'Vision', 'VisionHash', 'Hash',
    'SkirmishAi', 'SkirmishVictory', 'MissionScripts', 'MissionEvents'
$present = @($rows | ForEach-Object { $_.Values.Keys } | Where-Object { $_ -like 'phase_*_us' } | Sort-Object -Unique)
$phaseKeys = @($order | ForEach-Object { "phase_$($_)_us" } | Where-Object { $present -contains $_ }) + @($present | Where-Object { $order -notcontains $_.Substring(6, $_.Length - 9) })
if ($phaseKeys.Count -gt 0) {
    $lines += '| Run | ' + (($phaseKeys | ForEach-Object { $_.Substring(6, $_.Length - 9) }) -join ' | ') + ' |'
    $lines += '|---|' + (($phaseKeys | ForEach-Object { '---:' }) -join '|') + '|'
    foreach ($row in $rows) { $lines += "| $($row.Run.Name) | " + (($phaseKeys | ForEach-Object { Field $row $_ }) -join ' | ') + ' |' }
}
$summary = Join-Path $OutDir 'summary.md'
[IO.File]::WriteAllText($summary, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "== Summary: $summary" -ForegroundColor Cyan

exit ($failed -gt 0 ? 1 : 0)
