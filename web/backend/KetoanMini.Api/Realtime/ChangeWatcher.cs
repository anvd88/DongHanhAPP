using KetoanMini.Api.Data;
using Microsoft.AspNetCore.SignalR;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Dịch vụ nền: theo dõi "mốc thay đổi" rẻ tiền của các bảng dùng chung mỗi ~1.5s.
/// Khi mốc khác lần trước (bất kỳ nguồn ghi nào — web, desktop, hay nơi khác) → phát
/// tín hiệu "changed" qua SignalR để mọi client tự làm mới.
/// Nhờ vậy không cần sửa đường ghi của desktop, chỉ cần desktop NHẬN tín hiệu.
/// </summary>
public sealed class ChangeWatcher(
    IHubContext<ChangesHub> hub,
    Database db,
    ILogger<ChangeWatcher> logger) : BackgroundService
{
    // CHECKSUM_AGG bắt được cả thêm/sửa/xóa (đổi count hoặc đổi nội dung dòng).
    private const string TokenSql = @"
        SELECT CONCAT(
            (SELECT COUNT(*) FROM dbo.documents), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(id, voucher_no, content, doc_date, customer_name))), '0') FROM dbo.documents), '|',
            (SELECT COUNT(*) FROM dbo.document_lines), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(document_id, quantity, unit_price, line_content))), '0') FROM dbo.document_lines), '|',
            (SELECT COUNT(*) FROM dbo.payments), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(id, amount, pay_date))), '0') FROM dbo.payments), '|',
            (SELECT COUNT(*) FROM dbo.customers), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(id, name, is_active))), '0') FROM dbo.customers), '|',
            (SELECT COUNT(*) FROM dbo.app_users), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(id, role, is_active, approval_status, is_deleted))), '0') FROM dbo.app_users), '|',
            (SELECT COUNT(*) FROM (SELECT username FROM dbo.user_sessions WHERE is_active = 1 AND last_seen >= DATEADD(SECOND, -90, SYSDATETIME()) GROUP BY username) online_users), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(username))), '0') FROM (SELECT username FROM dbo.user_sessions WHERE is_active = 1 AND last_seen >= DATEADD(SECOND, -90, SYSDATETIME()) GROUP BY username) online_users), '|',
            (SELECT COUNT(*) FROM dbo.gia_cong_phieu), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(id, trang_thai, tien_do, doi_tac))), '0') FROM dbo.gia_cong_phieu), '|',
            (SELECT COUNT(*) FROM dbo.gia_cong_hang_hoa), '|',
            (SELECT ISNULL(CONVERT(VARCHAR(20), CHECKSUM_AGG(CHECKSUM(phieu_id, so_luong, don_gia_gia_cong))), '0') FROM dbo.gia_cong_hang_hoa)
        ) AS token;";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? last = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await db.OpenAsync(stoppingToken);
                await using var cmd = conn.Cmd(TokenSql);
                var token = (await cmd.ExecuteScalarAsync(stoppingToken))?.ToString() ?? "";

                // Lần đầu chỉ ghi nhận, không broadcast (tránh tín hiệu thừa lúc khởi động).
                if (last is not null && token != last)
                {
                    await hub.Clients.All.SendAsync("changed", "all", stoppingToken);
                    logger.LogDebug("ChangeWatcher: phát tín hiệu changed.");
                }

                last = token;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // DB tạm offline / lỗi truy vấn → bỏ qua chu kỳ này, không làm sập service.
                logger.LogDebug("ChangeWatcher lỗi: {Msg}", ex.Message);
            }

            try { await Task.Delay(1500, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
