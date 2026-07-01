using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Nền tảng nhân sự: phòng ban, hồ sơ nhân viên, hợp đồng, phiếu lương, số phép, bằng cấp/chứng chỉ.
/// Mọi module khác (chấm công/ca làm, đơn từ) liên kết về hr_employees.id; cầu nối với dữ liệu
/// khuôn mặt/chấm công cũ dùng chung cột username.
/// </summary>
public static class HrEndpoints
{
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS hr_departments (
                id uuid PRIMARY KEY,
                code varchar(32) NOT NULL DEFAULT '',
                name varchar(200) NOT NULL,
                parent_id uuid NULL REFERENCES hr_departments(id) ON DELETE SET NULL,
                manager_employee_id uuid NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE SEQUENCE IF NOT EXISTS hr_employee_code_seq START 1;

            CREATE TABLE IF NOT EXISTS hr_employees (
                id uuid PRIMARY KEY,
                employee_code varchar(32) NOT NULL DEFAULT '',
                user_id uuid NULL REFERENCES app_users(id) ON DELETE SET NULL,
                username varchar(128) NOT NULL DEFAULT '',
                full_name varchar(200) NOT NULL DEFAULT '',
                dob date NULL,
                gender varchar(16) NOT NULL DEFAULT '',
                phone varchar(32) NOT NULL DEFAULT '',
                email varchar(200) NOT NULL DEFAULT '',
                address text NOT NULL DEFAULT '',
                department_id uuid NULL REFERENCES hr_departments(id) ON DELETE SET NULL,
                position varchar(120) NOT NULL DEFAULT '',
                manager_id uuid NULL REFERENCES hr_employees(id) ON DELETE SET NULL,
                hire_date date NULL,
                status varchar(20) NOT NULL DEFAULT 'Active',
                avatar text NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_employees_code ON hr_employees (employee_code) WHERE employee_code <> '';
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_employees_username ON hr_employees (username) WHERE username <> '';
            CREATE INDEX IF NOT EXISTS ix_hr_employees_department ON hr_employees (department_id);
            CREATE INDEX IF NOT EXISTS ix_hr_employees_manager ON hr_employees (manager_id);

            CREATE TABLE IF NOT EXISTS hr_contracts (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                contract_no varchar(64) NOT NULL DEFAULT '',
                contract_type varchar(64) NOT NULL DEFAULT '',
                start_date date NULL,
                end_date date NULL,
                base_salary numeric(18,2) NOT NULL DEFAULT 0,
                allowance numeric(18,2) NOT NULL DEFAULT 0,
                status varchar(20) NOT NULL DEFAULT 'Active',
                note text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_hr_contracts_emp ON hr_contracts (employee_id, start_date DESC);

            CREATE TABLE IF NOT EXISTS hr_payslips (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                period varchar(7) NOT NULL DEFAULT '',
                work_days numeric(6,2) NOT NULL DEFAULT 0,
                overtime_hours numeric(6,2) NOT NULL DEFAULT 0,
                base_salary numeric(18,2) NOT NULL DEFAULT 0,
                allowance numeric(18,2) NOT NULL DEFAULT 0,
                overtime_pay numeric(18,2) NOT NULL DEFAULT 0,
                deductions numeric(18,2) NOT NULL DEFAULT 0,
                net_pay numeric(18,2) NOT NULL DEFAULT 0,
                note text NOT NULL DEFAULT '',
                published boolean NOT NULL DEFAULT FALSE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_payslips_emp_period ON hr_payslips (employee_id, period);

            CREATE TABLE IF NOT EXISTS hr_leave_balances (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                year integer NOT NULL,
                leave_type varchar(32) NOT NULL DEFAULT 'annual',
                total_days numeric(6,1) NOT NULL DEFAULT 0,
                used_days numeric(6,1) NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_leave_balances ON hr_leave_balances (employee_id, year, leave_type);

            CREATE TABLE IF NOT EXISTS hr_documents (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                doc_type varchar(32) NOT NULL DEFAULT 'certificate',
                title varchar(200) NOT NULL DEFAULT '',
                issued_by varchar(200) NOT NULL DEFAULT '',
                issued_date date NULL,
                file_url text NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_hr_documents_emp ON hr_documents (employee_id, doc_type);
            """).ExecuteNonQueryAsync(ct);
    }

    // ---- Cầu nối tài khoản → hồ sơ nhân viên (tự tạo hồ sơ tối thiểu ở lần truy cập đầu) ----

    /// <summary>Lấy (hoặc tạo mới tối thiểu) id hồ sơ nhân viên cho một username đăng nhập.</summary>
    public static async Task<Guid> EnsureEmployeeForUser(NpgsqlConnection conn, string username)
    {
        var existing = await conn.Cmd("SELECT id FROM hr_employees WHERE username = @u LIMIT 1")
            .With("@u", username).ExecuteScalarAsync();
        if (existing is Guid g) return g;

        // Đọc thông tin cơ bản từ tài khoản để dựng hồ sơ.
        Guid? userId = null;
        var fullName = username;
        var email = "";
        await using (var r = await conn.Cmd(
            "SELECT id, full_name, email FROM app_users WHERE username = @u AND is_deleted = FALSE LIMIT 1")
            .With("@u", username).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                userId = r.Guid("id");
                var fn = r.Str("full_name");
                if (!string.IsNullOrWhiteSpace(fn)) fullName = fn;
                email = r.Str("email");
            }
        }

        var id = Guid.NewGuid();
        var code = await NextEmployeeCode(conn);
        await conn.Cmd("""
            INSERT INTO hr_employees (id, employee_code, user_id, username, full_name, email, status)
            VALUES (@id, @code, @uid, @u, @fn, @em, 'Active')
            ON CONFLICT (username) WHERE username <> '' DO NOTHING
            """)
            .With("@id", id).With("@code", code).With("@uid", (object?)userId ?? DBNull.Value)
            .With("@u", username).With("@fn", fullName).With("@em", email)
            .ExecuteNonQueryAsync();

        var again = await conn.Cmd("SELECT id FROM hr_employees WHERE username = @u LIMIT 1")
            .With("@u", username).ExecuteScalarAsync();
        return again is Guid g2 ? g2 : id;
    }

    private static async Task<string> NextEmployeeCode(NpgsqlConnection conn)
    {
        var n = Convert.ToInt64(await conn.Cmd("SELECT nextval('hr_employee_code_seq')").ExecuteScalarAsync());
        return $"NV{n:D4}";
    }

    public static void MapHr(this WebApplication app)
    {
        var g = app.MapGroup("/api/hr").RequireAuthorization();

        // ---------------- Phòng ban ----------------
        g.MapGet("/departments", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT d.id, d.code, d.name, d.parent_id, d.manager_employee_id,
                       COALESCE(p.name, '') AS parent_name,
                       COALESCE(m.full_name, '') AS manager_name,
                       (SELECT COUNT(*) FROM hr_employees e WHERE e.department_id = d.id) AS emp_count
                FROM hr_departments d
                LEFT JOIN hr_departments p ON p.id = d.parent_id
                LEFT JOIN hr_employees m ON m.id = d.manager_employee_id
                ORDER BY d.name
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    code = r.Str("code"),
                    name = r.Str("name"),
                    parentId = r.IsDBNull(r.GetOrdinal("parent_id")) ? (Guid?)null : r.Guid("parent_id"),
                    parentName = r.Str("parent_name"),
                    managerEmployeeId = r.IsDBNull(r.GetOrdinal("manager_employee_id")) ? (Guid?)null : r.Guid("manager_employee_id"),
                    managerName = r.Str("manager_name"),
                    employeeCount = r.Int("emp_count"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/departments", async (SaveDepartmentReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { message = "Vui lòng nhập tên phòng ban." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_departments (id, code, name, parent_id, manager_employee_id)
                VALUES (@id, @code, @name, @parent, @mgr)
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name.Trim())
                .With("@parent", (object?)req.ParentId ?? DBNull.Value)
                .With("@mgr", (object?)req.ManagerEmployeeId ?? DBNull.Value)
                .ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Tạo phòng ban", "Department", req.Name);
            return Results.Ok(new { id });
        });

        g.MapPut("/departments/{id:guid}", async (Guid id, SaveDepartmentReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_departments SET code=@code, name=@name, parent_id=@parent, manager_employee_id=@mgr
                WHERE id=@id
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", (req.Name ?? "").Trim())
                .With("@parent", (object?)req.ParentId ?? DBNull.Value)
                .With("@mgr", (object?)req.ManagerEmployeeId ?? DBNull.Value)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Cập nhật phòng ban", "Department", req.Name ?? "");
            return Results.NoContent();
        });

        g.MapDelete("/departments/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_departments WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa phòng ban", "Department", id.ToString());
            return Results.NoContent();
        });

        // ---------------- Danh bạ / hồ sơ nhân viên ----------------
        g.MapGet("/employees", async (Database db, string? search, Guid? departmentId) =>
        {
            await using var conn = await db.OpenAsync();
            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                where.Add("(e.full_name ILIKE @s OR e.employee_code ILIKE @s OR e.username ILIKE @s OR e.position ILIKE @s)");
            if (departmentId is not null) where.Add("e.department_id = @dept");
            var sql = $"""
                SELECT e.id, e.employee_code, e.username, e.full_name, e.position, e.status,
                       e.phone, e.email, e.avatar, e.department_id,
                       COALESCE(d.name, '') AS department_name,
                       COALESCE(m.full_name, '') AS manager_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id = e.department_id
                LEFT JOIN hr_employees m ON m.id = e.manager_id
                {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
                ORDER BY e.full_name
                """;
            var cmd = conn.Cmd(sql);
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search.Trim()}%");
            if (departmentId is not null) cmd.With("@dept", departmentId.Value);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(ReadEmployeeCard(r));
            return Results.Ok(list);
        });

        // Hồ sơ của chính người đang đăng nhập (tự tạo nếu chưa có).
        g.MapGet("/me", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var id = await EnsureEmployeeForUser(conn, u.Username());
            return Results.Ok(await ReadEmployeeDetail(conn, id));
        });

        g.MapGet("/employees/{id:guid}", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var detail = await ReadEmployeeDetail(conn, id);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        g.MapPost("/employees", async (SaveEmployeeReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.FullName)) return Results.BadRequest(new { message = "Vui lòng nhập họ tên." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            var code = string.IsNullOrWhiteSpace(req.EmployeeCode) ? await NextEmployeeCode(conn) : req.EmployeeCode!.Trim();
            try
            {
                await conn.Cmd("""
                    INSERT INTO hr_employees
                        (id, employee_code, username, full_name, dob, gender, phone, email, address,
                         department_id, position, manager_id, hire_date, status)
                    VALUES (@id, @code, @username, @fn, @dob, @gender, @phone, @email, @addr,
                            @dept, @pos, @mgr, @hire, @status)
                    """)
                    .With("@id", id).With("@code", code).With("@username", (req.Username ?? "").Trim())
                    .With("@fn", req.FullName.Trim()).With("@dob", (object?)req.Dob ?? DBNull.Value)
                    .With("@gender", req.Gender ?? "").With("@phone", req.Phone ?? "").With("@email", req.Email ?? "")
                    .With("@addr", req.Address ?? "").With("@dept", (object?)req.DepartmentId ?? DBNull.Value)
                    .With("@pos", req.Position ?? "").With("@mgr", (object?)req.ManagerId ?? DBNull.Value)
                    .With("@hire", (object?)req.HireDate ?? DBNull.Value).With("@status", req.Status ?? "Active")
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Json(new { message = "Mã nhân viên hoặc tài khoản đã tồn tại." }, statusCode: 400);
            }
            await Signal(hub, db, u, "Tạo hồ sơ nhân viên", "Employee", req.FullName);
            return Results.Ok(new { id, employeeCode = code });
        });

        g.MapPut("/employees/{id:guid}", async (Guid id, SaveEmployeeReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            // Admin sửa mọi hồ sơ; nhân viên chỉ sửa liên hệ của chính mình.
            var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", id).ExecuteScalarAsync() as string;
            var isSelf = string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase);
            if (!u.IsAdmin() && !isSelf) return Results.Forbid();

            NpgsqlCommand cmd;
            if (u.IsAdmin())
            {
                cmd = conn.Cmd("""
                    UPDATE hr_employees SET employee_code=@code, username=@username, full_name=@fn, dob=@dob,
                        gender=@gender, phone=@phone, email=@email, address=@addr, department_id=@dept,
                        position=@pos, manager_id=@mgr, hire_date=@hire, status=@status, avatar=@avatar,
                        updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id
                    """)
                    .With("@code", (req.EmployeeCode ?? "").Trim()).With("@username", (req.Username ?? "").Trim())
                    .With("@fn", (req.FullName ?? "").Trim()).With("@dob", (object?)req.Dob ?? DBNull.Value)
                    .With("@gender", req.Gender ?? "").With("@phone", req.Phone ?? "").With("@email", req.Email ?? "")
                    .With("@addr", req.Address ?? "").With("@dept", (object?)req.DepartmentId ?? DBNull.Value)
                    .With("@pos", req.Position ?? "").With("@mgr", (object?)req.ManagerId ?? DBNull.Value)
                    .With("@hire", (object?)req.HireDate ?? DBNull.Value).With("@status", req.Status ?? "Active")
                    .With("@avatar", (object?)req.Avatar ?? DBNull.Value);
            }
            else
            {
                // Nhân viên chỉ được cập nhật liên hệ cá nhân.
                cmd = conn.Cmd("""
                    UPDATE hr_employees SET phone=@phone, email=@email, address=@addr, dob=@dob,
                        gender=@gender, avatar=@avatar, updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id
                    """)
                    .With("@phone", req.Phone ?? "").With("@email", req.Email ?? "").With("@addr", req.Address ?? "")
                    .With("@dob", (object?)req.Dob ?? DBNull.Value).With("@gender", req.Gender ?? "")
                    .With("@avatar", (object?)req.Avatar ?? DBNull.Value);
            }
            cmd.With("@id", id);
            var n = await cmd.ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Cập nhật hồ sơ nhân viên", "Employee", req.FullName ?? id.ToString());
            return Results.NoContent();
        });

        g.MapDelete("/employees/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_employees WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa hồ sơ nhân viên", "Employee", id.ToString());
            return Results.NoContent();
        });

        // ---------------- Hợp đồng ----------------
        g.MapGet("/employees/{id:guid}/contracts", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, contract_no, contract_type, start_date, end_date, base_salary, allowance, status, note
                FROM hr_contracts WHERE employee_id=@id ORDER BY start_date DESC NULLS LAST, created_at DESC
                """).With("@id", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    contractNo = r.Str("contract_no"),
                    contractType = r.Str("contract_type"),
                    startDate = DateOrNull(r, "start_date"),
                    endDate = DateOrNull(r, "end_date"),
                    baseSalary = r.Dec("base_salary"),
                    allowance = r.Dec("allowance"),
                    status = r.Str("status"),
                    note = r.Str("note"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/employees/{id:guid}/contracts", async (Guid id, SaveContractReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var cid = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_contracts (id, employee_id, contract_no, contract_type, start_date, end_date, base_salary, allowance, status, note)
                VALUES (@id, @emp, @no, @type, @start, @end, @base, @allow, @status, @note)
                """)
                .With("@id", cid).With("@emp", id).With("@no", req.ContractNo ?? "").With("@type", req.ContractType ?? "")
                .With("@start", (object?)req.StartDate ?? DBNull.Value).With("@end", (object?)req.EndDate ?? DBNull.Value)
                .With("@base", req.BaseSalary).With("@allow", req.Allowance).With("@status", req.Status ?? "Active")
                .With("@note", req.Note ?? "").ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Thêm hợp đồng", "Contract", req.ContractNo ?? "");
            return Results.Ok(new { id = cid });
        });

        g.MapPut("/contracts/{cid:guid}", async (Guid cid, SaveContractReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_contracts SET contract_no=@no, contract_type=@type, start_date=@start, end_date=@end,
                    base_salary=@base, allowance=@allow, status=@status, note=@note WHERE id=@id
                """)
                .With("@id", cid).With("@no", req.ContractNo ?? "").With("@type", req.ContractType ?? "")
                .With("@start", (object?)req.StartDate ?? DBNull.Value).With("@end", (object?)req.EndDate ?? DBNull.Value)
                .With("@base", req.BaseSalary).With("@allow", req.Allowance).With("@status", req.Status ?? "Active")
                .With("@note", req.Note ?? "").ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Cập nhật hợp đồng", "Contract", req.ContractNo ?? "");
            return Results.NoContent();
        });

        g.MapDelete("/contracts/{cid:guid}", async (Guid cid, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_contracts WHERE id=@id").With("@id", cid).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa hợp đồng", "Contract", cid.ToString());
            return Results.NoContent();
        });

        // ---------------- Phiếu lương ----------------
        g.MapGet("/employees/{id:guid}/payslips", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            // Nhân viên chỉ xem phiếu đã phát hành của mình; admin xem tất cả.
            var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", id).ExecuteScalarAsync() as string;
            var isSelf = string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase);
            if (!u.IsAdmin() && !isSelf) return Results.Forbid();
            var onlyPublished = !u.IsAdmin();
            var list = new List<object>();
            await using var r = await conn.Cmd($"""
                SELECT id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, published
                FROM hr_payslips WHERE employee_id=@id {(onlyPublished ? "AND published = TRUE" : "")}
                ORDER BY period DESC
                """).With("@id", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    period = r.Str("period"),
                    workDays = r.Dec("work_days"),
                    overtimeHours = r.Dec("overtime_hours"),
                    baseSalary = r.Dec("base_salary"),
                    allowance = r.Dec("allowance"),
                    overtimePay = r.Dec("overtime_pay"),
                    deductions = r.Dec("deductions"),
                    netPay = r.Dec("net_pay"),
                    note = r.Str("note"),
                    published = r.Bool("published"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/employees/{id:guid}/payslips", async (Guid id, SavePayslipReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Period)) return Results.BadRequest(new { message = "Thiếu kỳ lương (yyyy-MM)." });
            await using var conn = await db.OpenAsync();
            var pid = Guid.NewGuid();
            var net = req.BaseSalary + req.Allowance + req.OvertimePay - req.Deductions;
            await conn.Cmd("""
                INSERT INTO hr_payslips (id, employee_id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, published)
                VALUES (@id, @emp, @period, @wd, @ot, @base, @allow, @otp, @ded, @net, @note, @pub)
                ON CONFLICT (employee_id, period) DO UPDATE SET
                    work_days=@wd, overtime_hours=@ot, base_salary=@base, allowance=@allow,
                    overtime_pay=@otp, deductions=@ded, net_pay=@net, note=@note, published=@pub
                """)
                .With("@id", pid).With("@emp", id).With("@period", req.Period.Trim())
                .With("@wd", req.WorkDays).With("@ot", req.OvertimeHours).With("@base", req.BaseSalary)
                .With("@allow", req.Allowance).With("@otp", req.OvertimePay).With("@ded", req.Deductions)
                .With("@net", net).With("@note", req.Note ?? "").With("@pub", req.Published)
                .ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Lập phiếu lương", "Payslip", req.Period);
            return Results.Ok(new { id = pid });
        });

        g.MapDelete("/payslips/{pid:guid}", async (Guid pid, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_payslips WHERE id=@id").With("@id", pid).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa phiếu lương", "Payslip", pid.ToString());
            return Results.NoContent();
        });

        // ---------------- Số ngày phép ----------------
        g.MapGet("/employees/{id:guid}/leave-balances", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, year, leave_type, total_days, used_days
                FROM hr_leave_balances WHERE employee_id=@id ORDER BY year DESC, leave_type
                """).With("@id", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    year = r.Int("year"),
                    leaveType = r.Str("leave_type"),
                    totalDays = r.Dec("total_days"),
                    usedDays = r.Dec("used_days"),
                    remainingDays = r.Dec("total_days") - r.Dec("used_days"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/employees/{id:guid}/leave-balances", async (Guid id, SaveLeaveBalanceReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var bid = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_leave_balances (id, employee_id, year, leave_type, total_days, used_days)
                VALUES (@id, @emp, @year, @type, @total, @used)
                ON CONFLICT (employee_id, year, leave_type) DO UPDATE SET total_days=@total, used_days=@used
                """)
                .With("@id", bid).With("@emp", id).With("@year", req.Year)
                .With("@type", req.LeaveType ?? "annual").With("@total", req.TotalDays).With("@used", req.UsedDays)
                .ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Cập nhật số phép", "LeaveBalance", $"{req.Year}/{req.LeaveType}");
            return Results.Ok(new { id = bid });
        });

        // ---------------- Bằng cấp / chứng chỉ / khen thưởng ----------------
        g.MapGet("/employees/{id:guid}/documents", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, doc_type, title, issued_by, issued_date, file_url, note
                FROM hr_documents WHERE employee_id=@id ORDER BY issued_date DESC NULLS LAST, created_at DESC
                """).With("@id", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    docType = r.Str("doc_type"),
                    title = r.Str("title"),
                    issuedBy = r.Str("issued_by"),
                    issuedDate = DateOrNull(r, "issued_date"),
                    fileUrl = r.Str("file_url"),
                    note = r.Str("note"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/employees/{id:guid}/documents", async (Guid id, SaveDocumentReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", id).ExecuteScalarAsync() as string;
            var isSelf = string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase);
            if (!u.IsAdmin() && !isSelf) return Results.Forbid();
            var did = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_documents (id, employee_id, doc_type, title, issued_by, issued_date, file_url, note)
                VALUES (@id, @emp, @type, @title, @by, @date, @url, @note)
                """)
                .With("@id", did).With("@emp", id).With("@type", req.DocType ?? "certificate")
                .With("@title", req.Title ?? "").With("@by", req.IssuedBy ?? "")
                .With("@date", (object?)req.IssuedDate ?? DBNull.Value).With("@url", req.FileUrl ?? "").With("@note", req.Note ?? "")
                .ExecuteNonQueryAsync();
            await Signal(hub, db, u, "Thêm hồ sơ bằng cấp", "EmployeeDocument", req.Title ?? "");
            return Results.Ok(new { id = did });
        });

        g.MapDelete("/documents/{did:guid}", async (Guid did, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.IsAdmin())
            {
                var owner = await conn.Cmd("""
                    SELECT e.username FROM hr_documents d JOIN hr_employees e ON e.id = d.employee_id WHERE d.id=@id
                    """).With("@id", did).ExecuteScalarAsync() as string;
                if (!string.Equals(owner, u.Username(), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            }
            var n = await conn.Cmd("DELETE FROM hr_documents WHERE id=@id").With("@id", did).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(hub, db, u, "Xóa hồ sơ bằng cấp", "EmployeeDocument", did.ToString());
            return Results.NoContent();
        });
    }

    // ---- Đọc dữ liệu ----

    private static object ReadEmployeeCard(NpgsqlDataReader r) => new
    {
        id = r.Guid("id"),
        employeeCode = r.Str("employee_code"),
        username = r.Str("username"),
        fullName = r.Str("full_name"),
        position = r.Str("position"),
        status = r.Str("status"),
        phone = r.Str("phone"),
        email = r.Str("email"),
        avatar = r.IsDBNull(r.GetOrdinal("avatar")) ? null : r.Str("avatar"),
        departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? (Guid?)null : r.Guid("department_id"),
        departmentName = r.Str("department_name"),
        managerName = r.Str("manager_name"),
    };

    private static async Task<object?> ReadEmployeeDetail(NpgsqlConnection conn, Guid id)
    {
        await using var r = await conn.Cmd("""
            SELECT e.id, e.employee_code, e.username, e.full_name, e.dob, e.gender, e.phone, e.email, e.address,
                   e.department_id, e.position, e.manager_id, e.hire_date, e.status, e.avatar,
                   COALESCE(d.name, '') AS department_name,
                   COALESCE(m.full_name, '') AS manager_name
            FROM hr_employees e
            LEFT JOIN hr_departments d ON d.id = e.department_id
            LEFT JOIN hr_employees m ON m.id = e.manager_id
            WHERE e.id=@id
            """).With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new
        {
            id = r.Guid("id"),
            employeeCode = r.Str("employee_code"),
            username = r.Str("username"),
            fullName = r.Str("full_name"),
            dob = DateOrNull(r, "dob"),
            gender = r.Str("gender"),
            phone = r.Str("phone"),
            email = r.Str("email"),
            address = r.Str("address"),
            departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? (Guid?)null : r.Guid("department_id"),
            departmentName = r.Str("department_name"),
            position = r.Str("position"),
            managerId = r.IsDBNull(r.GetOrdinal("manager_id")) ? (Guid?)null : r.Guid("manager_id"),
            managerName = r.Str("manager_name"),
            hireDate = DateOrNull(r, "hire_date"),
            status = r.Str("status"),
            avatar = r.IsDBNull(r.GetOrdinal("avatar")) ? null : r.Str("avatar"),
        };
    }

    private static DateOnly? DateOrNull(NpgsqlDataReader r, string col)
        => r.IsDBNull(r.GetOrdinal(col)) ? (DateOnly?)null : r.DateOnly(col);

    private static async Task Signal(IHubContext<ChangesHub> hub, Database db, ClaimsPrincipal u, string action, string entity, string name)
    {
        await db.RecordAudit(u.Username(), action, entity, name, $"{action} (web).");
        await hub.Clients.All.SendAsync("changed", "data");
    }

    // ---- DTO nhận từ client ----
    public record SaveDepartmentReq(string? Code, string? Name, Guid? ParentId, Guid? ManagerEmployeeId);
    public record SaveEmployeeReq(string? EmployeeCode, string? Username, string? FullName, DateOnly? Dob, string? Gender,
        string? Phone, string? Email, string? Address, Guid? DepartmentId, string? Position, Guid? ManagerId,
        DateOnly? HireDate, string? Status, string? Avatar);
    public record SaveContractReq(string? ContractNo, string? ContractType, DateOnly? StartDate, DateOnly? EndDate,
        decimal BaseSalary, decimal Allowance, string? Status, string? Note);
    public record SavePayslipReq(string? Period, decimal WorkDays, decimal OvertimeHours, decimal BaseSalary,
        decimal Allowance, decimal OvertimePay, decimal Deductions, string? Note, bool Published);
    public record SaveLeaveBalanceReq(int Year, string? LeaveType, decimal TotalDays, decimal UsedDays);
    public record SaveDocumentReq(string? DocType, string? Title, string? IssuedBy, DateOnly? IssuedDate, string? FileUrl, string? Note);
}
