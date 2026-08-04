using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 006: bảo toàn các vai trò chính/phụ hiện có bằng cách chuyển chúng thành chức vụ tương
/// ứng. Nhờ vậy khi chức vụ trở thành source-of-truth, quyền kiêm nhiệm cũ không bị kẹt hoặc mất ở lần
/// cập nhật hồ sơ đầu tiên. Vai trò phụ đã hết hạn không biến thành chức vụ vĩnh viễn.
/// </summary>
public static class LegacyRolePositionBackfillMigration
{
    public const string Version = "006_backfill_roles_to_employee_positions";

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
            VALUES (@version, 'Preserve existing primary and active secondary account roles as employee positions')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        -- Bảo toàn vai trò chính trước. Nếu hồ sơ chưa có chức vụ nào, chức vụ suy ra này trở thành
        -- chức vụ chính; nếu đã có chức vụ chính thì nó chỉ là một chức vụ kiêm nhiệm bổ sung.
        INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
        SELECT e.id, chosen.id,
               NOT EXISTS (SELECT 1 FROM hr_employee_positions existing WHERE existing.employee_id=e.id),
               'system-migration'
        FROM hr_employees e
        JOIN app_users u ON u.is_deleted=FALSE
         AND (u.id=e.user_id OR lower(u.username)=lower(e.username))
        JOIN LATERAL (
            SELECT p.id
            FROM hr_job_positions p
            WHERE p.default_role=u.role AND p.is_active=TRUE
            ORDER BY p.is_system DESC, p.sort_order, p.code
            LIMIT 1
        ) chosen ON TRUE
        WHERE NOT EXISTS (
            SELECT 1
            FROM hr_employee_positions existing
            JOIN hr_job_positions represented ON represented.id=existing.position_id
            WHERE existing.employee_id=e.id AND represented.default_role=u.role
        )
        ON CONFLICT (employee_id, position_id) DO NOTHING;

        -- Mỗi vai trò phụ còn hiệu lực được biểu diễn bởi đúng một chức vụ mặc định cùng vai trò.
        INSERT INTO hr_employee_positions(employee_id, position_id, is_primary, assigned_by)
        SELECT e.id, chosen.id, FALSE, COALESCE(NULLIF(ur.granted_by, ''), 'system-migration')
        FROM hr_employees e
        JOIN app_users u ON u.is_deleted=FALSE
         AND (u.id=e.user_id OR lower(u.username)=lower(e.username))
        JOIN user_roles ur ON ur.username=u.username
         AND ur.expires_at IS NULL
        JOIN LATERAL (
            SELECT p.id
            FROM hr_job_positions p
            WHERE p.default_role=ur.role AND p.is_active=TRUE
            ORDER BY p.is_system DESC, p.sort_order, p.code
            LIMIT 1
        ) chosen ON TRUE
        WHERE NOT EXISTS (
            SELECT 1
            FROM hr_employee_positions existing
            JOIN hr_job_positions represented ON represented.id=existing.position_id
            WHERE existing.employee_id=e.id AND represented.default_role=ur.role
        )
        ON CONFLICT (employee_id, position_id) DO NOTHING;

        -- Đồng bộ lại các cột tương thích cũ và phạm vi hiệu lực từ TOÀN BỘ chức vụ.
        UPDATE hr_employees e
        SET position_id=primary_position.position_id,
            -- Không ghi đè chức danh tự do cũ. Catalog 003/005 có thể vừa được seed sau lần đối chiếu
            -- đầu tiên của 002; migration 009 sẽ dùng chính chuỗi còn nguyên này để khớp chức vụ chuẩn.
            position=CASE WHEN btrim(e.position)='' THEN primary_catalog.name ELSE e.position END,
            access_role=COALESCE((
                SELECT p.default_access_role
                FROM hr_employee_positions ep
                JOIN hr_job_positions p ON p.id=ep.position_id
                WHERE ep.employee_id=e.id
                ORDER BY CASE p.default_access_role
                    WHEN 'location_manager' THEN 2
                    WHEN 'dept_manager' THEN 1
                    ELSE 0
                END DESC, p.sort_order, p.code
                LIMIT 1
            ), 'staff')
        FROM hr_employee_positions primary_position
        JOIN hr_job_positions primary_catalog ON primary_catalog.id=primary_position.position_id
        WHERE primary_position.employee_id=e.id AND primary_position.is_primary=TRUE;
        """;
}
