using System.Text;
using System.Text.RegularExpressions;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Cài trigger mức STATEMENT cho các bảng cần báo real-time. PostgreSQL giữ thông báo tới khi giao
/// dịch commit, nên máy khách không bao giờ thấy sự kiện của một lần ghi đã bị rollback.
///
/// Danh sách bảng nằm trong C# (<see cref="Watched"/>) chứ không nhúng trong chuỗi SQL, để lúc cài
/// còn ĐỐI CHIẾU được bảng nào chưa tồn tại mà BÁO ĐỘNG. Trước đây khối DO trong SQL lặng lẽ bỏ qua
/// bảng thiếu: không lỗi, không log, chỉ là màn hình đó vĩnh viễn không tự cập nhật — đúng kiểu hỏng
/// âm thầm mà một xương sống không được phép có.
/// </summary>
public static class DatabaseChangePublisher
{
    public const string ChannelName = "ketoanmini_changes";
    private const string TriggerName = "ketoanmini_publish_change";

    /// <summary>
    /// Bảng → các phạm vi (scope) phát khi bảng đổi. Giữ khớp với scope mà lib/realtime.ts và
    /// useApi.ts đang nghe; scope lạ sẽ bị ChangeWatcher.AllowedScopes chặn.
    /// Muốn một màn hình tự làm mới: thêm bảng vào đây — KHÔNG gọi hub trong endpoint
    /// (RealtimeCoverageTests cưỡng chế quy tắc này).
    /// </summary>
    internal static readonly (string Table, string[] Scopes)[] Watched =
    [
        ("documents", ["data"]),
        ("document_lines", ["data"]),
        ("payments", ["data"]),
        ("customers", ["data"]),
        ("customer_opening_balances", ["data"]),
        ("customer_aliases", ["data"]),
        // Kế toán lõi: mọi thay đổi tài khoản, bút toán, kỳ, đối chiếu và ngân sách
        // cùng phát scope nghiệp vụ để các máy đang mở sổ tự làm mới sau khi giao dịch commit.
        ("core_accounts", ["data"]),
        ("core_periods", ["data"]),
        ("core_journal_entries", ["data"]),
        ("core_journal_lines", ["data"]),
        ("core_budgets", ["data"]),
        ("core_reconciliations", ["data"]),
        ("core_period_events", ["data"]),
        ("gia_cong_phieu", ["data"]),
        ("gia_cong_hang_hoa", ["data"]),
        // Chấm công đứng riêng scope 'attendance' thay vì đi chung 'data'. 'data' là scope BẮT-TẤT của
        // useApi (mọi path không khớp luật nào đều nghe nó), nên để chung có hai chiều lãng phí: một bút
        // toán kế toán làm trang chấm công tải lại, và một lượt chấm công làm mọi màn kế toán tải lại.
        // Vẫn giữ 'hr' vì bảng công/hồ sơ nhân sự tính trực tiếp từ các bảng này.
        ("cham_cong_face", ["attendance", "hr"]),
        ("cham_cong_log", ["attendance", "hr"]),

        ("app_users", ["presence"]),
        ("user_sessions", ["presence"]),
        ("user_roles", ["presence"]),
        // Nhóm dưới đây phục vụ /api/users mà useApi ánh xạ sang scope 'presence', nên dùng chung
        // scope đó thì màn hình quản trị tài khoản tự làm mới đúng chỗ.
        ("work_access_requests", ["presence"]),
        ("password_reset_requests", ["presence"]),
        ("registration_codes", ["presence"]),
        ("web_verified_users", ["presence"]),
        ("web_diamond_members", ["presence"]),
        ("web_user_avatars", ["presence"]),
        ("app_settings", ["presence"]),

        ("help_faqs", ["data"]),

        ("hr_departments", ["hr"]),
        ("hr_employees", ["hr"]),
        ("hr_contracts", ["hr"]),
        ("hr_payslips", ["hr"]),
        ("hr_leave_balances", ["hr"]),
        ("hr_documents", ["hr"]),
        ("hr_anniversary_letter", ["hr"]),
        ("hr_shifts", ["hr"]),
        ("hr_shift_assignments", ["hr"]),
        ("hr_requests", ["hr"]),
        ("hr_request_approvals", ["hr"]),
        ("hr_request_attachments", ["hr"]),
        ("hr_approval_delegations", ["hr"]),
        ("hr_locations", ["hr"]),
        ("hr_holidays", ["hr"]),
        ("cham_cong_offline", ["attendance", "hr"]),
        ("cham_cong_qr_sites", ["attendance", "hr"]),
        ("web_system_settings", ["hr"]),

        // Lương / phạt / chi tiền / tài khoản ngân hàng: trước đây các endpoint này tự gọi hub.
        ("hr_salaries", ["hr"]),
        ("hr_payslip_inquiries", ["hr"]),
        ("hr_penalties", ["hr"]),
        ("hr_penalty_ledger", ["hr"]),
        ("hr_penalty_refunds", ["hr"]),
        ("hr_payout_categories", ["hr"]),
        ("hr_payout_vouchers", ["hr"]),
        ("hr_bank_accounts", ["hr"]),

        ("work_tasks", ["tasks"]),
        ("work_task_events", ["tasks"]),

        ("app_portal_posts", ["portal"]),
        ("app_portal_about", ["portal"]),

        ("app_config", ["config"]),
        ("audit_logs", ["audit"]),

        ("app_releases", ["release"]),

        ("app_feedbacks", ["feedback"]),
        ("app_general_feedback", ["feedback"]),
        ("app_support_tickets", ["feedback"]),
        // Báo cáo tin nhắn xấu hiện ở hộp thư xử lý cùng chỗ với góp ý nên dùng chung scope 'feedback'.
        // Các bảng web_chat_* CÒN LẠI cố ý KHÔNG có trigger: tín hiệu chat phải nhắm đúng thành viên
        // cuộc trò chuyện (ChatEndpoints.NotifyChat).
        ("web_chat_reports", ["feedback"]),

        ("surveys", ["data"]),
        ("survey_questions", ["data"]),
        ("survey_answers", ["data"]),
        ("survey_responses", ["data"]),
        ("app_survey_responses", ["data"]),

        ("hr_onboarding_tasks", ["talent"]),
        ("hr_performance_goals", ["talent"]),
        ("hr_performance_reviews", ["talent"]),
        ("hr_training_courses", ["talent"]),
        ("hr_training_enrollments", ["talent"]),
        ("hr_employee_benefits", ["talent"]),
        ("hr_employee_rewards", ["talent"]),
    ];

    /// <summary>Hàm trigger dùng chung: phát từng scope được truyền qua TG_ARGV.</summary>
    internal const string FunctionSql = $"""
        CREATE OR REPLACE FUNCTION public.{TriggerName}()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            scope_name text;
        BEGIN
            FOREACH scope_name IN ARRAY TG_ARGV
            LOOP
                PERFORM pg_notify('{ChannelName}', scope_name);
            END LOOP;
            RETURN NULL;
        END;
        $function$;
        """;

    // Tên bảng/scope đều là hằng trong mã nguồn này chứ không đến từ người dùng, nhưng vẫn chặn ký tự
    // lạ trước khi ghép vào DDL: nếu sau này ai đó nạp danh sách từ cấu hình thì cửa này đã khoá sẵn.
    private static readonly Regex SafeIdentifier = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Cài hàm + trigger cho mọi bảng trong <see cref="Watched"/> ĐANG TỒN TẠI, và trả về danh sách
    /// bảng bị bỏ qua vì chưa có trong lược đồ. Bảng thiếu không làm hỏng realtime của bảng khác
    /// (một module tắt không kéo sập cả hệ thống), nhưng người gọi PHẢI báo động — xem ChangeWatcher.
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnsureAsync(Database db, CancellationToken ct = default)
        => await EnsureAsync(db, Watched, ct);

    internal static async Task<IReadOnlyList<string>> EnsureAsync(
        Database db, IReadOnlyCollection<(string Table, string[] Scopes)> watched, CancellationToken ct = default)
    {
        foreach (var (table, scopes) in watched)
        {
            if (!SafeIdentifier.IsMatch(table))
                throw new InvalidOperationException($"Tên bảng realtime không hợp lệ: {table}");
            foreach (var scope in scopes)
                if (!SafeIdentifier.IsMatch(scope))
                    throw new InvalidOperationException($"Tên scope realtime không hợp lệ: {scope}");
        }

        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(FunctionSql).ExecuteNonQueryAsync(ct);

        // Hỏi MỘT lượt xem bảng nào có thật, thay vì mỗi bảng một vòng đi-về.
        var names = watched.Select(w => w.Table).ToArray();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        await using (var r = await conn.Cmd(
            "SELECT n FROM unnest(@names) AS n WHERE to_regclass('public.' || quote_ident(n)) IS NOT NULL")
            .With("@names", names).ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) existing.Add(r.GetString(0));
        }

        var ddl = new StringBuilder();
        foreach (var (table, scopes) in watched)
        {
            if (!existing.Contains(table)) continue;
            var args = string.Join(", ", scopes.Select(s => $"'{s}'"));
            // Nháy kép quanh tên bảng: mọi tên ở đây đều chữ thường nên nháy không đổi ý nghĩa, nhưng
            // che được trường hợp tên trùng từ khoá SQL (vd một bảng tên "user") làm vỡ câu lệnh.
            ddl.AppendLine($"DROP TRIGGER IF EXISTS {TriggerName} ON public.\"{table}\";");
            ddl.AppendLine(
                $"CREATE TRIGGER {TriggerName} AFTER INSERT OR UPDATE OR DELETE ON public.\"{table}\" " +
                $"FOR EACH STATEMENT EXECUTE FUNCTION public.{TriggerName}({args});");
        }
        if (ddl.Length > 0) await conn.Cmd(ddl.ToString()).ExecuteNonQueryAsync(ct);

        return names.Where(n => !existing.Contains(n)).ToArray();
    }
}
