<#
.SYNOPSIS
  Runs the staged demos (capture-building, tank-duel, jet-strafe; with -Scenario exits the factory exits) without a window and checks what they are meant to show.

.DESCRIPTION
  Builds PresentationTool (Release) and starts `scenario-audit` for every scene: timeline of the objects, events and the scene-specific checks
  (turret angle at every shot, capture stages UNPACKING/RAISING_FLAG/PACKING, flight phases of the jet). The exit code is 1 as soon as a scene fails a check -
  a failure here is a finding about the state of the simulation/presentation, not a tool error. The output lies under -OutDir (default: %TEMP%\cncnew-scenarios).

  -Scenario all    (default) the three behaviour scenes capture-building, tank-duel, jet-strafe
  -Scenario exits  the nine factory exits exit-<faction>-<building> (with -Faction only one faction): spawn position, door, exit through the door side, stopping point
  -Scenario <name> exactly this scene (names: PresentationTool scenario-audit <content> without --name)

.EXAMPLE
  .\scripts\audit-scenarios.ps1
  .\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
  .\scripts\audit-scenarios.ps1 -Scenario exits -Faction China
#>
[CmdletBinding()]
param(
    [string]$Scenario = 'all',
    # Only with -Scenario exits: one faction instead of all three.
    [ValidateSet('', 'America', 'China', 'GLA')]
    [string]$Faction = '',
    [string]$ContentDir,
    [string]$OutDir,
    [int]$Every = 30,
    # Simulation rate (30, 60 or 120 Hz); 0 = the game layer's default. -Every and all times in the output count original frames (1/30 s) at every rate.
    [int]$TickRate = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-scenarios' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$project = Join-Path $repo 'tools\CnCNew.PresentationTool\CnCNew.PresentationTool.csproj'
if (-not $NoBuild) {
    Write-Host '== Building PresentationTool (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

$names = switch ($Scenario) {
    'all'   { @('capture-building', 'tank-duel', 'jet-strafe') }
    'exits' {
        $factions = if ($Faction) { @($Faction.ToLowerInvariant()) } else { @('america', 'china', 'gla') }
        foreach ($f in $factions) { foreach ($b in @('commandcenter', 'barracks', 'warfactory')) { "exit-$f-$b" } }
    }
    default { @($Scenario) }
}
$failed = 0
foreach ($name in $names) {
    $log = Join-Path $OutDir "$name.txt"
    Write-Host "== $name" -ForegroundColor Cyan
    $rateArgs = if ($TickRate -gt 0) { @('--tick-rate', "$TickRate") } else { @() }
    dotnet run --project $project -c Release --no-build -- scenario-audit $ContentDir --name $name --every $Every @rateArgs *> $log
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log | Where-Object { $_ -match '^(RESULT|  FAIL|Unhandled)' } | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { $failed++ }
    Write-Host "   full output: $log"
}

exit ($failed -gt 0 ? 1 : 0)
