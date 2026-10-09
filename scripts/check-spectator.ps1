<#
.SYNOPSIS
  Pruefung des Zuschauermodus ohne Fenster: dasselbe KI-gegen-KI-Gefecht wie scripts/demo-spectator.ps1 als Rauchtest, mit gedrueckten Zuschauertasten.

.DESCRIPTION
  Startet den Release-Client mit --spectate --smoke-test --frames N und dem Eingabeskript scripts/input/spectator.txt (Tafeln, KI-Kamera 1/2 folgen, Uebersicht,
  Nebelschalter, Pause und Tempo, dazu Klicks und Pfeiltasten, die nichts befehlen duerfen) und prueft an der Ausgabe des Clients:
    - das Gefecht lief (Ticks, Befehle beider KI), kein Befehl des Zuschauers wurde aufgezeichnet;
    - die Tafeln hatten Daten (Geld, Einheiten, Gebaeude, gesammelte Vorraete), alle vier Tafeln, alle Nebelsichten und alle Tempi waren an;
    - die simulierten KI-Kameras fanden Blickpunkte;
    - die Kamera erreichte die Uebersichtshoehe weit ueber der Grenze eines Spielers; die Bildkosten nah und fern werden ausgegeben.
  Alle 300 Bilder entsteht ein PNG; sie liegen danach benannt unter -OutDir (Standard: %TEMP%\cncnew-spectator-check, ausserhalb des Repos).
  Die Bilder belegen, dass etwas gezeichnet wird - ob es richtig aussieht und lesbar ist, kann nur ein Mensch beurteilen (Checkliste in docs/SPECTATOR-MODE.md).
  Exitcode 0, wenn alle Pruefungen bestehen, sonst 1.

.EXAMPLE
  pwsh scripts/check-spectator.ps1
  pwsh scripts/check-spectator.ps1 -NoBuild -Seed 11 -TickRate 60
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [string]$OutDir,
    [string]$Map = 'Maps/Alpine Assault/Alpine Assault.map',
    [string]$FactionA = 'FactionAmerica',
    [string]$FactionB = 'FactionChina',
    [int]$Seed = 7,
    # Simulationsrate (30, 60 oder 120 Hz); 0 = Standard des Clients. Die Bildnummern des Eingabeskripts sind Originalbilder (1/30 s) bei jeder Rate.
    [int]$TickRate = 0,
    [string]$Size = '1280x720',
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 1500
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Inhaltsverzeichnis nicht gefunden: $ContentDir (mit -ContentDir angeben)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-spectator-check' }
if (Test-Path -LiteralPath $OutDir) { Get-ChildItem -LiteralPath $OutDir -File | Remove-Item -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Baue Client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}
$exe = Get-ChildItem (Join-Path $repo 'src\CnCNew.Client.Stride\bin\Release') -Recurse -Filter 'CnCNew.Client.Stride.exe' | Select-Object -First 1
if (-not $exe) { throw 'Release-Client nicht gefunden; ohne -NoBuild starten.' }

# Was das Bild nach je 300 Bildern zeigt (scripts/input/spectator.txt stellt es in den Bildern davor ein).
$shots = [ordered]@{
    300  = '01-tafel-produktion'
    600  = '02-tafel-einheiten'
    900  = '03-tafel-verluste'
    1200 = '04-tafel-wirtschaft'
    1500 = '05-uebersicht-ganze-karte'
    1800 = '06-uebersicht-nebel-spieler-1'
    2100 = '07-folgt-ki-kamera-1'
    2400 = '08-folgt-ki-kamera-2'
    2700 = '09-sprung-zu-ki-1-tafel-einheiten'
    3000 = '10-tafeln-aus-tempo-1'
}
$frames = 3010
$film = Join-Path $OutDir 'film'
$out = Join-Path $OutDir 'client.txt'
$all = @('--smoke-test', '--content-dir', $ContentDir, '--size', $Size, '--match', $Map, '--faction', $FactionA, '--opponent', $FactionB, '--spectate', '--seed', "$Seed",
    '--spectator-speed', '3', '--frames', "$frames", '--mute', '--input-script', (Join-Path $repo 'scripts\input\spectator.txt'), '--filmstrip', $film, '--every', '300')
if ($TickRate -gt 0) { $all += @('--tick-rate', "$TickRate") }
$quoted = $all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
Write-Host "== Starte Zuschauer-Rauchtest: Seed $Seed, $Map, $FactionA gegen $FactionB, $frames Bilder ..." -ForegroundColor Cyan
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $exe.FullName -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err"
$problems = [Collections.Generic.List[string]]::new()
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    $problems.Add("Zeitlimit von $TimeoutSeconds s ueberschritten")
}
elseif ($process.ExitCode -ne 0) { $problems.Add("Exitcode $($process.ExitCode)") }
Write-Host "   Laufzeit $([int]$watch.Elapsed.TotalSeconds) s, Ausgabe: $out"
$lines = if (Test-Path $out) { Get-Content $out } else { @() }

function Line([string]$Prefix) { $lines | Where-Object { $_.StartsWith($Prefix) } | Select-Object -Last 1 }
function Number([string]$Text, [string]$Key) {
    if ($Text -and $Text -cmatch "\b$([regex]::Escape($Key))=(-?\d+)") { return [long]$Matches[1] }
    $script:problems.Add("kein Wert '$Key' in: $Text")
    return 0
}
function Expect([bool]$Condition, [string]$What) { if (-not $Condition) { $script:problems.Add($What) } }

$summary = Line 'input-script summary:'
$match = Line 'spectator: map'
$commands = Line 'spectator commands:'
$camera = Line 'spectator camera:'
$view = Line 'spectator view:'
$far = Line 'spectator frame cost far:'
$near = Line 'spectator frame cost near:'
foreach ($needed in @{ 'input-script summary' = $summary; 'spectator' = $match; 'spectator commands' = $commands; 'spectator camera' = $camera; 'spectator view' = $view; 'spectator frame cost far' = $far }.GetEnumerator()) {
    if (-not $needed.Value) { $problems.Add("keine Zeile '$($needed.Key)' in der Ausgabe") }
}

if ($problems.Count -eq 0) {
    # Das Gefecht lief, beide KI befahlen, vom Zuschauer kam nichts in der Simulation an.
    # 100 Spielsekunden sind 3000 Originalbilder; die Tickzahl derselben Spielzeit haengt an der Simulationsrate.
    Expect ((Number $match 'gameSeconds') -ge 100) 'das Gefecht ist keine 100 Spielsekunden (3000 Originalbilder) weit gekommen'
    Expect ((Number $commands 'player1') -gt 0 -and (Number $commands 'player2') -gt 0) 'nicht beide KI haben Befehle gegeben'
    Expect ((Number $commands 'viewerOrdersRecorded') -eq 0) 'ein Befehl des Zuschauers wurde aufgezeichnet'
    Expect ((Number $summary 'unresolved') -eq 0) 'das Eingabeskript hatte unaufgeloeste Ziele'
    Expect ((Number $summary 'commandsAccepted') -eq 0 -and (Number $summary 'commandsRejected') -eq 0) 'ueber die Eingabe des Zuschauers wurde ein Befehl eingereicht'
    Expect ((Number $summary 'selectionChanges') -eq 0) 'der Zuschauer hat etwas ausgewaehlt'

    # Die Kamera: Uebersicht weit ueber der Grenze eines Spielers.
    $playerMax = Number $camera 'playerMaxHeight'
    $overview = Number $camera 'overviewHeight'
    $reached = Number $camera 'highestReached'
    Expect ($overview -gt 2 * $playerMax) "die Uebersichtshoehe ($overview) liegt nicht weit ueber der Grenze eines Spielers ($playerMax)"
    Expect ($reached -ge $overview - 1) "die Kamera hat die Uebersichtshoehe nicht erreicht ($reached von $overview)"
    Expect ((Number $far 'frames') -ge 300) 'weniger als 300 Bilder im fernen Zoom'

    # Die simulierten KI-Kameras.
    foreach ($index in 1, 2) {
        $ai = Line "spectator ai-camera ${index}:"
        if (-not $ai) { $problems.Add("keine Zeile zur KI-Kamera $index"); continue }
        Expect ((Number $ai 'orders') -gt 0 -and (Number $ai 'interests') -ge 3) "die KI-Kamera $index hat keine Blickpunkte gefunden: $ai"
    }

    # Die Ansicht: alle Tafeln, alle Nebelsichten, alle Tempi.
    foreach ($word in 'Production', 'Units', 'Losses', 'Economy', 'All', 'Player 1', 'Player 2', '1x', '2x', '4x', '8x', 'paused') {
        Expect ($view -clike "*$word*") "in der Ansicht fehlt '$word': $view"
    }
    Expect ((Number $view 'panelRebuilds') -gt 100) 'die Tafeln wurden kaum neu aufgebaut'

    # Die Tafeln hatten Daten.
    $panelLines = @($lines | Where-Object { $_.StartsWith('spectator panel:') })
    $players = @($panelLines | Where-Object { $_ -cmatch 'spectator panel: player \d' })
    Expect ($players.Count -eq 2) "Tafelzeilen fuer $($players.Count) statt 2 Spieler"
    $collected = 0; $structuresBuilt = 0
    foreach ($player in $players) { $collected += Number $player 'collected' }
    foreach ($detail in @($panelLines | Where-Object { $_ -clike '*structuresBuilt=*' })) { $structuresBuilt += Number $detail 'structuresBuilt' }
    Expect ($collected -gt 0) 'niemand hat Vorraete gesammelt'
    Expect ($structuresBuilt -gt 0) 'niemand hat ein Gebaeude fertiggestellt'
}

# Die Bilder.
$captures = @()
foreach ($shot in $shots.GetEnumerator()) {
    $source = Join-Path $film ('frame-{0:D5}.png' -f [int]$shot.Key)
    $target = Join-Path $OutDir "$($shot.Value).png"
    if (-not (Test-Path -LiteralPath $source)) { $problems.Add("Bild $($shot.Key) fehlt ($source)"); continue }
    Move-Item -LiteralPath $source -Destination $target -Force
    if ((Get-Item -LiteralPath $target).Length -lt 20000) { $problems.Add("Bild $($shot.Value) ist auffaellig klein") }
    $captures += $target
}

Write-Host ''
$lines | Where-Object { $_.StartsWith('spectator') } | ForEach-Object { Write-Host "   $_" }
Write-Host ''
Write-Host "   Bildkosten nah : $near"
Write-Host "   Bildkosten fern: $far"
Write-Host "   Bilder ($($captures.Count)) unter $OutDir"
if ($problems.Count -eq 0) {
    Write-Host 'PASS check-spectator' -ForegroundColor Green
    exit 0
}
Write-Host "FAIL check-spectator: $($problems -join '; ')" -ForegroundColor Red
exit 1
