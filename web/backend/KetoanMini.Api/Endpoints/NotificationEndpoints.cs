using System.Security.Claims;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Đăng ký / hủy token thiết bị (FCM) cho thông báo đẩy tức thì. Mỗi user có thể có nhiều thiết bị;
/// token là PRIMARY KEY nên khi thiết bị đổi chủ (đăng nhập user khác) sẽ tự gán lại đúng username.
/// </summary>
public static class NotificationEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS hr_device_tokens (
                token varchar(300) PRIMARY KEY,
                username varchar(128) NOT NULL,
                platform varchar(20) NOT NULL DEFAULT 'android',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_hr_device_tokens_user ON hr_device_tokens (username);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapNotifications(this WebApplication app)
    {
        var g = app.MapGroup("/api/notifications").RequireAuthorization();

        g.MapPost("/register-token", async (RegisterTokenReq req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token)) return Results.BadRequest(new { message = "Thiếu token thiết bị." });
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                INSERT INTO hr_device_tokens (token, username, platform, updated_at)
                VALUES (@t, @u, @p, CURRENT_TIMESTAMP)
                ON CONFLICT (token) DO UPDATE SET username=@u, platform=@p, updated_at=CURRENT_TIMESTAMP
                """)
                .With("@t", req.Token!.Trim())
                .With("@u", u.Username())
                .With("@p", string.IsNullOrWhiteSpace(req.Platform) ? "android" : req.Platform!.Trim())
                .ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        g.MapPost("/unregister-token", async (TokenReq req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token)) return Results.NoContent();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM hr_device_tokens WHERE token=@t AND lower(username)=lower(@u)")
                .With("@t", req.Token!.Trim()).With("@u", u.Username()).ExecuteNonQueryAsync();
            return Results.NoContent();
        });
    }

    public record RegisterTokenReq(string? Token, string? Platform);
    public record TokenReq(string? Token);
}
