<#
.SYNOPSIS
  Vergleicht zwei Ausgabeverzeichnisse von scripts/perf-profile.ps1 (docs/PERF-GAME-LAYER.md): je Lauf, der in beiden liegt, `SimTool perf-compare`.

.DESCRIPTION
  Sucht in beiden Verzeichnissen die <name>.json der Messlaeufe (perf, perf-mission, sync-perf), ruft fuer jeden Namen, der in beiden vorkommt,
  `SimTool perf-compare <alt> <neu>` auf und schreibt
    - compare-<name>.txt   die volle Tabelle des Werkzeugs je Lauf,
    - compare.md           eine Uebersicht: je Lauf Tickzeit (Mittel, 99 %, Maximum), Allokation, Aufholtempo, Zahl der markierten Aenderungen.
  in das neue Verzeichnis (oder nach -OutFile). Laeufe, die nur auf einer Seite liegen, und abgelehnte Vergleiche (andere Parameter, anderer Endhash)
  stehen als solche in der Tabelle.

  Das Skript schlaegt nie wegen einer Zeit fehl (Zeiten sind rechnerabhaengig); Exitcode 1 nur, wenn kein Lauf vergleichbar war oder das Werkzeug fehlt.
  Nur mit pwsh (PowerShell 7) ausfuehren.

.EXAMPLE
  .\scripts\perf-compare.ps1 $env:TEMP\perf-alt $env:TEMP\perf-neu
  .\scripts\perf-compare.ps1 $env:TEMP\perf-alt $env:TEMP\perf-neu -ThresholdPercent 5 -AllowDifferentHash
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)][string]$OldDir,
    [Parameter(Mandatory, Position = 1)][string]$NewDir,
    # Ab welcher Aenderung (in Prozent des alten Werts) eine Kennzahl markiert wird.
    [double]$ThresholdPercent = 10,
    # Auch Laeufe mit anderem Endhash vergleichen (das sind andere Laeufe; die Tabelle traegt dann eine Warnung).
    [switch]$AllowDifferentHash,
    [string]$OutFile,
    # SimTool.exe; ohne Angabe <NewDir>\bin (dort legt perf-profile.ps1 es ab), sonst wird SimTool gebaut.
    [string]$Tool
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$OldDir = (Resolve-Path -LiteralPath $OldDir).Path
$NewDir = (Resolve-Path -LiteralPath $NewDir).Path
if (-not $OutFile) { $OutFile = Join-Path $NewDir 'compare.md' }

if (-not $Tool) {
    $Tool = Join-Path $NewDir 'bin' 'CnCNew.SimTool.exe'
    if (-not (Test-Path -LiteralPath $Tool)) {
        Write-Host '== Baue SimTool (Release) ...' -ForegroundColor Cyan
        dotnet build (Join-Path $repo 'tools' 'CnCNew.SimTool' 'CnCNew.SimTool.csproj') -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
        $built = Get-ChildItem -LiteralPath (Join-Path $repo 'tools' 'CnCNew.SimTool' 'bin' 'Release') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $Tool = Join-Path $built.FullName 'CnCNew.SimTool.exe'
    }
}
if (-not (Test-Path -LiteralPath $Tool)) { throw "SimTool nicht gefunden: $Tool" }

function Get-Runs([string]$directory) {
    $runs = @{}
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.json' -File) {
        # Nur Zusammenfassungen der Messwerkzeuge; andere JSON-Dateien im Verzeichnis werden uebergangen.
        try { $document = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable } catch { continue }
        if ($document -is [hashtable] -and $document['kind'] -ceq 'perf-summary') { $runs[$file.BaseName] = @{ Path = $file.FullName; Document = $document } }
    }
    $runs
}

function Format-Us($ns) { if ($null -eq $ns) { '-' } else { ([double]$ns / 1000).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) } }
function Format-Delta($old, $new) {
    if ($null -eq $old -or $null -eq $new) { return '-' }
    if ([double]$old -eq 0) { return ([double]$new -eq 0 ? '0.0 %' : 'n/a') }
    (100.0 * ([double]$new - [double]$old) / [double]$old).ToString('+0.0;-0.0;0.0', [Globalization.CultureInfo]::InvariantCulture) + ' %'
}
function Get-Value($document, [string]$section, [string]$name) {
    if ($document.ContainsKey($section) -and $document[$section] -is [hashtable] -and $document[$section].ContainsKey($name)) { $document[$section][$name] } else { $null }
}

$old = Get-Runs $OldDir
$new = Get-Runs $NewDir
$names = @(@($old.Keys) + @($new.Keys) | Sort-Object -Unique)
if ($names.Count -eq 0) { throw "Keine Zusammenfassungen (*.json mit kind = perf-summary) in $OldDir und $NewDir." }

$lines = @()
$lines += '# Leistungsvergleich'
$lines += ''
$lines += "Erzeugt von ``scripts/perf-compare.ps1`` am $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Schwelle $($ThresholdPercent.ToString([Globalization.CultureInfo]::InvariantCulture)) %. Zeiten in Mikrosekunden je Tick."
$lines += 'Zeiten haengen vom Rechner und seiner Last ab: das ist ein Bericht, kein Test. Die volle Tabelle je Lauf steht in `compare-<lauf>.txt`.'
$lines += ''
$lines += '| Lauf | Stand alt | Stand neu | Ergebnis | Mittel alt | Mittel neu | Aenderung | 99 % alt | 99 % neu | Aenderung | Maximum alt | Maximum neu | Bytes je Tick alt | Bytes je Tick neu | Ticks/s Wiedergabe alt | neu | Verschlechterungen | Verbesserungen | Endhash |'
$lines += '|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|'
$compared = 0
foreach ($name in $names) {
    if (-not $old.ContainsKey($name)) { $lines += "| $name | - | $($new[$name].Document['label']) | nur im neuen Verzeichnis | | | | | | | | | | | | | | | |"; continue }
    if (-not $new.ContainsKey($name)) { $lines += "| $name | $($old[$name].Document['label']) | - | nur im alten Verzeichnis | | | | | | | | | | | | | | | |"; continue }
    $a = $old[$name].Document
    $b = $new[$name].Document
    $report = Join-Path (Split-Path -Parent $OutFile) "compare-$name.txt"
    $arguments = @('perf-compare', $old[$name].Path, $new[$name].Path, '--threshold-percent', $ThresholdPercent.ToString([Globalization.CultureInfo]::InvariantCulture), '--out', $report)
    if ($AllowDifferentHash) { $arguments += '--allow-different-hash' }
    $output = & $Tool @arguments 2>&1
    $code = $LASTEXITCODE
    $result = 'verglichen'
    $regressions = ''
    $improvements = ''
    if ($code -eq 2) {
        $refusal = @($output | Where-Object { "$_" -cmatch '^REFUSED: ' } | Select-Object -First 1)
        $result = 'ABGELEHNT: ' + ("$refusal" -replace '^REFUSED: ', '' -replace '\|', '/')
    }
    elseif ($code -ne 0) { $result = "Fehler (Exitcode $code)" }
    else {
        $compared++
        $last = @($output | Where-Object { "$_" -cmatch '^\d+ regression\(s\) and \d+ improvement\(s\)' } | Select-Object -Last 1)
        if ("$last" -cmatch '^(\d+) regression\(s\) and (\d+) improvement\(s\)') { $regressions = $Matches[1]; $improvements = $Matches[2] }
        if (@($output | Where-Object { "$_" -cmatch '^WARNING: DIFFERENT FINAL HASH' }).Count -gt 0) { $result = 'verglichen, ANDERER ENDHASH' }
    }
    $hashOld = Get-Value $a 'results' 'finalHash'
    $hashNew = Get-Value $b 'results' 'finalHash'
    $hash = if ($hashOld -ceq $hashNew) { "``$hashOld``" } else { "``$hashOld`` / ``$hashNew``" }
    $cells = @(
        $name, $a['label'], $b['label'], $result,
        (Format-Us (Get-Value $a 'tick' 'meanNs')), (Format-Us (Get-Value $b 'tick' 'meanNs')), (Format-Delta (Get-Value $a 'tick' 'meanNs') (Get-Value $b 'tick' 'meanNs')),
        (Format-Us (Get-Value $a 'tick' 'p99Ns')), (Format-Us (Get-Value $b 'tick' 'p99Ns')), (Format-Delta (Get-Value $a 'tick' 'p99Ns') (Get-Value $b 'tick' 'p99Ns')),
        (Format-Us (Get-Value $a 'tick' 'maxNs')), (Format-Us (Get-Value $b 'tick' 'maxNs')),
        (Get-Value $a 'allocation' 'bytesPerTickMean'), (Get-Value $b 'allocation' 'bytesPerTickMean'),
        (Get-Value $a 'replay' 'ticksPerSecond'), (Get-Value $b 'replay' 'ticksPerSecond'), $regressions, $improvements, $hash)
    $lines += '| ' + (($cells | ForEach-Object { if ($null -eq $_) { '-' } else { "$_" } }) -join ' | ') + ' |'
    Write-Host "== $name`: $result; Mittel $(Format-Us (Get-Value $a 'tick' 'meanNs')) -> $(Format-Us (Get-Value $b 'tick' 'meanNs')) us ($(Format-Delta (Get-Value $a 'tick' 'meanNs') (Get-Value $b 'tick' 'meanNs')))"
}
[IO.File]::WriteAllText($OutFile, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "== Vergleich: $OutFile" -ForegroundColor Cyan
exit ($compared -gt 0 ? 0 : 1)
