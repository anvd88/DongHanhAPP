using System.Linq;
using System.Security.Claims;
using System.Text.Json;
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
            var result = await ComputePayroll(conn, req.EmployeeId, req.Period, req.Adjustments);
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
    }

    // ---- Tính lương ----

    private sealed record PayLine(string Label, decimal Amount);

    private sealed record PayrollResult(
        Guid EmployeeId, string EmployeeName, string EmployeeCode, string Period,
        decimal BaseSalary, decimal Allowance, decimal OvertimeRate, decimal OvertimePay,
        int WorkedDays, int AbsentDays, int LateDays, decimal OvertimeHours,
        List<PayLine> Earnings, List<PayLine> Deductions,
        decimal TotalEarnings, decimal TotalDeductions, decimal NetPay,
        object Details);

    /// <summary>Tính toàn bộ phiếu lương cho (nhân viên, kỳ). Trả null nếu không tìm thấy nhân viên.</summary>
    private static async Task<PayrollResult?> ComputePayroll(NpgsqlConnection conn, Guid employeeId, string period, SalaryComponentDto[]? adjustments)
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
        var ts = await ShiftEndpoints.ComputeSummaryAsync(conn, employeeId, period);
        var overtimeHours = Math.Round(ts.TotalOvertimeMinutes / 60m, 2);
        var overtimePay = Math.Round(salary.OvertimeRate * ts.TotalOvertimeMinutes / 60m, 0);

        var earnings = new List<PayLine>
        {
            new("Lương cơ bản", salary.BaseSalary),
        };
        if (salary.Allowance != 0) earnings.Add(new("Phụ cấp", salary.Allowance));
        if (overtimePay != 0) earnings.Add(new($"Tăng ca ({overtimeHours} giờ)", overtimePay));

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

        var totalEarnings = earnings.Sum(e => e.Amount);
        var totalDeductions = deductions.Sum(e => e.Amount);
        var net = totalEarnings - totalDeductions;

        var details = new
        {
            earnings = earnings.ConvertAll(e => new { label = e.Label, amount = e.Amount }),
            deductions = deductions.ConvertAll(e => new { label = e.Label, amount = e.Amount }),
            timesheet = new
            {
                workedDays = ts.WorkedDays,
                absentDays = ts.AbsentDays,
                lateDays = ts.LateDays,
                overtimeHours,
                totalWorkedHours = ts.TotalWorkedHours,
            },
            penaltyTotal,
            totalEarnings,
            totalDeductions,
            netPay = net,
        };

        return new PayrollResult(
            employeeId, empName, empCode, period,
            salary.BaseSalary, salary.Allowance, salary.OvertimeRate, overtimePay,
            ts.WorkedDays, ts.AbsentDays, ts.LateDays, overtimeHours,
            earnings, deductions, totalEarnings, totalDeductions, net, details);
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

    // ---- DTO ----
    public record SalaryComponentDto(string? Label, decimal Amount, string? Kind);
    public record SaveSalaryReq(decimal BaseSalary, decimal Allowance, decimal OvertimeRate,
        SalaryComponentDto[]? Components, string? Note);
    public record CreatePayslipReq(Guid EmployeeId, string Period, bool Published,
        SalaryComponentDto[]? Adjustments, string? Note);
}
