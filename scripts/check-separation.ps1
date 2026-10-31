<#
.SYNOPSIS
    Proves that a package directory holds no original data: every file is classified as this project's code, the .NET runtime, a third-party package, or a
    document of the package - and none is a file of the game.

.DESCRIPTION
    For every file of -Directory:
      - its SHA-256 is compared (an empty file carries no data and is only counted) with every file of the master installation (baselines/steam-master.json), of the patch (baselines/patch-master.json) and, when this machine has it, every file of the
        exported content (game/content/content-manifest.json): an equal hash is a finding;
      - its extension is compared with the formats of the game's data (.big .w3d .map .csf .str .wnd .ini .scb .tga .dds .wav .mp3 .bik .wak .ani): a file of such a
        format is a finding unless the table of known files below names it with its origin;
      - it is classified: own (CnCNew.*), runtime (the .NET runtime and its native parts), third-party (everything else a publish copies from a package),
        document (*.md, *.txt, *.cmd, a sample configuration, the licences folder).
    Writes the classification as a table (counts and bytes per class) and, with -List FILE, every file with its class and hash.
    Exit code 0 when nothing is found, 1 otherwise.

.EXAMPLE
    pwsh scripts/check-separation.ps1 -Directory artifacts/client/cncnew-win-x64
#>
param(
    [Parameter(Mandatory)][string]$Directory,
    [string]$List
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$Directory = (Resolve-Path -LiteralPath $Directory).Path

$gameHashes = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($description in 'baselines/steam-master.json', 'baselines/patch-master.json') {
    foreach ($file in (Get-Content -Raw $description | ConvertFrom-Json).files) { [void]$gameHashes.Add([string]$file.sha256) }
}
$contentManifest = 'game/content/content-manifest.json'
$withContent = Test-Path $contentManifest
foreach ($manifest in $contentManifest, 'game/original/content/content-manifest.json') {
    if (Test-Path $manifest) { foreach ($asset in (Get-Content -Raw $manifest | ConvertFrom-Json).assets) { [void]$gameHashes.Add([string]$asset.sha256) } }
}

$gameFormats = '.big', '.w3d', '.map', '.csf', '.str', '.wnd', '.ini', '.scb', '.tga', '.dds', '.wav', '.mp3', '.bik', '.wak', '.ani'
# Files of a data format that are not the game's, with their origin. Empty today; a new entry names where the file comes from.
$knownFormatFiles = @{}
$runtimeNames = '^(System\.|Microsoft\.(?!.*Stride)|mscorlib|netstandard|WindowsBase|PresentationCore|coreclr|clrjit|clrgc|hostfxr|hostpolicy|clretwrc|mscordaccore|mscordbi|mscorrc|createdump|api-ms-|ucrtbase|Microsoft\.DiaSymReader|libclrjit|libcoreclr|libhostfxr|libhostpolicy|libSystem\.|libmscordaccore|libmscordbi|libclrgc)'

$findings = 0
$empty = 0
$classes = [ordered]@{ own = @(0, 0L); runtime = @(0, 0L); 'third-party' = @(0, 0L); document = @(0, 0L) }
$lines = New-Object System.Collections.Generic.List[string]
foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $name = $file.Name
    $class = if ($relative -like 'licences/*' -or $file.Extension -in '.md', '.txt', '.cmd' -or $name -like '*.sample.json') { 'document' }
             elseif ($name -like 'CnCNew.*') { 'own' }
             elseif ($name -match $runtimeNames) { 'runtime' }
             else { 'third-party' }
    $classes[$class][0]++
    $classes[$class][1] += $file.Length
    $lines.Add("$class`t$hash`t$($file.Length)`t$relative")
    # An empty file carries no data; its hash is the hash of every empty file (the game has some, so do packages): counted, not compared.
    if ($file.Length -eq 0) { $empty++ }
    elseif ($gameHashes.Contains($hash)) { Write-Host "FOUND: $relative is a file of the game (SHA-256 $hash)"; $findings++ }
    if ($gameFormats -contains $file.Extension.ToLowerInvariant() -and -not $knownFormatFiles.ContainsKey($relative)) { Write-Host "FOUND: $relative has a format of the game's data"; $findings++ }
}
Write-Host ("{0}: {1} files ({4} empty, not compared) compared with {2:N0} files of the game ({3})" -f $Directory, $lines.Count, $gameHashes.Count, $(if ($withContent) { 'master installation and its export' } else { 'master installation only; no game/content here' }), $empty)
foreach ($entry in $classes.GetEnumerator()) { Write-Host ("  {0,-12} {1,6} files {2,10:N1} MB" -f $entry.Key, $entry.Value[0], ($entry.Value[1] / 1MB)) }
if ($List) { [IO.File]::WriteAllLines([IO.Path]::GetFullPath($List), $lines) }
if ($findings -gt 0) { Write-Host "check-separation: $findings finding(s)"; exit 1 }
Write-Host 'check-separation: ok - no file of the game, no file of a game data format'
exit 0
