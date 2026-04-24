@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

where pwsh.exe >nul 2>nul
if errorlevel 1 (
    echo [run-readboard-ui-debug] pwsh.exe not found on PATH.
    echo This repo requires PowerShell 7. Install it from https://aka.ms/powershell,
    echo or run the .ps1 script directly with your preferred PowerShell host.
    exit /b 1
)

pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run-readboard-ui-debug.ps1" %*
