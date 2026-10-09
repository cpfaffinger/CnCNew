<#
.SYNOPSIS
  Renders the staged demos (capture-building, tank-duel, jet-strafe; individually also any other scene, e.g. exit-america-warfactory) without a window as an image sequence and writes an HTML overview.

.DESCRIPTION
  For every scene the Stride client runs in smoke-test mode (one frame = one original frame of 1/30 s game time, at every simulation rate; at 30 Hz that is one tick)
  and writes a PNG every -Every frames
  (`--filmstrip`). Together with the scene description and the check sentence from docs/DEMO-SCENARIOS.md, index.html says what to look for in the sequence.
  The images do not belong in the repository (original assets). To watch it yourself in a window: scripts\demo-capture-building.ps1, demo-tank-duel.ps1, demo-jet-strafe.ps1.

.EXAMPLE
  .\scripts\capture-scenarios.ps1
  .\scripts\capture-scenarios.ps1 -Scenario tank-duel -Every 5 -Size 1920x1080
#>
[CmdletBinding()]
param(
    # all = the three behaviour scenes; otherwise the name of a scene (also exit-<faction>-<building>|all).
    [string]$Scenario = 'all',
    [string]$ContentDir,
    [string]$OutDir,
    [string]$Size = '1280x720',
    # Distance between the images in original frames (1/30 s); 0 = the default of the scene.
    [int]$Every = 0,
    # Simulation rate (30, 60 or 120 Hz); 0 = the game layer's default. Frame counts and distances stay game time.
    [int]$TickRate = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-scenario-frames' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

# Scene: (original frames, default distance, what to look for)
$plan = [ordered]@{
    'capture-building' = @(1500, 100, 'The Ranger walks to the barracks, unpacks (UNPACKING), raises the flag (RAISING_FLAG, the barracks flashes in his colour), the barracks changes sides, the Ranger packs up again.')
    'tank-duel'        = @(300, 10, 'Both turrets turn to the opponent before the first shot is fired; muzzle flash only after aiming; the barrels point at the target at every shot.')
    'jet-strafe'       = @(2100, 60, 'The airfield builds the Raptors (tick ~600), taxiing, take-off (~830), approach without standing still, four missiles, circling instead of hovering, return flight, landing, reloading.')
}

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Building client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

$names = if ($Scenario -eq 'all') { $plan.Keys } else { @($Scenario) }
foreach ($name in $names) {
    # A scene without an entry of its own (the factory exits): 900 original frames, an image every 15.
    if (-not $plan.Contains($name)) { $plan[$name] = @(900, 15, 'Factory exit: the unit appears inside the building, the door is open at that moment, it drives out through the door side (right) and stops in front of it.') }
}
$html = New-Object System.Text.StringBuilder
[void]$html.AppendLine('<!doctype html><meta charset="utf-8"><title>cncnew scenarios</title><style>body{font-family:sans-serif;background:#222;color:#ddd}img{width:420px;margin:2px}h2{margin-top:2em}</style>')
foreach ($name in $names) {
    $ticks, $default, $note = $plan[$name]
    $step = if ($Every -gt 0) { $Every } else { $default }
    $dir = Join-Path $OutDir $name
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    Write-Host "== $name ($ticks original frames, every $step)" -ForegroundColor Cyan
    $rateArgs = if ($TickRate -gt 0) { @('--tick-rate', "$TickRate") } else { @() }
    dotnet run --project $project -c Release --no-build -- --smoke-test --content-dir $ContentDir --size $Size --scenario $name --frames $ticks --filmstrip $dir --every $step @rateArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Scene '$name' failed (exit code $LASTEXITCODE)." }
    [void]$html.AppendLine("<h2>$name</h2><p>$note</p>")
    foreach ($png in Get-ChildItem -LiteralPath $dir -Filter 'frame-*.png' | Sort-Object Name) {
        $tick = [int]($png.BaseName -replace 'frame-', '')
        [void]$html.AppendLine("<a href=""$name/$($png.Name)""><img src=""$name/$($png.Name)"" title=""Tick $tick"" alt=""Tick $tick""></a>")
    }
}

$index = Join-Path $OutDir 'index.html'
[IO.File]::WriteAllText($index, $html.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "`nImage sequences: $OutDir`nOverview: $index" -ForegroundColor Green
