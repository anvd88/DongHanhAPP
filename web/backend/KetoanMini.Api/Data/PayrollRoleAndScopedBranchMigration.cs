using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 009: tách quyền lập lương khỏi Kế toán, thu hẹp Giám đốc chi nhánh về phạm vi chi nhánh,
/// khôi phục chức vụ cũ theo danh mục đầy đủ và đồng bộ ngay tài khoản lấy chức vụ làm nguồn phân quyền.
/// </summary>
public static class PayrollRoleAndScopedBranchMigration
{
    public const string Version = "009_payroll_role_and_scoped_branch";

    public static async Task ApplyAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        NpgsqlCommand Cmd(string sql) => new(sql, conn, tx);

        var applied = await Cmd("SELECT 1 FROM schema_migrations WHERE version=@version LIMIT 1")
            .With("@version", Version).ExecuteScalarAsync(ct);
        if (applied is not null and not DBNull)
        {
            await tx.CommitAsync(ct);
            return;
        }

        // Dùng cùng khóa với mọi đường hạ quyền Admin. Reconciliation bên dưới cố ý không tự hạ Admin,
        // nhưng khóa chung giữ migration nối tiếp an toàn với request quản trị đang chạy.
        await Cmd("SELECT pg_advisory_xact_lock(823746120031)").ExecuteNonQueryAsync(ct);
        await Cmd(MigrationSql).ExecuteNonQueryAsync(ct);
        await Cmd("""
            INSERT INTO schema_migrations(version, description)
            VALUES (@version, 'Dedicated Payroll role, branch-scoped director and position-account reconciliation')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        INSERT INTO system_roles(code, name, is_assignable, is_technical, sort_order)
        VALUES ('Payroll', 'Kế toán tiền lương', TRUE, FALSE, 35)
        ON CONFLICT (code) DO UPDATE
        SET name=EXCLUDED.name,
            is_assignable=EXCLUDED.is_assignable,
            is_technical=EXCLUDED.is_technical,
            sort_order=EXCLUDED.sort_order;

        -- 002 chỉ biết catalog nền, còn 003/005 bổ sung nhiều chức danh sau đó. Giữ và đối chiếu chuỗi
        -- chức danh cũ trước khi đồng bộ role để không biến "Kế toán thuế"/"Bảo vệ" thành "Nhân viên".
        CREATE TEMP TABLE legacy_title_matches ON COMMIT DROP AS
        SELECT DISTINCT ON (e.id)
               e.id AS employee_id,
               e.position_id AS old_position_id,
               p.id AS matched_position_id
        FROM hr_employees e
        JOIN app_users u
          ON u.is_deleted=FALSE
         AND (u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username)))
        JOIN hr_job_positions p
          ON lower(btrim(e.position))=lower(p.name) OR upper(btrim(e.position))=p.code
        WHERE btrim(e.position)<>'' AND p.is_active=TRUE
          -- Chuỗi chức danh cũ không phải bằng chứng để nâng quyền. Chỉ chuyên biệt hóa trong đúng
          -- role tài khoản đang có; các mismatch đặc quyền phải được Admin xác nhận qua form chức vụ.
          AND p.default_role=u.role
        ORDER BY e.id,
                 (upper(btrim(e.position))=p.code) DESC,
                 p.is_system DESC, p.sort_order, p.code;

        UPDATE hr_employee_positions ep
        SET is_primary=FALSE
        FROM legacy_title_matches match
        WHERE ep.employee_id=match.employee_id;

        DELETE FROM hr_employee_positions ep
        USING legacy_title_matches match
        WHERE ep.employee_id=match.employee_id
          AND ep.position_id=match.old_position_id
          AND ep.position_id<>match.matched_position_id
          AND ep.assigned_by='system-migration';

        INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
        SELECT employee_id, matched_position_id, TRUE, 'system-migration'
        FROM legacy_title_matches
        ON CONFLICT (employee_id, position_id) DO UPDATE SET is_primary=TRUE;

        UPDATE hr_employees e
        SET position_id=match.matched_position_id,
            position=p.name,
            updated_at=CURRENT_TIMESTAMP
        FROM legacy_title_matches match
        JOIN hr_job_positions p ON p.id=match.matched_position_id
        WHERE e.id=match.employee_id;

        -- Kế toán thường không được lập lương. Chỉ người giữ chức vụ Kế toán tiền lương nhận role Payroll.
        UPDATE hr_job_positions
        SET default_role='Payroll'
        WHERE code='PAYROLL_ACCOUNTANT';

        -- Giám đốc chi nhánh quản lý theo location; không được thừa hưởng CompanyScopeAll của Ban giám đốc.
        UPDATE hr_job_positions
        SET default_role='Manager', default_access_role='location_manager'
        WHERE code='BRANCH_DIRECTOR';

        -- Dựng tập role hiệu lực từ TOÀN BỘ chức vụ. Tài khoản Admin không được migration tự hạ quyền;
        -- các đường thay đổi Admin lúc chạy có kiểm tra "Admin cuối cùng" riêng.
        CREATE TEMP TABLE position_role_reconciliation ON COMMIT DROP AS
        WITH role_rows AS (
            SELECT DISTINCT u.id AS user_id, u.username, u.role AS old_primary,
                   p.default_role::text AS derived_role
            FROM app_users u
            JOIN hr_employees e
              ON u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username))
            JOIN hr_employee_positions ep ON ep.employee_id=e.id
            JOIN hr_job_positions p ON p.id=ep.position_id
            WHERE u.is_deleted=FALSE
        ), desired AS (
            SELECT user_id, username, old_primary,
                   (array_agg(derived_role ORDER BY
                       CASE derived_role
                           WHEN 'Admin' THEN 1000
                           WHEN 'ChiefAccountant' THEN 900
                           WHEN 'Accounting' THEN 800
                           WHEN 'Payroll' THEN 750
                           WHEN 'Cashier' THEN 700
                           WHEN 'HR' THEN 600
                           WHEN 'Manager' THEN 500
                           WHEN 'Warehouse' THEN 400
                           WHEN 'Executive' THEN 300
                           WHEN 'Employee' THEN 100
                           ELSE 0
                       END DESC, derived_role))[1] AS new_primary,
                   array_agg(derived_role ORDER BY derived_role) AS desired_roles
            FROM role_rows
            GROUP BY user_id, username, old_primary
        )
        SELECT desired.user_id, desired.username, desired.old_primary, desired.new_primary,
               current.current_extras,
               array_remove(desired.desired_roles, desired.new_primary) AS desired_extras,
               (desired.old_primary IS DISTINCT FROM desired.new_primary
                OR current.current_extras IS DISTINCT FROM array_remove(desired.desired_roles, desired.new_primary)
                OR current.has_expiring_grant) AS changed,
               array_to_string(ARRAY[desired.old_primary]::text[] || current.current_extras, ', ') AS roles_before,
               array_to_string(ARRAY[desired.new_primary]::text[]
                               || array_remove(desired.desired_roles, desired.new_primary), ', ') AS roles_after
        FROM desired
        CROSS JOIN LATERAL (
            SELECT COALESCE(array_agg(ur.role::text ORDER BY ur.role::text), ARRAY[]::text[]) AS current_extras,
                   COALESCE(bool_or(ur.expires_at IS NOT NULL), FALSE) AS has_expiring_grant
            FROM user_roles ur
            WHERE ur.username=desired.username
        ) current
        WHERE desired.new_primary IS NOT NULL
          AND NOT (desired.old_primary='Admin' AND desired.new_primary<>'Admin');

        INSERT INTO user_role_history
            (username, changed_by, action, roles_before, roles_after, reason, client_ip)
        SELECT username, 'system-migration', 'Đồng bộ vai trò theo chức vụ', roles_before, roles_after,
               'Migration 009: tách Payroll, thu hẹp Giám đốc chi nhánh và chốt chức vụ là nguồn phân quyền.', ''
        FROM position_role_reconciliation
        WHERE changed;

        UPDATE app_users u
        SET role=reconciliation.new_primary,
            authorization_version=COALESCE(u.authorization_version, 1)+1
        FROM position_role_reconciliation reconciliation
        WHERE u.id=reconciliation.user_id AND reconciliation.changed;

        DELETE FROM user_roles ur
        USING position_role_reconciliation reconciliation
        WHERE ur.username=reconciliation.username AND reconciliation.changed;

        INSERT INTO user_roles(username, role, granted_by, granted_at, expires_at)
        SELECT reconciliation.username, extra.role, 'system-position-migration', CURRENT_TIMESTAMP, NULL
        FROM position_role_reconciliation reconciliation
        CROSS JOIN LATERAL unnest(reconciliation.desired_extras) AS extra(role)
        WHERE reconciliation.changed
        ON CONFLICT (username, role) DO UPDATE
        SET granted_by=EXCLUDED.granted_by, granted_at=EXCLUDED.granted_at, expires_at=NULL;

        -- Đồng bộ cột tương thích cũ và phạm vi sau khi catalog đã đổi.
        WITH ranked AS (
            SELECT ep.employee_id, ep.position_id,
                   row_number() OVER (
                       PARTITION BY ep.employee_id
                       ORDER BY ep.is_primary DESC, p.sort_order, p.code
                   ) AS rn
            FROM hr_employee_positions ep
            JOIN hr_job_positions p ON p.id=ep.position_id
        )
        UPDATE hr_employee_positions ep
        SET is_primary=(ranked.rn=1)
        FROM ranked
        WHERE ep.employee_id=ranked.employee_id AND ep.position_id=ranked.position_id;

        UPDATE hr_employees e
        SET position_id=primary_assignment.position_id,
            position=CASE WHEN btrim(e.position)='' THEN primary_position.name ELSE e.position END,
            access_role=COALESCE((
                SELECT scope_position.default_access_role
                FROM hr_employee_positions assignment
                JOIN hr_job_positions scope_position ON scope_position.id=assignment.position_id
                WHERE assignment.employee_id=e.id
                ORDER BY CASE scope_position.default_access_role
                    WHEN 'location_manager' THEN 2
                    WHEN 'dept_manager' THEN 1
                    ELSE 0
                END DESC, scope_position.sort_order, scope_position.code
                LIMIT 1
            ), 'staff'),
            updated_at=CURRENT_TIMESTAMP
        FROM hr_employee_positions primary_assignment
        JOIN hr_job_positions primary_position ON primary_position.id=primary_assignment.position_id
        WHERE primary_assignment.employee_id=e.id AND primary_assignment.is_primary=TRUE;
        """;
}
