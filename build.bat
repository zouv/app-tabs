@echo off
chcp 65001 >nul
setlocal
rem AppTabs - build: Release build, self-contained tests, then dev/release package into dist\.
pushd "%~dp0"
if errorlevel 1 exit /b 1
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is required. Install the .NET SDK and the .NET Framework 4.8 targeting pack.
    popd & exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1 %*
set "CODE=%ERRORLEVEL%"
popd
exit /b %CODE%
