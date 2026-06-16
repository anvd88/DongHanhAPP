@echo off
setlocal
cd /d "%~dp0"
dotnet publish -c Release -r win-x64 --self-contained true
pause
