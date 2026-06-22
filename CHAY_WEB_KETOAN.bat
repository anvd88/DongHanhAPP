@echo off
setlocal
chcp 65001 >nul

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Đang xin quyền Administrator để mở firewall cổng 5443...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "ROOT=%~dp0"
set "API_DIR=%ROOT%web\backend\KetoanMini.Api"

echo.
echo ==========================================
echo   KETOAN MINI WEB SERVER
echo ==========================================
echo.

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo Khong tim thay dotnet. Hay cai .NET SDK/Runtime truoc.
    echo.
    pause
    exit /b 1
)

if not exist "%API_DIR%\KetoanMini.Api.csproj" (
    echo Khong tim thay backend API:
    echo %API_DIR%
    echo.
    pause
    exit /b 1
)

echo Mo firewall cong HTTPS 5443...
powershell -NoProfile -ExecutionPolicy Bypass -Command "if (-not (Get-NetFirewallRule -DisplayName 'KetoanMini Web HTTPS 5443' -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName 'KetoanMini Web HTTPS 5443' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5443 -Profile Domain,Private,Public | Out-Null; Write-Host 'Da tao firewall rule.' } else { Write-Host 'Firewall rule da ton tai.' }"

echo.
echo Dia chi truy cap tren may nay:
echo   https://localhost:5443
echo.
echo Dia chi truy cap tu may LAN:
echo   https://192.168.1.88:5443
echo.
echo Neu trinh duyet bao chung chi khong tin cay, chon Advanced/Nang cao roi Continue/Tiep tuc.
echo.
echo Dang khoi dong server... DUNG TAT CUA SO NAY khi con muon dung web.
echo.

cd /d "%API_DIR%"
dotnet run --launch-profile lan

echo.
echo Server da dung.
pause
