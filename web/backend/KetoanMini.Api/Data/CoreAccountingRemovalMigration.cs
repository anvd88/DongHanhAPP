using Npgsql;

namespace KetoanMini.Api.Data;

/// <summary>
/// Migration 011: gỡ hẳn sổ kế toán kép (kế toán lõi).
///
/// Vì sao xoá chứ không giữ lại cho chắc: mọi con số mà giao diện đang dùng đều DẪN XUẤT lúc đọc từ
/// chứng từ gốc — công nợ là tổng trên documents/payments, sổ quỹ là view cash_fund_ledger. Bảy bảng
/// core_* là một bản sao THỨ HAI của cùng sự thật, dựng bằng một nút bấm tay theo kỳ với định khoản
/// gán cứng trong mã nguồn. Bản sao ấy không dẫn dắt màn hình nào, nên khi nó lệch thì không ai biết;
/// giữ lại chỉ để lại một nguồn số liệu mâu thuẫn cho người đọc CSDL sau này.
///
/// CASCADE là cố ý: bảng con (core_journal_lines, core_period_events) và các trigger realtime từng
/// gắn lên nhóm bảng này phải đi cùng. Không bảng nào ngoài nhóm core_* tham chiếu tới chúng.
/// </summary>
public static class CoreAccountingRemovalMigration
{
    public const string Version = "011_drop_core_accounting";

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

        await Cmd("SELECT pg_advisory_xact_lock(823746120032)").ExecuteNonQueryAsync(ct);
        await Cmd("""
            DROP TABLE IF EXISTS core_journal_lines CASCADE;
            DROP TABLE IF EXISTS core_journal_entries CASCADE;
            DROP TABLE IF EXISTS core_period_events CASCADE;
            DROP TABLE IF EXISTS core_periods CASCADE;
            DROP TABLE IF EXISTS core_reconciliations CASCADE;
            DROP TABLE IF EXISTS core_budgets CASCADE;
            DROP TABLE IF EXISTS core_accounts CASCADE;
            """).ExecuteNonQueryAsync(ct);

        await Cmd("""
            INSERT INTO schema_migrations(version, description)
            VALUES (@version, 'Drop the duplicated double-entry ledger; derived reads are the single source')
            """).With("@version", Version).ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
