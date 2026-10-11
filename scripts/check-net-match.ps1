<#
.SYNOPSIS
    A multiplayer match of real processes on this machine: the dedicated server, the UDP proxy and one client process per member (docs/NETWORK-PROTOCOL.md section 15).

.DESCRIPTION
    Runs CnCNew.NetTool net-match twice on an eight-player map:
      1. three players, five computer players and one watcher through the proxy with 40 ms delay, 10 ms jitter and 1 % loss - every client must play the same states
         in every tick (equal hash chains), and the server must agree on every tick;
      2. the same with a client process that vanishes without a word - the others must play on in step, and the server must drop it by its rule.
    Nothing leaves the machine: every socket is on the loopback interface, with ports the operating system assigns. Logs and results go to a directory outside the
    repository.

.PARAMETER ContentDir
    The runtime content (default game/content).

.PARAMETER Seconds
    Game time of each match in seconds (default 45).

.PARAMETER NoBuild
    Do not build the two tools first (the caller has).

.EXAMPLE
    pwsh scripts/check-net-match.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$Seconds = 45,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-match'),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this check needs it (scripts/dev-content.ps1)."
    exit 2
}
if (-not $NoBuild) {
    foreach ($project in 'tools/CnCNew.DedicatedServer', 'tools/CnCNew.NetTool') {
        dotnet build -c Release $project -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "Building $project failed."; exit 1 }
    }
}
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
$tool = 'tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll'
$map = 'Maps/Twilight Flame/Twilight Flame.map'
$common = @('net-match', $ContentDir, '--map', $map, '--humans', '3', '--ais', '5', '--watchers', '1', '--seconds', "$Seconds", '--delay', '40', '--jitter', '10', '--loss-percent', '1')
$failed = 0

Write-Host "== Three players, five computer players and a watcher through the proxy (40 ms, 10 ms jitter, 1 % loss), $Seconds s of game time"
dotnet $tool @common --label steady --out (Join-Path $OutDir 'steady')
if ($LASTEXITCODE -ne 0) { $failed++ }

# The third player's process vanishes after a third of the match; with short limits the rule runs its whole course inside the match.
$vanishAt = [int]($Seconds * 60 / 3)
Write-Host "== The same, and the process of player 3 vanishes at tick $vanishAt"
dotnet $tool @common --label vanish --out (Join-Path $OutDir 'vanish') --disturb "2:vanish:$vanishAt" --server-args '--timeout-ms 4000 --lag-throttle-ms 1500 --throttle-limit-ms 5000'
if ($LASTEXITCODE -ne 0) { $failed++ }
$server = Get-Content (Join-Path $OutDir 'vanish/server.log')
$left = $server | Where-Object { $_ -match ' event=left ' -and $_ -cmatch 'phase=Match' -and $_ -match 'player=3' }
if (-not $left) { Write-Host 'FAIL: the server did not drop the vanished player during the match.'; $failed++ }
else { Write-Host "     the server dropped it: $($left | Select-Object -First 1)" }

if ($failed -gt 0) { Write-Host "Net match: $failed check(s) failed; logs in $OutDir"; exit 1 }
Write-Host "Net match: both matches in step on every client; logs in $OutDir"
exit 0
