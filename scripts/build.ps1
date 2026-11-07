param([string]$OutputDirectory = 'dist\release', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
if (-not $taskOutput.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must stay inside the project.' }
$taskExe = Join-Path $taskOutput 'AppTabs.exe'
foreach ($taskProcess in @(Get-Process -Name AppTabs -ErrorAction SilentlyContinue)) {
    try { $taskProcessPath = $taskProcess.Path } catch { throw 'Cannot verify running AppTabs. Choose a new output directory.' }
    if ([string]::Equals($taskProcessPath, $taskExe, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output is running. Exit AppTabs before rebuilding.' }
}
& dotnet build (Join-Path $taskRoot 'tests\AppTabs.Tests\AppTabs.Tests.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (-not $SkipTests) {
    & (Join-Path $taskRoot 'tests\AppTabs.Tests\bin\Release\net48\AppTabs.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskSource = Join-Path $taskRoot 'src\AppTabs.App\bin\Release\net48'
foreach ($taskName in @('AppTabs.exe','AppTabs.exe.config','AppTabs.Core.dll','AppTabs.pdb','AppTabs.Core.pdb')) {
    $taskFile = Join-Path $taskSource $taskName
    if (Test-Path -LiteralPath $taskFile) { Copy-Item -LiteralPath $taskFile -Destination (Join-Path $taskOutput $taskName) -Force }
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskOutput -Force
$taskZip = Join-Path $taskRoot 'dist\AppTabs-0.1.0-win-x64.zip'
Compress-Archive -Path (Join-Path $taskOutput '*') -DestinationPath $taskZip -Force
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $taskRoot 'dist\SHA256SUMS.txt'), "$taskHash  AppTabs-0.1.0-win-x64.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Ready: $taskExe"
