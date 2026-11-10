<#
.SYNOPSIS
  Removes a CnCNew game directory created by install.ps1.

.DESCRIPTION
  Removes only a directory that carries our install.json, and of it only what an installation consists of (content, bin, Start-CnCNew.cmd,
  install.json); anything else in it is kept and listed. The desktop and Start menu shortcuts the launcher made for that folder go with it. Never a Steam game directory. Asks nothing: -Yes is required.
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
$exit = $LASTEXITCODE

# The shortcuts the launcher made for this folder (docs/INSTALLATION.md): its record is %LOCALAPPDATA%\CnCNew\shortcuts.txt, one line "install folder|shortcut" each.
# Only what is recorded for this folder, and only a .lnk, goes; the record keeps the lines of other folders.
$record = Join-Path $env:LOCALAPPDATA 'CnCNew\shortcuts.txt'
if (($exit -eq 0 -or -not (Test-Path -LiteralPath $Target)) -and (Test-Path -LiteralPath $record)) {
    $kept = @()
    foreach ($line in Get-Content -LiteralPath $record) {
        $parts = $line -split '\|', 2
        if ($parts.Count -eq 2 -and [string]::Equals($parts[0].TrimEnd('\'), $Target.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            if ($parts[1].EndsWith('.lnk', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $parts[1])) {
                Remove-Item -LiteralPath $parts[1] -Force
                Write-Host "Removed the shortcut $($parts[1])"
            }
        } elseif ($line) { $kept += $line }
    }
    if ($kept.Count -gt 0) { [IO.File]::WriteAllLines($record, [string[]]$kept) } else { Remove-Item -LiteralPath $record -Force }
}
exit $exit
