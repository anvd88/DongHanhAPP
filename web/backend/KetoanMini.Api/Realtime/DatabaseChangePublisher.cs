using System.Text;
using System.Text.RegularExpressions;
using KetoanMini.Api.Data;

namespace KetoanMini.Api.Realtime;

/// <summary>
/// Cài trigger mức STATEMENT cho các bảng legacy. Trigger ghi integration outbox trong chính
/// transaction nguồn; pg_notify chỉ còn là wake-up chuyển tiếp trong giai đoạn cutover.
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
    /// Bản realtime cũ dùng một trigger gộp INSERT/UPDATE/DELETE có đúng tên <see cref="TriggerName"/>
    /// và không khai báo transition table. Nếu một lần nâng cấp dừng sau khi thay hàm nhưng trước khi
    /// thay trigger, mọi lệnh ghi sẽ chết với 42P01 trước khi <see cref="EnsureAsync(Database, CancellationToken)"/>
    /// có cơ hội cài lại bridge. Bước tiền khởi động này chỉ gỡ trigger đời cũ; các trigger mới có hậu
    /// tố _ins/_upd/_del không bị đụng tới và EnsureAsync sẽ cài lại đầy đủ sau khi schema sẵn sàng.
    /// </summary>
    internal const string LegacyTriggerCleanupSql = $"""
        DO $cleanup$
        DECLARE
            legacy record;
        BEGIN
            FOR legacy IN
                SELECT ns.nspname AS schema_name, cls.relname AS table_name
                FROM pg_trigger trg
                JOIN pg_class cls ON cls.oid = trg.tgrelid
                JOIN pg_namespace ns ON ns.oid = cls.relnamespace
                WHERE NOT trg.tgisinternal
                  AND ns.nspname = 'public'
                  AND trg.tgname = '{TriggerName}'
            LOOP
                EXECUTE format(
                    'DROP TRIGGER IF EXISTS %I ON %I.%I',
                    '{TriggerName}', legacy.schema_name, legacy.table_name);
            END LOOP;
        END
        $cleanup$;
        """;

    /// <summary>
    /// Bảng chuyển tiếp (transition table) chứa các dòng THẬT SỰ đổi của câu lệnh. Ba trigger
    /// INSERT/UPDATE/DELETE cùng đặt một tên nên hàm trigger dùng chung đọc được ở cả ba nhánh.
    /// </summary>
    private const string ChangedRows = "ketoanmini_changed_rows";

    /// <summary>Hậu tố tên trigger → sự kiện + bảng chuyển tiếp tương ứng.</summary>
    private static readonly (string Suffix, string Operation, string Transition)[] TriggerVariants =
    [
        ("ins", "INSERT", "NEW"),
        ("upd", "UPDATE", "NEW"),
        ("del", "DELETE", "OLD"),
    ];

    /// <summary>
    /// Bảng → điều kiện WHEN cho trigger UPDATE. Bảng có mặt ở đây dùng trigger MỨC DÒNG cho UPDATE
    /// (chỉ mức dòng mới so được OLD với NEW), nên phát hay không là do nội dung thay đổi quyết định
    /// chứ không phải do có câu lệnh chạy. Dòng nào cũng gọi hàm, nhưng bridge_key
    /// "tx:&lt;txid&gt;:scope" + ON CONFLICT DO NOTHING vẫn gom về đúng một sự kiện cho mỗi giao dịch.
    ///
    /// user_sessions là bảng bị ghi nhiều nhất trong hệ thống: mỗi nhịp tim của mỗi máy (45 giây một
    /// lần) chạm last_seen. Trước đây mỗi nhịp tim ấy là một lượt phát 'presence' cho TẤT CẢ máy đang
    /// mở — tức tải tăng theo BÌNH PHƯƠNG số người dùng, để báo một tin không ai cần: "người này vẫn
    /// đang online như một phút trước". Điều kiện dưới đây giữ lại đúng những thay đổi có nghĩa: đăng
    /// xuất/thu hồi/đổi chủ phiên, và bước NHẢY của last_seen vượt cửa sổ online — tức khoảnh khắc
    /// một người đang hiện Offline quay lại Online. (Chiều ngược lại — Online tắt vì im lặng quá lâu
    /// — KHÔNG có lệnh ghi nào nên không trigger nào bắt được; máy khách tự làm mới chậm.)
    /// </summary>
    private static readonly Dictionary<string, string> UpdateGuards = new(StringComparer.Ordinal)
    {
        ["user_sessions"] = $"""
            OLD.is_active IS DISTINCT FROM NEW.is_active
            OR OLD.revoked IS DISTINCT FROM NEW.revoked
            OR OLD.username IS DISTINCT FROM NEW.username
            OR (NEW.last_seen IS NOT NULL AND OLD.last_seen IS NULL)
            OR NEW.last_seen > OLD.last_seen + INTERVAL '{PresencePolicy.OnlineWindow}'
            """,
    };

    /// <summary>
    /// Bảng → các chủ đề phát khi bảng đổi. Giữ khớp với SCOPES trong lib/realtime.ts; phần tử đầu
    /// của mỗi queryKey ở máy khách chính là tên chủ đề.
    /// Muốn một màn hình tự làm mới: thêm bảng vào đây — KHÔNG gọi hub trong endpoint
    /// (RealtimeCoverageTests cưỡng chế quy tắc này).
    ///
    /// Cách chọn chủ đề cho một bảng: liệt kê những MÀN HÌNH đọc bảng đó, rồi lấy chủ đề của chúng.
    /// Không phải "bảng này thuộc phân hệ nào" — một bảng có thể nuôi nhiều màn hình.
    ///
    /// Trước đây cả khối kế toán dùng chung một chủ đề tên "data", nên sửa một phiếu thu là đánh
    /// thức mọi màn hình của mọi máy đang mở: bán hàng, mua hàng, công nợ, sổ quỹ, danh mục. Chẻ ra
    /// làm năm chủ đề chỉ có nghĩa khi đi kèm bộ lọc theo kết nối ở RealtimeEndpoints — một kết nối
    /// chỉ nhận chủ đề mà nó đang mở.
    /// </summary>
    internal static readonly (string Table, string[] Scopes)[] Watched =
    [
        // Một phiếu chạm vào ba chỗ: sổ bán hàng, công nợ khách (phiếu bán/trả/thu) và sổ quỹ
        // (phiếu thu/chi tiền mặt). Đây là bảng duy nhất phát ba chủ đề; muốn hẹp hơn thì phải để
        // trigger đọc document_type của các dòng vừa đổi, việc đó chưa làm.
        ("documents", ["sales", "debts", "cash"]),
        ("document_lines", ["sales", "debts", "cash"]),
        // Khách trả tiền: chỉ công nợ đọc bảng này. Sổ quỹ dựng từ cash_fund_ledger, không đọc payments.
        ("payments", ["debts"]),
        ("cash_collection_orders", ["cash", "hr"]),
        ("cash_count_sessions", ["cash", "hr"]),
        ("cash_count_lines", ["cash", "hr"]),
        ("cash_collection_events", ["cash", "hr"]),
        // Bút toán tay là MỘT trong bốn nguồn của view cash_fund_ledger. Thiếu trigger ở đây thì
        // người vừa nộp/rút quỹ thấy số mới, còn máy của người khác giữ số dư cũ tới lần ghi sau —
        // sổ quỹ lệch mà không ai biết.
        ("cash_fund_manual_entries", ["cash"]),
        ("customers", ["debts"]),
        ("products", ["catalog"]),
        ("suppliers", ["purchases"]),
        ("supplier_aliases", ["purchases"]),
        ("purchases", ["purchases"]),
        ("purchase_lines", ["purchases"]),
        ("customer_opening_balances", ["debts"]),
        ("customer_aliases", ["debts"]),
        ("gia_cong_phieu", ["sales"]),
        ("gia_cong_hang_hoa", ["sales"]),
        // Chấm công có chủ đề riêng: một lượt chấm công không được làm màn hình kế toán tải lại, và
        // ngược lại. Vẫn giữ 'hr' vì bảng công và hồ sơ nhân sự tính trực tiếp từ các bảng này.
        ("cham_cong_face", ["attendance", "hr"]),
        ("cham_cong_face_enrollments", ["attendance", "hr"]),
        ("cham_cong_face_enrollment_samples", ["attendance", "hr"]),
        ("cham_cong_log", ["attendance", "hr"]),

        ("app_users", ["presence"]),
        ("user_sessions", ["presence"]),
        ("user_roles", ["presence"]),
        ("system_roles", ["presence"]),
        // Nhóm dưới đây phục vụ /api/users, mà màn hình quản trị tài khoản đọc dưới chủ đề 'presence'.
        ("work_access_requests", ["presence"]),
        ("password_reset_requests", ["presence"]),
        ("registration_codes", ["presence"]),
        ("web_verified_users", ["presence"]),
        ("web_diamond_members", ["presence"]),
        ("web_user_avatars", ["presence"]),
        ("app_settings", ["presence"]),

        // Câu hỏi thường gặp hiện ở trang Trợ giúp, cùng chỗ với cấu hình hệ thống.
        ("help_faqs", ["config"]),

        ("hr_departments", ["hr"]),
        ("hr_job_positions", ["hr"]),
        ("hr_employee_positions", ["hr"]),
        // Thêm 'presence': ảnh đại diện nay nằm ở hr_employees.avatar, mà danh bạ (/api/directory)
        // nghe scope 'presence'. Thiếu nó thì đổi ảnh xong danh bạ vẫn giữ ảnh cũ
        // tới lần tải trang sau. Hồ sơ nhân sự đổi rất thưa nên không gây bão tải lại.
        ("hr_employees", ["hr", "presence"]),
        ("hr_contracts", ["hr"]),
        ("hr_salary_raises", ["hr"]),
        ("hr_payslips", ["hr"]),
        ("hr_payslip_history", ["hr"]),
        ("hr_leave_balances", ["hr"]),
        ("hr_documents", ["hr"]),
        ("hr_anniversary_letter", ["hr"]),
        ("hr_shifts", ["hr"]),
        ("hr_shift_assignments", ["hr"]),
        ("hr_requests", ["hr"]),
        ("hr_request_approvals", ["hr"]),
        ("hr_request_attachments", ["hr"]),
        ("hr_approval_delegations", ["hr"]),
        ("hr_attendance_corrections", ["attendance", "hr"]),
        ("hr_attendance_reminders", ["attendance", "hr"]),
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
        // Phiếu chi tiền mặt là một trong bốn nguồn của view cash_fund_ledger. Thiếu "cash" ở đây
        // thì duyệt chi xong tồn quỹ trên máy người khác vẫn là số cũ.
        ("hr_payout_categories", ["cash", "hr"]),
        ("hr_payout_vouchers", ["cash", "hr"]),
        ("hr_payout_voucher_events", ["cash", "hr"]),
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
        ("surveys", ["portal"]),
        ("survey_questions", ["portal"]),
        ("survey_answers", ["portal"]),
        ("survey_responses", ["portal"]),
        ("app_survey_responses", ["portal"]),

        ("hr_onboarding_tasks", ["talent"]),
        ("hr_performance_goals", ["talent"]),
        ("hr_performance_reviews", ["talent"]),
        ("hr_training_courses", ["talent"]),
        ("hr_training_enrollments", ["talent"]),
        ("hr_employee_benefits", ["talent"]),
        ("hr_employee_rewards", ["talent"]),

        // Hộp thư thông báo của web. Trigger phát sau khi giao dịch nghiệp vụ COMMIT, nên cái chuông
        // không bao giờ hiện một thông báo của lần ghi đã rollback. Tín hiệu chỉ mang tên scope, mỗi
        // máy khách tự gọi /api/notifications để lấy đúng phần của mình.
        ("web_notifications", ["notify"]),
    ];

    /// <summary>
    /// Hàm trigger dùng chung: mỗi transaction/scope chỉ có một invalidation durable. Không gắn
    /// trigger vào chính outbox/inbox/realtime store nên không thể tạo vòng lặp.
    ///
    /// CỬA ĐẦU TIÊN là phép thử "câu lệnh có đổi dòng nào không". Trigger mức STATEMENT vẫn chạy
    /// khi câu lệnh khớp 0 dòng, mà lớp xác thực chạy đúng một câu như thế ở MỌI request
    /// (UPDATE user_sessions ... AND last_seen &lt; now()-2 phút). Không có cửa này thì mỗi lần bấm
    /// của mỗi người sinh một dòng outbox + một dòng realtime_events và một khung SSE gửi tới TẤT CẢ
    /// máy đang mở — đo được 10 GET chỉ đọc = 10 lượt phát 'presence' cho toàn hệ thống.
    /// </summary>
    internal const string FunctionSql = $"""
        CREATE OR REPLACE FUNCTION public.{TriggerName}()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $function$
        DECLARE
            scope_name text;
            event_uuid uuid;
            occurred timestamptz;
            bridge_dedupe text;
        BEGIN
            -- IF lồng chứ không gộp thành một biểu thức: plpgsql chỉ lập kế hoạch cho câu lệnh bên
            -- trong khi thật sự CHẠY tới nó, nhờ vậy trigger mức DÒNG (không có bảng chuyển tiếp)
            -- vẫn dùng chung được hàm này. Gộp một dòng thì tên bảng chuyển tiếp bị phân giải ngay
            -- và trigger mức dòng sẽ đổ lỗi "relation does not exist".
            -- Chỉ ba trigger mới có bảng chuyển tiếp. Trigger gộp đời cũ mang đúng tên không hậu tố;
            -- cho nó đi qua để một DB đang nâng cấp dở không bị khóa mọi lệnh ghi trước bước cleanup.
            IF TG_LEVEL = 'STATEMENT'
               AND TG_NAME IN ('{TriggerName}_ins', '{TriggerName}_upd', '{TriggerName}_del') THEN
                IF NOT EXISTS (SELECT 1 FROM {ChangedRows}) THEN
                    RETURN NULL;
                END IF;
            END IF;
            FOREACH scope_name IN ARRAY TG_ARGV
            LOOP
                occurred := clock_timestamp();
                event_uuid := md5(random()::text || occurred::text || txid_current()::text || scope_name)::uuid;
                bridge_dedupe := 'tx:' || txid_current()::text || ':scope:' || scope_name;
                INSERT INTO integration_outbox
                    (id,event_type,routing_key,aggregate_type,aggregate_id,aggregate_version,
                     payload,headers,occurred_at,bridge_key)
                VALUES
                    (event_uuid,'realtime.invalidate.v1','legacy.realtime.invalidated.v1',TG_TABLE_NAME,
                     NULL,NULL,
                     jsonb_build_object(
                        'eventId',event_uuid,
                        'eventType','realtime.invalidate.v1',
                        'occurredAt',occurred,
                        'producer','KetoanMini.Host/legacy-trigger-bridge',
                        'aggregateId',NULL,
                        'aggregateVersion',NULL,
                        'actor',NULL,
                        'correlationId','pg-tx:' || txid_current()::text,
                        'causationId',NULL,
                        'audience',jsonb_build_array('all'),
                        'data',jsonb_build_object('scope',scope_name)),
                     jsonb_build_object('bridge','postgres-trigger','table',TG_TABLE_NAME),
                     occurred,bridge_dedupe)
                ON CONFLICT DO NOTHING;
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

    public static async Task RemoveLegacyCombinedTriggersAsync(Database db, CancellationToken ct = default)
    {
        // Database có thể chưa tồn tại ở lần chạy đầu tiên. Tạo DB trước để cleanup là no-op an toàn
        // trên cài đặt mới, đồng thời vẫn cứu được cài đặt cũ đang mắc kẹt trước PostgresSchema.
        await db.EnsureDatabaseExistsAsync(ct);
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(LegacyTriggerCleanupSql).ExecuteNonQueryAsync(ct);
    }

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

        // Hỏi MỘT lượt xem bảng nào có thật, thay vì mỗi bảng một vòng đi-về.
        var names = watched.Select(w => w.Table).ToArray();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        await using (var r = await conn.Cmd(
            "SELECT n FROM unnest(@names) AS n WHERE to_regclass('public.' || quote_ident(n)) IS NOT NULL")
            .With("@names", names).ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) existing.Add(r.GetString(0));
        }

        // Hàm VÀ trigger đi chung MỘT lệnh, tức một giao dịch ngầm của PostgreSQL. Tách làm hai lượt
        // thì có một khe hở chết người: nếu tiến trình dừng giữa chừng, cơ sở dữ liệu còn lại hàm MỚI
        // (đọc bảng chuyển tiếp) gắn với trigger CŨ (không khai báo bảng chuyển tiếp) — và mọi lệnh
        // ghi vào bảng đó sẽ đổ lỗi "relation ketoanmini_changed_rows does not exist".
        var ddl = new StringBuilder(FunctionSql).AppendLine();
        foreach (var (table, scopes) in watched)
        {
            if (!existing.Contains(table)) continue;
            var args = string.Join(", ", scopes.Select(s => $"'{s}'"));
            // Nháy kép quanh tên bảng: mọi tên ở đây đều chữ thường nên nháy không đổi ý nghĩa, nhưng
            // che được trường hợp tên trùng từ khoá SQL (vd một bảng tên "user") làm vỡ câu lệnh.
            //
            // BA trigger chứ không một: PostgreSQL chỉ cho khai báo bảng chuyển tiếp khi trigger gắn
            // ĐÚNG MỘT sự kiện. Đổi lại, hàm trigger biết được câu lệnh có đổi dòng nào không.
            // Tên cũ (một trigger gộp) vẫn được gỡ để bản cài đặt cũ tự nâng cấp.
            ddl.AppendLine($"DROP TRIGGER IF EXISTS {TriggerName} ON public.\"{table}\";");
            foreach (var (suffix, operation, transition) in TriggerVariants)
            {
                ddl.AppendLine($"DROP TRIGGER IF EXISTS {TriggerName}_{suffix} ON public.\"{table}\";");
                // Bảng có bộ lọc UPDATE (xem UpdateGuards) đổi sang trigger mức DÒNG cho riêng
                // UPDATE: chỉ mức dòng mới có OLD/NEW để mệnh đề WHEN so sánh. INSERT/DELETE giữ
                // mức câu lệnh như mọi bảng khác.
                var guard = operation == "UPDATE" && UpdateGuards.TryGetValue(table, out var when) ? when : null;
                ddl.AppendLine(guard is null
                    ? $"CREATE TRIGGER {TriggerName}_{suffix} AFTER {operation} ON public.\"{table}\" " +
                      $"REFERENCING {transition} TABLE AS {ChangedRows} " +
                      $"FOR EACH STATEMENT EXECUTE FUNCTION public.{TriggerName}({args});"
                    : $"CREATE TRIGGER {TriggerName}_{suffix} AFTER {operation} ON public.\"{table}\" " +
                      $"FOR EACH ROW WHEN ({guard.Replace("\r", "").Replace('\n', ' ')}) " +
                      $"EXECUTE FUNCTION public.{TriggerName}({args});");
            }
        }
        if (ddl.Length > 0) await conn.Cmd(ddl.ToString()).ExecuteNonQueryAsync(ct);

        return names.Where(n => !existing.Contains(n)).ToArray();
    }
}
