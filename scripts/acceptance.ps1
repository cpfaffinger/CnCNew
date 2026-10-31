#requires -Version 7
<#
.SYNOPSIS
  Guided acceptance by a human (docs/ACCEPTANCE.md): shows the scene for every acceptance item, asks for the verdict and records it outside the repository.

.DESCRIPTION
  The catalogue is scripts/acceptance/catalogue.json (items with a stable id, groups, scenes); the agent's pre-check is scripts/acceptance/precheck.json.

    -Suite                THE WAY TO DO THE ACCEPTANCE (Start-Acceptance.cmd): a window leads through the packages one after the other, starts every scene by itself and asks for every named
                          goal: reached, not reached (with a comment) or skip. Progress is saved after every scene; stop whenever you like and start it again. At the end the report lies on the desktop.
    -List                 groups and items with id, minutes and totals; starts nothing.
    (no switch)           guided run through all open items that need motion or sound: one window per scene (60 Hz), then one key per item.
    -Stills               opens the picture page (index.html made by -Capture) and asks for the items that can be judged from still pictures.
    -Item <id,...>        only these items;  -Group <group>  only this group (ids such as INST, HUD, UNIT ...).
    -Compare              shows every scene once more at 30 Hz afterwards (with -SideBySide both windows at once, at three quarters of the size).
    -Capture [-All]       no window, no human: film strips of every scene at 60 and 30 Hz, sound captures, the scene audit, and a picture page index.html.
    -Report               a Markdown summary (passed / failed with note / skipped / open), on the console and into a file.
    -Resume               continues the most recent results file instead of today's.
    -IncludeOptional      takes the optional items into the guided run as well (-List marks them "opt").

  Keys after every scene, per item:  p pass   f fail (then one line of text)   s skip   r show the scene again   c compare at 30 Hz   q save and quit

  The script NEVER writes into the repository and never touches the Steam installation. Results: %LOCALAPPDATA%\CnCNew\acceptance\results-<date>.json (-ResultsPath);
  pictures and sound: %TEMP%\cncnew-acceptance (-OutDir). The picture page contains imagery of the game and must not be published or committed.
  Content: -ContentDir, else game\content of the developer checkout, else the installation made by scripts\install.ps1 (%LOCALAPPDATA%\CnCNew\Game, -InstallDir; found through install.json).

  Testable without a human: -Answers "p,f:too dark,s,q" supplies the keys (a note cannot contain a comma), -NoWindow starts no windows, -SelfTest runs a whole trial into a throw-away directory.

.EXAMPLE
  pwsh scripts/acceptance.ps1 -Capture -All      # unattended; look at index.html afterwards
  pwsh scripts/acceptance.ps1 -Stills            # verdicts for the still-picture items
  pwsh scripts/acceptance.ps1                    # the part in the window
  pwsh scripts/acceptance.ps1 -Report
  pwsh scripts/acceptance.ps1 -Item UNIT-04 -Capture
#>
[CmdletBinding()]
param(
    [switch]$Suite,
    # -Suite only: draws the first page into this picture and ends (the check that the window is complete).
    [string]$SuiteScreenshot,
    [switch]$List,
    [string[]]$Item,
    [string]$Group,
    [switch]$Stills,
    [switch]$Compare,
    [switch]$SideBySide,
    [switch]$Capture,
    [switch]$All,
    [switch]$Report,
    [switch]$Resume,
    [switch]$Fresh,
    [switch]$IncludeSkipped,
    # Also take the items marked optional (installer, uninstall, old saved game, classic mouse, further factions ...) into the guided run.
    [switch]$IncludeOptional,
    [string]$ResultsPath,
    [string]$ReportPath,
    [string]$OutDir,
    [string]$ContentDir,
    [string]$InstallDir,
    [ValidateSet(30, 60, 120)]
    [int]$TickRate = 60,
    [string]$Size,
    [switch]$Mute,
    [switch]$NoBuild,
    [switch]$UseInstalledClient,
    [switch]$KeepPng,
    [switch]$NoAudit,
    # -Capture only: just these rates (default: the catalogue's, 60 and 30).
    [int[]]$Rates,
    [string]$Answers,
    [switch]$NoWindow,
    [switch]$SelfTest,
    [int]$TimeoutSeconds = 1500
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
[Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$repo = Split-Path -Parent $PSScriptRoot
$utf8 = [Text.UTF8Encoding]::new($false)

function Get-FullPath([string]$Path) { [IO.Path]::GetFullPath($Path, (Get-Location).Path) }

# Nothing this script produces may land in the repository (results name a person's verdicts, captures contain game imagery) or in a Steam library.
function Assert-OutsideRepo([string]$Path, [string]$What) {
    $full = (Get-FullPath $Path).TrimEnd('\') + '\'
    $root = $repo.TrimEnd('\') + '\'
    if ($full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "$What must not lie inside the repository: $Path" }
    if ($full.IndexOf('\steamapps\', [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "$What must not lie inside a Steam library: $Path" }
}

# ------------------------------------------------------------------------------------------------------------------ catalogue

$catalogue = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'acceptance\catalogue.json') -Raw | ConvertFrom-Json
$preCheckFile = Join-Path $PSScriptRoot 'acceptance\precheck.json'
$preCheck = if (Test-Path -LiteralPath $preCheckFile) { Get-Content -LiteralPath $preCheckFile -Raw | ConvertFrom-Json } else { $null }
if (-not $Size) { $Size = $catalogue.defaults.size }
if ($Suite -and -not $Fresh) { $Resume = $true }

function Get-Scene([string]$Name) {
    $property = $catalogue.scenes.PSObject.Properties[$Name]
    if (-not $property) { throw "The catalogue has no scene '$Name'." }
    $property.Value
}
function Get-Prop($Object, [string]$Name) { if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { $Object.PSObject.Properties[$Name].Value } else { $null } }
function Test-Live($Entry) { [bool]$Entry.needsMotion -or [bool]$Entry.needsSound }
function Get-ItemScenes($Entry) { @($Entry.scene) + @(Get-Prop $Entry 'moreScenes') | Where-Object { $_ } }
function Get-PreCheck([string]$Id) { if ($preCheck) { Get-Prop $preCheck.items $Id } else { $null } }
function Format-PreCheck($Reading) {
    if (-not $Reading) { return 'none' }
    $word = switch ($Reading.reading) { 'right' { 'looks right' } 'wrong-fixed' { 'looked wrong, fixed' } 'wrong-open' { 'looks wrong, open' } 'cannot-judge' { 'cannot be judged from stills' } default { $Reading.reading } }
    $fixed = Get-Prop $Reading 'fixedIn'
    "$word$(if ($fixed) { " ($fixed)" })$(if ($Reading.note) { ": $($Reading.note)" })"
}

foreach ($entry in $catalogue.items) {
    foreach ($name in (Get-ItemScenes $entry)) { [void](Get-Scene $name) }
    if (-not ($catalogue.groups | Where-Object id -eq $entry.group)) { throw "Item $($entry.id): unknown group '$($entry.group)'." }
}
if (($catalogue.items.id | Sort-Object -Unique).Count -ne $catalogue.items.Count) { throw 'The catalogue contains an id twice.' }
foreach ($section in $catalogue.groups) { if (@($catalogue.packages | Where-Object { $_.groups -contains $section.id }).Count -ne 1) { throw "Group $($section.id) is not in exactly one package." } }

function Select-Items {
    $selected = @($catalogue.items)
    if ($Item) {
        $wanted = $Item | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        foreach ($id in $wanted) { if (-not ($selected | Where-Object id -eq $id)) { throw "Unknown item '$id' (see -List)." } }
        $selected = @($selected | Where-Object { $wanted -contains $_.id })
    }
    if ($Group) {
        if (-not ($catalogue.groups | Where-Object id -eq $Group)) { throw "Unknown group '$Group'. Known: $($catalogue.groups.id -join ', ')." }
        $selected = @($selected | Where-Object group -eq $Group)
    }
    , $selected
}

# ------------------------------------------------------------------------------------------------------------------ -List

if ($List) {
    $liveSum = 0.0; $stillSum = 0.0; $liveCount = 0; $stillCount = 0; $optionalSum = 0.0; $optionalCount = 0
    foreach ($section in $catalogue.groups) {
        $members = @($catalogue.items | Where-Object group -eq $section.id)
        $minutes = ($members | Measure-Object minutes -Sum).Sum
        Write-Host ("`n{0}  {1}  ({2} items, {3:0.0} min)" -f $section.id, $section.title, $members.Count, $minutes) -ForegroundColor Cyan
        foreach ($entry in $members) {
            $kind = if (Test-Live $entry) { 'window' } else { 'stills' }
            $need = "$(if ($entry.needsMotion) { 'M' } else { '-' })$(if ($entry.needsSound) { 'S' } else { '-' })"
            $optional = [bool](Get-Prop $entry 'optional')
            Write-Host ("  {0,-9} {1} {2} {3,4:0.0} min {4} {5}  [{6}]" -f $entry.id, $kind, $need, $entry.minutes, $(if ($optional) { 'opt' } else { '   ' }), $entry.title, $entry.scene)
            if ($optional) { $optionalSum += $entry.minutes; $optionalCount++ } elseif (Test-Live $entry) { $liveSum += $entry.minutes; $liveCount++ } else { $stillSum += $entry.minutes; $stillCount++ }
        }
    }
    Write-Host ("`n{0} items in {1} groups. In the window (M = needs motion, S = needs sound): {2} items, {3:0.0} min. From stills: {4} items, {5:0.0} min. Optional (opt, only with -IncludeOptional): {6} items, {7:0.0} min." -f $catalogue.items.Count, $catalogue.groups.Count, $liveCount, $liveSum, $stillCount, $stillSum, $optionalCount, $optionalSum) -ForegroundColor Green
    Write-Host 'The minutes are planned times: the loading time of the window (about half a minute, on the first item of every scene) plus the length of the scene or of the actions.'
    exit 0
}

# ------------------------------------------------------------------------------------------------------------------ self test

if ($SelfTest) {
    $work = Join-Path ([IO.Path]::GetTempPath()) ("cncnew-acceptance-selftest-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $work | Out-Null
    $results = Join-Path $work 'results.json'
    $self = $PSCommandPath
    $problems = @()
    function Invoke-Self([string[]]$Arguments) { $output = & pwsh -NoProfile -File $self @Arguments 2>&1; [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($output | Out-String) } }
    $common = @('-ResultsPath', $results, '-NoWindow', '-NoBuild', '-ContentDir', $work)
    # p, f with a note, s, r (asks the same item again), p, c (asks again), f, q
    $first = Invoke-Self (@('-Group', 'UNIT', '-Answers', 'p,f:too dark; and too fast,s,r,p,c,f:second note,q') + $common)
    if ($first.Code -ne 0) { $problems += "first run: exit code $($first.Code)`n$($first.Text)" }
    $data = if (Test-Path $results) { Get-Content $results -Raw | ConvertFrom-Json } else { $null }
    if (-not $data) { $problems += 'no results file after the first run' }
    else {
        $verdicts = ($data.entries | ForEach-Object { "$($_.id)=$($_.verdict)" }) -join ' '
        if ($verdicts -ne 'UNIT-01=pass UNIT-02=fail UNIT-03=skip UNIT-04=pass UNIT-05=fail') { $problems += "first run: verdicts '$verdicts'" }
        if (($data.entries | Where-Object id -eq 'UNIT-02').note -ne 'too dark; and too fast') { $problems += 'first run: the note of UNIT-02 was not kept' }
        foreach ($field in 'commit', 'tickRate', 'contentManifestSha256', 'timestamp', 'scene') { if (-not ($data.entries[0].PSObject.Properties[$field])) { $problems += "entry without '$field'" } }
        if (-not $data.PSObject.Properties['preCheck']) { $problems += 'results file without the preCheck field' }
        if ($data.entries | Where-Object { $_.PSObject.Properties['preCheck'] -or $_.verdict -notin 'pass', 'fail', 'skip' -or $_.by -ne 'human' }) { $problems += 'an entry carries something other than a human verdict' }
    }
    # Resume continues behind what has a verdict: the next open item of the group is UNIT-06.
    $second = Invoke-Self (@('-Group', 'UNIT', '-Resume', '-Answers', 'p,q') + $common)
    $data = Get-Content $results -Raw | ConvertFrom-Json
    if ($second.Code -ne 0 -or $data.entries.Count -ne 6 -or $data.entries[5].id -ne 'UNIT-06' -or $data.entries[5].verdict -ne 'pass') { $problems += "resume: exit $($second.Code), $($data.entries.Count) entries, last $($data.entries[-1].id)=$($data.entries[-1].verdict)`n$($second.Text)" }
    $reportFile = Join-Path $work 'report.md'
    $third = Invoke-Self @('-Report', '-ResultsPath', $results, '-ReportPath', $reportFile)
    $reportText = if (Test-Path $reportFile) { Get-Content $reportFile -Raw } else { '' }
    foreach ($needle in 'UNIT-02', 'too dark; and too fast', 'second note', '3 passed, 2 failed, 1 skipped', '## Open') { if ($reportText -notlike "*$needle*") { $problems += "report without '$needle'" } }
    if ($third.Code -ne 0) { $problems += "report: exit code $($third.Code)" }
    $refused = Invoke-Self @('-Report', '-ResultsPath', (Join-Path $repo 'results.json'))
    if ($refused.Code -eq 0) { $problems += 'a results path inside the repository was not refused' }
    # The suite: the same verdict file, package after package; a page stops at the first q and is asked again by the next run.
    $suiteResults = Join-Path $work 'suite.json'
    $suiteRun = Invoke-Self @('-Suite', '-Answers', 'p,f:the bar is cut off,s,q', '-ResultsPath', $suiteResults, '-NoWindow', '-NoBuild', '-ContentDir', $work, '-ReportPath', (Join-Path $work 'suite.md'))
    $suiteData = if (Test-Path $suiteResults) { Get-Content $suiteResults -Raw | ConvertFrom-Json } else { $null }
    if ($suiteRun.Code -ne 0 -or -not $suiteData -or $suiteData.entries.Count -ne 3 -or $suiteData.entries[1].verdict -ne 'fail' -or $suiteData.entries[1].note -ne 'the bar is cut off') { $problems += "suite: exit code $($suiteRun.Code), $(@($suiteData.entries).Count) entries`n$($suiteRun.Text)" }
    $suiteReport = if (Test-Path (Join-Path $work 'suite.md')) { Get-Content (Join-Path $work 'suite.md') -Raw } else { '' }
    foreach ($needle in '## Packages', 'the bar is cut off') { if ($suiteReport -notlike "*$needle*") { $problems += "suite report without '$needle'" } }
    $listed = Invoke-Self @('-List')
    if ($listed.Code -ne 0 -or $listed.Text -notlike '*items in*' -or $listed.Text -notlike '*UNIT-01*' -or $listed.Text -like '*(0 items*') { $problems += '-List failed' }
    if ($problems.Count -eq 0) { Write-Host "SELFTEST PASS (guided run with fed answers, resume, report, repository refused, list); files in $work" -ForegroundColor Green; exit 0 }
    Write-Host "SELFTEST FAIL:" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# ------------------------------------------------------------------------------------------------------------------ results

$resultsDir = Join-Path $env:LOCALAPPDATA 'CnCNew\acceptance'
if ($ResultsPath) { $ResultsPath = Get-FullPath $ResultsPath }
else {
    $today = Join-Path $resultsDir ("results-{0:yyyy-MM-dd}.json" -f (Get-Date))
    $latestFile = if (Test-Path -LiteralPath $resultsDir) { Get-ChildItem -LiteralPath $resultsDir -Filter 'results-*.json' | Sort-Object LastWriteTime | Select-Object -Last 1 } else { $null }
    $ResultsPath = if ($Fresh) { Join-Path $resultsDir ("results-{0:yyyy-MM-dd-HHmmss}.json" -f (Get-Date)) } elseif (($Resume -or $Report) -and $latestFile) { $latestFile.FullName } else { $today }
}
Assert-OutsideRepo $ResultsPath 'The results file'

function Read-Results {
    if (Test-Path -LiteralPath $ResultsPath) { return (Get-Content -LiteralPath $ResultsPath -Raw | ConvertFrom-Json) }
    [pscustomobject]@{ schemaVersion = 1; created = (Get-Date).ToString('o'); about = 'CnCNew acceptance: verdicts of a human (entries) and, separately, the readings of the agent before (preCheck). The latest entry of an item counts.'; preCheck = $preCheck; entries = @() }
}
function Save-Results($Data) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $ResultsPath) | Out-Null
    # The agent's readings travel with the file in their own field, never as an entry.
    $Data.preCheck = $preCheck
    $temporary = "$ResultsPath.tmp"
    [IO.File]::WriteAllText($temporary, ($Data | ConvertTo-Json -Depth 8), $utf8)
    Move-Item -LiteralPath $temporary -Destination $ResultsPath -Force
}
function Get-Latest($Data) { $map = @{}; foreach ($entry in $Data.entries) { $map[$entry.id] = $entry }; $map }

# ------------------------------------------------------------------------------------------------------------------ -Report

if ($Report) {
    $data = Read-Results
    $latest = Get-Latest $data
    $lines = [Collections.Generic.List[string]]::new()
    $by = { param($verdict) @($catalogue.items | Where-Object { $latest.ContainsKey($_.id) -and $latest[$_.id].verdict -eq $verdict }) }
    $passed = & $by 'pass'; $failed = & $by 'fail'; $skipped = & $by 'skip'
    $open = @($catalogue.items | Where-Object { -not $latest.ContainsKey($_.id) })
    $commits = @($data.entries.commit | Sort-Object -Unique) -join ', '
    $lines.Add('# CnCNew - acceptance report')
    $lines.Add('')
    $lines.Add("Results file: ``$ResultsPath``  ")
    $lines.Add("As of $((Get-Date).ToString('yyyy-MM-dd HH:mm')); commit(s) of the verdicts: $(if ($commits) { $commits } else { '-' }); content: $(@($data.entries.contentManifestSha256 | Sort-Object -Unique) -join ', ')")
    $lines.Add('')
    $lines.Add("**$($catalogue.items.Count) items: $($passed.Count) passed, $($failed.Count) failed, $($skipped.Count) skipped, $($open.Count) open.** The column 'pre-check' is the agent's reading of still pictures and numbers - not a human verdict.")
    $lines.Add('')
    $lines.Add('## Packages')
    $lines.Add('')
    $lines.Add('| Package | Reached | Not reached | Skipped | Open |')
    $lines.Add('| --- | --- | --- | --- | --- |')
    foreach ($package in $catalogue.packages) {
        $members = @($catalogue.items | Where-Object { $package.groups -contains $_.group })
        $count = { param($verdict) @($members | Where-Object { $latest.ContainsKey($_.id) -and $latest[$_.id].verdict -eq $verdict }).Count }
        $lines.Add("| $($package.title) | $(& $count 'pass') | $(& $count 'fail') | $(& $count 'skip') | $(@($members | Where-Object { -not $latest.ContainsKey($_.id) }).Count) |")
    }
    $lines.Add('')
    $lines.Add('## Failed')
    $lines.Add('')
    if ($failed.Count -eq 0) { $lines.Add('- none -') }
    else {
        $lines.Add('| Item | Title | Note of the human | Scene | Rate | Reproduce without a window | Pre-check of the agent |')
        $lines.Add('| --- | --- | --- | --- | --- | --- | --- |')
        foreach ($entry in $failed) {
            $verdict = $latest[$entry.id]
            $lines.Add("| $($entry.id) | $($entry.title) | $(($verdict.note -replace '\|', '/')) | ``$($entry.scene)`` | $($verdict.tickRate) Hz | ``pwsh scripts/acceptance.ps1 -Item $($entry.id) -Capture`` | $((Format-PreCheck (Get-PreCheck $entry.id)) -replace '\|', '/') |")
        }
    }
    $commented = @($catalogue.items | Where-Object { $latest.ContainsKey($_.id) -and $latest[$_.id].verdict -ne 'fail' -and $latest[$_.id].note })
    if ($commented.Count -gt 0) {
        $lines.Add('')
        $lines.Add('## Comments on goals that were reached or skipped')
        $lines.Add('')
        foreach ($entry in $commented) { $lines.Add("- **$($entry.id) $($entry.title):** $($latest[$entry.id].note)") }
    }
    foreach ($part in @(@('Passed', $passed), @('Skipped', $skipped), @('Open', $open))) {
        $lines.Add('')
        $lines.Add("## $($part[0])")
        $lines.Add('')
        if ($part[1].Count -eq 0) { $lines.Add('- none -'); continue }
        $lines.Add('| Item | Title | Window or stills | Pre-check of the agent |')
        $lines.Add('| --- | --- | --- | --- |')
        foreach ($entry in $part[1]) { $lines.Add("| $($entry.id) | $($entry.title) | $(if (Test-Live $entry) { 'window' } else { 'stills' }) | $((Format-PreCheck (Get-PreCheck $entry.id)) -replace '\|', '/') |") }
    }
    if (-not $ReportPath) { $ReportPath = Join-Path (Split-Path -Parent $ResultsPath) ("report-{0:yyyy-MM-dd}.md" -f (Get-Date)) }
    $ReportPath = Get-FullPath $ReportPath
    Assert-OutsideRepo $ReportPath 'The report'
    New-Item -ItemType Directory -Force (Split-Path -Parent $ReportPath) | Out-Null
    [IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", $utf8)
    $lines | ForEach-Object { Write-Host $_ }
    Write-Host "`nReport: $ReportPath" -ForegroundColor Green
    exit 0
}

# ------------------------------------------------------------------------------------------------------------------ content and client

function Resolve-Content {
    $candidates = @()
    if ($ContentDir) { $candidates += , @((Get-FullPath $ContentDir), $null) }
    else {
        $candidates += , @((Join-Path $repo 'game\content'), (Join-Path $repo 'game\install.json'))
        $install = if ($InstallDir) { Get-FullPath $InstallDir } else { Join-Path $env:LOCALAPPDATA 'CnCNew\Game' }
        $candidates += , @((Join-Path $install 'content'), (Join-Path $install 'install.json'))
    }
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate[0] -PathType Container)) { continue }
        $hash = 'unknown'
        $record = if ($candidate[1]) { $candidate[1] } else { Join-Path (Split-Path -Parent $candidate[0]) 'install.json' }
        if (Test-Path -LiteralPath $record) { $hash = (Get-Content -LiteralPath $record -Raw | ConvertFrom-Json).contentManifestSha256 }
        elseif (Test-Path -LiteralPath (Join-Path $candidate[0] 'content-manifest.json')) { $hash = (Get-FileHash -LiteralPath (Join-Path $candidate[0] 'content-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() }
        return [pscustomobject]@{ Path = (Resolve-Path -LiteralPath $candidate[0]).Path; ManifestSha256 = $hash }
    }
    throw "No content directory found (looked at: $(($candidates | ForEach-Object { $_[0] }) -join '; ')).`nDevelopers: pwsh scripts\dev-content.ps1; otherwise pwsh scripts\install.ps1, or pass -ContentDir/-InstallDir."
}
$content = Resolve-Content
$commit = (& git -C $repo rev-parse --short=10 HEAD 2>$null)
if (-not $commit) { $commit = 'unknown' } elseif (& git -C $repo status --porcelain 2>$null) { $commit += '-dirty' }

$script:clientDll = $null
function Get-Client {
    if ($script:clientDll) { return $script:clientDll }
    if ($UseInstalledClient) {
        $installed = Join-Path (Split-Path -Parent $content.Path) 'bin\CnCNew.Client.Stride.dll'
        if (-not (Test-Path -LiteralPath $installed)) { throw "No installed client beside the content: $installed" }
        $script:clientDll = $installed
        return $script:clientDll
    }
    $project = Join-Path $repo 'src\CnCNew.Client.Stride\CnCNew.Client.Stride.csproj'
    if (-not $NoBuild) {
        Write-Host '== Building the client (Release) ...' -ForegroundColor Cyan
        dotnet build $project -c Release --nologo -v quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "The build failed (exit code $LASTEXITCODE)." }
    }
    $dll = Get-ChildItem (Join-Path $repo 'src\CnCNew.Client.Stride\bin\Release') -Recurse -Filter 'CnCNew.Client.Stride.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $dll) { throw 'Release client not found; call without -NoBuild.' }
    $script:clientDll = $dll.FullName
    $script:clientDll
}

function Format-Arguments([string[]]$Arguments) { ($Arguments | ForEach-Object { if ($_ -match '[\s;|"]' -or $_ -eq '') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' ' }

# Starts the client (dotnet <dll>: the exe is a window application nobody could wait for) and waits for it; returns the exit code.
function Invoke-Client([string[]]$Arguments, [string]$Log, [hashtable]$Environment, [int]$Timeout = 0) {
    $dll = Get-Client
    $previous = @{}
    if ($Environment) { foreach ($key in $Environment.Keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key]) } }
    try {
        $process = Start-Process -FilePath 'dotnet' -ArgumentList ((Format-Arguments (@($dll) + $Arguments))) -NoNewWindow -PassThru -RedirectStandardOutput $Log -RedirectStandardError "$Log.err"
        $null = $process.Handle # keeps the exit code readable after the process ended
        if ($Timeout -gt 0) {
            if (-not $process.WaitForExit($Timeout * 1000)) { $process.Kill($true); return -100 }
        } else { $process.WaitForExit() }
        return $process.ExitCode
    } finally {
        foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    }
}

# ------------------------------------------------------------------------------------------------------------------ -Capture

if (-not $OutDir) { $OutDir = Join-Path ([IO.Path]::GetTempPath()) 'cncnew-acceptance' }
$OutDir = Get-FullPath $OutDir
Assert-OutsideRepo $OutDir 'The output directory'

function Convert-ToJpeg([string]$Png, [string]$Jpg) {
    $image = [System.Drawing.Image]::FromFile($Png)
    try {
        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
        $parameters = [System.Drawing.Imaging.EncoderParameters]::new(1)
        $parameters.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, 88L)
        $image.Save($Jpg, $codec, $parameters)
    } finally { $image.Dispose() }
}

# One picture with every frame of a strip, labelled: what the agent reads for the pre-check and what the page shows for items that share a scene.
function New-ContactSheet([string[]]$Frames, [string]$Sheet, [int]$CellWidth = 480, [int]$Columns = 4) {
    if ($Frames.Count -eq 0) { return }
    $first = [System.Drawing.Image]::FromFile($Frames[0]); $ratio = $first.Height / $first.Width; $first.Dispose()
    $cellHeight = [int]($CellWidth * $ratio)
    $columns = [Math]::Min($Columns, $Frames.Count)
    $rows = [int][Math]::Ceiling($Frames.Count / $columns)
    $bitmap = [System.Drawing.Bitmap]::new($columns * $CellWidth, $rows * $cellHeight)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $font = [System.Drawing.Font]::new('Arial', 11, [System.Drawing.FontStyle]::Bold)
    for ($index = 0; $index -lt $Frames.Count; $index++) {
        $image = [System.Drawing.Image]::FromFile($Frames[$index])
        $x = ($index % $columns) * $CellWidth; $y = [int][Math]::Floor($index / $columns) * $cellHeight
        $graphics.DrawImage($image, $x, $y, $CellWidth, $cellHeight)
        $image.Dispose()
        $label = [IO.Path]::GetFileNameWithoutExtension($Frames[$index]) -replace '^frame-0*(\d)', '$1'
        $graphics.FillRectangle([System.Drawing.Brushes]::Black, $x, $y + $cellHeight - 20, 14 + 8 * $label.Length, 20)
        $graphics.DrawString($label, $font, [System.Drawing.Brushes]::Yellow, $x + 3, $y + $cellHeight - 19)
    }
    $graphics.Dispose(); $font.Dispose()
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
    $parameters = [System.Drawing.Imaging.EncoderParameters]::new(1)
    $parameters.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, 85L)
    $bitmap.Save($Sheet, $codec, $parameters); $bitmap.Dispose()
}

function ConvertTo-Html([string]$Text) { [System.Net.WebUtility]::HtmlEncode($Text) }

function Write-Page($Meta) {
    $html = [Text.StringBuilder]::new()
    [void]$html.AppendLine('<!doctype html><html lang="en"><meta charset="utf-8"><title>CnCNew - acceptance</title><style>')
    [void]$html.AppendLine('body{font-family:Segoe UI,Arial,sans-serif;background:#1b1d21;color:#ddd;margin:0 24px 80px}a{color:#8cf}h1{margin-top:20px}h2{margin-top:48px;border-bottom:1px solid #555;padding-bottom:4px}')
    [void]$html.AppendLine('.warn{background:#5a1d1d;border:1px solid #c44;padding:10px 14px;margin:12px 0;font-weight:600}.item{background:#25282e;border:1px solid #3a3e46;border-radius:6px;margin:18px 0;padding:12px 16px}')
    [void]$html.AppendLine('.id{font-family:Consolas,monospace;background:#000;color:#fd6;padding:2px 6px;border-radius:3px}.badge{font-size:12px;padding:1px 7px;border-radius:9px;margin-left:6px;background:#345}.live{background:#743}.still{background:#364}')
    [void]$html.AppendLine('.pre{color:#fc8}.audit{font-family:Consolas,monospace;font-size:12px;color:#9c9;white-space:pre-wrap}table.strip{border-collapse:collapse;width:100%}table.strip td{width:50%;padding:2px;vertical-align:top}table.strip th{text-align:left;font-weight:600;padding:4px 2px}')
    [void]$html.AppendLine('img{width:100%;display:block}.frame{font-size:11px;color:#999}.nav a{margin-right:14px}.sheet{max-width:1100px}dt{color:#9ab;margin-top:6px}dd{margin-left:0}</style>')
    [void]$html.AppendLine('<h1>CnCNew - acceptance: film strips and sound per item</h1>')
    [void]$html.AppendLine('<div class="warn">This page was generated locally and contains pictures and sound of the game. It must not be published, passed on or ever committed to the repository.</div>')
    [void]$html.AppendLine("<p>Commit <code>$($Meta.commit)</code>, content <code>$($Meta.contentManifestSha256)</code>, made $($Meta.date). Per item 60 Hz (the default) on the left and 30 Hz on the right; a picture is one original frame (1/30 s of game time), the number below it is its frame number. Items marked <span class=""badge live"">window</span> need motion or sound and are accepted with <code>pwsh scripts/acceptance.ps1</code>; the pictures here are only a hint for them. Verdicts for the <span class=""badge still"">stills</span> items: <code>pwsh scripts/acceptance.ps1 -Stills</code>. The pre-check is the agent's reading, not a human verdict.</p>")
    [void]$html.Append('<p class="nav">')
    foreach ($section in $catalogue.groups) { [void]$html.Append("<a href=""#g-$($section.id)"">$($section.id) $(ConvertTo-Html $section.title)</a>") }
    [void]$html.AppendLine('</p>')
    $shown = @{}
    foreach ($section in $catalogue.groups) {
        [void]$html.AppendLine("<h2 id=""g-$($section.id)"">$($section.id) - $(ConvertTo-Html $section.title)</h2>")
        foreach ($entry in @($catalogue.items | Where-Object group -eq $section.id)) {
            $live = Test-Live $entry
            $badges = "<span class=""badge $(if ($live) { 'live' } else { 'still' })"">$(if ($live) { 'window' } else { 'stills' })</span>"
            if ($entry.needsMotion) { $badges += '<span class="badge">needs motion</span>' }
            if ($entry.needsSound) { $badges += '<span class="badge">needs sound</span>' }
            if (Get-Prop $entry 'optional') { $badges += '<span class="badge">optional</span>' }
            $badges += "<span class=""badge"">$($entry.minutes) min</span>"
            [void]$html.AppendLine("<div class=""item"" id=""$($entry.id)""><h3><span class=""id"">$($entry.id)</span> $(ConvertTo-Html $entry.title)$badges</h3>")
            [void]$html.AppendLine("<dl><dt>Do</dt><dd>$(ConvertTo-Html $entry.instruction)</dd><dt>Right is</dt><dd>$(ConvertTo-Html $entry.expectation)</dd><dt>Pre-check of the agent (not a human verdict)</dt><dd class=""pre"">$(ConvertTo-Html (Format-PreCheck (Get-PreCheck $entry.id)))</dd></dl>")
            foreach ($name in (Get-ItemScenes $entry)) {
                $scene = Get-Scene $name
                $captured = Get-Prop $Meta.scenes $name
                if (-not $captured) {
                    $why = if (Get-Prop $scene 'capture') { "not captured yet (<code>pwsh scripts/acceptance.ps1 -Item $($entry.id) -Capture</code>)." } else { 'cannot be captured - only in the window or by hand.' }
                    [void]$html.AppendLine("<p>Scene <code>$name</code>: $why</p>")
                    continue
                }
                if ($shown.ContainsKey($name)) {
                    [void]$html.AppendLine("<p>Scene <code>$name</code> - $(ConvertTo-Html $scene.title): the strip stands at <a href=""#$($shown[$name])"">$($shown[$name])</a>; overview at 60 Hz:</p>")
                    $sheet = "$name/60hz/sheet.jpg"
                    if (Test-Path -LiteralPath (Join-Path $OutDir $sheet)) { [void]$html.AppendLine("<a href=""$sheet""><img class=""sheet"" loading=""lazy"" src=""$sheet""></a>") }
                    continue
                }
                $shown[$name] = $entry.id
                [void]$html.AppendLine("<p>Scene <code>$name</code> - $(ConvertTo-Html $scene.title)</p>")
                $audit = Get-Prop $captured 'audit'
                if ($audit) { $auditText = ConvertTo-Html (@($audit) -join "`n"); [void]$html.AppendLine("<div class=""audit"">Scene audit without a window (60 Hz): $auditText</div>") }
                $rateList = @($captured.rates.PSObject.Properties | ForEach-Object { [int]$_.Name } | Sort-Object -Descending)
                $frameNames = @($rateList | ForEach-Object { @($captured.rates."$_".frames) } | Sort-Object -Unique)
                $headers = foreach ($rate in $rateList) { $state = $captured.rates."$rate"; $trouble = if ($state.exit -ne 0) { " - the client ended with exit code $($state.exit)" } else { '' }; "<th>$rate Hz$trouble</th>" }
                [void]$html.AppendLine('<table class="strip"><tr>' + ($headers -join '') + '</tr>')
                $audioCells = foreach ($rate in $rateList) { if ($captured.rates."$rate".audio) { "<td><audio controls preload=""none"" src=""$name/$($rate)hz/audio.wav""></audio></td>" } else { '<td></td>' } }
                $audioRow = $audioCells -join ''
                if ($audioRow -match 'audio') { [void]$html.AppendLine("<tr>$audioRow</tr>") }
                foreach ($frame in $frameNames) {
                    [void]$html.Append('<tr>')
                    foreach ($rate in $rateList) {
                        if (@($captured.rates."$rate".frames) -contains $frame) { [void]$html.Append("<td><a href=""$name/$($rate)hz/$frame""><img loading=""lazy"" src=""$name/$($rate)hz/$frame""></a><span class=""frame"">$([IO.Path]::GetFileNameWithoutExtension($frame))</span></td>") } else { [void]$html.Append('<td></td>') }
                    }
                    [void]$html.AppendLine('</tr>')
                }
                [void]$html.AppendLine('</table>')
            }
            [void]$html.AppendLine('</div>')
        }
    }
    [void]$html.AppendLine('</html>')
    $index = Join-Path $OutDir 'index.html'
    [IO.File]::WriteAllText($index, $html.ToString(), $utf8)
    $index
}

if ($Capture) {
    Add-Type -AssemblyName System.Drawing
    if (-not $All -and -not $Item -and -not $Group) { throw '-Capture needs -All, -Item or -Group.' }
    $selected = if ($All) { @($catalogue.items) } else { Select-Items }
    $sceneNames = @($selected | ForEach-Object { Get-ItemScenes $_ } | Select-Object -Unique | Where-Object { Get-Prop (Get-Scene $_) 'capture' })
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $metaPath = Join-Path $OutDir 'capture.json'
    $meta = if (Test-Path -LiteralPath $metaPath) { Get-Content -LiteralPath $metaPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ scenes = [pscustomobject]@{} } }
    $meta | Add-Member -Force NoteProperty commit $commit
    $meta | Add-Member -Force NoteProperty contentManifestSha256 $content.ManifestSha256
    $meta | Add-Member -Force NoteProperty date ((Get-Date).ToString('yyyy-MM-dd HH:mm'))
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $auditDll = $null
    if (-not $NoAudit -and ($sceneNames | Where-Object { Get-Prop (Get-Scene $_) 'audit' })) {
        $auditProject = Join-Path $repo 'tools\CnCNew.PresentationTool\CnCNew.PresentationTool.csproj'
        if (-not $NoBuild) { dotnet build $auditProject -c Release --nologo -v quiet | Out-Host; if ($LASTEXITCODE -ne 0) { throw 'Building the PresentationTool failed.' } }
        $auditDll = (Get-ChildItem (Join-Path $repo 'tools\CnCNew.PresentationTool\bin\Release') -Recurse -Filter 'CnCNew.PresentationTool.dll' | Select-Object -First 1).FullName
    }
    $uiRendered = $null
    $failures = 0
    foreach ($name in $sceneNames) {
        $scene = Get-Scene $name
        $spec = $scene.capture
        $sceneDir = Join-Path $OutDir $name
        $record = [pscustomobject]@{ rates = [pscustomobject]@{} }
        $sceneRates = @(if (Get-Prop $spec 'rates') { $spec.rates } else { $catalogue.defaults.rates })
        if ($Rates) { $sceneRates = @($sceneRates | Where-Object { $Rates -contains $_ }); if ($sceneRates.Count -eq 0) { continue } }
        $existing = Get-Prop $meta.scenes $name
        if ($existing -and $Rates) { $record = $existing }
        foreach ($rate in $sceneRates) {
            $dir = Join-Path $sceneDir "$($rate)hz"
            if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
            New-Item -ItemType Directory -Force $dir | Out-Null
            $started = $watch.Elapsed.TotalSeconds
            $exit = 0
            $ui = Get-Prop $spec 'uiRender'
            if ($ui) {
                if (-not $uiRendered) {
                    $uiRendered = Join-Path $OutDir '_uirender'
                    $uiProject = Join-Path $repo 'tools\CnCNew.UiRenderTool\CnCNew.UiRenderTool.csproj'
                    Write-Host '== UiRenderTool (menus as single pictures) ...' -ForegroundColor Cyan
                    if (-not $NoBuild) { dotnet build $uiProject -c Release --nologo -v quiet | Out-Host; if ($LASTEXITCODE -ne 0) { throw 'Building the UiRenderTool failed.' } }
                    dotnet run --project $uiProject -c Release --no-build -- --content-dir $content.Path --out-dir $uiRendered *> (Join-Path $OutDir '_uirender.txt')
                    $exit = $LASTEXITCODE
                }
                foreach ($file in $ui) {
                    $source = Join-Path $uiRendered $file
                    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $dir ("frame-" + $file)) } else { Write-Host "   missing: $file" -ForegroundColor Yellow; $exit = 4 }
                }
            } else {
                $arguments = @('--smoke-test', '--content-dir', $content.Path, '--size', $Size, '--tick-rate', "$rate") + @($spec.client)
                $inputScript = Get-Prop $spec 'inputScript'
                if ($inputScript) { $arguments += @('--input-script', (Join-Path $repo ($inputScript -replace '/', '\'))) }
                if (Get-Prop $spec 'single') { $arguments += @('--capture', (Join-Path $dir 'frame-00000.png')) }
                # Two frames more than the last picture: the client leaves in the update of its last frame, before that frame is drawn.
                else { $arguments += @('--frames', "$([int]$spec.frames + 2)", '--filmstrip', $dir, '--every', "$($spec.every)") }
                if (Get-Prop $spec 'audio') { $arguments += @('--audio-capture', $dir) }
                $environment = @{}
                $variables = Get-Prop $spec 'env'
                if ($variables) { foreach ($property in $variables.PSObject.Properties) { $environment[$property.Name] = $property.Value } }
                Write-Host ("== {0} at {1} Hz ..." -f $name, $rate) -ForegroundColor Cyan
                $exit = Invoke-Client $arguments (Join-Path $dir 'client.txt') $environment $TimeoutSeconds
                if ($exit -ne 0) { Write-Host "   the client ended with exit code $exit (output: $(Join-Path $dir 'client.txt'))" -ForegroundColor Yellow; $failures++ }
            }
            $frames = @()
            foreach ($png in @(Get-ChildItem -LiteralPath $dir -Filter 'frame-*.png' | Sort-Object Name)) {
                $jpg = [IO.Path]::ChangeExtension($png.FullName, '.jpg')
                Convert-ToJpeg $png.FullName $jpg
                if (-not $KeepPng) { Remove-Item -LiteralPath $png.FullName -Force }
                $frames += [IO.Path]::GetFileName($jpg)
            }
            if ($frames.Count -gt 0) { New-ContactSheet @($frames | ForEach-Object { Join-Path $dir $_ }) (Join-Path $dir 'sheet.jpg') }
            elseif ($exit -eq 0) { Write-Host '   no pictures were produced' -ForegroundColor Yellow; $failures++ }
            $record.rates | Add-Member -Force NoteProperty "$rate" ([pscustomobject]@{ frames = $frames; audio = (Test-Path -LiteralPath (Join-Path $dir 'audio.wav')); exit = $exit; seconds = [int]($watch.Elapsed.TotalSeconds - $started) })
        }
        $auditName = Get-Prop $scene 'audit'
        if ($auditDll -and $auditName) {
            $log = Join-Path $sceneDir 'audit.txt'
            dotnet $auditDll scenario-audit $content.Path --name $auditName --every 30 *> $log
            $record | Add-Member -Force NoteProperty audit @(Get-Content -LiteralPath $log | Where-Object { $_ -match '^(RESULT|  FAIL|  KNOWN|Unhandled)' } | ForEach-Object { $_.Trim() })
            Write-Host "   audit: $((Get-Content -LiteralPath $log | Where-Object { $_ -match '^RESULT' }) -join ' ')"
        }
        $meta.scenes | Add-Member -Force NoteProperty $name $record
        [IO.File]::WriteAllText($metaPath, ($meta | ConvertTo-Json -Depth 8), $utf8)
    }
    $index = Write-Page $meta
    Write-Host ("`n{0} scenes captured in {1:0.0} min, {2} with trouble.`nStrips: {3}`nPage:   {4}`n(Imagery of the game - do not publish, do not commit.)" -f $sceneNames.Count, $watch.Elapsed.TotalMinutes, $failures, $OutDir, $index) -ForegroundColor Green
    exit ($failures -gt 0 ? 1 : 0)
}

# ------------------------------------------------------------------------------------------------------------------ guided run

$answerQueue = [Collections.Generic.Queue[string]]::new()
$scripted = $PSBoundParameters.ContainsKey('Answers')
if ($scripted) { foreach ($token in ($Answers -split ',')) { if ($token.Trim()) { $answerQueue.Enqueue($token.Trim()) } } }

# One verdict: a single key on the console, or the next token of -Answers ("f:note" carries the note; nothing left means q).
function Read-Verdict([string]$Prompt) {
    while ($true) {
        if ($scripted) {
            $token = if ($answerQueue.Count -gt 0) { $answerQueue.Dequeue() } else { 'q' }
            $key = $token.Substring(0, 1).ToLowerInvariant()
            $note = if ($token.Length -gt 2 -and $token[1] -eq ':') { $token.Substring(2) } else { '' }
            Write-Host "$Prompt $token"
        } else {
            Write-Host -NoNewline "$Prompt "
            $key = ([Console]::ReadKey($true)).KeyChar.ToString().ToLowerInvariant()
            Write-Host $key
            $note = ''
            if ($key -eq 'f') { $note = Read-Host '   What is wrong? (one line)' }
        }
        if ('p', 'f', 's', 'r', 'c', 'q' -contains $key) { return [pscustomobject]@{ Key = $key; Note = $note } }
        Write-Host '   p pass, f fail, s skip, r again, c compare at 30 Hz, q save and quit'
    }
}

Add-Type -Namespace CnCNewAcceptance -Name Win32 -MemberDefinition '[DllImport("user32.dll")] public static extern bool MoveWindow(System.IntPtr handle, int x, int y, int width, int height, bool repaint);' -ErrorAction SilentlyContinue

function Start-SceneClient($Scene, $Live, [string]$Name, $Entries, [int]$UseRate, [string]$UseSize, [string]$LogDir) {
    $caption = "$($Scene.title) - $UseRate Hz - close the window when you have seen it|" + (($Entries | Select-Object -First 5 | ForEach-Object { "$($_.id) $($_.title)" }) -join '|')
    $arguments = @('--content-dir', $content.Path, '--size', $UseSize, '--tick-rate', "$UseRate", '--caption', $caption) + @($Live.client)
    if ($Mute) { $arguments += '--mute' }
    $log = Join-Path $LogDir ("{0}-{1}hz-{2:HHmmss}.txt" -f $Name, $UseRate, (Get-Date))
    Write-Host "   dotnet <client> $(Format-Arguments $arguments)"
    if ($NoWindow) { return $null }
    $started = Start-Process -FilePath 'dotnet' -ArgumentList (Format-Arguments (@((Get-Client)) + $arguments)) -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    $null = $started.Handle
    $started
}

function Show-Scene([string]$Name, [int]$Rate, $Entries, [switch]$Both) {
    $scene = Get-Scene $Name
    $live = Get-Prop $scene 'live'
    if (-not $live) {
        # A scene without a window: its pictures come from -Capture.
        $sheet = Join-Path $OutDir "$Name\$($Rate)hz\sheet.jpg"
        if (-not (Test-Path -LiteralPath $sheet)) { $sheet = Join-Path $OutDir "$Name\60hz\sheet.jpg" }
        if (Test-Path -LiteralPath $sheet) { Write-Host "   pictures: $sheet"; if (-not $NoWindow) { Start-Process $sheet } }
        else { Write-Host "   no pictures yet - first: pwsh scripts/acceptance.ps1 -Item $($Entries[0].id) -Capture" -ForegroundColor Yellow }
        return
    }
    $manual = Get-Prop $live 'manual'
    if ($manual) {
        Write-Host "   Run by hand:  $manual" -ForegroundColor Yellow
        if (-not $scripted -and -not $NoWindow) { [void](Read-Host '   Press Enter when done') }
        return
    }
    $sceneRate = if (Get-Prop $live 'rate') { [int]$live.rate } else { $Rate }
    $script = Get-Prop $live 'script'
    if ($script) {
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot $script)) + @($live.args) + @('-ContentDir', $content.Path, '-TickRate', "$sceneRate", '-Size', $Size)
        if ($Mute) { $arguments += '-Mute' }
        Write-Host "   pwsh $(Format-Arguments $arguments)"
        if ($NoWindow) { return }
        & pwsh @arguments | Out-Host
        return
    }
    $logDir = Join-Path (Split-Path -Parent $ResultsPath) 'logs'
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $start = {
        param([int]$UseRate, [string]$UseSize)
        Start-SceneClient $scene $live $Name $Entries $UseRate $UseSize $logDir
    }
    if ($Both) {
        # Two windows at once, three quarters of the size each, 60 Hz left and 30 Hz right (the scenes are deterministic, so both show the same moments).
        $parts = $Size -split 'x'; $w = [int]([int]$parts[0] * 0.75); $h = [int]([int]$parts[1] * 0.75)
        $left = & $start 60 "${w}x$h"; $right = & $start 30 "${w}x$h"
        if ($left -and $right) {
            $deadline = (Get-Date).AddSeconds(90)
            $placed = @{}
            while ((Get-Date) -lt $deadline -and $placed.Count -lt 2 -and -not ($left.HasExited -and $right.HasExited)) {
                foreach ($pair in @(@($left, 0), @($right, 1))) {
                    $pair[0].Refresh()
                    if (-not $placed.ContainsKey($pair[1]) -and -not $pair[0].HasExited -and $pair[0].MainWindowHandle -ne [IntPtr]::Zero) {
                        try { [void][CnCNewAcceptance.Win32]::MoveWindow($pair[0].MainWindowHandle, $pair[1] * ($w + 16), 40, $w + 16, $h + 39, $true); $placed[$pair[1]] = $true } catch { $placed[$pair[1]] = $false }
                    }
                }
                Start-Sleep -Milliseconds 500
            }
            $left.WaitForExit(); $right.WaitForExit()
        }
        return
    }
    $process = & $start $sceneRate $Size
    if ($process) {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { Write-Host "   the client ended with exit code $($process.ExitCode); its output is under $logDir" -ForegroundColor Yellow }
    }
}

# ------------------------------------------------------------------------------------------------------------------ -Suite

# The acceptance a person can simply start: packages one after the other, every scene started by itself, every named goal asked for. Results go into the same file as the guided run.
function Get-SuiteSteps($Latest) {
    $steps = [Collections.Generic.List[object]]::new()
    $numbers = @{}
    foreach ($package in $catalogue.packages) {
        $members = @($catalogue.items | Where-Object { ($package.groups -contains $_.group) -and ($IncludeOptional -or -not (Get-Prop $_ 'optional')) })
        $open = @($members | Where-Object {
                -not ($Latest.ContainsKey($_.id) -and ($Latest[$_.id].verdict -in 'pass', 'fail' -or ($Latest[$_.id].verdict -eq 'skip' -and -not $IncludeSkipped)))
            })
        foreach ($sceneName in @($open | ForEach-Object { $_.scene } | Select-Object -Unique)) {
            $steps.Add([pscustomobject]@{ Package = $package; Scene = $sceneName; Entries = @($open | Where-Object scene -eq $sceneName) })
        }
    }
    , $steps
}

function Add-Verdict($Data, $Entry, [string]$Verdict, [string]$Note, [string]$SceneName) {
    $Data.entries = @($Data.entries) + [pscustomobject]@{
        id = $Entry.id; verdict = $Verdict; note = $Note; scene = $SceneName; tickRate = $TickRate; comparedWith30Hz = $false
        commit = $commit; contentManifestSha256 = $content.ManifestSha256; timestamp = (Get-Date).ToString('o'); by = 'human'
    }
}

# What the page runs: a process to stop when the page closes, a picture to look at, or a text saying what to do by hand.
function Start-SuiteScene([string]$Name, $Entries, [string]$WindowSize) {
    $scene = Get-Scene $Name
    $live = Get-Prop $scene 'live'
    if (-not $live) {
        $sheet = Join-Path $OutDir "$Name\60hz\sheet.jpg"
        if (-not (Test-Path -LiteralPath $sheet)) {
            $arguments = @('-NoProfile', '-File', $PSCommandPath, '-Item', $Entries[0].id, '-Capture', '-Rates', '60', '-NoAudit', '-ContentDir', $content.Path, '-OutDir', $OutDir, '-Size', $Size)
            if ($NoBuild) { $arguments += '-NoBuild' }
            return [pscustomobject]@{ Process = (Start-Process -FilePath 'pwsh' -ArgumentList (Format-Arguments $arguments) -PassThru -WindowStyle Hidden); Picture = $sheet; Text = 'Preparing the pictures of this scene ...' }
        }
        return [pscustomobject]@{ Process = $null; Picture = $sheet; Text = 'Judge this scene from the pictures.' }
    }
    $manual = Get-Prop $live 'manual'
    if ($manual) { return [pscustomobject]@{ Process = $null; Picture = $null; Text = "Run this yourself in a terminal:  $manual" } }
    $script = Get-Prop $live 'script'
    if ($script) {
        $sceneRate = if (Get-Prop $live 'rate') { [int]$live.rate } else { $TickRate }
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot $script)) + @($live.args) + @('-ContentDir', $content.Path, '-TickRate', "$sceneRate")
        if ($Mute) { $arguments += '-Mute' }
        return [pscustomobject]@{ Process = (Start-Process -FilePath 'pwsh' -ArgumentList (Format-Arguments $arguments) -PassThru -WindowStyle Minimized); Picture = $null; Text = 'The scene is starting ...' }
    }
    $logDir = Join-Path (Split-Path -Parent $ResultsPath) 'logs'
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $sceneRate = if (Get-Prop $live 'rate') { [int]$live.rate } else { $TickRate }
    [pscustomobject]@{ Process = (Start-SceneClient $scene $live $Name $Entries $sceneRate $WindowSize $logDir); Picture = $null; Text = 'The scene is starting; its window opens in a few seconds.' }
}

function Stop-SuiteScene($Running) {
    if ($Running -and $Running.Process -and -not $Running.Process.HasExited) { try { $Running.Process.Kill($true) } catch { Write-Verbose "$_" } }
}

# One page: the scene runs beside it, and every goal of the scene has its answer and its comment. Returns 'next' or 'stop'; the answers are in the rows.
function Show-SuitePage($Step, [int]$Number, [int]$Total, [int]$PackageNumber, $Rows) {
    $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $pageWidth = 540
    $form = [Windows.Forms.Form]::new()
    $form.Text = 'CnCNew acceptance'
    $form.TopMost = $true
    $form.StartPosition = 'Manual'
    $form.SetBounds($area.Right - $pageWidth, $area.Top, $pageWidth, $area.Height)
    $form.Font = [Drawing.Font]::new('Segoe UI', 10)

    $head = [Windows.Forms.Panel]::new(); $head.Dock = 'Top'; $head.Height = 118; $head.BackColor = [Drawing.Color]::FromArgb(32, 48, 72)
    $title = [Windows.Forms.Label]::new(); $title.Text = "Package $PackageNumber of $(@($catalogue.packages).Count): $($Step.Package.title)"; $title.ForeColor = 'White'
    $title.Font = [Drawing.Font]::new('Segoe UI', 13, [Drawing.FontStyle]::Bold); $title.SetBounds(12, 8, $pageWidth - 40, 28)
    $sceneLabel = [Windows.Forms.Label]::new(); $sceneLabel.Text = "Scene: $((Get-Scene $Step.Scene).title)"; $sceneLabel.ForeColor = 'White'; $sceneLabel.SetBounds(12, 38, $pageWidth - 40, 22)
    $progress = [Windows.Forms.ProgressBar]::new(); $progress.SetBounds(12, 64, $pageWidth - 40, 10); $progress.Maximum = [Math]::Max(1, $Total); $progress.Value = [Math]::Min($progress.Maximum, $Number)
    $status = [Windows.Forms.Label]::new(); $status.Text = "Page $Number of $Total. Starting the scene ..."; $status.ForeColor = [Drawing.Color]::FromArgb(255, 220, 120); $status.SetBounds(12, 80, $pageWidth - 40, 34)
    $head.Controls.AddRange(@($title, $sceneLabel, $progress, $status))

    $foot = [Windows.Forms.Panel]::new(); $foot.Dock = 'Bottom'; $foot.Height = 84
    $problem = [Windows.Forms.Label]::new(); $problem.ForeColor = 'Firebrick'; $problem.SetBounds(12, 4, $pageWidth - 40, 22)
    $again = [Windows.Forms.Button]::new(); $again.Text = 'Show the scene again'; $again.SetBounds(12, 30, 170, 40)
    $stop = [Windows.Forms.Button]::new(); $stop.Text = 'Stop for now'; $stop.SetBounds(190, 30, 110, 40)
    $next = [Windows.Forms.Button]::new(); $next.Text = 'Save and go on'; $next.SetBounds(308, 30, 200, 40); $next.Font = [Drawing.Font]::new('Segoe UI', 10, [Drawing.FontStyle]::Bold)
    $foot.Controls.AddRange(@($problem, $again, $stop, $next))

    $body = [Windows.Forms.FlowLayoutPanel]::new(); $body.Dock = 'Fill'; $body.AutoScroll = $true; $body.FlowDirection = 'TopDown'; $body.WrapContents = $false
    $width = $pageWidth - 52
    foreach ($row in $Rows) {
        $card = [Windows.Forms.FlowLayoutPanel]::new(); $card.FlowDirection = 'TopDown'; $card.WrapContents = $false; $card.AutoSize = $true; $card.Width = $width; $card.Margin = [Windows.Forms.Padding]::new(6, 6, 6, 10); $card.BorderStyle = 'FixedSingle'; $card.Padding = [Windows.Forms.Padding]::new(8, 6, 8, 6)
        foreach ($part in @(@("Goal: $($row.Entry.title)", 'Bold'), @("Do: $($row.Entry.instruction)", 'Regular'), @("Reached when: $($row.Entry.expectation)", 'Regular'))) {
            $line = [Windows.Forms.Label]::new(); $line.Text = $part[0].Replace('``', ''); $line.AutoSize = $true; $line.MaximumSize = [Drawing.Size]::new($width - 24, 0); $line.Margin = [Windows.Forms.Padding]::new(0, 0, 0, 6)
            $line.Font = [Drawing.Font]::new('Segoe UI', 10, [Drawing.FontStyle]::($part[1]))
            $card.Controls.Add($line)
        }
        $choices = [Windows.Forms.FlowLayoutPanel]::new(); $choices.AutoSize = $true; $choices.Margin = [Windows.Forms.Padding]::new(0)
        $row.Pass = [Windows.Forms.RadioButton]::new(); $row.Pass.Text = 'Reached'; $row.Pass.AutoSize = $true
        $row.Fail = [Windows.Forms.RadioButton]::new(); $row.Fail.Text = 'Not reached'; $row.Fail.AutoSize = $true
        $row.Skip = [Windows.Forms.RadioButton]::new(); $row.Skip.Text = 'Skip'; $row.Skip.AutoSize = $true
        $choices.Controls.AddRange(@($row.Pass, $row.Fail, $row.Skip))
        $card.Controls.Add($choices)
        $hint = [Windows.Forms.Label]::new(); $hint.Text = 'Comment (what you saw; needed when not reached):'; $hint.AutoSize = $true; $hint.Margin = [Windows.Forms.Padding]::new(0, 4, 0, 2)
        $row.Comment = [Windows.Forms.TextBox]::new(); $row.Comment.Multiline = $true; $row.Comment.Width = $width - 24; $row.Comment.Height = 52; $row.Comment.ScrollBars = 'Vertical'
        $card.Controls.AddRange(@($hint, $row.Comment))
        $body.Controls.Add($card)
    }
    $form.Controls.AddRange(@($body, $foot, $head))
    $body.BringToFront()

    $state = [pscustomobject]@{ Running = $null; Result = 'stop'; Placed = $false; PictureForm = $null; PictureShown = $false }
    $gameWidth = [Math]::Min([int]($Size -split 'x')[0], $area.Width - $pageWidth)
    $gameHeight = [int][Math]::Round($gameWidth * [int]($Size -split 'x')[1] / [int]($Size -split 'x')[0])
    $windowSize = "${gameWidth}x$gameHeight"
    $showPicture = {
        if (-not (Test-Path -LiteralPath $state.Running.Picture)) { $status.Text = 'No pictures could be made for this scene: judge what you can, or skip.'; return }
        $viewer = [Windows.Forms.Form]::new(); $viewer.Text = (Get-Scene $Step.Scene).title; $viewer.StartPosition = 'Manual'; $viewer.SetBounds($area.Left, $area.Top, $area.Width - $pageWidth, $area.Height)
        $box = [Windows.Forms.PictureBox]::new(); $box.Dock = 'Fill'; $box.SizeMode = 'Zoom'; $box.ImageLocation = $state.Running.Picture
        $viewer.Controls.Add($box); $viewer.Show(); $state.PictureForm = $viewer; $state.PictureShown = $true
        $status.Text = "Page $Number of $Total. Judge this scene from the pictures on the left."
    }
    $launch = {
        Stop-SuiteScene $state.Running
        if ($state.PictureForm) { $state.PictureForm.Close(); $state.PictureForm = $null; $state.PictureShown = $false }
        $state.Placed = $false
        $state.Running = Start-SuiteScene $Step.Scene $Step.Entries $windowSize
        $status.Text = "Page $Number of $Total. $($state.Running.Text)"
        if ($state.Running.Picture -and -not $state.Running.Process) { & $showPicture }
    }
    $timer = [Windows.Forms.Timer]::new(); $timer.Interval = 500
    $timer.Add_Tick({
            $running = $state.Running
            if (-not $running -or -not $running.Process) { return }
            if ($running.Picture) { if ($running.Process.HasExited -and -not $state.PictureShown) { & $showPicture }; return }
            $running.Process.Refresh()
            if (-not $state.Placed -and -not $running.Process.HasExited -and $running.Process.MainWindowHandle -ne [IntPtr]::Zero) {
                try { [void][CnCNewAcceptance.Win32]::MoveWindow($running.Process.MainWindowHandle, $area.Left, $area.Top, $gameWidth + 16, $gameHeight + 39, $true) } catch { Write-Verbose "$_" }
                $state.Placed = $true
                $status.Text = "Page $Number of $Total. The scene runs on the left. Follow the goals, then answer below."
            }
            if ($running.Process.HasExited -and $state.Placed) { $status.Text = "Page $Number of $Total. The scene has ended. 'Show the scene again' starts it once more." }
        })
    $form.Add_Shown({ $timer.Start(); if (-not $SuiteScreenshot) { & $launch } })
    $again.Add_Click({ & $launch })
    $stop.Add_Click({ $state.Result = 'stop'; $form.Close() })
    $next.Add_Click({
            foreach ($row in $Rows) {
                if (-not ($row.Pass.Checked -or $row.Fail.Checked -or $row.Skip.Checked)) { $problem.Text = "Please answer: $($row.Entry.title)"; return }
                if ($row.Fail.Checked -and -not $row.Comment.Text.Trim()) { $problem.Text = "Please say what is wrong: $($row.Entry.title)"; $row.Comment.Focus(); return }
            }
            $state.Result = 'next'; $form.Close()
        })
    $form.Add_FormClosed({ $timer.Stop(); Stop-SuiteScene $state.Running; if ($state.PictureForm) { $state.PictureForm.Close() } })
    if ($SuiteScreenshot) {
        $form.Add_Shown({
                $bitmap = [Drawing.Bitmap]::new($form.Width, $form.Height)
                $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0, 0, $form.Width, $form.Height))
                $bitmap.Save($SuiteScreenshot, [Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose(); $form.Close()
            })
    }
    [void]$form.ShowDialog()
    $state.Result
}

if ($Suite) {
    $data = Read-Results
    $steps = Get-SuiteSteps (Get-Latest $data)
    if (-not $SuiteScreenshot -and -not $scripted -and -not $NoWindow) { [void](Get-Client) }
    if (-not $scripted) {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        [Windows.Forms.Application]::EnableVisualStyles()
    }
    $packageIds = @($catalogue.packages.id)
    $pageNumber = 0
    $finished = $true
    foreach ($step in $steps) {
        $pageNumber++
        if ($scripted) {
            $rows = @()
            foreach ($entry in $step.Entries) {
                $answer = Read-Verdict ("  [{0}] {1}  (p/f/s/q)" -f $entry.id, $entry.title)
                if ($answer.Key -eq 'q') { $finished = $false; break }
                $rows += [pscustomobject]@{ Entry = $entry; Verdict = @{ p = 'pass'; f = 'fail'; s = 'skip' }[$answer.Key]; Note = $answer.Note }
            }
        } else {
            $rows = @($step.Entries | ForEach-Object { [pscustomobject]@{ Entry = $_; Pass = $null; Fail = $null; Skip = $null; Comment = $null } })
            $result = Show-SuitePage $step $pageNumber $steps.Count ([array]::IndexOf($packageIds, $step.Package.id) + 1) $rows
            if ($SuiteScreenshot) { exit 0 }
            if ($result -ne 'next') { $finished = $false; break }
            $rows = @($rows | ForEach-Object { [pscustomobject]@{ Entry = $_.Entry; Verdict = $(if ($_.Pass.Checked) { 'pass' } elseif ($_.Fail.Checked) { 'fail' } else { 'skip' }); Note = $_.Comment.Text.Trim() } })
        }
        foreach ($row in $rows) { Add-Verdict $data $row.Entry $row.Verdict $row.Note $step.Scene }
        Save-Results $data
        if (-not $finished) { break }
    }
    $reportFile = if ($ReportPath) { $ReportPath } else { Join-Path ([Environment]::GetFolderPath('Desktop')) ("CnCNew-acceptance-report-{0:yyyy-MM-dd}.md" -f (Get-Date)) }
    & pwsh -NoProfile -File $PSCommandPath -Report -ResultsPath $ResultsPath -ReportPath $reportFile -ContentDir $content.Path | Out-Null
    $left = @(Get-SuiteSteps (Get-Latest (Read-Results))).Count
    $message = if ($finished -and $left -eq 0) { "All packages are done.`n`nGive me this report:`n$reportFile" } else { "Your answers are saved. Start the acceptance again to go on where you stopped.`n`nThe report so far:`n$reportFile" }
    Write-Host $message
    if (-not $scripted) {
        [void][Windows.Forms.MessageBox]::Show($message, 'CnCNew acceptance')
        Start-Process explorer.exe -ArgumentList "/select,`"$reportFile`""
    }
    exit 0
}

$data = Read-Results
$latest = Get-Latest $data
$selected = Select-Items
$candidates = @($selected | Where-Object {
        $done = $latest.ContainsKey($_.id) -and ($latest[$_.id].verdict -in 'pass', 'fail' -or ($latest[$_.id].verdict -eq 'skip' -and -not $IncludeSkipped))
        if ($Item) { $true } else { (-not $done) -and ((Test-Live $_) -ne [bool]$Stills) -and ($IncludeOptional -or -not (Get-Prop $_ 'optional')) }
    })
if ($candidates.Count -eq 0) {
    Write-Host "No open item in this selection ($(if ($Stills) { 'stills' } else { 'window' })). Results file: $ResultsPath; summary: pwsh scripts/acceptance.ps1 -Report" -ForegroundColor Green
    exit 0
}
if (-not $Stills -and -not $NoWindow -and ($candidates | Where-Object { Get-Prop (Get-Prop (Get-Scene $_.scene) 'live') 'client' })) { [void](Get-Client) }

# Shortest path: the items that share a scene are shown in one window, in the order the scenes first appear in the catalogue.
$sceneOrder = @($candidates | ForEach-Object { $_.scene } | Select-Object -Unique)
$minutes = ($candidates | Measure-Object minutes -Sum).Sum
Write-Host ("`nAcceptance: {0} open items in {1} scenes, about {2:0} minutes. Results: {3}" -f $candidates.Count, $sceneOrder.Count, $minutes, $ResultsPath) -ForegroundColor Green
Write-Host "Commit $commit, content $($content.ManifestSha256), $TickRate Hz. Keys: p pass, f fail (+note), s skip, r again, c compare at 30 Hz, q save and quit.`n"
if ($Stills) {
    $index = Join-Path $OutDir 'index.html'
    if (Test-Path -LiteralPath $index) { Write-Host "Picture page: $index"; if (-not $NoWindow) { Start-Process $index } }
    else { Write-Host "The picture page is missing: first  pwsh scripts/acceptance.ps1 -Capture -All  (unattended)." -ForegroundColor Yellow }
}

$quit = $false
foreach ($sceneName in $sceneOrder) {
    if ($quit) { break }
    $entries = @($candidates | Where-Object scene -eq $sceneName)
    $scene = Get-Scene $sceneName
    Write-Host ("=== {0}  [{1}]  {2} item(s), about {3:0.0} min" -f $scene.title, $sceneName, $entries.Count, ($entries | Measure-Object minutes -Sum).Sum) -ForegroundColor Cyan
    foreach ($entry in $entries) {
        Write-Host ("  [{0}] {1}" -f $entry.id, $entry.title) -ForegroundColor White
        Write-Host "      Do:    $($entry.instruction)"
        Write-Host "      Right: $($entry.expectation)"
        $reading = Get-PreCheck $entry.id
        if ($reading) { Write-Host "      Pre-check of the agent (not a verdict): $(Format-PreCheck $reading)" -ForegroundColor DarkYellow }
        $check = Get-Prop $entry 'check'
        if ($check) { Write-Host "      Check without a window: $check" -ForegroundColor DarkGray }
    }
    if (-not $Stills) {
        Show-Scene $sceneName $TickRate $entries -Both:($Compare -and $SideBySide)
        if ($Compare -and -not $SideBySide) { Write-Host '   ... and the same scene at 30 Hz:'; Show-Scene $sceneName 30 $entries }
    }
    $compared = [bool]$Compare
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = $entries[$index]
        $answer = Read-Verdict ("  [{0}] {1}  (p/f/s/r/c/q)" -f $entry.id, $entry.title)
        switch ($answer.Key) {
            'q' { $quit = $true }
            'r' { Show-Scene $sceneName $TickRate $entries; $index-- }
            'c' { if ($SideBySide) { Show-Scene $sceneName $TickRate $entries -Both } else { Show-Scene $sceneName 30 $entries }; $compared = $true; $index-- }
            default {
                $verdict = switch ($answer.Key) { 'p' { 'pass' } 'f' { 'fail' } 's' { 'skip' } }
                $data.entries = @($data.entries) + [pscustomobject]@{
                    id = $entry.id; verdict = $verdict; note = $answer.Note; scene = $sceneName; tickRate = $TickRate; comparedWith30Hz = $compared
                    commit = $commit; contentManifestSha256 = $content.ManifestSha256; timestamp = (Get-Date).ToString('o'); by = 'human'
                }
                Save-Results $data
            }
        }
        if ($quit) { break }
    }
}
$latest = Get-Latest $data
$counts = 'pass', 'fail', 'skip' | ForEach-Object { $verdict = $_; @($catalogue.items | Where-Object { $latest.ContainsKey($_.id) -and $latest[$_.id].verdict -eq $verdict }).Count }
Write-Host ("`nSaved: {0}`nSo far: {1} passed, {2} failed, {3} skipped, {4} open. Continue with  pwsh scripts/acceptance.ps1 -Resume ; summary:  pwsh scripts/acceptance.ps1 -Report" -f $ResultsPath, $counts[0], $counts[1], $counts[2], ($catalogue.items.Count - $counts[0] - $counts[1] - $counts[2])) -ForegroundColor Green
exit 0
