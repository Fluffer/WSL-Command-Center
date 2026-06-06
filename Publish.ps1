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

# Version: csproj Version if present, else 1.0.0; zip name also carries the short commit sha.
$csproj = [xml](Get-Content (Join-Path $root 'Wsl.App\Wsl.App.csproj'))
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1) ?? '1.0.0'
$sha = (git -C $root rev-parse --short HEAD 2>$null) ?? 'nogit'

$publishDir = Join-Path $root "Wsl.App\bin\$Configuration\net9.0-windows10.0.26100.0\$rid\publish"

# Stale publish dirs mix assemblies from earlier publishes with different settings (e.g. a
# trimmed run) and the app then dies at startup with bizarre TypeLoadExceptions. Always clean.
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "==> Publishing Wsl.App ($Configuration, $rid)" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'Wsl.App\Wsl.App.csproj') `
    -c $Configuration -r $rid -p:Platform=$Platform --nologo
if ($LASTEXITCODE -ne 0) { throw "Wsl.App publish failed" }
if (-not (Test-Path (Join-Path $publishDir 'Wsl.App.exe'))) { throw "Wsl.App.exe missing in $publishDir" }

$brokerPublishDir = Join-Path $root "Wsl.Broker\bin\$Configuration\net9.0\$rid\publish"
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
