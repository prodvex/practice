@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-GamingClub.ps1" -RunTests %*
if errorlevel 1 pause
endlocal
