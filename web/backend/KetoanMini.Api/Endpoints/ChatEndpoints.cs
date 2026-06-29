using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Trò chuyện (web-only). Dùng lại hệ tài khoản sẵn có: danh bạ và tên hiển thị lấy từ
/// app_users (full_name), ảnh đại diện từ web_user_avatars, trạng thái online từ
/// user_sessions. Dữ liệu chat lưu trong bảng riêng tiền tố web_chat_* để KHÔNG đụng
/// schema dùng chung với app desktop (LAN chat cũ đã bỏ).
/// "Tích xanh": tài khoản Admin luôn có, hoặc được admin cấp thủ công (web_verified_users).
/// </summary>
public static class ChatEndpoints
{
    // Biểu thức tính "tích xanh" cho một người dùng (cần JOIN bí danh app_users là `au`
    // và LEFT JOIN web_verified_users là `vu`).
    private const string VerifiedExpr =
        "(au.role = 'Admin' OR vu.username IS NOT NULL)";

    public static void MapChat(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chat").RequireAuthorization();

        // Danh bạ: mọi tài khoản đang hoạt động (trừ chính mình) để bắt đầu cuộc trò chuyện.
        g.MapGet("/contacts", async (ClaimsPrincipal principal, Database db, string? search) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();

            var where = "WHERE au.is_deleted = FALSE AND au.is_active = TRUE AND au.approval_status = 'Approved' AND au.username <> @me";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (au.username ILIKE @s OR au.full_name ILIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT au.username, au.full_name, au.role,
                          {VerifiedExpr} AS verified,
                          av.image_data_url AS avatar,
                          COALESCE(pres.is_online, FALSE) AS is_online
                   FROM app_users au
                   LEFT JOIN web_verified_users vu ON vu.username = au.username
                   LEFT JOIN LATERAL (
                       SELECT wa.image_data_url FROM web_user_avatars wa WHERE wa.user_id = au.id LIMIT 1
                   ) av ON TRUE
                   LEFT JOIN LATERAL (
                       SELECT BOOL_OR(us.is_active = TRUE AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds') AS is_online
                       FROM user_sessions us WHERE us.username = au.username
                   ) pres ON TRUE
                   {where}
                   -- PostgreSQL xếp NULL trước khi DESC → người không có phiên (is_online NULL) sẽ
                   -- nổi lên trên người đang online. COALESCE về FALSE để online luôn đứng đầu.
                   ORDER BY COALESCE(pres.is_online, FALSE) DESC, au.full_name, au.username")
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
                "SELECT COUNT(*) FROM app_users WHERE username = @u AND is_deleted = FALSE AND is_active = TRUE")
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
                  FROM web_chat_messages m
                  LEFT JOIN app_users au ON au.username = m.sender_username AND au.is_deleted = FALSE
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
            await AttachReactions(conn, id, me, list);
            await MarkRead(conn, id, me);
            return Results.Ok(list);
        });

        // Gửi tin nhắn.
        g.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
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
                @"INSERT INTO web_chat_messages (conversation_id, sender_username, body, is_forwarded, created_at)
                  VALUES (@cid, @me, @body, @fwd, CURRENT_TIMESTAMP)
                  RETURNING id;")
                .With("@cid", id).With("@me", me).With("@body", body).With("@fwd", forwarded).ExecuteScalarAsync());

            await MarkRead(conn, id, me);
            await NotifyChat(hub, conn, id);

            return Results.Ok(new ChatMessageDto(newId, me, me, true, body, DateTime.UtcNow, null, false, forwarded));
        });

        // Chỉnh sửa tin nhắn (chỉ người gửi, chưa bị gỡ).
        g.MapPut("/conversations/{id:guid}/messages/{msgId:long}", async (Guid id, long msgId, EditMessageRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            var body = (req?.Body ?? "").Trim();
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { message = "Tin nhắn trống." });
            if (body.Length > 4000) body = body[..4000];

            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE web_chat_messages SET body = @body, edited_at = CURRENT_TIMESTAMP
                  WHERE id = @mid AND conversation_id = @cid AND sender_username = @me AND is_removed = FALSE")
                .With("@body", body).With("@mid", msgId).With("@cid", id).With("@me", me).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không sửa được tin nhắn này." });
            await NotifyChat(hub, conn, id);
            return Results.NoContent();
        });

        // Gỡ tin nhắn (chỉ người gửi) — giữ lại dòng làm placeholder "Tin nhắn đã được gỡ",
        // nhưng XÓA RỖNG nội dung (body) khỏi DB để không lưu văn bản → tránh phình DB.
        g.MapDelete("/conversations/{id:guid}/messages/{msgId:long}", async (Guid id, long msgId, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd(
                @"UPDATE web_chat_messages SET is_removed = TRUE, body = '', edited_at = CURRENT_TIMESTAMP
                  WHERE id = @mid AND conversation_id = @cid AND sender_username = @me AND is_removed = FALSE")
                .With("@mid", msgId).With("@cid", id).With("@me", me).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không gỡ được tin nhắn này." });
            await NotifyChat(hub, conn, id);
            return Results.NoContent();
        });

        // Thả / đổi / bỏ biểu cảm (cảm xúc) cho một tin nhắn. Mỗi người chỉ giữ MỘT biểu cảm
        // trên một tin: bấm lại đúng biểu cảm đang chọn → bỏ; bấm biểu cảm khác → đổi sang biểu cảm mới.
        g.MapPost("/conversations/{id:guid}/messages/{msgId:long}/react", async (Guid id, long msgId, ReactRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            var emoji = (req?.Emoji ?? "").Trim();
            if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 16)
                return Results.BadRequest(new { message = "Biểu cảm không hợp lệ." });

            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();

            // Tin nhắn phải thuộc cuộc trò chuyện này và chưa bị gỡ.
            var ok = await conn.Cmd(
                "SELECT COUNT(*) FROM web_chat_messages WHERE id = @mid AND conversation_id = @cid AND is_removed = FALSE")
                .With("@mid", msgId).With("@cid", id).ExecuteScalarAsync();
            if (Convert.ToInt32(ok) == 0) return Results.NotFound(new { message = "Không tìm thấy tin nhắn." });

            var existing = await conn.Cmd(
                "SELECT emoji FROM web_chat_reactions WHERE message_id = @mid AND username = @me")
                .With("@mid", msgId).With("@me", me).ExecuteScalarAsync() as string;
            if (string.Equals(existing, emoji, StringComparison.Ordinal))
            {
                await conn.Cmd("DELETE FROM web_chat_reactions WHERE message_id = @mid AND username = @me")
                    .With("@mid", msgId).With("@me", me).ExecuteNonQueryAsync();
            }
            else
            {
                await conn.Cmd(
                    @"INSERT INTO web_chat_reactions (message_id, username, emoji, created_at)
                      VALUES (@mid, @me, @emoji, CURRENT_TIMESTAMP)
                      ON CONFLICT (message_id, username)
                      DO UPDATE SET emoji = EXCLUDED.emoji, created_at = CURRENT_TIMESTAMP")
                    .With("@mid", msgId).With("@me", me).With("@emoji", emoji).ExecuteNonQueryAsync();
            }

            await NotifyChat(hub, conn, id);
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

        // Dung lượng DB của mục Trò chuyện (chỉ admin) — phục vụ trang Hệ thống → tab Cơ sở dữ liệu.
        g.MapGet("/db-usage", async (ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var usage = await ReadChatDbUsage(conn);
            return Results.Ok(usage);
        });
    }

    private static readonly (string Table, string Label)[] ChatTables =
    [
        ("web_chat_messages", "Tin nhắn"),
        ("web_chat_conversations", "Cuộc trò chuyện"),
        ("web_chat_members", "Thành viên"),
        ("web_chat_reactions", "Biểu cảm"),
        ("web_verified_users", "Tài khoản tích xanh"),
    ];

    private static async Task<ChatDbUsageDto> ReadChatDbUsage(NpgsqlConnection conn)
    {
        var sizes = new Dictionary<string, (long rows, long dataKb, long indexKb)>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await conn.Cmd(
            @"WITH table_rows AS (
                  SELECT 'web_chat_messages'::text AS table_name, COUNT(*)::bigint AS row_count FROM web_chat_messages
                  UNION ALL SELECT 'web_chat_conversations', COUNT(*)::bigint FROM web_chat_conversations
                  UNION ALL SELECT 'web_chat_members', COUNT(*)::bigint FROM web_chat_members
                  UNION ALL SELECT 'web_chat_reactions', COUNT(*)::bigint FROM web_chat_reactions
                  UNION ALL SELECT 'web_verified_users', COUNT(*)::bigint FROM web_verified_users
              )
              SELECT table_name,
                     row_count,
                     -- pg_table_size = heap + TOAST + FSM/VM. Nội dung 'body' (text dài) nằm trong
                     -- TOAST nên PHẢI dùng pg_table_size; pg_relation_size sẽ tính thiếu phần này.
                     (pg_table_size(format('%I', table_name)::regclass) / 1024)::bigint AS data_kb,
                     (pg_indexes_size(format('%I', table_name)::regclass) / 1024)::bigint AS index_kb
              FROM table_rows").ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                sizes[r.Str("table_name")] = (r.Long("row_count"), r.Long("data_kb"), r.Long("index_kb"));
        }

        var tables = new List<ChatTableUsageDto>();
        long totalData = 0, totalIndex = 0;
        foreach (var (table, label) in ChatTables)
        {
            var s = sizes.TryGetValue(table, out var v) ? v : (0L, 0L, 0L);
            totalData += s.Item2;
            totalIndex += s.Item3;
            tables.Add(new ChatTableUsageDto(table, label, s.Item1, s.Item2, s.Item3, s.Item2 + s.Item3));
        }

        // Tổng dung lượng cả database (để hiển thị tỉ lệ phần chat chiếm).
        long dbTotalKb = 0;
        try
        {
            dbTotalKb = Convert.ToInt64(await conn.Cmd(
                "SELECT (pg_database_size(current_database()) / 1024)::bigint")
                .ExecuteScalarAsync() ?? 0L);
        }
        catch { /* thiếu quyền VIEW DATABASE STATE → bỏ qua, để 0 */ }

        long Rows(string t) => sizes.TryGetValue(t, out var v) ? v.rows : 0;
        return new ChatDbUsageDto(
            totalData + totalIndex, totalData, totalIndex,
            Rows("web_chat_messages"), Rows("web_chat_conversations"), Rows("web_chat_members"),
            dbTotalKb, tables);
    }

    /// <summary>Phát tín hiệu "chat" CHỈ tới các thành viên của cuộc trò chuyện (không broadcast cả 100 máy).</summary>
    private static async Task NotifyChat(IHubContext<ChangesHub> hub, NpgsqlConnection conn, Guid conversationId)
    {
        var members = new List<string>();
        await using (var r = await conn.Cmd(
            "SELECT username FROM web_chat_members WHERE conversation_id = @cid")
            .With("@cid", conversationId).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) members.Add(r.GetString(0));
        }
        if (members.Count == 0) return;
        await hub.Clients.Users(members).SendAsync("changed", "chat", conversationId.ToString());
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task<bool> IsMember(NpgsqlConnection conn, Guid conversationId, string username)
    {
        var n = await conn.Cmd(
            "SELECT COUNT(*) FROM web_chat_members WHERE conversation_id = @cid AND username = @u")
            .With("@cid", conversationId).With("@u", username).ExecuteScalarAsync();
        return Convert.ToInt32(n) > 0;
    }

    /// <summary>Gắn danh sách biểu cảm (gộp theo emoji) cho từng tin nhắn đã đọc của cuộc trò chuyện.</summary>
    private static async Task AttachReactions(NpgsqlConnection conn, Guid conversationId, string me, List<ChatMessageDto> messages)
    {
        if (messages.Count == 0) return;

        var byMessage = new Dictionary<long, List<ChatReactionDto>>();
        await using (var r = await conn.Cmd(
            @"SELECT rx.message_id, rx.emoji, COUNT(*) AS cnt, BOOL_OR(rx.username = @me) AS mine
              FROM web_chat_reactions rx
              JOIN web_chat_messages m ON m.id = rx.message_id
              WHERE m.conversation_id = @cid
              GROUP BY rx.message_id, rx.emoji
              ORDER BY rx.message_id, MIN(rx.created_at)")
            .With("@cid", conversationId).With("@me", me).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var mid = r.Long("message_id");
                if (!byMessage.TryGetValue(mid, out var l)) { l = new List<ChatReactionDto>(); byMessage[mid] = l; }
                l.Add(new ChatReactionDto(r.Str("emoji"), (int)r.Long("cnt"), r.Bool("mine")));
            }
        }
        if (byMessage.Count == 0) return;

        for (var i = 0; i < messages.Count; i++)
            if (byMessage.TryGetValue(messages[i].Id, out var rl))
                messages[i] = messages[i] with { Reactions = rl };
    }

    private static async Task MarkRead(NpgsqlConnection conn, Guid conversationId, string username)
    {
        await conn.Cmd(
            "UPDATE web_chat_members SET last_read_at = CURRENT_TIMESTAMP WHERE conversation_id = @cid AND username = @u")
            .With("@cid", conversationId).With("@u", username).ExecuteNonQueryAsync();
    }

    private static async Task<Guid> GetOrCreateDirect(NpgsqlConnection conn, string me, string other)
    {
        var found = await conn.Cmd(
            @"SELECT c.id
              FROM web_chat_conversations c
              WHERE c.is_group = FALSE
                AND EXISTS (SELECT 1 FROM web_chat_members m WHERE m.conversation_id = c.id AND m.username = @me)
                AND EXISTS (SELECT 1 FROM web_chat_members m WHERE m.conversation_id = c.id AND m.username = @other)
                AND (SELECT COUNT(*) FROM web_chat_members m WHERE m.conversation_id = c.id) = 2
              LIMIT 1")
            .With("@me", me).With("@other", other).ExecuteScalarAsync();
        if (found is Guid g) return g;

        var id = Guid.NewGuid();
        await conn.Cmd(
            @"INSERT INTO web_chat_conversations (id, is_group, title, created_by, created_at)
              VALUES (@id, FALSE, '', @me, CURRENT_TIMESTAMP);
              INSERT INTO web_chat_members (conversation_id, username, joined_at)
              VALUES (@id, @me, CURRENT_TIMESTAMP), (@id, @other, CURRENT_TIMESTAMP);")
            .With("@id", id).With("@me", me).With("@other", other).ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<List<ChatConversationDto>> ReadConversations(NpgsqlConnection conn, string me, Guid? onlyId)
    {
        var list = new List<ChatConversationDto>();
        var cmd = conn.Cmd(
            $@"SELECT c.id, c.is_group, c.title,
                      o.username AS other_username,
                      au.full_name AS other_full_name,
                      {VerifiedExpr} AS other_verified,
                      oav.image_data_url AS other_avatar,
                      COALESCE(pres.is_online, FALSE) AS other_online,
                      pres.last_seen_utc AS other_last_seen,
                      lm.body AS last_body, lm.sender_username AS last_sender, lm.created_at AS last_at,
                      COALESCE(lm.is_removed, FALSE) AS last_removed,
                      COALESCE(unr.cnt, 0) AS unread
               FROM web_chat_members me
               JOIN web_chat_conversations c ON c.id = me.conversation_id
               LEFT JOIN LATERAL (
                   SELECT m2.username FROM web_chat_members m2
                   WHERE m2.conversation_id = c.id AND m2.username <> @me ORDER BY m2.joined_at
                   LIMIT 1
               ) o ON TRUE
               LEFT JOIN app_users au ON au.username = o.username AND au.is_deleted = FALSE
               LEFT JOIN web_verified_users vu ON vu.username = o.username
               LEFT JOIN LATERAL (
                   SELECT wa.image_data_url FROM web_user_avatars wa WHERE wa.user_id = au.id LIMIT 1
               ) oav ON TRUE
               LEFT JOIN LATERAL (
                   SELECT BOOL_OR(us.is_active = TRUE AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds') AS is_online,
                          MAX(us.last_seen) AS last_seen_utc
                   FROM user_sessions us WHERE us.username = o.username
               ) pres ON TRUE
               LEFT JOIN LATERAL (
                   SELECT mm.body, mm.sender_username, mm.created_at, mm.is_removed
                   FROM web_chat_messages mm WHERE mm.conversation_id = c.id
                   ORDER BY mm.created_at DESC, mm.id DESC
                   LIMIT 1
               ) lm ON TRUE
               LEFT JOIN LATERAL (
                   SELECT COUNT(*) AS cnt FROM web_chat_messages mm
                   WHERE mm.conversation_id = c.id AND mm.sender_username <> @me AND mm.is_removed = FALSE
                     AND (me.last_read_at IS NULL OR mm.created_at > me.last_read_at)
               ) unr ON TRUE
               WHERE me.username = @me {(onlyId is null ? "" : "AND c.id = @only")}
               -- NULLS LAST: PostgreSQL mặc định xếp NULL trước khi DESC, sẽ đẩy hội thoại chưa có
               -- tin nhắn (last_at NULL) lên đầu. SQL Server xếp NULL cuối — giữ nguyên hành vi đó.
               ORDER BY lm.created_at DESC NULLS LAST")
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
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS web_chat_conversations (
                id uuid NOT NULL PRIMARY KEY,
                is_group boolean NOT NULL DEFAULT FALSE,
                title varchar(200) NOT NULL DEFAULT '',
                created_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS web_chat_members (
                conversation_id uuid NOT NULL,
                username varchar(128) NOT NULL,
                last_read_at timestamptz NULL,
                joined_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT pk_web_chat_members PRIMARY KEY (conversation_id, username)
            );

            CREATE TABLE IF NOT EXISTS web_chat_messages (
                id bigserial NOT NULL PRIMARY KEY,
                conversation_id uuid NOT NULL,
                sender_username varchar(128) NOT NULL,
                body text NOT NULL,
                edited_at timestamptz NULL,
                is_removed boolean NOT NULL DEFAULT FALSE,
                is_forwarded boolean NOT NULL DEFAULT FALSE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS edited_at timestamptz NULL;
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS is_removed boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS is_forwarded boolean NOT NULL DEFAULT FALSE;

            CREATE TABLE IF NOT EXISTS web_verified_users (
                username varchar(128) NOT NULL PRIMARY KEY,
                granted_by varchar(128) NOT NULL DEFAULT '',
                granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS web_chat_reactions (
                message_id bigint NOT NULL,
                username varchar(128) NOT NULL,
                emoji varchar(16) NOT NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT pk_web_chat_reactions PRIMARY KEY (message_id, username)
            );

            CREATE INDEX IF NOT EXISTS ix_web_chat_messages_conv ON web_chat_messages (conversation_id, created_at DESC, id DESC);
            CREATE INDEX IF NOT EXISTS ix_web_chat_members_user ON web_chat_members (username, conversation_id, last_read_at);
            CREATE INDEX IF NOT EXISTS ix_web_chat_reactions_msg ON web_chat_reactions (message_id);
            """)
            .ExecuteNonQueryAsync(ct);
    }
}
