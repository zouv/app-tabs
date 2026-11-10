param([ValidateSet('snapshot', 'build', 'collect')][string]$Action)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root 'src\AppTabs.App\AppTabs.App.csproj'
$dist = Join-Path $root 'dist'
$finalDev = Join-Path $dist 'dev'
$finalRelease = Join-Path $dist 'release'
$staging = Join-Path $root '.runtime\package-staging'
$stagingBuildDebug = Join-Path $staging 'build\debug'
$stagingBuildRelease = Join-Path $staging 'build\release'
$stagingDev = Join-Path $staging 'dev'
$stagingRelease = Join-Path $staging 'release'
$stagingLogs = Join-Path $staging 'logs'
$stagingDebugRoot = Join-Path $staging 'debug-source'
$snapshotFile = Join-Path $root '.runtime\package-snapshot.json'
. (Join-Path $PSScriptRoot 'PackageTransaction.ps1')

function Write-Utf8([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

function Read-Version {
    $value = (& dotnet msbuild $appProject -getProperty:Version -nologo 2>$null | Select-Object -Last 1)
    if ($null -ne $value) { $value = $value.Trim() }
    if ([string]::IsNullOrEmpty($value) -or $value -notmatch '^\d+\.\d+\.\d+$') {
        throw "Cannot read <Version> from Directory.Build.props via $appProject."
    }
    return $value
}

function Read-Snapshot {
    if (-not (Test-Path -LiteralPath $snapshotFile -PathType Leaf)) {
        throw 'Package snapshot missing. Run package.bat so it records the previous output before building.'
    }
    $record = Get-Content -LiteralPath $snapshotFile -Raw | ConvertFrom-Json
    $started = [DateTime]::Parse($record.StartedUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
    return [pscustomobject]@{ Version = $record.Version; StartedUtc = $started; Existing = $record.Existing }
}

function Assert-OutputUnlocked {
    $roots = @($finalDev, $finalRelease, $stagingBuildDebug, $stagingBuildRelease, $stagingDev, $stagingRelease)
    $running = @()
    foreach ($process in @(Get-Process -Name AppTabs -ErrorAction SilentlyContinue)) {
        $id = $process.Id
        try { $path = $process.Path }
        catch { throw "Cannot verify running AppTabs PID $id. No processes were closed. Exit AppTabs normally and retry." }
        finally { $process.Dispose() }
        if ([string]::IsNullOrWhiteSpace($path)) {
            throw "Cannot verify running AppTabs PID $id. No processes were closed. Exit AppTabs normally and retry."
        }
        foreach ($rootPath in $roots) {
            if ($path.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                $running += "  PID $id  $path"
                break
            }
        }
    }
    if ($running.Count -gt 0) {
        throw ("AppTabs is running from the package output:`n" + ($running -join "`n") +
            "`nExit through the main window or tray to detach and keep all software, wait for recovery to finish, then retry. No processes were closed.")
    }
}

function Assert-Built([string]$directory, [string]$version, [DateTime]$started) {
    $exe = Join-Path $directory 'AppTabs.exe'
    $core = Join-Path $directory 'AppTabs.Core.dll'
    foreach ($file in @($exe, $core)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Built file not found: $file" }
    }
    foreach ($file in @($exe, $core)) {
        $built = [Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString(3)
        if ($built -ne $version) { throw "Stale build version: $file ($built != $version)" }
        if ((Get-Item -LiteralPath $file).LastWriteTimeUtc -lt $started.AddSeconds(-5)) {
            throw "No fresh build was produced: $file. Stale artifacts are not exported."
        }
    }
}

function Invoke-Build([string]$configuration, [string]$output, [string]$log) {
    New-Item -ItemType Directory -Force -Path $output, (Split-Path -Parent $log) | Out-Null
    & dotnet build $appProject -c $configuration --nologo --no-incremental -o $output 2>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 40 }
        throw "$configuration build failed. Log: $log"
    }
}

function Copy-AppFiles([string]$source, [string]$destination, [bool]$includeSymbols, [string]$version) {
    $names = @('AppTabs.exe', 'AppTabs.exe.config', 'AppTabs.Core.dll')
    if ($includeSymbols) { $names += @('AppTabs.pdb', 'AppTabs.Core.pdb') }
    foreach ($name in $names) {
        $file = Join-Path $source $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing build output: $file" }
        if ($name -eq 'AppTabs.exe' -or $name -eq 'AppTabs.Core.dll') {
            $built = [Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString(3)
            if ($built -ne $version) { throw "Stale build version: $file ($built != $version)" }
        }
        Copy-Item -LiteralPath $file -Destination (Join-Path $destination $name) -Force
    }
}

function Write-BuildInfo([string]$path, [string]$version) {
    $git = 'unknown'
    try { $git = (& git -C $root rev-parse --short HEAD 2>$null | Select-Object -First 1) } catch { }
    $sdk = (& dotnet --version 2>$null | Select-Object -First 1)
    $lines = @(
        "AppTabs $version (Debug, x64)",
        "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "Git: $git",
        "Dotnet: $sdk",
        "OS: $([Environment]::OSVersion.VersionString)"
    )
    Write-Utf8 $path (($lines -join "`r`n") + "`r`n")
}

function Write-Snapshot {
    Assert-OutputUnlocked
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stagingBuildDebug, $stagingBuildRelease, $stagingLogs | Out-Null
    $version = Read-Version
    $started = [DateTime]::UtcNow
    $existing = @()
    foreach ($directory in @($finalDev, $finalRelease)) {
        if (-not (Test-Path -LiteralPath $directory)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
            $existing += [pscustomobject]@{
                Path = $file.FullName.Substring($root.Length + 1)
                Length = $file.Length
                LastWriteUtc = $file.LastWriteTimeUtc.ToString('o')
            }
        }
    }
    $record = [pscustomobject]@{ Version = $version; StartedUtc = $started.ToString('o'); Existing = $existing }
    Write-Utf8 $snapshotFile (ConvertTo-Json -InputObject $record -Depth 5)
    Write-Output "[OK] Recorded $($existing.Count) existing artifact(s) before build (version $version)"
}

function Invoke-PackageBuild {
    $record = Read-Snapshot
    Assert-OutputUnlocked
    if (-not (Test-Path -LiteralPath $staging)) { throw 'Package staging missing. Run package.bat from the start.' }
    Invoke-Build 'Debug' $stagingBuildDebug (Join-Path $stagingLogs 'debug-build.log')
    Invoke-Build 'Release' $stagingBuildRelease (Join-Path $stagingLogs 'release-build.log')
    Assert-Built $stagingBuildDebug $record.Version $record.StartedUtc
    Assert-Built $stagingBuildRelease $record.Version $record.StartedUtc
    Write-Output "[OK] Debug and Release builds completed (version $($record.Version))"
}

function Write-Package {
    $record = Read-Snapshot
    Assert-OutputUnlocked
    Assert-Built $stagingBuildDebug $record.Version $record.StartedUtc
    Assert-Built $stagingBuildRelease $record.Version $record.StartedUtc
    $version = $record.Version

    $debugName = "AppTabs_${version}_x64-unpack-debug"
    $debugSource = Join-Path $stagingDebugRoot $debugName
    $debugLogs = Join-Path $debugSource 'logs'
    New-Item -ItemType Directory -Force -Path $debugLogs, $stagingDev | Out-Null
    Copy-AppFiles $stagingBuildDebug $debugSource $true $version
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $debugSource 'README.md') -Force
    Copy-Item -LiteralPath (Join-Path $stagingLogs 'debug-build.log') -Destination (Join-Path $debugLogs 'dev-build.log') -Force
    Write-BuildInfo (Join-Path $debugSource 'build-info.txt') $version
    Write-Utf8 (Join-Path $debugSource 'README.txt') (@(
        "AppTabs $version (Debug, x64)",
        "=================================",
        "",
        "Debug build for development and troubleshooting.",
        "Contains PDB symbols, build-info.txt and logs\dev-build.log.",
        "User data: %LOCALAPPDATA%\AppTabs"
    ) -join "`r`n")
    $debugZip = Join-Path $stagingDev "$debugName.zip"
    New-PackageZip $debugSource $debugZip $debugName

    $relUnpack = Join-Path $stagingRelease 'unpack'
    New-Item -ItemType Directory -Force -Path $relUnpack | Out-Null
    Copy-AppFiles $stagingBuildRelease $relUnpack $false $version
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $relUnpack 'README.md') -Force
    Write-Utf8 (Join-Path $relUnpack 'README.txt') (@(
        "AppTabs $version (x64)",
        "======================",
        "",
        "Portable release: double-click AppTabs.exe. No installer or admin rights needed.",
        "User data: %LOCALAPPDATA%\AppTabs"
    ) -join "`r`n")
    $relZipName = "AppTabs_${version}_x64-unpack.zip"
    $relZip = Join-Path $stagingRelease $relZipName
    New-PackageZip $relUnpack $relZip ''

    Test-PackageZip $debugSource $debugZip $debugName
    Test-PackageZip $relUnpack $relZip ''
    $debugHash = (Get-FileHash -LiteralPath $debugZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $relHash = (Get-FileHash -LiteralPath $relZip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8 (Join-Path $stagingRelease 'SHA256SUMS.txt') "$relHash  $relZipName`n$debugHash  $debugName.zip`n"

    try { Publish-PackageSet $root $staging $dist }
    catch {
        $message = $_.Exception.Message
        if ($message -match 'rollback incomplete') { throw $message }
        throw ("Package publish failed: " + $message +
            "`nThe previous dist\dev / dist\release were preserved. Exit any process using them normally and retry.")
    }
    try { Remove-Item -LiteralPath $staging -Recurse -Force }
    catch { Write-Output "[WARN] Package staging is still in use; left at $staging" }
    Remove-Item -LiteralPath $snapshotFile -Force -ErrorAction SilentlyContinue
    foreach ($legacy in @(Get-ChildItem -LiteralPath $dist -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'AppTabs-*-win-x64.zip' -or $_.Name -eq 'SHA256SUMS.txt' })) {
        try { Remove-Item -LiteralPath $legacy.FullName -Force }
        catch { Write-Output "[WARN] Previous layout artifact is still in use: $($legacy.FullName)" }
    }

    Write-Output "[OK] Package published:"
    foreach ($file in @(Get-ChildItem -LiteralPath $finalDev, $finalRelease -Recurse -File | Sort-Object FullName)) {
        Write-Output ("  " + $file.FullName.Substring($dist.Length + 1) + "  " + $file.Length + " bytes")
    }
}

try {
    if ($Action -eq 'snapshot') { Write-Snapshot }
    elseif ($Action -eq 'build') { Invoke-PackageBuild }
    else { Write-Package }
}
catch {
    Write-Output "[ERROR] $($_.Exception.Message)"
    exit 1
}
