<#
.SYNOPSIS
    Writes the CHANGELOG.md section of one version to a file (the text of a release page).

.DESCRIPTION
    The release pipelines call this with the version of the tag. The section is the one scripts/release.ps1 wrote ("## [X.Y.Z] - date"); when there is none
    (a tag made by hand) the notes say so instead of failing, so a release is never stopped by missing notes.

.EXAMPLE
    pwsh scripts/release-notes.ps1 -Version 0.2.0 -OutFile artifacts/release-notes.md
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$changelog = Join-Path $root 'CHANGELOG.md'
$text = "CnCNew $Version`n"
if (Test-Path -LiteralPath $changelog) {
    $all = [IO.File]::ReadAllText($changelog) -replace "`r`n", "`n"
    $start = $all.IndexOf("`n## [$Version]")
    if ($start -ge 0) {
        $start += 1
        $end = $all.IndexOf("`n## [", $start)
        $section = if ($end -ge 0) { $all.Substring($start, $end - $start) } else { $all.Substring($start) }
        $text = $section.TrimEnd() + "`n"
    } else {
        $text += "`nNo CHANGELOG.md entry for this version.`n"
    }
}
$text += "`nYou need your own copy of Command & Conquer Generals - Zero Hour (English, Steam); the release holds no game data. See README.md.`n"
$dir = Split-Path -Parent ([IO.Path]::GetFullPath($OutFile))
New-Item -ItemType Directory -Force -Path $dir | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutFile), $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Release notes for $Version written to $OutFile"
