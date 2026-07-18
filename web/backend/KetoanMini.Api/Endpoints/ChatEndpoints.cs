using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Http.Features;
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
    public const string SupportUsername = "__support__";
    public const string SupportDisplayName = "Hỗ Trợ Người Dùng";

    // Biểu thức tính "tích xanh" cho một người dùng (cần JOIN bí danh app_users là `au`
    // và LEFT JOIN web_verified_users là `vu`).
    private const string VerifiedExpr =
        "(au.role = 'Admin' OR vu.username IS NOT NULL)";
    private const string DiamondExpr =
        "(au.role = 'Admin' OR dm.username IS NOT NULL)";

    public static void MapChat(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/chat").RequireAuthorization();

        // Đổ chuông cuộc gọi qua FCM: để máy người nhận reo KỂ CẢ khi app đóng/nền (SignalR chỉ chạy khi
        // app mở). Đây chỉ là "chuông" — bắt tay + media của cuộc gọi vẫn P2P WebRTC (mã hóa DTLS-SRTP).
        // Yêu cầu đăng nhập; không cho tự gọi mình; giới hạn độ dài callId để chống lạm dụng.
        g.MapPost("/call/ring", async (CallRingRequest req, ClaimsPrincipal principal, Database db, PushService push) =>
        {
            var me = principal.Username();
            if (string.IsNullOrWhiteSpace(req.ToUsername) || string.IsNullOrWhiteSpace(req.CallId)) return Results.BadRequest();
            if (req.CallId.Length > 64) return Results.BadRequest();
            if (string.Equals(req.ToUsername, me, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest();
            var media = string.Equals(req.Media, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
            await using var conn = await db.OpenAsync();
            var name = await conn.Cmd("SELECT full_name FROM app_users WHERE lower(username)=lower(@u)")
                .With("@u", me).ExecuteScalarAsync() as string;
            await push.SendCallInviteAsync(req.ToUsername, me, string.IsNullOrWhiteSpace(name) ? me : name, req.CallId, media);
            return Results.Ok();
        });

        // TURN credential ĐỘNG (an toàn: KHÔNG nhúng mật khẩu vào APK — credential có hạn giờ, chỉ người
        // đã đăng nhập mới xin được). Hai nhà cung cấp, tự chọn theo cấu hình:
        //   1) Cloudflare TURN (khuyên dùng): gọi API Cloudflare cấp credential (miễn phí ~1TB/tháng,
        //      không cần dựng server). Bật khi có Turn:Cloudflare:KeyId + ApiToken.
        //   2) coturn tự dựng: ký HMAC-SHA1 theo cơ chế TURN REST (khi có Turn:Secret + Turn:Urls).
        // Chưa cấu hình cái nào → trả rỗng, app tự lùi về STUN (vẫn gọi được trong cùng LAN).
        g.MapGet("/call/turn", async (ClaimsPrincipal principal, IConfiguration config, IHttpClientFactory httpFactory) =>
        {
            var ttl = int.TryParse(config["Turn:TtlSeconds"], out var t) && t > 0 ? t : 3600;

            // --- (1) Cloudflare TURN ---
            var cfKeyId = config["Turn:Cloudflare:KeyId"];
            var cfToken = config["Turn:Cloudflare:ApiToken"];
            if (!string.IsNullOrWhiteSpace(cfKeyId) && !string.IsNullOrWhiteSpace(cfToken))
            {
                var creds = await CloudflareTurnAsync(httpFactory, cfKeyId!, cfToken!, ttl);
                return Results.Ok(creds ?? new TurnCredsDto(Array.Empty<string>(), "", "", 0));
            }

            // --- (2) coturn (shared secret) ---
            var secret = config["Turn:Secret"];
            var urlsRaw = config["Turn:Urls"];
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(urlsRaw))
                return Results.Ok(new TurnCredsDto(Array.Empty<string>(), "", "", 0));
            var me = principal.Username();
            var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttl;
            var username = $"{expiry}:{me}";
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
            var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
            var urls = urlsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Results.Ok(new TurnCredsDto(urls, username, credential, ttl));
        });

        // Hủy chuông (người gọi cúp trước khi người kia bắt máy) → tắt thông báo đổ chuông ở máy nhận.
        g.MapPost("/call/cancel", async (CallCancelRequest req, ClaimsPrincipal principal, PushService push) =>
        {
            var me = principal.Username();
            if (string.IsNullOrWhiteSpace(req.ToUsername) || string.IsNullOrWhiteSpace(req.CallId)) return Results.BadRequest();
            await push.SendCallCancelAsync(req.ToUsername, me, req.CallId);
            return Results.Ok();
        });

        // CUỘC GỌI NHỠ: gọi khi người kia KHÔNG bắt máy/không online. Gửi qua kênh thông báo THƯỜNG
        // (TTL mặc định ~4 tuần) nên máy người nhận nhận được KHI online lại, dù lúc gọi đang tắt mạng.
        g.MapPost("/call/missed", async (CallMissedRequest req, ClaimsPrincipal principal, Database db, PushService push) =>
        {
            var me = principal.Username();
            if (string.IsNullOrWhiteSpace(req.ToUsername) || string.IsNullOrWhiteSpace(req.CallId)) return Results.BadRequest();
            if (string.Equals(req.ToUsername, me, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest();
            await using var conn = await db.OpenAsync();
            var name = await conn.Cmd("SELECT full_name FROM app_users WHERE lower(username)=lower(@u)")
                .With("@u", me).ExecuteScalarAsync() as string;
            var caller = string.IsNullOrWhiteSpace(name) ? me : name;
            var isVideo = string.Equals(req.Media, "video", StringComparison.OrdinalIgnoreCase);
            var media = isVideo ? "video" : "audio";
            // Lưu bền vào DB (chống trùng theo (to, call_id)) → người nhận lấy được khi mở app dù lúc
            // gọi đang offline / chưa đăng ký token / tắt thông báo.
            await conn.Cmd("""
                INSERT INTO web_call_events (to_username, from_username, from_name, call_id, media)
                VALUES (@to, @from, @name, @cid, @media)
                ON CONFLICT (to_username, call_id) DO NOTHING
                """)
                .With("@to", req.ToUsername).With("@from", me).With("@name", caller)
                .With("@cid", req.CallId).With("@media", media)
                .ExecuteNonQueryAsync();
            await push.SendToUserAsync(req.ToUsername, "Cuộc gọi nhỡ", $"Cuộc gọi {(isVideo ? "video" : "thoại")} nhỡ từ {caller}", $"callmiss:{req.CallId}");
            return Results.Ok();
        });

        // Lấy danh sách cuộc gọi nhỡ CHƯA XEM (từ DB) — app gọi khi mở/đăng nhập để hiện dù trước đó offline.
        g.MapGet("/call/missed", async (ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            var list = new List<MissedCallDto>();
            await using var r = await conn.Cmd("""
                SELECT id, from_username, from_name, media, call_id, created_at
                FROM web_call_events WHERE lower(to_username)=lower(@u) AND seen=FALSE
                ORDER BY created_at DESC LIMIT 50
                """).With("@u", me).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new MissedCallDto(r.Long("id"), r.Str("from_username"), r.Str("from_name"),
                    r.Str("media"), r.Str("call_id"), r.Dt("created_at")));
            return Results.Ok(list);
        });

        // Đánh dấu đã xem hết các cuộc gọi nhỡ (sau khi app đã hiện lên).
        g.MapPost("/call/missed/seen", async (ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("UPDATE web_call_events SET seen=TRUE WHERE lower(to_username)=lower(@u) AND seen=FALSE")
                .With("@u", me).ExecuteNonQueryAsync();
            return Results.Ok();
        });

        g.MapPost("/call/history", async (RecordCallRequest req, ClaimsPrincipal principal, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.CallId) || string.IsNullOrWhiteSpace(req.PeerUsername)) return Results.BadRequest();
            var ended = DateTimeOffset.FromUnixTimeMilliseconds(req.EndedAtEpochMs).UtcDateTime;
            DateTime? started = req.StartedAtEpochMs is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(req.StartedAtEpochMs.Value).UtcDateTime : null;
            var duration = started is null ? 0 : Math.Max(0, (int)(ended - started.Value).TotalSeconds);
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                INSERT INTO web_call_history
                    (username, peer_username, peer_name, call_id, media, direction, outcome, started_at, ended_at, duration_seconds)
                VALUES (@u,@peer,@name,@cid,@media,@direction,@outcome,@started,@ended,@duration)
                ON CONFLICT (username, call_id) DO UPDATE SET
                    outcome=EXCLUDED.outcome, started_at=EXCLUDED.started_at, ended_at=EXCLUDED.ended_at,
                    duration_seconds=EXCLUDED.duration_seconds, peer_name=EXCLUDED.peer_name
                """)
                .With("@u", principal.Username()).With("@peer", req.PeerUsername).With("@name", req.PeerName ?? "")
                .With("@cid", req.CallId).With("@media", string.Equals(req.Media, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio")
                .With("@direction", string.Equals(req.Direction, "incoming", StringComparison.OrdinalIgnoreCase) ? "incoming" : "outgoing")
                .With("@outcome", req.Outcome ?? "ended").With("@started", started).With("@ended", ended).With("@duration", duration)
                .ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        g.MapGet("/call/history", async (ClaimsPrincipal principal, Database db, int? take) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<CallHistoryDto>();
            await using var r = await conn.Cmd("""
                SELECT id, peer_username, peer_name, call_id, media, direction, outcome, started_at, ended_at, duration_seconds
                FROM web_call_history WHERE lower(username)=lower(@u) ORDER BY ended_at DESC LIMIT @take
                """).With("@u", principal.Username()).With("@take", Math.Clamp(take ?? 100, 1, 200)).ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new CallHistoryDto(
                r.Long("id"), r.Str("peer_username"), r.Str("peer_name"), r.Str("call_id"), r.Str("media"),
                r.Str("direction"), r.Str("outcome"), r.DtNull("started_at"), r.Dt("ended_at"), r.Int("duration_seconds")));
            return Results.Ok(list);
        });

        // Danh bạ: mọi tài khoản đang hoạt động (trừ chính mình) để bắt đầu cuộc trò chuyện.
        g.MapGet("/contacts", async (ClaimsPrincipal principal, Database db, string? search) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();

            var where = "WHERE au.is_deleted = FALSE AND au.is_active = TRUE AND au.approval_status = 'Approved' AND au.username <> @me";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (au.username ILIKE @s OR au.full_name ILIKE @s OR e.position ILIKE @s OR d.name ILIKE @s)";

            var cmd = conn.Cmd(
                $@"SELECT au.username, au.full_name, au.role,
                          {VerifiedExpr} AS verified,
                          {DiamondExpr} AS is_diamond,
                          av.image_data_url AS avatar,
                          COALESCE(pres.is_online, FALSE) AS is_online,
                          COALESCE(e.id::text, '') AS employee_id, e.employee_code, e.position, e.phone, e.email,
                          COALESCE(d.id::text, '') AS department_id, d.name AS department_name,
                          mgr.username AS manager_username, mgr.full_name AS manager_name,
                          (e.id = viewer.manager_id) AS is_direct_manager,
                          (e.department_id IS NOT NULL AND e.department_id = viewer.department_id) AS same_department,
                          e.show_phone_in_directory, e.show_email_in_directory
                   FROM app_users au
                   LEFT JOIN hr_employees e ON lower(e.username)=lower(au.username)
                   LEFT JOIN hr_departments d ON d.id=e.department_id
                   LEFT JOIN hr_employees mgr ON mgr.id=e.manager_id
                   LEFT JOIN hr_employees viewer ON lower(viewer.username)=lower(@me)
                   LEFT JOIN web_verified_users vu ON vu.username = au.username
                   LEFT JOIN web_diamond_members dm ON dm.username = au.username
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
                    NullIfEmpty(r.Str("avatar")), r.Bool("is_online"), r.Bool("verified"), r.Bool("is_diamond"), r.Str("role"),
                    r.Str("employee_id"), r.Str("employee_code"), r.Str("department_id"), r.Str("department_name"), r.Str("position"),
                    principal.IsAdmin() || r.Bool("show_phone_in_directory") ? r.Str("phone") : "",
                    principal.IsAdmin() || r.Bool("show_email_in_directory") ? r.Str("email") : "",
                    r.Str("manager_username"), r.Str("manager_name"), r.Bool("is_direct_manager"), r.Bool("same_department")));
            }
            if (!string.Equals(me, SupportUsername, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(search) ||
                 SupportDisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 "ho tro nguoi dung".Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                list.Insert(0, new ChatContactDto(SupportUsername, SupportDisplayName, null, true, true, false, "Support"));
            }
            return Results.Ok(list);
        });

        // Danh sách cuộc trò chuyện của tôi (kèm tin nhắn cuối, số chưa đọc, thông tin người kia).
        g.MapGet("/conversations", async (ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            var list = await ReadConversations(conn, me, null, principal.IsAdmin());
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
            if (!IsSupportUser(other))
            {
                var exists = await conn.Cmd(
                    "SELECT COUNT(*) FROM app_users WHERE username = @u AND is_deleted = FALSE AND is_active = TRUE")
                    .With("@u", other).ExecuteScalarAsync();
                if (Convert.ToInt32(exists) == 0)
                    return Results.NotFound(new { message = "Không tìm thấy người dùng." });
            }

            var id = await GetOrCreateDirect(conn, me, other);
            return Results.Ok(new { id });
        });

        // Admin mở luồng Hỗ Trợ riêng với một nhân viên. Tin nhắn gửi bằng tư cách Hỗ Trợ
        // sẽ nằm ở hội thoại này, không trộn vào chat cá nhân admin ↔ nhân viên.
        g.MapPost("/support/{username}", async (string username, ClaimsPrincipal principal, Database db) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();
            var employee = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(employee) || IsSupportUser(employee))
                return Results.BadRequest(new { message = "Người nhận không hợp lệ." });

            await using var conn = await db.OpenAsync();
            var exists = await conn.Cmd(
                "SELECT COUNT(*) FROM app_users WHERE username = @u AND is_deleted = FALSE AND is_active = TRUE")
                .With("@u", employee).ExecuteScalarAsync();
            if (Convert.ToInt32(exists) == 0)
                return Results.NotFound(new { message = "Không tìm thấy người dùng." });

            var id = await GetOrCreateDirect(conn, employee, SupportUsername);
            return Results.Ok(new { id });
        });

        // Tin nhắn của một cuộc trò chuyện (phải là thành viên). Đồng thời đánh dấu đã đọc.
        g.MapGet("/conversations/{id:guid}/messages", async (Guid id, ClaimsPrincipal principal, Database db, long? beforeId, int? take, string? search) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            await using var conn = await db.OpenAsync();
            if (!await CanAccessConversation(conn, id, me, admin)) return Results.Forbid();
            var viewerMember = await ViewerMemberUsername(conn, id, me, admin) ?? me;

            var list = new List<ChatMessageDto>();
            var beforeFilter = beforeId is > 0 ? "AND m.id < @before" : "";
            var searchFilter = !string.IsNullOrWhiteSpace(search) ? "AND (m.body ILIKE @search OR m.file_name ILIKE @search)" : "";
            var cmd = conn.Cmd(
                $@"SELECT m.id, m.sender_username, au.full_name AS sender_name, m.body, m.created_at,
                         m.edited_at, m.is_removed, m.is_forwarded, m.kind, m.file_name, m.file_size, m.file_mime,
                         m.has_blob,
                         EXISTS (
                             SELECT 1 FROM web_chat_members rm
                             WHERE rm.conversation_id=m.conversation_id
                               AND rm.username<>m.sender_username
                               AND rm.last_read_at IS NOT NULL
                               AND rm.last_read_at>=m.created_at
                         ) AS is_read
                  FROM web_chat_messages m
                  LEFT JOIN app_users au ON au.username = m.sender_username AND au.is_deleted = FALSE
                  WHERE m.conversation_id = @cid
                    AND NOT (m.kind = 'voice' AND m.has_blob = FALSE AND m.is_removed = FALSE)
                    {beforeFilter} {searchFilter}
                  ORDER BY m.id DESC LIMIT @take")
                .With("@cid", id)
                .With("@take", Math.Clamp(take ?? 50, 1, 100));
            if (beforeId is > 0) cmd.With("@before", beforeId.Value);
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@search", $"%{search.Trim()}%");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var sender = r.Str("sender_username");
                var name = r.Str("sender_name");
                if (IsSupportUser(sender)) name = SupportDisplayName;
                var removed = r.Bool("is_removed");
                var kind = r.Str("kind");
                if (string.IsNullOrEmpty(kind)) kind = "text";
                list.Add(new ChatMessageDto(
                    r.Long("id"), sender, string.IsNullOrWhiteSpace(name) ? sender : name,
                    IsMine(sender, me, admin),
                    removed ? "" : r.Str("body"), r.Dt("created_at"),
                    r.DtNull("edited_at"), removed, r.Bool("is_forwarded"), null,
                    removed ? "text" : kind,
                    removed ? null : NullIfEmpty(r.Str("file_name")),
                    removed ? null : r.LongNull("file_size"),
                    removed ? null : NullIfEmpty(r.Str("file_mime")),
                    !removed && r.Bool("has_blob"), r.Bool("is_read")));
            }
            await r.CloseAsync();
            list.Reverse();
            await AttachReactions(conn, id, viewerMember, list);
            await MarkReadForViewer(conn, id, me, admin);
            return Results.Ok(list);
        });

        // Gửi tin nhắn.
        g.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub, PushService push) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            var body = (req?.Body ?? "").Trim();
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { message = "Tin nhắn trống." });
            if (body.Length > 4000) body = body[..4000];

            await using var conn = await db.OpenAsync();
            if (!await CanAccessConversation(conn, id, me, admin)) return Results.Forbid();
            var sender = req?.SendAsSupport == true ? SupportUsername : me;
            if (IsSupportUser(sender) && !admin) return Results.Forbid();
            if (IsSupportUser(sender) && !await HasSupportMember(conn, id))
                return Results.BadRequest(new { message = "Hãy mở hội thoại Hỗ Trợ trước khi gửi bằng tài khoản Hỗ Trợ." });

            var forwarded = req?.Forwarded ?? false;
            var newId = Convert.ToInt64(await conn.Cmd(
                @"INSERT INTO web_chat_messages (conversation_id, sender_username, body, is_forwarded, created_at)
                  VALUES (@cid, @sender, @body, @fwd, CURRENT_TIMESTAMP)
                  RETURNING id;")
                .With("@cid", id).With("@sender", sender).With("@body", body).With("@fwd", forwarded).ExecuteScalarAsync());

            await conn.Cmd(
                @"UPDATE web_chat_members
                  SET is_hidden = FALSE, deleted_at = NULL
                  WHERE conversation_id = @cid
                    AND (@sender = @support OR username <> @sender)")
                .With("@cid", id).With("@sender", sender).With("@support", SupportUsername).ExecuteNonQueryAsync();
            await MarkReadForViewer(conn, id, me, admin);
            await NotifyChat(hub, conn, id);
            await SendChatPush(push, conn, id, sender, newId, body);

            return Results.Ok(new ChatMessageDto(newId, sender, IsSupportUser(sender) ? SupportDisplayName : me, true, body, DateTime.UtcNow, null, false, forwarded));
        });

        // Ghi lại "đã gửi tệp X" qua LAN. CHỈ lưu metadata (tên/dung lượng/kiểu) để hiện trong lịch sử
        // chat; nội dung tệp KHÔNG đi qua đây — nó truyền thẳng P2P (WebRTC) giữa 2 trình duyệt qua LAN.
        // Trả về id tin nhắn để phía gửi gắn (correlate) với phiên truyền WebRTC tương ứng.
        g.MapPost("/conversations/{id:guid}/messages/file", async (Guid id, SendFileMessageRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub, PushService push) =>
        {
            var me = principal.Username();
            var name = (req?.FileName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { message = "Thiếu tên tệp." });
            if (name.Length > 260) name = name[..260];
            var size = req?.FileSize ?? 0;
            if (size < 0) size = 0;
            var mime = (req?.FileMime ?? "").Trim();
            if (mime.Length > 160) mime = mime[..160];
            if (!ChatAttachmentPolicy.TryResolveKind(req?.Kind, name, mime, out var kind))
                return Results.BadRequest(new { message = "Loại tin nhắn tệp không hợp lệ hoặc voice không phải dữ liệu âm thanh." });
            var persistentVoice = ChatAttachmentPolicy.IsPersistentVoice(kind, name, mime);
            var clientMessageId = persistentVoice ? (req?.ClientMessageId ?? "").Trim() : "";
            if (clientMessageId.Length > 128)
                return Results.BadRequest(new { message = "Mã gửi tin thoại quá dài." });

            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();
            // Tin thoại là chức năng chat cho mọi thành viên; chỉ tệp đính kèm LAN mới cần hạng Kim Cương.
            if (!persistentVoice && !await IsDiamondMember(conn, me))
                return Results.Json(new { message = "Chi hoi vien kim cuong moi duoc gui tep qua LAN." }, statusCode: StatusCodes.Status403Forbidden);

            long newId;
            var alreadyStored = false;
            var messageCreatedAt = DateTime.UtcNow;
            await using (var r = await conn.Cmd(
                @"INSERT INTO web_chat_messages
                      (conversation_id, sender_username, body, kind, file_name, file_size, file_mime, client_message_id, created_at)
                  VALUES (@cid, @me, '', @kind, @name, @size, @mime, @client_id, CURRENT_TIMESTAMP)
                  ON CONFLICT (conversation_id, sender_username, client_message_id)
                      WHERE client_message_id IS NOT NULL AND is_removed = FALSE
                  DO UPDATE SET client_message_id = EXCLUDED.client_message_id
                  RETURNING id, has_blob, created_at;")
                .With("@cid", id).With("@me", me).With("@name", name).With("@size", size)
                .With("@mime", string.IsNullOrEmpty(mime) ? null : mime).With("@kind", kind)
                .With("@client_id", string.IsNullOrEmpty(clientMessageId) ? null : clientMessageId).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.Problem("Không tạo được metadata tin nhắn.");
                newId = r.Long("id");
                alreadyStored = r.Bool("has_blob");
                messageCreatedAt = r.Dt("created_at");
            }

            await conn.Cmd(
                @"UPDATE web_chat_members
                  SET is_hidden = FALSE, deleted_at = NULL
                  WHERE conversation_id = @cid AND username <> @me")
                .With("@cid", id).With("@me", me).ExecuteNonQueryAsync();
            await MarkRead(conn, id, me);
            // Voice chỉ xuất hiện ở máy nhận sau khi blob upload hoàn tất; tránh bong bóng rỗng/push ma.
            if (!persistentVoice)
            {
                await NotifyChat(hub, conn, id);
                await SendChatPush(push, conn, id, me, newId, $"📎 {name}");
            }

            return Results.Ok(new ChatMessageDto(
                newId, me, me, true, "", messageCreatedAt, null, false, false, null,
                kind, name, size, string.IsNullOrEmpty(mime) ? null : mime, alreadyStored));
        });

        // Tệp thường được giữ tạm theo TTL. Voice là payload bền vững của tin nhắn: không TTL và chỉ bị
        // xóa khi người gửi chủ động gỡ. Ghi qua file .upload rồi atomic-replace để không lộ blob dở dang.
        g.MapPost("/conversations/{id:guid}/messages/{msgId:long}/upload", async (Guid id, long msgId, HttpContext ctx, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub, PushService push) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();

            string kind = "", fileName = "", fileMime = "";
            var alreadyStored = false;
            await using (var r = await conn.Cmd(
                @"SELECT kind, file_name, file_mime, has_blob FROM web_chat_messages
                  WHERE id = @mid AND conversation_id = @cid AND sender_username = @me
                    AND kind IN ('file', 'voice') AND is_removed = FALSE")
                .With("@mid", msgId).With("@cid", id).With("@me", me).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    kind = r.Str("kind");
                    fileName = r.Str("file_name");
                    fileMime = r.Str("file_mime");
                    alreadyStored = r.Bool("has_blob");
                }
            }
            if (string.IsNullOrEmpty(kind)) return Results.NotFound(new { message = "Không tìm thấy tin nhắn tệp." });

            var persistentVoice = ChatAttachmentPolicy.IsPersistentVoice(kind, fileName, fileMime);
            // Retry sau khi server đã lưu nhưng client mất response: không đọc/ghi blob và không push lần hai.
            if (persistentVoice && alreadyStored) return Results.Ok(new { stored = true });
            async Task RemovePendingVoiceAsync()
            {
                if (!persistentVoice) return;
                await conn.Cmd(
                    @"DELETE FROM web_chat_messages
                      WHERE id = @mid AND conversation_id = @cid AND sender_username = @me
                        AND has_blob = FALSE AND is_removed = FALSE")
                    .With("@mid", msgId).With("@cid", id).With("@me", me).ExecuteNonQueryAsync();
            }
            if (!persistentVoice && !await IsDiamondMember(conn, me))
                return Results.Json(new { message = "Chi hoi vien kim cuong moi duoc gui tep qua LAN." }, statusCode: StatusCodes.Status403Forbidden);
            var maxBytes = persistentVoice ? MaxVoiceBlobBytes : MaxBlobBytes;
            if (ctx.Request.ContentLength is > 0 && ctx.Request.ContentLength > maxBytes)
            {
                await RemovePendingVoiceAsync();
                return Results.BadRequest(new { message = persistentVoice ? "Tin thoại quá lớn (giới hạn 25MB)." : "Tệp quá lớn (giới hạn 100MB)." });
            }

            // Nâng giới hạn kích thước request cho riêng endpoint này (mặc định Kestrel ~30MB).
            var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = maxBytes + 1024;

            var path = BlobPath(msgId);
            var uploadPath = $"{path}.{Guid.NewGuid():N}.upload";
            long total = 0;
            var tooLarge = false;
            try
            {
                await using (var fs = new FileStream(uploadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
                    {
                        total += read;
                        if (total > maxBytes)
                        {
                            tooLarge = true;
                            break;
                        }
                        await fs.WriteAsync(buffer.AsMemory(0, read), ctx.RequestAborted);
                    }
                    if (!tooLarge) await fs.FlushAsync(ctx.RequestAborted);
                }

                if (tooLarge)
                {
                    TryDeletePath(uploadPath);
                    await RemovePendingVoiceAsync();
                    return Results.BadRequest(new { message = persistentVoice ? "Tin thoại quá lớn (giới hạn 25MB)." : "Tệp quá lớn (giới hạn 100MB)." });
                }
                if (total == 0)
                {
                    TryDeletePath(uploadPath);
                    await RemovePendingVoiceAsync();
                    return Results.BadRequest(new { message = "Nội dung tải lên trống." });
                }

                File.Move(uploadPath, path, true);
            }
            catch
            {
                TryDeletePath(uploadPath);
                await RemovePendingVoiceAsync();
                return Results.BadRequest(new { message = "Tải tệp lên thất bại." });
            }

            var expiresAt = ChatAttachmentPolicy.BlobExpiresAt(kind, fileName, fileMime, DateTime.UtcNow, BlobTtl);
            try
            {
                await conn.Cmd(
                    "UPDATE web_chat_messages SET has_blob = TRUE, blob_expires_at = @exp WHERE id = @mid")
                    .With("@exp", expiresAt).With("@mid", msgId).ExecuteNonQueryAsync();
            }
            catch
            {
                TryDeleteBlob(msgId);
                await RemovePendingVoiceAsync();
                throw;
            }
            await NotifyChat(hub, conn, id);
            if (persistentVoice)
                await SendChatPush(push, conn, id, me, msgId, "🎤 Tin nhắn thoại");
            return Results.Ok(new { stored = true });
        });

        // Tệp thường vẫn là store-and-forward một lần. Voice được tải/phát lại nhiều lần và KHÔNG đổi
        // has_blob chỉ vì người nhận đã tải hoặc đã đọc.
        g.MapGet("/conversations/{id:guid}/messages/{msgId:long}/download", async (Guid id, long msgId, HttpContext ctx, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();

            string sender = "", fileName = "", fileMime = "", kind = "";
            var hasBlob = false;
            await using (var r = await conn.Cmd(
                @"SELECT sender_username, file_name, file_mime, kind, has_blob
                  FROM web_chat_messages WHERE id = @mid AND conversation_id = @cid AND kind IN ('file', 'voice')")
                .With("@mid", msgId).With("@cid", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    sender = r.Str("sender_username");
                    fileName = r.Str("file_name");
                    fileMime = r.Str("file_mime");
                    kind = r.Str("kind");
                    hasBlob = r.Bool("has_blob");
                }
            }

            var path = BlobPath(msgId);
            if (!hasBlob || string.IsNullOrEmpty(sender) || !File.Exists(path))
                return Results.NotFound(new { message = "Tệp không còn trên máy chủ (đã tải xong hoặc hết hạn)." });

            var name = string.IsNullOrWhiteSpace(fileName) ? $"tep-{msgId}" : fileName;
            var mime = string.IsNullOrWhiteSpace(fileMime) ? "application/octet-stream" : fileMime;
            var isRecipient = !string.Equals(sender, me, StringComparison.OrdinalIgnoreCase);

            ctx.Response.ContentType = mime;
            ctx.Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(name)}";
            try
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                ctx.Response.ContentLength = fs.Length;
                await fs.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return Results.Empty; // tải dở dang → GIỮ tệp để người nhận tải lại
            }

            if (isRecipient && ChatAttachmentPolicy.DeleteAfterRecipientDownload(kind, fileName, fileMime))
            {
                TryDeleteBlob(msgId);
                await conn.Cmd("UPDATE web_chat_messages SET has_blob = FALSE, blob_expires_at = NULL WHERE id = @mid")
                    .With("@mid", msgId).ExecuteNonQueryAsync();
                await NotifyChat(hub, conn, id);
            }
            return Results.Empty;
        });

        // Chỉnh sửa tin nhắn (chỉ người gửi, chưa bị gỡ).
        g.MapPut("/conversations/{id:guid}/messages/{msgId:long}", async (Guid id, long msgId, EditMessageRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            var body = (req?.Body ?? "").Trim();
            if (string.IsNullOrWhiteSpace(body))
                return Results.BadRequest(new { message = "Tin nhắn trống." });
            if (body.Length > 4000) body = body[..4000];

            await using var conn = await db.OpenAsync();
            if (!await CanAccessConversation(conn, id, me, admin)) return Results.Forbid();
            var n = await conn.Cmd(
                @"UPDATE web_chat_messages SET body = @body, edited_at = CURRENT_TIMESTAMP
                  WHERE id = @mid AND conversation_id = @cid AND is_removed = FALSE
                    AND (
                        sender_username = @me
                        OR (@admin = TRUE AND sender_username = @support
                            AND EXISTS (
                                SELECT 1 FROM web_chat_members sm
                                WHERE sm.conversation_id = @cid AND sm.username = @support
                            ))
                    )")
                .With("@body", body).With("@mid", msgId).With("@cid", id).With("@me", me)
                .With("@admin", admin).With("@support", SupportUsername).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không sửa được tin nhắn này." });
            await NotifyChat(hub, conn, id);
            return Results.NoContent();
        });

        // Gỡ tin nhắn (chỉ người gửi) — giữ lại dòng làm placeholder "Tin nhắn đã được gỡ",
        // nhưng XÓA RỖNG nội dung (body) khỏi DB để không lưu văn bản → tránh phình DB.
        g.MapDelete("/conversations/{id:guid}/messages/{msgId:long}", async (Guid id, long msgId, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            await using var conn = await db.OpenAsync();
            if (!await CanAccessConversation(conn, id, me, admin)) return Results.Forbid();
            // Gỡ luôn xóa nội dung tệp đang giữ tạm (nếu có) khỏi server + bỏ cờ has_blob.
            var n = await conn.Cmd(
                @"UPDATE web_chat_messages
                  SET is_removed = TRUE, body = '', edited_at = CURRENT_TIMESTAMP,
                      has_blob = FALSE, blob_expires_at = NULL
                  WHERE id = @mid AND conversation_id = @cid AND is_removed = FALSE
                    AND (
                        sender_username = @me
                        OR (@admin = TRUE AND sender_username = @support
                            AND EXISTS (
                                SELECT 1 FROM web_chat_members sm
                                WHERE sm.conversation_id = @cid AND sm.username = @support
                            ))
                    )")
                .With("@mid", msgId).With("@cid", id).With("@me", me)
                .With("@admin", admin).With("@support", SupportUsername).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound(new { message = "Không gỡ được tin nhắn này." });
            TryDeleteBlob(msgId);
            await NotifyChat(hub, conn, id);
            return Results.NoContent();
        });

        // Thả / đổi / bỏ biểu cảm (cảm xúc) cho một tin nhắn. Mỗi người chỉ giữ MỘT biểu cảm
        // trên một tin: bấm lại đúng biểu cảm đang chọn → bỏ; bấm biểu cảm khác → đổi sang biểu cảm mới.
        g.MapPost("/conversations/{id:guid}/messages/{msgId:long}/react", async (Guid id, long msgId, ReactRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            var emoji = (req?.Emoji ?? "").Trim();
            if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 16)
                return Results.BadRequest(new { message = "Biểu cảm không hợp lệ." });

            await using var conn = await db.OpenAsync();
            var viewerMember = await ViewerMemberUsername(conn, id, me, admin);
            if (viewerMember is null) return Results.Forbid();

            // Tin nhắn phải thuộc cuộc trò chuyện này và chưa bị gỡ.
            var ok = await conn.Cmd(
                "SELECT COUNT(*) FROM web_chat_messages WHERE id = @mid AND conversation_id = @cid AND is_removed = FALSE")
                .With("@mid", msgId).With("@cid", id).ExecuteScalarAsync();
            if (Convert.ToInt32(ok) == 0) return Results.NotFound(new { message = "Không tìm thấy tin nhắn." });

            var existing = await conn.Cmd(
                "SELECT emoji FROM web_chat_reactions WHERE message_id = @mid AND username = @viewer")
                .With("@mid", msgId).With("@viewer", viewerMember).ExecuteScalarAsync() as string;
            if (string.Equals(existing, emoji, StringComparison.Ordinal))
            {
                await conn.Cmd("DELETE FROM web_chat_reactions WHERE message_id = @mid AND username = @viewer")
                    .With("@mid", msgId).With("@viewer", viewerMember).ExecuteNonQueryAsync();
            }
            else
            {
                await conn.Cmd(
                    @"INSERT INTO web_chat_reactions (message_id, username, emoji, created_at)
                      VALUES (@mid, @viewer, @emoji, CURRENT_TIMESTAMP)
                      ON CONFLICT (message_id, username)
                      DO UPDATE SET emoji = EXCLUDED.emoji, created_at = CURRENT_TIMESTAMP")
                    .With("@mid", msgId).With("@viewer", viewerMember).With("@emoji", emoji).ExecuteNonQueryAsync();
            }

            await NotifyChat(hub, conn, id);
            return Results.NoContent();
        });

        // Đánh dấu đã đọc (khi mở cuộc trò chuyện).
        g.MapPost("/conversations/{id:guid}/read", async (Guid id, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            await using var conn = await db.OpenAsync();
            if (!await CanAccessConversation(conn, id, me, admin)) return Results.Forbid();
            await MarkReadForViewer(conn, id, me, admin);
            return Results.NoContent();
        });

        // Ghim / bỏ ghim cuộc trò chuyện trong danh sách của riêng người đang đăng nhập.
        g.MapPost("/conversations/{id:guid}/pin", async (Guid id, SetConversationPinnedRequest req, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            await using var conn = await db.OpenAsync();
            var viewerMember = await ViewerMemberUsername(conn, id, me, admin);
            if (viewerMember is null) return Results.Forbid();
            await conn.Cmd(
                "UPDATE web_chat_members SET is_pinned = @p WHERE conversation_id = @cid AND username = @u")
                .With("@p", req.Pinned).With("@cid", id).With("@u", viewerMember).ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        // Ẩn khỏi danh sách của riêng người đang đăng nhập. Tin nhắn mới từ người khác sẽ tự hiện lại.
        g.MapPost("/conversations/{id:guid}/hide", async (Guid id, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            await using var conn = await db.OpenAsync();
            var viewerMember = await ViewerMemberUsername(conn, id, me, admin);
            if (viewerMember is null) return Results.Forbid();
            await conn.Cmd(
                "UPDATE web_chat_members SET is_hidden = TRUE WHERE conversation_id = @cid AND username = @u")
                .With("@cid", id).With("@u", viewerMember).ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        // Xóa khỏi danh sách của riêng người đang đăng nhập (không xóa dữ liệu của người còn lại).
        g.MapDelete("/conversations/{id:guid}", async (Guid id, ClaimsPrincipal principal, Database db) =>
        {
            var me = principal.Username();
            var admin = principal.IsAdmin();
            await using var conn = await db.OpenAsync();
            var viewerMember = await ViewerMemberUsername(conn, id, me, admin);
            if (viewerMember is null) return Results.Forbid();
            await conn.Cmd(
                @"UPDATE web_chat_members
                  SET is_hidden = TRUE, is_pinned = FALSE, deleted_at = CURRENT_TIMESTAMP
                  WHERE conversation_id = @cid AND username = @u")
                .With("@cid", id).With("@u", viewerMember).ExecuteNonQueryAsync();
            return Results.NoContent();
        });

        // Báo xấu cuộc trò chuyện để lưu lại dấu vết xử lý sau.
        g.MapPost("/conversations/{id:guid}/report", async (Guid id, ChatReportRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var me = principal.Username();
            var reason = (req?.Reason ?? "").Trim();
            if (reason.Length > 500) reason = reason[..500];

            await using var conn = await db.OpenAsync();
            if (!await IsMember(conn, id, me)) return Results.Forbid();
            var supportConversationId = await GetOrCreateDirect(conn, me, SupportUsername);
            await conn.Cmd(
                """
                INSERT INTO app_feedbacks (feedback_type, conversation_id, reporter_username, target_name, reason, created_at)
                VALUES ('ChatReport', @supportCid, @u, 'Cuộc trò chuyện', @reason, CURRENT_TIMESTAMP);

                INSERT INTO web_chat_messages (conversation_id, sender_username, body, created_at)
                VALUES
                    (@supportCid, @u, @userMessage, CURRENT_TIMESTAMP),
                    (@supportCid, @support, @supportMessage, CURRENT_TIMESTAMP + INTERVAL '1 millisecond');

                UPDATE web_chat_members
                SET is_hidden = FALSE, deleted_at = NULL
                WHERE conversation_id = @supportCid;
                """)
                .With("@supportCid", supportConversationId)
                .With("@u", me)
                .With("@reason", reason)
                .With("@support", SupportUsername)
                .With("@userMessage", string.IsNullOrWhiteSpace(reason)
                    ? "Báo xấu cuộc trò chuyện."
                    : $"Báo xấu cuộc trò chuyện:\n{reason}")
                .With("@supportMessage", "Hỗ Trợ Người Dùng đã nhận báo xấu của bạn. Admin sẽ kiểm tra và phản hồi tại đây.")
                .ExecuteNonQueryAsync();
            await NotifyChat(hub, conn, supportConversationId);
            await hub.Clients.All.SendAsync("changed", "feedback");
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
        ("web_chat_reports", "Báo xấu"),
        ("web_verified_users", "Tài khoản tích xanh"),
        ("web_diamond_members", "Hoi vien kim cuong"),
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
                  UNION ALL SELECT 'web_chat_reports', COUNT(*)::bigint FROM web_chat_reports
                  UNION ALL SELECT 'web_verified_users', COUNT(*)::bigint FROM web_verified_users
                  UNION ALL SELECT 'web_diamond_members', COUNT(*)::bigint FROM web_diamond_members
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

    // ----- Blob chat trên ĐĨA: file thường giữ tạm, voice giữ bền vững tới khi gỡ tin -----
    internal const long MaxBlobBytes = 100L * 1024 * 1024; // 100MB mỗi tệp
    internal const long MaxVoiceBlobBytes = 25L * 1024 * 1024; // hơn 2 giờ thoại AAC 24kbps
    internal static readonly TimeSpan BlobTtl = TimeSpan.FromDays(7); // không ai nhận → tự xóa sau 7 ngày
    private static string? _blobDirectory;

    internal static void ConfigureBlobDirectory(string? configuredPath, string contentRootPath)
    {
        var requested = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine("App_Data", "chat_blobs")
            : configuredPath.Trim();
        var target = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(contentRootPath, requested));
        Directory.CreateDirectory(target);

        // Bản cũ đặt dưới output bin/.../App_Data/lan_pending. Chuyển các blob còn sống sang root mới;
        // không ghi đè nếu đích đã có để tránh làm hỏng voice đã được upload ở cấu hình mới.
        var legacy = Path.Combine(AppContext.BaseDirectory, "App_Data", "lan_pending");
        if (!string.Equals(Path.GetFullPath(legacy), target, StringComparison.OrdinalIgnoreCase) && Directory.Exists(legacy))
        {
            foreach (var source in Directory.EnumerateFiles(legacy, "*.bin"))
            {
                var destination = Path.Combine(target, Path.GetFileName(source));
                if (File.Exists(destination)) continue;
                try
                {
                    File.Move(source, destination);
                }
                catch (IOException)
                {
                    // Volume dữ liệu có thể nằm khác ổ đĩa; khi đó Move không atomic/có thể bị từ chối.
                    try
                    {
                        File.Copy(source, destination, overwrite: false);
                        if (new FileInfo(source).Length == new FileInfo(destination).Length) File.Delete(source);
                    }
                    catch { TryDeletePath(destination); /* giữ source để lần restart sau thử lại */ }
                }
                catch { /* file đang được dùng → giữ lại để lần restart sau */ }
            }
        }

        _blobDirectory = target;
    }

    /// <summary>Thư mục blob ngoài wwwroot; Program cấu hình root bền vững trước khi nhận request.</summary>
    internal static string BlobDir()
    {
        var dir = _blobDirectory ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "chat_blobs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static string BlobPath(long msgId) => Path.Combine(BlobDir(), $"{msgId}.bin");

    internal static void TryDeleteBlob(long msgId)
    {
        try { File.Delete(BlobPath(msgId)); } catch { /* tệp không có / đang khóa → bỏ qua */ }
    }

    private static void TryDeletePath(string path)
    {
        try { File.Delete(path); } catch { /* upload dở dang/đang khóa → cleanup lần sau */ }
    }

    /// <summary>Phát tín hiệu "chat" CHỈ tới các thành viên của cuộc trò chuyện (không broadcast cả 100 máy).</summary>
    internal static async Task NotifyChat(IHubContext<ChangesHub> hub, NpgsqlConnection conn, Guid conversationId)
    {
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasSupport = false;
        await using (var r = await conn.Cmd(
            "SELECT username FROM web_chat_members WHERE conversation_id = @cid")
            .With("@cid", conversationId).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var username = r.GetString(0);
                members.Add(username);
                if (IsSupportUser(username)) hasSupport = true;
            }
        }
        if (hasSupport)
        {
            await using var admins = await conn.Cmd(
                "SELECT username FROM app_users WHERE role = 'Admin' AND is_deleted = FALSE AND is_active = TRUE")
                .ExecuteReaderAsync();
            while (await admins.ReadAsync()) members.Add(admins.GetString(0));
        }
        if (members.Count == 0) return;
        // Giữ đúng contract 1 đối số của sự kiện "changed" dùng chung. Android SignalR bind arity
        // nghiêm ngặt; gửi thêm conversationId làm toàn bộ refresh chat bị bỏ. Web không dùng payload này.
        await hub.Clients.Users(members).SendAsync("changed", "chat");
    }

    private static async Task SendChatPush(PushService push, NpgsqlConnection conn, Guid conversationId, string sender, long messageId, string preview)
    {
        var recipients = new List<string>();
        await using (var r = await conn.Cmd(
            "SELECT username FROM web_chat_members WHERE conversation_id=@cid AND username<>@sender")
            .With("@cid", conversationId).With("@sender", sender).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) recipients.Add(r.Str("username"));
        }
        var body = preview.Length > 160 ? preview[..160] : preview;
        foreach (var recipient in recipients.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var signature = $"chat:{conversationId}:{messageId}";
            if (IsSupportUser(recipient)) await push.SendToAdminsAsync("Tin nhắn mới", body, signature, "Chat");
            else await push.SendToUserAsync(recipient, "Tin nhắn mới", body, signature, "Chat");
        }
    }

    /// <summary>
    /// Gọi API Cloudflare TURN cấp credential có hạn giờ cho WebRTC. Trả null nếu lỗi/không cấu hình
    /// đúng (app tự lùi về STUN). Đáp ứng của Cloudflare: { "iceServers": { urls[], username, credential } }.
    /// </summary>
    private static async Task<TurnCredsDto?> CloudflareTurnAsync(IHttpClientFactory httpFactory, string keyId, string apiToken, int ttl)
    {
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var url = $"https://rtc.live.cloudflare.com/v1/turn/keys/{keyId}/credentials/generate";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
            req.Content = new StringContent($"{{\"ttl\":{ttl}}}", Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("iceServers", out var ice)) return null;
            var username = ice.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
            var credential = ice.TryGetProperty("credential", out var c) ? c.GetString() ?? "" : "";
            var urls = new List<string>();
            if (ice.TryGetProperty("urls", out var urlsEl))
            {
                if (urlsEl.ValueKind == JsonValueKind.Array)
                    foreach (var e in urlsEl.EnumerateArray())
                    { var s = e.GetString(); if (!string.IsNullOrWhiteSpace(s)) urls.Add(s!); }
                else if (urlsEl.ValueKind == JsonValueKind.String)
                { var s = urlsEl.GetString(); if (!string.IsNullOrWhiteSpace(s)) urls.Add(s!); }
            }
            if (urls.Count == 0 || string.IsNullOrEmpty(username)) return null;
            return new TurnCredsDto(urls.ToArray(), username, credential, ttl);
        }
        catch
        {
            return null;
        }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static bool IsSupportUser(string? username) =>
        string.Equals(username, SupportUsername, StringComparison.OrdinalIgnoreCase);

    private static bool IsMine(string sender, string me, bool admin) =>
        string.Equals(sender, me, StringComparison.OrdinalIgnoreCase) || (admin && IsSupportUser(sender));

    private static async Task<bool> IsMember(NpgsqlConnection conn, Guid conversationId, string username)
    {
        var n = await conn.Cmd(
            "SELECT COUNT(*) FROM web_chat_members WHERE conversation_id = @cid AND username = @u")
            .With("@cid", conversationId).With("@u", username).ExecuteScalarAsync();
        return Convert.ToInt32(n) > 0;
    }

    private static async Task<bool> HasSupportMember(NpgsqlConnection conn, Guid conversationId)
    {
        var n = await conn.Cmd(
            "SELECT COUNT(*) FROM web_chat_members WHERE conversation_id = @cid AND username = @support")
            .With("@cid", conversationId).With("@support", SupportUsername).ExecuteScalarAsync();
        return Convert.ToInt32(n) > 0;
    }

    private static async Task<bool> CanAccessConversation(NpgsqlConnection conn, Guid conversationId, string username, bool admin)
    {
        return await ViewerMemberUsername(conn, conversationId, username, admin) is not null;
    }

    private static async Task<string?> ViewerMemberUsername(NpgsqlConnection conn, Guid conversationId, string username, bool admin)
    {
        if (await IsMember(conn, conversationId, username)) return username;
        if (admin && await HasSupportMember(conn, conversationId)) return SupportUsername;
        return null;
    }

    private static async Task<bool> IsDiamondMember(NpgsqlConnection conn, string username)
    {
        var n = await conn.Cmd(
            @"SELECT COUNT(*)
              FROM app_users au
              LEFT JOIN web_diamond_members dm ON dm.username = au.username
              WHERE au.username = @u
                AND au.is_deleted = FALSE
                AND (au.role = 'Admin' OR dm.username IS NOT NULL)")
            .With("@u", username).ExecuteScalarAsync();
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

    private static async Task MarkReadForViewer(NpgsqlConnection conn, Guid conversationId, string username, bool admin)
    {
        if (await IsMember(conn, conversationId, username))
        {
            await MarkRead(conn, conversationId, username);
            return;
        }
        if (admin && await HasSupportMember(conn, conversationId))
            await MarkRead(conn, conversationId, SupportUsername);
    }

    internal static async Task<Guid> GetOrCreateDirect(NpgsqlConnection conn, string me, string other)
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
        if (found is Guid g)
        {
            await conn.Cmd(
                @"UPDATE web_chat_members
                  SET is_hidden = FALSE, deleted_at = NULL
                  WHERE conversation_id = @cid AND username = @me")
                .With("@cid", g).With("@me", me).ExecuteNonQueryAsync();
            return g;
        }

        var id = Guid.NewGuid();
        await conn.Cmd(
            @"INSERT INTO web_chat_conversations (id, is_group, title, created_by, created_at)
              VALUES (@id, FALSE, '', @me, CURRENT_TIMESTAMP);
              INSERT INTO web_chat_members (conversation_id, username, joined_at)
              VALUES (@id, @me, CURRENT_TIMESTAMP), (@id, @other, CURRENT_TIMESTAMP);")
            .With("@id", id).With("@me", me).With("@other", other).ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<List<ChatConversationDto>> ReadConversations(NpgsqlConnection conn, string me, Guid? onlyId, bool admin)
    {
        var list = new List<ChatConversationDto>();
        var cmd = conn.Cmd(
            $@"SELECT c.id, c.is_group, c.title, COALESCE(mine.is_pinned, FALSE) AS is_pinned,
                      EXISTS (
                          SELECT 1 FROM web_chat_members sm
                          WHERE sm.conversation_id = c.id AND sm.username = @support
                      ) AS is_support_conversation,
                      o.username AS other_username,
                      au.full_name AS other_full_name,
                      {VerifiedExpr} AS other_verified,
                      {DiamondExpr} AS other_is_diamond,
                      oav.image_data_url AS other_avatar,
                      COALESCE(pres.is_online, FALSE) AS other_online,
                      pres.last_seen_utc AS other_last_seen,
                      lm.body AS last_body, lm.sender_username AS last_sender, lm.created_at AS last_at,
                      COALESCE(lm.is_removed, FALSE) AS last_removed,
                      lm.kind AS last_kind, lm.file_name AS last_file_name,
                      COALESCE(unr.cnt, 0) AS unread
               FROM web_chat_conversations c
               JOIN web_chat_members mine ON mine.conversation_id = c.id
                    AND (
                        mine.username = @me
                        OR (@admin = TRUE AND mine.username = @support
                            AND NOT EXISTS (
                                SELECT 1 FROM web_chat_members own
                                WHERE own.conversation_id = c.id AND own.username = @me
                            ))
                    )
               LEFT JOIN LATERAL (
                   SELECT m2.username FROM web_chat_members m2
                   WHERE m2.conversation_id = c.id
                     AND (
                         (@admin = TRUE AND mine.username = @support AND m2.username <> @support)
                         OR NOT (@admin = TRUE AND mine.username = @support) AND m2.username <> @me
                     )
                   ORDER BY m2.joined_at
                   LIMIT 1
               ) o ON TRUE
               LEFT JOIN app_users au ON au.username = o.username AND au.is_deleted = FALSE
               LEFT JOIN web_verified_users vu ON vu.username = o.username
               LEFT JOIN web_diamond_members dm ON dm.username = o.username
               LEFT JOIN LATERAL (
                   SELECT wa.image_data_url FROM web_user_avatars wa WHERE wa.user_id = au.id LIMIT 1
               ) oav ON TRUE
               LEFT JOIN LATERAL (
                   SELECT BOOL_OR(us.is_active = TRUE AND us.last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds') AS is_online,
                          MAX(us.last_seen) AS last_seen_utc
                   FROM user_sessions us WHERE us.username = o.username
               ) pres ON TRUE
                LEFT JOIN LATERAL (
                    SELECT mm.body, mm.sender_username, mm.created_at, mm.is_removed, mm.kind, mm.file_name
                    FROM web_chat_messages mm WHERE mm.conversation_id = c.id
                      AND NOT (mm.kind = 'voice' AND mm.has_blob = FALSE AND mm.is_removed = FALSE)
                    ORDER BY mm.created_at DESC, mm.id DESC
                   LIMIT 1
               ) lm ON TRUE
               LEFT JOIN LATERAL (
                   SELECT COUNT(*) AS cnt FROM web_chat_messages mm
                   WHERE mm.conversation_id = c.id
                     AND NOT (
                         (@admin = TRUE AND mine.username = @support AND mm.sender_username IN (@support, @me))
                         OR (NOT (@admin = TRUE AND mine.username = @support) AND mm.sender_username = @me)
                      )
                      AND mm.is_removed = FALSE
                      AND NOT (mm.kind = 'voice' AND mm.has_blob = FALSE)
                      AND (mine.last_read_at IS NULL OR mm.created_at > mine.last_read_at)
               ) unr ON TRUE
               WHERE COALESCE(mine.is_hidden, FALSE) = FALSE
                 AND mine.deleted_at IS NULL
                 {(onlyId is null ? "" : "AND c.id = @only")}
               -- NULLS LAST: PostgreSQL mặc định xếp NULL trước khi DESC, sẽ đẩy hội thoại chưa có
               -- tin nhắn (last_at NULL) lên đầu. App cần giữ các hội thoại đã có tin mới ở trước.
               ORDER BY COALESCE(mine.is_pinned, FALSE) DESC, lm.created_at DESC NULLS LAST")
            .With("@me", me)
            .With("@admin", admin)
            .With("@support", SupportUsername);
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
                : IsSupportUser(otherUsername)
                    ? SupportDisplayName
                    : (string.IsNullOrWhiteSpace(otherName) ? otherUsername : otherName);

            var lastBody = r.Str("last_body");
            var lastSender = r.Str("last_sender");
            var lastRemoved = r.Bool("last_removed");
            var lastKind = r.Str("last_kind");
            var lastFileName = r.Str("last_file_name");
            var supportConversation = r.Bool("is_support_conversation");
            var mineLast = string.Equals(lastSender, me, StringComparison.OrdinalIgnoreCase) ||
                           (admin && supportConversation && IsSupportUser(lastSender));
            string preview = "";
            if (lastRemoved)
                preview = (mineLast ? "Bạn: " : "") + "Tin nhắn đã được gỡ";
            else if (string.Equals(lastKind, "voice", StringComparison.Ordinal))
                preview = (mineLast ? "Bạn: " : "") + "🎤 Tin nhắn thoại";
            else if (string.Equals(lastKind, "file", StringComparison.Ordinal))
                preview = (mineLast ? "Bạn: " : "") + "📎 " + (string.IsNullOrWhiteSpace(lastFileName) ? "Tệp" : lastFileName);
            else if (!string.IsNullOrEmpty(lastBody))
                preview = mineLast ? $"Bạn: {lastBody}" : lastBody;

            list.Add(new ChatConversationDto(
                r.Guid("id"), isGroup, displayName,
                isGroup ? null : NullIfEmpty(otherUsername),
                isGroup ? null : NullIfEmpty(r.Str("other_avatar")),
                !isGroup && (IsSupportUser(otherUsername) || r.Bool("other_online")),
                !isGroup && (IsSupportUser(otherUsername) || r.Bool("other_verified")),
                !isGroup && r.Bool("other_is_diamond"),
                preview, r.DtNull("last_at"), r.Int("unread"),
                isGroup ? null : r.DtNull("other_last_seen"),
                r.Bool("is_pinned"),
                supportConversation));
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
            -- kind=text | file | voice. Voice có blob bền vững; file LAN chỉ giữ blob tạm khi cần.
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS kind varchar(16) NOT NULL DEFAULT 'text';
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS file_name varchar(260) NULL;
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS file_size bigint NULL;
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS file_mime varchar(160) NULL;
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS client_message_id varchar(128) NULL;
            -- Blob nằm trên đĩa, không vào DB. blob_expires_at chỉ áp dụng cho kind=file.
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS has_blob boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE web_chat_messages ADD COLUMN IF NOT EXISTS blob_expires_at timestamptz NULL;
            -- APK cũ ghi voice thành kind=file. Giữ nguyên kind để APK cũ còn render được, nhưng bỏ TTL;
            -- policy runtime cũng bảo vệ đúng mẫu recorder này khỏi xóa-sau-download và cleanup.
            UPDATE web_chat_messages
               SET blob_expires_at = NULL
             WHERE kind = 'file' AND is_removed = FALSE
               AND lower(COALESCE(file_name, '')) ~ '^ghi-am-[0-9]+\.(ogg|m4a)$';
            ALTER TABLE web_chat_members ADD COLUMN IF NOT EXISTS is_pinned boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE web_chat_members ADD COLUMN IF NOT EXISTS is_hidden boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE web_chat_members ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL;

            CREATE TABLE IF NOT EXISTS web_verified_users (
                username varchar(128) NOT NULL PRIMARY KEY,
                granted_by varchar(128) NOT NULL DEFAULT '',
                granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS web_diamond_members (
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

            CREATE TABLE IF NOT EXISTS web_chat_reports (
                id bigserial NOT NULL PRIMARY KEY,
                conversation_id uuid NOT NULL,
                reporter_username varchar(128) NOT NULL,
                reason varchar(500) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_web_chat_messages_conv ON web_chat_messages (conversation_id, created_at DESC, id DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_web_chat_voice_client_message
                ON web_chat_messages (conversation_id, sender_username, client_message_id)
                WHERE client_message_id IS NOT NULL AND is_removed = FALSE;
            CREATE INDEX IF NOT EXISTS ix_web_chat_members_user ON web_chat_members (username, conversation_id, last_read_at);
            CREATE INDEX IF NOT EXISTS ix_web_chat_members_list ON web_chat_members (username, is_hidden, deleted_at, is_pinned);
            CREATE INDEX IF NOT EXISTS ix_web_chat_reactions_msg ON web_chat_reactions (message_id);
            CREATE INDEX IF NOT EXISTS ix_web_chat_reports_conv ON web_chat_reports (conversation_id, created_at DESC);

            -- Nhật ký cuộc gọi NHỠ (bền vững): người nhận offline/chưa có token vẫn lấy được khi mở app.
            CREATE TABLE IF NOT EXISTS web_call_events (
                id bigserial NOT NULL PRIMARY KEY,
                to_username varchar(128) NOT NULL,
                from_username varchar(128) NOT NULL DEFAULT '',
                from_name varchar(200) NOT NULL DEFAULT '',
                call_id varchar(64) NOT NULL DEFAULT '',
                media varchar(16) NOT NULL DEFAULT 'audio',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                seen boolean NOT NULL DEFAULT FALSE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_web_call_events_target ON web_call_events (to_username, call_id);
            CREATE INDEX IF NOT EXISTS ix_web_call_events_unseen ON web_call_events (to_username, seen, created_at DESC);
            CREATE TABLE IF NOT EXISTS web_call_history (
                id bigserial PRIMARY KEY,
                username varchar(128) NOT NULL,
                peer_username varchar(128) NOT NULL,
                peer_name varchar(200) NOT NULL DEFAULT '',
                call_id varchar(64) NOT NULL,
                media varchar(16) NOT NULL DEFAULT 'audio',
                direction varchar(16) NOT NULL DEFAULT 'outgoing',
                outcome varchar(32) NOT NULL DEFAULT 'ended',
                started_at timestamptz NULL,
                ended_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                duration_seconds integer NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_web_call_history_user_call ON web_call_history(username, call_id);
            CREATE INDEX IF NOT EXISTS ix_web_call_history_user_time ON web_call_history(username, ended_at DESC);
            """)
            .ExecuteNonQueryAsync(ct);
    }
}
