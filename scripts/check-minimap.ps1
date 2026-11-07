<#
.SYNOPSIS
  Checks that the radar on the screen shows the minimap's picture as it is composed, not brightened by the colour space (the owner's playtest of 7 November 2026:
  the fog of war did not show on the minimap).

.DESCRIPTION
  Starts the Release client once in a headless smoke test on Alpine Assault (1280x720, fixed seed), keeps a capture of the frame and the terrain layer's own dump of the
  composed minimap (CNCNEW_TERRAIN_DUMP), and compares the mean brightness of the fogged lower part of the radar in the capture with the same part of the dump.
  The minimap texture is made of display values (as W3DRadar's gamma-space texture is); read as linear light it showed three times too bright (a fogged mean of 86 against
  26) and the half level of the fog hardly differed from the bright one. Exit code 0 when the two means agree within the tolerance, 1 otherwise.

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
Remove-Item -LiteralPath $work -Recurse -Force
if ([Math]::Abs($ratio - 1.0) -gt $Tolerance) { Write-Host "FAIL: the radar is not shown as composed (colour space)." -ForegroundColor Red; exit 1 }
Write-Host 'ok' -ForegroundColor Green
exit 0
