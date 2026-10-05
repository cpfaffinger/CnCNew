<#
.SYNOPSIS
  Leistungsprofil der Simulation (docs/PERFORMANCE.md, docs/HANDOFF-PHASE18.md Punkt 5): fuehrt die feste Liste der Messlaeufe strikt nacheinander aus.

.DESCRIPTION
  Baut SimTool (Release), kopiert die Binaerdateien in das Ausgabeverzeichnis (damit ein spaeterer Build die laufende Messung nicht stoert) und startet
  nacheinander, nie parallel:
    - Gefechte mit `SimTool perf ... --replay`: kleine Karte mit 2 Spielern, mittlere Karte mit 2 und 4 Spielern, grosse Karte mit 2, 4 und 8 Spielern,
      je -Ticks Ticks (Standard 54.000 = 30 Spielminuten bei 30 Hz);
    - zwei grosse Missionen mit `SimTool perf-mission` (MD_USA05, MD_GLA05), je 3.000 und 9.000 Ticks.
  Je Lauf entstehen <name>.txt (volle Ausgabe) und <name>.csv (eine Zeile je 300 Ticks); am Ende summary.md (Tabelle aus den `summary:`-Zeilen).
  Die Ausgaben liegen ausserhalb des Repos unter -OutDir (Standard: %TEMP%\cncnew-perf).

  Nur mit pwsh (PowerShell 7) ausfuehren. Fuer eine ruhige Messung darf auf dem Rechner sonst nichts laufen (siehe docs/PERFORMANCE.md);
  das Skript prueft das nicht, es schreibt nur die CPU-Auslastung vor jedem Lauf in die Tabelle.

.EXAMPLE
  .\scripts\perf-profile.ps1
  .\scripts\perf-profile.ps1 -Ticks 6000 -Only 'large-*'
  .\scripts\perf-profile.ps1 -Only 'mission-*' -MissionTicks 3000
#>
[CmdletBinding()]
param(
    # Ticks je Gefecht.
    [int]$Ticks = 54000,
    # Ticks je Missionslauf (jede Mission laeuft einmal je Wert).
    [string[]]$MissionTicks = @('3000', '9000'),
    # Nur Laeufe, deren Name auf eines der Muster passt (Platzhalter erlaubt), z. B. 'small-*', 'large-8p', 'mission-*'.
    [string[]]$Only = @('*'),
    [string]$ContentDir,
    [string]$OutDir,
    # Obergrenze je Lauf; ein Lauf, der sie ueberschreitet, wird beendet und als TIMEOUT gefuehrt.
    [int]$TimeoutMinutes = 60,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
# `pwsh -File` reicht "a,b" als ein Argument durch: Listen deshalb hier selbst an Kommas trennen.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$missionTickCounts = @($MissionTicks | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { [int]$_ })
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Inhaltsverzeichnis nicht gefunden: $ContentDir" }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-perf' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
if ($OutDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw "Das Ausgabeverzeichnis muss ausserhalb des Repos liegen: $OutDir" }

# Karten (Zellgroessen laut `SimTool perf`, siehe docs/PERFORMANCE.md).
$small = 'Maps/Scorched Earth/Scorched Earth.map'        # 2 Startpositionen
$medium = 'Maps/Tournament Tundra/Tournament Tundra.map' # 4 Startpositionen
$large = 'Maps/Twilight Flame/Twilight Flame.map'        # 8 Startpositionen

$runs = @(
    @{ Name = 'small-2p';  Command = 'perf'; Map = $small;  Players = 2; Ticks = $Ticks }
    @{ Name = 'medium-2p'; Command = 'perf'; Map = $medium; Players = 2; Ticks = $Ticks }
    @{ Name = 'medium-4p'; Command = 'perf'; Map = $medium; Players = 4; Ticks = $Ticks }
    @{ Name = 'large-2p';  Command = 'perf'; Map = $large;  Players = 2; Ticks = $Ticks }
    @{ Name = 'large-4p';  Command = 'perf'; Map = $large;  Players = 4; Ticks = $Ticks }
    @{ Name = 'large-8p';  Command = 'perf'; Map = $large;  Players = 8; Ticks = $Ticks }
)
foreach ($mission in 'MD_USA05', 'MD_GLA05') {
    foreach ($count in $missionTickCounts) {
        $runs += @{ Name = "mission-$mission-$count"; Command = 'perf-mission'; Map = "Maps/$mission/$mission.map"; Players = 0; Ticks = $count }
    }
}
$runs = @($runs | Where-Object { $name = $_.Name; @($Only | Where-Object { $name -like $_ }).Count -gt 0 })
if ($runs.Count -eq 0) { throw "Kein Lauf passt auf -Only $($Only -join ', ')." }

$project = Join-Path $repo 'tools\CnCNew.SimTool\CnCNew.SimTool.csproj'
$binDir = Join-Path $OutDir 'bin'
if (-not $NoBuild) {
    Write-Host '== Baue SimTool (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    $built = Get-ChildItem -LiteralPath (Join-Path $repo 'tools\CnCNew.SimTool\bin\Release') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $built) { throw 'Release-Ausgabe von SimTool nicht gefunden.' }
    if (Test-Path -LiteralPath $binDir) { Remove-Item -LiteralPath $binDir -Recurse -Force }
    Copy-Item -LiteralPath $built.FullName -Destination $binDir -Recurse
}
$exe = Join-Path $binDir 'CnCNew.SimTool.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "SimTool nicht gefunden: $exe (ohne -NoBuild starten)." }

function Get-CpuLoad {
    try { [int](Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average } catch { -1 }
}

$rows = @()
$failed = 0
foreach ($run in $runs) {
    $log = Join-Path $OutDir "$($run.Name).txt"
    $errorLog = Join-Path $OutDir "$($run.Name).err.txt"
    $csv = Join-Path $OutDir "$($run.Name).csv"
    $arguments = @($run.Command, "`"$ContentDir`"", '--map', "`"$($run.Map)`"", '--ticks', $run.Ticks, '--csv', "`"$csv`"")
    if ($run.Command -eq 'perf') { $arguments += @('--players', $run.Players, '--replay') }
    $load = Get-CpuLoad
    Write-Host "== $($run.Name): $($run.Command) $($run.Map), $($run.Ticks) Ticks (CPU-Auslastung vorher $load %)" -ForegroundColor Cyan
    $started = Get-Date
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError $errorLog
    $status = 'ok'
    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        Stop-Process -Id $process.Id -Force
        $status = 'TIMEOUT'
    }
    else {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { $status = "Exitcode $($process.ExitCode)" }
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
    Write-Host "   $status nach $seconds s; Mittel $($values['mean_us']) us je Tick, Endhash $($values['hash']); Ausgabe: $log"
    $rows += [pscustomobject]@{ Run = $run; Status = $status; Seconds = $seconds; Load = $load; Values = $values }
}

function Field($row, [string]$key) { if ($row.Values.ContainsKey($key)) { $row.Values[$key] } else { '-' } }

$lines = @()
$lines += '# Leistungsprofil'
$lines += ''
$lines += "Erzeugt von ``scripts/perf-profile.ps1`` am $(Get-Date -Format 'yyyy-MM-dd HH:mm'); Stand $(git -C $repo rev-parse --short HEAD). Zeiten in Mikrosekunden je Tick, wenn nicht anders angegeben."
$hardware = Get-Content -LiteralPath (Join-Path $OutDir "$($runs[0].Name).txt") -ErrorAction Ignore | Where-Object { $_ -cmatch '^Hardware: ' } | Select-Object -First 1
if ($hardware) { $lines += ''; $lines += $hardware }
$lines += ''
$lines += '| Lauf | Karte | Zellen | Spieler | Ticks | Status | Mittel | Median | 95 % | 99 % | Max | Welt | KI / Skripte | Bytes je Tick | GC 0/1/2 | Objekte min/max/Ende | Wiedergabe us | Aufholen 30 Hz | Aufholen 60 Hz | Kernanteil 60 Hz | Snapshot Bytes | Schreiben ms | Lesen ms | Endhash | inkrementell = voll | Last vorher | Dauer s |'
$lines += '|---|---|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|'
foreach ($row in $rows) {
    $session = if ($row.Run.Command -eq 'perf') { Field $row 'ai_mean_us' } else { Field $row 'scripts_mean_us' }
    $catchup60 = if ($row.Values.ContainsKey('catchup60')) { "$($row.Values['catchup60'])x" } elseif ($row.Values.ContainsKey('catchup60_live')) { "$($row.Values['catchup60_live'])x (live)" } else { '-' }
    $catchup30 = if ($row.Values.ContainsKey('catchup30')) { "$($row.Values['catchup30'])x" } else { '-' }
    $cells = @(
        $row.Run.Name, ([IO.Path]::GetFileNameWithoutExtension($row.Run.Map)), (Field $row 'cells'), (Field $row 'players'), (Field $row 'ticks'), $row.Status,
        (Field $row 'mean_us'), (Field $row 'p50_us'), (Field $row 'p95_us'), (Field $row 'p99_us'), (Field $row 'max_us'), (Field $row 'world_mean_us'), $session,
        (Field $row 'alloc_bytes_per_tick'), (Field $row 'gc'), "$(Field $row 'objects_min')/$(Field $row 'objects_max')/$(Field $row 'objects_end')",
        (Field $row 'replay_us'), $catchup30, $catchup60, "$(Field $row 'core60_percent') %", (Field $row 'snapshot_bytes'), (Field $row 'snapshot_write_ms'), (Field $row 'snapshot_restore_ms'),
        "``$(Field $row 'hash')``", (Field $row 'incremental_equals_full'), "$($row.Load) %", $row.Seconds)
    $lines += '| ' + ($cells -join ' | ') + ' |'
}
$lines += ''
$lines += '## Tickphasen (Mittel in Mikrosekunden je Tick)'
$lines += ''
# Reihenfolge wie im Tick (TickPhase); unbekannte Phasen eines neueren Werkzeugs haengen hinten an.
$order = 'Commands', 'Timers', 'Updates', 'Acquisition', 'Turrets', 'FiringTrackers', 'Events', 'Sales', 'Destroys', 'DelayedDamage', 'Spawns', 'SpawnEvents', 'Vision', 'VisionHash', 'Hash',
    'SkirmishAi', 'SkirmishVictory', 'MissionScripts', 'MissionEvents'
$present = @($rows | ForEach-Object { $_.Values.Keys } | Where-Object { $_ -like 'phase_*_us' } | Sort-Object -Unique)
$phaseKeys = @($order | ForEach-Object { "phase_$($_)_us" } | Where-Object { $present -contains $_ }) + @($present | Where-Object { $order -notcontains $_.Substring(6, $_.Length - 9) })
if ($phaseKeys.Count -gt 0) {
    $lines += '| Lauf | ' + (($phaseKeys | ForEach-Object { $_.Substring(6, $_.Length - 9) }) -join ' | ') + ' |'
    $lines += '|---|' + (($phaseKeys | ForEach-Object { '---:' }) -join '|') + '|'
    foreach ($row in $rows) { $lines += "| $($row.Run.Name) | " + (($phaseKeys | ForEach-Object { Field $row $_ }) -join ' | ') + ' |' }
}
$summary = Join-Path $OutDir 'summary.md'
[IO.File]::WriteAllText($summary, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "== Zusammenfassung: $summary" -ForegroundColor Cyan

exit ($failed -gt 0 ? 1 : 0)
