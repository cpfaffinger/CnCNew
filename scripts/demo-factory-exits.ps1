<#
.SYNOPSIS
  Zeigt im Fenster, wie frisch gebaute Einheiten ihre Fabrik verlassen (gestellte Szenen exit-<fraktion>-<gebaeude>, docs/DEMO-SCENARIOS.md).

.DESCRIPTION
  Startet den Client in einer kleinen Szene auf Alpine Assault, die Kamera nah am Gebaeude, die Ausfahrt zeigt nach rechts (Osten). Jedes Gebaeude bekommt
  ab Bild 30 drei Bauauftraege nacheinander, die Ausfahrt ist also dreimal zu sehen:
    commandcenter  Kommandozentrale baut Planierraupen (GLA: Arbeiter)
    barracks       Kaserne baut Infanterie (Ranger / Rotgardist / Rebell)
    warfactory     Waffenfabrik bzw. Waffenhaendler baut Panzer (Crusader / Battlemaster / Scorpion)
    all            alle drei Gebaeude einer Fraktion in einem Bild, dazu ein Bauarbeiter, Kraftwerk und Versorgungslager (auch zum Ausprobieren der Bautasten)
  Die Szene laeuft mit der Skirmish-Befehlsleiste: eigene Befehle sind moeglich (z. B. weitere Einheiten in Auftrag geben, einen Sammelpunkt setzen).
  Dieselben Szenen prueft ohne Fenster:  .\scripts\audit-scenarios.ps1 -Scenario exits [-Faction America]

.EXAMPLE
  .\scripts\demo-factory-exits.ps1                                   # America, alle drei Gebaeude
  .\scripts\demo-factory-exits.ps1 -Faction China -Building warfactory
  .\scripts\demo-factory-exits.ps1 -Faction GLA -Building barracks -TickRate 60
  .\scripts\demo-factory-exits.ps1 -Building commandcenter -SmokeTest -Frames 600   # ohne Fenster, nur Lauffaehigkeit
#>
[CmdletBinding()]
param(
    [ValidateSet('America', 'China', 'GLA')]
    [string]$Faction = 'America',

    [ValidateSet('commandcenter', 'barracks', 'warfactory', 'all')]
    [string]$Building = 'all',

    # Simulationsrate (30, 60 oder 120 Hz); 0 = Standard der Spielschicht.
    [int]$TickRate = 0,

    [string]$ContentDir,
    [string]$Size = '1280x720',
    [ValidateSet('', 'alternate', 'classic')]
    [string]$Mouse = '',
    [switch]$Mute,
    [switch]$NoBuild,
    [switch]$SmokeTest,
    [int]$Frames = 0
)

$ErrorActionPreference = 'Stop'
$scenario = "exit-$($Faction.ToLowerInvariant())-$Building"

$units = @{
    'America' = @{ commandcenter = 'Planierraupe'; barracks = 'Ranger'; warfactory = 'Crusader' }
    'China'   = @{ commandcenter = 'Planierraupe'; barracks = 'Rotgardist'; warfactory = 'Battlemaster' }
    'GLA'     = @{ commandcenter = 'Arbeiter'; barracks = 'Rebell'; warfactory = 'Scorpion' }
}
$doors = @{
    'America' = @{ commandcenter = 'Tor (1,5 s auf, 3 s offen, 1,5 s zu)'; barracks = 'kein Tor'; warfactory = 'Tor (3,25 s auf, 3 s offen, 4 s zu)' }
    'China'   = @{ commandcenter = 'Tor (3 s auf, 3 s offen, 3 s zu)'; barracks = 'kein Tor'; warfactory = 'Tor (4 s auf, 2 s offen, 5 s zu)' }
    'GLA'     = @{ commandcenter = 'kein Tor'; barracks = 'kein Tor'; warfactory = 'Tor (2 s auf, 3 s offen, 2 s zu)' }
}

Write-Host "== Fabrikausfahrt: Szene '$scenario'" -ForegroundColor Cyan
$shown = if ($Building -eq 'all') { @('commandcenter', 'barracks', 'warfactory') } else { @($Building) }
foreach ($name in $shown) {
    Write-Host ("   {0,-14} baut dreimal: {1,-13} {2}" -f $name, $units[$Faction][$name], $doors[$Faction][$name])
}
Write-Host @'
   Worauf achten (das Original als Massstab: ProductionUpdate / DefaultProductionExitUpdate):
     1. Die Einheit erscheint IM Gebaeude (am UnitCreatePoint), nicht davor und nicht mitten auf dem Dach.
     2. Bei Gebaeuden mit Tor: das Tor ist ganz offen, wenn die Einheit erscheint, bleibt offen, solange sie hinausfaehrt, und schliesst danach.
     3. Die Einheit faehrt/geht von selbst durch die Torseite (rechts im Bild) hinaus - sie bleibt nicht im Gebaeude stehen und springt nicht.
     4. Sie haelt kurz vor dem Gebaeude (rund 20 Einheiten hinter der Gebaeudekante); die naechste Einheit stellt sich daneben, nicht in sie hinein.
     5. Waehrend der Ausfahrt gleitet sie durch die eigene Fabrik, weicht aber nicht seitlich durch die Wand aus.
     6. Nach einem gesetzten Sammelpunkt (Gebaeude waehlen, Rechtsklick auf den Boden) faehrt sie erst hinaus, dann zum Sammelpunkt.
   Ohne Fenster dieselbe Pruefung mit Zahlen: .\scripts\audit-scenarios.ps1 -Scenario exits
'@

$forward = @{ Scenario = $scenario; Size = $Size }
if ($ContentDir) { $forward.ContentDir = $ContentDir }
if ($TickRate -gt 0) { $forward.TickRate = $TickRate }
if ($Mouse) { $forward.Mouse = $Mouse }
if ($Mute) { $forward.Mute = $true }
if ($NoBuild) { $forward.NoBuild = $true }
if ($SmokeTest) { $forward.SmokeTest = $true }
if ($Frames -gt 0) { $forward.Frames = $Frames }
& "$PSScriptRoot\demo.ps1" scenario @forward
exit $LASTEXITCODE
