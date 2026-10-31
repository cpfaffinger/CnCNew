@echo off
rem The acceptance by a human: a window leads through the packages and writes a report to the desktop (docs/ACCEPTANCE.md).
cd /d "%~dp0"
pwsh -NoProfile -STA -File scripts\acceptance.ps1 -Suite %*
if errorlevel 1 pause
