# Shared guard for a running AppTabs process. ASCII only; Windows PowerShell 5.1.
# The caller decides which processes block: every AppTabs process, or only those
# whose image lives under the given output roots. Nothing is closed without an
# explicit Y from an interactive console.

function Test-AppTabsPathUnder([string]$Path, [string[]]$Roots) {
    # An unreadable path cannot be proven safe, so treat it as blocking.
    if ([string]::IsNullOrWhiteSpace($Path)) { return $true }
    if ($null -eq $Roots -or $Roots.Count -eq 0) { return $true }
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $prefix = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
        if ($Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Request-CloseRunningAppTabs {
    param(
        [Parameter(Mandatory = $true)][string]$Operation,
        [string[]]$Roots,
        [switch]$AnyProcess,
        [int]$GracefulSeconds = 12
    )
    $blocking = @()
    foreach ($process in @(Get-Process -Name AppTabs -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = [string]$process.Path } catch { $path = $null }
        $blocks = $AnyProcess.IsPresent
        if (-not $blocks) { $blocks = Test-AppTabsPathUnder $path $Roots }
        if ($blocks) { $blocking += [pscustomobject]@{ Id = $process.Id; Path = $path } }
        $process.Dispose()
    }
    if ($blocking.Count -eq 0) { return }

    Write-Output ''
    Write-Output "[WARN] AppTabs is running and blocks ${Operation}:"
    foreach ($entry in $blocking) {
        $display = if ([string]::IsNullOrWhiteSpace($entry.Path)) { '(path unavailable)' } else { $entry.Path }
        Write-Output ("       PID {0}  {1}" -f $entry.Id, $display)
    }
    Write-Output '       Closing tries a normal exit first (detach and restore managed windows).'
    Write-Output "       If it does not exit within $GracefulSeconds seconds it is force-closed;"
    Write-Output '       embedded windows may then be closed and cannot be restored.'
    Write-Output '       Only the AppTabs processes listed above are touched.'

    if ([Console]::IsInputRedirected) {
        throw 'AppTabs is still running. Re-run from an interactive console to confirm closing it. No process was closed.'
    }
    $answer = Read-Host 'Close the running AppTabs and continue? Type Y to confirm; any other input cancels'
    if ($answer -notmatch '^\s*[Yy]([Ee][Ss])?\s*$') {
        Write-Output '[INFO] Cancelled: no process was closed.'
        exit 2
    }

    $ids = @($blocking | ForEach-Object { $_.Id })
    foreach ($id in $ids) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        try { $null = $process.CloseMainWindow() } catch { }
        finally { $process.Dispose() }
    }
    $deadline = (Get-Date).AddSeconds($GracefulSeconds)
    while ((Get-Date) -lt $deadline) {
        $alive = @(Get-Process -Id $ids -ErrorAction SilentlyContinue)
        if ($alive.Count -eq 0) { break }
        foreach ($process in $alive) { $process.Dispose() }
        Start-Sleep -Milliseconds 250
    }
    foreach ($process in @(Get-Process -Id $ids -ErrorAction SilentlyContinue)) {
        try { $process.Kill(); $null = $process.WaitForExit(5000) } catch { }
        finally { $process.Dispose() }
    }
    $remaining = @(Get-Process -Id $ids -ErrorAction SilentlyContinue)
    foreach ($process in $remaining) { $process.Dispose() }
    if ($remaining.Count -gt 0) {
        throw ('Could not close running AppTabs (PID ' + ($ids -join ', ') + '). Close it manually and retry.')
    }
    Write-Output '[INFO] Running AppTabs closed; continuing.'
}
