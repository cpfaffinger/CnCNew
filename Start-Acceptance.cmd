@echo off
rem The acceptance by a human: a window leads through the packages and writes a report to the desktop (docs/ACCEPTANCE.md).
rem On a machine that is not set up yet the launcher does that first and leads here.
cd /d "%~dp0"
where pwsh >nul 2>nul || goto setup
echo The acceptance window opens in a moment.
pwsh -NoProfile -ExecutionPolicy Bypass -STA -File scripts\acceptance.ps1 -Suite %*
if errorlevel 5 goto setup
if errorlevel 1 pause
exit /b
:setup
call "%~dp0CnCNew-Launcher.cmd"
