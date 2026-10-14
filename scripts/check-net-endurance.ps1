<#
.SYNOPSIS
    Everything of Phase 29 that has a time limit, for at least an hour of one server's uptime (docs/MULTIPLAYER-FINDINGS.md section 4): a game hosts, and a second
    one comes, waits in front of the rooms, enters, plays a match, returns into the room and leaves - again and again.

.DESCRIPTION
    Runs the suite NetShellTests of Game.Tests with --endurance-minutes: two games in one process, each a shell driven by a script through input events (the
    person), the first one hosting a server on a loopback port the operating system assigns (the dedicated server's core on a thread of its own), the sockets real.
    One cycle: the guest connects (the first time it is asked about the server's key), stands in front of the list of rooms for a few seconds - once for five and
    a half minutes, longer than a server lets a connection browse, so that the lobby has to come back by itself -, enters the room, accepts; the host starts; both
    build the match behind the shared load screen and play fifteen seconds of it; both return from the score screen into the room (the host first, so it opens the
    room again); the guest leaves the room and the server.

    It passes when no step of either script failed, the server was up for the whole time, every cycle was a match that ended by its rules at the same tick and in
    the same state on both, the server reported no desync, and the long wait in front of the rooms ended with the lobby being there again.

    What it does not show: a second machine, a real network, a window (the two games draw their draw lists and nothing else), and a rejoin into a running match
    (scripts/check-net-robustness.ps1 has that, with clients without a window against the dedicated server).

    A measurement of real time: run it on a quiet machine and do not build meanwhile.

.EXAMPLE
    pwsh scripts/check-net-endurance.ps1 -Minutes 65
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$Minutes = 65,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) { Write-Host "No runtime content at $ContentDir (scripts/dev-content.ps1)."; exit 2 }
if (-not $NoBuild) {
    dotnet build -c Release tests/CnCNew.Game.Tests -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building tests/CnCNew.Game.Tests failed.'; exit 1 }
}
$watch = [Diagnostics.Stopwatch]::StartNew()
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir $ContentDir --only NetShellTests --endurance-minutes $Minutes | Where-Object { $_ -match 'endurance|FAIL|PASS|passed|failed|shell-script' } | ForEach-Object { Write-Host $_ }
$code = $LASTEXITCODE
Write-Host ("== {0} after {1:F0} min" -f $(if ($code -eq 0) { 'ok' } else { 'FAILED' }), $watch.Elapsed.TotalMinutes)
exit $code
