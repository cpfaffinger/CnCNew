<#
.SYNOPSIS
    The replay browser in the window client, driven by a script (docs/REPLAY-BROWSER.md): a skirmish is played and recorded, found in the replay list, played back
    with seeks back and forth to the recorded end, renamed and deleted; a damaged file is refused with its reason. The analysis of the match (docs/MATCH-ANALYSIS.md)
    is opened from the score screen and from the list.

.DESCRIPTION
    Starts the Release window client once with --shell-script (CnCNew.Game.Shell.ShellScript: every step waits for what a person would see and then clicks, types or
    presses a key; the device's mouse and keyboard are ignored). The client keeps its options, savegames and replays in a directory of this run (--settings-dir),
    never in the user's. Before it starts, a damaged replay file is put into that directory.

      main menu > Single Player > Skirmish > Start: a match of -MatchSeconds of game time against the computer (--skirmish-seconds); the score screen; its "Analysis"
      opens the analysis, "Next page" turns a page, OK leads back; back.
      main menu > Load/Replay > Replay: the list shows the recording as playable and the damaged file as damaged; "Load Replay" on the damaged one is refused with
      the reason; "Analysis" computes the analysis of the recording and shows its first page, Right turns a page, Escape leads back; the recording is played at 8x, sought back twice and forward once (PageUp, PageUp, PageDown), and played to its end; the message over the score
      screen says it reached the recorded end; the file is renamed and deleted.

    and checks from the client's console
      - the script ran through;
      - the match was recorded (replay: recorded), ended by its rules;
      - three seeks, each at the session hash the recording kept at that tick (replay: seek ... same);
      - the playback ended at the recorded end with the recorded final hash and result and no recorded hash that differs (replay: end ...).
    What this does not show: what a human sees and feels - whether the list is easy to read, whether the playback controls feel right. The pictures of the list are
    pinned by ReplayBrowserTests (six resolutions); this check is the same screens in the real client with its frame loop, its scene rebuilt at every seek.

    The window client needs a display; without one the check ends with exit code 3 (not run). Logs go to a directory outside the repository.

.EXAMPLE
    pwsh scripts/check-replay-browser.ps1 [-TickRate 30|60|120] [-MatchSeconds 40]
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$MatchSeconds = 40,
    [int]$TickRate = 0,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/replay-browser'),
    [switch]$NoBuild,
    [int]$TimeoutSeconds = 420
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this check needs it (scripts/dev-content.ps1)."
    exit 2
}
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.Screen]::AllScreens.Count -eq 0) {
    Write-Host 'NOT RUN: this machine has no display; the window client cannot open its window.'
    exit 3
}
if (-not $NoBuild) {
    dotnet build -c Release src/CnCNew.Client.Stride -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building src/CnCNew.Client.Stride failed.'; exit 1 }
}
$clientDll = Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.dll' | Select-Object -First 1
if (-not $clientDll) { Write-Host 'Release client not found; start without -NoBuild.'; exit 1 }
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }

$settings = Join-Path $OutDir 'settings'
$replays = Join-Path $settings 'Replays'
New-Item -ItemType Directory -Force -Path $replays | Out-Null
# A file that claims to be a replay and is not one any more: the list must show it as damaged and refuse to play it.
[IO.File]::WriteAllBytes((Join-Path $replays 'Broken file.cncreplay'), [Text.Encoding]::ASCII.GetBytes('CNCREPLY and then nothing that fits'))

$script = Join-Path $OutDir 'replay.script.txt'
[IO.File]::WriteAllLines($script, @(
    'wait screen MainMenuScreen',
    'click MainMenu.wnd:ButtonSinglePlayer',
    'click MainMenu.wnd:ButtonSkirmish',
    'wait screen SkirmishSetupScreen',
    'click SkirmishGameOptionsMenu.wnd:ButtonStart',
    'limit 240',
    'wait phase InMatch',
    'wait phase Results',
    'limit 30',
    'wait screen ScoreScreenScreen',
    'expect text ScoreScreen.wnd:ButtonSaveReplay equals Analysis',
    'click ScoreScreen.wnd:ButtonSaveReplay',
    'wait screen MatchAnalysisScreen',
    'expect text ScoreScreen.wnd:ChallengeWinLossText contains page 1 of 6: Economy',
    'click ScoreScreen.wnd:ButtonBuddy',
    'expect text ScoreScreen.wnd:ChallengeWinLossText contains page 2 of 6: Army',
    'click ScoreScreen.wnd:ButtonOk',
    'wait screen ScoreScreenScreen',
    'click ScoreScreen.wnd:ButtonOk',
    'wait screen MainMenuScreen',
    'click MainMenu.wnd:ButtonLoadReplay',
    'click MainMenu.wnd:ButtonReplay',
    'wait screen ReplayMenuScreen',
    'wait list ReplayMenu.wnd:ListboxReplayFiles contains Playable',
    'wait list ReplayMenu.wnd:ListboxReplayFiles contains Broken file',
    'expect text ReplayDetails.wnd:StaticTextDetails contains No winner (time limit)',
    'pick ReplayMenu.wnd:ListboxReplayFiles Broken file',
    'click ReplayMenu.wnd:ButtonLoadReplay',
    'wait screen MessageBoxScreen',
    'expect text MessageBox.wnd:StaticTextMessage contains damaged',
    'click MessageBox.wnd:ButtonOk',
    'wait screen ReplayMenuScreen',
    'pick ReplayMenu.wnd:ListboxReplayFiles Playable',
    'click ReplayAnalysis.wnd:ButtonAnalysis',
    'wait screen MatchAnalysisScreen',
    'limit 120',
    'wait text ScoreScreen.wnd:ChallengeWinLossText contains page 1 of 6',
    'limit 30',
    'key Right',
    'expect text ScoreScreen.wnd:ChallengeWinLossText contains page 2 of 6: Army',
    'key Escape',
    'wait screen ReplayMenuScreen',
    'pick ReplayMenu.wnd:ListboxReplayFiles Playable',
    'click ReplayMenu.wnd:ButtonLoadReplay',
    'limit 240',
    'wait phase InMatch',
    'key D8',
    "wait match tick atleast `$SEEKFROM",
    'key PageUp',
    'wait match seeks equals 1',
    'key PageUp',
    'wait match seeks equals 2',
    'key PageDown',
    'wait match seeks equals 3',
    'key D8',
    'wait phase Results',
    'limit 30',
    'wait screen MessageBoxScreen',
    'expect text MessageBox.wnd:StaticTextMessage contains reached the recorded end',
    'click MessageBox.wnd:ButtonOk',
    'click ScoreScreen.wnd:ButtonOk',
    'wait screen ReplayMenuScreen',
    'pick ReplayMenu.wnd:ListboxReplayFiles Playable',
    'click ReplayMenu.wnd:ButtonCopyReplay',
    'wait screen ReplayRenameScreen',
    'type PopupReplay.wnd:TextEntryReplayName Watched in the window',
    'click PopupReplay.wnd:ButtonSave',
    'wait screen ReplayMenuScreen',
    'wait list ReplayMenu.wnd:ListboxReplayFiles contains Watched in the window',
    'click ReplayMenu.wnd:ButtonDeleteReplay',
    'wait screen MessageBoxScreen',
    'click MessageBox.wnd:ButtonYes',
    'wait screen ReplayMenuScreen',
    'wait list ReplayMenu.wnd:ListboxReplayFiles lacks Watched in the window',
    'click ReplayMenu.wnd:ButtonBack',
    'wait screen MainMenuScreen'))

$rate = if ($TickRate -gt 0) { $TickRate } else { 60 }
$seekFrom = [int]([Math]::Floor($MatchSeconds * 0.75) * $rate)
$arguments = @($clientDll.FullName, '--content-dir', $ContentDir, '--size', '1024x768', '--mute', '--no-transitions', '--settings-dir', $settings, '--net-dir', (Join-Path $OutDir 'net'),
    '--skirmish-seconds', "$MatchSeconds", '--replay-checkpoint-seconds', '10', '--shell-script', $script, '--shell-var', "SEEKFROM=$seekFrom", '--shell-seconds', "$($TimeoutSeconds - 60)")
if ($TickRate -gt 0) { $arguments += @('--tick-rate', "$TickRate") }
Write-Host "== The replay browser in the window: a $MatchSeconds s skirmish at $rate Hz recorded, listed, played with three seeks, renamed, deleted (logs in $OutDir)"
$watch = [Diagnostics.Stopwatch]::StartNew()
$log = Join-Path $OutDir 'client.log'
$process = Start-Process -FilePath 'dotnet' -ArgumentList ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -NoNewWindow -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError (Join-Path $OutDir 'client.err')
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Fail "the client did not end within $TimeoutSeconds s"
}
else {
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { Fail "the client ended with exit code $($process.ExitCode)" }
}
$lines = if (Test-Path $log) { Get-Content $log } else { @() }

$summary = $lines | Where-Object { $_ -cmatch '^shell-script summary:' } | Select-Object -Last 1
if (-not $summary) { Fail 'no summary of the script (see client.log, client.err)' }
elseif ($summary -cnotmatch 'failed=0 finished=1') {
    Fail $summary
    $lines | Where-Object { $_ -cmatch 'shell-script: FAILED' } | ForEach-Object { Write-Host "      $_" }
}
else { Write-Host "   $summary" }

$recorded = $lines | Where-Object { $_ -cmatch '^replay: recorded ' } | Select-Object -Last 1
if (-not $recorded) { Fail 'the skirmish was not recorded' }
elseif ($recorded -cnotmatch ': completed at tick') { Fail "the recording did not end by the match's rules: $recorded" }
else { Write-Host "   $recorded" }

$seeks = @($lines | Where-Object { $_ -cmatch '^replay: seek \d+ ' })
if ($seeks.Count -ne 3) { Fail "three seeks expected, $($seeks.Count) made" }
foreach ($seek in $seeks) {
    if ($seek -cnotmatch ', same$') { Fail "a seek did not land at the recorded hash: $seek" }
    else { Write-Host "   $seek" }
}
if ($seeks.Count -eq 3 -and $seeks[1] -cnotmatch 'from a checkpoint') { Fail "the second seek back did not restore a checkpoint: $($seeks[1])" }

$end = $lines | Where-Object { $_ -cmatch '^replay: end tick ' } | Select-Object -Last 1
if (-not $end) { Fail 'the playback did not reach its end' }
elseif ($end -cnotmatch 'final hash same, result same, hashes checked \d+, mismatches 0') { Fail "the playback did not reach the recorded end: $end" }
else { Write-Host "   $end" }

$refused = $lines | Where-Object { $_ -cmatch "^replay: 'Broken file' cannot be played" }
if ($refused) { Fail "the damaged file reached the playback instead of being refused by the list: $refused" }
if (Get-ChildItem $replays -Filter '*.cncreplay' | Where-Object { $_.Name -ne 'Broken file.cncreplay' }) { Fail 'the recorded replay is still in the directory after it was deleted' }

Write-Host ("== {0} after {1:F0} s" -f $(if ($failed -eq 0) { 'ok' } else { "$failed problem(s)" }), $watch.Elapsed.TotalSeconds)
exit ([Math]::Min($failed, 1))
