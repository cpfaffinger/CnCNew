<#
.SYNOPSIS
  Baut den Stride-Client aus dem Quelltext und startet eine der drei Tech-Demos.

.DESCRIPTION
  skirmish  Live-Gefecht auf einer Skirmish-Karte (Befehlsleiste, Eingabe, Minimap, Musik, EVA) gegen KI-Gegner.
  mission   Kampagnenmission hinter der Befehlsleiste (Skripte, Missions-HUD, Briefing-Ablauf der Karte).
  scenario  Gestellte Einzeldemo zum Auditieren (-Scenario capture-building | tank-duel | jet-strafe): kleine Szene auf einer echten Karte,
            die ein Verhalten isoliert; dieselbe Szene prueft `PresentationTool scenario-audit`.
  shell     Hauptmenue-Shell aus den echten WND-Layouts (Kampagne waehlen, Briefing, Mission, Speichern/Laden, Abschluss).

.EXAMPLE
  .\scripts\demo.ps1 skirmish
  .\scripts\demo.ps1 skirmish -Faction FactionChina -Opponent FactionGLA,FactionAmerica -Map "Maps/Tournament Desert/Tournament Desert.map"
  .\scripts\demo.ps1 mission -Map "Maps/GC_ChemGeneral/GC_ChemGeneral.map"
  .\scripts\demo.ps1 scenario -Scenario tank-duel
  .\scripts\demo.ps1 shell -Size 1920x1080
  .\scripts\demo.ps1 skirmish -SmokeTest      # headless Rauchtest statt Fenster
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('skirmish', 'mission', 'shell', 'scenario')]
    [string]$Demo,

    # Schema-2-Runtimebestand (Originalassets, nicht im Repo).
    [string]$ContentDir,

    # Karte (nur skirmish/mission).
    [string]$Map,

    # Spielerfraktion und Gegner (nur skirmish); mehrere Gegner mit Komma.
    [string]$Faction = 'FactionAmerica',
    [string[]]$Opponent = @('FactionChina'),

    # Name der gestellten Demo (nur scenario).
    [ValidateSet('capture-building', 'tank-duel', 'jet-strafe')]
    [string]$Scenario = 'tank-duel',

    # Spielerseite (nur mission; leer = Standard der Karte).
    [string]$HumanSide,

    [string]$Size = '1280x720',
    [int]$Seed,

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # Ton: stumm starten bzw. die Mixer-Ausgabe statt auf das Geraet nach <Verzeichnis>udio.wav schreiben (ausserhalb des Repos; siehe docs/GAME-AUDIO.md).
    [switch]$Mute,
    [string]$AudioCapture,

    [switch]$NoBuild,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) {
    throw "Inhaltsverzeichnis nicht gefunden: $ContentDir`nDer geprueften Runtimebestand liegt lokal unter .local\phase2-import-a (nicht im Repo); mit -ContentDir angeben."
}
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path

if (-not $Map) {
    $Map = switch ($Demo) {
        'skirmish' { 'Maps/Alpine Assault/Alpine Assault.map' }
        'mission'  { 'Maps/GC_ChemGeneral/GC_ChemGeneral.map' }
        default    { '' }
    }
}

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host "== Baue Client ($Configuration) ..." -ForegroundColor Cyan
    dotnet build $project -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}

$clientArgs = @('--content-dir', $ContentDir, '--size', $Size)
switch ($Demo) {
    'skirmish' {
        $clientArgs += @('--match', $Map, '--faction', $Faction, '--opponent', ($Opponent -join ','))
    }
    'mission' {
        $clientArgs += @('--mission', $Map)
        if ($HumanSide) { $clientArgs += @('--human-side', $HumanSide) }
    }
    'shell' {
        $clientArgs += '--shell'
    }
    'scenario' {
        $clientArgs += @('--scenario', $Scenario)
    }
}
if ($PSBoundParameters.ContainsKey('Seed')) { $clientArgs += @('--seed', "$Seed") }
if ($SmokeTest) { $clientArgs += '--smoke-test' }
if ($Mute) { $clientArgs += '--mute' }
if ($AudioCapture) { $clientArgs += @('--audio-capture', $AudioCapture) }

Write-Host "== Starte Demo '$Demo': $($clientArgs -join ' ')" -ForegroundColor Cyan
dotnet run --project $project -c $Configuration --no-build -- @clientArgs
exit $LASTEXITCODE
