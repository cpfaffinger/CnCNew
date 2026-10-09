<#
.SYNOPSIS
  Checks before a push that the whole Git history contains no game file and no excerpt of one.

.DESCRIPTION
  Two checks over EVERY blob of the history (all branches, not only the current state - a push publishes every commit):
    1. scripts/audit-history-assets.py: SHA-256 of every blob against the SHA-256 of every file of the game installation and of every file of
       the exported content (game/content). In addition: binary blobs and the largest blobs.
    2. scripts/audit-history-excerpts.py: verbatim lines from the game's text files (INI, WND, STR, TXT) in the history.
       Single "key = value" lines in hand-written test data are allowed; the script lists them, the decision is made by hand.
  Exit code 1 when a blob equals a game file or a binary blob is in the history. The output lies outside the repository.
  Needs Python 3; the installation and game/content are only read.

.EXAMPLE
  pwsh scripts/audit-publish.ps1
  pwsh scripts/audit-publish.ps1 -GameDir 'D:\Steam\steamapps\common\Command & Conquer Generals - Zero Hour' -MinimumLineLength 34
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Command & Conquer Generals - Zero Hour',
    [string]$ContentDir,
    [string]$OutDir,
    # Minimum length of a line (after whitespace normalisation) from which it counts as an excerpt.
    [int]$MinimumLineLength = 60
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-audit-publish' }
if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) { throw "Game installation not found: $GameDir" }
if (-not (Test-Path -LiteralPath (Join-Path $ContentDir 'content-manifest.json'))) { throw "Exported content not found: $ContentDir (run scripts/dev-content.ps1)." }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
if ($OutDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw "The output directory must lie outside the repository: $OutDir" }

Write-Host '== Index of the installation (path, size, SHA-256) ...' -ForegroundColor Cyan
$index = Join-Path $OutDir 'installation-index.tsv'
$root = (Resolve-Path -LiteralPath $GameDir).Path
$lines = Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName | ForEach-Object {
    "{0}`t{1}`t{2}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Length, $_.FullName.Substring($root.Length).TrimStart('\')
}
[IO.File]::WriteAllLines($index, $lines, [Text.UTF8Encoding]::new($false))

Write-Host '== 1. Blobs against game files ...' -ForegroundColor Cyan
$assets = python (Join-Path $PSScriptRoot 'audit-history-assets.py') $repo $OutDir $index (Join-Path $ContentDir 'content-manifest.json')
$assets | Select-Object -First 4
$assets | Set-Content -Encoding utf8 (Join-Path $OutDir 'assets.txt')
$equal = [int](($assets | Select-String -Pattern '^blobs equal to a game file: (\d+)').Matches[0].Groups[1].Value)
$binary = [int](($assets | Select-String -Pattern '^binary blobs: (\d+)').Matches[0].Groups[1].Value)

Write-Host "== 2. Verbatim lines from game texts ($MinimumLineLength characters or longer) ..." -ForegroundColor Cyan
$excerpts = python (Join-Path $PSScriptRoot 'audit-history-excerpts.py') $repo $ContentDir $OutDir $MinimumLineLength
$excerpts | Select-Object -First 12
$excerpts | Set-Content -Encoding utf8 (Join-Path $OutDir 'excerpts.txt')

Write-Host "== Result: $equal blob(s) equal to a game file, $binary binary blob(s). Details: $OutDir" -ForegroundColor Cyan
exit (($equal -gt 0 -or $binary -gt 0) ? 1 : 0)
