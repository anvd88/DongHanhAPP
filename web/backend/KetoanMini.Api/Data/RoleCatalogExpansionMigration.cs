using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 003: mở rộng danh mục chức vụ cơ bản. Chức vụ nghề nghiệp tách khỏi vai trò truy cập:
/// đa số chức vụ nhận quyền Employee; trưởng bộ phận nhận Manager; Ban giám đốc nhận Executive.
/// </summary>
public static class RoleCatalogExpansionMigration
{
    public const string Version = "003_expand_job_position_catalog";

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
            VALUES (@version, 'Executive read-only scope and comprehensive seeded job-position catalog')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        INSERT INTO system_roles(code, name, is_assignable, is_technical, sort_order)
        VALUES ('Executive', 'Ban giám đốc', TRUE, FALSE, 15)
        ON CONFLICT (code) DO NOTHING;

        INSERT INTO hr_job_positions
            (id, code, name, default_role, default_access_role, is_system, sort_order) VALUES
            ('11111111-1111-4111-8111-111111111109', 'BOARD_MANAGEMENT', 'Ban giám đốc', 'Executive', 'staff', TRUE, 11),
            ('11111111-1111-4111-8111-111111111110', 'GENERAL_DIRECTOR', 'Tổng giám đốc', 'Executive', 'staff', TRUE, 12),
            ('11111111-1111-4111-8111-111111111111', 'DIRECTOR', 'Giám đốc', 'Executive', 'staff', TRUE, 13),
            ('11111111-1111-4111-8111-111111111112', 'DEPUTY_DIRECTOR', 'Phó giám đốc', 'Executive', 'staff', TRUE, 14),
            ('11111111-1111-4111-8111-111111111113', 'DEPARTMENT_HEAD', 'Trưởng phòng', 'Manager', 'dept_manager', TRUE, 71),
            ('11111111-1111-4111-8111-111111111114', 'DEPUTY_DEPARTMENT_HEAD', 'Phó phòng', 'Manager', 'dept_manager', TRUE, 72),
            ('11111111-1111-4111-8111-111111111115', 'HR_SPECIALIST', 'Nhân sự', 'HR', 'staff', TRUE, 81),
            ('11111111-1111-4111-8111-111111111116', 'SALES_MANAGER', 'Quản lý kinh doanh', 'Manager', 'dept_manager', TRUE, 90),
            ('11111111-1111-4111-8111-111111111117', 'SALES_STAFF', 'Nhân viên kinh doanh', 'Employee', 'staff', TRUE, 91),
            ('11111111-1111-4111-8111-111111111118', 'CUSTOMER_SERVICE_MANAGER', 'Quản lý chăm sóc khách hàng', 'Manager', 'dept_manager', TRUE, 100),
            ('11111111-1111-4111-8111-111111111119', 'CUSTOMER_SERVICE_STAFF', 'Nhân viên chăm sóc khách hàng', 'Employee', 'staff', TRUE, 101),
            ('11111111-1111-4111-8111-111111111120', 'PURCHASING_MANAGER', 'Quản lý mua hàng', 'Manager', 'dept_manager', TRUE, 110),
            ('11111111-1111-4111-8111-111111111121', 'PURCHASING_STAFF', 'Nhân viên mua hàng', 'Employee', 'staff', TRUE, 111),
            ('11111111-1111-4111-8111-111111111122', 'WAREHOUSE_MANAGER', 'Quản lý kho', 'Manager', 'dept_manager', TRUE, 120),
            ('11111111-1111-4111-8111-111111111123', 'WAREHOUSE_STAFF', 'Nhân viên kho', 'Employee', 'staff', TRUE, 122),
            ('11111111-1111-4111-8111-111111111124', 'PRODUCTION_MANAGER', 'Quản lý sản xuất', 'Manager', 'dept_manager', TRUE, 130),
            ('11111111-1111-4111-8111-111111111125', 'PRODUCTION_TEAM_LEADER', 'Tổ trưởng sản xuất', 'Manager', 'dept_manager', TRUE, 131),
            ('11111111-1111-4111-8111-111111111126', 'PRODUCTION_WORKER', 'Công nhân sản xuất', 'Employee', 'staff', TRUE, 132),
            ('11111111-1111-4111-8111-111111111127', 'TECHNICAL_MANAGER', 'Quản lý kỹ thuật', 'Manager', 'dept_manager', TRUE, 140),
            ('11111111-1111-4111-8111-111111111128', 'TECHNICIAN', 'Nhân viên kỹ thuật', 'Employee', 'staff', TRUE, 141),
            ('11111111-1111-4111-8111-111111111129', 'MAINTENANCE_TECHNICIAN', 'Nhân viên bảo trì', 'Employee', 'staff', TRUE, 142),
            ('11111111-1111-4111-8111-111111111130', 'IT_MANAGER', 'Quản lý IT', 'Manager', 'dept_manager', TRUE, 150),
            ('11111111-1111-4111-8111-111111111131', 'IT_STAFF', 'Nhân viên IT', 'Employee', 'staff', TRUE, 151),
            ('11111111-1111-4111-8111-111111111132', 'QA_QC_MANAGER', 'Quản lý QA/QC', 'Manager', 'dept_manager', TRUE, 160),
            ('11111111-1111-4111-8111-111111111133', 'QA_QC_STAFF', 'Nhân viên QA/QC', 'Employee', 'staff', TRUE, 161),
            ('11111111-1111-4111-8111-111111111134', 'MARKETING_MANAGER', 'Quản lý Marketing', 'Manager', 'dept_manager', TRUE, 170),
            ('11111111-1111-4111-8111-111111111135', 'MARKETING_STAFF', 'Nhân viên Marketing', 'Employee', 'staff', TRUE, 171),
            ('11111111-1111-4111-8111-111111111136', 'ADMINISTRATION_MANAGER', 'Quản lý hành chính', 'Manager', 'dept_manager', TRUE, 180),
            ('11111111-1111-4111-8111-111111111137', 'ADMINISTRATION_STAFF', 'Nhân viên hành chính', 'Employee', 'staff', TRUE, 181),
            ('11111111-1111-4111-8111-111111111138', 'LEGAL_STAFF', 'Nhân viên pháp chế', 'Employee', 'staff', TRUE, 190),
            ('11111111-1111-4111-8111-111111111139', 'RECEPTIONIST', 'Lễ tân', 'Employee', 'staff', TRUE, 200),
            ('11111111-1111-4111-8111-111111111140', 'DRIVER', 'Lái xe', 'Employee', 'staff', TRUE, 210),
            ('11111111-1111-4111-8111-111111111141', 'CLEANER', 'Tạp vụ', 'Employee', 'staff', TRUE, 220),
            ('11111111-1111-4111-8111-111111111142', 'SECURITY_GUARD', 'Bảo vệ', 'Employee', 'staff', TRUE, 230)
        ON CONFLICT (code) DO NOTHING;
        """;
}
