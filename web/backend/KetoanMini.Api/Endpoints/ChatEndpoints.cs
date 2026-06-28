using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Trò chuyện (web-only). Dùng lại hệ tài khoản sẵn có: danh bạ và tên hiển thị lấy từ
/// dbo.app_users (full_name), ảnh đại diện từ dbo.web_user_avatars, trạng thái online từ
/// dbo.user_sessions. Dữ liệu chat lưu trong bảng riêng tiền tố web_chat_* để KHÔNG đụng
/// schema dùng chung với app desktop (LAN chat cũ đã bỏ).
/// "Tích xanh": tài khoản Admin luôn có, hoặc được admin cấp thủ công (dbo.web_verified_users).
/// </summary>
public static class ChatEndpoints
{
    // Biểu thức tính "tích xanh" cho một người dùng (cần JOIN bí danh app_users là `au`
    // và LEFT JOIN dbo.web_verified_users là `vu`).
    private const string VerifiedExpr =
        "CAST(CASE WHEN au.role = N'Admin' OR vu.username IS NOT NULL THEN 1 ELSE 0 END AS bit)";

    public static void MapChat(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chat").RequireAuthorization();

        // Danh bạ: mọi tài khoản đang hoạt động (trừ chính mình) để bắt đầu cuộc trò chuyện.
        g.MapGet("/contacts", async (ClaimsPrincipal principal, Database db, string? search) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();

            var where = "WHERE au.is_deleted = 0 AND au.is_active = 1 AND au.approval_status = N'Approved' AND au.username <> @me";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (au.username LIKE @s OR au.full_name LIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT au.username, au.full_name, au.role,
                          {VerifiedExpr} AS verified,
                          av.image_data_url AS avatar,
                          CAST(ISNULL(pres.is_online, 0) AS bit) AS is_online
                   FROM dbo.app_users au
                   LEFT JOIN dbo.web_verified_users vu ON vu.username = au.username
                   OUTER APPLY (
                       SELECT TOP 1 wa.image_data_url FROM dbo.web_user_avatars wa WHERE wa.user_id = au.id
                   ) av
                   OUTER APPLY (
                       SELECT MAX(CASE WHEN us.is_active = 1 AND us.last_seen >= DATEADD(SECOND, -90, SYSDATETIME()) THEN 1 ELSE 0 END) AS is_online
                       FROM dbo.user_sessions us WHERE us.username = au.username
                   ) pres
                   {where}
                   ORDER BY pres.is_online DESC, au.full_name, au.username")
                .With("@me", me);
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search}%");

            var list = new List<ChatContactDto>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var username = r.Str("username");
                var name = r.Str("full_name");
                list.Add(new ChatContactDto(
                    username, string.IsNullOrWhiteSpace(name) ? username : name,
                    NullIfEmpty(r.Str("avatar")), r.Bool("is_online"), r.Bool("verified"), r.Str("role")));
            }
            return Results.Ok(list);
        });

        // Danh sách cuộc trò chuyện của tôi (kèm tin nhắn cuối, số chưa đọc, thông tin người kia).
        g.MapGet("/conversations", async (ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            var list = await ReadConversations(conn, me, null);
            return Results.Ok(list);
        });

        // Lấy (hoặc tạo) cuộc trò chuyện 1-1 với một người dùng → trả về id để mở khung chat.
        g.MapPost("/direct/{username}", async (string username, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var other = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(other) || string.Equals(other, me, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Người nhận không hợp lệ." });

            await using var conn = await db.OpenAsync();
            var exists = await conn.Cmd(
                "SELECT COUNT(*) FROM dbo.app_users WHERE username = @u AND is_deleted = 0 AND is_active = 1")
                .With("@u", other).ExecuteScalarAsync();
            if (Convert.ToInt32(exists) == 0)
                return Results.NotFound(new { message = "Không tìm thấy người dùng." });

            var id = await GetOrCreateDirect(conn, me, other);
            return Results.Ok(new { id });
        });

        // Tin nhắn của một cuộc trò chuyện (phải là thành viên). Đồng thời đánh dấu đã đọc.
        g.MapGet("/conversations/{id:guid}/messages", async (Guid id, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();

            var list = new List<ChatMessageDto>();
            await using var r = await conn.Cmd(
                @"SELECT m.id, m.sender_username, au.full_name AS sender_name, m.body, m.created_at,
                         m.edited_at, m.is_removed, m.is_forwarded
                  FROM dbo.web_chat_messages m
                  LEFT JOIN dbo.app_users au ON au.username = m.sender_username AND au.is_deleted = 0
                  WHERE m.conversation_id = @cid
                  ORDER BY m.created_at ASC, m.id ASC")
                .With("@cid", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var sender = r.Str("sender_username");
                var name = r.Str("sender_name");
                var removed = r.Bool("is_removed");
                list.Add(new ChatMessageDto(
                    r.Long("id"), sender, string.IsNullOrWhiteSpace(name) ? sender : name,
                    string.Equals(sender, me, StringComparison.OrdinalIgnoreCase),
                    removed ? "" : r.Str("body"), r.Dt("created_at"),
                    r.DtNull("edited_at"), removed, r.Bool("is_forwarded")));
            }
            await r.CloseAsync();
            await MarkRead(conn, id, me);
            return Results.Ok(list);
        });

        // Gửi tin nhắn.
        g.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var body = (req?.Body ?? "").Trim();
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { message = "Tin nhắn trống." });
            if (body.Length > 4000) body = body[..4000];

            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();

            var forwarded = req?.Forwarded ?? false;
            var newId = Convert.ToInt64(await conn.Cmd(
                @"INSERT INTO dbo.web_chat_messages (conversation_id, sender_username, body, is_forwarded, created_at)
                  VALUES (@cid, @me, @body, @fwd, SYSUTCDATETIME());
                  SELECT CAST(SCOPE_IDENTITY() AS BIGINT);")
                .With("@cid", id).With("@me", me).With("@body", body).With("@fwd", forwarded).ExecuteScalarAsync());

            await MarkRead(conn, id, me);

            return Results.Ok(new ChatMessageDto(newId, me, me, true, body, DateTime.UtcNow, null, false, forwarded));
        });

        // Chỉnh sửa tin nhắn (chỉ người gửi, chưa bị gỡ).
        g.MapPut("/conversations/{id:guid}/messages/{msgId:long}", async (Guid id, long msgId, EditMessageRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var body = (req?.Body ?? "").Trim();
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { message = "Tin nhắn trống." });
            if (body.Length > 4000) body = body[..4000];

            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE dbo.web_chat_messages SET body = @body, edited_at = SYSUTCDATETIME()
                  WHERE id = @mid AND conversation_id = @cid AND sender_username = @me AND is_removed = 0")
                .With("@body", body).With("@mid", msgId).With("@cid", id).With("@me", me).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không sửa được tin nhắn này." });
            return Results.NoContent();
        });

        // Gỡ tin nhắn (chỉ người gửi) — giữ lại dòng làm placeholder "Tin nhắn đã được gỡ",
        // nhưng XÓA RỖNG nội dung (body) khỏi DB để không lưu văn bản → tránh phình DB.
        g.MapDelete("/conversations/{id:guid}/messages/{msgId:long}", async (Guid id, long msgId, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE dbo.web_chat_messages SET is_removed = 1, body = N'', edited_at = SYSUTCDATETIME()
                  WHERE id = @mid AND conversation_id = @cid AND sender_username = @me AND is_removed = 0")
                .With("@mid", msgId).With("@cid", id).With("@me", me).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không gỡ được tin nhắn này." });
            return Results.NoContent();
        });

        // Đánh dấu đã đọc (khi mở cuộc trò chuyện).
        g.MapPost("/conversations/{id:guid}/read", async (Guid id, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();
            await MarkRead(conn, id, me);
            return Results.NoContent();
        });
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task<bool> IsMember(SqlConnection conn, Guid conversationId, string username)
    {
        var n = await conn.Cmd(
            "SELECT COUNT(*) FROM dbo.web_chat_members WHERE conversation_id = @cid AND username = @u")
            .With("@cid", conversationId).With("@u", username).ExecuteScalarAsync();
        return Convert.ToInt32(n) > 0;
    }

    private static async Task MarkRead(SqlConnection conn, Guid conversationId, string username)
    {
        await conn.Cmd(
            "UPDATE dbo.web_chat_members SET last_read_at = SYSUTCDATETIME() WHERE conversation_id = @cid AND username = @u")
            .With("@cid", conversationId).With("@u", username).ExecuteNonQueryAsync();
    }

    private static async Task<Guid> GetOrCreateDirect(SqlConnection conn, string me, string other)
    {
        var found = await conn.Cmd(
            @"SELECT TOP 1 c.id
              FROM dbo.web_chat_conversations c
              WHERE c.is_group = 0
                AND EXISTS (SELECT 1 FROM dbo.web_chat_members m WHERE m.conversation_id = c.id AND m.username = @me)
                AND EXISTS (SELECT 1 FROM dbo.web_chat_members m WHERE m.conversation_id = c.id AND m.username = @other)
                AND (SELECT COUNT(*) FROM dbo.web_chat_members m WHERE m.conversation_id = c.id) = 2")
            .With("@me", me).With("@other", other).ExecuteScalarAsync();
        if (found is Guid g) return g;

        var id = Guid.NewGuid();
        await conn.Cmd(
            @"INSERT INTO dbo.web_chat_conversations (id, is_group, title, created_by, created_at)
              VALUES (@id, 0, N'', @me, SYSUTCDATETIME());
              INSERT INTO dbo.web_chat_members (conversation_id, username, joined_at)
              VALUES (@id, @me, SYSUTCDATETIME()), (@id, @other, SYSUTCDATETIME());")
            .With("@id", id).With("@me", me).With("@other", other).ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<List<ChatConversationDto>> ReadConversations(SqlConnection conn, string me, Guid? onlyId)
    {
        var list = new List<ChatConversationDto>();
        var cmd = conn.Cmd(
            $@"SELECT c.id, c.is_group, c.title,
                      o.username AS other_username,
                      au.full_name AS other_full_name,
                      {VerifiedExpr} AS other_verified,
                      oav.image_data_url AS other_avatar,
                      CAST(ISNULL(pres.is_online, 0) AS bit) AS other_online,
                      pres.last_seen_utc AS other_last_seen,
                      lm.body AS last_body, lm.sender_username AS last_sender, lm.created_at AS last_at,
                      CAST(ISNULL(lm.is_removed, 0) AS bit) AS last_removed,
                      ISNULL(unr.cnt, 0) AS unread
               FROM dbo.web_chat_members me
               JOIN dbo.web_chat_conversations c ON c.id = me.conversation_id
               OUTER APPLY (
                   SELECT TOP 1 m2.username FROM dbo.web_chat_members m2
                   WHERE m2.conversation_id = c.id AND m2.username <> @me ORDER BY m2.joined_at
               ) o
               LEFT JOIN dbo.app_users au ON au.username = o.username AND au.is_deleted = 0
               LEFT JOIN dbo.web_verified_users vu ON vu.username = o.username
               OUTER APPLY (
                   SELECT TOP 1 wa.image_data_url FROM dbo.web_user_avatars wa WHERE wa.user_id = au.id
               ) oav
               OUTER APPLY (
                   SELECT MAX(CASE WHEN us.is_active = 1 AND us.last_seen >= DATEADD(SECOND, -90, SYSDATETIME()) THEN 1 ELSE 0 END) AS is_online,
                          -- last_seen lưu theo giờ local của server → quy đổi sang UTC để client tính đúng độ trễ.
                          DATEADD(SECOND, DATEDIFF(SECOND, SYSDATETIME(), SYSUTCDATETIME()), MAX(us.last_seen)) AS last_seen_utc
                   FROM dbo.user_sessions us WHERE us.username = o.username
               ) pres
               OUTER APPLY (
                   SELECT TOP 1 mm.body, mm.sender_username, mm.created_at, mm.is_removed
                   FROM dbo.web_chat_messages mm WHERE mm.conversation_id = c.id
                   ORDER BY mm.created_at DESC, mm.id DESC
               ) lm
               OUTER APPLY (
                   SELECT COUNT(*) AS cnt FROM dbo.web_chat_messages mm
                   WHERE mm.conversation_id = c.id AND mm.sender_username <> @me AND mm.is_removed = 0
                     AND (me.last_read_at IS NULL OR mm.created_at > me.last_read_at)
               ) unr
               WHERE me.username = @me {(onlyId is null ? "" : "AND c.id = @only")}
               ORDER BY lm.created_at DESC")
            .With("@me", me);
        if (onlyId is not null) cmd.With("@only", onlyId.Value);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var isGroup = r.Bool("is_group");
            var otherUsername = r.Str("other_username");
            var otherName = r.Str("other_full_name");
            var title = r.Str("title");

            var displayName = isGroup
                ? (string.IsNullOrWhiteSpace(title) ? "Nhóm trò chuyện" : title)
                : (string.IsNullOrWhiteSpace(otherName) ? otherUsername : otherName);

            var lastBody = r.Str("last_body");
            var lastSender = r.Str("last_sender");
            var lastRemoved = r.Bool("last_removed");
            var mineLast = string.Equals(lastSender, me, StringComparison.OrdinalIgnoreCase);
            string preview = "";
            if (lastRemoved)
                preview = (mineLast ? "Bạn: " : "") + "Tin nhắn đã được gỡ";
            else if (!string.IsNullOrEmpty(lastBody))
                preview = mineLast ? $"Bạn: {lastBody}" : lastBody;

            list.Add(new ChatConversationDto(
                r.Guid("id"), isGroup, displayName,
                isGroup ? null : NullIfEmpty(otherUsername),
                isGroup ? null : NullIfEmpty(r.Str("other_avatar")),
                !isGroup && r.Bool("other_online"),
                !isGroup && r.Bool("other_verified"),
                preview, r.DtNull("last_at"), r.Int("unread"),
                isGroup ? null : r.DtNull("other_last_seen")));
        }
        return list;
    }

    /// <summary>Tạo các bảng chat web-only + bảng tích xanh nếu chưa có (best-effort lúc khởi động).</summary>
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(@"
            IF OBJECT_ID(N'dbo.web_chat_conversations', N'U') IS NULL
            CREATE TABLE dbo.web_chat_conversations (
                id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_web_chat_conversations PRIMARY KEY,
                is_group BIT NOT NULL CONSTRAINT DF_web_chat_conv_group DEFAULT 0,
                title NVARCHAR(200) NOT NULL CONSTRAINT DF_web_chat_conv_title DEFAULT N'',
                created_by NVARCHAR(128) NOT NULL CONSTRAINT DF_web_chat_conv_by DEFAULT N'',
                created_at DATETIME2 NOT NULL CONSTRAINT DF_web_chat_conv_at DEFAULT SYSUTCDATETIME()
            );

            IF OBJECT_ID(N'dbo.web_chat_members', N'U') IS NULL
            CREATE TABLE dbo.web_chat_members (
                conversation_id UNIQUEIDENTIFIER NOT NULL,
                username NVARCHAR(128) NOT NULL,
                last_read_at DATETIME2 NULL,
                joined_at DATETIME2 NOT NULL CONSTRAINT DF_web_chat_mem_at DEFAULT SYSUTCDATETIME(),
                CONSTRAINT PK_web_chat_members PRIMARY KEY (conversation_id, username)
            );

            IF OBJECT_ID(N'dbo.web_chat_messages', N'U') IS NULL
            CREATE TABLE dbo.web_chat_messages (
                id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_web_chat_messages PRIMARY KEY,
                conversation_id UNIQUEIDENTIFIER NOT NULL,
                sender_username NVARCHAR(128) NOT NULL,
                body NVARCHAR(MAX) NOT NULL,
                edited_at DATETIME2 NULL,
                is_removed BIT NOT NULL CONSTRAINT DF_web_chat_msg_removed DEFAULT 0,
                is_forwarded BIT NOT NULL CONSTRAINT DF_web_chat_msg_fwd DEFAULT 0,
                created_at DATETIME2 NOT NULL CONSTRAINT DF_web_chat_msg_at DEFAULT SYSUTCDATETIME()
            );
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_web_chat_messages_conv' AND object_id = OBJECT_ID(N'dbo.web_chat_messages'))
                CREATE INDEX IX_web_chat_messages_conv ON dbo.web_chat_messages (conversation_id, created_at);

            -- Bổ sung cột cho bảng đã tồn tại (chỉnh sửa / gỡ / chuyển tiếp).
            IF COL_LENGTH(N'dbo.web_chat_messages', 'edited_at') IS NULL
                ALTER TABLE dbo.web_chat_messages ADD edited_at DATETIME2 NULL;
            IF COL_LENGTH(N'dbo.web_chat_messages', 'is_removed') IS NULL
                ALTER TABLE dbo.web_chat_messages ADD is_removed BIT NOT NULL CONSTRAINT DF_web_chat_msg_removed DEFAULT 0;
            IF COL_LENGTH(N'dbo.web_chat_messages', 'is_forwarded') IS NULL
                ALTER TABLE dbo.web_chat_messages ADD is_forwarded BIT NOT NULL CONSTRAINT DF_web_chat_msg_fwd DEFAULT 0;

            IF OBJECT_ID(N'dbo.web_verified_users', N'U') IS NULL
            CREATE TABLE dbo.web_verified_users (
                username NVARCHAR(128) NOT NULL CONSTRAINT PK_web_verified_users PRIMARY KEY,
                granted_by NVARCHAR(128) NOT NULL CONSTRAINT DF_web_verified_by DEFAULT N'',
                granted_at DATETIME2 NOT NULL CONSTRAINT DF_web_verified_at DEFAULT SYSUTCDATETIME()
            );")
            .ExecuteNonQueryAsync(ct);
    }
}
