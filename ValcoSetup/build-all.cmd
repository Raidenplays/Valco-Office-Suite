@echo off
rem Rebuilds every Valco app, then Valco Office Setup.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -BuildApps
echo.
echo Finished. Press any key to close.
pause >nul
