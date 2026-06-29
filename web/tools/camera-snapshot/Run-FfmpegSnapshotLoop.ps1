param(
    [string]$WorkspaceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path,
    [int]$RestartDelaySeconds = 5
)

$ErrorActionPreference = "Stop"

$appSettingsPath = Join-Path $WorkspaceRoot "backend\KetoanMini.Api\appsettings.json"
$settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
$cameraUrl = [string]$settings.KioskCamera.SnapshotRtspUrl
if ([string]::IsNullOrWhiteSpace($cameraUrl)) {
    $cameraUrl = [string]$settings.KioskCamera.SourceRtspUrl
}
$latestFramePath = [string]$settings.KioskCamera.LatestFramePath

if ([string]::IsNullOrWhiteSpace($cameraUrl)) {
    throw "KioskCamera:SourceRtspUrl is missing in $appSettingsPath."
}

if ([string]::IsNullOrWhiteSpace($latestFramePath)) {
    throw "KioskCamera:LatestFramePath is missing in $appSettingsPath."
}

if (-not [System.IO.Path]::IsPathRooted($latestFramePath)) {
    $latestFramePath = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $WorkspaceRoot "backend\KetoanMini.Api") $latestFramePath))
}

$latestFrameDir = Split-Path -Parent $latestFramePath
New-Item -ItemType Directory -Force -Path $latestFrameDir | Out-Null

$ffmpegSearchRoots = @(
    (Join-Path $PSScriptRoot "runtime\ffmpeg"),
    (Join-Path $WorkspaceRoot "tools\video-bridge\runtime\ffmpeg")
)

$ffmpeg = $null
foreach ($root in $ffmpegSearchRoots) {
    if (Test-Path -LiteralPath $root) {
        $ffmpeg = Get-ChildItem -LiteralPath $root -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
        if ($ffmpeg) { break }
    }
}

if (-not $ffmpeg) {
    $ffmpegCommand = Get-Command "ffmpeg.exe" -ErrorAction SilentlyContinue
    if ($ffmpegCommand) {
        $ffmpeg = [pscustomobject]@{ FullName = $ffmpegCommand.Source }
    }
}

if (-not $ffmpeg) {
    throw "ffmpeg.exe was not found. Put portable FFmpeg under tools\camera-snapshot\runtime\ffmpeg, tools\video-bridge\runtime\ffmpeg, or PATH."
}

$logDir = Join-Path $WorkspaceRoot ".codex-logs\camera-snapshot"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$loopLog = Join-Path $logDir "ffmpeg-snapshot-loop.log"
$ffmpegOut = Join-Path $logDir "ffmpeg-snapshot.out.log"
$ffmpegErr = Join-Path $logDir "ffmpeg-snapshot.err.log"

$inputAttempts = @()
if ($cameraUrl -match "127\.0\.0\.1:8555|localhost:8555") {
    $inputAttempts += ,@("-rtsp_transport", "tcp")
} else {
    $inputAttempts += ,@("-rtsp_flags", "prefer_tcp")
    $inputAttempts += ,@("-rtsp_transport", "tcp")
    $inputAttempts += ,@("-rtsp_transport", "udp")
    $inputAttempts += ,@()
}

# KHÔNG dùng "-fflags nobuffer -flags low_delay": với luồng H.265 hay rớt gói (camera này),
# tắt đệm khiến mất khung tham chiếu → decode hỏng nhiều → hình giật/đơ. Cho đệm + rtbufsize lớn
# để bộ giải mã giữ đủ khung tham chiếu, đổi lại độ trễ tăng vài trăm ms (chấp nhận được cho snapshot).
$commonInputArgs = @(
    "-hide_banner",
    "-nostdin",
    "-loglevel", "warning",
    "-rtbufsize", "16M",
    "-allowed_media_types", "video",
    "-timeout", "5000000"
)

$outputArgs = @(
    "-i", $cameraUrl,
    "-an",
    # crop=iw:ih/2:0:ih/2 → chỉ giữ NỬA DƯỚI khung hình (nửa trên của camera IP này bị đen, không có hình).
    # fps=10 cho hình mượt hơn (trước đây 3 fps nên nhìn giật); q:v 5 giảm dung lượng để ghi/đọc nhanh.
    "-vf", "fps=10,crop=iw:ih/2:0:ih/2,scale=960:-2",
    "-q:v", "5",
    "-update", "1",
    "-atomic_writing", "1",
    "-y",
    $latestFramePath
)

$retryBackoffSeconds = @(
    [Math]::Max(1, $RestartDelaySeconds),
    15,
    30,
    60
)
$retryIndex = 0

while ($true) {
    foreach ($attempt in $inputAttempts) {
        $attemptName = if ($attempt.Length -gt 0) { $attempt -join " " } else { "default-transport" }
        Add-Content -LiteralPath $loopLog -Value "[$(Get-Date -Format "yyyy-MM-dd HH:mm:ss")] Starting FFmpeg snapshot ($attemptName): $cameraUrl -> $latestFramePath"

        $startedAt = Get-Date
        $ffmpegArgs = $commonInputArgs + $attempt + $outputArgs

        $ErrorActionPreference = "Continue"
        & $ffmpeg.FullName @ffmpegArgs 1>> $ffmpegOut 2>> $ffmpegErr
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = "Stop"

        $exitReason = if ($exitCode -eq 0) { "ended" } else { "exited with code $exitCode" }
        Add-Content -LiteralPath $loopLog -Value "[$(Get-Date -Format "yyyy-MM-dd HH:mm:ss")] FFmpeg $exitReason after attempt $attemptName."
        if (Test-Path -LiteralPath $latestFramePath) {
            $lastFrame = Get-Item -LiteralPath $latestFramePath
            if ($lastFrame.LastWriteTime -ge $startedAt) {
                $retryIndex = 0
            }
        }
        Start-Sleep -Seconds 1
    }

    if ($retryIndex -ge $retryBackoffSeconds.Count) {
        Add-Content -LiteralPath $loopLog -Value "[$(Get-Date -Format "yyyy-MM-dd HH:mm:ss")] Max retry backoff reached. Camera snapshot loop is off until manual reconnect."
        exit 2
    }

    $delay = $retryBackoffSeconds[$retryIndex]
    $retryIndex++
    Add-Content -LiteralPath $loopLog -Value "[$(Get-Date -Format "yyyy-MM-dd HH:mm:ss")] Restarting FFmpeg snapshot attempts in $delay seconds."
    Start-Sleep -Seconds $delay
}
