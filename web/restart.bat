@echo off
setlocal enabledelayedexpansion
title KetoanMini - Restart (5173 HR + 5443 Web + BE + Cloudflare Tunnel)

set "ROOT=C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web"
set "FE=%ROOT%\frontend"
set "API=%ROOT%\backend\KetoanMini.Api"

echo ============================================================
echo [1/5] Dung cac tien trinh cu
echo ============================================================
rem -- Kill dev server dang listen tren 5173 (node cua Vite)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5173 " ^| findstr LISTENING') do (
  taskkill /F /PID %%p >nul 2>&1 && echo Da dung dev server 5173 ^(PID %%p^).
)
rem -- Kill backend
taskkill /F /IM KetoanMini.Api.exe >nul 2>&1 && echo Da dung backend cu.
rem -- Khi chay bang "dotnet run" hoac "dotnet KetoanMini.Api.dll", process co ten dotnet.exe.
rem -- Chi dung PID dang LISTEN tren cong cua du an, KHONG kill tat ca dotnet tren may.
for %%P in (5239 5443) do (
  for /f "tokens=5" %%q in ('netstat -ano ^| findstr ":%%P " ^| findstr LISTENING') do (
    taskkill /F /PID %%q >nul 2>&1 && echo Da dung backend cong %%P ^(PID %%q^).
  )
)
timeout /t 2 /nobreak >nul

echo.
echo ============================================================
echo [2/5] Build frontend cho web chinh (5443)
echo ============================================================
cd /d "%FE%" || goto :error
rem -- node_modules co the bi thieu mot phan sau khi cap nhat source/package-lock.
rem    Tu dong khoi phuc dependency truoc khi build de tranh loi TS2307.
if not exist "node_modules\@capacitor\core\package.json" (
  echo Thieu dependency frontend ^(@capacitor/core^). Dang khoi phuc bang npm install...
  call npm.cmd install || goto :dependency_error
)
call npm.cmd run build || goto :error

echo.
echo ============================================================
echo [3/5] Chay backend + web chinh: https://192.168.1.88:5443 (+ http :5239 cho tunnel)
echo ============================================================
rem -- Nap secret da rotate tu HKCU\Environment de batch van dung gia tri moi ngay ca khi
rem -- Explorer/Codex chua duoc mo lai sau luc cap nhat bien moi truong.
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v Jwt__Key 2^>nul') do set "Jwt__Key=%%b"
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v ConnectionStrings__KetoanMini 2^>nul') do set "ConnectionStrings__KetoanMini=%%b"
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v Bootstrap__AdminUsername 2^>nul') do set "Bootstrap__AdminUsername=%%b"
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v Bootstrap__AdminPassword 2^>nul') do set "Bootstrap__AdminPassword=%%b"
start "KetoanMini Backend (5443)" cmd.exe /k "cd /d ""%API%"" && set ""ASPNETCORE_ENVIRONMENT=Production"" && dotnet run --no-launch-profile --project KetoanMini.Api.csproj"

rem -- Cho backend toi da 45 giay de build/khoi dong, roi xac nhan DB cung san sang.
for /l %%i in (1,1,45) do (
  curl.exe -k -f -s https://localhost:5443/api/health >nul 2>&1 && goto :backend_ready
  timeout /t 1 /nobreak >nul
)
goto :backend_error

:backend_ready
echo Backend da san sang, health check OK.

echo.
echo ============================================================
echo [4/5] Chay dev server HR: http://192.168.1.88:5173
echo ============================================================
start "KetoanMini Dev HR (5173)" cmd.exe /k "cd /d ""%FE%"" && npm.cmd run dev"

echo.
echo ============================================================
echo [5/5] Dam bao Cloudflare Tunnel (cloudflared) dang chay -> public ra Internet
echo ============================================================
rem -- cloudflared cai dang dich vu Windows (tu bat khi khoi dong). Bat lai neu dang tat.
rem    Tunnel tro vao http://localhost:5239; backend vua khoi dong lai nen tunnel se tu ket noi lai.
sc query cloudflared >nul 2>&1
if errorlevel 1 (
  echo [CANH BAO] Chua cai dich vu cloudflared. Xem docs\cloudflare-tunnel-setup.md
) else (
  sc query cloudflared | findstr /C:"RUNNING" >nul 2>&1
  if not errorlevel 1 (
    echo cloudflared da chay san.
  ) else (
    net start cloudflared >nul 2>&1
    if errorlevel 1 (
      echo [CANH BAO] Khong the bat cloudflared. Hay chay restart.bat bang quyen Administrator neu can public Internet.
    ) else (
      echo Da bat cloudflared.
    )
  )
)

echo.
echo ============================================================
echo XONG. Da mo 2 cua so server:
echo   - Web chinh (LAN)  :  https://192.168.1.88:5443
echo   - Trang HR  (LAN)  :  http://192.168.1.88:5173
echo   - Public (Internet):  https://app.ketoancp.click
echo Dong cua so server tuong ung = tat server do.
echo Tunnel can vai chuc giay de ket noi lai sau khi backend khoi dong.
echo ============================================================
timeout /t 5 /nobreak >nul
goto :eof

:error
echo.
echo *** LOI: build frontend that bai. Kiem tra thong bao ben tren. ***
pause
exit /b 1

:dependency_error
echo.
echo *** LOI: khong the cai dependency frontend. Kiem tra ket noi mang va package-lock.json. ***
pause
exit /b 1

:backend_error
echo.
echo *** LOI: backend khong san sang sau 45 giay. Xem cua so "KetoanMini Backend (5443)" de biet loi chi tiet. ***
pause
exit /b 1
