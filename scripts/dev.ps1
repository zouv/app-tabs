param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot '.runtime\dev'
$taskExe = Join-Path $taskOutput 'AppTabs.exe'
. (Join-Path $PSScriptRoot 'ProcessGuard.ps1')
try {
    if ($BuildOnly) {
        # Only a process running from the dev output locks the file being rebuilt.
        Request-CloseRunningAppTabs -Operation 'the development build' -Roots @($taskOutput)
    }
    else {
        # The dev host shares the single-instance mutex and global hotkeys.
        Request-CloseRunningAppTabs -Operation 'the development host' -AnyProcess
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK is required. Install the .NET SDK and the .NET Framework 4.8 targeting pack.'
    }
    & dotnet build (Join-Path $taskRoot 'src\AppTabs.App\AppTabs.App.csproj') -c Debug --nologo -o $taskOutput
    if ($LASTEXITCODE -ne 0) { throw 'Debug build failed. Review the build output above.' }
    if (-not (Test-Path -LiteralPath $taskExe -PathType Leaf)) { throw 'Debug executable was not generated.' }
    Write-Output "[OK] Debug build: $taskExe"
    if ($BuildOnly) { exit 0 }
    Write-Output '[INFO] Starting AppTabs. Debug and Release share the same single instance and user data.'
    $taskProcess = Start-Process -FilePath $taskExe -WorkingDirectory $taskOutput -PassThru
    try { $null = $taskProcess.Handle; $taskProcess.WaitForExit(); $taskCode = $taskProcess.ExitCode }
    finally { $taskProcess.Dispose() }
    if ($taskCode -ne 0) { Write-Output "[ERROR] AppTabs exited with code $taskCode. Diagnostics: $env:LOCALAPPDATA\AppTabs\app.log" }
    exit $taskCode
}
catch {
    Write-Output "[ERROR] $($_.Exception.Message)"
    exit 1
}
