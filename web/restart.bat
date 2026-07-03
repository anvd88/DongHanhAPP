@echo off
setlocal enabledelayedexpansion
title KetoanMini - Restart (5173 HR + 5443 Web + BE)

set "ROOT=C:\Users\Admin\Desktop\KetoanMiniDotNet_Code_20260615_155926\web"
set "FE=%ROOT%\frontend"
set "API=%ROOT%\backend\KetoanMini.Api"

echo ============================================================
echo [1/4] Dung cac tien trinh cu
echo ============================================================
rem -- Kill dev server dang listen tren 5173 (node cua Vite)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5173 " ^| findstr LISTENING') do (
  taskkill /F /PID %%p >nul 2>&1 && echo Da dung dev server 5173 ^(PID %%p^).
)
rem -- Kill backend
taskkill /F /IM KetoanMini.Api.exe >nul 2>&1 && echo Da dung backend cu.
timeout /t 2 /nobreak >nul

echo.
echo ============================================================
echo [2/4] Build frontend cho web chinh (5443)
echo ============================================================
cd /d "%FE%" || goto :error
call npm.cmd run build || goto :error

echo.
echo ============================================================
echo [3/4] Chay backend + web chinh: https://192.168.1.88:5443
echo ============================================================
start "KetoanMini Backend (5443)" cmd /k "cd /d "%API%" && dotnet run --project KetoanMini.Api.csproj"

echo.
echo ============================================================
echo [4/4] Chay dev server HR: http://192.168.1.88:5173
echo ============================================================
start "KetoanMini Dev HR (5173)" cmd /k "cd /d "%FE%" && npm.cmd run dev"

echo.
echo ============================================================
echo XONG. Da mo 2 cua so server:
echo   - Web chinh:  https://192.168.1.88:5443
echo   - Trang HR :  http://192.168.1.88:5173
echo Dong cua so server tuong ung = tat server do.
echo ============================================================
timeout /t 5 /nobreak >nul
goto :eof

:error
echo.
echo *** LOI: buoc build that bai. Kiem tra thong bao ben tren. ***
pause
exit /b 1
