<#
.SYNOPSIS
    The self-contained Windows package of the game (scripts/package-client.ps1): what it holds, that it holds no original data, and that its two programs run.

.DESCRIPTION
    Builds the package into a directory of this run, then
      - its layout: bin\CnCNew.Client.Stride.exe, patcher\CnCNew.AssetTool.exe, Install-CnCNew.cmd, Start-CnCNew.cmd, LICENSE.md, NOTICE.md, THIRD-PARTY.md,
        README-FIRST.txt, licences\ - and both programs self-contained (their own .NET runtime beside them);
      - scripts/check-separation.ps1 over the whole package: no file of the game, no file of a game data format, every file classified;
      - the packaged patcher finds the Steam installation by itself ('auto', as Install-CnCNew.cmd calls it) and checks it against the master - it only reads;
      - the packaged client runs a smoke test against game/content (a model, then a match of 300 frames) and ends with exit code 0.
    What it does not do: the export into the package (3 GB; the patcher's export is the same code the installation tests and scripts/dev-content.ps1 use), and a
    machine without .NET (this machine has the SDK; that the runtime beside the programs is the one they load is shown by the runtime files the package holds,
    not by an empty machine).
    Exit code 0 when everything holds, 1 when something does not, 3 when the environment lacks what a part needs (no Steam installation, no game/content) -
    then the part is NOT RUN, never passed.

.EXAMPLE
    pwsh scripts/check-client-package.ps1
#>
param(
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/client-package'),
    [string]$ContentDir = 'game/content'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$failed = 0
$notRun = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }
function Pass([string]$text) { Write-Host "ok    $text" }

pwsh -NoLogo -NoProfile -File scripts/package-client.ps1 -OutDir $OutDir
if ($LASTEXITCODE -ne 0) { Write-Host 'Building the package failed.'; exit 1 }
$package = Join-Path ([IO.Path]::GetFullPath($OutDir)) 'cncnew-win-x64'

foreach ($needed in 'bin/CnCNew.Client.Stride.exe', 'patcher/CnCNew.AssetTool.exe', 'Install-CnCNew.cmd', 'Start-CnCNew.cmd', 'LICENSE.md', 'NOTICE.md', 'THIRD-PARTY.md', 'README-FIRST.txt',
                    'bin/coreclr.dll', 'patcher/coreclr.dll', 'bin/hostfxr.dll') {
    if (-not (Test-Path (Join-Path $package $needed))) { Fail "the package lacks $needed" }
}
if (-not (Get-ChildItem (Join-Path $package 'licences') -Recurse -File -ErrorAction SilentlyContinue)) { Fail 'the package carries no licence files of its packages' }
foreach ($program in 'bin', 'patcher') {
    $config = Get-ChildItem (Join-Path $package $program) -Filter '*.runtimeconfig.json' | Select-Object -First 1
    if (-not $config -or -not ((Get-Content -Raw $config.FullName) -match 'includedFrameworks')) { Fail "$program is not self-contained (its runtimeconfig names no included framework)" }
}
if ($failed -eq 0) { Pass 'layout: client, patcher, the two scripts, licence, notice, third-party list and licence files; both programs self-contained' }

pwsh -NoLogo -NoProfile -File scripts/check-separation.ps1 -Directory $package -List (Join-Path ([IO.Path]::GetFullPath($OutDir)) 'separation.tsv')
if ($LASTEXITCODE -ne 0) { Fail 'the separation check found original data or a game data format in the package' } else { Pass 'separation: no original data' }

& (Join-Path $package 'patcher/CnCNew.AssetTool.exe') check-install auto *> (Join-Path $OutDir 'patcher.log')
$code = $LASTEXITCODE
if ($code -eq 0) { Pass 'the packaged patcher found the Steam installation and found it to be the master' }
elseif ((Get-Content -Raw (Join-Path $OutDir 'patcher.log')) -match 'No Steam installation') { Write-Host 'NOT RUN: no Steam installation of the game on this machine'; $notRun++ }
else { Fail "the packaged patcher's check ended with exit code $code (log: $OutDir/patcher.log)" }

if (-not (Test-Path (Join-Path $ContentDir 'content-manifest.json'))) { Write-Host "NOT RUN: no runtime content at $ContentDir for the client's smoke test"; $notRun++ }
else {
    $content = (Resolve-Path $ContentDir).Path
    $exe = Join-Path $package 'bin/CnCNew.Client.Stride.exe'
    $netDir = Join-Path $OutDir 'net'
    foreach ($run in @(
        @{ Name = 'model'; Args = @('--smoke-test', '--content-dir', $content, '--model-path', 'Art/W3D/ABArFrcCmd.W3D', '--net-dir', $netDir) },
        @{ Name = 'match'; Args = @('--smoke-test', '--content-dir', $content, '--match', 'Maps/Alpine Assault/Alpine Assault.map', '--frames', '300', '--net-dir', $netDir) })) {
        $process = Start-Process -FilePath $exe -ArgumentList ($run.Args | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -NoNewWindow -PassThru -Wait `
            -RedirectStandardOutput (Join-Path $OutDir "client-$($run.Name).log") -RedirectStandardError (Join-Path $OutDir "client-$($run.Name).err")
        if ($process.ExitCode -ne 0) { Fail "the packaged client's smoke test ($($run.Name)) ended with exit code $($process.ExitCode) (log: $OutDir/client-$($run.Name).log)" }
        else { Pass "the packaged client ran its smoke test ($($run.Name))" }
    }
}

if ($failed -gt 0) { Write-Host "check-client-package: $failed problem(s); logs in $OutDir"; exit 1 }
if ($notRun -gt 0) { Write-Host "check-client-package: $notRun part(s) NOT RUN for lack of their environment"; exit 3 }
Write-Host "check-client-package: ok (logs in $OutDir)"
exit 0
