using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 004: một hồ sơ nhân sự có thể kiêm nhiệm nhiều chức vụ. Cột
/// <c>hr_employees.position_id</c> vẫn được giữ làm chức vụ chính để các client cũ tiếp tục hoạt động;
/// bảng liên kết mới là nguồn đầy đủ cho tập chức vụ.
/// </summary>
public static class EmployeePositionMigration
{
    public const string Version = "004_employee_multiple_positions";

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
            VALUES (@version, 'Many-to-many employee job positions with one deterministic primary position')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        CREATE TABLE IF NOT EXISTS hr_employee_positions (
            employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
            position_id uuid NOT NULL REFERENCES hr_job_positions(id) ON DELETE RESTRICT,
            is_primary boolean NOT NULL DEFAULT FALSE,
            assigned_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            assigned_by varchar(128) NOT NULL DEFAULT '',
            PRIMARY KEY (employee_id, position_id)
        );

        -- Dữ liệu một-chức-vụ cũ trở thành chức vụ chính, không mất hồ sơ hay tài khoản nào.
        INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
        SELECT id, position_id, TRUE, 'system-migration'
        FROM hr_employees
        WHERE position_id IS NOT NULL
        ON CONFLICT (employee_id, position_id) DO UPDATE SET is_primary=TRUE;

        -- Phòng trường hợp migration từng bị dừng giữa chừng: chuẩn hóa về đúng một chức vụ chính
        -- trước khi tạo unique index một-phần.
        WITH ranked AS (
            SELECT employee_id, position_id,
                   row_number() OVER (
                       PARTITION BY employee_id
                       ORDER BY is_primary DESC, assigned_at, position_id
                   ) AS rn
            FROM hr_employee_positions
        )
        UPDATE hr_employee_positions ep
        SET is_primary = (ranked.rn = 1)
        FROM ranked
        WHERE ep.employee_id=ranked.employee_id AND ep.position_id=ranked.position_id;

        CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_employee_positions_primary
            ON hr_employee_positions(employee_id) WHERE is_primary=TRUE;
        CREATE INDEX IF NOT EXISTS ix_hr_employee_positions_position
            ON hr_employee_positions(position_id, employee_id);
        """;
}
