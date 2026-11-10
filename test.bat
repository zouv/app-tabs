@echo off
chcp 65001 >nul
setlocal
rem AppTabs - test: Release-build the test harness and run the self-contained assertions.
pushd "%~dp0"
if errorlevel 1 exit /b 1
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK is required. Install the .NET SDK and the .NET Framework 4.8 targeting pack.
    popd & exit /b 1
)
dotnet build tests\AppTabs.Tests\AppTabs.Tests.csproj -c Release --nologo
set "CODE=%ERRORLEVEL%"
if not "%CODE%"=="0" (
    echo [ERROR] Test build failed with code %CODE%.
    popd & exit /b %CODE%
)
"tests\AppTabs.Tests\bin\Release\net48\AppTabs.Tests.exe"
set "CODE=%ERRORLEVEL%"
if %CODE%==0 (
    echo [OK] All self-contained tests passed.
) else (
    echo [ERROR] Tests failed with code %CODE%.
)
popd
exit /b %CODE%
