param([string]$GamePath = 'D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2')
$ErrorActionPreference = 'Stop'
$release = Get-Content -LiteralPath (Join-Path $GamePath 'release_info.json') -Raw | ConvertFrom-Json
if ($release.version -ne 'v0.113.1') { throw 'This build targets v0.113.1. Check compatibility before installing.' }
$sourceDir = Join-Path $PSScriptRoot 'package/ActFloorCustomizer'
$targetDir = Join-Path $GamePath 'mods/ActFloorCustomizer'
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $sourceDir -File) {
    $destination = Join-Path $targetDir $file.Name
    if ($file.Name -eq 'settings.cfg' -and (Test-Path -LiteralPath $destination)) { continue }
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
$expected = (Get-FileHash -LiteralPath (Join-Path $sourceDir 'ActFloorCustomizer.dll')).Hash
$actual = (Get-FileHash -LiteralPath (Join-Path $targetDir 'ActFloorCustomizer.dll')).Hash
if ($expected -ne $actual) { throw 'Installed DLL verification failed.' }
Write-Output "Installed and verified: $targetDir"
Get-Content -LiteralPath (Join-Path $targetDir 'settings.cfg')
