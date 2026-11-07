<#
.SYNOPSIS
  Checks two things the owner's playtest of 7 November 2026 found in how the fog of war reaches the screen: the radar shows the minimap's picture as it is composed (not
  brightened by the colour space), and an object in the fog is darkened to about half of its brightness (not to a quarter).

.DESCRIPTION
  Starts the Release client once in a headless smoke test on Alpine Assault (1280x720, fixed seed), keeps a capture of the frame and the terrain layer's own dump of the
  composed minimap (CNCNEW_TERRAIN_DUMP), and compares the mean brightness of the fogged lower part of the radar in the capture with the same part of the dump.
  The minimap texture is made of display values (as W3DRadar's gamma-space texture is); read as linear light it showed three times too bright (a fogged mean of 86 against
  26) and the half level of the fog hardly differed from the bright one.
  Then it captures the same scene with CNCNEW_OBJECT_FOG=1 (every object drawn as if it stood in the fog) and CNCNEW_TERRAIN_REVEAL=1 (the ground bright) and compares
  the brightness of the command centre's lit parts with the plain capture: the original multiplies a fogged object by 127/255 in display values (ratio about 0.5); the
  client multiplied its light by 0.25 once more through the colour conversion and showed 0.23.
  Exit code 0 when both agree, 1 otherwise.

.EXAMPLE
  .\scripts\check-minimap.ps1
  .\scripts\check-minimap.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [switch]$NoBuild,
    [double]$Tolerance = 0.15,
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo 'game\content' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Content directory not found: $ContentDir (pass -ContentDir)." }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
$project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
}
$dll = Join-Path $repo 'src\CnCNew.Client.Stride\bin\Release\net10.0-windows\win-x64\CnCNew.Client.Stride.dll'
$work = Join-Path ([IO.Path]::GetTempPath()) ('cncnew-minimap-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$capture = Join-Path $work 'frame.png'
$dump = Join-Path $work 'dump'
try {
    $env:CNCNEW_TERRAIN_DUMP = $dump
    $arguments = @($dll, '--smoke-test', '--capture', $capture, '--size', '1280x720', '--frames', '240', '--mute', '--no-intro', '--content-dir', $ContentDir,
        '--match', 'Maps/Alpine Assault/Alpine Assault.map', '--pitch', '30', '--height', '250', '--seed', '7')
    $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $work 'out.txt') -RedirectStandardError (Join-Path $work 'err.txt')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $process.Id -Force; throw "The client did not finish within $TimeoutSeconds s." }
    if ($process.ExitCode -ne 0) { throw "The client ended with exit code $($process.ExitCode)." }
} finally { Remove-Item Env:CNCNEW_TERRAIN_DUMP -ErrorAction SilentlyContinue }

Add-Type -AssemblyName System.Drawing
function Get-Mean([string]$path, [int]$x0, [int]$y0, [int]$x1, [int]$y1) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($path)
    try {
        $sum = 0.0; $count = 0
        for ($y = $y0; $y -lt $y1; $y += 2) { for ($x = $x0; $x -lt $x1; $x += 2) { $c = $bitmap.GetPixel($x, $y); $sum += ($c.R + $c.G + $c.B) / 3.0; $count++ } }
        return $sum / $count
    } finally { $bitmap.Dispose() }
}
# The radar of the 1280x720 bar lies at about x 24..192, y 532..712 and shows the 236x256 picture at 0.71; the lower part of the map is fogged, without the camera's frame.
$onScreen = Get-Mean $capture 30 640 190 710
$composed = Get-Mean (Join-Path $dump 'minimap.png') 10 150 230 250
$ratio = $onScreen / $composed
'fogged part of the radar: on the screen {0:F1}, in the composed picture {1:F1}, ratio {2:F2}' -f $onScreen, $composed, $ratio
$failed = $false
if ([Math]::Abs($ratio - 1.0) -gt $Tolerance) { Write-Host "FAIL: the radar is not shown as composed (colour space)." -ForegroundColor Red; $failed = $true }

# Part two: an object in the fog. The command centre of the 240th frame stands at these pixels; they are lit parts of its roof and walls.
$foggedCapture = Join-Path $work 'fogged.png'
try {
    $env:CNCNEW_OBJECT_FOG = '1'; $env:CNCNEW_TERRAIN_REVEAL = '1'
    $arguments[3] = $foggedCapture
    $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $work 'out2.txt') -RedirectStandardError (Join-Path $work 'err2.txt')
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { Stop-Process -Id $process.Id -Force; throw "The client did not finish within $TimeoutSeconds s." }
    if ($process.ExitCode -ne 0) { throw "The client ended with exit code $($process.ExitCode)." }
} finally { Remove-Item Env:CNCNEW_OBJECT_FOG, Env:CNCNEW_TERRAIN_REVEAL -ErrorAction SilentlyContinue }
$points = @(@(600, 330), @(520, 470), @(590, 340), @(530, 480))
$plain = [System.Drawing.Bitmap]::FromFile($capture); $fogged = [System.Drawing.Bitmap]::FromFile($foggedCapture)
$lit = 0.0; $dim = 0.0
foreach ($point in $points) {
    $a = $plain.GetPixel($point[0], $point[1]); $b = $fogged.GetPixel($point[0], $point[1])
    $lit += $a.R + $a.G + $a.B; $dim += $b.R + $b.G + $b.B
}
$plain.Dispose(); $fogged.Dispose()
$objectRatio = $dim / $lit
'lit parts of the command centre: in the fog {0:F0} of {1:F0}, ratio {2:F2} (the original: 0.50)' -f $dim, $lit, $objectRatio
Remove-Item -LiteralPath $work -Recurse -Force
if ($objectRatio -lt 0.42 -or $objectRatio -gt 0.62) { Write-Host "FAIL: an object in the fog is not darkened to about half." -ForegroundColor Red; $failed = $true }
if ($failed) { exit 1 }
Write-Host 'ok' -ForegroundColor Green
exit 0
