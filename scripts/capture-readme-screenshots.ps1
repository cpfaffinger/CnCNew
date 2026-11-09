<#
.SYNOPSIS
    Makes the pictures the public README can show (the modern skin), into a directory OUTSIDE the repository.

.DESCRIPTION
    THE PICTURES MUST NOT ENTER THE REPOSITORY. They show the original game's art (menus, command bar, cameos, units, terrain); CLAUDE.md "Git rules" forbids
    committing anything exported from the game, and whether to publish such pictures anywhere is the project owner's legal decision, not a build step.
    docs/screenshots/ is git-ignored for the case that someone copies the pictures there to look at the README with them; the README holds the image links only
    inside an HTML comment (README.md, "Screenshots").

    Produces <OutDir>/<name>.png for the names the README's comment block uses:
        main-menu, skirmish-setup, options, match-hud, replay-browser, match-analysis, observer
    plus extras (skirmish-setup-options, hud-units, score, ai-options).
    How:
      - the shell screens, the HUD frame, the replay list and the analysis: tools/CnCNew.UiRenderTool --skins draws every screen of the modern skin through the
        client's own renderer (GDI font) at 800x600, 1280x720 and 1920x1080 after a short skirmish; the 1920x1080 pictures are copied under the README's names.
      - match-hud, observer: the Stride client's smoke test with --capture (a real match on Alpine Assault, the modern skin, the world and the HUD in one frame).
    Needs the game data (scripts/dev-content.ps1 or an installation's content directory) and a display; without a display it ends with exit code 3 (not run).
    Pictures from a run depend on the machine's graphics and the match's moment; look at each before you use it.

.EXAMPLE
    pwsh scripts/capture-readme-screenshots.ps1
    pwsh scripts/capture-readme-screenshots.ps1 -OutDir D:\shots -NoBuild -Size 1920x1080
#>
param(
    [string]$ContentDir = 'game/content',
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-readme-screenshots'),
    [string]$Size = '1920x1080',
    [int]$TickRate = 0,
    [switch]$NoBuild,
    # Optional prebuilt programs (the Release build of the client and of the render tool); otherwise they are built from this checkout.
    [string]$ClientExe,
    [string]$UiRenderExe,
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# The output must be outside the repository (and so can never be staged by accident).
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (($OutDir + [IO.Path]::DirectorySeparatorChar).StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "Refused: $OutDir lies inside the repository. The pictures show the game's art and never belong in it; choose a directory outside."
    exit 1
}
if (-not (Test-Path -LiteralPath (Join-Path $ContentDir 'content-manifest.json'))) {
    Write-Host "No runtime content at $ContentDir - this needs the game data (scripts/dev-content.ps1)."
    exit 2
}
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
Add-Type -AssemblyName System.Windows.Forms
if ([System.Windows.Forms.Screen]::AllScreens.Count -eq 0) {
    Write-Host 'NOT RUN: this machine has no display; the client and the render tool cannot open a device.'
    exit 3
}
if ($Size -notmatch '^(\d+)x(\d+)$') { Write-Host "Size must be WIDTHxHEIGHT, not '$Size'."; exit 1 }
$sizeWidth = [int]$Matches[1]; $sizeHeight = [int]$Matches[2]

if (-not $NoBuild) {
    if (-not $ClientExe) {
        dotnet build -c Release src/CnCNew.Client.Stride -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host 'Building src/CnCNew.Client.Stride failed.'; exit 1 }
    }
    if (-not $UiRenderExe) {
        dotnet build -c Release tools/CnCNew.UiRenderTool -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host 'Building tools/CnCNew.UiRenderTool failed.'; exit 1 }
    }
}
if (-not $ClientExe) { $ClientExe = (Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.exe' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
if (-not $UiRenderExe) { $UiRenderExe = (Get-ChildItem 'tools/CnCNew.UiRenderTool/bin/Release' -Recurse -Filter 'CnCNew.UiRenderTool.exe' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
if (-not $ClientExe -or -not (Test-Path -LiteralPath $ClientExe)) { Write-Host 'The Release client was not found; run without -NoBuild or pass -ClientExe.'; exit 1 }
if (-not $UiRenderExe -or -not (Test-Path -LiteralPath $UiRenderExe)) { Write-Host 'The Release render tool was not found; run without -NoBuild or pass -UiRenderExe.'; exit 1 }

$work = Join-Path $OutDir '_work'
if (Test-Path -LiteralPath $work) { Remove-Item -Recurse -Force -LiteralPath $work }
New-Item -ItemType Directory -Force -Path $work | Out-Null
$failed = New-Object System.Collections.Generic.List[string]

function Invoke-Timed([string]$exe, [string[]]$arguments, [string]$log) {
    $quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath $exe -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput "$log.out" -RedirectStandardError "$log.err"
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        return -1
    }
    $process.WaitForExit()
    return $process.ExitCode
}

# --- 1. the shell screens, the HUD, the replay list and the analysis of the modern skin
Write-Host '== The modern skin through the render tool (a short skirmish is played for the HUD, the score and the analysis)'
$renderOut = Join-Path $work 'ui'
New-Item -ItemType Directory -Force -Path $renderOut | Out-Null
$renderArguments = @('--content-dir', $ContentDir, '--out-dir', $renderOut, '--skins')
if ($TickRate -gt 0) { $renderArguments += @('--tick-rate', "$TickRate") }
$code = Invoke-Timed $UiRenderExe $renderArguments (Join-Path $work 'render')
if ($code -ne 0) { $failed.Add("render tool exit code $code (see $work\render.err)") }
$map = [ordered]@{
    'main-menu'               = 'main'
    'skirmish-setup'          = 'skirmish-setup-map'
    'skirmish-setup-options'  = 'skirmish-setup-options-on'
    'ai-options'              = 'ai-options'
    'options'                 = 'more-options'
    'replay-browser'          = 'replays'
    'match-analysis'          = 'analysis-1'
    'hud-units'               = 'hud-units'
    'score'                   = 'score'
}
$extras = @("skirmish-setup-options", "ai-options", "hud-units", "score")
foreach ($name in $map.Keys) {
    $source = Join-Path $renderOut ("skin-modern-{0}-{1}x{2}.png" -f $map[$name], $sizeWidth, $sizeHeight)
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $OutDir "$name.png") -Force }
    elseif ($extras -contains $name) { Write-Host ("  note: no $name.png (an extra; this render tool build does not draw it)") }
    else { $failed.Add("no $name.png (the render tool made no $(Split-Path -Leaf $source); sizes it draws: 800x600, 1280x720, 1920x1080)") }
}

# --- 2. a real match, the world and the HUD in one frame: the player's view and the observer's
$alpine = 'Maps/Alpine Assault/Alpine Assault.map'
$common = @('--smoke-test', '--content-dir', $ContentDir, '--size', $Size, '--mute', '--no-intro', '--no-shell-map', '--skin', 'modern', '--seed', '7',
    '--settings-dir', (Join-Path $work 'settings'), '--net-dir', (Join-Path $work 'net'), '--no-log')
if ($TickRate -gt 0) { $common += @('--tick-rate', "$TickRate") }
$scenes = @(
    @{ Name = 'match-hud'; Arguments = @('--match', $alpine, '--faction', 'FactionAmerica', '--opponent', 'FactionChina', '--autoplay', '--select-own', '--frames', '4500') },
    @{ Name = 'observer'; Arguments = @('--match', $alpine, '--faction', 'FactionAmerica', '--opponent', 'FactionChina', '--spectate', '--frames', '6000') }
)
foreach ($scene in $scenes) {
    Write-Host "== $($scene.Name) (client smoke test with --capture)"
    $target = Join-Path $OutDir "$($scene.Name).png"
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    $code = Invoke-Timed $ClientExe ($common + $scene.Arguments + @('--capture', $target)) (Join-Path $work $scene.Name)
    if ($code -ne 0 -or -not (Test-Path -LiteralPath $target)) { $failed.Add("$($scene.Name): client exit code $code, picture " + $(if (Test-Path -LiteralPath $target) { 'made' } else { 'missing' }) + " (see $work\$($scene.Name).err)") }
}

Write-Host ''
Write-Host "Pictures in ${OutDir}:"
Get-ChildItem -LiteralPath $OutDir -Filter '*.png' | ForEach-Object { Write-Host ("  {0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB)) }
Write-Host 'They show the original game''s art: do not commit them. Look at each one before you use it anywhere.'
if ($failed.Count -gt 0) {
    Write-Host ''
    Write-Host 'Problems:'
    $failed | ForEach-Object { Write-Host "  $_" }
    exit 1
}
exit 0
