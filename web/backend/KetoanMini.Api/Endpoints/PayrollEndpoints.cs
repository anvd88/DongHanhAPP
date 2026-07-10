using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
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
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapPayroll(this WebApplication app)
    {
        var g = app.MapGroup("/api/payroll").RequireAuthorization();

        // ---------------- Mức lương theo nhân viên ----------------

        // Danh sách nhân viên kèm mức lương (admin) — cho trang bảng lương.
        g.MapGet("/salaries", async (ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT e.id, e.full_name, e.employee_code, COALESCE(d.name,'') AS dept_name,
                       s.base_salary, s.allowance, s.overtime_rate, s.components::text AS components
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id = e.department_id
                LEFT JOIN hr_salaries s ON s.employee_id = e.id
                WHERE e.status = 'Active'
                ORDER BY e.full_name
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var hasSalary = !r.IsDBNull(r.GetOrdinal("base_salary"));
                var components = hasSalary ? ParseComponents(r.Str("components")) : new List<SalaryComponent>();
                list.Add(new
                {
                    employeeId = r.Guid("id"),
                    employeeName = r.Str("full_name"),
                    employeeCode = r.Str("employee_code"),
                    departmentName = r.Str("dept_name"),
                    hasSalary,
                    baseSalary = hasSalary ? r.Dec("base_salary") : 0m,
                    allowance = hasSalary ? r.Dec("allowance") : 0m,
                    overtimeRate = hasSalary ? r.Dec("overtime_rate") : 0m,
                    extraCount = components.Count,
                });
            }
            return Results.Ok(list);
        });

        // Cấu trúc lương chi tiết của một nhân viên (admin, hoặc chính chủ xem của mình).
        g.MapGet("/salaries/{employeeId:guid}", async (Guid employeeId, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.IsAdmin())
            {
                var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", employeeId).ExecuteScalarAsync() as string;
                if (!string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            }
            return Results.Ok(await ReadSalary(conn, employeeId));
        });

        g.MapPut("/salaries/{employeeId:guid}", async (Guid employeeId, SaveSalaryReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
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
            await Signal(hub, db, conn, u, employeeId, "Cập nhật mức lương", "Salary");
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
                hasSalary = salary.HasSalary,
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
                       deductions, net_pay, note, details::text AS details, created_at
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
                    earnings = det.Earnings,
                    deductions = det.Deductions,
                    totalEarnings = det.TotalEarnings > 0 ? det.TotalEarnings : baseSalary + allowance + overtimePay,
                    totalDeductions = det.TotalDeductions > 0 ? det.TotalDeductions : colDeductions,
                    netPay = det.NetPay != 0 ? det.NetPay : colNet,
                    note = r.Str("note"),
                    createdAt = r.Dt("created_at"),
                });
            }
            return Results.Ok(list);
        });

        // ---------------- Tính & lập phiếu lương ----------------

        // Xem trước phiếu lương (chưa lưu): lấy mức lương + bảng công + phạt của kỳ.
        g.MapGet("/compute", async (ClaimsPrincipal u, Database db, Guid employeeId, string period) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (employeeId == Guid.Empty || !ValidPeriod(period))
                return Results.BadRequest(new { message = "Thiếu nhân viên hoặc kỳ lương (yyyy-MM)." });
            await using var conn = await db.OpenAsync();
            var result = await ComputePayroll(conn, employeeId, period, null);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Lập (hoặc cập nhật) phiếu lương cho kỳ từ dữ liệu đã tính; adjustments là các khoản điều chỉnh thủ công.
        g.MapPost("/payslips", async (CreatePayslipReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
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

            var pid = Guid.NewGuid();
            var detailsJson = JsonSerializer.Serialize(result.Details);
            await conn.Cmd("""
                INSERT INTO hr_payslips (id, employee_id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, details, published)
                VALUES (@id, @emp, @period, @wd, @ot, @base, @allow, @otp, @ded, @net, @note, @details::jsonb, @pub)
                ON CONFLICT (employee_id, period) DO UPDATE SET
                    work_days=@wd, overtime_hours=@ot, base_salary=@base, allowance=@allow,
                    overtime_pay=@otp, deductions=@ded, net_pay=@net, note=@note, details=@details::jsonb, published=@pub
                """)
                .With("@id", pid).With("@emp", req.EmployeeId).With("@period", req.Period)
                .With("@wd", (decimal)result.WorkedDays).With("@ot", result.OvertimeHours)
                .With("@base", result.BaseSalary).With("@allow", result.Allowance).With("@otp", result.OvertimePay)
                .With("@ded", result.TotalDeductions).With("@net", result.NetPay)
                .With("@note", req.Note ?? "").With("@details", detailsJson).With("@pub", req.Published)
                .ExecuteNonQueryAsync();

            // Đánh dấu các khoản hoàn "cộng vào lương" đã áp dụng vào phiếu kỳ này (đúng các dòng vừa cộng ở trên).
            await conn.Cmd("""
                UPDATE hr_penalty_refunds SET status='Paid', applied_period=@period, decided_at=CURRENT_TIMESTAMP
                WHERE employee_id=@emp AND status='Approved' AND payout_method='payroll' AND applied_period=''
                """).With("@emp", req.EmployeeId).With("@period", req.Period).ExecuteNonQueryAsync();

            await Signal(hub, db, conn, u, req.EmployeeId, "Lập phiếu lương", "Payslip");
            return Results.Ok(new { id = pid, netPay = result.NetPay });
        });

        // ---------------- Xuất Excel toàn công ty ----------------
        // Một file .xlsx: sheet "Tổng hợp" + mỗi nhân viên một sheet bảng công tháng +
        // sheet "Phiếu lương" xếp 6 phiếu/khổ A4 để in.
        g.MapGet("/export", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            var period = NormalizePeriod(month);
            await using var conn = await db.OpenAsync();
            var bytes = await BuildExportWorkbook(conn, period);
            var fileName = $"BangCong_PhieuLuong_{period}.xlsx";
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        });
    }

    // ---- Đọc chi tiết phiếu lương đã lưu (jsonb) cho màn "Phiếu lương của tôi" ----

    private sealed record PayslipDetail(List<object> Earnings, List<object> Deductions,
        decimal WorkedDays, decimal AbsentDays, decimal LateDays, decimal OvertimeHours,
        decimal TotalEarnings, decimal TotalDeductions, decimal NetPay);

    /// <summary>Phân giải cột details (jsonb) của phiếu lương ra khoản cộng/trừ + số liệu bảng công + các tổng.</summary>
    private static PayslipDetail ParsePayslipDetail(string json)
    {
        var earnings = new List<object>();
        var deductions = new List<object>();
        decimal workedDays = 0, absentDays = 0, lateDays = 0, overtimeHours = 0;
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
            }
            totalEarnings = NumProp(root, "totalEarnings");
            totalDeductions = NumProp(root, "totalDeductions");
            netPay = NumProp(root, "netPay");
        }
        catch { /* details hỏng → trả rỗng, số liệu lấy từ cột phiếu */ }
        return new PayslipDetail(earnings, deductions, workedDays, absentDays, lateDays, overtimeHours,
            totalEarnings, totalDeductions, netPay);
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

    /// <summary>Một ngày có tăng ca (giờ ra sau 17:20) — để admin duyệt từng ngày khi lập phiếu.</summary>
    private sealed record OtDay(DateOnly Date, string CheckOut, int Minutes);

    // Quy tắc tăng ca theo giờ RA: tính từ 17:00, nhưng chỉ tính khi tan làm SAU 17:20 (đệm 20').
    private static readonly TimeOnly OtStart = new(17, 0);
    private static readonly TimeOnly OtQualify = new(17, 20);

    private static List<OtDay> DetectOvertimeDays(List<ShiftEndpoints.TimesheetDayInfo> days)
    {
        var list = new List<OtDay>();
        foreach (var d in days)
        {
            if (string.IsNullOrWhiteSpace(d.CheckOut)) continue;
            if (!TimeOnly.TryParse(d.CheckOut, out var outTod)) continue;
            if (outTod <= OtQualify) continue; // ra ≤ 17:20 → không tính tăng ca
            var minutes = (int)(outTod - OtStart).TotalMinutes; // tính từ 17:00
            if (minutes > 0) list.Add(new OtDay(d.Date, d.CheckOut!, minutes));
        }
        return list;
    }

    private sealed record PayrollResult(
        Guid EmployeeId, string EmployeeName, string EmployeeCode, string Period,
        decimal BaseSalary, decimal Allowance, decimal OvertimeRate, decimal OvertimePay,
        int WorkedDays, int AbsentDays, int LateDays, decimal OvertimeHours,
        List<PayLine> Earnings, List<PayLine> Deductions,
        decimal TotalEarnings, decimal TotalDeductions, decimal NetPay,
        List<OtDay> OvertimeDays, object Details);

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
        var (ts, tsDays) = await ShiftEndpoints.ComputeDaysAsync(conn, employeeId, period);

        // Tăng ca theo giờ ra (17:00, đệm tới 17:20). null = tính tất cả; ngược lại chỉ các ngày đã duyệt.
        var otCandidates = DetectOvertimeDays(tsDays);
        var otMinutes = otCandidates
            .Where(o => approvedOtDates is null || approvedOtDates.Contains(o.Date))
            .Sum(o => o.Minutes);
        var overtimeHours = Math.Round(otMinutes / 60m, 2);
        var overtimePay = Math.Round(salary.OvertimeRate * otMinutes / 60m, 0);

        // Earnings KHÔNG gồm tăng ca (giao diện admin sẽ tự cộng theo ngày duyệt).
        var earnings = new List<PayLine>
        {
            new("Lương cơ bản", salary.BaseSalary),
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

        // Tiền phạt trong kỳ.
        var (penaltyTotal, penaltyItems) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, employeeId, period);
        foreach (var p in penaltyItems)
        {
            var label = $"Phạt {p.PenaltyNo}" + (p.Installments > 1 ? $" (đợt {p.InstallmentNo}/{p.Installments})" : "")
                + (string.IsNullOrWhiteSpace(p.Reason) ? "" : $" · {p.Reason}");
            deductions.Add(new(label, p.MonthAmount));
        }

        // Hoàn tiền phạt đã được kế toán duyệt (hình thức "cộng vào lương"), chưa áp dụng vào phiếu nào → cộng thu nhập.
        await using (var rr = await conn.Cmd("""
            SELECT penalty_no, amount FROM hr_penalty_refunds
            WHERE employee_id=@emp AND status='Approved' AND payout_method='payroll' AND applied_period=''
            ORDER BY created_at
            """).With("@emp", employeeId).ExecuteReaderAsync())
        {
            while (await rr.ReadAsync())
                earnings.Add(new($"Hoàn tiền phạt {rr.Str("penalty_no")}", rr.Dec("amount")));
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
            overtimeDays = otCandidates.ConvertAll(o => new { date = o.Date, checkOut = o.CheckOut, minutes = o.Minutes }),
            overtimeRate = salary.OvertimeRate,
            penaltyTotal,
            totalEarnings,
            totalDeductions,
            netPay = net,
        };

        return new PayrollResult(
            employeeId, empName, empCode, period,
            salary.BaseSalary, salary.Allowance, salary.OvertimeRate, overtimePay,
            ts.WorkedDays, ts.AbsentDays, ts.LateDays, overtimeHours,
            earnings, deductions, totalEarnings, totalDeductions, net, otCandidates, details);
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

    private static bool ValidPeriod(string? period)
        => !string.IsNullOrWhiteSpace(period) && period.Length >= 7
           && int.TryParse(period[..4], out _) && int.TryParse(period.Substring(5, 2), out var m) && m is >= 1 and <= 12;

    private static async Task Signal(IHubContext<ChangesHub> hub, Database db, NpgsqlConnection conn,
        ClaimsPrincipal u, Guid employeeId, string action, string entity)
    {
        await db.RecordAudit(u.Username(), action, entity, employeeId.ToString(), $"{action} (web).");
        await hub.Clients.All.SendAsync("changed", "data");
        await hub.Clients.All.SendAsync("changed", "hr");
        var target = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteScalarAsync() as string;
        if (!string.IsNullOrWhiteSpace(target))
        {
            await hub.Clients.User(target).SendAsync("changed", "data");
            await hub.Clients.User(target).SendAsync("changed", "hr");
        }
    }

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
