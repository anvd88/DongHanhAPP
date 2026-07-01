using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Ca làm việc, phân ca và bảng công. Bảng công đối chiếu log chấm công khuôn mặt (cham_cong_log,
/// khóa theo username) với ca được phân để tự tính đi muộn / về sớm / tăng ca. Đăng ký/đổi ca do
/// nhân viên thực hiện qua engine đơn từ (loại shift_swap); phần này lo khâu quản lý &amp; tính công.
/// </summary>
public static class ShiftEndpoints
{
    private const string Tz = "Asia/Ho_Chi_Minh";

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS hr_shifts (
                id uuid PRIMARY KEY,
                code varchar(32) NOT NULL DEFAULT '',
                name varchar(120) NOT NULL DEFAULT '',
                start_time time NOT NULL DEFAULT '08:00',
                end_time time NOT NULL DEFAULT '17:00',
                break_minutes integer NOT NULL DEFAULT 60,
                late_grace_minutes integer NOT NULL DEFAULT 5,
                standard_hours numeric(5,2) NOT NULL DEFAULT 8,
                is_overnight boolean NOT NULL DEFAULT FALSE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS hr_shift_assignments (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                shift_id uuid NOT NULL REFERENCES hr_shifts(id) ON DELETE CASCADE,
                work_date date NOT NULL,
                note text NOT NULL DEFAULT ''
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_shift_assignments ON hr_shift_assignments (employee_id, work_date);
            CREATE INDEX IF NOT EXISTS ix_hr_shift_assignments_date ON hr_shift_assignments (work_date);
            """).ExecuteNonQueryAsync(ct);

        // Seed 1 ca hành chính mặc định để dùng ngay.
        await conn.Cmd("""
            INSERT INTO hr_shifts (id, code, name, start_time, end_time, break_minutes, late_grace_minutes, standard_hours)
            SELECT @id, 'HC', 'Ca hành chính', '08:00', '17:00', 60, 5, 8
            WHERE NOT EXISTS (SELECT 1 FROM hr_shifts)
            """).With("@id", Guid.NewGuid()).ExecuteNonQueryAsync(ct);
    }

    public static void MapShifts(this WebApplication app)
    {
        var g = app.MapGroup("/api/shifts").RequireAuthorization();

        // ---------------- Danh mục ca ----------------
        g.MapGet("/", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, code, name, start_time, end_time, break_minutes, late_grace_minutes, standard_hours, is_overnight
                FROM hr_shifts ORDER BY start_time
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    code = r.Str("code"),
                    name = r.Str("name"),
                    startTime = ReadTime(r, "start_time").ToString("HH:mm"),
                    endTime = ReadTime(r, "end_time").ToString("HH:mm"),
                    breakMinutes = r.Int("break_minutes"),
                    lateGraceMinutes = r.Int("late_grace_minutes"),
                    standardHours = r.Dec("standard_hours"),
                    isOvernight = r.Bool("is_overnight"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/", async (SaveShiftReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (!TryTime(req.StartTime, out var start) || !TryTime(req.EndTime, out var end))
                return Results.BadRequest(new { message = "Giờ vào/ra không hợp lệ (HH:mm)." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_shifts (id, code, name, start_time, end_time, break_minutes, late_grace_minutes, standard_hours, is_overnight)
                VALUES (@id, @code, @name, @start, @end, @brk, @grace, @std, @overnight)
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name ?? "")
                .With("@start", start).With("@end", end).With("@brk", req.BreakMinutes)
                .With("@grace", req.LateGraceMinutes).With("@std", req.StandardHours).With("@overnight", req.IsOvernight)
                .ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Tạo ca làm", "Shift", req.Name ?? "");
            return Results.Ok(new { id });
        });

        g.MapPut("/{id:guid}", async (Guid id, SaveShiftReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (!TryTime(req.StartTime, out var start) || !TryTime(req.EndTime, out var end))
                return Results.BadRequest(new { message = "Giờ vào/ra không hợp lệ (HH:mm)." });
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_shifts SET code=@code, name=@name, start_time=@start, end_time=@end,
                    break_minutes=@brk, late_grace_minutes=@grace, standard_hours=@std, is_overnight=@overnight
                WHERE id=@id
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name ?? "")
                .With("@start", start).With("@end", end).With("@brk", req.BreakMinutes)
                .With("@grace", req.LateGraceMinutes).With("@std", req.StandardHours).With("@overnight", req.IsOvernight)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Cập nhật ca làm", "Shift", req.Name ?? "");
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_shifts WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa ca làm", "Shift", id.ToString());
            return Results.NoContent();
        });

        // ---------------- Phân ca ----------------
        g.MapGet("/assignments", async (Database db, DateOnly from, DateOnly to, Guid? employeeId) =>
        {
            await using var conn = await db.OpenAsync();
            var cmd = conn.Cmd($"""
                SELECT a.id, a.employee_id, a.shift_id, a.work_date, a.note,
                       e.full_name AS emp_name, e.employee_code, s.name AS shift_name, s.start_time, s.end_time
                FROM hr_shift_assignments a
                JOIN hr_employees e ON e.id=a.employee_id
                JOIN hr_shifts s ON s.id=a.shift_id
                WHERE a.work_date BETWEEN @from AND @to {(employeeId is not null ? "AND a.employee_id=@emp" : "")}
                ORDER BY a.work_date, e.full_name
                """).With("@from", from).With("@to", to);
            if (employeeId is not null) cmd.With("@emp", employeeId.Value);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    employeeId = r.Guid("employee_id"),
                    employeeName = r.Str("emp_name"),
                    employeeCode = r.Str("employee_code"),
                    shiftId = r.Guid("shift_id"),
                    shiftName = r.Str("shift_name"),
                    workDate = r.DateOnly("work_date"),
                    startTime = ReadTime(r, "start_time").ToString("HH:mm"),
                    endTime = ReadTime(r, "end_time").ToString("HH:mm"),
                    note = r.Str("note"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/assignments", async (AssignShiftReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_shift_assignments (id, employee_id, shift_id, work_date, note)
                VALUES (@id, @emp, @shift, @date, @note)
                ON CONFLICT (employee_id, work_date) DO UPDATE SET shift_id=@shift, note=@note
                """)
                .With("@id", id).With("@emp", req.EmployeeId).With("@shift", req.ShiftId)
                .With("@date", req.WorkDate).With("@note", req.Note ?? "")
                .ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Phân ca", "ShiftAssignment", req.WorkDate.ToString("yyyy-MM-dd"));
            return Results.Ok(new { id });
        });

        g.MapDelete("/assignments/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_shift_assignments WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Hủy phân ca", "ShiftAssignment", id.ToString());
            return Results.NoContent();
        });
    }

    public static void MapTimesheet(this WebApplication app)
    {
        var g = app.MapGroup("/api/timesheet").RequireAuthorization();

        g.MapGet("/me", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await HrEndpoints.EnsureEmployeeForUser(conn, u.Username());
            return Results.Ok(await BuildTimesheet(conn, empId, month));
        });

        g.MapGet("/employee/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", id).ExecuteScalarAsync() as string;
            if (!u.IsAdmin() && !string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            return Results.Ok(await BuildTimesheet(conn, id, month));
        });
    }

    private static async Task<object> BuildTimesheet(NpgsqlConnection conn, Guid employeeId, string? month)
    {
        // month: yyyy-MM (mặc định tháng hiện tại).
        var now = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        int year = now.Year, mon = now.Month;
        if (!string.IsNullOrWhiteSpace(month) && month.Length >= 7
            && int.TryParse(month[..4], out var yy) && int.TryParse(month.Substring(5, 2), out var mm) && mm is >= 1 and <= 12)
        {
            year = yy; mon = mm;
        }
        var from = new DateOnly(year, mon, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var username = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", employeeId).ExecuteScalarAsync() as string ?? "";

        // 1) Log chấm công gom theo ngày (giờ địa phương).
        var logs = new Dictionary<DateOnly, (DateTime In, DateTime Out, int Count)>();
        if (!string.IsNullOrWhiteSpace(username))
        {
            await using var r = await conn.Cmd($"""
                SELECT (occurred_at AT TIME ZONE @tz)::date AS d,
                       MIN(occurred_at AT TIME ZONE @tz) AS first_in,
                       MAX(occurred_at AT TIME ZONE @tz) AS last_out,
                       COUNT(*) AS cnt
                FROM cham_cong_log
                WHERE username=@u AND (occurred_at AT TIME ZONE @tz)::date BETWEEN @from AND @to
                GROUP BY 1
                """).With("@tz", Tz).With("@u", username).With("@from", from).With("@to", to).ExecuteReaderAsync();
            while (await r.ReadAsync())
                logs[r.DateOnly("d")] = (r.Dt("first_in"), r.Dt("last_out"), r.Int("cnt"));
        }

        // 2) Phân ca theo ngày.
        var shifts = new Dictionary<DateOnly, ShiftInfo>();
        await using (var r = await conn.Cmd("""
            SELECT a.work_date, s.name, s.start_time, s.end_time, s.break_minutes, s.late_grace_minutes, s.standard_hours
            FROM hr_shift_assignments a JOIN hr_shifts s ON s.id=a.shift_id
            WHERE a.employee_id=@id AND a.work_date BETWEEN @from AND @to
            """).With("@id", employeeId).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                shifts[r.DateOnly("work_date")] = new ShiftInfo(
                    r.Str("name"), ReadTime(r, "start_time"), ReadTime(r, "end_time"),
                    r.Int("break_minutes"), r.Int("late_grace_minutes"), r.Dec("standard_hours"));
        }

        // 3) Duyệt từng ngày có dữ liệu.
        var days = new List<object>();
        int workedDays = 0, lateDays = 0, earlyDays = 0, absentDays = 0;
        int totalLate = 0, totalEarly = 0, totalOt = 0;
        double totalWorkedHours = 0;

        var allDates = new SortedSet<DateOnly>(logs.Keys);
        foreach (var d in shifts.Keys) allDates.Add(d);

        foreach (var d in allDates)
        {
            shifts.TryGetValue(d, out var shift);
            var hasLog = logs.TryGetValue(d, out var log);
            string status;
            int lateMin = 0, earlyMin = 0, otMin = 0;
            double workedH = 0;
            string? checkIn = null, checkOut = null;

            if (hasLog)
            {
                checkIn = log.In.ToString("HH:mm");
                var hasOut = log.Count > 1 && log.Out > log.In;
                checkOut = hasOut ? log.Out.ToString("HH:mm") : null;
                var workedMinutes = hasOut ? (int)(log.Out - log.In).TotalMinutes : 0;

                if (shift is not null)
                {
                    var inTod = TimeOnly.FromDateTime(log.In);
                    var lateThreshold = shift.Start.AddMinutes(shift.Grace);
                    if (inTod > lateThreshold) lateMin = (int)(inTod - shift.Start).TotalMinutes;

                    if (hasOut)
                    {
                        var outTod = TimeOnly.FromDateTime(log.Out);
                        if (outTod < shift.End) earlyMin = (int)(shift.End - outTod).TotalMinutes;
                        var netWorked = Math.Max(0, workedMinutes - shift.Break);
                        var stdMin = (int)(shift.StandardHours * 60);
                        otMin = Math.Max(0, netWorked - stdMin);
                        workedH = Math.Round(netWorked / 60.0, 2);
                    }
                    status = lateMin > 0 && earlyMin > 0 ? "Đi muộn & về sớm"
                        : lateMin > 0 ? "Đi muộn"
                        : earlyMin > 0 ? "Về sớm"
                        : hasOut ? "Đủ công" : "Thiếu giờ ra";
                }
                else
                {
                    workedH = hasOut ? Math.Round(workedMinutes / 60.0, 2) : 0;
                    status = hasOut ? "Không phân ca" : "Thiếu giờ ra";
                }
                workedDays++;
                if (lateMin > 0) lateDays++;
                if (earlyMin > 0) earlyDays++;
                totalLate += lateMin; totalEarly += earlyMin; totalOt += otMin; totalWorkedHours += workedH;
            }
            else
            {
                status = "Vắng"; // có phân ca nhưng không có log
                absentDays++;
            }

            days.Add(new
            {
                date = d,
                shiftName = shift?.Name ?? "",
                checkIn,
                checkOut,
                lateMinutes = lateMin,
                earlyMinutes = earlyMin,
                overtimeMinutes = otMin,
                workedHours = workedH,
                status,
            });
        }

        return new
        {
            period = $"{year:D4}-{mon:D2}",
            summary = new
            {
                workedDays,
                absentDays,
                lateDays,
                earlyDays,
                totalLateMinutes = totalLate,
                totalEarlyMinutes = totalEarly,
                totalOvertimeMinutes = totalOt,
                totalWorkedHours = Math.Round(totalWorkedHours, 2),
            },
            days,
        };
    }

    private sealed record ShiftInfo(string Name, TimeOnly Start, TimeOnly End, int Break, int Grace, decimal StandardHours);

    private static TimeOnly ReadTime(NpgsqlDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        if (r.IsDBNull(i)) return default;
        var v = r.GetValue(i);
        return v switch
        {
            TimeOnly t => t,
            TimeSpan ts => TimeOnly.FromTimeSpan(ts),
            DateTime dt => TimeOnly.FromDateTime(dt),
            _ => TimeOnly.Parse(v.ToString() ?? "00:00"),
        };
    }

    private static bool TryTime(string? value, out TimeOnly time)
        => TimeOnly.TryParse(string.IsNullOrWhiteSpace(value) ? "" : value, out time);

    private static async Task Signal(IHubContext<ChangesHub> hub, Database db, ClaimsPrincipal u, string action, string entity, string name)
    {
        await db.RecordAudit(u.Username(), action, entity, name, $"{action} (web).");
        await hub.Clients.All.SendAsync("changed", "data");
    }

    public record SaveShiftReq(string? Code, string? Name, string? StartTime, string? EndTime,
        int BreakMinutes, int LateGraceMinutes, decimal StandardHours, bool IsOvernight);
    public record AssignShiftReq(Guid EmployeeId, Guid ShiftId, DateOnly WorkDate, string? Note);
}
