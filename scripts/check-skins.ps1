<#
.SYNOPSIS
    The two interface skins in the window (docs/UI-MODERN.md section 9): a scripted person switches to the modern skin in the options and walks the shell (R4), and the
    readability check of both skins measures every screen with the renderer's own GDI font (R3).

.DESCRIPTION
    1. Starts the Release window client once with --shell-script scripts/shell/skin-person.txt and a settings directory of this run (never the user's): the client
       starts in the original skin (nothing on its command line names a skin), the script opens Options > More options, chooses "Modern", accepts, and walks main menu >
       Single Player > Skirmish > a match of -MatchSeconds > the score screen > the analysis (two pages) > the replay list > the playback of the match to its end. The
       settings file must then say "skin": "modern".
    2. Builds and runs tools/CnCNew.UiRenderTool --skins: main menu, single player, skirmish setup and map picker, the HUD, the score screen, two pages of the analysis,
       the options, More options, the replay list, credits and the quit question, in both skins at 800 x 600, 1280 x 720 and 1920 x 1080, drawn with the GDI font of the
       renderer and written as PNG into the output directory; the modern skin must meet R3 (a) (no label wider than its window) and (b) (the smallest text 13, 16 and
       24 pixels high); the original's numbers are reported as its comparison. The report is skin-audit.txt.
    What this does not show: whether a person finds the modern skin easier to read and to use, whether it looks good - the acceptance of S3-M5 is a human's.

    The window client and the render tool need a display; without one the check ends with exit code 3 (not run). Logs and pictures go to a directory outside the repository.

.EXAMPLE
    pwsh scripts/check-skins.ps1 [-MatchSeconds 30] [-TickRate 30|60|120]
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$MatchSeconds = 30,
    [int]$TickRate = 0,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/skins'),
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
    Write-Host 'NOT RUN: this machine has no display; the window client and the render tool cannot open a device.'
    exit 3
}
if (-not $NoBuild) {
    dotnet build -c Release src/CnCNew.Client.Stride -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building src/CnCNew.Client.Stride failed.'; exit 1 }
    dotnet build tools/CnCNew.UiRenderTool -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Building tools/CnCNew.UiRenderTool failed.'; exit 1 }
}
$clientDll = Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.dll' | Select-Object -First 1
if (-not $clientDll) { Write-Host 'Release client not found; start without -NoBuild.'; exit 1 }
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$settings = Join-Path $OutDir 'settings'
New-Item -ItemType Directory -Force -Path $settings | Out-Null
$failures = 0
function Fail([string]$why) { Write-Host "   FAILED: $why"; $script:failures++ }

# 1. The person in the window.
$arguments = @($clientDll.FullName, '--content-dir', $ContentDir, '--size', '1280x720', '--mute', '--no-transitions', '--settings-dir', $settings, '--net-dir', (Join-Path $OutDir 'net'),
    '--skirmish-seconds', "$MatchSeconds", '--shell-script', (Join-Path $root 'scripts/shell/skin-person.txt'), '--shell-seconds', "$($TimeoutSeconds - 60)")
if ($TickRate -gt 0) { $arguments += @('--tick-rate', "$TickRate") }
Write-Host "== A person switches to the modern skin in the options and walks the shell in the window (logs in $OutDir)"
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
$file = Join-Path $settings 'settings.json'
if (-not (Test-Path $file)) { Fail 'the client wrote no settings' }
elseif ((Get-Content $file -Raw) -cnotmatch '"skin": "modern"') { Fail 'the settings do not keep the modern skin' }
else { Write-Host '   the settings keep "skin": "modern"' }

# 2. The readability of both skins with the renderer's font.
$render = Join-Path $OutDir 'render'
New-Item -ItemType Directory -Force -Path $render | Out-Null
Write-Host "== The readability check of both skins with the GDI font (pictures and skin-audit.txt in $render)"
$renderLog = Join-Path $OutDir 'render.log'
dotnet run --no-build --project tools/CnCNew.UiRenderTool -- --content-dir $ContentDir --out-dir $render --skins > $renderLog 2>&1
$renderExit = $LASTEXITCODE
$audit = Join-Path $render 'skin-audit.txt'
if (-not (Test-Path $audit)) { Fail "the render tool wrote no audit (exit code $renderExit, see render.log)" }
else {
    $rows = Get-Content $audit | Where-Object { $_ -cmatch '^(original|modern) \S+ \d+x\d+ \d+ \d+ \d+ \d+$' }
    foreach ($skin in 'original', 'modern') {
        $mine = @($rows | Where-Object { $_ -cmatch "^$skin " } | ForEach-Object { , ($_ -split ' ') })
        $wide = ($mine | ForEach-Object { [int]$_[5] } | Measure-Object -Sum).Sum
        $tall = ($mine | ForEach-Object { [int]$_[6] } | Measure-Object -Sum).Sum
        Write-Host ("   {0}: {1} screen pictures, labels too wide {2}, too tall {3}" -f $skin, $mine.Count, $wide, $tall)
        foreach ($size in '800x600', '1280x720', '1920x1080') {
            $smallest = ($mine | Where-Object { $_[2] -eq $size -and [int]$_[3] -gt 0 } | ForEach-Object { [int]$_[4] } | Measure-Object -Minimum).Minimum
            Write-Host "      smallest text at $size`: $smallest px"
        }
    }
    $verdict = Get-Content $audit | Select-Object -Last 1
    if ($renderExit -ne 0 -or $verdict -cnotmatch 'R3 \(a\) and \(b\) met') { Fail "the modern skin misses R3: $verdict (exit code $renderExit)" }
    else { Write-Host "   $verdict" }
}

if ($failures -gt 0) { Write-Host "check-skins: $failures failure(s)."; exit 1 }
Write-Host 'check-skins: passed (what a person judges is not part of it).'
exit 0
