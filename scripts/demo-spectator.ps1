<#
.SYNOPSIS
  Zuschauer-Demo: ein Gefecht zwischen zwei KI-Armeen im Fenster, niemand spielt (docs/SPECTATOR-MODE.md).

.DESCRIPTION
  Baut den Stride-Client (Release) und startet ein Skirmish, in dem beide Seiten vom Computer gespielt werden. Der Zuschauer sieht die ganze Karte ohne Nebel
  (umschaltbar auf die Sicht eines Spielers), darf weit herauszoomen, kann der simulierten Kamera einer KI folgen und sieht Beobachter-Tafeln (Produktion, Einheiten,
  Verluste, Wirtschaft). Das Gefecht endet mit der ueblichen Siegerkennung; der Sieger steht dann oben im Bild.

  Der Ausgang haengt am Seed. Ohne -Seed zieht das Skript bei jedem Start einen neuen (Get-Random, also die Zufallsquelle des Systems - nie die Simulation) und gibt ihn
  aus; mit -Seed N laeuft genau dasselbe Gefecht noch einmal (derselbe Seed wie `SimTool ai-match --seed N`). Ohne -Map/-FactionA/-FactionB waehlt das Skript eine der
  Paarungen, deren Ausgang in der Erhebung ueber 20 Seeds offen war (siehe $pairings unten).

.EXAMPLE
  pwsh scripts/demo-spectator.ps1
  pwsh scripts/demo-spectator.ps1 -Seed 7 -Map "Alpine Assault" -FactionA FactionAmerica -FactionB FactionChina
  pwsh scripts/demo-spectator.ps1 -Speed 2 -Size 1920x1080 -MaxZoom 2000
  pwsh scripts/demo-spectator.ps1 -TickRate 60
#>
[CmdletBinding()]
param(
    # Karte: virtueller Pfad oder ein eindeutiger Teil davon ("Alpine Assault").
    [string]$Map,
    # Die beiden Armeen (Fraktionsnamen wie FactionAmerica, FactionChina, FactionGLA, FactionChinaTankGeneral, FactionAmericaAirForceGeneral).
    [string]$FactionA,
    [string]$FactionB,
    # Seed des Gefechts (1 .. 2147483647); ohne Angabe ein frisch gezogener.
    [int]$Seed,
    # Simulationsrate 30, 60 oder 120; ohne Angabe der Standard des Clients.
    [ValidateSet(30, 60, 120)]
    [int]$TickRate,
    # Hoechste Kamerahoehe des Zuschauers ueber Grund; ohne Angabe die Hoehe, aus der die ganze Karte im Bild ist.
    [double]$MaxZoom,
    # Spieltempo beim Start (1 = Echtzeit; im Spiel mit 1/2/4/8 und Leertaste aenderbar).
    [double]$Speed = 1,
    [string]$Size = '1280x720',
    [string]$ContentDir,
    [switch]$Mute,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) {
    throw "Inhaltsverzeichnis nicht gefunden: $ContentDir`nDer gepruefte Runtimebestand liegt lokal unter game\content (nicht im Repo; scripts\dev-content.ps1 erzeugt ihn aus der eigenen Steam-Installation); sonst mit -ContentDir angeben."
}
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path

# Paarungen mit offenem Ausgang: Siege der ersten / der zweiten Fraktion / unentschieden bei 20 Seeds (Erhebung ueber 320 Gefechte, docs/SPECTATOR-MODE.md Abschnitt 8).
$pairings = @(
    @{ Map = 'Maps/Armored Fury/Armored Fury.map';     A = 'FactionChina';            B = 'FactionGLA';                    Survey = '13 / 4 / 3' },
    @{ Map = 'Maps/Bitter Winter/Bitter Winter.map';   A = 'FactionChina';            B = 'FactionGLA';                    Survey = '6 / 14 / 0' },
    @{ Map = 'Maps/Armored Fury/Armored Fury.map';     A = 'FactionAmerica';          B = 'FactionChina';                  Survey = '8 / 3 / 9' },
    @{ Map = 'Maps/Alpine Assault/Alpine Assault.map'; A = 'FactionAmerica';          B = 'FactionChina';                  Survey = '12 / 1 / 7' },
    @{ Map = 'Maps/Death Valley/Death Valley.map';     A = 'FactionChinaTankGeneral'; B = 'FactionAmericaAirForceGeneral'; Survey = '4 / 3 / 13' }
)

# Der Zufall liegt hier im Skript, nicht in der Simulation: ein Seed je Start, und (ohne Vorgabe) eine der Paarungen.
if (-not $PSBoundParameters.ContainsKey('Seed')) { $Seed = Get-Random -Minimum 1 -Maximum 2147483647 }
if ($Seed -lt 1) { throw '-Seed muss zwischen 1 und 2147483647 liegen (0 waere der eingebaute Standard-Seed des Clients).' }
$survey = $null
if (-not $Map -and -not $FactionA -and -not $FactionB) {
    $pick = $pairings[(Get-Random -Minimum 0 -Maximum $pairings.Count)]
    $Map = $pick.Map; $FactionA = $pick.A; $FactionB = $pick.B; $survey = $pick.Survey
}
else {
    if (-not $Map) { $Map = 'Maps/Alpine Assault/Alpine Assault.map' }
    if (-not $FactionA) { $FactionA = 'FactionAmerica' }
    if (-not $FactionB) { $FactionB = 'FactionChina' }
    $known = $pairings | Where-Object { $_.Map -like "*$Map*" -and $_.A -eq $FactionA -and $_.B -eq $FactionB } | Select-Object -First 1
    if ($known) { $survey = $known.Survey }
}

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Baue Client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}

$inv = [Globalization.CultureInfo]::InvariantCulture
$clientArgs = @('--content-dir', $ContentDir, '--size', $Size, '--match', $Map, '--faction', $FactionA, '--opponent', $FactionB, '--spectate', '--seed', "$Seed",
    '--spectator-speed', $Speed.ToString($inv))
if ($PSBoundParameters.ContainsKey('TickRate')) { $clientArgs += @('--tick-rate', "$TickRate") }
if ($PSBoundParameters.ContainsKey('MaxZoom')) { $clientArgs += @('--spectator-max-zoom', $MaxZoom.ToString($inv)) }
if ($Mute) { $clientArgs += '--mute' }

Write-Host ''
Write-Host "== Zuschauer-Gefecht" -ForegroundColor Cyan
Write-Host "   Seed     : $Seed    (wiederholen: pwsh scripts/demo-spectator.ps1 -Seed $Seed -Map `"$Map`" -FactionA $FactionA -FactionB $FactionB)"
Write-Host "   Karte    : $Map"
Write-Host "   Armeen   : 1 = $FactionA   gegen   2 = $FactionB   (beide KI)"
if ($survey) { Write-Host "   Erhebung : $survey  (Siege 1 / Siege 2 / unentschieden bei 20 Seeds)" }
Write-Host "   Tickrate : $(if ($PSBoundParameters.ContainsKey('TickRate')) { "$TickRate Hz" } else { 'Standard des Clients' }),  Tempo beim Start: $($Speed.ToString($inv))x"
Write-Host ''
Write-Host '   Tasten'
Write-Host '     P / U / L / E      Tafel: Produktion / Einheiten / Verluste / Wirtschaft   (Tab: naechste, H: Tafeln aus/an)'
Write-Host '     F1 / F2            der simulierten Kamera von KI 1 / KI 2 folgen (noch einmal oder F3: freie Kamera)'
Write-Host '     J / K              einmal zur Kamera von KI 1 / KI 2 springen'
Write-Host '     O                  Uebersicht ueber die ganze Karte (noch einmal: zurueck)'
Write-Host '     F                  Nebel: alles -> Sicht Spieler 1 -> Sicht Spieler 2 -> alles'
Write-Host '     Leertaste, 1 2 4 8 Pause, Tempo 1x / 2x / 4x / 8x'
Write-Host '     Mausrad            zoomen (weit ueber die Grenze eines Spielers hinaus)'
Write-Host '     Pfeiltasten, Bildrand, Rechtsziehen: scrollen;  Mitteltaste ziehen: drehen;  Klick aufs Radar: dorthin'
Write-Host '     Esc                beenden'
Write-Host ''
dotnet run --project $project -c Release --no-build -- @clientArgs
exit $LASTEXITCODE
