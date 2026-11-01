<#
.SYNOPSIS
  Checks the setup assistant (CnCNew-Launcher.cmd) the way a machine that never saw this project meets it.

.DESCRIPTION
  Without -Full, in seconds and without a download: under Windows PowerShell 5.1, with a PATH of Windows alone, an empty profile and a Steam library made of two folders,
  the assistant must name .NET, PowerShell 7 and Git as missing, name what it makes as to do, and find the game through the library file. That file must stay plain ASCII.

  With -Full, the machine as far as it can be imitated on one that has everything: a fresh clone, a profile of its own (no NuGet cache, no SDK, no settings), a PATH of Windows and
  Git, no winget. The assistant runs unattended, downloads .NET and PowerShell 7 from their makers into the empty profile, makes the data from the real Steam installation,
  builds, starts the game and compares it with the reference values; then the acceptance draws its first page and opens one real scene with the tools it installed.
  What this cannot imitate: a Windows without the Visual C++ runtime, without Git, and another machine's graphics and Steam. Exit code 3 without network or game: NOT RUN.

.EXAMPLE
  pwsh scripts/check-setup.ps1
  pwsh scripts/check-setup.ps1 -Full -Source https://github.com/cpfaffinger/CnCNew.git -OutDir C:\cr60
#>
[CmdletBinding()]
param(
    [switch]$Full,
    [string]$Source,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $PSScriptRoot 'CnCNew-Launcher.ps1'
$powershell = Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe'
$problems = [Collections.Generic.List[string]]::new()
$keep = @{}

function Set-Environment([hashtable]$Values) {
    foreach ($key in $Values.Keys) {
        if (-not $keep.ContainsKey($key)) { $keep[$key] = [Environment]::GetEnvironmentVariable($key) }
        [Environment]::SetEnvironmentVariable($key, $Values[$key])
    }
}
function Reset-Environment { foreach ($key in @($keep.Keys)) { [Environment]::SetEnvironmentVariable($key, $keep[$key]) }; $keep.Clear() }

function Get-EmptyMachine([string]$Root, [string[]]$MoreOnPath) {
    $home2 = Join-Path $Root 'home'
    New-Item -ItemType Directory -Force (Join-Path $home2 'AppData\Local'), (Join-Path $home2 'AppData\Roaming'), (Join-Path $Root 'tmp'), (Join-Path $Root 'program-files') | Out-Null
    @{
        Path = (@("$env:windir\System32", "$env:windir", "$env:windir\System32\Wbem", "$env:windir\System32\WindowsPowerShell\v1.0") + $MoreOnPath) -join ';'
        USERPROFILE = $home2; HOME = $home2; LOCALAPPDATA = (Join-Path $home2 'AppData\Local'); APPDATA = (Join-Path $home2 'AppData\Roaming')
        CNCNEW_SETUP_PROGRAM_FILES = (Join-Path $Root 'program-files'); TEMP = (Join-Path $Root 'tmp'); TMP = (Join-Path $Root 'tmp')
        DOTNET_ROOT = $null; DOTNET_CLI_HOME = $null; NUGET_PACKAGES = $null; PSModulePath = $null; CNCNEW_SETUP_ISOLATED = '1'
    }
}

# ------------------------------------------------------------------------------------------------------------------ the quick part

$bytes = [IO.File]::ReadAllBytes($launcher)
if (@($bytes | Where-Object { $_ -gt 127 }).Count -gt 0) { $problems.Add('the launcher is not plain ASCII: Windows PowerShell 5.1 would read it in the ANSI code page') }

$work = Join-Path ([IO.Path]::GetTempPath()) ('cncnew-setup-check-' + [Guid]::NewGuid().ToString('N'))
try {
    $library = Join-Path $work 'library'
    $steam = Join-Path $work 'steam'
    $app = (Get-Content -LiteralPath (Join-Path $repo 'baselines\steam-master.json') -Raw | ConvertFrom-Json).steamAppId
    New-Item -ItemType Directory -Force (Join-Path $library 'steamapps\common\A Folder Of Its Own'), (Join-Path $steam 'steamapps') | Out-Null
    # A library the file names, with doubled backslashes as Steam writes them, and a folder the app's manifest names.
    [IO.File]::WriteAllText((Join-Path $steam 'steamapps\libraryfolders.vdf'), "`"libraryfolders`"`n{`n`t`"0`"`n`t{`n`t`t`"path`"`t`t`"$($library -replace '\\', '\\')`"`n`t}`n}`n")
    [IO.File]::WriteAllText((Join-Path $library "steamapps\appmanifest_$app.acf"), "`"AppState`"`n{`n`t`"installdir`"`t`t`"A Folder Of Its Own`"`n}`n")
    Set-Environment (Get-EmptyMachine $work @())
    $output = & $powershell -NoProfile -ExecutionPolicy Bypass -File $launcher -CheckOnly -SteamRoots $steam 2>&1 | Out-String
    $exit = $LASTEXITCODE
    Reset-Environment
    if ($exit -ne 0) { $problems.Add("the check ended with exit code ${exit}: $output") }
    foreach ($expected in @('Missing\s+Git', 'Missing\s+\.NET SDK', 'Missing\s+PowerShell 7', 'OK\s+Windows 10', 'To do\s+Game data', 'To do\s+Game program', 'OK\s+Zero Hour.*A Folder Of Its Own')) {
        if ($output -notmatch $expected) { $problems.Add("the output of an empty machine lacks '$expected':`n$output") }
    }
} finally {
    Reset-Environment
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "FAIL $_" -ForegroundColor Red }; exit 1 }
Write-Host 'check-setup: the assistant names what an empty machine lacks and finds the game through its library' -ForegroundColor Green
if (-not $Full) { exit 0 }

# ------------------------------------------------------------------------------------------------------------------ the whole way

if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-setup-full' }
if (-not $Source) { $Source = $repo }
if ($OutDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw 'The run must lie outside the repository.' }
if (Test-Path -LiteralPath $OutDir) { throw "$OutDir exists: name a new directory." }
$git = (Get-Command git -ErrorAction Stop).Source
$clone = Join-Path $OutDir 'CnCNew'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
& $git clone --quiet $Source $clone
if ($LASTEXITCODE -ne 0) { Write-Host "NOT RUN: no clone of $Source"; exit 3 }
Write-Host ("clone: {0:0} s, commit {1}" -f $watch.Elapsed.TotalSeconds, (& $git -C $clone rev-parse --short=10 HEAD))

$report = Join-Path $OutDir 'setup-report.md'
$machine = Get-EmptyMachine $OutDir @((Split-Path -Parent $git))
Set-Environment $machine
try {
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $clone 'scripts\CnCNew-Launcher.ps1') -Unattended -NoWinget -ReportPath $report
    $exit = $LASTEXITCODE
    Write-Host ("the assistant ended after {0:0.0} min with exit code {1}" -f $watch.Elapsed.TotalMinutes, $exit)
    if ($exit -ne 0) { exit 1 }

    # What the assistant left behind must carry the acceptance by itself: the tools it installed, in a process that starts with its PATH.
    $dotnetDir = Join-Path $machine.LOCALAPPDATA 'Microsoft\dotnet'
    $pwshDir = Join-Path $machine.LOCALAPPDATA 'CnCNew\tools\pwsh'
    $env:Path = "$dotnetDir;$pwshDir;$env:Path"
    $pwsh = Join-Path $pwshDir 'pwsh.exe'
    $page = Join-Path $OutDir 'suite-page.png'
    & $pwsh -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $clone 'scripts\acceptance.ps1') -Suite -SuiteScreenshot $page -ResultsPath (Join-Path $OutDir 'results.json') -ReportPath (Join-Path $OutDir 'acceptance.md')
    if (-not (Test-Path -LiteralPath $page)) { $problems.Add('the acceptance drew no page') }
    & $pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $clone 'scripts\acceptance.ps1') -Probe shell -ProbeSeconds 45 -OutDir (Join-Path $OutDir 'probe') -ResultsPath (Join-Path $OutDir 'results.json')
    if ($LASTEXITCODE -ne 0) { $problems.Add('the first scene of the acceptance did not stay open') }
} finally { Reset-Environment }

Write-Host ''
Get-Content -LiteralPath $report | Write-Host
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "FAIL $_" -ForegroundColor Red }; exit 1 }
Write-Host ("check-setup -Full: ok after {0:0.0} min" -f $watch.Elapsed.TotalMinutes) -ForegroundColor Green
exit 0
