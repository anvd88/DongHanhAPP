using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.BuildingBlocks.Realtime;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Endpoints;

public static class FeedbackEndpoints
{
    public static void MapFeedback(this WebApplication app)
    {
        var g = app.MapGroup("/api/feedback").RequirePermission(Permissions.ChatAccess);

        g.MapGet("/", async (ClaimsPrincipal principal, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var admin = principal.Can(Permissions.UsersManage);
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
            return Results.NoContent();
        });

        g.MapPost("/{id:long}/resolve", async (long id, ClaimsPrincipal principal, Database db, IHubContext<ChangesHub> hub, BusinessEventWriter businessEvents) =>
        {
            if (!principal.Can(Permissions.UsersManage)) return Results.Forbid();

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            string reporter;
            string type;
            string target;
            long? legacyChatReportId;
            await using (var r = await conn.Cmd(
                """
                SELECT reporter_username, feedback_type, target_name, legacy_chat_report_id
                FROM app_feedbacks
                WHERE id = @id
                """, tx)
                .With("@id", id)
                .ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound(new { message = "Phản hồi không còn tồn tại." });
                reporter = r.Str("reporter_username");
                type = r.Str("feedback_type");
                target = r.Str("target_name");
                legacyChatReportId = r.LongNull("legacy_chat_report_id");
            }

            await conn.Cmd("DELETE FROM app_feedbacks WHERE id = @id", tx)
                .With("@id", id)
                .ExecuteNonQueryAsync();

            if (legacyChatReportId is long legacyId)
            {
                await conn.Cmd("DELETE FROM web_chat_reports WHERE id = @id", tx)
                    .With("@id", legacyId)
                    .ExecuteNonQueryAsync();
            }

            var label = TypeLabel(type);
            var message = string.IsNullOrWhiteSpace(target)
                ? $"Phản hồi \"{label}\" của bạn đã được quản trị viên giải quyết."
                : $"Phản hồi \"{label}\" về {target} đã được quản trị viên giải quyết.";

            var supportConversationId = await ChatEndpoints.GetOrCreateDirect(
                conn, reporter, ChatEndpoints.SupportUsername, tx);
            await conn.Cmd(
                """
                INSERT INTO web_chat_messages (conversation_id, sender_username, body, created_at)
                VALUES (@cid, @support, @message, CURRENT_TIMESTAMP);

                UPDATE web_chat_members
                SET is_hidden = FALSE, deleted_at = NULL
                WHERE conversation_id = @cid;
                """, tx)
                .With("@cid", supportConversationId)
                .With("@support", ChatEndpoints.SupportUsername)
                .With("@message", message)
                .ExecuteNonQueryAsync();

            await conn.RecordAudit(tx, principal.Username(), "Giải quyết phản hồi", "Feedback",
                id.ToString(), label);
            await businessEvents.WriteAsync(conn, tx, "feedback.resolved.v1",
                "portal.feedback.resolved.v1", "feedback", $"user:{reporter}", principal.Username(),
                id.ToString());
            await tx.CommitAsync();

            // Đây là thông báo chat support thật, nên vẫn dùng communication SignalR sau commit.
            await ChatEndpoints.NotifyChat(hub, conn, supportConversationId);
            return Results.NoContent();
        });

        g.MapGet("/surveys/open",async(ClaimsPrincipal u,Database db)=>{await using var c=await db.OpenAsync();var list=new List<object>();await using var r=await c.Cmd("""
          SELECT s.id,s.title,s.description,s.questions::text,s.closes_at,
          EXISTS(SELECT 1 FROM app_survey_responses x WHERE x.survey_id=s.id AND x.username=@u) answered
          FROM app_surveys s WHERE s.active=TRUE AND (s.closes_at IS NULL OR s.closes_at>CURRENT_TIMESTAMP) ORDER BY s.created_at DESC
          """).With("@u",u.Username()).ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new{id=r.Guid("id"),title=r.Str("title"),description=r.Str("description"),questions=ParseJson(r.Str("questions")),closesAt=r.DtNull("closes_at"),answered=r.Bool("answered")});return Results.Ok(list);});
        g.MapPost("/surveys/{id:guid}/responses",async(Guid id,SurveyResponseReq req,ClaimsPrincipal u,Database db)=>{await using var c=await db.OpenAsync();var exists=Convert.ToInt32(await c.Cmd("SELECT COUNT(*) FROM app_surveys WHERE id=@id AND active=TRUE AND (closes_at IS NULL OR closes_at>CURRENT_TIMESTAMP)").With("@id",id).ExecuteScalarAsync())>0;if(!exists)return Results.BadRequest(new{message="Khảo sát đã đóng."});try{await c.Cmd("INSERT INTO app_survey_responses(id,survey_id,username,answers) VALUES(@rid,@id,@u,@a::jsonb)").With("@rid",Guid.NewGuid()).With("@id",id).With("@u",u.Username()).With("@a",req.Answers.GetRawText()).ExecuteNonQueryAsync();}catch(Npgsql.PostgresException ex)when(ex.SqlState=="23505"){return Results.Conflict(new{message="Bạn đã trả lời khảo sát này."});}return Results.NoContent();});
        g.MapPost("/general",async(GeneralFeedbackReq req,ClaimsPrincipal u,Database db)=>{if(string.IsNullOrWhiteSpace(req.Message))return Results.BadRequest(new{message="Vui lòng nhập góp ý."});await using var c=await db.OpenAsync();var id=Guid.NewGuid();await c.Cmd("INSERT INTO app_general_feedback(id,username,anonymous,message,status) VALUES(@id,@u,@anon,@m,'open')").With("@id",id).With("@u",req.Anonymous?"":u.Username()).With("@anon",req.Anonymous).With("@m",req.Message.Trim()).ExecuteNonQueryAsync();return Results.Ok(new{id,status="open"});});
        g.MapGet("/general/mine",async(ClaimsPrincipal u,Database db)=>{await using var c=await db.OpenAsync();var list=new List<object>();await using var r=await c.Cmd("SELECT id,message,status,response,created_at FROM app_general_feedback WHERE username=@u AND anonymous=FALSE ORDER BY created_at DESC").With("@u",u.Username()).ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new{id=r.Guid("id"),message=r.Str("message"),status=r.Str("status"),response=r.Str("response"),createdAt=r.Dt("created_at")});return Results.Ok(list);});
        g.MapPost("/support",async(SupportTicketReq req,ClaimsPrincipal u,Database db)=>{if(string.IsNullOrWhiteSpace(req.Message))return Results.BadRequest(new{message="Vui lòng mô tả lỗi."});await using var c=await db.OpenAsync();var id=Guid.NewGuid();var code=$"HT{DateTime.UtcNow:yyMMdd}{Random.Shared.Next(1000,9999)}";await c.Cmd("INSERT INTO app_support_tickets(id,ticket_code,username,message,app_version,device_model,status) VALUES(@id,@c,@u,@m,@v,@d,'open')").With("@id",id).With("@c",code).With("@u",u.Username()).With("@m",req.Message.Trim()).With("@v",req.AppVersion??"").With("@d",req.DeviceModel??"").ExecuteNonQueryAsync();return Results.Ok(new{id,code,status="open"});});
        g.MapGet("/support/mine",async(ClaimsPrincipal u,Database db)=>{await using var c=await db.OpenAsync();var list=new List<object>();await using var r=await c.Cmd("SELECT id,ticket_code,message,status,response,created_at FROM app_support_tickets WHERE username=@u ORDER BY created_at DESC").With("@u",u.Username()).ExecuteReaderAsync();while(await r.ReadAsync())list.Add(new{id=r.Guid("id"),code=r.Str("ticket_code"),message=r.Str("message"),status=r.Str("status"),response=r.Str("response"),createdAt=r.Dt("created_at")});return Results.Ok(list);});
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
            CREATE TABLE IF NOT EXISTS app_surveys(id uuid PRIMARY KEY,title varchar(200) NOT NULL,description text NOT NULL DEFAULT '',questions jsonb NOT NULL DEFAULT '[]',active boolean NOT NULL DEFAULT TRUE,closes_at timestamptz NULL,created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS app_survey_responses(id uuid PRIMARY KEY,survey_id uuid NOT NULL REFERENCES app_surveys(id) ON DELETE CASCADE,username varchar(128) NOT NULL,answers jsonb NOT NULL,created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,UNIQUE(survey_id,username));
            CREATE TABLE IF NOT EXISTS app_general_feedback(id uuid PRIMARY KEY,username varchar(128) NOT NULL DEFAULT '',anonymous boolean NOT NULL DEFAULT FALSE,message text NOT NULL,status varchar(20) NOT NULL DEFAULT 'open',response text NOT NULL DEFAULT '',created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
            CREATE TABLE IF NOT EXISTS app_support_tickets(id uuid PRIMARY KEY,ticket_code varchar(20) NOT NULL UNIQUE,username varchar(128) NOT NULL,message text NOT NULL,app_version varchar(40) NOT NULL DEFAULT '',device_model varchar(160) NOT NULL DEFAULT '',status varchar(20) NOT NULL DEFAULT 'open',response text NOT NULL DEFAULT '',created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);

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
    private static System.Text.Json.JsonElement ParseJson(string value){try{return System.Text.Json.JsonDocument.Parse(value).RootElement.Clone();}catch{return System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone();}}
    public record SurveyResponseReq(System.Text.Json.JsonElement Answers);
    public record GeneralFeedbackReq(string? Message,bool Anonymous=false);
    public record SupportTicketReq(string? Message,string? AppVersion,string? DeviceModel);
}
