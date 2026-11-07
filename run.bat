@echo off
cd /d "%~dp0"
if not exist "dist\release\AppTabs.exe" (
  echo Please run build.bat first.
  pause
  exit /b 1
)
start "" "dist\release\AppTabs.exe"
