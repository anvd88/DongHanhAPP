using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Cấu hình ứng dụng điều khiển TỪ XA (remote config): admin đổi được một số thứ mà KHÔNG cần phát hành
/// APK mới — thông báo chạy chữ trong app, bật/tắt banner nhắc đăng ký khuôn mặt, nhịp tự làm mới nền…
/// App đọc lúc đăng nhập + mỗi lần quay lại foreground (có tiết chế) rồi áp dụng ngay.
/// Lưu 1 dòng duy nhất (id=1) cho gọn; đọc = mọi user đăng nhập, ghi = chỉ admin.
/// </summary>
public static class AppConfigEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS app_config (
                id integer PRIMARY KEY,
                announcement text NOT NULL DEFAULT '',
                announcement_level varchar(16) NOT NULL DEFAULT 'info',
                face_enroll_banner_enabled boolean NOT NULL DEFAULT TRUE,
                foreground_poll_seconds integer NOT NULL DEFAULT 20,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_by varchar(128) NOT NULL DEFAULT '',
                CONSTRAINT app_config_singleton CHECK (id = 1)
            );
            INSERT INTO app_config (id) VALUES (1) ON CONFLICT (id) DO NOTHING;

            -- Tham số CẮT ẢNH CHÂN DUNG trên app (ảnh thẻ "từ cổ hắt lên") — chỉnh từ xa, khỏi build APK.
            -- height_factor: chiều cao khung cắt = bấy nhiêu lần chiều cao mặt (lớn = lấy rộng hơn).
            -- vertical_nudge: nhích tâm khung theo chiều cao mặt (dương = lên cao, lấy nhiều đỉnh đầu).
            -- aspect: tỉ lệ ngang/dọc của ảnh (0.75 = 3:4). min_width_factor: bề rộng tối thiểu theo bề rộng mặt.
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS portrait_height_factor double precision NOT NULL DEFAULT 1.85;
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS portrait_vertical_nudge double precision NOT NULL DEFAULT 0.15;
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS portrait_aspect double precision NOT NULL DEFAULT 0.75;
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS portrait_min_width_factor double precision NOT NULL DEFAULT 1.35;

            -- Cấu hình GỌI THOẠI/VIDEO điều khiển từ xa (jsonb để thêm tham số sau này khỏi đổi schema).
            -- Backend chỉnh được: STUN, ép relay qua TURN, timeout, độ phân giải/FPS/bitrate video, và
            -- công tắc bật/tắt gọi thoại/video — app áp dụng ngay, KHÔNG cần build lại APK.
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS call_config jsonb NOT NULL DEFAULT '{}'::jsonb;

            -- Công tắc TÍNH NĂNG điều khiển từ xa (bật/tắt không cần phát hành APK): vị trí, chấm công
            -- ngoại tuyến, chấm công sinh trắc, gửi tệp trong chat, cổng thông tin… thêm cờ mới khỏi đổi schema.
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS feature_flags jsonb NOT NULL DEFAULT '{}'::jsonb;

            -- Nội dung ONBOARDING / lý do xin quyền (camera, vị trí, thông báo, micro) sửa từ xa — app hiển thị
            -- khi xin quyền, đổi câu chữ mà KHÔNG cần build lại APK.
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS onboarding jsonb NOT NULL DEFAULT '{}'::jsonb;

            -- Danh sách LỜI NHẮC/THÔNG BÁO chạy chữ trên Trang chủ app (mỗi phần tử một dòng), sửa từ xa.
            -- Khác với "announcement" (một câu duy nhất, có mức độ + banner): đây là NHIỀU lời nhắc luân phiên
            -- cùng dải gõ chữ với lời chào theo buổi và nhắc chấm công. Bỏ trống ⇒ chỉ còn lời nhắc gắn sẵn.
            ALTER TABLE app_config ADD COLUMN IF NOT EXISTS notices jsonb NOT NULL DEFAULT '[]'::jsonb;
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapAppConfig(this WebApplication app)
    {
        // Đọc: mọi user đăng nhập (web + app) đều lấy được cấu hình để áp dụng.
        app.MapGet("/api/app-config", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            return Results.Ok(await Read(conn));
        }).RequireAuthorization();

        // Ghi: chỉ admin. Trường nào null thì giữ nguyên (patch từng phần).
        app.MapPut("/api/app-config", async (AppConfigPatch req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var level = Normalize(req.AnnouncementLevel);
            var poll = req.ForegroundPollSeconds is int s ? Math.Clamp(s, 5, 3600) : (int?)null;
            // Kẹp các tham số cắt ảnh vào khoảng an toàn (tránh cấu hình sai làm hỏng ảnh).
            var hFactor = req.PortraitHeightFactor is double hf ? Math.Clamp(hf, 1.0, 4.0) : (double?)null;
            var vNudge = req.PortraitVerticalNudge is double vn ? Math.Clamp(vn, -1.0, 1.0) : (double?)null;
            var aspect = req.PortraitAspect is double ap ? Math.Clamp(ap, 0.4, 1.0) : (double?)null;
            var minW = req.PortraitMinWidthFactor is double mw ? Math.Clamp(mw, 0.5, 3.0) : (double?)null;
            // Cấu hình gọi: chỉ ghi khi patch có gửi khối "call" (đã kẹp giá trị vào khoảng an toàn).
            var callJson = req.Call is null ? null : JsonSerializer.Serialize(Clamp(req.Call));
            // Công tắc tính năng + nội dung onboarding: chỉ ghi khi patch có gửi khối tương ứng.
            var featuresJson = req.Features is null ? null : JsonSerializer.Serialize(req.Features);
            var onboardingJson = req.Onboarding is null ? null : JsonSerializer.Serialize(Clamp(req.Onboarding));
            // Lời nhắc chạy chữ: chỉ ghi khi patch có gửi (kể cả mảng rỗng = xoá hết); đã lọc rỗng & kẹp độ dài/số lượng.
            var noticesJson = req.Notices is null ? null : JsonSerializer.Serialize(CleanNotices(req.Notices));
            await conn.Cmd("""
                UPDATE app_config SET
                    announcement = COALESCE(@ann, announcement),
                    announcement_level = COALESCE(@lvl, announcement_level),
                    face_enroll_banner_enabled = COALESCE(@face, face_enroll_banner_enabled),
                    foreground_poll_seconds = COALESCE(@poll, foreground_poll_seconds),
                    portrait_height_factor = COALESCE(@phf, portrait_height_factor),
                    portrait_vertical_nudge = COALESCE(@pvn, portrait_vertical_nudge),
                    portrait_aspect = COALESCE(@pas, portrait_aspect),
                    portrait_min_width_factor = COALESCE(@pmw, portrait_min_width_factor),
                    call_config = COALESCE(@call::jsonb, call_config),
                    feature_flags = COALESCE(@features::jsonb, feature_flags),
                    onboarding = COALESCE(@onboarding::jsonb, onboarding),
                    notices = COALESCE(@notices::jsonb, notices),
                    updated_at = CURRENT_TIMESTAMP,
                    updated_by = @by
                WHERE id = 1
                """)
                .With("@ann", (object?)req.Announcement ?? DBNull.Value)
                .With("@lvl", (object?)level ?? DBNull.Value)
                .With("@face", (object?)req.FaceEnrollBannerEnabled ?? DBNull.Value)
                .With("@poll", (object?)poll ?? DBNull.Value)
                .With("@phf", (object?)hFactor ?? DBNull.Value)
                .With("@pvn", (object?)vNudge ?? DBNull.Value)
                .With("@pas", (object?)aspect ?? DBNull.Value)
                .With("@pmw", (object?)minW ?? DBNull.Value)
                .With("@call", (object?)callJson ?? DBNull.Value)
                .With("@features", (object?)featuresJson ?? DBNull.Value)
                .With("@onboarding", (object?)onboardingJson ?? DBNull.Value)
                .With("@notices", (object?)noticesJson ?? DBNull.Value)
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Sửa cấu hình ứng dụng", "AppConfig", "app", "Cập nhật remote config.");
            return Results.Ok(await Read(conn));
        }).RequirePermission(Permissions.SystemSettingsManage);
    }

    private static async Task<AppConfigDto> Read(Npgsql.NpgsqlConnection conn)
    {
        await using var r = await conn.Cmd("""
            SELECT announcement, announcement_level, face_enroll_banner_enabled, foreground_poll_seconds,
                   portrait_height_factor, portrait_vertical_nudge, portrait_aspect, portrait_min_width_factor,
                   call_config::text AS call_config, feature_flags::text AS feature_flags, onboarding::text AS onboarding,
                   notices::text AS notices
            FROM app_config WHERE id = 1
            """).ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return new AppConfigDto("", "info", true, 20, 1.85, 0.15, 0.75, 1.35, ParseCall(null), new FeatureFlagsDto(), new OnboardingDto(), Array.Empty<string>());
        return new AppConfigDto(
            r.Str("announcement"),
            r.Str("announcement_level"),
            r.Bool("face_enroll_banner_enabled"),
            r.Int("foreground_poll_seconds"),
            r.GetDouble(r.GetOrdinal("portrait_height_factor")),
            r.GetDouble(r.GetOrdinal("portrait_vertical_nudge")),
            r.GetDouble(r.GetOrdinal("portrait_aspect")),
            r.GetDouble(r.GetOrdinal("portrait_min_width_factor")),
            ParseCall(r.Str("call_config")),
            ParseFeatures(r.Str("feature_flags")),
            ParseOnboarding(r.Str("onboarding")),
            ParseNotices(r.Str("notices")));
    }

    private static readonly string[] DefaultStun =
        { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" };
    private static readonly JsonSerializerOptions CallJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Đọc khối call_config (jsonb) → DTO, điền mặc định cho trường thiếu / bản ghi trống.</summary>
    private static CallConfigDto ParseCall(string? json)
    {
        CallConfigDto? c = null;
        if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "{}")
            c = JsonSerializer.Deserialize<CallConfigDto>(json, CallJson);
        c ??= new CallConfigDto();
        if (c.StunServers is null || c.StunServers.Length == 0) c = c with { StunServers = DefaultStun };
        return c;
    }

    /// <summary>Đọc cờ tính năng (jsonb) → DTO, điền mặc định (bật) cho trường thiếu / bản ghi trống.</summary>
    private static FeatureFlagsDto ParseFeatures(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "{}")
            return JsonSerializer.Deserialize<FeatureFlagsDto>(json, CallJson) ?? new FeatureFlagsDto();
        return new FeatureFlagsDto();
    }

    /// <summary>Đọc nội dung onboarding / lý do xin quyền (jsonb) → DTO, chuỗi rỗng cho trường thiếu.</summary>
    private static OnboardingDto ParseOnboarding(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "{}")
            return JsonSerializer.Deserialize<OnboardingDto>(json, CallJson) ?? new OnboardingDto();
        return new OnboardingDto();
    }

    /// <summary>Đọc danh sách lời nhắc chạy chữ (jsonb mảng chuỗi) → mảng; bản ghi trống ⇒ mảng rỗng.</summary>
    private static string[] ParseNotices(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") return Array.Empty<string>();
        return CleanNotices(JsonSerializer.Deserialize<string[]>(json, CallJson) ?? Array.Empty<string>());
    }

    /// <summary>Chuẩn hoá lời nhắc: cắt khoảng trắng, bỏ dòng rỗng/trùng, kẹp độ dài (300) và số lượng (20).</summary>
    private static string[] CleanNotices(string[] notices) => notices
        .Select(n => (n ?? "").Trim())
        .Where(n => n.Length > 0)
        .Select(n => n.Length > 300 ? n[..300] : n)
        .Distinct()
        .Take(20)
        .ToArray();

    /// <summary>Kẹp độ dài nội dung onboarding (tránh lạm dụng lưu chuỗi khổng lồ).</summary>
    private static OnboardingDto Clamp(OnboardingDto o) => new(
        Cap(o.CameraReason), Cap(o.LocationReason), Cap(o.NotificationReason), Cap(o.MicrophoneReason), Cap(o.IntroText));
    private static string Cap(string? s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 2000 ? s[..2000] : s);

    /// <summary>Kẹp tham số cuộc gọi vào khoảng an toàn (tránh cấu hình sai làm hỏng/đơ cuộc gọi).</summary>
    private static CallConfigDto Clamp(CallConfigDto c) => c with
    {
        OutgoingTimeoutSeconds = Math.Clamp(c.OutgoingTimeoutSeconds, 10, 120),
        IncomingTimeoutSeconds = Math.Clamp(c.IncomingTimeoutSeconds, 10, 120),
        VideoWidth = Math.Clamp(c.VideoWidth, 160, 1920),
        VideoHeight = Math.Clamp(c.VideoHeight, 120, 1080),
        VideoFps = Math.Clamp(c.VideoFps, 1, 30),
        VideoMaxBitrateKbps = Math.Clamp(c.VideoMaxBitrateKbps, 0, 8000),
        AudioMaxBitrateKbps = Math.Clamp(c.AudioMaxBitrateKbps, 0, 512),
        StunServers = (c.StunServers is null || c.StunServers.Length == 0) ? DefaultStun : c.StunServers,
    };

    /// <summary>Chỉ chấp nhận mức cảnh báo hợp lệ; giá trị lạ/ null → không đổi.</summary>
    private static string? Normalize(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "info" => "info",
        "warning" => "warning",
        "critical" => "critical",
        _ => null,
    };

    public record AppConfigDto(string Announcement, string AnnouncementLevel, bool FaceEnrollBannerEnabled, int ForegroundPollSeconds,
        double PortraitHeightFactor, double PortraitVerticalNudge, double PortraitAspect, double PortraitMinWidthFactor,
        CallConfigDto Call, FeatureFlagsDto Features, OnboardingDto Onboarding, string[] Notices);
    public record AppConfigPatch(string? Announcement, string? AnnouncementLevel, bool? FaceEnrollBannerEnabled, int? ForegroundPollSeconds,
        double? PortraitHeightFactor = null, double? PortraitVerticalNudge = null, double? PortraitAspect = null, double? PortraitMinWidthFactor = null,
        CallConfigDto? Call = null, FeatureFlagsDto? Features = null, OnboardingDto? Onboarding = null, string[]? Notices = null);

    /// <summary>
    /// Công tắc TÍNH NĂNG điều khiển từ xa — admin bật/tắt là app áp dụng ngay, KHÔNG cần build lại APK.
    /// Thêm cờ mới ở đây là đủ (jsonb linh hoạt). Gọi thoại/video vẫn nằm trong CallConfigDto.
    /// </summary>
    public record FeatureFlagsDto(
        bool LocationEnabled = true,
        bool OfflineAttendanceEnabled = true,
        bool BiometricAttendanceEnabled = true,
        bool ChatFileTransferEnabled = true,
        bool CompanyPortalEnabled = true);

    /// <summary>
    /// Nội dung ONBOARDING / lý do xin quyền — sửa câu chữ từ backend, app hiển thị khi xin quyền tương ứng
    /// mà KHÔNG cần phát hành APK. Bỏ trống ⇒ app dùng câu mặc định gắn sẵn.
    /// </summary>
    public record OnboardingDto(
        string CameraReason = "",
        string LocationReason = "",
        string NotificationReason = "",
        string MicrophoneReason = "",
        string IntroText = "");

    /// <summary>
    /// Cấu hình GỌI THOẠI/VIDEO điều khiển từ xa. Đổi ở đây (qua PUT /api/app-config) là app áp dụng
    /// ngay lần đăng nhập / quay lại foreground kế tiếp — KHÔNG cần build APK mới.
    ///   • CallsEnabled / VideoCallEnabled: công tắc bật/tắt gọi thoại / gọi video toàn hệ thống.
    ///   • StunServers: danh sách STUN. ForceRelay=true: ép mọi media đi qua TURN (ổn định + giấu IP).
    ///   • Outgoing/IncomingTimeoutSeconds: thời gian chờ bắt máy.
    ///   • Video{Width,Height,Fps,MaxBitrateKbps}: chất lượng video (hạ xuống khi mạng yếu).
    ///   • AudioMaxBitrateKbps: trần bitrate thoại (0 = không giới hạn).
    /// </summary>
    public record CallConfigDto(
        bool CallsEnabled = true,
        bool VideoCallEnabled = true,
        string[]? StunServers = null,
        bool ForceRelay = false,
        int OutgoingTimeoutSeconds = 30,
        int IncomingTimeoutSeconds = 45,
        int VideoWidth = 1280,
        int VideoHeight = 720,
        int VideoFps = 30,
        int VideoMaxBitrateKbps = 0,
        int AudioMaxBitrateKbps = 0);
}
