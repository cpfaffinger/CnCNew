<#
.SYNOPSIS
  Spectator demo: a skirmish between two computer armies in a window, nobody plays (docs/SPECTATOR-MODE.md).

.DESCRIPTION
  Builds the Stride client (Release) and starts a skirmish in which both sides are played by the computer. The spectator sees the whole map without fog
  (switchable to the view of one player), may zoom far out, can follow the simulated camera of an AI and sees observer panels (production, units,
  losses, economy). The match ends with the usual victory decision; the winner is then shown at the top of the screen.

  Which AI plays: by default the simulation's own AI (-Ai builtin). With -Ai original both armies are played by the original skirmish AI with its script
  library (docs/SKIRMISH-AI-ORIGINAL.md); -DifficultyA and -DifficultyB then choose the army of each side (easy, medium, hard). Giving a difficulty
  without -Ai selects the original AI.

  The outcome depends on the seed. Without -Seed the script draws a new one at every start (Get-Random, the random source of the system - never the
  simulation) and prints it; with -Seed N exactly the same match runs again (the same seed as `SimTool ai-match --seed N`). Without -Map/-FactionA/-FactionB
  the script picks one of the pairings whose outcome was open in the survey over 20 seeds of the built-in AI (see $pairings below).

.EXAMPLE
  pwsh scripts/demo-spectator.ps1
  pwsh scripts/demo-spectator.ps1 -Seed 7 -Map "Alpine Assault" -FactionA FactionAmerica -FactionB FactionChina
  pwsh scripts/demo-spectator.ps1 -Ai original -DifficultyA hard -DifficultyB easy
  pwsh scripts/demo-spectator.ps1 -Speed 2 -Size 1920x1080 -MaxZoom 2000
  pwsh scripts/demo-spectator.ps1 -TickRate 60
#>
[CmdletBinding()]
param(
    # Map: virtual path or an unambiguous part of it ("Alpine Assault").
    [string]$Map,
    # The two armies (faction names such as FactionAmerica, FactionChina, FactionGLA, FactionChinaTankGeneral, FactionAmericaAirForceGeneral).
    [string]$FactionA,
    [string]$FactionB,
    # Who plays both armies: the simulation's own AI (builtin) or the original skirmish AI with its scripts (original).
    [ValidateSet('builtin', 'original')]
    [string]$Ai,
    # The army of each side when the original AI plays (easy, medium, hard); medium when not given.
    [ValidateSet('easy', 'medium', 'hard')]
    [string]$DifficultyA,
    [ValidateSet('easy', 'medium', 'hard')]
    [string]$DifficultyB,
    # Seed of the match (1 .. 2147483647); a freshly drawn one when not given.
    [int]$Seed,
    # Simulation rate 30, 60 or 120; the client's default when not given.
    [ValidateSet(30, 60, 120)]
    [int]$TickRate,
    # Highest camera height of the spectator above ground; when not given, the height from which the whole map is on screen.
    [double]$MaxZoom,
    # Game speed at the start (1 = real time; changeable in the game with 1/2/4/8 and the space bar).
    [double]$Speed = 1,
    [string]$Size = '1280x720',
    [string]$ContentDir,
    [switch]$Mute,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) {
    throw "Content directory not found: $ContentDir`nThe checked runtime content lies locally under game\content (not in the repository; scripts\dev-content.ps1 creates it from your own Steam installation); otherwise name it with -ContentDir."
}
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path

# -DifficultyA/-DifficultyB without -Ai mean the original AI: the built-in one has no levels.
if (-not $Ai) { $Ai = if ($DifficultyA -or $DifficultyB) { 'original' } else { 'builtin' } }
if ($Ai -eq 'builtin' -and ($DifficultyA -or $DifficultyB)) { throw '-DifficultyA/-DifficultyB belong to the original AI (-Ai original); the built-in AI has one strength.' }
if (-not $DifficultyA) { $DifficultyA = 'medium' }
if (-not $DifficultyB) { $DifficultyB = 'medium' }

# Pairings with an open outcome: wins of the first / of the second faction / undecided over 20 seeds (survey over 320 matches of the built-in AI, docs/SPECTATOR-MODE.md section 8).
$pairings = @(
    @{ Map = 'Maps/Armored Fury/Armored Fury.map';     A = 'FactionChina';            B = 'FactionGLA';                    Survey = '13 / 4 / 3' },
    @{ Map = 'Maps/Bitter Winter/Bitter Winter.map';   A = 'FactionChina';            B = 'FactionGLA';                    Survey = '6 / 14 / 0' },
    @{ Map = 'Maps/Armored Fury/Armored Fury.map';     A = 'FactionAmerica';          B = 'FactionChina';                  Survey = '8 / 3 / 9' },
    @{ Map = 'Maps/Alpine Assault/Alpine Assault.map'; A = 'FactionAmerica';          B = 'FactionChina';                  Survey = '12 / 1 / 7' },
    @{ Map = 'Maps/Death Valley/Death Valley.map';     A = 'FactionChinaTankGeneral'; B = 'FactionAmericaAirForceGeneral'; Survey = '4 / 3 / 13' }
)

# The randomness is here in the script, not in the simulation: one seed per start, and (without a choice) one of the pairings.
if (-not $PSBoundParameters.ContainsKey('Seed')) { $Seed = Get-Random -Minimum 1 -Maximum 2147483647 }
if ($Seed -lt 1) { throw '-Seed must be between 1 and 2147483647 (0 would be the built-in default seed of the client).' }
$survey = $null
if (-not $Map -and -not $FactionA -and -not $FactionB) {
    if ($Ai -eq 'original') {
        # The original AI is shown on a map that has the template sides of every faction, with a pairing whose levels are easy to tell apart.
        $Map = 'Maps/Alpine Assault/Alpine Assault.map'; $FactionA = 'FactionAmerica'; $FactionB = 'FactionGLA'
    }
    else {
        $pick = $pairings[(Get-Random -Minimum 0 -Maximum $pairings.Count)]
        $Map = $pick.Map; $FactionA = $pick.A; $FactionB = $pick.B; $survey = $pick.Survey
    }
}
else {
    if (-not $Map) { $Map = 'Maps/Alpine Assault/Alpine Assault.map' }
    if (-not $FactionA) { $FactionA = 'FactionAmerica' }
    if (-not $FactionB) { $FactionB = 'FactionChina' }
    if ($Ai -eq 'builtin') {
        $known = $pairings | Where-Object { $_.Map -like "*$Map*" -and $_.A -eq $FactionA -and $_.B -eq $FactionB } | Select-Object -First 1
        if ($known) { $survey = $known.Survey }
    }
}

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Building the client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

$inv = [Globalization.CultureInfo]::InvariantCulture
$clientArgs = @('--content-dir', $ContentDir, '--size', $Size, '--match', $Map, '--faction', $FactionA, '--opponent', $FactionB, '--spectate', '--seed', "$Seed",
    '--spectator-speed', $Speed.ToString($inv))
# One value per slot; a comma inside one argument, so that the client gets it as one.
if ($Ai -eq 'original') { $clientArgs += @('--ai', 'original', '--difficulty', "$DifficultyA,$DifficultyB") }
if ($PSBoundParameters.ContainsKey('TickRate')) { $clientArgs += @('--tick-rate', "$TickRate") }
if ($PSBoundParameters.ContainsKey('MaxZoom')) { $clientArgs += @('--spectator-max-zoom', $MaxZoom.ToString($inv)) }
if ($Mute) { $clientArgs += '--mute' }

$repeat = "pwsh scripts/demo-spectator.ps1 -Seed $Seed -Map `"$Map`" -FactionA $FactionA -FactionB $FactionB"
if ($Ai -eq 'original') { $repeat += " -Ai original -DifficultyA $DifficultyA -DifficultyB $DifficultyB" }
Write-Host ''
Write-Host "== Spectator match" -ForegroundColor Cyan
Write-Host "   Seed     : $Seed    (repeat: $repeat)"
Write-Host "   Map      : $Map"
if ($Ai -eq 'original') {
    Write-Host "   Armies   : 1 = $FactionA ($DifficultyA army)   against   2 = $FactionB ($DifficultyB army)"
    Write-Host '   AI       : the original skirmish AI with its script library (docs/SKIRMISH-AI-ORIGINAL.md)'
}
else {
    Write-Host "   Armies   : 1 = $FactionA   against   2 = $FactionB   (both the built-in AI)"
}
if ($survey) { Write-Host "   Survey   : $survey  (wins 1 / wins 2 / undecided over 20 seeds)" }
Write-Host "   Tick rate: $(if ($PSBoundParameters.ContainsKey('TickRate')) { "$TickRate Hz" } else { 'the client''s default' }),  speed at the start: $($Speed.ToString($inv))x"
Write-Host ''
Write-Host '   Keys'
Write-Host '     P / U / L / E      panel: production / units / losses / economy   (Tab: next, H: panels off/on)'
Write-Host '     F1 / F2            follow the simulated camera of AI 1 / AI 2 (once more or F3: free camera)'
Write-Host '     J / K              jump once to the camera of AI 1 / AI 2'
Write-Host '     O                  overview of the whole map (once more: back)'
Write-Host '     F                  fog: everything -> view of player 1 -> view of player 2 -> everything'
Write-Host '     Space, 1 2 4 8     pause, speed 1x / 2x / 4x / 8x'
Write-Host '     Mouse wheel        zoom (far beyond the limit of a player)'
Write-Host '     Arrow keys, screen edge, right drag: scroll;  middle drag: rotate;  click on the radar: go there'
Write-Host '     Esc                quit'
Write-Host ''
dotnet run --project $project -c Release --no-build -- @clientArgs
exit $LASTEXITCODE
