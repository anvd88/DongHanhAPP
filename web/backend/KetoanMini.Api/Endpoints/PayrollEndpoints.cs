using System.Linq;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Bảng lương: mỗi nhân viên có MỘT cấu trúc lương (lương cơ bản, phụ cấp, đơn giá tăng ca và các khoản
/// cộng/trừ tùy biến). Khi lập phiếu lương cho một kỳ, hệ thống tự lấy: mức lương của nhân viên +
/// tổng hợp bảng công (ngày công, giờ tăng ca) + tiền phạt cần khấu trừ trong kỳ, rồi tính lương thực nhận.
/// Phiếu lương lưu ở hr_payslips (dùng chung với hồ sơ) kèm chi tiết dòng lương ở cột details (jsonb).
/// </summary>
public static class PayrollEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS hr_salaries (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL UNIQUE REFERENCES hr_employees(id) ON DELETE CASCADE,
                base_salary numeric(18,2) NOT NULL DEFAULT 0,
                allowance numeric(18,2) NOT NULL DEFAULT 0,
                overtime_rate numeric(18,2) NOT NULL DEFAULT 0,
                components jsonb NOT NULL DEFAULT '[]',
                note text NOT NULL DEFAULT '',
                updated_by varchar(128) NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Mỗi lần lập/cập nhật/phát hành/xác nhận phiếu lương tạo đúng một phiên bản ở bảng này.
            -- Không đặt FK về phiếu/nhân viên: lịch sử kế toán phải còn nguyên kể cả khi hồ sơ nguồn bị xóa.
            ALTER TABLE hr_payslips ADD COLUMN IF NOT EXISTS created_by varchar(128) NOT NULL DEFAULT '';
            ALTER TABLE hr_payslips ADD COLUMN IF NOT EXISTS updated_by varchar(128) NOT NULL DEFAULT '';
            ALTER TABLE hr_payslips ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;
            ALTER TABLE hr_payslips ADD COLUMN IF NOT EXISTS published_at timestamptz NULL;

            CREATE TABLE IF NOT EXISTS hr_payslip_history (
                id uuid PRIMARY KEY,
                payslip_id uuid NOT NULL,
                employee_id uuid NOT NULL,
                employee_name varchar(200) NOT NULL DEFAULT '',
                employee_code varchar(32) NOT NULL DEFAULT '',
                period varchar(7) NOT NULL,
                revision integer NOT NULL,
                action varchar(32) NOT NULL,
                status_before varchar(24) NULL,
                status_after varchar(24) NOT NULL,
                actor varchar(128) NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                summary jsonb NOT NULL DEFAULT '{}',
                snapshot jsonb NOT NULL DEFAULT '{}',
                CONSTRAINT ck_hr_payslip_history_revision CHECK (revision > 0),
                CONSTRAINT ux_hr_payslip_history_revision UNIQUE (payslip_id, revision)
            );
            CREATE INDEX IF NOT EXISTS ix_hr_payslip_history_employee_period
                ON hr_payslip_history (employee_id, period, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS ix_hr_payslip_history_payslip
                ON hr_payslip_history (payslip_id, revision DESC);

            -- Bảng sự kiện là append-only. Ngay cả lỗi lập trình/quyền SQL thông thường cũng không được
            -- sửa lại quá khứ; muốn đính chính phải thêm một sự kiện mới.
            CREATE OR REPLACE FUNCTION prevent_hr_payslip_history_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'hr_payslip_history is append-only';
            END;
            $fn$;
            DROP TRIGGER IF EXISTS trg_hr_payslip_history_immutable ON hr_payslip_history;
            CREATE TRIGGER trg_hr_payslip_history_immutable
                BEFORE UPDATE OR DELETE ON hr_payslip_history
                FOR EACH ROW EXECUTE FUNCTION prevent_hr_payslip_history_mutation();

            -- Phiếu có từ phiên bản cũ được ghi nhận một lần để màn lịch sử không có khoảng trống.
            INSERT INTO hr_payslip_history
                (id, payslip_id, employee_id, employee_name, employee_code, period, revision,
                 action, status_before, status_after, actor, occurred_at, summary, snapshot)
            SELECT gen_random_uuid(), p.id, p.employee_id, COALESCE(e.full_name,''), COALESCE(e.employee_code,''),
                   p.period,
                   COALESCE((SELECT MAX(h.revision)+1 FROM hr_payslip_history h
                             WHERE h.employee_id=p.employee_id AND h.period=p.period),1),
                   'Imported', NULL,
                   CASE WHEN NOT p.published THEN 'Draft'
                        WHEN p.acknowledged_at IS NOT NULL THEN 'Acknowledged' ELSE 'Published' END,
                   COALESCE(NULLIF(p.created_by,''), 'system:migration'), p.created_at,
                   jsonb_build_object('netPay',p.net_pay,'totalDeductions',p.deductions,
                                      'published',p.published,'note',p.note),
                   jsonb_build_object('workDays',p.work_days,'overtimeHours',p.overtime_hours,
                                      'baseSalary',p.base_salary,'allowance',p.allowance,
                                      'overtimePay',p.overtime_pay,'deductions',p.deductions,
                                      'netPay',p.net_pay,'note',p.note,'details',p.details,
                                      'published',p.published,'acknowledgedAt',p.acknowledged_at)
            FROM hr_payslips p
            LEFT JOIN hr_employees e ON e.id=p.employee_id
            WHERE NOT EXISTS (SELECT 1 FROM hr_payslip_history h WHERE h.payslip_id=p.id)
            ON CONFLICT (payslip_id, revision) DO NOTHING;

            -- Dữ liệu cũ chưa có mốc phát hành riêng: ưu tiên sự kiện đầu tiên đưa phiếu sang Published/Acknowledged,
            -- rồi mới lùi về updated_at/created_at. Từ phiên bản này trở đi published_at được ghi chính xác lúc phát hành.
            UPDATE hr_payslips p
               SET published_at = COALESCE(
                   (SELECT MIN(h.occurred_at) FROM hr_payslip_history h
                     WHERE h.payslip_id=p.id AND h.status_after IN ('Published','Acknowledged')),
                   p.updated_at, p.created_at)
             WHERE p.published=TRUE AND p.published_at IS NULL;
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapPayroll(this WebApplication app)
    {
        var g = app.MapGroup("/api/payroll").RequireAuthorization();

        // ---------------- Mức lương theo nhân viên ----------------

        // Danh sách nhân viên kèm mức lương (admin) — cho trang bảng lương.
        g.MapGet("/salaries", async (ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.PayrollRead)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            // Lương cứng hiển thị theo THÁNG HIỆN TẠI. Hợp đồng chi phối + tổng tăng lương lấy ngay
            // trong một câu truy vấn (LATERAL) — mỗi nhân viên một vòng đọc riêng sẽ thành N+1.
            var period = NormalizePeriod(null);
            var (pStart, pEnd) = PeriodRange(period);
            const string effectiveSql =
                "(k.status='Active' AND (k.start_date IS NULL OR k.start_date <= @pEnd) AND (k.end_date IS NULL OR k.end_date >= @pStart))";
            var list = new List<object>();
            await using var r = await conn.Cmd($"""
                SELECT e.id, e.full_name, e.employee_code, COALESCE(d.name,'') AS dept_name,
                       s.base_salary, s.allowance, s.overtime_rate, s.components::text AS components,
                       c.id AS contract_id, c.contract_no, c.contract_type, c.base_salary AS contract_base,
                       c.end_date AS contract_end, c.effective AS contract_effective,
                       COALESCE(rz.raise_total, 0) AS raise_total
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id = e.department_id
                LEFT JOIN hr_salaries s ON s.employee_id = e.id
                LEFT JOIN LATERAL (
                    SELECT k.id, k.contract_no, k.contract_type, k.base_salary, k.end_date, {effectiveSql} AS effective
                    FROM hr_contracts k WHERE k.employee_id = e.id
                    ORDER BY {effectiveSql} DESC,
                             (k.start_date IS NULL OR k.start_date <= @pEnd) DESC,
                             k.start_date DESC NULLS LAST, k.created_at DESC
                    LIMIT 1
                ) c ON TRUE
                LEFT JOIN LATERAL (
                    SELECT SUM(z.amount) AS raise_total FROM hr_salary_raises z
                    WHERE z.contract_id = c.id AND z.effective_period <= @period
                ) rz ON TRUE
                WHERE e.status = 'Active'
                ORDER BY e.full_name
                """).With("@pStart", pStart).With("@pEnd", pEnd).With("@period", period).ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var hasLegacy = !r.IsDBNull(r.GetOrdinal("base_salary"));
                var components = hasLegacy ? ParseComponents(r.Str("components")) : new List<SalaryComponent>();
                var hasContract = !r.IsDBNull(r.GetOrdinal("contract_id"));
                var contractBase = hasContract ? r.Dec("contract_base") : 0m;
                var raiseTotal = hasContract ? r.Dec("raise_total") : 0m;
                var hard = hasContract
                    ? new HardSalaryInfo(contractBase + raiseTotal, true, r.Guid("contract_id"), r.Str("contract_no"),
                        r.Str("contract_type"), contractBase, raiseTotal, r.Bool("contract_effective"),
                        r.IsDBNull(r.GetOrdinal("contract_end")) ? null : r.DateOnly("contract_end"),
                        new List<HardSalaryRaise>())
                    : new HardSalaryInfo(hasLegacy ? r.Dec("base_salary") : 0m, false, null, "", "", 0, 0, false, null,
                        new List<HardSalaryRaise>());
                list.Add(new
                {
                    employeeId = r.Guid("id"),
                    employeeName = r.Str("full_name"),
                    employeeCode = r.Str("employee_code"),
                    departmentName = r.Str("dept_name"),
                    // "Đã gán" = có mức lương dùng được: hợp đồng có lương, hoặc bản ghi lương cũ.
                    hasSalary = hasLegacy || hasContract,
                    baseSalary = hard.Amount,
                    allowance = hasLegacy ? r.Dec("allowance") : 0m,
                    overtimeRate = hasLegacy ? r.Dec("overtime_rate") : 0m,
                    extraCount = components.Count,
                    hardSalary = HardSalaryPayload(hard),
                });
            }
            return Results.Ok(list);
        });

        // Cấu trúc lương chi tiết của một nhân viên (admin, hoặc chính chủ xem của mình).
        g.MapGet("/salaries/{employeeId:guid}", async (Guid employeeId, ClaimsPrincipal u, Database db, string? period) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.Can(Permissions.PayrollRead))
            {
                var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", employeeId).ExecuteScalarAsync() as string;
                if (!string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            }
            var salary = await ReadSalary(conn, employeeId);
            // Lương cứng là số DẪN XUẤT (hợp đồng + tăng lương). base_salary cũ vẫn trả về ở
            // legacyBaseSalary để màn hình còn chỗ sửa cho nhân viên chưa có hợp đồng nào.
            var hard = await ResolveHardSalaryAsync(conn, employeeId, NormalizePeriod(period), salary.BaseSalary);
            return Results.Ok(new
            {
                salary.EmployeeId,
                salary.HasSalary,
                baseSalary = hard.Amount,
                legacyBaseSalary = salary.BaseSalary,
                salary.Allowance,
                salary.OvertimeRate,
                salary.Components,
                salary.Note,
                hardSalary = HardSalaryPayload(hard),
            });
        });

        g.MapPut("/salaries/{employeeId:guid}", async (Guid employeeId, SaveSalaryReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.PayrollManage)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var componentsJson = SerializeComponents(req.Components);
            await conn.Cmd("""
                INSERT INTO hr_salaries (id, employee_id, base_salary, allowance, overtime_rate, components, note, updated_by, updated_at)
                VALUES (@id, @emp, @base, @allow, @otr, @comp::jsonb, @note, @by, CURRENT_TIMESTAMP)
                ON CONFLICT (employee_id) DO UPDATE SET
                    base_salary=@base, allowance=@allow, overtime_rate=@otr, components=@comp::jsonb,
                    note=@note, updated_by=@by, updated_at=CURRENT_TIMESTAMP
                """)
                .With("@id", Guid.NewGuid()).With("@emp", employeeId)
                .With("@base", req.BaseSalary).With("@allow", req.Allowance).With("@otr", req.OvertimeRate)
                .With("@comp", componentsJson).With("@note", req.Note ?? "").With("@by", u.Username())
                .ExecuteNonQueryAsync();
            await Signal(db, u, employeeId, "Cập nhật mức lương", "Salary");
            return Results.NoContent();
        });

        // Nhân viên tự xem LƯƠNG DỰ TÍNH của chính mình cho THÁNG HIỆN TẠI (gồm khấu trừ phạt nếu có).
        // Dùng chung bộ tính ComputePayroll như admin nên số liệu khớp phiếu lương sẽ lập.
        g.MapGet("/my-estimate", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (await conn.Cmd("SELECT id FROM hr_employees WHERE username=@u").With("@u", u.Username()).ExecuteScalarAsync() is not Guid employeeId)
                return Results.NotFound(new { message = "Tài khoản chưa gắn hồ sơ nhân sự." });
            var now = DateTime.UtcNow.AddHours(7);
            var period = $"{now.Year:D4}-{now.Month:D2}";
            var salary = await ReadSalary(conn, employeeId);
            var hard = await ResolveHardSalaryAsync(conn, employeeId, period, salary.BaseSalary);
            var result = await ComputePayroll(conn, employeeId, period, null); // null = dự tính tất cả ngày tăng ca
            if (result is null) return Results.NotFound();
            // Earnings ở result không chứa tăng ca → thêm dòng tăng ca (dự tính) để nhân viên thấy đầy đủ.
            var earnings = result.Earnings.Select(e => new { label = e.Label, amount = e.Amount }).ToList();
            if (result.OvertimePay != 0)
                earnings.Add(new { label = $"Tăng ca ({result.OvertimeHours} giờ)", amount = result.OvertimePay });
            return Results.Ok(new
            {
                result.EmployeeName, result.EmployeeCode, result.Period,
                result.BaseSalary, result.OvertimeHours, result.OvertimePay,
                result.WorkedDays, result.AbsentDays, result.LateDays,
                earnings, deductions = result.Deductions,
                result.TotalEarnings, result.TotalDeductions, result.NetPay,
                hasSalary = salary.HasSalary || hard.FromContract,
            });
        });

        // NHẬT KÝ MỘT NGÀY của chính mình — bấm vào một ô ngày trên lịch bảng công thì thấy ngày đó
        // đã làm những việc gì, có bị phạt/kỷ luật gì, xin ứng tiền hay được kế toán chi tiền không.
        // Mọi mốc đều trả nguyên timestamptz để app hiện đủ ngày/tháng/giờ/phút.
        g.MapGet("/my-day", async (ClaimsPrincipal u, Database db, string? date) =>
        {
            var me = u.Username();
            var day = DateOnly.TryParseExact((date ?? "").Trim(), "yyyy-MM-dd", out var parsed)
                ? parsed
                : DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            await using var conn = await db.OpenAsync();
            if (await conn.Cmd("SELECT id FROM hr_employees WHERE username=@u").With("@u", me).ExecuteScalarAsync() is not Guid employeeId)
                return Results.NotFound(new { message = "Tài khoản chưa gắn hồ sơ nhân sự." });

            // ── Việc đã làm trong ngày ──────────────────────────────────────────────────────────
            // Lấy theo SỰ KIỆN chứ không theo việc: một việc kéo dài nhiều ngày thì mỗi ngày chỉ hiện
            // đúng phần đã động tới hôm đó. Gồm cả sự kiện do người khác gây ra trên việc của tôi
            // (được giao, bị trả lại, được nghiệm thu) vì đó cũng là chuyện xảy ra với tôi hôm đó.
            var tasks = new List<object>();
            await using (var r = await conn.Cmd($"""
                SELECT t.id, t.task_no, t.title, t.status, t.priority, t.progress,
                       t.assigner_name, t.assignee_name,
                       ev.kind, ev.note, ev.actor_name, ev.created_at
                FROM work_task_events ev
                JOIN work_tasks t ON t.id = ev.task_id
                WHERE (lower(t.assignee_username) = lower(@me) OR lower(ev.actor_username) = lower(@me))
                  AND (ev.created_at AT TIME ZONE '{DayTz}')::date = @day
                ORDER BY ev.created_at
                """).With("@me", me).With("@day", day).ExecuteReaderAsync())
                while (await r.ReadAsync())
                    tasks.Add(new
                    {
                        id = r.Guid("id"),
                        taskNo = r.Str("task_no"),
                        title = r.Str("title"),
                        status = r.Str("status"),
                        statusLabel = TaskStatusLabel(r.Str("status")),
                        progress = r.Int("progress"),
                        kind = r.Str("kind"),
                        kindLabel = TaskEventLabel(r.Str("kind")),
                        note = r.Str("note"),
                        actorName = r.Str("actor_name"),
                        assignerName = r.Str("assigner_name"),
                        assigneeName = r.Str("assignee_name"),
                        at = r.Dt("created_at"),
                    });

            // ── Phạt / kỷ luật ghi nhận cho ngày đó ─────────────────────────────────────────────
            // Bắt cả quyết định LẬP hôm đó lẫn quyết định ghi cho NGÀY VI PHẠM là hôm đó, vì hai mốc
            // này thường lệch nhau (vi phạm hôm nay, quyết định ký hôm sau).
            var penalties = new List<object>();
            await using (var r = await conn.Cmd($"""
                SELECT id, penalty_no, penalty_type, penalty_date, amount, installments,
                       reason, note, status, created_by, created_at
                FROM hr_penalties
                WHERE employee_id = @emp
                  AND (penalty_date = @day OR (created_at AT TIME ZONE '{DayTz}')::date = @day)
                ORDER BY created_at
                """).With("@emp", employeeId).With("@day", day).ExecuteReaderAsync())
                while (await r.ReadAsync())
                    penalties.Add(new
                    {
                        id = r.Guid("id"),
                        code = r.Str("penalty_no"),
                        type = r.Str("penalty_type"),
                        typeLabel = PenaltyEndpoints.Types.FirstOrDefault(t => t.Type == r.Str("penalty_type")).Label
                            ?? r.Str("penalty_type"),
                        penaltyDate = r.DateOnly("penalty_date").ToString("yyyy-MM-dd"),
                        amount = r.Dec("amount"),
                        installments = r.Int("installments"),
                        reason = r.Str("reason"),
                        note = r.Str("note"),
                        status = r.Str("status"),
                        statusLabel = PenaltyStatusLabel(r.Str("status")),
                        createdBy = r.Str("created_by"),
                        at = r.Dt("created_at"),
                    });

            // ── Đơn tiền bạc (tạm ứng, thanh toán, hoàn ứng, mua sắm) ───────────────────────────
            var rawRequests = new List<DayRequest>();
            await using (var r = await conn.Cmd($"""
                SELECT id, request_no, req_type, title, status, payload::text AS payload,
                       created_at, updated_at
                FROM hr_requests
                WHERE employee_id = @emp
                  AND req_type IN ('advance','payment','reimbursement','purchase')
                  AND ((created_at AT TIME ZONE '{DayTz}')::date = @day
                       OR (updated_at AT TIME ZONE '{DayTz}')::date = @day)
                ORDER BY created_at
                """).With("@emp", employeeId).With("@day", day).ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    var type = r.Str("req_type");
                    rawRequests.Add(new DayRequest(
                        r.Guid("id"), r.Str("request_no"), type,
                        RequestEndpoints.Types.FirstOrDefault(t => t.Type == type).Label ?? type,
                        r.Str("title"), PayloadAmount(r.Str("payload")), r.Str("status"),
                        r.Dt("created_at"), r.Dt("updated_at")));
                }
            // Các cấp đã duyệt/từ chối, kèm giờ phút — "trạng thái chi tiết" của một đơn tiền.
            var requestSteps = new Dictionary<Guid, List<object>>();
            if (rawRequests.Count > 0)
            {
                await using var r = await conn.Cmd("""
                    SELECT request_id, step_no, approver_name, status, decided_at, decided_by, comment
                    FROM hr_request_approvals
                    WHERE request_id = ANY(@ids)
                    ORDER BY request_id, step_no
                    """).With("@ids", rawRequests.ConvertAll(x => x.Id).ToArray()).ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var rid = r.Guid("request_id");
                    if (!requestSteps.TryGetValue(rid, out var list)) requestSteps[rid] = list = new List<object>();
                    var approver = r.Str("approver_name");
                    list.Add(new
                    {
                        label = $"Cấp {r.Int("step_no")}" + (approver.Length > 0 ? $" · {approver}" : ""),
                        status = r.Str("status"),
                        statusLabel = RequestStatusLabel(r.Str("status")),
                        at = r.DtNull("decided_at"),
                        by = r.Str("decided_by"),
                        note = r.Str("comment"),
                    });
                }
            }
            var requests = rawRequests.ConvertAll(x => (object)new
            {
                id = x.Id,
                code = x.Code,
                type = x.Type,
                typeLabel = x.TypeLabel,
                title = x.Title,
                amount = x.Amount,
                status = x.Status,
                statusLabel = RequestStatusLabel(x.Status),
                at = x.CreatedAt,
                updatedAt = x.UpdatedAt,
                steps = requestSteps.TryGetValue(x.Id, out var s) ? s : new List<object>(),
            });

            // ── Phiếu chi tiền mặt kế toán lập cho tôi ──────────────────────────────────────────
            // Một phiếu chạm vào nhiều mốc trong ngày (lập, ký nhận, duyệt, thực chi) nên chỉ cần
            // MỘT mốc rơi vào ngày đang xem là phiếu đó thuộc về ngày này.
            var payouts = new List<object>();
            await using (var r = await conn.Cmd($"""
                SELECT v.id, v.voucher_no, v.amount, v.status, v.reason, v.note,
                       COALESCE(c.name,'') AS category_name,
                       v.created_by, v.created_at, v.confirmed_at, v.confirmed_by,
                       v.approved_at, v.approved_by, v.completed_at, v.completed_by, v.paid_at,
                       v.rejected_at, v.rejected_by, v.reject_reason,
                       v.cancelled_at, v.cancelled_by, v.cancel_reason
                FROM hr_payout_vouchers v
                LEFT JOIN hr_payout_categories c ON c.id = v.category_id
                WHERE v.employee_id = @emp
                  AND @day IN (
                      (v.created_at   AT TIME ZONE '{DayTz}')::date,
                      (v.confirmed_at AT TIME ZONE '{DayTz}')::date,
                      (v.approved_at  AT TIME ZONE '{DayTz}')::date,
                      (v.completed_at AT TIME ZONE '{DayTz}')::date,
                      (v.paid_at      AT TIME ZONE '{DayTz}')::date,
                      (v.rejected_at  AT TIME ZONE '{DayTz}')::date,
                      (v.cancelled_at AT TIME ZONE '{DayTz}')::date
                  )
                ORDER BY v.created_at
                """).With("@emp", employeeId).With("@day", day).ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    var steps = new List<object>();
                    void Step(string label, DateTime? at, string by, string note = "")
                    {
                        if (at is null) return;
                        steps.Add(new { label, at, by, note });
                    }
                    Step("Kế toán lập phiếu", r.Dt("created_at"), r.Str("created_by"));
                    Step("Người nhận quét QR ký nhận", r.DtNull("confirmed_at"), r.Str("confirmed_by"));
                    Step("Duyệt chi", r.DtNull("approved_at"), r.Str("approved_by"));
                    Step("Đã thực chi", r.DtNull("completed_at") ?? r.DtNull("paid_at"), r.Str("completed_by"));
                    Step("Từ chối", r.DtNull("rejected_at"), r.Str("rejected_by"), r.Str("reject_reason"));
                    Step("Huỷ phiếu", r.DtNull("cancelled_at"), r.Str("cancelled_by"), r.Str("cancel_reason"));
                    payouts.Add(new
                    {
                        id = r.Guid("id"),
                        code = r.Str("voucher_no"),
                        category = r.Str("category_name"),
                        amount = r.Dec("amount"),
                        status = r.Str("status"),
                        statusLabel = PayoutStatusLabel(r.Str("status")),
                        reason = r.Str("reason"),
                        note = r.Str("note"),
                        at = r.Dt("created_at"),
                        steps,
                    });
                }

            return Results.Ok(new
            {
                date = day.ToString("yyyy-MM-dd"),
                tasks,
                penalties,
                requests,
                payouts,
            });
        });

        // Nhân viên tự xem các PHIẾU LƯƠNG ĐÃ PHÁT HÀNH của chính mình (mỗi kỳ một phiếu), kèm chi tiết
        // khoản cộng/trừ để app hiển thị khi bấm vào từng tháng. Chỉ trả phiếu đã publish.
        g.MapGet("/my-payslips", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (await conn.Cmd("SELECT id FROM hr_employees WHERE username=@u").With("@u", u.Username()).ExecuteScalarAsync() is not Guid employeeId)
                return Results.NotFound(new { message = "Tài khoản chưa gắn hồ sơ nhân sự." });

            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, period, overtime_hours, base_salary, allowance, overtime_pay,
                       deductions, net_pay, note, details::text AS details, created_at, published_at, updated_at,
                       (((published_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date + 2)::timestamp
                           AT TIME ZONE 'Asia/Ho_Chi_Minh') AS acknowledgement_due_at,
                       acknowledged_at
                FROM hr_payslips
                WHERE employee_id=@id AND published = TRUE
                ORDER BY period DESC
                """).With("@id", employeeId).ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var det = ParsePayslipDetail(r.Str("details"));
                var baseSalary = r.Dec("base_salary");
                var allowance = r.Dec("allowance");
                var overtimePay = r.Dec("overtime_pay");
                var colDeductions = r.Dec("deductions");
                var colNet = r.Dec("net_pay");
                list.Add(new
                {
                    id = r.Guid("id"),
                    period = r.Str("period"),
                    baseSalary,
                    allowance,
                    overtimePay,
                    overtimeHours = r.Dec("overtime_hours"),
                    workedDays = det.WorkedDays,
                    absentDays = det.AbsentDays,
                    lateDays = det.LateDays,
                    totalWorkedHours = det.TotalWorkedHours,
                    overtimeRate = det.OvertimeRate,
                    overtimeDays = det.OvertimeDays,
                    earnings = det.Earnings,
                    deductions = det.Deductions,
                    totalEarnings = det.TotalEarnings > 0 ? det.TotalEarnings : baseSalary + allowance + overtimePay,
                    totalDeductions = det.TotalDeductions > 0 ? det.TotalDeductions : colDeductions,
                    netPay = det.NetPay != 0 ? det.NetPay : colNet,
                    note = r.Str("note"),
                    createdAt = r.Dt("created_at"),
                    publishedAt = r.Dt("published_at"),
                    updatedAt = r.Dt("updated_at"),
                    revisionToken = PayslipRevisionToken(r.Dt("updated_at")),
                    acknowledgementDueAt = r.Dt("acknowledgement_due_at"),
                    acknowledgementOverdue = r.DtNull("acknowledged_at") is null
                        && r.Dt("acknowledgement_due_at") <= DateTime.UtcNow,
                    acknowledgedAt = r.DtNull("acknowledged_at"),
                });
            }
            return Results.Ok(list);
        });

        // Trạng thái nhắc/xác nhận gọn để app kiểm tra ở Trang chủ và dựng cổng bắt buộc khi quá hạn.
        // Quy tắc theo NGÀY Việt Nam: phát hành ngày D -> được xác nhận hết ngày D+1 -> khóa từ 00:00 ngày D+2.
        g.MapGet("/my-payslips/requirement", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var requirement = await ReadPendingPayslipRequirement(conn, u.Username());
            return Results.Ok(new
            {
                pendingCount = requirement?.PendingCount ?? 0,
                overdueCount = requirement?.OverdueCount ?? 0,
                mustAcknowledge = requirement?.Overdue ?? false,
                serverNow = DateTime.UtcNow,
                payslip = requirement is null ? null : new
                {
                    id = requirement.Id,
                    period = requirement.Period,
                    publishedAt = requirement.PublishedAt,
                    updatedAt = requirement.UpdatedAt,
                    revisionToken = PayslipRevisionToken(requirement.UpdatedAt),
                    acknowledgementDueAt = requirement.DueAt,
                    overdue = requirement.Overdue,
                },
            });
        });

        // Sổ phiếu lương đã phát hành theo tháng cho quản trị/đối soát. Phân trang ngay ở DB để màn
        // tổng hợp vẫn dùng được khi doanh nghiệp có hàng nghìn nhân viên.
        g.MapGet("/payslips/published", async (
            ClaimsPrincipal u, Database db, string period, string? search, string? status,
            int? page, int? pageSize) =>
        {
            if (!u.Can(Permissions.PayrollRead)) return Results.Forbid();
            if (!ValidPeriod(period))
                return Results.BadRequest(new { message = "Kỳ lương phải có định dạng yyyy-MM." });

            var statusKey = (status ?? "all").Trim().ToLowerInvariant();
            if (statusKey is not ("all" or "pending" or "acknowledged"))
                return Results.BadRequest(new { message = "Trạng thái phiếu lương không hợp lệ." });

            var currentPage = Math.Max(1, page ?? 1);
            var take = Math.Clamp(pageSize ?? 50, 10, 200);
            var skip = (currentPage - 1) * take;
            var query = (search ?? "").Trim();
            var where = "p.period=@period AND p.published=TRUE";
            if (query.Length > 0)
                where += " AND (e.full_name ILIKE @search OR e.employee_code ILIKE @search OR COALESCE(d.name,'') ILIKE @search OR COALESCE(l.name,'') ILIKE @search)";
            if (statusKey == "pending") where += " AND p.acknowledged_at IS NULL";
            if (statusKey == "acknowledged") where += " AND p.acknowledged_at IS NOT NULL";

            await using var conn = await db.OpenAsync();
            NpgsqlCommand BindListParameters(NpgsqlCommand cmd)
            {
                cmd.With("@period", period);
                if (query.Length > 0) cmd.With("@search", $"%{query}%");
                return cmd;
            }

            int activeEmployees;
            await using (var activeCmd = conn.Cmd("SELECT COUNT(*)::int FROM hr_employees WHERE status='Active'"))
                activeEmployees = Convert.ToInt32(await activeCmd.ExecuteScalarAsync());

            int publishedCount, acknowledgedCount;
            decimal totalEarnings, totalDeductions, totalNetPay;
            await using (var summary = await conn.Cmd("""
                SELECT COUNT(*)::int AS published_count,
                       COUNT(*) FILTER (WHERE acknowledged_at IS NOT NULL)::int AS acknowledged_count,
                       COALESCE(SUM(net_pay + deductions),0) AS total_earnings,
                       COALESCE(SUM(deductions),0) AS total_deductions,
                       COALESCE(SUM(net_pay),0) AS total_net_pay
                FROM hr_payslips
                WHERE period=@period AND published=TRUE
                """).With("@period", period).ExecuteReaderAsync())
            {
                await summary.ReadAsync();
                publishedCount = summary.Int("published_count");
                acknowledgedCount = summary.Int("acknowledged_count");
                totalEarnings = summary.Dec("total_earnings");
                totalDeductions = summary.Dec("total_deductions");
                totalNetPay = summary.Dec("total_net_pay");
            }

            var countCmd = BindListParameters(conn.Cmd($"""
                SELECT COUNT(*)::int
                FROM hr_payslips p
                JOIN hr_employees e ON e.id=p.employee_id
                LEFT JOIN hr_departments d ON d.id=e.department_id
                LEFT JOIN hr_locations l ON l.id=e.location_id
                WHERE {where}
                """));
            var totalItems = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            var items = new List<object>();
            var listCmd = BindListParameters(conn.Cmd($"""
                SELECT p.id, p.employee_id, e.employee_code, e.full_name,
                       COALESCE(d.name,'') AS department_name, COALESCE(l.name,'') AS location_name,
                       p.period, p.overtime_hours, p.net_pay + p.deductions AS total_earnings,
                       p.deductions AS total_deductions, p.net_pay, p.acknowledged_at, p.updated_at
                FROM hr_payslips p
                JOIN hr_employees e ON e.id=p.employee_id
                LEFT JOIN hr_departments d ON d.id=e.department_id
                LEFT JOIN hr_locations l ON l.id=e.location_id
                WHERE {where}
                ORDER BY d.name NULLS FIRST, e.full_name, e.employee_code
                LIMIT @take OFFSET @skip
                """)).With("@take", take).With("@skip", skip);
            await using (var r = await listCmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    items.Add(new
                    {
                        id = r.Guid("id"),
                        employeeId = r.Guid("employee_id"),
                        employeeCode = r.Str("employee_code"),
                        employeeName = r.Str("full_name"),
                        departmentName = r.Str("department_name"),
                        locationName = r.Str("location_name"),
                        period = r.Str("period"),
                        overtimeHours = r.Dec("overtime_hours"),
                        totalEarnings = r.Dec("total_earnings"),
                        totalDeductions = r.Dec("total_deductions"),
                        netPay = r.Dec("net_pay"),
                        status = r.DtNull("acknowledged_at") is null ? "Published" : "Acknowledged",
                        acknowledgedAt = r.DtNull("acknowledged_at"),
                        updatedAt = r.Dt("updated_at"),
                    });
            }

            return Results.Ok(new
            {
                period,
                search = query,
                status = statusKey,
                page = currentPage,
                pageSize = take,
                totalItems,
                totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)take)),
                summary = new
                {
                    activeEmployeeCount = activeEmployees,
                    publishedCount,
                    acknowledgedCount,
                    pendingAcknowledgementCount = publishedCount - acknowledgedCount,
                    totalEarnings,
                    totalDeductions,
                    totalNetPay,
                },
                items,
            });
        });

        g.MapPost("/my-payslips/{id:guid}/ack", async (
            Guid id, string? expectedRevision, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await ReadPayslipState(conn, id, lockRow: true);
            if (before is null || !before.Published ||
                !string.Equals(before.EmployeeUsername, u.Username(), StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync();
                return Results.NotFound();
            }

            // Client mới gửi phiên bản phiếu đã thực sự hiển thị. Nếu quản trị vừa sửa số liệu trong
            // lúc nhân viên đang đọc, tuyệt đối không ghi nhận xác nhận cho phiên bản chưa được xem.
            // Query revision là tùy chọn để các APK cũ vẫn tương thích trong giai đoạn chuyển tiếp.
            if (!string.IsNullOrWhiteSpace(expectedRevision) &&
                !string.Equals(PayslipRevisionToken(before.UpdatedAt), expectedRevision,
                    StringComparison.Ordinal))
            {
                await tx.RollbackAsync();
                return Results.Conflict(new
                {
                    message = "Phiếu lương vừa được cập nhật. Vui lòng tải lại và kiểm tra số liệu mới trước khi xác nhận.",
                    code = "payslip_changed",
                });
            }

            // Idempotent: mở lại cùng phiếu không tạo thêm sự kiện giả.
            if (before.AcknowledgedAt is null)
            {
                await conn.Cmd("UPDATE hr_payslips SET acknowledged_at=CURRENT_TIMESTAMP, updated_at=CURRENT_TIMESTAMP, updated_by=@by WHERE id=@id")
                    .With("@id", id).With("@by", u.Username()).ExecuteNonQueryAsync();
                var after = await ReadPayslipState(conn, id);
                await AppendPayslipHistory(conn, after!, before.Status, "Acknowledged", u.Username());
            }
            await tx.CommitAsync();
            return Results.NoContent();
        });
        g.MapPost("/my-payslips/{id:guid}/inquiries",async(Guid id,PayslipInquiryReq req,ClaimsPrincipal u,Database db)=>{
            if(string.IsNullOrWhiteSpace(req.Message))return Results.BadRequest(new{message="Vui lòng nhập nội dung thắc mắc."});await using var c=await db.OpenAsync();
            var emp=await c.Cmd("SELECT p.employee_id FROM hr_payslips p JOIN hr_employees e ON e.id=p.employee_id WHERE p.id=@id AND e.username=@u AND p.published=TRUE").With("@id",id).With("@u",u.Username()).ExecuteScalarAsync();if(emp is not Guid eid)return Results.NotFound();
            var qid=Guid.NewGuid();await c.Cmd("INSERT INTO hr_payslip_inquiries(id,payslip_id,employee_id,line_label,message) VALUES(@q,@p,@e,@l,@m)").With("@q",qid).With("@p",id).With("@e",eid).With("@l",req.LineLabel??"").With("@m",req.Message.Trim()).ExecuteNonQueryAsync();return Results.Ok(new{id=qid,status="open"});});
        g.MapGet("/my-payslips/{id:guid}/pdf",async(Guid id,ClaimsPrincipal u,Database db)=>{
            await using var c=await db.OpenAsync();await using var r=await c.Cmd("""
              SELECT p.period,p.net_pay,p.base_salary,p.allowance,p.overtime_pay,p.deductions,e.full_name,e.employee_code
              FROM hr_payslips p JOIN hr_employees e ON e.id=p.employee_id WHERE p.id=@id AND e.username=@u AND p.published=TRUE
              """).With("@id",id).With("@u",u.Username()).ExecuteReaderAsync();if(!await r.ReadAsync())return Results.NotFound();
            var lines=new[]{"PAYSLIP "+r.Str("period"),"Employee: "+r.Str("full_name")+" ("+r.Str("employee_code")+")","Base salary: "+r.Dec("base_salary"),"Allowance: "+r.Dec("allowance"),"Overtime: "+r.Dec("overtime_pay"),"Deductions: "+r.Dec("deductions"),"NET PAY: "+r.Dec("net_pay")};
            var bytes=SimplePdf(lines);return Results.File(bytes,"application/pdf",$"Payslip_{r.Str("period")}.pdf");});

        // ---------------- Tính & lập phiếu lương ----------------

        // Xem trước phiếu lương (chưa lưu): lấy mức lương + bảng công + phạt của kỳ.
        g.MapGet("/compute", async (ClaimsPrincipal u, Database db, Guid employeeId, string period) =>
        {
            if (!u.Can(Permissions.PayrollRead)) return Results.Forbid();
            if (employeeId == Guid.Empty || !ValidPeriod(period))
                return Results.BadRequest(new { message = "Thiếu nhân viên hoặc kỳ lương (yyyy-MM)." });
            await using var conn = await db.OpenAsync();
            var result = await ComputePayroll(conn, employeeId, period, null);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Trạng thái hiện tại + toàn bộ dòng thời gian của một phiếu, kể cả bản nháp.
        // Chỉ người có quyền quản trị bảng lương được xem vì snapshot chứa số tiền chi tiết.
        g.MapGet("/payslips/history", async (ClaimsPrincipal u, Database db, Guid employeeId, string period) =>
        {
            if (!u.Can(Permissions.PayrollRead)) return Results.Forbid();
            if (employeeId == Guid.Empty || !ValidPeriod(period))
                return Results.BadRequest(new { message = "Thiếu nhân viên hoặc kỳ lương (yyyy-MM)." });

            await using var conn = await db.OpenAsync();
            var current = await ReadPayslipState(conn, employeeId, period);
            var history = await ReadPayslipHistory(conn, employeeId, period);
            object? payslip = current is null ? null : PayslipStatePayload(current);
            return Results.Ok(new { payslip, history });
        });

        // Lập (hoặc cập nhật) phiếu lương cho kỳ từ dữ liệu đã tính; adjustments là các khoản điều chỉnh thủ công.
        g.MapPost("/payslips", async (CreatePayslipReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.PayrollManage)) return Results.Forbid();
            if (req.EmployeeId == Guid.Empty || !ValidPeriod(req.Period))
                return Results.BadRequest(new { message = "Thiếu nhân viên hoặc kỳ lương (yyyy-MM)." });
            await using var conn = await db.OpenAsync();
            // Ngày tăng ca admin đã duyệt (yyyy-MM-dd). null = duyệt tất cả; mảng rỗng = không duyệt ngày nào.
            HashSet<DateOnly>? approvedOt = req.ApprovedOvertimeDates is null
                ? null
                : req.ApprovedOvertimeDates
                    .Select(s => DateOnly.TryParse(s, out var dd) ? (DateOnly?)dd : null)
                    .Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
            var result = await ComputePayroll(conn, req.EmployeeId, req.Period, req.Adjustments, approvedOt);
            if (result is null) return Results.NotFound();

            var detailsJson = JsonSerializer.Serialize(result.Details);
            await using var tx = await conn.BeginTransactionAsync();
            await LockPayslipKey(conn, req.EmployeeId, req.Period);
            var before = await ReadPayslipState(conn, req.EmployeeId, req.Period, lockRow: true);
            if (!req.Published && before is not null)
            {
                var cancel = await PayoutVoucherEndpoints.CancelPayslipVoucherForUnpublishAsync(
                    conn, before.Id, u.Username());
                if (cancel == PayoutVoucherEndpoints.PayslipVoucherCancelResult.Blocked)
                {
                    await tx.RollbackAsync();
                    return Results.Conflict(new
                    {
                        message = "Không thể chuyển phiếu lương về nháp vì phiếu chi liên quan đã được duyệt hoặc đã chi.",
                    });
                }
            }
            // RETURNING id: lập lại phiếu của kỳ cũ thì DO UPDATE trả về id của DÒNG ĐANG CÓ, không phải
            // guid vừa sinh — phiếu chi lương bám theo id này nên phải là id thật.
            var pid = (Guid)(await conn.Cmd("""
                INSERT INTO hr_payslips (id, employee_id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, details, published, created_by, updated_by, updated_at, published_at, acknowledged_at)
                VALUES (@id, @emp, @period, @wd, @ot, @base, @allow, @otp, @ded, @net, @note, @details::jsonb, @pub, @by, @by, CURRENT_TIMESTAMP, CASE WHEN @pub THEN CURRENT_TIMESTAMP ELSE NULL END, NULL)
                ON CONFLICT (employee_id, period) DO UPDATE SET
                    work_days=@wd, overtime_hours=@ot, base_salary=@base, allowance=@allow,
                    overtime_pay=@otp, deductions=@ded, net_pay=@net, note=@note, details=@details::jsonb,
                    published=@pub, updated_by=@by, updated_at=CURRENT_TIMESTAMP,
                    published_at=CASE WHEN @pub THEN CURRENT_TIMESTAMP ELSE NULL END, acknowledged_at=NULL
                RETURNING id
                """)
                .With("@id", Guid.NewGuid()).With("@emp", req.EmployeeId).With("@period", req.Period)
                .With("@wd", (decimal)result.WorkedDays).With("@ot", result.OvertimeHours)
                .With("@base", result.BaseSalary).With("@allow", result.Allowance).With("@otp", result.OvertimePay)
                .With("@ded", result.TotalDeductions).With("@net", result.NetPay)
                .With("@note", req.Note ?? "").With("@details", detailsJson).With("@pub", req.Published)
                .With("@by", u.Username())
                .ExecuteScalarAsync())!;

            if (req.Published)
            {
                // Ghi sổ tiền phạt THỰC trừ của kỳ (nguồn sự thật "đã thu bao nhiêu"); đánh "Đã tất toán" nếu đủ.
                await PenaltyEndpoints.RecordDeductionsAsync(conn, req.EmployeeId, req.Period, result.PenaltyLines);
                // Đánh dấu các khoản hoàn "cộng vào lương" đã áp dụng vào phiếu kỳ này (đúng các dòng vừa cộng ở trên).
                await conn.Cmd("""
                    UPDATE hr_penalty_refunds SET status='Paid', applied_period=@period, decided_at=CURRENT_TIMESTAMP
                    WHERE employee_id=@emp AND status='Approved' AND payout_method='payroll' AND applied_period=''
                    """).With("@emp", req.EmployeeId).With("@period", req.Period).ExecuteNonQueryAsync();
            }
            else
            {
                // Bản nháp: không ghi nhận thực thu (xóa sổ kỳ này nếu có), chưa "tiêu" khoản hoàn.
                await PenaltyEndpoints.ClearDeductionsForPeriod(conn, req.EmployeeId, req.Period);
            }

            // Phát hành phiếu lương = tiền lương sắp được trao tay → sinh phiếu chi để người nhận ký nhận
            // bằng QR. Phiếu nháp (chưa phát hành) không sinh gì.
            if (req.Published)
                await PayoutVoucherEndpoints.SyncPayslipVoucherAsync(conn, pid, req.EmployeeId, req.Period,
                    result.NetPay, u.Username());

            var after = await ReadPayslipState(conn, pid);
            var action = PayslipAction(before?.Status, after!.Status);
            await AppendPayslipHistory(conn, after, before?.Status, action, u.Username());
            await tx.CommitAsync();

            await Signal(db, u, req.EmployeeId, "Lập phiếu lương", "Payslip");
            return Results.Ok(new { id = pid, netPay = result.NetPay, status = after.Status });
        });

        // ---------------- Xuất Excel toàn công ty ----------------
        // Một file .xlsx: sheet "Tổng hợp" + mỗi nhân viên một sheet bảng công tháng +
        // sheet "Phiếu lương" xếp 6 phiếu/khổ A4 để in.
        g.MapGet("/export", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            if (!u.Can(Permissions.PayrollRead)) return Results.Forbid();
            var period = NormalizePeriod(month);
            await using var conn = await db.OpenAsync();
            var bytes = await BuildExportWorkbook(conn, period);
            var fileName = $"BangCong_PhieuLuong_{period}.xlsx";
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        });
    }

    // ---- Đọc chi tiết phiếu lương đã lưu (jsonb) cho màn "Phiếu lương của tôi" ----

    internal sealed record PayslipAuditState(
        Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeCode, string EmployeeUsername,
        string Period, decimal WorkDays, decimal OvertimeHours, decimal BaseSalary, decimal Allowance,
        decimal OvertimePay, decimal Deductions, decimal NetPay, string Note, bool Published,
        DateTime CreatedAt, DateTime UpdatedAt, DateTime? AcknowledgedAt, string CreatedBy, string UpdatedBy,
        string SnapshotJson, string SummaryJson)
    {
        public string Status => !Published ? "Draft" : AcknowledgedAt is not null ? "Acknowledged" : "Published";
    }

    internal sealed record PendingPayslipRequirement(
        Guid Id, string Period, DateTime PublishedAt, DateTime UpdatedAt, DateTime DueAt, bool Overdue,
        int PendingCount, int OverdueCount);

    /// <summary>
    /// Phiếu chưa xác nhận cấp bách nhất của một tài khoản. Hạn được tính theo ngày tại Việt Nam, không phải
    /// cộng cứng 24/48 giờ: phát hành bất kỳ lúc nào trong ngày D thì khóa từ 00:00 ngày D+2.
    /// </summary>
    internal static async Task<PendingPayslipRequirement?> ReadPendingPayslipRequirement(
        NpgsqlConnection conn, string username, bool overdueOnly = false)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        await using var r = await conn.Cmd("""
            WITH pending AS (
                SELECT p.id, p.period, p.published_at, p.updated_at,
                       (((p.published_at AT TIME ZONE @tz)::date + 2)::timestamp AT TIME ZONE @tz) AS due_at
                  FROM hr_payslips p
                  JOIN hr_employees e ON e.id=p.employee_id
                 WHERE lower(e.username)=lower(@username)
                   AND p.published=TRUE
                   AND p.acknowledged_at IS NULL
                   AND p.published_at IS NOT NULL
            ), counted AS (
                SELECT *,
                       COUNT(*) OVER ()::int AS pending_count,
                       COUNT(*) FILTER (WHERE due_at <= CURRENT_TIMESTAMP) OVER ()::int AS overdue_count
                  FROM pending
            )
            SELECT id, period, published_at, updated_at, due_at,
                   due_at <= CURRENT_TIMESTAMP AS overdue, pending_count, overdue_count
              FROM counted
             WHERE (NOT @overdue_only) OR due_at <= CURRENT_TIMESTAMP
             ORDER BY (due_at <= CURRENT_TIMESTAMP) DESC, due_at, period
             LIMIT 1
            """)
            .With("@tz", "Asia/Ho_Chi_Minh")
            .With("@username", username.Trim())
            .With("@overdue_only", overdueOnly)
            .ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new PendingPayslipRequirement(
            r.Guid("id"), r.Str("period"), r.Dt("published_at"), r.Dt("updated_at"),
            r.Dt("due_at"), r.Bool("overdue"),
            r.Int("pending_count"), r.Int("overdue_count"));
    }

    // JSON date của ứng dụng được cấu hình hiển thị đến mili-giây, trong khi PostgreSQL giữ micro-giây.
    // Gửi ticks dạng chuỗi làm opaque revision để so sánh chính xác, không phụ thuộc timezone/format.
    private static string PayslipRevisionToken(DateTime updatedAt) =>
        DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc).Ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Khóa logic theo nhân viên+kỳ, kể cả khi chưa có dòng hr_payslips. Nhờ vậy hai request lập phiếu
    /// đầu tiên đồng thời không thể cùng tự nhận là revision 1 / sự kiện tạo mới.
    /// </summary>
    internal static async Task LockPayslipKey(NpgsqlConnection conn, Guid employeeId, string period)
    {
        await conn.Cmd("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))")
            .With("@key", $"payslip:{employeeId:N}:{period}").ExecuteScalarAsync();
    }

    internal static async Task<PayslipAuditState?> ReadPayslipState(
        NpgsqlConnection conn, Guid employeeId, string period, bool lockRow = false)
        => await ReadPayslipStateCore(conn, "p.employee_id=@key AND p.period=@period", employeeId, period, lockRow);

    internal static async Task<PayslipAuditState?> ReadPayslipState(
        NpgsqlConnection conn, Guid payslipId, bool lockRow = false)
        => await ReadPayslipStateCore(conn, "p.id=@key", payslipId, null, lockRow);

    private static async Task<PayslipAuditState?> ReadPayslipStateCore(
        NpgsqlConnection conn, string predicate, Guid key, string? period, bool lockRow)
    {
        // FOR UPDATE chỉ khóa bảng p; LEFT JOIN nhân viên không cần/không được khóa.
        var sql = $"""
            SELECT p.id, p.employee_id, COALESCE(e.full_name,'') AS employee_name,
                   COALESCE(e.employee_code,'') AS employee_code, COALESCE(e.username,'') AS employee_username,
                   p.period, p.work_days, p.overtime_hours, p.base_salary, p.allowance, p.overtime_pay,
                   p.deductions, p.net_pay, p.note, p.published, p.created_at, p.updated_at,
                   p.acknowledged_at, p.created_by, p.updated_by,
                   jsonb_build_object(
                       'workDays',p.work_days,'overtimeHours',p.overtime_hours,'baseSalary',p.base_salary,
                       'allowance',p.allowance,'overtimePay',p.overtime_pay,'deductions',p.deductions,
                       'netPay',p.net_pay,'note',p.note,'details',p.details,'published',p.published,
                       'acknowledgedAt',p.acknowledged_at)::text AS snapshot,
                   jsonb_build_object(
                       'netPay',p.net_pay,'totalEarnings',p.net_pay+p.deductions,
                       'totalDeductions',p.deductions,'published',p.published,'note',p.note)::text AS summary
            FROM hr_payslips p
            LEFT JOIN hr_employees e ON e.id=p.employee_id
            WHERE {predicate}
            {(lockRow ? "FOR UPDATE OF p" : "")}
            """;
        var cmd = conn.Cmd(sql).With("@key", key);
        if (period is not null) cmd.With("@period", period);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new PayslipAuditState(
            r.Guid("id"), r.Guid("employee_id"), r.Str("employee_name"), r.Str("employee_code"),
            r.Str("employee_username"), r.Str("period"), r.Dec("work_days"), r.Dec("overtime_hours"),
            r.Dec("base_salary"), r.Dec("allowance"), r.Dec("overtime_pay"), r.Dec("deductions"),
            r.Dec("net_pay"), r.Str("note"), r.Bool("published"), r.Dt("created_at"), r.Dt("updated_at"),
            r.DtNull("acknowledged_at"), r.Str("created_by"), r.Str("updated_by"),
            r.Str("snapshot"), r.Str("summary"));
    }

    internal static async Task AppendPayslipHistory(
        NpgsqlConnection conn, PayslipAuditState state, string? statusBefore, string action, string actor,
        string? statusAfter = null)
    {
        await conn.Cmd("""
            INSERT INTO hr_payslip_history
                (id, payslip_id, employee_id, employee_name, employee_code, period, revision,
                 action, status_before, status_after, actor, occurred_at, summary, snapshot)
            VALUES
                (@id, @pid, @emp, @name, @code, @period,
                 COALESCE((SELECT MAX(revision)+1 FROM hr_payslip_history
                           WHERE employee_id=@emp AND period=@period),1),
                 @action, @before, @after, @actor, CURRENT_TIMESTAMP, @summary::jsonb, @snapshot::jsonb)
            """)
            .With("@id", Guid.NewGuid()).With("@pid", state.Id).With("@emp", state.EmployeeId)
            .With("@name", state.EmployeeName).With("@code", state.EmployeeCode).With("@period", state.Period)
            .With("@action", action).With("@before", (object?)statusBefore ?? DBNull.Value)
            .With("@after", statusAfter ?? state.Status)
            .With("@actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim())
            .With("@summary", state.SummaryJson).With("@snapshot", state.SnapshotJson)
            .ExecuteNonQueryAsync();
    }

    internal static string PayslipAction(string? before, string after)
    {
        if (before is null) return after == "Draft" ? "DraftCreated" : "PublishedCreated";
        if (after == "Draft") return before == "Draft" ? "DraftUpdated" : "ReturnedToDraft";
        if (before == "Draft") return "Published";
        if (before == "Acknowledged") return "PublishedRevised";
        return "PublishedUpdated";
    }

    private static object PayslipStatePayload(PayslipAuditState p) => new
    {
        id = p.Id,
        employeeId = p.EmployeeId,
        employeeName = p.EmployeeName,
        employeeCode = p.EmployeeCode,
        p.Period,
        p.Status,
        p.Published,
        p.NetPay,
        p.Note,
        p.CreatedAt,
        p.UpdatedAt,
        p.AcknowledgedAt,
        p.CreatedBy,
        p.UpdatedBy,
    };

    private static async Task<List<object>> ReadPayslipHistory(NpgsqlConnection conn, Guid employeeId, string period)
    {
        var result = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT id, payslip_id, employee_id, employee_name, employee_code, period, revision,
                   action, status_before, status_after, actor, occurred_at,
                   summary::text AS summary, snapshot::text AS snapshot
            FROM hr_payslip_history
            WHERE employee_id=@emp AND period=@period
            ORDER BY revision DESC, occurred_at DESC
            """).With("@emp", employeeId).With("@period", period).ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(new
            {
                id = r.Guid("id"),
                payslipId = r.Guid("payslip_id"),
                employeeId = r.Guid("employee_id"),
                employeeName = r.Str("employee_name"),
                employeeCode = r.Str("employee_code"),
                period = r.Str("period"),
                revision = r.Int("revision"),
                action = r.Str("action"),
                statusBefore = r.IsDBNull(r.GetOrdinal("status_before")) ? null : r.Str("status_before"),
                statusAfter = r.Str("status_after"),
                actor = r.Str("actor"),
                occurredAt = r.Dt("occurred_at"),
                summary = ParseJsonElement(r.Str("summary")),
                snapshot = ParseJsonElement(r.Str("snapshot")),
            });
        return result;
    }

    private static JsonElement ParseJsonElement(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone(); }
        catch { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    private sealed record PayslipDetail(List<object> Earnings, List<object> Deductions, List<object> OvertimeDays,
        decimal WorkedDays, decimal AbsentDays, decimal LateDays, decimal OvertimeHours,
        decimal TotalWorkedHours, decimal OvertimeRate,
        decimal TotalEarnings, decimal TotalDeductions, decimal NetPay);

    private static byte[] SimplePdf(IEnumerable<string> lines)
    {
        static string Safe(string value) => value.Replace("\\", "/").Replace("(", "[").Replace(")", "]");
        var text = string.Join("\n", lines.Select((x, i) => $"BT /F1 12 Tf 50 {780-i*24} Td ({Safe(x)}) Tj ET"));
        var objects = new[] { "1 0 obj<< /Type /Catalog /Pages 2 0 R>>endobj", "2 0 obj<< /Type /Pages /Kids[3 0 R] /Count 1>>endobj", "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox[0 0 595 842] /Resources<< /Font<< /F1 5 0 R>>>> /Contents 4 0 R>>endobj", $"4 0 obj<< /Length {System.Text.Encoding.ASCII.GetByteCount(text)}>>stream\n{text}\nendstream endobj", "5 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica>>endobj" };
        var sb = new System.Text.StringBuilder("%PDF-1.4\n"); var offsets = new List<int>{0};
        foreach(var o in objects){offsets.Add(System.Text.Encoding.ASCII.GetByteCount(sb.ToString()));sb.Append(o).Append('\n');}
        var xref=System.Text.Encoding.ASCII.GetByteCount(sb.ToString());sb.Append($"xref\n0 {objects.Length+1}\n0000000000 65535 f \n");
        for(var i=1;i<offsets.Count;i++)sb.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");
        sb.Append($"trailer<< /Size {objects.Length+1} /Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }
    public record PayslipInquiryReq(string? LineLabel,string? Message);

    /// <summary>Phân giải cột details (jsonb) của phiếu lương ra khoản cộng/trừ + số liệu bảng công + các tổng.</summary>
    private static PayslipDetail ParsePayslipDetail(string json)
    {
        var earnings = new List<object>();
        var deductions = new List<object>();
        var overtimeDays = new List<object>();
        decimal workedDays = 0, absentDays = 0, lateDays = 0, overtimeHours = 0;
        decimal totalWorkedHours = 0, overtimeRate = 0;
        decimal totalEarnings = 0, totalDeductions = 0, netPay = 0;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            earnings = ReadPayLines(root, "earnings");
            deductions = ReadPayLines(root, "deductions");
            if (root.TryGetProperty("timesheet", out var ts) && ts.ValueKind == JsonValueKind.Object)
            {
                workedDays = NumProp(ts, "workedDays");
                absentDays = NumProp(ts, "absentDays");
                lateDays = NumProp(ts, "lateDays");
                overtimeHours = NumProp(ts, "overtimeHours");
                totalWorkedHours = NumProp(ts, "totalWorkedHours");
            }
            overtimeRate = NumProp(root, "overtimeRate");
            overtimeDays = ReadOvertimeDays(root);
            totalEarnings = NumProp(root, "totalEarnings");
            totalDeductions = NumProp(root, "totalDeductions");
            netPay = NumProp(root, "netPay");
        }
        catch { /* details hỏng → trả rỗng, số liệu lấy từ cột phiếu */ }
        return new PayslipDetail(earnings, deductions, overtimeDays, workedDays, absentDays, lateDays, overtimeHours,
            totalWorkedHours, overtimeRate,
            totalEarnings, totalDeductions, netPay);
    }

    private static List<object> ReadOvertimeDays(JsonElement root)
    {
        var list = new List<object>();
        if (!root.TryGetProperty("overtimeDays", out var days) || days.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var day in days.EnumerateArray())
        {
            list.Add(new
            {
                date = day.TryGetProperty("date", out var date) ? date.GetString() ?? "" : "",
                checkIn = day.TryGetProperty("checkIn", out var checkIn) ? checkIn.GetString() ?? "" : "",
                checkOut = day.TryGetProperty("checkOut", out var checkOut) && checkOut.ValueKind != JsonValueKind.Null
                    ? checkOut.GetString() ?? ""
                    : "",
                minutes = day.TryGetProperty("minutes", out var minutes) && minutes.TryGetInt32(out var value)
                    ? value
                    : 0,
            });
        }
        return list;
    }

    private static List<object> ReadPayLines(JsonElement root, string prop)
    {
        var list = new List<object>();
        if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                list.Add(new
                {
                    label = e.TryGetProperty("label", out var l) ? (l.GetString() ?? "") : "",
                    amount = e.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDecimal() : 0m,
                });
        return list;
    }

    private static decimal NumProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;

    // ---- Tính lương ----

    private sealed record PayLine(string Label, decimal Amount);

    /// <summary>Một ngày có tăng ca trước 08:00 hoặc sau 17:00 — để admin duyệt khi lập phiếu.</summary>
    private sealed record OtDay(DateOnly Date, string CheckIn, string? CheckOut, int Minutes);

    // Sử dụng số phút đã được bảng công tính theo cùng một quy tắc để bảng công và bảng lương không lệch nhau.
    private static List<OtDay> DetectOvertimeDays(List<ShiftEndpoints.TimesheetDayInfo> days)
    {
        var list = new List<OtDay>();
        foreach (var d in days)
        {
            if (d.OvertimeMinutes <= 0 || string.IsNullOrWhiteSpace(d.CheckIn)) continue;
            list.Add(new OtDay(d.Date, d.CheckIn, d.CheckOut, d.OvertimeMinutes));
        }
        return list;
    }

    private sealed record PayrollResult(
        Guid EmployeeId, string EmployeeName, string EmployeeCode, string Period,
        decimal BaseSalary, decimal Allowance, decimal OvertimeRate, decimal OvertimePay,
        int WorkedDays, int AbsentDays, int LateDays, decimal OvertimeHours,
        List<PayLine> Earnings, List<PayLine> Deductions,
        decimal TotalEarnings, decimal TotalDeductions, decimal NetPay,
        List<OtDay> OvertimeDays, List<PenaltyEndpoints.PenaltyDeductionLine> PenaltyLines, object Details,
        object HardSalary);

    /// <summary>
    /// Tính toàn bộ phiếu lương cho (nhân viên, kỳ). Trả null nếu không tìm thấy nhân viên.
    /// <paramref name="approvedOtDates"/>: các ngày tăng ca được admin duyệt; null = tính TẤT CẢ ngày phát hiện
    /// (dùng cho lương dự tính của nhân viên và bản xem trước).
    /// Lưu ý: <c>Earnings</c> KHÔNG chứa dòng tăng ca (để giao diện admin cộng theo ngày đã duyệt);
    /// tổng <c>TotalEarnings</c> và <c>Details.earnings</c> thì ĐÃ gồm tăng ca đã duyệt.
    /// </summary>
    private static async Task<PayrollResult?> ComputePayroll(NpgsqlConnection conn, Guid employeeId, string period,
        SalaryComponentDto[]? adjustments, HashSet<DateOnly>? approvedOtDates = null)
    {
        string empName = "", empCode = "";
        await using (var r = await conn.Cmd("SELECT full_name, employee_code FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return null;
            empName = r.Str("full_name");
            empCode = r.Str("employee_code");
        }

        var salary = await ReadSalary(conn, employeeId);
        // Lương cứng KHÔNG lấy từ ô nhập tay nữa: hợp đồng + các kỳ tăng lương mới là nguồn chuẩn.
        var hard = await ResolveHardSalaryAsync(conn, employeeId, period, salary.BaseSalary);
        var baseSalary = hard.Amount;
        var (ts, tsDays) = await ShiftEndpoints.ComputeDaysAsync(conn, employeeId, period);

        // Tăng ca trước 08:00 và sau 17:00, mỗi khoảng tối thiểu 15 phút.
        // null = tính tất cả; ngược lại chỉ các ngày đã duyệt.
        var otCandidates = DetectOvertimeDays(tsDays);
        var approvedOvertimeDays = otCandidates
            .Where(o => approvedOtDates is null || approvedOtDates.Contains(o.Date))
            .ToList();
        var otMinutes = approvedOvertimeDays.Sum(o => o.Minutes);
        var overtimeHours = Math.Round(otMinutes / 60m, 2);
        var overtimePay = Math.Round(salary.OvertimeRate * otMinutes / 60m, 0);

        // Earnings KHÔNG gồm tăng ca (giao diện admin sẽ tự cộng theo ngày duyệt).
        var earnings = new List<PayLine>
        {
            new(hard.RaiseTotal != 0 ? "Lương cứng (HĐ + tăng lương)" : "Lương cơ bản", baseSalary),
        };
        if (salary.Allowance != 0) earnings.Add(new("Phụ cấp", salary.Allowance));

        var deductions = new List<PayLine>();
        foreach (var c in salary.Components)
        {
            if (c.Kind == "deduction") deductions.Add(new(c.Label, c.Amount));
            else earnings.Add(new(c.Label, c.Amount));
        }

        // Điều chỉnh thủ công khi lập phiếu.
        if (adjustments is not null)
            foreach (var a in adjustments)
            {
                var label = string.IsNullOrWhiteSpace(a.Label) ? "Khoản khác" : a.Label!.Trim();
                if ((a.Kind ?? "earning") == "deduction") deductions.Add(new(label, a.Amount));
                else earnings.Add(new(label, a.Amount));
            }

        // Hoàn tiền phạt đã được kế toán duyệt (hình thức "cộng vào lương"), chưa áp dụng vào phiếu nào → cộng thu nhập.
        // Tính TRƯỚC phạt để khoản hoàn cũng được kể vào lương còn có thể trừ.
        await using (var rr = await conn.Cmd("""
            SELECT penalty_no, amount FROM hr_penalty_refunds
            WHERE employee_id=@emp AND status='Approved' AND payout_method='payroll' AND applied_period=''
            ORDER BY created_at
            """).With("@emp", employeeId).ExecuteReaderAsync())
        {
            while (await rr.ReadAsync())
                earnings.Add(new($"Hoàn tiền phạt {rr.Str("penalty_no")}", rr.Dec("amount")));
        }

        // Tiền phạt trong kỳ — CAP theo lương còn lại (tổng thu nhập gồm tăng ca đã duyệt + hoàn phạt,
        // trừ đi các khấu trừ KHÁC). Phạt không được làm âm lương; phần chưa thu tự chuyển sang kỳ sau (sổ cái).
        var availableForPenalties = Math.Max(0m,
            earnings.Sum(e => e.Amount) + overtimePay - deductions.Sum(e => e.Amount));
        var (penaltyTotal, penaltyItems) = await PenaltyEndpoints.ComputeDeductionsAsync(
            conn, employeeId, period, availableForPenalties);
        foreach (var p in penaltyItems)
        {
            var label = $"Phạt {p.PenaltyNo}" + (p.Installments > 1 ? $" (đợt {p.InstallmentNo}/{p.Installments})" : "")
                + (string.IsNullOrWhiteSpace(p.Reason) ? "" : $" · {p.Reason}");
            deductions.Add(new(label, p.MonthAmount));
        }

        var totalEarnings = earnings.Sum(e => e.Amount) + overtimePay;
        var totalDeductions = deductions.Sum(e => e.Amount);
        var net = totalEarnings - totalDeductions;

        // Dòng lương đầy đủ để LƯU/HIỂN THỊ phiếu: gồm cả tăng ca (chèn ngay sau Phụ cấp/Lương cơ bản).
        var detailEarnings = new List<PayLine>(earnings);
        if (overtimePay != 0)
        {
            var otLine = new PayLine($"Tăng ca ({overtimeHours} giờ)", overtimePay);
            var insertAt = salary.Allowance != 0 ? 2 : 1;
            detailEarnings.Insert(Math.Min(insertAt, detailEarnings.Count), otLine);
        }

        var details = new
        {
            earnings = detailEarnings.ConvertAll(e => new { label = e.Label, amount = e.Amount }),
            deductions = deductions.ConvertAll(e => new { label = e.Label, amount = e.Amount }),
            timesheet = new
            {
                workedDays = ts.WorkedDays,
                absentDays = ts.AbsentDays,
                lateDays = ts.LateDays,
                overtimeHours,
                totalWorkedHours = ts.TotalWorkedHours,
            },
            // Chỉ lưu các ngày đã được duyệt vào phiếu. Danh sách ứng viên đầy đủ vẫn được trả ở
            // PayrollResult.OvertimeDays để màn lập lương cho phép quản trị chọn/bỏ chọn từng ngày.
            overtimeDays = approvedOvertimeDays.ConvertAll(o => new { date = o.Date, checkIn = o.CheckIn, checkOut = o.CheckOut, minutes = o.Minutes }),
            overtimeRate = salary.OvertimeRate,
            hardSalary = HardSalaryPayload(hard),
            penaltyTotal,
            totalEarnings,
            totalDeductions,
            netPay = net,
        };

        return new PayrollResult(
            employeeId, empName, empCode, period,
            baseSalary, salary.Allowance, salary.OvertimeRate, overtimePay,
            ts.WorkedDays, ts.AbsentDays, ts.LateDays, overtimeHours,
            earnings, deductions, totalEarnings, totalDeductions, net, otCandidates, penaltyItems, details,
            HardSalaryPayload(hard));
    }

    // ---- Mức lương ----

    private sealed record SalaryComponent(string Label, decimal Amount, string Kind);

    private sealed record SalaryData(Guid EmployeeId, bool HasSalary, decimal BaseSalary, decimal Allowance,
        decimal OvertimeRate, List<SalaryComponent> Components, string Note);

    private static async Task<SalaryData> ReadSalary(NpgsqlConnection conn, Guid employeeId)
    {
        await using var r = await conn.Cmd("""
            SELECT base_salary, allowance, overtime_rate, components::text AS components, note
            FROM hr_salaries WHERE employee_id=@id
            """).With("@id", employeeId).ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return new SalaryData(employeeId, false, 0, 0, 0, new List<SalaryComponent>(), "");
        return new SalaryData(employeeId, true, r.Dec("base_salary"), r.Dec("allowance"), r.Dec("overtime_rate"),
            ParseComponents(r.Str("components")), r.Str("note"));
    }

    // ---- Lương cứng lấy từ hợp đồng + các kỳ tăng lương ----

    internal sealed record HardSalaryRaise(string Period, decimal Amount, string DecisionNo, string Reason);

    /// <summary>
    /// Lương cứng của một kỳ. Nguồn chuẩn là HỢP ĐỒNG: lương cơ bản đã ký + mọi lần tăng lương có
    /// hiệu lực từ kỳ đó trở về trước. <see cref="FromContract"/> = false nghĩa là nhân viên chưa có
    /// hợp đồng nào — khi đó rơi về con số nhập tay cũ ở hr_salaries để phiếu lương cũ không bị về 0.
    /// </summary>
    internal sealed record HardSalaryInfo(
        decimal Amount, bool FromContract, Guid? ContractId, string ContractNo, string ContractType,
        decimal ContractBase, decimal RaiseTotal, bool ContractEffective, DateOnly? ContractEndDate,
        List<HardSalaryRaise> Raises);

    private static (DateOnly Start, DateOnly End) PeriodRange(string period)
    {
        var start = new DateOnly(int.Parse(period[..4]), int.Parse(period.Substring(5, 2)), 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// Chọn hợp đồng chi phối kỳ lương rồi cộng các lần tăng của chính hợp đồng đó. Ưu tiên hợp đồng
    /// còn hiệu lực trong kỳ; nếu không có thì lấy hợp đồng gần nhất đã bắt đầu — để hợp đồng vừa hết
    /// hạn chờ ký lại không làm lương của kỳ đang tính tụt về 0.
    /// </summary>
    private static async Task<HardSalaryInfo> ResolveHardSalaryAsync(
        NpgsqlConnection conn, Guid employeeId, string period, decimal legacyBase)
    {
        var (pStart, pEnd) = PeriodRange(period);
        const string effectiveSql =
            "(status='Active' AND (start_date IS NULL OR start_date <= @pEnd) AND (end_date IS NULL OR end_date >= @pStart))";

        Guid? contractId = null;
        string contractNo = "", contractType = "";
        decimal contractBase = 0;
        DateOnly? contractEnd = null;
        var effective = false;

        await using (var r = await conn.Cmd($"""
            SELECT id, contract_no, contract_type, base_salary, end_date, {effectiveSql} AS effective
            FROM hr_contracts WHERE employee_id=@emp
            ORDER BY {effectiveSql} DESC,
                     (start_date IS NULL OR start_date <= @pEnd) DESC,
                     start_date DESC NULLS LAST, created_at DESC
            LIMIT 1
            """).With("@emp", employeeId).With("@pStart", pStart).With("@pEnd", pEnd).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                contractId = r.Guid("id");
                contractNo = r.Str("contract_no");
                contractType = r.Str("contract_type");
                contractBase = r.Dec("base_salary");
                contractEnd = r.IsDBNull(r.GetOrdinal("end_date")) ? null : r.DateOnly("end_date");
                effective = r.Bool("effective");
            }
        }

        if (contractId is null)
            return new HardSalaryInfo(legacyBase, false, null, "", "", 0, 0, false, null, new List<HardSalaryRaise>());

        var raises = new List<HardSalaryRaise>();
        await using (var r = await conn.Cmd("""
            SELECT effective_period, amount, decision_no, reason FROM hr_salary_raises
            WHERE contract_id=@c AND effective_period <= @period
            ORDER BY effective_period, created_at
            """).With("@c", contractId).With("@period", period).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                raises.Add(new HardSalaryRaise(r.Str("effective_period"), r.Dec("amount"), r.Str("decision_no"), r.Str("reason")));
        }

        var raiseTotal = raises.Sum(x => x.Amount);
        return new HardSalaryInfo(contractBase + raiseTotal, true, contractId, contractNo, contractType,
            contractBase, raiseTotal, effective, contractEnd, raises);
    }

    private static object HardSalaryPayload(HardSalaryInfo h) => new
    {
        amount = h.Amount,
        fromContract = h.FromContract,
        contractId = h.ContractId,
        contractNo = h.ContractNo,
        contractType = h.ContractType,
        contractBase = h.ContractBase,
        raiseTotal = h.RaiseTotal,
        contractEffective = h.ContractEffective,
        contractEndDate = h.ContractEndDate,
        raises = h.Raises.ConvertAll(x => new { period = x.Period, amount = x.Amount, decisionNo = x.DecisionNo, reason = x.Reason }),
    };

    private static List<SalaryComponent> ParseComponents(string json)
    {
        var list = new List<SalaryComponent>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var label = el.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(label)) continue;
                decimal amount = 0;
                if (el.TryGetProperty("amount", out var a))
                {
                    if (a.ValueKind == JsonValueKind.Number) a.TryGetDecimal(out amount);
                    else if (a.ValueKind == JsonValueKind.String) decimal.TryParse(a.GetString(), out amount);
                }
                var kind = el.TryGetProperty("kind", out var k) ? (k.GetString() ?? "earning") : "earning";
                list.Add(new SalaryComponent(label.Trim(), amount, kind == "deduction" ? "deduction" : "earning"));
            }
        }
        catch { /* jsonb hỏng → bỏ qua */ }
        return list;
    }

    private static string SerializeComponents(SalaryComponentDto[]? components)
    {
        var clean = (components ?? Array.Empty<SalaryComponentDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Label))
            .Select(c => new { label = c.Label!.Trim(), amount = c.Amount, kind = (c.Kind ?? "earning") == "deduction" ? "deduction" : "earning" });
        return JsonSerializer.Serialize(clean);
    }

    internal static bool ValidPeriod(string? period)
        => !string.IsNullOrWhiteSpace(period) && period.Length == 7 && period[4] == '-'
           && int.TryParse(period[..4], out var y) && y is >= 1900 and <= 9999
           && int.TryParse(period.Substring(5, 2), out var m) && m is >= 1 and <= 12;

    /// <summary>
    /// Chỉ ghi audit. Trigger trên hr_salaries / hr_payslips / hr_payslip_inquiries / hr_penalty_refunds
    /// tự phát scope 'hr' sau khi commit — không gọi hub ở đây nữa (một đường duy nhất).
    /// </summary>
    private static async Task Signal(Database db, ClaimsPrincipal u, Guid employeeId, string action, string entity)
    {
        await db.RecordAudit(u.Username(), action, entity, employeeId.ToString(), $"{action} (web).");
    }

    // ---- Nhật ký một ngày (/my-day) ----

    /// <summary>Ngày làm việc chốt theo giờ Việt Nam, không theo giờ máy chủ.</summary>
    private const string DayTz = "Asia/Ho_Chi_Minh";

    private sealed record DayRequest(Guid Id, string Code, string Type, string TypeLabel, string Title,
        decimal Amount, string Status, DateTime CreatedAt, DateTime UpdatedAt);

    /// <summary>Số tiền nằm trong payload đơn — mỗi loại đơn gọi tên trường một kiểu.</summary>
    private static decimal PayloadAmount(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0m;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0m;
            foreach (var key in new[] { "amount", "advancedAmount", "spentAmount" })
            {
                if (!doc.RootElement.TryGetProperty(key, out var v)) continue;
                switch (v.ValueKind)
                {
                    case JsonValueKind.Number when v.TryGetDecimal(out var n): return n;
                    case JsonValueKind.String when decimal.TryParse(
                        v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s): return s;
                }
            }
        }
        catch (JsonException) { /* payload hỏng thì coi như không có số tiền */ }
        return 0m;
    }

    private static string TaskStatusLabel(string s) => s switch
    {
        "assigned" => "Chờ nhận",
        "in_progress" => "Đang làm",
        "submitted" => "Chờ nghiệm thu",
        "accepted" => "Đã nghiệm thu",
        "completed" => "Đã hoàn thành",
        "rejected" => "Bị trả lại",
        "cancelled" => "Đã huỷ",
        _ => s,
    };

    private static string TaskEventLabel(string kind) => kind switch
    {
        "assigned" => "được giao việc",
        "reassigned" => "chuyển người nhận",
        "updated" => "cập nhật việc",
        "started" => "bắt đầu làm",
        "progress" => "cập nhật tiến độ",
        "submitted" => "báo xong / nộp nghiệm thu",
        "accepted" => "nghiệm thu đạt",
        "completed" => "hoàn thành",
        "rejected" => "bị trả lại",
        "cancelled" => "huỷ việc",
        "comment" => "trao đổi",
        _ => kind,
    };

    private static string PenaltyStatusLabel(string s) => s switch
    {
        "Active" => "Còn hiệu lực",
        "Settled" => "Đã tất toán",
        "Waived" => "Đã xoá phạt",
        _ => s,
    };

    private static string RequestStatusLabel(string s) => s switch
    {
        "Pending" => "Chờ duyệt",
        "Approved" => "Đã duyệt",
        "Rejected" => "Từ chối",
        "Cancelled" => "Đã huỷ",
        _ => s,
    };

    private static string PayoutStatusLabel(string s) => s switch
    {
        "AwaitingScan" => "Chờ quét QR",
        "AwaitingApproval" => "Chờ duyệt",
        "Confirmed" => "Đã ký nhận · chờ duyệt",
        "Approved" => "Đã duyệt · chờ thực chi",
        "Paid" => "Đã thực chi",
        "Rejected" => "Đã từ chối",
        "Cancelled" => "Đã huỷ",
        _ => s,
    };

    // ---- Xuất Excel ----

    private static string NormalizePeriod(string? month)
    {
        if (ValidPeriod(month)) return month![..7];
        var now = DateTime.UtcNow.AddHours(7);
        return $"{now.Year:D4}-{now.Month:D2}";
    }

    private sealed record ExportEmp(Guid Id, string Name, string Code, string Dept);

    /// <summary>Dựng workbook: Tổng hợp + 1 sheet/nhân viên (bảng công) + sheet Phiếu lương (6/A4).</summary>
    private static async Task<byte[]> BuildExportWorkbook(NpgsqlConnection conn, string period)
    {
        var (year, mon) = (int.Parse(period[..4]), int.Parse(period.Substring(5, 2)));
        var monthStart = new DateOnly(year, mon, 1);
        var daysInMonth = DateTime.DaysInMonth(year, mon);
        var periodLabel = $"{mon:D2}/{year}";

        // Danh sách nhân viên đang làm việc.
        var emps = new List<ExportEmp>();
        await using (var r = await conn.Cmd("""
            SELECT e.id, e.full_name, e.employee_code, COALESCE(d.name,'') AS dept_name
            FROM hr_employees e
            LEFT JOIN hr_departments d ON d.id = e.department_id
            WHERE e.status = 'Active'
            ORDER BY d.name NULLS FIRST, e.full_name
            """).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                emps.Add(new ExportEmp(r.Guid("id"), r.Str("full_name"), r.Str("employee_code"), r.Str("dept_name")));
        }

        using var wb = new XLWorkbook();
        var overview = wb.Worksheets.Add("Tổng hợp");
        var payrolls = new List<PayrollResult>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ----- Sheet bảng công từng nhân viên -----
        var overviewRows = new List<object[]>();
        foreach (var e in emps)
        {
            var (summary, days) = await ShiftEndpoints.ComputeDaysAsync(conn, e.Id, period);
            var payroll = await ComputePayroll(conn, e.Id, period, null);
            if (payroll is not null) payrolls.Add(payroll);

            var ws = wb.Worksheets.Add(UniqueSheetName(e.Name, usedNames));
            BuildTimesheetSheet(ws, e, periodLabel, monthStart, daysInMonth, summary, days);

            overviewRows.Add(new object[]
            {
                e.Code, e.Name, e.Dept,
                summary.WorkedDays, summary.AbsentDays, summary.LateDays, summary.EarlyDays,
                Math.Round(summary.TotalOvertimeMinutes / 60.0, 2), summary.TotalWorkedHours,
                payroll?.NetPay ?? 0m,
            });
        }

        BuildOverviewSheet(overview, periodLabel, overviewRows);

        // ----- Sheet phiếu lương: 6 phiếu / khổ A4 -----
        BuildPayslipSheet(wb.Worksheets.Add("Phiếu lương"), periodLabel, payrolls);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildOverviewSheet(IXLWorksheet ws, string periodLabel, List<object[]> rows)
    {
        ws.Cell(1, 1).Value = $"TỔNG HỢP CÔNG & LƯƠNG THÁNG {periodLabel}";
        ws.Range(1, 1, 1, 10).Merge().Style.Font.SetBold().Font.SetFontSize(14)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "STT", "Mã NV", "Họ tên", "Phòng ban", "Ngày công", "Vắng",
            "Đi muộn (lần)", "Về sớm (lần)", "Tăng ca (giờ)", "Lương thực nhận" };
        var hr = 3;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8EEF7"));
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var row = hr + 1;
        for (var i = 0; i < rows.Count; i++)
        {
            var d = rows[i];
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = (string)d[0];
            ws.Cell(row, 3).Value = (string)d[1];
            ws.Cell(row, 4).Value = (string)d[2];
            ws.Cell(row, 5).Value = Convert.ToDouble(d[3]);
            ws.Cell(row, 6).Value = Convert.ToDouble(d[4]);
            ws.Cell(row, 7).Value = Convert.ToDouble(d[5]);
            ws.Cell(row, 8).Value = Convert.ToDouble(d[6]);
            ws.Cell(row, 9).Value = Convert.ToDouble(d[7]);
            ws.Cell(row, 10).Value = Convert.ToDecimal(d[9]);
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0";
            ws.Range(row, 1, row, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            row++;
        }
        ws.Columns(1, 10).AdjustToContents();
        ws.Column(3).Width = Math.Max(ws.Column(3).Width, 22);
        ws.SheetView.FreezeRows(hr);
    }

    private static void BuildTimesheetSheet(IXLWorksheet ws, ExportEmp e, string periodLabel,
        DateOnly monthStart, int daysInMonth, ShiftEndpoints.TimesheetSummary s,
        List<ShiftEndpoints.TimesheetDayInfo> days)
    {
        var byDate = days.ToDictionary(d => d.Date);

        ws.Cell(1, 1).Value = $"BẢNG CÔNG THÁNG {periodLabel}";
        ws.Range(1, 1, 1, 10).Merge().Style.Font.SetBold().Font.SetFontSize(13)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Cell(2, 1).Value = $"{e.Name}  ·  Mã: {e.Code}  ·  {e.Dept}";
        ws.Range(2, 1, 2, 10).Merge().Style.Font.SetItalic()
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var headers = new[] { "Ngày", "Thứ", "Ca làm", "Giờ vào", "Giờ ra", "Giờ làm",
            "Đi muộn (phút)", "Về sớm (phút)", "Tăng ca (phút)", "Trạng thái" };
        var hr = 4;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8EEF7"));
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var row = hr + 1;
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(monthStart.Year, monthStart.Month, day);
            byDate.TryGetValue(date, out var info);
            var isSunday = date.DayOfWeek == DayOfWeek.Sunday;

            ws.Cell(row, 1).Value = date.ToString("dd/MM");
            ws.Cell(row, 2).Value = WeekdayVi(date.DayOfWeek);
            ws.Cell(row, 3).Value = info?.ShiftName ?? "";
            ws.Cell(row, 4).Value = info?.CheckIn ?? "";
            ws.Cell(row, 5).Value = info?.CheckOut ?? "";
            if (info is { WorkedHours: > 0 } wi) ws.Cell(row, 6).Value = wi.WorkedHours;
            if (info is { LateMinutes: > 0 } li) ws.Cell(row, 7).Value = li.LateMinutes;
            if (info is { EarlyMinutes: > 0 } ei) ws.Cell(row, 8).Value = ei.EarlyMinutes;
            if (info is { OvertimeMinutes: > 0 } oi) ws.Cell(row, 9).Value = oi.OvertimeMinutes;
            ws.Cell(row, 10).Value = info?.Status ?? (isSunday ? "Nghỉ chủ nhật" : "");

            var rng = ws.Range(row, 1, row, 10);
            rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            if (isSunday) rng.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F3F4F6"));
            if (info is { Status: "Vắng" }) ws.Cell(row, 10).Style.Font.SetFontColor(XLColor.FromHtml("#B91C1C")).Font.SetBold();
            row++;
        }

        // Dòng tổng kết.
        ws.Cell(row + 1, 1).Value = "TỔNG KẾT";
        ws.Cell(row + 1, 1).Style.Font.SetBold();
        ws.Cell(row + 1, 3).Value = $"Ngày công: {s.WorkedDays}";
        ws.Cell(row + 1, 5).Value = $"Vắng: {s.AbsentDays}";
        ws.Cell(row + 1, 7).Value = $"Đi muộn: {s.LateDays} lần";
        ws.Cell(row + 1, 8).Value = $"Về sớm: {s.EarlyDays} lần";
        ws.Cell(row + 1, 9).Value = $"Tăng ca: {Math.Round(s.TotalOvertimeMinutes / 60.0, 2)} giờ";
        ws.Range(row + 1, 1, row + 1, 10).Style.Font.SetBold();

        ws.Columns(1, 10).AdjustToContents();
        ws.Column(10).Width = Math.Max(ws.Column(10).Width, 18);
        ws.SheetView.FreezeRows(hr);
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(hr, hr);
    }

    private static void BuildPayslipSheet(IXLWorksheet ws, string periodLabel, List<PayrollResult> payrolls)
    {
        // Bố cục: 2 cột phiếu × 3 hàng phiếu = 6 phiếu / trang A4.
        // Cột sheet:  A(đệm) B(nhãn) C(số tiền) D(đệm) E(nhãn) F(số tiền) G(đệm)
        // Mỗi phiếu cao 13 dòng (12 nội dung + 1 đệm).
        const int blockRows = 13;
        const int rowsPerPage = blockRows * 3; // 3 hàng phiếu mỗi trang

        ws.Column(1).Width = 2;
        ws.Column(2).Width = 22; ws.Column(3).Width = 15;
        ws.Column(4).Width = 3;
        ws.Column(5).Width = 22; ws.Column(6).Width = 15;
        ws.Column(7).Width = 2;

        for (var i = 0; i < payrolls.Count; i++)
        {
            var page = i / 6;
            var idxInPage = i % 6;      // 0..5
            var blockRow = idxInPage / 2; // 0..2 (hàng phiếu trong trang)
            var blockCol = idxInPage % 2; // 0..1 (cột trái/phải)
            var startRow = page * rowsPerPage + blockRow * blockRows + 1;
            var labelCol = blockCol == 0 ? 2 : 5;
            DrawPayslip(ws, startRow, labelCol, periodLabel, payrolls[i]);
        }

        // Ngắt trang ngang sau mỗi 3 hàng phiếu để mỗi trang in đúng 6 phiếu.
        var totalPages = (payrolls.Count + 5) / 6;
        for (var p = 1; p < totalPages; p++)
            ws.PageSetup.AddHorizontalPageBreak(p * rowsPerPage);

        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.PagesWide = 1;
        ws.PageSetup.Margins.SetTop(0.4).SetBottom(0.4).SetLeft(0.4).SetRight(0.4);
    }

    private static void DrawPayslip(IXLWorksheet ws, int startRow, int labelCol, string periodLabel, PayrollResult p)
    {
        var amtCol = labelCol + 1;
        var r = startRow;

        void Line(string label, decimal? amount, bool bold = false, bool money = true)
        {
            ws.Cell(r, labelCol).Value = label;
            if (bold) ws.Cell(r, labelCol).Style.Font.SetBold();
            if (amount is not null)
            {
                ws.Cell(r, amtCol).Value = amount.Value;
                if (money) ws.Cell(r, amtCol).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, amtCol).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                if (bold) ws.Cell(r, amtCol).Style.Font.SetBold();
            }
            r++;
        }

        // Tiêu đề
        ws.Range(startRow, labelCol, startRow, amtCol).Merge();
        ws.Cell(startRow, labelCol).Value = "PHIẾU LƯƠNG";
        ws.Cell(startRow, labelCol).Style.Font.SetBold().Font.SetFontSize(11)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#E8EEF7"));
        r++;
        ws.Range(r, labelCol, r, amtCol).Merge();
        ws.Cell(r, labelCol).Value = $"{p.EmployeeName} ({p.EmployeeCode}) · Kỳ {periodLabel}";
        ws.Cell(r, labelCol).Style.Font.SetItalic().Font.SetFontSize(9);
        r++;

        Line("Lương cơ bản", p.BaseSalary);
        Line("Phụ cấp", p.Allowance);
        Line($"Tăng ca ({p.OvertimeHours} giờ)", p.OvertimePay);
        var otherEarn = p.TotalEarnings - p.BaseSalary - p.Allowance - p.OvertimePay;
        Line("Thu nhập khác", otherEarn);
        Line("Tổng thu nhập", p.TotalEarnings, bold: true);
        Line("Tổng khấu trừ", p.TotalDeductions);
        Line($"Ngày công / vắng", null);
        ws.Cell(r - 1, amtCol).Value = $"{p.WorkedDays} / {p.AbsentDays}";
        ws.Cell(r - 1, amtCol).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        Line("THỰC NHẬN", p.NetPay, bold: true);
        ws.Cell(r - 1, labelCol).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FEF3C7"));
        ws.Cell(r - 1, amtCol).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FEF3C7"));

        // Viền quanh phiếu (12 dòng nội dung).
        ws.Range(startRow, labelCol, startRow + 11, amtCol).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
    }

    private static string WeekdayVi(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "T2",
        DayOfWeek.Tuesday => "T3",
        DayOfWeek.Wednesday => "T4",
        DayOfWeek.Thursday => "T5",
        DayOfWeek.Friday => "T6",
        DayOfWeek.Saturday => "T7",
        _ => "CN",
    };

    private static string UniqueSheetName(string name, HashSet<string> used)
    {
        var clean = new string((name ?? "NV").Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = "NV";
        if (clean.Length > 28) clean = clean[..28];
        var candidate = clean;
        var i = 2;
        while (!used.Add(candidate))
        {
            var suffix = $" ({i++})";
            candidate = clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] + suffix : clean + suffix;
        }
        return candidate;
    }

    // ---- DTO ----
    public record SalaryComponentDto(string? Label, decimal Amount, string? Kind);
    public record SaveSalaryReq(decimal BaseSalary, decimal Allowance, decimal OvertimeRate,
        SalaryComponentDto[]? Components, string? Note);
    public record CreatePayslipReq(Guid EmployeeId, string Period, bool Published,
        SalaryComponentDto[]? Adjustments, string? Note, string[]? ApprovedOvertimeDates = null);
}
