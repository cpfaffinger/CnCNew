<#
.SYNOPSIS
  Fuehrt die gestellten Demos (capture-building, tank-duel, jet-strafe; mit -Scenario exits die Fabrikausfahrten) ohne Fenster aus und prueft, was sie zeigen sollen.

.DESCRIPTION
  Baut PresentationTool (Release) und startet fuer jede Szene `scenario-audit`: Zeitverlauf der Objekte, Ereignisse und die szenenspezifischen Pruefungen
  (Turmwinkel bei jedem Schuss, Fangstufen UNPACKING/RAISING_FLAG/PACKING, Flugphasen des Jets). Der Exitcode ist 1, sobald eine Szene eine Pruefung nicht besteht –
  ein Fehlschlag ist hier ein Befund ueber den Stand der Simulation/Darstellung, kein Werkzeugfehler. Ausgaben liegen unter -OutDir (Standard: %TEMP%\cncnew-scenarios).

  -Scenario all    (Standard) die drei Verhaltensszenen capture-building, tank-duel, jet-strafe
  -Scenario exits  die neun Fabrikausfahrten exit-<fraktion>-<gebaeude> (mit -Faction nur eine Fraktion): Erscheinungsort, Tor, Ausfahrt durch die Torseite, Haltepunkt
  -Scenario <Name> genau diese Szene (Namen: PresentationTool scenario-audit <Inhalt> ohne --name)

.EXAMPLE
  .\scripts\audit-scenarios.ps1
  .\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
  .\scripts\audit-scenarios.ps1 -Scenario exits -Faction China
#>
[CmdletBinding()]
param(
    [string]$Scenario = 'all',
    # Nur mit -Scenario exits: eine Fraktion statt aller drei.
    [ValidateSet('', 'America', 'China', 'GLA')]
    [string]$Faction = '',
    [string]$ContentDir,
    [string]$OutDir,
    [int]$Every = 30,
    # Simulationsrate (30, 60 oder 120 Hz); 0 = Standard der Spielschicht. -Every und alle Zeitangaben der Ausgabe zaehlen Originalbilder (1/30 s) bei jeder Rate.
    [int]$TickRate = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Inhaltsverzeichnis nicht gefunden: $ContentDir" }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-scenarios' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$project = Join-Path $repo 'tools\CnCNew.PresentationTool\CnCNew.PresentationTool.csproj'
if (-not $NoBuild) {
    Write-Host '== Baue PresentationTool (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
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
    Write-Host "   vollstaendige Ausgabe: $log"
}

exit ($failed -gt 0 ? 1 : 0)
