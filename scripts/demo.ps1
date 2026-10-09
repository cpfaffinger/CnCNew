<#
.SYNOPSIS
  Baut den Stride-Client aus dem Quelltext und startet eine der Tech-Demos (skirmish, mission, shell) oder eine gestellte Szene (scenario).

.DESCRIPTION
  skirmish  Live-Gefecht auf einer Skirmish-Karte (Befehlsleiste, Eingabe, Minimap, Musik, EVA) gegen KI-Gegner.
  mission   Kampagnenmission hinter der Befehlsleiste (Skripte, Missions-HUD, Briefing-Ablauf der Karte).
  scenario  Gestellte Einzeldemo zum Auditieren (-Scenario <Name>): kleine Szene auf einer echten Karte, die ein Verhalten isoliert;
            dieselbe Szene prueft `PresentationTool scenario-audit`. OHNE -Scenario startet nichts: das Skript listet die vorhandenen Szenen
            (capture-building, tank-duel, jet-strafe, exit-<fraktion>-<gebaeude> ...) und endet. Es gibt keine Standardszene.
  shell     Hauptmenue-Shell aus den echten WND-Layouts (Kampagne waehlen, Briefing, Mission, Speichern/Laden, Abschluss).

.EXAMPLE
  .\scripts\demo.ps1 skirmish
  .\scripts\demo.ps1 skirmish -Faction FactionChina -Opponent FactionGLA,FactionAmerica -Map "Maps/Tournament Tundra/Tournament Tundra.map"   # Karte mit genug Startpositionen (Tournament Desert hat nur zwei)
  .\scripts\demo.ps1 mission -Map "Maps/GC_ChemGeneral/GC_ChemGeneral.map"
  .\scripts\demo.ps1 scenario                       # listet die Szenen
  .\scripts\demo.ps1 scenario -Scenario tank-duel
  .\scripts\demo.ps1 skirmish -Mouse classic         # Mausbelegung des Originals (links waehlen und befehlen); Vorgabe: alternate (links waehlen, rechts befehlen)
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

    # Name der gestellten Demo (nur scenario); ohne Namen werden die Szenen gelistet. Die Namen kennt der Client (--list-scenarios).
    [string]$Scenario,

    # Spielerseite (nur mission; leer = Standard der Karte).
    [string]$HumanSide,

    [string]$Size = '1280x720',
    [int]$Seed,

    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # Simulationsrate (30, 60 oder 120 Hz); 0 = Standard der Spielschicht.
    [int]$TickRate = 0,

    # Mausbelegung dieses Laufs: alternate (links waehlen, rechts befehlen; Vorgabe der Einstellungen) oder classic (Original: links waehlen und befehlen).
    [ValidateSet('', 'alternate', 'classic')]
    [string]$Mouse = '',

    # Nur mit -SmokeTest: Zahl der Bilder (Originalbilder) des Rauchtests.
    [int]$Frames = 0,

    # Ton: stumm starten bzw. die Mixer-Ausgabe statt auf das Geraet nach <Verzeichnis>\audio.wav schreiben (ausserhalb des Repos; siehe docs/GAME-AUDIO.md).
    [switch]$Mute,
    [string]$AudioCapture,

    [switch]$NoBuild,
    [switch]$SmokeTest
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) {
    throw "Inhaltsverzeichnis nicht gefunden: $ContentDir`nDer geprueften Runtimebestand liegt lokal unter game\content (nicht im Repo; scripts\dev-content.ps1 erzeugt ihn aus der eigenen Steam-Installation); sonst mit -ContentDir angeben."
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

if ($Demo -eq 'scenario') {
    # The client is a windowed application without a console of its own: its redirected output is in the system's ANSI code page.
    $previousEncoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage)
    $known = @(dotnet run --project $project -c $Configuration --no-build -- --list-scenarios)
    $listExit = $LASTEXITCODE
    [Console]::OutputEncoding = $previousEncoding
    if ($listExit -ne 0) { throw "Der Client konnte die Szenen nicht auflisten (Exitcode $listExit)." }
    $names = $known | ForEach-Object { ($_ -split '\s+', 2)[0] }
    if (-not $Scenario) {
        Write-Host "Keine Szene angegeben - es wird nichts gestartet. Vorhandene Szenen (Start: .\scripts\demo.ps1 scenario -Scenario <Name>):" -ForegroundColor Yellow
        $known | ForEach-Object { Write-Host "  $_" }
        exit 0
    }
    if ($names -notcontains $Scenario) {
        Write-Host "Unbekannte Szene '$Scenario'. Vorhandene Szenen:" -ForegroundColor Red
        $known | ForEach-Object { Write-Host "  $_" }
        exit 2
    }
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
if ($SmokeTest -and $Frames -gt 0) { $clientArgs += @('--frames', "$Frames") }
if ($TickRate -gt 0) { $clientArgs += @('--tick-rate', "$TickRate") }
if ($Mouse) { $clientArgs += @('--mouse', $Mouse) }
if ($Mute) { $clientArgs += '--mute' }
if ($AudioCapture) { $clientArgs += @('--audio-capture', $AudioCapture) }

Write-Host "== Starte Demo '$Demo': $($clientArgs -join ' ')" -ForegroundColor Cyan
dotnet run --project $project -c $Configuration --no-build -- @clientArgs
exit $LASTEXITCODE
