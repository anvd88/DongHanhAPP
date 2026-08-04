using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Khoản hoàn tiền phạt: khi một khiếu nại án phạt được duyệt (bác bỏ / giảm tiền) mà tiền đã bị trừ
/// vào lương, hệ thống sinh một khoản hoàn cho nhân viên. Khoản này phải được PHÒNG KẾ TOÁN duyệt
/// (chọn hình thức: cộng vào phiếu lương kỳ kế tiếp hoặc chi tiền mặt) trước khi trả cho nhân viên.
/// Ngoài quyền kỹ thuật tương ứng, người xử lý phải là nhân sự đang hoạt động thuộc phòng ban có cờ is_accounting.
/// </summary>
public static class PenaltyRefundEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS hr_penalty_refund_seq START 1;

            CREATE TABLE IF NOT EXISTS hr_penalty_refunds (
                id uuid PRIMARY KEY,
                refund_no varchar(20) NOT NULL DEFAULT '',
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                penalty_id uuid NULL,
                penalty_no varchar(20) NOT NULL DEFAULT '',
                appeal_request_no varchar(20) NOT NULL DEFAULT '',
                amount numeric(18,2) NOT NULL DEFAULT 0,
                reason text NOT NULL DEFAULT '',
                status varchar(20) NOT NULL DEFAULT 'PendingAccounting',
                payout_method varchar(16) NOT NULL DEFAULT '',
                applied_period varchar(7) NOT NULL DEFAULT '',
                created_by varchar(128) NOT NULL DEFAULT '',
                approved_by varchar(128) NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                decided_at timestamptz NULL
            );
            CREATE INDEX IF NOT EXISTS ix_hr_penalty_refunds_emp ON hr_penalty_refunds (employee_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_hr_penalty_refunds_status ON hr_penalty_refunds (status, created_at DESC);
            """).ExecuteNonQueryAsync(ct);
    }

    /// <summary>Sinh một khoản hoàn tiền phạt (trạng thái chờ kế toán duyệt). Dùng khi khiếu nại được duyệt.</summary>
    public static async Task CreateAsync(NpgsqlConnection conn, Guid employeeId, Guid? penaltyId,
        string penaltyNo, string appealRequestNo, decimal amount, string reason, string createdBy)
    {
        var no = $"HP{Convert.ToInt64(await conn.Cmd("SELECT nextval('hr_penalty_refund_seq')").ExecuteScalarAsync()):D5}";
        await conn.Cmd("""
            INSERT INTO hr_penalty_refunds (id, refund_no, employee_id, penalty_id, penalty_no, appeal_request_no,
                amount, reason, status, created_by)
            VALUES (@id, @no, @emp, @pid, @pno, @ano, @amount, @reason, 'PendingAccounting', @by)
            """)
            .With("@id", Guid.NewGuid()).With("@no", no).With("@emp", employeeId)
            .With("@pid", (object?)penaltyId ?? DBNull.Value).With("@pno", penaltyNo)
            .With("@ano", appealRequestNo).With("@amount", amount).With("@reason", reason ?? "")
            .With("@by", createdBy)
            .ExecuteNonQueryAsync();
    }

    /// <summary>Người dùng đang hoạt động và thuộc phòng ban kế toán hay không.</summary>
    public static async Task<bool> IsAccountingAsync(NpgsqlConnection conn, string username)
    {
        var v = await conn.Cmd("""
            SELECT EXISTS(
                SELECT 1 FROM hr_employees e
                JOIN hr_departments d ON d.id = e.department_id
                WHERE lower(e.username) = lower(@u)
                  AND e.status = 'Active'
                  AND d.is_accounting = true
            )
            """).With("@u", username).ExecuteScalarAsync();
        return v is bool b && b;
    }

    public static void MapPenaltyRefunds(this WebApplication app)
    {
        var g = app.MapGroup("/api/penalty-refunds").RequirePermission(Permissions.PenaltyRead);

        // scope: mine (mặc định – của tôi) | queue (phòng kế toán – đang chờ xử lý) | all (phòng kế toán – tất cả).
        g.MapGet("/", async (ClaimsPrincipal u, Database db, string? scope) =>
        {
            await using var conn = await db.OpenAsync();
            var me = u.Username();
            scope = string.IsNullOrWhiteSpace(scope) ? "mine" : scope.Trim().ToLowerInvariant();
            if (scope is not ("mine" or "queue" or "all"))
                return Results.BadRequest(new { message = "Phạm vi danh sách không hợp lệ." });
            if ((scope is "queue" or "all")
                && (!u.Can(Permissions.PayoutRead) || !await IsAccountingAsync(conn, me)))
                return Results.Forbid();

            var where = new List<string>();
            Guid myId = default;
            if (scope == "queue")
                where.Add("r.status IN ('PendingAccounting','Approved')");
            else if (scope == "all")
            {
                // không lọc
            }
            else
            {
                myId = await HrEndpoints.EnsureEmployeeForUser(conn, me);
                where.Add("r.employee_id = @myId");
            }

            var sql = $"""
                SELECT r.id, r.refund_no, r.employee_id, r.penalty_id, r.penalty_no, r.appeal_request_no,
                       r.amount, r.reason, r.status, r.payout_method, r.applied_period, r.created_by,
                       r.approved_by, r.note, r.created_at, r.decided_at,
                       e.full_name AS emp_name, e.employee_code
                FROM hr_penalty_refunds r JOIN hr_employees e ON e.id = r.employee_id
                {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
                ORDER BY r.created_at DESC
                """;
            var cmd = conn.Cmd(sql);
            if (where.Contains("r.employee_id = @myId")) cmd.With("@myId", myId);

            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(ReadRefund(r));
            return Results.Ok(list);
        });

        // Kế toán duyệt: chọn hình thức chi trả (payroll = cộng vào lương kỳ sau | cash = chi tiền mặt).
        g.MapPost("/{id:guid}/approve", async (Guid id, ApproveRefundReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingAsync(conn, u.Username())) return Results.Forbid();
            var payout = req.PayoutMethod == "cash" ? "cash" : "payroll";
            var n = await conn.Cmd("""
                UPDATE hr_penalty_refunds SET status='Approved', payout_method=@pm, approved_by=@by,
                    note=@note, decided_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status='PendingAccounting'
                """)
                .With("@id", id).With("@pm", payout).With("@by", u.Username()).With("@note", req.Note ?? "")
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Khoản hoàn không tồn tại hoặc đã được xử lý." });
            await SignalRefund(db, u, id, "Duyệt hoàn tiền phạt");
            return Results.NoContent();
        }).RequirePermission(Permissions.PayoutApprove);

        // Kế toán từ chối khoản hoàn.
        g.MapPost("/{id:guid}/reject", async (Guid id, ApproveRefundReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingAsync(conn, u.Username())) return Results.Forbid();
            var n = await conn.Cmd("""
                UPDATE hr_penalty_refunds SET status='Rejected', approved_by=@by, note=@note,
                    decided_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status='PendingAccounting'
                """)
                .With("@id", id).With("@by", u.Username()).With("@note", req.Note ?? "")
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Khoản hoàn không tồn tại hoặc đã được xử lý." });
            await SignalRefund(db, u, id, "Từ chối hoàn tiền phạt");
            return Results.NoContent();
        }).RequirePermission(Permissions.PayoutApprove);

        // Đánh dấu đã chi tiền mặt (chỉ với khoản đã duyệt hình thức cash).
        g.MapPost("/{id:guid}/mark-paid", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingAsync(conn, u.Username())) return Results.Forbid();
            var n = await conn.Cmd("""
                UPDATE hr_penalty_refunds SET status='Paid', decided_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status='Approved' AND payout_method='cash'
                """).With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Chỉ đánh dấu đã chi cho khoản đã duyệt hình thức tiền mặt." });
            await SignalRefund(db, u, id, "Chi tiền mặt hoàn phạt");
            return Results.NoContent();
        }).RequirePermission(Permissions.PayoutPay);
    }

    private static object ReadRefund(NpgsqlDataReader r) => new
    {
        id = r.Guid("id"),
        refundNo = r.Str("refund_no"),
        employeeId = r.Guid("employee_id"),
        employeeName = r.Str("emp_name"),
        employeeCode = r.Str("employee_code"),
        penaltyNo = r.Str("penalty_no"),
        appealRequestNo = r.Str("appeal_request_no"),
        amount = r.Dec("amount"),
        reason = r.Str("reason"),
        status = r.Str("status"),
        payoutMethod = r.Str("payout_method"),
        appliedPeriod = r.Str("applied_period"),
        createdBy = r.Str("created_by"),
        approvedBy = r.Str("approved_by"),
        note = r.Str("note"),
        createdAt = r.Dt("created_at"),
        decidedAt = r.DtNull("decided_at"),
    };

    /// <summary>
    /// Chỉ ghi audit. Trigger trên hr_penalty_refunds tự phát scope 'hr' sau khi commit.
    /// </summary>
    private static async Task SignalRefund(Database db, ClaimsPrincipal u, Guid refundId, string action)
        => await db.RecordAudit(u.Username(), action, "PenaltyRefund", refundId.ToString(), $"{action} (web).");

    public record ApproveRefundReq(string? PayoutMethod, string? Note);
}
