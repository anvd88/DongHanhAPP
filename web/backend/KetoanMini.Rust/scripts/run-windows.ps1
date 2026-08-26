<#
.SYNOPSIS
    Chạy tiến trình Rust KetoanMini ở cổng riêng, song song với backend .NET đang chạy.

.DESCRIPTION
    Dùng cho giai đoạn kiểm chứng: Rust nghe 127.0.0.1:5240 và stream mọi route chưa port sang
    ASP.NET :5239. KHÔNG có ai gọi vào 5240 cho tới khi Cloudflare tunnel được trỏ sang — nên chạy
    script này không ảnh hưởng gì tới web/app đang hoạt động.

    Secret KHÔNG nằm trong script. Chúng được đọc từ HKCU\Environment ngay lúc chạy, đúng nguồn mà
    restart.bat dùng, nên không có bản sao thứ hai nào để rò rỉ.

.PARAMETER Bind
    Địa chỉ nghe. Mặc định 127.0.0.1:5240. Đừng mở ra 0.0.0.0: TLS phải kết thúc ở Cloudflare.

.PARAMETER Upstream
    Backend .NET để stream các route chưa port. Bắt buộc là HTTP loopback (Rust tự từ chối thứ khác).

.EXAMPLE
    .\scripts\run-windows.ps1
#>
[CmdletBinding()]
param(
    [string] $Bind = "127.0.0.1:5240",
    [string] $Upstream = "http://127.0.0.1:5239",
    [switch] $UsePackaged
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$exe = if ($UsePackaged) {
    Join-Path $projectRoot "dist\windows-x64\ketoanmini-server.exe"
} else {
    Join-Path $projectRoot "target\release\ketoanmini-server.exe"
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Chua co executable: $exe`nChay truoc: cargo build --release --locked"
}

# Đọc secret tại chỗ. Không in giá trị ra màn hình/log dưới bất kỳ dạng nào.
$jwtKey = (Get-ItemProperty -Path 'HKCU:\Environment' -Name 'Jwt__Key' -ErrorAction SilentlyContinue).'Jwt__Key'
$connectionString = (Get-ItemProperty -Path 'HKCU:\Environment' -Name 'ConnectionStrings__KetoanMini' -ErrorAction SilentlyContinue).'ConnectionStrings__KetoanMini'

if ([string]::IsNullOrWhiteSpace($jwtKey)) {
    throw "Thieu Jwt__Key trong HKCU\Environment. Rust phai ky JWT bang DUNG khoa cua .NET, neu khong token web/app se bi tu choi."
}
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "Thieu ConnectionStrings__KetoanMini trong HKCU\Environment."
}

$env:Jwt__Key = $jwtKey
$env:ConnectionStrings__KetoanMini = $connectionString
$env:KETOANMINI_RUST_BIND = $Bind
$env:KETOANMINI_COMPAT_UPSTREAM = $Upstream
if (-not $env:RUST_LOG) {
    $env:RUST_LOG = "ketoanmini_server=info,tower_http=info,hyper_util=warn"
}

Write-Host "KetoanMini Rust  ->  $Bind"
Write-Host "Compat upstream  ->  $Upstream"
Write-Host "Kiem tra nhanh   :  curl http://$Bind/api/info"
Write-Host ""

try {
    & $exe
} finally {
    # Đừng để secret nằm lại trong session PowerShell sau khi tiến trình dừng.
    Remove-Item Env:\Jwt__Key -ErrorAction SilentlyContinue
    Remove-Item Env:\ConnectionStrings__KetoanMini -ErrorAction SilentlyContinue
}
