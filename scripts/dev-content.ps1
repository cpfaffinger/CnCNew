<#
.SYNOPSIS
  Creates the developer installation <repo>\game from your own Steam installation: the content every script, tool and test expects.

.DESCRIPTION
  The same installation a user gets (install.ps1), with the repository-local target `game` (git-ignored) and content only:
  game\content and game\install.json. The Steam installation is only read and must be the known master version; the export is verified
  file by file and its manifest compared with the one recorded in baselines/steam-master.json. Running it again verifies and repairs.
  Pass -WithClient to also publish the client into game\bin.

.EXAMPLE
  pwsh scripts\dev-content.ps1
  dotnet run --project tools/CnCNew.AssetTool -- verify game/content
#>
[CmdletBinding()]
param(
    # The Steam installation of the game (default: found as install.ps1 finds it).
    [string]$GameDir,

    [switch]$WithClient
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$arguments = @{ Target = (Join-Path $repo 'game') }
if ($GameDir) { $arguments.GameDir = $GameDir }
if (-not $WithClient) { $arguments.ContentOnly = $true }
& (Join-Path $PSScriptRoot 'install.ps1') @arguments
exit $LASTEXITCODE
