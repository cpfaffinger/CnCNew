<#
.SYNOPSIS
  Creates a playable CnCNew game directory from your own Steam installation of Command & Conquer Generals Zero Hour.

.DESCRIPTION
  The Steam installation is only read, never changed. It must be exactly the known master version (a fresh English Steam installation,
  baselines/steam-master.json); anything else is refused with a list of the differences. Above it lies the community patch GeneralsGamePatch at the commit of
  baselines/patch-master.json: its files are fetched from that commit (36 MB; the network is needed once) and each is taken only with its recorded hash
  (docs/PATCH-LAYER.md). Everything is written below -Target:

    <Target>\content          the runtime content: the game's files with the patch above them (plain files, no .big archive)
    <Target>\install.json     what the installation was made from
    <Target>\bin              the published client
    <Target>\Start-CnCNew.cmd starts the client with this content

  The logic lives in tools/CnCNew.AssetTool (check-install, install); this script finds the game, builds the tool, publishes the client.
  Running it again on an existing installation verifies it and repairs missing or changed files.

.EXAMPLE
  pwsh scripts\install.ps1
  pwsh scripts\install.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Command & Conquer Generals - Zero Hour" -Target D:\Games\CnCNew
  pwsh scripts\install.ps1 -SkipClient      # content and install record only now; keeps an already published client
#>
[CmdletBinding()]
param(
    # The Steam installation of the game. Default: found through the default Steam path, the registry and Steam's library folders.
    [string]$GameDir,

    # Where the game directory is created. Outside the repository and outside Steam.
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'CnCNew\Game'),

    # Do not publish the client in this run.
    [switch]$SkipClient,

    # Content and install record only: no client, no start script (the developer installation, see dev-content.ps1).
    [switch]$ContentOnly,

    # A checkout of the patch (its GameFilesEdited directory): files found there are not fetched.
    [string]$PatchDir,

    # The game without the patch: what tells a defect of the patch from one of the port, and what the audits compare with (dev-content.ps1).
    [switch]$Original
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$master = Get-Content -LiteralPath (Join-Path $repo 'baselines\steam-master.json') -Raw | ConvertFrom-Json
$appId = $master.steamAppId

function Find-SteamGame {
    $default = 'C:\Program Files (x86)\Steam\steamapps\common\Command & Conquer Generals - Zero Hour'
    if (Test-Path -LiteralPath $default -PathType Container) {
        Write-Host "Found the game at the default Steam path."
        return $default
    }
    $steamRoots = @()
    foreach ($source in @(@('HKCU:\Software\Valve\Steam', 'SteamPath'), @('HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath'))) {
        $value = (Get-ItemProperty -LiteralPath $source[0] -Name $source[1] -ErrorAction SilentlyContinue).($source[1])
        if ($value) { $steamRoots += ($value -replace '/', '\') }
    }
    $libraries = @()
    foreach ($root in ($steamRoots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($line in Get-Content -LiteralPath $vdf) {
                if ($line -match '^\s*"path"\s+"(.+)"\s*$') { $libraries += ($Matches[1] -replace '\\\\', '\') }
            }
        }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $manifest = Join-Path $library "steamapps\appmanifest_$appId.acf"
        if (-not (Test-Path -LiteralPath $manifest)) { continue }
        foreach ($line in Get-Content -LiteralPath $manifest) {
            if ($line -match '^\s*"installdir"\s+"(.+)"\s*$') {
                $candidate = Join-Path $library (Join-Path 'steamapps\common' $Matches[1])
                if (Test-Path -LiteralPath $candidate -PathType Container) {
                    Write-Host "Found the game through Steam's library '$library' (app $appId)."
                    return $candidate
                }
            }
        }
    }
    throw "No Steam installation of '$($master.name)' (app $appId) was found. Install the game in Steam (English) or pass -GameDir."
}

if (-not $GameDir) { $GameDir = Find-SteamGame }
if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) { throw "Game directory not found: $GameDir" }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$Target = [IO.Path]::GetFullPath($Target, (Get-Location).Path)
Write-Host "Game directory (read only): $GameDir"
Write-Host "Target:                     $Target"

Write-Host "== Building the asset tool (Release) ..." -ForegroundColor Cyan
dotnet build (Join-Path $repo 'tools\CnCNew.AssetTool\CnCNew.AssetTool.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Building the asset tool failed (exit code $LASTEXITCODE)." }
$tool = Join-Path $repo 'tools\CnCNew.AssetTool\bin\Release\net10.0\CnCNew.AssetTool.dll'

Write-Host "== Checking the game and exporting its content ..." -ForegroundColor Cyan
$installArguments = @()
if ($PatchDir) { $installArguments += '--patch', $PatchDir }
if ($Original) { $installArguments += '--original' }
dotnet $tool install $GameDir $Target @installArguments
$installExit = $LASTEXITCODE
if ($installExit -ne 0) {
    Write-Host "The installation was not created (exit code $installExit; 1 = the game directory is not the required version, 2 = refused target or error)." -ForegroundColor Red
    exit $installExit
}

$client = Join-Path $Target 'bin\CnCNew.Client.Stride.exe'
$start = Join-Path $Target 'Start-CnCNew.cmd'
if (-not $ContentOnly) {
    if (-not $SkipClient) {
        Write-Host "== Publishing the client (Release) ..." -ForegroundColor Cyan
        dotnet publish (Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj') -c Release --nologo -v quiet -o (Join-Path $Target 'bin')
        if ($LASTEXITCODE -ne 0) { throw "Publishing the client failed (exit code $LASTEXITCODE)." }
    }
    if (Test-Path -LiteralPath $client) {
        # Paths relative to the script's own location: the installation can be moved as a whole.
        $lines = @(
            '@echo off',
            'rem Starts CnCNew with the content of this installation. Further options are passed on, e.g. --size 1920x1080.',
            'start "" "%~dp0bin\CnCNew.Client.Stride.exe" --content-dir "%~dp0content" %*'
        )
        [IO.File]::WriteAllText($start, ($lines -join "`r`n") + "`r`n", [Text.Encoding]::ASCII)
    }
}

Write-Host ''
Write-Host "Installation: $Target" -ForegroundColor Green
Write-Host "  content:     $(Join-Path $Target 'content')"
Write-Host "  record:      $(Join-Path $Target 'install.json')"
if (Test-Path -LiteralPath $start) {
    Write-Host "  client:      $client"
    Write-Host "Start the game with: $start"
} elseif ($ContentOnly) {
    Write-Host "Content only: no client was published and no start script was written."
} else {
    Write-Host "No client is published yet; run this script again without -SkipClient."
}
Write-Host "The Steam installation was only read. Remove this installation with: pwsh scripts\uninstall.ps1 -Target `"$Target`" -Yes"
exit 0
