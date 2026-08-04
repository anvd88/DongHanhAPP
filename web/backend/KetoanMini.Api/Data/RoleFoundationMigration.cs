using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 002: chuẩn hóa vai trò và tạo danh mục chức vụ dùng chung cho web/Android.
/// Migration chỉ chuyển đổi dữ liệu phân quyền; không xóa tài khoản, hồ sơ hay dữ liệu nghiệp vụ.
/// </summary>
public static class RoleFoundationMigration
{
    public const string Version = "002_role_position_foundation";

    public static async Task ApplyAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        NpgsqlCommand Cmd(string sql) => new(sql, conn, tx);

        await Cmd("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version varchar(64) PRIMARY KEY,
                description text NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            )
            """).ExecuteNonQueryAsync(ct);

        var applied = await Cmd("SELECT 1 FROM schema_migrations WHERE version=@version LIMIT 1")
            .With("@version", Version)
            .ExecuteScalarAsync(ct);
        if (applied is not null and not DBNull)
        {
            await tx.CommitAsync(ct);
            return;
        }

        await Cmd(MigrationSql).ExecuteNonQueryAsync(ct);
        await Cmd("""
            INSERT INTO schema_migrations(version, description)
            VALUES (@version, 'Canonical system roles and seeded HR job positions')
            """)
            .With("@version", Version)
            .ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        CREATE TABLE IF NOT EXISTS system_roles (
            code varchar(32) PRIMARY KEY,
            name varchar(120) NOT NULL,
            is_assignable boolean NOT NULL DEFAULT TRUE,
            is_technical boolean NOT NULL DEFAULT FALSE,
            sort_order integer NOT NULL DEFAULT 100,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        INSERT INTO system_roles(code, name, is_assignable, is_technical, sort_order) VALUES
            ('Admin', 'Quản trị hệ thống', TRUE, FALSE, 10),
            ('ChiefAccountant', 'Kế toán trưởng', TRUE, FALSE, 20),
            ('Accounting', 'Kế toán', TRUE, FALSE, 30),
            ('Cashier', 'Thủ quỹ', TRUE, FALSE, 40),
            ('Warehouse', 'Thủ kho', TRUE, FALSE, 50),
            ('HR', 'Quản lý nhân sự', TRUE, FALSE, 60),
            ('Manager', 'Quản lý', TRUE, FALSE, 70),
            ('Employee', 'Nhân viên', TRUE, FALSE, 80),
            ('Kiosk', 'Thiết bị Kiosk', FALSE, TRUE, 900)
        ON CONFLICT (code) DO NOTHING;

        -- Ghi lại mọi thay đổi trước khi chuẩn hóa. Vai trò lạ bị hạ an toàn về Employee thay vì
        -- được giữ như một quyền không xác định hoặc xóa tài khoản của người dùng.
        WITH normalized AS (
            SELECT username, role AS old_role,
                   CASE lower(btrim(role))
                       WHEN 'admin' THEN 'Admin'
                       WHEN 'accounting' THEN 'Accounting'
                       WHEN 'ketoan' THEN 'Accounting'
                       WHEN 'ke toan' THEN 'Accounting'
                       WHEN 'chiefaccountant' THEN 'ChiefAccountant'
                       WHEN 'ketoantruong' THEN 'ChiefAccountant'
                       WHEN 'ke toan truong' THEN 'ChiefAccountant'
                       WHEN 'kế toán trưởng' THEN 'ChiefAccountant'
                       WHEN 'cashier' THEN 'Cashier'
                       WHEN 'thuquy' THEN 'Cashier'
                       WHEN 'thu quy' THEN 'Cashier'
                       WHEN 'thủ quỹ' THEN 'Cashier'
                       WHEN 'warehouse' THEN 'Warehouse'
                       WHEN 'thukho' THEN 'Warehouse'
                       WHEN 'thu kho' THEN 'Warehouse'
                       WHEN 'thủ kho' THEN 'Warehouse'
                       WHEN 'storekeeper' THEN 'Warehouse'
                       WHEN 'hr' THEN 'HR'
                       WHEN 'humanresources' THEN 'HR'
                       WHEN 'manager' THEN 'Manager'
                       WHEN 'truongphong' THEN 'Manager'
                       WHEN 'truong phong' THEN 'Manager'
                       WHEN 'trưởng phòng' THEN 'Manager'
                       WHEN 'employee' THEN 'Employee'
                       WHEN 'user' THEN 'Employee'
                       WHEN 'kiosk' THEN 'Kiosk'
                       ELSE 'Employee'
                   END AS new_role
            FROM app_users
        )
        INSERT INTO user_role_history
            (username, changed_by, action, roles_before, roles_after, reason, client_ip)
        SELECT username, 'system-migration', 'Chuẩn hóa vai trò', old_role, new_role,
               'Migration 002: chuyển sang danh mục vai trò chuẩn; không xóa tài khoản.', ''
        FROM normalized
        WHERE old_role IS DISTINCT FROM new_role;

        UPDATE app_users
        SET role = CASE lower(btrim(role))
            WHEN 'admin' THEN 'Admin'
            WHEN 'accounting' THEN 'Accounting'
            WHEN 'ketoan' THEN 'Accounting'
            WHEN 'ke toan' THEN 'Accounting'
            WHEN 'chiefaccountant' THEN 'ChiefAccountant'
            WHEN 'ketoantruong' THEN 'ChiefAccountant'
            WHEN 'ke toan truong' THEN 'ChiefAccountant'
            WHEN 'kế toán trưởng' THEN 'ChiefAccountant'
            WHEN 'cashier' THEN 'Cashier'
            WHEN 'thuquy' THEN 'Cashier'
            WHEN 'thu quy' THEN 'Cashier'
            WHEN 'thủ quỹ' THEN 'Cashier'
            WHEN 'warehouse' THEN 'Warehouse'
            WHEN 'thukho' THEN 'Warehouse'
            WHEN 'thu kho' THEN 'Warehouse'
            WHEN 'thủ kho' THEN 'Warehouse'
            WHEN 'storekeeper' THEN 'Warehouse'
            WHEN 'hr' THEN 'HR'
            WHEN 'humanresources' THEN 'HR'
            WHEN 'manager' THEN 'Manager'
            WHEN 'truongphong' THEN 'Manager'
            WHEN 'truong phong' THEN 'Manager'
            WHEN 'trưởng phòng' THEN 'Manager'
            WHEN 'employee' THEN 'Employee'
            WHEN 'user' THEN 'Employee'
            WHEN 'kiosk' THEN 'Kiosk'
            ELSE 'Employee'
        END;

        -- Chuẩn hóa vai trò phụ theo kiểu insert-canonical rồi xóa giá trị cũ để không va khóa chính
        -- nếu một tài khoản từng có đồng thời cả dạng viết cũ và dạng chuẩn.
        WITH normalized AS (
            SELECT username, granted_by, granted_at, expires_at,
                   CASE lower(btrim(role))
                       WHEN 'accounting' THEN 'Accounting'
                       WHEN 'ketoan' THEN 'Accounting'
                       WHEN 'ke toan' THEN 'Accounting'
                       WHEN 'chiefaccountant' THEN 'ChiefAccountant'
                       WHEN 'ketoantruong' THEN 'ChiefAccountant'
                       WHEN 'ke toan truong' THEN 'ChiefAccountant'
                       WHEN 'kế toán trưởng' THEN 'ChiefAccountant'
                       WHEN 'cashier' THEN 'Cashier'
                       WHEN 'thuquy' THEN 'Cashier'
                       WHEN 'thu quy' THEN 'Cashier'
                       WHEN 'thủ quỹ' THEN 'Cashier'
                       WHEN 'warehouse' THEN 'Warehouse'
                       WHEN 'thukho' THEN 'Warehouse'
                       WHEN 'thu kho' THEN 'Warehouse'
                       WHEN 'thủ kho' THEN 'Warehouse'
                       WHEN 'storekeeper' THEN 'Warehouse'
                       WHEN 'hr' THEN 'HR'
                       WHEN 'humanresources' THEN 'HR'
                       WHEN 'manager' THEN 'Manager'
                       WHEN 'truongphong' THEN 'Manager'
                       WHEN 'truong phong' THEN 'Manager'
                       WHEN 'trưởng phòng' THEN 'Manager'
                       ELSE NULL
                   END AS new_role
            FROM user_roles
        )
        INSERT INTO user_roles(username, role, granted_by, granted_at, expires_at)
        SELECT username, new_role, granted_by, granted_at, expires_at
        FROM normalized
        WHERE new_role IS NOT NULL
        ON CONFLICT (username, role) DO NOTHING;

        DELETE FROM user_roles
        WHERE role NOT IN ('Accounting', 'ChiefAccountant', 'Cashier', 'Warehouse', 'HR', 'Manager');

        CREATE TABLE IF NOT EXISTS hr_job_positions (
            id uuid PRIMARY KEY,
            code varchar(48) NOT NULL UNIQUE,
            name varchar(120) NOT NULL,
            default_role varchar(32) NOT NULL REFERENCES system_roles(code),
            default_access_role varchar(24) NOT NULL DEFAULT 'staff',
            is_system boolean NOT NULL DEFAULT TRUE,
            is_active boolean NOT NULL DEFAULT TRUE,
            sort_order integer NOT NULL DEFAULT 100,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT ck_hr_job_positions_access_role
                CHECK (default_access_role IN ('staff', 'dept_manager', 'location_manager'))
        );

        INSERT INTO hr_job_positions
            (id, code, name, default_role, default_access_role, is_system, sort_order) VALUES
            ('11111111-1111-4111-8111-111111111101', 'SYSTEM_ADMIN', 'Quản trị hệ thống', 'Admin', 'staff', TRUE, 10),
            ('11111111-1111-4111-8111-111111111102', 'CHIEF_ACCOUNTANT', 'Kế toán trưởng', 'ChiefAccountant', 'staff', TRUE, 20),
            ('11111111-1111-4111-8111-111111111103', 'ACCOUNTANT', 'Kế toán', 'Accounting', 'staff', TRUE, 30),
            ('11111111-1111-4111-8111-111111111104', 'CASHIER', 'Thủ quỹ', 'Cashier', 'staff', TRUE, 40),
            ('11111111-1111-4111-8111-111111111105', 'STOREKEEPER', 'Thủ kho', 'Warehouse', 'staff', TRUE, 50),
            ('11111111-1111-4111-8111-111111111106', 'HR_MANAGER', 'Quản lý nhân sự', 'HR', 'staff', TRUE, 60),
            ('11111111-1111-4111-8111-111111111107', 'MANAGER', 'Quản lý', 'Manager', 'dept_manager', TRUE, 70),
            ('11111111-1111-4111-8111-111111111108', 'EMPLOYEE', 'Nhân viên', 'Employee', 'staff', TRUE, 80)
        ON CONFLICT (code) DO NOTHING;

        ALTER TABLE hr_employees
            ADD COLUMN IF NOT EXISTS position_id uuid NULL REFERENCES hr_job_positions(id) ON DELETE RESTRICT;
        CREATE INDEX IF NOT EXISTS ix_hr_employees_position ON hr_employees(position_id);

        -- Ưu tiên vai trò tài khoản đang có để gắn chức vụ hệ thống tương ứng; không ghi đè tên chức vụ
        -- tự do của hồ sơ cũ. Hồ sơ chưa có tài khoản được khớp theo mã/tên chức vụ nếu có thể.
        UPDATE hr_employees e
        SET position_id = p.id
        FROM app_users u, hr_job_positions p
        WHERE e.position_id IS NULL
          AND u.is_deleted = FALSE
          AND lower(u.username) = lower(e.username)
          AND p.default_role = u.role;

        UPDATE hr_employees e
        SET position_id = p.id
        FROM hr_job_positions p
        WHERE e.position_id IS NULL
          AND (lower(btrim(e.position)) = lower(p.name) OR upper(btrim(e.position)) = p.code);

        UPDATE hr_employees e
        SET position = CASE WHEN btrim(e.position) = '' THEN p.name ELSE e.position END,
            access_role = CASE
                WHEN e.access_role = 'staff' THEN p.default_access_role
                ELSE e.access_role
            END
        FROM hr_job_positions p
        WHERE e.position_id = p.id;

        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_app_users_system_role') THEN
                ALTER TABLE app_users
                    ADD CONSTRAINT fk_app_users_system_role FOREIGN KEY(role) REFERENCES system_roles(code);
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='fk_user_roles_system_role') THEN
                ALTER TABLE user_roles
                    ADD CONSTRAINT fk_user_roles_system_role FOREIGN KEY(role) REFERENCES system_roles(code);
            END IF;
        END $$;
        """;
}
