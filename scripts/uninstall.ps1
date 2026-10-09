<#
.SYNOPSIS
  Removes a CnCNew game directory created by install.ps1.

.DESCRIPTION
  Removes only a directory that carries our install.json, and of it only what an installation consists of (content, bin, Start-CnCNew.cmd,
  install.json); anything else in it is kept and listed. Never a Steam game directory. Asks nothing: -Yes is required.
  The decision is made by `CnCNew.AssetTool uninstall`.

.EXAMPLE
  pwsh scripts\uninstall.ps1 -Target "$env:LOCALAPPDATA\CnCNew\Game" -Yes
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Target,

    # Explicit confirmation; without it nothing is removed.
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Yes) {
    Write-Host "Nothing was removed: pass -Yes to remove the installation at '$Target'." -ForegroundColor Yellow
    exit 2
}
$Target = [IO.Path]::GetFullPath($Target, (Get-Location).Path)

dotnet build (Join-Path $repo 'tools\CnCNew.AssetTool\CnCNew.AssetTool.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Building the asset tool failed (exit code $LASTEXITCODE)." }
dotnet (Join-Path $repo 'tools\CnCNew.AssetTool\bin\Release\net10.0\CnCNew.AssetTool.dll') uninstall $Target --yes
exit $LASTEXITCODE
