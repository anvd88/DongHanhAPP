[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $CargoArgs
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$workspaceRoot = (Resolve-Path (Join-Path $projectRoot "..\..")).Path
$defaultLlvmBin = Join-Path $workspaceRoot ".codex-work\llvm-mingw-20260616\llvm-mingw-20260616-ucrt-x86_64\bin"
$llvmBin = if (-not [string]::IsNullOrWhiteSpace($env:KETOANMINI_LLVM_MINGW_BIN)) {
    $env:KETOANMINI_LLVM_MINGW_BIN
} else {
    $defaultLlvmBin
}
$linker = Join-Path $llvmBin "x86_64-w64-mingw32-clang.exe"
if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) {
    throw "Thieu LLVM-MinGW linker. Chay truoc: .\scripts\setup-toolchain-windows.ps1"
}

$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
if (-not (Test-Path -LiteralPath $cargo -PathType Leaf)) {
    $cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
}
if ([string]::IsNullOrWhiteSpace($cargo) -or -not (Test-Path -LiteralPath $cargo -PathType Leaf)) {
    throw "Khong tim thay cargo. Chay truoc: .\scripts\setup-toolchain-windows.ps1"
}

$env:Path = "$llvmBin;$env:Path"
Push-Location $projectRoot
$cargoExitCode = 0
try {
    & $cargo @CargoArgs
    $cargoExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($cargoExitCode -ne 0) {
    throw "cargo ket thuc voi ma loi $cargoExitCode"
}
