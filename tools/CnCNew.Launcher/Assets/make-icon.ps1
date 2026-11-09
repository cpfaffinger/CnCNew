<#
.SYNOPSIS
    Draws the launcher's emblem (the same shapes as Emblem.xaml) and writes Assets/launcher.ico: PNG-compressed images of 16, 24, 32, 48, 64, 128 and 256 pixels.

.DESCRIPTION
    The emblem is this project's own artwork - a hexagonal badge with an amber "C" and a crosshair - drawn from primitives. It is not taken from the game. Run it once after
    changing the emblem: pwsh tools/CnCNew.Launcher/Assets/make-icon.ps1
#>
param([string]$Out = (Join-Path $PSScriptRoot 'launcher.ico'))
Add-Type -AssemblyName System.Drawing

function New-Emblem([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 100.0
    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($x * $s)), ([single]($y * $s)) }
    [System.Drawing.PointF[]]$outer = @((P 50 4), (P 90 27), (P 90 73), (P 50 96), (P 10 73), (P 10 27))
    [System.Drawing.PointF[]]$inner = @((P 50 12), (P 83 31), (P 83 69), (P 50 88), (P 17 69), (P 17 31))
    $back = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P 100 100), ([System.Drawing.Color]::FromArgb(255, 36, 48, 65)), ([System.Drawing.Color]::FromArgb(255, 15, 20, 27))
    $g.FillPolygon($back, $outer)
    $rim = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P 100 100), ([System.Drawing.Color]::FromArgb(255, 255, 214, 120)), ([System.Drawing.Color]::FromArgb(255, 214, 140, 30))
    $rimPen = New-Object System.Drawing.Pen $rim, ([single](4 * $s))
    $rimPen.LineJoin = 'Round'
    $g.DrawPolygon($rimPen, $outer)
    $thin = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 60, 74, 94)), ([single]([Math]::Max(1, 1.5 * $s)))
    $g.DrawPolygon($thin, $inner)
    $amber = [System.Drawing.Color]::FromArgb(255, 242, 179, 61)
    $arc = New-Object System.Drawing.Pen $amber, ([single](9 * $s))
    $arc.StartCap = 'Round'; $arc.EndCap = 'Round'
    $g.DrawArc($arc, [single](28 * $s), [single](28 * $s), [single](44 * $s), [single](44 * $s), [single]40, [single]280)
    $tick = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 169, 180, 194)), ([single]([Math]::Max(1, 1.6 * $s)))
    foreach ($line in @(@(50, 19, 50, 27), @(50, 73, 50, 81), @(21, 50, 29, 50))) { $g.DrawLine($tick, (P $line[0] $line[1]), (P $line[2] $line[3])) }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $amber), [single](72 * $s - 4 * $s), [single](50 * $s - 4 * $s), [single](8 * $s), [single](8 * $s))
    $g.Dispose()
    return $bitmap
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-Emblem $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size }))); $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes($Out, $file.ToArray())
Write-Host "Wrote $Out ($((Get-Item $Out).Length) bytes)"
