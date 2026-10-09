<#
.SYNOPSIS
  Builds the Stride client from source and starts one of the tech demos (skirmish, mission, shell) or a staged scene (scenario).

.DESCRIPTION
  skirmish  Live match on a skirmish map (command bar, input, minimap, music, EVA) against AI opponents.
  mission   Campaign mission behind the command bar (scripts, mission HUD, the map's briefing sequence).
  scenario  Staged single demo for auditing (-Scenario <name>): a small scene on a real map that isolates one behaviour;
            `PresentationTool scenario-audit` checks the same scene. WITHOUT -Scenario nothing starts: the script lists the available scenes
            (capture-building, tank-duel, jet-strafe, exit-<faction>-<building> ...) and ends. There is no default scene.
  shell     Main-menu shell from the real WND layouts (choose a campaign, briefing, mission, save/load, score screen).

.EXAMPLE
  .\scripts\demo.ps1 skirmish
  .\scripts\demo.ps1 skirmish -Faction FactionChina -Opponent FactionGLA,FactionAmerica -Map "Maps/Tournament Tundra/Tournament Tundra.map"   # a map with enough start positions (Tournament Desert has only two)
  .\scripts\demo.ps1 mission -Map "Maps/GC_ChemGeneral/GC_ChemGeneral.map"
  .\scripts\demo.ps1 scenario                       # lists the scenes
  .\scripts\demo.ps1 scenario -Scenario tank-duel
  .\scripts\demo.ps1 skirmish -Mouse classic         # mouse setup of the original (left selects and commands); default: alternate (left selects, right commands)
  .\scripts\demo.ps1 shell -Size 1920x1080
  .\scripts\demo.ps1 skirmish -SmokeTest      # headless smoke test instead of a window
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('skirmish', 'mission', 'shell', 'scenario')]
    [string]$Demo,

    # Schema-2 runtime content (original assets, not in the repository).
    [string]$ContentDir,

    # Map (skirmish/mission only).
    [string]$Map,

    # Player faction and opponents (skirmish only); several opponents separated by commas.
    [string]$Faction = 'FactionAmerica',
    [string[]]$Opponent = @('FactionChina'),

    # Name of the staged demo (scenario only); without a name the scenes are listed. The client knows the names (--list-scenarios).
    [string]$Scenario,

    # Player side (mission only; empty = the map's default).
    [string]$HumanSide,

    [string]$Size = '1280x720',
    [int]$Seed,

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # Simulation rate (30, 60 or 120 Hz); 0 = the game layer's default.
    [int]$TickRate = 0,

    # Mouse setup of this run: alternate (left selects, right commands; the default of the settings) or classic (original: left selects and commands).
    [ValidateSet('', 'alternate', 'classic')]
    [string]$Mouse = '',

    # Only with -SmokeTest: number of frames (original frames) of the smoke test.
    [int]$Frames = 0,

    # Sound: start muted, or write the mixer output to <directory>\audio.wav instead of the device (outside the repository; see docs/GAME-AUDIO.md).
    [switch]$Mute,
    [string]$AudioCapture,

    [switch]$NoBuild,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) {
    throw "Content directory not found: $ContentDir`nThe verified runtime content lies locally under game\content (not in the repository; scripts\dev-content.ps1 creates it from your own Steam installation); otherwise pass -ContentDir."
}
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path

if (-not $Map) {
    $Map = switch ($Demo) {
        'skirmish' { 'Maps/Alpine Assault/Alpine Assault.map' }
        'mission'  { 'Maps/GC_ChemGeneral/GC_ChemGeneral.map' }
        default    { '' }
    }
}

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host "== Building client ($Configuration) ..." -ForegroundColor Cyan
    dotnet build $project -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

if ($Demo -eq 'scenario') {
    # The client is a windowed application without a console of its own: its redirected output is in the system's ANSI code page.
    $previousEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage)
    $known = @(dotnet run --project $project -c $Configuration --no-build -- --list-scenarios)
    $listExit = $LASTEXITCODE
    [Console]::OutputEncoding = $previousEncoding
    if ($listExit -ne 0) { throw "The client could not list the scenes (exit code $listExit)." }
    $names = $known | ForEach-Object { ($_ -split '\s+', 2)[0] }
    if (-not $Scenario) {
        Write-Host "No scene given - nothing is started. Available scenes (start: .\scripts\demo.ps1 scenario -Scenario <name>):" -ForegroundColor Yellow
        $known | ForEach-Object { Write-Host "  $_" }
        exit 0
    }
    if ($names -notcontains $Scenario) {
        Write-Host "Unknown scene '$Scenario'. Available scenes:" -ForegroundColor Red
        $known | ForEach-Object { Write-Host "  $_" }
        exit 2
    }
}

$clientArgs = @('--content-dir', $ContentDir, '--size', $Size)
switch ($Demo) {
    'skirmish' {
        $clientArgs += @('--match', $Map, '--faction', $Faction, '--opponent', ($Opponent -join ','))
    }
    'mission' {
        $clientArgs += @('--mission', $Map)
        if ($HumanSide) { $clientArgs += @('--human-side', $HumanSide) }
    }
    'shell' {
        $clientArgs += '--shell'
    }
    'scenario' {
        $clientArgs += @('--scenario', $Scenario)
    }
}
if ($PSBoundParameters.ContainsKey('Seed')) { $clientArgs += @('--seed', "$Seed") }
if ($SmokeTest) { $clientArgs += '--smoke-test' }
if ($SmokeTest -and $Frames -gt 0) { $clientArgs += @('--frames', "$Frames") }
if ($TickRate -gt 0) { $clientArgs += @('--tick-rate', "$TickRate") }
if ($Mouse) { $clientArgs += @('--mouse', $Mouse) }
if ($Mute) { $clientArgs += '--mute' }
if ($AudioCapture) { $clientArgs += @('--audio-capture', $AudioCapture) }

Write-Host "== Starting demo '$Demo': $($clientArgs -join ' ')" -ForegroundColor Cyan
dotnet run --project $project -c $Configuration --no-build -- @clientArgs
exit $LASTEXITCODE
