using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Endpoints;

public static class FeedbackEndpoints
{
    public static void MapFeedback(this WebApplication app)
    {
        var g = app.MapGroup("/api/feedback").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal principal, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var admin = principal.IsAdmin();
            var me = principal.Username();
            var rows = new List<FeedbackDto>();

            await using (var r = await conn.Cmd(
                $"""
                SELECT f.id,
                       f.feedback_type,
                       f.reporter_username,
                       COALESCE(NULLIF(u.full_name, ''), f.reporter_username) AS reporter_name,
                       f.target_name,
                       f.reason,
                       f.conversation_id,
                       f.created_at
                FROM app_feedbacks f
                LEFT JOIN app_users u ON u.username = f.reporter_username
                WHERE {(admin ? "TRUE" : "f.reporter_username = @me")}
                ORDER BY f.created_at DESC, f.id DESC
                """)
                .With("@me", me)
                .ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var type = r.Str("feedback_type");
                    rows.Add(new FeedbackDto(
                        r.Long("id"),
                        type,
                        TypeLabel(type),
                        r.Str("reporter_username"),
                        r.Str("reporter_name"),
                        r.Str("target_name"),
                        r.Str("reason"),
                        r.IsDBNull(r.GetOrdinal("conversation_id")) ? null : r.GetGuid(r.GetOrdinal("conversation_id")),
                        r.Dt("created_at")));
                }
            }

            return Results.Ok(rows);
        });

        g.MapPost("/attendance", async (AttendanceFeedbackRequest req, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            var targetName = (req.TargetName ?? "").Trim();
            var reason = (req.Reason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(targetName))
                return Results.BadRequest(new { message = "Vui lòng nhập tên người bị chấm lỗi." });
            if (targetName.Length > 256) targetName = targetName[..256];
            if (reason.Length > 500) reason = reason[..500];

            await using var conn = await db.OpenAsync();
            var reporter = principal.Username();
            var conversationId = await ChatEndpoints.GetOrCreateDirect(conn, reporter, ChatEndpoints.SupportUsername);

            await conn.Cmd(
                """
                INSERT INTO app_feedbacks (feedback_type, reporter_username, target_name, reason, conversation_id, created_at)
                VALUES ('AttendanceIssue', @reporter, @target, @reason, @cid, CURRENT_TIMESTAMP)
                """)
                .With("@reporter", reporter)
                .With("@target", targetName)
                .With("@reason", reason)
                .With("@cid", conversationId)
                .ExecuteNonQueryAsync();

            var detail = string.IsNullOrWhiteSpace(reason) ? "Không ghi thêm nội dung." : reason;
            var userMessage = $"Phản hồi chấm công: {targetName}\n{detail}";
            var supportMessage = "Hỗ Trợ Người Dùng đã nhận phản hồi chấm công của bạn. Admin sẽ kiểm tra và phản hồi tại đây.";
            await conn.Cmd(
                """
                INSERT INTO web_chat_messages (conversation_id, sender_username, body, created_at)
                VALUES
                    (@cid, @reporter, @userMessage, CURRENT_TIMESTAMP),
                    (@cid, @support, @supportMessage, CURRENT_TIMESTAMP + INTERVAL '1 millisecond');

                UPDATE web_chat_members
                SET is_hidden = FALSE, deleted_at = NULL
                WHERE conversation_id = @cid;
                """)
                .With("@cid", conversationId)
                .With("@reporter", reporter)
                .With("@support", ChatEndpoints.SupportUsername)
                .With("@userMessage", userMessage)
                .With("@supportMessage", supportMessage)
                .ExecuteNonQueryAsync();

            await ChatEndpoints.NotifyChat(hub, conn, conversationId);
            await hub.Clients.All.SendAsync("changed", "feedback");
            return Results.NoContent();
        });

        g.MapPost("/{id:long}/resolve", async (long id, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!principal.IsAdmin()) return Results.Forbid();

            await using var conn = await db.OpenAsync();
            string reporter;
            string type;
            string target;
            long? legacyChatReportId;
            await using (var r = await conn.Cmd(
                """
                SELECT reporter_username, feedback_type, target_name, legacy_chat_report_id
                FROM app_feedbacks
                WHERE id = @id
                """)
                .With("@id", id)
                .ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound(new { message = "Phản hồi không còn tồn tại." });
                reporter = r.Str("reporter_username");
                type = r.Str("feedback_type");
                target = r.Str("target_name");
                legacyChatReportId = r.LongNull("legacy_chat_report_id");
            }

            await conn.Cmd("DELETE FROM app_feedbacks WHERE id = @id")
                .With("@id", id)
                .ExecuteNonQueryAsync();

            if (legacyChatReportId is long legacyId)
            {
                await conn.Cmd("DELETE FROM web_chat_reports WHERE id = @id")
                    .With("@id", legacyId)
                    .ExecuteNonQueryAsync();
            }

            var label = TypeLabel(type);
            var message = string.IsNullOrWhiteSpace(target)
                ? $"Phản hồi \"{label}\" của bạn đã được quản trị viên giải quyết."
                : $"Phản hồi \"{label}\" về {target} đã được quản trị viên giải quyết.";

            var supportConversationId = await ChatEndpoints.GetOrCreateDirect(conn, reporter, ChatEndpoints.SupportUsername);
            await conn.Cmd(
                """
                INSERT INTO web_chat_messages (conversation_id, sender_username, body, created_at)
                VALUES (@cid, @support, @message, CURRENT_TIMESTAMP);

                UPDATE web_chat_members
                SET is_hidden = FALSE, deleted_at = NULL
                WHERE conversation_id = @cid;
                """)
                .With("@cid", supportConversationId)
                .With("@support", ChatEndpoints.SupportUsername)
                .With("@message", message)
                .ExecuteNonQueryAsync();

            await ChatEndpoints.NotifyChat(hub, conn, supportConversationId);
            await hub.Clients.User(reporter).SendAsync("feedbackResolved", message);
            await hub.Clients.All.SendAsync("changed", "feedback");

            await db.RecordAudit(principal.Username(), "Giải quyết phản hồi", "Feedback", id.ToString(), label);
            return Results.NoContent();
        });
    }

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(
            """
            CREATE TABLE IF NOT EXISTS app_feedbacks (
                id bigserial NOT NULL PRIMARY KEY,
                feedback_type varchar(32) NOT NULL,
                reporter_username varchar(128) NOT NULL,
                target_name varchar(256) NOT NULL DEFAULT '',
                reason varchar(500) NOT NULL DEFAULT '',
                conversation_id uuid NULL,
                legacy_chat_report_id bigint NULL UNIQUE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_app_feedbacks_type_created ON app_feedbacks (feedback_type, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_app_feedbacks_reporter ON app_feedbacks (reporter_username, created_at DESC);

            INSERT INTO app_feedbacks
                (feedback_type, reporter_username, target_name, reason, conversation_id, legacy_chat_report_id, created_at)
            SELECT 'ChatReport', reporter_username, 'Cuộc trò chuyện', reason, conversation_id, id, created_at
            FROM web_chat_reports r
            WHERE NOT EXISTS (
                SELECT 1 FROM app_feedbacks f WHERE f.legacy_chat_report_id = r.id
            );
            """)
            .ExecuteNonQueryAsync(ct);
    }

    private static string TypeLabel(string type) => type switch
    {
        "ChatReport" => "Báo xấu trò chuyện",
        "AttendanceIssue" => "Báo lỗi chấm công",
        _ => "Phản hồi",
    };
}
