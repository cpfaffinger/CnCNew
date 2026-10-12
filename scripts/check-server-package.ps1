<#
.SYNOPSIS
    The self-contained package of the dedicated server, built and used (docs/DEDICATED-SERVER.md section 13).

.DESCRIPTION
    -Platform windows (the default):
      - builds the win-x64 and the linux-x64 package with scripts/package-server.ps1 into a directory outside the repository;
      - checks what is in them: the server, the protocol, the room and the two libraries those reference - and nothing of the game layer, the
        client, the importer or Stride, and no Windows-only library in the Linux package; the start guide, the sample configuration and the
        licence are there, and the sample configuration is one the server accepts; a ban file is read, and one with a line that is no entry
        stops the server at its start with the number of the line;
      - starts the server of the win-x64 package (its own program, not "dotnet <dll>") and plays a match of three client processes and five
        computer players on it through the proxy; every client must end with the same hash chain, and the server must have agreed on every tick.
    -Platform linux:
      - builds the linux-x64 package (under Windows) and starts its server in the WSL distribution with an empty environment - no DOTNET_ROOT,
        a PATH of the system's directories only, HOME=/tmp - so that it cannot use a .NET runtime of the user; three client processes under
        Windows play a match on it over the address of the distribution.
      Without WSL or without the distribution the step ends with exit code 3: NOT RUN, never passed.

    What this does not show: the package on a machine that has never seen a .NET installation (the Windows machine here has the SDK; the Linux
    side is started without access to its runtime, but it is the same computer), and a Linux distribution other than the one in WSL.

.EXAMPLE
    pwsh scripts/check-server-package.ps1
    pwsh scripts/check-server-package.ps1 -Platform linux
#>
param(
    [ValidateSet('windows', 'linux')][string]$Platform = 'windows',
    [string]$ContentDir = 'game/content',
    [string]$Distribution = 'Debian',
    [int]$Seconds = 30,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) "cncnew-test/server-package-$Platform"),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this check needs it for its clients (scripts/dev-content.ps1)."
    exit 2
}
if ($Platform -eq 'linux') {
    $answer = $null
    try { $answer = (& wsl.exe -d $Distribution --exec /bin/sh -c 'echo ok' 2>$null) -replace "`0", '' } catch { $answer = $null }
    if ("$answer".Trim() -ne 'ok') {
        Write-Host "NOT RUN: no WSL distribution '$Distribution' on this machine - the Linux package was not started and nothing is proved about it."
        exit 3
    }
}
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
$packages = Join-Path $OutDir 'packages'
$runtimes = $Platform -eq 'linux' ? @('linux-x64') : @('win-x64', 'linux-x64')
& (Join-Path $PSScriptRoot 'package-server.ps1') -OutDir $packages -Runtime $runtimes
if ($LASTEXITCODE -ne 0) { Write-Host 'Building the packages failed.'; exit 1 }
if (-not $NoBuild) {
    dotnet build -c Release tools/CnCNew.NetTool -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building tools/CnCNew.NetTool failed.'; exit 1 }
}
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }

# ---- What is in a package.
$allowed = 'CnCNew.DedicatedServer', 'CnCNew.Net', 'CnCNew.Server', 'CnCNew.Simulation', 'CnCNew.Data'
foreach ($rid in $runtimes) {
    $directory = Join-Path $packages $rid
    $names = @(Get-ChildItem $directory -File | ForEach-Object Name)
    $own = @($names | Where-Object { $_ -like 'CnCNew.*.dll' } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
    $foreign = @($own | Where-Object { $_ -notin $allowed })
    if ($foreign.Count -gt 0) { Fail "$rid holds assemblies a relay must not need: $($foreign -join ', ')" }
    if (@($allowed | Where-Object { $_ -notin $own }).Count -gt 0) { Fail "$rid lacks one of: $($allowed -join ', ')" }
    $stride = @($names | Where-Object { $_ -match '^(Stride|SharpDX|Silk|SDL|OpenAL|NAudio|FFmpeg|BulletSharp)' })
    if ($stride.Count -gt 0) { Fail "$rid holds libraries of the client: $($stride -join ', ')" }
    if ($rid -like 'linux-*') {
        $windowsOnly = @($names | Where-Object { $_ -match '\.exe$' -or $_ -match '^(System\.Windows\.Forms|PresentationCore|coreclr\.dll|hostfxr\.dll|clrjit\.dll)' })
        if ($windowsOnly.Count -gt 0) { Fail "the Linux package holds Windows files: $($windowsOnly -join ', ')" }
        if ('CnCNew.DedicatedServer' -notin $names -or 'libcoreclr.so' -notin $names) { Fail 'the Linux package lacks its program or its runtime' }
    }
    else {
        if ('CnCNew.DedicatedServer.exe' -notin $names -or 'coreclr.dll' -notin $names) { Fail 'the Windows package lacks its program or its runtime' }
    }
    foreach ($file in 'START.md', 'server.sample.json', 'LICENSE.md', 'NOTICE.md') { if ($file -notin $names) { Fail "$rid lacks $file" } }
    if ($failed -eq 0) { Write-Host "     ${rid}: $($names.Count) files; of the project only $($own -join ', '); nothing of the client, the game layer or the importer" }
}

$tool = 'tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll'
$map = 'Maps/Twilight Flame/Twilight Flame.map'
if ($Platform -eq 'windows') {
    # ---- The sample configuration is one the server takes (an unknown key would be an error), started as the package's own program.
    $work = Join-Path $OutDir 'sample'
    New-Item -ItemType Directory -Force $work | Out-Null
    Copy-Item (Join-Path $packages 'win-x64/server.sample.json') (Join-Path $work 'server.json')
    $exe = Join-Path $packages 'win-x64/CnCNew.DedicatedServer.exe'
    $sample = Start-Process -FilePath $exe -ArgumentList @('--config', 'server.json', '--port', '0', '--address', '127.0.0.1', '--run-seconds', '2') -WorkingDirectory $work -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $work 'server.log') -RedirectStandardError (Join-Path $work 'server.err')
    if (-not $sample.WaitForExit(30000)) { $sample.Kill($true); Fail 'the server with the sample configuration did not stop' }
    $lines = @(Get-Content (Join-Path $work 'server.log') -ErrorAction SilentlyContinue)
    if (-not ($lines | Where-Object { $_ -match ' event=listening .* key=[0-9A-F]{64}' })) { Fail "the server did not start with the sample configuration ($(Get-Content (Join-Path $work 'server.err') -ErrorAction SilentlyContinue | Select-Object -First 2))" }
    elseif (-not (Test-Path (Join-Path $work 'server-key.pem')) -or -not (Test-Path (Join-Path $work 'logs'))) { Fail 'the sample configuration made no key file or no log directory' }
    else { Write-Host '     the server of the win-x64 package starts with the sample configuration, makes its key file and writes its log' }

    # ---- A ban file with a line that is no entry stops the server at its start (a typing mistake must not unban everybody); a good one is read.
    Set-Content (Join-Path $work 'bans.txt') @('# test', '203.0.113.7', '2001:db8::/48', 'not an address')
    $bad = Start-Process -FilePath $exe -ArgumentList @('--config', 'server.json', '--port', '0', '--address', '127.0.0.1', '--run-seconds', '2') -WorkingDirectory $work -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $work 'bad-bans.log') -RedirectStandardError (Join-Path $work 'bad-bans.err')
    if (-not $bad.WaitForExit(30000)) { $bad.Kill($true); Fail 'the server with a bad ban file did not stop' }
    elseif ($bad.ExitCode -ne 2 -or -not ((Get-Content (Join-Path $work 'bad-bans.err') -Raw) -match 'line 4')) { Fail "a ban file with a line that is no entry did not stop the server with its line number (exit code $($bad.ExitCode))" }
    Set-Content (Join-Path $work 'bans.txt') @('# test', '203.0.113.7', '2001:db8::/48')
    $good = Start-Process -FilePath $exe -ArgumentList @('--config', 'server.json', '--port', '0', '--address', '127.0.0.1', '--run-seconds', '2') -WorkingDirectory $work -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $work 'good-bans.log') -RedirectStandardError (Join-Path $work 'good-bans.err')
    if (-not $good.WaitForExit(30000)) { $good.Kill($true); Fail 'the server with a ban file did not stop' }
    elseif (-not (Get-Content (Join-Path $work 'good-bans.log') | Where-Object { $_ -match ' event=listening .* bans=2 ' })) { Fail 'the server did not read a ban file of two entries' }
    elseif ($failed -eq 0) { Write-Host '     a ban file of two entries is read; one with a line that is no entry stops the server at its start, naming the line' }

    Write-Host "== The server of the win-x64 package, a proxy and four client processes: $Seconds s of game time"
    dotnet $tool net-match $ContentDir --map $map --humans 3 --ais 5 --watchers 1 --seconds $Seconds --delay 40 --jitter 10 --loss-percent 1 --server-package (Join-Path $packages 'win-x64') `
        --label package-win --out (Join-Path $OutDir 'match')
    if ($LASTEXITCODE -ne 0) { Fail 'the match on the server of the win-x64 package' }
}
else {
    Write-Host "== The server of the linux-x64 package in '$Distribution' with an empty environment; three client processes under Windows: $Seconds s of game time"
    # No proxy: it would be this repository's tool under the distribution's runtime, which this check must not need.
    dotnet $tool net-match $ContentDir --map $map --humans 3 --ais 5 --seconds $Seconds --no-proxy --server-package (Join-Path $packages 'linux-x64') --linux server --linux-distribution $Distribution `
        --label package-linux --out (Join-Path $OutDir 'match')
    if ($LASTEXITCODE -eq 3) { Write-Host "NOT RUN: the WSL distribution '$Distribution' does not answer - the Linux package was not started."; exit 3 }
    if ($LASTEXITCODE -ne 0) { Fail 'the match on the server of the linux-x64 package' }
    else {
        $listening = Get-Content (Join-Path $OutDir 'match/server.log') | Where-Object { $_ -match ' event=listening ' } | Select-Object -First 1
        if ($listening -notmatch ' os="?[^"]*Linux') { Fail "the server did not run under Linux ($listening)" }
        else { Write-Host '     the server ran under Linux from the package alone (no DOTNET_ROOT, no runtime of the user in its PATH)' }
    }
}

if ($failed -gt 0) { Write-Host "Server package ($Platform): $failed check(s) failed; logs in $OutDir"; exit 1 }
Write-Host "Server package ($Platform): built, its contents as they should be, and a match played on its server; logs in $OutDir"
exit 0
