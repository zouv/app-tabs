@echo off
chcp 65001 >nul
setlocal
rem AppTabs - package: snapshot, build Debug/Release, then publish dev and release packages.
rem A running AppTabs that locks dist is listed and only closed after an explicit Y.
pushd "%~dp0"
if errorlevel 1 exit /b 1
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is required. Install the .NET SDK and the .NET Framework 4.8 targeting pack.
    popd & exit /b 1
)
where powershell >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Windows PowerShell is required.
    popd & exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 snapshot
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" goto :Failed
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 build
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" goto :Failed
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 collect
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" goto :Failed
echo [OK] dev zip, release unpack/zip and SHA256 are under dist\
popd
exit /b 0

:Failed
echo.
if "%CODE%"=="2" (
    echo [INFO] Packaging cancelled; no AppTabs process was closed.
) else (
    echo [ERROR] Packaging failed with code %CODE%.
    echo Review the messages above; no AppTabs process was closed unless you confirmed it.
)
echo Press any key to close this window.
pause >nul
popd
exit /b %CODE%
