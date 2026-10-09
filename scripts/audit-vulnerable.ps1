<#
.SYNOPSIS
  Checks all projects (the solution plus the Stride tool outside the solution) for known NuGet vulnerabilities.

.DESCRIPTION
  Calls `dotnet list <target> package --vulnerable --include-transitive --format json` and exits with code 1
  as soon as any package (direct or transitive) carries a vulnerability. NuGetAudit stays on in the build;
  this script is the explicit acceptance check (docs/DEMO-FINDINGS.md #1).

.EXAMPLE
  .\scripts\audit-vulnerable.ps1
#>
[CmdletBinding()]
param(
    # Other targets (project/solution files), e.g. for the script's self-test.
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
    if ($LASTEXITCODE -ne 0) { throw "dotnet list failed for $target (exit code $LASTEXITCODE)." }
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
    Write-Host "$findings vulnerability(ies) found." -ForegroundColor Red
    exit 1
}
Write-Host 'No known vulnerabilities in direct or transitive packages.' -ForegroundColor Green
exit 0
