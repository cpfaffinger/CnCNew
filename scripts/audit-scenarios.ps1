<#
.SYNOPSIS
  Fuehrt die gestellten Demos (capture-building, tank-duel, jet-strafe) ohne Fenster aus und prueft, was sie zeigen sollen.

.DESCRIPTION
  Baut PresentationTool (Release) und startet fuer jede Szene `scenario-audit`: Zeitverlauf der Objekte, Ereignisse und die szenenspezifischen Pruefungen
  (Turmwinkel bei jedem Schuss, Fangstufen UNPACKING/RAISING_FLAG/PACKING, Flugphasen des Jets). Der Exitcode ist 1, sobald eine Szene eine Pruefung nicht besteht –
  ein Fehlschlag ist hier ein Befund ueber den Stand der Simulation/Darstellung, kein Werkzeugfehler. Ausgaben liegen unter -OutDir (Standard: %TEMP%\cncnew-scenarios).

.EXAMPLE
  .\scripts\audit-scenarios.ps1
  .\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'capture-building', 'tank-duel', 'jet-strafe')]
    [string]$Scenario = 'all',
    [string]$ContentDir,
    [string]$OutDir,
    [int]$Every = 30,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
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

$names = if ($Scenario -eq 'all') { @('capture-building', 'tank-duel', 'jet-strafe') } else { @($Scenario) }
$failed = 0
foreach ($name in $names) {
    $log = Join-Path $OutDir "$name.txt"
    Write-Host "== $name" -ForegroundColor Cyan
    dotnet run --project $project -c Release --no-build -- scenario-audit $ContentDir --name $name --every $Every *> $log
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log | Where-Object { $_ -match '^(RESULT|  FAIL)' } | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { $failed++ }
    Write-Host "   vollstaendige Ausgabe: $log"
}

exit ($failed -gt 0 ? 1 : 0)
