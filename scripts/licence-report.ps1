<#
.SYNOPSIS
    Writes docs/THIRD-PARTY.md: every NuGet package any project of the repository uses (directly or transitively), with its version, its licence as the
    package declares it, and which programs carry it.

.DESCRIPTION
    Calls `dotnet list <target> package --include-transitive --format json` for the solution, the Stride render tool outside it and the dedicated server, and
    reads each package's licence from its .nuspec in the NuGet package folder (`<license type="expression">`, else `<license type="file">`, else `<licenseUrl>`).
    A package whose licence cannot be read is written as UNKNOWN and makes the script fail - an unknown licence is never assumed.

    The programs a package belongs to are read from the project names: the client (CnCNew.Client.Stride and the libraries it references), the dedicated
    server (CnCNew.DedicatedServer: the script fails if it has any package - it is base class library and own code only), tools, tests.

    A package that declares no licence at all is UNKNOWN unless the table $named below names its licence with the place that states it (a reasoned entry, never
    a guess). -CollectTo DIR copies the licence and notice files each package of the client carries (LICENSE*, LICENCE*, NOTICE*, THIRD*PARTY*) into
    DIR/<package>/ - what scripts/package-client.ps1 puts beside the game.

    -Check compares with the committed file instead of writing it (exit code 1 on a difference). The output is sorted and carries no date.

.EXAMPLE
    pwsh scripts/licence-report.ps1
    pwsh scripts/licence-report.ps1 -Check
#>
param([switch]$Check, [string]$CollectTo)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$output = Join-Path $repo 'docs/THIRD-PARTY.md'
$packagesRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }

# Packages that declare no licence, with the licence their source states and where it says so.
$named = @{
    'Stride.SharpFont' = 'MIT, not declared by the package: the Stride repository''s "THIRD PARTY.md" names SharpFont (MIT) and the FreeType library it binds (FreeType License)'
}
$targets = @('CnCNew.slnx', 'tools/CnCNew.UiRenderTool/CnCNew.UiRenderTool.csproj', 'tools/CnCNew.DedicatedServer/CnCNew.DedicatedServer.csproj')
$uses = @{}       # "id|version" -> set of project names
$direct = @{}     # "id|version" -> true when some project references it directly
foreach ($target in $targets) {
    $json = dotnet list $target package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "dotnet list failed for $target (exit code $LASTEXITCODE) - restore first." }
    $report = ($json -join "`n") | ConvertFrom-Json
    foreach ($project in $report.projects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project.path)
        foreach ($framework in @($project.frameworks)) {
            foreach ($kind in 'topLevelPackages', 'transitivePackages') {
                foreach ($package in @($framework.$kind)) {
                    if (-not $package) { continue }
                    $key = "$($package.id)|$($package.resolvedVersion)"
                    if (-not $uses.ContainsKey($key)) { $uses[$key] = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal) }
                    [void]$uses[$key].Add($name)
                    if ($kind -eq 'topLevelPackages') { $direct[$key] = $true }
                }
            }
        }
    }
}

function Program-Of([string]$project) {
    if ($project -like 'CnCNew.DedicatedServer') { return 'server' }
    if ($project -like '*.Tests') { return 'tests' }
    if ($project -like 'CnCNew.Client.Stride' -or $project -like 'CnCNew.UiRenderTool') { return 'client' }
    if ($project -like 'CnCNew.*Tool' -or $project -like 'CnCNew.*Tool*') { return 'tools' }
    return 'libraries'
}

$rows = @()
$unknown = 0
$serverPackages = 0
foreach ($key in ($uses.Keys | Sort-Object { $_.ToLowerInvariant() })) {
    $id, $version = $key -split '\|'
    $nuspec = Join-Path $packagesRoot (Join-Path $id.ToLowerInvariant() (Join-Path $version.ToLowerInvariant() "$($id.ToLowerInvariant()).nuspec"))
    $licence = 'UNKNOWN'
    if (Test-Path $nuspec) {
        [xml]$document = Get-Content -Raw $nuspec
        $metadata = $document.package.metadata
        if ($metadata.license) {
            $value = if ($metadata.license -is [string]) { $metadata.license } else { $metadata.license.'#text' }
            $type = if ($metadata.license -is [string]) { 'expression' } else { $metadata.license.type }
            $licence = if ($type -eq 'file') { "file in the package: $value" } else { $value }
        }
        elseif ($metadata.licenseUrl) { $licence = "URL: $($metadata.licenseUrl)" }
    }
    if ($licence -eq 'UNKNOWN' -and $named.ContainsKey($id)) { $licence = $named[$id] }
    if ($licence -eq 'UNKNOWN') { $unknown++ }
    $projects = @($uses[$key])
    $programs = @($projects | ForEach-Object { Program-Of $_ } | Sort-Object -Unique)
    if ($programs -contains 'server') { $serverPackages++ }
    if ($CollectTo -and $programs -contains 'client') {
        $folder = Split-Path -Parent $nuspec
        $notices = @(Get-ChildItem -LiteralPath $folder -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(LICEN[CS]E|NOTICE|THIRD.?PARTY)' })
        if ($notices.Count -gt 0) {
            $into = Join-Path $CollectTo $id
            New-Item -ItemType Directory -Force -Path $into | Out-Null
            foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $into }
        }
    }
    $rows += "| $id | $version | $licence | $(if ($direct[$key]) { 'direct' } else { 'transitive' }) | $($programs -join ', ') |"
}

$lines = @(
    '# Third-party packages and their licences',
    '',
    'Generated by `pwsh scripts/licence-report.ps1` (do not edit by hand; `-Check` compares). Every NuGet package a project of this repository uses, directly or',
    'transitively, with the licence its package declares (`.nuspec`). "Programs" says what carries it: the **client** (the game, with the Stride engine), the **server**',
    '(none: the dedicated server is the base class library and this project''s code only), **tools**, **tests** and the shared **libraries**. A package that',
    'declares no licence is named with the licence its source states and where. The client package carries the licence and notice files the packages ship',
    '(`licences/`). The native FreeType library of the client is under the FreeType License: portions of this software are copyright The FreeType Project',
    '(www.freetype.org). The .NET runtime of a',
    'self-contained package is Microsoft''s (MIT); the licence of this repository is in `LICENSE.md`, its notice and the terms of the Electronic Arts source release',
    'in `NOTICE.md`; packaged programs carry both.',
    '',
    "Packages: $($rows.Count), of them used by the server: $serverPackages, with a licence that could not be read: $unknown.",
    '',
    '| Package | Version | Licence (as declared) | Reference | Programs |',
    '| --- | --- | --- | --- | --- |'
) + $rows
$text = ($lines -join "`n") + "`n"

if ($CollectTo) { Write-Host "Collected the licence files of the client's packages under $CollectTo." }
if ($Check) {
    $committed = if (Test-Path $output) { [IO.File]::ReadAllText($output) } else { '' }
    if ($committed -ne $text) { Write-Host 'docs/THIRD-PARTY.md differs from what the packages declare now - run scripts/licence-report.ps1 and commit the result.'; exit 1 }
    Write-Host "licence-report: docs/THIRD-PARTY.md is current ($($rows.Count) packages)."
}
else {
    [IO.File]::WriteAllText($output, $text, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Wrote $output ($($rows.Count) packages, $unknown unknown licence(s))."
}
if ($unknown -gt 0) { Write-Host "$unknown package(s) without a readable licence."; exit 1 }
if ($serverPackages -gt 0) { Write-Host "The dedicated server uses $serverPackages package(s); it must use none."; exit 1 }
exit 0
