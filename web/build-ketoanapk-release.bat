@echo off
chcp 65001 >nul
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-ketoanapk-release.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
if not "%KETOANAPK_NO_PAUSE%"=="1" pause
exit /b %EXIT_CODE%
