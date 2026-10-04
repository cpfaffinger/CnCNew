<#
.SYNOPSIS
  Prueft alle Projekte (Solution plus das Stride-Werkzeug ausserhalb der Solution) auf bekannte NuGet-Sicherheitsluecken.

.DESCRIPTION
  Ruft `dotnet list <ziel> package --vulnerable --include-transitive --format json` auf und beendet sich mit Exitcode 1,
  sobald irgendein Paket (direkt oder transitiv) eine Sicherheitsanfaelligkeit traegt. NuGetAudit bleibt im Build an;
  dieses Skript ist die ausdrueckliche Abnahmepruefung (docs/DEMO-FINDINGS.md #1).

.EXAMPLE
  .\scripts\audit-vulnerable.ps1
#>
[CmdletBinding()]
param(
    # Abweichende Ziele (Projekt-/Solution-Dateien), z. B. fuer den Selbsttest des Skripts.
    [string[]]$Target
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$targets = if ($Target) { $Target } else { @(
    (Join-Path $repo 'CnCNew.slnx'),
    (Join-Path $repo 'tools\CnCNew.UiRenderTool\CnCNew.UiRenderTool.csproj')
) }

$findings = 0
foreach ($target in $targets) {
    Write-Host "== $([IO.Path]::GetFileName($target))" -ForegroundColor Cyan
    $json = dotnet list $target package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "dotnet list fehlgeschlagen fuer $target (Exitcode $LASTEXITCODE)." }
    $report = ($json -join "`n") | ConvertFrom-Json
    foreach ($project in $report.projects) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if (-not $package) { continue }
                foreach ($vulnerability in @($package.vulnerabilities)) {
                    $findings++
                    Write-Host ("  {0}: {1} {2} - {3} ({4})" -f (Split-Path -Leaf $project.path), $package.id, $package.resolvedVersion, $vulnerability.severity, $vulnerability.advisoryurl) -ForegroundColor Red
                }
            }
        }
    }
}

if ($findings -gt 0) {
    Write-Host "$findings Sicherheitsanfaelligkeit(en) gefunden." -ForegroundColor Red
    exit 1
}
Write-Host 'Keine bekannten Sicherheitsanfaelligkeiten in direkten oder transitiven Paketen.' -ForegroundColor Green
exit 0
