<#
.SYNOPSIS
    The clean-room build (docs/BUILDING.md, "Clean room"): the committed source, in a fresh directory, with empty package and tool caches, turned into a
    built solution, a fresh content import from the master installation, checks against the yardsticks and the packages - and a report of all of it.

.DESCRIPTION
    Steps, every one timed and written to <OutDir>/clean-room-report.md:
      1. prerequisites: git, the .NET SDK global.json names, pwsh, python; nuget.org reachable (without it the run is NOT RUN, exit code 3);
      2. a clone of the commit (-Commit, default HEAD of this repository) into <OutDir>/src - what is not committed is not in it;
      3. empty caches for this run only: NUGET_PACKAGES, NUGET_HTTP_CACHE_PATH, DOTNET_CLI_HOME under <OutDir>; nothing of this machine's caches is used;
      4. dotnet build CnCNew.slnx and tools/CnCNew.UiRenderTool, Release (warnings are errors: 0 warnings or the step fails);
      5. a fresh import: the clone's patcher exports the Steam installation (found by itself, -GameDir to name it) into <OutDir>/src/game - the developer layout -
         and verifies the export against the master (no Steam installation: NOT RUN);
      6. checks against the fresh content: the input manifest, the quick set of the reference manifest, Simulation.Tests and Data.Tests with real data;
      7. the packages: scripts/package-client.ps1 and scripts/package-server.ps1 (win-x64, linux-x64) from the clone, scripts/check-separation.ps1 over each;
      8. reproducibility: the files of the client package are compared with a package this repository's tree built from the same commit (-Compare DIR, optional).
    The full test matrix is not run here (tier 4 of scripts/test.ps1 in the working tree is that); -Tier gate runs tier 3 inside the clone in addition.
    Exit code 0 when every step passed, 1 when one failed, 3 when the environment lacks the network or the game.

.EXAMPLE
    pwsh scripts/clean-room.ps1
    pwsh scripts/clean-room.ps1 -OutDir D:\cncnew-clean -Tier gate
#>
param(
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-clean-room'),
    [string]$Commit = 'HEAD',
    [string]$GameDir = 'auto',
    [ValidateSet('none', 'gate')][string]$Tier = 'none',
    [string]$Compare
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$commitId = (git -C $repo rev-parse $Commit).Trim()
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$report = New-Object System.Collections.Generic.List[string]
$results = New-Object System.Collections.Generic.List[object]
$failed = 0
$notRun = 0

function Step([string]$name, [scriptblock]$body) {
    Write-Host "== $name" -ForegroundColor Cyan
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $state = 'ok'
    $detail = ''
    try { $detail = & $body } catch { $state = 'FAILED'; $detail = $_.Exception.Message }
    if ($detail -is [array]) { $detail = $detail -join ' ' }
    if ($detail -like 'NOT RUN*') { $state = 'NOT RUN' }
    $results.Add([pscustomobject]@{ Step = $name; State = $state; Seconds = [int]$watch.Elapsed.TotalSeconds; Detail = "$detail" })
    if ($state -eq 'FAILED') { $script:failed++ }
    if ($state -eq 'NOT RUN') { $script:notRun++ }
    Write-Host "   $state in $([int]$watch.Elapsed.TotalSeconds) s  $detail"
    return $state
}
function Run([string]$log, [string[]]$command) {
    $file = Join-Path $OutDir $log
    & $command[0] $command[1..($command.Count - 1)] *> $file
    if ($LASTEXITCODE -ne 0) { throw "$($command -join ' ') ended with exit code $LASTEXITCODE (log: $file)" }
    return $file
}

$src = Join-Path $OutDir 'src'
$global = Get-Content -Raw (Join-Path $repo 'global.json') | ConvertFrom-Json
$wantedSdk = $global.sdk.version

$state = Step 'Prerequisites' {
    $versions = @("git $((git --version) -replace 'git version ', '')", "dotnet $((dotnet --version).Trim())", "pwsh $($PSVersionTable.PSVersion)", "$((python --version 2>&1) -join '')")
    try { $null = Invoke-WebRequest -Uri 'https://api.nuget.org/v3/index.json' -UseBasicParsing -TimeoutSec 20 } catch { return "NOT RUN: nuget.org is not reachable ($($_.Exception.Message))" }
    "$($versions -join ', '); global.json asks for SDK $wantedSdk ($($global.sdk.rollForward))"
}
if ($state -eq 'NOT RUN') { Write-Host 'clean-room: NOT RUN - no network'; exit 3 }

Step 'Clone' {
    git clone --quiet --no-hardlinks $repo $src
    if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
    git -C $src checkout --quiet $commitId
    if ($LASTEXITCODE -ne 0) { throw "checkout of $commitId failed" }
    $files = @(git -C $src ls-files).Count
    "commit $commitId, $files files; nothing of game/, .local/, references/ (not committed)"
} | Out-Null

# Empty caches of this run: every package is fetched again from nuget.org, every tool starts fresh.
$env:NUGET_PACKAGES = Join-Path $OutDir 'cache/nuget-packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $OutDir 'cache/nuget-http'
$env:DOTNET_CLI_HOME = Join-Path $OutDir 'cache/dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $src
try {
    Step 'Build' {
        Run 'build-solution.log' @('dotnet', 'build', 'CnCNew.slnx', '-c', 'Release', '--nologo') | Out-Null
        Run 'build-render-tool.log' @('dotnet', 'build', 'tools/CnCNew.UiRenderTool', '-c', 'Release', '--nologo') | Out-Null
        $warnings = @(Select-String -Path (Join-Path $OutDir 'build-solution.log'), (Join-Path $OutDir 'build-render-tool.log') -Pattern ' warning [A-Z]+[0-9]+' ).Count
        if ($warnings -gt 0) { throw "$warnings warning line(s)" }
        $packages = @(Get-ChildItem $env:NUGET_PACKAGES -Directory).Count
        "0 warnings; $packages packages fetched into the empty cache"
    } | Out-Null

    $import = Step 'Fresh import' {
        $log = Join-Path $OutDir 'import.log'
        & dotnet run -c Release --project tools/CnCNew.AssetTool -- install $GameDir (Join-Path $src 'game') *> $log
        if ($LASTEXITCODE -ne 0) {
            if ((Get-Content -Raw $log) -match 'No Steam installation') { return 'NOT RUN: no Steam installation of the game' }
            throw "the import ended with exit code $LASTEXITCODE (log: $log)"
        }
        Run 'check-export.log' @('dotnet', 'run', '-c', 'Release', '--no-build', '--project', 'tools/CnCNew.AssetTool', '--', 'check-export', 'game/content') | Out-Null
        $record = Get-Content -Raw 'game/install.json' | ConvertFrom-Json
        "$($record.runtimeFileCount) runtime files, content manifest $($record.contentManifestSha256) - the known export"
    }

    if ($import -eq 'ok') {
        Step 'Input manifest' {
            Run 'input-manifest.log' @('dotnet', 'run', '-c', 'Release', '--no-build', '--project', 'tools/CnCNew.SimTool', '--', 'input-manifest', 'check', 'game/content', 'baselines/input-manifest.json') | Out-Null
            'identical'
        } | Out-Null
        Step 'Reference manifest (quick set)' {
            Run 'reference-quick.log' @('dotnet', 'run', '-c', 'Release', '--no-build', '--project', 'tools/CnCNew.SimTool', '--', 'reference-manifest', 'check', 'game/content', 'baselines/reference-manifest.json', '--set', 'quick') | Out-Null
            (Get-Content (Join-Path $OutDir 'reference-quick.log') | Select-Object -Last 1)
        } | Out-Null
        Step 'Simulation.Tests (real data)' {
            Run 'simulation-tests.log' @('dotnet', 'run', '-c', 'Release', '--no-build', '--project', 'tests/CnCNew.Simulation.Tests', '--', '--content-dir', 'game/content') | Out-Null
            (Get-Content (Join-Path $OutDir 'simulation-tests.log') | Select-Object -Last 1)
        } | Out-Null
        Step 'Data.Tests (real data)' {
            Run 'data-tests.log' @('dotnet', 'run', '-c', 'Release', '--no-build', '--project', 'tests/CnCNew.Data.Tests', '--', '--content-dir', 'game/content') | Out-Null
            (Get-Content (Join-Path $OutDir 'data-tests.log') | Select-Object -Last 1)
        } | Out-Null
        if ($Tier -eq 'gate') {
            Step 'Tier 3 (gate) in the clone' {
                Run 'tier-gate.log' @('pwsh', '-NoLogo', '-NoProfile', '-File', 'scripts/test.ps1', '-Tier', 'gate', '-Since', "$commitId~1") | Out-Null
                'ok'
            } | Out-Null
        }
    }

    Step 'Packages' {
        Run 'package-client.log' @('pwsh', '-NoLogo', '-NoProfile', '-File', 'scripts/package-client.ps1', '-OutDir', (Join-Path $OutDir 'packages/client')) | Out-Null
        Run 'package-server.log' @('pwsh', '-NoLogo', '-NoProfile', '-File', 'scripts/package-server.ps1', '-OutDir', (Join-Path $OutDir 'packages/server')) | Out-Null
        $sizes = foreach ($directory in (Join-Path $OutDir 'packages/client/cncnew-win-x64'), (Join-Path $OutDir 'packages/server/win-x64'), (Join-Path $OutDir 'packages/server/linux-x64')) {
            Run "separation-$([IO.Path]::GetFileName($directory)).log" @('pwsh', '-NoLogo', '-NoProfile', '-File', 'scripts/check-separation.ps1', '-Directory', $directory) | Out-Null
            $files = @(Get-ChildItem $directory -Recurse -File)
            "$([IO.Path]::GetFileName($directory)) $($files.Count) files $([math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)) MB"
        }
        "$($sizes -join '; '); no original data in any"
    } | Out-Null

    if ($Compare) {
        Step 'Reproducibility' {
            $clean = Join-Path $OutDir 'packages/client/cncnew-win-x64'
            $same = 0; $differ = New-Object System.Collections.Generic.List[string]
            foreach ($file in Get-ChildItem $clean -Recurse -File) {
                $relative = [IO.Path]::GetRelativePath($clean, $file.FullName)
                $other = Join-Path $Compare $relative
                if ((Test-Path $other) -and (Get-FileHash $file.FullName).Hash -eq (Get-FileHash $other).Hash) { $same++ } else { $differ.Add($relative) }
            }
            "$same files identical, $($differ.Count) differ: $(($differ | Select-Object -First 12) -join ', ')$(if ($differ.Count -gt 12) { ' ...' })"
        } | Out-Null
    }
}
finally { Pop-Location }

$report.Add("# Clean-room build of $commitId")
$report.Add('')
$report.Add("Made by scripts/clean-room.ps1 on $([Environment]::MachineName -replace '.', '*') ($([Runtime.InteropServices.RuntimeInformation]::OSDescription)), $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC.")
$report.Add('')
$report.Add('| Step | State | Seconds | Detail |')
$report.Add('| --- | --- | ---: | --- |')
foreach ($result in $results) { $report.Add("| $($result.Step) | $($result.State) | $($result.Seconds) | $($result.Detail -replace '\|', '/') |") }
[IO.File]::WriteAllLines((Join-Path $OutDir 'clean-room-report.md'), $report)
Write-Host "Report: $(Join-Path $OutDir 'clean-room-report.md')"
if ($failed -gt 0) { Write-Host "clean-room: $failed step(s) failed"; exit 1 }
if ($notRun -gt 0) { Write-Host "clean-room: $notRun step(s) NOT RUN"; exit 3 }
Write-Host 'clean-room: ok'
exit 0
