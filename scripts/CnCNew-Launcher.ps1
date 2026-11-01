<#
.SYNOPSIS
  The setup assistant and launcher of CnCNew: from a fresh clone on a machine that never saw this game to a running acceptance.

.DESCRIPTION
  Start it with a double-click on CnCNew-Launcher.cmd. It runs under the Windows PowerShell 5.1 that ships with Windows, because on a fresh machine PowerShell 7,
  which every other script needs, is one of the things it installs: keep this file free of PowerShell 7 syntax and in plain ASCII (5.1 reads a file without a byte
  order mark in the ANSI code page).

  It checks what the game needs, offers to install what is missing, builds the game from this checkout and its data from the player's own Steam installation (only
  read) and the community patch, starts the game once, compares the simulation with the reference values and writes a report. Afterwards it is the launcher: acceptance,
  play, get the new version. Running it again repeats only what is not done: every step knows its state.

    -Unattended          no window: installs what is missing, builds, writes the report and ends (exit code 1 when a required step failed)
    -CheckOnly           no window: prints the checks and ends; changes nothing
    -GameDir DIR         the Steam installation of the game (default: the Steam libraries of this machine)
    -InstallDir DIR      where the game is made (default: the folder chosen before, else CnCNew in the user's home); Steam itself is only read
    -NoWinget            leaves winget out: the fallbacks that download from the makers directly
    -NoSimulationCheck   leaves the comparison with the reference values out
    -SteamRoots DIR,...  further Steam installations to search
    -ReportPath FILE     where the report goes (default: the desktop)

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\CnCNew-Launcher.ps1 -CheckOnly
#>
[CmdletBinding()]
param(
    [switch]$Unattended,
    [switch]$CheckOnly,
    [string]$GameDir,
    [string]$InstallDir,
    [switch]$NoWinget,
    [switch]$NoSimulationCheck,
    [string[]]$SteamRoots,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $env:LOCALAPPDATA 'CnCNew'
$logDir = Join-Path $dataDir 'setup'
New-Item -ItemType Directory -Force $logDir | Out-Null
$clientStamp = Join-Path $dataDir 'acceptance\client-built.txt'
$gameFolderFile = Join-Path $dataDir 'game-folder.txt'
$installFolderFile = Join-Path $dataDir 'install-folder.txt'
$contentStamp = Join-Path $logDir 'content-done.txt'
$clientProject = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
$assetProject = Join-Path $repo 'tools\CnCNew.AssetTool\CnCNew.AssetTool.csproj'
$simProject = Join-Path $repo 'tools\CnCNew.SimTool\CnCNew.SimTool.csproj'
$steamMaster = Get-Content -LiteralPath (Join-Path $repo 'baselines\steam-master.json') -Raw | ConvertFrom-Json
$steamAppId = $steamMaster.steamAppId
$steamFolderName = 'Command & Conquer Generals - Zero Hour'
$wantedSdk = [version](Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
# The version the scripts were measured with; only the fallback without winget names one.
$powerShellVersion = '7.6.9'
# A check of this script itself runs in an empty profile and must not change the real user's PATH.
$isolated = [bool]$env:CNCNEW_SETUP_ISOLATED
# Windows sets ProgramFiles anew for every process, so that check names its empty folder here.
$programFiles = if ($env:CNCNEW_SETUP_PROGRAM_FILES) { $env:CNCNEW_SETUP_PROGRAM_FILES } else { $env:ProgramFiles }
$needForBuild = 6GB
$needForGame = 4GB

$script:form = $null
$script:cancel = $false
$script:busy = $false
$script:rows = New-Object System.Collections.ArrayList
$script:tools = @{ Dotnet = $null; Pwsh = $null; Git = $null }
$script:gameDir = $GameDir
$script:behind = 0
$script:report = New-Object System.Collections.ArrayList
$script:summary = ''

# ------------------------------------------------------------------------------------------------------------------ rows

function Add-Row([string]$Id, [string]$Title, [string]$Group) {
    [void]$script:rows.Add([pscustomobject]@{ Id = $Id; Title = $Title; Group = $Group; State = 'Waiting'; Detail = ''; Seconds = 0; Item = $null })
}
function Get-Row([string]$Id) { $script:rows | Where-Object { $_.Id -eq $Id } | Select-Object -First 1 }

Add-Row 'windows' 'Windows 10 or 11, 64-bit' 'need'
Add-Row 'folder' 'Folder of this checkout' 'need'
Add-Row 'disk' 'Free disk space' 'need'
Add-Row 'network' 'Internet (nuget.org, GitHub)' 'need'
Add-Row 'git' 'Git' 'need'
Add-Row 'dotnet' ".NET SDK $wantedSdk or newer" 'need'
Add-Row 'pwsh' 'PowerShell 7' 'need'
Add-Row 'vc' 'Visual C++ runtime (x64)' 'need'
Add-Row 'graphics' 'Graphics adapter (Direct3D 11)' 'need'
Add-Row 'sound' 'Sound device' 'need'
Add-Row 'game' 'Zero Hour in a Steam library' 'need'
Add-Row 'target' 'The game goes to this folder' 'need'
Add-Row 'content' 'Game data: from Steam and the patch' 'make'
Add-Row 'client' 'Game program: built from this checkout' 'make'
Add-Row 'verify' 'Game data checked file by file' 'make'
Add-Row 'start' 'First start of the game' 'make'
Add-Row 'sim' 'Simulation against the reference values' 'make'

$stateColors = @{ OK = 'ForestGreen'; Missing = 'Firebrick'; Failed = 'Firebrick'; Warning = 'DarkOrange'; Working = 'RoyalBlue'; Waiting = 'Gray'; 'To do' = 'Black'; Skipped = 'Gray' }

function Set-Row([string]$Id, [string]$State, [string]$Detail) {
    $row = Get-Row $Id
    $row.State = $State
    $row.Detail = $Detail
    if ($row.Item) {
        $row.Item.SubItems[1].Text = $State
        $row.Item.SubItems[2].Text = $Detail
        $row.Item.ForeColor = [Drawing.Color]::FromName($stateColors[$State])
        $row.Item.EnsureVisible()
        [Windows.Forms.Application]::DoEvents()
    } elseif ($Unattended -or $CheckOnly) {
        Write-Host ('{0,-8} {1,-44} {2}' -f $State, $row.Title, $Detail)
    }
}

function Say([string]$Text) {
    if ($script:statusLabel) { $script:statusLabel.Text = $Text; [Windows.Forms.Application]::DoEvents() }
    elseif ($Unattended) { Write-Host "== $Text" }
}

# ------------------------------------------------------------------------------------------------------------------ what is on this machine

function Update-EnvironmentPath {
    # An installer changes the registry, not this process: without this a program installed a minute ago is not found until the next start.
    if ($isolated) { return }
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@($machine, $user) | Where-Object { $_ }) -join ';'
}

function Add-ToolPath([string]$Directory) {
    if (($env:Path -split ';') -notcontains $Directory) { $env:Path = "$Directory;$env:Path" }
    if ($isolated) { return }
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $user) { $user = '' }
    if (($user -split ';') -notcontains $Directory) { [Environment]::SetEnvironmentVariable('Path', ((@($Directory, $user) | Where-Object { $_ }) -join ';'), 'User') }
}

function Test-Sdk([string]$Dotnet) {
    foreach ($line in @(& $Dotnet --list-sdks 2>$null)) {
        if ($line -match '^(\d+\.\d+\.\d+) ') {
            $version = [version]$Matches[1]
            if ($version.Major -eq $wantedSdk.Major -and $version.Minor -eq $wantedSdk.Minor -and $version.Build -ge $wantedSdk.Build) { return $Matches[1] }
        }
    }
    $null
}

function Find-Dotnet {
    $candidates = @()
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $candidates += $command.Source }
    $candidates += (Join-Path $programFiles 'dotnet\dotnet.exe')
    $candidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $sdk = Test-Sdk $candidate
            if ($sdk) { return [pscustomobject]@{ Path = $candidate; Version = $sdk } }
        }
    }
    $null
}

function Find-Pwsh {
    $candidates = @()
    $command = Get-Command pwsh.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $candidates += $command.Source }
    $candidates += (Join-Path $programFiles 'PowerShell\7\pwsh.exe')
    $candidates += (Join-Path $dataDir 'tools\pwsh\pwsh.exe')
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate) { return $candidate } }
    $null
}

function Find-Git {
    $candidates = @()
    $command = Get-Command git.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $candidates += $command.Source }
    $candidates += (Join-Path $programFiles 'Git\cmd\git.exe')
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate) { return $candidate } }
    $null
}

function Get-SteamLibraries {
    $roots = @()
    if ($SteamRoots) { $roots += $SteamRoots }
    $roots += 'C:\Program Files (x86)\Steam'
    foreach ($source in @(@('HKCU:\Software\Valve\Steam', 'SteamPath'), @('HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath'))) {
        $value = (Get-ItemProperty -LiteralPath $source[0] -Name $source[1] -ErrorAction SilentlyContinue).($source[1])
        if ($value) { $roots += ($value -replace '/', '\') }
    }
    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($line in Get-Content -LiteralPath $vdf) {
                if ($line -match '^\s*"path"\s+"(.+)"\s*$') { $libraries += ($Matches[1] -replace '\\\\', '\') }
            }
        }
    }
    $libraries | Select-Object -Unique
}

function Find-SteamGame {
    if ($script:gameDir -and (Test-Path -LiteralPath $script:gameDir -PathType Container)) { return $script:gameDir }
    if (Test-Path -LiteralPath $gameFolderFile) {
        $saved = (Get-Content -LiteralPath $gameFolderFile -Raw).Trim()
        if ($saved -and (Test-Path -LiteralPath $saved -PathType Container)) { return $saved }
    }
    foreach ($library in Get-SteamLibraries) {
        $manifest = Join-Path $library "steamapps\appmanifest_$steamAppId.acf"
        if (Test-Path -LiteralPath $manifest) {
            foreach ($line in Get-Content -LiteralPath $manifest) {
                if ($line -match '^\s*"installdir"\s+"(.+)"\s*$') {
                    $candidate = Join-Path $library (Join-Path 'steamapps\common' $Matches[1])
                    if (Test-Path -LiteralPath $candidate -PathType Container) { return $candidate }
                }
            }
        }
        $standard = Join-Path $library (Join-Path 'steamapps\common' $steamFolderName)
        if (Test-Path -LiteralPath $standard -PathType Container) { return $standard }
    }
    $null
}

function Test-Inside([string]$Path, [string]$Directory) {
    $inner = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $outer = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $inner.StartsWith($outer, [StringComparison]::OrdinalIgnoreCase)
}

function Get-DefaultInstallDir {
    $folder = Join-Path $env:USERPROFILE 'CnCNew'
    # A checkout there is no empty folder: the game then lies in the part of it Git ignores.
    if (Test-Inside $repo $folder) { return (Join-Path $repo 'game') }
    $folder
}

function Find-InstallDir {
    if ($InstallDir) { return [IO.Path]::GetFullPath($InstallDir) }
    if (Test-Path -LiteralPath $installFolderFile) {
        $saved = (Get-Content -LiteralPath $installFolderFile -Raw).Trim()
        if ($saved) { return $saved }
    }
    # A developer's checkout keeps the game it already has; a check of an empty machine has none.
    if (-not $isolated -and (Test-Path -LiteralPath (Join-Path $repo 'game\install.json'))) { return (Join-Path $repo 'game') }
    Get-DefaultInstallDir
}

function Set-InstallDir([string]$Folder) {
    $script:installDir = $Folder
    $script:contentDir = Join-Path $Folder 'content'
}

# Why a folder cannot take the game, or nothing. Steam is only ever read, so no folder of Steam will do.
function Test-InstallFolder([string]$Folder) {
    $full = [IO.Path]::GetFullPath($Folder).TrimEnd('\')
    if ($full.Length -le 2) { return 'A drive itself cannot be the folder: choose or make a folder on it.' }
    if ("$full\" -match '\\steamapps\\') { return 'This is a folder of Steam. Steam is only ever read: choose a folder outside it.' }
    foreach ($library in Get-SteamLibraries) {
        if ((Test-Inside $full $library) -or (Test-Inside $library $full)) { return "This overlaps the Steam library $library. Steam is only ever read: choose a folder outside it." }
    }
    if ($script:gameDir -and ((Test-Inside $full $script:gameDir) -or (Test-Inside $script:gameDir $full))) { return 'This overlaps the game in Steam. Steam is only ever read: choose a folder outside it.' }
    if ((Test-Inside $repo $full) -or ((Test-Inside $full $repo) -and -not (Test-Inside $full (Join-Path $repo 'game')))) { return 'This overlaps the checkout of the source code: choose another folder.' }
    if ((Test-Path -LiteralPath $full -PathType Container) -and (Get-ChildItem -LiteralPath $full -Force | Select-Object -First 1) -and -not (Test-Path -LiteralPath (Join-Path $full 'install.json'))) {
        return 'This folder holds other files: choose an empty one.'
    }
    if ($CheckOnly) { return $null }
    # A probe in the nearest folder that exists: nothing is made before the user starts.
    $existing = $full
    while (-not (Test-Path -LiteralPath $existing -PathType Container)) { $existing = Split-Path -Parent $existing; if (-not $existing) { return 'Its drive does not exist.' } }
    $probe = Join-Path $existing ('cncnew-probe-' + [Guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe, ''); [IO.File]::Delete($probe) }
    catch { return "This folder cannot be written ($existing): choose another one." }
    $null
}

function Test-Reachable([string]$HostName) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $attempt = $client.BeginConnect($HostName, 443, $null, $null)
        $attempt.AsyncWaitHandle.WaitOne(5000) -and $client.Connected
    } catch { $false } finally { $client.Close() }
}

function Get-Commit {
    if (-not $script:tools.Git) { return 'unknown' }
    $commit = & $script:tools.Git -C $repo rev-parse --short=10 HEAD 2>$null
    if (-not $commit) { return 'unknown' }
    if (& $script:tools.Git -C $repo status --porcelain 2>$null) { return "$commit-dirty" }
    $commit
}

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($Path))) } finally { $sha.Dispose() }
}

# What the data depend on: the two masters and the code that makes it. A new version that changes none of these keeps the data.
function Get-ContentKey {
    $key = @((Get-Sha256 (Join-Path $repo 'baselines\steam-master.json')), (Get-Sha256 (Join-Path $repo 'baselines\patch-master.json')))
    if ($script:tools.Git) { $key += @(& $script:tools.Git -C $repo log -1 '--format=%H' -- src/CnCNew.Import src/CnCNew.Formats src/CnCNew.Images tools/CnCNew.AssetTool 2>$null) }
    $key += $script:installDir
    $key -join ' '
}

function Find-Client {
    $dll = Get-ChildItem (Join-Path $repo 'src\CnCNew.Client.Stride\bin\Release') -Recurse -Filter 'CnCNew.Client.Stride.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($dll) { $dll.FullName }
}

function Test-ContentDone {
    (Test-Path -LiteralPath (Join-Path $contentDir 'content-manifest.json')) -and (Test-Path -LiteralPath $contentStamp) -and ((Get-Content -LiteralPath $contentStamp -Raw).Trim() -eq (Get-ContentKey))
}

function Test-ClientDone {
    (Find-Client) -and (Test-Path -LiteralPath $clientStamp) -and ((Get-Content -LiteralPath $clientStamp -Raw).Trim() -eq (Get-Commit))
}

function Invoke-Checks {
    Update-EnvironmentPath
    $os = [Environment]::OSVersion.Version
    if ([Environment]::Is64BitOperatingSystem -and $os.Major -ge 10) { Set-Row 'windows' 'OK' "Windows build $($os.Build)" }
    else { Set-Row 'windows' 'Failed' "Windows ${os}: the game needs 64-bit Windows 10 or 11." }

    if ($repo.Length -gt 100) { Set-Row 'folder' 'Failed' "$($repo.Length) characters deep: clone it nearer to the root (for example C:\Games\CnCNew), or a build breaks the Windows path limit." }
    elseif ($repo -match 'OneDrive') { Set-Row 'folder' 'Warning' 'In a synchronised folder: gigabytes get synchronised and files get locked. A plain folder is better.' }
    else { Set-Row 'folder' 'OK' $repo }

    # What is needed where: the build beside the checkout and in the profile, the game in its folder. What is done needs nothing more.
    Set-InstallDir (Find-InstallDir)
    $needs = @{}
    if (-not (Test-ClientDone)) { $needs[[IO.Path]::GetPathRoot($repo)] = $needForBuild }
    if (-not (Test-ContentDone)) { $root = [IO.Path]::GetPathRoot($script:installDir); $needs[$root] = [long]$needs[$root] + $needForGame }
    $short = @()
    foreach ($root in $needs.Keys) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $free = (New-Object System.IO.DriveInfo $root).AvailableFreeSpace
        if ($free -lt $needs[$root]) { $short += ('{0:0.0} GB free on {1}; about {2:0} GB are needed.' -f ($free / 1GB), $root, ($needs[$root] / 1GB)) }
    }
    $free = (New-Object System.IO.DriveInfo ([IO.Path]::GetPathRoot($repo))).AvailableFreeSpace
    if ($short) { Set-Row 'disk' 'Failed' ($short -join ' ') } else { Set-Row 'disk' 'OK' ('{0:0.0} GB free on {1}' -f ($free / 1GB), [IO.Path]::GetPathRoot($repo)) }

    $silent = @('api.nuget.org', 'raw.githubusercontent.com') | Where-Object { -not (Test-Reachable $_) }
    if (-not $silent) { Set-Row 'network' 'OK' 'nuget.org and GitHub answer' }
    else { Set-Row 'network' 'Warning' "No answer from $($silent -join ', '): the build and the patch need them the first time." }

    $script:tools.Git = Find-Git
    if ($script:tools.Git) { Add-SessionPath (Split-Path -Parent $script:tools.Git); Set-Row 'git' 'OK' ((& $script:tools.Git --version) -replace 'git version ', '') }
    else { Set-Row 'git' 'Missing' 'Will be installed. Needed to get new versions.' }

    $dotnet = Find-Dotnet
    if ($dotnet) { $script:tools.Dotnet = $dotnet.Path; Add-SessionPath (Split-Path -Parent $dotnet.Path); Set-Row 'dotnet' 'OK' "SDK $($dotnet.Version)" }
    else { $script:tools.Dotnet = $null; Set-Row 'dotnet' 'Missing' 'Will be installed (about 250 MB).' }

    $pwsh = Find-Pwsh
    if ($pwsh) { $script:tools.Pwsh = $pwsh; Add-SessionPath (Split-Path -Parent $pwsh); Set-Row 'pwsh' 'OK' $pwsh }
    else { $script:tools.Pwsh = $null; Set-Row 'pwsh' 'Missing' 'Will be installed (about 100 MB).' }

    if (Test-Path -LiteralPath (Join-Path $env:windir 'System32\vcruntime140.dll')) { Set-Row 'vc' 'OK' 'vcruntime140.dll is there' }
    else { Set-Row 'vc' 'Missing' 'Will be installed (about 25 MB). The game cannot start without it.' }

    $adapters = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | Where-Object { $_.Name } | ForEach-Object { $_.Name })
    if ($adapters) { Set-Row 'graphics' 'OK' ($adapters -join ', ') } else { Set-Row 'graphics' 'Warning' 'No adapter reported; the first start of the game will tell.' }

    Add-Type -Namespace CnCNewSetup -Name Sound -MemberDefinition '[DllImport("winmm.dll")] public static extern uint waveOutGetNumDevs();' -ErrorAction SilentlyContinue
    $devices = [CnCNewSetup.Sound]::waveOutGetNumDevs()
    if ($devices -gt 0) { Set-Row 'sound' 'OK' "$devices output device(s)" } else { Set-Row 'sound' 'Warning' 'None: the game runs silently and the goals about sound cannot be judged here.' }

    $found = Find-SteamGame
    $script:gameDir = $found
    if ($found) { Set-Row 'game' 'OK' $found } else { Set-Row 'game' 'Missing' "Not found. Install it in Steam (app $steamAppId) in English, or choose its folder." }

    $refusal = Test-InstallFolder $script:installDir
    if ($refusal) { Set-Row 'target' 'Failed' "$($script:installDir): $refusal" } else { Set-Row 'target' 'OK' $script:installDir }

    if (Test-ContentDone) { Set-Row 'content' 'OK' 'Made for this version' } else { Set-Row 'content' 'To do' 'About 5 minutes; fetches the patch once (110 MB)' }
    if (Test-ClientDone) { Set-Row 'client' 'OK' "Built at $(Get-Commit)" } else { Set-Row 'client' 'To do' 'About 5 minutes the first time; fetches 259 packages' }
    foreach ($id in 'verify', 'start', 'sim') {
        $file = Join-Path $dataDir "setup\$id-done.txt"
        if ((Test-Path -LiteralPath $file) -and ((Get-Content -LiteralPath $file -Raw).Trim() -eq (Get-Commit))) { Set-Row $id 'OK' 'Done for this version' }
        elseif ($id -eq 'sim' -and $NoSimulationCheck) { Set-Row $id 'Skipped' 'Left out' }
        else { Set-Row $id 'To do' $(switch ($id) { 'verify' { 'A minute' } 'start' { 'A window opens for a moment' } 'sim' { 'About 10 minutes; the first run of the game on this PC against the reference PC' } }) }
    }

    $script:behind = 0
    if ($script:tools.Git -and -not (Get-Row 'network').Detail.StartsWith('No answer')) {
        & $script:tools.Git -C $repo fetch --quiet origin master 2>$null
        if ($LASTEXITCODE -eq 0) { $script:behind = [int](& $script:tools.Git -C $repo rev-list --count 'HEAD..FETCH_HEAD' 2>$null) }
    }
}

function Add-SessionPath([string]$Directory) { if (($env:Path -split ';') -notcontains $Directory) { $env:Path = "$Directory;$env:Path" } }

# ------------------------------------------------------------------------------------------------------------------ running things

function Get-LastLine([string]$Path) {
    try {
        $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite,Delete')
        try {
            $length = [Math]::Min(2048, $stream.Length)
            if ($length -eq 0) { return '' }
            [void]$stream.Seek(-$length, 'End')
            $bytes = New-Object byte[] $length
            [void]$stream.Read($bytes, 0, $length)
        } finally { $stream.Dispose() }
        $lines = [Text.Encoding]::UTF8.GetString($bytes) -split "[\r\n]+" | Where-Object { $_.Trim() }
        if ($lines) { return (@($lines)[-1]).Trim() }
    } catch { Write-Verbose "$_" }
    ''
}

# Runs one program with its output in a log; the window stays alive meanwhile and shows the last line.
function Invoke-Logged([string]$Name, [string]$Title, [string]$File, [string]$Arguments) {
    $log = Join-Path $logDir "$Name.log"
    [IO.File]::WriteAllText($log, '')
    $line = "/c `"`"$File`" $Arguments >> `"$log`" 2>&1`""
    $process = Start-Process -FilePath $env:ComSpec -ArgumentList $line -WorkingDirectory $repo -WindowStyle Hidden -PassThru
    $null = $process.Handle
    $started = Get-Date
    $shown = ''
    while (-not $process.HasExited) {
        if ($script:cancel) { & "$env:windir\System32\taskkill.exe" /PID $process.Id /T /F 2>$null | Out-Null; return -1 }
        $last = Get-LastLine $log
        $seconds = [int]((Get-Date) - $started).TotalSeconds
        if ($script:statusLabel) {
            Say ("{0}  ({1}:{2:00})`n{3}" -f $Title, [int][Math]::Floor($seconds / 60), ($seconds % 60), $last)
        } elseif ($Unattended -and $last -and $last -ne $shown) { $shown = $last }
        Start-Sleep -Milliseconds 200
    }
    $process.WaitForExit()
    $process.ExitCode
}

function Save-Download([string]$Uri, [string]$File) {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $Uri -OutFile $File -UseBasicParsing
}

function Invoke-Winget([string]$Name, [string]$Id, [string]$Title) {
    if ($NoWinget) { return }
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $winget) { return }
    [void](Invoke-Logged $Name $Title $winget.Source "install --id $Id -e --source winget --accept-source-agreements --accept-package-agreements --silent")
    Update-EnvironmentPath
}

function Install-Dotnet {
    Set-Row 'dotnet' 'Working' 'Installing'
    Invoke-Winget 'install-dotnet' 'Microsoft.DotNet.SDK.10' 'Installing the .NET SDK (Windows may ask for permission)'
    if (Find-Dotnet) { return }
    Say 'Installing the .NET SDK for this user ...'
    $script = Join-Path $logDir 'dotnet-install.ps1'
    Save-Download 'https://dot.net/v1/dotnet-install.ps1' $script
    $directory = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    [void](Invoke-Logged 'install-dotnet-user' 'Installing the .NET SDK for this user' (Join-Path $PSHOME 'powershell.exe') "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Channel $($wantedSdk.Major).$($wantedSdk.Minor) -InstallDir `"$directory`" -NoPath")
    Add-ToolPath $directory
}

function Install-Pwsh {
    Set-Row 'pwsh' 'Working' 'Installing'
    Invoke-Winget 'install-pwsh' 'Microsoft.PowerShell' 'Installing PowerShell 7 (Windows may ask for permission)'
    if (Find-Pwsh) { return }
    Say 'Installing PowerShell 7 for this user ...'
    $zip = Join-Path $logDir 'pwsh.zip'
    Save-Download "https://github.com/PowerShell/PowerShell/releases/download/v$powerShellVersion/PowerShell-$powerShellVersion-win-x64.zip" $zip
    $directory = Join-Path $dataDir 'tools\pwsh'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $directory)
    Remove-Item -LiteralPath $zip -Force
    Add-ToolPath $directory
}

function Install-VcRuntime {
    Set-Row 'vc' 'Working' 'Installing'
    Invoke-Winget 'install-vc' 'Microsoft.VCRedist.2015+.x64' 'Installing the Visual C++ runtime (Windows may ask for permission)'
    if (Test-Path -LiteralPath (Join-Path $env:windir 'System32\vcruntime140.dll')) { return }
    Say 'Installing the Visual C++ runtime ...'
    $file = Join-Path $logDir 'vc_redist.x64.exe'
    Save-Download 'https://aka.ms/vs/17/release/vc_redist.x64.exe' $file
    [void](Invoke-Logged 'install-vc-direct' 'Installing the Visual C++ runtime (Windows may ask for permission)' $file '/install /quiet /norestart')
}

function Install-Git {
    Set-Row 'git' 'Working' 'Installing'
    Invoke-Winget 'install-git' 'Git.Git' 'Installing Git (Windows may ask for permission)'
}

function Set-Done([string]$Id) { [IO.File]::WriteAllText((Join-Path $logDir "$Id-done.txt"), (Get-Commit)) }

function Get-Refusal {
    $log = Join-Path $logDir 'content.log'
    if (-not (Test-Path -LiteralPath $log)) { return '' }
    $lines = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '^\s*(missing|changed|renamed|extra):' })
    if ($lines.Count -eq 0) { return '' }
    "$($lines.Count) file(s) differ from the required version. In Steam: game properties > General > Language English, then Installed Files > Verify integrity, and remove any mods."
}

function Add-ReportLine([string]$Line) { [void]$script:report.Add($Line) }

function Step-Content {
    if (Test-ContentDone) { return $true }
    Set-Row 'content' 'Working' 'Building the tool, checking the Steam installation, exporting'
    $patch = Join-Path $repo 'references\generals-game-patch\Patch104pZH\GameFilesEdited'
    $patchArgument = if (Test-Path -LiteralPath $patch) { " -PatchDir `"$patch`"" } else { '' }
    $exit = Invoke-Logged 'content' 'Making the game data from Steam and the patch' $script:tools.Pwsh "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $repo 'scripts\install.ps1')`" -GameDir `"$($script:gameDir)`" -Target `"$($script:installDir)`" -ContentOnly$patchArgument"
    if ($exit -ne 0) {
        $refusal = Get-Refusal
        Set-Row 'content' 'Failed' $(if ($refusal) { $refusal } else { "Exit code $exit. See the log." })
        return $false
    }
    [IO.File]::WriteAllText($contentStamp, (Get-ContentKey))
    Set-Row 'content' 'OK' 'Made for this version'
    $true
}

function Step-Client {
    if (Test-ClientDone) { return $true }
    Set-Row 'client' 'Working' 'Building'
    $exit = Invoke-Logged 'client' 'Building the game (the first time fetches its packages)' $script:tools.Dotnet "build `"$clientProject`" -c Release --nologo -v quiet"
    if ($exit -ne 0) { Set-Row 'client' 'Failed' "Exit code $exit. See the log."; return $false }
    New-Item -ItemType Directory -Force (Split-Path -Parent $clientStamp) | Out-Null
    $commit = Get-Commit
    if ($commit -notlike '*-dirty') { [IO.File]::WriteAllText($clientStamp, $commit) }
    Set-Row 'client' 'OK' "Built at $commit"
    $true
}

function Step-Verify {
    $done = Join-Path $logDir 'verify-done.txt'
    if ((Test-Path -LiteralPath $done) -and ((Get-Content -LiteralPath $done -Raw).Trim() -eq (Get-Commit))) { return $true }
    Set-Row 'verify' 'Working' 'Comparing every file with its recorded hash'
    $tool = Join-Path $repo 'tools\CnCNew.AssetTool\bin\Release\net10.0\CnCNew.AssetTool.dll'
    if (-not (Test-Path -LiteralPath $tool)) { [void](Invoke-Logged 'verify-build' 'Building the asset tool' $script:tools.Dotnet "build `"$assetProject`" -c Release --nologo -v quiet") }
    $exit = Invoke-Logged 'verify' 'Checking the game data file by file' $script:tools.Dotnet "`"$tool`" verify `"$contentDir`""
    if ($exit -ne 0) { Set-Row 'verify' 'Failed' "Exit code $exit. See the log."; return $false }
    Set-Done 'verify'
    Set-Row 'verify' 'OK' 'Every file as recorded'
    $true
}

function Step-Start {
    $done = Join-Path $logDir 'start-done.txt'
    if ((Test-Path -LiteralPath $done) -and ((Get-Content -LiteralPath $done -Raw).Trim() -eq (Get-Commit))) { return $true }
    Set-Row 'start' 'Working' 'A window of the game opens and closes by itself'
    $exit = Invoke-Logged 'start' 'Starting the game once (the first start prepares its shaders)' $script:tools.Dotnet "`"$(Find-Client)`" --smoke-test --content-dir `"$contentDir`" --size 1280x720 --match `"Maps/Alpine Assault/Alpine Assault.map`" --frames 120"
    if ($exit -ne 0) { Set-Row 'start' 'Failed' "Exit code ${exit}: the game did not run here. See the log, and hand it back."; return $false }
    Set-Done 'start'
    Set-Row 'start' 'OK' 'The game ran here'
    $true
}

# Not required for the acceptance: a difference here is itself a finding (two machines that compute differently cannot play together), so it is reported, not fatal.
function Step-Simulation {
    if ($NoSimulationCheck) { Set-Row 'sim' 'Skipped' 'Left out'; return $true }
    $done = Join-Path $logDir 'sim-done.txt'
    if ((Test-Path -LiteralPath $done) -and ((Get-Content -LiteralPath $done -Raw).Trim() -eq (Get-Commit))) { return $true }
    Set-Row 'sim' 'Working' 'Building the tool'
    $exit = Invoke-Logged 'sim-build' 'Building the measuring tool' $script:tools.Dotnet "build `"$simProject`" -c Release --nologo -v quiet"
    if ($exit -ne 0) { Set-Row 'sim' 'Warning' "The tool did not build (exit code $exit)."; return $true }
    $tool = Join-Path $repo 'tools\CnCNew.SimTool\bin\Release\net10.0\CnCNew.SimTool.dll'
    Set-Row 'sim' 'Working' 'The data against the reference values'
    $inputs = Invoke-Logged 'sim-inputs' 'Comparing the data with the reference values' $script:tools.Dotnet "`"$tool`" input-manifest check `"$contentDir`" baselines/input-manifest.json"
    Set-Row 'sim' 'Working' 'Nine matches against their recorded results'
    $matches9 = Invoke-Logged 'sim-matches' 'Playing nine matches against their recorded results' $script:tools.Dotnet "`"$tool`" reference-manifest check `"$contentDir`" baselines/reference-manifest.json --set quick"
    Add-ReportLine "Input manifest: exit code $inputs. $(Get-LastLine (Join-Path $logDir 'sim-inputs.log'))"
    Add-ReportLine "Reference matches, quick set: exit code $matches9. $(Get-LastLine (Join-Path $logDir 'sim-matches.log'))"
    if ($inputs -eq 0 -and $matches9 -eq 0) { Set-Done 'sim'; Set-Row 'sim' 'OK' 'The same results as on the reference PC'; return $true }
    Set-Row 'sim' 'Warning' 'This PC computes differently: a finding. The acceptance can go on; hand back the report.'
    $true
}

function Write-Report {
    $file = $ReportPath
    if (-not $file) { $file = Join-Path ([Environment]::GetFolderPath('Desktop')) ('CnCNew-setup-report-{0:yyyy-MM-dd}.md' -f (Get-Date)) }
    $cpu = (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1).Name
    $memory = (Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).TotalPhysicalMemory
    $lines = New-Object System.Collections.ArrayList
    [void]$lines.Add('# CnCNew setup report')
    [void]$lines.Add('')
    [void]$lines.Add("Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm'). Commit: $(Get-Commit). Windows: $([Environment]::OSVersion.Version). CPU: $cpu. Memory: $('{0:0} GB' -f ($memory / 1GB)).")
    [void]$lines.Add('')
    [void]$lines.Add('| Step | State | Detail |')
    [void]$lines.Add('| --- | --- | --- |')
    foreach ($row in $script:rows) { [void]$lines.Add("| $($row.Title) | $($row.State) | $($row.Detail -replace '\|', '/') |") }
    [void]$lines.Add('')
    foreach ($line in $script:report) { [void]$lines.Add("- $line") }
    [IO.File]::WriteAllLines($file, [string[]]$lines)
    $script:reportFile = $file
    $file
}

function Test-Required {
    foreach ($id in 'windows', 'folder', 'disk', 'game', 'target') {
        if ((Get-Row $id).State -in 'Failed', 'Missing') { return $false }
    }
    $true
}

function Invoke-Setup {
    $script:busy = $true
    try {
        if (-not (Test-Required)) { return $false }
        [IO.File]::WriteAllText($installFolderFile, $script:installDir)
        if ((Get-Row 'git').State -eq 'Missing') { Install-Git }
        if ((Get-Row 'dotnet').State -eq 'Missing') { Install-Dotnet }
        if ((Get-Row 'pwsh').State -eq 'Missing') { Install-Pwsh }
        if ((Get-Row 'vc').State -eq 'Missing') { Install-VcRuntime }
        if ($script:cancel) { return $false }
        Invoke-Checks
        foreach ($id in 'dotnet', 'pwsh', 'vc') {
            if ((Get-Row $id).State -ne 'OK') { Set-Row $id 'Failed' 'Could not be installed. See the log folder.'; return $false }
        }
        # The pitfall of BUILDING.md: a user configuration without nuget.org fails the restore with NU1101.
        if (@(& $script:tools.Dotnet nuget list source 2>$null) -notmatch 'nuget\.org') { & $script:tools.Dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org 2>$null | Out-Null }
        foreach ($step in 'Step-Content', 'Step-Client', 'Step-Verify', 'Step-Start', 'Step-Simulation') {
            if ($script:cancel) { return $false }
            if (-not (& $step)) { return $false }
        }
        $true
    } finally {
        $script:busy = $false
    }
}

# ------------------------------------------------------------------------------------------------------------------ no window

if ($CheckOnly) {
    Invoke-Checks
    if ($script:behind -gt 0) { Write-Host "$($script:behind) newer commit(s) on GitHub" }
    exit 0
}

if ($Unattended) {
    Invoke-Checks
    $ok = Invoke-Setup
    Add-ReportLine "Finished: $ok"
    $file = Write-Report
    Write-Host "Report: $file"
    if (-not $ok) {
        foreach ($row in $script:rows) { if ($row.State -in 'Failed', 'Missing') { Write-Host "$($row.Title): $($row.Detail)" } }
        Write-Host "Logs: $logDir"
        exit 1
    }
    exit 0
}

# ------------------------------------------------------------------------------------------------------------------ the window

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object Windows.Forms.Form
$script:form = $form
$form.Text = 'CnCNew'
$form.StartPosition = 'CenterScreen'
$form.ClientSize = New-Object Drawing.Size 820, 720
$form.MinimumSize = New-Object Drawing.Size 700, 560
$form.Font = New-Object Drawing.Font 'Segoe UI', 10

$head = New-Object Windows.Forms.Panel
$head.Dock = 'Top'; $head.Height = 64; $head.BackColor = [Drawing.Color]::FromArgb(32, 48, 72)
$title = New-Object Windows.Forms.Label
$title.Text = 'CnCNew setup'; $title.ForeColor = 'White'; $title.Font = New-Object Drawing.Font 'Segoe UI', 15, ([Drawing.FontStyle]::Bold); $title.SetBounds(14, 6, 780, 30)
$subtitle = New-Object Windows.Forms.Label
$subtitle.Text = 'Makes the game in a folder of its own from your Steam installation. Steam itself is only read, never changed.'; $subtitle.ForeColor = 'White'; $subtitle.SetBounds(14, 37, 780, 22)
$head.Controls.AddRange(@($title, $subtitle))

$list = New-Object Windows.Forms.ListView
$list.Dock = 'Fill'; $list.View = 'Details'; $list.FullRowSelect = $true; $list.HeaderStyle = 'None'; $list.BorderStyle = 'None'
[void]$list.Columns.Add('Step', 290); [void]$list.Columns.Add('State', 76); [void]$list.Columns.Add('Detail', 430)
foreach ($row in $script:rows) {
    $item = New-Object Windows.Forms.ListViewItem $row.Title
    [void]$item.SubItems.Add($row.State); [void]$item.SubItems.Add('')
    $item.UseItemStyleForSubItems = $true
    [void]$list.Items.Add($item)
    $row.Item = $item
}

$foot = New-Object Windows.Forms.Panel
$foot.Dock = 'Bottom'; $foot.Height = 216
$script:statusLabel = New-Object Windows.Forms.Label
$script:statusLabel.SetBounds(14, 8, 790, 62); $script:statusLabel.Anchor = 'Top,Left,Right'
$progress = New-Object Windows.Forms.ProgressBar
$progress.SetBounds(14, 72, 790, 8); $progress.Anchor = 'Top,Left,Right'; $progress.Style = 'Marquee'; $progress.MarqueeAnimationSpeed = 30; $progress.Visible = $false
$main = New-Object Windows.Forms.Button
$main.SetBounds(14, 88, 290, 44); $main.Font = New-Object Drawing.Font 'Segoe UI', 11, ([Drawing.FontStyle]::Bold)
$play = New-Object Windows.Forms.Button
$play.Text = 'Play the game'; $play.SetBounds(312, 88, 150, 44)
$update = New-Object Windows.Forms.Button
$update.SetBounds(470, 88, 334, 44); $update.Visible = $false
$chooseTarget = New-Object Windows.Forms.Button
$chooseTarget.Text = 'Choose the install folder ...'; $chooseTarget.SetBounds(14, 136, 290, 32)
$choose = New-Object Windows.Forms.Button
$choose.Text = 'Choose the game in Steam ...'; $choose.SetBounds(14, 176, 290, 32)
$steamPage = New-Object Windows.Forms.Button
$steamPage.Text = 'Install it in Steam'; $steamPage.SetBounds(312, 176, 150, 32)
$again = New-Object Windows.Forms.Button
$again.Text = 'Check again'; $again.SetBounds(312, 136, 150, 32)
$logs = New-Object Windows.Forms.Button
$logs.Text = 'Open the logs'; $logs.SetBounds(470, 136, 150, 32)
$close = New-Object Windows.Forms.Button
$close.Text = 'Close'; $close.SetBounds(684, 136, 120, 32)
$foot.Controls.AddRange(@($script:statusLabel, $progress, $main, $play, $update, $chooseTarget, $choose, $steamPage, $again, $logs, $close))

$form.Controls.AddRange(@($list, $foot, $head))
$list.BringToFront()

function Update-Buttons {
    $done = -not ($script:rows | Where-Object { $_.Group -eq 'make' -and $_.State -notin 'OK', 'Skipped', 'Warning' })
    $gameMissing = (Get-Row 'game').State -eq 'Missing'
    $blocked = -not (Test-Required)
    $missing = @($script:rows | Where-Object { $_.Group -eq 'need' -and $_.State -eq 'Missing' -and $_.Id -ne 'game' } | ForEach-Object { $_.Title })
    $main.Enabled = -not $script:busy -and -not $blocked
    $main.Text = if ($done) { 'Start the acceptance' } else { 'Set up the game' }
    $play.Enabled = $done -and -not $script:busy
    $play.Visible = $done
    foreach ($button in @($choose, $steamPage)) { $button.Visible = $gameMissing -or ((Get-Row 'content').State -eq 'Failed'); $button.Enabled = -not $script:busy }
    $again.Enabled = -not $script:busy
    $chooseTarget.Enabled = -not $script:busy
    $update.Visible = $script:behind -gt 0
    $update.Enabled = -not $script:busy
    $update.Text = "Get the new version ($($script:behind) change(s) on GitHub)"
    $progress.Visible = $script:busy
    if ($script:busy) { return }
    if ($blocked) {
        $failed = @($script:rows | Where-Object { $_.Group -eq 'need' -and $_.State -in 'Failed', 'Missing' -and $_.Id -notin 'git', 'dotnet', 'pwsh', 'vc' } | ForEach-Object { "$($_.Title): $($_.Detail)" })
        Say ($failed -join "`n")
    } elseif ($done) {
        $warning = (Get-Row 'sound').State -eq 'Warning'
        Say ("Ready. Everything is set up for version $(Get-Commit)." + $(if ($warning) { "`nThis PC has no sound device: skip the goals about sound." } else { '' }))
    } else {
        $text = "The game goes to $($script:installDir). Steam is only read. The rest is automatic and takes 15 to 30 minutes; you can stop and go on later."
        if ($missing.Count -gt 0) { $text = "Set up will also install, each from its maker and under its own licence: $($missing -join ', '). Windows may ask for permission.`n$text" }
        Say $text
    }
}

function Invoke-Guarded([scriptblock]$Action) {
    try { & $Action } catch { $script:busy = $false; Say "Something went wrong: $($_.Exception.Message)`nThe logs are in $logDir"; $progress.Visible = $false }
}

function Start-Acceptance {
    Start-Process -FilePath $script:tools.Pwsh -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-File', "`"$(Join-Path $repo 'scripts\acceptance.ps1')`"", '-Suite', '-ContentDir', "`"$contentDir`"" -WorkingDirectory $repo
    $form.Close()
}

$main.Add_Click({
        Invoke-Guarded {
            if ($main.Text -eq 'Start the acceptance') { Start-Acceptance; return }
            $script:busy = $true
            Update-Buttons
            $ok = Invoke-Setup
            Add-ReportLine "Finished: $ok"
            $file = Write-Report
            $script:busy = $false
            if ($script:cancel) { $form.Close(); return }
            Update-Buttons
            if (-not $ok) {
                $failed = @($script:rows | Where-Object { $_.State -eq 'Failed' } | Select-Object -First 1)
                Say ("Stopped at: $($failed[0].Title).`n$($failed[0].Detail)`nThe report lies at $file")
            } else { Say ("Ready. The report lies at $file.`nStart the acceptance when you like.") }
        }
    })
$play.Add_Click({
        Invoke-Guarded {
            Start-Process -FilePath $script:tools.Dotnet -ArgumentList "`"$(Find-Client)`" --content-dir `"$contentDir`"" -WorkingDirectory $repo -WindowStyle Hidden
            Say 'The game is starting; its window opens in about 20 seconds.'
        }
    })
$update.Add_Click({
        Invoke-Guarded {
            $script:busy = $true
            Update-Buttons
            $exit = Invoke-Logged 'update' 'Getting the new version' $script:tools.Git "-C `"$repo`" pull --ff-only origin master"
            $script:busy = $false
            Invoke-Checks
            Update-Buttons
            if ($exit -ne 0) { Say 'The new version could not be taken (see the log update.log). Changes in this folder stand in the way.' }
            else { Say 'The new version is here. Set up the game once more; only what changed is made again.' }
        }
    })
$chooseTarget.Add_Click({
        Invoke-Guarded {
            $dialog = New-Object Windows.Forms.FolderBrowserDialog
            $dialog.Description = 'Where the game is made: about 3 GB. Steam is only read and stays as it is.'
            $dialog.ShowNewFolderButton = $true
            $start = $script:installDir
            while ($start -and -not (Test-Path -LiteralPath $start -PathType Container)) { $start = Split-Path -Parent $start }
            if ($start) { $dialog.SelectedPath = $start }
            if ($dialog.ShowDialog($form) -ne 'OK') { return }
            $folder = $dialog.SelectedPath.TrimEnd('\')
            # As installers do: a folder that holds other things gets a CnCNew of its own.
            if ((Get-ChildItem -LiteralPath $dialog.SelectedPath -Force -ErrorAction SilentlyContinue | Select-Object -First 1) -and -not (Test-Path -LiteralPath (Join-Path $folder 'install.json'))) { $folder = Join-Path $folder 'CnCNew' }
            $refusal = Test-InstallFolder $folder
            if ($refusal) { Say "$folder`n$refusal"; return }
            [IO.File]::WriteAllText($installFolderFile, $folder)
            Invoke-Checks
            Update-Buttons
        }
    })
$choose.Add_Click({
        Invoke-Guarded {
            $dialog = New-Object Windows.Forms.FolderBrowserDialog
            $dialog.Description = "The folder of Zero Hour in your Steam library: ...\steamapps\common\$steamFolderName"
            if ($dialog.ShowDialog($form) -eq 'OK') {
                if (-not (Get-ChildItem -LiteralPath $dialog.SelectedPath -Filter '*.big' -ErrorAction SilentlyContinue)) { Say 'That folder holds no game archives (*.big).'; return }
                [IO.File]::WriteAllText($gameFolderFile, $dialog.SelectedPath)
                $script:gameDir = $dialog.SelectedPath
                Invoke-Checks
                Update-Buttons
            }
        }
    })
$steamPage.Add_Click({ Invoke-Guarded { Start-Process "steam://install/$steamAppId" } })
$again.Add_Click({ Invoke-Guarded { Say 'Checking ...'; Invoke-Checks; Update-Buttons } })
$logs.Add_Click({ Invoke-Guarded { Start-Process explorer.exe -ArgumentList "`"$logDir`"" } })
$close.Add_Click({ $form.Close() })
$form.Add_FormClosing({
        param($sender, $closing)
        # The running step is stopped first; the click that started it closes the window when it has unwound.
        if ($script:busy) { $script:cancel = $true; $closing.Cancel = $true; Say 'Stopping ...' }
    })
$form.Add_Shown({
        $form.Activate()
        Invoke-Guarded { Say 'Checking this PC ...'; Invoke-Checks; Update-Buttons }
    })
[Windows.Forms.Application]::Run($form)
