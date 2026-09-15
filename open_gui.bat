@echo off
setlocal
title Network Resetter
if not exist "%~dp0dist\NetworkResetter-1.0.exe" (
    echo NetworkResetter-1.0.exe was not found.
    echo Run build.ps1 first, then open this launcher again.
    pause
    exit /b 1
)
start "" "%~dp0dist\NetworkResetter-1.0.exe"
exit /b 0
