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
    // Hai mốc tách biệt theo "phạm vi" (scope) để client chỉ refetch đúng thứ liên quan:
    //  • data    = nghiệp vụ (chứng từ, thanh toán, khách hàng, gia công, chấm công)
    //  • presence = hiện diện online + thay đổi tài khoản (vai trò/khóa/avatar ảnh hưởng tên & tích xanh)
    // KHÔNG còn theo dõi chat ở đây: tín hiệu chat được endpoint phát thẳng tới ĐÚNG thành viên
    // cuộc trò chuyện (xem ChatEndpoints.NotifyChat) nên không gây "bão" refetch cho cả 100 máy.
    // CHECKSUM_AGG bắt được cả thêm/sửa/xóa (đổi count hoặc đổi nội dung dòng).
    private const string TokenSql = @"
        SELECT
        CONCAT(
            (SELECT COUNT(*) FROM documents), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, voucher_no, content, doc_date, customer_name), '|' ORDER BY id::text)), '0') FROM documents), '|',
            (SELECT COUNT(*) FROM document_lines), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', document_id, quantity, unit_price, line_content), '|' ORDER BY id::text)), '0') FROM document_lines), '|',
            (SELECT COUNT(*) FROM payments), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, amount, pay_date), '|' ORDER BY id::text)), '0') FROM payments), '|',
            (SELECT COUNT(*) FROM customers), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, name, is_active), '|' ORDER BY id::text)), '0') FROM customers), '|',
            (SELECT COUNT(*) FROM gia_cong_phieu), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, loai_phieu, doi_tac, nhan_vien, ngay_lap, han_hoan_thanh), '|' ORDER BY id::text)), '0') FROM gia_cong_phieu), '|',
            (SELECT COUNT(*) FROM gia_cong_hang_hoa), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', phieu_id, loai_dong, ten_hang, quy_cach, don_vi_tinh, so_luong, don_gia_gia_cong), '|' ORDER BY id::text)), '0') FROM gia_cong_hang_hoa), '|',
            (SELECT COUNT(*) FROM cham_cong_face), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, username, full_name, created_at, created_by), '|' ORDER BY id::text)), '0') FROM cham_cong_face), '|',
            (SELECT COUNT(*) FROM cham_cong_log), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, username, loai, similarity, occurred_at), '|' ORDER BY id::text)), '0') FROM cham_cong_log)
        ) AS data_token,
        CONCAT(
            (SELECT COUNT(*) FROM app_users), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, role, is_active, approval_status, is_deleted), '|' ORDER BY id::text)), '0') FROM app_users), '|',
            (SELECT COUNT(*) FROM (SELECT username FROM user_sessions WHERE is_active = TRUE AND last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds' GROUP BY username) online_users), '|',
            (SELECT COALESCE(md5(string_agg(username, '|' ORDER BY username)), '0') FROM (SELECT username FROM user_sessions WHERE is_active = TRUE AND last_seen >= CURRENT_TIMESTAMP - INTERVAL '90 seconds' GROUP BY username) online_users), '|',
            (SELECT COUNT(*) FROM user_roles), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', username, role), '|' ORDER BY username, role)), '0') FROM user_roles)
        ) AS presence_token,
        CONCAT(
            (SELECT COUNT(*) FROM hr_departments), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, code, name, parent_id, manager_employee_id), '|' ORDER BY id::text)), '0') FROM hr_departments), '|',
            (SELECT COUNT(*) FROM hr_employees), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, employee_code, username, full_name, department_id, position, manager_id, status, updated_at), '|' ORDER BY id::text)), '0') FROM hr_employees), '|',
            (SELECT COUNT(*) FROM hr_contracts), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, employee_id, contract_no, contract_type, start_date, end_date, base_salary, allowance, status), '|' ORDER BY id::text)), '0') FROM hr_contracts), '|',
            (SELECT COUNT(*) FROM hr_payslips), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, employee_id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, published), '|' ORDER BY id::text)), '0') FROM hr_payslips), '|',
            (SELECT COUNT(*) FROM hr_leave_balances), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, employee_id, year, leave_type, total_days, used_days), '|' ORDER BY id::text)), '0') FROM hr_leave_balances), '|',
            (SELECT COUNT(*) FROM hr_documents), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, employee_id, doc_type, title, issued_by, issued_date), '|' ORDER BY id::text)), '0') FROM hr_documents), '|',
            (SELECT COUNT(*) FROM hr_shifts), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, code, name, start_time, end_time, break_minutes, late_grace_minutes, standard_hours, is_overnight), '|' ORDER BY id::text)), '0') FROM hr_shifts), '|',
            (SELECT COUNT(*) FROM hr_shift_assignments), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, employee_id, shift_id, work_date, note), '|' ORDER BY id::text)), '0') FROM hr_shift_assignments), '|',
            (SELECT COUNT(*) FROM hr_requests), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, request_no, req_type, employee_id, requester_username, status, current_step, updated_at), '|' ORDER BY id::text)), '0') FROM hr_requests), '|',
            (SELECT COUNT(*) FROM hr_request_approvals), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, request_id, step_no, approver_username, status, decided_at, decided_by), '|' ORDER BY id::text)), '0') FROM hr_request_approvals), '|',
            (SELECT COUNT(*) FROM cham_cong_log), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, username, loai, similarity, occurred_at), '|' ORDER BY id::text)), '0') FROM cham_cong_log), '|',
            (SELECT COUNT(*) FROM cham_cong_face), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, username, full_name, created_at, created_by), '|' ORDER BY id::text)), '0') FROM cham_cong_face), '|',
            (SELECT COUNT(*) FROM work_tasks), '|',
            (SELECT COALESCE(md5(string_agg(concat_ws('|', id, status, progress, assignee_username, assigner_username, updated_at), '|' ORDER BY id::text)), '0') FROM work_tasks)
        ) AS hr_token;";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? lastData = null, lastPresence = null, lastHr = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = await db.OpenAsync(stoppingToken);
                await using var cmd = conn.Cmd(TokenSql);
                string dataToken = "", presenceToken = "", hrToken = "";
                await using (var r = await cmd.ExecuteReaderAsync(stoppingToken))
                {
                    if (await r.ReadAsync(stoppingToken))
                    {
                        dataToken = r.IsDBNull(0) ? "" : r.GetString(0);
                        presenceToken = r.IsDBNull(1) ? "" : r.GetString(1);
                        hrToken = r.IsDBNull(2) ? "" : r.GetString(2);
                    }
                }

                // Lần đầu chỉ ghi nhận, không broadcast (tránh tín hiệu thừa lúc khởi động).
                if (lastData is not null && dataToken != lastData)
                {
                    await hub.Clients.All.SendAsync("changed", "data", stoppingToken);
                    logger.LogDebug("ChangeWatcher: phát tín hiệu data.");
                }
                if (lastPresence is not null && presenceToken != lastPresence)
                {
                    await hub.Clients.All.SendAsync("changed", "presence", stoppingToken);
                    logger.LogDebug("ChangeWatcher: phát tín hiệu presence.");
                }
                if (lastHr is not null && hrToken != lastHr)
                {
                    await hub.Clients.All.SendAsync("changed", "hr", stoppingToken);
                    logger.LogDebug("ChangeWatcher: phat tin hieu hr.");
                }

                lastData = dataToken;
                lastPresence = presenceToken;
                lastHr = hrToken;
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
