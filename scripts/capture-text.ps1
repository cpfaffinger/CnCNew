<#
.SYNOPSIS
  Nimmt den Textaufnahmesatz auf (Phase 16, Punkt 4) und schreibt PNGs, den Schriftvergleich und eine Pruefsummenliste.

.DESCRIPTION
  Drei Teile, alle reproduzierbar:
   1. tools/CnCNew.UiRenderTool: Schriftvergleich GDI gegen FreeType auf allen Originallayouts (`font-audit.txt`), dann die feste Szenenmenge in
      1280x720 und 1920x1080 (Hauptmenue, Optionen, Skirmish-Einrichtung, Kartenwahl, Ladeschirm, Abschlussbildschirm, Kampagnenbildschirme,
      Zeigertooltip) sowie die rohen Layouts in vier Aufloesungen.
   2. Stride-Client `--smoke-test --capture`: Gefecht mit der Befehlsleiste (Geld, Auswahl), und der Zeigertooltip eines Befehlsknopfs ueber ein
      Eingabeskript (`--input-script`).
   3. Eine Pruefsummenliste (SHA-256 ueber die dekodierten Pixel) je Bild: sie belegt Reproduzierbarkeit, nicht Richtigkeit. Die Bilder gehoeren
      NICHT ins Repo (Originalassets); der Standardausgabeort ist ein Verzeichnis unter %TEMP%. Ein Vergleich mit dem Original ist nur durch einen
      Menschen moeglich (docs/TEXT-AUDIT.md, Abschnitt "Was nicht belegt ist").

.EXAMPLE
  .\scripts\capture-text.ps1
  .\scripts\capture-text.ps1 -OutDir D:\captures\text1 -NoBuild
#>
[CmdletBinding()]
param(
    [string]$ContentDir,
    [string]$OutDir,
    # Simulationsrate der beiden Client-Szenen (30, 60 oder 120 Hz); 0 = Standard des Clients. Das UiRenderTool baut keine Welt und kennt keine Rate.
    [int]$TickRate = 0,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ContentDir) { $ContentDir = Join-Path $repo '.local\phase2-import-a' }
if (-not (Test-Path -LiteralPath $ContentDir -PathType Container)) { throw "Inhaltsverzeichnis nicht gefunden: $ContentDir" }
$ContentDir = (Resolve-Path -LiteralPath $ContentDir).Path
if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-text-captures' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$tool = Join-Path $repo 'tools\CnCNew.UiRenderTool\CnCNew.UiRenderTool.csproj'
$client = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
if (-not $NoBuild) {
    Write-Host '== Baue UiRenderTool und Client (Release) ...' -ForegroundColor Cyan
    dotnet build $tool -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build des UiRenderTool fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    dotnet build $client -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build des Clients fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}

$toolDir = Join-Path $OutDir 'ui'
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
Write-Host '== Schriftvergleich GDI/FreeType' -ForegroundColor Cyan
dotnet run --project $tool -c Release --no-build -- --content-dir $ContentDir --out-dir $toolDir --font-audit | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Schriftvergleich fehlgeschlagen (Exitcode $LASTEXITCODE)." }
Write-Host '== Szenen des UiRenderTool (GDI)' -ForegroundColor Cyan
dotnet run --project $tool -c Release --no-build -- --content-dir $ContentDir --out-dir $toolDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "UiRenderTool fehlgeschlagen (Exitcode $LASTEXITCODE)." }

# Eingabeskript: Planierraupe waehlen, auf den ersten Baubefehl zeigen (Tooltip nach TOOLTIPDELAY 1 ms und 250 ms Einblendung).
$scriptPath = Join-Path $OutDir 'tooltip-input.txt'
[IO.File]::WriteAllLines($scriptPath, @('10 click left own:DOZER', '40 move command:DozerConstruct'))

$alpine = 'Maps/Alpine Assault/Alpine Assault.map'
$clientDir = Join-Path $OutDir 'client'
New-Item -ItemType Directory -Force -Path $clientDir | Out-Null
$scenes = @(
    @('hud-money', @('--match', $alpine, '--frames', '120', '--pitch', '30', '--height', '250', '--select-own')),
    @('hud-tooltip', @('--match', $alpine, '--frames', '120', '--pitch', '30', '--height', '250', '--input-script', $scriptPath))
)
foreach ($size in @('1280x720', '1920x1080')) {
    foreach ($scene in $scenes) {
        $png = Join-Path $clientDir "$($scene[0])-$size.png"
        if (Test-Path -LiteralPath $png) { Remove-Item -LiteralPath $png }
        Write-Host "== $($scene[0]) $size" -ForegroundColor Cyan
        $clientArgs = @('--smoke-test', '--content-dir', $ContentDir, '--size', $size) + $scene[1] + @('--capture', $png)
        if ($TickRate -gt 0) { $clientArgs += @('--tick-rate', "$TickRate") }
        dotnet run --project $client -c Release --no-build -- @clientArgs | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $png)) { throw "Szene '$($scene[0])' fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    }
}

# SHA-256 of the decoded pixels, not of the PNG file (the encoder's output differs between runs for identical pixels).
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
$manifest.Add('# cncnew text capture set, VORLAEUFIGE Pruefsummen ueber die Pixel (Reproduzierbarkeit, nicht Richtigkeit)')
foreach ($file in (Get-ChildItem -LiteralPath $toolDir, $clientDir -Filter '*.png' | Sort-Object { $_.Directory.Name }, Name)) {
    $manifest.Add("$(Get-PixelHash $file.FullName)  $($file.Directory.Name)/$($file.Name)")
}
$manifestPath = Join-Path $OutDir 'manifest.txt'
[IO.File]::WriteAllLines($manifestPath, $manifest)
Write-Host "`nAufnahmen: $OutDir ($($manifest.Count - 1) Bilder)" -ForegroundColor Green
Write-Host "Schriftvergleich: $(Join-Path $toolDir 'font-audit.txt')"
