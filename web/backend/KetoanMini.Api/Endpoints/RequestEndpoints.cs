using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.SignalR;
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
        ("overtime", "Đăng ký tăng ca", "Công"),
        ("attendance_fix", "Điều chỉnh chấm công", "Công"),
        ("forgot_checkin", "Báo quên chấm công", "Công"),
        ("shift_swap", "Đổi ca / nhờ nhận ca", "Công"),
        ("payment", "Đề nghị thanh toán", "Tài chính"),
        ("advance", "Tạm ứng", "Tài chính"),
        ("purchase", "Mua sắm vật tư", "Tài chính"),
        ("booking", "Đăng ký xe / phòng họp", "Hành chính"),
    };

    private static string TypeLabel(string type) =>
        Array.Find(Types, t => t.Type == type).Label ?? type;

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
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapRequests(this WebApplication app)
    {
        var g = app.MapGroup("/api/requests").RequireAuthorization();

        g.MapGet("/types", () => Results.Ok(Array.ConvertAll(Types, t => new { type = t.Type, label = t.Label, category = t.Category })));

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
                       r.status, r.current_step, r.created_at, e.full_name AS emp_name, e.employee_code,
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
            return Results.Ok(new { request = head, approvals });
        });

        g.MapPost("/", async (CreateRequestReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
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
            await hub.Clients.All.SendAsync("changed", "data");
            return Results.Ok(new { id = reqId, requestNo = no });
        });

        g.MapPost("/{id:guid}/approve", async (Guid id, DecideReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
            await Decide(id, req, u, db, hub, approve: true));

        g.MapPost("/{id:guid}/reject", async (Guid id, DecideReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
            await Decide(id, req, u, db, hub, approve: false));

        g.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("""
                UPDATE hr_requests SET status='Cancelled', updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND requester_username=@me AND status='Pending'
                """).With("@id", id).With("@me", u.Username()).ExecuteNonQueryAsync();
            if (n == 0) return Results.BadRequest(new { message = "Chỉ hủy được đơn của bạn khi còn chờ duyệt." });
            await hub.Clients.All.SendAsync("changed", "data");
            return Results.NoContent();
        });
    }

    private static async Task InsertStep(NpgsqlConnection conn, NpgsqlTransaction tx, Guid reqId, int step, string role, string username, string name)
    {
        await new NpgsqlCommand("""
            INSERT INTO hr_request_approvals (request_id, step_no, approver_role, approver_username, approver_name, status)
            VALUES (@r, @s, @role, @u, @n, 'Pending')
            """, conn, tx)
        {
            Parameters = { new("@r", reqId), new("@s", step), new("@role", role), new("@u", username), new("@n", name) }
        }.ExecuteNonQueryAsync();
    }

    private static async Task<IResult> Decide(Guid id, DecideReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub, bool approve)
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

        // Bước duyệt hiện tại + kiểm quyền.
        long stepId = 0;
        string stepRole = "", stepUser = "";
        await using (var r = await conn.Cmd("""
            SELECT id, approver_role, approver_username FROM hr_request_approvals
            WHERE request_id=@id AND step_no=@step AND status='Pending'
            """).With("@id", id).With("@step", currentStep).ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return Results.BadRequest(new { message = "Không tìm thấy bước duyệt hiện tại." });
            stepId = r.Long("id");
            stepRole = r.Str("approver_role");
            stepUser = r.Str("approver_username");
        }
        var canDecide = stepUser == me || (stepRole == "Admin" && admin);
        if (!canDecide) return Results.Forbid();

        var newStepStatus = approve ? "Approved" : "Rejected";
        await conn.Cmd("""
            UPDATE hr_request_approvals SET status=@st, decided_at=CURRENT_TIMESTAMP, decided_by=@me,
                comment=@comment, signature=@sig WHERE id=@id
            """)
            .With("@st", newStepStatus).With("@me", me).With("@comment", req.Comment ?? "")
            .With("@sig", (object?)req.Signature ?? DBNull.Value).With("@id", stepId)
            .ExecuteNonQueryAsync();

        if (!approve)
        {
            await conn.Cmd("UPDATE hr_requests SET status='Rejected', updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@id", id).ExecuteNonQueryAsync();
        }
        else
        {
            // Còn bước kế tiếp?
            var next = await conn.Cmd("SELECT MIN(step_no) FROM hr_request_approvals WHERE request_id=@id AND status='Pending'")
                .With("@id", id).ExecuteScalarAsync();
            if (next is int nextStep)
            {
                await conn.Cmd("UPDATE hr_requests SET current_step=@s, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                    .With("@s", nextStep).With("@id", id).ExecuteNonQueryAsync();
            }
            else
            {
                await conn.Cmd("UPDATE hr_requests SET status='Approved', updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                    .With("@id", id).ExecuteNonQueryAsync();
                await ApplyApprovedEffects(conn, reqType, employeeId, payloadJson);
            }
        }

        await db.RecordAudit(me, approve ? "Duyệt đơn từ" : "Từ chối đơn từ", "Request", requestNo, TypeLabel(reqType));
        // Báo cho người gửi biết đơn đã được xử lý (tín hiệu chung + nhắm riêng người gửi).
        await hub.Clients.All.SendAsync("changed", "data");
        await hub.Clients.User(requester).SendAsync("changed", "data");
        return Results.NoContent();
    }

    /// <summary>Tác động phụ khi đơn được duyệt hoàn tất (vd. trừ ngày phép, ghi bù chấm công).</summary>
    private static async Task ApplyApprovedEffects(NpgsqlConnection conn, string reqType, Guid employeeId, string payloadJson)
    {
        // Báo quên chấm công: ghi (đè) giờ nhân viên khai vào nhật ký chấm công của hệ thống.
        if (reqType == "forgot_checkin")
        {
            await ApplyForgotCheckin(conn, employeeId, payloadJson);
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
    /// Ghi bù chấm công từ đơn "Báo quên chấm công" đã duyệt: lấy ngày + giờ thực tế nhân viên khai,
    /// suy ra Vào/Ra theo giờ trong ngày rồi GHI ĐÈ (xóa bản ghi cùng loại trong ngày, chèn bản mới)
    /// vào cham_cong_log để bảng công phản ánh đúng.
    /// </summary>
    private static async Task ApplyForgotCheckin(NpgsqlConnection conn, Guid employeeId, string payloadJson)
    {
        var dateStr = ReadString(payloadJson, "date");
        var timeStr = ReadString(payloadJson, "time");
        if (!DateOnly.TryParse(dateStr, out var day) || !TimeOnly.TryParse(timeStr, out var time))
            return;

        string username = "", fullName = "";
        await using (var r = await conn.Cmd("SELECT username, full_name FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteReaderAsync())
        {
            if (await r.ReadAsync()) { username = r.Str("username"); fullName = r.Str("full_name"); }
        }
        if (string.IsNullOrWhiteSpace(username)) return;

        var localNaive = day.ToDateTime(time);
        var occurredUtc = AttendancePolicy.LocalToUtc(localNaive);
        var loai = AttendancePolicy.LoaiForLocalTime(time.ToTimeSpan());

        // Ghi đè: bỏ bản ghi cùng loại trong đúng ngày (theo giờ VN) rồi chèn giờ đã khai.
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
            .With("@at", occurredUtc).With("@note", "Bù công theo đơn báo quên chấm công")
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
    public record DecideReq(string? Comment, string? Signature);
}
