<#
.SYNOPSIS
    A multiplayer match across the boundary of two operating systems on this machine: the dedicated server and some clients under Linux (WSL), the others under Windows
    (docs/DETERMINISM.md, docs/NETWORK-PROTOCOL.md section 18.7).

.DESCRIPTION
    Runs CnCNew.NetTool net-match twice with --linux: the server, its proxy and the clients 2 and 4 run in the WSL distribution with the same binaries (built here),
    the clients 1 and 3 run under Windows, and they meet over the address of the WSL interface.
      1. three players, five computer players and a watcher - every client must play the same states in every tick (equal hash chains);
      2. the same, and the connection of the Linux client 2 dies and comes back: it takes over the state a Windows client wrote, and all end in the same state.
    Every process says itself which system it ran on, and the check fails when one ran elsewhere than asked.

    What this proves: the simulation, the state format and the protocol give the same result under Windows and Linux with their two JIT targets, on this CPU.
    What it does not prove: equality between two different CPUs - that needs a second machine.

    Needs a WSL distribution with the .NET runtime of this build (default distribution 'Debian', runtime in ~/.dotnet or on the PATH). Without it the check does
    not run and says so: exit code 3 - which is neither passed nor failed.

.PARAMETER Distribution
    The WSL distribution (default Debian).

.EXAMPLE
    pwsh scripts/check-net-cross-os.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [string]$Distribution = 'Debian',
    [int]$Seconds = 45,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-cross-os'),
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
$common = @('net-match', $ContentDir, '--map', $map, '--humans', '3', '--ais', '5', '--watchers', '1', '--seconds', "$Seconds", '--delay', '40', '--jitter', '10', '--loss-percent', '1',
    '--linux', 'server,2,4', '--linux-distribution', $Distribution)
$failed = 0

Write-Host "== Server and clients 2 and 4 under Linux ($Distribution), clients 1 and 3 under Windows, $Seconds s of game time"
dotnet $tool @common --label cross-os --out (Join-Path $OutDir 'steady')
if ($LASTEXITCODE -eq 3) { Write-Host "NOT RUN: no Linux environment with the .NET runtime in WSL distribution '$Distribution' - this check did not run and proves nothing."; exit 3 }
if ($LASTEXITCODE -ne 0) { $failed++ }

Write-Host '== The same, and the Linux client 2 loses its connection and takes over the state of a Windows client'
$rejoinAt = [int]($Seconds * 60 / 3)
dotnet $tool @common --label cross-os-rejoin --out (Join-Path $OutDir 'rejoin') --disturb "1:rejoin:${rejoinAt}:3000" --server-args '--seat-hold-ms 60000'
if ($LASTEXITCODE -ne 0) { $failed++ }
else {
    $row = Get-Content (Join-Path $OutDir 'rejoin/row.json') -Raw | ConvertFrom-Json
    if ($row.notes -notmatch 'c2 lost its connection at tick \d+ and joined again at tick [1-9]') { Write-Host 'FAIL: client 2 did not join the running match again.'; $failed++ }
    if ($row.notes -notmatch 'c1 handed over') { Write-Host 'FAIL: the state did not come from the Windows client 1.'; $failed++ }
}

if ($failed -gt 0) { Write-Host "Net cross-OS: $failed check(s) failed; logs in $OutDir"; exit 1 }
Write-Host "Net cross-OS: Windows and Linux processes played the same states in every tick (same CPU; two systems, two JIT targets); logs in $OutDir"
exit 0
