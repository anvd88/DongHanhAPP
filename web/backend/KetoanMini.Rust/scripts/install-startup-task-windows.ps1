<#
.SYNOPSIS
    Đăng ký tiến trình Rust tự chạy khi đăng nhập (Scheduled Task), thay cho Windows service.

.DESCRIPTION
    VÌ SAO KHÔNG DÙNG `sc.exe create`:
    ketoanmini-server.exe là console app thuần, KHÔNG cài đặt service control handler. Đăng ký
    thẳng bằng sc.exe/New-Service thì Windows SCM sẽ báo lỗi 1053 ("service did not respond in a
    timely fashion") và không bao giờ start được. Muốn chạy đúng kiểu service thì phải bọc bằng
    NSSM hoặc WinSW. Scheduled Task chạy lúc đăng nhập là cách gọn nhất mà không cần tải thêm gì,
    và khớp với mô hình triển khai hiện tại (backend .NET cũng chạy trong phiên tương tác vì cần
    Excel COM + máy in).

    Task chạy dưới TÀI KHOẢN HIỆN TẠI nên đọc được HKCU\Environment — nơi chứa Jwt__Key và
    ConnectionStrings__KetoanMini. Không có bản sao secret nào được ghi vào task.

.PARAMETER Remove
    Gỡ task đã đăng ký.

.EXAMPLE
    .\scripts\install-startup-task-windows.ps1
    .\scripts\install-startup-task-windows.ps1 -Remove
#>
[CmdletBinding()]
param(
    [string] $TaskName = "KetoanMini Rust Gateway",
    [switch] $Remove
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runScript = Join-Path $projectRoot "scripts\run-windows.ps1"

if ($Remove) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Da go task: $TaskName"
    } else {
        Write-Host "Khong co task ten: $TaskName"
    }
    return
}

$packaged = Join-Path $projectRoot "dist\windows-x64\ketoanmini-server.exe"
if (-not (Test-Path -LiteralPath $packaged -PathType Leaf)) {
    throw "Chua dong goi. Chay truoc: .\scripts\package-windows.ps1"
}

# Chạy bản ĐÓNG GÓI chứ không phải target\release: bản đóng gói có libunwind.dll nằm cạnh.
# Toolchain windows-gnullvm link dong libunwind; thieu DLL thi tien trinh chet TRUOC khi vao main
# nen KHONG in ra bat ky loi nao — rat kho doan neu khong biet truoc.
$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$runScript`" -UsePackaged" `
    -WorkingDirectory $projectRoot

$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description "Gateway Rust KetoanMini nghe 127.0.0.1:5240, stream route chua port sang ASP.NET :5239." `
    -Force | Out-Null

Write-Host "Da dang ky task: $TaskName"
Write-Host "Chay ngay        : Start-ScheduledTask -TaskName `"$TaskName`""
Write-Host "Kiem tra         : curl http://127.0.0.1:5240/api/info"
Write-Host ""
Write-Host "LUU Y: dang ky task KHONG lam thay doi duong di cua web/app."
Write-Host "Cloudflare tunnel van tro :5239 cho toi khi ban tu doi trong dashboard."
