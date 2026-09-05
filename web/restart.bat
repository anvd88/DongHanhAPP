@echo off
setlocal enabledelayedexpansion
title KetoanMini - Restart (.NET sidecar + Rust gateway + Cloudflare Tunnel)

set "ROOT=C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web"
set "FE=%ROOT%\frontend"
set "API=%ROOT%\backend\KetoanMini.Api"
set "PUBLISH=%API%\bin\Release\net8.0\publish"
set "RUST=%ROOT%\backend\KetoanMini.Rust"
set "RUST_RUN=%RUST%\scripts\run-windows.ps1"

echo ============================================================
echo [1/6] Dung cac tien trinh cu
echo ============================================================
rem -- Kill dev server dang listen tren 5173 (node cua Vite)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5173 " ^| findstr LISTENING') do (
  taskkill /F /PID %%p >nul 2>&1 && echo Da dung dev server 5173 ^(PID %%p^).
)
rem -- Kill backend
taskkill /F /IM KetoanMini.Api.exe >nul 2>&1 && echo Da dung backend cu.
taskkill /F /IM ketoanmini-server.exe >nul 2>&1 && echo Da dung Rust gateway cu.
rem -- Khi chay bang "dotnet run" hoac "dotnet KetoanMini.Api.dll", process co ten dotnet.exe.
rem -- Chi dung PID dang LISTEN tren cong cua du an, KHONG kill tat ca dotnet tren may.
for %%P in (5239 5240 5443) do (
  for /f "tokens=5" %%q in ('netstat -ano ^| findstr ":%%P " ^| findstr LISTENING') do (
    taskkill /F /PID %%q >nul 2>&1 && echo Da dung backend cong %%P ^(PID %%q^).
  )
)
timeout /t 2 /nobreak >nul

echo.
echo ============================================================
echo [2/6] Build frontend cho web chinh (5443)
echo ============================================================
cd /d "%FE%" || goto :error
call npm.cmd run build || goto :error

echo.
echo ============================================================
echo [3/6] Publish Release va chay backend .NET sidecar + web chinh
echo ============================================================
rem -- Khong dung "dotnet run" tren server: no giu them dotnet host + compiler trong RAM.
rem -- Tat compiler server cho rieng lenh publish; backend sau do chay truc tiep bang file Release.
cd /d "%API%" || goto :backend_build_error
set "DOTNET_CLI_USE_MSBUILD_SERVER=0"
dotnet publish KetoanMini.Api.csproj -c Release --nologo /p:UseSharedCompilation=false || goto :backend_build_error

rem -- Nap secret da rotate tu HKCU\Environment de batch van dung gia tri moi ngay ca khi
rem -- Explorer/Codex chua duoc mo lai sau luc cap nhat bien moi truong.
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v Jwt__Key 2^>nul') do set "Jwt__Key=%%b"
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v ConnectionStrings__KetoanMini 2^>nul') do set "ConnectionStrings__KetoanMini=%%b"
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v Bootstrap__AdminUsername 2^>nul') do set "Bootstrap__AdminUsername=%%b"
for /f "tokens=2,*" %%a in ('reg query HKCU\Environment /v Bootstrap__AdminPassword 2^>nul') do set "Bootstrap__AdminPassword=%%b"
rem -- Giu content root tai thu muc API de doc appsettings.Local.json ma khong sao chep secret
rem -- sang thu muc publish; executable va model van duoc nap tu ban Release.
start "KetoanMini Backend (5443)" cmd.exe /k "cd /d ""%API%"" && set ""ASPNETCORE_ENVIRONMENT=Production"" && ""%PUBLISH%\KetoanMini.Api.exe"""

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
echo [4/6] Rust gateway (opt-in bang KETOANMINI_USE_RUST_GATEWAY=1)
echo ============================================================
if /i "%KETOANMINI_USE_RUST_GATEWAY%"=="1" (
  cd /d "%RUST%" || goto :rust_build_error
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%RUST%\scripts\package-windows.ps1" || goto :rust_build_error
  start "KetoanMini Rust Gateway (5240)" powershell.exe -NoExit -NoProfile -ExecutionPolicy Bypass -File "%RUST_RUN%" -UsePackaged

  rem -- Chi coi gateway san sang khi schema guard + ket noi DB deu qua.
  for /l %%i in (1,1,30) do (
    curl.exe -f -s http://127.0.0.1:5240/api/health >nul 2>&1 && goto :rust_ready
    timeout /t 1 /nobreak >nul
  )
  goto :rust_error
) else (
  echo Rust gateway dang tat; .NET tiep tuc phuc vu truc tiep tai :5239 va :5443.
  echo Thu cutover an toan: set KETOANMINI_USE_RUST_GATEWAY=1 ^&^& restart.bat
  goto :rust_step_done
)

:rust_ready
echo Rust gateway da san sang, health check OK.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%RUST%\scripts\verify-cutover-windows.ps1" || goto :rust_error

:rust_step_done
echo.
echo ============================================================
echo [5/6] Dev server Vite (mac dinh TAT de tiet kiem RAM)
echo ============================================================
if /i "%KETOANMINI_START_VITE%"=="1" (
  start "KetoanMini Dev HR (5173)" cmd.exe /k "cd /d ""%FE%"" && npm.cmd run dev"
) else (
  echo Khong chay Vite. Trang web va HR da duoc backend phuc vu tai cong 5443.
  echo Khi can lap trinh, chay: set KETOANMINI_START_VITE=1 ^&^& restart.bat
)

echo.
echo ============================================================
echo [6/6] Dam bao Cloudflare Tunnel (cloudflared) dang chay -^> public ra Internet
echo ============================================================
rem -- cloudflared cai dang dich vu Windows (tu bat khi khoi dong). Bat lai neu dang tat.
rem    Dashboard/config Cloudflare quyet dinh origin :5239 hay Rust :5240; script KHONG tu doi route public.
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
echo XONG. Server tiet kiem RAM dang chay:
echo   - Web chinh (LAN)  :  https://192.168.1.88:5443
echo   - Public (Internet):  https://app.ketoancp.click
if /i "%KETOANMINI_USE_RUST_GATEWAY%"=="1" echo   - Rust gateway       :  http://127.0.0.1:5240 ^(da verify native + compat^)
if /i "%KETOANMINI_START_VITE%"=="1" echo   - Vite dev (LAN)   :  http://192.168.1.88:5173
echo Dong cua so backend = tat server.
echo Tunnel can vai chuc giay de ket noi lai sau khi backend khoi dong.
echo ============================================================
timeout /t 5 /nobreak >nul
goto :eof

:error
echo.
echo *** LOI: build frontend that bai. Kiem tra thong bao ben tren. ***
pause
exit /b 1

:backend_build_error
echo.
echo *** LOI: publish backend Release that bai. Kiem tra thong bao ben tren. ***
pause
exit /b 1

:backend_error
echo.
echo *** LOI: backend khong san sang sau 45 giay. Xem cua so "KetoanMini Backend (5443)" de biet loi chi tiet. ***
pause
exit /b 1

:rust_build_error
echo.
echo *** LOI: dong goi Rust that bai. .NET van dang chay tai :5239/:5443; KHONG doi Cloudflare sang :5240. ***
pause
exit /b 1

:rust_error
echo.
echo *** LOI: Rust gateway khong qua health/compat check. .NET van dang chay tai :5239/:5443. ***
echo *** Neu Cloudflare da tro :5240, rollback origin ve http://localhost:5239 ngay. ***
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5240 " ^| findstr LISTENING') do taskkill /F /PID %%p >nul 2>&1
pause
exit /b 1
