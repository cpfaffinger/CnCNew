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

  Each folder is an ordinary clone of its upstream repository (remote `origin`), in detached HEAD at the recorded commit, so the
  state can be restored on any machine. With -Shallow only the recorded commit is fetched (no history; much smaller and faster,
  Stride in particular); a shallow folder can be completed later with `git -C references/<name> fetch --unshallow`.
  Running the script again repairs: a present folder is fetched and put back on the recorded commit; local changes inside a
  reference folder are never discarded (the checkout fails and the folder is reported).

  None of it is ever committed (references/ is in .gitignore). The licences of the sources are in their repositories;
  see NOTICE.md.

.EXAMPLE
  pwsh scripts/get-references.ps1
  pwsh scripts/get-references.ps1 -Only ea-generals-zero-hour
  pwsh scripts/get-references.ps1 -Shallow
#>
[CmdletBinding()]
param(
    # Fetch only these references (names of the target folders).
    [string[]]$Only = @(),

    # Fetch only the recorded commit of each reference instead of the whole history.
    [switch]$Shallow
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repo 'references'
$sources = @(
    @{ Name = 'ea-generals-zero-hour'; Url = 'https://github.com/electronicarts/CnC_Generals_Zero_Hour.git'; Commit = '0a05454d8574207440a5fb15241b98ad0b435590' }
    @{ Name = 'generals-game-code';    Url = 'https://github.com/TheSuperHackers/GeneralsGameCode.git';     Commit = 'd6837ff7adeb9b40787db49085d96e1fc1180424' }
    @{ Name = 'generals-game-patch';   Url = 'https://github.com/TheSuperHackers/GeneralsGamePatch.git';    Commit = '6c7f40d6af6a4babb77a33a4502ec7656446c13e' }
    @{ Name = 'stride';                Url = 'https://github.com/stride3d/stride.git';                      Commit = 'e023d874ba2985dbb132e9608f00d28d021b6f78' }
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
    $fresh = $false
    if (Test-Path -LiteralPath (Join-Path $directory '.git')) {
        Write-Host "== $($source.Name): present, expecting $($source.Commit.Substring(0, 8))" -ForegroundColor Cyan
        git -C $directory cat-file -e "$($source.Commit)^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) {
            if ($Shallow) { git -C $directory fetch --quiet --depth 1 origin $source.Commit } else { git -C $directory fetch --quiet origin }
        }
    }
    elseif ($Shallow) {
        Write-Host "== $($source.Name): fetching $($source.Commit.Substring(0, 8)) from $($source.Url) (shallow)" -ForegroundColor Cyan
        git init --quiet $directory
        git -C $directory remote add origin $source.Url
        git -C $directory fetch --quiet --depth 1 origin $source.Commit
    }
    else {
        Write-Host "== $($source.Name): cloning $($source.Url)" -ForegroundColor Cyan
        git clone --quiet --no-checkout $source.Url $directory
        $fresh = $true
    }
    if ($LASTEXITCODE -ne 0) { Write-Warning "$($source.Name): git failed (exit code $LASTEXITCODE)."; $failed++; continue }
    # Some reference trees hold paths longer than 260 characters.
    git -C $directory config core.longpaths true
    if ($fresh) {
        # A clone without checkout has an empty index; when its default branch already is the commit, a plain checkout writes nothing (the files count as deleted).
        git -C $directory -c advice.detachedHead=false checkout --quiet --force $source.Commit
    }
    elseif ((git -C $directory rev-parse HEAD 2>$null) -ne $source.Commit) {
        git -C $directory -c advice.detachedHead=false checkout --quiet $source.Commit
    }
    if ($LASTEXITCODE -ne 0) { Write-Warning "$($source.Name): commit $($source.Commit) cannot be checked out."; $failed++; continue }
    $head = git -C $directory rev-parse HEAD
    if ($head -ne $source.Commit) { Write-Warning "$($source.Name): HEAD is $head, expected $($source.Commit)."; $failed++; continue }
    Write-Host "   at $($head.Substring(0, 8)) in $directory"
}
exit ($failed -gt 0 ? 1 : 0)
