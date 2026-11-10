param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot '.runtime\dev'
$taskExe = Join-Path $taskOutput 'AppTabs.exe'
try {
    $taskProcesses = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -eq 'AppTabs' })
    $taskBlocked = $false
    foreach ($taskProcess in $taskProcesses) {
        try {
            $taskPath = $taskProcess.Path
            if ([string]::IsNullOrWhiteSpace($taskPath)) { throw 'Process image is unavailable.' }
            if (-not $BuildOnly -or [string]::Equals($taskPath, $taskExe, [StringComparison]::OrdinalIgnoreCase)) {
                Write-Output "[INFO] Running AppTabs: PID $($taskProcess.Id) | $taskPath"
                $taskBlocked = $true
            }
        }
        catch { throw "Cannot verify AppTabs PID $($taskProcess.Id). No processes were closed. Retry after exiting AppTabs normally." }
        finally { $taskProcess.Dispose() }
    }
    if ($taskBlocked) {
        throw 'AppTabs is still running (including its recovery watchdog). Exit through the main window or tray to detach and keep all software, wait for recovery to finish, then retry. Do not end the processes in Task Manager.'
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
