using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 007: migration 006 chọn ứng viên theo sort_order nên với các vai trò có nhiều chức vụ
/// (đặc biệt Employee/Executive) có thể chọn một chức danh chuyên biệt thay vì chức danh nền. Migration
/// này chỉ sửa đúng các hàng được tạo trong cửa sổ thời gian của 006; chức vụ thật đã có từ 004 được giữ.
/// </summary>
public static class CanonicalRolePositionCorrectionMigration
{
    public const string Version = "007_correct_canonical_role_positions";

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
            VALUES (@version, 'Correct migration-006 generated assignments to explicit canonical role positions')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        CREATE TEMP TABLE role_position_corrections ON COMMIT DROP AS
        WITH canonical(role, code) AS (
            VALUES
                ('Admin', 'SYSTEM_ADMIN'),
                ('Executive', 'BOARD_MANAGEMENT'),
                ('ChiefAccountant', 'CHIEF_ACCOUNTANT'),
                ('Accounting', 'ACCOUNTANT'),
                ('Cashier', 'CASHIER'),
                ('Warehouse', 'STOREKEEPER'),
                ('HR', 'HR_MANAGER'),
                ('Manager', 'MANAGER'),
                ('Employee', 'EMPLOYEE')
        ), migration_window AS (
            SELECT
                (SELECT applied_at FROM schema_migrations WHERE version='004_employee_multiple_positions') AS after_004,
                (SELECT applied_at FROM schema_migrations WHERE version='006_backfill_roles_to_employee_positions') AS through_006
        )
        SELECT DISTINCT ON (ep.employee_id, target.id)
               ep.employee_id, ep.position_id AS old_position_id, target.id AS new_position_id,
               ep.is_primary, ep.assigned_at, ep.assigned_by
        FROM hr_employee_positions ep
        JOIN hr_job_positions old_position ON old_position.id=ep.position_id
        JOIN canonical c ON c.role=old_position.default_role
        JOIN hr_job_positions target ON target.code=c.code
        CROSS JOIN migration_window mw
        WHERE ep.position_id<>target.id
          AND ep.assigned_by='system-migration'
          AND ep.assigned_at>mw.after_004
          AND ep.assigned_at<=mw.through_006
        ORDER BY ep.employee_id, target.id, ep.is_primary DESC, ep.assigned_at, ep.position_id;

        DELETE FROM hr_employee_positions ep
        USING role_position_corrections c
        WHERE ep.employee_id=c.employee_id AND ep.position_id=c.old_position_id;

        INSERT INTO hr_employee_positions
            (employee_id, position_id, is_primary, assigned_at, assigned_by)
        SELECT employee_id, new_position_id, is_primary, assigned_at, assigned_by
        FROM role_position_corrections
        ON CONFLICT (employee_id, position_id) DO UPDATE
        SET is_primary=EXCLUDED.is_primary,
            assigned_at=LEAST(hr_employee_positions.assigned_at, EXCLUDED.assigned_at),
            assigned_by=EXCLUDED.assigned_by;

        UPDATE hr_employees e
        SET position_id=ep.position_id,
            position=CASE WHEN btrim(e.position)='' THEN p.name ELSE e.position END,
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
        FROM hr_employee_positions ep
        JOIN hr_job_positions p ON p.id=ep.position_id
        WHERE ep.employee_id=e.id AND ep.is_primary=TRUE;
        """;
}
