# Builds the mod, refreshes package/, and produces the installable zip in releases/.
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 0.8.4
# Afterwards publish it:  python tools\publish-release.py --version 0.8.4 --notes-file notes.md
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$GameBuild = "v0.113.1"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$manifest = Join-Path $root "package\ActFloorCustomizer\ActFloorCustomizer.json"
$json = Get-Content $manifest -Raw | ConvertFrom-Json
if ($json.version -ne $Version) {
    $text = [System.IO.File]::ReadAllText($manifest)
    $text = $text.Replace('"version": "' + $json.version + '"', '"version": "' + $Version + '"')
    [System.IO.File]::WriteAllText($manifest, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "manifest version -> $Version"
}

Write-Host "building..."
& dotnet build "src\ActFloorCustomizer.csproj" -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "build failed" }

& dotnet run --project "tests\Smoke.csproj" -c Release | Select-String -Pattern "^PASS|FAIL" | Out-Host

$dll = Join-Path $root "src\bin\Release\net9.0\ActFloorCustomizer.dll"
Copy-Item $dll (Join-Path $root "package\ActFloorCustomizer\ActFloorCustomizer.dll") -Force
# Every guide in docs/ is shipped flat inside the installed mod folder (English and Chinese).
Get-ChildItem (Join-Path $root "docs") -Filter "*.md" -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $root ("package\ActFloorCustomizer\" + $_.Name)) -Force
}
Write-Host ("packaged dll {0} bytes, sha256 {1}" -f (Get-Item $dll).Length,
    (Get-FileHash $dll -Algorithm SHA256).Hash)

$releaseDir = Join-Path $root "releases"
if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir | Out-Null }
$zipName = "ActFloorCustomizer-v$Version-sts2-$GameBuild.zip"
$zip = Join-Path $releaseDir $zipName
if (Test-Path $zip) { Remove-Item $zip -Force }

# PowerShell 5.1's Compress-Archive writes backslash entry names, which some unzippers on other
# platforms treat as a single file name, so the entries are written by hand with forward slashes.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source = Join-Path $root "package\ActFloorCustomizer"
$stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::CreateNew)
$archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
foreach ($file in (Get-ChildItem $source -File | Sort-Object Name)) {
    $entry = $archive.CreateEntry("ActFloorCustomizer/" + $file.Name,
        [System.IO.Compression.CompressionLevel]::Optimal)
    $target = $entry.Open()
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $target.Write($bytes, 0, $bytes.Length)
    $target.Dispose()
}
$archive.Dispose()
$stream.Dispose()
Write-Host ("zip releases\{0} ({1} bytes)" -f $zipName, (Get-Item $zip).Length)

# Keep exactly one distributable committed.
$ignore = Join-Path $root ".gitignore"
$text = [System.IO.File]::ReadAllText($ignore)
$text = [regex]::Replace($text, '(?m)^!/releases/ActFloorCustomizer-v[0-9.]+-sts2-[^\r\n]*\.zip$',
    "!/releases/$zipName")
[System.IO.File]::WriteAllText($ignore, $text, (New-Object System.Text.UTF8Encoding($false)))

# Keep exactly one distributable in releases/. The v0.4.0 zip is the original handoff artifact and
# stays committed for provenance, so it is never removed here.
Get-ChildItem $releaseDir -Filter "ActFloorCustomizer-v*.zip" |
    Where-Object { $_.Name -ne $zipName -and $_.Name -notlike "*v0.4.0*" } |
    ForEach-Object { Remove-Item $_.FullName -Force; Write-Host "removed old $($_.Name)" }

Write-Host ""
Write-Host "next:"
Write-Host "  1. update docs/CHANGELOG.md, docs/CHANGELOG.zhs.md and the README latest-release section"
Write-Host "  2. copy package to the workshop workspace and run ModUploader.exe upload -w ActFloorCustomizer"
Write-Host "  3. python tools\publish-release.py --version $Version --notes-file <notes.md>"
