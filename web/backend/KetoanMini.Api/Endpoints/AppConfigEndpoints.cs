using System.Security.Claims;
using KetoanMini.Api.Data;

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
            await conn.Cmd("""
                UPDATE app_config SET
                    announcement = COALESCE(@ann, announcement),
                    announcement_level = COALESCE(@lvl, announcement_level),
                    face_enroll_banner_enabled = COALESCE(@face, face_enroll_banner_enabled),
                    foreground_poll_seconds = COALESCE(@poll, foreground_poll_seconds),
                    updated_at = CURRENT_TIMESTAMP,
                    updated_by = @by
                WHERE id = 1
                """)
                .With("@ann", (object?)req.Announcement ?? DBNull.Value)
                .With("@lvl", (object?)level ?? DBNull.Value)
                .With("@face", (object?)req.FaceEnrollBannerEnabled ?? DBNull.Value)
                .With("@poll", (object?)poll ?? DBNull.Value)
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Sửa cấu hình ứng dụng", "AppConfig", "app", "Cập nhật remote config.");
            return Results.Ok(await Read(conn));
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    private static async Task<AppConfigDto> Read(Npgsql.NpgsqlConnection conn)
    {
        await using var r = await conn.Cmd("""
            SELECT announcement, announcement_level, face_enroll_banner_enabled, foreground_poll_seconds
            FROM app_config WHERE id = 1
            """).ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return new AppConfigDto("", "info", true, 20);
        return new AppConfigDto(
            r.Str("announcement"),
            r.Str("announcement_level"),
            r.Bool("face_enroll_banner_enabled"),
            r.Int("foreground_poll_seconds"));
    }

    /// <summary>Chỉ chấp nhận mức cảnh báo hợp lệ; giá trị lạ/ null → không đổi.</summary>
    private static string? Normalize(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "info" => "info",
        "warning" => "warning",
        "critical" => "critical",
        _ => null,
    };

    public record AppConfigDto(string Announcement, string AnnouncementLevel, bool FaceEnrollBannerEnabled, int ForegroundPollSeconds);
    public record AppConfigPatch(string? Announcement, string? AnnouncementLevel, bool? FaceEnrollBannerEnabled, int? ForegroundPollSeconds);
}
