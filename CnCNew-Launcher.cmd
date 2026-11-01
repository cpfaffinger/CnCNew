@echo off
rem The setup assistant and launcher: from a fresh clone to the game. Needs nothing but Windows (docs/INSTALLATION.md).
cd /d "%~dp0"
echo The CnCNew window opens in a moment.
powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts\CnCNew-Launcher.ps1 %*
if errorlevel 1 pause
