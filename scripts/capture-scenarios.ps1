<#
.SYNOPSIS
  Rendert die gestellten Demos (capture-building, tank-duel, jet-strafe) ohne Fenster als Bildfolge und legt eine HTML-Uebersicht an.

.DESCRIPTION
  Fuer jede Szene laeuft der Stride-Client im Rauchtestmodus (feste 30 Ticks pro Sekunde, also Bild = Tick) und schreibt alle -Every Bilder ein PNG
  (`--filmstrip`). Zusammen mit der Szenenbeschreibung und dem Pruefsatz aus docs/DEMO-SCENARIOS.md steht in index.html, worauf man in der Folge achten soll.
  Die Bilder gehoeren nicht ins Repo (Originalassets). Zum Selbstansehen im Fenster: scripts\demo-capture-building.ps1, demo-tank-duel.ps1, demo-jet-strafe.ps1.

.EXAMPLE
  .\scripts\capture-scenarios.ps1
  .\scripts\capture-scenarios.ps1 -Scenario tank-duel -Every 5 -Size 1920x1080
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'capture-building', 'tank-duel', 'jet-strafe')]
    [string]$Scenario = 'all',
    [string]$ContentDir,
    [string]$OutDir,
    [string]$Size = '1280x720',
    # Abstand der Bilder in Ticks; 0 = Standard je Szene.
    [int]$Every = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Inhaltsverzeichnis nicht gefunden: $ContentDir" }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-scenario-frames' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

# Szene: (Ticks, Standardabstand, worauf zu achten ist)
$plan = [ordered]@{
    'capture-building' = @(1500, 100, 'Der Ranger geht zur Kaserne, packt aus (UNPACKING), hisst die Flagge (RAISING_FLAG, die Kaserne blinkt in seiner Farbe), die Kaserne wechselt die Seite, der Ranger packt wieder ein.')
    'tank-duel'        = @(300, 10, 'Beide Tuerme drehen sich zum Gegner, bevor der erste Schuss faellt; Muendungsfeuer erst nach Ausrichtung; die Rohre zeigen bei jedem Schuss auf das Ziel.')
    'jet-strafe'       = @(2100, 60, 'Der Flugplatz baut die Raptors (Tick ~600), Rollen, Start (~830), Anflug ohne Stillstand, vier Raketen, Kreisen statt Schweben, Rueckflug, Landung, Nachladen.')
}

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Baue Client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}

$names = if ($Scenario -eq 'all') { $plan.Keys } else { @($Scenario) }
$html = New-Object System.Text.StringBuilder
[void]$html.AppendLine('<!doctype html><meta charset="utf-8"><title>cncnew scenarios</title><style>body{font-family:sans-serif;background:#222;color:#ddd}img{width:420px;margin:2px}h2{margin-top:2em}</style>')
foreach ($name in $names) {
    $ticks, $default, $note = $plan[$name]
    $step = if ($Every -gt 0) { $Every } else { $default }
    $dir = Join-Path $OutDir $name
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    Write-Host "== $name ($ticks Ticks, alle $step)" -ForegroundColor Cyan
    dotnet run --project $project -c Release --no-build -- --smoke-test --content-dir $ContentDir --size $Size --scenario $name --frames $ticks --filmstrip $dir --every $step | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Szene '$name' fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    [void]$html.AppendLine("<h2>$name</h2><p>$note</p>")
    foreach ($png in Get-ChildItem -LiteralPath $dir -Filter 'frame-*.png' | Sort-Object Name) {
        $tick = [int]($png.BaseName -replace 'frame-', '')
        [void]$html.AppendLine("<a href=""$name/$($png.Name)""><img src=""$name/$($png.Name)"" title=""Tick $tick"" alt=""Tick $tick""></a>")
    }
}

$index = Join-Path $OutDir 'index.html'
[IO.File]::WriteAllText($index, $html.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "`nBildfolgen: $OutDir`nUebersicht: $index" -ForegroundColor Green
