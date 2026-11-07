<#
.SYNOPSIS
    The in-game menu of a skirmish in the window (docs/GAME-SHELL.md "The in-game menu"): a scripted person starts a skirmish, opens the menu with Escape and presses
    Options, Save/Load (saves, then loads the file back into a running game), Restart (No, then Yes) and Exit (No, then Yes).

.DESCRIPTION
    Starts the Release window client once with --shell-script scripts/shell/ingame-menu-person.txt and a settings directory of this run (never the user's). The script is a
    list of conditions the shell shows, so it is the same walk Game.Tests (InGameMenuTests) makes without a window; here the real client wires the buttons to the shell's
    screens and handles what the shell asks of it (end the game, begin it again, load a save).
    What this does not show: how the options, the save list and the questions look over the game - that is a person's to judge.
    The window client needs a display; without one the check ends with exit code 3 (not run). Logs go to a directory outside the repository.

.EXAMPLE
    pwsh scripts/check-ingame-menu.ps1
#>
param(
    [string]$ContentDir = 'game/content',
    [int]$TickRate = 0,
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/ingame-menu'),
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
    Write-Host 'NOT RUN: this machine has no display; the window client cannot open a device.'
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
$settings = Join-Path $OutDir 'settings'
New-Item -ItemType Directory -Force -Path $settings | Out-Null

$arguments = @($clientDll.FullName, '--content-dir', $ContentDir, '--size', '1280x720', '--mute', '--no-transitions', '--no-intro', '--settings-dir', $settings, '--net-dir', (Join-Path $OutDir 'net'),
    '--skirmish-seconds', '60', '--shell-script', (Join-Path $root 'scripts/shell/ingame-menu-person.txt'), '--shell-seconds', "$($TimeoutSeconds - 60)")
if ($TickRate -gt 0) { $arguments += @('--tick-rate', "$TickRate") }
Write-Host "== A person at the in-game menu of a skirmish in the window (logs in $OutDir)"
$log = Join-Path $OutDir 'client.log'
$process = Start-Process -FilePath 'dotnet' -ArgumentList ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -NoNewWindow -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError (Join-Path $OutDir 'client.err')
$failed = $false
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Write-Host "   FAILED: the client did not end within $TimeoutSeconds s"
    $failed = $true
}
else {
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { Write-Host "   FAILED: the client ended with exit code $($process.ExitCode)"; $failed = $true }
}
$lines = if (Test-Path $log) { Get-Content $log } else { @() }
$summary = $lines | Where-Object { $_ -cmatch '^shell-script summary:' } | Select-Object -Last 1
if (-not $summary) { Write-Host '   FAILED: no summary of the script (see client.log, client.err)'; $failed = $true }
elseif ($summary -cnotmatch 'failed=0 finished=1') {
    Write-Host "   FAILED: $summary"
    $lines | Where-Object { $_ -cmatch 'shell-script: FAILED' } | ForEach-Object { Write-Host "      $_" }
    $failed = $true
}
else { Write-Host "   $summary" }
if ($failed) { Write-Host 'check-ingame-menu: failed.'; exit 1 }
Write-Host 'check-ingame-menu: passed (what a person judges is not part of it).'
exit 0
