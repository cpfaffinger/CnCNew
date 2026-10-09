<#
.SYNOPSIS
  Leistungsprofil der Simulation (docs/PERFORMANCE.md, docs/HANDOFF-PHASE18.md Punkt 5): fuehrt die feste Liste der Messlaeufe strikt nacheinander aus.

.DESCRIPTION
  Baut SimTool (Release), kopiert die Binaerdateien in das Ausgabeverzeichnis (damit ein spaeterer Build die laufende Messung nicht stoert) und startet
  nacheinander, nie parallel:
    - Gefechte mit `SimTool perf ... --replay`: kleine Karte mit 2 Spielern, mittlere Karte mit 2 und 4 Spielern, grosse Karte mit 2, 4 und 8 Spielern,
      je 30 Spielminuten (als `--minutes 30` an das Werkzeug gereicht: 54.000 Ticks bei 30 Hz, 108.000 bei 60 Hz) oder -Ticks Ticks;
    - zwei grosse Missionen mit `SimTool perf-mission` (MD_USA05, MD_GLA05), je 100 und 300 Spielsekunden (`--seconds`; die Laufnamen
      `mission-<Karte>-3000` und `-9000` nennen diese Spielzeit in Originalbildern von 1/30 s, bei jeder Rate dieselben Namen).
  Die Rate nennt das Skript nicht selbst: ohne -TickRate laeuft SimTool mit seinem Standard (der Produktrate, `GameData.DefaultSessionRate`), und die
  Zusammenfassung liest die Rate aus der `summary:`-Zeile des Werkzeugs. Ein Vergleich zweier Staende braucht dieselbe Rate auf beiden Seiten.
  Alle Echtzeit-Groessen (Aufholtempo, Kernanteil) gelten fuer die Rate des Laufs; es wird nicht auf eine andere Rate hochgerechnet.
  Je Lauf entstehen <name>.txt (volle Ausgabe), <name>.csv (eine Zeile je 300 Ticks) und <name>.json (maschinenlesbare Zusammenfassung fuer
  `SimTool perf-compare` bzw. scripts/perf-compare.ps1); am Ende summary.md (Tabelle aus den `summary:`-Zeilen).
  Die Ausgaben liegen ausserhalb des Repos unter -OutDir (Standard: %TEMP%\cncnew-perf).

  Nur mit pwsh (PowerShell 7) ausfuehren. Fuer eine ruhige Messung darf auf dem Rechner sonst nichts laufen (siehe docs/PERFORMANCE.md);
  das Skript prueft das nicht, es schreibt nur die CPU-Auslastung vor jedem Lauf in die Tabelle.

.EXAMPLE
  .\scripts\perf-profile.ps1
  .\scripts\perf-profile.ps1 -Ticks 6000 -Only 'large-*'
  .\scripts\perf-profile.ps1 -TickRate 60 -Only 'large-8p'
  .\scripts\perf-profile.ps1 -Only 'mission-*' -MissionTicks 3000
  .\scripts\perf-profile.ps1 -TickRate 30 -Label 'Vergleich bei 30 Hz'
  .\scripts\perf-profile.ps1 -Label 'b432fb7 vor der Aenderung' -OutDir $env:TEMP\perf-alt
#>
[CmdletBinding()]
param(
    # Simulationsrate der Laeufe (30, 60 oder 120; wird als --tick-rate an SimTool gereicht); 0 = Standard des Werkzeugs (die Produktrate).
    [ValidateSet(0, 30, 60, 120)][int]$TickRate = 0,
    # Ticks je Gefecht (Ticks der Rate des Laufs). Ohne Angabe 30 Spielminuten.
    [int]$Ticks = 0,
    # Ticks je Missionslauf (jede Mission laeuft einmal je Wert; Ticks der Rate des Laufs). Ohne Angabe 100 und 300 Spielsekunden.
    [string[]]$MissionTicks = @(),
    # Nur Laeufe, deren Name auf eines der Muster passt (Platzhalter erlaubt), z. B. 'small-*', 'large-8p', 'mission-*'.
    [string[]]$Only = @('*'),
    [string]$ContentDir,
    [string]$OutDir,
    # Obergrenze je Lauf; ein Lauf, der sie ueberschreitet, wird beendet und als TIMEOUT gefuehrt.
    [int]$TimeoutMinutes = 60,
    # Freitext, der in jede <name>.json geschrieben wird (ueblich: der Commit). Ohne Angabe der Kurz-Hash von HEAD; das Werkzeug selbst fragt git nicht.
    [string]$Label,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
# `pwsh -File` reicht "a,b" als ein Argument durch: Listen deshalb hier selbst an Kommas trennen.
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
# Budgets ohne Angabe sind Spielzeit; das Werkzeug rechnet sie mit seiner Rate in Ticks um (--minutes/--seconds). Eine Angabe in Ticks geht als --ticks durch.
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
if (-not $Label) { $Label = (git -C $repo rev-parse --short HEAD) }
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
    $json = Join-Path $OutDir "$($run.Name).json"
    # Eine alte Zusammenfassung darf nicht als Ergebnis dieses Laufs liegen bleiben, wenn er scheitert.
    if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
    $arguments = @($run.Command, "`"$ContentDir`"", '--map', "`"$($run.Map)`"") + $run.Budget + $rateArgs + @('--csv', "`"$csv`"", '--json', "`"$json`"", '--label', "`"$($Label -replace '"', "'")`"")
    if ($run.Command -eq 'perf') { $arguments += @('--players', $run.Players, '--replay') }
    $load = Get-CpuLoad
    Write-Host "== $($run.Name): $($run.Command) $($run.Map), $($run.Budget -join ' ') bei $(if ($TickRate -gt 0) { "$TickRate Hz" } else { 'der Standardrate des Werkzeugs' }) (CPU-Auslastung vorher $load %)" -ForegroundColor Cyan
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

# Die Rate der Laeufe steht in den summary-Zeilen des Werkzeugs (tick_rate); das Skript nennt keine eigene.
$measuredRates = @($rows | Where-Object { $_.Values.ContainsKey('tick_rate') } | ForEach-Object { $_.Values['tick_rate'] } | Sort-Object -Unique)
$rateText = if ($measuredRates.Count -eq 1) { "$($measuredRates[0]) Hz" } elseif ($measuredRates.Count -eq 0) { 'unbekannt (kein Lauf hat eine summary-Zeile geschrieben)' } else { "uneinheitlich ($($measuredRates -join ', ') Hz)" }
$lines = @()
$lines += '# Leistungsprofil'
$lines += ''
$lines += "Erzeugt von ``scripts/perf-profile.ps1`` am $(Get-Date -Format 'yyyy-MM-dd HH:mm'); Stand $(git -C $repo rev-parse --short HEAD). Simulationsrate $rateText; Zeiten in Mikrosekunden je Tick, wenn nicht anders angegeben. Aufholtempo und Kernanteil gelten fuer diese Rate."
$hardware = Get-Content -LiteralPath (Join-Path $OutDir "$($runs[0].Name).txt") -ErrorAction Ignore | Where-Object { $_ -cmatch '^Hardware: ' } | Select-Object -First 1
if ($hardware) { $lines += ''; $lines += $hardware }
$lines += ''
$lines += '| Lauf | Karte | Zellen | Spieler | Ticks | Status | Mittel | Median | 95 % | 99 % | Max | Welt | KI / Skripte | Bytes je Tick | GC 0/1/2 | Objekte min/max/Ende | Wiedergabe us | Aufholen | Kernanteil | Snapshot Bytes | Schreiben ms | Lesen ms | Endhash | inkrementell = voll | Last vorher | Dauer s |'
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
