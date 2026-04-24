@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%run-readboard-ui-debug.ps1" %*
