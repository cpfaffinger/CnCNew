# Prints the Stride version the client references (src/CnCNew.Client.Stride/CnCNew.Client.Stride.csproj, the Stride.Engine package).
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/CnCNew.Client.Stride/CnCNew.Client.Stride.csproj'
$match = Select-String -LiteralPath $project -Pattern '<PackageReference Include="Stride.Engine" Version="([^"]+)"'
if (-not $match) { throw "No Stride.Engine reference in $project." }
$match.Matches[0].Groups[1].Value
