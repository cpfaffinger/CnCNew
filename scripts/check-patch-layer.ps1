<#
.SYNOPSIS
    The content is the patched one (docs/PATCH-LAYER.md): its definitions compile against the compatibility baseline, a match on it is deterministic, and on a server
    two clients of it play together while a client with the game without the patch is turned away by its definitions.

.DESCRIPTION
    Needs the patched content (game/content) and the game without the patch (game/original/content, made by scripts/dev-content.ps1; without either the check is
    NOT RUN, exit code 3). Runs:
      - `IniTool compat-check game/content baselines/compatibility.json`: no new, missing or changed compatibility entry;
      - `SimTool ai-match` twice (Alpine Assault, FactionGLA against FactionChina, 5 game minutes): the same final hash both times;
      - the dedicated server on a loopback port, a client without a window as host of a room (two human seats, two computer players), a client of the game without
        the patch that asks for the second seat - it must be refused for its definitions and never play - and then a client of the patched content that takes
        the seat; the server must agree on every tick of both clients and report no desync.
    Logs go to a directory outside the repository. Exit code 0 when everything holds, 1 when something does not, 3 when a content is missing.

.EXAMPLE
    pwsh scripts/check-patch-layer.ps1
#>
param(
    [string]$PatchContent = 'game/content',
    [string]$ContentDir = 'game/original/content',
    [string]$Map = 'Maps/Alpine Assault/Alpine Assault.map',
    [string]$NetMap = 'Maps/Twilight Flame/Twilight Flame.map',
    [int]$Seconds = 30,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/patch-layer'),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not (Test-Path (Join-Path $PatchContent 'content-manifest.json'))) {
    Write-Host "NOT RUN: no patched content at $PatchContent (scripts/dev-content.ps1)."
    exit 3
}
if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) { Write-Host "NOT RUN: no game without the patch at $ContentDir (scripts/dev-content.ps1)."; exit 3 }
if (-not $NoBuild) {
    foreach ($project in 'tools/CnCNew.IniTool', 'tools/CnCNew.SimTool', 'tools/CnCNew.DedicatedServer', 'tools/CnCNew.NetTool') {
        dotnet build -c Release $project -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "Building $project failed."; exit 1 }
    }
}
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }
function Pass([string]$text) { Write-Host "ok    $text" }
function Quote([string]$text) { if ($text -match '\s') { '"' + $text + '"' } else { $text } }
function Start-Logged([string]$name, [string[]]$arguments) {
    Start-Process -FilePath 'dotnet' -ArgumentList $arguments -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $OutDir "$name.log") -RedirectStandardError (Join-Path $OutDir "$name.err")
}
function Wait-Line([string]$name, [string]$pattern, [int]$seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        $line = Get-Content (Join-Path $OutDir "$name.log") -ErrorAction SilentlyContinue | Where-Object { $_ -match $pattern } | Select-Object -First 1
        if ($line) { return $line }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

# ---- the definitions
& dotnet tools/CnCNew.IniTool/bin/Release/net10.0/CnCNew.IniTool.dll compat-check $PatchContent baselines/compatibility.json *> (Join-Path $OutDir 'compat.log')
if ($LASTEXITCODE -ne 0) { Fail "the layer's definitions deviate from baselines/compatibility.json ($(Get-Content (Join-Path $OutDir 'compat.log') | Select-Object -Last 1))" }
else { Pass "the layer's definitions compile and match their baseline ($(Get-Content (Join-Path $OutDir 'compat.log') | Select-Object -Last 1))" }

# ---- a deterministic match
$hashes = foreach ($run in 1, 2) {
    & dotnet tools/CnCNew.SimTool/bin/Release/net10.0/CnCNew.SimTool.dll ai-match $PatchContent --map $Map --factions FactionGLA,FactionChina --minutes 5 *> (Join-Path $OutDir "ai-match-$run.log")
    $line = Get-Content (Join-Path $OutDir "ai-match-$run.log") | Where-Object { $_ -match 'final hash ([0-9A-F]{16})' } | Select-Object -First 1
    if ($line -match 'final hash ([0-9A-F]{16})') { $Matches[1] } else { 'none' }
}
if ($hashes[0] -eq 'none' -or $hashes[0] -ne $hashes[1]) { Fail "an AI match on the layer is not deterministic ($($hashes -join ' / '))" }
else { Pass "an AI match on the layer ends with the same final hash twice ($($hashes[0]))" }

# ---- a server: two clients of the layer play, one of the original content is refused
$ticks = $Seconds * 60
$server = Start-Logged 'server' @('tools/CnCNew.DedicatedServer/bin/Release/net10.0/CnCNew.DedicatedServer.dll', '--port', '0', '--address', '127.0.0.1', '--allow-modified-builds', '--exit-after-rooms', '1', '--status-seconds', '10')
$host_ = $null; $original = $null; $guest = $null
try {
    $listening = Wait-Line 'server' ' event=listening ' 40
    if (-not $listening -or $listening -notmatch ' port=(\d+)') { Write-Host 'FAIL: the server did not start.'; exit 1 }
    $port = $Matches[1]
    $common = @('--server', "127.0.0.1:$port", '--room', 'patch', '--humans', '2', '--ais', '2', '--members', '2', '--map', (Quote $NetMap), '--ticks', "$ticks")
    $host_ = Start-Logged 'host' (@('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $PatchContent), '--name', 'host', '--seat', '0', '--result', (Quote (Join-Path $OutDir 'host.json'))) + $common)
    if (-not (Wait-Line 'server' ' event=joined .*name=host' 180)) { Fail 'the host of the layer did not reach the server' }
    $original = Start-Logged 'original' (@('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $ContentDir), '--name', 'original', '--seat', '1', '--result', (Quote (Join-Path $OutDir 'original.json'))) + $common)
    if (-not $original.WaitForExit(180000)) { Fail 'the client of the original content did not end' }
    $guest = Start-Logged 'guest' (@('tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll', 'client', (Quote $PatchContent), '--name', 'guest', '--seat', '1', '--result', (Quote (Join-Path $OutDir 'guest.json'))) + $common)
    if (-not $guest.WaitForExit(600000)) { Fail 'the guest of the layer did not end in time' }
    if (-not $host_.WaitForExit(120000)) { Fail 'the host of the layer did not end in time' }
    $server.WaitForExit(20000) | Out-Null
}
finally {
    foreach ($process in $guest, $original, $host_, $server) { if ($process -and -not $process.HasExited) { $process.Kill($true) } }
}
$log = Get-Content (Join-Path $OutDir 'server.log')
$originalText = (Get-Content (Join-Path $OutDir 'original.log'), (Join-Path $OutDir 'original.err'), (Join-Path $OutDir 'original.json') -ErrorAction SilentlyContinue) -join "`n"
if ($original.ExitCode -eq 0) { Fail 'the client of the original content played in a room of the layer' }
elseif ($originalText -notmatch 'Definitions' -and -not ($log | Where-Object { $_ -match ' event=reject.*Definitions' })) { Fail "the client of the original content was not refused for its definitions ($($originalText -split "`n" | Select-Object -Last 2))" }
else { Pass 'the client of the original content was refused for its definitions' }
$closed = $log | Where-Object { $_ -match ' event=room-closed ' } | Select-Object -Last 1
if ($log | Where-Object { $_ -match ' event=desync ' }) { Fail 'the server reported a desync between the two clients of the layer' }
elseif (-not $closed -or $closed -notmatch 'lastAgreedTick=(-?\d+)' -or [long]$Matches[1] -lt $ticks - 1) { Fail "the server did not agree on every tick of the two clients of the layer ($closed)" }
else { Pass "two clients of the layer played $Seconds s on the server, every tick agreed up to $($Matches[1])" }

if ($failed -gt 0) { Write-Host "check-patch-layer: $failed problem(s); logs in $OutDir"; exit 1 }
Write-Host "check-patch-layer: ok (logs in $OutDir)"
exit 0
