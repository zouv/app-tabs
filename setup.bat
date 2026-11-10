@echo off
chcp 65001 >nul
setlocal
rem AppTabs - setup: check toolchain and restore build inputs.
pushd "%~dp0"
if errorlevel 1 exit /b 1
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is required. Install the .NET SDK and the .NET Framework 4.8 targeting pack.
    popd & exit /b 1
)
set "HAS_SDK="
for /f "delims=" %%S in ('dotnet --list-sdks 2^>nul') do set "HAS_SDK=1"
if not defined HAS_SDK (
    echo [ERROR] No .NET SDK found. Install the .NET SDK, not just the runtime.
    popd & exit /b 1
)
set "REFROOT=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework"
if not exist "%REFROOT%\v4.8" set "REFROOT=%ProgramFiles%\Reference Assemblies\Microsoft\Framework\.NETFramework"
if not exist "%REFROOT%\v4.8" (
    echo [WARN] .NET Framework 4.8 targeting pack not found in the default location.
    echo        Install the .NET Framework 4.8 Developer Pack if build.bat reports missing reference assemblies.
)
dotnet restore AppTabs.sln --nologo
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" (
    echo [ERROR] Restore failed with code %CODE%.
    popd & exit /b %CODE%
)
echo [OK] Toolchain ready. Use build.bat to build and test, or dev.bat for a debug run.
popd
exit /b 0
