<#
.SYNOPSIS
  Records the fixed capture set of the world presentation (Phase 15) and writes PNGs plus a checksum list.

.DESCRIPTION
  Every scene is a reproducible `--smoke-test --capture` run of the Stride client (fixed map, seed, frame count, camera). The images
  do NOT belong in the repository (original assets exist only locally); the default output location is a directory under %TEMP%.
  The autoplay scenes (match-base-late, match-combat) are not pixel-identical between runs (1-11 pixels, also before Phase 15); their checksum is not a golden hash. The checklist that a human
  ticks off against the images is in docs/CLIENT-OBJECTS.md (the visual check of the world presentation). The checksums in manifest.txt are
  *provisional*: they verify reproducibility, not correctness, and are used as golden hashes only after acceptance by a human.

.EXAMPLE
  .\scripts\capture-world.ps1
  .\scripts\capture-world.ps1 -OutDir D:\captures\run1 -Size 1920x1080 -NoBuild
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [string]$OutDir,
    [string]$Size = '1280x720',
    # Simulation rate (30, 60 or 120 Hz); 0 = the game layer's default. The frame counts of the scenes are original frames (1/30 s game time) at every rate.
    [int]$TickRate = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-captures' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Building client (Release) ...' -ForegroundColor Cyan
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}

$alpine = 'Maps/Alpine Assault/Alpine Assault.map'
# name, arguments (without --smoke-test/--content-dir/--size/--capture), what the image has to show
$scenes = @(
    @('viewer-commandcenter', @('--model-path', 'Art/W3D/ABBtCmdHQ.W3D'), 'Command center: closed walls, star emblem on the front, no black lattice skeleton.'),
    @('viewer-barracks', @('--model-path', 'Art/W3D/ABBARRACKS.W3D'), 'Barracks: all walls present, fence, flagpole.'),
    @('viewer-crusader', @('--model-path', 'Art/W3D/AVLEOPARD.W3D'), 'Crusader: hull, turret and barrel in rest pose, tracks at the bottom.'),
    @('viewer-dozer', @('--model-path', 'Art/W3D/AVCONSTDOZ_A.W3D'), 'Dozer: four tyres at the bottom, blade in front.'),
    @('match-start', @('--match', $alpine, '--frames', '120'), 'Starting base: command center with wall details, dozer, rest of the map black (unexplored), no buildings without terrain.'),
    @('match-lowangle', @('--match', $alpine, '--frames', '120', '--pitch', '30', '--height', '250'), 'Like match-start, flat camera: walls of the command center visible and lit (not black).'),
    @('match-selected', @('--match', $alpine, '--frames', '120', '--pitch', '30', '--height', '250', '--select-own'), 'Selection: green corner brackets and health bar on the command center, ring and bar on the dozer (overlay, docs/GAME-HUD.md).'),
    @('match-cursor-move', @('--match', $alpine, '--frames', '120', '--pitch', '30', '--height', '250', '--select-own', '--cursor-at', '300,380'), 'Mouse pointer (docs/PRESENTATION-INPUT.md section 11): with the dozer selected, the move cursor (four green arrows pointing inwards) is over free ground at (300,380), not the Windows arrow.'),
    @('match-waypoints', @('--match', $alpine, '--frames', '30', '--pitch', '30', '--height', '250', '--select-own', '--cursor-at', '700,560', '--waypoints', '-10,-60;-90,-70;-170,-90'), 'Waypoints with Alt (docs/GAME-HUD.md section 16): blue line from the dozer through three stations, a diamond per station, a fading ring at every station (order marker); waypoint cursor at (700,560).'),
    @('match-fog-unexplored', @('--match', $alpine, '--frames', '120', '--look', '2002,1349'), 'Camera on unexplored terrain with an oil derrick: everything black, no building in the black.'),
    @('match-base-late', @('--match', $alpine, '--autoplay', '--frames', '4000'), 'Developed base: buildings with team colour, gun turrets, fences aligned with the building edges.'),
    @('match-combat', @('--match', $alpine, '--autoplay', '--frames', '7500'), 'Combat: the Patriot barrel points at the target, muzzle flash; tyres/turrets not in rest pose.'),
    @('scenario-tank-duel', @('--scenario', 'tank-duel', '--frames', '30'), 'Staged demo tank against tank, tick 30: the Crusader has already turned its turret to the opponent (it has to before the first shot; PresentationTool scenario-audit checks it).'),
    @('scenario-capture', @('--scenario', 'capture-building', '--frames', '400'), 'Staged demo capture a building, tick 400: the Ranger stands at the barracks (raising the flag is visible once the ability is staged).'),
    @('scenario-jet', @('--scenario', 'jet-strafe', '--frames', '900'), 'Staged demo jet against missile troopers, tick 900: Raptor approaching the Missile Defenders.'),
    @('mission-start', @('--mission', 'Maps/GC_ChemGeneral/GC_ChemGeneral.map', '--frames', '120'), 'Mission start (Generals Challenge): base with buildings, lighting as in a skirmish.'),
    # Fourth entry 'reveal': the scene runs with CNCNEW_TERRAIN_REVEAL=1 (whole map visible) because roads lie outside the initial vision.
    @('roads-junctions', @('--match', $alpine, '--frames', '120', '--pitch', '60', '--height', '150', '--look', '1177,968'), 'Roads (docs/CLIENT-ROADS.md): Y junction with zebra crossing, continuous carriageway with yellow centre line, edges blended softly (no grey rectangles at the corners), the road follows the terrain.', 'reveal'),
    @('roads-crossing', @('--match', $alpine, '--frames', '120', '--pitch', '60', '--height', '150', '--look', '1217,1200'), 'Roads: four-way junction (rotated 45 degrees), pavement corners, closed transitions between junction and carriageway.', 'reveal'),
    @('roads-overview', @('--match', $alpine, '--frames', '120', '--pitch', '45', '--height', '400', '--look', '1000,1080'), 'Roads: gravel road with curves and a T piece, railway tracks, asphalt road with a junction; road types overlap in the right order.', 'reveal')
)

# SHA-256 of the decoded pixels, not of the PNG file: the encoder's output differs by a few bytes between runs for identical pixels (seen on match-base-late, 4 runs, 4 file hashes, 0 differing pixels).
Add-Type -AssemblyName System.Drawing
function Get-PixelHash([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($path)
    try {
        $rect = New-Object System.Drawing.Rectangle 0, 0, $bitmap.Width, $bitmap.Height
        $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bytes = New-Object byte[] ($data.Stride * $bitmap.Height)
            [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        } finally { $bitmap.UnlockBits($data) }
        return [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($bytes)).Replace('-', '')
    } finally { $bitmap.Dispose() }
}

$manifest = New-Object System.Collections.Generic.List[string]
$manifest.Add("# cncnew world capture set, size $Size, PROVISIONAL checksums over the pixels (reproducibility, not correctness)")
foreach ($scene in $scenes) {
    $name = $scene[0]
    $png = Join-Path $OutDir "$name.png"
    if (Test-Path -LiteralPath $png) { Remove-Item -LiteralPath $png }
    $clientArgs = @('--smoke-test', '--content-dir', $ContentDir, '--size', $Size) + $scene[1] + @('--capture', $png)
    if ($TickRate -gt 0) { $clientArgs += @('--tick-rate', "$TickRate") }
    Write-Host "== $name" -ForegroundColor Cyan
    if ($scene.Count -gt 3 -and $scene[3] -eq 'reveal') { $env:CNCNEW_TERRAIN_REVEAL = '1' } else { Remove-Item Env:CNCNEW_TERRAIN_REVEAL -ErrorAction SilentlyContinue }
    dotnet run --project $project -c Release --no-build -- @clientArgs | Out-Null
    Remove-Item Env:CNCNEW_TERRAIN_REVEAL -ErrorAction SilentlyContinue
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $png)) { throw "Scene '$name' failed (exit code $LASTEXITCODE)." }
    $hash = Get-PixelHash $png
    $manifest.Add("$hash  $name.png  | $($scene[2])")
}

$manifestPath = Join-Path $OutDir 'manifest.txt'
[IO.File]::WriteAllLines($manifestPath, $manifest)
Write-Host "`nCaptures: $OutDir" -ForegroundColor Green
Get-Content -LiteralPath $manifestPath
