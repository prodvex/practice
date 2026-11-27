@echo off
setlocal
if not exist "%~dp0runtime\python\python.exe" (
    echo Extract the complete GamingClub ZIP before running this file.
    pause
    exit /b 1
)
"%~dp0runtime\python\python.exe" -X utf8 "%~dp0launcher.py" %*
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
