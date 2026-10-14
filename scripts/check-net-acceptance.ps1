<#
.SYNOPSIS
    The acceptance lines of Phase 29 that need several players, as one scenario with real processes (docs/MULTIPLAYER-FINDINGS.md section 4): two window clients,
    three clients without a window, a watcher and two computer players on a dedicated server, through a line with delay, jitter and loss - with a lost connection
    that comes back, a client that stalls, one that vanishes, and a pause.

.DESCRIPTION
    Starts on this machine
      - the dedicated server on a loopback port the operating system assigns, holding the seat of a lost connection for 20 s,
      - CnCNew.NetTool proxy in front of it: 40 ms each way, 10 ms of jitter, 1 % loss,
      - window client Alice, driven through the menus by --shell-script: connects, says yes to the server's key, opens the room, picks an eight-player map, makes
        two seats computer armies, waits for the others, starts,
      - window client Bob, through the menus: finds the room in the list, enters, accepts,
      - CnCNew.NetTool client three times as the players Carol, Dave and Erin (clients without a window, played by a command generator) and once as a watcher:
          Carol  loses her connection at tick 1500 and comes back fourteen seconds later with her token (the server notices the loss after ten and holds her seat),
          Dave   stands still for a second and a half at tick 2700 (a slow machine),
          Erin   vanishes at tick 3600 and never comes back (her seat is given up when the hold is over),
      - and at tick 5400 Alice's stand-in asks for a pause and ends it two seconds later.
    Both window clients print what their in-match indicators show (--net-stats: a line per change, and every notice).

    and checks
      - every script ran through, and both window clients stood on the score screen at the end;
      - one match on everybody who played to the end: Alice, Bob, Dave and the watcher have the same hash chain over every tick, and the server agreed on every
        tick and reported no desync;
      - the indicators of both window clients said what happened, by name: Carol's seat held and Carol back; Dave behind; Erin's seat held and then given up;
        the pause with who asked for it (on Bob's screen: "Paused by Alice");
      - both window clients gave orders that went through the server.
    What this does not show: what a human sees and how it feels; a real network; a second machine; a window client that comes back to a running match (the
    menus cannot do that - docs/MULTIPLAYER-FINDINGS.md section 5).

    The window clients need a display; without one the check ends with exit code 3 (not run).

.EXAMPLE
    pwsh scripts/check-net-acceptance.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$MatchSeconds = 120,
    [string]$Map = 'Twilight Flame',
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-acceptance'),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) { Write-Host "No runtime content at $ContentDir (scripts/dev-content.ps1)."; exit 2 }
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.Screen]::AllScreens.Count -eq 0) { Write-Host 'NOT RUN: this machine has no display; the window client cannot open its window.'; exit 3 }
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
$ticks = $MatchSeconds * 60
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }
function Quote([string]$text) { if ($text -match '\s') { '"' + $text + '"' } else { $text } }
function Start-Logged([string]$name, [string[]]$arguments) {
    Start-Process -FilePath 'dotnet' -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $OutDir "$name.log") -RedirectStandardError (Join-Path $OutDir "$name.err")
}
function Log([string]$name) { $path = Join-Path $OutDir "$name.log"; if (Test-Path $path) { Get-Content $path } else { @() } }
function Wait-Line([string]$name, [string]$pattern, [int]$seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        $line = Log $name | Where-Object { $_ -match $pattern } | Select-Object -First 1
        if ($line) { return $line }
        Start-Sleep -Milliseconds 250
    }
    return $null
}
function Write-Script([string]$name, [string[]]$lines) {
    $path = Join-Path $OutDir "$name.script.txt"
    [IO.File]::WriteAllLines($path, $lines)
    $path
}
function Window([string]$name, [string]$script, [string[]]$extra) {
    Start-Logged $name (@($clientDll.FullName, '--content-dir', (Quote $ContentDir), '--size', '1024x768', '--mute', '--no-transitions', '--net-dir', (Quote (Join-Path $OutDir "$name-net")),
        '--settings-dir', (Quote (Join-Path $OutDir "$name-settings")), '--shell-script', (Quote $script), '--shell-seconds', "$($MatchSeconds + 420)", '--net-stats', '--net-stats-trace', '--net-order-every', '120') + $extra)
}

$running = @()
$watch = [Diagnostics.Stopwatch]::StartNew()
try {
    $server = Start-Logged 'server' @('tools/CnCNew.DedicatedServer/bin/Release/net10.0/CnCNew.DedicatedServer.dll', '--port', '0', '--address', '127.0.0.1', '--allow-modified-builds', '--status-seconds', '30',
        '--seat-hold-ms', '20000', '--key-file', (Quote (Join-Path $OutDir 'server-key.pem')))
    $running += $server
    $listening = Wait-Line 'server' ' event=listening ' 40
    if (-not $listening -or $listening -notmatch ' port=(\d+)') { Write-Host 'FAIL: the server did not start.'; exit 1 }
    $serverPort = $Matches[1]
    if ($listening -notmatch ' key=([0-9A-F]{64})') { Write-Host 'FAIL: the server did not print the fingerprint of its key.'; exit 1 }
    $serverKey = $Matches[1]
    $proxy = Start-Logged 'proxy' @('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'proxy', '--listen', '0', '--server', "127.0.0.1:$serverPort", '--delay', '40', '--jitter', '10', '--loss-percent', '1')
    $running += $proxy
    $proxyLine = Wait-Line 'proxy' ' event=listening ' 30
    if (-not $proxyLine -or $proxyLine -notmatch ' port=(\d+)') { Write-Host 'FAIL: the proxy did not start.'; exit 1 }
    $proxyPort = $Matches[1]
    Write-Host "== A dedicated server behind 40 ms, 10 ms of jitter and 1 % loss (127.0.0.1:$proxyPort); a match of $MatchSeconds s on $Map (logs in $OutDir)"

    $toLobby = @('wait screen MainMenuScreen', 'click MainMenu.wnd:ButtonMultiplayer', 'click MainMenu.wnd:ButtonNetwork', 'wait screen NetworkDirectConnectScreen',
        "type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:$proxyPort")
    $trust = @('click NetworkDirectConnect.wnd:ButtonJoin', 'limit 30', 'wait screen MessageBoxScreen', 'expect text MessageBox.wnd:StaticTextTitle equals New server', 'click MessageBox.wnd:ButtonYes',
        'wait screen NetLobbyScreen')
    $play = @("limit $($MatchSeconds + 240)", 'wait screen NetLoadingScreen', 'wait phase InMatch', 'wait phase Results', 'wait screen ScoreScreenScreen')
    $alice = Write-Script 'alice' ($toLobby + @('type NetworkDirectConnect.wnd:EditPlayerName Alice') + $trust + @(
        'wait enabled LanLobbyMenu.wnd:ButtonHost', 'click LanLobbyMenu.wnd:ButtonHost', 'wait screen NetHostPopupScreen', 'type PopupHostGame.wnd:TextEntryGameName acceptance',
        'click PopupHostGame.wnd:ButtonCreateGame', 'wait screen NetRoomScreen', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice',
        'click LanGameOptionsMenu.wnd:ButtonSelectMap', 'wait screen SkirmishMapSelectScreen', "pick LanMapSelectMenu.wnd:ListboxMap $Map", 'click LanMapSelectMenu.wnd:ButtonOK',
        'wait screen NetRoomScreen', "wait text LanGameOptionsMenu.wnd:TextEntryMapDisplay contains $Map", 'wait shown LanGameOptionsMenu.wnd:ComboBoxPlayer7',
        'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice',
        # Two computer armies in the sixth and seventh seat; the eighth stays open and is closed by the start.
        'select LanGameOptionsMenu.wnd:ComboBoxPlayer5 Medium Army', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer5 shows Medium Army',
        'select LanGameOptionsMenu.wnd:ComboBoxPlayer6 Medium Army', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer6 shows Medium Army',
        'wait seconds 1', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice', 'note room-ready',
        'limit 300', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer2 shows Carol',
        'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer3 shows Dave', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer4 shows Erin',
        'wait shown LanGameOptionsMenu.wnd:ButtonAccept1', 'wait shown LanGameOptionsMenu.wnd:ButtonAccept2', 'wait shown LanGameOptionsMenu.wnd:ButtonAccept3', 'wait shown LanGameOptionsMenu.wnd:ButtonAccept4',
        'note everybody-ready', 'wait seconds 3', 'click LanGameOptionsMenu.wnd:ButtonStart') + $play)
    $bob = Write-Script 'bob' ($toLobby + @('type NetworkDirectConnect.wnd:EditPlayerName Bob') + $trust + @(
        'pick LanLobbyMenu.wnd:ListboxGames acceptance', "wait text LanLobbyMenu.wnd:StaticTextGameInfo contains $Map", 'click LanLobbyMenu.wnd:ButtonJoin', 'wait screen NetRoomScreen',
        'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob', 'expect text LanGameOptionsMenu.wnd:ButtonStart equals ACCEPT', 'click LanGameOptionsMenu.wnd:ButtonStart',
        'wait shown LanGameOptionsMenu.wnd:ButtonAccept1') + $play)

    $aliceProcess = Window 'alice' $alice @('--net-match-seconds', "$MatchSeconds", '--net-pause-at-tick', '5400')
    $running += $aliceProcess
    if (-not (Wait-Line 'alice' 'shell-script: note: room-ready' 240)) { Fail 'Alice did not set up her room'; throw 'stop' }
    $bobProcess = Window 'bob' $bob @()
    $running += $bobProcess
    # Bob takes the first open seat (the second); the clients without a window take the third to the fifth, in this order, when Bob sits.
    if (-not (Wait-Line 'server' ' event=joined room=acceptance .*name=Bob' 240)) { Fail 'Bob did not reach the room'; throw 'stop' }
    Start-Sleep -Seconds 4
    function Headless([string]$name, [int]$seat, [string[]]$extra) {
        Start-Logged $name (@('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $ContentDir), '--server', "127.0.0.1:$proxyPort", '--room', 'acceptance', '--name', $name,
            '--seat', "$seat", '--humans', '5', '--ais', '3', '--members', '5', '--map', (Quote "Maps/$Map/$Map.map"), '--ticks', "$ticks", '--result', (Quote (Join-Path $OutDir "$name.json")),
            '--server-key', $serverKey) + $extra)
    }
    $carol = Headless 'Carol' 2 @('--rejoin-at-tick', '1500', '--rejoin-after-ms', '14000')
    $dave = Headless 'Dave' 3 @('--hitch-at-tick', '2700', '--hitch-ms', '1500')
    $erin = Headless 'Erin' 4 @('--vanish-at-tick', '3600')
    $watcher = Headless 'Watcher' -1 @('--watch')
    $running += $carol, $dave, $erin, $watcher

    foreach ($pair in @(@('alice', $aliceProcess), @('bob', $bobProcess))) {
        if (-not $pair[1].WaitForExit(($MatchSeconds + 600) * 1000)) { $pair[1].Kill($true); Fail "$($pair[0]): the window client did not end in time" }
        elseif ($pair[1].ExitCode -ne 0) { Fail "$($pair[0]): exit code $($pair[1].ExitCode) ($(Log $pair[0] | Where-Object { $_ -cmatch 'FAILED' } | Select-Object -First 1))" }
    }
    foreach ($pair in @(@('Dave', $dave), @('Watcher', $watcher), @('Carol', $carol), @('Erin', $erin))) {
        if (-not $pair[1].WaitForExit(90000)) { $pair[1].Kill($true); if ($pair[0] -ne 'Erin') { Fail "$($pair[0]): the client without a window did not end in time" } }
    }
}
catch { if ($_.Exception.Message -ne 'stop') { throw } }
finally {
    foreach ($process in $running) { if ($process -and -not $process.HasExited) { $process.Kill($true) } }
}

# ---- Every script ran through.
foreach ($name in 'alice', 'bob') {
    $summary = Log $name | Where-Object { $_ -cmatch '^shell-script summary:' } | Select-Object -Last 1
    if ($summary -cnotmatch 'failed=0 finished=1') { Fail "${name}: $summary" } else { Write-Host "   ${name}: $summary" }
}

# ---- One match on everybody who played to the end.
$chains = [ordered]@{}
foreach ($name in 'alice', 'bob') {
    $line = Log $name | Where-Object { $_ -cmatch 'net-match: end tick' } | Select-Object -Last 1
    if (-not $line -or $line -cnotmatch 'end tick (\d+) hash [0-9A-F]{16} chain ([0-9A-F]{16}) player (\d+) .* orders (\d+) intents (\d+) stalls (\d+) note (.*)$') { Fail "${name}: no match reported"; continue }
    Write-Host "   ${name}: $($line -replace '^.*net-match: ', '')"
    if ([int]$Matches[1] -ne $ticks -or $Matches[7] -ne 'none') { Fail "${name}: the match did not end by its rules at tick $ticks (tick $($Matches[1]), note $($Matches[7]))" }
    if ([int]$Matches[4] -lt 10 -or [int]$Matches[5] -lt 10) { Fail "${name}: fewer than ten orders went to the server ($($Matches[4]) given, $($Matches[5]) sent)" }
    $chains[$name] = $Matches[2]
}
foreach ($name in 'Dave', 'Watcher') {
    $file = Join-Path $OutDir "$name.json"
    if (-not (Test-Path $file)) { Fail "${name}: no result"; continue }
    $result = Get-Content $file -Raw | ConvertFrom-Json
    if (-not $result.ok) { Fail "${name}: $($result.problem)" }
    $chains[$name] = $result.hashChain
}
if (($chains.Values | Select-Object -Unique).Count -ne 1 -or $chains.Count -ne 4) { Fail "the hash chains differ or are missing: $(($chains.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')" }
else { Write-Host "   one hash chain over every tick on Alice, Bob, Dave and the watcher: $($chains['alice'])" }
$serverLog = Log 'server'
if ($serverLog | Where-Object { $_ -match ' event=desync ' }) { Fail 'the server reported a desync' }
$closed = $serverLog | Where-Object { $_ -match ' event=room-closed room=acceptance ' } | Select-Object -Last 1
$ended = $serverLog | Where-Object { $_ -match ' event=match-ended room=acceptance ' } | Select-Object -Last 1
if (-not $ended -or $ended -notmatch 'lastAgreedTick=(-?\d+)' -or [long]$Matches[1] -lt $ticks - 3) { Fail "the server did not agree on every tick ($ended)" }
else { Write-Host "   the server agreed on the session hash of every tick up to tick $($Matches[1])" }

# ---- What the indicators said, by name, on both window clients.
function Said([string]$name, [string]$pattern, [string]$what) {
    $line = Log $name | Where-Object { $_ -cmatch '^net-(notice|status): ' -and $_ -cmatch $pattern } | Select-Object -First 1
    if (-not $line) { Fail "${name}: the indicators never said: $what" } else { Write-Host "   ${name}: $($line.Substring(0, [Math]::Min(150, $line.Length)))" }
}
foreach ($name in 'alice', 'bob') {
    Said $name 'net-status: .*Carol lost the connection - the seat is held for' "Carol's seat is held"
    Said $name 'net-notice: Carol is back\.' 'Carol is back'
    Said $name 'net-status: .*Dave is [0-9.]+ s behind' 'Dave is behind'
    Said $name 'net-status: .*Carol is [0-9.]+ s behind.*half speed' 'the game is slowed while Carol does not answer'
    Said $name 'net-status: .*Erin lost the connection - the seat is held for' "Erin's seat is held"
    Said $name 'net-notice: Erin did not come back in time\.' 'Erin did not come back'
    Said $name 'net-status: Ping \d+ ms, buffer \d+ ms' 'the connection line'
}
Said 'bob' 'net-status: .*banner Paused by Alice' 'the pause, with who asked'
Said 'alice' 'net-status: .*banner Paused by you' 'her own pause'

Write-Host ("== {0} after {1:F0} s" -f $(if ($failed -eq 0) { 'ok' } else { "$failed problem(s)" }), $watch.Elapsed.TotalSeconds)
exit ([Math]::Min($failed, 1))
