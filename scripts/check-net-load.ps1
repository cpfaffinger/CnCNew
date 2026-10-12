<#
.SYNOPSIS
    Many rooms at once on one dedicated server, with members that vanish, leave and come back (docs/DEDICATED-SERVER.md section 11).

.DESCRIPTION
    Starts the dedicated server on a loopback port the operating system assigns and CnCNew.NetTool load against it: rooms of eight members that
    speak the whole protocol through the sealed transport and simulate nothing (the relay does not simulate either). Every room plays short
    matches one after another; in every match one player vanishes, says it leaves, or vanishes and comes back with its token and is handed a state.
    Checks
      - every match that began ended as finished: no room stuck, no desync, nobody refused, no connection lost that was not meant to be;
      - the players that came back were handed a state and played on;
      - when the load is over the server holds no room and no connection, and it closed as many rooms as it opened;
      - the server counted no forged and no malformed datagram, and its working set did not grow beyond a bound.
    Needs no game data. What it does not show: anything about a client (the members report a constant hash), and a network.

.EXAMPLE
    pwsh scripts/check-net-load.ps1
#>
param(
    [int]$Rooms = 8,
    [int]$Seconds = 60,
    [int]$MatchSeconds = 15,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-load'),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $NoBuild) {
    foreach ($project in 'tools/CnCNew.DedicatedServer', 'tools/CnCNew.NetTool') {
        dotnet build -c Release $project -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "Building $project failed."; exit 1 }
    }
}
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }

$serverLog = Join-Path $OutDir 'server.log'
# All members come from one address: the limits per address are raised for that, every other limit is the default. The connection of a member that
# vanished stays with the server until its timeout, so there is room for twice the members.
$members = $Rooms * 8
$server = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru -RedirectStandardOutput $serverLog -RedirectStandardError (Join-Path $OutDir 'server.err') -ArgumentList @(
    'tools/CnCNew.DedicatedServer/bin/Release/net10.0/CnCNew.DedicatedServer.dll', '--port', '0', '--address', '127.0.0.1', '--allow-modified-builds', '--status-seconds', '5',
    '--seat-hold-ms', '30000', '--max-connections-per-address', "$($members * 2)", '--set', "maxConnections=$($members * 2)", '--set', "maxRooms=$($Rooms + 4)",
    '--set', 'handshakesPerAddressPerSecond=1000', '--set', 'handshakeBurst=2000', '--run-seconds', "$($Seconds + $MatchSeconds * 3 + 150)")
try {
    $listening = $null
    $until = (Get-Date).AddSeconds(40)
    while (-not $listening -and (Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 200
        $listening = Get-Content $serverLog -ErrorAction SilentlyContinue | Where-Object { $_ -match ' event=listening ' } | Select-Object -First 1
    }
    if (-not $listening -or $listening -notmatch ' port=(\d+)') { Write-Host 'FAIL: the server did not start.'; exit 1 }
    $port = $Matches[1]
    $null = $listening -match ' key=([0-9A-F]{64})'
    $key = $Matches[1]
    Write-Host "== $Rooms rooms of eight on 127.0.0.1:$port for $Seconds s: matches of $MatchSeconds s, one player of every match vanishes, leaves or comes back"
    dotnet tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll load --server "127.0.0.1:$port" --server-key $key --rooms $Rooms --seconds $Seconds --match-seconds $MatchSeconds `
        --churn-percent 100 --out (Join-Path $OutDir 'load.json') | ForEach-Object { Write-Host "     $_" }
    if ($LASTEXITCODE -ne 0) { Fail 'the load generator reports a problem' }
    # The seats of those who vanished for good are held for 30 s and their connections time out after 10 s: then the server must be empty.
    $empty = $null
    $until = (Get-Date).AddSeconds(60)
    while (-not $empty -and (Get-Date) -lt $until) {
        Start-Sleep -Seconds 1
        $empty = Get-Content $serverLog | Where-Object { $_ -match ' event=status rooms=0 connections=0 ' } | Select-Object -Last 1
    }
}
finally {
    if ($server -and -not $server.HasExited) { $server.Kill($true) }
}

$result = Get-Content (Join-Path $OutDir 'load.json') -Raw | ConvertFrom-Json
if ($result.MatchesFinished -lt $Rooms) { Fail "only $($result.MatchesFinished) match(es) finished" }
if ($result.MatchesFinished + $result.MatchesAbandoned -ne $result.MatchesStarted) { Fail "$($result.MatchesStarted) matches began, $($result.MatchesFinished) finished and $($result.MatchesAbandoned) were abandoned at the end" }
# A member that comes back when its match is just ending finds none: nine of ten must make it.
if ($result.Rejoins -lt 1 -or $result.RejoinsBack * 10 -lt $result.Rejoins * 9) { Fail "$($result.RejoinsBack) of $($result.Rejoins) members that came back played on" }
if ($result.Vanished -lt 1) { Fail 'no member vanished' }
if ($result.Discards -gt 0) { Fail "the members discarded $($result.Discards) message(s)" }
$log = Get-Content $serverLog
if (-not $empty) { Fail 'the server still holds a room or a connection a minute after the load' }
else {
    $value = @{}
    foreach ($pair in ($empty -split ' ')) { $parts = $pair -split '=', 2; if ($parts.Count -eq 2) { $value[$parts[0]] = $parts[1] } }
    Write-Host "     server after the load: rooms opened $($value.roomsOpened), closed $($value.roomsClosed); working set $([int]($value.workingSetKb / 1024)) MB, handles $($value.handles), CPU $([int]($value.cpuMs / 1000)) s; forged $($value.forged), malformed $($value.malformed), rate-limited $($value.rateLimited)"
    if ($value.roomsOpened -ne $value.roomsClosed) { Fail "the server opened $($value.roomsOpened) rooms and closed $($value.roomsClosed)" }
    if ([long]$value.forged -gt 0 -or [long]$value.malformed -gt 0) { Fail 'the server counted forged or malformed datagrams from honest members' }
    if ([long]$value.workingSetKb -gt 600000) { Fail "the server's working set is $([int]($value.workingSetKb / 1024)) MB" }
}
if ($log | Where-Object { $_ -match ' event=desync ' }) { Fail 'the server reported a desync' }

if ($failed -gt 0) { Write-Host "Net load: $failed check(s) failed; logs in $OutDir"; exit 1 }
Write-Host "Net load: $($result.MatchesFinished) matches in $Rooms rooms at once, $($result.Vanished) vanished, $($result.Left) left, $($result.RejoinsBack) came back; the server ended empty; logs in $OutDir"
exit 0
