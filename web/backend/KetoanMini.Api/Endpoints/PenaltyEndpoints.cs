using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
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

            -- Sổ cái: số tiền phạt THỰC trừ của mỗi quyết định phạt trong mỗi kỳ lương đã phát hành.
            -- collected(penalty) = SUM(amount). Là nguồn sự thật cho "đã thu bao nhiêu / còn nợ bao nhiêu".
            CREATE TABLE IF NOT EXISTS hr_penalty_ledger (
                penalty_id uuid NOT NULL REFERENCES hr_penalties(id) ON DELETE CASCADE,
                period varchar(7) NOT NULL,
                amount numeric(18,2) NOT NULL DEFAULT 0,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (penalty_id, period)
            );
            """).ExecuteNonQueryAsync(ct);

        await SeedLedgerFromLegacyAsync(conn, ct);
    }

    /// <summary>
    /// Di trú một lần: phạt tiền có từ TRƯỚC khi có sổ cái sẽ chưa có dòng ghi nào. Nạp sổ theo lịch cũ —
    /// mỗi đợt trong <see cref="BuildSchedule"/> ứng với một kỳ mà nhân viên ĐÃ có phiếu lương phát hành thì
    /// coi như đã trừ đúng phần đó (đúng bằng logic suy luận cũ, nên số dư khớp trạng thái hiện tại). Chỉ
    /// nạp cho phạt CHƯA có dòng sổ nào (idempotent), rồi đánh "Đã tất toán" cho phạt đã thu đủ.
    /// </summary>
    private static async Task SeedLedgerFromLegacyAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var legacy = new List<(Guid Id, decimal Amount, int Inst, string Start, Guid Emp)>();
        await using (var r = await conn.Cmd("""
            SELECT p.id, p.amount, p.installments, p.start_period, p.employee_id
            FROM hr_penalties p
            WHERE p.penalty_type='fine' AND p.amount > 0 AND p.status IN ('Active','Settled')
              AND NOT EXISTS (SELECT 1 FROM hr_penalty_ledger l WHERE l.penalty_id = p.id)
            """).ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
                legacy.Add((r.Guid("id"), r.Dec("amount"), r.Int("installments"), r.Str("start_period"), r.Guid("employee_id")));
        }
        if (legacy.Count == 0) return;

        var published = await LoadPublishedPeriods(conn, legacy.ConvertAll(x => x.Emp));
        foreach (var f in legacy)
        {
            if (!published.TryGetValue(f.Emp, out var periods)) continue;
            var schedule = BuildSchedule(f.Amount, f.Inst);
            for (var i = 0; i < schedule.Length; i++)
            {
                var period = AddPeriod(f.Start, i);
                if (!periods.Contains(period)) continue;
                await conn.Cmd("""
                    INSERT INTO hr_penalty_ledger (penalty_id, period, amount)
                    VALUES (@pid, @period, @amt) ON CONFLICT (penalty_id, period) DO NOTHING
                    """).With("@pid", f.Id).With("@period", period).With("@amt", schedule[i]).ExecuteNonQueryAsync(ct);
            }
        }

        await conn.Cmd("""
            UPDATE hr_penalties p SET status='Settled', updated_at=CURRENT_TIMESTAMP
            WHERE p.penalty_type='fine' AND p.status='Active' AND p.amount > 0
              AND (SELECT COALESCE(SUM(amount),0) FROM hr_penalty_ledger l WHERE l.penalty_id=p.id) >= p.amount
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

    public sealed record PenaltyDeductionLine(Guid PenaltyId, string PenaltyNo, string Reason, decimal Amount,
        int Installments, int InstallmentNo, decimal MonthAmount);

    /// <summary>
    /// Tổng tiền phạt cần khấu trừ + chi tiết từng khoản cho một nhân viên trong một kỳ lương.
    /// Nguyên tắc: tổng thực thu của mỗi phạt KHÔNG vượt <c>amount</c>; mỗi kỳ chỉ trừ trong phạm vi
    /// <paramref name="availableForPenalties"/> (lương còn lại, không để âm); phần chưa thu tự CHUYỂN
    /// sang kỳ sau. Lịch <see cref="BuildSchedule"/> chỉ dùng để GIỮ NHỊP "trả góp/chia N tháng" —
    /// một kỳ có thể thu bù cả phần thiếu của các kỳ trước. Đọc phần đã thu từ sổ cái (loại kỳ đang lập).
    /// Truyền <paramref name="availableForPenalties"/> = <see cref="decimal.MaxValue"/> để xem trước KHÔNG cap.
    /// </summary>
    public static async Task<(decimal Total, List<PenaltyDeductionLine> Items)> ComputeDeductionsAsync(
        NpgsqlConnection conn, Guid employeeId, string period, decimal availableForPenalties = decimal.MaxValue)
    {
        var items = new List<PenaltyDeductionLine>();
        var fines = new List<(Guid Id, string No, decimal Amount, int Inst, string Start, string Reason)>();
        await using (var r = await conn.Cmd("""
            SELECT id, penalty_no, amount, installments, start_period, reason
            FROM hr_penalties
            WHERE employee_id=@emp AND status='Active' AND penalty_type='fine' AND amount > 0
            ORDER BY penalty_date, created_at
            """).With("@emp", employeeId).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                fines.Add((r.Guid("id"), r.Str("penalty_no"), r.Dec("amount"), r.Int("installments"),
                    r.Str("start_period"), r.Str("reason")));
        }
        if (fines.Count == 0) return (0, items);

        var collected = await LoadCollectedAsync(conn, fines.ConvertAll(f => f.Id), period);
        var availableLeft = availableForPenalties;
        decimal total = 0;

        foreach (var f in fines)
        {
            if (availableLeft <= 0) break;
            collected.TryGetValue(f.Id, out var already);
            var remaining = f.Amount - already;
            if (remaining <= 0) continue;                       // đã thu đủ (sẽ được đánh "Đã tất toán")
            var off = MonthOffset(f.Start, period);
            if (off is null || off < 0) continue;               // kỳ trước khi phạt bắt đầu → chưa trừ

            var schedule = BuildSchedule(f.Amount, f.Inst);
            // Mục tiêu luỹ kế phải thu tính đến hết kỳ này; vượt lịch → phải thu hết (gom nốt carry-over).
            decimal cumTarget;
            if (off.Value >= schedule.Length) cumTarget = f.Amount;
            else { cumTarget = 0; for (var i = 0; i <= off.Value; i++) cumTarget += schedule[i]; }

            var catchUp = Math.Min(remaining, cumTarget - already);
            if (catchUp <= 0) continue;                         // kỳ này chưa tới hạn thu thêm
            var due = Math.Min(catchUp, availableLeft);
            if (due <= 0) continue;
            total += due;
            availableLeft -= due;
            items.Add(new PenaltyDeductionLine(f.Id, f.No, f.Reason, f.Amount, f.Inst, off.Value + 1, due));
        }
        return (total, items);
    }

    /// <summary>Tổng đã thu (sổ cái) theo từng phạt; <paramref name="excludePeriod"/> để loại kỳ đang lập lại.</summary>
    public static async Task<Dictionary<Guid, decimal>> LoadCollectedAsync(NpgsqlConnection conn,
        List<Guid> penaltyIds, string? excludePeriod = null)
    {
        var map = new Dictionary<Guid, decimal>();
        var ids = new List<Guid>(new HashSet<Guid>(penaltyIds));
        if (ids.Count == 0) return map;
        var sql = "SELECT penalty_id, COALESCE(SUM(amount),0)::numeric AS c FROM hr_penalty_ledger "
            + "WHERE penalty_id = ANY(@ids)" + (excludePeriod is null ? "" : " AND period <> @ex")
            + " GROUP BY penalty_id";
        var cmd = conn.Cmd(sql).With("@ids", ids.ToArray());
        if (excludePeriod is not null) cmd.With("@ex", excludePeriod);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) map[r.Guid("penalty_id")] = r.Dec("c");
        return map;
    }

    /// <summary>Tổng đã thu (sổ cái) của MỘT phạt; <paramref name="excludePeriod"/> để loại kỳ đang lập lại.</summary>
    public static async Task<decimal> GetCollectedAsync(NpgsqlConnection conn, Guid penaltyId, string? excludePeriod = null)
    {
        var sql = "SELECT COALESCE(SUM(amount),0)::numeric FROM hr_penalty_ledger WHERE penalty_id=@id"
            + (excludePeriod is null ? "" : " AND period <> @ex");
        var cmd = conn.Cmd(sql).With("@id", penaltyId);
        if (excludePeriod is not null) cmd.With("@ex", excludePeriod);
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? 0m : Convert.ToDecimal(v);
    }

    /// <summary>
    /// Ghi sổ tiền phạt THỰC trừ của một kỳ khi PHÁT HÀNH phiếu lương (idempotent theo penalty+kỳ).
    /// Xóa các dòng cũ của nhân viên trong kỳ này không còn trong lần tính mới (vd. phạt vừa bị bỏ), rồi
    /// đánh "Đã tất toán" cho phạt đã thu đủ.
    /// </summary>
    public static async Task RecordDeductionsAsync(NpgsqlConnection conn, Guid employeeId, string period,
        IReadOnlyList<PenaltyDeductionLine> lines)
    {
        var keepIds = new List<Guid>();
        foreach (var l in lines) if (!keepIds.Contains(l.PenaltyId)) keepIds.Add(l.PenaltyId);

        if (keepIds.Count == 0)
        {
            await ClearDeductionsForPeriod(conn, employeeId, period);
            return;
        }

        await conn.Cmd("""
            DELETE FROM hr_penalty_ledger l USING hr_penalties p
            WHERE l.penalty_id = p.id AND p.employee_id = @emp AND l.period = @period
              AND NOT (l.penalty_id = ANY(@keep))
            """).With("@emp", employeeId).With("@period", period).With("@keep", keepIds.ToArray())
            .ExecuteNonQueryAsync();

        foreach (var l in lines)
            await conn.Cmd("""
                INSERT INTO hr_penalty_ledger (penalty_id, period, amount, updated_at)
                VALUES (@pid, @period, @amt, CURRENT_TIMESTAMP)
                ON CONFLICT (penalty_id, period) DO UPDATE SET amount=@amt, updated_at=CURRENT_TIMESTAMP
                """).With("@pid", l.PenaltyId).With("@period", period).With("@amt", l.MonthAmount)
                .ExecuteNonQueryAsync();

        await MarkSettledAsync(conn, employeeId);
    }

    /// <summary>Xóa ghi sổ phạt của một kỳ cho nhân viên (khi lưu nháp/không phát hành hoặc xóa phiếu lương),
    /// rồi trả phạt "Đã tất toán" về "Còn hiệu lực" nếu vì thế mà chưa còn thu đủ.</summary>
    public static async Task ClearDeductionsForPeriod(NpgsqlConnection conn, Guid employeeId, string period)
    {
        await conn.Cmd("""
            DELETE FROM hr_penalty_ledger l USING hr_penalties p
            WHERE l.penalty_id = p.id AND p.employee_id=@emp AND l.period=@period
            """).With("@emp", employeeId).With("@period", period).ExecuteNonQueryAsync();

        await conn.Cmd("""
            UPDATE hr_penalties p SET status='Active', updated_at=CURRENT_TIMESTAMP
            WHERE p.employee_id=@emp AND p.status='Settled' AND p.penalty_type='fine'
              AND (SELECT COALESCE(SUM(amount),0) FROM hr_penalty_ledger l WHERE l.penalty_id=p.id) < p.amount
            """).With("@emp", employeeId).ExecuteNonQueryAsync();
    }

    /// <summary>Đánh "Đã tất toán" cho các phạt tiền còn hiệu lực đã thu đủ (collected ≥ amount).</summary>
    private static async Task MarkSettledAsync(NpgsqlConnection conn, Guid employeeId)
    {
        await conn.Cmd("""
            UPDATE hr_penalties p SET status='Settled', updated_at=CURRENT_TIMESTAMP
            WHERE p.employee_id=@emp AND p.status='Active' AND p.penalty_type='fine' AND p.amount > 0
              AND (SELECT COALESCE(SUM(amount),0) FROM hr_penalty_ledger l WHERE l.penalty_id=p.id) >= p.amount
            """).With("@emp", employeeId).ExecuteNonQueryAsync();
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
        g.MapGet("/deductions", async (ClaimsPrincipal u, Database db, Guid employeeId, string period, decimal? available) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (employeeId == Guid.Empty || string.IsNullOrWhiteSpace(period))
                return Results.BadRequest(new { message = "Thiếu nhân viên hoặc kỳ lương." });

            await using var conn = await db.OpenAsync();
            // available = lương còn lại có thể trừ (base+phụ cấp+tăng ca − khấu trừ khác). Không truyền → không cap (xem trước).
            var (total, items) = await ComputeDeductionsAsync(conn, employeeId, period,
                available is > 0 ? available.Value : (available is null ? decimal.MaxValue : 0));
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

            // Tiến trình khấu trừ (phạt tiền): lấy số THỰC trừ từ sổ cái theo từng kỳ.
            var ledger = await LoadLedgerAsync(conn, recs.ConvertAll(p => p.Id));

            var list = recs.ConvertAll(p => ProjectPenalty(p, ledger));
            return Results.Ok(list);
        });

        g.MapPost("/", async (SavePenaltyReq req, ClaimsPrincipal u, Database db, PushService push) =>
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

            await Signal(db, u, "Lập quyết định phạt", no);
            await push.SendToEmployeeAsync(conn, req.EmployeeId, "Quyết định phạt mới",
                $"{no} · {req.Reason!.Trim()}", $"pen:{id}", "Penalty");
            return Results.Ok(new { id, penaltyNo = no });
        });

        g.MapPut("/{id:guid}", async (Guid id, SavePenaltyReq req, ClaimsPrincipal u, Database db) =>
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
            await Signal(db, u, "Cập nhật quyết định phạt", id.ToString());
            return Results.NoContent();
        });

        // Miễn / hủy hiệu lực phạt (không xóa để giữ lịch sử).
        g.MapPost("/{id:guid}/waive", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE hr_penalties SET status='Waived', updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Miễn phạt", id.ToString());
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_penalties WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa quyết định phạt", id.ToString());
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

    private static object ProjectPenalty(PenaltyRec p, Dictionary<Guid, Dictionary<string, decimal>> ledger) => new
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
        progress = BuildProgress(p, ledger),
    };

    /// <summary>
    /// Tiến trình khấu trừ của một quyết định phạt tiền: số THỰC trừ lấy từ sổ cái theo từng kỳ. Kỳ nào
    /// đã có dòng sổ = đã trừ (hiển thị số thực trừ); kỳ chưa trừ hiển thị số dự kiến theo lịch. Trả null
    /// cho phạt không phải "fine", đã miễn (Waived), hoặc ≤ 0₫. Phạt đã thu đủ → <c>settled=true</c>.
    /// </summary>
    private static object? BuildProgress(PenaltyRec p, Dictionary<Guid, Dictionary<string, decimal>> ledger)
    {
        if (p.PenaltyType != "fine" || p.Status == "Waived" || p.Amount <= 0) return null;
        var schedule = BuildSchedule(p.Amount, p.Installments);
        if (schedule.Length == 0) return null;
        ledger.TryGetValue(p.Id, out var paid);

        decimal deducted = 0;
        if (paid is not null) foreach (var v in paid.Values) deducted += v;
        if (deducted > p.Amount) deducted = p.Amount;
        var remaining = p.Amount - deducted;

        var paidMonths = 0;
        string? nextPeriod = null;
        decimal nextAmount = 0;
        var periods = new List<object>(schedule.Length);
        for (var i = 0; i < schedule.Length; i++)
        {
            var period = AddPeriod(p.StartPeriod, i);
            var isPaid = paid is not null && paid.TryGetValue(period, out var actual);
            var shown = isPaid ? paid![period] : schedule[i];
            if (isPaid) paidMonths++;
            else if (nextPeriod is null && remaining > 0)
            {
                nextPeriod = period;
                nextAmount = Math.Min(schedule[i], remaining);
            }
            periods.Add(new { period, amount = shown, paid = isPaid, installmentNo = i + 1 });
        }

        return new
        {
            total = p.Amount,
            deducted,
            remaining,
            settled = p.Status == "Settled" || remaining <= 0,
            totalMonths = schedule.Length,
            paidMonths,
            remainingMonths = remaining <= 0 ? 0 : Math.Max(1, schedule.Length - paidMonths),
            nextPeriod,
            nextAmount,
            periods,
        };
    }

    /// <summary>Sổ cái theo từng phạt: penalty_id → (kỳ "yyyy-MM" → số tiền thực trừ).</summary>
    private static async Task<Dictionary<Guid, Dictionary<string, decimal>>> LoadLedgerAsync(
        NpgsqlConnection conn, List<Guid> penaltyIds)
    {
        var map = new Dictionary<Guid, Dictionary<string, decimal>>();
        var ids = new List<Guid>(new HashSet<Guid>(penaltyIds));
        if (ids.Count == 0) return map;
        await using var r = await conn.Cmd("""
            SELECT penalty_id, period, amount FROM hr_penalty_ledger WHERE penalty_id = ANY(@ids)
            """).With("@ids", ids.ToArray()).ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var pid = r.Guid("penalty_id");
            if (!map.TryGetValue(pid, out var d)) map[pid] = d = new Dictionary<string, decimal>();
            d[r.Str("period")] = r.Dec("amount");
        }
        return map;
    }

    /// <summary>Các kỳ lương ĐÃ phát hành của từng nhân viên (employee_id → tập "yyyy-MM"). Dùng cho di trú sổ cái.</summary>
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

    /// <summary>
    /// Chỉ ghi audit. Tín hiệu real-time KHÔNG gửi tay ở đây nữa: trigger trên hr_penalties và
    /// hr_penalty_ledger tự phát scope 'hr' sau khi giao dịch commit (xem DatabaseChangePublisher).
    /// Một đường duy nhất — muốn màn hình nào đó biết thì thêm bảng vào danh sách trigger, đừng gọi hub.
    /// </summary>
    private static async Task Signal(Database db, ClaimsPrincipal u, string action, string name)
        => await db.RecordAudit(u.Username(), action, "Penalty", name, $"{action} (web).");

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
