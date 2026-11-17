<#
.SYNOPSIS
    Makes build/stride-shaders: the database the Stride asset compiler writes for the client (bin/data/db), so that a machine without Windows can package the client.

.DESCRIPTION
    The client needs the shader sources of Stride, which the Stride asset compiler (Stride.Core.Assets.CompilerApp, a net10.0-windows program that compiles for
    Direct3D 11) packs into bin/data/db when the client is built. The client has no assets and no shaders of its own (docs/RELEASING.md, "The runner"), so the
    database depends on the Stride version and the graphics API alone. A release is built on a Linux runner, where the compiler cannot run:
    scripts/package-client.ps1 skips it there and copies this directory into the package instead, after checking that it was made for the Stride version the
    client references.

    This script builds the client on Windows with the asset compiler, copies bin/data/db/bundles and bin/data/db/index into build/stride-shaders and writes
    build/stride-shaders/stride-shaders.json (the Stride version and the graphics API). Run it again when the Stride version changes; the bundles are Stride's
    shader sources under Stride's licence (MIT, docs/THIRD-PARTY.md), no file of the game.

.EXAMPLE
    pwsh scripts/make-stride-shaders.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $IsWindows) { Write-Host 'The Stride asset compiler runs on Windows only.'; exit 1 }

$version = & (Join-Path $PSScriptRoot 'stride-version.ps1')
$out = Join-Path ([IO.Path]::GetTempPath()) ("cncnew-stride-shaders-" + [guid]::NewGuid().ToString('N'))
Write-Host "== Building the client with the Stride asset compiler (Stride $version)"
dotnet publish src/CnCNew.Client.Stride -c Release -r win-x64 --self-contained true -o $out -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host 'Building the client failed.'; exit 1 }

$source = Join-Path $out 'data/db'
$target = Join-Path $root 'build/stride-shaders'
if (Test-Path -LiteralPath $target) { Remove-Item -Recurse -Force -LiteralPath $target }
New-Item -ItemType Directory -Force -Path (Join-Path $target 'bundles') | Out-Null
Copy-Item -Path (Join-Path $source 'bundles/*.bundle') -Destination (Join-Path $target 'bundles')
Copy-Item -LiteralPath (Join-Path $source 'index') -Destination $target
$json = "{`n  `"stride`": `"$version`",`n  `"graphicsApi`": `"Direct3D11`"`n}`n"
[IO.File]::WriteAllText((Join-Path $target 'stride-shaders.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
Remove-Item -Recurse -Force -LiteralPath $out
Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes" -f $_.FullName.Substring($root.Length + 1), $_.Length) }
