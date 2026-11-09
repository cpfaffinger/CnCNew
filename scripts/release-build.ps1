<#
.SYNOPSIS
    Builds every asset of a release into one directory (docs/RELEASING.md). Both release pipelines (.gitlab-ci.yml and .github/workflows/release.yml) call this
    one script, so what a release holds is defined in one place; it can be run by hand on a clean checkout of the tag.

.DESCRIPTION
    Writes into <OutDir> (default artifacts/release), for version X.Y.Z:
        CnCNew-Launcher-X.Y.Z.exe              + .sha256   the launcher (scripts/package-launcher.ps1, tools/CnCNew.Launcher)
        CnCNew-Client-X.Y.Z-win-x64.zip        + .sha256   the self-contained client and patcher (scripts/package-client.ps1 -Zip)
        CnCNew-Server-X.Y.Z-win-x64.zip        + .sha256   the dedicated server (scripts/package-server.ps1 -Zip), unless -SkipServer
        CnCNew-Server-X.Y.Z-linux-x64.zip      + .sha256
        release-notes.md                                    the CHANGELOG.md section of the version (scripts/release-notes.ps1)
    The .sha256 files hold "<hash>  <file name>" (the format of sha256sum, which the launcher reads).

    It refuses when version.txt does not state X.Y.Z, and when the client package fails scripts/check-separation.ps1 (no file of the game or of a game data
    format may ever be released). It does NOT run scripts/audit-publish.ps1 (that needs the game data): the person who tags runs it first.

    Needs PowerShell 7, Git and the .NET 10 SDK (global.json). Run it from a clean checkout of the tag: the packages state their build's commit, and a dirty tree
    makes it "+modified".

.EXAMPLE
    pwsh scripts/release-build.ps1 -Version 0.2.0
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutDir = 'artifacts/release',
    [switch]$SkipServer,
    [switch]$SkipLauncher
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail([string]$message) { Write-Host "Release build failed: $message"; exit 1 }
function Run([string]$what, [scriptblock]$block) {
    Write-Host "== $what"
    & $block
    if ($LASTEXITCODE -ne 0) { Fail "$what (exit code $LASTEXITCODE)." }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "'$Version' is not MAJOR.MINOR.PATCH." }
$stated = (Get-Content -LiteralPath (Join-Path $root 'version.txt') -Raw).Trim()
if ($stated -ne $Version) { Fail "version.txt states $stated, the release is $Version (scripts/release.ps1 raises it in the release commit)." }
if (git status --porcelain) { Write-Host 'Note: the working tree has local changes - the packages state a "+modified" build.' }

$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { Remove-Item -Recurse -Force -LiteralPath $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$work = Join-Path $OutDir '_work'

function Publish-Asset([string]$source, [string]$name) {
    $destination = Join-Path $OutDir $name
    Copy-Item -LiteralPath $source -Destination $destination
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$destination.sha256", "$hash  $name`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("     {0}  {1:N1} MB  {2}" -f $name, ((Get-Item -LiteralPath $destination).Length / 1MB), $hash)
}

# --- the client and the patcher
Run 'Client package (win-x64, self-contained)' { pwsh -NoLogo -NoProfile -File scripts/package-client.ps1 -OutDir (Join-Path $work 'client') -Zip }
$clientDirectory = Join-Path $work 'client/cncnew-win-x64'
Run 'Separation check: the client package holds no original data' { pwsh -NoLogo -NoProfile -File scripts/check-separation.ps1 -Directory $clientDirectory }
Publish-Asset (Join-Path $work 'client/cncnew-win-x64.zip') "CnCNew-Client-$Version-win-x64.zip"

# --- the launcher (written by scripts/package-launcher.ps1, project tools/CnCNew.Launcher)
if (-not $SkipLauncher) {
    if (-not (Test-Path -LiteralPath 'scripts/package-launcher.ps1')) { Fail 'scripts/package-launcher.ps1 does not exist; a release needs the launcher (-SkipLauncher builds without).' }
    Run 'Launcher' { pwsh -NoLogo -NoProfile -File scripts/package-launcher.ps1 -OutDir (Join-Path $work 'launcher') }
    $launcher = @(Get-ChildItem -LiteralPath (Join-Path $work 'launcher') -Recurse -File -Filter '*.exe' | Sort-Object Length -Descending)
    if ($launcher.Count -eq 0) { Fail 'scripts/package-launcher.ps1 made no .exe.' }
    Publish-Asset $launcher[0].FullName "CnCNew-Launcher-$Version.exe"
}

# --- the dedicated server
if (-not $SkipServer) {
    Run 'Dedicated server packages (win-x64, linux-x64)' { pwsh -NoLogo -NoProfile -File scripts/package-server.ps1 -OutDir (Join-Path $work 'server') -Zip }
    foreach ($rid in 'win-x64', 'linux-x64') {
        Publish-Asset (Join-Path $work "server/cncnew-server-$rid.zip") "CnCNew-Server-$Version-$rid.zip"
    }
}

# --- the notes of the release page
Run 'Release notes' { pwsh -NoLogo -NoProfile -File scripts/release-notes.ps1 -Version $Version -OutFile (Join-Path $OutDir 'release-notes.md') }

Remove-Item -Recurse -Force -LiteralPath $work
Write-Host ''
Write-Host "Release $Version is in ${OutDir}:"
Get-ChildItem -LiteralPath $OutDir -File | ForEach-Object { Write-Host ("  {0}" -f $_.Name) }
exit 0
