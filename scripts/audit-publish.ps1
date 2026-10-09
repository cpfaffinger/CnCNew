<#
.SYNOPSIS
  Prueft vor einem Push, dass der gesamte Git-Verlauf keine Spieldatei und keinen Auszug daraus enthaelt.

.DESCRIPTION
  Zwei Pruefungen ueber JEDEN Blob des Verlaufs (alle Zweige, nicht nur der aktuelle Stand - ein Push veroeffentlicht jeden Commit):
    1. scripts/audit-history-assets.py: SHA-256 jedes Blobs gegen SHA-256 jeder Datei der Spielinstallation und jeder Datei des
       exportierten Bestands (game/content). Dazu: binaere Blobs und die groessten Blobs.
    2. scripts/audit-history-excerpts.py: woertliche Zeilen aus den Textdateien des Spiels (INI, WND, STR, TXT) im Verlauf.
       Einzelne "Schluessel = Wert"-Zeilen in selbstgeschriebenen Testdaten sind erlaubt; das Skript listet sie, entschieden wird von Hand.
  Exitcode 1, wenn ein Blob einer Spieldatei gleicht oder ein binaerer Blob im Verlauf liegt. Die Ausgaben liegen ausserhalb des Repos.
  Braucht Python 3 und liest die Installation und game/content nur.

.EXAMPLE
  pwsh scripts/audit-publish.ps1
  pwsh scripts/audit-publish.ps1 -GameDir 'D:\Steam\steamapps\common\Command & Conquer Generals - Zero Hour' -MinimumLineLength 34
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Command & Conquer Generals - Zero Hour',
    [string]$ContentDir,
    [string]$OutDir,
    # Mindestlaenge einer Zeile (nach Normalisierung der Leerzeichen), ab der sie als Auszug zaehlt.
    [int]$MinimumLineLength = 60
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-audit-publish' }
if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) { throw "Spielinstallation nicht gefunden: $GameDir" }
if (-not (Test-Path -LiteralPath (Join-Path $ContentDir 'content-manifest.json'))) { throw "Exportierter Bestand nicht gefunden: $ContentDir (scripts/dev-content.ps1 ausfuehren)." }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
if ($OutDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw "Das Ausgabeverzeichnis muss ausserhalb des Repos liegen: $OutDir" }

Write-Host '== Index der Installation (Pfad, Groesse, SHA-256) ...' -ForegroundColor Cyan
$index = Join-Path $OutDir 'installation-index.tsv'
$root = (Resolve-Path -LiteralPath $GameDir).Path
$lines = Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName | ForEach-Object {
    "{0}`t{1}`t{2}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Length, $_.FullName.Substring($root.Length).TrimStart('\')
}
[IO.File]::WriteAllLines($index, $lines, [Text.UTF8Encoding]::new($false))

Write-Host '== 1. Blobs gegen Spieldateien ...' -ForegroundColor Cyan
$assets = python (Join-Path $PSScriptRoot 'audit-history-assets.py') $repo $OutDir $index (Join-Path $ContentDir 'content-manifest.json')
$assets | Select-Object -First 4
$assets | Set-Content -Encoding utf8 (Join-Path $OutDir 'assets.txt')
$equal = [int](($assets | Select-String -Pattern '^blobs equal to a game file: (\d+)').Matches[0].Groups[1].Value)
$binary = [int](($assets | Select-String -Pattern '^binary blobs: (\d+)').Matches[0].Groups[1].Value)

Write-Host "== 2. Woertliche Zeilen aus Spieltexten (ab $MinimumLineLength Zeichen) ..." -ForegroundColor Cyan
$excerpts = python (Join-Path $PSScriptRoot 'audit-history-excerpts.py') $repo $ContentDir $OutDir $MinimumLineLength
$excerpts | Select-Object -First 12
$excerpts | Set-Content -Encoding utf8 (Join-Path $OutDir 'excerpts.txt')

Write-Host "== Ergebnis: $equal Blob(s) gleich einer Spieldatei, $binary binaere(r) Blob(s). Einzelheiten: $OutDir" -ForegroundColor Cyan
exit (($equal -gt 0 -or $binary -gt 0) ? 1 : 0)
