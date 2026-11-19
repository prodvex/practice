@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-GamingClub.ps1" -ArchivePath "%~1" %2 %3 %4 %5
if errorlevel 1 pause
endlocal
