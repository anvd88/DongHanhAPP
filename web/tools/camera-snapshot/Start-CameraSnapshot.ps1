param(
    [string]$WorkspaceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

$logDir = Join-Path $WorkspaceRoot ".codex-logs\camera-snapshot"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

& (Join-Path $PSScriptRoot "Stop-CameraSnapshot.ps1") -Quiet

$settings = Get-Content -LiteralPath (Join-Path $WorkspaceRoot "backend\KetoanMini.Api\appsettings.json") -Raw | ConvertFrom-Json
$sourceRtspUrl = [string]$settings.KioskCamera.SourceRtspUrl
if ([string]::IsNullOrWhiteSpace($sourceRtspUrl)) {
    $sourceRtspUrl = [string]$settings.KioskCamera.RtspUrl
}
if ([string]::IsNullOrWhiteSpace($sourceRtspUrl)) {
    throw "KioskCamera:SourceRtspUrl or KioskCamera:RtspUrl is missing."
}

try {
    $sourceUri = [Uri]$sourceRtspUrl
}
catch {
    throw "Invalid KioskCamera RTSP URL: $sourceRtspUrl"
}

$latestFramePath = [string]$settings.KioskCamera.LatestFramePath
if ([string]::IsNullOrWhiteSpace($latestFramePath)) {
    throw "KioskCamera:LatestFramePath is missing."
}
if (-not [System.IO.Path]::IsPathRooted($latestFramePath)) {
    $latestFramePath = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $WorkspaceRoot "backend\KetoanMini.Api") $latestFramePath))
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $latestFramePath) | Out-Null
Remove-Item -LiteralPath $latestFramePath -Force -ErrorAction SilentlyContinue

$proxyScript = Join-Path $PSScriptRoot "RtspTransportFixProxy.js"
$proxyOut = Join-Path $logDir "rtsp-proxy.out.log"
$proxyErr = Join-Path $logDir "rtsp-proxy.err.log"
$oldTargetHost = $env:RTSP_PROXY_TARGET_HOST
$oldTargetPort = $env:RTSP_PROXY_TARGET_PORT
$env:RTSP_PROXY_TARGET_HOST = $sourceUri.Host
$env:RTSP_PROXY_TARGET_PORT = if ($sourceUri.IsDefaultPort) { "554" } else { [string]$sourceUri.Port }
try {
    $proxyProcess = Start-Process -FilePath "node.exe" `
        -ArgumentList @($proxyScript) `
        -WorkingDirectory $PSScriptRoot `
        -RedirectStandardOutput $proxyOut `
        -RedirectStandardError $proxyErr `
        -WindowStyle Hidden `
        -PassThru
}
finally {
    $env:RTSP_PROXY_TARGET_HOST = $oldTargetHost
    $env:RTSP_PROXY_TARGET_PORT = $oldTargetPort
}

$proxyReady = $false
for ($i = 0; $i -lt 20; $i++) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $client.Connect("127.0.0.1", 8555)
        $client.Close()
        $proxyReady = $true
        break
    }
    catch {
        Start-Sleep -Milliseconds 250
    }
}

if (-not $proxyReady) {
    throw "RTSP transport fix proxy did not open 127.0.0.1:8555. See $proxyErr."
}

$loopScript = Join-Path $PSScriptRoot "Run-FfmpegSnapshotLoop.ps1"
$loopOut = Join-Path $logDir "ffmpeg-snapshot-loop.stdout.log"
$loopErr = Join-Path $logDir "ffmpeg-snapshot-loop.stderr.log"
$quotedLoop = '"' + $loopScript + '"'
$quotedWorkspace = '"' + $WorkspaceRoot + '"'

$loopProcess = Start-Process -FilePath "powershell.exe" `
    -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File $quotedLoop -WorkspaceRoot $quotedWorkspace" `
    -WorkingDirectory $PSScriptRoot `
    -RedirectStandardOutput $loopOut `
    -RedirectStandardError $loopErr `
    -WindowStyle Hidden `
    -PassThru

[pscustomobject]@{
    RtspProxyProcessId = $proxyProcess.Id
    FfmpegLoopProcessId = $loopProcess.Id
    LatestFramePath = $latestFramePath
    LogDirectory = $logDir
}
