<#
.SYNOPSIS
  Checks all projects (the solution, the Stride tool outside the solution, and the dedicated server as it is packaged) for known NuGet vulnerabilities.

.DESCRIPTION
  Calls `dotnet list <target> package --vulnerable --include-transitive --format json` and exits with code 1
  as soon as any package (direct or transitive) carries a vulnerability. NuGetAudit stays on in the build;
  this script is the explicit acceptance check (docs/DEMO-FINDINGS.md #1).

  The dedicated server is the one program of this repository that listens on an open port, and it is handed out as a self-contained package
  (scripts/package-server.ps1). It is therefore checked a second time on its own, restored for each runtime it is packaged for, and the
  script fails if it references any NuGet package at all: the server is the base class library and the project's own code, nothing else
  (docs/DEDICATED-SERVER.md section 13). The .NET runtime inside a package is the one of the SDK that built it - `dotnet --version` is printed
  for that reason; a package is rebuilt when a runtime update is published.

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
# Asked before the loop below: PowerShell names are case-insensitive, and the loop variable is this parameter.
$defaultTargets = -not $Target
$serverProject = Join-Path $repo 'tools\CnCNew.DedicatedServer\CnCNew.DedicatedServer.csproj'

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

if ($defaultTargets) {
    Write-Host "== CnCNew.DedicatedServer.csproj as packaged (SDK $(dotnet --version))" -ForegroundColor Cyan
    $json = dotnet list $serverProject package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "dotnet list failed for $serverProject (exit code $LASTEXITCODE)." }
    $report = ($json -join "`n") | ConvertFrom-Json
    foreach ($project in $report.projects) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if (-not $package) { continue }
                $findings++
                Write-Host ("  the dedicated server references the NuGet package {0} {1} - it must reference none" -f $package.id, $package.resolvedVersion) -ForegroundColor Red
            }
        }
    }
    $json = dotnet list $serverProject package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "dotnet list failed for $serverProject (exit code $LASTEXITCODE)." }
    if ($findings -eq 0) { Write-Host '  no NuGet package besides the base class library' -ForegroundColor DarkGray }
    foreach ($project in (($json -join "`n") | ConvertFrom-Json).projects) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if (-not $package) { continue }
                foreach ($vulnerability in @($package.vulnerabilities)) {
                    $findings++
                    Write-Host ("  dedicated server: {0} {1} - {2} ({3})" -f $package.id, $package.resolvedVersion, $vulnerability.severity, $vulnerability.advisoryurl) -ForegroundColor Red
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
