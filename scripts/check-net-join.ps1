<#
.SYNOPSIS
    The window client joins a match on a dedicated server (docs/NETWORK-PROTOCOL.md section 18.6): a client without a window is the other player, and the server
    compares what the two play.

.DESCRIPTION
    Starts on this machine
      - the dedicated server on a loopback port the operating system assigns,
      - CnCNew.NetTool client as the host of a room: player 1 of a match of two players and two computer players, played by a command generator,
      - the Release window client with --smoke-test --join: it enters the room, takes the second seat, builds the match the server describes, plays it at the
        server's pace and orders one of its own units to move every five seconds, through the command sink the HUD uses.
    and checks
      - the server agreed on the session hash of every tick up to the last one of the check - the server compares the hash reports of both clients tick by tick,
        so this is the comparison of the window client's world with the headless one's - and reported no desync;
      - the window client's own orders went to the server, came back in bundles and were executed;
      - the client without a window played to the end without a problem; the window client left with a goodbye (the server logs a leave, not a lost connection);
      - the server's key (docs/NETWORK-PROTOCOL.md section 20.2): the client without a window is told the key the server printed and takes no other; a third
        client that is told another key refuses the server and never says hello; the window client meets the server for the first time and remembers its key
        in a file of this run (never the user's own file).
    What this does not show: a lobby screen (the client is told the room on its command line), and what a human sees.

    The window client needs a display (as scripts/check-spectator.ps1 does). Logs and results go to a directory outside the repository.

.EXAMPLE
    pwsh scripts/check-net-join.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$Seconds = 40,
    [string]$Map = 'Maps/Twilight Flame/Twilight Flame.map',
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-join'),
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this check needs it (scripts/dev-content.ps1)."
    exit 2
}
if (-not $NoBuild) {
    foreach ($project in 'tools/CnCNew.DedicatedServer', 'tools/CnCNew.NetTool', 'src/CnCNew.Client.Stride') {
        dotnet build -c Release $project -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "Building $project failed."; exit 1 }
    }
}
$clientDll = Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.dll' | Select-Object -First 1
if (-not $clientDll) { Write-Host 'Release client not found; start without -NoBuild.'; exit 1 }
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$ticks = $Seconds * 60
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }
function Start-Logged([string]$name, [string[]]$arguments) {
    Start-Process -FilePath 'dotnet' -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $OutDir "$name.log") -RedirectStandardError (Join-Path $OutDir "$name.err")
}
function Wait-Line([string]$name, [string]$pattern, [int]$seconds) {
    $file = Join-Path $OutDir "$name.log"
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        $line = Get-Content $file -ErrorAction SilentlyContinue | Where-Object { $_ -match $pattern } | Select-Object -First 1
        if ($line) { return $line }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Quote([string]$text) { if ($text -match '\s') { '"' + $text + '"' } else { $text } }

$knownFile = Join-Path $OutDir 'known-servers.json'
Remove-Item $knownFile, (Join-Path $OutDir 'server-key.pem') -ErrorAction SilentlyContinue
$server = Start-Logged 'server' @('tools/CnCNew.DedicatedServer/bin/Release/net10.0/CnCNew.DedicatedServer.dll', '--port', '0', '--address', '127.0.0.1', '--allow-modified-builds', '--exit-after-rooms', '1', '--status-seconds', '10',
    '--key-file', (Quote (Join-Path $OutDir 'server-key.pem')))
$headless = $null
$window = $null
$stranger = $null
try {
    $listening = Wait-Line 'server' ' event=listening ' 40
    if (-not $listening -or $listening -notmatch ' port=(\d+)') { Write-Host 'FAIL: the server did not start.'; exit 1 }
    $port = $Matches[1]
    if ($listening -notmatch ' key=([0-9A-F]{64})') { Write-Host 'FAIL: the server did not print the fingerprint of its key.'; exit 1 }
    $serverKey = $Matches[1]
    Write-Host "== The server listens on 127.0.0.1:$port; a client without a window opens the room 'join' and is its host"
    $headless = Start-Logged 'headless' @('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $ContentDir), '--server', "127.0.0.1:$port", '--room', 'join', '--name', 'headless',
        '--seat', '0', '--humans', '2', '--ais', '2', '--members', '2', '--map', (Quote $Map), '--ticks', "$ticks", '--result', (Quote (Join-Path $OutDir 'headless.json')), '--server-key', $serverKey)
    # Somebody who was told another key: it must refuse the server before it says a word.
    $stranger = Start-Logged 'stranger' @('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $ContentDir), '--server', "127.0.0.1:$port", '--room', 'join', '--name', 'stranger',
        '--seat', '1', '--humans', '2', '--ais', '2', '--members', '2', '--map', (Quote $Map), '--ticks', "$ticks", '--result', (Quote (Join-Path $OutDir 'stranger.json')), '--server-key', ('AB' * 32))
    # The room must be the headless client's before the window client knocks: whoever names a room first is its host.
    if (-not (Wait-Line 'server' ' event=joined .*name=headless' 180)) { Write-Host 'FAIL: the client without a window did not reach the server.'; exit 1 }
    Write-Host "== The window client joins, takes the second seat and plays $Seconds s of game time"
    $window = Start-Logged 'window' @($clientDll.FullName, '--smoke-test', '--content-dir', (Quote $ContentDir), '--join', "127.0.0.1:$port", '--room', 'join', '--seat', '2', '--player-name', 'window',
        '--join-until-tick', "$ticks", '--join-order-every', '300', '--join-result', (Quote (Join-Path $OutDir 'window.json')), '--mute', '--known-servers', (Quote $knownFile))
    if (-not $window.WaitForExit($TimeoutSeconds * 1000)) { Fail 'the window client did not end in time' }
    if (-not $headless.WaitForExit(120000)) { Fail 'the client without a window did not end in time' }
    $server.WaitForExit(20000) | Out-Null
    if (-not $stranger.WaitForExit(20000)) { Fail 'the client that was told another key did not end' }
}
finally {
    foreach ($process in $window, $headless, $stranger, $server) { if ($process -and -not $process.HasExited) { $process.Kill($true) } }
}

$log = Get-Content (Join-Path $OutDir 'server.log')
$closed = $log | Where-Object { $_ -match ' event=room-closed ' } | Select-Object -Last 1
if ($log | Where-Object { $_ -match ' event=desync ' }) { Fail 'the server reported a desync between the window client and the client without a window' }
if (-not $closed -or $closed -notmatch 'lastAgreedTick=(-?\d+)') { Fail 'the server did not close the room' }
elseif ([long]$Matches[1] -lt $ticks - 1) { Fail "the server agreed on the hashes of both clients only up to tick $($Matches[1]) of $($ticks - 1)" }
else { Write-Host "     the server compared the session hash of both clients in every tick up to tick $($Matches[1]): equal" }
if (-not ($log | Where-Object { $_ -match ' event=left .*reason=Left' -or $_ -match ' event=left .*reason=Closed' })) { Fail 'the window client did not leave with a goodbye' }

$strangerText = (Get-Content (Join-Path $OutDir 'stranger.log'), (Join-Path $OutDir 'stranger.err'), (Join-Path $OutDir 'stranger.json') -ErrorAction SilentlyContinue) -join "`n"
if ($stranger.HasExited -and $stranger.ExitCode -eq 0) { Fail 'a client that was told another key played on this server' }
elseif ($strangerText -notmatch 'not the one this client was told to expect') { Fail "a client that was told another key did not say why it refused the server ($($strangerText | Select-Object -Last 1))" }
elseif ($log | Where-Object { $_ -match ' event=joined .*name=stranger' }) { Fail 'a client that was told another key said hello to the server' }
else { Write-Host "     a client that was told another key refused the server (key $($serverKey.Substring(0, 16))...) and said nothing to it" }
if (-not (Test-Path $knownFile) -or (Get-Content $knownFile -Raw) -notmatch $serverKey) { Fail 'the window client did not remember the key of the server it met for the first time' }
else { Write-Host '     the window client remembered the key of the server it met for the first time' }

$windowFile = Join-Path $OutDir 'window.json'
if (-not (Test-Path $windowFile)) { Fail "the window client wrote no result ($(Get-Content (Join-Path $OutDir 'window.err') -ErrorAction SilentlyContinue | Select-Object -Last 3))" }
else {
    $result = Get-Content $windowFile -Raw | ConvertFrom-Json
    Write-Host "     window client: player $($result.player), left at tick $($result.tick) after $($result.frames) frames, $($result.intentsSent) order(s) sent, $($result.ordersResolved) executed, $($result.stalls) wait(s)"
    if ($result.tick -lt $ticks) { Fail "the window client played $($result.tick) of $ticks ticks" }
    if ($result.player -ne 2) { Fail "the window client was player $($result.player), not 2" }
    if ($result.intentsSent -lt 1 -or $result.ordersResolved -lt 1) { Fail 'no order of the window client went through the server and was executed' }
    if ($result.desyncTick -ge 0) { Fail "the window client was told of a desync at tick $($result.desyncTick)" }
    if ($result.discarded -gt 0) { Fail 'the window client discarded messages' }
}
$headlessFile = Join-Path $OutDir 'headless.json'
if (-not (Test-Path $headlessFile)) { Fail 'the client without a window wrote no result' }
else {
    $other = Get-Content $headlessFile -Raw | ConvertFrom-Json
    if (-not $other.ok) { Fail "the client without a window: $($other.problem)" }
}

if ($failed -gt 0) { Write-Host "Net join: $failed check(s) failed; logs in $OutDir"; exit 1 }
Write-Host "Net join: the window client played a match on a dedicated server in step with a client without a window; logs in $OutDir"
exit 0
