using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Giao việc &amp; nghiệm thu ("Việc được giao"). Người có thẩm quyền — Admin hoặc tài khoản giữ vai trò
/// THỦ KHO (Warehouse) — giao việc cho nhân viên; nhân viên nhận, làm, nộp; người giao nghiệm thu (đạt/trả lại).
///
/// Vòng đời trạng thái:
///   assigned → in_progress → submitted → accepted            (nghiệm thu đạt = hoàn thành)
///                              submitted → rejected → in_progress (trả lại làm tiếp, có thể nộp lại)
///   bất kỳ trạng thái chưa kết thúc → cancelled              (người giao huỷ)
///
/// Realtime: PostgreSQL phát scope "tasks" sau khi giao dịch ghi hoàn tất; thông báo FCM vẫn nhắm
/// tới đúng người liên quan khi app đang ở nền.
/// </summary>
public static class TaskAssignmentEndpoints
{
    private static readonly string[] Priorities = ["low", "normal", "high", "urgent"];
    // Trạng thái nhân viên còn được thao tác (nhận/nộp).
    private static readonly string[] AssigneeOpen = ["assigned", "in_progress", "rejected"];

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS work_task_seq START 1;

            CREATE TABLE IF NOT EXISTS work_tasks (
                id uuid PRIMARY KEY,
                task_no varchar(24) NOT NULL DEFAULT '',
                title varchar(300) NOT NULL DEFAULT '',
                description text NOT NULL DEFAULT '',
                assigner_username varchar(128) NOT NULL DEFAULT '',
                assigner_name varchar(200) NOT NULL DEFAULT '',
                assignee_username varchar(128) NOT NULL DEFAULT '',
                assignee_name varchar(200) NOT NULL DEFAULT '',
                priority varchar(16) NOT NULL DEFAULT 'normal',
                due_at timestamptz NULL,
                status varchar(20) NOT NULL DEFAULT 'assigned',
                progress int NOT NULL DEFAULT 0,
                submit_note text NOT NULL DEFAULT '',
                submitted_at timestamptz NULL,
                review_note text NOT NULL DEFAULT '',
                rating int NULL,
                reviewed_at timestamptz NULL,
                reviewed_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_work_tasks_assignee ON work_tasks (assignee_username, status);
            CREATE INDEX IF NOT EXISTS ix_work_tasks_assigner ON work_tasks (assigner_username, status);

            CREATE TABLE IF NOT EXISTS work_task_events (
                id bigserial PRIMARY KEY,
                task_id uuid NOT NULL REFERENCES work_tasks(id) ON DELETE CASCADE,
                actor_username varchar(128) NOT NULL DEFAULT '',
                actor_name varchar(200) NOT NULL DEFAULT '',
                kind varchar(20) NOT NULL DEFAULT 'comment',
                note text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_work_task_events_task ON work_task_events (task_id, id);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapTasks(this WebApplication app)
    {
        // Ai đăng nhập cũng vào được: người thường thấy việc được giao cho mình; Thủ kho/Admin còn giao & nghiệm thu.
        var g = app.MapGroup("/api/tasks").RequireAuthorization();

        // Metadata dựng form giao việc: có được quyền giao không + danh sách người có thể nhận việc.
        g.MapGet("/meta", async (ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var canAssign = await ApiHelpers.IsTaskAssignerAsync(conn, me, PrimaryRole(u));
            var assignees = new List<object>();
            if (canAssign)
            {
                await using var r = await conn.Cmd("""
                    SELECT e.username, e.full_name, e.position, COALESCE(d.name,'') AS dept
                    FROM hr_employees e
                    LEFT JOIN hr_departments d ON d.id = e.department_id
                    WHERE e.status = 'Active' AND e.username <> ''
                      AND EXISTS (SELECT 1 FROM app_users au WHERE au.username = e.username AND au.is_deleted = FALSE)
                    ORDER BY d.name NULLS LAST, e.full_name
                    """).ExecuteReaderAsync();
                while (await r.ReadAsync())
                    assignees.Add(new
                    {
                        username = r.Str("username"),
                        fullName = r.Str("full_name"),
                        position = r.Str("position"),
                        department = r.Str("dept"),
                    });
            }
            return Results.Ok(new { canAssign, priorities = Priorities, assignees });
        });

        // Danh sách việc liên quan tới người đang đăng nhập.
        //  • inbox  = việc được giao CHO TÔI.
        //  • outbox = việc TÔI giao (Admin thấy toàn bộ việc trong hệ thống để giám sát).
        g.MapGet("/", async (ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            var admin = u.IsAdmin();
            await using var conn = await db.OpenAsync();
            var canAssign = await ApiHelpers.IsTaskAssignerAsync(conn, me, PrimaryRole(u));

            var inbox = new List<WorkTaskDto>();
            await using (var r = await conn.Cmd(
                SelectTask + " WHERE t.assignee_username = @me ORDER BY " + ListOrder)
                .With("@me", me).ExecuteReaderAsync())
                while (await r.ReadAsync()) inbox.Add(ReadTask(r));

            var outbox = new List<WorkTaskDto>();
            if (canAssign)
            {
                var where = admin ? "" : " WHERE t.assigner_username = @me";
                await using var r = await conn.Cmd(SelectTask + where + " ORDER BY " + ListOrder)
                    .With("@me", me).ExecuteReaderAsync();
                while (await r.ReadAsync()) outbox.Add(ReadTask(r));
            }

            var summary = new
            {
                inbox = inbox.Count,
                inboxActionable = inbox.Count(t => AssigneeOpen.Contains(t.Status)),
                outbox = outbox.Count,
                outboxReview = outbox.Count(t => t.Status == "submitted"),
            };
            return Results.Ok(new { canAssign, isAdmin = admin, inbox, outbox, summary });
        });

        // Chi tiết một việc + dòng thời gian sự kiện. Chỉ người giao/người nhận (hoặc Admin) xem được.
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            WorkTaskDto? task = null;
            await using (var r = await conn.Cmd(SelectTask + " WHERE t.id = @id").With("@id", id).ExecuteReaderAsync())
                if (await r.ReadAsync()) task = ReadTask(r);
            if (task is null) return Results.NotFound();

            var isAssigner = string.Equals(task.AssignerUsername, me, StringComparison.OrdinalIgnoreCase);
            var isAssignee = string.Equals(task.AssigneeUsername, me, StringComparison.OrdinalIgnoreCase);
            if (!isAssigner && !isAssignee && !u.IsAdmin()) return Results.Forbid();

            var events = new List<WorkTaskEventDto>();
            await using (var r = await conn.Cmd(
                "SELECT id, actor_username, actor_name, kind, note, created_at FROM work_task_events WHERE task_id=@id ORDER BY id")
                .With("@id", id).ExecuteReaderAsync())
                while (await r.ReadAsync())
                    events.Add(new WorkTaskEventDto(r.Long("id"), r.Str("actor_username"), r.Str("actor_name"),
                        r.Str("kind"), r.Str("note"), r.Dt("created_at")));

            var canReview = (isAssigner || u.IsAdmin());
            var flags = new
            {
                mine = isAssignee,
                assignedByMe = isAssigner || u.IsAdmin(),
                canSubmit = isAssignee && AssigneeOpen.Contains(task.Status),
                canStart = isAssignee && task.Status == "assigned",
                canReview = canReview && task.Status == "submitted",
                canEdit = canReview && task.Status != "accepted" && task.Status != "cancelled",
                canCancel = canReview && task.Status != "accepted" && task.Status != "cancelled",
            };
            return Results.Ok(new { task, events, flags });
        });

        // Giao việc mới (chỉ Thủ kho/Admin).
        g.MapPost("/", async (CreateTaskReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            if (!await ApiHelpers.IsTaskAssignerAsync(conn, me, PrimaryRole(u)))
                return Results.Json(new { message = "Bạn không có quyền giao việc." }, statusCode: 403);

            var title = (req.Title ?? "").Trim();
            if (title.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên công việc." });
            var assignee = (req.AssigneeUsername ?? "").Trim();
            if (assignee.Length == 0) return Results.BadRequest(new { message = "Vui lòng chọn người nhận việc." });

            var assigneeName = await conn.Cmd(
                "SELECT COALESCE(full_name, username) FROM app_users WHERE username=@u AND is_deleted=FALSE LIMIT 1")
                .With("@u", assignee).ExecuteScalarAsync() as string;
            if (assigneeName is null) return Results.BadRequest(new { message = "Không tìm thấy người nhận việc." });

            var priority = NormalizePriority(req.Priority);
            var assignerName = await DisplayName(conn, me);
            var id = Guid.NewGuid();
            var no = await NextTaskNo(conn);

            await conn.Cmd("""
                INSERT INTO work_tasks (id, task_no, title, description, assigner_username, assigner_name,
                    assignee_username, assignee_name, priority, due_at, status)
                VALUES (@id, @no, @title, @desc, @au, @an, @eu, @en, @pri, @due, 'assigned')
                """)
                .With("@id", id).With("@no", no).With("@title", title).With("@desc", (req.Description ?? "").Trim())
                .With("@au", me).With("@an", assignerName).With("@eu", assignee).With("@en", assigneeName)
                .With("@pri", priority).With("@due", (object?)req.DueAt ?? DBNull.Value)
                .ExecuteNonQueryAsync();
            await AddEvent(conn, id, me, assignerName, "assigned", $"Giao việc cho {assigneeName}.");

            await db.RecordAudit(me, "Giao việc", "WorkTask", no, $"{title} → {assigneeName}.");
            await push.SendToUserAsync(assignee, "Bạn được giao việc mới", $"{no}: {title}", $"task:{id}:assigned", "WorkTasks");
            return Results.Ok(new { id, taskNo = no });
        });

        // Sửa thông tin việc (người giao/Admin, khi chưa nghiệm thu/huỷ).
        g.MapPut("/{id:guid}", async (Guid id, CreateTaskReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.IsAdmin())) return Results.Forbid();
            if (t.Status is "accepted" or "cancelled")
                return Results.BadRequest(new { message = "Việc đã kết thúc, không sửa được." });

            var title = (req.Title ?? "").Trim();
            if (title.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên công việc." });
            var priority = NormalizePriority(req.Priority);

            // Cho phép đổi người nhận (nếu gửi lên và khác hiện tại).
            var assignee = (req.AssigneeUsername ?? "").Trim();
            var assigneeName = t.AssigneeName;
            var reassigned = false;
            if (assignee.Length > 0 && !string.Equals(assignee, t.AssigneeUsername, StringComparison.OrdinalIgnoreCase))
            {
                assigneeName = await conn.Cmd(
                    "SELECT COALESCE(full_name, username) FROM app_users WHERE username=@u AND is_deleted=FALSE LIMIT 1")
                    .With("@u", assignee).ExecuteScalarAsync() as string ?? "";
                if (assigneeName.Length == 0) return Results.BadRequest(new { message = "Không tìm thấy người nhận việc." });
                reassigned = true;
            }
            else assignee = t.AssigneeUsername;

            await conn.Cmd("""
                UPDATE work_tasks SET title=@title, description=@desc, priority=@pri, due_at=@due,
                    assignee_username=@eu, assignee_name=@en, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """)
                .With("@title", title).With("@desc", (req.Description ?? "").Trim()).With("@pri", priority)
                .With("@due", (object?)req.DueAt ?? DBNull.Value).With("@eu", assignee).With("@en", assigneeName)
                .With("@id", id).ExecuteNonQueryAsync();

            var actorName = await DisplayName(conn, me);
            await AddEvent(conn, id, me, actorName, reassigned ? "reassigned" : "updated",
                reassigned ? $"Chuyển việc cho {assigneeName}." : "Cập nhật thông tin việc.");
            await db.RecordAudit(me, "Sửa việc", "WorkTask", t.TaskNo, title);
            if (reassigned) await push.SendToUserAsync(assignee, "Bạn được giao việc", $"{t.TaskNo}: {title}", $"task:{id}:assigned", "WorkTasks");
            return Results.NoContent();
        });

        // Nhân viên bắt đầu làm.
        g.MapPost("/{id:guid}/start", async (Guid id, ClaimsPrincipal u, Database db) =>
            await AssigneeTransition(id, u, db, expect: ["assigned"], to: "in_progress", kind: "started", note: "Bắt đầu thực hiện."));

        // Nhân viên cập nhật tiến độ.
        g.MapPost("/{id:guid}/progress", async (Guid id, TaskNoteReq req, ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!string.Equals(t.AssigneeUsername, me, StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!AssigneeOpen.Contains(t.Status)) return Results.BadRequest(new { message = "Việc không ở trạng thái đang làm." });

            var pct = Math.Clamp(req.Progress ?? t.Progress, 0, 100);
            var status = t.Status == "assigned" ? "in_progress" : t.Status;
            await conn.Cmd("UPDATE work_tasks SET progress=@p, status=@s, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@p", pct).With("@s", status).With("@id", id).ExecuteNonQueryAsync();
            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "progress", string.IsNullOrWhiteSpace(req.Note) ? $"Tiến độ {pct}%." : $"Tiến độ {pct}%: {req.Note!.Trim()}");
            return Results.NoContent();
        });

        // Nhân viên nộp để nghiệm thu.
        g.MapPost("/{id:guid}/submit", async (Guid id, TaskNoteReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!string.Equals(t.AssigneeUsername, me, StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
            if (!AssigneeOpen.Contains(t.Status)) return Results.BadRequest(new { message = "Việc không thể nộp ở trạng thái hiện tại." });

            var note = (req.Note ?? "").Trim();
            await conn.Cmd("""
                UPDATE work_tasks SET status='submitted', submit_note=@note, submitted_at=CURRENT_TIMESTAMP,
                    updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """).With("@note", note).With("@id", id).ExecuteNonQueryAsync();
            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "submitted", note.Length > 0 ? $"Nộp nghiệm thu: {note}" : "Nộp nghiệm thu.");
            await db.RecordAudit(me, "Nộp nghiệm thu", "WorkTask", t.TaskNo, t.Title);
            await push.SendToUserAsync(t.AssignerUsername, "Có việc chờ nghiệm thu", $"{t.TaskNo}: {t.Title}", $"task:{id}:submitted", "WorkTasks");
            return Results.NoContent();
        });

        // Người giao NGHIỆM THU ĐẠT.
        g.MapPost("/{id:guid}/accept", async (Guid id, ReviewReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.IsAdmin())) return Results.Forbid();
            if (t.Status != "submitted") return Results.BadRequest(new { message = "Chỉ nghiệm thu được việc đang chờ nghiệm thu." });

            var note = (req.Note ?? "").Trim();
            var rating = req.Rating is >= 1 and <= 5 ? req.Rating : null;
            await conn.Cmd("""
                UPDATE work_tasks SET status='accepted', progress=100, review_note=@note, rating=@rating,
                    reviewed_at=CURRENT_TIMESTAMP, reviewed_by=@me, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """).With("@note", note).With("@rating", (object?)rating ?? DBNull.Value).With("@me", me).With("@id", id)
                .ExecuteNonQueryAsync();
            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "accepted",
                (rating is not null ? $"Nghiệm thu đạt ({rating}★). " : "Nghiệm thu đạt. ") + note);
            await db.RecordAudit(me, "Nghiệm thu đạt", "WorkTask", t.TaskNo, t.Title);
            await push.SendToUserAsync(t.AssigneeUsername, "Việc đã được nghiệm thu", $"{t.TaskNo}: {t.Title}", $"task:{id}:accepted", "WorkTasks");
            return Results.NoContent();
        });

        // Người giao TRẢ LẠI (không đạt) → nhân viên làm tiếp rồi nộp lại.
        g.MapPost("/{id:guid}/reject", async (Guid id, ReviewReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.IsAdmin())) return Results.Forbid();
            if (t.Status != "submitted") return Results.BadRequest(new { message = "Chỉ trả lại được việc đang chờ nghiệm thu." });

            var note = (req.Note ?? "").Trim();
            if (note.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do trả lại." });
            await conn.Cmd("""
                UPDATE work_tasks SET status='rejected', review_note=@note,
                    reviewed_at=CURRENT_TIMESTAMP, reviewed_by=@me, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """).With("@note", note).With("@me", me).With("@id", id).ExecuteNonQueryAsync();
            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "rejected", $"Trả lại: {note}");
            await db.RecordAudit(me, "Trả lại việc", "WorkTask", t.TaskNo, note);
            await push.SendToUserAsync(t.AssigneeUsername, "Việc bị trả lại", $"{t.TaskNo}: {note}", $"task:{id}:rejected", "WorkTasks");
            return Results.NoContent();
        });

        // Người giao huỷ việc.
        g.MapPost("/{id:guid}/cancel", async (Guid id, TaskNoteReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.IsAdmin())) return Results.Forbid();
            if (t.Status is "accepted" or "cancelled") return Results.BadRequest(new { message = "Việc đã kết thúc." });

            var note = (req.Note ?? "").Trim();
            await conn.Cmd("UPDATE work_tasks SET status='cancelled', review_note=@note, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@note", note).With("@id", id).ExecuteNonQueryAsync();
            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "cancelled", note.Length > 0 ? $"Huỷ việc: {note}" : "Huỷ việc.");
            await db.RecordAudit(me, "Huỷ việc", "WorkTask", t.TaskNo, t.Title);
            await push.SendToUserAsync(t.AssigneeUsername, "Việc đã bị huỷ", $"{t.TaskNo}: {t.Title}", $"task:{id}:cancelled", "WorkTasks");
            return Results.NoContent();
        });

        // Bình luận / trao đổi trên việc (cả hai phía).
        g.MapPost("/{id:guid}/comment", async (Guid id, TaskNoteReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            var note = (req.Note ?? "").Trim();
            if (note.Length == 0) return Results.BadRequest(new { message = "Nội dung trống." });
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            var isAssigner = CanReview(t, me, u.IsAdmin());
            var isAssignee = string.Equals(t.AssigneeUsername, me, StringComparison.OrdinalIgnoreCase);
            if (!isAssigner && !isAssignee) return Results.Forbid();

            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "comment", note);
            var other = isAssignee ? t.AssignerUsername : t.AssigneeUsername;
            await push.SendToUserAsync(other, $"Trao đổi việc {t.TaskNo}", note, $"task:{id}:comment", "WorkTasks");
            return Results.NoContent();
        });

        // Xoá việc (người giao/Admin).
        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.IsAdmin())) return Results.Forbid();
            await conn.Cmd("DELETE FROM work_tasks WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            await db.RecordAudit(me, "Xoá việc", "WorkTask", t.TaskNo, t.Title);
            return Results.NoContent();
        });
    }

    // ── Trợ giúp chung ───────────────────────────────────────────────────────────

    private static string PrimaryRole(ClaimsPrincipal u) =>
        u.FindFirstValue(ClaimTypes.Role) ?? "";

    private static string NormalizePriority(string? p)
    {
        var v = (p ?? "").Trim().ToLowerInvariant();
        return Priorities.Contains(v) ? v : "normal";
    }

    private static bool CanReview(TaskCore t, string me, bool admin) =>
        admin || string.Equals(t.AssignerUsername, me, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> DisplayName(NpgsqlConnection conn, string username)
    {
        var n = await conn.Cmd("SELECT COALESCE(NULLIF(full_name,''), username) FROM app_users WHERE username=@u LIMIT 1")
            .With("@u", username).ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(n) ? username : n;
    }

    private static async Task<string> NextTaskNo(NpgsqlConnection conn)
    {
        var n = Convert.ToInt64(await conn.Cmd("SELECT nextval('work_task_seq')").ExecuteScalarAsync());
        return $"CV{n:D4}";
    }

    private static async Task AddEvent(NpgsqlConnection conn, Guid taskId, string actor, string actorName, string kind, string note) =>
        await conn.Cmd(
            "INSERT INTO work_task_events (task_id, actor_username, actor_name, kind, note) VALUES (@t, @au, @an, @k, @n)")
            .With("@t", taskId).With("@au", actor).With("@an", actorName).With("@k", kind).With("@n", note)
            .ExecuteNonQueryAsync();

    private static async Task<IResult> AssigneeTransition(Guid id, ClaimsPrincipal u, Database db,
        string[] expect, string to, string kind, string note)
    {
        var me = u.Username();
        await using var conn = await db.OpenAsync();
        var t = await LoadCore(conn, id);
        if (t is null) return Results.NotFound();
        if (!string.Equals(t.AssigneeUsername, me, StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
        if (!expect.Contains(t.Status)) return Results.BadRequest(new { message = "Không thực hiện được ở trạng thái hiện tại." });

        await conn.Cmd("UPDATE work_tasks SET status=@s, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
            .With("@s", to).With("@id", id).ExecuteNonQueryAsync();
        var name = await DisplayName(conn, me);
        await AddEvent(conn, id, me, name, kind, note);
        return Results.NoContent();
    }

    private static async Task<TaskCore?> LoadCore(NpgsqlConnection conn, Guid id)
    {
        await using var r = await conn.Cmd(
            "SELECT task_no, title, assigner_username, assignee_username, assignee_name, status, progress FROM work_tasks WHERE id=@id")
            .With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new TaskCore(r.Str("task_no"), r.Str("title"), r.Str("assigner_username"),
            r.Str("assignee_username"), r.Str("assignee_name"), r.Str("status"), r.Int("progress"));
    }

    private const string SelectTask = """
        SELECT t.id, t.task_no, t.title, t.description, t.assigner_username, t.assigner_name,
               t.assignee_username, t.assignee_name, t.priority, t.due_at, t.status, t.progress,
               t.submit_note, t.submitted_at, t.review_note, t.rating, t.reviewed_at, t.reviewed_by,
               t.created_at, t.updated_at
        FROM work_tasks t
        """;

    // Ưu tiên việc còn "sống" (chưa accepted/cancelled) lên trước, rồi theo hạn gần nhất, mới nhất.
    private const string ListOrder =
        "(CASE WHEN t.status IN ('accepted','cancelled') THEN 1 ELSE 0 END), t.due_at NULLS LAST, t.created_at DESC";

    private static WorkTaskDto ReadTask(NpgsqlDataReader r)
    {
        var due = r.DtNull("due_at");
        var status = r.Str("status");
        var overdue = due is not null && due.Value < DateTime.UtcNow && status is not ("accepted" or "cancelled");
        return new WorkTaskDto(
            r.Guid("id"), r.Str("task_no"), r.Str("title"), r.Str("description"),
            r.Str("assigner_username"), r.Str("assigner_name"), r.Str("assignee_username"), r.Str("assignee_name"),
            r.Str("priority"), due, status, r.Int("progress"),
            r.Str("submit_note"), r.DtNull("submitted_at"), r.Str("review_note"),
            r.IsDBNull(r.GetOrdinal("rating")) ? null : r.Int("rating"),
            r.DtNull("reviewed_at"), r.Str("reviewed_by"), r.Dt("created_at"), r.Dt("updated_at"), overdue);
    }

    private sealed record TaskCore(string TaskNo, string Title, string AssignerUsername,
        string AssigneeUsername, string AssigneeName, string Status, int Progress);

    public record WorkTaskDto(Guid Id, string TaskNo, string Title, string Description,
        string AssignerUsername, string AssignerName, string AssigneeUsername, string AssigneeName,
        string Priority, DateTime? DueAt, string Status, int Progress,
        string SubmitNote, DateTime? SubmittedAt, string ReviewNote, int? Rating,
        DateTime? ReviewedAt, string ReviewedBy, DateTime CreatedAt, DateTime UpdatedAt, bool Overdue);

    public record WorkTaskEventDto(long Id, string ActorUsername, string ActorName, string Kind, string Note, DateTime CreatedAt);

    public record CreateTaskReq(string? Title, string? Description, string? AssigneeUsername, string? Priority, DateTime? DueAt);
    public record TaskNoteReq(string? Note, int? Progress);
    public record ReviewReq(string? Note, int? Rating);
}
