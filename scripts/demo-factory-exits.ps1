<#
.SYNOPSIS
  Shows in a window how freshly built units leave their factory (staged scenes exit-<faction>-<building>, docs/DEMO-SCENARIOS.md).

.DESCRIPTION
  Starts the client in a small scene on Alpine Assault, the camera close to the building; it looks at the door side from the south-east, the exit (east) points to the lower right of the image. Every building gets
  three production orders one after another from frame 30 on, so the exit can be seen three times:
    commandcenter  the command center builds dozers (GLA: workers)
    barracks       the barracks builds infantry (Ranger / Red Guard / Rebel)
    warfactory     the war factory or arms dealer builds tanks (Crusader / Battlemaster / Scorpion)
    all            all three buildings of a faction in one image, plus a builder, a power plant and a supply center (also for trying out the build hot keys)
  The scene runs with the skirmish command bar: commands of your own are possible (e.g. ordering further units, setting a rally point).
  The same scenes are checked without a window by:  .\scripts\audit-scenarios.ps1 -Scenario exits [-Faction America]

.EXAMPLE
  .\scripts\demo-factory-exits.ps1                                   # America, all three buildings
  .\scripts\demo-factory-exits.ps1 -Faction China -Building warfactory
  .\scripts\demo-factory-exits.ps1 -Faction GLA -Building barracks -TickRate 60
  .\scripts\demo-factory-exits.ps1 -Building commandcenter -SmokeTest -Frames 600   # without a window, only that it runs
#>
[CmdletBinding()]
param(
    [ValidateSet('America', 'China', 'GLA')]
    [string]$Faction = 'America',

    [ValidateSet('commandcenter', 'barracks', 'warfactory', 'all')]
    [string]$Building = 'all',

    # Simulation rate (30, 60 or 120 Hz); 0 = the game layer's default.
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
    'America' = @{ commandcenter = 'Dozer'; barracks = 'Ranger'; warfactory = 'Crusader' }
    'China'   = @{ commandcenter = 'Dozer'; barracks = 'Red Guard'; warfactory = 'Battlemaster' }
    'GLA'     = @{ commandcenter = 'Worker'; barracks = 'Rebel'; warfactory = 'Scorpion' }
}
$doors = @{
    'America' = @{ commandcenter = 'door (1.5 s opening, 3 s open, 1.5 s closing)'; barracks = 'no door'; warfactory = 'door (3.25 s opening, 3 s open, 4 s closing)' }
    'China'   = @{ commandcenter = 'door (3 s opening, 3 s open, 3 s closing)'; barracks = 'no door'; warfactory = 'door (4 s opening, 2 s open, 5 s closing)' }
    'GLA'     = @{ commandcenter = 'no door'; barracks = 'no door'; warfactory = 'door (2 s opening, 3 s open, 2 s closing)' }
}

Write-Host "== Factory exit: scene '$scenario'" -ForegroundColor Cyan
$shown = if ($Building -eq 'all') { @('commandcenter', 'barracks', 'warfactory') } else { @($Building) }
foreach ($name in $shown) {
    Write-Host ("   {0,-14} builds three times: {1,-13} {2}" -f $name, $units[$Faction][$name], $doors[$Faction][$name])
}
Write-Host @'
   What to look for (the original as the yardstick: ProductionUpdate / DefaultProductionExitUpdate):
     1. The unit appears INSIDE the building (at the UnitCreatePoint), not in front of it and not in the middle of the roof.
     2. For buildings with a door: the door is fully open when the unit appears, stays open while it drives out, and closes afterwards.
     3. The unit drives/walks out through the door side (lower right of the image) by itself - it does not stay inside the building and does not jump.
     4. It stops just in front of the building (about 20 units beyond the building edge); the next unit lines up beside it, not inside it.
     5. While exiting it glides through its own factory, but does not dodge sideways through the wall.
     6. With a rally point set (select the building, right-click the ground) it first drives out, then to the rally point.
   The same check without a window, with numbers: .\scripts\audit-scenarios.ps1 -Scenario exits
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
