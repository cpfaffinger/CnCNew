<#
.SYNOPSIS
    A survey of the whole product as a player would use it, by machine: what a program finds broken.

.DESCRIPTION
    Part 1 (no window; Game.Tests suite ProductSurveyTests, real data): every shell screen of both skins, found by clicking every button from the main menu, looked at in
    800x600, 1280x720, 1920x1080 and 1920x1200 at the text sizes 100 % and 150 % (text wider or taller than its window, a window outside the screen, buttons that
    overlap or that another window covers, an opened drop-down that something covers, a missing picture or word, a callback nobody registered, a button that does nothing);
    a person by script through the AI, advanced and debug options of a skirmish, a match and the in-game menu (Options, Save/Load, Restart, Exit); the structure groups of a
    real match; and whole skirmishes of the built-in AI in each tactics profile (balanced, rush, turtle, eco) at the game speeds 1x and 3x - they must end, every player must
    build and collect, a frame must cost what a frame may, and the match must play again from its recording to the same state.
    Part 2 (the window client; needs a display): the same person scripts (scripts/shell/survey-skirmish.txt, scripts/shell/ingame-menu-person.txt) in the real client, in both
    skins.
    Findings that are known and explained are listed in tests/CnCNew.Game.Tests/baselines/product-survey-known.txt (KEY | reason, separated by space-bar-space); they are reported and do not fail.
    Exit code: 0 nothing found, 1 a finding (or a part failed), 2 no game data, 3 not run (no display for part 2; part 1 found nothing).
    What this does not show: how anything looks or feels - that is a person's to judge. The text measure of part 1 is the synthetic one (0.6 of the point size per
    character); an exact check with the renderer's font is
    dotnet run --project tools/CnCNew.UiRenderTool -- --content-dir game/content --out-dir DIR --skins

.EXAMPLE
    pwsh scripts/check-product-survey.ps1 -MatchMinutes 15
#>
param(
    [string]$ContentDir = 'game/content',
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/product-survey'),
    [int]$MatchMinutes = 8,
    [switch]$NoBuild,
    [switch]$SkipWindow,
    [int]$ClientTimeoutSeconds = 420
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this check needs it (scripts/dev-content.ps1)."
    exit 2
}
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$report = Join-Path $OutDir 'survey-report.txt'
$failed = $false
$notRun = $false

# ---- Part 1: no window
if (-not $NoBuild) {
    dotnet build -c Release tests/CnCNew.Game.Tests -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building tests/CnCNew.Game.Tests failed.'; exit 1 }
}
Write-Host "== Part 1: menus, skirmish by script, structure groups, matches (report $report)"
$env:CNCNEW_SURVEY_REPORT = $report
$env:CNCNEW_SURVEY_MATCH_MINUTES = "$MatchMinutes"
Remove-Item Env:CNCNEW_TEST_PREFIX -ErrorAction SilentlyContinue
$part1 = Join-Path $OutDir 'part1.log'
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir $ContentDir --only ProductSurveyTests 2>&1 | Tee-Object -FilePath $part1 | Where-Object { $_ -match '^(FAIL|PASS|\s+(FOUND|menus|skirmish|match))' } | ForEach-Object { Write-Host "   $_" }
if ($LASTEXITCODE -ne 0) { Write-Host '   Part 1 found something broken (or failed).'; $failed = $true }

# ---- Part 2: the window client
function Invoke-ClientScript([string]$Name, [string]$Script, [string]$Skin) {
    $clientDll = Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $clientDll) { return 'the Release client is not built (run without -NoBuild)' }
    $dir = Join-Path $OutDir "$Name-$Skin"
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'settings') | Out-Null
    $arguments = @($clientDll.FullName, '--content-dir', $ContentDir, '--size', '1280x720', '--mute', '--no-transitions', '--no-intro', '--skin', $Skin, '--settings-dir', (Join-Path $dir 'settings'),
        '--net-dir', (Join-Path $dir 'net'), '--skirmish-seconds', '60', '--shell-script', (Join-Path $root $Script), '--shell-seconds', "$($ClientTimeoutSeconds - 60)")
    $log = Join-Path $dir 'client.log'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -NoNewWindow -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError (Join-Path $dir 'client.err')
    if (-not $process.WaitForExit($ClientTimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        return "the client did not end within $ClientTimeoutSeconds s ($log)"
    }
    $process.WaitForExit()
    $lines = if (Test-Path $log) { Get-Content $log } else { @() }
    $summary = $lines | Where-Object { $_ -cmatch '^shell-script summary:' } | Select-Object -Last 1
    if ($process.ExitCode -ne 0 -or -not $summary -or $summary -cnotmatch 'failed=0 finished=1') {
        $step = $lines | Where-Object { $_ -cmatch 'shell-script: FAILED' } | Select-Object -First 1
        return "exit code $($process.ExitCode); $summary $step ($log)"
    }
    return $null
}

Add-Type -AssemblyName System.Windows.Forms
if ($SkipWindow -or [System.Windows.Forms.Screen]::AllScreens.Count -eq 0) {
    Write-Host '== Part 2: NOT RUN (no display, or -SkipWindow): the window client cannot open a device.'
    $notRun = $true
}
else {
    Write-Host '== Part 2: the person scripts in the window client'
    if (-not $NoBuild) {
        dotnet build -c Release src/CnCNew.Client.Stride -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host 'Building src/CnCNew.Client.Stride failed.'; exit 1 }
    }
    foreach ($skin in @('original', 'modern')) {
        foreach ($pair in @(@('options-and-match', 'scripts/shell/survey-skirmish.txt'), @('ingame-menu', 'scripts/shell/ingame-menu-person.txt'))) {
            $problem = Invoke-ClientScript $pair[0] $pair[1] $skin
            if ($problem) {
                Write-Host "   FOUND the script $($pair[1]) in the $skin skin fails in the window client: $problem"
                Add-Content -Path $report -Value "- FOUND window-script|$skin|$($pair[0]): $problem`n    reproduce: pwsh scripts/check-product-survey.ps1 (script $($pair[1]), --skin $skin)"
                $failed = $true
            }
            else { Write-Host "   ok: $($pair[1]) ($skin)" }
        }
    }
}

Write-Host "== Report: $report"
if (Test-Path $report) { Get-Content $report | Select-Object -First 12 | ForEach-Object { Write-Host "   $_" } }
if ($failed) { Write-Host 'check-product-survey: FINDINGS (see the report).'; exit 1 }
if ($notRun) { Write-Host 'check-product-survey: part 1 found nothing; part 2 was not run (exit code 3).'; exit 3 }
Write-Host 'check-product-survey: nothing found broken (what a person judges is not part of it).'
exit 0
