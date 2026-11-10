@echo off
chcp 65001 >nul
setlocal
rem AppTabs - clean: remove build intermediates and test screenshots, keep dist\ packages.
pushd "%~dp0"
if errorlevel 1 exit /b 1
for %%P in (src\AppTabs.Core src\AppTabs.App tests\AppTabs.Tests) do (
    for %%N in (bin obj) do (
        if exist "%%P\%%N" (
            rmdir /s /q "%%P\%%N" 2>nul
            if exist "%%P\%%N" (
                echo [WARN] Could not remove %%P\%%N\ - it may be in use.
            ) else (
                echo [OK] Removed %%P\%%N\
            )
        )
    )
)
if exist artifacts (
    rmdir /s /q artifacts 2>nul
    if exist artifacts (
        echo [WARN] Could not remove artifacts\ - it may be in use.
    ) else (
        echo [OK] Removed artifacts\
    )
)
if exist dist\release echo [OK] Kept dist\ (dev zip, release unpack/zip and checksums)
echo [OK] Clean done.
popd
exit /b 0
