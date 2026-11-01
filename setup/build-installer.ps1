# Builds the PokeLauncher installer end-to-end:
#   1. Release publish (self-contained by default - includes the .NET runtime,
#      so players need nothing else installed).
#   2. Inno Setup compile (if ISCC.exe is available).
#   3. Generates setup\installer\update.json for the update checker
#      (version + installer URL placeholder + SHA-256 of the built installer).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\setup\build-installer.ps1 [-Version "0.11.1"] [-FrameworkDependent]
# Output:
#   setup\installer\PokeLauncher-<version>-setup.exe
#   setup\installer\update.json

param(
    [string]$Version = "0.12.4",
    [switch]$FrameworkDependent,
    [string]$DownloadUrlBase = "https://github.com/riley13542-cloud/PokeLauncher/releases/download"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

Write-Host "== 1. Release publish (v$Version) ==" -ForegroundColor Cyan
# Clean the previous publish output so stale files can never reach the installer.
$stalePublish = Join-Path $repo "PokeLauncher\bin\Release\net10.0\win-x64\publish"
if (Test-Path $stalePublish) { Remove-Item $stalePublish -Recurse -Force }
$publishArgs = @(
    'publish', "PokeLauncher\PokeLauncher.csproj",
    '-c', 'Release', '-r', 'win-x64',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=false'
)
if ($SelfContained) { $publishArgs += '--self-contained', 'true' } else { $publishArgs += '--self-contained', 'false' }
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }

$publishDir = Join-Path $repo "PokeLauncher\bin\Release\net10.0\win-x64\publish"
Write-Host "Published to $publishDir" -ForegroundColor Green

Write-Host "== 2. Inno Setup ==" -ForegroundColor Cyan
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}

$installerDir = Join-Path $repo "setup\installer"
New-Item -ItemType Directory -Force $installerDir | Out-Null

if (-not $iscc) {
    Write-Warning "Inno Setup 6 (ISCC.exe) was not found."
    Write-Host "Install it with:  winget install -e --id JRSoftware.InnoSetup" -ForegroundColor Yellow
    Write-Host "Then re-run this script. Meanwhile a portable ZIP has been produced:" -ForegroundColor Yellow
    $zip = Join-Path $installerDir "PokeLauncher-$Version-portable.zip"
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zip -Force
} else {
    & $iscc "/DAppVersion=$Version" (Join-Path $repo "setup\PokeLauncher.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)" }
}

$installer = Join-Path $installerDir "PokeLauncher-$Version-setup.exe"
if (-not (Test-Path $installer)) { $installer = Join-Path $installerDir "PokeLauncher-$Version-portable.zip" }

# SHA-256 of the distributable (works for .exe and .zip alike)
$sha = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$size = [math]::Round((Get-Item $installer).Length / 1MB, 1)
Write-Host "Built: $installer ($size MB)" -ForegroundColor Green
Write-Host "SHA-256: $sha"

Write-Host "== 3. update.json (for the in-app update checker) ==" -ForegroundColor Cyan
$fileName = Split-Path $installer -Leaf
$updateJson = [ordered]@{
    version = $Version
    url     = "$DownloadUrlBase/v$Version/$fileName"
    sha256  = $sha
    notes   = "See the About page in the app for what is new."
}
$updateJson | ConvertTo-Json | Set-Content (Join-Path $installerDir "update.json") -Encoding UTF8
Write-Host "Wrote $(Join-Path $installerDir 'update.json')" -ForegroundColor Green
Write-Host ""
Write-Host "NEXT STEPS:" -ForegroundColor Cyan
Write-Host "  1. Upload '$fileName' and 'update.json' to your download host."
Write-Host "  2. Edit update.json's 'url' so it points at the real hosted location"
Write-Host "     (or re-run with -DownloadUrlBase 'https://your-host/path/')."
Write-Host "  3. Set UpdateService.DefaultManifestUrl in the app source to that"
Write-Host "     update.json URL, rebuild, and ship that build once - after that,"
Write-Host "     users get the update prompt automatically on every new release."
