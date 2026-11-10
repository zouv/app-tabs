@echo off
cd /d "%~dp0"
if not exist "dist\release\unpack\AppTabs.exe" (
  echo Please run package.bat first.
  pause
  exit /b 1
)
start "" "dist\release\unpack\AppTabs.exe"
