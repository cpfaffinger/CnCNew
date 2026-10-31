<#
.SYNOPSIS
    Builds the self-contained Windows package of the game (docs/INSTALLATION.md, "The package"): the client, the patcher that makes the content from the
    player's own Steam installation, the two scripts a player starts, the licence, the notice and the list of third-party licences.

.DESCRIPTION
    Publishes src/CnCNew.Client.Stride and tools/CnCNew.AssetTool for win-x64, self-contained (no .NET installation needed on the player's machine), into
    <OutDir>/cncnew-win-x64:
        bin\            the client (CnCNew.Client.Stride.exe)
        patcher\        the patcher (CnCNew.AssetTool.exe), with the description of the one supported installation built in
        Install-CnCNew.cmd   finds the Steam installation, checks it and exports the content into game\ beside it (reads the installation only)
        Start-CnCNew.cmd     starts the client with that content
        LICENSE.md, NOTICE.md, THIRD-PARTY.md, licences\ (the licence files the packages ship), README-FIRST.txt
    The package holds no file of the game; the content is made on the player's machine by Install-CnCNew.cmd. scripts/check-client-package.ps1 proves both.
    With -Zip the directory is also packed as <OutDir>/cncnew-win-x64.zip. The package states the build it was made from; a package for players is built from a
    clean working tree.

.EXAMPLE
    pwsh scripts/package-client.ps1 -Zip
#>
param(
    [string]$OutDir = 'artifacts/client',
    [switch]$Zip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (git status --porcelain) { Write-Host 'Note: the working tree has local changes - the package states a "+modified" build.' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$target = Join-Path $OutDir 'cncnew-win-x64'
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force -Path $target | Out-Null

Write-Host '== Publishing the client (win-x64, self-contained)'
dotnet publish src/CnCNew.Client.Stride -c Release -r win-x64 --self-contained true -o (Join-Path $target 'bin') -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Publishing the client failed.'; exit 1 }
Write-Host '== Publishing the patcher (win-x64, self-contained)'
dotnet publish tools/CnCNew.AssetTool -c Release -r win-x64 --self-contained true -o (Join-Path $target 'patcher') -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Publishing the patcher failed.'; exit 1 }

Copy-Item LICENSE.md, NOTICE.md, docs/THIRD-PARTY.md -Destination $target
# The licence and notice files the client's packages ship, beside the list of them (docs/THIRD-PARTY.md).
pwsh -NoLogo -NoProfile -File scripts/licence-report.ps1 -Check -CollectTo (Join-Path $target 'licences')
if ($LASTEXITCODE -ne 0) { Write-Host 'docs/THIRD-PARTY.md is not current (scripts/licence-report.ps1).'; exit 1 }
$commit = (git rev-parse HEAD).Trim()
$modified = if (git status --porcelain) { '+modified' } else { '' }
$crlf = "`r`n"
[IO.File]::WriteAllText((Join-Path $target 'Install-CnCNew.cmd'), (@(
    '@echo off',
    'rem Makes the content of CnCNew from your own Steam installation of Command & Conquer Generals - Zero Hour (English) and the community patch, fetched once from GitHub.',
    'rem The installation is only read. Pass its directory if it is not found: Install-CnCNew.cmd "D:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour"',
    'setlocal',
    'set "GAME=%~1"',
    'if "%GAME%"=="" set "GAME=auto"',
    '"%~dp0patcher\CnCNew.AssetTool.exe" install "%GAME%" "%~dp0game"',
    'if errorlevel 1 (echo. & echo The content was not made; the lines above say why. & pause & exit /b 1)',
    'echo. & echo Done. Start the game with Start-CnCNew.cmd.',
    'pause'
) -join $crlf) + $crlf, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $target 'Start-CnCNew.cmd'), (@(
    '@echo off',
    'if not exist "%~dp0game\content\content-manifest.json" (echo Run Install-CnCNew.cmd first: it makes the content from your Steam installation. & pause & exit /b 1)',
    'start "" "%~dp0bin\CnCNew.Client.Stride.exe" --content-dir "%~dp0game\content" %*'
) -join $crlf) + $crlf, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $target 'README-FIRST.txt'), (@(
    "CnCNew $commit$modified - a new engine for Command & Conquer Generals - Zero Hour.",
    '',
    'This package holds no file of the game. You need your own copy: the English Steam version of Zero Hour (Steam app 2732960), freshly installed.',
    '',
    '1. Run Install-CnCNew.cmd. It finds the Steam installation, checks every file of it against the one version this package knows, fetches the community patch',
    '   GeneralsGamePatch from GitHub (36 MB, once, each file checked against its recorded hash) and exports the content into the folder "game" beside this file',
    '   (about 3 GB). The Steam installation is only read, never changed.',
    '2. Run Start-CnCNew.cmd.',
    '',
    'Logs, crash reports and keys are kept in %LOCALAPPDATA%\CnCNew, settings and saved games in %APPDATA%\CnCNew. Nothing is sent anywhere.',
    'Help: docs/INSTALLATION.md and docs/TROUBLESHOOTING.md of the source repository (https://github.com/cpfaffinger/CnCNew).',
    'Licence: GPL-3.0-or-later (LICENSE.md); NOTICE.md says what this is and what it is not; THIRD-PARTY.md lists the packages it uses and their licences.'
) -join $crlf) + $crlf, (New-Object Text.UTF8Encoding($false)))

$files = @(Get-ChildItem $target -Recurse -File)
Write-Host ("     {0}: {1} files, {2:N1} MB" -f $target, $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
if ($Zip) {
    $archive = Join-Path $OutDir 'cncnew-win-x64.zip'
    if (Test-Path $archive) { Remove-Item -Force $archive }
    Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive
    Write-Host "     $archive"
}
exit 0
