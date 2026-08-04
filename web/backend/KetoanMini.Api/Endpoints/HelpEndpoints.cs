using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Trung tâm trợ giúp — Đợt 7, nhiệm vụ 22 (phần FAQ + tình trạng dịch vụ). Việc tiếp nhận báo lỗi
/// (mã yêu cầu, phiên bản app, loại máy, trạng thái xử lý) đã có ở FeedbackEndpoints (app_support_tickets);
/// đây bổ sung: kho câu hỏi thường gặp (Admin biên tập, mọi người xem) và endpoint kiểm tra tình trạng dịch vụ.
/// </summary>
public static class HelpEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS help_faqs (
                id uuid PRIMARY KEY,
                category varchar(80) NOT NULL DEFAULT '',
                question text NOT NULL DEFAULT '',
                answer text NOT NULL DEFAULT '',
                order_no int NOT NULL DEFAULT 0,
                is_published boolean NOT NULL DEFAULT TRUE,
                updated_by varchar(128) NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_help_faqs_order ON help_faqs (is_published, category, order_no);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapHelp(this WebApplication app)
    {
        var g = app.MapGroup("/api/help").RequirePermission(Permissions.PortalRead);

        // FAQ: Admin thấy tất cả; người khác chỉ thấy mục đã xuất bản.
        g.MapGet("/faqs", async (ClaimsPrincipal u, Database db) =>
        {
            var admin = u.Can(Permissions.PortalManage);
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd($"""
                SELECT id, category, question, answer, order_no, is_published
                FROM help_faqs {(admin ? "" : "WHERE is_published = TRUE")}
                ORDER BY category, order_no, updated_at DESC
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"), category = r.Str("category"), question = r.Str("question"),
                    answer = r.Str("answer"), orderNo = r.Int("order_no"), isPublished = r.Bool("is_published"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/faqs", async (FaqReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.PortalManage)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Question)) return Results.BadRequest(new { message = "Thiếu câu hỏi." });
            var id = Guid.NewGuid();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                INSERT INTO help_faqs (id, category, question, answer, order_no, is_published, updated_by)
                VALUES (@id, @cat, @q, @a, @ord, @pub, @by)
                """)
                .With("@id", id).With("@cat", req.Category ?? "").With("@q", req.Question.Trim())
                .With("@a", req.Answer ?? "").With("@ord", req.OrderNo ?? 0).With("@pub", req.IsPublished ?? true)
                .With("@by", u.Username()).ExecuteNonQueryAsync();
            await db.RecordAudit(u.Username(), "Tạo FAQ", "Faq", id.ToString(), req.Question.Trim());
            return Results.Ok(new { id });
        });

        g.MapPut("/faqs/{id:guid}", async (Guid id, FaqReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.PortalManage)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE help_faqs SET category=@cat, question=@q, answer=@a, order_no=@ord, is_published=@pub,
                    updated_by=@by, updated_at=CURRENT_TIMESTAMP WHERE id=@id
                """)
                .With("@id", id).With("@cat", req.Category ?? "").With("@q", (req.Question ?? "").Trim())
                .With("@a", req.Answer ?? "").With("@ord", req.OrderNo ?? 0).With("@pub", req.IsPublished ?? true)
                .With("@by", u.Username()).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Sửa FAQ", "Faq", id.ToString(), "");
            return Results.NoContent();
        });

        g.MapDelete("/faqs/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.PortalManage)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM help_faqs WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Xóa FAQ", "Faq", id.ToString(), "");
            return Results.NoContent();
        });

        // Tình trạng dịch vụ cho màn hình trợ giúp (kiểm tra kết nối DB). Có auth để tránh lộ ra ngoài.
        g.MapGet("/status", async (Database db) =>
        {
            var dbOk = true;
            try { await using var conn = await db.OpenAsync(); }
            catch { dbOk = false; }
            return Results.Ok(new { db = dbOk ? "ok" : "error", serverTime = DateTime.UtcNow });
        });
    }

    public record FaqReq(string? Category, string? Question, string? Answer, int? OrderNo, bool? IsPublished);
}
