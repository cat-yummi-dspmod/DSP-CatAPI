# Thunderstore package: YummiHarness.dll + manifest + README + CHANGELOG + icon

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$Dll = Join-Path $Root 'plugin\bin\Release\net472\YummiHarness.dll'
$Zip = Join-Path $Root 'bin\Release\YummiHarness.zip'

if (-not (Test-Path $Dll)) {
    dotnet build (Join-Path $Root 'plugin\YummiHarness.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$Temp = Join-Path $Root 'thunderstore_package'
if (Test-Path $Temp) { Remove-Item $Temp -Recurse -Force }
New-Item -ItemType Directory -Path $Temp | Out-Null

Copy-Item (Join-Path $Root 'manifest.json') $Temp
Copy-Item (Join-Path $Root 'README.md') $Temp
Copy-Item (Join-Path $Root 'CHANGELOG.md') $Temp
Copy-Item (Join-Path $Root 'icon.png') $Temp
Copy-Item $Dll $Temp

New-Item -ItemType Directory -Force -Path (Split-Path $Zip) | Out-Null
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Temp '*') -DestinationPath $Zip -Force
Remove-Item $Temp -Recurse -Force
Write-Host "Package created: $Zip" -ForegroundColor Green
