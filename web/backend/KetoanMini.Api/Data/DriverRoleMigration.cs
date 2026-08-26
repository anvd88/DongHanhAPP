using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 010: tách Lái xe khỏi Employee để chỉ đúng người giữ chức vụ này được nhận và xử lý
/// lệnh thu tiền. Dữ liệu chức vụ Lái xe hiện có được giữ nguyên và đồng bộ sang role Driver.
/// </summary>
public static class DriverRoleMigration
{
    public const string Version = "010_driver_role_for_cash_collection";

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

        await Cmd("SELECT pg_advisory_xact_lock(823746120031)").ExecuteNonQueryAsync(ct);
        await Cmd("""
            INSERT INTO system_roles(code, name, is_assignable, is_technical, sort_order)
            VALUES ('Driver', 'Lái xe', TRUE, FALSE, 75)
            ON CONFLICT (code) DO UPDATE
            SET name=EXCLUDED.name,
                is_assignable=EXCLUDED.is_assignable,
                is_technical=EXCLUDED.is_technical,
                sort_order=EXCLUDED.sort_order;

            UPDATE hr_job_positions
            SET default_role='Driver', default_access_role='staff', is_active=TRUE
            WHERE code='DRIVER';

            -- Hồ sơ cũ có tên/chức vụ Lái xe nhưng chưa có hàng nối chức vụ được gắn lại đúng catalog.
            WITH candidates AS (
                SELECT e.id AS employee_id, p.id AS position_id
                FROM hr_employees e
                CROSS JOIN hr_job_positions p
                WHERE p.code='DRIVER'
                  AND (e.position_id=p.id OR lower(btrim(e.position)) IN ('lái xe','lai xe','driver'))
            )
            INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
            SELECT c.employee_id, c.position_id,
                   NOT EXISTS (SELECT 1 FROM hr_employee_positions current WHERE current.employee_id=c.employee_id),
                   'system-driver-role-migration'
            FROM candidates c
            ON CONFLICT (employee_id, position_id) DO NOTHING;

            -- Nếu chuỗi chức danh chính của hồ sơ đã là Lái xe thì chốt luôn hàng DRIVER làm chức vụ
            -- chính; các chức vụ kiêm nhiệm khác vẫn được bảo toàn.
            UPDATE hr_employee_positions assignment
            SET is_primary=FALSE
            FROM hr_employees employee
            WHERE assignment.employee_id=employee.id
              AND lower(btrim(employee.position)) IN ('lái xe','lai xe','driver');

            UPDATE hr_employee_positions assignment
            SET is_primary=TRUE
            FROM hr_employees employee, hr_job_positions driver_position
            WHERE assignment.employee_id=employee.id
              AND assignment.position_id=driver_position.id AND driver_position.code='DRIVER'
              AND lower(btrim(employee.position)) IN ('lái xe','lai xe','driver');

            UPDATE hr_employees employee
            SET position_id=driver_position.id, position=driver_position.name, access_role='staff',
                updated_at=CURRENT_TIMESTAMP
            FROM hr_job_positions driver_position
            WHERE driver_position.code='DRIVER'
              AND lower(btrim(employee.position)) IN ('lái xe','lai xe','driver');
            """).ExecuteNonQueryAsync(ct);

        var employeeIds = new List<Guid>();
        await using (var r = await Cmd("""
            SELECT DISTINCT e.id
            FROM hr_employees e
            JOIN hr_employee_positions ep ON ep.employee_id=e.id
            JOIN hr_job_positions p ON p.id=ep.position_id AND p.code='DRIVER'
            JOIN app_users u ON u.is_deleted=FALSE
             AND (u.id=e.user_id OR (e.user_id IS NULL AND lower(u.username)=lower(e.username)))
            WHERE u.role<>'Admin'
            ORDER BY e.id
            """).ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) employeeIds.Add(r.GetGuid(0));

        foreach (var employeeId in employeeIds)
            await EmployeePositionRoleService.SyncAsync(
                conn, employeeId, "system-migration", "", tx, ct,
                changeReason: "Migration 010: chức vụ Lái xe được tách khỏi Nhân viên để xử lý lệnh thu tiền đúng vai trò.");

        await Cmd("""
            INSERT INTO schema_migrations(version, description)
            VALUES (@version, 'Dedicated Driver role for assigned cash-collection orders')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
