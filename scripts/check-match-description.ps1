<#
.SYNOPSIS
  Checks that a match description builds the same match in separate processes (docs/SIMULATION-CORE.md section 45.2).

.DESCRIPTION
  One process writes the canonical bytes of a match with a random faction and random start positions (SimTool match-describe); two further processes each
  read those bytes, build tick 0, play some ticks and print the resolved slots and the session hashes (SimTool match-start). The outputs of the two processes
  must be equal line for line. A third process is given the same bytes with a tick rate of its own on the command line: the description names its rate,
  so the output must still be the same.
  Exit code 0 when the outputs agree, 1 otherwise.

.EXAMPLE
  .\scripts\check-match-description.ps1
  .\scripts\check-match-description.ps1 -NoBuild -Map "Maps/Alpine Assault/Alpine Assault.map"
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [string]$Map = 'Maps/Alpine Assault/Alpine Assault.map',
    [int]$Ticks = 1800,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path

$project = Join-Path $repo 'tools\CnCNew.SimTool\CnCNew.SimTool.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'The build of CnCNew.SimTool failed.' }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('cncnew-match-description-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$file = Join-Path $work 'match.bin'

dotnet run -c Release --no-build --project $project -- match-describe $ContentDir --map $Map --slots 'computer:random:random;computer:FactionChina:random' --seed 24 --minutes 5 --out $file
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: match-describe failed.'; exit 1 }

$outputs = @()
foreach ($run in 'first process', 'second process', 'third process (started with --tick-rate 30)') {
    $extra = @()
    if ($run -like 'third*') { $extra = @('--tick-rate', '30') }
    $lines = dotnet run -c Release --no-build --project $project -- match-start $ContentDir $file --ticks $Ticks @extra
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: match-start failed in the $run."; exit 1 }
    $outputs += , @($lines)
    Write-Host "--- $run"
    $lines | ForEach-Object { Write-Host "  $_" }
}

$failed = $false
for ($index = 1; $index -lt $outputs.Count; $index++) {
    if (Compare-Object $outputs[0] $outputs[$index] -CaseSensitive) {
        Write-Host "FAIL: process $($index + 1) built another match than process 1 from the same bytes."
        $failed = $true
    }
}
if ($outputs[0].Count -lt 4) { Write-Host 'FAIL: the output of match-start is incomplete.'; $failed = $true }
if ($failed) { exit 1 }
Write-Host "All match description checks passed: three processes built the same match from $((Get-Item $file).Length) bytes."
exit 0
