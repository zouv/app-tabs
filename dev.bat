@echo off
chcp 65001 >nul
setlocal
rem AppTabs - dev: check toolchain and build inputs, run an isolated Debug build, and wait for exit.
rem A running AppTabs is listed and only closed after an explicit Y.
pushd "%~dp0"
if errorlevel 1 exit /b 1
where powershell >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Windows PowerShell is required.
    popd & exit /b 1
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is required. Install the .NET SDK and the .NET Framework 4.8 targeting pack.
    popd & exit /b 1
)
if not exist "src\AppTabs.App\obj\project.assets.json" (
    echo [INFO] Build inputs missing, running dotnet restore first...
    dotnet restore AppTabs.sln --nologo || (popd & exit /b 1)
)
echo [INFO] Starting AppTabs in dev mode (isolated Debug output under .runtime\dev)...
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\dev.ps1 %*
set "CODE=%ERRORLEVEL%"
if "%CODE%"=="2" (
    echo.
    echo [INFO] Dev cancelled; no AppTabs process was closed.
    echo Press any key to close this window.
    pause >nul
    popd
    exit /b 2
)
if not "%CODE%"=="0" (
    echo.
    echo [ERROR] Dev exited with code %CODE%.
    echo Check the messages above, then press any key to close this window.
    pause >nul
)
popd
exit /b %CODE%
