using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Phiếu chi của phòng kế toán: mọi khoản tiền thực chi (lương, hoàn tiền phạt, mua vật dụng, dầu…) đều
/// lập thành một phiếu gắn với NGƯỜI NHẬN là một nhân viên. Phiếu tự sinh mã QR; người nhận quét mã bằng
/// app để ký nhận đã cầm tiền, SAU ĐÓ kế toán mới bấm "Duyệt chi" chốt sổ. Chưa có chữ ký điện tử của
/// người nhận thì không duyệt chi được — đây là chốt chống gian lận của cả luồng, xem <see cref="StatusConfirmed"/>.
///
/// Server tách quyền lập, duyệt và hoàn tất chi bằng payout.create / payout.approve / payout.pay. Ngoài
/// permission, mọi thao tác tiền đều buộc hồ sơ nhân viên thuộc phòng ban is_accounting; client không thể
/// tự khai vai trò hoặc trạng thái để vượt luồng.
/// </summary>
public static class PayoutVoucherEndpoints
{
    public enum PayslipVoucherCancelResult { NotFound, Cancelled, Blocked }

    /// <summary>Tiền đã trao tay chưa? Phiếu mới lập luôn chờ người nhận quét QR ký nhận.</summary>
    public const string StatusAwaitingScan = "AwaitingScan";
    /// <summary>Phiếu không yêu cầu người nhận ký QR và đang chờ cấp có thẩm quyền duyệt.</summary>
    public const string StatusAwaitingApproval = "AwaitingApproval";
    /// <summary>Người nhận đã quét QR xác nhận cầm tiền — điều kiện BẮT BUỘC để duyệt chi.</summary>
    public const string StatusConfirmed = "Confirmed";
    /// <summary>Đã được kế toán trưởng duyệt, đang chờ thủ quỹ thực chi/hoàn tất.</summary>
    public const string StatusApproved = "Approved";
    public const string StatusPaid = "Paid";
    public const string StatusRejected = "Rejected";
    public const string StatusCancelled = "Cancelled";

    private const string EventCreated = "created";
    private const string EventQrRegenerated = "qr_regenerated";
    private const string EventRecipientConfirmed = "recipient_confirmed";
    private const string EventApproved = "approved";
    private const string EventRejected = "rejected";
    private const string EventCancelled = "cancelled";
    private const string EventCompleted = "completed";
    private const string EventAmountUpdated = "amount_updated";

    public const string SourceManual = "manual";
    public const string SourceRefund = "refund";
    public const string SourcePayslip = "payslip";

    /// <summary>Danh mục lõi do hệ thống tự dùng khi sinh phiếu — không cho xóa/đổi mã.</summary>
    public const string CategorySalary = "salary";
    public const string CategoryPenaltyRefund = "penalty-refund";

    /// <summary>Tiền tố QR của phiếu chi. Đứng riêng với QR đăng nhập nên hai luồng không lẫn nhau.</summary>
    public const string QrPrefix = "ketoanmini-payout:";
    /// <summary>Handler của vé quyết định QR (xem QrActionTokenService).</summary>
    public const string QrHandler = "payout_voucher";
    public const string QrConfirmAction = "payout_confirm";

    /// <summary>
    /// Mã QR chỉ sống ngắn: người nhận đứng ngay tại bàn kế toán thì 15 phút là thừa, mà ảnh chụp màn hình
    /// mã bị chuyển cho người khác/gửi từ xa cũng hết tác dụng rất nhanh. Hết hạn thì kế toán bấm "Tạo lại mã".
    /// </summary>
    public static readonly TimeSpan QrLifetime = TimeSpan.FromMinutes(15);

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS hr_payout_voucher_seq START 1;

            CREATE TABLE IF NOT EXISTS hr_payout_categories (
                id uuid PRIMARY KEY,
                code varchar(40) NOT NULL UNIQUE,
                name varchar(120) NOT NULL DEFAULT '',
                description text NOT NULL DEFAULT '',
                is_active boolean NOT NULL DEFAULT TRUE,
                is_system boolean NOT NULL DEFAULT FALSE,
                sort_order integer NOT NULL DEFAULT 100,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS hr_payout_vouchers (
                id uuid PRIMARY KEY,
                voucher_no varchar(20) NOT NULL DEFAULT '',
                category_id uuid NULL REFERENCES hr_payout_categories(id),
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                amount numeric(18,2) NOT NULL DEFAULT 0,
                source_kind varchar(16) NOT NULL DEFAULT 'manual',
                source_id uuid NULL,
                source_no varchar(32) NOT NULL DEFAULT '',
                reason text NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                status varchar(20) NOT NULL DEFAULT 'AwaitingScan',
                qr_code varchar(64) NOT NULL DEFAULT '',
                qr_expires_at timestamptz NULL,
                created_by varchar(128) NOT NULL DEFAULT '',
                requires_recipient_confirmation boolean NOT NULL DEFAULT TRUE,
                confirmed_at timestamptz NULL,
                confirmed_by varchar(128) NOT NULL DEFAULT '',
                approved_by varchar(128) NOT NULL DEFAULT '',
                approved_at timestamptz NULL,
                paid_at timestamptz NULL,
                completed_by varchar(128) NOT NULL DEFAULT '',
                completed_at timestamptz NULL,
                rejected_by varchar(128) NOT NULL DEFAULT '',
                rejected_at timestamptz NULL,
                reject_reason text NOT NULL DEFAULT '',
                cancelled_by varchar(128) NOT NULL DEFAULT '',
                cancelled_at timestamptz NULL,
                cancel_reason text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            ALTER TABLE hr_payout_vouchers
                ADD COLUMN IF NOT EXISTS requires_recipient_confirmation boolean NOT NULL DEFAULT TRUE,
                ADD COLUMN IF NOT EXISTS confirmed_by varchar(128) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS approved_at timestamptz NULL,
                ADD COLUMN IF NOT EXISTS completed_by varchar(128) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL,
                ADD COLUMN IF NOT EXISTS rejected_by varchar(128) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS rejected_at timestamptz NULL,
                ADD COLUMN IF NOT EXISTS reject_reason text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS cancelled_by varchar(128) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS cancelled_at timestamptz NULL;

            -- Các cột mốc mới phải có nghĩa ngay cả với dữ liệu đã phát sinh trước bản nâng cấp.
            UPDATE hr_payout_vouchers v SET confirmed_by=e.username
            FROM hr_employees e
            WHERE v.employee_id=e.id AND v.confirmed_at IS NOT NULL AND v.confirmed_by='';
            UPDATE hr_payout_vouchers
            SET approved_at=paid_at
            WHERE status='Paid' AND approved_at IS NULL;
            UPDATE hr_payout_vouchers
            SET completed_at=paid_at, completed_by=approved_by
            WHERE status='Paid' AND completed_at IS NULL;
            UPDATE hr_payout_vouchers
            SET cancelled_at=updated_at
            WHERE status='Cancelled' AND cancelled_at IS NULL;

            -- Nhật ký riêng của phiếu chi: ứng dụng chỉ INSERT, không có API sửa/xóa. before_data/after_data
            -- là snapshot nghiệp vụ (không bao giờ chứa mã QR), nên lịch sử vẫn đọc được khi schema phiếu đổi.
            CREATE TABLE IF NOT EXISTS hr_payout_voucher_events (
                id uuid PRIMARY KEY,
                voucher_id uuid NOT NULL,
                action varchar(48) NOT NULL,
                actor_username varchar(128) NOT NULL DEFAULT '',
                before_status varchar(32) NULL,
                after_status varchar(32) NULL,
                note text NOT NULL DEFAULT '',
                before_data jsonb NULL,
                after_data jsonb NULL,
                occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            -- Không FK có chủ ý: lịch sử tài chính phải còn lại nếu hồ sơ nhân viên/chứng từ nguồn bị xóa.
            ALTER TABLE hr_payout_voucher_events
                DROP CONSTRAINT IF EXISTS hr_payout_voucher_events_voucher_id_fkey;
            CREATE INDEX IF NOT EXISTS ix_hr_payout_voucher_events_voucher
                ON hr_payout_voucher_events (voucher_id, occurred_at, id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_payout_voucher_events_lifecycle
                ON hr_payout_voucher_events (voucher_id, action)
                WHERE action IN ('created','recipient_confirmed','approved','rejected','cancelled','completed');

            -- Chặn sửa/xóa ở tầng DB, không chỉ ở API. Muốn đính chính phải thêm sự kiện mới.
            CREATE OR REPLACE FUNCTION prevent_hr_payout_voucher_event_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'hr_payout_voucher_events is append-only';
            END;
            $fn$;
            DROP TRIGGER IF EXISTS trg_hr_payout_voucher_events_immutable ON hr_payout_voucher_events;
            CREATE TRIGGER trg_hr_payout_voucher_events_immutable
                BEFORE UPDATE OR DELETE ON hr_payout_voucher_events
                FOR EACH ROW EXECUTE FUNCTION prevent_hr_payout_voucher_event_mutation();

            -- Dựng lịch sử tối thiểu cho phiếu cũ. ID xác định từ voucher/action giúp migration chạy lặp an toàn.
            INSERT INTO hr_payout_voucher_events
                (id, voucher_id, action, actor_username, before_status, after_status, note,
                 before_data, after_data, occurred_at)
            SELECT md5(v.id::text || ':created')::uuid, v.id, 'created', v.created_by, NULL,
                   CASE WHEN v.requires_recipient_confirmation THEN 'AwaitingScan' ELSE 'AwaitingApproval' END,
                   v.reason, NULL,
                   jsonb_build_object('status', CASE WHEN v.requires_recipient_confirmation THEN 'AwaitingScan' ELSE 'AwaitingApproval' END),
                   v.created_at
            FROM hr_payout_vouchers v
            WHERE NOT EXISTS (SELECT 1 FROM hr_payout_voucher_events e
                              WHERE e.voucher_id=v.id AND e.action='created');
            INSERT INTO hr_payout_voucher_events
                (id, voucher_id, action, actor_username, before_status, after_status, note,
                 before_data, after_data, occurred_at)
            SELECT md5(v.id::text || ':recipient_confirmed')::uuid, v.id, 'recipient_confirmed',
                   v.confirmed_by, 'AwaitingScan', 'Confirmed', '',
                   jsonb_build_object('status','AwaitingScan'), jsonb_build_object('status','Confirmed'), v.confirmed_at
            FROM hr_payout_vouchers v
            WHERE v.confirmed_at IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM hr_payout_voucher_events e
                              WHERE e.voucher_id=v.id AND e.action='recipient_confirmed');
            INSERT INTO hr_payout_voucher_events
                (id, voucher_id, action, actor_username, before_status, after_status, note,
                 before_data, after_data, occurred_at)
            SELECT md5(v.id::text || ':approved')::uuid, v.id, 'approved', v.approved_by,
                   CASE WHEN v.confirmed_at IS NULL THEN 'AwaitingApproval' ELSE 'Confirmed' END,
                   'Approved', '',
                   jsonb_build_object('status', CASE WHEN v.confirmed_at IS NULL THEN 'AwaitingApproval' ELSE 'Confirmed' END),
                   jsonb_build_object('status','Approved'), v.approved_at
            FROM hr_payout_vouchers v
            WHERE v.approved_at IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM hr_payout_voucher_events e
                              WHERE e.voucher_id=v.id AND e.action='approved');
            INSERT INTO hr_payout_voucher_events
                (id, voucher_id, action, actor_username, before_status, after_status, note,
                 before_data, after_data, occurred_at)
            SELECT md5(v.id::text || ':completed')::uuid, v.id, 'completed', v.completed_by,
                   'Approved', 'Paid', '', jsonb_build_object('status','Approved'),
                   jsonb_build_object('status','Paid'), v.completed_at
            FROM hr_payout_vouchers v
            WHERE v.completed_at IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM hr_payout_voucher_events e
                              WHERE e.voucher_id=v.id AND e.action='completed');
            INSERT INTO hr_payout_voucher_events
                (id, voucher_id, action, actor_username, before_status, after_status, note,
                 before_data, after_data, occurred_at)
            SELECT md5(v.id::text || ':cancelled')::uuid, v.id, 'cancelled', v.cancelled_by,
                   NULL, 'Cancelled', v.cancel_reason, NULL, jsonb_build_object('status','Cancelled'), v.cancelled_at
            FROM hr_payout_vouchers v
            WHERE v.cancelled_at IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM hr_payout_voucher_events e
                              WHERE e.voucher_id=v.id AND e.action='cancelled');
            CREATE INDEX IF NOT EXISTS ix_hr_payout_vouchers_emp ON hr_payout_vouchers (employee_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_hr_payout_vouchers_status ON hr_payout_vouchers (status, created_at DESC);
            -- Sổ chi luôn xem theo tháng/mới nhất trước mà KHÔNG lọc nhân viên hay trạng thái, nên cần
            -- index riêng cho thời gian; đo trên 60k phiếu: seq scan 23ms → index 2ms.
            CREATE INDEX IF NOT EXISTS ix_hr_payout_vouchers_time ON hr_payout_vouchers (created_at DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_payout_vouchers_qr ON hr_payout_vouchers (qr_code)
                WHERE qr_code <> '';
            -- Một chứng từ gốc (khoản hoàn / phiếu lương) chỉ đẻ ra đúng một phiếu chi còn hiệu lực.
            DROP INDEX IF EXISTS ux_hr_payout_vouchers_source;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_payout_vouchers_source_v2
                ON hr_payout_vouchers (source_kind, source_id)
                WHERE source_id IS NOT NULL AND status NOT IN ('Cancelled','Rejected');
            """).ExecuteNonQueryAsync(ct);

        await SeedCategories(conn, ct);
    }

    /// <summary>Danh mục mặc định để trang chi dùng được ngay; quản trị vẫn tự thêm/sửa/ẩn được sau đó.</summary>
    private static async Task SeedCategories(NpgsqlConnection conn, CancellationToken ct)
    {
        (string Code, string Name, bool System, int Sort)[] seed =
        {
            (CategorySalary, "Lương", true, 10),
            (CategoryPenaltyRefund, "Hoàn tiền phạt", true, 20),
            ("supplies", "Mua vật dụng", false, 30),
            ("fuel", "Nhiên liệu (dầu/xăng)", false, 40),
            ("advance", "Tạm ứng", false, 50),
            ("travel", "Công tác phí", false, 60),
            ("other", "Khác", false, 90),
        };
        foreach (var (code, name, system, sort) in seed)
            await conn.Cmd("""
                INSERT INTO hr_payout_categories (id, code, name, is_system, sort_order)
                VALUES (@id, @code, @name, @sys, @sort)
                ON CONFLICT (code) DO NOTHING
                """)
                .With("@id", Guid.NewGuid()).With("@code", code).With("@name", name)
                .With("@sys", system).With("@sort", sort)
                .ExecuteNonQueryAsync(ct);
    }

    // ---------------- Quyền ----------------

    /// <summary>
    /// Giữ tên hàm để tương thích AuditEndpoints: người được xem phần tiền phải có payout.read VÀ hồ sơ
    /// đang hoạt động thuộc phòng kế toán. Không kiểm tên role để Kế toán trưởng/Thủ quỹ kế thừa đúng matrix.
    /// </summary>
    public static async Task<bool> IsCashierAsync(NpgsqlConnection conn, ClaimsPrincipal u)
    {
        if (!u.Can(Permissions.PayoutRead)) return false;
        return await IsAccountingDepartmentMemberAsync(conn, u);
    }

    private static async Task<bool> IsAccountingDepartmentMemberAsync(NpgsqlConnection conn, ClaimsPrincipal u)
    {
        var v = await conn.Cmd("""
            SELECT EXISTS(
                SELECT 1 FROM hr_employees e
                JOIN hr_departments d ON d.id = e.department_id
                WHERE lower(e.username) = lower(@u) AND e.status='Active' AND d.is_accounting = true
            )
            """).With("@u", u.Username()).ExecuteScalarAsync();
        return v is bool b && b;
    }

    public static void MapPayoutVouchers(this WebApplication app)
    {
        var g = app.MapGroup("/api/payout-vouchers").RequireAuthorization();

        // ---------------- Danh mục loại chi ----------------

        // Ai cũng đọc được (app của nhân viên cần tên loại chi để hiển thị); mặc định chỉ loại đang bật.
        g.MapGet("/categories", async (Database db, bool? all) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<object>();
            await using var r = await conn.Cmd($"""
                SELECT id, code, name, description, is_active, is_system, sort_order
                FROM hr_payout_categories
                {(all == true ? "" : "WHERE is_active = TRUE")}
                ORDER BY sort_order, name
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    code = r.Str("code"),
                    name = r.Str("name"),
                    description = r.Str("description"),
                    isActive = r.Bool("is_active"),
                    isSystem = r.Bool("is_system"),
                    sortOrder = r.Int("sort_order"),
                });
            return Results.Ok(list);
        });

        g.MapPost("/categories", async (SaveCategoryReq req, ClaimsPrincipal u, Database db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên loại chi." });
            var code = NormCode(req.Code, name);
            if (code.Length == 0) return Results.BadRequest(new { message = "Mã loại chi không hợp lệ (chỉ chữ thường, số và dấu gạch)." });

            await using var conn = await db.OpenAsync();
            var dup = await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_payout_categories WHERE code=@c)")
                .With("@c", code).ExecuteScalarAsync();
            if (dup is true) return Results.BadRequest(new { message = $"Mã loại chi \"{code}\" đã tồn tại." });

            var id = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_payout_categories (id, code, name, description, is_active, sort_order)
                VALUES (@id, @code, @name, @desc, @active, @sort)
                """)
                .With("@id", id).With("@code", code).With("@name", name)
                .With("@desc", (req.Description ?? "").Trim()).With("@active", req.IsActive)
                .With("@sort", NormSort(req.SortOrder)).ExecuteNonQueryAsync();
            await Signal(db, u, "Thêm loại chi", code);
            return Results.Ok(new { id, code });
        }).RequirePermission(Permissions.SystemSettingsManage);

        g.MapPut("/categories/{id:guid}", async (Guid id, SaveCategoryReq req, ClaimsPrincipal u, Database db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên loại chi." });

            await using var conn = await db.OpenAsync();
            // Danh mục lõi được đổi tên/mô tả cho hợp cách gọi của công ty, nhưng mã và trạng thái bật
            // phải giữ nguyên vì luồng tự sinh phiếu (lương, hoàn phạt) tra theo mã.
            var isSystem = await conn.Cmd("SELECT is_system FROM hr_payout_categories WHERE id=@id")
                .With("@id", id).ExecuteScalarAsync();
            if (isSystem is null) return Results.NotFound();
            var system = isSystem is true;

            var n = conn.Cmd($"""
                UPDATE hr_payout_categories SET name=@name, description=@desc, sort_order=@sort
                    {(system ? "" : ", is_active=@active")}
                    , updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """)
                .With("@id", id).With("@name", name).With("@desc", (req.Description ?? "").Trim())
                .With("@sort", NormSort(req.SortOrder));
            if (!system) n.With("@active", req.IsActive);
            if (await n.ExecuteNonQueryAsync() == 0) return Results.NotFound();

            await Signal(db, u, "Cập nhật loại chi", id.ToString());
            return Results.NoContent();
        }).RequirePermission(Permissions.SystemSettingsManage);

        g.MapDelete("/categories/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var isSystem = await conn.Cmd("SELECT is_system FROM hr_payout_categories WHERE id=@id")
                .With("@id", id).ExecuteScalarAsync();
            if (isSystem is null) return Results.NotFound();
            if (isSystem is true)
                return Results.BadRequest(new { message = "Loại chi của hệ thống không thể xóa." });

            // Xóa danh mục đang có phiếu sẽ làm sổ chi mất tên loại → chỉ cho ẩn để giữ nguyên lịch sử.
            var used = await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_payout_vouchers WHERE category_id=@id)")
                .With("@id", id).ExecuteScalarAsync();
            if (used is true)
                return Results.BadRequest(new { message = "Loại chi đã có phiếu nên không xóa được — hãy tắt (ẩn) loại này." });

            await conn.Cmd("DELETE FROM hr_payout_categories WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            await Signal(db, u, "Xóa loại chi", id.ToString());
            return Results.NoContent();
        }).RequirePermission(Permissions.SystemSettingsManage);

        // ---------------- Nguồn để lập phiếu ----------------

        // Danh sách người có thể nhận tiền. Kế toán KHÔNG phải HR nên /api/hr/employees bị lọc theo phạm vi
        // phòng ban (thường chỉ thấy chính mình) — mà tiền thì chi cho người ở mọi phòng (kho đi mua dầu,
        // ai đó mua thay khi kho bận). Vì vậy có danh sách riêng, chỉ lộ ĐÚNG những trường cần cho ô chọn.
        g.MapGet("/recipients", async (ClaimsPrincipal u, Database db, string? search) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();

            var cmd = conn.Cmd($"""
                SELECT e.id, e.employee_code, e.full_name, COALESCE(d.name, '') AS department_name
                FROM hr_employees e LEFT JOIN hr_departments d ON d.id = e.department_id
                WHERE e.status = 'Active'
                  {(string.IsNullOrWhiteSpace(search) ? "" : "AND (e.full_name ILIKE @s OR e.employee_code ILIKE @s)")}
                ORDER BY e.full_name
                LIMIT 500
                """);
            if (!string.IsNullOrWhiteSpace(search)) cmd.With("@s", $"%{search.Trim()}%");

            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    employeeCode = r.Str("employee_code"),
                    fullName = r.Str("full_name"),
                    departmentName = r.Str("department_name"),
                });
            return Results.Ok(list);
        }).RequirePermission(Permissions.PayoutCreate);

        // Các khoản hoàn tiền phạt (khiếu nại đã được duyệt) đang chờ kế toán chi — kế toán chọn một
        // khoản là ra đúng số tiền phải chi, không phải gõ tay.
        g.MapGet("/sources/refunds", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();

            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT r.id, r.refund_no, r.employee_id, r.penalty_no, r.appeal_request_no, r.amount,
                       r.reason, r.created_at, e.full_name AS emp_name, e.employee_code
                FROM hr_penalty_refunds r JOIN hr_employees e ON e.id = r.employee_id
                WHERE r.status = 'PendingAccounting'
                  AND NOT EXISTS (SELECT 1 FROM hr_payout_vouchers v
                                  WHERE v.source_kind = 'refund' AND v.source_id = r.id
                                    AND v.status NOT IN ('Cancelled','Rejected'))
                ORDER BY r.created_at
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    refundNo = r.Str("refund_no"),
                    employeeId = r.Guid("employee_id"),
                    employeeName = r.Str("emp_name"),
                    employeeCode = r.Str("employee_code"),
                    penaltyNo = r.Str("penalty_no"),
                    appealRequestNo = r.Str("appeal_request_no"),
                    amount = r.Dec("amount"),
                    reason = r.Str("reason"),
                    createdAt = r.Dt("created_at"),
                });
            return Results.Ok(list);
        }).RequirePermission(Permissions.PayoutCreate);

        // ---------------- Sổ phiếu chi ----------------

        // scope: mine (mặc định – phiếu của tôi) | all (kế toán + admin – toàn bộ sổ chi).
        g.MapGet("/", async (ClaimsPrincipal u, Database db, string? scope, string? status,
            Guid? categoryId, Guid? employeeId, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var canExposeQr = u.Can(Permissions.PayoutCreate)
                              && await IsAccountingDepartmentMemberAsync(conn, u);
            var canSeeAll = u.Can(Permissions.PayoutRead);
            scope ??= "all";

            var where = new List<string>();
            var cmdParams = new List<(string, object)>();
            if (scope == "all" && canSeeAll)
            {
                if (categoryId is { } c) { where.Add("v.category_id=@cat"); cmdParams.Add(("@cat", c)); }
                if (employeeId is { } e) { where.Add("v.employee_id=@emp"); cmdParams.Add(("@emp", e)); }
            }
            else
            {
                var myId = await HrEndpoints.EnsureEmployeeForUser(conn, u.Username());
                where.Add("v.employee_id=@myId");
                cmdParams.Add(("@myId", myId));
            }
            if (!string.IsNullOrWhiteSpace(status)) { where.Add("v.status=@st"); cmdParams.Add(("@st", status.Trim())); }
            // So sánh theo KHOẢNG chứ không to_char(created_at)=... : hàm bọc quanh cột làm index vô dụng
            // (đo trên 60k phiếu: seq scan 23ms → index 2ms, và khoảng cách này giãn ra theo số phiếu).
            if (TryMonthRange(month, out var monthStart, out var monthEnd))
            {
                where.Add("v.created_at >= @monthStart AND v.created_at < @monthEnd");
                cmdParams.Add(("@monthStart", monthStart));
                cmdParams.Add(("@monthEnd", monthEnd));
            }

            var cmd = conn.Cmd($"""
                {VoucherSelect}
                {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
                ORDER BY v.created_at DESC
                LIMIT 500
                """);
            foreach (var (n, v) in cmdParams) cmd.With(n, v);

            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(ReadVoucher(r, canExposeQr));
            return Results.Ok(list);
        }).RequirePermission(Permissions.PayoutRead);

        // Timeline bất biến của một phiếu. Không nhận scope/trạng thái từ client; quyền payout.read ở
        // policy là cửa duy nhất, và API không hề có PUT/DELETE cho bảng sự kiện.
        g.MapGet("/{id:guid}/history", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var exists = await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_payout_vouchers WHERE id=@id)")
                .With("@id", id).ExecuteScalarAsync();
            if (exists is not true) return Results.NotFound();

            var events = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT e.id, e.action, e.actor_username,
                       COALESCE(NULLIF(u.full_name,''), e.actor_username) AS actor_name,
                       e.before_status, e.after_status, e.note,
                       e.before_data::text AS before_data, e.after_data::text AS after_data,
                       e.occurred_at
                FROM hr_payout_voucher_events e
                LEFT JOIN app_users u ON lower(u.username)=lower(e.actor_username) AND u.is_deleted=FALSE
                WHERE e.voucher_id=@id
                ORDER BY e.occurred_at, e.id
                """).With("@id", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
                events.Add(new
                {
                    id = r.Guid("id"),
                    action = r.Str("action"),
                    actor = r.Str("actor_username"),
                    actorName = r.Str("actor_name"),
                    beforeStatus = r.IsDBNull(r.GetOrdinal("before_status")) ? null : r.Str("before_status"),
                    afterStatus = r.IsDBNull(r.GetOrdinal("after_status")) ? null : r.Str("after_status"),
                    note = r.Str("note"),
                    before = ReadJsonSnapshot(r, "before_data"),
                    after = ReadJsonSnapshot(r, "after_data"),
                    occurredAt = r.Dt("occurred_at"),
                });
            return Results.Ok(events);
        }).RequirePermission(Permissions.PayoutRead);

        // Tổng hợp sổ chi theo loại cho một tháng (yyyy-MM) — phần "chi tiết các khoản chi" của trang.
        g.MapGet("/summary", async (ClaimsPrincipal u, Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var period = string.IsNullOrWhiteSpace(month)
                ? DateTime.Now.ToString("yyyy-MM")
                : month.Trim();
            // Kỳ không hợp lệ → rơi về tháng hiện tại thay vì quét sạch bảng.
            if (!TryMonthRange(period, out var from, out var to))
                TryMonthRange(DateTime.Now.ToString("yyyy-MM"), out from, out to);

            var byCategory = new List<object>();
            decimal paid = 0, pending = 0;
            await using (var r = await conn.Cmd("""
                SELECT COALESCE(c.name, 'Không rõ') AS cat_name, c.id AS cat_id,
                       COUNT(*) AS cnt,
                       SUM(CASE WHEN v.status='Paid' THEN v.amount ELSE 0 END) AS paid_amount,
                       SUM(CASE WHEN v.status IN ('AwaitingScan','AwaitingApproval','Confirmed','Approved') THEN v.amount ELSE 0 END) AS pending_amount
                FROM hr_payout_vouchers v LEFT JOIN hr_payout_categories c ON c.id = v.category_id
                WHERE v.created_at >= @from AND v.created_at < @to AND v.status <> 'Cancelled'
                GROUP BY c.id, c.name, c.sort_order
                ORDER BY c.sort_order, c.name
                """).With("@from", from).With("@to", to).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var p = r.Dec("paid_amount");
                    var q = r.Dec("pending_amount");
                    paid += p;
                    pending += q;
                    byCategory.Add(new
                    {
                        categoryId = r.IsDBNull(r.GetOrdinal("cat_id")) ? (Guid?)null : r.Guid("cat_id"),
                        categoryName = r.Str("cat_name"),
                        count = r.Int("cnt"),
                        paidAmount = p,
                        pendingAmount = q,
                    });
                }
            }
            return Results.Ok(new { month = period, totalPaid = paid, totalPending = pending, byCategory });
        }).RequirePermission(Permissions.PayoutRead);

        // ---------------- Lập / xác nhận / duyệt / thực chi ----------------

        g.MapPost("/", async (CreateVoucherReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u))
                return Results.Json(new { message = "Chỉ nhân viên thuộc phòng kế toán và có quyền lập phiếu chi mới được thao tác." }, statusCode: 403);
            await using var tx = await conn.BeginTransactionAsync();

            var kind = (req.SourceKind ?? SourceManual).Trim();
            var reason = (req.Reason ?? "").Trim();
            var requiresConfirmation = req.RequiresRecipientConfirmation ?? true;
            Guid employeeId;
            decimal amount;
            Guid? sourceId = null;
            var sourceNo = "";
            Guid categoryId;

            if (kind == SourceRefund)
            {
                // Số tiền và người nhận LẤY TỪ KHOẢN HOÀN, không tin số client gửi lên.
                if (req.SourceId is not { } rid) return Results.BadRequest(new { message = "Thiếu khoản hoàn cần chi." });
                await using var rr = await conn.Cmd("""
                    SELECT r.employee_id, r.amount, r.refund_no, r.penalty_no, r.status
                    FROM hr_penalty_refunds r WHERE r.id=@id FOR UPDATE
                    """).With("@id", rid).ExecuteReaderAsync();
                if (!await rr.ReadAsync()) return Results.BadRequest(new { message = "Khoản hoàn không tồn tại." });
                if (rr.Str("status") != "PendingAccounting")
                    return Results.BadRequest(new { message = "Khoản hoàn này đã được xử lý." });
                employeeId = rr.Guid("employee_id");
                amount = rr.Dec("amount");
                sourceId = rid;
                sourceNo = rr.Str("refund_no");
                if (reason.Length == 0) reason = $"Hoàn tiền phạt {rr.Str("penalty_no")} theo khiếu nại được duyệt";
                await rr.CloseAsync();
                categoryId = await CategoryIdByCode(conn, CategoryPenaltyRefund);
                // Khoản hoàn/lương luôn cần chính người nhận ký; client không được hạ chốt kiểm soát này.
                requiresConfirmation = true;
            }
            else
            {
                kind = SourceManual;
                if (req.EmployeeId == Guid.Empty) return Results.BadRequest(new { message = "Vui lòng chọn người nhận tiền." });
                if (req.Amount <= 0) return Results.BadRequest(new { message = "Số tiền chi phải lớn hơn 0." });
                if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập nội dung chi." });
                if (req.CategoryId == Guid.Empty) return Results.BadRequest(new { message = "Vui lòng chọn loại chi." });
                var okCat = await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_payout_categories WHERE id=@id AND is_active=TRUE)")
                    .With("@id", req.CategoryId).ExecuteScalarAsync();
                if (okCat is not true) return Results.BadRequest(new { message = "Loại chi không hợp lệ hoặc đã bị tắt." });
                employeeId = req.EmployeeId;
                amount = decimal.Round(req.Amount, 2);
                categoryId = req.CategoryId;
                var activeRecipient = await conn.Cmd("SELECT EXISTS(SELECT 1 FROM hr_employees WHERE id=@id AND status='Active')")
                    .With("@id", employeeId).ExecuteScalarAsync();
                if (activeRecipient is not true)
                    return Results.BadRequest(new { message = "Người nhận không tồn tại hoặc đã nghỉ việc." });
            }

            var (id, no) = await InsertVoucher(conn, categoryId, employeeId, amount, kind, sourceId, sourceNo,
                reason, (req.Note ?? "").Trim(), u.Username(), requiresConfirmation);

            // Lập phiếu chi cho khoản hoàn = chốt luôn hình thức "chi tiền mặt" cho khoản đó.
            if (kind == SourceRefund && sourceId is { } refundId)
                await conn.Cmd("""
                    UPDATE hr_penalty_refunds SET status='Approved', payout_method='cash', approved_by=@by,
                        decided_at=CURRENT_TIMESTAMP
                    WHERE id=@id AND status='PendingAccounting'
                    """).With("@id", refundId).With("@by", u.Username()).ExecuteNonQueryAsync();

            await tx.CommitAsync();
            await Signal(db, u, "Lập phiếu chi", no);
            return Results.Ok(new { id, voucherNo = no });
        }).RequirePermission(Permissions.PayoutCreate);

        // Tạo lại mã QR khi mã cũ hết hạn (người nhận tới muộn) — chỉ khi phiếu còn chờ ký nhận.
        g.MapPost("/{id:guid}/qr", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadVoucherStateAsync(conn, id, forUpdate: true);
            if (before is null) return Results.NotFound();
            if (before.Status != StatusAwaitingScan || !before.RequiresRecipientConfirmation)
                return Results.BadRequest(new { message = "Phiếu không còn chờ người nhận quét mã." });
            var expires = DateTime.UtcNow.Add(QrLifetime);
            var code = NewQrCode();
            var n = await conn.Cmd("""
                UPDATE hr_payout_vouchers SET qr_code=@code, qr_expires_at=@exp, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@st
                """).With("@id", id).With("@code", code).With("@exp", expires)
                .With("@st", StatusAwaitingScan).ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Phiếu không còn chờ người nhận quét mã." });
            var after = await LoadVoucherStateAsync(conn, id);
            await AppendEventAsync(conn, id, EventQrRegenerated, u.Username(),
                $"Mã xác nhận mới có hiệu lực đến {expires:O}.", before, after);
            await tx.CommitAsync();
            await Signal(db, u, "Tạo lại mã xác nhận phiếu chi", before.VoucherNo);
            return Results.Ok(new { qrValue = QrPrefix + code, qrExpiresAt = expires });
        }).RequirePermission(Permissions.PayoutCreate);

        // Kế toán trưởng duyệt về mặt thẩm quyền. Đây CHƯA phải thực chi; thủ quỹ hoàn tất ở /complete.
        g.MapPost("/{id:guid}/approve", async (Guid id, TransitionVoucherReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadVoucherStateAsync(conn, id, forUpdate: true);
            if (before is null) return Results.NotFound();
            if (before.Status == StatusAwaitingScan)
                return Results.BadRequest(new { message = "Người nhận chưa quét mã QR xác nhận đã nhận tiền." });
            if (before.Status is not (StatusConfirmed or StatusAwaitingApproval))
                return Results.BadRequest(new { message = "Phiếu này không ở trạng thái chờ duyệt chi." });

            await conn.Cmd("""
                UPDATE hr_payout_vouchers SET status=@approved, approved_by=@by, approved_at=CURRENT_TIMESTAMP,
                    qr_code='', qr_expires_at=NULL, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@before
                """).With("@id", id).With("@approved", StatusApproved).With("@before", before.Status)
                .With("@by", u.Username()).ExecuteNonQueryAsync();
            var after = await LoadVoucherStateAsync(conn, id);
            await AppendEventAsync(conn, id, EventApproved, u.Username(), (req.Note ?? "").Trim(), before, after,
                after?.ApprovedAt);
            await tx.CommitAsync();
            await Signal(db, u, "Duyệt phiếu chi", before.VoucherNo);
            return Results.NoContent();
        }).RequirePermission(Permissions.PayoutApprove);

        // Thủ quỹ xác nhận tiền đã thực chi. Trạng thái client gửi lên bị bỏ qua; server khóa hàng và chỉ
        // chấp nhận đúng Approved → Paid.
        g.MapPost("/{id:guid}/complete", async (Guid id, TransitionVoucherReq req, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadVoucherStateAsync(conn, id, forUpdate: true);
            if (before is null) return Results.NotFound();
            if (before.Status != StatusApproved)
                return Results.BadRequest(new { message = "Phiếu chưa được duyệt hoặc đã hoàn tất." });

            await conn.Cmd("""
                UPDATE hr_payout_vouchers
                SET status=@paid, paid_at=CURRENT_TIMESTAMP, completed_at=CURRENT_TIMESTAMP,
                    completed_by=@by, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@approved
                """).With("@id", id).With("@paid", StatusPaid).With("@approved", StatusApproved)
                .With("@by", u.Username()).ExecuteNonQueryAsync();

            if (before.SourceKind == SourceRefund && before.SourceId is { } refundId)
                await conn.Cmd("UPDATE hr_penalty_refunds SET status='Paid', decided_at=CURRENT_TIMESTAMP WHERE id=@id AND status='Approved'")
                    .With("@id", refundId).ExecuteNonQueryAsync();

            var after = await LoadVoucherStateAsync(conn, id);
            await AppendEventAsync(conn, id, EventCompleted, u.Username(), (req.Note ?? "").Trim(), before, after,
                after?.CompletedAt);
            await tx.CommitAsync();
            await Signal(db, u, "Hoàn tất chi phiếu chi", before.VoucherNo);
            return Results.NoContent();
        }).RequirePermission(Permissions.PayoutPay);

        // Người duyệt có thể từ chối từ bất kỳ trạng thái tiền duyệt nào; lý do là bắt buộc để tra soát.
        g.MapPost("/{id:guid}/reject", async (Guid id, CancelVoucherReq req, ClaimsPrincipal u, Database db) =>
        {
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do từ chối." });
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadVoucherStateAsync(conn, id, forUpdate: true);
            if (before is null) return Results.NotFound();
            if (before.Status is not (StatusAwaitingScan or StatusAwaitingApproval or StatusConfirmed))
                return Results.BadRequest(new { message = "Phiếu không còn ở trạng thái có thể từ chối." });

            await conn.Cmd("""
                UPDATE hr_payout_vouchers
                SET status=@rejected, rejected_by=@by, rejected_at=CURRENT_TIMESTAMP, reject_reason=@reason,
                    qr_code='', qr_expires_at=NULL, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@before
                """).With("@id", id).With("@rejected", StatusRejected).With("@before", before.Status)
                .With("@by", u.Username()).With("@reason", reason).ExecuteNonQueryAsync();
            await ReturnRefundToQueueAsync(conn, before);
            var after = await LoadVoucherStateAsync(conn, id);
            await AppendEventAsync(conn, id, EventRejected, u.Username(), reason, before, after, after?.RejectedAt);
            await tx.CommitAsync();
            await Signal(db, u, "Từ chối phiếu chi", before.VoucherNo);
            return Results.NoContent();
        }).RequirePermission(Permissions.PayoutApprove);

        g.MapPost("/{id:guid}/cancel", async (Guid id, CancelVoucherReq req, ClaimsPrincipal u, Database db) =>
        {
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do hủy phiếu." });
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingDepartmentMemberAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadVoucherStateAsync(conn, id, forUpdate: true);
            if (before is null) return Results.NotFound();
            var beforeApproval = before.Status is StatusAwaitingScan or StatusAwaitingApproval or StatusConfirmed;
            var approvedWithAuthority = before.Status == StatusApproved && u.Can(Permissions.PayoutApprove);
            if (!beforeApproval && !approvedWithAuthority)
                return Results.BadRequest(new { message = "Phiếu đã hoàn tất hoặc đã kết thúc nên không hủy được." });

            await conn.Cmd("""
                UPDATE hr_payout_vouchers
                SET status=@cancelled, cancel_reason=@reason, cancelled_by=@by,
                    cancelled_at=CURRENT_TIMESTAMP, qr_code='', qr_expires_at=NULL, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@before
                """).With("@id", id).With("@cancelled", StatusCancelled).With("@before", before.Status)
                .With("@reason", reason).With("@by", u.Username()).ExecuteNonQueryAsync();
            await ReturnRefundToQueueAsync(conn, before);
            var after = await LoadVoucherStateAsync(conn, id);
            await AppendEventAsync(conn, id, EventCancelled, u.Username(), reason, before, after, after?.CancelledAt);
            await tx.CommitAsync();
            await Signal(db, u, "Hủy phiếu chi", before.VoucherNo);
            return Results.NoContent();
        }).RequireAnyPermission(Permissions.PayoutCreate, Permissions.PayoutApprove);
    }

    // ---------------- Dùng chung với module khác ----------------

    /// <summary>
    /// Sinh (hoặc cập nhật) phiếu chi lương khi một phiếu lương được PHÁT HÀNH. Số tiền luôn bám thực lĩnh;
    /// phiếu đã được người nhận ký nhận hoặc đã duyệt chi thì giữ nguyên, không bị ghi đè.
    /// </summary>
    public static async Task SyncPayslipVoucherAsync(NpgsqlConnection conn, Guid payslipId, Guid employeeId,
        string period, decimal netPay, string createdBy)
    {
        if (netPay <= 0) return;
        var existing = await conn.Cmd("""
            SELECT id, status FROM hr_payout_vouchers
            WHERE source_kind=@kind AND source_id=@id AND status NOT IN (@cancelled,@rejected)
            """).With("@kind", SourcePayslip).With("@id", payslipId).With("@cancelled", StatusCancelled)
            .With("@rejected", StatusRejected)
            .ExecuteReaderAsync();
        string? status = null;
        Guid voucherId = default;
        await using (existing)
            if (await existing.ReadAsync()) { voucherId = existing.Guid("id"); status = existing.Str("status"); }

        if (status == StatusAwaitingScan)
        {
            var before = await LoadVoucherStateAsync(conn, voucherId);
            await conn.Cmd("""
                UPDATE hr_payout_vouchers SET amount=@amt, updated_at=CURRENT_TIMESTAMP WHERE id=@id
                """).With("@id", voucherId).With("@amt", netPay).ExecuteNonQueryAsync();
            if (before is not null && before.Amount != netPay)
            {
                var after = await LoadVoucherStateAsync(conn, voucherId);
                await AppendEventAsync(conn, voucherId, EventAmountUpdated, createdBy,
                    $"Cập nhật thực lĩnh kỳ {period}: {before.Amount} → {netPay}.", before, after);
            }
            return;
        }
        if (status is not null) return; // đã ký nhận / đã chi → không đụng vào nữa

        var categoryId = await CategoryIdByCode(conn, CategorySalary);
        await InsertVoucher(conn, categoryId, employeeId, netPay, SourcePayslip, payslipId, period,
            $"Lương kỳ {period}", "", createdBy, requiresRecipientConfirmation: true);
    }

    /// <summary>
    /// Payroll gọi helper này TRONG transaction của nó khi đưa phiếu lương về nháp. Phiếu chi chưa duyệt
    /// được hủy và ghi history cùng transaction; đã Approved/Paid thì chặn để không âm thầm đảo một khoản
    /// đã được cấp thẩm quyền duyệt hoặc đã thực chi. Helper không tự commit/rollback.
    /// </summary>
    public static async Task<PayslipVoucherCancelResult> CancelPayslipVoucherForUnpublishAsync(
        NpgsqlConnection conn, Guid payslipId, string actor, string reason = "Phiếu lương trả về nháp")
    {
        var raw = await conn.Cmd("""
            SELECT id FROM hr_payout_vouchers
            WHERE source_kind=@kind AND source_id=@source
            ORDER BY created_at DESC LIMIT 1
            """).With("@kind", SourcePayslip).With("@source", payslipId).ExecuteScalarAsync();
        if (raw is not Guid voucherId) return PayslipVoucherCancelResult.NotFound;

        var before = await LoadVoucherStateAsync(conn, voucherId, forUpdate: true);
        if (before is null || before.Status is StatusCancelled or StatusRejected)
            return PayslipVoucherCancelResult.NotFound;
        if (before.Status is StatusApproved or StatusPaid)
            return PayslipVoucherCancelResult.Blocked;
        if (before.Status is not (StatusAwaitingScan or StatusAwaitingApproval or StatusConfirmed))
            return PayslipVoucherCancelResult.Blocked;

        await conn.Cmd("""
            UPDATE hr_payout_vouchers
            SET status=@cancelled, cancel_reason=@reason, cancelled_by=@actor,
                cancelled_at=CURRENT_TIMESTAMP, qr_code='', qr_expires_at=NULL, updated_at=CURRENT_TIMESTAMP
            WHERE id=@id AND status=@before
            """).With("@id", voucherId).With("@cancelled", StatusCancelled).With("@before", before.Status)
            .With("@reason", reason).With("@actor", actor).ExecuteNonQueryAsync();
        var after = await LoadVoucherStateAsync(conn, voucherId);
        await AppendEventAsync(conn, voucherId, EventCancelled, actor, reason, before, after, after?.CancelledAt);
        return PayslipVoucherCancelResult.Cancelled;
    }

    public static bool LooksLikePayoutQr(string? value)
        => value?.Trim().StartsWith(QrPrefix, StringComparison.Ordinal) == true;

    public sealed record PayoutScanInfo(bool Ok, string Title, string Message, DateTime ExpiresAt,
        Guid VoucherId, string VoucherNo, decimal Amount, string CategoryName, string Reason);

    /// <summary>
    /// Tra phiếu theo mã QR cho người đang quét. Chỉ ĐÚNG người nhận ghi trên phiếu mới thấy nội dung —
    /// người khác quét chỉ nhận thông báo từ chối, không lộ số tiền hay tên ai.
    /// </summary>
    public static async Task<PayoutScanInfo> LookupForScanAsync(NpgsqlConnection conn, string qrValue, string username)
    {
        var fail = (string msg) => new PayoutScanInfo(false, "Không dùng được mã này", msg, default, default, "", 0, "", "");
        var code = qrValue.Trim()[QrPrefix.Length..];
        if (code.Length is 0 or > 64) return fail("Mã QR phiếu chi không hợp lệ.");

        await using var r = await conn.Cmd($"""
            SELECT v.id, v.voucher_no, v.amount, v.reason, v.status, v.qr_expires_at,
                   COALESCE(c.name, '') AS cat_name, e.username AS emp_username, e.full_name AS emp_name
            FROM hr_payout_vouchers v
            JOIN hr_employees e ON e.id = v.employee_id
            LEFT JOIN hr_payout_categories c ON c.id = v.category_id
            WHERE v.qr_code = @code
            """).With("@code", code).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return fail("Mã QR phiếu chi không tồn tại hoặc đã hết hiệu lực.");

        if (!string.Equals(r.Str("emp_username"), username, StringComparison.OrdinalIgnoreCase))
            return fail("Phiếu chi này không phải của bạn nên bạn không thể ký nhận thay.");
        var status = r.Str("status");
        if (status == StatusConfirmed) return fail("Bạn đã xác nhận nhận tiền cho phiếu này rồi.");
        if (status != StatusAwaitingScan) return fail("Phiếu chi này không còn chờ ký nhận.");
        var expires = r.DtNull("qr_expires_at");
        if (expires is null || expires.Value <= DateTime.UtcNow)
            return fail("Mã QR đã hết hạn — hãy nhờ kế toán tạo lại mã.");

        return new PayoutScanInfo(true, "Xác nhận đã nhận tiền?", "", expires.Value, r.Guid("id"),
            r.Str("voucher_no"), r.Dec("amount"), r.Str("cat_name"), r.Str("reason"));
    }

    /// <summary>Người nhận ký nhận: chuyển phiếu sang chờ kế toán duyệt chi. Trả về phiếu vừa ký (null nếu hỏng).</summary>
    public static async Task<(Guid VoucherId, Guid EmployeeId, string VoucherNo)?> ConfirmScanAsync(
        NpgsqlConnection conn, string qrValue, string username)
    {
        if (!LooksLikePayoutQr(qrValue)) return null;
        var code = qrValue.Trim()[QrPrefix.Length..];
        await using var r = await conn.Cmd("""
            WITH changed AS (
                UPDATE hr_payout_vouchers v SET status=@confirmed, confirmed_at=CURRENT_TIMESTAMP,
                    confirmed_by=@u, qr_code='', qr_expires_at=NULL, updated_at=CURRENT_TIMESTAMP
                FROM hr_employees e
                WHERE v.employee_id = e.id AND v.qr_code=@code AND v.status=@awaiting
                  AND v.requires_recipient_confirmation=TRUE
                  AND v.qr_expires_at > CURRENT_TIMESTAMP AND lower(e.username)=lower(@u)
                RETURNING v.id, v.employee_id, v.voucher_no, v.amount, v.reason, v.confirmed_at
            ), logged AS (
                INSERT INTO hr_payout_voucher_events
                    (id, voucher_id, action, actor_username, before_status, after_status, note,
                     before_data, after_data, occurred_at)
                SELECT @eventId, c.id, @action, @u, @awaiting, @confirmed, '',
                       jsonb_build_object('status', @awaiting),
                       jsonb_build_object('status', @confirmed, 'amount', c.amount,
                                          'reason', c.reason, 'confirmedAt', c.confirmed_at,
                                          'confirmedBy', @u),
                       c.confirmed_at
                FROM changed c
                RETURNING voucher_id
            )
            SELECT c.id, c.employee_id, c.voucher_no
            FROM changed c JOIN logged l ON l.voucher_id=c.id
            """).With("@code", code).With("@confirmed", StatusConfirmed).With("@awaiting", StatusAwaitingScan)
            .With("@action", EventRecipientConfirmed).With("@eventId", Guid.NewGuid())
            .With("@u", username).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.Guid("id"), r.Guid("employee_id"), r.Str("voucher_no"));
    }

    // SignalScanAsync đã bỏ: ConfirmScanAsync UPDATE hr_payout_vouchers nên trigger tự phát scope 'hr'
    // cho cả màn hình kế toán lẫn người nhận, không cần báo tay sau khi quét QR nữa.

    // ---------------- Trợ giúp ----------------

    private const string VoucherSelect = """
        SELECT v.id, v.voucher_no, v.category_id, v.employee_id, v.amount, v.source_kind, v.source_id,
               v.source_no, v.reason, v.note, v.status, v.qr_code, v.qr_expires_at, v.created_by,
               v.requires_recipient_confirmation, v.confirmed_at, v.confirmed_by,
               v.approved_by, v.approved_at, v.paid_at, v.completed_by, v.completed_at,
               v.rejected_by, v.rejected_at, v.reject_reason,
               v.cancelled_by, v.cancelled_at, v.cancel_reason, v.created_at,
               COALESCE(c.name, '') AS cat_name, COALESCE(c.code, '') AS cat_code,
               e.full_name AS emp_name, e.employee_code
        FROM hr_payout_vouchers v
        JOIN hr_employees e ON e.id = v.employee_id
        LEFT JOIN hr_payout_categories c ON c.id = v.category_id
        """;

    private static async Task<(Guid Id, string No)> InsertVoucher(NpgsqlConnection conn, Guid categoryId,
        Guid employeeId, decimal amount, string sourceKind, Guid? sourceId, string sourceNo, string reason,
        string note, string createdBy, bool requiresRecipientConfirmation = true)
    {
        var id = Guid.NewGuid();
        var no = $"PC{Convert.ToInt64(await conn.Cmd("SELECT nextval('hr_payout_voucher_seq')").ExecuteScalarAsync()):D5}";
        var status = requiresRecipientConfirmation ? StatusAwaitingScan : StatusAwaitingApproval;
        var qr = requiresRecipientConfirmation ? NewQrCode() : "";
        var expires = requiresRecipientConfirmation ? DateTime.UtcNow.Add(QrLifetime) : (DateTime?)null;
        await conn.Cmd("""
            INSERT INTO hr_payout_vouchers (id, voucher_no, category_id, employee_id, amount, source_kind,
                source_id, source_no, reason, note, status, qr_code, qr_expires_at, created_by,
                requires_recipient_confirmation)
            VALUES (@id, @no, @cat, @emp, @amt, @kind, @sid, @sno, @reason, @note, @st, @qr, @exp, @by, @confirm)
            """)
            .With("@id", id).With("@no", no).With("@cat", categoryId).With("@emp", employeeId)
            .With("@amt", amount).With("@kind", sourceKind)
            .With("@sid", (object?)sourceId ?? DBNull.Value).With("@sno", sourceNo)
            .With("@reason", reason).With("@note", note).With("@st", status)
            .With("@qr", qr).With("@exp", (object?)expires ?? DBNull.Value).With("@by", createdBy)
            .With("@confirm", requiresRecipientConfirmation)
            .ExecuteNonQueryAsync();
        var after = await LoadVoucherStateAsync(conn, id);
        await AppendEventAsync(conn, id, EventCreated, createdBy, note.Length > 0 ? note : reason,
            null, after, after?.CreatedAt);
        return (id, no);
    }

    private static async Task<Guid> CategoryIdByCode(NpgsqlConnection conn, string code)
    {
        var id = await conn.Cmd("SELECT id FROM hr_payout_categories WHERE code=@c").With("@c", code).ExecuteScalarAsync();
        if (id is Guid g) return g;
        // Danh mục lõi lỡ bị xóa tay dưới DB thì dựng lại để luồng tự sinh phiếu không gãy.
        var newId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO hr_payout_categories (id, code, name, is_system, sort_order) VALUES (@id, @c, @n, TRUE, 10)
            ON CONFLICT (code) DO NOTHING
            """).With("@id", newId).With("@c", code)
            .With("@n", code == CategorySalary ? "Lương" : "Hoàn tiền phạt").ExecuteNonQueryAsync();
        return await conn.Cmd("SELECT id FROM hr_payout_categories WHERE code=@c").With("@c", code)
            .ExecuteScalarAsync() is Guid g2 ? g2 : newId;
    }

    private sealed record VoucherState(
        Guid Id, string VoucherNo, Guid EmployeeId, decimal Amount, string SourceKind, Guid? SourceId,
        string SourceNo, string Reason, string Note, string Status, bool RequiresRecipientConfirmation,
        string CreatedBy, DateTime CreatedAt, string ConfirmedBy, DateTime? ConfirmedAt,
        string ApprovedBy, DateTime? ApprovedAt, string CompletedBy, DateTime? CompletedAt,
        string RejectedBy, DateTime? RejectedAt, string RejectReason,
        string CancelledBy, DateTime? CancelledAt, string CancelReason);

    private static async Task<VoucherState?> LoadVoucherStateAsync(NpgsqlConnection conn, Guid id,
        bool forUpdate = false)
    {
        await using var r = await conn.Cmd($"""
            SELECT id, voucher_no, employee_id, amount, source_kind, source_id, source_no, reason, note,
                   status, requires_recipient_confirmation, created_by, created_at,
                   confirmed_by, confirmed_at, approved_by, approved_at, completed_by, completed_at,
                   rejected_by, rejected_at, reject_reason, cancelled_by, cancelled_at, cancel_reason
            FROM hr_payout_vouchers WHERE id=@id {(forUpdate ? "FOR UPDATE" : "")}
            """).With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new VoucherState(
            r.Guid("id"), r.Str("voucher_no"), r.Guid("employee_id"), r.Dec("amount"),
            r.Str("source_kind"), r.IsDBNull(r.GetOrdinal("source_id")) ? null : r.Guid("source_id"),
            r.Str("source_no"), r.Str("reason"), r.Str("note"), r.Str("status"),
            r.Bool("requires_recipient_confirmation"), r.Str("created_by"), r.Dt("created_at"),
            r.Str("confirmed_by"), r.DtNull("confirmed_at"), r.Str("approved_by"), r.DtNull("approved_at"),
            r.Str("completed_by"), r.DtNull("completed_at"), r.Str("rejected_by"), r.DtNull("rejected_at"),
            r.Str("reject_reason"), r.Str("cancelled_by"), r.DtNull("cancelled_at"), r.Str("cancel_reason"));
    }

    private static object VoucherSnapshot(VoucherState s) => new
    {
        s.VoucherNo,
        s.EmployeeId,
        s.Amount,
        s.SourceKind,
        s.SourceNo,
        s.Reason,
        s.Note,
        s.Status,
        s.RequiresRecipientConfirmation,
        s.CreatedBy,
        s.CreatedAt,
        s.ConfirmedBy,
        s.ConfirmedAt,
        s.ApprovedBy,
        s.ApprovedAt,
        s.CompletedBy,
        s.CompletedAt,
        s.RejectedBy,
        s.RejectedAt,
        s.RejectReason,
        s.CancelledBy,
        s.CancelledAt,
        s.CancelReason,
    };

    private static async Task AppendEventAsync(NpgsqlConnection conn, Guid voucherId, string action,
        string actor, string note, VoucherState? before, VoucherState? after, DateTime? occurredAt = null)
    {
        var beforeJson = before is null ? null : JsonSerializer.Serialize(VoucherSnapshot(before));
        var afterJson = after is null ? null : JsonSerializer.Serialize(VoucherSnapshot(after));
        await conn.Cmd("""
            INSERT INTO hr_payout_voucher_events
                (id, voucher_id, action, actor_username, before_status, after_status, note,
                 before_data, after_data, occurred_at)
            VALUES (@id, @voucher, @action, @actor, @beforeStatus, @afterStatus, @note,
                    CAST(@beforeData AS jsonb), CAST(@afterData AS jsonb), @occurred)
            """)
            .With("@id", Guid.NewGuid()).With("@voucher", voucherId).With("@action", action)
            .With("@actor", actor).With("@beforeStatus", (object?)before?.Status ?? DBNull.Value)
            .With("@afterStatus", (object?)after?.Status ?? DBNull.Value).With("@note", note)
            .With("@beforeData", (object?)beforeJson ?? DBNull.Value)
            .With("@afterData", (object?)afterJson ?? DBNull.Value)
            .With("@occurred", occurredAt ?? DateTime.UtcNow)
            .ExecuteNonQueryAsync();
    }

    private static async Task ReturnRefundToQueueAsync(NpgsqlConnection conn, VoucherState voucher)
    {
        if (voucher.SourceKind != SourceRefund || voucher.SourceId is not { } refundId) return;
        await conn.Cmd("""
            UPDATE hr_penalty_refunds SET status='PendingAccounting', payout_method='', approved_by='',
                decided_at=NULL
            WHERE id=@id AND status='Approved'
            """).With("@id", refundId).ExecuteNonQueryAsync();
    }

    private static JsonElement ReadJsonSnapshot(NpgsqlDataReader r, string column)
    {
        if (r.IsDBNull(r.GetOrdinal(column))) return JsonSerializer.SerializeToElement<object?>(null);
        using var doc = JsonDocument.Parse(r.Str(column));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// "yyyy-MM" → nửa khoảng [đầu tháng, đầu tháng sau) theo giờ địa phương. Dùng khoảng thay vì
    /// to_char(cột) để index trên created_at còn tác dụng.
    /// </summary>
    private static bool TryMonthRange(string? month, out DateTime start, out DateTime end)
    {
        start = end = default;
        if (string.IsNullOrWhiteSpace(month)) return false;
        var parts = month.Trim().Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)
            || y is < 2000 or > 9999 || m is < 1 or > 12) return false;
        start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Local);
        end = start.AddMonths(1);
        return true;
    }

    /// <summary>Mã QR ngẫu nhiên, không đoán được (không dùng id phiếu để người ngoài không dựng được mã).</summary>
    private static string NewQrCode()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private static string NormCode(string? code, string name)
    {
        var raw = (string.IsNullOrWhiteSpace(code) ? name : code).Trim().ToLowerInvariant();
        var chars = raw.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        return slug.Length <= 40 ? slug : slug[..40];
    }

    private static int NormSort(int sortOrder) => sortOrder is < 0 or > 9999 ? 100 : sortOrder;

    private static object ReadVoucher(NpgsqlDataReader r, bool cashier)
    {
        var qr = r.Str("qr_code");
        var expires = r.DtNull("qr_expires_at");
        return new
        {
            id = r.Guid("id"),
            voucherNo = r.Str("voucher_no"),
            categoryId = r.IsDBNull(r.GetOrdinal("category_id")) ? (Guid?)null : r.Guid("category_id"),
            categoryName = r.Str("cat_name"),
            categoryCode = r.Str("cat_code"),
            employeeId = r.Guid("employee_id"),
            employeeName = r.Str("emp_name"),
            employeeCode = r.Str("employee_code"),
            amount = r.Dec("amount"),
            sourceKind = r.Str("source_kind"),
            sourceNo = r.Str("source_no"),
            reason = r.Str("reason"),
            note = r.Str("note"),
            status = r.Str("status"),
            createdBy = r.Str("created_by"),
            requiresRecipientConfirmation = r.Bool("requires_recipient_confirmation"),
            confirmedAt = r.DtNull("confirmed_at"),
            confirmedBy = r.Str("confirmed_by"),
            approvedBy = r.Str("approved_by"),
            approvedAt = r.DtNull("approved_at"),
            paidAt = r.DtNull("paid_at"),
            completedBy = r.Str("completed_by"),
            completedAt = r.DtNull("completed_at"),
            rejectedBy = r.Str("rejected_by"),
            rejectedAt = r.DtNull("rejected_at"),
            rejectReason = r.Str("reject_reason"),
            cancelledBy = r.Str("cancelled_by"),
            cancelledAt = r.DtNull("cancelled_at"),
            cancelReason = r.Str("cancel_reason"),
            createdAt = r.Dt("created_at"),
            // Chỉ kế toán mới nhận được nội dung mã QR để hiển thị; sổ của nhân viên không cần nó.
            qrValue = cashier && qr.Length > 0 ? QrPrefix + qr : null,
            qrExpiresAt = cashier ? expires : null,
        };
    }

    /// <summary>
    /// Chỉ ghi audit. Trigger trên hr_payout_vouchers / hr_payout_categories tự phát scope 'hr' sau khi
    /// commit — không gọi hub ở đây nữa (một đường duy nhất, xem DatabaseChangePublisher).
    /// </summary>
    private static async Task Signal(Database db, ClaimsPrincipal u, string action, string name)
        => await db.RecordAudit(u.Username(), action, "PayoutVoucher", name, $"{action} (web).");

    public record SaveCategoryReq(string? Code, string? Name, string? Description, bool IsActive, int SortOrder);
    public record CreateVoucherReq(Guid CategoryId, Guid EmployeeId, decimal Amount, string? Reason,
        string? Note, string? SourceKind, Guid? SourceId, bool? RequiresRecipientConfirmation);
    public record TransitionVoucherReq(string? Note);
    public record CancelVoucherReq(string? Reason);
}
