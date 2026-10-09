<#
.SYNOPSIS
  Check of the spectator mode without a window: the same AI-against-AI match as scripts/demo-spectator.ps1 as a smoke test, with the spectator's keys pressed.

.DESCRIPTION
  Starts the Release client with --spectate --smoke-test --frames N and the input script scripts/input/spectator.txt (panels, follow AI camera 1/2, overview,
  fog switch, pause and speed, plus clicks and arrow keys that must not order anything) and checks the output of the client:
    - the match ran (ticks, commands of both AIs), no command of the spectator was recorded;
    - the panels had data (money, units, structures, collected supplies), all four panels, all fog views and all speeds were on;
    - the simulated AI cameras found points of interest;
    - the camera reached the overview height far above the limit of a player; the frame costs near and far are printed.
  It runs two passes: the simulation's own AI (builtin) and the original skirmish AI with its script library (original, docs/SKIRMISH-AI-ORIGINAL.md). The
  second pass also checks that no script type without implementation was hit. -Ai builtin or -Ai original runs one pass only.
  Every 300 frames a PNG is written; afterwards they lie, named, under -OutDir\<ai> (default: %TEMP%\cncnew-spectator-check, outside the repository).
  The pictures show that something is drawn - whether it looks right and is readable only a human can judge (checklist in docs/SPECTATOR-MODE.md).
  Exit code 0 when every check of every pass holds, otherwise 1.

.EXAMPLE
  pwsh scripts/check-spectator.ps1
  pwsh scripts/check-spectator.ps1 -NoBuild -Seed 11 -TickRate 60
  pwsh scripts/check-spectator.ps1 -Ai original -DifficultyA hard -DifficultyB easy
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [string]$OutDir,
    [string]$Map = 'Maps/Alpine Assault/Alpine Assault.map',
    [string]$FactionA = 'FactionAmerica',
    [string]$FactionB = 'FactionChina',
    # Which AI plays both armies; both passes when not given.
    [ValidateSet('builtin', 'original')]
    [string]$Ai,
    # The army of each side in the pass of the original AI (easy, medium, hard).
    [ValidateSet('easy', 'medium', 'hard')]
    [string]$DifficultyA = 'medium',
    [ValidateSet('easy', 'medium', 'hard')]
    [string]$DifficultyB = 'medium',
    [int]$Seed = 7,
    # Simulation rate (30, 60 or 120 Hz); 0 = the client's default. The frame numbers of the input script are frames of the original (1/30 s) at every rate.
    [int]$TickRate = 0,
    [string]$Size = '1280x720',
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 1500
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (name it with -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-spectator-check' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Building the client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}
$exe = Get-ChildItem (Join-Path $repo 'src\CnCNew.Client.Stride\bin\Release') -Recurse -Filter 'CnCNew.Client.Stride.exe' | Select-Object -First 1
if (-not $exe) { throw 'Release client not found; start without -NoBuild.' }

# What the picture after every 300 frames shows (scripts/input/spectator.txt sets it up in the frames before).
$shots = [ordered]@{
    300  = '01-panel-production'
    600  = '02-panel-units'
    900  = '03-panel-losses'
    1200 = '04-panel-economy'
    1500 = '05-overview-whole-map'
    1800 = '06-overview-fog-player-1'
    2100 = '07-follows-ai-camera-1'
    2400 = '08-follows-ai-camera-2'
    2700 = '09-jump-to-ai-1-panel-units'
    3000 = '10-panels-off-speed-1'
}
$frames = 3010

function Invoke-SpectatorPass([string]$PassAi) {
    $passDir = Join-Path $OutDir $PassAi
    if (Test-Path -LiteralPath $passDir) { Get-ChildItem -LiteralPath $passDir -File | Remove-Item -Force }
    New-Item -ItemType Directory -Force -Path $passDir | Out-Null
    $film = Join-Path $passDir 'film'
    $out = Join-Path $passDir 'client.txt'
    $all = @('--smoke-test', '--content-dir', $ContentDir, '--size', $Size, '--match', $Map, '--faction', $FactionA, '--opponent', $FactionB, '--spectate', '--seed', "$Seed",
        '--spectator-speed', '3', '--frames', "$frames", '--mute', '--input-script', (Join-Path $repo 'scripts\input\spectator.txt'), '--filmstrip', $film, '--every', '300')
    if ($PassAi -eq 'original') { $all += @('--ai', 'original', '--difficulty', "$DifficultyA,$DifficultyB") }
    if ($TickRate -gt 0) { $all += @('--tick-rate', "$TickRate") }
    $quoted = $all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $who = if ($PassAi -eq 'original') { "original AI ($DifficultyA against $DifficultyB)" } else { 'built-in AI' }
    Write-Host "== Starting the spectator smoke test, ${who}: seed $Seed, $Map, $FactionA against $FactionB, $frames frames ..." -ForegroundColor Cyan
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $exe.FullName -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    $problems = [Collections.Generic.List[string]]::new()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        $problems.Add("time limit of $TimeoutSeconds s exceeded")
    }
    elseif ($process.ExitCode -ne 0) { $problems.Add("exit code $($process.ExitCode)") }
    Write-Host "   Run time $([int]$watch.Elapsed.TotalSeconds) s, output: $out"
    $lines = if (Test-Path $out) { Get-Content $out } else { @() }

    function Line([string]$Prefix) { $lines | Where-Object { $_.StartsWith($Prefix) } | Select-Object -Last 1 }
    function Number([string]$Text, [string]$Key) {
        if ($Text -and $Text -cmatch "\b$([regex]::Escape($Key))=(-?\d+)") { return [long]$Matches[1] }
        $problems.Add("no value '$Key' in: $Text")
        return 0
    }
    function Expect([bool]$Condition, [string]$What) { if (-not $Condition) { $problems.Add($What) } }

    $summary = Line 'input-script summary:'
    $match = Line 'spectator: map'
    $commands = Line 'spectator commands:'
    $camera = Line 'spectator camera:'
    $view = Line 'spectator view:'
    $aiLine = Line 'spectator ai:'
    $far = Line 'spectator frame cost far:'
    $near = Line 'spectator frame cost near:'
    foreach ($needed in @{ 'input-script summary' = $summary; 'spectator' = $match; 'spectator commands' = $commands; 'spectator camera' = $camera; 'spectator view' = $view; 'spectator ai' = $aiLine; 'spectator frame cost far' = $far }.GetEnumerator()) {
        if (-not $needed.Value) { $problems.Add("no line '$($needed.Key)' in the output") }
    }

    if ($problems.Count -eq 0) {
        # The match ran, both AIs gave orders, nothing of the spectator reached the simulation.
        # 100 game seconds are 3000 frames of the original; the tick count of the same game time depends on the simulation rate.
        Expect ((Number $match 'gameSeconds') -ge 100) 'the match did not get 100 game seconds (3000 frames of the original) far'
        Expect ((Number $commands 'player1') -gt 0 -and (Number $commands 'player2') -gt 0) 'not both AIs gave orders'
        Expect ((Number $commands 'viewerOrdersRecorded') -eq 0) 'an order of the spectator was recorded'
        Expect ((Number $summary 'unresolved') -eq 0) 'the input script had unresolved targets'
        Expect ((Number $summary 'commandsAccepted') -eq 0 -and (Number $summary 'commandsRejected') -eq 0) 'a command was submitted through the input of the spectator'
        Expect ((Number $summary 'selectionChanges') -eq 0) 'the spectator selected something'

        # Who played, and - for the original AI - that every script type it met is implemented.
        if ($PassAi -eq 'original') {
            Expect ($aiLine -clike "*player1=original/$DifficultyA*" -and $aiLine -clike "*player2=original/$DifficultyB*") "the slots are not played by the original AI as asked: $aiLine"
            Expect ((Number $aiLine 'missingScriptTypeHits') -eq 0) "script types without implementation were hit: $aiLine"
        }
        else {
            Expect ($aiLine -clike '*player1=builtin*' -and $aiLine -clike '*player2=builtin*') "the slots are not played by the built-in AI: $aiLine"
        }

        # The camera: overview far above the limit of a player.
        $playerMax = Number $camera 'playerMaxHeight'
        $overview = Number $camera 'overviewHeight'
        $reached = Number $camera 'highestReached'
        Expect ($overview -gt 2 * $playerMax) "the overview height ($overview) is not far above the limit of a player ($playerMax)"
        Expect ($reached -ge $overview - 1) "the camera did not reach the overview height ($reached of $overview)"
        Expect ((Number $far 'frames') -ge 300) 'fewer than 300 frames in the far zoom'

        # The simulated AI cameras.
        foreach ($index in 1, 2) {
            $ai = Line "spectator ai-camera ${index}:"
            if (-not $ai) { $problems.Add("no line for AI camera $index"); continue }
            Expect ((Number $ai 'orders') -gt 0 -and (Number $ai 'interests') -ge 3) "AI camera $index found no points of interest: $ai"
        }

        # The view: all panels, all fog views, all speeds.
        foreach ($word in 'Production', 'Units', 'Losses', 'Economy', 'All', 'Player 1', 'Player 2', '1x', '2x', '4x', '8x', 'paused') {
            Expect ($view -clike "*$word*") "the view lacks '$word': $view"
        }
        Expect ((Number $view 'panelRebuilds') -gt 100) 'the panels were hardly rebuilt'

        # The panels had data.
        $panelLines = @($lines | Where-Object { $_.StartsWith('spectator panel:') })
        $players = @($panelLines | Where-Object { $_ -cmatch 'spectator panel: player \d' })
        Expect ($players.Count -eq 2) "panel lines for $($players.Count) instead of 2 players"
        $collected = 0; $structuresBuilt = 0
        foreach ($player in $players) { $collected += Number $player 'collected' }
        foreach ($detail in @($panelLines | Where-Object { $_ -clike '*structuresBuilt=*' })) { $structuresBuilt += Number $detail 'structuresBuilt' }
        Expect ($collected -gt 0) 'nobody collected supplies'
        Expect ($structuresBuilt -gt 0) 'nobody finished a structure'
    }

    # The pictures.
    $captures = @()
    foreach ($shot in $shots.GetEnumerator()) {
        $source = Join-Path $film ('frame-{0:D5}.png' -f [int]$shot.Key)
        $target = Join-Path $passDir "$($shot.Value).png"
        if (-not (Test-Path -LiteralPath $source)) { $problems.Add("picture $($shot.Key) is missing ($source)"); continue }
        Move-Item -LiteralPath $source -Destination $target -Force
        if ((Get-Item -LiteralPath $target).Length -lt 20000) { $problems.Add("picture $($shot.Value) is conspicuously small") }
        $captures += $target
    }

    Write-Host ''
    $lines | Where-Object { $_.StartsWith('spectator') } | ForEach-Object { Write-Host "   $_" }
    Write-Host ''
    Write-Host "   Frame cost near: $near"
    Write-Host "   Frame cost far : $far"
    Write-Host "   Pictures ($($captures.Count)) under $passDir"
    if ($problems.Count -eq 0) {
        Write-Host "PASS check-spectator ($PassAi)" -ForegroundColor Green
        return $true
    }
    Write-Host "FAIL check-spectator ($PassAi): $($problems -join '; ')" -ForegroundColor Red
    return $false
}

$passes = if ($Ai) { @($Ai) } else { @('builtin', 'original') }
$failed = 0
foreach ($pass in $passes) {
    if (-not (Invoke-SpectatorPass $pass)) { $failed++ }
    Write-Host ''
}
if ($failed -eq 0) {
    Write-Host "PASS check-spectator: $($passes -join ', ')" -ForegroundColor Green
    exit 0
}
Write-Host "FAIL check-spectator: $failed of $($passes.Count) passes failed" -ForegroundColor Red
exit 1
