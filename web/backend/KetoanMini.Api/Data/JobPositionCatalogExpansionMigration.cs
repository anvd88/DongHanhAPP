using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>Migration 005: bổ sung các chức vụ doanh nghiệp phổ biến còn thiếu trong danh mục chuẩn.</summary>
public static class JobPositionCatalogExpansionMigration
{
    public const string Version = "005_expand_job_position_catalog_2";

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
            VALUES (@version, 'Additional board, executive, accounting, HR, logistics, R&D and support positions')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private const string MigrationSql = """
        INSERT INTO hr_job_positions
            (id, code, name, default_role, default_access_role, is_system, sort_order) VALUES
            ('11111111-1111-4111-8111-111111111143', 'BOARD_CHAIRMAN', 'Chủ tịch Hội đồng quản trị', 'Executive', 'staff', TRUE, 1),
            ('11111111-1111-4111-8111-111111111144', 'BOARD_MEMBER', 'Thành viên Hội đồng quản trị', 'Executive', 'staff', TRUE, 2),
            ('11111111-1111-4111-8111-111111111145', 'CFO', 'Giám đốc tài chính (CFO)', 'Executive', 'staff', TRUE, 15),
            ('11111111-1111-4111-8111-111111111146', 'COO', 'Giám đốc vận hành (COO)', 'Executive', 'staff', TRUE, 16),
            ('11111111-1111-4111-8111-111111111147', 'BRANCH_DIRECTOR', 'Giám đốc chi nhánh', 'Executive', 'location_manager', TRUE, 17),
            ('11111111-1111-4111-8111-111111111148', 'EXECUTIVE_ASSISTANT', 'Trợ lý giám đốc', 'Employee', 'staff', TRUE, 18),
            ('11111111-1111-4111-8111-111111111149', 'EXECUTIVE_SECRETARY', 'Thư ký giám đốc', 'Employee', 'staff', TRUE, 19),
            ('11111111-1111-4111-8111-111111111150', 'GENERAL_ACCOUNTANT', 'Kế toán tổng hợp', 'Accounting', 'staff', TRUE, 31),
            ('11111111-1111-4111-8111-111111111151', 'TAX_ACCOUNTANT', 'Kế toán thuế', 'Accounting', 'staff', TRUE, 32),
            ('11111111-1111-4111-8111-111111111152', 'RECEIVABLE_ACCOUNTANT', 'Kế toán công nợ phải thu', 'Accounting', 'staff', TRUE, 33),
            ('11111111-1111-4111-8111-111111111153', 'PAYABLE_ACCOUNTANT', 'Kế toán công nợ phải trả', 'Accounting', 'staff', TRUE, 34),
            ('11111111-1111-4111-8111-111111111154', 'PAYMENT_ACCOUNTANT', 'Kế toán thanh toán', 'Accounting', 'staff', TRUE, 35),
            ('11111111-1111-4111-8111-111111111155', 'COST_ACCOUNTANT', 'Kế toán giá thành', 'Accounting', 'staff', TRUE, 36),
            ('11111111-1111-4111-8111-111111111156', 'PAYROLL_ACCOUNTANT', 'Kế toán tiền lương', 'Accounting', 'staff', TRUE, 37),
            ('11111111-1111-4111-8111-111111111157', 'RECRUITER', 'Chuyên viên tuyển dụng', 'HR', 'staff', TRUE, 82),
            ('11111111-1111-4111-8111-111111111158', 'COMPENSATION_BENEFITS', 'Chuyên viên C&B', 'HR', 'staff', TRUE, 83),
            ('11111111-1111-4111-8111-111111111159', 'SALES_TEAM_LEADER', 'Trưởng nhóm kinh doanh', 'Manager', 'dept_manager', TRUE, 92),
            ('11111111-1111-4111-8111-111111111160', 'IMPORT_EXPORT_STAFF', 'Nhân viên xuất nhập khẩu', 'Employee', 'staff', TRUE, 112),
            ('11111111-1111-4111-8111-111111111161', 'LOGISTICS_MANAGER', 'Quản lý logistics', 'Manager', 'dept_manager', TRUE, 113),
            ('11111111-1111-4111-8111-111111111162', 'LOGISTICS_STAFF', 'Nhân viên logistics', 'Employee', 'staff', TRUE, 114),
            ('11111111-1111-4111-8111-111111111163', 'RND_MANAGER', 'Quản lý nghiên cứu và phát triển', 'Manager', 'dept_manager', TRUE, 143),
            ('11111111-1111-4111-8111-111111111164', 'RND_STAFF', 'Nhân viên nghiên cứu và phát triển', 'Employee', 'staff', TRUE, 144),
            ('11111111-1111-4111-8111-111111111165', 'OCCUPATIONAL_SAFETY', 'Nhân viên an toàn lao động', 'Employee', 'staff', TRUE, 162),
            ('11111111-1111-4111-8111-111111111166', 'CANTEEN_MANAGER', 'Quản lý căn tin', 'Manager', 'dept_manager', TRUE, 231),
            ('11111111-1111-4111-8111-111111111167', 'COOK', 'Đầu bếp', 'Employee', 'staff', TRUE, 232),
            ('11111111-1111-4111-8111-111111111168', 'CANTEEN_STAFF', 'Nhân viên căn tin', 'Employee', 'staff', TRUE, 233)
        ON CONFLICT (code) DO NOTHING;
        """;
}
