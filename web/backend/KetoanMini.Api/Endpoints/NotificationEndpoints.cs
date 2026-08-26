using System.Security.Claims;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Hai việc quanh THÔNG BÁO:
///
///  1. Đăng ký / hủy token thiết bị (FCM) cho thông báo đẩy tức thì. Mỗi user có thể có nhiều thiết
///     bị; token là PRIMARY KEY nên khi thiết bị đổi chủ (đăng nhập user khác) sẽ tự gán lại đúng
///     username.
///
///  2. HỘP THƯ THÔNG BÁO CỦA WEB (<c>web_notifications</c>) — cái chuông trên header đọc từ đây.
///     Trình duyệt không có FCM, đóng tab là mất gói tin, nên mỗi lần đẩy push
///     <see cref="Services.PushService"/> ghi kèm một dòng vào bảng này. Nhờ vậy người dùng mở web
///     sau vẫn thấy đủ việc đã xảy ra, biết cái nào chưa đọc, và bấm vào là nhảy tới đúng màn hình.
///
/// Dòng thông báo là BẢN SAO để đọc, không phải sổ sách: xóa đi không mất dữ liệu nghiệp vụ nào, và
/// bản ghi cũ hơn <see cref="RetentionDays"/> ngày bị dọn lúc khởi động cho bảng khỏi phình.
/// </summary>
public static class NotificationEndpoints
{
    /// <summary>Giữ lại bao nhiêu ngày thông báo cũ. Quá mốc này thì không ai còn tra lại nữa.</summary>
    public const int RetentionDays = 90;

    /// <summary>Số dòng tối đa trả về một lần cho chuông — đủ cuộn, không đủ để làm nặng trang.</summary>
    private const int MaxFeed = 50;

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

            CREATE TABLE IF NOT EXISTS web_notifications (
                id bigserial PRIMARY KEY,
                username varchar(128) NOT NULL,
                title varchar(200) NOT NULL,
                body text NOT NULL DEFAULT '',
                category varchar(40) NOT NULL DEFAULT 'general',
                link varchar(300) NOT NULL DEFAULT '',
                app_target varchar(60) NOT NULL DEFAULT '',
                notif_id varchar(200) NOT NULL DEFAULT '',
                actor varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                read_at timestamptz NULL
            );
            -- Đọc luôn là "thông báo của TÔI, mới nhất trước" nên index đúng theo hình đó.
            CREATE INDEX IF NOT EXISTS ix_web_notifications_user
                ON web_notifications (lower(username), created_at DESC);
            -- Cùng một sự kiện (notif_id) chỉ được nằm MỘT dòng trong hộp thư mỗi người: worker đẩy
            -- lại hay người dùng bấm hai lần thì cũng không đẻ ra thông báo trùng.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_web_notifications_event
                ON web_notifications (lower(username), notif_id) WHERE notif_id <> '';
            -- Cột thêm sau: bảng có thể đã tồn tại từ bản trước nên phải ALTER, CREATE TABLE ở trên
            -- chỉ chạy khi bảng còn chưa có.
            ALTER TABLE web_notifications ADD COLUMN IF NOT EXISTS app_target varchar(60) NOT NULL DEFAULT '';
            """).ExecuteNonQueryAsync(ct);

        await conn.Cmd($"DELETE FROM web_notifications WHERE created_at < CURRENT_TIMESTAMP - INTERVAL '{RetentionDays} days'")
            .ExecuteNonQueryAsync(ct);
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

        // ---- Hộp thư thông báo của web ----
        // Không có tham số "xem hộ người khác": mọi câu lệnh dưới đây đều khoá cứng theo username của
        // chính phiên đang gọi, nên không có đường nào đọc hay xoá thông báo của người khác.

        g.MapGet("/", async (int? limit, ClaimsPrincipal u, Database db) =>
        {
            var take = Math.Clamp(limit ?? 30, 1, MaxFeed);
            var me = u.Username();
            await using var conn = await db.OpenAsync();

            var unread = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM web_notifications WHERE lower(username)=lower(@u) AND read_at IS NULL")
                .With("@u", me).ExecuteScalarAsync());

            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, title, body, category, link, app_target, notif_id, created_at, read_at
                FROM web_notifications
                WHERE lower(username) = lower(@u)
                ORDER BY created_at DESC, id DESC
                LIMIT @n
                """).With("@u", me).With("@n", take).ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    id = r.Long("id"),
                    title = r.Str("title"),
                    body = r.Str("body"),
                    category = r.Str("category"),
                    // link = màn hình WEB, appTarget = màn hình APP. Hai máy khách khác nhau nên
                    // mỗi bên lấy đúng phần của mình thay vì phải tự đoán từ phần của bên kia.
                    link = r.Str("link"),
                    appTarget = r.Str("app_target"),
                    // Chữ ký sự kiện: APP dùng nó để không hiện trùng với thông báo đã nhận qua FCM.
                    notifId = r.Str("notif_id"),
                    createdAt = r.Dt("created_at"),
                    read = r.DtNull("read_at") is not null,
                });

            return Results.Ok(new { unread, items });
        });

        g.MapPost("/{id:long}/read", async (long id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                UPDATE web_notifications SET read_at = CURRENT_TIMESTAMP
                WHERE id=@id AND lower(username)=lower(@u) AND read_at IS NULL
                """).With("@id", id).With("@u", u.Username()).ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        g.MapPost("/read-all", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                UPDATE web_notifications SET read_at = CURRENT_TIMESTAMP
                WHERE lower(username)=lower(@u) AND read_at IS NULL
                """).With("@u", u.Username()).ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        // Dọn hộp thư: chỉ xoá phần ĐÃ ĐỌC, để thông báo chưa xem không bị mất vì một cú bấm nhầm.
        // Đặt TRƯỚC route "/{id:long}" để "read" không bị nuốt bởi ràng buộc số.
        g.MapDelete("/read", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM web_notifications WHERE lower(username)=lower(@u) AND read_at IS NOT NULL")
                .With("@u", u.Username()).ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        g.MapDelete("/{id:long}", async (long id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM web_notifications WHERE id=@id AND lower(username)=lower(@u)")
                .With("@id", id).With("@u", u.Username()).ExecuteNonQueryAsync();
            return Results.NoContent();
        });
    }

    public record RegisterTokenReq(string? Token, string? Platform);
    public record TokenReq(string? Token);
}
