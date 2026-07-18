using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Engine đơn từ &amp; phê duyệt dùng chung cho MỌI loại đơn (nghỉ phép, nghỉ ốm, tăng ca, thanh toán,
/// tạm ứng, mua vật tư, điều chỉnh công, đổi ca, đăng ký xe/phòng họp…). Mỗi đơn giữ chi tiết linh hoạt
/// trong cột jsonb; luồng duyệt nhiều cấp (nhân viên → quản lý trực tiếp → admin) lưu ở hr_request_approvals,
/// hỗ trợ ký xác nhận điện tử và theo dõi trạng thái hồ sơ.
/// </summary>
public static class RequestEndpoints
{
    /// <summary>Danh mục loại đơn: type → (nhãn, nhóm). Frontend dựng form động từ đây.</summary>
    public static readonly (string Type, string Label, string Category)[] Types =
    {
        ("leave", "Xin nghỉ phép", "Nghỉ"),
        ("sick", "Xin nghỉ ốm", "Nghỉ"),
        ("business_trip", "Đăng ký công tác", "Công"),
        ("overtime", "Đăng ký tăng ca", "Công"),
        ("attendance_fix", "Điều chỉnh chấm công", "Công"),
        ("forgot_checkin", "Báo quên chấm công", "Công"),
        ("shift_swap", "Đổi ca / nhờ nhận ca", "Công"),
        ("payment", "Đề nghị thanh toán", "Tài chính"),
        ("advance", "Tạm ứng", "Tài chính"),
        ("reimbursement", "Hoàn ứng / quyết toán", "Tài chính"),
        ("purchase", "Mua sắm vật tư", "Tài chính"),
        ("booking", "Đăng ký xe / phòng họp", "Hành chính"),
        ("penalty_appeal", "Khiếu nại án phạt", "Kỷ luật"),
    };

    private static string TypeLabel(string type) =>
        Array.Find(Types, t => t.Type == type).Label ?? type;

    public record ReqOption(string Value, string Label);
    public record ReqField(string Key, string Label, string Type, string Hint = "", bool Required = true, ReqOption[]? Options = null);

    /// <summary>
    /// Định nghĩa các trường nhập cho từng loại đơn — NGUỒN CHUẨN DUY NHẤT. Web và app native dựng
    /// form động từ đây (endpoint /types trả kèm), nên thêm/sửa trường KHÔNG cần build lại app.
    /// `type`: text | date | time | number | money | textarea | select | checkboxes.
    /// </summary>
    private static readonly Dictionary<string, ReqField[]> FieldDefs = new()
    {
        ["leave"] = new[]
        {
            new ReqField("fromDate", "Từ ngày", "date", "Ngày đầu tiên bạn nghỉ"),
            new ReqField("toDate", "Đến ngày", "date", "Ngày cuối cùng bạn nghỉ"),
            new ReqField("days", "Số ngày nghỉ", "number", "Tự động tính theo khoảng ngày"),
            new ReqField("reason", "Lý do nghỉ", "textarea", "Ví dụ: về quê, việc gia đình…"),
        },
        ["sick"] = new[]
        {
            new ReqField("fromDate", "Từ ngày", "date", "Ngày đầu tiên bạn nghỉ"),
            new ReqField("toDate", "Đến ngày", "date", "Ngày cuối cùng bạn nghỉ"),
            new ReqField("days", "Số ngày nghỉ", "number", "Tự động tính theo khoảng ngày"),
            new ReqField("reason", "Lý do nghỉ ốm", "textarea", "Ví dụ: sốt, đi khám bệnh…"),
        },
        ["business_trip"] = new[]
        {
            new ReqField("fromDate", "Từ ngày", "date", "Ngày bắt đầu đi công tác"),
            new ReqField("toDate", "Đến ngày", "date", "Ngày kết thúc công tác"),
            new ReqField("destination", "Nơi công tác", "text", "Ví dụ: Hà Nội, kho Bình Dương…"),
            new ReqField("reason", "Nội dung công tác", "textarea", "Bạn đi làm việc gì?"),
        },
        ["overtime"] = new[]
        {
            new ReqField("date", "Ngày tăng ca", "date", "Ngày bạn làm thêm giờ"),
            new ReqField("fromTime", "Từ giờ", "time", "Giờ bắt đầu làm thêm"),
            new ReqField("toTime", "Đến giờ", "time", "Giờ kết thúc làm thêm"),
            new ReqField("reason", "Nội dung công việc", "textarea", "Bạn làm thêm việc gì?"),
        },
        ["attendance_fix"] = new[]
        {
            new ReqField("date", "Ngày cần điều chỉnh", "date", "Ngày chấm công bị sai"),
            new ReqField("checkIn", "Giờ vào đúng", "time", "Bỏ trống nếu không cần sửa", Required: false),
            new ReqField("checkOut", "Giờ ra đúng", "time", "Bỏ trống nếu không cần sửa", Required: false),
            new ReqField("reason", "Lý do", "textarea", "Vì sao chấm công bị sai?"),
        },
        ["forgot_checkin"] = new[]
        {
            new ReqField("date", "Ngày quên chấm", "date", "Ngày bạn quên chấm công"),
            new ReqField("direction", "Bạn quên chấm giờ nào?", "checkboxes", "Chọn giờ vào hoặc giờ ra",
                Options: new[] { new ReqOption("in", "Giờ vào"), new ReqOption("out", "Giờ ra") }),
            new ReqField("time", "Giờ thực tế", "time", "Giờ bạn thực sự vào/ra"),
            new ReqField("reason", "Lý do", "textarea", "Vì sao bạn quên chấm?"),
        },
        ["shift_swap"] = new[]
        {
            new ReqField("action", "Hình thức", "select", "Đổi ca của bạn hoặc đăng ký nhận ca trống",
                Options: new[] { new ReqOption("swap", "Xin đổi ca"), new ReqOption("take", "Xin nhận ca") }),
            new ReqField("date", "Ngày đổi ca", "date", "Ngày cần đổi ca"),
            new ReqField("withPerson", "Người đổi / bàn giao ca", "text", "Tên hoặc mã nhân viên đồng nghiệp", Required: false),
            new ReqField("reason", "Lý do", "textarea", "Vì sao bạn cần đổi ca?"),
        },
        ["payment"] = new[]
        {
            new ReqField("amount", "Số tiền", "money", "Số tiền cần thanh toán"),
            new ReqField("content", "Nội dung thanh toán", "textarea", "Thanh toán cho khoản gì?"),
        },
        ["advance"] = new[]
        {
            new ReqField("amount", "Số tiền tạm ứng", "money", "Số tiền bạn muốn tạm ứng"),
            new ReqField("reason", "Lý do", "textarea", "Bạn tạm ứng để làm gì?"),
        },
        ["reimbursement"] = new[]
        {
            new ReqField("advanceRef", "Mã đơn tạm ứng", "text", "Mã đơn đã được duyệt", Required:false),
            new ReqField("advancedAmount", "Số đã ứng", "money", "Tổng tiền công ty đã tạm ứng"),
            new ReqField("spentAmount", "Số đã chi", "money", "Tổng chi phí theo các hóa đơn"),
            new ReqField("receiptSummary", "Danh sách chi phí", "textarea", "Mỗi dòng ghi ngày, nội dung và số tiền"),
            new ReqField("reason", "Ghi chú quyết toán", "textarea", "Giải thích khoản cần hoàn hoặc cần thanh toán thêm", Required:false),
        },
        ["purchase"] = new[]
        {
            new ReqField("item", "Vật tư cần mua", "text", "Tên món đồ cần mua"),
            new ReqField("quantity", "Số lượng", "number", "Cần mua bao nhiêu?"),
            new ReqField("amount", "Dự trù chi phí", "money", "Ước tính hết bao nhiêu tiền", Required: false),
            new ReqField("reason", "Mục đích", "textarea", "Mua để dùng vào việc gì?"),
        },
        ["booking"] = new[]
        {
            new ReqField("resource", "Xe / phòng họp", "text", "Ví dụ: xe tải, phòng họp tầng 2…"),
            new ReqField("date", "Ngày sử dụng", "date", "Ngày bạn cần dùng"),
            new ReqField("fromTime", "Từ giờ", "time", "Giờ bắt đầu dùng"),
            new ReqField("toTime", "Đến giờ", "time", "Giờ trả lại"),
            new ReqField("reason", "Mục đích", "textarea", "Dùng để làm gì?"),
        },
        ["penalty_appeal"] = new[]
        {
            new ReqField("appealKind", "Bạn muốn đề nghị gì?", "select", "Chọn hình thức đề nghị với án phạt này",
                Options: new[] { new ReqOption("dispute", "Bỏ phạt"), new ReqOption("reduce", "Giảm tiền"), new ReqOption("installment", "Trả góp") }),
            new ReqField("penaltyNo", "Mã quyết định phạt", "text", "Chọn quyết định phạt để tự điền hình thức và số tiền"),
            new ReqField("penaltyType", "Hình thức phạt", "text", "", Required: false),
            new ReqField("penaltyAmount", "Số tiền phạt hiện tại", "money", "", Required: false),
            new ReqField("requestedAmount", "Số tiền đề nghị còn lại", "money", "Số tiền bạn mong muốn sau khi được giảm"),
            new ReqField("requestedMonths", "Số tháng muốn chia đóng", "number", "Ví dụ: 3, 6, 12 tháng"),
            new ReqField("reason", "Lý do đề nghị", "textarea", "Vì sao bạn cho rằng nên bỏ / giảm / chia nhỏ khoản phạt này?"),
        },
    };

    private static readonly ReqField[] DefaultFields =
    {
        new ReqField("reason", "Nội dung đơn", "textarea", "Mô tả chi tiết yêu cầu của bạn"),
    };

    private static object FieldsPayload(string type)
    {
        var fields = FieldDefs.TryGetValue(type, out var f) ? f : DefaultFields;
        return Array.ConvertAll(fields, x => new
        {
            key = x.Key,
            label = x.Label,
            type = x.Type,
            hint = x.Hint,
            required = x.Required,
            options = Array.ConvertAll(x.Options ?? Array.Empty<ReqOption>(), o => new { value = o.Value, label = o.Label }),
        });
    }

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS hr_request_seq START 1;

            CREATE TABLE IF NOT EXISTS hr_requests (
                id uuid PRIMARY KEY,
                request_no varchar(20) NOT NULL DEFAULT '',
                req_type varchar(32) NOT NULL,
                title varchar(200) NOT NULL DEFAULT '',
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                requester_username varchar(128) NOT NULL DEFAULT '',
                payload jsonb NOT NULL DEFAULT '{}',
                status varchar(20) NOT NULL DEFAULT 'Pending',
                current_step integer NOT NULL DEFAULT 1,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_hr_requests_requester ON hr_requests (requester_username, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_hr_requests_status ON hr_requests (status, created_at DESC);
            ALTER TABLE hr_requests ADD COLUMN IF NOT EXISTS due_at timestamptz NULL;
            ALTER TABLE hr_requests ADD COLUMN IF NOT EXISTS last_reminded_at timestamptz NULL;
            UPDATE hr_requests SET due_at=created_at + INTERVAL '2 days' WHERE due_at IS NULL;

            CREATE TABLE IF NOT EXISTS hr_approval_delegations (
                from_username varchar(128) PRIMARY KEY,
                to_username varchar(128) NOT NULL,
                from_date date NOT NULL,
                to_date date NOT NULL,
                active boolean NOT NULL DEFAULT TRUE,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS hr_request_approvals (
                id bigserial PRIMARY KEY,
                request_id uuid NOT NULL REFERENCES hr_requests(id) ON DELETE CASCADE,
                step_no integer NOT NULL,
                approver_role varchar(32) NOT NULL DEFAULT '',
                approver_username varchar(128) NOT NULL DEFAULT '',
                approver_name varchar(200) NOT NULL DEFAULT '',
                status varchar(20) NOT NULL DEFAULT 'Pending',
                decided_at timestamptz NULL,
                decided_by varchar(128) NOT NULL DEFAULT '',
                comment text NOT NULL DEFAULT '',
                signature text NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_hr_request_approvals ON hr_request_approvals (request_id, step_no);
            CREATE INDEX IF NOT EXISTS ix_hr_request_approvals_approver ON hr_request_approvals (approver_username, status);

            CREATE TABLE IF NOT EXISTS hr_request_attachments (
                id bigserial PRIMARY KEY,
                request_id uuid NOT NULL REFERENCES hr_requests(id) ON DELETE CASCADE,
                file_name varchar(260) NOT NULL,
                mime_type varchar(120) NOT NULL DEFAULT 'application/octet-stream',
                file_size bigint NOT NULL,
                content bytea NOT NULL,
                uploaded_by varchar(128) NOT NULL,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_hr_request_attachments_request ON hr_request_attachments(request_id, id);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapRequests(this WebApplication app)
    {
        var g = app.MapGroup("/api/requests").RequireAuthorization();

        g.MapGet("/types", () => Results.Ok(Array.ConvertAll(Types, t => new
        {
            type = t.Type,
            label = t.Label,
            category = t.Category,
            fields = FieldsPayload(t.Type),
        })));

        // scope: mine (mặc định) | inbox (chờ tôi duyệt) | all (admin)
        g.MapGet("/", async (ClaimsPrincipal u, Database db, string? scope, string? status) =>
        {
            await using var conn = await db.OpenAsync();
            var me = u.Username();
            var admin = u.IsAdmin();
            scope ??= "mine";

            string where;
            if (scope == "inbox")
                where = """
                    r.status='Pending' AND EXISTS (
                        SELECT 1 FROM hr_request_approvals a
                        WHERE a.request_id=r.id AND a.step_no=r.current_step AND a.status='Pending'
                          AND (a.approver_username=@me OR (a.approver_role='Admin' AND @admin))
                    )
                    """;
            else if (scope == "all" && admin)
                where = "TRUE";
            else
                where = "r.requester_username=@me";

            if (!string.IsNullOrWhiteSpace(status))
                where = $"({where}) AND r.status=@status";

            var cmd = conn.Cmd($"""
                SELECT r.id, r.request_no, r.req_type, r.title, r.requester_username, r.status, r.current_step,
                       r.created_at, e.full_name AS emp_name, e.employee_code,
                       (SELECT COUNT(*) FROM hr_request_approvals a WHERE a.request_id=r.id) AS total_steps
                FROM hr_requests r JOIN hr_employees e ON e.id=r.employee_id
                WHERE {where}
                ORDER BY r.created_at DESC
                """).With("@me", me).With("@admin", admin);
            if (!string.IsNullOrWhiteSpace(status)) cmd.With("@status", status);

            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new
                {
                    id = r.Guid("id"),
                    requestNo = r.Str("request_no"),
                    type = r.Str("req_type"),
                    typeLabel = TypeLabel(r.Str("req_type")),
                    title = r.Str("title"),
                    requesterUsername = r.Str("requester_username"),
                    employeeName = r.Str("emp_name"),
                    employeeCode = r.Str("employee_code"),
                    status = r.Str("status"),
                    currentStep = r.Int("current_step"),
                    totalSteps = r.Int("total_steps"),
                    createdAt = r.Dt("created_at"),
                });
            return Results.Ok(list);
        });

        // Số đơn đang chờ tôi duyệt (cho badge trên thanh điều hướng).
        g.MapGet("/inbox-count", async (ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var count = await conn.Cmd("""
                SELECT COUNT(*) FROM hr_requests r
                WHERE r.status='Pending' AND EXISTS (
                    SELECT 1 FROM hr_request_approvals a
                    WHERE a.request_id=r.id AND a.step_no=r.current_step AND a.status='Pending'
                      AND (a.approver_username=@me OR (a.approver_role='Admin' AND @admin))
                )
                """).With("@me", u.Username()).With("@admin", u.IsAdmin()).ExecuteScalarAsync();
            return Results.Ok(new { count = Convert.ToInt32(count) });
        });

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            object? head = null;
            string requester = "";
            await using (var r = await conn.Cmd("""
                SELECT r.id, r.request_no, r.req_type, r.title, r.requester_username, r.payload::text AS payload,
                       r.status, r.current_step, r.created_at, r.due_at, e.full_name AS emp_name, e.employee_code,
                       COALESCE(d.name,'') AS dept_name
                FROM hr_requests r JOIN hr_employees e ON e.id=r.employee_id
                LEFT JOIN hr_departments d ON d.id=e.department_id
                WHERE r.id=@id
                """).With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                requester = r.Str("requester_username");
                head = new
                {
                    id = r.Guid("id"),
                    requestNo = r.Str("request_no"),
                    type = r.Str("req_type"),
                    typeLabel = TypeLabel(r.Str("req_type")),
                    title = r.Str("title"),
                    requesterUsername = requester,
                    employeeName = r.Str("emp_name"),
                    employeeCode = r.Str("employee_code"),
                    departmentName = r.Str("dept_name"),
                    payload = ParseJson(r.Str("payload")),
                    status = r.Str("status"),
                    currentStep = r.Int("current_step"),
                    createdAt = r.Dt("created_at"),
                    dueAt = r.DtNull("due_at"),
                };
            }

            // Chỉ người gửi, người duyệt liên quan, hoặc admin được xem chi tiết.
            var me = u.Username();
            var admin = u.IsAdmin();
            var approvals = new List<object>();
            var iAmApprover = false;
            await using (var r = await conn.Cmd("""
                SELECT step_no, approver_role, approver_username, approver_name, status, decided_at, decided_by, comment,
                       (signature IS NOT NULL AND signature <> '') AS has_signature
                FROM hr_request_approvals WHERE request_id=@id ORDER BY step_no
                """).With("@id", id).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var au = r.Str("approver_username");
                    var role = r.Str("approver_role");
                    if (au == me || (role == "Admin" && admin)) iAmApprover = true;
                    approvals.Add(new
                    {
                        stepNo = r.Int("step_no"),
                        approverRole = role,
                        approverUsername = au,
                        approverName = r.Str("approver_name"),
                        status = r.Str("status"),
                        decidedAt = r.DtNull("decided_at"),
                        decidedBy = r.Str("decided_by"),
                        comment = r.Str("comment"),
                        hasSignature = r.Bool("has_signature"),
                    });
                }
            }

            if (requester != me && admin == false && !iAmApprover) return Results.Forbid();
            var attachments = new List<object>();
            await using (var ar = await conn.Cmd("SELECT id, file_name, mime_type, file_size FROM hr_request_attachments WHERE request_id=@id ORDER BY id")
                .With("@id", id).ExecuteReaderAsync())
            {
                while (await ar.ReadAsync()) attachments.Add(new { id = ar.Long("id"), fileName = ar.Str("file_name"), mimeType = ar.Str("mime_type"), fileSize = ar.Long("file_size") });
            }
            return Results.Ok(new { request = head, approvals, attachments });
        });

        g.MapPost("/{id:guid}/attachments", async (Guid id, string fileName, HttpContext ctx, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var owner = await conn.Cmd("SELECT requester_username FROM hr_requests WHERE id=@id AND status='Pending'").With("@id", id).ExecuteScalarAsync() as string;
            if (owner is null) return Results.BadRequest(new { message = "Chỉ có thể đính kèm khi đơn còn chờ duyệt." });
            if (!string.Equals(owner, u.Username(), StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            const int max = 15 * 1024 * 1024;
            await using var ms = new MemoryStream();
            var buffer = new byte[81920]; int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
            {
                if (ms.Length + read > max) return Results.BadRequest(new { message = "Mỗi tệp tối đa 15 MB." });
                await ms.WriteAsync(buffer.AsMemory(0, read));
            }
            var safe = Path.GetFileName(fileName).Trim();
            if (safe.Length == 0) safe = "dinh-kem";
            var mime = ctx.Request.ContentType ?? "application/octet-stream";
            var attachmentId = Convert.ToInt64(await conn.Cmd("""
                INSERT INTO hr_request_attachments(request_id,file_name,mime_type,file_size,content,uploaded_by)
                VALUES (@r,@n,@m,@s,@c,@u) RETURNING id
                """).With("@r", id).With("@n", safe[..Math.Min(safe.Length,260)]).With("@m", mime[..Math.Min(mime.Length,120)])
                .With("@s", ms.Length).With("@c", ms.ToArray()).With("@u", u.Username()).ExecuteScalarAsync());
            return Results.Ok(new { id = attachmentId, fileName = safe, mimeType = mime, fileSize = ms.Length });
        });

        g.MapGet("/{id:guid}/attachments/{attachmentId:long}", async (Guid id, long attachmentId, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var allowed = u.IsAdmin() || Convert.ToInt32(await conn.Cmd("""
                SELECT COUNT(*) FROM hr_requests r LEFT JOIN hr_request_approvals a ON a.request_id=r.id
                WHERE r.id=@id AND (r.requester_username=@u OR a.approver_username=@u)
                """).With("@id", id).With("@u", u.Username()).ExecuteScalarAsync()) > 0;
            if (!allowed) return Results.Forbid();
            await using var r = await conn.Cmd("SELECT file_name,mime_type,content FROM hr_request_attachments WHERE id=@a AND request_id=@id")
                .With("@a", attachmentId).With("@id", id).ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.NotFound();
            return Results.File((byte[])r.GetValue(r.GetOrdinal("content")), r.Str("mime_type"), r.Str("file_name"));
        });

        g.MapPost("/", async (CreateRequestReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            if (string.IsNullOrWhiteSpace(req.Type) || Array.FindIndex(Types, t => t.Type == req.Type) < 0)
                return Results.BadRequest(new { message = "Loại đơn không hợp lệ." });

            await using var conn = await db.OpenAsync();
            var me = u.Username();
            var empId = await HrEndpoints.EnsureEmployeeForUser(conn, me);

            // Tìm quản lý trực tiếp để dựng chuỗi duyệt.
            string mgrUsername = "", mgrName = "";
            await using (var r = await conn.Cmd("""
                SELECT m.username, m.full_name FROM hr_employees e
                LEFT JOIN hr_employees m ON m.id = e.manager_id WHERE e.id=@id
                """).With("@id", empId).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    mgrUsername = r.Str("username");
                    mgrName = r.Str("full_name");
                }
            }

            var reqId = Guid.NewGuid();
            var no = $"DT{Convert.ToInt64(await conn.Cmd("SELECT nextval('hr_request_seq')").ExecuteScalarAsync()):D5}";
            var payloadJson = req.Payload.HasValue ? req.Payload.Value.GetRawText() : "{}";

            if (req.Type == "shift_swap")
            {
                var dateText = ReadString(payloadJson, "date");
                if (!DateOnly.TryParse(dateText, out var workDate))
                    return Results.BadRequest(new { message = "Vui lòng chọn ngày đổi/nhận ca hợp lệ." });
                var duplicate = Convert.ToInt32(await conn.Cmd("""
                    SELECT COUNT(*) FROM hr_requests
                    WHERE employee_id=@emp AND req_type='shift_swap' AND status='Pending'
                      AND payload->>'date'=@date
                    """).With("@emp", empId).With("@date", dateText).ExecuteScalarAsync());
                if (duplicate > 0)
                    return Results.Conflict(new { message = "Bạn đã có một yêu cầu đổi/nhận ca đang chờ cho ngày này." });

                var action = ReadString(payloadJson, "action");
                var assigned = Convert.ToInt32(await conn.Cmd("""
                    SELECT COUNT(*) FROM hr_shift_assignments WHERE employee_id=@emp AND work_date=@date
                    """).With("@emp", empId).With("@date", workDate).ExecuteScalarAsync()) > 0;
                if (action != "take" && !assigned)
                    return Results.Conflict(new { message = "Ngày đã chọn chưa có ca của bạn để đổi. Hãy chọn Nhận ca nếu muốn đăng ký ca trống." });
            }

            await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                await new NpgsqlCommand("""
                    INSERT INTO hr_requests (id, request_no, req_type, title, employee_id, requester_username, payload, status, current_step)
                    VALUES (@id, @no, @type, @title, @emp, @me, @payload::jsonb, 'Pending', 1)
                    """, conn, tx)
                {
                    Parameters =
                    {
                        new("@id", reqId), new("@no", no), new("@type", req.Type),
                        new("@title", string.IsNullOrWhiteSpace(req.Title) ? TypeLabel(req.Type) : req.Title!.Trim()),
                        new("@emp", empId), new("@me", me), new("@payload", payloadJson),
                    }
                }.ExecuteNonQueryAsync();

                // Chuỗi duyệt: có quản lý (khác người gửi) → B1 quản lý, B2 admin; ngược lại → B1 admin.
                var step = 1;
                if (!string.IsNullOrWhiteSpace(mgrUsername) && !string.Equals(mgrUsername, me, StringComparison.OrdinalIgnoreCase))
                {
                    await InsertStep(conn, tx, reqId, step++, "", mgrUsername, mgrName);
                }
                await InsertStep(conn, tx, reqId, step, "Admin", "", "Quản trị viên / HR");

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            await db.RecordAudit(me, "Gửi đơn từ", "Request", no, $"{TypeLabel(req.Type)} (web).");

            // Đẩy thông báo tới người sẽ duyệt bước đầu tiên (quản lý trực tiếp, hoặc quản trị).
            var pushBody = $"{me} · {TypeLabel(req.Type)}";
            var inboxSig = $"inbox:{reqId}";
            if (!string.IsNullOrWhiteSpace(mgrUsername) && !string.Equals(mgrUsername, me, StringComparison.OrdinalIgnoreCase))
                await push.SendToUserAsync(mgrUsername, "Đơn mới chờ duyệt", pushBody, inboxSig, "Approval");
            else
                await push.SendToAdminsAsync("Đơn mới chờ duyệt", pushBody, inboxSig, "Approval");

            return Results.Ok(new { id = reqId, requestNo = no });
        });

        g.MapPut("/{id:guid}", async (Guid id, CreateRequestReq req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Type) || Array.FindIndex(Types, t => t.Type == req.Type) < 0)
                return Results.BadRequest(new { message = "Loại đơn không hợp lệ." });
            var payload = req.Payload.HasValue ? req.Payload.Value.GetRawText() : "{}";
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_requests SET req_type=@type,title=@title,payload=@payload::jsonb,updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND requester_username=@u AND status='Pending'
                """).With("@id", id).With("@u", u.Username()).With("@type", req.Type)
                .With("@title", string.IsNullOrWhiteSpace(req.Title) ? TypeLabel(req.Type) : req.Title.Trim())
                .With("@payload", payload).ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Đơn không còn đủ điều kiện chỉnh sửa." });
            await db.RecordAudit(u.Username(), "Sửa đơn từ", "Request", id.ToString(), TypeLabel(req.Type));
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/approve", async (Guid id, DecideReq req, ClaimsPrincipal u, Database db, PushService push) =>
            await Decide(id, req, u, db, push, approve: true));

        g.MapPost("/{id:guid}/reject", async (Guid id, DecideReq req, ClaimsPrincipal u, Database db, PushService push) =>
            await Decide(id, req, u, db, push, approve: false));

        g.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_requests SET status='Cancelled', updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND requester_username=@me AND status='Pending'
                """).With("@id", id).With("@me", u.Username()).ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Chỉ hủy được đơn của bạn khi còn chờ duyệt." });
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/remind", async (Guid id, ClaimsPrincipal u, Database db, PushService push) =>
        {
            await using var conn = await db.OpenAsync();
            string approver = "", no = "";
            await using (var r = await conn.Cmd("""
                SELECT r.request_no, COALESCE(a.approver_username,'') approver, r.last_reminded_at
                FROM hr_requests r LEFT JOIN hr_request_approvals a ON a.request_id=r.id AND a.step_no=r.current_step
                WHERE r.id=@id AND r.requester_username=@u AND r.status='Pending'
                """).With("@id", id).With("@u", u.Username()).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.BadRequest(new { message = "Đơn không còn chờ duyệt." });
                if (r.DtNull("last_reminded_at") is DateTime last && last > DateTime.UtcNow.AddHours(-24))
                    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                approver = r.Str("approver"); no = r.Str("request_no");
            }
            await conn.Cmd("UPDATE hr_requests SET last_reminded_at=CURRENT_TIMESTAMP WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            if (approver.Length > 0) await push.SendToUserAsync(approver, "Nhắc duyệt đơn", $"{no} đang chờ bạn xử lý.", $"inbox:{id}:remind", "Approval");
            else await push.SendToAdminsAsync("Nhắc duyệt đơn", $"{no} đang chờ xử lý.", $"inbox:{id}:remind", "Approval");
            return Results.NoContent();
        });

        g.MapPut("/delegations/me", async (ApprovalDelegationReq req, ClaimsPrincipal u, Database db) =>
        {
            if (string.IsNullOrWhiteSpace(req.ToUsername) || req.ToDate < req.FromDate)
                return Results.BadRequest(new { message = "Thông tin ủy quyền không hợp lệ." });
            await using var conn = await db.OpenAsync();
            var exists = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM hr_employees WHERE username=@u AND status='Active'")
                .With("@u", req.ToUsername.Trim()).ExecuteScalarAsync()) > 0;
            if (!exists) return Results.BadRequest(new { message = "Người được ủy quyền không tồn tại hoặc đã nghỉ." });
            await conn.Cmd("""
                INSERT INTO hr_approval_delegations(from_username,to_username,from_date,to_date,active)
                VALUES (@f,@t,@d1,@d2,TRUE) ON CONFLICT(from_username) DO UPDATE
                SET to_username=@t,from_date=@d1,to_date=@d2,active=TRUE,updated_at=CURRENT_TIMESTAMP
                """).With("@f", u.Username()).With("@t", req.ToUsername.Trim()).With("@d1", req.FromDate).With("@d2", req.ToDate).ExecuteNonQueryAsync();
            return Results.NoContent();
        });
    }

    private static async Task InsertStep(NpgsqlConnection conn, NpgsqlTransaction tx, Guid reqId, int step, string role, string username, string name)
    {
        if (username.Length > 0)
        {
            await using (var delegated = await new NpgsqlCommand("""
                    SELECT d.to_username, COALESCE(e.full_name,d.to_username) full_name
                    FROM hr_approval_delegations d LEFT JOIN hr_employees e ON e.username=d.to_username
                    WHERE d.from_username=@u AND d.active=TRUE AND CURRENT_DATE BETWEEN d.from_date AND d.to_date
                    """, conn, tx) { Parameters = { new("@u", username) } }.ExecuteReaderAsync())
            {
                if (await delegated.ReadAsync()) { username = delegated.GetString(0); name = delegated.GetString(1) + " (được ủy quyền)"; }
            }
        }
        await new NpgsqlCommand("""
            INSERT INTO hr_request_approvals (request_id, step_no, approver_role, approver_username, approver_name, status)
            VALUES (@r, @s, @role, @u, @n, 'Pending')
            """, conn, tx)
        {
            Parameters = { new("@r", reqId), new("@s", step), new("@role", role), new("@u", username), new("@n", name) }
        }.ExecuteNonQueryAsync();
    }

    private static async Task<IResult> Decide(Guid id, DecideReq req, ClaimsPrincipal u, Database db, PushService push, bool approve)
    {
        await using var conn = await db.OpenAsync();
        var me = u.Username();
        var admin = u.IsAdmin();

        // Nạp trạng thái đơn.
        string reqStatus = "", reqType = "", requester = "", requestNo = "", payloadJson = "{}";
        int currentStep = 0;
        Guid employeeId = default;
        await using (var r = await conn.Cmd("""
            SELECT status, req_type, requester_username, request_no, current_step, employee_id, payload::text AS payload
            FROM hr_requests WHERE id=@id
            """).With("@id", id).ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return Results.NotFound();
            reqStatus = r.Str("status");
            reqType = r.Str("req_type");
            requester = r.Str("requester_username");
            requestNo = r.Str("request_no");
            currentStep = r.Int("current_step");
            employeeId = r.Guid("employee_id");
            payloadJson = r.Str("payload");
        }
        if (reqStatus != "Pending") return Results.BadRequest(new { message = "Đơn không còn ở trạng thái chờ duyệt." });

        // Từ chối BẮT BUỘC nêu lý do (ghi vào comment) — để người gửi biết vì sao và có dấu vết xử lý.
        var comment = (req.Comment ?? "").Trim();
        if (!approve && comment.Length == 0)
            return Results.BadRequest(new { message = "Vui lòng nhập lý do từ chối đơn." });

        // Bước duyệt hiện tại + kiểm quyền. Đọc KHÔNG lọc theo trạng thái để phân biệt rõ:
        // sai người duyệt → 403 (Forbid); bước vừa có người khác xử lý (đua thiết bị) → 409 (Conflict).
        long stepId = 0;
        string stepRole = "", stepUser = "", stepStatus = "";
        await using (var r = await conn.Cmd("""
            SELECT id, approver_role, approver_username, status FROM hr_request_approvals
            WHERE request_id=@id AND step_no=@step
            """).With("@id", id).With("@step", currentStep).ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return Results.BadRequest(new { message = "Không tìm thấy bước duyệt hiện tại." });
            stepId = r.Long("id");
            stepRole = r.Str("approver_role");
            stepUser = r.Str("approver_username");
            stepStatus = r.Str("status");
        }
        var canDecide = stepUser == me || (stepRole == "Admin" && admin);
        if (!canDecide) return Results.Forbid();
        if (stepStatus != "Pending")
            return Results.Conflict(new { message = "Bước duyệt này vừa được người khác xử lý. Vui lòng tải lại." });

        // GHI QUYẾT ĐỊNH KIỂU "GIÀNH CHỖ" NGUYÊN TỬ: chỉ đổi được khi bước còn 'Pending'. Nếu hai thiết bị
        // cùng duyệt, PostgreSQL tuần tự hóa UPDATE này nên đúng MỘT lệnh chạm 1 dòng; lệnh thua chạm 0 dòng
        // → trả 409 và các tác động phụ (trừ phép, hoàn phạt, bù công…) chỉ chạy đúng một lần cho người thắng.
        var newStepStatus = approve ? "Approved" : "Rejected";
        var claimed = await conn.Cmd("""
            UPDATE hr_request_approvals SET status=@st, decided_at=CURRENT_TIMESTAMP, decided_by=@me,
                comment=@comment, signature=@sig WHERE id=@id AND status='Pending'
            """)
            .With("@st", newStepStatus).With("@me", me).With("@comment", comment)
            .With("@sig", (object?)req.Signature ?? DBNull.Value).With("@id", stepId)
            .ExecuteNonQueryAsync();
        if (claimed == 0)
            return Results.Conflict(new { message = "Đơn vừa được người khác xử lý. Vui lòng tải lại." });

        var pushBody = $"{TypeLabel(reqType)} · {requestNo}";
        if (!approve)
        {
            await conn.Cmd("UPDATE hr_requests SET status='Rejected', updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status='Pending'")
                .With("@id", id).ExecuteNonQueryAsync();
            await push.SendToUserAsync(requester, "Đơn bị từ chối", pushBody, $"req:{id}:rejected", "Requests");
        }
        else
        {
            // Còn bước kế tiếp?
            var next = await conn.Cmd("SELECT MIN(step_no) FROM hr_request_approvals WHERE request_id=@id AND status='Pending'")
                .With("@id", id).ExecuteScalarAsync();
            if (next is int nextStep)
            {
                await conn.Cmd("UPDATE hr_requests SET current_step=@s, updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status='Pending'")
                    .With("@s", nextStep).With("@id", id).ExecuteNonQueryAsync();

                // Đẩy thông báo tới người duyệt của bước kế tiếp.
                string nextRole = "", nextUser = "";
                await using (var r = await conn.Cmd("SELECT approver_role, approver_username FROM hr_request_approvals WHERE request_id=@id AND step_no=@s")
                    .With("@id", id).With("@s", nextStep).ExecuteReaderAsync())
                {
                    if (await r.ReadAsync()) { nextRole = r.Str("approver_role"); nextUser = r.Str("approver_username"); }
                }
                var nextSig = $"inbox:{id}";
                if (!string.IsNullOrWhiteSpace(nextUser))
                    await push.SendToUserAsync(nextUser, "Đơn chờ bạn duyệt", pushBody, nextSig, "Approval");
                else if (nextRole == "Admin")
                    await push.SendToAdminsAsync("Đơn chờ bạn duyệt", pushBody, nextSig, "Approval");
            }
            else
            {
                await conn.Cmd("UPDATE hr_requests SET status='Approved', updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status='Pending'")
                    .With("@id", id).ExecuteNonQueryAsync();
                await ApplyApprovedEffects(conn, reqType, employeeId, payloadJson, req, requestNo, me);
                await push.SendToUserAsync(requester, "Đơn đã được duyệt", pushBody, $"req:{id}:approved", "Requests");
            }
        }

        await db.RecordAudit(me, approve ? "Duyệt đơn từ" : "Từ chối đơn từ", "Request", requestNo,
            approve ? TypeLabel(reqType) : $"{TypeLabel(reqType)} — Lý do từ chối: {comment}");
        // Báo cho người gửi biết đơn đã được xử lý (tín hiệu chung + nhắm riêng người gửi).
        return Results.NoContent();
    }

    /// <summary>Tác động phụ khi đơn được duyệt hoàn tất (vd. trừ ngày phép, ghi bù chấm công, xử lý phạt).</summary>
    private static async Task ApplyApprovedEffects(NpgsqlConnection conn, string reqType, Guid employeeId,
        string payloadJson, DecideReq req, string requestNo, string decidedBy)
    {
        // Khiếu nại án phạt: bác bỏ (miễn) hoặc giảm tiền phạt; nếu tiền đã trừ → sinh khoản hoàn cho kế toán.
        if (reqType == "penalty_appeal")
        {
            await ApplyPenaltyAppeal(conn, employeeId, payloadJson, req, requestNo, decidedBy);
            return;
        }

        // Báo quên chấm công: ghi (đè) giờ nhân viên khai vào nhật ký chấm công của hệ thống.
        if (reqType == "forgot_checkin")
        {
            await ApplyForgotCheckin(conn, employeeId, payloadJson);
            return;
        }

        // Điều chỉnh chấm công: ghi đè giờ Vào/Ra đúng do nhân viên khai vào nhật ký chấm công.
        if (reqType == "attendance_fix")
        {
            await ApplyAttendanceFix(conn, employeeId, payloadJson);
            return;
        }

        if (reqType != "leave" && reqType != "sick") return;
        var days = ReadNumber(payloadJson, "days");
        if (days <= 0) return;
        var leaveType = reqType == "sick" ? "sick" : "annual";
        var year = DateTime.UtcNow.Year;
        await conn.Cmd("""
            INSERT INTO hr_leave_balances (id, employee_id, year, leave_type, total_days, used_days)
            VALUES (@id, @emp, @year, @type, 0, @days)
            ON CONFLICT (employee_id, year, leave_type) DO UPDATE SET used_days = hr_leave_balances.used_days + @days
            """)
            .With("@id", Guid.NewGuid()).With("@emp", employeeId).With("@year", year)
            .With("@type", leaveType).With("@days", days)
            .ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Xử lý khiếu nại án phạt đã được duyệt: bác bỏ (miễn toàn bộ) hoặc giảm tiền phạt. Nếu tiền phạt
    /// đã bị trừ vào các phiếu lương đã phát hành thì sinh một khoản hoàn (chờ kế toán duyệt) cho phần
    /// chênh: bác bỏ → hoàn toàn bộ đã trừ; giảm → hoàn phần đã trừ vượt quá mức mới.
    /// </summary>
    private static async Task ApplyPenaltyAppeal(NpgsqlConnection conn, Guid employeeId, string payloadJson,
        DecideReq req, string requestNo, string decidedBy)
    {
        var penaltyNo = ReadString(payloadJson, "penaltyNo");
        if (string.IsNullOrWhiteSpace(penaltyNo)) return;

        // Tra án phạt tiền còn hiệu lực HOẶC đã tất toán (đã thu đủ vẫn được khiếu nại để hoàn).
        Guid penaltyId = default;
        decimal amount = 0; int installments = 1; string note = "";
        var found = false;
        await using (var r = await conn.Cmd("""
            SELECT id, amount, installments, note FROM hr_penalties
            WHERE penalty_no=@no AND employee_id=@emp AND penalty_type='fine' AND status IN ('Active','Settled')
            """).With("@no", penaltyNo).With("@emp", employeeId).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                found = true;
                penaltyId = r.Guid("id");
                amount = r.Dec("amount");
                installments = r.Int("installments");
                note = r.Str("note");
            }
        }
        if (!found) return;

        // Đã thu bao nhiêu (sổ cái) — nền tảng để chốt: tổng thực thu KHÔNG BAO GIỜ vượt mức phạt hiện tại.
        var collected = await PenaltyEndpoints.GetCollectedAsync(conn, penaltyId);

        // Hình thức xử lý: ưu tiên chỉ định của người duyệt (web admin); nếu không có thì theo ĐỀ NGHỊ
        // của nhân viên ghi trong đơn (appealKind): dispute → bác bỏ, reduce → giảm tiền, installment → chia đóng.
        var payloadKind = ReadString(payloadJson, "appealKind").Trim().ToLowerInvariant();
        var outcome = !string.IsNullOrWhiteSpace(req.PenaltyOutcome)
            ? req.PenaltyOutcome!.Trim().ToLowerInvariant()
            : payloadKind switch { "reduce" => "reduce", "installment" => "installment", _ => "waive" };

        decimal refund;
        string appended;

        if (outcome == "installment")
        {
            // Chia nhỏ tiền phạt ra nhiều tháng (KHÔNG đổi tổng tiền → không hoàn). Các kỳ sau tự trừ theo
            // lịch mới nhưng tổng thực thu vẫn bị chặn ở mức phạt, nên không thu quá.
            var months = req.NewInstallments is > 0
                ? req.NewInstallments!.Value
                : (int)Math.Round(ReadNumber(payloadJson, "requestedMonths"));
            months = Math.Clamp(months, 1, 60);
            // collected < amount vì tổng không đổi → giữ Active để thu nốt phần còn thiếu theo nhịp mới.
            var status = collected >= amount ? "Settled" : "Active";
            await conn.Cmd("UPDATE hr_penalties SET installments=@inst, status=@st, note=@note, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@id", penaltyId).With("@inst", months).With("@st", status)
                .With("@note", Append(note, $"Chia đóng {months} tháng theo khiếu nại {requestNo}"))
                .ExecuteNonQueryAsync();
            refund = 0;
            appended = $"Chia đóng {months} tháng phạt {penaltyNo}";
        }
        else
        {
            // Số tiền giảm còn: ưu tiên mức người duyệt nhập, sau đó tới mức nhân viên đề nghị trong đơn.
            decimal? reduceTo = null;
            if (outcome == "reduce")
            {
                var candidate = req.NewAmount ?? ReadNumber(payloadJson, "requestedAmount");
                if (candidate > 0 && candidate < amount) reduceTo = decimal.Round(candidate, 0);
            }

            if (reduceTo is not null)
            {
                var newAmount = reduceTo.Value;
                // CHỐT THEO TỔNG ĐÃ THU: hoàn phần đã thu vượt mức mới; nếu đã thu ≥ mức mới → tất toán,
                // DỪNG HẲN các kỳ sau; nếu chưa đủ → còn hiệu lực để thu nốt (mức mới − đã thu).
                refund = Math.Max(0, collected - newAmount);
                var status = collected >= newAmount ? "Settled" : "Active";
                await conn.Cmd("UPDATE hr_penalties SET amount=@amt, status=@st, note=@note, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                    .With("@id", penaltyId).With("@amt", newAmount).With("@st", status)
                    .With("@note", Append(note, $"Giảm còn {newAmount:0} theo khiếu nại {requestNo}"))
                    .ExecuteNonQueryAsync();
                appended = $"Giảm tiền phạt {penaltyNo}";
            }
            else
            {
                // Bác bỏ = miễn toàn bộ + hoàn TẤT CẢ phần đã thu (mặc định, kể cả khi mức giảm không hợp lệ).
                await conn.Cmd("UPDATE hr_penalties SET status='Waived', note=@note, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                    .With("@id", penaltyId)
                    .With("@note", Append(note, $"Miễn theo khiếu nại {requestNo}"))
                    .ExecuteNonQueryAsync();
                refund = collected;
                appended = $"Bác bỏ phạt {penaltyNo}";
            }
        }

        if (refund > 0)
            await PenaltyRefundEndpoints.CreateAsync(conn, employeeId, penaltyId, penaltyNo, requestNo, refund,
                $"Hoàn tiền phạt {penaltyNo} ({appended}, khiếu nại {requestNo} được duyệt)", decidedBy);
    }

    private static string Append(string note, string extra) =>
        string.IsNullOrWhiteSpace(note) ? extra : $"{note} | {extra}";

    /// <summary>
    /// Ghi bù chấm công từ đơn "Báo quên chấm công" đã duyệt: lấy ngày + giờ thực tế nhân viên khai,
    /// dùng loại Vào/Ra do người khai chọn (thiếu thì suy ra theo giờ trong ngày) rồi GHI ĐÈ
    /// (xóa bản ghi cùng loại trong ngày, chèn bản mới) vào cham_cong_log để bảng công phản ánh đúng.
    /// </summary>
    private static async Task ApplyForgotCheckin(NpgsqlConnection conn, Guid employeeId, string payloadJson)
    {
        var dateStr = ReadString(payloadJson, "date");
        var timeStr = ReadString(payloadJson, "time");
        if (!DateOnly.TryParse(dateStr, out var day) || !TimeOnly.TryParse(timeStr, out var time))
            return;

        var (username, fullName) = await LoadEmployeeUser(conn, employeeId);
        if (string.IsNullOrWhiteSpace(username)) return;

        // Ưu tiên loại Vào/Ra do người khai chọn; nếu không có thì suy ra theo giờ trong ngày.
        var direction = ReadString(payloadJson, "direction");
        var loai = direction switch
        {
            "in" => AttendancePolicy.CheckInTypeIn,
            "out" => AttendancePolicy.CheckInTypeOut,
            _ => AttendancePolicy.LoaiForLocalTime(time.ToTimeSpan()),
        };

        await OverwriteAttendance(conn, username, fullName, day, loai, time, "Bù công theo đơn báo quên chấm công");
    }

    /// <summary>
    /// Điều chỉnh chấm công đã duyệt: ghi đè giờ Vào và/hoặc giờ Ra đúng do nhân viên khai vào
    /// cham_cong_log cho đúng ngày (bảng công tự tính lại từ log). Trường để trống thì giữ nguyên.
    /// </summary>
    private static async Task ApplyAttendanceFix(NpgsqlConnection conn, Guid employeeId, string payloadJson)
    {
        var dateStr = ReadString(payloadJson, "date");
        if (!DateOnly.TryParse(dateStr, out var day)) return;

        var (username, fullName) = await LoadEmployeeUser(conn, employeeId);
        if (string.IsNullOrWhiteSpace(username)) return;

        if (TimeOnly.TryParse(ReadString(payloadJson, "checkIn"), out var checkIn))
            await OverwriteAttendance(conn, username, fullName, day, AttendancePolicy.CheckInTypeIn, checkIn,
                "Điều chỉnh giờ vào theo đơn đã duyệt");

        if (TimeOnly.TryParse(ReadString(payloadJson, "checkOut"), out var checkOut))
            await OverwriteAttendance(conn, username, fullName, day, AttendancePolicy.CheckInTypeOut, checkOut,
                "Điều chỉnh giờ ra theo đơn đã duyệt");
    }

    private static async Task<(string Username, string FullName)> LoadEmployeeUser(NpgsqlConnection conn, Guid employeeId)
    {
        await using var r = await conn.Cmd("SELECT username, full_name FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteReaderAsync();
        if (await r.ReadAsync()) return (r.Str("username"), r.Str("full_name"));
        return ("", "");
    }

    /// <summary>
    /// Ghi đè một mốc chấm công (Vào/Ra) của nhân viên trong đúng ngày (giờ VN): xóa bản ghi cùng loại
    /// trong ngày rồi chèn giờ đã khai. Dùng chung cho báo quên chấm công &amp; điều chỉnh chấm công.
    /// </summary>
    private static async Task OverwriteAttendance(NpgsqlConnection conn, string username, string fullName,
        DateOnly day, string loai, TimeOnly time, string note)
    {
        var occurredUtc = AttendancePolicy.LocalToUtc(day.ToDateTime(time));

        await conn.Cmd("""
            DELETE FROM cham_cong_log
            WHERE username=@u AND loai=@loai AND (occurred_at AT TIME ZONE @tz)::date = @date
            """)
            .With("@u", username).With("@loai", loai).With("@tz", AttendancePolicy.TzId).With("@date", day)
            .ExecuteNonQueryAsync();

        await conn.Cmd("""
            INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
            VALUES (@u, @fn, @loai, 0, @at, @note)
            """)
            .With("@u", username).With("@fn", fullName).With("@loai", loai)
            .With("@at", occurredUtc).With("@note", note)
            .ExecuteNonQueryAsync();
    }

    private static string ReadString(string json, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(prop, out var v))
                return v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString();
        }
        catch { /* payload không hợp lệ → bỏ qua */ }
        return "";
    }

    private static decimal ReadNumber(string json, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(prop, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds)) return ds;
            }
        }
        catch { /* payload không hợp lệ → bỏ qua */ }
        return 0m;
    }

    private static JsonElement ParseJson(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json).RootElement.Clone(); }
        catch { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    public record CreateRequestReq(string? Type, string? Title, JsonElement? Payload);
    public record DecideReq(string? Comment, string? Signature, string? PenaltyOutcome, decimal? NewAmount, int? NewInstallments);
    public record ApprovalDelegationReq(string ToUsername, DateOnly FromDate, DateOnly ToDate);
}
