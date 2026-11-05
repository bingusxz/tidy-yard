@echo off
setlocal
cd /d "%~dp0"
title Build the Tidy Yard installer

rem Double-click this file. It builds the installer you send to friends.
rem You can also run it with a version already filled in:  Build-Installer.bat 1.0.1

set "VERSION=%~1"
if "%VERSION%"=="" (
    echo.
    echo  Version number for this build, like 1.0.0
    echo  Use a number higher than the last one you sent to friends.
    echo.
    set /p "VERSION=  Version: "
)

if "%VERSION%"=="" (
    echo.
    echo  No version entered, so nothing was built.
    echo.
    pause
    exit /b 1
)

echo %VERSION%| findstr /R /C:"^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 (
    echo.
    echo  "%VERSION%" is not a version number. Use three numbers with dots, like 1.0.1
    echo.
    pause
    exit /b 1
)

echo.
echo  Building Tidy Yard %VERSION% ... this takes a minute or two.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Version %VERSION%
if errorlevel 1 (
    echo.
    echo  Something went wrong. Scroll up to read the reason.
    echo.
    pause
    exit /b 1
)

echo.
echo  ------------------------------------------------------------
echo   Done. Send this one file to your friends:
echo.
echo     releases\TidyYard-win-Setup.exe
echo.
echo   Tell them Windows may warn "Windows protected your PC".
echo   They click "More info", then "Run anyway".
echo  ------------------------------------------------------------
echo.

start "" explorer "%~dp0releases"
pause
