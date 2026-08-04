using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 008: chốt identity không phân biệt hoa/thường và dựng lại đúng phần position được sinh bởi
/// migration 006 theo tài khoản liên kết bằng user_id (chỉ fallback username khi user_id chưa có).
/// </summary>
public static class IdentityConsistencyMigration
{
    public const string Version = "008_identity_and_role_position_consistency";

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

        await Cmd(MigrationSql).ExecuteNonQueryAsync(ct);
        await Cmd("""
            INSERT INTO schema_migrations(version, description)
            VALUES (@version, 'Case-insensitive active identities and user-id-safe role-position reconciliation')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        -- Danh tính trước đây unique phân biệt hoa/thường trong khi login/liên kết lại dùng lower().
        -- Nếu đã có Foo/foo, giữ một bản ghi xác định (ưu tiên Admin, tài khoản gắn hồ sơ, đang hoạt
        -- động, tạo sớm), vô hiệu hóa + đổi tên bản trùng. Không xóa dữ liệu nghiệp vụ.
        CREATE TEMP TABLE ambiguous_identity_keys ON COMMIT DROP AS
        SELECT lower(username) AS identity_key
        FROM app_users
        WHERE is_deleted=FALSE
        GROUP BY lower(username)
        HAVING COUNT(*)>1;

        CREATE TEMP TABLE ambiguous_app_users ON COMMIT DROP AS
        SELECT u.id, lower(u.username) AS identity_key
        FROM app_users u
        JOIN ambiguous_identity_keys key ON key.identity_key=lower(u.username)
        WHERE u.is_deleted=FALSE;

        -- Chốt ID trước khi đổi tên các hồ sơ trùng; sau quarantine không được suy luận lại bằng
        -- lower(username), nếu không hồ sơ của phía thua có thể lọt khỏi bước thu hồi quyền.
        CREATE TEMP TABLE ambiguous_employee_ids ON COMMIT DROP AS
        SELECT DISTINCT e.id AS employee_id
        FROM hr_employees e
        WHERE EXISTS (SELECT 1 FROM ambiguous_identity_keys k
                      WHERE k.identity_key=lower(e.username))
           OR EXISTS (SELECT 1 FROM ambiguous_app_users member
                      WHERE member.id=e.user_id);

        CREATE TEMP TABLE duplicate_app_users ON COMMIT DROP AS
        WITH ranked AS (
            SELECT u.id,
                   u.username AS old_username,
                   row_number() OVER (
                       PARTITION BY lower(u.username)
                       ORDER BY (u.role='Admin') DESC,
                                EXISTS(SELECT 1 FROM hr_employees e WHERE e.user_id=u.id) DESC,
                                u.is_active DESC, u.created_at, u.id
                   ) AS rn
            FROM app_users u
            WHERE u.is_deleted=FALSE
        )
        SELECT id, old_username
        FROM ranked
        WHERE rn>1;

        -- A duplicate identity is quarantined, never merged. In particular, secondary roles and
        -- device/session credentials belonging to the loser must not leak to the surviving account.
        DELETE FROM user_roles ur
        USING duplicate_app_users duplicate
        WHERE ur.username=duplicate.old_username;

        UPDATE user_sessions s
        SET revoked=TRUE, revoked_at=CURRENT_TIMESTAMP, revoked_by='identity-migration',
            is_active=FALSE, ended_at=CURRENT_TIMESTAMP, end_reason='Trùng danh tính tài khoản'
        FROM duplicate_app_users duplicate
        WHERE s.username=duplicate.old_username
          AND (s.is_active=TRUE OR s.revoked=FALSE);

        DO $$
        BEGIN
            IF to_regclass('public.hr_device_tokens') IS NOT NULL THEN
                DELETE FROM hr_device_tokens dt
                USING duplicate_app_users duplicate
                WHERE dt.username=duplicate.old_username;
            END IF;
        END $$;

        UPDATE app_users u
        SET username=left(u.username, 84) || '__duplicate_' || replace(u.id::text, '-', ''),
            is_active=FALSE,
            is_deleted=TRUE,
            authorization_version=COALESCE(u.authorization_version, 1)+1
        FROM duplicate_app_users duplicate
        WHERE u.id=duplicate.id;

        WITH ranked AS (
            SELECT e.id,
                   row_number() OVER (
                       PARTITION BY lower(e.username)
                       ORDER BY (e.user_id IS NOT NULL) DESC, e.created_at, e.id
                   ) AS rn
            FROM hr_employees e
            WHERE btrim(e.username)<>''
        )
        UPDATE hr_employees e
        SET username=left(e.username, 84) || '__duplicate_' || replace(e.id::text, '-', ''),
            updated_at=CURRENT_TIMESTAMP
        FROM ranked
        WHERE e.id=ranked.id AND ranked.rn>1;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_username_ci_active
            ON app_users(lower(username)) WHERE is_deleted=FALSE;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_employees_username_ci
            ON hr_employees(lower(username)) WHERE btrim(username)<>'';

        UPDATE hr_employees e
        SET user_id=u.id
        FROM app_users u
        WHERE e.user_id IS NULL AND u.is_deleted=FALSE AND lower(u.username)=lower(e.username);

        -- 002/004 chạy trước unique không phân biệt hoa/thường. Với Foo/foo, cả position chính từ 004 và
        -- position phụ từ 006 đều có thể đến từ tài khoản thua; assigned_by của position phụ còn là tên
        -- người cấp nên phải dùng chính cửa sổ migration làm provenance, không lọc assigned_by.
        DELETE FROM hr_employee_positions ep
        USING ambiguous_employee_ids ambiguous,
              (SELECT
                  (SELECT applied_at FROM schema_migrations WHERE version='004_employee_multiple_positions') AS from_004,
                  (SELECT applied_at FROM schema_migrations WHERE version='006_backfill_roles_to_employee_positions') AS through_006
              ) mw
        WHERE ep.employee_id=ambiguous.employee_id
          AND ep.assigned_at>=mw.from_004
          AND ep.assigned_at<=mw.through_006;

        CREATE TEMP TABLE canonical_role_positions(role varchar(32) PRIMARY KEY, code varchar(48) NOT NULL)
        ON COMMIT DROP;
        INSERT INTO canonical_role_positions(role, code) VALUES
            ('Admin', 'SYSTEM_ADMIN'),
            ('Executive', 'BOARD_MANAGEMENT'),
            ('ChiefAccountant', 'CHIEF_ACCOUNTANT'),
            ('Accounting', 'ACCOUNTANT'),
            ('Cashier', 'CASHIER'),
            ('Warehouse', 'STOREKEEPER'),
            ('HR', 'HR_MANAGER'),
            ('Manager', 'MANAGER'),
            ('Employee', 'EMPLOYEE');

        -- Chỉ phần assignment được tạo trong transaction 006 mới bị dựng lại. Assignment thật được
        -- 004 backfill có assigned_at bằng mốc 004 nên nằm ngoài cửa sổ này.
        DELETE FROM hr_employee_positions ep
        USING hr_employees e,
              (SELECT
                  (SELECT applied_at FROM schema_migrations WHERE version='004_employee_multiple_positions') AS after_004,
                  (SELECT applied_at FROM schema_migrations WHERE version='006_backfill_roles_to_employee_positions') AS through_006
              ) mw
        WHERE ep.employee_id=e.id
          AND ep.assigned_at>mw.after_004
          AND ep.assigned_at<=mw.through_006;

        INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
        SELECT e.id, p.id,
               NOT EXISTS (SELECT 1 FROM hr_employee_positions existing
                           WHERE existing.employee_id=e.id AND existing.is_primary=TRUE),
               'system-migration'
        FROM hr_employees e
        JOIN app_users u ON u.is_deleted=FALSE
         AND (u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username)))
        JOIN canonical_role_positions canonical ON canonical.role=u.role
        JOIN hr_job_positions p ON p.code=canonical.code
        WHERE NOT EXISTS (
            SELECT 1 FROM hr_employee_positions existing
            JOIN hr_job_positions represented ON represented.id=existing.position_id
            WHERE existing.employee_id=e.id AND represented.default_role=u.role
        )
        ON CONFLICT (employee_id, position_id) DO NOTHING;

        INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
        SELECT e.id, p.id, FALSE, COALESCE(NULLIF(ur.granted_by, ''), 'system-migration')
        FROM hr_employees e
        JOIN app_users u ON u.is_deleted=FALSE
         AND (u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username)))
        JOIN user_roles ur ON ur.username=u.username
         AND ur.expires_at IS NULL
        JOIN canonical_role_positions canonical ON canonical.role=ur.role
        JOIN hr_job_positions p ON p.code=canonical.code
        WHERE NOT EXISTS (
            SELECT 1 FROM hr_employee_positions existing
            JOIN hr_job_positions represented ON represented.id=existing.position_id
            WHERE existing.employee_id=e.id AND represented.default_role=ur.role
        )
        ON CONFLICT (employee_id, position_id) DO NOTHING;

        WITH ranked AS (
            SELECT ep.employee_id, ep.position_id,
                   row_number() OVER (
                       PARTITION BY ep.employee_id
                       ORDER BY ep.is_primary DESC, (p.default_role=u.role) DESC, p.sort_order, p.code
                   ) AS rn
            FROM hr_employee_positions ep
            JOIN hr_job_positions p ON p.id=ep.position_id
            JOIN hr_employees e ON e.id=ep.employee_id
            LEFT JOIN app_users u ON u.is_deleted=FALSE
             AND (u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username)))
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
            ), 'staff')
        FROM hr_employee_positions primary_assignment
        JOIN hr_job_positions primary_position ON primary_position.id=primary_assignment.position_id
        WHERE primary_assignment.employee_id=e.id AND primary_assignment.is_primary=TRUE;

        UPDATE hr_employees e
        SET position_id=NULL,
            access_role='staff',
            updated_at=CURRENT_TIMESTAMP
        WHERE NOT EXISTS (SELECT 1 FROM hr_employee_positions ep WHERE ep.employee_id=e.id)
          AND EXISTS (SELECT 1 FROM ambiguous_employee_ids ambiguous
                      WHERE ambiguous.employee_id=e.id);
        """;
}
