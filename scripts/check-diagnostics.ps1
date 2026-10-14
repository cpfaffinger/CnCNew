<#
.SYNOPSIS
    The diagnostics of the client and of the dedicated server (docs/TROUBLESHOOTING.md): a failure leaves a crash report, a run leaves a log, and a support
    bundle holds what helps and nothing it must not.

.DESCRIPTION
    Runs on this machine, every file in a directory of this run (never the user's own):
      - the Release client with --crash-test --net-dir <run>: it fails on purpose before any window; the check wants a crash report under crashes/ that names the
        exception, the build and the arguments, and the log of the run under logs/ (the report carries its last lines);
      - the client with --support-bundle: into the same directory a player key (player-key.pem), a saved game (*.sav) and a desync package were put beforehand;
        the bundle must hold the crash report, the log and the desync package, ABOUT.txt and CONTENTS.txt - and neither the key nor the saved game;
      - the dedicated server with --crash-test --log-dir <run>: exit code 70 and a crash report beside the logs, with the configuration;
      - the server with --support-bundle: the crash report and the configuration, not the key file and not the ban file (it holds addresses);
      - every file of both bundles is compared by SHA-256 with every file of the master installation (baselines/steam-master.json): none may be a file of the game.
    Exit code 0 when everything holds, 1 when something does not. Needs no display and no game data.

.EXAMPLE
    pwsh scripts/check-diagnostics.ps1
#>
param(
    [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) 'cncnew-test/diagnostics'),
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $NoBuild) {
    foreach ($project in 'tools/CnCNew.DedicatedServer', 'src/CnCNew.Client.Stride') {
        dotnet build -c Release $project -v q --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "Building $project failed."; exit 1 }
    }
}
$clientDll = Get-ChildItem 'src/CnCNew.Client.Stride/bin/Release' -Recurse -Filter 'CnCNew.Client.Stride.dll' | Select-Object -First 1
if (-not $clientDll) { Write-Host 'Release client not found; start without -NoBuild.'; exit 1 }
$serverDll = 'tools/CnCNew.DedicatedServer/bin/Release/net10.0/CnCNew.DedicatedServer.dll'
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
$failed = 0
function Fail([string]$text) { Write-Host "FAIL: $text"; $script:failed++ }
function Pass([string]$text) { Write-Host "ok    $text" }
function Entries([string]$zip) {
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try { @($archive.Entries | ForEach-Object { $_.FullName }) } finally { $archive.Dispose() }
}

# The hashes of every file of the master installation: no file of a bundle may be one of them.
$master = Get-Content -Raw 'baselines/steam-master.json' | ConvertFrom-Json
$gameHashes = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $master.files) { [void]$gameHashes.Add([string]$file.sha256) }
# ... and of every file of the exported content, when this machine has it (game/content): the runtime files are the game's files too.
$contentManifest = 'game/content/content-manifest.json'
if (Test-Path $contentManifest) { foreach ($asset in (Get-Content -Raw $contentManifest | ConvertFrom-Json).assets) { [void]$gameHashes.Add([string]$asset.sha256) } }
if ($gameHashes.Count -lt 100) { Write-Host "baselines/steam-master.json lists only $($gameHashes.Count) hashes."; exit 1 }
function Assert-NoGameFile([string]$zip) {
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $stream = $entry.Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
            if ($gameHashes.Contains($hash)) { Fail "$([IO.Path]::GetFileName($zip)) holds $($entry.FullName), a file of the game" }
        }
        Pass "$([IO.Path]::GetFileName($zip)): none of its $($archive.Entries.Count) files is one of the $($gameHashes.Count) files of the master installation and its export"
    } finally { $archive.Dispose() }
}

# ---- the client
$clientDir = Join-Path $OutDir 'client'
New-Item -ItemType Directory -Force -Path $clientDir | Out-Null
& dotnet $clientDll.FullName --crash-test --net-dir $clientDir --settings-dir (Join-Path $OutDir 'settings') *> (Join-Path $OutDir 'client-crash.log')
$code = $LASTEXITCODE
if ($code -eq 0) { Fail 'the client with --crash-test ended with exit code 0' } else { Pass "the client with --crash-test failed (exit code $code)" }
$report = Get-ChildItem (Join-Path $clientDir 'crashes') -Filter 'client-*.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $report) { Fail 'no crash report under crashes/' }
else {
    $text = Get-Content -Raw $report.FullName
    foreach ($needed in 'InvalidOperationException', 'A failure on purpose', 'build:', 'arguments:', '--crash-test', 'runtime:', 'last ') {
        if (-not $text.Contains($needed)) { Fail "the crash report does not name '$needed'" }
    }
    Pass "crash report $($report.Name) ($($text.Length) characters)"
}
if (-not (Get-ChildItem (Join-Path $clientDir 'logs') -Filter 'client-*.log' -ErrorAction SilentlyContinue)) { Fail 'no log of the run under logs/' } else { Pass 'the run left its log under logs/' }
if (-not ((Get-Content -Raw (Join-Path $OutDir 'client-crash.log')) -match 'crash report: ')) { Fail 'the client did not say where the crash report is' }

New-Item -ItemType Directory -Force -Path (Join-Path $clientDir 'diagnostics/desync-1') | Out-Null
Set-Content -Path (Join-Path $clientDir 'diagnostics/desync-1/info.json') -Value '{"tick": 1}'
Set-Content -Path (Join-Path $clientDir 'diagnostics/player-key.pem') -Value '-----BEGIN PRIVATE KEY-----'
Set-Content -Path (Join-Path $clientDir 'crashes/game.sav') -Value 'a saved game'
$clientBundle = Join-Path $OutDir 'client-bundle.zip'
& dotnet $clientDll.FullName --support-bundle $clientBundle --net-dir $clientDir *> (Join-Path $OutDir 'client-bundle.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $clientBundle)) { Fail "the client wrote no support bundle (exit code $LASTEXITCODE)" }
else {
    $names = Entries $clientBundle
    foreach ($needed in 'ABOUT.txt', 'CONTENTS.txt', 'diagnostics/desync-1/info.json') { if ($names -notcontains $needed) { Fail "the client's bundle lacks $needed" } }
    if (-not ($names | Where-Object { $_ -like 'crashes/client-*.txt' })) { Fail "the client's bundle lacks the crash report" }
    if (-not ($names | Where-Object { $_ -like 'logs/client-*.log' })) { Fail "the client's bundle lacks the log" }
    if ($names | Where-Object { $_ -like '*.pem' -or $_ -like '*.sav' }) { Fail "the client's bundle holds a key or a saved game: $($names -join ', ')" }
    Pass "client bundle: $($names.Count) files, no key, no saved game"
    Assert-NoGameFile $clientBundle
}

# ---- the server
$serverDir = Join-Path $OutDir 'server'
$banFile = Join-Path $OutDir 'bans.txt'
Set-Content -Path $banFile -Value '203.0.113.7'
& dotnet $serverDll --crash-test --port 0 --address 127.0.0.1 --log-dir $serverDir --ban-file $banFile --key-file (Join-Path $OutDir 'server-key.pem') *> (Join-Path $OutDir 'server-crash.log')
if ($LASTEXITCODE -ne 70) { Fail "the server with --crash-test ended with exit code $LASTEXITCODE, not 70" } else { Pass 'the server with --crash-test failed with exit code 70' }
$serverReport = Get-ChildItem $serverDir -Filter 'crash-*.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $serverReport) { Fail 'the server wrote no crash report into its log directory' }
else {
    $text = Get-Content -Raw $serverReport.FullName
    foreach ($needed in 'InvalidOperationException', 'build:', 'protocol:', 'configuration:', '"LogDir"') { if (-not $text.Contains($needed)) { Fail "the server's crash report does not name '$needed'" } }
    Pass "server crash report $($serverReport.Name)"
}
$serverBundle = Join-Path $OutDir 'server-bundle.zip'
& dotnet $serverDll --support-bundle $serverBundle --log-dir $serverDir --ban-file $banFile --key-file (Join-Path $OutDir 'server-key.pem') *> (Join-Path $OutDir 'server-bundle.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $serverBundle)) { Fail "the server wrote no support bundle (exit code $LASTEXITCODE)" }
else {
    $names = Entries $serverBundle
    foreach ($needed in 'ABOUT.txt', 'CONTENTS.txt', 'configuration.json') { if ($names -notcontains $needed) { Fail "the server's bundle lacks $needed" } }
    if (-not ($names | Where-Object { $_ -like 'logs/crash-*.txt' })) { Fail "the server's bundle lacks the crash report" }
    if ($names | Where-Object { $_ -like '*.pem' -or $_ -like '*bans*' }) { Fail "the server's bundle holds the key or the ban file: $($names -join ', ')" }
    $archive = [IO.Compression.ZipFile]::OpenRead($serverBundle)
    try { foreach ($entry in $archive.Entries) { $reader = New-Object IO.StreamReader($entry.Open()); try { if ($reader.ReadToEnd().Contains('203.0.113.7')) { Fail "$($entry.FullName) holds an address of the ban file" } } finally { $reader.Dispose() } } }
    finally { $archive.Dispose() }
    Pass "server bundle: $($names.Count) files, no key, no ban list"
    Assert-NoGameFile $serverBundle
}

if ($failed -gt 0) { Write-Host "check-diagnostics: $failed problem(s); logs in $OutDir"; exit 1 }
Write-Host "check-diagnostics: ok (logs in $OutDir)"
exit 0
