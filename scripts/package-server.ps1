<#
.SYNOPSIS
    Builds the self-contained packages of the dedicated server (docs/DEDICATED-SERVER.md section 13): win-x64 and linux-x64, each a directory that
    needs no .NET runtime on the machine it runs on.

.DESCRIPTION
    Publishes tools/CnCNew.DedicatedServer for every runtime named, self-contained, into <OutDir>/<runtime>, and adds the start guide, the sample
    configuration, the licence and the notice. With -Zip every directory is also packed as <OutDir>/cncnew-server-<runtime>.zip.

    The package states the build it was made from (the server's first line: build=<commit>[+modified]). A package for players is built from a
    clean working tree: a build with local changes is another identity, and a server only admits it with allowModifiedBuilds.

    The packages are build results: they go to artifacts/ (ignored by Git) or to the directory named, never into the repository.

.EXAMPLE
    pwsh scripts/package-server.ps1 -Zip
#>
param(
    [string]$OutDir = 'artifacts/server',
    [string[]]$Runtime = @('win-x64', 'linux-x64'),
    [switch]$Zip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (git status --porcelain) { Write-Host 'Note: the working tree has local changes - the packages state a "+modified" build, which a server admits only with allowModifiedBuilds.' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
foreach ($rid in $Runtime) {
    $target = Join-Path $OutDir $rid
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Write-Host "== Publishing the dedicated server for $rid"
    dotnet publish tools/CnCNew.DedicatedServer -c Release -r $rid --self-contained true -o $target -v q --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host "Publishing for $rid failed."; exit 1 }
    Copy-Item tools/CnCNew.DedicatedServer/package/START.md, tools/CnCNew.DedicatedServer/package/server.sample.json, LICENSE.md, NOTICE.md -Destination $target
    $files = @(Get-ChildItem $target -Recurse -File)
    Write-Host ("     {0}: {1} files, {2:N1} MB" -f $target, $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
    if ($Zip) {
        $archive = Join-Path $OutDir "cncnew-server-$rid.zip"
        if (Test-Path $archive) { Remove-Item -Force $archive }
        Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive
        Write-Host ("     {0}: {1:N1} MB, SHA-256 {2}" -f $archive, ((Get-Item $archive).Length / 1MB), (Get-FileHash $archive -Algorithm SHA256).Hash)
    }
}
exit 0
