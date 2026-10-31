<#
.SYNOPSIS
  Creates the developer installation <repo>\game from your own Steam installation: the content every script, tool and test expects.

.DESCRIPTION
  The same installation a user gets (install.ps1), with the repository-local target `game` (git-ignored) and content only:
  game\content and game\install.json. The Steam installation is only read and must be the known master version; the export is verified
  file by file and its manifest compared with the one recorded in baselines/patch-master.json. Running it again verifies and repairs.
  Pass -WithClient to also publish the client into game\bin.

  Beside it game\original\content, the game without the patch: the publication audit compares the repository with both, and it tells a defect of the patch from
  one of the port. -SkipOriginal leaves it out. The patch's files come from references\generals-game-patch when it is there, from its commit otherwise.

.EXAMPLE
  pwsh scripts\dev-content.ps1
  dotnet run --project tools/CnCNew.AssetTool -- verify game/content
#>
[CmdletBinding()]
param(
    # The Steam installation of the game (default: found as install.ps1 finds it).
    [string]$GameDir,

    [switch]$WithClient,

    [switch]$SkipOriginal
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$arguments = @{ Target = (Join-Path $repo 'game') }
if ($GameDir) { $arguments.GameDir = $GameDir }
if (-not $WithClient) { $arguments.ContentOnly = $true }
$checkout = Join-Path $repo 'references\generals-game-patch\Patch104pZH\GameFilesEdited'
if (Test-Path -LiteralPath $checkout) { $arguments.PatchDir = $checkout }
& (Join-Path $PSScriptRoot 'install.ps1') @arguments
if ($LASTEXITCODE -ne 0 -or $SkipOriginal) { exit $LASTEXITCODE }

$original = @{ Target = (Join-Path $repo 'game\original'); Original = $true; ContentOnly = $true }
if ($GameDir) { $original.GameDir = $GameDir }
& (Join-Path $PSScriptRoot 'install.ps1') @original
exit $LASTEXITCODE
