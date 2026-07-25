using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Nền tảng nhân sự: phòng ban, hồ sơ nhân viên, hợp đồng, phiếu lương, số phép, bằng cấp/chứng chỉ.
/// Mọi module khác (chấm công/ca làm, đơn từ) liên kết về hr_employees.id; cầu nối với dữ liệu
/// khuôn mặt/chấm công cũ dùng chung cột username.
/// </summary>
public static class HrEndpoints
{
    private const string Tz = "Asia/Ho_Chi_Minh";

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
                is_accounting boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            ALTER TABLE hr_departments ADD COLUMN IF NOT EXISTS is_accounting boolean NOT NULL DEFAULT false;

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
            ALTER TABLE hr_employees ADD COLUMN IF NOT EXISTS show_phone_in_directory boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE hr_employees ADD COLUMN IF NOT EXISTS show_email_in_directory boolean NOT NULL DEFAULT FALSE;

            -- Địa điểm/chi nhánh làm việc (phục vụ phân quyền theo địa điểm).
            CREATE TABLE IF NOT EXISTS hr_locations (
                id uuid PRIMARY KEY,
                code varchar(32) NOT NULL DEFAULT '',
                name varchar(200) NOT NULL,
                address text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Phân quyền: mỗi nhân viên gắn địa điểm + vai trò truy cập (chức vụ phân quyền).
            -- access_role: 'staff' | 'dept_manager' (quản lý phòng ban) | 'location_manager' (quản lý địa điểm).
            ALTER TABLE hr_employees ADD COLUMN IF NOT EXISTS location_id uuid NULL REFERENCES hr_locations(id) ON DELETE SET NULL;
            ALTER TABLE hr_employees ADD COLUMN IF NOT EXISTS access_role varchar(24) NOT NULL DEFAULT 'staff';
            CREATE INDEX IF NOT EXISTS ix_hr_employees_location ON hr_employees (location_id);

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
                details jsonb NOT NULL DEFAULT '{}',
                published boolean NOT NULL DEFAULT FALSE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            ALTER TABLE hr_payslips ADD COLUMN IF NOT EXISTS details jsonb NOT NULL DEFAULT '{}';
            ALTER TABLE hr_payslips ADD COLUMN IF NOT EXISTS acknowledged_at timestamptz NULL;
            CREATE TABLE IF NOT EXISTS hr_payslip_inquiries(
              id uuid PRIMARY KEY,payslip_id uuid NOT NULL REFERENCES hr_payslips(id) ON DELETE CASCADE,
              employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,line_label varchar(200) NOT NULL DEFAULT '',
              message text NOT NULL,status varchar(20) NOT NULL DEFAULT 'open',response text NOT NULL DEFAULT '',created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);
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
            ALTER TABLE hr_documents ADD COLUMN IF NOT EXISTS doc_number varchar(120) NOT NULL DEFAULT '';
            ALTER TABLE hr_documents ADD COLUMN IF NOT EXISTS expires_at date NULL;
            ALTER TABLE hr_documents ADD COLUMN IF NOT EXISTS approval_status varchar(20) NOT NULL DEFAULT 'approved';
            ALTER TABLE hr_documents ADD COLUMN IF NOT EXISTS file_name varchar(260) NOT NULL DEFAULT '';
            ALTER TABLE hr_documents ADD COLUMN IF NOT EXISTS mime_type varchar(120) NOT NULL DEFAULT '';
            ALTER TABLE hr_documents ADD COLUMN IF NOT EXISTS file_content bytea NULL;

            -- Thư tri ân "tròn X năm gắn bó": MỘT mẫu dùng chung (bảng một dòng, id=1). App tự tính mốc
            -- tròn năm theo hire_date và điền các chỗ trống {ten}/{so_nam}/{ngay_vao_lam}.
            CREATE TABLE IF NOT EXISTS hr_anniversary_letter (
                id smallint PRIMARY KEY DEFAULT 1 CHECK (id = 1),
                enabled boolean NOT NULL DEFAULT true,
                title text NOT NULL DEFAULT '',
                body text NOT NULL DEFAULT '',
                signature text NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO hr_anniversary_letter (id, enabled, title, body, signature)
            VALUES (
                1,
                true,
                'Tri ân {so_nam} năm gắn bó',
                E'Kính gửi Anh/Chị {ten},\n\nNhân dịp Anh/Chị tròn {so_nam} năm làm việc tại Công ty, Công ty xin gửi lời cảm ơn vì sự đồng hành và những đóng góp của Anh/Chị trong suốt thời gian qua.\n\n{so_nam} năm là một dấu mốc đáng ghi nhận. Trong quá trình làm việc, Anh/Chị đã tích lũy nhiều kinh nghiệm, hoàn thành các nhiệm vụ được giao và góp phần vào hoạt động chung của tập thể.\n\nCông ty trân trọng sự gắn bó của Anh/Chị và mong rằng trong thời gian tới, Anh/Chị sẽ tiếp tục phối hợp tốt cùng đồng nghiệp, duy trì tinh thần trách nhiệm và cùng Công ty hướng tới những mục tiêu mới.\n\nKính chúc Anh/Chị nhiều sức khỏe, thuận lợi trong công việc và cuộc sống.',
                E'Trân trọng,\nBan Lãnh đạo Công ty'
            )
            ON CONFLICT (id) DO NOTHING;
            """).ExecuteNonQueryAsync(ct);

        await EnsureAccountingDepartment(conn, ct);
        await BackfillMissingDepartments(conn, ct);
        await BackfillSharedEmployeeAccountData(conn, ct);
    }

    /// <summary>
    /// Phòng Kế Toán là phòng ban do HỆ THỐNG quản lý: nó mang cờ <c>is_accounting</c> — cửa duy nhất để
    /// nhân viên trong phòng được lập/duyệt phiếu chi tiền mặt (xem PayoutVoucherEndpoints). Vì là quyền
    /// nhạy cảm nên KHÔNG để người dùng tự bật/tắt bằng ô tích trong form phòng ban; hệ thống tự bảo đảm
    /// luôn tồn tại đúng một phòng kế toán. Idempotent, không đụng dữ liệu đã đánh dấu sẵn.
    /// </summary>
    private static async Task<Guid> EnsureAccountingDepartment(NpgsqlConnection conn, CancellationToken ct)
    {
        const string accountingName = "Phòng Kế Toán";

        // Đã có phòng được đánh dấu kế toán (kể cả dữ liệu cũ) → giữ nguyên, dùng phòng đầu tiên.
        if (await conn.Cmd("SELECT id FROM hr_departments WHERE is_accounting = true ORDER BY created_at LIMIT 1")
                .ExecuteScalarAsync(ct) is Guid existing)
            return existing;

        // Có phòng trùng tên nhưng chưa bật cờ → bật cờ cho nó thay vì tạo phòng mới trùng tên.
        if (await conn.Cmd("SELECT id FROM hr_departments WHERE name = @n LIMIT 1")
                .With("@n", accountingName).ExecuteScalarAsync(ct) is Guid byName)
        {
            await conn.Cmd("UPDATE hr_departments SET is_accounting = true WHERE id = @id")
                .With("@id", byName).ExecuteNonQueryAsync(ct);
            return byName;
        }

        // Chưa có gì → tạo phòng kế toán chuẩn của hệ thống.
        var newId = Guid.NewGuid();
        await conn.Cmd("INSERT INTO hr_departments (id, code, name, is_accounting) VALUES (@id, @c, @n, true)")
            .With("@id", newId).With("@c", "KT").With("@n", accountingName).ExecuteNonQueryAsync(ct);
        return newId;
    }

    /// <summary>
    /// Mọi nhân viên phải thuộc một phòng ban. Với dữ liệu cũ còn nhân viên chưa gán phòng ban,
    /// gán tạm về phòng kế toán của hệ thống (<see cref="EnsureAccountingDepartment"/>).
    /// </summary>
    private static async Task BackfillMissingDepartments(NpgsqlConnection conn, CancellationToken ct)
    {
        var hasOrphans = await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_employees WHERE department_id IS NULL)")
            .ExecuteScalarAsync(ct) is bool b && b;
        if (!hasOrphans) return;

        var deptId = await EnsureAccountingDepartment(conn, ct);
        await conn.Cmd("UPDATE hr_employees SET department_id = @d WHERE department_id IS NULL")
            .With("@d", deptId).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Dữ liệu cũ có thể đã tạo hồ sơ và tài khoản ở hai thời điểm khác nhau. Ghép theo username rồi
    /// đưa họ tên/email/ngày vào làm về một nguồn thống nhất; hồ sơ HR được ưu tiên khi đã có giá trị.
    /// </summary>
    private static async Task BackfillSharedEmployeeAccountData(NpgsqlConnection conn, CancellationToken ct)
    {
        var ids = new List<Guid>();
        await using (var r = await conn.Cmd("SELECT id FROM hr_employees WHERE username <> ''").ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) ids.Add(r.Guid("id"));
        foreach (var id in ids) await SyncEmployeeAccountSharedFields(conn, id, ct);
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
        DateOnly? hireDate = null;
        await using (var r = await conn.Cmd(
            "SELECT id, full_name, email, (created_at AT TIME ZONE @tz)::date AS account_date FROM app_users WHERE username = @u AND is_deleted = FALSE LIMIT 1")
            .With("@u", username).With("@tz", Tz).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                userId = r.Guid("id");
                var fn = r.Str("full_name");
                if (!string.IsNullOrWhiteSpace(fn)) fullName = fn;
                email = r.Str("email");
                hireDate = DateOrNull(r, "account_date");
            }
        }

        var id = Guid.NewGuid();
        var code = await NextEmployeeCode(conn);
        await conn.Cmd("""
            INSERT INTO hr_employees (id, employee_code, user_id, username, full_name, email, hire_date, status)
            VALUES (@id, @code, @uid, @u, @fn, @em, @hire, 'Active')
            ON CONFLICT (username) WHERE username <> '' DO NOTHING
            """)
            .With("@id", id).With("@code", code).With("@uid", (object?)userId ?? DBNull.Value)
            .With("@u", username).With("@fn", fullName).With("@em", email).With("@hire", (object?)hireDate ?? DBNull.Value)
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

    /// <summary>
    /// Sinh tên đăng nhập từ họ tên + mã nhân viên theo quy ước: <b>tên</b> (token cuối) + <b>viết tắt</b>
    /// các token đầu (họ, đệm) + <b>phần số</b> của mã nhân viên (tối thiểu 2 chữ số). Bỏ dấu tiếng Việt.
    /// Ví dụ: "Nguyễn Văn An" + NV0001 → <c>annv01</c>.
    /// </summary>
    internal static string DeriveLoginUsername(string? fullName, string? employeeCode)
    {
        var tokens = (fullName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var given = tokens.Length > 0 ? FoldAscii(tokens[^1]) : "";
        var initials = tokens.Length > 0
            ? string.Concat(tokens[..^1].Select(FoldAscii).Where(t => t.Length > 0).Select(t => t[..1]))
            : "";
        var baseName = given + initials;
        if (baseName.Length == 0) baseName = "nv";
        var digits = new string((employeeCode ?? "").Where(char.IsDigit).ToArray());
        var num = long.TryParse(digits, out var v) ? v : 0;
        return baseName + num.ToString("D2");
    }

    /// <summary>Bỏ dấu tiếng Việt + hạ chữ thường, chỉ giữ [a-z0-9] (đ/Đ → d).</summary>
    private static string FoldAscii(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        var folded = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('đ', 'd');
        return new string(folded.Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')).ToArray());
    }

    public static void MapHr(this WebApplication app)
    {
        var g = app.MapGroup("/api/hr").RequirePermission(Permissions.HrSelfAccess);

        // ---------------- Phòng ban ----------------
        g.MapGet("/departments", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT d.id, d.code, d.name, d.parent_id, d.manager_employee_id, d.is_accounting,
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
                    isAccounting = r.Bool("is_accounting"),
                    employeeCount = r.Int("emp_count"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/departments", async (SaveDepartmentReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { message = "Vui lòng nhập tên phòng ban." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            // Cờ is_accounting KHÔNG nhận từ form — phòng kế toán do hệ thống quản lý (EnsureAccountingDepartment).
            await conn.Cmd("""
                INSERT INTO hr_departments (id, code, name, parent_id, manager_employee_id)
                VALUES (@id, @code, @name, @parent, @mgr)
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name.Trim())
                .With("@parent", (object?)req.ParentId ?? DBNull.Value)
                .With("@mgr", (object?)req.ManagerEmployeeId ?? DBNull.Value)
                .ExecuteNonQueryAsync();
            await Signal(db, u, "Tạo phòng ban", "Department", req.Name);
            return Results.Ok(new { id });
        });

        g.MapPut("/departments/{id:guid}", async (Guid id, SaveDepartmentReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            // Giữ nguyên is_accounting — form không đổi được (phòng kế toán do hệ thống quản lý).
            var n = await conn.Cmd("""
                UPDATE hr_departments SET code=@code, name=@name, parent_id=@parent, manager_employee_id=@mgr
                WHERE id=@id
                """)
                .With("@id", id).With("@code", req.Code ?? "").With("@name", (req.Name ?? "").Trim())
                .With("@parent", (object?)req.ParentId ?? DBNull.Value)
                .With("@mgr", (object?)req.ManagerEmployeeId ?? DBNull.Value)
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Cập nhật phòng ban", "Department", req.Name ?? "");
            return Results.NoContent();
        });

        g.MapDelete("/departments/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            // Không cho xóa phòng kế toán CUỐI CÙNG — đó là cửa quyền lập/duyệt phiếu chi tiền mặt.
            var isAccounting = await conn.Cmd("SELECT is_accounting FROM hr_departments WHERE id=@id")
                .With("@id", id).ExecuteScalarAsync() is bool acc && acc;
            if (isAccounting)
            {
                var others = await conn.Cmd("SELECT COUNT(*) FROM hr_departments WHERE is_accounting = true AND id <> @id")
                    .With("@id", id).ExecuteScalarAsync();
                if (Convert.ToInt64(others) == 0)
                    return Results.BadRequest(new { message = "Không thể xóa phòng kế toán cuối cùng của hệ thống." });
            }
            var n = await conn.Cmd("DELETE FROM hr_departments WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa phòng ban", "Department", id.ToString());
            return Results.NoContent();
        });

        // ---------------- Địa điểm / chi nhánh ----------------
        g.MapGet("/locations", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT l.id, l.code, l.name, l.address,
                       (SELECT COUNT(*) FROM hr_employees e WHERE e.location_id = l.id) AS emp_count
                FROM hr_locations l ORDER BY l.name
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"), code = r.Str("code"), name = r.Str("name"),
                    address = r.Str("address"), employeeCount = r.Int("emp_count"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/locations", async (SaveLocationReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { message = "Vui lòng nhập tên địa điểm." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            await conn.Cmd("INSERT INTO hr_locations (id, code, name, address) VALUES (@id, @code, @name, @addr)")
                .With("@id", id).With("@code", req.Code ?? "").With("@name", req.Name.Trim()).With("@addr", req.Address ?? "")
                .ExecuteNonQueryAsync();
            await Signal(db, u, "Tạo địa điểm", "Location", req.Name);
            return Results.Ok(new { id });
        });

        g.MapPut("/locations/{id:guid}", async (Guid id, SaveLocationReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("UPDATE hr_locations SET code=@code, name=@name, address=@addr WHERE id=@id")
                .With("@id", id).With("@code", req.Code ?? "").With("@name", (req.Name ?? "").Trim()).With("@addr", req.Address ?? "")
                .ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Cập nhật địa điểm", "Location", req.Name ?? "");
            return Results.NoContent();
        });

        g.MapDelete("/locations/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_locations WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa địa điểm", "Location", id.ToString());
            return Results.NoContent();
        });

        // ---------------- Danh bạ / hồ sơ nhân viên ----------------
        // Phân quyền theo phạm vi: Admin xem toàn bộ; quản lý phòng ban chỉ xem phòng mình; quản lý
        // địa điểm chỉ xem địa điểm mình; nhân viên thường chỉ xem chính mình. Siết truy cập danh bạ.
        g.MapGet("/employees", async (Database db, ClaimsPrincipal u, string? search, Guid? departmentId) =>
        {
            await using var conn = await db.OpenAsync();
            var scope = await ResolveScopeAsync(conn, u);

            var where = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                where.Add("(e.full_name ILIKE @s OR e.employee_code ILIKE @s OR e.username ILIKE @s OR e.position ILIKE @s)");
            if (departmentId is not null) where.Add("e.department_id = @dept");

            // Áp bộ lọc phạm vi (trừ Admin thấy tất cả).
            switch (scope.Kind)
            {
                case ScopeKind.Department when scope.DepartmentId is not null:
                    where.Add("e.department_id = @scopeDept"); break;
                case ScopeKind.Location when scope.LocationId is not null:
                    where.Add("e.location_id = @scopeLoc"); break;
                case ScopeKind.SelfOnly:
                    where.Add("e.username = @scopeUser"); break;
                // ScopeKind.All (Admin) → không thêm điều kiện.
            }

            var sql = $"""
                SELECT e.id, e.employee_code, e.username, e.full_name, e.position, e.hire_date, e.status,
                       e.phone, e.email, e.avatar, e.department_id, e.location_id, e.access_role,
                       COALESCE(d.name, '') AS department_name,
                       COALESCE(l.name, '') AS location_name,
                       COALESCE(m.full_name, '') AS manager_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id = e.department_id
                LEFT JOIN hr_locations l ON l.id = e.location_id
                LEFT JOIN hr_employees m ON m.id = e.manager_id
                {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
                ORDER BY e.full_name
                """;
            var cmd = conn.Cmd(sql);
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search.Trim()}%");
            if (departmentId is not null) cmd.With("@dept", departmentId.Value);
            if (scope.Kind == ScopeKind.Department && scope.DepartmentId is not null) cmd.With("@scopeDept", scope.DepartmentId.Value);
            if (scope.Kind == ScopeKind.Location && scope.LocationId is not null) cmd.With("@scopeLoc", scope.LocationId.Value);
            if (scope.Kind == ScopeKind.SelfOnly) cmd.With("@scopeUser", u.Username());
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

        g.MapGet("/me/documents", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var emp = await EnsureEmployeeForUser(conn, u.Username());
            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id,doc_type,title,issued_by,issued_date,doc_number,expires_at,approval_status,file_name,mime_type,note
                FROM hr_documents WHERE employee_id=@e ORDER BY created_at DESC
                """).With("@e", emp).ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new {
                id=r.Guid("id"), docType=r.Str("doc_type"), title=r.Str("title"), issuedBy=r.Str("issued_by"),
                issuedDate=DateOrNull(r,"issued_date"), docNumber=r.Str("doc_number"), expiresAt=DateOrNull(r,"expires_at"),
                approvalStatus=r.Str("approval_status"), fileName=r.Str("file_name"), mimeType=r.Str("mime_type"), note=r.Str("note")
            });
            return Results.Ok(list);
        });

        g.MapPost("/me/documents", async (string docType, string title, string? docNumber, string? expiresAt,
            string? issuedBy, HttpContext ctx, ClaimsPrincipal u, Database db) =>
        {
            var allowed = new[] { "cccd", "contract", "degree", "certificate", "emergency_contact" };
            if (!allowed.Contains(docType)) return Results.BadRequest(new { message = "Loại hồ sơ không hợp lệ." });
            if (string.IsNullOrWhiteSpace(title)) return Results.BadRequest(new { message = "Tên hồ sơ là bắt buộc." });
            const int max = 15 * 1024 * 1024;
            await using var ms = new MemoryStream(); var buffer = new byte[81920]; int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0) { if (ms.Length + read > max) return Results.BadRequest(new { message = "Tệp tối đa 15 MB." }); await ms.WriteAsync(buffer.AsMemory(0, read)); }
            DateOnly? expiry = DateOnly.TryParse(expiresAt, out var d) ? d : null;
            await using var conn = await db.OpenAsync();
            var emp = await EnsureEmployeeForUser(conn, u.Username()); var id = Guid.NewGuid();
            var fileName = Path.GetFileName(ctx.Request.Headers["X-File-Name"].FirstOrDefault() ?? "");
            await conn.Cmd("""
                INSERT INTO hr_documents(id,employee_id,doc_type,title,issued_by,doc_number,expires_at,approval_status,file_name,mime_type,file_content)
                VALUES(@id,@e,@t,@title,@by,@no,@exp,'pending',@fn,@mime,@content)
                """).With("@id",id).With("@e",emp).With("@t",docType).With("@title",title.Trim()).With("@by",issuedBy ?? "")
                .With("@no",docNumber ?? "").With("@exp",(object?)expiry ?? DBNull.Value).With("@fn",fileName)
                .With("@mime",ctx.Request.ContentType ?? "application/octet-stream").With("@content",ms.Length == 0 ? DBNull.Value : ms.ToArray()).ExecuteNonQueryAsync();
            await Signal(db,u,"Gửi cập nhật hồ sơ","EmployeeDocument",title);
            return Results.Ok(new { id, approvalStatus="pending" });
        });

        // Cập nhật ảnh chân dung của CHÍNH người đang đăng nhập (app tự chụp có hướng dẫn).
        // Nhận data URL JPEG (data:image/jpeg;base64,...); gửi chuỗi rỗng/null để xoá ảnh.
        g.MapPut("/me/avatar", async (SaveAvatarReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var id = await EnsureEmployeeForUser(conn, u.Username());
            var avatar = string.IsNullOrWhiteSpace(req.Avatar) ? null : req.Avatar;
            if (avatar is not null && !PayloadLimits.TryDecodeImage(avatar, out _))
                return Results.BadRequest(new { message = $"Ảnh chân dung phải nhỏ hơn {PayloadLimits.MaxImageBytes / 1024 / 1024} MB." });
            await conn.Cmd("UPDATE hr_employees SET avatar=@a, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@a", (object?)avatar ?? DBNull.Value).With("@id", id).ExecuteNonQueryAsync();
            await Signal(db, u, "Cập nhật ảnh chân dung", "Employee", u.Username());
            return Results.NoContent();
        });

        // ---------------- Thư tri ân "tròn X năm gắn bó" ----------------
        // Admin (hr.manage) soạn MỘT mẫu thư dùng chung; app tự tính mốc tròn năm theo ngày vào làm.
        g.MapGet("/anniversary/template", async (ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsHrManager()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var row = await ReadAnniversaryTemplateRow(conn);
            return Results.Ok(new { enabled = row.Enabled, title = row.Title, body = row.Body, signature = row.Signature });
        });

        g.MapPut("/anniversary/template", async (SaveAnniversaryLetterReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsHrManager()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                UPDATE hr_anniversary_letter
                SET enabled=@en, title=@title, body=@body, signature=@sig, updated_at=CURRENT_TIMESTAMP
                WHERE id=1
                """)
                .With("@en", req.Enabled)
                .With("@title", (req.Title ?? "").Trim())
                .With("@body", (req.Body ?? "").Trim())
                .With("@sig", (req.Signature ?? "").Trim())
                .ExecuteNonQueryAsync();
            await Signal(db, u, "Cập nhật thư tri ân", "AnniversaryLetter", "");
            var row = await ReadAnniversaryTemplateRow(conn);
            return Results.Ok(new { enabled = row.Enabled, title = row.Title, body = row.Body, signature = row.Signature });
        });

        // App gọi lúc mở: trả thư ĐÃ ĐIỀN sẵn nếu hôm nay rơi vào tuần kỷ niệm tròn năm của chính người
        // dùng; ngược lại { show:false }. App tự nhớ (cục bộ) đã xem để mỗi mốc chỉ hiện một lần.
        g.MapGet("/anniversary/my-greeting", async (ClaimsPrincipal u, Database db, bool? preview) =>
        {
            // Chế độ xem thử bỏ qua ngày vào làm, nhưng chỉ người có quyền quản trị nhân sự được gọi.
            if (preview == true && !u.IsHrManager()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var emp = await EnsureEmployeeForUser(conn, u.Username());
            var fullName = u.Username();
            DateOnly? hire = null;
            await using (var r = await conn.Cmd("SELECT full_name, hire_date FROM hr_employees WHERE id=@e")
                .With("@e", emp).ExecuteReaderAsync())
                if (await r.ReadAsync())
                {
                    var fn = r.Str("full_name");
                    if (!string.IsNullOrWhiteSpace(fn)) fullName = fn;
                    hire = DateOrNull(r, "hire_date");
                }
            var today = await conn.Cmd("SELECT (CURRENT_TIMESTAMP AT TIME ZONE @tz)::date")
                .With("@tz", Tz).ExecuteScalarAsync() switch
                {
                    DateOnly d => d,
                    DateTime dt => DateOnly.FromDateTime(dt),
                    _ => DateOnly.FromDateTime(DateTime.UtcNow),
            };
            var tpl = await ReadAnniversaryTemplateRow(conn);
            if (preview == true)
                return Results.Ok(BuildAnniversaryPreview(tpl, fullName, today));
            return Results.Ok(BuildAnniversaryGreeting(tpl, fullName, hire, today));
        });

        g.MapGet("/employees/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.IsHrManager() && !await IsEmployeeOwner(conn, id, u.Username())) return Results.Forbid();
            var detail = await ReadEmployeeDetail(conn, id);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        g.MapPost("/employees", async (SaveEmployeeReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.FullName)) return Results.BadRequest(new { message = "Vui lòng nhập họ tên." });
            if (req.DepartmentId is null) return Results.BadRequest(new { message = "Vui lòng chọn phòng ban cho nhân viên." });
            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            var code = string.IsNullOrWhiteSpace(req.EmployeeCode) ? await NextEmployeeCode(conn) : req.EmployeeCode!.Trim();

            // Tên đăng nhập: admin gõ tay thì tôn trọng; để trống → sinh theo họ tên + mã (vd "Nguyễn Văn An"
            // + NV0001 → annv01), tự thêm hậu tố nếu trùng để luôn duy nhất.
            async Task<bool> UsernameTaken(string uname) => Convert.ToInt64(await conn.Cmd(
                    "SELECT (SELECT COUNT(*) FROM app_users WHERE lower(username)=lower(@u) AND is_deleted=FALSE)"
                    + " + (SELECT COUNT(*) FROM hr_employees WHERE lower(username)=lower(@u))")
                .With("@u", uname).ExecuteScalarAsync()) > 0;

            string loginUsername;
            if (!string.IsNullOrWhiteSpace(req.Username))
                loginUsername = req.Username!.Trim();
            else
            {
                var baseName = DeriveLoginUsername(req.FullName, code);
                loginUsername = baseName;
                for (var k = 0; await UsernameTaken(loginUsername); k++)
                    loginUsername = baseName + (char)('a' + k);
            }

            try
            {
                await conn.Cmd("""
                    INSERT INTO hr_employees
                        (id, employee_code, username, full_name, dob, gender, phone, email, address,
                         department_id, position, manager_id, hire_date, status, location_id, access_role)
                    VALUES (@id, @code, @username, @fn, @dob, @gender, @phone, @email, @addr,
                            @dept, @pos, @mgr, @hire, @status, @loc, @arole)
                    """)
                    .With("@id", id).With("@code", code).With("@username", loginUsername)
                    .With("@fn", req.FullName.Trim()).With("@dob", (object?)req.Dob ?? DBNull.Value)
                    .With("@gender", req.Gender ?? "").With("@phone", req.Phone ?? "").With("@email", req.Email ?? "")
                    .With("@addr", req.Address ?? "").With("@dept", (object?)req.DepartmentId ?? DBNull.Value)
                    .With("@pos", req.Position ?? "").With("@mgr", (object?)req.ManagerId ?? DBNull.Value)
                    .With("@hire", (object?)req.HireDate ?? DBNull.Value).With("@status", req.Status ?? "Active")
                    .With("@loc", (object?)req.LocationId ?? DBNull.Value).With("@arole", NormalizeAccessRole(req.AccessRole))
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Json(new { message = "Mã nhân viên hoặc tài khoản đã tồn tại." }, statusCode: 400);
            }

            // Tự tạo tài khoản đăng nhập cho hồ sơ mới (mật khẩu mặc định "123"), trừ khi đã có sẵn hoặc bị tắt.
            // Vai trò hệ thống của tài khoản lấy từ "Chức vụ" chọn ở form (Admin/Kế toán/HR/…); mặc định Nhân viên.
            var accountRole = AppRoles.Normalize(req.Role) ?? AppRoles.Employee;
            var accountCreated = false;
            if (req.CreateAccount && !string.IsNullOrWhiteSpace(loginUsername))
            {
                var exists = Convert.ToInt64(await conn.Cmd(
                        "SELECT COUNT(*) FROM app_users WHERE lower(username)=lower(@u) AND is_deleted=FALSE")
                    .With("@u", loginUsername).ExecuteScalarAsync()) > 0;
                if (!exists)
                {
                    await conn.Cmd("""
                        INSERT INTO app_users
                            (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at)
                        VALUES (@id, @u, @fn, @em, @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, @by,
                                COALESCE((SELECT (e.hire_date::timestamp + TIME '09:00') AT TIME ZONE 'Asia/Ho_Chi_Minh'
                                          FROM hr_employees e WHERE lower(e.username)=lower(@u) AND e.hire_date IS NOT NULL LIMIT 1), CURRENT_TIMESTAMP))
                        """)
                        .With("@id", Guid.NewGuid()).With("@u", loginUsername).With("@fn", req.FullName.Trim())
                        .With("@em", req.Email ?? "").With("@role", accountRole)
                        .With("@ph", PasswordHasher.Hash("123")).With("@by", u.Username())
                        .ExecuteNonQueryAsync();
                    accountCreated = true;
                    await db.RecordAudit(u.Username(), "Tạo người dùng", "User", loginUsername, $"Tự tạo tài khoản khi lập hồ sơ nhân viên (vai trò {AppRoles.Label(accountRole)}, mật khẩu mặc định 123).");
                }
            }

            await Signal(db, u, "Tạo hồ sơ nhân viên", "Employee", req.FullName);
            await SyncEmployeeAccountSharedFields(conn, id);
            return Results.Ok(new { id, employeeCode = code, username = loginUsername, accountCreated, password = accountCreated ? "123" : null });
        });

        g.MapPut("/employees/{id:guid}", async (Guid id, SaveEmployeeReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            // Admin sửa mọi hồ sơ; nhân viên chỉ sửa liên hệ của chính mình.
            var mine = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id").With("@id", id).ExecuteScalarAsync() as string;
            var isSelf = string.Equals(mine, u.Username(), StringComparison.OrdinalIgnoreCase);
            if (!u.IsAdmin() && !isSelf) return Results.Forbid();

            if (u.IsAdmin() && req.DepartmentId is null)
                return Results.BadRequest(new { message = "Vui lòng chọn phòng ban cho nhân viên." });

            NpgsqlCommand cmd;
            if (u.IsAdmin())
            {
                cmd = conn.Cmd("""
                    UPDATE hr_employees SET employee_code=@code, username=@username, full_name=@fn, dob=@dob,
                        gender=@gender, phone=@phone, email=@email, address=@addr, department_id=@dept,
                        position=@pos, manager_id=@mgr, hire_date=@hire, status=@status, avatar=@avatar,
                        location_id=@loc, access_role=@arole,
                        updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id
                    """)
                    .With("@code", (req.EmployeeCode ?? "").Trim()).With("@username", (req.Username ?? "").Trim())
                    .With("@fn", (req.FullName ?? "").Trim()).With("@dob", (object?)req.Dob ?? DBNull.Value)
                    .With("@gender", req.Gender ?? "").With("@phone", req.Phone ?? "").With("@email", req.Email ?? "")
                    .With("@addr", req.Address ?? "").With("@dept", (object?)req.DepartmentId ?? DBNull.Value)
                    .With("@pos", req.Position ?? "").With("@mgr", (object?)req.ManagerId ?? DBNull.Value)
                    .With("@hire", (object?)req.HireDate ?? DBNull.Value).With("@status", req.Status ?? "Active")
                    .With("@avatar", (object?)req.Avatar ?? DBNull.Value)
                    .With("@loc", (object?)req.LocationId ?? DBNull.Value).With("@arole", NormalizeAccessRole(req.AccessRole));
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
            await SyncEmployeeAccountSharedFields(conn, id);
            await Signal(db, u, "Cập nhật hồ sơ nhân viên", "Employee", req.FullName ?? id.ToString());
            return Results.NoContent();
        });

        g.MapDelete("/employees/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_employees WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa hồ sơ nhân viên", "Employee", id.ToString());
            return Results.NoContent();
        });

        // ---------------- Hợp đồng ----------------
        g.MapGet("/employees/{id:guid}/contracts", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.IsHrManager() && !await IsEmployeeOwner(conn, id, u.Username())) return Results.Forbid();
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

        g.MapPost("/employees/{id:guid}/contracts", async (Guid id, SaveContractReq req, ClaimsPrincipal u, Database db) =>
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
            await Signal(db, u, "Thêm hợp đồng", "Contract", req.ContractNo ?? "");
            return Results.Ok(new { id = cid });
        });

        g.MapPut("/contracts/{cid:guid}", async (Guid cid, SaveContractReq req, ClaimsPrincipal u, Database db) =>
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
            await Signal(db, u, "Cập nhật hợp đồng", "Contract", req.ContractNo ?? "");
            return Results.NoContent();
        });

        g.MapDelete("/contracts/{cid:guid}", async (Guid cid, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM hr_contracts WHERE id=@id").With("@id", cid).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa hợp đồng", "Contract", cid.ToString());
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
                SELECT id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, details::text AS details, published
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
                    details = ParseJson(r.Str("details")),
                    published = r.Bool("published"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/employees/{id:guid}/payslips", async (Guid id, SavePayslipReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Period)) return Results.BadRequest(new { message = "Thiếu kỳ lương (yyyy-MM)." });
            await using var conn = await db.OpenAsync();
            var period = req.Period.Trim();
            var pid = Guid.NewGuid();

            // BACKEND làm chủ tiền phạt: req.Deductions = khấu trừ KHÁC (không gồm phạt). Phạt được CAP theo
            // lương còn lại (không làm âm lương); phần chưa thu tự chuyển sang kỳ sau (theo sổ cái).
            var available = Math.Max(0m, req.BaseSalary + req.Allowance + req.OvertimePay - req.Deductions);
            var (penaltyTotal, penaltyItems) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, id, period, available);
            var totalDeductions = req.Deductions + penaltyTotal;
            var net = req.BaseSalary + req.Allowance + req.OvertimePay - totalDeductions;

            await conn.Cmd("""
                INSERT INTO hr_payslips (id, employee_id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, published)
                VALUES (@id, @emp, @period, @wd, @ot, @base, @allow, @otp, @ded, @net, @note, @pub)
                ON CONFLICT (employee_id, period) DO UPDATE SET
                    work_days=@wd, overtime_hours=@ot, base_salary=@base, allowance=@allow,
                    overtime_pay=@otp, deductions=@ded, net_pay=@net, note=@note, published=@pub
                """)
                .With("@id", pid).With("@emp", id).With("@period", period)
                .With("@wd", req.WorkDays).With("@ot", req.OvertimeHours).With("@base", req.BaseSalary)
                .With("@allow", req.Allowance).With("@otp", req.OvertimePay).With("@ded", totalDeductions)
                .With("@net", net).With("@note", req.Note ?? "").With("@pub", req.Published)
                .ExecuteNonQueryAsync();

            // Ghi sổ tiền phạt thực trừ khi phát hành; nháp thì xóa sổ kỳ này (chưa tính là đã thu).
            if (req.Published)
                await PenaltyEndpoints.RecordDeductionsAsync(conn, id, period, penaltyItems);
            else
                await PenaltyEndpoints.ClearDeductionsForPeriod(conn, id, period);

            await Signal(db, u, "Lập phiếu lương", "Payslip", period);
            return Results.Ok(new { id = pid, penaltyTotal });
        });

        g.MapDelete("/payslips/{pid:guid}", async (Guid pid, ClaimsPrincipal u, Database db) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();

            // Trước khi xóa: gỡ ghi sổ tiền phạt của kỳ này (coi như chưa thu) để phạt còn nợ được thu lại kỳ sau.
            Guid emp = default; string period = "";
            await using (var r = await conn.Cmd("SELECT employee_id, period FROM hr_payslips WHERE id=@id")
                .With("@id", pid).ExecuteReaderAsync())
                if (await r.ReadAsync()) { emp = r.Guid("employee_id"); period = r.Str("period"); }
            if (emp == default) return Results.NotFound();
            await PenaltyEndpoints.ClearDeductionsForPeriod(conn, emp, period);

            var n = await conn.Cmd("DELETE FROM hr_payslips WHERE id=@id").With("@id", pid).ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();
            await Signal(db, u, "Xóa phiếu lương", "Payslip", pid.ToString());
            return Results.NoContent();
        });

        // ---------------- Số ngày phép ----------------
        g.MapGet("/employees/{id:guid}/leave-balances", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.IsHrManager() && !await IsEmployeeOwner(conn, id, u.Username())) return Results.Forbid();
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

        g.MapPost("/employees/{id:guid}/leave-balances", async (Guid id, SaveLeaveBalanceReq req, ClaimsPrincipal u, Database db) =>
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
            await Signal(db, u, "Cập nhật số phép", "LeaveBalance", $"{req.Year}/{req.LeaveType}");
            return Results.Ok(new { id = bid });
        });

        // ---------------- Bằng cấp / chứng chỉ / khen thưởng ----------------
        g.MapGet("/employees/{id:guid}/documents", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!u.IsHrManager() && !await IsEmployeeOwner(conn, id, u.Username())) return Results.Forbid();
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

        g.MapPost("/employees/{id:guid}/documents", async (Guid id, SaveDocumentReq req, ClaimsPrincipal u, Database db) =>
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
            await Signal(db, u, "Thêm hồ sơ bằng cấp", "EmployeeDocument", req.Title ?? "");
            return Results.Ok(new { id = did });
        });

        g.MapDelete("/documents/{did:guid}", async (Guid did, ClaimsPrincipal u, Database db) =>
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
            await Signal(db, u, "Xóa hồ sơ bằng cấp", "EmployeeDocument", did.ToString());
            return Results.NoContent();
        });

        // ---------------- Trung tâm quản lý trên KetoanAPK ----------------
        g.MapGet("/manager/summary", async (ClaimsPrincipal u, Database db, string? date, string? month) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            var day = ManagerDate(date);
            var period = ManagerPeriod(month, day);
            await using var conn = await db.OpenAsync();
            return Results.Ok(new
            {
                date = day,
                month = period,
                headcount = await ReadManagerHeadcount(conn, u, day),
                departments = await ReadManagerDepartments(conn, day),
            });
        });

        g.MapGet("/manager/attendance", async (ClaimsPrincipal u, Database db, string? date, string? status, Guid? departmentId) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            return Results.Ok(await ReadManagerAttendance(conn, ManagerDate(date), status, departmentId));
        });

        g.MapGet("/manager/contracts/expiring", async (ClaimsPrincipal u, Database db, int? days) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            return Results.Ok(await ReadExpiringContracts(conn, Math.Clamp(days ?? 60, 1, 365)));
        });

        g.MapGet("/manager/reports", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            return Results.Ok(await ReadManagerReport(conn, month));
        });

        g.MapGet("/manager/alerts", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            if (!u.IsAdmin()) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            return Results.Ok(await ReadManagerAlerts(conn, month));
        });
    }

    // ---- Đọc dữ liệu ----

    private static DateOnly ManagerDate(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && DateOnly.TryParse(value, out var parsed)) return parsed;
        return DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    }

    private static string ManagerPeriod(string? value, DateOnly? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Length >= 7
            && int.TryParse(value[..4], out var y) && int.TryParse(value.Substring(5, 2), out var m)
            && m is >= 1 and <= 12)
            return $"{y:D4}-{m:D2}";
        var d = fallback ?? ManagerDate(null);
        return $"{d.Year:D4}-{d.Month:D2}";
    }

    private static (DateOnly From, DateOnly To, string Period) ManagerMonthRange(string? month)
    {
        var period = ManagerPeriod(month);
        var from = new DateOnly(int.Parse(period[..4]), int.Parse(period.Substring(5, 2)), 1);
        return (from, from.AddMonths(1).AddDays(-1), period);
    }

    private static string ManagerStatusLabel(string status) => status switch
    {
        "present" => "Có mặt",
        "late" => "Đi muộn",
        "leave" => "Nghỉ phép",
        "business" => "Công tác",
        "absent" => "Vắng",
        "unassigned" => "Chưa phân ca",
        _ => status,
    };

    private static async Task<object> ReadManagerHeadcount(NpgsqlConnection conn, ClaimsPrincipal u, DateOnly day)
    {
        var total = 0;
        var present = 0;
        var onLeave = 0;
        var business = 0;
        var absent = 0;
        var late = 0;
        var overtime = 0;
        var unassigned = 0;

        await using (var r = await conn.Cmd("""
            WITH active AS (
                SELECT id, username FROM hr_employees WHERE status='Active'
            ),
            logs AS (
                SELECT e.id AS employee_id,
                       MIN(l.occurred_at AT TIME ZONE @tz) AS first_in,
                       MAX(l.occurred_at AT TIME ZONE @tz) AS last_out,
                       COUNT(*) AS cnt
                FROM cham_cong_log l
                JOIN active e ON e.username=l.username
                WHERE (l.occurred_at AT TIME ZONE @tz)::date=@day
                GROUP BY e.id
            ),
            shifts AS (
                SELECT a.employee_id, s.start_time, s.break_minutes, s.late_grace_minutes, s.standard_hours
                FROM hr_shift_assignments a
                JOIN hr_shifts s ON s.id=a.shift_id
                WHERE a.work_date=@day
            ),
            leave_req AS (
                SELECT DISTINCT r.employee_id
                FROM hr_requests r
                WHERE r.status='Approved' AND r.req_type IN ('leave','sick')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @day
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @day
            ),
            business_req AS (
                SELECT DISTINCT r.employee_id
                FROM hr_requests r
                WHERE r.status='Approved' AND r.req_type IN ('business_trip','work_trip','booking')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @day
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @day
            ),
            rows AS (
                SELECT e.id,
                       l.first_in, l.last_out, COALESCE(l.cnt, 0) AS cnt,
                       s.employee_id IS NOT NULL AS has_shift,
                       s.start_time, s.break_minutes, s.late_grace_minutes, s.standard_hours,
                       lr.employee_id IS NOT NULL AS on_leave,
                       br.employee_id IS NOT NULL AS on_business
                FROM active e
                LEFT JOIN logs l ON l.employee_id=e.id
                LEFT JOIN shifts s ON s.employee_id=e.id
                LEFT JOIN leave_req lr ON lr.employee_id=e.id
                LEFT JOIN business_req br ON br.employee_id=e.id
            )
            SELECT COUNT(*) AS total,
                   COUNT(*) FILTER (WHERE first_in IS NOT NULL) AS present,
                   COUNT(*) FILTER (WHERE first_in IS NULL AND on_leave) AS leave_count,
                   COUNT(*) FILTER (WHERE first_in IS NULL AND NOT on_leave AND on_business) AS business_count,
                   COUNT(*) FILTER (WHERE first_in IS NULL AND NOT on_leave AND NOT on_business AND has_shift) AS absent,
                   COUNT(*) FILTER (WHERE first_in IS NULL AND NOT on_leave AND NOT on_business AND NOT has_shift) AS unassigned,
                   COUNT(*) FILTER (
                     WHERE first_in IS NOT NULL AND start_time IS NOT NULL
                       AND first_in::time > start_time + (late_grace_minutes * INTERVAL '1 minute')
                   ) AS late,
                   COUNT(*) FILTER (
                     WHERE first_in IS NOT NULL AND cnt > 1 AND standard_hours IS NOT NULL
                       AND GREATEST(0, EXTRACT(EPOCH FROM (last_out - first_in)) / 60 - break_minutes - (standard_hours * 60)) > 0
                   ) AS overtime
            FROM rows
            """)
            .With("@tz", Tz).With("@day", day).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                total = r.Int("total");
                present = r.Int("present");
                onLeave = r.Int("leave_count");
                business = r.Int("business_count");
                absent = r.Int("absent");
                unassigned = r.Int("unassigned");
                late = r.Int("late");
                overtime = r.Int("overtime");
            }
        }

        var pending = Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(*) FROM hr_requests r
            WHERE r.status='Pending' AND EXISTS (
                SELECT 1 FROM hr_request_approvals a
                WHERE a.request_id=r.id AND a.step_no=r.current_step AND a.status='Pending'
                  AND (a.approver_username=@me OR (a.approver_role='Admin' AND @admin))
            )
            """).With("@me", u.Username()).With("@admin", u.IsAdmin()).ExecuteScalarAsync());

        var expiring = Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(DISTINCT c.employee_id)
            FROM hr_contracts c
            JOIN hr_employees e ON e.id=c.employee_id
            WHERE e.status='Active' AND c.status='Active'
              AND c.end_date IS NOT NULL AND c.end_date <= @until
            """).With("@until", day.AddDays(60)).ExecuteScalarAsync());

        var alerts = (await ReadManagerAlerts(conn, ManagerPeriod(null, day))).Count;

        return new
        {
            total,
            active = total,
            present,
            leave = onLeave,
            business,
            absent,
            late,
            overtime,
            unassigned,
            pendingApprovals = pending,
            expiringContracts = expiring,
            alerts,
        };
    }

    private static async Task<List<object>> ReadManagerDepartments(NpgsqlConnection conn, DateOnly day)
    {
        var list = new List<object>();
        await using var r = await conn.Cmd("""
            WITH active AS (
                SELECT e.id, e.username, e.department_id, COALESCE(d.name, 'Chưa có phòng ban') AS department_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id=e.department_id
                WHERE e.status='Active'
            ),
            logs AS (
                SELECT e.id AS employee_id,
                       MIN(l.occurred_at AT TIME ZONE @tz) AS first_in
                FROM cham_cong_log l
                JOIN active e ON e.username=l.username
                WHERE (l.occurred_at AT TIME ZONE @tz)::date=@day
                GROUP BY e.id
            ),
            shifts AS (
                SELECT employee_id FROM hr_shift_assignments WHERE work_date=@day
            ),
            leave_req AS (
                SELECT DISTINCT employee_id FROM hr_requests r
                WHERE r.status='Approved' AND r.req_type IN ('leave','sick')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @day
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @day
            ),
            business_req AS (
                SELECT DISTINCT employee_id FROM hr_requests r
                WHERE r.status='Approved' AND r.req_type IN ('business_trip','work_trip','booking')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @day
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @day
            )
            SELECT a.department_id, a.department_name,
                   COUNT(*) AS total,
                   COUNT(*) FILTER (WHERE l.first_in IS NOT NULL) AS present,
                   COUNT(*) FILTER (WHERE l.first_in IS NULL AND lr.employee_id IS NOT NULL) AS leave_count,
                   COUNT(*) FILTER (WHERE l.first_in IS NULL AND lr.employee_id IS NULL AND br.employee_id IS NOT NULL) AS business_count,
                   COUNT(*) FILTER (WHERE l.first_in IS NULL AND lr.employee_id IS NULL AND br.employee_id IS NULL AND s.employee_id IS NOT NULL) AS absent
            FROM active a
            LEFT JOIN logs l ON l.employee_id=a.id
            LEFT JOIN shifts s ON s.employee_id=a.id
            LEFT JOIN leave_req lr ON lr.employee_id=a.id
            LEFT JOIN business_req br ON br.employee_id=a.id
            GROUP BY a.department_id, a.department_name
            ORDER BY a.department_name
            """).With("@tz", Tz).With("@day", day).ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new
            {
                departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? (Guid?)null : r.Guid("department_id"),
                departmentName = r.Str("department_name"),
                total = r.Int("total"),
                present = r.Int("present"),
                leave = r.Int("leave_count"),
                business = r.Int("business_count"),
                absent = r.Int("absent"),
            });
        }
        return list;
    }

    private static async Task<List<object>> ReadManagerAttendance(NpgsqlConnection conn, DateOnly day, string? status, Guid? departmentId)
    {
        var list = new List<object>();
        var sql = $"""
            WITH active AS (
                SELECT e.id, e.employee_code, e.username, e.full_name, e.position, e.avatar, e.department_id,
                       COALESCE(d.name, 'Chưa có phòng ban') AS department_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id=e.department_id
                WHERE e.status='Active' {(departmentId is not null ? "AND e.department_id=@dept" : "")}
            ),
            logs AS (
                SELECT e.id AS employee_id,
                       MIN(l.occurred_at AT TIME ZONE @tz) AS first_in,
                       MAX(l.occurred_at AT TIME ZONE @tz) AS last_out,
                       COUNT(*) AS cnt
                FROM cham_cong_log l
                JOIN active e ON e.username=l.username
                WHERE (l.occurred_at AT TIME ZONE @tz)::date=@day
                GROUP BY e.id
            ),
            shifts AS (
                SELECT a.employee_id, s.name AS shift_name, s.start_time, s.end_time, s.break_minutes, s.late_grace_minutes, s.standard_hours
                FROM hr_shift_assignments a
                JOIN hr_shifts s ON s.id=a.shift_id
                WHERE a.work_date=@day
            ),
            leave_req AS (
                SELECT DISTINCT ON (r.employee_id) r.employee_id, r.request_no, r.title
                FROM hr_requests r
                WHERE r.status='Approved' AND r.req_type IN ('leave','sick')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @day
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @day
                ORDER BY r.employee_id, r.created_at DESC
            ),
            business_req AS (
                SELECT DISTINCT ON (r.employee_id) r.employee_id, r.request_no, r.title
                FROM hr_requests r
                WHERE r.status='Approved' AND r.req_type IN ('business_trip','work_trip','booking')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @day
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @day
                ORDER BY r.employee_id, r.created_at DESC
            )
            SELECT a.id, a.employee_code, a.full_name, a.position, a.avatar, a.department_id, a.department_name,
                   s.shift_name,
                   TO_CHAR(l.first_in, 'HH24:MI') AS check_in,
                   CASE WHEN l.cnt > 1 AND l.last_out > l.first_in THEN TO_CHAR(l.last_out, 'HH24:MI') ELSE '' END AS check_out,
                   COALESCE(
                     CASE WHEN l.first_in IS NOT NULL AND s.start_time IS NOT NULL
                       AND l.first_in::time > s.start_time + (s.late_grace_minutes * INTERVAL '1 minute')
                       THEN FLOOR(EXTRACT(EPOCH FROM (l.first_in::time - s.start_time)) / 60)::int
                     END, 0
                   ) AS late_minutes,
                   COALESCE(
                     CASE WHEN l.first_in IS NOT NULL AND l.cnt > 1 AND s.standard_hours IS NOT NULL
                       THEN GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (l.last_out - l.first_in)) / 60 - s.break_minutes - (s.standard_hours * 60)))::int
                     END, 0
                   ) AS overtime_minutes,
                   lr.request_no AS leave_request_no,
                   lr.title AS leave_title,
                   br.request_no AS business_request_no,
                   br.title AS business_title,
                   CASE
                     WHEN l.first_in IS NOT NULL AND s.start_time IS NOT NULL
                       AND l.first_in::time > s.start_time + (s.late_grace_minutes * INTERVAL '1 minute') THEN 'late'
                     WHEN l.first_in IS NOT NULL THEN 'present'
                     WHEN lr.employee_id IS NOT NULL THEN 'leave'
                     WHEN br.employee_id IS NOT NULL THEN 'business'
                     WHEN s.employee_id IS NOT NULL THEN 'absent'
                     ELSE 'unassigned'
                   END AS current_status
            FROM active a
            LEFT JOIN logs l ON l.employee_id=a.id
            LEFT JOIN shifts s ON s.employee_id=a.id
            LEFT JOIN leave_req lr ON lr.employee_id=a.id
            LEFT JOIN business_req br ON br.employee_id=a.id
            ORDER BY current_status, a.department_name, a.full_name
            """;
        var cmd = conn.Cmd(sql).With("@tz", Tz).With("@day", day);
        if (departmentId is not null) cmd.With("@dept", departmentId.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var current = r.Str("current_status");
            if (!string.IsNullOrWhiteSpace(status) && status != "all" && current != status) continue;
            list.Add(new
            {
                employeeId = r.Guid("id"),
                employeeCode = r.Str("employee_code"),
                employeeName = r.Str("full_name"),
                position = r.Str("position"),
                avatar = r.IsDBNull(r.GetOrdinal("avatar")) ? null : r.Str("avatar"),
                departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? (Guid?)null : r.Guid("department_id"),
                departmentName = r.Str("department_name"),
                status = current,
                statusLabel = ManagerStatusLabel(current),
                shiftName = r.Str("shift_name"),
                checkIn = r.Str("check_in"),
                checkOut = r.Str("check_out"),
                lateMinutes = r.Int("late_minutes"),
                overtimeMinutes = r.Int("overtime_minutes"),
                requestNo = current == "business" ? r.Str("business_request_no") : r.Str("leave_request_no"),
                requestTitle = current == "business" ? r.Str("business_title") : r.Str("leave_title"),
            });
        }
        return list;
    }

    private static async Task<List<object>> ReadExpiringContracts(NpgsqlConnection conn, int days)
    {
        var list = new List<object>();
        var today = ManagerDate(null);
        await using var r = await conn.Cmd("""
            SELECT c.id, c.contract_no, c.contract_type, c.start_date, c.end_date, c.status,
                   e.id AS employee_id, e.employee_code, e.full_name, COALESCE(d.name, '') AS department_name,
                   (c.end_date - @today) AS days_left
            FROM hr_contracts c
            JOIN hr_employees e ON e.id=c.employee_id
            LEFT JOIN hr_departments d ON d.id=e.department_id
            WHERE e.status='Active' AND c.status='Active' AND c.end_date IS NOT NULL
              AND c.end_date <= @until
            ORDER BY c.end_date, e.full_name
            LIMIT 200
            """).With("@today", today).With("@until", today.AddDays(days)).ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var daysLeft = r.Int("days_left");
            list.Add(new
            {
                id = r.Guid("id"),
                employeeId = r.Guid("employee_id"),
                employeeCode = r.Str("employee_code"),
                employeeName = r.Str("full_name"),
                departmentName = r.Str("department_name"),
                contractNo = r.Str("contract_no"),
                contractType = r.Str("contract_type"),
                startDate = DateOrNull(r, "start_date"),
                endDate = DateOrNull(r, "end_date"),
                status = r.Str("status"),
                daysLeft,
                statusLabel = daysLeft < 0 ? "Đã hết hạn" : daysLeft == 0 ? "Hết hạn hôm nay" : $"Còn {daysLeft} ngày",
            });
        }
        return list;
    }

    private static async Task<object> ReadManagerReport(NpgsqlConnection conn, string? month)
    {
        var (from, to, period) = ManagerMonthRange(month);
        var departments = new List<object>();
        decimal totalWorked = 0, totalAbsent = 0, totalLate = 0, totalLeave = 0, totalOvertimeMinutes = 0;

        await using (var r = await conn.Cmd("""
            WITH active AS (
                SELECT e.id, e.username, COALESCE(d.name, 'Chưa có phòng ban') AS department_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id=e.department_id
                WHERE e.status='Active' OR e.hire_date BETWEEN @from AND @to
            ),
            logs AS (
                SELECT e.id AS employee_id, (l.occurred_at AT TIME ZONE @tz)::date AS d,
                       MIN(l.occurred_at AT TIME ZONE @tz) AS first_in,
                       MAX(l.occurred_at AT TIME ZONE @tz) AS last_out,
                       COUNT(*) AS cnt
                FROM cham_cong_log l
                JOIN active e ON e.username=l.username
                WHERE (l.occurred_at AT TIME ZONE @tz)::date BETWEEN @from AND @to
                GROUP BY e.id, (l.occurred_at AT TIME ZONE @tz)::date
            ),
            assignments AS (
                SELECT a.employee_id, a.work_date, s.start_time, s.break_minutes, s.late_grace_minutes, s.standard_hours
                FROM hr_shift_assignments a
                JOIN hr_shifts s ON s.id=a.shift_id
                WHERE a.work_date BETWEEN @from AND @to
            ),
            day_rows AS (
                SELECT COALESCE(a.employee_id, l.employee_id) AS employee_id,
                       a.employee_id IS NOT NULL AS has_shift,
                       l.first_in, l.last_out, COALESCE(l.cnt, 0) AS cnt,
                       a.start_time, a.break_minutes, a.late_grace_minutes, a.standard_hours
                FROM assignments a
                FULL JOIN logs l ON l.employee_id=a.employee_id AND l.d=a.work_date
            ),
            attendance_agg AS (
                SELECT e.department_name,
                       COUNT(*) FILTER (WHERE dr.first_in IS NOT NULL) AS worked_days,
                       COUNT(*) FILTER (WHERE dr.has_shift AND dr.first_in IS NULL) AS absent_days,
                       COUNT(*) FILTER (
                         WHERE dr.first_in IS NOT NULL AND dr.start_time IS NOT NULL
                           AND dr.first_in::time > dr.start_time + (dr.late_grace_minutes * INTERVAL '1 minute')
                       ) AS late_days,
                       COALESCE(SUM(
                         CASE WHEN dr.first_in IS NOT NULL AND dr.cnt > 1 AND dr.standard_hours IS NOT NULL
                           THEN GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (dr.last_out - dr.first_in)) / 60 - dr.break_minutes - (dr.standard_hours * 60)))
                           ELSE 0
                         END
                       ), 0) AS overtime_minutes
                FROM day_rows dr
                JOIN active e ON e.id=dr.employee_id
                GROUP BY e.department_name
            ),
            leave_agg AS (
                SELECT COALESCE(d.name, 'Chưa có phòng ban') AS department_name,
                       COALESCE(SUM(
                         CASE WHEN r.payload->>'days' ~ '^[0-9]+(\.[0-9]+)?$' THEN (r.payload->>'days')::numeric
                         ELSE GREATEST(1,
                           COALESCE(
                             CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                             CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                             (r.created_at AT TIME ZONE @tz)::date
                           )
                           -
                           COALESCE(
                             CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                             CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                             (r.created_at AT TIME ZONE @tz)::date
                           ) + 1
                         )::numeric END
                       ), 0) AS leave_days
                FROM hr_requests r
                JOIN hr_employees e ON e.id=r.employee_id
                LEFT JOIN hr_departments d ON d.id=e.department_id
                WHERE r.status='Approved' AND r.req_type IN ('leave','sick')
                  AND COALESCE(
                    CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) <= @to
                  AND COALESCE(
                    CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                    CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                    (r.created_at AT TIME ZONE @tz)::date
                  ) >= @from
                GROUP BY COALESCE(d.name, 'Chưa có phòng ban')
            )
            SELECT COALESCE(a.department_name, l.department_name) AS department_name,
                   COALESCE(a.worked_days, 0) AS worked_days,
                   COALESCE(a.absent_days, 0) AS absent_days,
                   COALESCE(a.late_days, 0) AS late_days,
                   COALESCE(a.overtime_minutes, 0) AS overtime_minutes,
                   COALESCE(l.leave_days, 0) AS leave_days
            FROM attendance_agg a
            FULL JOIN leave_agg l ON l.department_name=a.department_name
            ORDER BY department_name
            """).With("@tz", Tz).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var worked = r.Dec("worked_days");
                var absent = r.Dec("absent_days");
                var late = r.Dec("late_days");
                var ot = r.Dec("overtime_minutes");
                var leave = r.Dec("leave_days");
                totalWorked += worked;
                totalAbsent += absent;
                totalLate += late;
                totalOvertimeMinutes += ot;
                totalLeave += leave;
                departments.Add(new
                {
                    departmentName = r.Str("department_name"),
                    workedDays = worked,
                    absentDays = absent,
                    lateDays = late,
                    leaveDays = leave,
                    overtimeHours = Math.Round(ot / 60m, 2),
                });
            }
        }

        var pendingRequests = 0;
        var approvedRequests = 0;
        var rejectedRequests = 0;
        await using (var r = await conn.Cmd("""
            SELECT COUNT(*) FILTER (WHERE status='Pending') AS pending,
                   COUNT(*) FILTER (WHERE status='Approved') AS approved,
                   COUNT(*) FILTER (WHERE status='Rejected') AS rejected
            FROM hr_requests
            WHERE (created_at AT TIME ZONE @tz)::date BETWEEN @from AND @to
            """).With("@tz", Tz).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                pendingRequests = r.Int("pending");
                approvedRequests = r.Int("approved");
                rejectedRequests = r.Int("rejected");
            }
        }

        var newHires = 0;
        var inactiveThisMonth = 0;
        var activeEnd = 0;
        await using (var r = await conn.Cmd("""
            SELECT COUNT(*) FILTER (WHERE hire_date BETWEEN @from AND @to) AS new_hires,
                   COUNT(*) FILTER (WHERE status <> 'Active' AND updated_at::date BETWEEN @from AND @to) AS inactive_this_month,
                   COUNT(*) FILTER (WHERE status='Active' AND (hire_date IS NULL OR hire_date <= @to)) AS active_end
            FROM hr_employees
            """).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                newHires = r.Int("new_hires");
                inactiveThisMonth = r.Int("inactive_this_month");
                activeEnd = r.Int("active_end");
            }
        }

        return new
        {
            period,
            totals = new
            {
                workedDays = totalWorked,
                absentDays = totalAbsent,
                leaveDays = totalLeave,
                lateDays = totalLate,
                overtimeHours = Math.Round(totalOvertimeMinutes / 60m, 2),
                pendingRequests,
                approvedRequests,
                rejectedRequests,
            },
            headcountChange = new
            {
                newHires,
                inactiveThisMonth,
                activeEnd,
            },
            byDepartment = departments,
        };
    }

    private static async Task<List<ManagerAlert>> ReadManagerAlerts(NpgsqlConnection conn, string? month)
    {
        var (from, to, period) = ManagerMonthRange(month);
        var alerts = new List<ManagerAlert>();

        await using (var r = await conn.Cmd("""
            WITH active AS (
                SELECT e.id, e.employee_code, e.username, e.full_name, COALESCE(d.name, '') AS department_name
                FROM hr_employees e
                LEFT JOIN hr_departments d ON d.id=e.department_id
                WHERE e.status='Active'
            ),
            logs AS (
                SELECT e.id AS employee_id, (l.occurred_at AT TIME ZONE @tz)::date AS d,
                       MIN(l.occurred_at AT TIME ZONE @tz) AS first_in
                FROM cham_cong_log l
                JOIN active e ON e.username=l.username
                WHERE (l.occurred_at AT TIME ZONE @tz)::date BETWEEN @from AND @to
                GROUP BY e.id, (l.occurred_at AT TIME ZONE @tz)::date
            ),
            assignments AS (
                SELECT a.employee_id, a.work_date, s.start_time, s.late_grace_minutes
                FROM hr_shift_assignments a
                JOIN hr_shifts s ON s.id=a.shift_id
                WHERE a.work_date BETWEEN @from AND @to
            ),
            day_rows AS (
                SELECT a.employee_id, a.work_date, l.first_in, a.start_time, a.late_grace_minutes
                FROM assignments a
                LEFT JOIN logs l ON l.employee_id=a.employee_id AND l.d=a.work_date
            )
            SELECT e.id, e.employee_code, e.full_name, e.department_name,
                   COUNT(*) FILTER (
                     WHERE dr.first_in IS NOT NULL
                       AND dr.first_in::time > dr.start_time + (dr.late_grace_minutes * INTERVAL '1 minute')
                   ) AS late_days,
                   COUNT(*) FILTER (WHERE dr.first_in IS NULL) AS absent_days
            FROM active e
            JOIN day_rows dr ON dr.employee_id=e.id
            GROUP BY e.id, e.employee_code, e.full_name, e.department_name
            HAVING COUNT(*) FILTER (
                     WHERE dr.first_in IS NOT NULL
                       AND dr.first_in::time > dr.start_time + (dr.late_grace_minutes * INTERVAL '1 minute')
                   ) >= 3
                OR COUNT(*) FILTER (WHERE dr.first_in IS NULL) >= 2
            """).With("@tz", Tz).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var lateDays = r.Int("late_days");
                var absentDays = r.Int("absent_days");
                if (lateDays >= 3)
                    alerts.Add(new ManagerAlert(
                        $"late-{r.Guid("id")}-{period}", "late", lateDays >= 5 ? "danger" : "warning",
                        "Đi muộn thường xuyên",
                        $"{lateDays} ngày đi muộn trong kỳ {period}.",
                        r.Guid("id"), r.Str("full_name"), r.Str("employee_code"), r.Str("department_name"),
                        lateDays, $"{lateDays} ngày"));
                if (absentDays >= 2)
                    alerts.Add(new ManagerAlert(
                        $"absent-{r.Guid("id")}-{period}", "absent", absentDays >= 4 ? "danger" : "warning",
                        "Vắng nhiều ngày",
                        $"{absentDays} ngày được phân ca nhưng chưa có log chấm công.",
                        r.Guid("id"), r.Str("full_name"), r.Str("employee_code"), r.Str("department_name"),
                        absentDays, $"{absentDays} ngày"));
            }
        }

        await using (var r = await conn.Cmd("""
            SELECT e.id, e.employee_code, e.full_name, COALESCE(d.name, '') AS department_name,
                   COALESCE(SUM(
                     CASE WHEN r.payload->>'days' ~ '^[0-9]+(\.[0-9]+)?$' THEN (r.payload->>'days')::numeric
                     ELSE GREATEST(1,
                       COALESCE(
                         CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                         CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                         (r.created_at AT TIME ZONE @tz)::date
                       )
                       -
                       COALESCE(
                         CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                         CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                         (r.created_at AT TIME ZONE @tz)::date
                       ) + 1
                     )::numeric END
                   ), 0) AS leave_days
            FROM hr_requests r
            JOIN hr_employees e ON e.id=r.employee_id
            LEFT JOIN hr_departments d ON d.id=e.department_id
            WHERE e.status='Active' AND r.status='Approved' AND r.req_type IN ('leave','sick')
              AND COALESCE(
                CASE WHEN r.payload->>'fromDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'fromDate')::date END,
                CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                (r.created_at AT TIME ZONE @tz)::date
              ) <= @to
              AND COALESCE(
                CASE WHEN r.payload->>'toDate' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'toDate')::date END,
                CASE WHEN r.payload->>'date' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN (r.payload->>'date')::date END,
                (r.created_at AT TIME ZONE @tz)::date
              ) >= @from
            GROUP BY e.id, e.employee_code, e.full_name, COALESCE(d.name, '')
            HAVING COALESCE(SUM(
                     CASE WHEN r.payload->>'days' ~ '^[0-9]+(\.[0-9]+)?$' THEN (r.payload->>'days')::numeric
                     ELSE 1::numeric END
                   ), 0) >= 3
            """).With("@tz", Tz).With("@from", from).With("@to", to).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var leaveDays = r.Dec("leave_days");
                alerts.Add(new ManagerAlert(
                    $"leave-{r.Guid("id")}-{period}", "leave", leaveDays >= 5 ? "danger" : "warning",
                    "Nghỉ nhiều trong tháng",
                    $"{leaveDays:0.#} ngày nghỉ đã được duyệt trong kỳ {period}.",
                    r.Guid("id"), r.Str("full_name"), r.Str("employee_code"), r.Str("department_name"),
                    leaveDays, $"{leaveDays:0.#} ngày"));
            }
        }

        var today = ManagerDate(null);
        await using (var r = await conn.Cmd("""
            SELECT e.id, e.employee_code, e.full_name, COALESCE(d.name, '') AS department_name,
                   c.contract_no, c.end_date, (c.end_date - @today) AS days_left
            FROM hr_contracts c
            JOIN hr_employees e ON e.id=c.employee_id
            LEFT JOIN hr_departments d ON d.id=e.department_id
            WHERE e.status='Active' AND c.status='Active' AND c.end_date IS NOT NULL
              AND c.end_date <= @until
            ORDER BY c.end_date
            """).With("@today", today).With("@until", today.AddDays(30)).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var daysLeft = r.Int("days_left");
                alerts.Add(new ManagerAlert(
                    $"contract-{r.Guid("id")}-{r.Str("contract_no")}", "contract", daysLeft <= 7 ? "danger" : "warning",
                    daysLeft < 0 ? "Hợp đồng đã hết hạn" : "Hợp đồng sắp hết hạn",
                    daysLeft < 0
                        ? $"Hợp đồng {r.Str("contract_no")} đã hết hạn {Math.Abs(daysLeft)} ngày."
                        : $"Hợp đồng {r.Str("contract_no")} còn {daysLeft} ngày.",
                    r.Guid("id"), r.Str("full_name"), r.Str("employee_code"), r.Str("department_name"),
                    daysLeft, daysLeft < 0 ? $"Quá {Math.Abs(daysLeft)} ngày" : $"{daysLeft} ngày"));
            }
        }

        return alerts
            .OrderBy(a => a.Level == "danger" ? 0 : 1)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.EmployeeName)
            .ToList();
    }

    private sealed record ManagerAlert(
        string Id,
        string Type,
        string Level,
        string Title,
        string Message,
        Guid EmployeeId,
        string EmployeeName,
        string EmployeeCode,
        string DepartmentName,
        decimal Metric,
        string MetricLabel);

    private static object ReadEmployeeCard(NpgsqlDataReader r) => new
    {
        id = r.Guid("id"),
        employeeCode = r.Str("employee_code"),
        username = r.Str("username"),
        fullName = r.Str("full_name"),
        position = r.Str("position"),
        hireDate = DateOrNull(r, "hire_date"),
        status = r.Str("status"),
        phone = r.Str("phone"),
        email = r.Str("email"),
        avatar = r.IsDBNull(r.GetOrdinal("avatar")) ? null : r.Str("avatar"),
        departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? (Guid?)null : r.Guid("department_id"),
        departmentName = r.Str("department_name"),
        locationId = r.IsDBNull(r.GetOrdinal("location_id")) ? (Guid?)null : r.Guid("location_id"),
        locationName = r.Str("location_name"),
        accessRole = r.Str("access_role"),
        managerName = r.Str("manager_name"),
    };

    /// <summary>
    /// Đồng bộ các trường dùng chung giữa hr_employees và app_users. Ngày vào làm là ngày tạo tài
    /// khoản; nếu hồ sơ đã có ngày vào làm thì giữ ngày nghiệp vụ đó và đưa ngày tạo tài khoản về cùng
    /// ngày. Nhân viên đã nghỉ bị khóa tài khoản và thu hồi các phiên đang hoạt động.
    /// </summary>
    internal static async Task SyncEmployeeAccountSharedFields(
        NpgsqlConnection conn, Guid employeeId, CancellationToken ct = default)
    {
        await conn.Cmd("""
            UPDATE hr_employees e
            SET user_id = u.id,
                full_name = CASE WHEN btrim(e.full_name) = '' THEN u.full_name ELSE e.full_name END,
                email = CASE WHEN btrim(e.email) = '' THEN u.email ELSE e.email END,
                hire_date = COALESCE(e.hire_date, (u.created_at AT TIME ZONE @tz)::date),
                updated_at = CURRENT_TIMESTAMP
            FROM app_users u
            WHERE e.id=@id AND u.is_deleted=FALSE AND lower(u.username)=lower(e.username)
            """).With("@id", employeeId).With("@tz", Tz).ExecuteNonQueryAsync(ct);

        await conn.Cmd("""
            UPDATE app_users u
            SET full_name = e.full_name,
                email = e.email,
                created_at = CASE
                    WHEN e.hire_date IS NULL THEN u.created_at
                    ELSE ((e.hire_date::timestamp + (u.created_at AT TIME ZONE @tz)::time) AT TIME ZONE @tz)
                END,
                is_active = CASE
                    WHEN lower(e.status) <> 'active'
                         AND (lower(u.role) <> 'admin' OR EXISTS (
                             SELECT 1 FROM app_users other
                             WHERE other.role='Admin' AND other.is_active=TRUE AND other.is_deleted=FALSE AND other.id<>u.id
                         )) THEN FALSE
                    ELSE u.is_active
                END
            FROM hr_employees e
            WHERE e.id=@id AND u.is_deleted=FALSE
              AND (u.id=e.user_id OR lower(u.username)=lower(e.username))
            """).With("@id", employeeId).With("@tz", Tz).ExecuteNonQueryAsync(ct);

        await conn.Cmd("""
            UPDATE user_sessions s
            SET revoked=TRUE, revoked_at=CURRENT_TIMESTAMP, revoked_by='employment-status',
                is_active=FALSE, ended_at=CURRENT_TIMESTAMP, end_reason='Nhân viên đã nghỉ việc'
            FROM hr_employees e
            JOIN app_users u ON u.is_deleted=FALSE AND (u.id=e.user_id OR lower(u.username)=lower(e.username))
            WHERE e.id=@id AND lower(e.status)<>'active' AND u.is_active=FALSE
              AND s.username=u.username AND (s.is_active=TRUE OR s.revoked=FALSE)
            """).With("@id", employeeId).ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ReadEmployeeDetail(NpgsqlConnection conn, Guid id)
    {
        await using var r = await conn.Cmd("""
            SELECT e.id, e.employee_code, e.username, e.full_name, e.dob, e.gender, e.phone, e.email, e.address,
                   e.department_id, e.position, e.manager_id, e.hire_date, e.status, e.avatar,
                   e.location_id, e.access_role,
                   COALESCE(d.name, '') AS department_name,
                   COALESCE(d.is_accounting, false) AS is_accounting,
                   COALESCE(l.name, '') AS location_name,
                   COALESCE(m.full_name, '') AS manager_name
            FROM hr_employees e
            LEFT JOIN hr_departments d ON d.id = e.department_id
            LEFT JOIN hr_locations l ON l.id = e.location_id
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
            locationId = r.IsDBNull(r.GetOrdinal("location_id")) ? (Guid?)null : r.Guid("location_id"),
            locationName = r.Str("location_name"),
            accessRole = r.Str("access_role"),
            position = r.Str("position"),
            managerId = r.IsDBNull(r.GetOrdinal("manager_id")) ? (Guid?)null : r.Guid("manager_id"),
            managerName = r.Str("manager_name"),
            hireDate = DateOrNull(r, "hire_date"),
            status = r.Str("status"),
            avatar = r.IsDBNull(r.GetOrdinal("avatar")) ? null : r.Str("avatar"),
            isAccounting = r.Bool("is_accounting"),
        };
    }

    // ---- Phân quyền theo phạm vi (chức vụ/phòng ban/địa điểm) ----
    private enum ScopeKind { All, Department, Location, SelfOnly }
    private sealed record AccessScope(ScopeKind Kind, Guid? DepartmentId, Guid? LocationId);

    private static readonly string[] ValidAccessRoles = ["staff", "dept_manager", "location_manager"];
    private static string NormalizeAccessRole(string? role)
        => ValidAccessRoles.Contains(role) ? role! : "staff";

    /// <summary>Xác định phạm vi dữ liệu người dùng được xem: Admin = tất cả; quản lý phòng ban =
    /// phòng của mình; quản lý địa điểm = địa điểm của mình; còn lại = chỉ chính mình.</summary>
    private static async Task<AccessScope> ResolveScopeAsync(NpgsqlConnection conn, ClaimsPrincipal u)
    {
        if (u.IsHrManager()) return new AccessScope(ScopeKind.All, null, null);
        await using var r = await conn.Cmd(
            "SELECT access_role, department_id, location_id FROM hr_employees WHERE username=@u LIMIT 1")
            .With("@u", u.Username()).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return new AccessScope(ScopeKind.SelfOnly, null, null);
        var role = r.Str("access_role");
        Guid? dept = r.IsDBNull(r.GetOrdinal("department_id")) ? null : r.Guid("department_id");
        Guid? loc = r.IsDBNull(r.GetOrdinal("location_id")) ? null : r.Guid("location_id");
        return role switch
        {
            "dept_manager" when dept is not null => new AccessScope(ScopeKind.Department, dept, loc),
            "location_manager" when loc is not null => new AccessScope(ScopeKind.Location, dept, loc),
            _ => new AccessScope(ScopeKind.SelfOnly, dept, loc),
        };
    }

    private static async Task<bool> IsEmployeeOwner(NpgsqlConnection conn, Guid employeeId, string username)
    {
        var owner = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id LIMIT 1")
            .With("@id", employeeId).ExecuteScalarAsync() as string;
        return !string.IsNullOrWhiteSpace(owner)
               && string.Equals(owner, username, StringComparison.OrdinalIgnoreCase);
    }

    private static DateOnly? DateOrNull(NpgsqlDataReader r, string col)
        => r.IsDBNull(r.GetOrdinal(col)) ? (DateOnly?)null : r.DateOnly(col);

    // ---- Thư tri ân "tròn X năm gắn bó" ----
    private const int AnniversaryWindowDays = 7; // hiện suốt tuần kỷ niệm: ngày tròn năm + 6 ngày sau

    private static async Task<AnniversaryLetterRow> ReadAnniversaryTemplateRow(NpgsqlConnection conn)
    {
        await using var r = await conn.Cmd(
            "SELECT enabled, title, body, signature FROM hr_anniversary_letter WHERE id=1").ExecuteReaderAsync();
        if (await r.ReadAsync())
            return new AnniversaryLetterRow(r.Bool("enabled"), r.Str("title"), r.Str("body"), r.Str("signature"));
        return new AnniversaryLetterRow(false, "", "", "");
    }

    /// <summary>
    /// Tính xem hôm nay có rơi vào tuần kỷ niệm tròn năm của nhân viên không, và điền sẵn thư nếu có.
    /// Không bật/không có ngày vào làm/chưa tới mốc → { show = false }.
    /// </summary>
    internal static AnniversaryGreetingResponse BuildAnniversaryGreeting(
        AnniversaryLetterRow tpl, string fullName, DateOnly? hire, DateOnly today)
    {
        if (!tpl.Enabled || hire is null) return new AnniversaryGreetingResponse();
        var h = hire.Value;
        // Xét mốc năm nay và năm ngoái để tuần kỷ niệm bắc qua giao thừa (vd vào làm cuối tháng 12) vẫn bắt được.
        foreach (var year in new[] { today.Year, today.Year - 1 })
        {
            var anniv = AnniversaryInYear(h, year);
            var years = anniv.Year - h.Year;
            if (years < 1) continue;
            var end = anniv.AddDays(AnniversaryWindowDays - 1);
            if (today >= anniv && today <= end)
            {
                var hireVi = $"{h.Day:D2}/{h.Month:D2}/{h.Year:D4}";
                string Fill(string s) => (s ?? "")
                    .Replace("{ten}", fullName)
                    .Replace("{so_nam}", years.ToString())
                    .Replace("{ngay_vao_lam}", hireVi);
                return new AnniversaryGreetingResponse(
                    Show: true,
                    Years: years,
                    AnniversaryDate: $"{anniv.Year:D4}-{anniv.Month:D2}-{anniv.Day:D2}",
                    Key: $"anniv-{anniv.Year}-{years}",
                    Title: Fill(tpl.Title),
                    Body: Fill(tpl.Body),
                    Signature: Fill(tpl.Signature));
            }
        }
        return new AnniversaryGreetingResponse();
    }

    // Ngày kỷ niệm trong một năm; 29/02 rơi vào năm không nhuận thì lùi về 28/02.
    internal static DateOnly AnniversaryInYear(DateOnly hire, int year)
    {
        var day = (hire.Month == 2 && hire.Day == 29 && !DateTime.IsLeapYear(year)) ? 28 : hire.Day;
        return new DateOnly(year, hire.Month, day);
    }

    /// <summary>
    /// Dựng bản xem thử 5 năm cho quản lý mà không sửa ngày vào làm thật và không phụ thuộc mẫu đang bật/tắt.
    /// </summary>
    internal static AnniversaryGreetingResponse BuildAnniversaryPreview(
        AnniversaryLetterRow tpl, string fullName, DateOnly today, int years = 5)
    {
        var safeYears = Math.Max(1, years);
        var result = BuildAnniversaryGreeting(
            tpl with { Enabled = true }, fullName, today.AddYears(-safeYears), today);
        return result with
        {
            Preview = true,
            Key = $"preview-anniv-{today:yyyy-MM-dd}-{safeYears}",
        };
    }

    internal record AnniversaryLetterRow(bool Enabled, string Title, string Body, string Signature);
    internal record AnniversaryGreetingResponse(
        bool Show = false,
        bool Preview = false,
        int Years = 0,
        string AnniversaryDate = "",
        string Key = "",
        string Title = "",
        string Body = "",
        string Signature = "");

    private static JsonElement ParseJson(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone(); }
        catch { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    /// <summary>
    /// Chỉ ghi audit. Mọi bảng nhân sự sửa ở đây (hr_employees, hr_departments, hr_contracts,
    /// hr_payslips, hr_leave_balances, hr_documents, hr_locations) đều có trigger tự phát scope 'hr'
    /// sau khi commit — không gọi hub ở đây nữa (một đường duy nhất, xem DatabaseChangePublisher).
    /// </summary>
    private static async Task Signal(Database db, ClaimsPrincipal u, string action, string entity, string name)
        => await db.RecordAudit(u.Username(), action, entity, name, $"{action} (web).");

    // ---- DTO nhận từ client ----
    public record SaveDepartmentReq(string? Code, string? Name, Guid? ParentId, Guid? ManagerEmployeeId);
    public record SaveLocationReq(string? Code, string? Name, string? Address);
    public record SaveEmployeeReq(string? EmployeeCode, string? Username, string? FullName, DateOnly? Dob, string? Gender,
        string? Phone, string? Email, string? Address, Guid? DepartmentId, string? Position, Guid? ManagerId,
        DateOnly? HireDate, string? Status, string? Avatar, Guid? LocationId = null, string? AccessRole = null,
        bool CreateAccount = true, string? Role = null);
    public record SaveAvatarReq(string? Avatar);
    public record SaveAnniversaryLetterReq(bool Enabled, string? Title, string? Body, string? Signature);
    public record SaveContractReq(string? ContractNo, string? ContractType, DateOnly? StartDate, DateOnly? EndDate,
        decimal BaseSalary, decimal Allowance, string? Status, string? Note);
    public record SavePayslipReq(string? Period, decimal WorkDays, decimal OvertimeHours, decimal BaseSalary,
        decimal Allowance, decimal OvertimePay, decimal Deductions, string? Note, bool Published);
    public record SaveLeaveBalanceReq(int Year, string? LeaveType, decimal TotalDays, decimal UsedDays);
    public record SaveDocumentReq(string? DocType, string? Title, string? IssuedBy, DateOnly? IssuedDate, string? FileUrl, string? Note);
}
