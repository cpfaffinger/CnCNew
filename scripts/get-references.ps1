<#
.SYNOPSIS
  Clones the reference sources that the documentation and comments refer to into <repo>\references (ignored by Git).

.DESCRIPTION
  This repository contains no third-party source code. Where an algorithm was understood from the published sources of the original
  and rewritten, the documentation names file and function. To look these references up, the script fetches the
  four public repositories at exactly the commits they were read at:

    ea-generals-zero-hour   https://github.com/electronicarts/CnC_Generals_Zero_Hour.git   0a05454
    generals-game-code      https://github.com/TheSuperHackers/GeneralsGameCode.git        d6837ff7
    generals-game-patch     https://github.com/TheSuperHackers/GeneralsGamePatch.git       6c7f40d6
    stride                  https://github.com/stride3d/stride.git                         e023d87

  None of it is ever committed (references/ is in .gitignore). The licences of the sources are in their repositories;
  see NOTICE.md.

.EXAMPLE
  pwsh scripts/get-references.ps1
  pwsh scripts/get-references.ps1 -Only ea-generals-zero-hour
#>
[CmdletBinding()]
param(
    # Fetch only these references (names of the target folders).
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repo 'references'
$sources = @(
    @{ Name = 'ea-generals-zero-hour'; Url = 'https://github.com/electronicarts/CnC_Generals_Zero_Hour.git'; Commit = '0a05454' }
    @{ Name = 'generals-game-code';    Url = 'https://github.com/TheSuperHackers/GeneralsGameCode.git';     Commit = 'd6837ff7' }
    @{ Name = 'generals-game-patch';   Url = 'https://github.com/TheSuperHackers/GeneralsGamePatch.git';    Commit = '6c7f40d6' }
    @{ Name = 'stride';                Url = 'https://github.com/stride3d/stride.git';                      Commit = 'e023d87' }
)
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Only.Count -gt 0) {
    $unknown = @($Only | Where-Object { $name = $_; -not ($sources | Where-Object { $_.Name -eq $name }) })
    if ($unknown.Count -gt 0) { throw "Unknown reference: $($unknown -join ', '). Known: $(($sources | ForEach-Object { $_.Name }) -join ', ')." }
    $sources = @($sources | Where-Object { $Only -contains $_.Name })
}
New-Item -ItemType Directory -Force -Path $target | Out-Null

$failed = 0
foreach ($source in $sources) {
    $directory = Join-Path $target $source.Name
    if (Test-Path -LiteralPath (Join-Path $directory '.git')) {
        Write-Host "== $($source.Name): present, fetching $($source.Commit)" -ForegroundColor Cyan
        git -C $directory fetch --quiet origin
    }
    else {
        Write-Host "== $($source.Name): cloning $($source.Url)" -ForegroundColor Cyan
        git clone --quiet $source.Url $directory
    }
    if ($LASTEXITCODE -ne 0) { Write-Warning "$($source.Name): git failed (exit code $LASTEXITCODE)."; $failed++; continue }
    git -C $directory -c advice.detachedHead=false checkout --quiet $source.Commit
    if ($LASTEXITCODE -ne 0) { Write-Warning "$($source.Name): commit $($source.Commit) cannot be checked out."; $failed++; continue }
    Write-Host "   at $(git -C $directory rev-parse --short HEAD) in $directory"
}
exit ($failed -gt 0 ? 1 : 0)
