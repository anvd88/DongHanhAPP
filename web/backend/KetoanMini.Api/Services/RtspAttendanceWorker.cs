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
    int EnrolledTemplates = 0);

public sealed class RtspAttendanceWorker(
    IConfiguration config,
    Database db,
    IFaceEngine faceEngine,
    IHubContext<ChangesHub> hub,
    ILogger<RtspAttendanceWorker> logger) : BackgroundService
{
    private const int CapPropOpenTimeoutMsec = 53;
    private const int CapPropReadTimeoutMsec = 54;
    private const string DefaultFfmpegOptions = "rtsp_transport;tcp|stimeout;5000000|max_delay;500000";
    private sealed record FaceTemplate(string Username, string FullName, float[] Embedding);

    private readonly object _statusGate = new();
    private readonly object _templateGate = new();
    private readonly Dictionary<string, DateTime> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<FaceTemplate> _templateCache = [];
    private DateTime _templateCacheLoadedAt = DateTime.MinValue;
    private RtspAttendanceStatus _status = new(false, false, "Disabled", null, null, null, "", "", "RTSP attendance is disabled.");

    public RtspAttendanceStatus GetStatus()
    {
        lock (_statusGate) return _status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rtspUrl = config["KioskCamera:RtspUrl"];
        var enabled = config.GetValue<bool?>("KioskCamera:Enabled") ?? !string.IsNullOrWhiteSpace(rtspUrl);
        if (!enabled || string.IsNullOrWhiteSpace(rtspUrl))
        {
            SetStatus(new RtspAttendanceStatus(false, false, "Disabled", null, null, null, "", "",
                "Set KioskCamera:Enabled=true and KioskCamera:RtspUrl to enable RTSP attendance."));
            return;
        }

        var standbyInterval = TimeSpan.FromMilliseconds(config.GetValue("KioskCamera:StandbyFrameIntervalMs", 1000));
        var burstInterval = TimeSpan.FromMilliseconds(config.GetValue("KioskCamera:BurstFrameIntervalMs", 1200));
        var burstDuration = TimeSpan.FromSeconds(config.GetValue("KioskCamera:BurstDurationSeconds", 15));
        var reconnectDelay = TimeSpan.FromSeconds(config.GetValue("KioskCamera:ReconnectDelaySeconds", 5));
        var cooldown = TimeSpan.FromSeconds(config.GetValue("KioskCamera:CooldownSeconds", 60));
        var templateRefresh = TimeSpan.FromSeconds(config.GetValue("KioskCamera:TemplateRefreshSeconds", 30));
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
        var ffmpegOptions = config["KioskCamera:FfmpegCaptureOptions"];
        if (string.IsNullOrWhiteSpace(ffmpegOptions))
            ffmpegOptions = DefaultFfmpegOptions;
        Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", ffmpegOptions);

        standbyInterval = Clamp(standbyInterval, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
        burstInterval = Clamp(burstInterval, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10));
        burstDuration = Clamp(burstDuration, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(2));
        reconnectDelay = Clamp(reconnectDelay, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
        cooldown = Clamp(cooldown, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10));
        templateRefresh = Clamp(templateRefresh, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10));
        motionRatio = Math.Clamp(motionRatio, 0.001, 0.5);
        openTimeoutMs = Math.Clamp(openTimeoutMs, 1000, 60000);
        readTimeoutMs = Math.Clamp(readTimeoutMs, 1000, 60000);
        scanMaxWidth = Math.Clamp(scanMaxWidth, 320, 1920);
        jpegQuality = Math.Clamp(jpegQuality, 55, 95);
        motionFrameWidth = Math.Clamp(motionFrameWidth, 96, 640);
        maxBurstScans = Math.Clamp(maxBurstScans, 1, 20);
        motionWakeCooldown = Clamp(motionWakeCooldown, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        minSimilarity = Math.Clamp(minSimilarity, faceEngine.MatchThreshold, 0.95);

        logger.LogInformation("RTSP attendance worker enabled.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SetStatus(GetStatus() with { Enabled = true, CameraConnected = false, Mode = "Connecting", LastMessage = "Connecting to RTSP camera..." });
                using var capture = OpenCapture(rtspUrl, ffmpegOptions, openTimeoutMs, readTimeoutMs);
                if (!capture.IsOpened())
                {
                    SetStatus(GetStatus() with { CameraConnected = false, Mode = "Disconnected", LastMessage = "Cannot open RTSP camera." });
                    await Task.Delay(reconnectDelay, stoppingToken);
                    continue;
                }

                capture.Set(VideoCaptureProperties.BufferSize, 1);
                logger.LogInformation("RTSP attendance camera connected.");
                await RunCameraLoop(capture, standbyInterval, burstInterval, burstDuration, cooldown,
                    templateRefresh, motionRatio, scanMaxWidth, jpegQuality,
                    motionFrameWidth, maxBurstScans, motionWakeCooldown, requireLiveness, minSimilarity, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("RTSP attendance worker error: {Msg}", ex.Message);
                SetStatus(GetStatus() with { CameraConnected = false, Mode = "Error", LastMessage = ex.Message });
                try { await Task.Delay(reconnectDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static VideoCapture OpenCapture(string rtspUrl, string ffmpegOptions, int openTimeoutMs, int readTimeoutMs)
    {
        if (ffmpegOptions.Contains("rtsp_transport;udp", StringComparison.OrdinalIgnoreCase))
            return new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG);

        return new VideoCapture(rtspUrl, VideoCaptureAPIs.FFMPEG,
            [CapPropOpenTimeoutMsec, openTimeoutMs, CapPropReadTimeoutMsec, readTimeoutMs]);
    }

    private async Task RunCameraLoop(
        VideoCapture capture,
        TimeSpan standbyInterval,
        TimeSpan burstInterval,
        TimeSpan burstDuration,
        TimeSpan cooldown,
        TimeSpan templateRefresh,
        double motionRatio,
        int scanMaxWidth,
        int jpegQuality,
        int motionFrameWidth,
        int maxBurstScans,
        TimeSpan motionWakeCooldown,
        bool requireLiveness,
        double minSimilarity,
        CancellationToken ct)
    {
        using var frame = new Mat();
        Mat? previousGray = null;
        var burstUntil = DateTime.MinValue;
        var nextScanAt = DateTime.MinValue;
        var showResultUntil = DateTime.MinValue;
        var suppressBurstUntil = DateTime.MinValue;
        var burstScanCount = 0;

        try
        {
            SetStatus(GetStatus() with { CameraConnected = true, Mode = "Standby", LastMessage = "Camera connected. Waiting for motion." });

            while (!ct.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                    throw new InvalidOperationException("Cannot read frame from RTSP camera.");

                var now = DateTime.UtcNow;
                var currentMotionRatio = DetectMotionRatio(frame, ref previousGray, motionFrameWidth);
                var hasMotion = currentMotionRatio >= motionRatio;
                var inBurst = now <= burstUntil;
                UpdateStatus(s => s with
                {
                    CameraConnected = true,
                    LastFrameAt = now,
                    LastMotionScore = currentMotionRatio
                });

                if (hasMotion && now >= suppressBurstUntil)
                {
                    if (!inBurst)
                        burstScanCount = 0;

                    if (burstScanCount < maxBurstScans)
                    {
                        burstUntil = now.Add(burstDuration);
                        UpdateStatus(s => s with
                        {
                            CameraConnected = true,
                            Mode = "Motion",
                            LastMotionAt = now,
                            ScanBurstCount = burstScanCount,
                            LastMessage = "Motion detected. Starting face scan burst."
                        });
                    }
                }

                if (now <= burstUntil && now >= nextScanAt)
                {
                    burstScanCount++;
                    nextScanAt = now.Add(burstInterval);
                    UpdateStatus(s => s with { Mode = "Scanning", LastScanAt = now, ScanBurstCount = burstScanCount });
                    var matched = await TryRecognizeFrame(frame, cooldown, templateRefresh,
                        scanMaxWidth, jpegQuality, requireLiveness, minSimilarity, ct);
                    if (matched)
                    {
                        burstUntil = DateTime.MinValue;
                        nextScanAt = DateTime.MinValue;
                        showResultUntil = DateTime.UtcNow.AddSeconds(5);
                        burstScanCount = 0;
                    }
                    else if (burstScanCount >= maxBurstScans)
                    {
                        burstUntil = DateTime.MinValue;
                        nextScanAt = DateTime.MinValue;
                        suppressBurstUntil = now.Add(motionWakeCooldown);
                        UpdateStatus(s => s with
                        {
                            Mode = "Standby",
                            ScanBurstCount = 0,
                            LastMessage = $"No face match after {maxBurstScans} scans. Pausing motion wake briefly."
                        });
                    }
                }
                else if (now > burstUntil)
                {
                    var current = GetStatus();
                    if (current.Mode is not "Standby" && now >= showResultUntil)
                        SetStatus(current with { Mode = "Standby", ScanBurstCount = 0, LastMessage = "Waiting for motion." });
                }

                await Task.Delay(now <= burstUntil ? burstInterval : standbyInterval, ct);
            }
        }
        finally
        {
            previousGray?.Dispose();
        }
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
        CancellationToken ct)
    {
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
            UpdateStatus(s => s with { LastSimilarity = 0, LastMessage = "No face detected in scan burst." });
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
}
