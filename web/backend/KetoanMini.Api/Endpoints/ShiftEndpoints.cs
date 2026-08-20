using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
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
    private static readonly TimeOnly OvertimeMorningEnd = new(8, 0);
    private static readonly TimeOnly OvertimeEveningStart = new(17, 0);
    private const int MinimumOvertimeMinutes = 15;

    internal static int CalculateOvertimeMinutes(TimeOnly checkIn, TimeOnly? checkOut)
    {
        var morningMinutes = checkIn < OvertimeMorningEnd
            ? (int)(OvertimeMorningEnd - checkIn).TotalMinutes
            : 0;
        var eveningMinutes = checkOut is { } outTime && outTime > OvertimeEveningStart
            ? (int)(outTime - OvertimeEveningStart).TotalMinutes
            : 0;

        return (morningMinutes >= MinimumOvertimeMinutes ? morningMinutes : 0)
             + (eveningMinutes >= MinimumOvertimeMinutes ? eveningMinutes : 0);
    }

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
                checkout_grace_minutes integer NOT NULL DEFAULT 120,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            ALTER TABLE hr_shifts ADD COLUMN IF NOT EXISTS checkout_grace_minutes integer NOT NULL DEFAULT 120;

            CREATE TABLE IF NOT EXISTS hr_shift_assignments (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                shift_id uuid NOT NULL REFERENCES hr_shifts(id) ON DELETE CASCADE,
                work_date date NOT NULL,
                note text NOT NULL DEFAULT ''
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_shift_assignments ON hr_shift_assignments (employee_id, work_date);
            CREATE INDEX IF NOT EXISTS ix_hr_shift_assignments_date ON hr_shift_assignments (work_date);

            CREATE TABLE IF NOT EXISTS hr_holidays (
                id uuid PRIMARY KEY,
                holiday_date date NOT NULL,
                name varchar(160) NOT NULL DEFAULT '',
                holiday_type varchar(24) NOT NULL DEFAULT 'company',
                note text NOT NULL DEFAULT '',
                created_by varchar(100) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_holidays_date_type ON hr_holidays (holiday_date, holiday_type);
            CREATE INDEX IF NOT EXISTS ix_hr_holidays_date ON hr_holidays (holiday_date);
            """).ExecuteNonQueryAsync(ct);

        // Seed 1 ca hành chính mặc định để dùng ngay.
        await conn.Cmd("""
            INSERT INTO hr_shifts (id, code, name, start_time, end_time, break_minutes, late_grace_minutes, standard_hours)
            SELECT @id, 'HC', 'Ca hành chính', '08:00', '17:00', 60, 5, 8
            WHERE NOT EXISTS (SELECT 1 FROM hr_shifts)
            """).With("@id", Guid.NewGuid()).ExecuteNonQueryAsync(ct);

        // Một nguồn đọc hiệu lực dùng chung cho bảng công, dashboard và policy. Log thiết bị vẫn bất biến;
        // correction mới nhất chỉ che mốc cùng chiều/ngày trong lớp đọc. Ra của ca đêm được gắn về work_date
        // hôm trước để mọi downstream consumer thống nhất với bảng công chi tiết.
        await conn.Cmd("""
            CREATE OR REPLACE VIEW hr_effective_attendance_log AS
            WITH raw_mapped AS (
                SELECT l.id,l.username,l.full_name,l.loai,l.similarity,l.anh,l.occurred_at,l.ghi_chu,
                       COALESCE((
                           SELECT a.work_date
                           FROM hr_employees e
                           JOIN hr_shift_assignments a ON a.employee_id=e.id
                           JOIN hr_shifts s ON s.id=a.shift_id AND s.is_overnight=TRUE
                           WHERE lower(e.username)=lower(l.username) AND l.loai='Ra'
                             AND (l.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh') >=
                                 a.work_date + s.start_time
                             AND (l.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh') <=
                                 (a.work_date + 1) + s.end_time
                                 + make_interval(mins => s.checkout_grace_minutes)
                           ORDER BY a.work_date DESC LIMIT 1
                       ), (l.occurred_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date) AS logical_work_date
                FROM cham_cong_log l
                WHERE l.loai IN ('Vào','Ra')
            ), latest_correction AS (
                SELECT DISTINCT ON (employee_id,work_date,loai)
                       c.id,c.request_id,c.employee_id,c.username,c.full_name,c.work_date,c.loai,
                       c.occurred_at,c.reason,c.applied_at
                FROM hr_attendance_corrections c
                ORDER BY employee_id,work_date,loai,applied_at DESC,id DESC
            )
            SELECT r.id,r.username,r.full_name,r.loai,r.similarity,r.anh,r.occurred_at,r.ghi_chu,
                   r.logical_work_date,FALSE AS is_correction,NULL::uuid AS request_id
            FROM raw_mapped r
            WHERE NOT EXISTS (
                SELECT 1 FROM latest_correction c
                JOIN hr_employees e ON e.id=c.employee_id
                WHERE lower(e.username)=lower(r.username)
                  AND c.work_date=r.logical_work_date AND c.loai=r.loai
            )
            UNION ALL
            SELECT -c.id,e.username,e.full_name,c.loai,0::double precision,NULL::text,c.occurred_at,
                   c.reason,c.work_date,TRUE,c.request_id
            FROM latest_correction c
            JOIN hr_employees e ON e.id=c.employee_id
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapShifts(this WebApplication app)
    {
        var g = app.MapGroup("/api/shifts").RequirePermission(Permissions.AttendanceSelf);

        // ---------------- Danh mục ca ----------------
        g.MapGet("/", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, code, name, start_time, end_time, break_minutes, late_grace_minutes,
                       standard_hours, is_overnight, checkout_grace_minutes
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
                    checkoutGraceMinutes = r.Int("checkout_grace_minutes"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/", async (SaveShiftReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!TryTime(req.StartTime, out var start) || !TryTime(req.EndTime, out var end))
                return Results.BadRequest(new { message = "Giờ vào/ra không hợp lệ (HH:mm)." });
            if (req.CheckoutGraceMinutes is < 0 or > 720)
                return Results.BadRequest(new { message = "Thời gian chờ chấm ra phải từ 0 đến 720 phút." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_shifts (id, code, name, start_time, end_time, break_minutes, late_grace_minutes,
                                       standard_hours, is_overnight, checkout_grace_minutes)
                VALUES (@id, @code, @name, @start, @end, @brk, @grace, @std, @overnight, @checkoutGrace)
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name ?? "")
                .With("@start", start).With("@end", end).With("@brk", req.BreakMinutes)
                .With("@grace", req.LateGraceMinutes).With("@std", req.StandardHours).With("@overnight", req.IsOvernight)
                .With("@checkoutGrace", req.CheckoutGraceMinutes)
                .ExecuteNonQueryAsync();
            await Signal(db, u, "Tạo ca làm", "Shift", req.Name ?? "");
            return Results.Ok(new { id });
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapPut("/{id:guid}", async (Guid id, SaveShiftReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!TryTime(req.StartTime, out var start) || !TryTime(req.EndTime, out var end))
                return Results.BadRequest(new { message = "Giờ vào/ra không hợp lệ (HH:mm)." });
            if (req.CheckoutGraceMinutes is < 0 or > 720)
                return Results.BadRequest(new { message = "Thời gian chờ chấm ra phải từ 0 đến 720 phút." });
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_shifts SET code=@code, name=@name, start_time=@start, end_time=@end,
                    break_minutes=@brk, late_grace_minutes=@grace, standard_hours=@std,
                    is_overnight=@overnight, checkout_grace_minutes=@checkoutGrace
                WHERE id=@id
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name ?? "")
                .With("@start", start).With("@end", end).With("@brk", req.BreakMinutes)
                .With("@grace", req.LateGraceMinutes).With("@std", req.StandardHours).With("@overnight", req.IsOvernight)
                .With("@checkoutGrace", req.CheckoutGraceMinutes)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Cập nhật ca làm", "Shift", req.Name ?? "");
            return Results.NoContent();
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_shifts WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa ca làm", "Shift", id.ToString());
            return Results.NoContent();
        }).RequirePermission(Permissions.AttendanceManage);

        // ---------------- Phân ca ----------------
        g.MapGet("/assignments", async (ClaimsPrincipal u, Database db, DateOnly from, DateOnly to, Guid? employeeId) =>
        {
            await using var conn = await db.OpenAsync();
            var scope = await ResolveAttendanceScopeAsync(conn, u);
            if (employeeId is { } requestedEmployee
                && !await EmployeeWithinAttendanceScopeAsync(conn, requestedEmployee, scope))
            {
                return Results.Forbid();
            }

            var where = new List<string> { "a.work_date BETWEEN @from AND @to" };
            if (employeeId is { }) where.Add("a.employee_id=@emp");
            switch (scope.Kind)
            {
                case AttendanceScopeKind.Department when scope.DepartmentId is not null:
                    where.Add("e.department_id=@scopeDept");
                    break;
                case AttendanceScopeKind.Location when scope.LocationId is not null:
                    where.Add("e.location_id=@scopeLoc");
                    break;
                case AttendanceScopeKind.Self when scope.EmployeeId is not null:
                    where.Add("a.employee_id=@scopeEmployee");
                    break;
                case AttendanceScopeKind.Self:
                    // Không có hồ sơ liên kết: đóng mặc định và không làm lộ lịch của người khác.
                    where.Add("FALSE");
                    break;
            }

            var cmd = conn.Cmd($"""
                SELECT a.id, a.employee_id, a.shift_id, a.work_date, a.note,
                       e.full_name AS emp_name, e.employee_code, s.name AS shift_name, s.start_time, s.end_time
                FROM hr_shift_assignments a
                JOIN hr_employees e ON e.id=a.employee_id
                JOIN hr_shifts s ON s.id=a.shift_id
                WHERE {string.Join(" AND ", where)}
                ORDER BY a.work_date, e.full_name
                """).With("@from", from).With("@to", to);
            if (employeeId is { } target) cmd.With("@emp", target);
            if (scope.DepartmentId is { } departmentId) cmd.With("@scopeDept", departmentId);
            if (scope.LocationId is { } locationId) cmd.With("@scopeLoc", locationId);
            if (scope.EmployeeId is { } ownEmployeeId) cmd.With("@scopeEmployee", ownEmployeeId);
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

        g.MapPost("/assignments", async (AssignShiftReq req, ClaimsPrincipal u, Database db) =>
        {
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
            await Signal(db, u, "Phân ca", "ShiftAssignment", req.WorkDate.ToString("yyyy-MM-dd"));
            return Results.Ok(new { id });
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapDelete("/assignments/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_shift_assignments WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Hủy phân ca", "ShiftAssignment", id.ToString());
            return Results.NoContent();
        }).RequirePermission(Permissions.AttendanceManage);

        // ---------------- Ngay nghi le / nghi cong ty ----------------
        g.MapGet("/holidays", async (Database db, DateOnly? from, DateOnly? to) =>
        {
            var now = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var start = from ?? new DateOnly(now.Year, 1, 1);
            var end = to ?? new DateOnly(now.Year, 12, 31);
            if (end < start) (start, end) = (end, start);

            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, holiday_date, name, holiday_type, note, created_by, created_at
                FROM hr_holidays
                WHERE holiday_date BETWEEN @from AND @to
                ORDER BY holiday_date, holiday_type, name
                """)
                .With("@from", start).With("@to", end)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    holidayDate = r.DateOnly("holiday_date"),
                    name = r.Str("name"),
                    holidayType = r.Str("holiday_type"),
                    note = r.Str("note"),
                    createdBy = r.Str("created_by"),
                    createdAt = r.Dt("created_at"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/holidays", async (SaveHolidayReq req, ClaimsPrincipal u, Database db) =>
        {
            var holidayType = NormalizeHolidayType(req.HolidayType);
            var name = string.IsNullOrWhiteSpace(req.Name)
                ? (holidayType == "public" ? "Ngày nghỉ lễ" : "Ngày nghỉ công ty")
                : req.Name.Trim();

            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_holidays (id, holiday_date, name, holiday_type, note, created_by)
                VALUES (@id, @date, @name, @type, @note, @by)
                ON CONFLICT (holiday_date, holiday_type) DO UPDATE
                SET name=@name, note=@note, created_by=@by
                """)
                .With("@id", id).With("@date", req.HolidayDate).With("@name", name)
                .With("@type", holidayType).With("@note", req.Note ?? "").With("@by", u.Username())
                .ExecuteNonQueryAsync();
            await Signal(db, u, "Cap nhat ngay nghi", "Holiday", req.HolidayDate.ToString("yyyy-MM-dd"));
            return Results.Ok(new { id });
        }).RequirePermission(Permissions.AttendanceManage);

        g.MapDelete("/holidays/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var name = await conn.Cmd("SELECT holiday_date::text FROM hr_holidays WHERE id=@id")
                .With("@id", id).ExecuteScalarAsync() as string ?? id.ToString();
            var n = await conn.Cmd("DELETE FROM hr_holidays WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xoa ngay nghi", "Holiday", name);
            return Results.NoContent();
        }).RequirePermission(Permissions.AttendanceManage);
    }

    public static void MapTimesheet(this WebApplication app)
    {
        var g = app.MapGroup("/api/timesheet").RequirePermission(Permissions.AttendanceSelf);

        g.MapGet("/me", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await HrEndpoints.EnsureEmployeeForUser(conn, u.Username());
            return Results.Ok(await BuildTimesheet(conn, empId, month));
        });

        g.MapGet("/employee/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var scope = await ResolveAttendanceScopeAsync(conn, u);
            if (!await EmployeeWithinAttendanceScopeAsync(conn, id, scope))
                return Results.Forbid();
            return Results.Ok(await BuildTimesheet(conn, id, month));
        });
    }

    /// <summary>Tổng hợp bảng công của một nhân viên trong một tháng — dùng lại khi tính lương.</summary>
    public sealed record TimesheetSummary(
        string Period, int WorkedDays, int AbsentDays, int LateDays, int EarlyDays,
        int TotalLateMinutes, int TotalEarlyMinutes, int TotalOvertimeMinutes, double TotalWorkedHours);

    public static async Task<TimesheetSummary> ComputeSummaryAsync(NpgsqlConnection conn, Guid employeeId, string? month)
        => (await ComputeCore(conn, employeeId, month)).Summary;

    /// <summary>Bảng công chi tiết theo ngày (kiểu dữ liệu tường minh) — dùng khi xuất Excel.</summary>
    public static Task<(TimesheetSummary Summary, List<TimesheetDayInfo> Days)> ComputeDaysAsync(NpgsqlConnection conn, Guid employeeId, string? month)
        => ComputeCore(conn, employeeId, month);

    /// <summary>Một dòng bảng công (một ngày).</summary>
    public sealed record TimesheetDayInfo(
        DateOnly Date, string ShiftName, string HolidayName, string HolidayType,
        string ShiftStart, string ShiftEnd, string EventType,
        string? CheckIn, string? CheckOut, int LateMinutes, int EarlyMinutes,
        int OvertimeMinutes, double WorkedHours, string Status,
        bool IsOvernight = false, int CheckoutGraceMinutes = 0,
        string? MissingCheckoutRequestStatus = null, bool? HasOpenCheckoutRequest = null,
        Guid? MissingCheckoutRequestId = null);

    private static async Task<object> BuildTimesheet(NpgsqlConnection conn, Guid employeeId, string? month)
    {
        var (s, days) = await ComputeCore(conn, employeeId, month);
        return new
        {
            period = s.Period,
            summary = new
            {
                workedDays = s.WorkedDays,
                absentDays = s.AbsentDays,
                lateDays = s.LateDays,
                earlyDays = s.EarlyDays,
                totalLateMinutes = s.TotalLateMinutes,
                totalEarlyMinutes = s.TotalEarlyMinutes,
                totalOvertimeMinutes = s.TotalOvertimeMinutes,
                totalWorkedHours = s.TotalWorkedHours,
            },
            days = days.ConvertAll(d => new
            {
                date = d.Date,
                shiftName = d.ShiftName,
                holidayName = d.HolidayName,
                holidayType = d.HolidayType,
                shiftStart = d.ShiftStart,
                shiftEnd = d.ShiftEnd,
                eventType = d.EventType,
                checkIn = d.CheckIn,
                checkOut = d.CheckOut,
                lateMinutes = d.LateMinutes,
                earlyMinutes = d.EarlyMinutes,
                overtimeMinutes = d.OvertimeMinutes,
                workedHours = d.WorkedHours,
                status = d.Status,
                isOvernight = d.IsOvernight,
                checkoutGraceMinutes = d.CheckoutGraceMinutes,
                missingCheckoutRequestStatus = d.MissingCheckoutRequestStatus,
                hasOpenCheckoutRequest = d.HasOpenCheckoutRequest,
                missingCheckoutRequestId = d.MissingCheckoutRequestId,
            }),
        };
    }

    private static async Task<(TimesheetSummary Summary, List<TimesheetDayInfo> Days)> ComputeCore(NpgsqlConnection conn, Guid employeeId, string? month)
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

        // 1) Phân ca phải được nạp trước log để một lượt Ra sau nửa đêm được gắn về đúng ngày công
        // của ca qua đêm, thay vì biến thành một lượt Vào giả của ngày hôm sau.
        var shifts = new Dictionary<DateOnly, ShiftInfo>();
        await using (var r = await conn.Cmd("""
            SELECT a.work_date, s.name, s.start_time, s.end_time, s.break_minutes, s.late_grace_minutes,
                   s.standard_hours, s.is_overnight, s.checkout_grace_minutes
            FROM hr_shift_assignments a JOIN hr_shifts s ON s.id=a.shift_id
            WHERE a.employee_id=@id AND a.work_date BETWEEN @shiftFrom AND @to
            """).With("@id", employeeId).With("@shiftFrom", from.AddDays(-1)).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                shifts[r.DateOnly("work_date")] = new ShiftInfo(
                    r.Str("name"), ReadTime(r, "start_time"), ReadTime(r, "end_time"),
                    r.Int("break_minutes"), r.Int("late_grace_minutes"), r.Dec("standard_hours"),
                    r.Bool("is_overnight"), r.Int("checkout_grace_minutes"));
        }

        // Trạng thái đơn gần nhất cho phép mobile triệt nhắc cũ giữa nhiều thiết bị. Rejected/Cancelled
        // vẫn được trả cùng request id để hai phía dùng chung generation retry của notification.
        var checkoutRequests = new Dictionary<DateOnly, CheckoutRequestInfo>();
        await using (var r = await conn.Cmd("""
            SELECT DISTINCT ON (payload->>'date')
                   payload->>'date' AS work_date, id, status
            FROM hr_requests
            WHERE employee_id=@id AND req_type='forgot_checkin' AND payload->>'direction'='out'
              AND payload->>'date' BETWEEN @fromText AND @toText
            ORDER BY payload->>'date', created_at DESC, id DESC
            """).With("@id", employeeId).With("@fromText", from.ToString("yyyy-MM-dd"))
            .With("@toText", to.ToString("yyyy-MM-dd")).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                if (!DateOnly.TryParseExact(r.Str("work_date"), "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var requestDate)) continue;
                var requestStatus = r.Str("status");
                checkoutRequests[requestDate] = new CheckoutRequestInfo(r.Guid("id"), requestStatus,
                    requestStatus is "Pending" or "Approved" or "Resolved" or "Completed");
            }
        }

        // 2) Log gốc là bất biến. Tách Vào/Ra theo cột loai; correction mới nhất của từng chiều sẽ
        // thay thế về mặt tính toán nhưng không xóa bằng chứng khuôn mặt/QR.
        var logs = new Dictionary<DateOnly, AttendanceBucket>();
        AttendanceBucket Bucket(DateOnly date)
        {
            if (!logs.TryGetValue(date, out var bucket)) logs[date] = bucket = new AttendanceBucket();
            return bucket;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            await using (var r = await conn.Cmd("""
                SELECT loai, occurred_at AT TIME ZONE @tz AS local_at
                FROM cham_cong_log
                WHERE lower(username)=lower(@u)
                  AND loai IN ('Vào','Ra')
                  AND (occurred_at AT TIME ZONE @tz)::date BETWEEN @from AND @rawTo
                ORDER BY occurred_at
                """).With("@tz", Tz).With("@u", username).With("@from", from)
                .With("@rawTo", to.AddDays(1)).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var localAt = r.Dt("local_at");
                    var localDate = DateOnly.FromDateTime(localAt);
                    var loai = r.Str("loai");
                    var workDate = localDate;
                    if (loai == AttendancePolicy.CheckInTypeOut)
                    {
                        var previous = localDate.AddDays(-1);
                        if (shifts.TryGetValue(previous, out var previousShift) && previousShift.Overnight)
                        {
                            var startsAt = previous.ToDateTime(previousShift.Start);
                            var endsAt = previous.AddDays(1).ToDateTime(previousShift.End)
                                .AddMinutes(previousShift.CheckoutGrace);
                            if (localAt >= startsAt && localAt <= endsAt) workDate = previous;
                        }
                    }
                    if (workDate < from || workDate > to) continue;
                    if (loai == AttendancePolicy.CheckInTypeIn) Bucket(workDate).RawIns.Add(localAt);
                    else if (loai == AttendancePolicy.CheckInTypeOut) Bucket(workDate).RawOuts.Add(localAt);
                }
            }

            await using var corrections = await conn.Cmd("""
                SELECT DISTINCT ON (work_date, loai)
                       work_date, loai, occurred_at AT TIME ZONE @tz AS local_at
                FROM hr_attendance_corrections
                WHERE employee_id=@id AND work_date BETWEEN @from AND @to
                ORDER BY work_date, loai, applied_at DESC, id DESC
                """).With("@tz", Tz).With("@id", employeeId).With("@from", from).With("@to", to)
                .ExecuteReaderAsync();
            while (await corrections.ReadAsync())
            {
                var bucket = Bucket(corrections.DateOnly("work_date"));
                if (corrections.Str("loai") == AttendancePolicy.CheckInTypeIn)
                    bucket.CorrectedIn = corrections.Dt("local_at");
                else
                    bucket.CorrectedOut = corrections.Dt("local_at");
            }
        }

        // 3) Duyệt từng ngày có dữ liệu.
        var holidays = new Dictionary<DateOnly, HolidayInfo>();
        await using (var r = await conn.Cmd("""
            SELECT holiday_date,
                   string_agg(name, ', ' ORDER BY holiday_type, name) AS holiday_name,
                   CASE WHEN BOOL_OR(holiday_type='public') THEN 'public' ELSE 'company' END AS holiday_type
            FROM hr_holidays
            WHERE holiday_date BETWEEN @from AND @to
            GROUP BY holiday_date
            """).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                holidays[r.DateOnly("holiday_date")] = new HolidayInfo(r.Str("holiday_name"), r.Str("holiday_type"));
        }
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday && !holidays.ContainsKey(d))
                holidays[d] = new HolidayInfo("Chủ nhật", "weekly");
        }

        // 4) Các đơn lịch đã duyệt. API trả loại sự kiện tường minh để mobile tô màu đúng,
        // không phải suy đoán từ chuỗi trạng thái đã bản địa hoá.
        var calendarEvents = new Dictionary<DateOnly, string>();
        await using (var r = await conn.Cmd("""
            SELECT req_type, payload::text AS payload
            FROM hr_requests
            WHERE employee_id=@id AND status='Approved'
              AND req_type IN ('leave','sick','business_trip','overtime')
              AND created_at < (@to::date + INTERVAL '2 months')
            """).With("@id", employeeId).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var type = r.Str("req_type") switch
                {
                    "leave" or "sick" => "leave",
                    "business_trip" => "business_trip",
                    "overtime" => "overtime",
                    _ => ""
                };
                if (type.Length == 0) continue;
                var payload = r.Str("payload");
                var startText = ReadJsonString(payload, type == "leave" ? "fromDate" : "date");
                if (!DateOnly.TryParse(startText, out var start)) continue;
                var endText = type == "leave" ? ReadJsonString(payload, "toDate") : startText;
                if (!DateOnly.TryParse(endText, out var end)) end = start;
                if (end < start) (start, end) = (end, start);
                for (var d = start; d <= end; d = d.AddDays(1))
                    if (d >= from && d <= to) calendarEvents[d] = type;
            }
        }

        var days = new List<TimesheetDayInfo>();
        int workedDays = 0, lateDays = 0, earlyDays = 0, absentDays = 0;
        int totalLate = 0, totalEarly = 0, totalOt = 0;
        double totalWorkedHours = 0;

        var allDates = new SortedSet<DateOnly>(logs.Keys);
        foreach (var d in shifts.Keys.Where(d => d >= from && d <= to)) allDates.Add(d);
        foreach (var d in holidays.Keys) allDates.Add(d);
        foreach (var d in calendarEvents.Keys) allDates.Add(d);

        foreach (var d in allDates)
        {
            shifts.TryGetValue(d, out var shift);
            var log = logs.GetValueOrDefault(d);
            var hasLog = log?.HasAny == true;
            holidays.TryGetValue(d, out var holiday);
            calendarEvents.TryGetValue(d, out var eventType);
            string status;
            int lateMin = 0, earlyMin = 0, otMin = 0;
            double workedH = 0;
            string? checkIn = null, checkOut = null;

            if (log is not null && hasLog)
            {
                var inAt = log.CheckIn;
                var outAt = log.CheckOut;
                var hasIn = inAt is not null;
                var hasOut = outAt is not null;
                var validPair = hasIn && hasOut && outAt > inAt;
                checkIn = inAt?.ToString("HH:mm");
                checkOut = outAt?.ToString("HH:mm");
                var workedMinutes = validPair ? (int)(outAt!.Value - inAt!.Value).TotalMinutes : 0;
                var inTod = hasIn ? TimeOnly.FromDateTime(inAt!.Value) : (TimeOnly?)null;
                var outTod = hasOut ? TimeOnly.FromDateTime(outAt!.Value) : (TimeOnly?)null;
                if (inTod is { } overtimeIn && shift?.Overnight != true)
                    otMin = CalculateOvertimeMinutes(overtimeIn, outTod);

                if (shift is not null)
                {
                    var expectedStart = d.ToDateTime(shift.Start);
                    var expectedEnd = (shift.Overnight ? d.AddDays(1) : d).ToDateTime(shift.End);
                    var lateThreshold = expectedStart.AddMinutes(shift.Grace);
                    if (inAt is { } actualIn && actualIn > lateThreshold)
                        lateMin = (int)(actualIn - expectedStart).TotalMinutes;

                    if (outAt is { } actualOut)
                    {
                        if (actualOut < expectedEnd) earlyMin = (int)(expectedEnd - actualOut).TotalMinutes;
                        var netWorked = Math.Max(0, workedMinutes - shift.Break);
                        workedH = Math.Round(netWorked / 60.0, 2);
                    }
                    status = !hasIn ? "Thiếu giờ vào"
                        : !hasOut ? "Thiếu giờ ra"
                        : !validPair ? "Giờ ra không hợp lệ"
                        : lateMin > 0 && earlyMin > 0 ? "Đi muộn & về sớm"
                        : lateMin > 0 ? "Đi muộn"
                        : earlyMin > 0 ? "Về sớm"
                        : "Đủ công";
                }
                else
                {
                    workedH = validPair ? Math.Round(workedMinutes / 60.0, 2) : 0;
                    status = !hasIn ? "Thiếu giờ vào"
                        : !hasOut ? "Thiếu giờ ra"
                        : !validPair ? "Giờ ra không hợp lệ"
                        : "Không phân ca";
                }
                if (holiday is not null && shift is null && validPair)
                    status = WorkedHolidayStatus(holiday);
                if (hasIn) workedDays++;
                else absentDays++;
                if (lateMin > 0) lateDays++;
                if (earlyMin > 0) earlyDays++;
                totalLate += lateMin; totalEarly += earlyMin; totalOt += otMin; totalWorkedHours += workedH;
            }
            else
            {
                status = eventType switch
                {
                    "leave" => "Nghỉ phép",
                    "business_trip" => "Công tác",
                    "overtime" => "Tăng ca đã duyệt",
                    _ => "Vắng"
                };
                if (eventType is not ("leave" or "business_trip")) absentDays++;
            }

            if (!hasLog && holiday is not null)
            {
                status = OffHolidayStatus(holiday);
                absentDays = Math.Max(0, absentDays - 1);
            }

            checkoutRequests.TryGetValue(d, out var checkoutRequest);

            days.Add(new TimesheetDayInfo(
                d,
                shift?.Name ?? "",
                holiday?.Name ?? "",
                holiday?.Type ?? "",
                shift?.Start.ToString("HH:mm") ?? "",
                shift?.End.ToString("HH:mm") ?? "",
                eventType ?? "",
                checkIn,
                checkOut,
                lateMin,
                earlyMin,
                otMin,
                workedH,
                status,
                shift?.Overnight ?? false,
                shift?.CheckoutGrace ?? 0,
                checkoutRequest?.Status,
                checkoutRequest?.SuppressReminder,
                checkoutRequest?.RequestId));
        }

        var summary = new TimesheetSummary(
            $"{year:D4}-{mon:D2}", workedDays, absentDays, lateDays, earlyDays,
            totalLate, totalEarly, totalOt, Math.Round(totalWorkedHours, 2));
        return (summary, days);
    }

    private sealed record ShiftInfo(string Name, TimeOnly Start, TimeOnly End, int Break, int Grace,
        decimal StandardHours, bool Overnight, int CheckoutGrace);
    private sealed record CheckoutRequestInfo(Guid RequestId, string Status, bool SuppressReminder);
    private sealed class AttendanceBucket
    {
        public List<DateTime> RawIns { get; } = [];
        public List<DateTime> RawOuts { get; } = [];
        public DateTime? CorrectedIn { get; set; }
        public DateTime? CorrectedOut { get; set; }
        public DateTime? CheckIn => CorrectedIn ?? (RawIns.Count == 0 ? null : RawIns.Min());
        public DateTime? CheckOut => CorrectedOut ?? (RawOuts.Count == 0 ? null : RawOuts.Max());
        public bool HasAny => CheckIn is not null || CheckOut is not null;
    }
    private sealed record HolidayInfo(string Name, string Type);

    private static string ReadJsonString(string json, string name)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value) ? value.ToString() : "";
        }
        catch { return ""; }
    }

    private static string NormalizeHolidayType(string? type)
        => string.Equals(type, "public", StringComparison.OrdinalIgnoreCase) ? "public" : "company";

    private static string OffHolidayStatus(HolidayInfo holiday)
        => holiday.Type switch
        {
            "public" => "Nghỉ lễ",
            "weekly" => "Nghỉ chủ nhật",
            _ => "Nghỉ công ty",
        };

    private static string WorkedHolidayStatus(HolidayInfo holiday)
        => holiday.Type switch
        {
            "public" => "Làm ngày nghỉ lễ",
            "weekly" => "Làm ngày chủ nhật",
            _ => "Làm ngày nghỉ công ty",
        };

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

    private enum AttendanceScopeKind { All, Department, Location, Self }

    private sealed record AttendanceScope(
        AttendanceScopeKind Kind, Guid? EmployeeId, Guid? DepartmentId, Guid? LocationId);

    /// <summary>
    /// Phạm vi đọc lịch/bảng công. HR/Admin và Ban giám đốc cấp công ty xem toàn bộ; người có
    /// attendance.read còn lại bị giới hạn theo chức vụ quản lý phòng/chi nhánh. Thiếu hồ sơ hoặc
    /// thiếu khóa phạm vi thì đóng về chính mình, không suy rộng.
    /// </summary>
    private static async Task<AttendanceScope> ResolveAttendanceScopeAsync(
        NpgsqlConnection conn, ClaimsPrincipal u)
    {
        if (u.Can(Permissions.AttendanceManage) || u.Can(Permissions.CompanyScopeAll))
            return new AttendanceScope(AttendanceScopeKind.All, null, null, null);

        Guid? employeeId = null;
        Guid? departmentId = null;
        Guid? locationId = null;
        var accessRole = "staff";
        await using (var r = await conn.Cmd("""
            SELECT e.id, e.access_role, e.department_id, e.location_id
            FROM app_users account
            JOIN hr_employees e
              ON e.user_id=account.id
              OR (e.user_id IS NULL AND lower(e.username)=lower(account.username))
            WHERE account.username=@username AND account.is_deleted=FALSE
            ORDER BY (e.user_id=account.id) DESC, e.created_at, e.id
            LIMIT 1
            """).With("@username", u.Username()).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                employeeId = r.Guid("id");
                accessRole = r.Str("access_role");
                departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? null : r.Guid("department_id");
                locationId = r.IsDBNull(r.GetOrdinal("location_id")) ? null : r.Guid("location_id");
            }
        }

        if (u.Can(Permissions.AttendanceRead))
        {
            if (string.Equals(accessRole, "location_manager", StringComparison.Ordinal)
                && locationId is not null)
                return new AttendanceScope(AttendanceScopeKind.Location, employeeId, departmentId, locationId);
            if (string.Equals(accessRole, "dept_manager", StringComparison.Ordinal)
                && departmentId is not null)
                return new AttendanceScope(AttendanceScopeKind.Department, employeeId, departmentId, locationId);
        }

        return new AttendanceScope(AttendanceScopeKind.Self, employeeId, departmentId, locationId);
    }

    private static async Task<bool> EmployeeWithinAttendanceScopeAsync(
        NpgsqlConnection conn, Guid employeeId, AttendanceScope scope)
    {
        if (scope.Kind == AttendanceScopeKind.All)
            return await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_employees WHERE id=@id)")
                .With("@id", employeeId).ExecuteScalarAsync() is true;
        if (scope.Kind == AttendanceScopeKind.Self)
            return scope.EmployeeId == employeeId;

        var sql = scope.Kind switch
        {
            AttendanceScopeKind.Department =>
                "SELECT EXISTS(SELECT 1 FROM hr_employees WHERE id=@id AND department_id=@scope)",
            AttendanceScopeKind.Location =>
                "SELECT EXISTS(SELECT 1 FROM hr_employees WHERE id=@id AND location_id=@scope)",
            _ => "SELECT FALSE",
        };
        var scopeId = scope.Kind == AttendanceScopeKind.Department
            ? scope.DepartmentId
            : scope.LocationId;
        if (scopeId is null) return false;
        return await conn.Cmd(sql).With("@id", employeeId).With("@scope", scopeId.Value)
            .ExecuteScalarAsync() is true;
    }

    /// <summary>
    /// Chỉ ghi audit. Tín hiệu real-time do trigger trên hr_shifts / hr_shift_assignments / hr_holidays
    /// tự phát scope 'hr' sau khi commit (xem DatabaseChangePublisher) — không gọi hub ở đây nữa.
    /// </summary>
    private static async Task Signal(Database db, ClaimsPrincipal u, string action, string entity, string name)
        => await db.RecordAudit(u.Username(), action, entity, name, $"{action} (web).");

    public record SaveShiftReq(string? Code, string? Name, string? StartTime, string? EndTime,
        int BreakMinutes, int LateGraceMinutes, decimal StandardHours, bool IsOvernight,
        int CheckoutGraceMinutes = 120);
    public record AssignShiftReq(Guid EmployeeId, Guid ShiftId, DateOnly WorkDate, string? Note);
    public record SaveHolidayReq(DateOnly HolidayDate, string? Name, string? HolidayType, string? Note);
}
