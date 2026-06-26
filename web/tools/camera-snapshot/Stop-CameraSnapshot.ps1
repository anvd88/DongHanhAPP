param(
    [switch]$Quiet
)

$ErrorActionPreference = "SilentlyContinue"
$snapshotRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path

$targets = Get-CimInstance Win32_Process |
    Where-Object {
        $_.ProcessId -ne $PID -and
        $_.CommandLine -and
        (
            $_.CommandLine.Contains($snapshotRoot) -or
            $_.CommandLine.Contains("RtspTransportFixProxy.js") -or
            $_.CommandLine.Contains("ffmpeg-snapshot")
        ) -and
        $_.Name -in @("powershell.exe", "pwsh.exe", "ffmpeg.exe", "node.exe")
    }

foreach ($target in $targets) {
    Stop-Process -Id $target.ProcessId -Force
}

if (-not $Quiet) {
    $targets | Select-Object ProcessId, Name, CommandLine
}
