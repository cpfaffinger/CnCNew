<#
.SYNOPSIS
    Prepares a release (docs/RELEASING.md): raises version.txt, writes the CHANGELOG.md entry, commits "chore(release): vX.Y.Z" and creates the annotated tag vX.Y.Z.

.DESCRIPTION
    The release pipelines (.gitlab-ci.yml, .github/workflows/release.yml) build and publish when a tag vMAJOR.MINOR.PATCH is pushed. This script makes that tag. It

      1. checks that the working tree is clean, the branch is master, the version is a plain MAJOR.MINOR.PATCH and higher than version.txt, and the tag does not exist;
      2. writes version.txt;
      3. adds a section to CHANGELOG.md (Keep a Changelog style) from the Conventional Commit subjects since the last tag v*
         (all commits when there is no tag yet; at most -MaxPerSection lines per heading);
      4. commits version.txt and CHANGELOG.md as "chore(release): vX.Y.Z";
      5. creates the annotated tag vX.Y.Z on that commit.

    It NEVER pushes. The person who releases pushes the commit and the tag themselves ("git push origin master vX.Y.Z"), after the checklist in docs/RELEASING.md.
    With -DryRun it does the checks, prints the CHANGELOG section it would write and changes nothing.

.EXAMPLE
    pwsh scripts/release.ps1 -Version 0.2.0 -DryRun
    pwsh scripts/release.ps1 -Version 0.2.0
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$DryRun,
    [string]$Branch = 'master',
    [int]$MaxPerSection = 100
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Fail([string]$message) { Write-Host "Release refused: $message"; exit 1 }

# --- the checks
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { Fail "'$Version' is not MAJOR.MINOR.PATCH (no 'v' in front, no suffix)." }
$new = [version]$Version
$tag = "v$Version"

$versionFile = Join-Path $root 'version.txt'
$current = if (Test-Path -LiteralPath $versionFile) { (Get-Content -LiteralPath $versionFile -Raw).Trim() } else { '0.0.0' }
if ($current -notmatch '^\d+\.\d+\.\d+$') { Fail "version.txt holds '$current', not MAJOR.MINOR.PATCH." }
if ($new -le [version]$current) { Fail "$Version is not higher than the current version $current." }

$branchNow = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branchNow -ne $Branch) { Fail "the branch is '$branchNow', a release is made on '$Branch'." }
if (git status --porcelain) { Fail 'the working tree is not clean (git status --porcelain is not empty).' }
if (git tag --list $tag) { Fail "the tag $tag exists already." }

# --- the commits since the last tag
$lastTag = (git describe --tags --abbrev=0 --match 'v[0-9]*' 2>$null)
if ($LASTEXITCODE -ne 0) { $lastTag = $null }
$range = if ($lastTag) { "$($lastTag.Trim())..HEAD" } else { 'HEAD' }
$subjects = @(git log --no-merges --format=%s $range)

# Conventional Commit type -> Keep a Changelog heading. Types not listed (chore, build, ci, style) are left out of the notes.
$headings = [ordered]@{
    'Added'         = @('feat')
    'Fixed'         = @('fix')
    'Changed'       = @('perf', 'refactor', 'revert')
    'Documentation' = @('docs')
    'Tests'         = @('test')
}
$sections = @{}
foreach ($name in $headings.Keys) { $sections[$name] = New-Object System.Collections.Generic.List[string] }
$skipped = 0
foreach ($subject in $subjects) {
    if ($subject -notmatch '^(?<type>[a-z]+)(\((?<scope>[^)]*)\))?(?<breaking>!)?:\s*(?<text>.+)$') { $skipped++; continue }
    $heading = $null
    foreach ($name in $headings.Keys) { if ($headings[$name] -contains $Matches['type']) { $heading = $name } }
    if (-not $heading) { $skipped++; continue }
    $line = if ($Matches['scope']) { "**$($Matches['scope'])**: $($Matches['text'])" } else { $Matches['text'] }
    if ($Matches['breaking']) { $line = "BREAKING: $line" }
    $sections[$heading].Add($line)
}

$date = Get-Date -Format 'yyyy-MM-dd'
$entry = New-Object System.Text.StringBuilder
[void]$entry.Append("## [$Version] - $date`n`n")
$any = $false
foreach ($name in $headings.Keys) {
    $lines = $sections[$name]
    if ($lines.Count -eq 0) { continue }
    $any = $true
    [void]$entry.Append("### $name`n`n")
    foreach ($line in ($lines | Select-Object -First $MaxPerSection)) { [void]$entry.Append("- $line`n") }
    if ($lines.Count -gt $MaxPerSection) { [void]$entry.Append("- ... and $($lines.Count - $MaxPerSection) more; see the git history.`n") }
    [void]$entry.Append("`n")
}
if (-not $any) { [void]$entry.Append("No user-visible changes recorded.`n`n") }
$entryText = $entry.ToString()

Write-Host ("Release {0} -> {1} on {2}; {3} commit subjects since {4} ({5} not in the notes)." -f $current, $Version, $Branch, $subjects.Count, ($(if ($lastTag) { $lastTag.Trim() } else { 'the beginning' })), $skipped)
Write-Host '--- CHANGELOG section'
Write-Host $entryText
if ($DryRun) {
    Write-Host '--- Dry run: nothing was changed. A real run would write version.txt and CHANGELOG.md, commit them as'
    Write-Host "    'chore(release): $tag' and create the annotated tag $tag. It does not push."
    exit 0
}

# --- the changes (UTF-8 without BOM, LF)
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($versionFile, "$Version`n", $utf8)

$changelogFile = Join-Path $root 'CHANGELOG.md'
$header = "# Changelog`n`nAll notable changes of CnCNew releases are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);`nthe versions follow [Semantic Versioning](https://semver.org/). Entries are made by ``scripts/release.ps1`` from the Conventional Commit subjects.`n`n"
if (Test-Path -LiteralPath $changelogFile) {
    $existing = [IO.File]::ReadAllText($changelogFile)
    $first = $existing.IndexOf("`n## [")
    $body = if ($first -ge 0) { $existing.Substring($first + 1) } else { '' }
    $head = if ($first -ge 0) { $existing.Substring(0, $first + 1) } else { $existing.TrimEnd() + "`n`n" }
    [IO.File]::WriteAllText($changelogFile, $head + $entryText + $body, $utf8)
} else {
    [IO.File]::WriteAllText($changelogFile, $header + $entryText.TrimEnd() + "`n", $utf8)
}

git add -- version.txt CHANGELOG.md
if ($LASTEXITCODE -ne 0) { Fail 'git add failed.' }
git commit -m "chore(release): $tag"
if ($LASTEXITCODE -ne 0) { Fail 'git commit failed.' }
git tag -a $tag -m "CnCNew $Version"
if ($LASTEXITCODE -ne 0) { Fail "git tag $tag failed (the release commit exists; fix and tag it by hand)." }

Write-Host ''
Write-Host "Made the commit and the annotated tag $tag. Nothing was pushed."
Write-Host "To publish (the pipelines start on the tag):  git push origin $Branch $tag"
exit 0
