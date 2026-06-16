@echo off
setlocal
title Cau hinh SQL Server LAN - Ketoan Mini

set "SCRIPT=%~dp0configure_sql_server_lan.ps1"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Dang mo lai bang quyen Administrator...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    echo Neu Windows hien UAC, hay bam Yes.
    pause
    exit /b
)

cls
echo ================================================
echo   CAU HINH SQL SERVER QUA LAN CHO KETOAN MINI
echo ================================================
echo.
echo Script se cau hinh:
echo   - Port: 1433
echo   - Login SQL: ketoan_app
echo.
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -InstanceName SQLEXPRESS01 -Database KetoanMini -Port 1433 -Login ketoan_app

echo.
echo ================================================
if "%errorlevel%"=="0" (
    echo DA CHAY XONG. Xem dong DONE/ERROR o ben tren.
) else (
    echo Da xay ra loi. Lien he ngay voi quan tri vien.
)
echo ================================================
pause
