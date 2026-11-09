<#
.SYNOPSIS
    Builds the public launcher (docs/INSTALLATION.md, "The public launcher") as one self-contained exe: CnCNew-Launcher-<version>.exe and its .sha256.

.DESCRIPTION
    Publishes tools/CnCNew.Launcher for win-x64 as a single compressed file with the .NET runtime inside (nothing to install, nothing beside it). The version is the one line
    of version.txt at the repository root, the same file the release uses. The exe holds no file of the game; the launcher fetches the client package of the release
    from GitHub and makes the game from the player's own Steam copy.
    <OutDir>\CnCNew-Launcher-<version>.exe
    <OutDir>\CnCNew-Launcher-<version>.exe.sha256     "<sha256>  CnCNew-Launcher-<version>.exe"
    A launcher for players is built from a clean working tree (the script says so when it is not).

.EXAMPLE
    pwsh scripts/package-launcher.ps1 -OutDir artifacts/launcher
#>
param(
    [string]$OutDir = 'artifacts/launcher'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = (Get-Content -Raw (Join-Path $root 'version.txt')).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Write-Host "version.txt must hold one line MAJOR.MINOR.PATCH, it holds '$version'."; exit 1 }
if (git status --porcelain) { Write-Host 'Note: the working tree has local changes; build a release launcher from a clean tree.' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) ("cncnew-launcher-publish-" + [Guid]::NewGuid().ToString('N'))

Write-Host "== Publishing the launcher $version (win-x64, self-contained, single file)"
dotnet publish tools/CnCNew.Launcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $work -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Publishing the launcher failed.'; exit 1 }
$built = Join-Path $work 'CnCNew-Launcher.exe'
if (-not (Test-Path $built)) { Write-Host 'The published launcher is missing.'; exit 1 }
$extra = @(Get-ChildItem $work -File | Where-Object { $_.Name -ne 'CnCNew-Launcher.exe' })
if ($extra.Count -gt 0) { Write-Host ("Note: files beside the exe: " + (($extra | ForEach-Object Name) -join ', ')) }

$name = "CnCNew-Launcher-$version.exe"
$target = Join-Path $OutDir $name
Copy-Item $built $target -Force
$hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$target.sha256", "$hash  $name`n", (New-Object Text.UTF8Encoding($false)))
Remove-Item -Recurse -Force $work
Write-Host ("     {0}: {1:N1} MB" -f $target, ((Get-Item $target).Length / 1MB))
Write-Host "     $target.sha256"
Write-Host "     sha256 $hash"
exit 0
