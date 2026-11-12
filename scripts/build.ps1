param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $root 'tests\AppTabs.Tests\AppTabs.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (-not $SkipTests) {
    & (Join-Path $root 'tests\AppTabs.Tests\bin\Release\net48\AppTabs.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
# Packaging is owned by package.ps1 so build.bat and package.bat publish the same layout.
$package = Join-Path $PSScriptRoot 'package.ps1'
foreach ($action in @('snapshot', 'build', 'collect')) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $package $action
    if ($LASTEXITCODE -eq 2) {
        Write-Output '[INFO] Packaging cancelled by user; no process was closed.'
        exit 2
    }
    if ($LASTEXITCODE -ne 0) { throw "Packaging step '$action' failed." }
}
Write-Output 'Ready: dist\release\unpack\AppTabs.exe'
