[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workspaceRoot = (Resolve-Path (Join-Path $projectRoot "..\..")).Path
$cacheRoot = Join-Path $workspaceRoot ".codex-work"
$llvmVersion = "20260616"
$llvmArchiveName = "llvm-mingw-$llvmVersion-ucrt-x86_64.zip"
$llvmArchive = Join-Path $cacheRoot $llvmArchiveName
$llvmRoot = Join-Path $cacheRoot "llvm-mingw-$llvmVersion\llvm-mingw-$llvmVersion-ucrt-x86_64"
$llvmUrl = "https://github.com/mstorsjo/llvm-mingw/releases/download/$llvmVersion/$llvmArchiveName"
$llvmSha256 = "b9b68a4d276e16fa25802aaba458e4638f64b3884c290aaccdc2d87083b6ca35"
$rustup = Join-Path $env:USERPROFILE ".cargo\bin\rustup.exe"

if (-not (Test-Path -LiteralPath $rustup -PathType Leaf)) {
    throw "Chua co rustup. Cai rustup x64 tu https://rust-lang.org/tools/install roi chay lai script nay."
}

& $rustup set default-host x86_64-pc-windows-gnullvm
if ($LASTEXITCODE -ne 0) { throw "Khong dat duoc Rust default host gnullvm." }
& $rustup toolchain install 1.98.0-x86_64-pc-windows-gnullvm --profile minimal
if ($LASTEXITCODE -ne 0) { throw "Khong cai duoc Rust 1.98.0 gnullvm." }
& $rustup component add rust-mingw --toolchain 1.98.0-x86_64-pc-windows-gnullvm
if ($LASTEXITCODE -ne 0) { throw "Khong cai duoc rust-mingw component." }

$linker = Join-Path $llvmRoot "bin\x86_64-w64-mingw32-clang.exe"
if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) {
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $llvmArchive -PathType Leaf)) {
        Write-Host "Dang tai LLVM-MinGW $llvmVersion..."
        Invoke-WebRequest -Uri $llvmUrl -OutFile $llvmArchive
    }

    $actualHash = (Get-FileHash -LiteralPath $llvmArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $llvmSha256) {
        throw "LLVM-MinGW SHA-256 khong khop. Xoa '$llvmArchive' va thu lai; khong giai nen tep khong tin cay."
    }

    $extractRoot = Split-Path -Parent $llvmRoot
    if (Test-Path -LiteralPath $extractRoot) {
        $resolvedExtract = (Resolve-Path -LiteralPath $extractRoot).Path
        $resolvedCache = (Resolve-Path -LiteralPath $cacheRoot).Path
        if (-not $resolvedExtract.StartsWith($resolvedCache, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Thu muc giai nen nam ngoai cache workspace: $resolvedExtract"
        }
        Remove-Item -LiteralPath $resolvedExtract -Recurse -Force
    }
    New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
    Expand-Archive -LiteralPath $llvmArchive -DestinationPath $extractRoot -Force
}

if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) {
    throw "LLVM-MinGW da giai nen nhung thieu linker: $linker"
}

Write-Host "Rust toolchain : 1.98.0-x86_64-pc-windows-gnullvm"
Write-Host "LLVM-MinGW     : $llvmRoot"
Write-Host "Kiem tra       : .\scripts\cargo-windows.ps1 test --locked"
