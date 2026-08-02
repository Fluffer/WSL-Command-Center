<#
.SYNOPSIS
    Builds a deployable, self-contained release of WSL Command Center.

.DESCRIPTION
    Publishes Wsl.App (self-contained, bundled WindowsAppRuntime — no installs needed on the
    target machine) plus Wsl.Broker as a self-contained single-file exe (the broker is otherwise
    framework-dependent and would require .NET on the target), then zips the result into .\dist.

    Output: dist\WslCommandCenter-<version>-win-<platform>.zip
    Run the app on any Windows 10 17763+ x64 machine by extracting and starting Wsl.App.exe.

.EXAMPLE
    .\Publish.ps1                  # Release x64
    .\Publish.ps1 -Platform arm64  # Release ARM64
#>
param(
    [ValidateSet('x64', 'arm64', 'x86')]
    [string]$Platform = 'x64',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rid = "win-$Platform"

# Version, in order of preference:
#   1. <Version> in Wsl.App.csproj, if anyone ever sets one there;
#   2. Identity/@Version in Package.appxmanifest -- the number the MSIX actually ships as.
# The manifest is the real source of truth today: the csproj carries no <Version>, so this used
# to fall through to a hardcoded '1.0.0' and every zip was named 1.0.0 regardless of the release
# it belonged to (a v1.0.2 release shipped 'WslCommandCenter-1.0.0-<sha>.zip').
# The manifest is 4-part (1.0.2.0); trim a trailing '.0' so the zip reads 1.0.2, matching the tag.
$csproj = [xml](Get-Content (Join-Path $root 'Wsl.App\Wsl.App.csproj'))
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    $manifestPath = Join-Path $root 'Wsl.App\Package.appxmanifest'
    if (Test-Path $manifestPath) {
        $manifestVersion = ([xml](Get-Content $manifestPath)).Package.Identity.Version
        if ($manifestVersion) { $version = $manifestVersion -replace '\.0$', '' }
    }
}
if (-not $version) { throw "Could not determine a version from Wsl.App.csproj or Package.appxmanifest." }
$sha = (git -C $root rev-parse --short HEAD 2>$null) ?? 'nogit'
Write-Host "==> Version $version ($sha)" -ForegroundColor Cyan

$publishDir = Join-Path $root "Wsl.App\bin\$Configuration\net10.0-windows10.0.26100.0\$rid\publish"

# Stale publish dirs mix assemblies from earlier publishes with different settings (e.g. a
# trimmed run) and the app then dies at startup with bizarre TypeLoadExceptions. Always clean.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "==> Publishing Wsl.App ($Configuration, $rid)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'Wsl.App\Wsl.App.csproj') `
    -c $Configuration -r $rid -p:Platform=$Platform --nologo
if ($LASTEXITCODE -ne 0) { throw "Wsl.App publish failed" }
if (-not (Test-Path (Join-Path $publishDir 'Wsl.App.exe'))) { throw "Wsl.App.exe missing in $publishDir" }

$brokerPublishDir = Join-Path $root "Wsl.Broker\bin\$Configuration\net10.0\$rid\publish"
if (Test-Path $brokerPublishDir) { Remove-Item $brokerPublishDir -Recurse -Force }

Write-Host "==> Publishing Wsl.Broker (self-contained single file)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'Wsl.Broker\Wsl.Broker.csproj') `
    -c $Configuration -r $rid --self-contained -p:PublishSingleFile=true --nologo
if ($LASTEXITCODE -ne 0) { throw "Wsl.Broker publish failed" }

# Overwrite the framework-dependent broker the app build copied next to the exe.
$brokerExe = Join-Path $brokerPublishDir 'Wsl.Broker.exe'
Copy-Item $brokerExe $publishDir -Force

Write-Host "==> Zipping" -ForegroundColor Cyan
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "WslCommandCenter-$version-$sha-$rid.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "==> Done: $zip ($sizeMb MB)" -ForegroundColor Green
