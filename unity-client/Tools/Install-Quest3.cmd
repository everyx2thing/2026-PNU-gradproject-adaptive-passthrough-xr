@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Install-Quest3.ps1" -Launch %*
if errorlevel 1 (
    echo.
    echo Installation failed. Check the message above, reconnect Quest 3, and try again.
    pause
    exit /b 1
)
echo.
echo Quest 3 installation and launch completed.
pause
