using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using OpenCvSharp;

namespace KetoanMini.Api.Services;

public sealed record RtspAttendanceStatus(
    bool Enabled,
    bool CameraConnected,
    string Mode,
    DateTime? LastMotionAt,
    DateTime? LastScanAt,
    DateTime? LastMatchedAt,
    string LastMatchedUser,
    string LastMatchedName,
    string LastMessage,
    DateTime? LastFrameAt = null,
    double LastMotionScore = 0,
    double LastSimilarity = 0,
    int ScanBurstCount = 0,
    int EnrolledTemplates = 0,
    string Source = "",
    bool AutoAttendanceEnabled = false,
    bool TestScanEnabled = false,
    int TestScanIntervalMs = 0);

public sealed class RtspAttendanceWorker(
    IConfiguration config,
    IHostEnvironment env,
    Database db,
    IFaceEngine faceEngine,
    IHubContext<ChangesHub> hub,
    ILogger<RtspAttendanceWorker> logger) : BackgroundService
{
    private const int CapPropOpenTimeoutMsec = 53;
    private const int CapPropReadTimeoutMsec = 54;
    private const string DefaultFfmpegOptions = "rtsp_transport;tcp|stimeout;5000000|max_delay;500000";
    private const string AutoAttendanceSettingKey = "KioskCamera.AutoAttendanceEnabled";
    private static readonly TimeSpan[] SnapshotRetryBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];
    private enum CameraFrameSource { Rtsp, ImageFile }
    private sealed record FaceTemplate(string Username, string FullName, float[] Embedding);
    private sealed record FrameReadResult(bool Success, DateTime? FrameAt, string Message);

    private readonly object _statusGate = new();
    private readonly object _templateGate = new();
    private readonly Dictionary<string, DateTime> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<FaceTemplate> _templateCache = [];
    private DateTime _templateCacheLoadedAt = DateTime.MinValue;
    private TimeSpan _frameStaleAfter = TimeSpan.FromSeconds(15);
    private TimeSpan _testScanInterval = TimeSpan.FromSeconds(3);
    private int _reconnectSignal;
    private int _autoAttendanceEnabled = 1;
    private int _testScanEnabled;
    private RtspAttendanceStatus _status = new(false, false, "Đang tắt", null, null, null, "", "", "Chấm công RTSP đang tắt.");

    public RtspAttendanceStatus GetStatus()
    {
        lock (_statusGate) return ApplyFrameWatchdog(_status, DateTime.UtcNow);
    }

    public void RequestReconnect(string message = "Đang kết nối lại camera...")
    {
        Interlocked.Increment(ref _reconnectSignal);
        UpdateStatus(s => s with
        {
            CameraConnected = false,
            Mode = "Đang kết nối lại",
            LastMotionScore = 0,
            ScanBurstCount = 0,
            LastMessage = message
        });
    }

    public RtspAttendanceStatus SetTestScan(bool enabled)
    {
        Interlocked.Exchange(ref _testScanEnabled, enabled ? 1 : 0);
        UpdateStatus(s => s with
        {
            AutoAttendanceEnabled = IsAutoAttendanceEnabled(),
            TestScanEnabled = enabled,
            TestScanIntervalMs = (int)_testScanInterval.TotalMilliseconds,
            Mode = enabled ? "Test scan" : s.Mode == "Test scan" ? IsAutoAttendanceEnabled() ? "Chờ" : "Tạm dừng" : s.Mode,
            LastMessage = enabled
                ? $"Đã bật test scan. Hệ thống quét mỗi {_testScanInterval.TotalSeconds:0.#} giây và không ghi chấm công."
                : "Đã tắt test scan."
        });
        return GetStatus();
    }

    public RtspAttendanceStatus SetAutoAttendance(bool enabled)
    {
        Interlocked.Exchange(ref _autoAttendanceEnabled, enabled ? 1 : 0);
        UpdateStatus(s => s with
        {
            AutoAttendanceEnabled = enabled,
            Mode = enabled
                ? (s.Mode == "Tạm dừng" ? "Chờ" : s.Mode)
                : (IsTestScanEnabled() ? "Test scan" : "Tạm dừng"),
            ScanBurstCount = enabled ? s.ScanBurstCount : 0,
            LastMessage = enabled
                ? "Đã bật chấm công nhận diện tự động."
                : "Đã tạm dừng chấm công nhận diện tự động."
        });
        return GetStatus();
    }

    private bool IsAutoAttendanceEnabled() => Volatile.Read(ref _autoAttendanceEnabled) == 1;
    private bool IsTestScanEnabled() => Volatile.Read(ref _testScanEnabled) == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredFrameSource = config["KioskCamera:FrameSource"];
        var latestFramePath = config["KioskCamera:LatestFramePath"];
        var rtspUrl = config["KioskCamera:RtspUrl"];
        var frameSource = ResolveFrameSource(configuredFrameSource,
            !string.IsNullOrWhiteSpace(rtspUrl),
            !string.IsNullOrWhiteSpace(latestFramePath));
        var useImageFile = frameSource == CameraFrameSource.ImageFile;
        var sourceLabel = useImageFile ? "Snapshot" : "RTSP";
        var enabled = config.GetValue<bool?>("KioskCamera:Enabled")
            ?? (useImageFile ? !string.IsNullOrWhiteSpace(latestFramePath) : !string.IsNullOrWhiteSpace(rtspUrl));
        var autoAttendanceDefault = config.GetValue("KioskCamera:AutoAttendanceEnabled", true);
        var autoAttendanceEnabled = await LoadAutoAttendanceEnabled(autoAttendanceDefault, stoppingToken);
        Interlocked.Exchange(ref _autoAttendanceEnabled, autoAttendanceEnabled ? 1 : 0);
        if (!enabled || (useImageFile ? string.IsNullOrWhiteSpace(latestFramePath) : string.IsNullOrWhiteSpace(rtspUrl)))
        {
            SetStatus(new RtspAttendanceStatus(false, false, "Đang tắt", null, null, null, "", "",
                "Camera kiosk chưa được cấu hình hoặc đang tắt.", Source: sourceLabel));
            return;
        }

        var standbyInterval = TimeSpan.FromMilliseconds(config.GetValue("KioskCamera:StandbyFrameIntervalMs", 1000));
        var burstInterval = TimeSpan.FromMilliseconds(config.GetValue("KioskCamera:BurstFrameIntervalMs", 1200));
        var burstDuration = TimeSpan.FromSeconds(config.GetValue("KioskCamera:BurstDurationSeconds", 15));
        var reconnectDelay = TimeSpan.FromSeconds(config.GetValue("KioskCamera:ReconnectDelaySeconds", 5));
        var cooldown = TimeSpan.FromSeconds(config.GetValue("KioskCamera:CooldownSeconds", 60));
        var templateRefresh = TimeSpan.FromSeconds(config.GetValue("KioskCamera:TemplateRefreshSeconds", 30));
        var idleScanInterval = TimeSpan.FromMilliseconds(config.GetValue("KioskCamera:IdleScanIntervalMs", 0));
        var testScanInterval = TimeSpan.FromMilliseconds(config.GetValue("KioskCamera:TestScanIntervalMs", 3000));
        var motionRatio = config.GetValue("KioskCamera:MotionRatio", 0.025);
        var openTimeoutMs = config.GetValue("KioskCamera:OpenTimeoutMs", 5000);
        var readTimeoutMs = config.GetValue("KioskCamera:ReadTimeoutMs", 5000);
        var scanMaxWidth = config.GetValue("KioskCamera:ScanMaxWidth", 960);
        var jpegQuality = config.GetValue("KioskCamera:JpegQuality", 86);
        var motionFrameWidth = config.GetValue("KioskCamera:MotionFrameWidth", 160);
        var maxBurstScans = config.GetValue("KioskCamera:MaxBurstScans", 4);
        var motionWakeCooldown = TimeSpan.FromSeconds(config.GetValue("KioskCamera:MotionWakeCooldownSeconds", 20));
        var requireLiveness = config.GetValue("KioskCamera:RequireLiveness", false);
        var minSimilarity = config.GetValue("KioskCamera:MinSimilarity", 0.58);
        var minSharpness = config.GetValue("KioskCamera:MinSharpness", 20d);
        var minBrightness = config.GetValue("KioskCamera:MinBrightness", 20d);
        var maxBrightness = config.GetValue("KioskCamera:MaxBrightness", 245d);
        var frameStaleSeconds = config.GetValue("KioskCamera:FrameStaleSeconds", 0d);
        var ffmpegOptions = config["KioskCamera:FfmpegCaptureOptions"];
        if (string.IsNullOrWhiteSpace(ffmpegOptions))
            ffmpegOptions = DefaultFfmpegOptions;
        var resolvedLatestFramePath = useImageFile ? ResolveConfiguredPath(latestFramePath!) : "";

        standbyInterval = Clamp(standbyInterval, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
        burstInterval = Clamp(burstInterval, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
        burstDuration = Clamp(burstDuration, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(2));
        reconnectDelay = Clamp(reconnectDelay, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
        cooldown = Clamp(cooldown, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10));
        templateRefresh = Clamp(templateRefresh, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10));
        idleScanInterval = idleScanInterval <= TimeSpan.Zero
            ? TimeSpan.Zero
            : Clamp(idleScanInterval, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30));
        _testScanInterval = Clamp(testScanInterval, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        motionRatio = Math.Clamp(motionRatio, 0.001, 0.5);
        openTimeoutMs = Math.Clamp(openTimeoutMs, 1000, 60000);
        readTimeoutMs = Math.Clamp(readTimeoutMs, 1000, 60000);
        scanMaxWidth = Math.Clamp(scanMaxWidth, 320, 1920);
        jpegQuality = Math.Clamp(jpegQuality, 55, 95);
        motionFrameWidth = Math.Clamp(motionFrameWidth, 96, 640);
        maxBurstScans = Math.Clamp(maxBurstScans, 1, 20);
        motionWakeCooldown = Clamp(motionWakeCooldown, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        minSimilarity = Math.Clamp(minSimilarity, faceEngine.MatchThreshold, 0.95);
        minSharpness = Math.Clamp(minSharpness, 0, 1000);
        minBrightness = Math.Clamp(minBrightness, 0, 255);
        maxBrightness = Math.Clamp(maxBrightness, minBrightness, 255);
        _frameStaleAfter = frameStaleSeconds > 0
            ? Clamp(TimeSpan.FromSeconds(frameStaleSeconds), TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(2))
            : Clamp(
                TimeSpan.FromMilliseconds(readTimeoutMs + Math.Max(standbyInterval.TotalMilliseconds, burstInterval.TotalMilliseconds) * 3),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(45));
        if (!useImageFile)
        {
            ffmpegOptions = EnsureFfmpegTimeoutOptions(ffmpegOptions, readTimeoutMs);
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", ffmpegOptions);
        }

        logger.LogInformation("Kiosk attendance worker enabled with {FrameSource} source.", useImageFile ? "snapshot" : "RTSP");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SetStatus(GetStatus() with
                {
                    Enabled = true,
                    CameraConnected = false,
                    Mode = "Đang kết nối",
                    LastFrameAt = null,
                    LastMotionScore = 0,
                    ScanBurstCount = 0,
                    Source = sourceLabel,
                    AutoAttendanceEnabled = IsAutoAttendanceEnabled(),
                    TestScanEnabled = IsTestScanEnabled(),
                    TestScanIntervalMs = (int)_testScanInterval.TotalMilliseconds,
                    LastMessage = useImageFile ? "Đang chờ ảnh snapshot từ FFmpeg..." : "Đang kết nối camera RTSP..."
                });

                if (useImageFile)
                {
                    await RunImageFileLoop(resolvedLatestFramePath, standbyInterval, burstInterval, burstDuration, cooldown,
                        templateRefresh, idleScanInterval, motionRatio, scanMaxWidth, jpegQuality,
                        motionFrameWidth, maxBurstScans, motionWakeCooldown, requireLiveness, minSimilarity,
                        minSharpness, minBrightness, maxBrightness, stoppingToken);
                    continue;
                }

                using var capture = OpenCapture(rtspUrl!, openTimeoutMs, readTimeoutMs);
                if (!capture.IsOpened())
                {
                    SetStatus(GetStatus() with
                    {
                        CameraConnected = false,
                        Mode = "Mất kết nối",
                        LastFrameAt = null,
                        LastMotionScore = 0,
                        ScanBurstCount = 0,
                        Source = sourceLabel,
                        AutoAttendanceEnabled = IsAutoAttendanceEnabled(),
                        TestScanEnabled = IsTestScanEnabled(),
                        TestScanIntervalMs = (int)_testScanInterval.TotalMilliseconds,
                        LastMessage = "Không mở được camera RTSP."
                    });
                    await Task.Delay(reconnectDelay, stoppingToken);
                    continue;
                }

                capture.Set(VideoCaptureProperties.BufferSize, 1);
                logger.LogInformation("RTSP attendance stream opened.");
                await RunCameraLoop(capture, standbyInterval, burstInterval, burstDuration, cooldown,
                    templateRefresh, idleScanInterval, motionRatio, scanMaxWidth, jpegQuality,
                    motionFrameWidth, maxBurstScans, motionWakeCooldown, requireLiveness, minSimilarity,
                    minSharpness, minBrightness, maxBrightness, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("RTSP attendance worker error: {Msg}", ex.Message);
                SetStatus(GetStatus() with { CameraConnected = false, Mode = "Lỗi", LastMotionScore = 0, ScanBurstCount = 0, LastMessage = ex.Message });
                try { await Task.Delay(reconnectDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static CameraFrameSource ResolveFrameSource(string? configured, bool hasRtspUrl, bool hasLatestFramePath)
    {
        var value = (configured ?? "Rtsp").Trim();
        if (value.Equals("ImageFile", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Snapshot", StringComparison.OrdinalIgnoreCase))
            return CameraFrameSource.ImageFile;

        if (value.Equals("Rtsp", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Camera", StringComparison.OrdinalIgnoreCase))
            return CameraFrameSource.Rtsp;

        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return hasRtspUrl ? CameraFrameSource.Rtsp : CameraFrameSource.ImageFile;

        return hasRtspUrl || !hasLatestFramePath ? CameraFrameSource.Rtsp : CameraFrameSource.ImageFile;
    }

    private static VideoCapture OpenCapture(string rtspUrl, int openTimeoutMs, int readTimeoutMs)
    {
        return new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG,
            [CapPropOpenTimeoutMsec, openTimeoutMs, CapPropReadTimeoutMsec, readTimeoutMs]);
    }

    private static string EnsureFfmpegTimeoutOptions(string ffmpegOptions, int readTimeoutMs)
    {
        var options = ffmpegOptions.Trim();
        if (ContainsFfmpegOption(options, "stimeout") || ContainsFfmpegOption(options, "timeout"))
            return options;

        var timeoutMicroseconds = Math.Max(readTimeoutMs, 1000) * 1000L;
        return $"{options}|stimeout;{timeoutMicroseconds}";
    }

    private static bool ContainsFfmpegOption(string options, string key)
    {
        foreach (var part in options.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith($"{key};", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task RunCameraLoop(
        VideoCapture capture,
        TimeSpan standbyInterval,
        TimeSpan burstInterval,
        TimeSpan burstDuration,
        TimeSpan cooldown,
        TimeSpan templateRefresh,
        TimeSpan idleScanInterval,
        double motionRatio,
        int scanMaxWidth,
        int jpegQuality,
        int motionFrameWidth,
        int maxBurstScans,
        TimeSpan motionWakeCooldown,
        bool requireLiveness,
        double minSimilarity,
        double minSharpness,
        double minBrightness,
        double maxBrightness,
        CancellationToken ct)
    {
        await RunFrameLoop(
            frame =>
            {
                if (!capture.Read(frame) || frame.Empty())
                    return new FrameReadResult(false, null, "Không đọc được khung hình từ camera RTSP.");

                return new FrameReadResult(true, DateTime.UtcNow, "");
            },
            throwOnReadFailure: true,
            openingMessage: "Đã mở luồng RTSP. Đang chờ khung hình đầu tiên.",
            connectedMessage: "Camera đã kết nối. Đang chờ chuyển động.",
            standbyInterval, burstInterval, burstDuration, cooldown,
            templateRefresh, idleScanInterval, motionRatio, scanMaxWidth, jpegQuality,
            motionFrameWidth, maxBurstScans, motionWakeCooldown, requireLiveness, minSimilarity,
            minSharpness, minBrightness, maxBrightness, ct);
    }

    private async Task RunImageFileLoop(
        string latestFramePath,
        TimeSpan standbyInterval,
        TimeSpan burstInterval,
        TimeSpan burstDuration,
        TimeSpan cooldown,
        TimeSpan templateRefresh,
        TimeSpan idleScanInterval,
        double motionRatio,
        int scanMaxWidth,
        int jpegQuality,
        int motionFrameWidth,
        int maxBurstScans,
        TimeSpan motionWakeCooldown,
        bool requireLiveness,
        double minSimilarity,
        double minSharpness,
        double minBrightness,
        double maxBrightness,
        CancellationToken ct)
    {
        await RunFrameLoop(
            frame => ReadImageFileFrame(latestFramePath, frame),
            throwOnReadFailure: false,
            openingMessage: $"Đang chờ file snapshot camera: {latestFramePath}",
            connectedMessage: "Snapshot camera đã sẵn sàng. Đang chờ chuyển động.",
            standbyInterval, burstInterval, burstDuration, cooldown,
            templateRefresh, idleScanInterval, motionRatio, scanMaxWidth, jpegQuality,
            motionFrameWidth, maxBurstScans, motionWakeCooldown, requireLiveness, minSimilarity,
            minSharpness, minBrightness, maxBrightness, ct);
    }

    private async Task RunFrameLoop(
        Func<Mat, FrameReadResult> readFrame,
        bool throwOnReadFailure,
        string openingMessage,
        string connectedMessage,
        TimeSpan standbyInterval,
        TimeSpan burstInterval,
        TimeSpan burstDuration,
        TimeSpan cooldown,
        TimeSpan templateRefresh,
        TimeSpan idleScanInterval,
        double motionRatio,
        int scanMaxWidth,
        int jpegQuality,
        int motionFrameWidth,
        int maxBurstScans,
        TimeSpan motionWakeCooldown,
        bool requireLiveness,
        double minSimilarity,
        double minSharpness,
        double minBrightness,
        double maxBrightness,
        CancellationToken ct)
    {
        using var frame = new Mat();
        Mat? previousGray = null;
        var reconnectSignalAtStart = Volatile.Read(ref _reconnectSignal);
        var burstUntil = DateTime.MinValue;
        var nextScanAt = DateTime.MinValue;
        var nextIdleScanAt = DateTime.UtcNow.Add(idleScanInterval);
        var nextTestScanAt = DateTime.MinValue;
        var testScanWasEnabled = false;
        var showResultUntil = DateTime.MinValue;
        var suppressBurstUntil = DateTime.MinValue;
        var burstScanCount = 0;
        var snapshotReadFailures = 0;

        try
        {
            SetStatus(GetStatus() with
            {
                CameraConnected = false,
                Mode = "Đang kết nối",
                LastFrameAt = null,
                LastMotionScore = 0,
                ScanBurstCount = 0,
                LastMessage = openingMessage
            });

            while (!ct.IsCancellationRequested)
            {
                if (Volatile.Read(ref _reconnectSignal) != reconnectSignalAtStart)
                {
                    UpdateStatus(s => s with
                    {
                        CameraConnected = false,
                        Mode = "Đang kết nối lại",
                        LastMotionScore = 0,
                        ScanBurstCount = 0,
                        LastMessage = "Đang mở lại luồng camera..."
                    });
                    return;
                }

                await RefreshTemplateCacheIfDue(templateRefresh, ct);
                var read = readFrame(frame);
                if (!read.Success || frame.Empty())
                {
                    previousGray?.Dispose();
                    previousGray = null;

                    if (throwOnReadFailure)
                        throw new InvalidOperationException(read.Message);

                    snapshotReadFailures++;
                    var retryDelay = GetSnapshotRetryDelay(snapshotReadFailures);
                    if (retryDelay is null)
                    {
                        UpdateStatus(s => s with
                        {
                            CameraConnected = false,
                            Mode = "Tạm tắt",
                            LastMotionScore = 0,
                            ScanBurstCount = 0,
                            LastMessage = "Mất tín hiệu."
                        });
                        await WaitForReconnectSignal(reconnectSignalAtStart, ct);
                        UpdateStatus(s => s with
                        {
                            CameraConnected = false,
                            Mode = "Đang kết nối lại",
                            LastMotionScore = 0,
                            ScanBurstCount = 0,
                            LastMessage = "Đang mở lại luồng camera..."
                        });
                        return;
                    }

                    UpdateStatus(s => s with
                    {
                        CameraConnected = false,
                        Mode = "Đang thử lại",
                        LastMotionScore = 0,
                        ScanBurstCount = 0,
                        LastMessage = BuildSnapshotRetryMessage(read.Message, retryDelay.Value)
                    });
                    if (await DelayUntilReconnectOrTimeout(retryDelay.Value, reconnectSignalAtStart, ct))
                    {
                        UpdateStatus(s => s with
                        {
                            CameraConnected = false,
                            Mode = "Đang kết nối lại",
                            LastMotionScore = 0,
                            ScanBurstCount = 0,
                            LastMessage = "Đang mở lại luồng camera..."
                        });
                        return;
                    }
                    continue;
                }

                snapshotReadFailures = 0;
                var now = DateTime.UtcNow;
                var frameAt = read.FrameAt ?? now;
                var currentMotionRatio = DetectMotionRatio(frame, ref previousGray, motionFrameWidth);
                var hasMotion = currentMotionRatio >= motionRatio;
                var inBurst = now <= burstUntil;
                var autoAttendanceEnabled = IsAutoAttendanceEnabled();
                var testScanEnabled = IsTestScanEnabled();
                if (testScanEnabled && !testScanWasEnabled)
                {
                    burstUntil = DateTime.MinValue;
                    nextScanAt = DateTime.MinValue;
                    burstScanCount = 0;
                    nextTestScanAt = DateTime.MinValue;
                }
                testScanWasEnabled = testScanEnabled;

                UpdateStatus(s => s with
                {
                    CameraConnected = true,
                    Mode = s.Mode is "Đang kết nối" or "Mất kết nối" or "Lỗi"
                        ? (testScanEnabled ? "Test scan" : autoAttendanceEnabled ? "Chờ" : "Tạm dừng")
                        : s.Mode,
                    LastFrameAt = frameAt,
                    LastMotionScore = currentMotionRatio,
                    AutoAttendanceEnabled = autoAttendanceEnabled,
                    TestScanEnabled = testScanEnabled,
                    TestScanIntervalMs = (int)_testScanInterval.TotalMilliseconds,
                    LastMessage = s.Mode is "Đang kết nối" or "Mất kết nối" or "Đang thử lại" or "Lỗi"
                        ? connectedMessage
                        : s.LastMessage
                });

                if (testScanEnabled)
                {
                    if (now >= nextTestScanAt)
                    {
                        nextTestScanAt = now.Add(_testScanInterval);
                        UpdateStatus(s => s with
                        {
                            Mode = "Test scan",
                            LastScanAt = now,
                            ScanBurstCount = 1,
                            LastMessage = "Đang test scan..."
                        });
                        await TryRecognizeFrame(frame, cooldown, templateRefresh,
                            scanMaxWidth, jpegQuality, requireLiveness, minSimilarity,
                            minSharpness, minBrightness, maxBrightness, testMode: true, ct: ct);
                    }

                    await Task.Delay(standbyInterval, ct);
                    continue;
                }

                if (!autoAttendanceEnabled)
                {
                    burstUntil = DateTime.MinValue;
                    nextScanAt = DateTime.MinValue;
                    burstScanCount = 0;
                    UpdateStatus(s => s with
                    {
                        Mode = "Tạm dừng",
                        ScanBurstCount = 0,
                        LastMessage = "Đã tạm dừng chấm công nhận diện tự động."
                    });
                    await Task.Delay(standbyInterval, ct);
                    continue;
                }

                if (hasMotion && now >= suppressBurstUntil)
                {
                    nextIdleScanAt = idleScanInterval > TimeSpan.Zero ? now.Add(idleScanInterval) : DateTime.MaxValue;
                    if (!inBurst)
                        burstScanCount = 0;

                    if (burstScanCount < maxBurstScans)
                    {
                        burstUntil = now.Add(burstDuration);
                        UpdateStatus(s => s with
                        {
                            CameraConnected = true,
                            Mode = "Có chuyển động",
                            LastMotionAt = now,
                            ScanBurstCount = burstScanCount,
                            LastMessage = "Phát hiện chuyển động. Bắt đầu quét khuôn mặt."
                        });
                    }
                }
                else if (!inBurst && idleScanInterval > TimeSpan.Zero && now >= nextIdleScanAt)
                {
                    burstScanCount = 0;
                    burstUntil = now.Add(TimeSpan.FromMilliseconds(
                        Math.Max(burstInterval.TotalMilliseconds * maxBurstScans, 1000)));
                    nextScanAt = now;
                    nextIdleScanAt = now.Add(idleScanInterval);
                    UpdateStatus(s => s with
                    {
                        CameraConnected = true,
                        Mode = "Đang quét",
                        LastMessage = "Đang quét khuôn mặt định kỳ."
                    });
                }

                if (now <= burstUntil && now >= nextScanAt)
                {
                    burstScanCount++;
                    nextScanAt = now.Add(burstInterval);
                    UpdateStatus(s => s with { Mode = "Đang quét", LastScanAt = now, ScanBurstCount = burstScanCount });
                    var matched = await TryRecognizeFrame(frame, cooldown, templateRefresh,
                        scanMaxWidth, jpegQuality, requireLiveness, minSimilarity,
                        minSharpness, minBrightness, maxBrightness, testMode: false, ct: ct);
                    if (matched)
                    {
                        burstUntil = DateTime.MinValue;
                        nextScanAt = DateTime.MinValue;
                        nextIdleScanAt = idleScanInterval > TimeSpan.Zero ? DateTime.UtcNow.Add(idleScanInterval) : DateTime.MaxValue;
                        showResultUntil = DateTime.UtcNow.AddSeconds(5);
                        burstScanCount = 0;
                    }
                    else if (burstScanCount >= maxBurstScans)
                    {
                        burstUntil = DateTime.MinValue;
                        nextScanAt = DateTime.MinValue;
                        suppressBurstUntil = now.Add(motionWakeCooldown);
                        nextIdleScanAt = idleScanInterval > TimeSpan.Zero ? suppressBurstUntil : DateTime.MaxValue;
                        UpdateStatus(s => s with
                        {
                            Mode = "Chờ",
                            ScanBurstCount = 0,
                            LastMessage = $"Chưa nhận diện được sau {maxBurstScans} lần quét. Tạm nghỉ ngắn trước khi quét lại."
                        });
                    }
                }
                else if (now > burstUntil)
                {
                    var current = GetStatus();
                    if (current.Mode is not "Chờ" && now >= showResultUntil)
                        SetStatus(current with { Mode = "Chờ", ScanBurstCount = 0, LastMessage = "Đang chờ chuyển động." });
                }

                await Task.Delay(now <= burstUntil ? burstInterval : standbyInterval, ct);
            }
        }
        finally
        {
            previousGray?.Dispose();
        }
    }

    private static TimeSpan? GetSnapshotRetryDelay(int failureCount)
    {
        var index = failureCount - 1;
        return index >= 0 && index < SnapshotRetryBackoff.Length
            ? SnapshotRetryBackoff[index]
            : null;
    }

    private static string BuildSnapshotRetryMessage(string message, TimeSpan retryDelay)
        => $"{message} Đang thử lại sau {retryDelay.TotalSeconds:0} giây.";

    private async Task<bool> DelayUntilReconnectOrTimeout(TimeSpan delay, int reconnectSignalAtStart, CancellationToken ct)
    {
        var until = DateTime.UtcNow.Add(delay);
        while (DateTime.UtcNow < until)
        {
            if (Volatile.Read(ref _reconnectSignal) != reconnectSignalAtStart)
                return true;

            var remaining = until - DateTime.UtcNow;
            await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, ct);
        }

        return Volatile.Read(ref _reconnectSignal) != reconnectSignalAtStart;
    }

    private async Task WaitForReconnectSignal(int reconnectSignalAtStart, CancellationToken ct)
    {
        while (Volatile.Read(ref _reconnectSignal) == reconnectSignalAtStart)
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }

    private FrameReadResult ReadImageFileFrame(string latestFramePath, Mat destination)
    {
        if (!File.Exists(latestFramePath))
            return new FrameReadResult(false, null, "Đang chờ FFmpeg ghi ảnh camera đầu tiên.");

        var lastWriteUtc = File.GetLastWriteTimeUtc(latestFramePath);
        var age = DateTime.UtcNow - lastWriteUtc;
        if (age > _frameStaleAfter)
        {
            return new FrameReadResult(false, lastWriteUtc,
                $"Ảnh camera mới nhất đã cũ ({Math.Ceiling(age.TotalSeconds):0} giây).");
        }

        try
        {
            using var decoded = Cv2.ImRead(latestFramePath, ImreadModes.Color);
            if (decoded.Empty())
                return new FrameReadResult(false, lastWriteUtc, "Ảnh camera mới nhất đang được ghi hoặc không hợp lệ.");

            decoded.CopyTo(destination);
            return new FrameReadResult(true, lastWriteUtc, "");
        }
        catch (Exception ex)
        {
            return new FrameReadResult(false, lastWriteUtc, $"Không đọc được ảnh camera mới nhất: {ex.Message}");
        }
    }

    private string ResolveConfiguredPath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        return Path.GetFullPath(Path.Combine(env.ContentRootPath, path));
    }

    private async Task<bool> LoadAutoAttendanceEnabled(bool defaultValue, CancellationToken ct)
    {
        try
        {
            await using var conn = await db.OpenAsync(ct);
            var value = await conn.Cmd(
                @"SELECT TOP 1 setting_value
                  FROM dbo.web_system_settings
                  WHERE setting_key = @key")
                .With("@key", AutoAttendanceSettingKey)
                .ExecuteScalarAsync(ct);
            return ParseBoolSetting(value, defaultValue);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Cannot load automatic attendance setting: {Msg}", ex.Message);
            return defaultValue;
        }
    }

    private static bool ParseBoolSetting(object? value, bool defaultValue)
    {
        if (value is null or DBNull)
            return defaultValue;

        var text = Convert.ToString(value)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;

        if (bool.TryParse(text, out var parsed))
            return parsed;

        return text == "1" ? true : text == "0" ? false : defaultValue;
    }

    private static double DetectMotionRatio(Mat frame, ref Mat? previousGray, int motionFrameWidth)
    {
        using var small = new Mat();
        using var gray = new Mat();
        using var diff = new Mat();
        using var mask = new Mat();

        var motionFrameHeight = Math.Max(1, (int)Math.Round(frame.Height * (motionFrameWidth / (double)frame.Width)));
        Cv2.Resize(frame, small, new Size(motionFrameWidth, motionFrameHeight), 0, 0, InterpolationFlags.Area);
        Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(7, 7), 0);

        if (previousGray is null || previousGray.Empty())
        {
            previousGray?.Dispose();
            previousGray = gray.Clone();
            return 0;
        }

        Cv2.Absdiff(gray, previousGray, diff);
        Cv2.Threshold(diff, mask, 25, 255, ThresholdTypes.Binary);
        var changedRatio = Cv2.CountNonZero(mask) / (double)(mask.Rows * mask.Cols);

        previousGray.Dispose();
        previousGray = gray.Clone();

        return changedRatio;
    }

    private async Task<bool> TryRecognizeFrame(
        Mat frame,
        TimeSpan cooldown,
        TimeSpan templateRefresh,
        int scanMaxWidth,
        int jpegQuality,
        bool requireLiveness,
        double minSimilarity,
        double minSharpness,
        double minBrightness,
        double maxBrightness,
        bool testMode,
        CancellationToken ct)
    {
        var quality = CheckFrameQuality(frame, minSharpness, minBrightness, maxBrightness);
        if (!quality.Passed)
        {
            UpdateStatus(s => s with { LastSimilarity = 0, LastMessage = quality.Message });
            return false;
        }

        var bytes = EncodeForRecognition(frame, scanMaxWidth, jpegQuality);
        if (bytes.Length == 0)
            return false;

        if (requireLiveness && !faceEngine.CheckLiveness(bytes))
        {
            UpdateStatus(s => s with { LastSimilarity = 0, LastMessage = "Face not found or liveness check failed." });
            return false;
        }

        var probe = faceEngine.ExtractEmbedding(bytes);
        if (probe is null)
        {
            UpdateStatus(s => s with { LastSimilarity = 0, LastMessage = "Không phát hiện khuôn mặt trong lượt quét." });
            return false;
        }

        await using var conn = await db.OpenAsync(ct);
        var templates = await LoadTemplatesCached(conn, templateRefresh, ct);
        if (templates.Count == 0)
        {
            UpdateStatus(s => s with { EnrolledTemplates = 0, LastSimilarity = 0, LastMessage = "No enrolled faces found." });
            return false;
        }

        FaceTemplate? bestTemplate = null;
        var best = 0d;
        foreach (var template in templates)
        {
            var similarity = faceEngine.Compare(probe, template.Embedding);
            if (similarity > best)
            {
                best = similarity;
                bestTemplate = template;
            }
        }

        var requiredSimilarity = Math.Max(faceEngine.MatchThreshold, minSimilarity);
        if (bestTemplate is null || best < requiredSimilarity)
        {
            UpdateStatus(s => s with
            {
                EnrolledTemplates = templates.Count,
                LastSimilarity = best,
                LastMessage = $"No match. Best similarity {best:0.000}; required {requiredSimilarity:0.000}."
            });
            return false;
        }

        var now = DateTime.UtcNow;
        if (testMode)
        {
            UpdateStatus(s => s with
            {
                Mode = "TestScan",
                LastMatchedAt = now,
                LastMatchedUser = bestTemplate.Username,
                LastMatchedName = bestTemplate.FullName,
                LastSimilarity = best,
                EnrolledTemplates = templates.Count,
                LastMessage = $"Test khớp: {bestTemplate.FullName} ({best:0.000}). Chưa ghi chấm công."
            });
            return true;
        }

        PruneCooldowns(now);
        if (_cooldownUntil.TryGetValue(bestTemplate.Username, out var until) && now < until)
        {
            UpdateStatus(s => s with
            {
                Mode = "Cooldown",
                LastMatchedAt = now,
                LastMatchedUser = bestTemplate.Username,
                LastMatchedName = bestTemplate.FullName,
                LastSimilarity = best,
                EnrolledTemplates = templates.Count,
                LastMessage = $"{bestTemplate.FullName} đang trong thời gian chờ."
            });
            return true;
        }

        var decision = await RecordAttendance(conn, bestTemplate, best, ct);
        _cooldownUntil[bestTemplate.Username] = now.Add(cooldown);

        UpdateStatus(s => s with
        {
            Mode = "Matched",
            LastMatchedAt = now,
            LastMatchedUser = bestTemplate.Username,
            LastMatchedName = bestTemplate.FullName,
            LastSimilarity = best,
            EnrolledTemplates = templates.Count,
            LastMessage = decision.Message
        });

        if (decision.ShouldRecord)
        {
            logger.LogInformation("RTSP attendance recorded for {Username} ({Similarity:0.000}).",
                bestTemplate.Username, best);
            await hub.Clients.All.SendAsync("changed", "all", ct);
        }

        return true;
    }

    private async Task RefreshTemplateCacheIfDue(TimeSpan refreshInterval, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        lock (_templateGate)
        {
            if (now - _templateCacheLoadedAt < refreshInterval)
            {
                UpdateStatus(s => s with { EnrolledTemplates = _templateCache.Count });
                return;
            }
        }

        try
        {
            await using var conn = await db.OpenAsync(ct);
            var templates = await LoadTemplates(conn, ct);
            lock (_templateGate)
            {
                _templateCache = templates;
                _templateCacheLoadedAt = DateTime.UtcNow;
            }
            UpdateStatus(s => s with { EnrolledTemplates = templates.Count });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Cannot refresh RTSP attendance face templates: {Msg}", ex.Message);
        }
    }

    private static (bool Passed, string Message) CheckFrameQuality(
        Mat frame,
        double minSharpness,
        double minBrightness,
        double maxBrightness)
    {
        using var gray = new Mat();
        if (frame.Channels() == 1)
            frame.CopyTo(gray);
        else
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

        var brightness = Cv2.Mean(gray).Val0;
        if (brightness < minBrightness)
            return (false, $"Ảnh quá tối ({brightness:0}). Vui lòng đứng gần nguồn sáng hơn.");

        if (brightness > maxBrightness)
            return (false, $"Ảnh quá sáng ({brightness:0}). Vui lòng tránh ngược sáng.");

        if (minSharpness > 0)
        {
            using var laplacian = new Mat();
            Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
            Cv2.MeanStdDev(laplacian, out _, out var stddev);
            var sharpness = stddev.Val0 * stddev.Val0;
            if (sharpness < minSharpness)
                return (false, $"Ảnh bị mờ ({sharpness:0.0}). Vui lòng đứng yên và nhìn thẳng camera.");
        }

        return (true, "");
    }

    private static byte[] EncodeForRecognition(Mat frame, int maxWidth, int jpegQuality)
    {
        Mat? resized = null;
        var source = frame;

        try
        {
            if (frame.Width > maxWidth)
            {
                var scale = maxWidth / (double)frame.Width;
                var size = new Size(maxWidth, Math.Max(1, (int)Math.Round(frame.Height * scale)));
                resized = new Mat();
                Cv2.Resize(frame, resized, size, 0, 0, InterpolationFlags.Area);
                source = resized;
            }

            Cv2.ImEncode(".jpg", source, out var bytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality));
            return bytes;
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private async Task<IReadOnlyList<FaceTemplate>> LoadTemplatesCached(
        SqlConnection conn,
        TimeSpan refreshInterval,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        lock (_templateGate)
        {
            if (now - _templateCacheLoadedAt < refreshInterval)
                return _templateCache;
        }

        var templates = await LoadTemplates(conn, ct);
        lock (_templateGate)
        {
            _templateCache = templates;
            _templateCacheLoadedAt = now;
        }
        UpdateStatus(s => s with { EnrolledTemplates = templates.Count });

        return templates;
    }

    private static async Task<List<FaceTemplate>> LoadTemplates(SqlConnection conn, CancellationToken ct)
    {
        var templates = new List<FaceTemplate>();
        await using var reader = await conn.Cmd(
            @"SELECT f.username, f.full_name, f.embedding
              FROM dbo.cham_cong_face f
              JOIN dbo.app_users u ON u.username = f.username AND u.is_deleted = 0 AND u.is_active = 1
              ORDER BY f.username, f.id")
            .ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            if (reader["embedding"] is byte[] embeddingBytes && embeddingBytes.Length > 0)
                templates.Add(new FaceTemplate(reader.Str("username"), reader.Str("full_name"), EmbeddingCodec.FromBytes(embeddingBytes)));
        }

        return templates;
    }

    private void PruneCooldowns(DateTime now)
    {
        if (_cooldownUntil.Count == 0)
            return;

        foreach (var stale in _cooldownUntil.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToArray())
            _cooldownUntil.Remove(stale);
    }

    private async Task<AttendanceDecision> RecordAttendance(
        SqlConnection conn,
        FaceTemplate template,
        double similarity,
        CancellationToken ct)
    {
        var decision = await AttendancePolicy.DecideAsync(conn, template.Username, template.FullName, ct);
        if (!decision.ShouldRecord)
            return decision;

        await conn.Cmd(
            @"INSERT INTO dbo.cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
              VALUES (@u, @fn, @loai, @sim, SYSUTCDATETIME(), N'RTSP kiosk')")
            .With("@u", template.Username)
            .With("@fn", template.FullName)
            .With("@loai", decision.Loai)
            .With("@sim", similarity)
            .ExecuteNonQueryAsync(ct);

        await db.RecordAudit(template.Username, $"Chấm công {decision.Loai}", "ChamCong", template.Username,
            $"RTSP kiosk, độ khớp {similarity:0.000}.");
        return decision;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private void SetStatus(RtspAttendanceStatus status)
    {
        lock (_statusGate) _status = status;
    }

    private void UpdateStatus(Func<RtspAttendanceStatus, RtspAttendanceStatus> update)
    {
        lock (_statusGate) _status = update(_status);
    }

    private RtspAttendanceStatus ApplyFrameWatchdog(RtspAttendanceStatus status, DateTime now)
    {
        if (!status.Enabled || !status.CameraConnected || status.LastFrameAt is not { } lastFrameAt)
            return status;

        var silence = now - lastFrameAt;
        if (silence <= _frameStaleAfter)
            return status;

        return status with
        {
            CameraConnected = false,
            Mode = "Mất kết nối",
            LastMotionScore = 0,
            ScanBurstCount = 0,
            LastMessage = $"Không có khung hình RTSP trong {Math.Ceiling(silence.TotalSeconds):0} giây. Đang kết nối lại camera..."
        };
    }
}
