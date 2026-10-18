# Builds ImlightSetup.exe.
#
# Needs, on Windows:
#   - The .NET 10 SDK.
#   - Inno Setup 6 (https://jrsoftware.org/isinfo.php).
#   - The Imcodec generator inputs from your own game client, as described in the main README
#     under "Building Imcodec". The server cannot be compiled without them.
#
# Usage, from the repository root or this folder:
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0

param(
    [string]$Version = "0.0.0"
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot
$repoRoot = Split-Path $installerDir -Parent
$publishDir = Join-Path $installerDir "publish"

Write-Host "Publishing the server.."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# Self-contained, so the people installing it do not need .NET.
dotnet publish (Join-Path $repoRoot "src\Imlight.Director\Imlight.Director.csproj") `
    -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php" }

Write-Host "Building the installer.."
& $iscc "/DAppVersion=$Version" (Join-Path $installerDir "Imlight.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host "Done: $(Join-Path $installerDir 'output\ImlightSetup.exe')"
