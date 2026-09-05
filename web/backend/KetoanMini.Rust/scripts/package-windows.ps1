[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageRoot = Join-Path $projectRoot "dist\windows-x64"
$manifestPath = Join-Path $packageRoot "SHA256SUMS.txt"

Push-Location $projectRoot
try {
    $rustc = Join-Path $env:USERPROFILE ".cargo\bin\rustc.exe"
    if (-not (Test-Path -LiteralPath $rustc -PathType Leaf)) {
        throw "Khong tim thay rustc. Chay truoc: .\scripts\setup-toolchain-windows.ps1"
    }
    $hostTriple = (& $rustc -vV | Select-String '^host:' | ForEach-Object { $_.Line.Split(':', 2)[1].Trim() })
    if ($hostTriple -ne 'x86_64-pc-windows-gnullvm') {
        throw "Sai Rust host '$hostTriple'. Chay: rustup set default-host x86_64-pc-windows-gnullvm"
    }

    & (Join-Path $PSScriptRoot "cargo-windows.ps1") build --release --locked
    if ($LASTEXITCODE -ne 0) { throw "Cargo release build that bai." }

    $executable = Join-Path $projectRoot "target\release\ketoanmini-server.exe"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Release executable was not produced: $executable"
    }

    $rustSysroot = (& $rustc --print sysroot).Trim()
    if (-not $rustSysroot) {
        throw "rustc did not report its sysroot"
    }

    # windows-gnullvm links libunwind dynamically. Keeping it beside the executable makes the
    # package runnable by a service account without adding a Rust/LLVM toolchain to system PATH.
    $unwindCandidates = @(
        (Join-Path $rustSysroot "bin\libunwind.dll"),
        (Join-Path $rustSysroot "lib\rustlib\x86_64-pc-windows-gnullvm\bin\libunwind.dll")
    )
    $unwind = $unwindCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $unwind) {
        throw "The windows-gnullvm runtime libunwind.dll was not found under the active Rust sysroot"
    }

    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    Copy-Item -LiteralPath $executable -Destination (Join-Path $packageRoot "ketoanmini-server.exe") -Force
    Copy-Item -LiteralPath $unwind -Destination (Join-Path $packageRoot "libunwind.dll") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "rust.env.example") -Destination $packageRoot -Force

    $hashLines = Get-ChildItem -LiteralPath $packageRoot -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object Name |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($_.Name)"
        }
    Set-Content -LiteralPath $manifestPath -Value $hashLines -Encoding ascii

    Write-Host "Windows package created at $packageRoot"
} finally {
    Pop-Location
}
