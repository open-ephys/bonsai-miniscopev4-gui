@echo off
PowerShell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo An error occurred. Press any key to close...
    pause > nul
)
