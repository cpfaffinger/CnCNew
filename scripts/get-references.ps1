<#
.SYNOPSIS
  Klont die Referenzquellen, auf die Doku und Kommentare verweisen, nach <repo>\references (von Git ausgenommen).

.DESCRIPTION
  Dieses Repository enthaelt keinen fremden Quelltext. Wo ein Algorithmus anhand der veroeffentlichten Quellen des Originals
  verstanden und neu geschrieben wurde, nennt die Doku Datei und Funktion. Um diese Verweise nachzulesen, holt das Skript die
  vier oeffentlichen Repositories in genau den Staenden, gegen die gelesen wurde:

    ea-generals-zero-hour   https://github.com/electronicarts/CnC_Generals_Zero_Hour.git   0a05454
    generals-game-code      https://github.com/TheSuperHackers/GeneralsGameCode.git        d6837ff7
    generals-game-patch     https://github.com/TheSuperHackers/GeneralsGamePatch.git       6c7f40d6
    stride                  https://github.com/stride3d/stride.git                         e023d87

  Nichts davon wird je committet (references/ steht in .gitignore). Die Lizenzen der Quellen stehen in ihren Repositories;
  siehe NOTICE.md.

.EXAMPLE
  pwsh scripts/get-references.ps1
  pwsh scripts/get-references.ps1 -Only ea-generals-zero-hour
#>
[CmdletBinding()]
param(
    # Nur diese Referenzen holen (Namen der Zielordner).
    [string[]]$Only = @()
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repo 'references'
$sources = @(
    @{ Name = 'ea-generals-zero-hour'; Url = 'https://github.com/electronicarts/CnC_Generals_Zero_Hour.git'; Commit = '0a05454' }
    @{ Name = 'generals-game-code';    Url = 'https://github.com/TheSuperHackers/GeneralsGameCode.git';     Commit = 'd6837ff7' }
    @{ Name = 'generals-game-patch';   Url = 'https://github.com/TheSuperHackers/GeneralsGamePatch.git';    Commit = '6c7f40d6' }
    @{ Name = 'stride';                Url = 'https://github.com/stride3d/stride.git';                      Commit = 'e023d87' }
)
$Only = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Only.Count -gt 0) {
    $unknown = @($Only | Where-Object { $name = $_; -not ($sources | Where-Object { $_.Name -eq $name }) })
    if ($unknown.Count -gt 0) { throw "Unbekannte Referenz: $($unknown -join ', '). Bekannt: $(($sources | ForEach-Object { $_.Name }) -join ', ')." }
    $sources = @($sources | Where-Object { $Only -contains $_.Name })
}
New-Item -ItemType Directory -Force -Path $target | Out-Null

$failed = 0
foreach ($source in $sources) {
    $directory = Join-Path $target $source.Name
    if (Test-Path -LiteralPath (Join-Path $directory '.git')) {
        Write-Host "== $($source.Name): vorhanden, hole $($source.Commit)" -ForegroundColor Cyan
        git -C $directory fetch --quiet origin
    }
    else {
        Write-Host "== $($source.Name): klone $($source.Url)" -ForegroundColor Cyan
        git clone --quiet $source.Url $directory
    }
    if ($LASTEXITCODE -ne 0) { Write-Warning "$($source.Name): git ist fehlgeschlagen (Exitcode $LASTEXITCODE)."; $failed++; continue }
    git -C $directory -c advice.detachedHead=false checkout --quiet $source.Commit
    if ($LASTEXITCODE -ne 0) { Write-Warning "$($source.Name): der Stand $($source.Commit) ist nicht auscheckbar."; $failed++; continue }
    Write-Host "   Stand $(git -C $directory rev-parse --short HEAD) in $directory"
}
exit ($failed -gt 0 ? 1 : 0)
