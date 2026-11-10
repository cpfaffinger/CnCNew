<#
.SYNOPSIS
    Draws the game's own emblem and writes Assets/cncnew.ico: PNG-compressed images of 16, 24, 32, 48, 64, 128 and 256 pixels.

.DESCRIPTION
    The emblem is this project's own artwork, drawn from primitives: a dark round-cornered shield with an amber rim, three amber chevrons (an insignia of rank) and a small
    star above them. It is of the same family as the public launcher's badge (tools/CnCNew.Launcher/Assets/make-icon.ps1: hexagon, amber "C", crosshair) and not taken
    from the game: no logo of Electronic Arts or of Command & Conquer. Run it once after changing the emblem: pwsh src/CnCNew.Client.Stride/Assets/make-icon.ps1
#>
param([string]$Out = (Join-Path $PSScriptRoot 'cncnew.ico'))
Add-Type -AssemblyName System.Drawing

function New-Emblem([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 100.0
    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($x * $s)), ([single]($y * $s)) }
    # The shield: flat top with cut corners, pointed bottom.
    [System.Drawing.PointF[]]$outer = @((P 18 6), (P 82 6), (P 92 16), (P 92 56), (P 50 95), (P 8 56), (P 8 16))
    [System.Drawing.PointF[]]$inner = @((P 21 13), (P 79 13), (P 85 19), (P 85 54), (P 50 87), (P 15 54), (P 15 19))
    $back = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P 100 100), ([System.Drawing.Color]::FromArgb(255, 36, 48, 65)), ([System.Drawing.Color]::FromArgb(255, 15, 20, 27))
    $g.FillPolygon($back, $outer)
    $rim = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P 100 100), ([System.Drawing.Color]::FromArgb(255, 255, 214, 120)), ([System.Drawing.Color]::FromArgb(255, 214, 140, 30))
    $rimPen = New-Object System.Drawing.Pen $rim, ([single](4 * $s))
    $rimPen.LineJoin = 'Round'
    $g.DrawPolygon($rimPen, $outer)
    $thin = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 60, 74, 94)), ([single]([Math]::Max(1, 1.5 * $s)))
    $g.DrawPolygon($thin, $inner)
    $amber = [System.Drawing.Color]::FromArgb(255, 242, 179, 61)
    $chevron = New-Object System.Drawing.Pen $amber, ([single](7 * $s))
    $chevron.StartCap = 'Round'; $chevron.EndCap = 'Round'; $chevron.LineJoin = 'Round'
    foreach ($top in 35, 48, 61) {
        [System.Drawing.PointF[]]$points = @((P 32 ($top + 11)), (P 50 $top), (P 68 ($top + 11)))
        $g.DrawLines($chevron, $points)
    }
    # A five-pointed star above the chevrons.
    [System.Drawing.PointF[]]$star = foreach ($i in 0..9) {
        $radius = if ($i % 2 -eq 0) { 9.5 } else { 4 }
        $angle = ([Math]::PI / 5) * $i - [Math]::PI / 2
        P (50 + $radius * [Math]::Cos($angle)) (24 + $radius * [Math]::Sin($angle))
    }
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $amber), $star)
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
