@echo off
rem The setup assistant and launcher: from a fresh clone to the game. Needs nothing but Windows (docs/INSTALLATION.md).
cd /d "%~dp0"
echo The CnCNew window opens in a moment.
rem Started from a PowerShell 7 terminal, Windows PowerShell would inherit that one's module path and miss its own commands.
set PSModulePath=
powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts\CnCNew-Launcher.ps1 %*
if errorlevel 1 pause
