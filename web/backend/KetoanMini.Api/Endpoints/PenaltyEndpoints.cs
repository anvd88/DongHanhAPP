using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Phạt / kỷ luật nhân sự: quản trị (HR/Admin) lập quyết định phạt cho nhân viên (nhắc nhở, cảnh cáo,
/// phạt tiền…), nhân viên xem lại các lần bị phạt của chính mình. Mỗi bản ghi gắn với hr_employees.id
/// và có thể kèm số tiền phạt để đối chiếu khấu trừ lương. Bảng tự tạo lúc khởi động như các module khác.
/// </summary>
public static class PenaltyEndpoints
{
    /// <summary>Hình thức phạt: type → nhãn hiển thị. Frontend dựng danh sách chọn từ đây.</summary>
    public static readonly (string Type, string Label)[] Types =
    {
        ("reminder", "Nhắc nhở"),
        ("warning", "Cảnh cáo"),
        ("fine", "Phạt tiền"),
        ("suspension", "Đình chỉ"),
        ("other", "Khác"),
    };

    private static string TypeLabel(string type) =>
        Array.Find(Types, t => t.Type == type).Label ?? type;

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS hr_penalty_seq START 1;

            CREATE TABLE IF NOT EXISTS hr_penalties (
                id uuid PRIMARY KEY,
                penalty_no varchar(20) NOT NULL DEFAULT '',
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                penalty_type varchar(32) NOT NULL DEFAULT 'reminder',
                penalty_date date NOT NULL DEFAULT CURRENT_DATE,
                amount numeric(18,2) NOT NULL DEFAULT 0,
                installments integer NOT NULL DEFAULT 1,
                start_period varchar(7) NOT NULL DEFAULT '',
                reason text NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                status varchar(20) NOT NULL DEFAULT 'Active',
                created_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            ALTER TABLE hr_penalties ADD COLUMN IF NOT EXISTS installments integer NOT NULL DEFAULT 1;
            ALTER TABLE hr_penalties ADD COLUMN IF NOT EXISTS start_period varchar(7) NOT NULL DEFAULT '';
            CREATE INDEX IF NOT EXISTS ix_hr_penalties_emp ON hr_penalties (employee_id, penalty_date DESC);
            CREATE INDEX IF NOT EXISTS ix_hr_penalties_status ON hr_penalties (status, penalty_date DESC);
            """).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Chia số tiền phạt thành lịch trừ theo tháng: mỗi tháng (trừ tháng cuối) trừ phần đã LÀM TRÒN LÊN
    /// đến hàng chục của (tổng / số tháng); THÁNG CUỐI trừ nốt phần còn lại. Nếu làm tròn khiến trả hết
    /// sớm, lịch tự dừng (không sinh số âm). Trả mảng số tiền theo từng tháng (độ dài ≤ số tháng).
    /// </summary>
    public static decimal[] BuildSchedule(decimal amount, int installments)
    {
        if (amount <= 0) return Array.Empty<decimal>();
        if (installments < 1) installments = 1;
        var monthly = Math.Ceiling(amount / installments / 10m) * 10m;
        var result = new List<decimal>();
        var remaining = amount;
        for (var i = 1; i <= installments && remaining > 0; i++)
        {
            var take = i == installments ? remaining : Math.Min(monthly, remaining);
            result.Add(take);
            remaining -= take;
        }
        return result.ToArray();
    }

    public sealed record PenaltyDeductionLine(string PenaltyNo, string Reason, decimal Amount,
        int Installments, int InstallmentNo, decimal MonthAmount);

    /// <summary>Tổng tiền phạt cần khấu trừ + chi tiết từng đợt cho một nhân viên trong một kỳ lương.</summary>
    public static async Task<(decimal Total, List<PenaltyDeductionLine> Items)> ComputeDeductionsAsync(
        NpgsqlConnection conn, Guid employeeId, string period)
    {
        var items = new List<PenaltyDeductionLine>();
        decimal total = 0;
        await using var r = await conn.Cmd("""
            SELECT penalty_no, amount, installments, start_period, reason
            FROM hr_penalties
            WHERE employee_id=@emp AND status='Active' AND penalty_type='fine' AND amount > 0
            ORDER BY penalty_date
            """).With("@emp", employeeId).ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var amount = r.Dec("amount");
            var inst = r.Int("installments");
            var schedule = BuildSchedule(amount, inst);
            var off = MonthOffset(r.Str("start_period"), period);
            if (off is null || off < 0 || off >= schedule.Length) continue;
            var due = schedule[off.Value];
            if (due <= 0) continue;
            total += due;
            items.Add(new PenaltyDeductionLine(r.Str("penalty_no"), r.Str("reason"), amount, inst, off.Value + 1, due));
        }
        return (total, items);
    }

    /// <summary>Số tháng chênh lệch giữa hai kỳ "yyyy-MM" (target - start); null nếu kỳ không hợp lệ.</summary>
    private static int? MonthOffset(string startPeriod, string targetPeriod)
    {
        if (!TryPeriod(startPeriod, out var sy, out var sm) || !TryPeriod(targetPeriod, out var ty, out var tm))
            return null;
        return (ty * 12 + tm) - (sy * 12 + sm);
    }

    private static bool TryPeriod(string period, out int year, out int month)
    {
        year = month = 0;
        if (string.IsNullOrWhiteSpace(period)) return false;
        var parts = period.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out year) && int.TryParse(parts[1], out month)
            && month is >= 1 and <= 12;
    }

    public static void MapPenalties(this WebApplication app)
    {
        var g = app.MapGroup("/api/penalties").RequireAuthorization();

        g.MapGet("/types", () => Results.Ok(Array.ConvertAll(Types, t => new { type = t.Type, label = t.Label })));

        // Tổng tiền phạt cần khấu trừ cho một nhân viên trong một kỳ lương (yyyy-MM) + chi tiết từng đợt.
        // Dùng khi lập phiếu lương để tự cộng vào khấu trừ.
        g.MapGet("/deductions", async (ClaimsPrincipal u, Database db, Guid employeeId, string period) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (employeeId == Guid.Empty || string.IsNullOrWhiteSpace(period))
                return Results.BadRequest(new { message = "Thiếu nhân viên hoặc kỳ lương." });

            await using var conn = await db.OpenAsync();
            var (total, items) = await ComputeDeductionsAsync(conn, employeeId, period);
            return Results.Ok(new
            {
                total,
                items = items.ConvertAll(it => new
                {
                    penaltyNo = it.PenaltyNo,
                    reason = it.Reason,
                    amount = it.Amount,
                    installments = it.Installments,
                    installmentNo = it.InstallmentNo,
                    monthAmount = it.MonthAmount,
                }),
            });
        });

        // scope: mine (mặc định – chỉ của tôi) | all (admin – toàn công ty, lọc theo employeeId/month tùy chọn).
        g.MapGet("/", async (ClaimsPrincipal u, Database db, string? scope, Guid? employeeId, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var admin = u.IsAdmin();
            scope ??= admin ? "all" : "mine";

            var isAll = scope == "all" && admin;
            var where = new List<string>();
            Guid myId = default;
            if (isAll)
            {
                if (employeeId is not null) where.Add("p.employee_id=@emp");
                if (!string.IsNullOrWhiteSpace(month)) where.Add("to_char(p.penalty_date, 'YYYY-MM')=@month");
            }
            else
            {
                // Nhân viên chỉ xem của chính mình.
                myId = await HrEndpoints.EnsureEmployeeForUser(conn, u.Username());
                where.Add("p.employee_id=@myId");
            }

            var sql = $"""
                SELECT p.id, p.penalty_no, p.employee_id, p.penalty_type, p.penalty_date, p.amount,
                       p.installments, p.start_period, p.reason, p.note, p.status, p.created_by, p.created_at,
                       e.full_name AS emp_name, e.employee_code
                FROM hr_penalties p JOIN hr_employees e ON e.id=p.employee_id
                {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
                ORDER BY p.penalty_date DESC, p.created_at DESC
                """;
            var cmd = conn.Cmd(sql);
            if (isAll)
            {
                if (employeeId is not null) cmd.With("@emp", employeeId.Value);
                if (!string.IsNullOrWhiteSpace(month)) cmd.With("@month", month.Trim());
            }
            else
            {
                cmd.With("@myId", myId);
            }

            var recs = new List<PenaltyRec>();
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    recs.Add(ReadPenaltyRec(r));

            // Tiến trình khấu trừ (chỉ phạt tiền còn hiệu lực): dựa trên các kỳ lương ĐÃ phát hành
            // (hr_payslips.published) của nhân viên — mỗi kỳ có phiếu lương coi như đã trừ đợt tương ứng.
            var paidPeriods = await LoadPublishedPeriods(conn, recs.ConvertAll(p => p.EmployeeId));

            var list = recs.ConvertAll(p => ProjectPenalty(p, paidPeriods));
            return Results.Ok(list);
        });

        g.MapPost("/", async (SavePenaltyReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (req.EmployeeId == Guid.Empty) return Results.BadRequest(new { message = "Vui lòng chọn nhân viên." });
            if (string.IsNullOrWhiteSpace(req.Reason)) return Results.BadRequest(new { message = "Vui lòng nhập lý do phạt." });

            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            var no = $"PH{Convert.ToInt64(await conn.Cmd("SELECT nextval('hr_penalty_seq')").ExecuteScalarAsync()):D5}";
            await conn.Cmd("""
                INSERT INTO hr_penalties (id, penalty_no, employee_id, penalty_type, penalty_date, amount, installments, start_period, reason, note, status, created_by)
                VALUES (@id, @no, @emp, @type, @date, @amount, @inst, @start, @reason, @note, 'Active', @by)
                """)
                .With("@id", id).With("@no", no).With("@emp", req.EmployeeId)
                .With("@type", req.PenaltyType ?? "reminder")
                .With("@date", (object?)req.PenaltyDate ?? DBNull.Value)
                .With("@amount", req.Amount)
                .With("@inst", NormInstallments(req.Installments))
                .With("@start", NormStartPeriod(req.StartPeriod, req.PenaltyDate))
                .With("@reason", req.Reason!.Trim()).With("@note", req.Note ?? "")
                .With("@by", u.Username())
                .ExecuteNonQueryAsync();

            await SignalEmployee(hub, db, conn, u, req.EmployeeId, "Lập quyết định phạt", no);
            return Results.Ok(new { id, penaltyNo = no });
        });

        g.MapPut("/{id:guid}", async (Guid id, SavePenaltyReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_penalties SET penalty_type=@type, penalty_date=@date, amount=@amount,
                    installments=@inst, start_period=@start, reason=@reason, note=@note, status=@status,
                    updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """)
                .With("@id", id).With("@type", req.PenaltyType ?? "reminder")
                .With("@date", (object?)req.PenaltyDate ?? DBNull.Value)
                .With("@amount", req.Amount)
                .With("@inst", NormInstallments(req.Installments))
                .With("@start", NormStartPeriod(req.StartPeriod, req.PenaltyDate))
                .With("@reason", (req.Reason ?? "").Trim())
                .With("@note", req.Note ?? "").With("@status", req.Status ?? "Active")
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await SignalEmployee(hub, db, conn, u, req.EmployeeId, "Cập nhật quyết định phạt", id.ToString());
            return Results.NoContent();
        });

        // Miễn / hủy hiệu lực phạt (không xóa để giữ lịch sử).
        g.MapPost("/{id:guid}/waive", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE hr_penalties SET status='Waived', updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Miễn phạt", id.ToString());
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_penalties WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa quyết định phạt", id.ToString());
            return Results.NoContent();
        });
    }

    private sealed record PenaltyRec(Guid Id, string PenaltyNo, Guid EmployeeId, string EmployeeName,
        string EmployeeCode, string PenaltyType, DateOnly? PenaltyDate, decimal Amount, int Installments,
        string StartPeriod, string Reason, string Note, string Status, string CreatedBy, DateTime CreatedAt);

    private static PenaltyRec ReadPenaltyRec(NpgsqlDataReader r) => new(
        r.Guid("id"), r.Str("penalty_no"), r.Guid("employee_id"), r.Str("emp_name"), r.Str("employee_code"),
        r.Str("penalty_type"),
        r.IsDBNull(r.GetOrdinal("penalty_date")) ? null : r.DateOnly("penalty_date"),
        r.Dec("amount"), r.Int("installments"), r.Str("start_period"), r.Str("reason"), r.Str("note"),
        r.Str("status"), r.Str("created_by"), r.Dt("created_at"));

    private static object ProjectPenalty(PenaltyRec p, Dictionary<Guid, HashSet<string>> paidPeriods) => new
    {
        id = p.Id,
        penaltyNo = p.PenaltyNo,
        employeeId = p.EmployeeId,
        employeeName = p.EmployeeName,
        employeeCode = p.EmployeeCode,
        penaltyType = p.PenaltyType,
        penaltyTypeLabel = TypeLabel(p.PenaltyType),
        penaltyDate = p.PenaltyDate,
        amount = p.Amount,
        installments = p.Installments,
        startPeriod = p.StartPeriod,
        reason = p.Reason,
        note = p.Note,
        status = p.Status,
        createdBy = p.CreatedBy,
        createdAt = p.CreatedAt,
        progress = BuildProgress(p, paidPeriods),
    };

    /// <summary>
    /// Tiến trình khấu trừ của một quyết định phạt tiền còn hiệu lực: mỗi kỳ trong lịch trừ mà nhân viên
    /// ĐÃ có phiếu lương phát hành coi như đã trừ. Trả null cho phạt không phải "fine", đã miễn, hoặc ≤ 0₫.
    /// </summary>
    private static object? BuildProgress(PenaltyRec p, Dictionary<Guid, HashSet<string>> paidPeriods)
    {
        if (p.PenaltyType != "fine" || p.Status != "Active" || p.Amount <= 0) return null;
        var schedule = BuildSchedule(p.Amount, p.Installments);
        if (schedule.Length == 0) return null;
        paidPeriods.TryGetValue(p.EmployeeId, out var paid);

        decimal deducted = 0;
        var paidMonths = 0;
        string? nextPeriod = null;
        decimal nextAmount = 0;
        var periods = new List<object>(schedule.Length);
        for (var i = 0; i < schedule.Length; i++)
        {
            var period = AddPeriod(p.StartPeriod, i);
            var isPaid = paid is not null && paid.Contains(period);
            if (isPaid) { deducted += schedule[i]; paidMonths++; }
            else if (nextPeriod is null) { nextPeriod = period; nextAmount = schedule[i]; }
            periods.Add(new { period, amount = schedule[i], paid = isPaid, installmentNo = i + 1 });
        }

        return new
        {
            total = p.Amount,
            deducted,
            remaining = p.Amount - deducted,
            totalMonths = schedule.Length,
            paidMonths,
            remainingMonths = schedule.Length - paidMonths,
            nextPeriod,
            nextAmount,
            periods,
        };
    }

    /// <summary>
    /// Tổng tiền phạt của MỘT quyết định đã thực sự bị trừ vào lương = tổng các đợt (theo lịch) rơi vào
    /// các kỳ mà nhân viên ĐÃ có phiếu lương phát hành. Dùng khi khiếu nại được duyệt để tính tiền hoàn.
    /// </summary>
    public static async Task<decimal> ComputeDeductedForPenaltyAsync(NpgsqlConnection conn, Guid employeeId,
        string startPeriod, decimal amount, int installments)
    {
        if (amount <= 0) return 0;
        var schedule = BuildSchedule(amount, installments);
        if (schedule.Length == 0) return 0;
        var paidMap = await LoadPublishedPeriods(conn, new List<Guid> { employeeId });
        paidMap.TryGetValue(employeeId, out var paid);
        if (paid is null) return 0;
        decimal deducted = 0;
        for (var i = 0; i < schedule.Length; i++)
            if (paid.Contains(AddPeriod(startPeriod, i))) deducted += schedule[i];
        return deducted;
    }

    /// <summary>Các kỳ lương ĐÃ phát hành của từng nhân viên (employee_id → tập "yyyy-MM").</summary>
    private static async Task<Dictionary<Guid, HashSet<string>>> LoadPublishedPeriods(NpgsqlConnection conn, List<Guid> employeeIds)
    {
        var map = new Dictionary<Guid, HashSet<string>>();
        var ids = new List<Guid>(new HashSet<Guid>(employeeIds));
        if (ids.Count == 0) return map;
        await using var r = await conn.Cmd("""
            SELECT employee_id, period FROM hr_payslips
            WHERE published = TRUE AND employee_id = ANY(@ids)
            """).With("@ids", ids.ToArray()).ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var emp = r.Guid("employee_id");
            if (!map.TryGetValue(emp, out var set)) map[emp] = set = new HashSet<string>();
            set.Add(r.Str("period"));
        }
        return map;
    }

    /// <summary>Cộng thêm <paramref name="offset"/> tháng vào kỳ "yyyy-MM"; trả về "yyyy-MM".</summary>
    private static string AddPeriod(string startPeriod, int offset)
    {
        if (!TryPeriod(startPeriod, out var y, out var m)) return startPeriod;
        var total = y * 12 + (m - 1) + offset;
        return $"{total / 12:D4}-{total % 12 + 1:D2}";
    }

    private static async Task Signal(IHubContext<ChangesHub> hub, Database db, ClaimsPrincipal u, string action, string name)
    {
        await db.RecordAudit(u.Username(), action, "Penalty", name, $"{action} (web).");
        await hub.Clients.All.SendAsync("changed", "data");
        await hub.Clients.All.SendAsync("changed", "hr");
    }

    /// <summary>Ghi audit + báo real-time, đồng thời nhắm riêng người bị phạt để màn hình của họ tự cập nhật.</summary>
    private static async Task SignalEmployee(IHubContext<ChangesHub> hub, Database db, NpgsqlConnection conn,
        ClaimsPrincipal u, Guid employeeId, string action, string name)
    {
        await Signal(hub, db, u, action, name);
        var target = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteScalarAsync() as string;
        if (!string.IsNullOrWhiteSpace(target))
        {
            await hub.Clients.User(target).SendAsync("changed", "data");
            await hub.Clients.User(target).SendAsync("changed", "hr");
        }
    }

    private static int NormInstallments(int installments) => installments < 1 ? 1 : (installments > 60 ? 60 : installments);

    /// <summary>Kỳ bắt đầu trừ: dùng giá trị gửi lên nếu hợp lệ, ngược lại lấy tháng của ngày phạt (hoặc tháng hiện tại).</summary>
    private static string NormStartPeriod(string? startPeriod, DateOnly? penaltyDate)
    {
        if (!string.IsNullOrWhiteSpace(startPeriod) && TryPeriod(startPeriod.Trim(), out _, out _))
            return startPeriod.Trim();
        var d = penaltyDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return $"{d.Year:D4}-{d.Month:D2}";
    }

    public record SavePenaltyReq(Guid EmployeeId, string? PenaltyType, DateOnly? PenaltyDate,
        decimal Amount, int Installments, string? StartPeriod, string? Reason, string? Note, string? Status);
}
