using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Giao việc &amp; nghiệm thu ("Việc được giao"). Người có thẩm quyền — Admin hoặc tài khoản giữ vai trò
/// THỦ KHO (Warehouse) — giao việc cho nhân viên; nhân viên nhận, làm, nộp; người giao nghiệm thu (đạt/trả lại).
///
/// Vòng đời trạng thái:
///   assigned → in_progress → submitted → accepted            (nghiệm thu đạt)
///                              submitted → rejected → in_progress (trả lại làm tiếp, có thể nộp lại)
///   bất kỳ trạng thái chưa kết thúc → cancelled              (người giao huỷ)
///
/// VIỆC GIAO HÀNG (source_kind='delivery') đi đường NGẮN HƠN — KHÔNG có chặng nghiệm thu:
///   assigned → in_progress → submitted → completed
/// 'submitted' = lái xe báo đã giao xong, đang chờ nộp tờ phiếu ký nhận; 'completed' do kế toán
/// xác nhận phiếu về kho (DeliverySettlementEndpoints). Khách đã nhận hàng thì tờ phiếu có chữ ký
/// mới là bằng chứng, thêm một cú bấm "nghiệm thu" của chính người đó chỉ là thủ tục thừa (chốt của
/// người dùng 2026-08-24). Đường 'rejected' vẫn giữ cho tình huống lái xe báo đã giao nhưng hàng
/// phải quay đầu.
///
/// Realtime: PostgreSQL phát scope "tasks" sau khi giao dịch ghi hoàn tất; thông báo FCM vẫn nhắm
/// tới đúng người liên quan khi app đang ở nền.
/// </summary>
public static class TaskAssignmentEndpoints
{
    private static readonly string[] Priorities = ["low", "normal", "high", "urgent"];
    // Trạng thái nhân viên còn được thao tác (nhận/nộp).
    private static readonly string[] AssigneeOpen = ["assigned", "in_progress", "rejected"];
    // Trạng thái ĐÃ ĐÓNG SỔ: không sửa, không huỷ, không kéo lệnh thu vào nữa, và xếp xuống cuối
    // danh sách. 'completed' là bước sau 'accepted' của việc giao hàng (phiếu giấy đã về kho).
    internal static bool IsClosed(string status) => status is "accepted" or "completed" or "cancelled";

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

            -- Nguồn sinh ra việc. '' = người dùng tự giao (mặc định cũ);
            -- 'delivery' = sinh tự động khi gán phiếu xuất kho cho lái xe.
            ALTER TABLE work_tasks ADD COLUMN IF NOT EXISTS source_kind varchar(24) NOT NULL DEFAULT '';
            ALTER TABLE work_tasks ADD COLUMN IF NOT EXISTS source_document_id uuid NULL;
            -- Mỗi phiếu xuất kho chỉ đẻ ra ĐÚNG MỘT việc giao hàng còn sống. Gán lại lái xe thì
            -- sửa chính việc đó chứ không tạo việc thứ hai, nếu không lái xe cũ vẫn thấy việc cũ.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_work_tasks_delivery_document
                ON work_tasks (source_document_id)
                WHERE source_kind = 'delivery' AND source_document_id IS NOT NULL;

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
        var g = app.MapGroup("/api/tasks").RequirePermission(Permissions.TasksSelf);

        // Metadata dựng form giao việc: có được quyền giao không + danh sách người có thể nhận việc.
        g.MapGet("/meta", async (ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var canAssign = u.Can(Permissions.TasksAssign);
            var assignees = new List<object>();
            if (canAssign)
            {
                var scope = await ResolveAssigneeScopeAsync(conn, u);
                await using var r = await conn.Cmd("""
                    SELECT au.username, e.full_name, e.position, COALESCE(d.name,'') AS dept
                    FROM hr_employees e
                    JOIN app_users au ON au.is_deleted=FALSE
                     AND (au.id=e.user_id OR (e.user_id IS NULL AND lower(au.username)=lower(e.username)))
                    LEFT JOIN hr_departments d ON d.id = e.department_id
                    WHERE e.status = 'Active' AND e.username <> ''
                      AND (@all OR (@location IS NOT NULL AND e.location_id=@location)
                                OR (@department IS NOT NULL AND e.department_id=@department))
                    ORDER BY d.name NULLS LAST, e.full_name
                    """).With("@all", scope.Kind == AssigneeScopeKind.All)
                    .With("@location", (object?)scope.LocationId ?? DBNull.Value)
                    .With("@department", (object?)scope.DepartmentId ?? DBNull.Value)
                    .ExecuteReaderAsync();
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
        //
        // activeOnly=true (app di động dùng): HẾT NGÀY thì việc đã xong của hôm qua rời màn hình, chỉ
        // còn việc chưa xong kéo sang hôm nay. Việc xong rồi tra ở /history chứ không nằm chắn màn
        // "Việc cần làm" nữa. Web không truyền cờ này nên vẫn thấy đủ như trước.
        g.MapGet("/", async (ClaimsPrincipal u, Database db, bool? activeOnly) =>
        {
            var me = u.Username();
            var admin = u.Can(Permissions.UsersManage);
            await using var conn = await db.OpenAsync();
            var canAssign = u.Can(Permissions.TasksAssign);
            var todayOnly = activeOnly == true ? " AND " + ClosedTodayOrOpen : "";

            var inbox = new List<WorkTaskDto>();
            await using (var r = await conn.Cmd(
                SelectTask + " WHERE t.assignee_username = @me" + todayOnly + " ORDER BY " + ListOrder)
                .With("@me", me).ExecuteReaderAsync())
                while (await r.ReadAsync()) inbox.Add(ReadTask(r));

            // ── Gộp "giao hàng" và "thu tiền" của CÙNG một khách thành một dòng việc ──────────
            // Lái xe tới một khách để làm hai chuyện; tách hai thẻ khiến họ dễ bỏ sót khoản thu.
            // Hai bản ghi vẫn nằm riêng dưới CSDL, chỉ nối lại khi trả về cho máy đọc.
            var openCollections = await LoadOpenCollections(conn, me);
            var mergedCollectionIds = new HashSet<Guid>();
            for (var i = 0; i < inbox.Count; i++)
            {
                var task = inbox[i];
                if (task.Delivery?.CustomerId is not { } customerId) continue;
                // Việc đã nghiệm thu/huỷ thì không kéo khoản thu vào nữa.
                if (IsClosed(task.Status)) continue;
                var match = openCollections.FirstOrDefault(
                    c => c.CustomerId == customerId && !mergedCollectionIds.Contains(c.Id));
                if (match is null) continue;
                mergedCollectionIds.Add(match.Id);
                inbox[i] = task with { Delivery = task.Delivery with { Collection = match } };
            }
            // Lệnh thu không đi kèm phiếu giao nào vẫn phải hiện trong "Việc được giao".
            var standaloneCollections = openCollections
                .Where(c => !mergedCollectionIds.Contains(c.Id))
                .ToList();

            // "Việc tôi giao" bám theo VIỆC MÌNH ĐÃ GIAO, không bám theo quyền TasksAssign.
            // Lý do: kế toán gán phiếu xuất kho cho lái xe là sinh ra một việc giao hàng và trở
            // thành người giao việc đó — nhưng kế toán KHÔNG có TasksAssign (quyền đó chỉ mở cửa
            // "Giao việc mới" cho Thủ kho/Trưởng phòng). Nếu lọc theo quyền thì chính người giao
            // không thấy việc mình giao để nghiệm thu, và phiếu kẹt mãi ở "Chờ nghiệm thu".
            var outbox = new List<WorkTaskDto>();
            {
                var where = admin ? " WHERE TRUE" : " WHERE t.assigner_username = @me";
                await using var r = await conn.Cmd(SelectTask + where + todayOnly + " ORDER BY " + ListOrder)
                    .With("@me", me).ExecuteReaderAsync();
                while (await r.ReadAsync()) outbox.Add(ReadTask(r));
            }

            var summary = new
            {
                inbox = inbox.Count,
                inboxActionable = inbox.Count(t => AssigneeOpen.Contains(t.Status)),
                outbox = outbox.Count,
                // "Chờ nghiệm thu" chỉ đếm việc THƯỜNG. Việc giao hàng nằm ở 'submitted' cho tới khi
                // tờ phiếu về kho — đếm vào đây thì kế toán thấy một đống việc treo mà không có gì
                // để bấm ở màn Công việc.
                outboxReview = outbox.Count(t => t.Status == "submitted" && t.Delivery is null),
                outboxAwaitingVoucher = outbox.Count(t => t.Status == "submitted" && t.Delivery is not null),
                // Việc phải làm ở màn lái xe = việc còn mở + lệnh thu chưa gộp vào việc nào.
                collections = openCollections.Count,
                collectionsStandalone = standaloneCollections.Count,
            };
            return Results.Ok(new
            {
                canAssign,
                isAdmin = admin,
                inbox,
                outbox,
                collections = standaloneCollections,
                summary,
            });
        });

        // LỊCH SỬ việc đã hoàn thành trong một khoảng ngày (app lọc theo tuần/tháng).
        //  • Nhân viên thường: việc của chính mình + việc mình đã giao cho người khác.
        //  • Admin: toàn hệ thống, lọc thêm theo từng nhân viên qua ?assignee=.
        // "Hoàn thành" = đã nghiệm thu đạt ('accepted') hoặc đã đóng phiếu giao hàng ('completed').
        // Việc bị huỷ không phải thành tích nên không nằm ở đây.
        g.MapGet("/history", async (ClaimsPrincipal u, Database db, string? from, string? to, string? assignee) =>
        {
            var me = u.Username();
            var admin = u.Can(Permissions.UsersManage);
            var fromDate = ParseDate(from) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(-30);
            var toDate = ParseDate(to) ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);

            await using var conn = await db.OpenAsync();
            var items = new List<WorkTaskDto>();
            // Khoảng ngày Việt Nam quy về timestamptz: [00:00 ngày from, 00:00 ngày to+1).
            const string range =
                " AND " + ClosedAt + " >= ((@from::date)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh')" +
                " AND " + ClosedAt + " <  (((@to::date) + 1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh')";
            var scope = admin
                ? ""
                : " AND (t.assignee_username = @me OR t.assigner_username = @me)";
            await using (var r = await conn.Cmd(
                SelectTask + " WHERE t.status IN ('accepted','completed')" + range + scope +
                " ORDER BY " + ClosedAt + " DESC")
                .With("@me", me).With("@from", fromDate).With("@to", toDate).ExecuteReaderAsync())
                while (await r.ReadAsync()) items.Add(ReadTask(r));

            // Danh sách người để lọc dựng từ CHÍNH khoảng đang xem (trước khi lọc theo người), nếu
            // không thì chọn một người xong là mất luôn các tên còn lại.
            var people = items
                .GroupBy(t => t.AssigneeUsername, StringComparer.OrdinalIgnoreCase)
                .Select(grp => new
                {
                    username = grp.Key,
                    fullName = grp.First().AssigneeName,
                    count = grp.Count(),
                })
                .OrderByDescending(p => p.count).ThenBy(p => p.fullName, StringComparer.CurrentCulture)
                .ToList();

            var filtered = string.IsNullOrWhiteSpace(assignee)
                ? items
                : items.Where(t => string.Equals(t.AssigneeUsername, assignee, StringComparison.OrdinalIgnoreCase)).ToList();

            return Results.Ok(new
            {
                from = fromDate.ToString("yyyy-MM-dd"),
                to = toDate.ToString("yyyy-MM-dd"),
                isAdmin = admin,
                items = filtered,
                people,
                total = filtered.Count,
            });
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
            if (!isAssigner && !isAssignee && !u.Can(Permissions.UsersManage)) return Results.Forbid();

            var events = new List<WorkTaskEventDto>();
            await using (var r = await conn.Cmd(
                "SELECT id, actor_username, actor_name, kind, note, created_at FROM work_task_events WHERE task_id=@id ORDER BY id")
                .With("@id", id).ExecuteReaderAsync())
                while (await r.ReadAsync())
                    events.Add(new WorkTaskEventDto(r.Long("id"), r.Str("actor_username"), r.Str("actor_name"),
                        r.Str("kind"), r.Str("note"), r.Dt("created_at")));

            var canReview = isAssigner || u.Can(Permissions.UsersManage);
            var flags = new
            {
                mine = isAssignee,
                assignedByMe = isAssigner || u.Can(Permissions.UsersManage),
                canSubmit = isAssignee && AssigneeOpen.Contains(task.Status),
                canStart = isAssignee && task.Status == "assigned",
                // Việc giao hàng không nghiệm thu: nó đóng ở màn Phiếu khi kế toán nhận lại tờ phiếu.
                canReview = canReview && task.Status == "submitted" && task.Delivery is null,
                // Trả lại thì việc giao hàng VẪN cần: lái xe báo đã giao nhưng hàng phải quay đầu.
                canReject = canReview && task.Status == "submitted",
                canEdit = canReview && !IsClosed(task.Status),
                canCancel = canReview && !IsClosed(task.Status),
            };
            return Results.Ok(new { task, events, flags });
        });

        // Giao việc mới (chỉ Thủ kho/Admin).
        g.MapPost("/", async (CreateTaskReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            if (!u.Can(Permissions.TasksAssign))
                return Results.Json(new { message = "Bạn không có quyền giao việc." }, statusCode: 403);

            var title = (req.Title ?? "").Trim();
            if (title.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên công việc." });
            var assignee = (req.AssigneeUsername ?? "").Trim();
            if (assignee.Length == 0) return Results.BadRequest(new { message = "Vui lòng chọn người nhận việc." });

            var scope = await ResolveAssigneeScopeAsync(conn, u);
            var target = await LoadAssignableEmployeeAsync(conn, scope, assignee);
            if (target is null)
                return Results.BadRequest(new { message = "Người nhận việc không nằm trong phạm vi bạn được giao." });
            assignee = target.Username;
            var assigneeName = target.FullName;

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
            await push.SendToUserAsync(assignee, "Bạn được giao việc mới", $"{no}: {title}", $"task:{id}:assigned", "Tasks");
            return Results.Ok(new { id, taskNo = no });
        });

        // Sửa thông tin việc (người giao/Admin, khi chưa nghiệm thu/huỷ).
        g.MapPut("/{id:guid}", async (Guid id, CreateTaskReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.Can(Permissions.UsersManage))) return Results.Forbid();
            if (IsClosed(t.Status))
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
                if (!u.Can(Permissions.TasksAssign)) return Results.Forbid();
                var scope = await ResolveAssigneeScopeAsync(conn, u);
                var target = await LoadAssignableEmployeeAsync(conn, scope, assignee);
                if (target is null)
                    return Results.BadRequest(new { message = "Người nhận việc không nằm trong phạm vi bạn được giao." });
                assignee = target.Username;
                assigneeName = target.FullName;
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
            if (reassigned) await push.SendToUserAsync(assignee, "Bạn được giao việc", $"{t.TaskNo}: {title}", $"task:{id}:assigned", "Tasks");
            return Results.NoContent();
        });

        // Nhân viên bắt đầu làm. Với việc GIAO HÀNG, "bắt đầu" nghĩa là tài xế đã cầm phiếu lên đường —
        // đó là mốc đầu tiên của tiến trình mà kho và kế toán phải nhìn thấy.
        g.MapPost("/{id:guid}/start", async (Guid id, ClaimsPrincipal u, Database db, PushService push) =>
            await AssigneeTransition(id, u, db, expect: ["assigned"], to: "in_progress", kind: "started",
                note: "Bắt đầu thực hiện.",
                announce: (t, actorName) => t.IsDelivery
                    ? new DeliveryAnnouncement("Tài xế đã nhận chuyến",
                        $"{actorName} đã nhận chuyến và đang đi giao — {t.Title}", $"delivery:{id}:started")
                    : null,
                push: push));

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
            await push.SendToUserAsync(t.AssignerUsername, "Có việc chờ nghiệm thu", $"{t.TaskNo}: {t.Title}", $"task:{id}:submitted", "Tasks");
            // Giao hàng không có bước nghiệm thu, nên cú "nộp" chính là lúc hàng đã tới tay khách.
            // Báo cho CẢ BỘ PHẬN (thủ kho, kế toán kho, quản trị viên) chứ không chỉ người giao việc:
            // kho cần biết để chờ phiếu ký nhận về, kế toán cần biết để theo dõi công nợ và tiền hàng.
            if (t.IsDelivery)
                await AnnounceDeliveryAsync(push, me,
                    "Đã giao hàng cho khách",
                    $"{name} đã giao xong — {t.Title}" + (note.Length > 0 ? $". {note}" : ""),
                    $"delivery:{id}:delivered");
            return Results.NoContent();
        });

        // Người giao NGHIỆM THU ĐẠT.
        g.MapPost("/{id:guid}/accept", async (Guid id, ReviewReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.Can(Permissions.UsersManage))) return Results.Forbid();
            // Việc giao hàng bỏ hẳn chặng nghiệm thu: khách nhận hàng rồi thì chỉ còn chờ tờ phiếu ký
            // nhận về kho, và chính cú "xác nhận phiếu về kho" đóng việc luôn.
            if (t.IsDelivery)
                return Results.BadRequest(new
                {
                    message = "Việc giao hàng không cần nghiệm thu. Mở phiếu và bấm “Xác nhận phiếu đã về kho” là xong.",
                });
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
            await push.SendToUserAsync(t.AssigneeUsername, "Việc đã được nghiệm thu", $"{t.TaskNo}: {t.Title}", $"task:{id}:accepted", "Tasks");
            return Results.NoContent();
        });

        // Người giao TRẢ LẠI (không đạt) → nhân viên làm tiếp rồi nộp lại.
        g.MapPost("/{id:guid}/reject", async (Guid id, ReviewReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.Can(Permissions.UsersManage))) return Results.Forbid();
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
            await push.SendToUserAsync(t.AssigneeUsername, "Việc bị trả lại", $"{t.TaskNo}: {note}", $"task:{id}:rejected", "Tasks");
            return Results.NoContent();
        });

        // Người giao huỷ việc.
        g.MapPost("/{id:guid}/cancel", async (Guid id, TaskNoteReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.Can(Permissions.UsersManage))) return Results.Forbid();
            if (IsClosed(t.Status)) return Results.BadRequest(new { message = "Việc đã kết thúc." });

            var note = (req.Note ?? "").Trim();
            await conn.Cmd("UPDATE work_tasks SET status='cancelled', review_note=@note, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@note", note).With("@id", id).ExecuteNonQueryAsync();
            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "cancelled", note.Length > 0 ? $"Huỷ việc: {note}" : "Huỷ việc.");
            await db.RecordAudit(me, "Huỷ việc", "WorkTask", t.TaskNo, t.Title);
            await push.SendToUserAsync(t.AssigneeUsername, "Việc đã bị huỷ", $"{t.TaskNo}: {t.Title}", $"task:{id}:cancelled", "Tasks");
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
            var isAssigner = CanReview(t, me, u.Can(Permissions.UsersManage));
            var isAssignee = string.Equals(t.AssigneeUsername, me, StringComparison.OrdinalIgnoreCase);
            if (!isAssigner && !isAssignee) return Results.Forbid();

            var name = await DisplayName(conn, me);
            await AddEvent(conn, id, me, name, "comment", note);
            var other = isAssignee ? t.AssignerUsername : t.AssigneeUsername;
            await push.SendToUserAsync(other, $"Trao đổi việc {t.TaskNo}", note, $"task:{id}:comment", "Tasks");
            return Results.NoContent();
        });

        // Xoá việc (người giao/Admin).
        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var t = await LoadCore(conn, id);
            if (t is null) return Results.NotFound();
            if (!CanReview(t, me, u.Can(Permissions.UsersManage))) return Results.Forbid();
            await conn.Cmd("DELETE FROM work_tasks WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            await db.RecordAudit(me, "Xoá việc", "WorkTask", t.TaskNo, t.Title);
            return Results.NoContent();
        });
    }

    // ── Trợ giúp chung ───────────────────────────────────────────────────────────

    private enum AssigneeScopeKind { None, Department, Location, All }
    private sealed record AssigneeScope(AssigneeScopeKind Kind, Guid? DepartmentId, Guid? LocationId);
    private sealed record AssignableEmployee(string Username, string FullName);

    private static async Task<AssigneeScope> ResolveAssigneeScopeAsync(
        NpgsqlConnection conn, ClaimsPrincipal u)
    {
        if (!u.Can(Permissions.TasksAssign))
            return new AssigneeScope(AssigneeScopeKind.None, null, null);
        if (u.Can(Permissions.UsersManage))
            return new AssigneeScope(AssigneeScopeKind.All, null, null);

        await using var r = await conn.Cmd("""
            SELECT e.access_role, e.department_id, e.location_id
            FROM app_users account
            JOIN hr_employees e
              ON e.user_id=account.id
              OR (e.user_id IS NULL AND lower(e.username)=lower(account.username))
            WHERE lower(account.username)=lower(@u) AND account.is_deleted=FALSE
            ORDER BY (e.user_id=account.id) DESC
            LIMIT 1
            """).With("@u", u.Username()).ExecuteReaderAsync();
        if (!await r.ReadAsync())
            return new AssigneeScope(AssigneeScopeKind.None, null, null);

        var accessRole = r.Str("access_role");
        var departmentId = r.IsDBNull(r.GetOrdinal("department_id")) ? (Guid?)null : r.Guid("department_id");
        var locationId = r.IsDBNull(r.GetOrdinal("location_id")) ? (Guid?)null : r.Guid("location_id");
        if (string.Equals(accessRole, "location_manager", StringComparison.Ordinal) && locationId is not null)
            return new AssigneeScope(AssigneeScopeKind.Location, null, locationId);
        if (departmentId is not null)
            return new AssigneeScope(AssigneeScopeKind.Department, departmentId, null);
        return new AssigneeScope(AssigneeScopeKind.None, null, null);
    }

    private static async Task<AssignableEmployee?> LoadAssignableEmployeeAsync(
        NpgsqlConnection conn, AssigneeScope scope, string username)
    {
        if (scope.Kind == AssigneeScopeKind.None) return null;
        await using var r = await conn.Cmd("""
            SELECT account.username,
                   COALESCE(NULLIF(e.full_name,''), NULLIF(account.full_name,''), account.username) AS full_name
            FROM app_users account
            JOIN hr_employees e
              ON e.user_id=account.id
              OR (e.user_id IS NULL AND lower(e.username)=lower(account.username))
            WHERE lower(account.username)=lower(@u)
              AND account.is_deleted=FALSE AND account.is_active=TRUE AND e.status='Active'
              AND (@all OR (@location IS NOT NULL AND e.location_id=@location)
                        OR (@department IS NOT NULL AND e.department_id=@department))
            ORDER BY (e.user_id=account.id) DESC
            LIMIT 1
            """).With("@u", username)
            .With("@all", scope.Kind == AssigneeScopeKind.All)
            .With("@location", (object?)scope.LocationId ?? DBNull.Value)
            .With("@department", (object?)scope.DepartmentId ?? DBNull.Value)
            .ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new AssignableEmployee(r.Str("username"), r.Str("full_name"))
            : null;
    }

    /// <summary>Ngày "yyyy-MM-dd" từ query string; null nếu thiếu/sai để bên gọi tự chọn mặc định.</summary>
    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", out var d) ? d : null;

    private static string NormalizePriority(string? p)
    {
        var v = (p ?? "").Trim().ToLowerInvariant();
        return Priorities.Contains(v) ? v : "normal";
    }

    private static bool CanReview(TaskCore t, string me, bool admin) =>
        admin || string.Equals(t.AssignerUsername, me, StringComparison.OrdinalIgnoreCase);

    // internal: việc giao hàng sinh từ phiếu xuất kho (DeliveryAssignmentEndpoints) phải dùng CHUNG
    // bộ đánh số và sổ sự kiện này, nếu không sẽ có hai nguồn sinh số việc lệch nhau.
    internal static async Task<string> DisplayName(NpgsqlConnection conn, string username)
    {
        var n = await conn.Cmd("SELECT COALESCE(NULLIF(full_name,''), username) FROM app_users WHERE username=@u LIMIT 1")
            .With("@u", username).ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(n) ? username : n;
    }

    internal static async Task<string> NextTaskNo(NpgsqlConnection conn)
    {
        var n = Convert.ToInt64(await conn.Cmd("SELECT nextval('work_task_seq')").ExecuteScalarAsync());
        return $"CV{n:D4}";
    }

    internal static async Task AddEvent(NpgsqlConnection conn, Guid taskId, string actor, string actorName, string kind, string note) =>
        await conn.Cmd(
            "INSERT INTO work_task_events (task_id, actor_username, actor_name, kind, note) VALUES (@t, @au, @an, @k, @n)")
            .With("@t", taskId).With("@au", actor).With("@an", actorName).With("@k", kind).With("@n", note)
            .ExecuteNonQueryAsync();

    /// <summary>
    /// Ai phải biết tiến trình một chuyến giao hàng: THỦ KHO / trưởng phòng điều phối
    /// (<see cref="Permissions.TasksAssign"/>) và KẾ TOÁN theo dõi phiếu xuất
    /// (<see cref="Permissions.VouchersRead"/>). Quản trị viên được cộng thêm ở
    /// <see cref="PushService.SendToPermissionAsync"/>.
    /// </summary>
    internal static readonly string[] DeliveryAudience = [Permissions.TasksAssign, Permissions.VouchersRead];

    /// <summary>Một mốc tiến trình giao hàng đáng báo cho cả bộ phận.</summary>
    internal sealed record DeliveryAnnouncement(string Title, string Body, string NotifId);

    internal static Task AnnounceDeliveryAsync(PushService push, string actorUsername,
        string title, string body, string notifId)
        => push.SendToPermissionAsync(DeliveryAudience, title, body, notifId,
            target: "Tasks", link: "/cong-viec", category: "delivery", exceptUsername: actorUsername);

    private static async Task<IResult> AssigneeTransition(Guid id, ClaimsPrincipal u, Database db,
        string[] expect, string to, string kind, string note,
        Func<TaskCore, string, DeliveryAnnouncement?>? announce = null, PushService? push = null)
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

        // Thông báo phát SAU khi đã ghi xong: hỏng ở đây thì mất một tin nhắn, không mất trạng thái việc.
        if (push is not null && announce?.Invoke(t, name) is { } message)
            await AnnounceDeliveryAsync(push, me, message.Title, message.Body, message.NotifId);
        return Results.NoContent();
    }

    private static async Task<TaskCore?> LoadCore(NpgsqlConnection conn, Guid id)
    {
        await using var r = await conn.Cmd(
            """
            SELECT task_no, title, assigner_username, assignee_username, assignee_name, status, progress,
                   COALESCE(source_kind,'') AS source_kind
            FROM work_tasks WHERE id=@id
            """)
            .With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new TaskCore(r.Str("task_no"), r.Str("title"), r.Str("assigner_username"),
            r.Str("assignee_username"), r.Str("assignee_name"), r.Str("status"), r.Int("progress"),
            r.Str("source_kind"));
    }

    private const string SelectTask = """
        SELECT t.id, t.task_no, t.title, t.description, t.assigner_username, t.assigner_name,
               t.assignee_username, t.assignee_name, t.priority, t.due_at, t.status, t.progress,
               t.submit_note, t.submitted_at, t.review_note, t.rating, t.reviewed_at, t.reviewed_by,
               t.created_at, t.updated_at,
               COALESCE(t.source_kind,'') AS source_kind, t.source_document_id,
               COALESCE(d.voucher_no,'') AS doc_voucher_no,
               COALESCE(NULLIF(d.customer_name,''), d.customer_input_name, '') AS doc_customer_name,
               d.customer_id AS doc_customer_id
        FROM work_tasks t
        LEFT JOIN documents d ON d.id = t.source_document_id
        """;

    /// <summary>Thời điểm việc được đóng sổ (nghiệm thu/hoàn thành/huỷ). Việc còn mở thì vô nghĩa.</summary>
    private const string ClosedAt = "COALESCE(t.reviewed_at, t.updated_at)";

    /// <summary>Nửa đêm HÔM NAY theo giờ Việt Nam, quy về timestamptz để so với các cột mốc.</summary>
    private const string TodayStart =
        "(date_trunc('day', CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh') AT TIME ZONE 'Asia/Ho_Chi_Minh')";

    /// <summary>
    /// Việc còn phải để mắt trong ngày: chưa đóng sổ (kể cả việc tồn từ hôm qua — chính là thứ phải
    /// kéo sang hôm nay để làm nốt), hoặc vừa đóng sổ trong ngày hôm nay.
    /// </summary>
    private const string ClosedTodayOrOpen =
        "(t.status NOT IN ('accepted','completed','cancelled') OR " + ClosedAt + " >= " + TodayStart + ")";

    // Ưu tiên việc còn "sống" (chưa đóng sổ) lên trước, rồi theo hạn gần nhất, mới nhất.
    private const string ListOrder =
        "(CASE WHEN t.status IN ('accepted','completed','cancelled') THEN 1 ELSE 0 END), t.due_at NULLS LAST, t.created_at DESC";

    private static WorkTaskDto ReadTask(NpgsqlDataReader r)
    {
        var due = r.DtNull("due_at");
        var status = r.Str("status");
        var overdue = due is not null && due.Value < DateTime.UtcNow && !IsClosed(status);
        var documentOrdinal = r.GetOrdinal("source_document_id");
        var customerOrdinal = r.GetOrdinal("doc_customer_id");
        // Việc sinh từ phiếu xuất kho mang thêm số phiếu + khách hàng để lái xe không phải mở
        // sang màn khác mới biết mình đang chở phiếu nào cho ai.
        var delivery = r.Str("source_kind") == "delivery" && !r.IsDBNull(documentOrdinal)
            ? new TaskDeliveryDto(
                r.GetGuid(documentOrdinal),
                r.Str("doc_voucher_no"),
                r.Str("doc_customer_name"),
                r.IsDBNull(customerOrdinal) ? null : r.GetGuid(customerOrdinal),
                null)
            : null;
        return new WorkTaskDto(
            r.Guid("id"), r.Str("task_no"), r.Str("title"), r.Str("description"),
            r.Str("assigner_username"), r.Str("assigner_name"), r.Str("assignee_username"), r.Str("assignee_name"),
            r.Str("priority"), due, status, r.Int("progress"),
            r.Str("submit_note"), r.DtNull("submitted_at"), r.Str("review_note"),
            r.IsDBNull(r.GetOrdinal("rating")) ? null : r.Int("rating"),
            r.DtNull("reviewed_at"), r.Str("reviewed_by"), r.Dt("created_at"), r.Dt("updated_at"), overdue,
            delivery);
    }

    /// <summary>
    /// Lệnh thu tiền còn hiệu lực của một lái xe, khoá theo khách hàng để ghép vào việc giao hàng.
    /// Dữ liệu vẫn nằm nguyên ở <c>cash_collection_orders</c>; đây chỉ là bản đọc để hiển thị.
    /// </summary>
    private static async Task<List<TaskCollectionDto>> LoadOpenCollections(NpgsqlConnection conn, string driverUsername)
    {
        var collections = new List<TaskCollectionDto>();
        await using var r = await conn.Cmd("""
            SELECT id, order_no, customer_id, customer_name, expected_amount, status, handover_due_at
            FROM cash_collection_orders
            WHERE lower(driver_username) = lower(@me)
              AND status IN ('Assigned','Accepted','PendingHandover','Variance')
            ORDER BY handover_due_at
            """).With("@me", driverUsername).ExecuteReaderAsync();
        while (await r.ReadAsync())
            collections.Add(new TaskCollectionDto(
                r.Guid("id"), r.Str("order_no"), r.Guid("customer_id"), r.Str("customer_name"),
                r.Dec("expected_amount"), r.Str("status"), r.Dt("handover_due_at")));
        return collections;
    }

    private sealed record TaskCore(string TaskNo, string Title, string AssignerUsername,
        string AssigneeUsername, string AssigneeName, string Status, int Progress, string SourceKind)
    {
        /// <summary>
        /// Việc GIAO HÀNG sinh tự động từ phiếu xuất kho — không đi qua nghiệm thu như việc thường
        /// (xem <see cref="DeliverySettlementEndpoints"/>).
        /// </summary>
        public bool IsDelivery => SourceKind == "delivery";
    }

    public record WorkTaskDto(Guid Id, string TaskNo, string Title, string Description,
        string AssignerUsername, string AssignerName, string AssigneeUsername, string AssigneeName,
        string Priority, DateTime? DueAt, string Status, int Progress,
        string SubmitNote, DateTime? SubmittedAt, string ReviewNote, int? Rating,
        DateTime? ReviewedAt, string ReviewedBy, DateTime CreatedAt, DateTime UpdatedAt, bool Overdue,
        TaskDeliveryDto? Delivery = null);

    /// <summary>Phần phiếu xuất kho của một việc giao hàng, kèm lệnh thu tiền cùng khách (nếu có).</summary>
    public record TaskDeliveryDto(Guid DocumentId, string VoucherNo, string CustomerName,
        Guid? CustomerId, TaskCollectionDto? Collection);

    public record TaskCollectionDto(Guid Id, string OrderNo, Guid CustomerId, string CustomerName,
        decimal ExpectedAmount, string Status, DateTime HandoverDueAt);

    public record WorkTaskEventDto(long Id, string ActorUsername, string ActorName, string Kind, string Note, DateTime CreatedAt);

    public record CreateTaskReq(string? Title, string? Description, string? AssigneeUsername, string? Priority, DateTime? DueAt);
    public record TaskNoteReq(string? Note, int? Progress);
    public record ReviewReq(string? Note, int? Rating);
}
