<#
.SYNOPSIS
  Nimmt den festen Aufnahmesatz der Weltdarstellung auf (Phase 15) und schreibt PNGs plus eine Pruefsummenliste.

.DESCRIPTION
  Jede Szene ist ein reproduzierbarer `--smoke-test --capture`-Lauf des Stride-Clients (feste Karte, Seed, Frameanzahl, Kamera). Die Bilder
  gehoeren NICHT ins Repo (Originalassets liegen nur lokal); der Standardausgabeort ist ein Verzeichnis unter %TEMP%. Die Checkliste, die ein Mensch
  gegen die Bilder abhakt, steht in docs/CLIENT-OBJECTS.md ("Sichtpruefung der Weltdarstellung"). Die Pruefsummen in manifest.txt sind
  *vorlaeufig*: sie belegen Reproduzierbarkeit, nicht Richtigkeit, und werden erst nach der Abnahme durch einen Menschen als Golden Hashes verwendet.

.EXAMPLE
  .\scripts\capture-world.ps1
  .\scripts\capture-world.ps1 -OutDir D:\captures\run1 -Size 1920x1080 -NoBuild
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [string]$OutDir,
    [string]$Size = '1280x720',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Inhaltsverzeichnis nicht gefunden: $ContentDir" }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-captures' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Baue Client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}

$alpine = 'Maps/Alpine Assault/Alpine Assault.map'
# name, Argumente (ohne --smoke-test/--content-dir/--size/--capture), was das Bild zeigen muss
$scenes = @(
    @('viewer-commandcenter', @('--model-path', 'Art/W3D/ABBtCmdHQ.W3D'), 'Kommandozentrale: geschlossene Waende, Sternwappen an der Vorderseite, kein schwarzes Gitter-Skelett.'),
    @('viewer-barracks', @('--model-path', 'Art/W3D/ABBARRACKS.W3D'), 'Kaserne: alle Waende vorhanden, Zaun, Fahnenmast.'),
    @('viewer-crusader', @('--model-path', 'Art/W3D/AVLEOPARD.W3D'), 'Crusader: Wanne, Turm und Rohr in Ruhepose, Ketten unten.'),
    @('viewer-dozer', @('--model-path', 'Art/W3D/AVCONSTDOZ_A.W3D'), 'Planierraupe: vier Reifen unten, Schaufel vorn.'),
    @('match-start', @('--match', $alpine, '--frames', '120'), 'Startbasis: Kommandozentrale mit Wanddetails, Planierraupe, Rest der Karte schwarz (unerkundet), keine Gebaeude ohne Gelaende.'),
    @('match-lowangle', @('--match', $alpine, '--frames', '120', '--pitch', '30', '--height', '250'), 'Wie match-start, flache Kamera: Waende der Kommandozentrale sichtbar und beleuchtet (nicht schwarz).'),
    @('match-fog-unexplored', @('--match', $alpine, '--frames', '120', '--look', '2002,1349'), 'Kamera auf unerkundetem Gelaende mit einer Oelquelle: alles schwarz, kein Gebaeude im Schwarz.'),
    @('match-base-late', @('--match', $alpine, '--autoplay', '--frames', '4000'), 'Ausgebaute Basis: Gebaeude mit Teamfarbe, Geschuetztuerme, Zaeune ausgerichtet an den Gebaeudekanten.'),
    @('match-combat', @('--match', $alpine, '--autoplay', '--frames', '7500'), 'Gefecht: Patriot-Rohr zeigt auf das Ziel, Muendungsfeuer; Reifen/Tuerme nicht in Ruhepose.'),
    @('mission-start', @('--mission', 'Maps/GC_ChemGeneral/GC_ChemGeneral.map', '--frames', '120'), 'Missionsstart (Generals Challenge): Basis mit Gebaeuden, Beleuchtung wie im Gefecht.')
)

$manifest = New-Object System.Collections.Generic.List[string]
$manifest.Add("# cncnew world capture set, size $Size, VORLAEUFIGE Pruefsummen (Reproduzierbarkeit, nicht Richtigkeit)")
foreach ($scene in $scenes) {
    $name = $scene[0]
    $png = Join-Path $OutDir "$name.png"
    if (Test-Path -LiteralPath $png) { Remove-Item -LiteralPath $png }
    $clientArgs = @('--smoke-test', '--content-dir', $ContentDir, '--size', $Size) + $scene[1] + @('--capture', $png)
    Write-Host "== $name" -ForegroundColor Cyan
    dotnet run --project $project -c Release --no-build -- @clientArgs | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $png)) { throw "Szene '$name' fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $png).Hash
    $manifest.Add("$hash  $name.png  | $($scene[2])")
}

$manifestPath = Join-Path $OutDir 'manifest.txt'
[IO.File]::WriteAllLines($manifestPath, $manifest)
Write-Host "`nAufnahmen: $OutDir" -ForegroundColor Green
Get-Content -LiteralPath $manifestPath
