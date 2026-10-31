<#
.SYNOPSIS
  Runs the checks that fit a change: the test programs of the touched areas (affected), the gate before a push (gate) or everything (full).

.DESCRIPTION
  The strategy is docs/TESTING.md; this script is its executable form. It reads which files differ from -Since (default: everything that is
  not on origin/master yet, committed or not, plus untracked files), maps every path to an area and runs the steps that area asks for at the tier:

    affected  before every commit: build, the test programs of the touched areas and of what reads them directly. Minutes.
    gate      before a push that can change behaviour: affected, plus the determinism yardstick (input manifest, quick reference set),
              the other tick rates and the consumers further up. Around a quarter of an hour.
    full      completion of a phase, a written reference manifest, a cross-cutting change: every step.

  Steps run from cheap to expensive and the run stops at the first failure (-KeepGoing runs on). Steps that need game/content are skipped
  and named when it is missing. The logs lie outside the repository (default: %TEMP%\cncnew-test). Exit code 1 when a step failed.
  Focused runs while working (one test, one suite) are called directly; -Plan prints the commands of a tier to copy from.

.EXAMPLE
  pwsh scripts/test.ps1                              # affected, from the changes against origin/master
  pwsh scripts/test.ps1 -Plan -Tier gate             # print what the gate would run, run nothing
  pwsh scripts/test.ps1 -Tier gate -Area simulation  # the gate of an area, whatever the changes
  pwsh scripts/test.ps1 -Tier full                   # everything
#>
[CmdletBinding()]
param(
    [ValidateSet('affected', 'gate', 'full')]
    [string]$Tier = 'affected',

    # Compare with this commit (the working tree counts, committed or not).
    [string]$Since = 'origin/master',

    # Use these repository paths instead of asking Git.
    [string[]]$Paths = @(),

    # Use these areas instead of deriving them from paths (names: -Plan -Area ? lists them).
    [string[]]$Area = @(),

    # Print the areas and the steps with their commands; run nothing.
    [switch]$Plan,

    # Do not stop at the first failed step.
    [switch]$KeepGoing,

    [string]$ContentDir,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = 'game/content' }
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test' }
$Paths = @($Paths | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Area = @($Area | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })

# ---- Areas: the first matching prefix decides. A path nothing matches counts as 'build' (everything is affected).
$areaOfPath = @(
    @('src/CnCNew.Archives/', 'archives'), @('tests/CnCNew.Archives.Tests/', 'archives'),
    @('src/CnCNew.IO/', 'io'), @('tests/CnCNew.IO.Tests/', 'io'),
    @('src/CnCNew.Content/', 'content'), @('tests/CnCNew.Content.Tests/', 'content'),
    @('src/CnCNew.Import/', 'import'), @('tests/CnCNew.Import.Tests/', 'import'), @('tools/CnCNew.AssetTool/', 'import'),
    @('baselines/steam-master.json', 'import'),
    @('src/CnCNew.Images/', 'images'), @('tests/CnCNew.Images.Tests/', 'images'), @('tools/CnCNew.ImageTool/', 'images'),
    @('src/CnCNew.Formats/', 'formats'), @('tests/CnCNew.Formats.Tests/', 'formats'), @('tools/CnCNew.MapTool/', 'formats'),
    @('src/CnCNew.W3D/', 'w3d'), @('tests/CnCNew.W3D.Tests/', 'w3d'), @('tools/CnCNew.W3dTool/', 'w3d'),
    @('src/CnCNew.Data/', 'data'), @('tests/CnCNew.Data.Tests/', 'data'), @('tools/CnCNew.IniTool/', 'data'),
    @('baselines/compatibility.json', 'data'),
    @('src/CnCNew.Simulation/', 'simulation'), @('tests/CnCNew.Simulation.Tests/', 'simulation'),
    @('tools/CnCNew.SimTool/', 'simtool'), @('baselines/input-manifest.json', 'simtool'),
    @('baselines/reference-manifest.json', 'manifest'),
    @('src/CnCNew.Presentation/', 'presentation'), @('tests/CnCNew.Presentation.Tests/', 'presentation'),
    @('tools/CnCNew.PresentationTool/', 'presentation'),
    @('src/CnCNew.Ui/', 'ui'), @('tests/CnCNew.Ui.Tests/', 'ui'), @('tools/CnCNew.UiRenderTool/', 'ui'),
    @('src/CnCNew.Audio/', 'audio'), @('tests/CnCNew.Audio.Tests/', 'audio'),
    @('src/CnCNew.Net/', 'net'), @('src/CnCNew.Server/', 'net'), @('tests/CnCNew.Net.Tests/', 'net'), @('tools/CnCNew.NetTool/', 'net'), @('tools/CnCNew.DedicatedServer/', 'net'),
    @('tests/Shared/', 'simulation'),
    @('src/CnCNew.Game/', 'game'), @('tests/CnCNew.Game.Tests/', 'game'), @('tools/CnCNew.CampaignTool/', 'game'),
    @('src/CnCNew.Client.Stride/', 'client'),
    @('scripts/shell/', 'game'),
    @('scripts/', 'scripts'),
    @('docs/', 'docs')
)
function Get-AreaOfPath([string]$path) {
    $path = $path -replace '\\', '/'
    foreach ($entry in $areaOfPath) { if ($path.StartsWith($entry[0], [StringComparison]::OrdinalIgnoreCase)) { return $entry[1] } }
    if ($path -notmatch '/' -and $path -match '\.(md|txt)$') { return 'docs' }
    if ($path -in '.gitignore', '.gitattributes') { return 'docs' }
    return 'build'
}

# ---- Which steps an area asks for. 'gate' is what the gate adds to 'affected'.
$stepsOfArea = @{
    archives     = @{ affected = 'archives', 'io', 'import';                gate = @() }
    io           = @{ affected = 'io', 'import';                            gate = @() }
    content      = @{ affected = 'content', 'import';                       gate = 'verify' }
    import       = @{ affected = 'import';                                  gate = 'verify' }
    images       = @{ affected = 'images', 'import', 'presentation';        gate = 'presentation-content', 'game' }
    formats      = @{ affected = 'formats', 'import';                       gate = 'inputs', 'quick', 'presentation-content' }
    w3d          = @{ affected = 'w3d';                                     gate = 'presentation-content', 'game' }
    data         = @{ affected = 'data', 'data-content', 'compat';          gate = 'ui', 'audio', 'sim', 'inputs', 'quick', 'patch-layer' }
    simulation   = @{ affected = 'sim';                                     gate = 'sim-30', 'sim-120', 'presentation', 'inputs', 'quick', 'rate-suite', 'game-sim', 'check-match', 'net' }
    simtool      = @{ affected = @();                                       gate = 'inputs', 'quick' }
    manifest     = @{ affected = 'inputs', 'reference';                     gate = @() }
    presentation = @{ affected = 'presentation';                            gate = 'presentation-content', 'game' }
    ui           = @{ affected = 'ui';                                      gate = 'game', 'skins' }
    audio        = @{ affected = 'audio';                                   gate = 'audio-content', 'game' }
    net          = @{ affected = 'net';                                     gate = 'net-30', 'net-120', 'net-probe', 'net-match', 'net-robust', 'net-join', 'net-lobby', 'net-acceptance', 'net-load', 'server-package', 'diagnostics', 'game' }
    game         = @{ affected = 'game-focus', 'net';                       gate = 'game', 'net', 'net-lobby', 'replay-browser', 'diagnostics', 'skins' }
    client       = @{ affected = @();                                       gate = 'check-spectator', 'check-dozer', 'check-hotkeys', 'net-join', 'net-lobby', 'net-acceptance', 'replay-browser', 'diagnostics', 'client-package', 'skins' }
    scripts      = @{ affected = @();                                       gate = @() }
    docs         = @{ affected = @();                                       gate = @() }
    build        = @{ affected = 'archives', 'io', 'content', 'import', 'images', 'formats', 'w3d', 'data', 'ui', 'audio', 'presentation', 'sim', 'net'
                      gate     = 'data-content', 'compat', 'verify', 'presentation-content', 'inputs', 'quick', 'game' }
}

# ---- The suites of Game.Tests a folder of CnCNew.Game asks for at 'affected' (a file outside these folders asks for all of them).
$gameSuites = @{
    Analysis  = 'MatchAnalysisTests', 'ReplayBrowserTests', 'ObserverTests'
    Audio     = 'AudioDirectorTests'
    Campaign  = 'CampaignTests', 'CampaignShellTests'
    Hud       = 'HudTests', 'MissionHudTests', 'HotkeyTests', 'NetHudTests', 'SkinTests', 'CampaignPinTests'
    Lockstep  = 'LiveMatchTests', 'HudTests', 'CampaignShellTests', 'SpectatorTests', 'NetLobbyTests', 'NetShellTests'
    Live      = 'LiveMatchTests', 'DeterminismTests', 'TimeScaleTests', 'ScenarioTests', 'MissionPresentationTests', 'MissionEffectsTests', 'MissionTextTests', 'ReplayBrowserTests', 'AttachedEffectsTests', 'MatchAnalysisTests'
    Replays   = 'ReplayBrowserTests', 'NetShellTests', 'MatchAnalysisTests', 'ObserverTests'
    Save      = 'CampaignTests', 'CampaignShellTests'
    Shell     = 'ShellTests', 'ShellTransitionTests', 'CampaignShellTests', 'NetScreenTests', 'NetShellTests', 'ReplayBrowserTests', 'MatchAnalysisTests', 'SkinTests', 'CampaignPinTests'
    Spectator = 'SpectatorTests', 'ReplayBrowserTests', 'ObserverTests', 'SkinTests'
}
$gameSuitesOfSimulation = 'DeterminismTests', 'LiveMatchTests', 'SourceRuleTests'

# ---- Changed paths and their areas.
if ($Area.Count -eq 0 -and $Paths.Count -eq 0 -and $Tier -ne 'full') {
    Push-Location $repo
    try {
        git rev-parse --verify --quiet "$Since^{commit}" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Unknown commit '$Since' (pass -Since, -Paths or -Area)." }
        $Paths = @(git diff --name-only $Since) + @(git ls-files --others --exclude-standard) | Where-Object { $_ } | Sort-Object -Unique
    }
    finally { Pop-Location }
}
$known = @($stepsOfArea.Keys | Sort-Object)
if ($Area.Count -gt 0) {
    if ($Area -contains '?') { Write-Host "Areas: $($known -join ', ')"; exit 0 }
    $unknown = @($Area | Where-Object { $_ -notin $known })
    if ($unknown.Count -gt 0) { throw "Unknown area: $($unknown -join ', '). Known: $($known -join ', ')." }
    $areas = $Area
}
else { $areas = @($Paths | ForEach-Object { Get-AreaOfPath $_ } | Sort-Object -Unique) }

$focus = [System.Collections.Generic.SortedSet[string]]::new()
$wholeGame = $false
foreach ($path in $Paths) {
    if ($path -match '^src/CnCNew\.Game/([^/]+)/') { if ($gameSuites.ContainsKey($Matches[1])) { foreach ($suite in $gameSuites[$Matches[1]]) { [void]$focus.Add($suite) } } else { $wholeGame = $true } }
    elseif ($path -match '^tests/CnCNew\.Game\.Tests/(\w+Tests)\.cs$') { [void]$focus.Add($Matches[1]) }
    elseif ((Get-AreaOfPath $path) -eq 'game') { $wholeGame = $true }
}
if ($focus.Count -eq 0) { $wholeGame = $true } else { [void]$focus.Add('SourceRuleTests') }

$wanted = [System.Collections.Generic.HashSet[string]]::new()
foreach ($name in $areas) {
    foreach ($id in @($stepsOfArea[$name].affected)) { [void]$wanted.Add($id) }
    if ($Tier -ne 'affected') { foreach ($id in @($stepsOfArea[$name].gate)) { [void]$wanted.Add($id) } }
}
if ($wanted.Contains('game-focus') -and $wholeGame) { [void]$wanted.Remove('game-focus'); [void]$wanted.Add('game') }
if ($wanted.Contains('game')) { [void]$wanted.Remove('game-focus'); [void]$wanted.Remove('game-sim') }
$needsBuild = @($areas | Where-Object { $_ -notin 'docs', 'scripts' }).Count -gt 0

# ---- The steps, from cheap to expensive. Release = the project that is built in Release before the step.
$simTests = 'tests/CnCNew.Simulation.Tests'; $gameTests = 'tests/CnCNew.Game.Tests'; $simTool = 'tools/CnCNew.SimTool'
# -Environment: the step needs something of the machine that is not part of the repository's requirements (a Linux environment); it says so with exit code 3, and the
# step is then shown as NOT RUN - never as passed.
function New-Step([string]$Id, [string]$Title, [string[]]$Command, [switch]$Content, [string]$Release, [switch]$Environment) {
    [pscustomobject]@{ Id = $Id; Title = $Title; Command = $Command; Content = [bool]$Content; Release = $Release; Environment = [bool]$Environment }
}
function Test-Program([string]$name) { , @('dotnet', 'run', '--no-build', '--project', "tests/CnCNew.$name.Tests") }
function Release-Run([string]$project) { , @('dotnet', 'run', '-c', 'Release', '--no-build', '--project', $project, '--') }
function Script-Run([string]$name) { , @('pwsh', '-NoLogo', '-NoProfile', '-File', "scripts/$name.ps1") }
$content = '--content-dir', $ContentDir
$steps = @(
    New-Step 'build'        'Build the solution (0 warnings)'                 @('dotnet', 'build', 'CnCNew.slnx', '-v', 'q', '--nologo')
    New-Step 'archives'     'Archives.Tests'                                  (Test-Program 'Archives')
    New-Step 'io'           'IO.Tests'                                        (Test-Program 'IO')
    New-Step 'content'      'Content.Tests'                                   (Test-Program 'Content')
    New-Step 'images'       'Images.Tests'                                    (Test-Program 'Images')
    New-Step 'formats'      'Formats.Tests'                                   (Test-Program 'Formats')
    New-Step 'import'       'Import.Tests'                                    (Test-Program 'Import')
    New-Step 'ui'           'Ui.Tests'                                        (Test-Program 'Ui')
    New-Step 'audio'        'Audio.Tests'                                     (Test-Program 'Audio')
    New-Step 'presentation' 'Presentation.Tests'                              (Test-Program 'Presentation')
    New-Step 'data'         'Data.Tests'                                      (Test-Program 'Data')
    New-Step 'compat'       'Compatibility baseline'                          @('dotnet', 'run', '--no-build', '--project', 'tools/CnCNew.IniTool', '--', 'compat-check', $ContentDir, 'baselines/compatibility.json') -Content
    New-Step 'verify'       'Content against its manifest'                    @('dotnet', 'run', '--no-build', '--project', 'tools/CnCNew.AssetTool', '--', 'verify', $ContentDir) -Content
    New-Step 'w3d'          'W3D.Tests (real data)'                           ((Test-Program 'W3D') + '--' + $content) -Content
    New-Step 'data-content' 'Data.Tests (real data)'                          ((Test-Program 'Data') + '--' + $content) -Content
    New-Step 'vulnerable'   'Known vulnerabilities of the packages'           (Script-Run 'audit-vulnerable')
    New-Step 'licences'     'The third-party list is the packages'' declared licences; the server has none' ((Script-Run 'licence-report') + '-Check')
    New-Step 'ggc-catalogue' 'The GeneralsGameCode fix catalogue is current (needs the reference checkout)' @('python', 'scripts/ggc-fix-catalogue.py', '--check') -Environment
    New-Step 'net'          'Net.Tests (60 Hz)'                               (Test-Program 'Net')
    New-Step 'presentation-content' 'Presentation.Tests (real data)'          ((Test-Program 'Presentation') + '--' + $content) -Content
    New-Step 'sim'          'Simulation.Tests (60 Hz)'                        (Release-Run $simTests) -Release $simTests
    New-Step 'game-focus'   "Game.Tests ($($focus -join ', '))"               ((Release-Run $gameTests) + $content + '--only' + ($focus -join ',')) -Content -Release $gameTests
    New-Step 'game-sim'     "Game.Tests ($($gameSuitesOfSimulation -join ', '))" ((Release-Run $gameTests) + $content + '--only' + ($gameSuitesOfSimulation -join ',')) -Content -Release $gameTests
    New-Step 'inputs'       'Input manifest'                                  ((Release-Run $simTool) + 'input-manifest' + 'check' + $ContentDir + 'baselines/input-manifest.json') -Content -Release $simTool
    New-Step 'sim-30'       'Simulation.Tests (30 Hz)'                        ((Release-Run $simTests) + '--tick-rate' + '30') -Release $simTests
    New-Step 'sim-120'      'Simulation.Tests (120 Hz)'                       ((Release-Run $simTests) + '--tick-rate' + '120') -Release $simTests
    New-Step 'net-30'       'Net.Tests (30 Hz)'                               ((Test-Program 'Net') + '--' + '--tick-rate' + '30')
    New-Step 'net-120'      'Net.Tests (120 Hz)'                              ((Test-Program 'Net') + '--' + '--tick-rate' + '120')
    New-Step 'audio-content' 'Audio.Tests (real data)'                        ((Test-Program 'Audio') + '--' + $content) -Content
    New-Step 'quick'        'Reference manifest, quick set'                   ((Release-Run $simTool) + 'reference-manifest' + 'check' + $ContentDir + 'baselines/reference-manifest.json' + '--set' + 'quick') -Content -Release $simTool
    New-Step 'rate-suite'   'Rate-equivalence suite (30/60/120 Hz)'           ((Release-Run $simTests) + $content + '--only' + 'RateEquivalence') -Content -Release $simTests
    New-Step 'game'         'Game.Tests (real data)'                          ((Release-Run $gameTests) + $content) -Content -Release $gameTests
    New-Step 'check-spectator' 'Spectator mode through the client'            (Script-Run 'check-spectator') -Content
    New-Step 'check-dozer'  'Build click path through the client'             ((Script-Run 'check-dozer-build') + '-Quick') -Content
    New-Step 'check-hotkeys' 'Build keys through the client'                  (Script-Run 'check-build-hotkeys') -Content
    New-Step 'sim-content'  'Simulation.Tests (real data, with the rate suite)' ((Release-Run $simTests) + $content) -Content -Release $simTests
    New-Step 'reference'    'Reference manifest, full set'                    ((Release-Run $simTool) + 'reference-manifest' + 'check' + $ContentDir + 'baselines/reference-manifest.json' + '--set' + 'full') -Content -Release $simTool
    New-Step 'check-match'  'Match description in three processes'            ((Script-Run 'check-match-description') + '-NoBuild') -Content -Release $simTool
    New-Step 'session-check' 'Skirmish sessions saved and continued (AI table, eight players)' ((Release-Run $simTool) + 'session-check' + $ContentDir + '--jobs' + '3') -Content -Release $simTool
    New-Step 'net-probe'    'Four clients and four computer players over the relay with delay and jitter' ((Release-Run 'tools/CnCNew.NetTool') + 'loopback-match' + $ContentDir + '--map' + 'Maps/Twilight Flame/Twilight Flame.map' + '--humans' + '4' + '--ais' + '4' + '--seconds' + '60' + '--delay' + '40' + '--jitter' + '60') -Content -Release 'tools/CnCNew.NetTool'
    New-Step 'net-match'    'Server, proxy and four client processes: a match with delay, jitter and loss, and one with a vanishing client' (Script-Run 'check-net-match') -Content
    New-Step 'net-robust'   'Rejoin, late and delayed watchers, the server replay and the desync diagnosis with real processes' (Script-Run 'check-net-robustness') -Content
    New-Step 'net-join'     'The window client joins a match on a dedicated server; a client without a window is the other player' (Script-Run 'check-net-join') -Content
    New-Step 'net-lobby'    'The menus in the window client by script: host game, a changed key, join with a password, a match, back into the room; a local skirmish' (Script-Run 'check-net-lobby') -Content -Environment
    New-Step 'replay-browser' 'The replay browser in the window client by script: a skirmish recorded, listed, a damaged file refused, played with three seeks to the recorded end, renamed, deleted' (Script-Run 'check-replay-browser') -Content -Environment
    New-Step 'net-acceptance' 'Two window clients, three clients without a window, a watcher and two computer players on a dedicated server with delay and loss: a rejoin, a stall, a vanished client, a pause - and what the indicators said' (Script-Run 'check-net-acceptance') -Content -Environment
    New-Step 'net-cross-os' 'A match across Windows and Linux (WSL): server and two clients under Linux, two under Windows' (Script-Run 'check-net-cross-os') -Content -Environment
    New-Step 'net-load'     'Eight rooms at once on one server, with members that vanish, leave and come back' (Script-Run 'check-net-load')
    New-Step 'server-package' 'The self-contained server packages: their contents, the sample configuration, a match on the win-x64 server' (Script-Run 'check-server-package') -Content
    New-Step 'server-package-linux' 'The linux-x64 server package under Linux (WSL) with an empty environment, clients under Windows' ((Script-Run 'check-server-package') + '-Platform' + 'linux') -Content -Environment
    New-Step 'diagnostics'  'Crash reports, run logs and support bundles of the client and the server: what they hold and what they never hold' (Script-Run 'check-diagnostics')
    New-Step 'client-package' 'The self-contained Windows package: layout, no original data, the packaged patcher and client run' (Script-Run 'check-client-package') -Content -Environment
    New-Step 'skins'        'The two interface skins: a person by script switches to the modern skin in the window and walks the shell; the readability of both skins with the GDI font' (Script-Run 'check-skins') -Content -Environment
    New-Step 'patch-layer'  'The patched content: its compatibility baseline, a deterministic match, two clients on a server and a client of the game without the patch refused' (Script-Run 'check-patch-layer') -Content -Environment
    New-Step 'presentation-30'  'Presentation.Tests (real data, 30 Hz)'       ((Test-Program 'Presentation') + '--' + $content + '--tick-rate' + '30') -Content
    New-Step 'presentation-120' 'Presentation.Tests (real data, 120 Hz)'      ((Test-Program 'Presentation') + '--' + $content + '--tick-rate' + '120') -Content
    New-Step 'game-30'      'Game.Tests (real data, 30 Hz)'                   ((Release-Run $gameTests) + $content + '--tick-rate' + '30') -Content -Release $gameTests
    New-Step 'game-120'     'Game.Tests (real data, 120 Hz)'                  ((Release-Run $gameTests) + $content + '--tick-rate' + '120') -Content -Release $gameTests
    New-Step 'sim-long'     'Simulation.Tests (long run)'                     ((Release-Run $simTests) + '--long' + '--only' + 'Long run') -Release $simTests
    New-Step 'scenes'       'Staged scenes'                                   (Script-Run 'audit-scenarios') -Content
)
# 'net-cross-os' and 'server-package-linux' are selected by no area: they run at 'full' only.
# Not part of 'full': what a wider step already contains.
$contained = 'game-focus', 'game-sim', 'quick', 'rate-suite'
if ($Tier -eq 'full') { $selected = @($steps | Where-Object { $_.Id -notin $contained }) }
else { $selected = @($steps | Where-Object { $wanted.Contains($_.Id) -or ($_.Id -eq 'build' -and $needsBuild) }) }

$haveContent = Test-Path -LiteralPath (Join-Path $repo (Join-Path $ContentDir 'content-manifest.json'))
Write-Host "Tier: $Tier" -ForegroundColor Cyan
if ($Tier -ne 'full') {
    Write-Host "Areas: $(if ($areas.Count) { $areas -join ', ' } else { '(no change)' })$(if ($Paths.Count) { "  ($($Paths.Count) path(s) against $Since)" })"
}
if ($selected.Count -eq 0) { Write-Host 'Nothing to run for this change at this tier (documentation or scripts only: run the script you changed).'; exit 0 }
if ($Plan) {
    foreach ($step in $selected) {
        Write-Host ("  {0,-22} {1}" -f $step.Id, $step.Title)
        if ($step.Release) { Write-Host "      dotnet build -c Release $($step.Release) -v q --nologo" -ForegroundColor DarkGray }
        $words = $step.Command | ForEach-Object { if ($_ -match '[\s,]') { '"' + $_ + '"' } else { $_ } }
        Write-Host "      $($words -join ' ')" -ForegroundColor DarkGray
    }
    exit 0
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
if ($OutDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw "The output directory must lie outside the repository: $OutDir" }
$results = [System.Collections.Generic.List[object]]::new()
$built = [System.Collections.Generic.HashSet[string]]::new()
$failed = $false
function Invoke-Logged([string]$id, [string[]]$command) {
    $log = Join-Path $OutDir "$id.log"
    $rest = @($command | Select-Object -Skip 1)
    & $command[0] @rest *>&1 | ForEach-Object { "$_" } | Set-Content -LiteralPath $log -Encoding utf8
    return $LASTEXITCODE
}
$total = [Diagnostics.Stopwatch]::StartNew()
Push-Location $repo
try {
    foreach ($step in $selected) {
        if ($failed -and -not $KeepGoing) { $results.Add([pscustomobject]@{ Step = $step.Id; State = 'not run'; Seconds = ''; What = $step.Title }); continue }
        if ($step.Content -and -not $haveContent) { $results.Add([pscustomobject]@{ Step = $step.Id; State = 'SKIPPED'; Seconds = ''; What = "$($step.Title) - no $ContentDir" }); continue }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $exit = 0
        if ($step.Release -and $built.Add($step.Release)) {
            Write-Host "== build -c Release $($step.Release)" -ForegroundColor DarkGray
            $exit = Invoke-Logged "$($step.Id)-build" @('dotnet', 'build', '-c', 'Release', $step.Release, '-v', 'q', '--nologo')
        }
        Write-Host "== $($step.Title)" -ForegroundColor Cyan
        if ($exit -eq 0) { $exit = Invoke-Logged $step.Id $step.Command }
        $log = Join-Path $OutDir "$($step.Id).log"
        $last = (Test-Path -LiteralPath $log) ? (Get-Content -LiteralPath $log | Where-Object { $_.Trim() } | Select-Object -Last 1) : ''
        # The build counts its warnings itself: a build with warnings fails through TreatWarningsAsErrors.
        $state = $exit -eq 0 ? 'ok' : ($exit -eq 3 -and $step.Environment) ? 'NOT RUN' : 'FAILED'
        Write-Host ("   {0} in {1:n0} s  {2}" -f $state, $watch.Elapsed.TotalSeconds, $last)
        if ($state -eq 'FAILED') {
            $failed = $true
            Write-Host "   log: $log" -ForegroundColor Red
            Get-Content -LiteralPath $log -ErrorAction SilentlyContinue | Where-Object { $_ -match 'FAIL|error|Error|differ|mismatch' } | Select-Object -First 15 | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
        }
        $results.Add([pscustomobject]@{ Step = $step.Id; State = $state; Seconds = [int]$watch.Elapsed.TotalSeconds; What = $step.Title })
    }
}
finally { Pop-Location }

Write-Host "== Result: tier $Tier, $([int]$total.Elapsed.TotalSeconds) s, logs in $OutDir" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
$skipped = @($results | Where-Object State -eq 'SKIPPED').Count
if ($skipped -gt 0) { Write-Warning "$skipped step(s) skipped for lack of $ContentDir (scripts/dev-content.ps1): this run does not count as the tier." }
$absent = @($results | Where-Object { $_.State -eq 'NOT RUN' -and $_.Seconds -ne '' })
if ($absent.Count -gt 0) { Write-Warning "$($absent.Count) step(s) did not run because this machine lacks what they need ($(($absent | ForEach-Object Step) -join ', ')): they are not passed, and a report names them as not run." }
exit ($failed ? 1 : 0)
