<#
.SYNOPSIS
    What a person at a match of a server would feel, measured (docs/MULTIPLAYER-FINDINGS.md sections 2 and 3): from a click to the first frame that shows it and
    to the execution, and how evenly the picture moves - on a local match and on lines with 0, 20, 40 and 80 ms of delay in each direction.

.DESCRIPTION
    A measurement, not a check: it prints the table and writes it to <OutDir>/latency.md; whether the numbers meet the thresholds of
    docs/MULTIPLAYER-FINDINGS.md section 2 is read from the table. It fails (exit code 1) only when a line could not be measured or when the games of a line were
    not in the same state (threshold T5).

    Per line it starts on this machine
      - the dedicated server (once, for all lines) on a loopback port the operating system assigns,
      - CnCNew.NetTool proxy in front of it with the line's delay in each direction (no jitter, no loss),
      - the Release window client, driven through the menus by --shell-script: it connects to the proxy, says yes to the server's key, opens a room, picks the
        map, waits for the other player and starts; with --net-stats --net-order-every 120 its stand-in for a person clicks every two seconds
        (CnCNew.Game.Lockstep.NetStats),
      - CnCNew.NetTool client as the other player, through the same proxy: a client without a window, without pictures and without statistics.
    The line "local" is the window client alone: a skirmish against the computer through the server embedded in the game.

    Run it on a quiet machine with nothing else building or measuring; the window clients need a display (exit code 3 without one).

.EXAMPLE
    pwsh scripts/measure-net-latency.ps1 -Label before
#>
param(
    [string]$ContentDir = 'game/content',
    [int[]]$Delays = @(0, 20, 40, 80),
    [int]$MatchSeconds = 60,
    [string]$Label = 'run',
    [string]$Map = 'Alpine Assault',
    # The army of the window client on every line: the stand-in orders its builder about, and how a unit starts and stops is part of what is measured.
    [string]$Army = 'USA',
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-latency'),
    [string[]]$ClientArguments = @(),
    [switch]$NoBuild,
    [switch]$SkipLocal
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
$OutDir = Join-Path $OutDir $Label
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
function Write-Script([string]$name, [string[]]$lines) {
    $path = Join-Path $OutDir "$name.script.txt"
    [IO.File]::WriteAllLines($path, $lines)
    $path
}
function Client-Arguments([string]$name, [string]$script, [string[]]$extra) {
    @($clientDll.FullName, '--content-dir', (Quote $ContentDir), '--size', '1024x768', '--mute', '--no-transitions', '--net-dir', (Quote (Join-Path $OutDir "$name-net")),
        '--settings-dir', (Quote (Join-Path $OutDir "$name-settings")), '--shell-script', (Quote $script), '--shell-seconds', "$($MatchSeconds + 240)", '--net-stats', '--net-order-every', '120') + $extra + $ClientArguments
}

# One row of the table from the lines of a window client.
function Read-Stats([string]$name) {
    $log = Get-Content (Join-Path $OutDir "$name.log") -ErrorAction SilentlyContinue
    $row = [ordered]@{}
    foreach ($line in $log | Where-Object { $_ -cmatch '^net-stats: ' }) {
        if ($line -cmatch '^net-stats: clicks=(\d+) withFeedback=(\d+) executed=(\d+) withoutFeedback=(\d+) withoutOrder=(\d+) unresolved=(\d+) deviations=(\d+) rejected=(\d+)') {
            $row.clicks = [int]$Matches[1]; $row.withFeedback = [int]$Matches[2]; $row.executed = [int]$Matches[3]; $row.deviations = [int]$Matches[7]; $row.rejected = [int]$Matches[8]
        }
        elseif ($line -cmatch '^net-stats: (?<name>[a-z-]+): n=(?<n>\d+) min=(?<min>[\d.]+) p50=(?<p50>[\d.]+) p90=(?<p90>[\d.]+) p95=(?<p95>[\d.]+) p99=(?<p99>[\d.]+) max=(?<max>[\d.]+) mean=(?<mean>[\d.]+)') {
            $row[$Matches['name']] = [pscustomobject]@{ n = [int]$Matches['n']; p50 = [double]$Matches['p50']; p95 = [double]$Matches['p95']; p99 = [double]$Matches['p99']; max = [double]$Matches['max'] }
        }
        elseif ($line -cmatch '^net-stats: motion: .*movingUnitFrames=(\d+) jumps=(\d+) jumpShare=([\d.]+)% holds=(\d+) holdShare=([\d.]+)%') {
            $row.movingFrames = [int]$Matches[1]; $row.jumpShare = [double]$Matches[3]; $row.holdShare = [double]$Matches[5]
        }
        elseif ($line -cmatch '^net-stats: steps (?<name>[a-z-]+): changed=(?<changed>\d+) unchanged=(?<unchanged>\d+)(?: p50=(?<p50>[\d.]+) p99=(?<p99>[\d.]+) max=(?<max>[\d.]+))?') {
            $row["steps-$($Matches['name'])"] = [pscustomobject]@{ changed = [int]$Matches['changed']; unchanged = [int]$Matches['unchanged']; p50 = $Matches['p50']; p99 = $Matches['p99'] }
        }
    }
    $row
}

$rows = [ordered]@{}
$watch = [Diagnostics.Stopwatch]::StartNew()

if (-not $SkipLocal) {
    Write-Host "== local: a skirmish through the embedded server, $MatchSeconds s"
    $script = Write-Script 'local' @('wait screen MainMenuScreen', 'click MainMenu.wnd:ButtonSinglePlayer', 'click MainMenu.wnd:ButtonSkirmish', 'wait screen SkirmishSetupScreen',
        'click SkirmishGameOptionsMenu.wnd:ButtonSelectMap', 'wait screen SkirmishMapSelectScreen', "pick SkirmishMapSelectMenu.wnd:ListboxMap $Map", 'click SkirmishMapSelectMenu.wnd:ButtonOK',
        'wait screen SkirmishSetupScreen', "select SkirmishGameOptionsMenu.wnd:ComboBoxPlayerTemplate0 $Army", "select SkirmishGameOptionsMenu.wnd:ComboBoxPlayerTemplate1 $Army", 'click SkirmishGameOptionsMenu.wnd:ButtonStart', "limit $($MatchSeconds + 120)", 'wait phase InMatch', "wait match tick atleast $ticks")
    $local = Start-Logged 'local' (Client-Arguments 'local' $script @())
    if (-not $local.WaitForExit(($MatchSeconds + 300) * 1000)) { $local.Kill($true); Fail 'local: the client did not end in time' }
    elseif ($local.ExitCode -ne 0) { Fail "local: exit code $($local.ExitCode)" }
    $rows['local'] = Read-Stats 'local'
}

$server = Start-Logged 'server' @('tools/CnCNew.DedicatedServer/bin/Release/net10.0/CnCNew.DedicatedServer.dll', '--port', '0', '--address', '127.0.0.1', '--allow-modified-builds', '--status-seconds', '30',
    '--key-file', (Quote (Join-Path $OutDir 'server-key.pem')))
$running = @($server)
try {
    $listening = Wait-Line 'server' ' event=listening ' 40
    if (-not $listening -or $listening -notmatch ' port=(\d+)') { Write-Host 'FAIL: the server did not start.'; exit 1 }
    $serverPort = $Matches[1]
    if ($listening -notmatch ' key=([0-9A-F]{64})') { Write-Host 'FAIL: the server did not print the fingerprint of its key.'; exit 1 }
    $serverKey = $Matches[1]
    foreach ($delay in $Delays) {
        $line = "net$delay"
        $room = "latency-$delay"
        $proxy = Start-Logged "$line-proxy" @('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'proxy', '--listen', '0', '--server', "127.0.0.1:$serverPort", '--delay', "$delay", '--jitter', '0', '--loss-percent', '0')
        $running += $proxy
        $proxyLine = Wait-Line "$line-proxy" ' event=listening ' 30
        if (-not $proxyLine -or $proxyLine -notmatch ' port=(\d+)') { Fail "${line}: the proxy did not start"; continue }
        $proxyPort = $Matches[1]
        Write-Host "== ${line}: $delay ms each way through 127.0.0.1:$proxyPort, $MatchSeconds s"
        $script = Write-Script $line @(
            'wait screen MainMenuScreen', 'click MainMenu.wnd:ButtonMultiplayer', 'click MainMenu.wnd:ButtonNetwork', 'wait screen NetworkDirectConnectScreen',
            'type NetworkDirectConnect.wnd:EditPlayerName Alice', "type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:$proxyPort", 'click NetworkDirectConnect.wnd:ButtonJoin',
            'limit 30', 'wait screen MessageBoxScreen', 'expect text MessageBox.wnd:StaticTextTitle equals New server', 'click MessageBox.wnd:ButtonYes', 'wait screen NetLobbyScreen',
            'wait enabled LanLobbyMenu.wnd:ButtonHost', 'click LanLobbyMenu.wnd:ButtonHost', 'wait screen NetHostPopupScreen', "type PopupHostGame.wnd:TextEntryGameName $room",
            'click PopupHostGame.wnd:ButtonCreateGame', 'wait screen NetRoomScreen', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice',
            'click LanGameOptionsMenu.wnd:ButtonSelectMap', 'wait screen SkirmishMapSelectScreen', "pick LanMapSelectMenu.wnd:ListboxMap $Map", 'click LanMapSelectMenu.wnd:ButtonOK',
            'wait screen NetRoomScreen', "wait text LanGameOptionsMenu.wnd:TextEntryMapDisplay contains $Map", 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice',
            "select LanGameOptionsMenu.wnd:ComboBoxPlayerTemplate0 $Army", 'wait seconds 1', "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayerTemplate0 shows $Army", 'note room-ready',
            'limit 240', 'wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob', 'wait shown LanGameOptionsMenu.wnd:ButtonAccept1', 'click LanGameOptionsMenu.wnd:ButtonStart',
            "limit $($MatchSeconds + 120)", 'wait screen NetLoadingScreen', 'wait phase InMatch', 'wait phase Results', 'wait screen ScoreScreenScreen')
        $window = Start-Logged $line (Client-Arguments $line $script @('--net-match-seconds', "$MatchSeconds"))
        $running += $window
        # The room must be the window client's, with its map set, before the other player knocks: whoever names a room first is its host, and the client without a
        # window says "ready" once.
        if (-not (Wait-Line $line 'shell-script: note: room-ready' 180)) { Fail "${line}: the window client did not open its room"; $window.Kill($true); $proxy.Kill($true); continue }
        $headless = Start-Logged "$line-headless" @('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $ContentDir), '--server', "127.0.0.1:$proxyPort", '--room', (Quote $room), '--name', 'Bob',
            '--seat', '1', '--humans', '2', '--ais', '0', '--members', '2', '--map', (Quote "Maps/$Map/$Map.map"), '--ticks', "$ticks", '--result', (Quote (Join-Path $OutDir "$line-headless.json")), '--server-key', $serverKey)
        $running += $headless
        if (-not $window.WaitForExit(($MatchSeconds + 420) * 1000)) { $window.Kill($true); Fail "${line}: the window client did not end in time" }
        elseif ($window.ExitCode -ne 0) { Fail "${line}: the window client ended with exit code $($window.ExitCode) ($(Get-Content (Join-Path $OutDir "$line.log") | Where-Object { $_ -cmatch 'FAILED' } | Select-Object -First 1))" }
        if (-not $headless.WaitForExit(120000)) { $headless.Kill($true); Fail "${line}: the client without a window did not end in time" }
        $proxy.Kill($true)
        $row = Read-Stats $line
        # T5: the same match on both, and the server agreed on every tick.
        $match = Get-Content (Join-Path $OutDir "$line.log") | Where-Object { $_ -cmatch 'net-match: end tick' } | Select-Object -Last 1
        $result = if (Test-Path (Join-Path $OutDir "$line-headless.json")) { Get-Content (Join-Path $OutDir "$line-headless.json") -Raw | ConvertFrom-Json } else { $null }
        $closed = Get-Content (Join-Path $OutDir 'server.log') | Where-Object { $_ -match " event=room-closed room=$room " } | Select-Object -Last 1
        if (-not $match -or $match -cnotmatch 'end tick (\d+) hash ([0-9A-F]{16}) chain ([0-9A-F]{16}) .* note (.*)$') { Fail "${line}: the window client reported no match" }
        else {
            $row.tick = [int]$Matches[1]; $row.chain = $Matches[3]; $row.note = $Matches[4]
            if ($row.tick -ne $ticks -or $row.note -ne 'none') { Fail "${line}: the match did not end by its rules at tick $ticks (tick $($row.tick), note $($row.note))" }
            if (-not $result) { Fail "${line}: the client without a window wrote no result" }
            elseif ($result.hashChain -ne $row.chain) { Fail "${line}: the hash chains of the two games differ ($($row.chain) / $($result.hashChain))" }
            else { $row.sameState = $true }
        }
        if (Get-Content (Join-Path $OutDir 'server.log') | Where-Object { $_ -match " event=desync room=$room " }) { Fail "${line}: the server reported a desync" }
        if (-not $closed -or $closed -notmatch 'lastAgreedTick=(-?\d+)' -or [long]$Matches[1] -lt $ticks - 2) { Fail "${line}: the server did not agree on every tick ($closed)" }
        else { $row.agreed = [long]$Matches[1] }
        $rows[$line] = $row
    }
}
finally {
    foreach ($process in $running) { if ($process -and -not $process.HasExited) { $process.Kill($true) } }
}

# ---- The table.
function Cell($value, [string]$field, [string]$format = 'F1') { if ($null -eq $value) { '-' } else { ([double]$value.$field).ToString($format, [Globalization.CultureInfo]::InvariantCulture) } }
$lines = [Collections.Generic.List[string]]::new()
$lines.Add("| Line | Clicks (answered / executed) | Click to first changed frame, ms: median / 95 % / 99 % / max | Click to execution, ms: median / 99 % | Order delay in game time, ms: median | Frame time, ms: median / 99 % | Jumps | Holds | Same state |")
$lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
foreach ($name in $rows.Keys) {
    $r = $rows[$name]
    $f = $r['click-to-first-changed-frame']; $e = $r['click-to-execution']; $d = $r['order-delay-in-game-time']; $t = $r['frame-time']
    $same = if ($name -eq 'local') { 'one game' } elseif ($r.sameState -and $null -ne $r.agreed) { "yes (server agreed to tick $($r.agreed))" } else { 'NO' }
    $lines.Add(("| {0} | {1} ({2} / {3}) | {4} / {5} / {6} / {7} | {8} / {9} | {10} | {11} / {12} | {13} % | {14} % | {15} |" -f $name, $r.clicks, $r.withFeedback, $r.executed,
        (Cell $f 'p50'), (Cell $f 'p95'), (Cell $f 'p99'), (Cell $f 'max'), (Cell $e 'p50'), (Cell $e 'p99'), (Cell $d 'p50'), (Cell $t 'p50'), (Cell $t 'p99'), ([double]$r.jumpShare).ToString('F2', [Globalization.CultureInfo]::InvariantCulture), ([double]$r.holdShare).ToString('F2', [Globalization.CultureInfo]::InvariantCulture), $same))
}
$table = Join-Path $OutDir 'latency.md'
[IO.File]::WriteAllLines($table, $lines)
Write-Host ''
$lines | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host ("== {0} after {1:F0} s; table in {2}" -f $(if ($failed -eq 0) { 'measured' } else { "$failed problem(s)" }), $watch.Elapsed.TotalSeconds, $table)
exit ([Math]::Min($failed, 1))
