<#
.SYNOPSIS
  Restores the developer state on a fresh clone: prerequisites, reference sources, developer content, build.

.DESCRIPTION
  The repository holds only what was written for it. Everything else a developer machine needs is recreated by this script:

    1. prerequisites  git, the .NET SDK named in global.json, PowerShell 7, Python 3 (only for scripts/audit-publish.ps1)
    2. references     scripts/get-references.ps1 clones the four upstream repositories into <repo>\references at the
                      recorded commits (git-ignored, never committed)
    3. content        scripts/dev-content.ps1 exports <repo>\game\content from your own Steam installation of
                      Command & Conquer Generals Zero Hour (git-ignored, never committed; the installation is only read)
    4. build          dotnet build CnCNew.slnx (0 warnings expected)
    5. check          AssetTool verify, the input manifest and the quick set of the reference manifest

  Every step can be skipped; a step that fails is reported and the script goes on, so that a machine without the game still
  gets references and a build. Exit code 1 when a step that ran has failed. Running it again repairs and re-checks.

.EXAMPLE
  git clone https://github.com/cpfaffinger/CnCNew.git
  pwsh CnCNew/scripts/setup-dev.ps1
  pwsh scripts/setup-dev.ps1 -ShallowReferences -SkipCheck
  pwsh scripts/setup-dev.ps1 -GameDir 'D:\Steam\steamapps\common\Command & Conquer Generals - Zero Hour'
#>
[CmdletBinding()]
param(
    # The Steam installation of the game (default: found as install.ps1 finds it).
    [string]$GameDir,

    # Fetch only the recorded commit of each reference (no history).
    [switch]$ShallowReferences,

    [switch]$SkipReferences,
    [switch]$SkipContent,
    [switch]$SkipBuild,
    [switch]$SkipCheck
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$results = [System.Collections.Generic.List[object]]::new()
function Add-StepResult([string]$Step, [string]$State, [string]$Detail) {
    $results.Add([pscustomobject]@{ Step = $Step; State = $State; Detail = $Detail })
}

Write-Host '== 1. Prerequisites' -ForegroundColor Cyan
$missing = @()
foreach ($tool in 'git', 'dotnet') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { $missing += $tool }
}
if ($missing.Count -gt 0) { throw "Missing on PATH: $($missing -join ', '). Install git and the .NET SDK, then run this script again." }
$wanted = (Get-Content -Raw -LiteralPath (Join-Path $repo 'global.json') | ConvertFrom-Json).sdk.version
Push-Location $repo
try { $sdk = (dotnet --version) 2>$null; $sdkExit = $LASTEXITCODE } finally { Pop-Location }
if ($sdkExit -ne 0) { throw "The .NET SDK required by global.json ($wanted, roll-forward latestFeature) is not installed." }
Write-Host "   git $((git --version) -replace 'git version ', ''), .NET SDK $sdk (global.json: $wanted), PowerShell $($PSVersionTable.PSVersion)"
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or later is required (pwsh).' }
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    Write-Warning 'Python 3 was not found: only scripts/audit-publish.ps1 (the check before a push) needs it.'
}
Add-StepResult 'prerequisites' 'ok' "SDK $sdk"

if ($SkipReferences) { Add-StepResult 'references' 'skipped' '' }
else {
    Write-Host '== 2. Reference sources' -ForegroundColor Cyan
    $arguments = @{}
    if ($ShallowReferences) { $arguments.Shallow = $true }
    & (Join-Path $PSScriptRoot 'get-references.ps1') @arguments
    if ($LASTEXITCODE -eq 0) { Add-StepResult 'references' 'ok' (Join-Path $repo 'references') }
    else { Add-StepResult 'references' 'FAILED' 'see the warnings above; run scripts/get-references.ps1 again' }
}

$content = Join-Path $repo 'game\content'
if ($SkipContent) { Add-StepResult 'content' 'skipped' '' }
else {
    Write-Host '== 3. Developer content from the Steam installation' -ForegroundColor Cyan
    $arguments = @{}
    if ($GameDir) { $arguments.GameDir = $GameDir }
    try {
        & (Join-Path $PSScriptRoot 'dev-content.ps1') @arguments
        if ($LASTEXITCODE -eq 0) { Add-StepResult 'content' 'ok' $content }
        else { Add-StepResult 'content' 'FAILED' 'the installation was not found or is not the master version (docs/INSTALLATION.md); pass -GameDir' }
    }
    catch {
        Write-Warning $_.Exception.Message
        Add-StepResult 'content' 'FAILED' 'the installation was not found or is not the master version (docs/INSTALLATION.md); pass -GameDir'
    }
}

if ($SkipBuild) { Add-StepResult 'build' 'skipped' '' }
else {
    Write-Host '== 4. Build' -ForegroundColor Cyan
    dotnet build (Join-Path $repo 'CnCNew.slnx') -v q
    if ($LASTEXITCODE -eq 0) { Add-StepResult 'build' 'ok' 'CnCNew.slnx' } else { Add-StepResult 'build' 'FAILED' 'dotnet build CnCNew.slnx' }
}

$haveContent = Test-Path -LiteralPath (Join-Path $content 'content-manifest.json')
if ($SkipCheck) { Add-StepResult 'check' 'skipped' '' }
elseif (-not $haveContent) { Add-StepResult 'check' 'skipped' 'no game\content' }
else {
    Write-Host '== 5. Check against the baselines' -ForegroundColor Cyan
    Push-Location $repo
    try {
        dotnet run --project tools/CnCNew.AssetTool -- verify game/content | Select-Object -Last 2
        $verify = $LASTEXITCODE
        dotnet build -c Release tools/CnCNew.SimTool -v q | Select-Object -Last 2
        dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- input-manifest check game/content baselines/input-manifest.json | Select-Object -Last 2
        $inputs = $LASTEXITCODE
        dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check game/content baselines/reference-manifest.json --set quick | Select-Object -Last 2
        $reference = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($verify -eq 0 -and $inputs -eq 0 -and $reference -eq 0) { Add-StepResult 'check' 'ok' 'content verified, input manifest and quick reference set identical' }
    else { Add-StepResult 'check' 'FAILED' "AssetTool verify $verify, input manifest $inputs, reference manifest (quick) $reference" }
}

Write-Host '== Result' -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host
Write-Host 'Next: README.md (quick start), CLAUDE.md (working rules and all commands), docs/README.md (index of the documentation).'
exit (@($results | Where-Object State -eq 'FAILED').Count -gt 0 ? 1 : 0)
