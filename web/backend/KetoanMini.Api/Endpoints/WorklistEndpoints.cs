using System.Security.Claims;
using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Trung tâm tác vụ ("Việc cần làm") — Đợt 1, nhiệm vụ 3. Tổng hợp MỌI việc người dùng đang đăng nhập
/// cần xử lý về một nguồn duy nhất cho màn hình worklist của app/web:
///  • Đơn chờ chính mình duyệt (kèm hạn xử lý SLA để tính ưu tiên/quá hạn).
///  • Phiếu lương đã phát hành nhưng chưa xác nhận.
///  • Giấy tờ (CCCD, chứng chỉ…) sắp hết hạn.
///  • Hợp đồng sắp hết hạn.
///  • Thông báo bắt buộc (announcement mức cảnh báo/nghiêm trọng).
/// Mỗi tác vụ có KHÓA ổn định (kind:id) nên tính lại mỗi lần gọi mà KHÔNG tạo trùng. Realtime: client
/// đăng ký scope "hr"/"data"; các thao tác duyệt/phát lương/thêm hồ sơ vốn đã phát "changed" nên
/// worklist tự làm mới khi tác vụ phát sinh hoặc hoàn thành — không cần đường tín hiệu riêng.
/// </summary>
public static class WorklistEndpoints
{
    // Ngưỡng cảnh báo (ngày) cho giấy tờ / hợp đồng sắp hết hạn.
    private const int DocWindowDays = 30;
    private const int ContractWindowDays = 45;

    public static void MapWorklist(this WebApplication app)
    {
        // Ai đăng nhập cũng lấy được worklist CỦA CHÍNH MÌNH.
        var g = app.MapGroup("/api/worklist").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            var admin = u.IsAdmin();
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var items = new List<WorklistItem>();

            await using var conn = await db.OpenAsync();

            // 1) Đơn chờ chính mình duyệt (dùng lại đúng điều kiện của inbox-count).
            await using (var r = await conn.Cmd("""
                SELECT r.id, r.request_no, r.title, r.req_type, r.due_at
                FROM hr_requests r
                WHERE r.status='Pending' AND EXISTS (
                    SELECT 1 FROM hr_request_approvals a
                    WHERE a.request_id=r.id AND a.step_no=r.current_step AND a.status='Pending'
                      AND (a.approver_username=@me OR (a.approver_role='Admin' AND @admin))
                )
                ORDER BY r.due_at NULLS LAST, r.created_at
                LIMIT 100
                """).With("@me", me).With("@admin", admin).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var no = r.Str("request_no");
                    var title = r.Str("title");
                    var due = r.DtNull("due_at");
                    items.Add(new WorklistItem(
                        $"approval:{r.Guid("id")}", "approval", "Đơn chờ bạn duyệt",
                        string.IsNullOrWhiteSpace(title) ? $"Đơn {no}" : $"{no} · {title}",
                        DuePriority(due, now), due, "/duyet"));
                }
            }

            // 2) Phiếu lương đã phát hành nhưng chưa xác nhận (của chính mình).
            await using (var r = await conn.Cmd("""
                SELECT p.id, p.period
                FROM hr_payslips p JOIN hr_employees e ON e.id=p.employee_id
                WHERE e.username=@me AND p.published=TRUE AND p.acknowledged_at IS NULL
                ORDER BY p.period DESC LIMIT 24
                """).With("@me", me).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    items.Add(new WorklistItem(
                        $"payslip:{r.Guid("id")}", "payslip", "Phiếu lương chưa xác nhận",
                        $"Kỳ lương {r.Str("period")}", "medium", null, "/phieu-luong"));
            }

            // 3) Giấy tờ sắp hết hạn (của chính mình).
            await using (var r = await conn.Cmd("""
                SELECT d.id, d.title, d.doc_type, d.expires_at
                FROM hr_documents d JOIN hr_employees e ON e.id=d.employee_id
                WHERE e.username=@me AND d.expires_at IS NOT NULL AND d.expires_at <= CURRENT_DATE + @win::int
                ORDER BY d.expires_at LIMIT 50
                """).With("@me", me).With("@win", DocWindowDays).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var exp = r.DateOnly("expires_at");
                    var title = r.Str("title");
                    items.Add(new WorklistItem(
                        $"doc:{r.Guid("id")}", "document", "Giấy tờ sắp hết hạn",
                        $"{(string.IsNullOrWhiteSpace(title) ? r.Str("doc_type") : title)} · hết hạn {exp:dd/MM/yyyy}",
                        ExpiryPriority(exp, today), exp.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), "/ho-so"));
                }
            }

            // 4) Hợp đồng sắp hết hạn (của chính mình).
            await using (var r = await conn.Cmd("""
                SELECT c.id, c.contract_no, c.contract_type, c.end_date
                FROM hr_contracts c JOIN hr_employees e ON e.id=c.employee_id
                WHERE e.username=@me AND c.end_date IS NOT NULL AND c.end_date <= CURRENT_DATE + @win::int
                  AND c.status <> 'Ended'
                ORDER BY c.end_date LIMIT 20
                """).With("@me", me).With("@win", ContractWindowDays).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var end = r.DateOnly("end_date");
                    var no = r.Str("contract_no");
                    items.Add(new WorklistItem(
                        $"contract:{r.Guid("id")}", "contract", "Hợp đồng sắp hết hạn",
                        $"HĐ {(string.IsNullOrWhiteSpace(no) ? r.Str("contract_type") : no)} · hết hạn {end:dd/MM/yyyy}",
                        ExpiryPriority(end, today), end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), "/ho-so"));
                }
            }

            // 5) Thông báo bắt buộc (announcement mức warning/critical).
            await using (var r = await conn.Cmd(
                "SELECT announcement, announcement_level FROM app_config WHERE id=1").ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    var msg = r.Str("announcement");
                    var level = r.Str("announcement_level");
                    if (!string.IsNullOrWhiteSpace(msg) && (level == "warning" || level == "critical"))
                        items.Add(new WorklistItem("notice", "notice", "Thông báo quan trọng",
                            msg.Length > 160 ? msg[..160] + "…" : msg,
                            level == "critical" ? "high" : "medium", null, "/"));
                }
            }

            var summary = new WorklistSummary(
                items.Count,
                items.Count(i => i.Kind == "approval"),
                items.Count(i => i.Kind == "payslip"),
                items.Count(i => i.Kind == "document"),
                items.Count(i => i.Kind == "contract"),
                items.Count(i => i.Kind == "notice"),
                items.Count(i => i.Priority == "high"));

            // Ưu tiên cao lên trước; trong cùng mức thì việc có hạn sớm hơn lên trước.
            var order = new Dictionary<string, int> { ["high"] = 0, ["medium"] = 1, ["normal"] = 2 };
            var sorted = items
                .OrderBy(i => order.GetValueOrDefault(i.Priority, 2))
                .ThenBy(i => i.DueAt ?? DateTime.MaxValue)
                .ToList();

            return Results.Ok(new WorklistResult(sorted, summary));
        });
    }

    /// <summary>Ưu tiên theo hạn xử lý (SLA): quá hạn → cao; trong 24h → vừa; còn lại → thường.</summary>
    private static string DuePriority(DateTime? dueUtc, DateTime now)
    {
        if (dueUtc is null) return "normal";
        if (dueUtc.Value <= now) return "high";
        if (dueUtc.Value <= now.AddDays(1)) return "medium";
        return "normal";
    }

    /// <summary>Ưu tiên theo ngày hết hạn: đã/≤7 ngày → cao; ≤30 ngày → vừa; xa hơn → thường.</summary>
    private static string ExpiryPriority(DateOnly expiry, DateOnly today)
    {
        var days = expiry.DayNumber - today.DayNumber;
        if (days <= 7) return "high";
        if (days <= 30) return "medium";
        return "normal";
    }

    public record WorklistItem(string Key, string Kind, string Title, string Description, string Priority, DateTime? DueAt, string Route);
    public record WorklistSummary(int Total, int Approvals, int Payslips, int Documents, int Contracts, int Notices, int Overdue);
    public record WorklistResult(IReadOnlyList<WorklistItem> Items, WorklistSummary Summary);
}
