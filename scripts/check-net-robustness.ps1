<#
.SYNOPSIS
    The robustness of a multiplayer match, with real processes on this machine (docs/NETWORK-PROTOCOL.md section 18, docs/NETWORK-DIAGNOSTICS.md).

.DESCRIPTION
    Runs CnCNew.NetTool net-match three times on an eight-player map, through the proxy with 40 ms delay, 10 ms jitter and 1 % loss:
      1. rejoin - the connection of player 2 dies without a word, three seconds later the same player is back with its token, takes the state of another member and
         plays on; a watcher comes when the match is already running; at the end every client has the same state, and the replay file the server wrote, played
         again, arrives at the hash the clients agreed on;
      2. watcher on a delay - somebody without a seat is sent the match five seconds late and plays the same states;
      3. desync - one client changes one object behind the back of the others; the server must report it at that tick and name the parts of the hash, every client
         must write its package, and desync-diff must name the tick, the object and the field from two of them.
    Nothing leaves the machine: every socket is on the loopback interface, with ports the operating system assigns. Logs and results go to a directory outside the
    repository.

.PARAMETER ContentDir
    The runtime content (default game/content).

.PARAMETER NoBuild
    Do not build the two tools first (the caller has).

.EXAMPLE
    pwsh scripts/check-net-robustness.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/net-robustness'),
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
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
if ($OutDir -match '\s') { Write-Host "The output directory must not contain a blank: $OutDir"; exit 2 }
$tool = 'tools/CnCNew.NetTool/bin/Release/net10.0/CnCNew.NetTool.dll'
$map = 'Maps/Twilight Flame/Twilight Flame.map'
$line = @('--delay', '40', '--jitter', '10', '--loss-percent', '1')
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }

# ---- 1. rejoin, a late watcher, the server's replay
$rejoin = Join-Path $OutDir 'rejoin'
$replays = Join-Path $rejoin 'replays'
Write-Host '== Rejoin: player 2 loses its connection at tick 1200 and is back three seconds later; a watcher comes eight seconds into the match'
dotnet $tool net-match $ContentDir --map $map --humans 3 --ais 5 --watchers 1 --late-watchers 1 --late-after-seconds 8 --seconds 60 @line `
    --disturb '1:rejoin:1200:3000' --server-args "--seat-hold-ms 60000 --allow-late-watchers --replay-dir $replays" --label rejoin --out $rejoin
if ($LASTEXITCODE -ne 0) { Fail 'the match with a rejoin' }
else {
    $row = Get-Content (Join-Path $rejoin 'row.json') -Raw | ConvertFrom-Json
    if ($row.notes -notmatch 'c2 lost its connection at tick \d+ and joined again at tick [1-9]') { Fail 'player 2 did not join the running match again' }
    if ($row.notes -notmatch 'c5 joined the running match at tick [1-9]') { Fail 'the late watcher did not join the running match' }
    $server = Get-Content (Join-Path $rejoin 'server.log')
    foreach ($event in 'seat-held', 'seat-returned', 'snapshot-relayed', 'joined-late', 'replay-written') {
        if (-not ($server | Where-Object { $_ -match " event=$event " })) { Fail "the server log has no $event" }
    }
    if ($server | Where-Object { $_ -match ' event=left ' -and $_ -cmatch 'phase=Ended' -and $_ -notmatch 'reason=Closed' }) { Fail 'a close after the end was logged as something else than a close' }
    $file = Get-ChildItem -Path $replays -Filter '*.replay' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $file) { Fail 'the server wrote no replay file' }
    else {
        Write-Host "== The replay file of the server, played again ($($file.Length) bytes)"
        dotnet $tool replay-check $ContentDir $file.FullName
        if ($LASTEXITCODE -ne 0) { Fail 'the replay file does not lead to the hash the clients agreed on' }
    }
}

# ---- 2. a watcher on a delay
$delayed = Join-Path $OutDir 'delayed'
Write-Host '== A watcher that is sent the match five seconds late'
dotnet $tool net-match $ContentDir --map $map --humans 2 --ais 6 --watchers 1 --seconds 30 @line --server-args '--spectator-delay-ms 5000' --label delayed --out $delayed
if ($LASTEXITCODE -ne 0) { Fail 'the match with a watcher on a delay' }

# ---- 3. a desync and its diagnosis
$desync = Join-Path $OutDir 'desync'
Write-Host '== Desync: client 2 changes one object at tick 900'
dotnet $tool net-match $ContentDir --map $map --humans 3 --ais 5 --seconds 40 @line --disturb '1:corrupt:900' --expect-desync --label desync --out $desync
if ($LASTEXITCODE -ne 0) { Fail 'the match that was meant to break' }
else {
    $changed = (Get-Content (Join-Path $desync 'client-2.json') -Raw | ConvertFrom-Json).corrupted
    if ($changed -notmatch '^object (\d+) .* at tick (\d+)$') { Fail "client 2 did not say what it changed ('$changed')" }
    else {
        $object = [int]$Matches[1]; $tick = [long]$Matches[2]
        $out = Join-Path $desync 'diff.json'
        dotnet $tool desync-diff $ContentDir (Join-Path $desync 'packages/client-1') (Join-Path $desync 'packages/client-2') --out $out
        if ($LASTEXITCODE -ne 0) { Fail 'desync-diff' }
        else {
            $diff = Get-Content $out -Raw | ConvertFrom-Json
            if ($diff.firstDifferingTick -ne $tick) { Fail "desync-diff names tick $($diff.firstDifferingTick), the change was at tick $tick" }
            if (@($diff.objects).Count -ne 1 -or $diff.objects[0] -ne $object) { Fail "desync-diff names the objects '$($diff.objects -join ', ')', the change was in object $object" }
            if ($diff.parts -notcontains 'world.objects') { Fail "desync-diff names the parts '$($diff.parts -join ', ')'" }
            if (@($diff.fieldsA).Count -ne 0) { Fail 'the client that is in step differs from the match played again' }
            if (-not (@($diff.fieldsB) | Where-Object { $_ -match "\[id=$object\]" -and $_ -match 'health' })) { Fail 'desync-diff does not name the field that was changed' }
        }
    }
}

if ($failed -gt 0) { Write-Host "Net robustness: $failed check(s) failed; logs in $OutDir"; exit 1 }
Write-Host "Net robustness: rejoin, late and delayed watchers, the server's replay and the desync diagnosis as expected; logs in $OutDir"
exit 0
