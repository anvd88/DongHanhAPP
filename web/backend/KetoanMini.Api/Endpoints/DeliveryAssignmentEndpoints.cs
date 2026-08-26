using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Gán phiếu xuất kho ĐÃ IN cho đường giao hàng: lái xe chở đi, hoặc khách tự lấy tại kho.
///
/// Vì sao cần: phiếu xuất kho là giấy tờ vật lý. In xong mà không ghi ai đang cầm thì cuối ngày
/// thiếu phiếu không truy được. Gán phiếu = vừa chốt trách nhiệm giấy tờ, vừa đẩy luôn một việc
/// vào mục "Việc được giao" của lái xe.
///
/// Bất biến:
///   • Chỉ gán được phiếu đã phát hành (issued_at IS NOT NULL) và chưa hủy.
///   • Mỗi phiếu chỉ có ĐÚNG MỘT việc giao hàng còn sống (ux_work_tasks_delivery_document).
///     Đổi lái xe = sửa chính việc đó, không đẻ việc thứ hai.
///   • Đổi sang "khách lấy tại kho" thì việc giao hàng đang mở bị huỷ, không để treo ở máy lái xe.
///   • ĐỔI ĐƯỢC NGƯỜI KỂ CẢ KHI LÁI XE ĐÃ NHẬN CHUYẾN (in_progress/rejected): xe hỏng giữa đường,
///     lái xe ốm, đổi tuyến… là chuyện thường ngày. Nhưng lúc đó tờ phiếu giấy ĐANG NẰM TRONG TAY
///     lái xe cũ, nên bắt buộc phải có LÝ DO và lái xe cũ được báo để bàn giao lại phiếu.
///   • Lái xe đã BÁO GIAO XONG trở đi (submitted/completed) thì hết đổi: hàng đã tới khách, đổi
///     người lúc này chỉ làm sai lệch sổ sách. Muốn đổi thì phải "trả lại chuyến" trước.
/// </summary>
public static class DeliveryAssignmentEndpoints
{
    public const string ModeUnassigned = "";
    public const string ModeDriver = "driver";
    public const string ModePickup = "pickup";

    /// <summary>Trạng thái việc giao hàng còn cho phép đổi người/huỷ.</summary>
    private static readonly string[] ReassignableTaskStatuses = ["assigned", "in_progress", "rejected"];

    /// <summary>
    /// Lái xe ĐÃ NHẬN CHUYẾN: vẫn đổi được người, nhưng phải nêu lý do vì tờ phiếu giấy đang ở
    /// ngoài đường và phải thu hồi về.
    /// </summary>
    private static readonly string[] StartedTaskStatuses = ["in_progress", "rejected"];

    /// <summary>Việc giao hàng còn "sống" (đã gán và chưa bị huỷ) — mốc để xét khoá/đòi lý do.</summary>
    private static bool IsLiveTask(DeliveryState state) =>
        state.TaskId is not null && state.TaskStatus.Length > 0 && state.TaskStatus != "cancelled";

    public static void MapDeliveryAssignments(this IEndpointRouteBuilder app)
    {
        // Kế toán là người in phiếu; Thủ kho/Trưởng phòng là người điều phối giao hàng. Cả hai đều
        // phải gán được, nên chốt bằng "có MỘT TRONG HAI quyền" thay vì nhét vào nhóm kế toán.
        var api = app.MapGroup("/api")
            .RequireAnyPermission(Permissions.AccountingAccess, Permissions.TasksAssign);

        // Danh sách lái xe có thể nhận phiếu: tài khoản đang hoạt động và giữ vai trò Lái xe
        // (vai trò chính hoặc vai trò phụ còn hạn).
        api.MapGet("/delivery-assignments/drivers", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var drivers = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT au.username,
                       COALESCE(NULLIF(e.full_name,''), NULLIF(au.full_name,''), au.username) AS full_name,
                       COALESCE(d.name,'') AS dept
                FROM app_users au
                LEFT JOIN hr_employees e
                  ON e.user_id = au.id OR (e.user_id IS NULL AND lower(e.username) = lower(au.username))
                LEFT JOIN hr_departments d ON d.id = e.department_id
                WHERE au.is_deleted = FALSE AND au.is_active = TRUE
                  AND (au.role = @driver OR EXISTS (
                        SELECT 1 FROM user_roles ur
                        WHERE lower(ur.username) = lower(au.username) AND ur.role = @driver
                          AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)))
                  AND (e.id IS NULL OR e.status = 'Active')
                ORDER BY full_name
                """).With("@driver", AppRoles.Driver).ExecuteReaderAsync();
            while (await r.ReadAsync())
                drivers.Add(new { username = r.Str("username"), fullName = r.Str("full_name"), department = r.Str("dept") });
            return Results.Ok(new { drivers });
        });

        // Sổ đối soát phiếu: phiếu xuất kho đã in trong khoảng ngày, kèm ai đang cầm.
        // scope=unassigned lọc riêng phiếu in rồi mà CHƯA gán — đây là cái cần soi khi thiếu phiếu.
        api.MapGet("/delivery-assignments", async (string? from, string? to, string? driver, string? scope, Database db) =>
        {
            var fromDate = ParseDate(from) ?? DateTime.UtcNow.Date.AddDays(-7);
            var toDate = ParseDate(to) ?? DateTime.UtcNow.Date;
            if (toDate < fromDate) (fromDate, toDate) = (toDate, fromDate);
            var driverFilter = (driver ?? "").Trim();
            var unassignedOnly = string.Equals(scope, "unassigned", StringComparison.OrdinalIgnoreCase);

            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd($"""
                SELECT d.id, d.voucher_no, d.doc_date, d.customer_name, d.customer_input_name,
                       d.delivery_mode, d.delivery_driver_username, d.delivery_driver_name,
                       d.delivery_assigned_at, d.delivery_assigned_by, d.delivery_note,
                       d.delivery_task_id, d.issued_at,
                       COALESCE(t.status,'') AS task_status, COALESCE(t.task_no,'') AS task_no
                FROM documents d
                LEFT JOIN work_tasks t ON t.id = d.delivery_task_id
                WHERE d.document_type = 'document'
                  AND d.issued_at IS NOT NULL
                  AND d.cancelled_at IS NULL
                  AND d.doc_date BETWEEN @from AND @to
                  {(unassignedOnly ? "AND d.delivery_mode = ''" : "")}
                  {(driverFilter.Length > 0 ? "AND lower(d.delivery_driver_username) = lower(@driver)" : "")}
                ORDER BY d.doc_date DESC, d.voucher_no DESC
                LIMIT 500
                """)
                .With("@from", fromDate).With("@to", toDate).With("@driver", driverFilter)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var customer = r.Str("customer_name");
                if (customer.Length == 0) customer = r.Str("customer_input_name");
                items.Add(new
                {
                    id = r.GetGuid(r.GetOrdinal("id")),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.GetDateTime(r.GetOrdinal("doc_date")),
                    customerName = customer,
                    mode = r.Str("delivery_mode"),
                    driverUsername = r.Str("delivery_driver_username"),
                    driverName = r.Str("delivery_driver_name"),
                    assignedAt = r.IsDBNull(r.GetOrdinal("delivery_assigned_at")) ? (DateTime?)null : r.GetDateTime(r.GetOrdinal("delivery_assigned_at")),
                    assignedBy = r.Str("delivery_assigned_by"),
                    note = r.Str("delivery_note"),
                    taskNo = r.Str("task_no"),
                    taskStatus = r.Str("task_status"),
                });
            }
            return Results.Ok(new { items });
        });

        // Trạng thái giao hàng của MỘT phiếu.
        api.MapGet("/documents/{id:guid}/delivery", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var state = await LoadDeliveryState(conn, id, null);
            return state is null ? Results.NotFound() : Results.Ok(state.ToPayload());
        });

        // Gán phiếu: mode = 'driver' (chọn lái xe) | 'pickup' (khách lấy tại kho) | '' (gỡ gán).
        api.MapPost("/documents/{id:guid}/delivery", async (
            Guid id, AssignDeliveryReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var me = u.Username();
            var mode = NormalizeMode(req.Mode);
            if (mode is null)
                return Results.BadRequest(new { message = "Hình thức giao hàng không hợp lệ." });
            var driverUsername = (req.DriverUsername ?? "").Trim();
            if (mode == ModeDriver && driverUsername.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng chọn lái xe nhận phiếu." });
            var note = (req.Note ?? "").Trim();
            if (note.Length > 1000) note = note[..1000];
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length > 500) reason = reason[..500];

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Khoá dòng phiếu: hai người cùng gán một phiếu thì người sau phải thấy kết quả người trước.
            var state = await LoadDeliveryState(conn, id, tx, forUpdate: true);
            if (state is null) return Results.NotFound();
            if (state.CancelledAt is not null)
                return Results.Conflict(new { message = "Phiếu đã hủy, không gán giao hàng được." });
            if (state.IssuedAt is null)
                return Results.BadRequest(new { message = "Phiếu chưa in nên chưa thể gán giao hàng." });

            // Hàng đã tới khách (lái xe nộp nghiệm thu trở đi) thì chốt sổ, không đổi người nữa.
            if (!state.CanChange)
                return Results.Conflict(new { message = state.LockMessage });

            string driverName = "";
            if (mode == ModeDriver)
            {
                var driver = await LoadDriver(conn, tx, driverUsername);
                if (driver is null)
                    return Results.BadRequest(new { message = "Tài khoản nhận phiếu không phải lái xe đang hoạt động." });
                driverUsername = driver.Value.Username;
                driverName = driver.Value.FullName;
            }
            else driverUsername = "";

            // Người cầm phiếu có đổi thật không? Chỉ sửa ghi chú thì không phải là "đổi người".
            // Căn cứ là NGƯỜI ĐANG CẦM VIỆC (work_tasks), vì cột lái xe ở documents bị xoá trắng mỗi
            // lần chuyển sang "khách lấy tại kho".
            var liveTask = IsLiveTask(state);
            var sameDriver = liveTask
                && string.Equals(driverUsername, state.TaskAssignee, StringComparison.OrdinalIgnoreCase);
            // Thu chuyến khỏi tay một lái xe đang cầm việc (đổi sang người khác, sang khách tự lấy,
            // hoặc gỡ gán) — đây mới là việc cần lý do và cần báo cho người bị thu.
            var takingFromDriver = liveTask && !sameDriver && state.TaskAssignee.Length > 0;

            // Lái xe đã nhận chuyến: tờ phiếu đang ở ngoài đường. Vẫn cho đổi, nhưng phải nói vì sao
            // — cuối tháng đối soát còn biết ai làm và tại sao.
            if (takingFromDriver && state.ChangeNeedsReason && reason.Length == 0)
            {
                return Results.BadRequest(new
                {
                    message = $"Lái xe {state.HolderName} đã nhận chuyến này. Vui lòng nhập lý do đổi người giao hàng.",
                    needsReason = true,
                });
            }

            Guid? taskId = state.TaskId;
            var actorName = await TaskAssignmentEndpoints.DisplayName(conn, me);
            var reasonSuffix = reason.Length > 0 ? $" Lý do: {reason}" : "";

            if (mode == ModeDriver)
            {
                var title = $"Giao hàng phiếu {state.VoucherNo}"
                    + (state.CustomerName.Length > 0 ? $" · {state.CustomerName}" : "");
                if (taskId is null)
                {
                    taskId = Guid.NewGuid();
                    var taskNo = await TaskAssignmentEndpoints.NextTaskNo(conn);
                    await conn.Cmd("""
                        INSERT INTO work_tasks (id, task_no, title, description, assigner_username, assigner_name,
                            assignee_username, assignee_name, priority, status, source_kind, source_document_id)
                        VALUES (@id, @no, @title, @desc, @au, @an, @eu, @en, 'normal', 'assigned', 'delivery', @doc)
                        """, tx)
                        .With("@id", taskId).With("@no", taskNo).With("@title", title)
                        .With("@desc", note).With("@au", me).With("@an", actorName)
                        .With("@eu", driverUsername).With("@en", driverName).With("@doc", id)
                        .ExecuteNonQueryAsync();
                    await TaskAssignmentEndpoints.AddEvent(conn, taskId.Value, me, actorName, "assigned",
                        $"Giao phiếu {state.VoucherNo} cho {driverName}.");
                }
                else
                {
                    // Gán lại: sửa chính việc cũ và mở lại nếu trước đó đã huỷ (khách đổi ý về hình thức nhận).
                    //
                    // Đổi NGƯỜI thì việc phải sạch cho người mới: tiến độ/ghi chú nộp/ý kiến trả lại
                    // là của lái xe cũ, để lại thì người mới mở ra thấy "đã đi 60%" của chuyến người
                    // khác. Chỉ sửa ghi chú (cùng lái xe) thì giữ nguyên tiến độ đang có.
                    await conn.Cmd("""
                        UPDATE work_tasks
                        SET title=@title, description=@desc, assignee_username=@eu, assignee_name=@en,
                            status='assigned', updated_at=CURRENT_TIMESTAMP,
                            progress     = CASE WHEN @fresh THEN 0    ELSE progress     END,
                            submit_note  = CASE WHEN @fresh THEN ''   ELSE submit_note  END,
                            submitted_at = CASE WHEN @fresh THEN NULL ELSE submitted_at END,
                            review_note  = CASE WHEN @fresh THEN ''   ELSE review_note  END
                        WHERE id=@id
                        """, tx)
                        .With("@id", taskId).With("@title", title).With("@desc", note)
                        .With("@eu", driverUsername).With("@en", driverName)
                        .With("@fresh", !sameDriver)
                        .ExecuteNonQueryAsync();
                    if (takingFromDriver)
                        await TaskAssignmentEndpoints.AddEvent(conn, taskId.Value, me, actorName, "reassigned",
                            $"Chuyển phiếu {state.VoucherNo} từ {state.HolderName} sang {driverName}."
                            + (state.ChangeNeedsReason ? " (Lái xe cũ đã nhận chuyến, phải thu lại tờ phiếu.)" : "")
                            + reasonSuffix);
                    else
                        await TaskAssignmentEndpoints.AddEvent(conn, taskId.Value, me, actorName, "assigned",
                            $"Chuyển phiếu {state.VoucherNo} cho {driverName}.{reasonSuffix}");
                }
            }
            else if (taskId is not null)
            {
                // Không còn giao bằng lái xe: đóng việc để nó biến khỏi máy lái xe.
                await conn.Cmd("""
                    UPDATE work_tasks SET status='cancelled', updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id AND status NOT IN ('accepted','completed')
                    """, tx).With("@id", taskId).ExecuteNonQueryAsync();
                await TaskAssignmentEndpoints.AddEvent(conn, taskId.Value, me, actorName, "cancelled",
                    (mode == ModePickup
                        ? $"Khách tự lấy phiếu {state.VoucherNo} tại kho."
                        : $"Gỡ gán giao hàng phiếu {state.VoucherNo}.")
                    + (takingFromDriver ? $" Thu chuyến khỏi {state.HolderName}." : "")
                    + reasonSuffix);
            }

            await conn.Cmd("""
                UPDATE documents
                SET delivery_mode=@mode, delivery_driver_username=@du, delivery_driver_name=@dn,
                    delivery_assigned_at=CURRENT_TIMESTAMP, delivery_assigned_by=@by,
                    delivery_note=@note, delivery_task_id=@task, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """, tx)
                .With("@mode", mode).With("@du", driverUsername).With("@dn", driverName)
                .With("@by", me).With("@note", note)
                .With("@task", mode == ModeDriver ? taskId : (object?)null)
                .With("@id", id)
                .ExecuteNonQueryAsync();

            await tx.CommitAsync();

            var summary = mode switch
            {
                ModeDriver => $"Giao lái xe {driverName}.",
                ModePickup => "Khách tự lấy tại kho.",
                _ => "Gỡ gán giao hàng.",
            };
            if (takingFromDriver) summary = $"Thu chuyến khỏi {state.HolderName}. " + summary + reasonSuffix;
            await db.RecordAudit(me, takingFromDriver ? "Đổi người giao hàng" : "Gán giao hàng phiếu xuất kho",
                "Document", state.VoucherNo, summary);

            // Lái xe cũ PHẢI được báo: việc vừa biến khỏi máy anh ta, mà tờ phiếu giấy (và có khi cả
            // hàng trên xe) thì vẫn đang ở chỗ anh ta.
            if (takingFromDriver)
            {
                var handover = mode == ModeDriver
                    ? $"{state.VoucherNo} chuyển sang {driverName}."
                    : mode == ModePickup
                        ? $"{state.VoucherNo}: khách tự lấy tại kho."
                        : $"{state.VoucherNo} đã được gỡ khỏi lịch giao.";
                await push.SendToUserAsync(state.TaskAssignee, "Chuyến giao hàng đã chuyển cho người khác",
                    handover + (reason.Length > 0 ? $" Lý do: {reason}." : "") + " Vui lòng bàn giao lại tờ phiếu.",
                    $"task:{taskId}:reassigned", "Tasks");
            }

            if (mode == ModeDriver && !sameDriver)
                await push.SendToUserAsync(driverUsername, "Bạn được giao phiếu xuất kho",
                    $"{state.VoucherNo}: giao cho {state.CustomerName}", $"task:{taskId}:assigned", "Tasks");

            return Results.Ok(new { mode, driverUsername, driverName, taskId, takenFrom = takingFromDriver ? state.HolderName : "" });
        });
    }

    private static string? NormalizeMode(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "none" or "unassigned" => ModeUnassigned,
        "driver" => ModeDriver,
        "pickup" or "self" => ModePickup,
        _ => null,
    };

    private static DateTime? ParseDate(string? raw) =>
        DateTime.TryParse(raw, out var value) ? value.Date : null;

    private static async Task<(string Username, string FullName)?> LoadDriver(
        NpgsqlConnection conn, NpgsqlTransaction tx, string username)
    {
        await using var r = await conn.Cmd("""
            SELECT au.username,
                   COALESCE(NULLIF(e.full_name,''), NULLIF(au.full_name,''), au.username) AS full_name
            FROM app_users au
            LEFT JOIN hr_employees e
              ON e.user_id = au.id OR (e.user_id IS NULL AND lower(e.username) = lower(au.username))
            WHERE lower(au.username) = lower(@u)
              AND au.is_deleted = FALSE AND au.is_active = TRUE
              AND (au.role = @driver OR EXISTS (
                    SELECT 1 FROM user_roles ur
                    WHERE lower(ur.username) = lower(au.username) AND ur.role = @driver
                      AND (ur.expires_at IS NULL OR ur.expires_at > CURRENT_TIMESTAMP)))
              AND (e.id IS NULL OR e.status = 'Active')
            LIMIT 1
            """, tx).With("@u", username).With("@driver", AppRoles.Driver).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return (r.Str("username"), r.Str("full_name"));
    }

    /// <param name="TaskAssignee">
    /// Người ĐANG CẦM VIỆC theo bảng work_tasks. Khác với <paramref name="DriverUsername"/> ở cột
    /// documents: cột đó bị xoá trắng khi chuyển sang "khách lấy tại kho", còn đây mới là căn cứ để
    /// biết đang thu chuyến của ai.
    /// </param>
    private sealed record DeliveryState(
        Guid Id, string VoucherNo, string CustomerName, DateTime? IssuedAt, DateTime? CancelledAt,
        string Mode, string DriverUsername, string DriverName, string Note,
        Guid? TaskId, string TaskStatus, string TaskNo,
        string TaskAssignee, string TaskAssigneeName)
    {
        public object ToPayload() => new
        {
            mode = Mode,
            driverUsername = DriverUsername,
            driverName = DriverName,
            note = Note,
            taskId = TaskId,
            taskNo = TaskNo,
            taskStatus = TaskStatus,
            voucherNo = VoucherNo,
            customerName = CustomerName,
            issuedAt = IssuedAt,
            // Cờ do MÁY CHỦ chốt, giao diện chỉ việc nghe theo — luật "đổi được đến bao giờ" nằm
            // đúng một chỗ, không phải chép lại ở web và ở app.
            canChange = CanChange,
            changeNeedsReason = ChangeNeedsReason,
            lockMessage = LockMessage,
        };

        /// <summary>Tên người đang cầm phiếu để đưa vào thông báo cho người dùng.</summary>
        public string HolderName => IsLiveTask(this) && TaskAssigneeName.Length > 0 ? TaskAssigneeName : DriverName;

        /// <summary>Còn đổi được hình thức giao hàng / người giao hay không.</summary>
        public bool CanChange => !IsLiveTask(this) || ReassignableTaskStatuses.Contains(TaskStatus);

        /// <summary>Lái xe đã nhận chuyến ⇒ đổi người phải nêu lý do.</summary>
        public bool ChangeNeedsReason => IsLiveTask(this) && StartedTaskStatuses.Contains(TaskStatus);

        /// <summary>Vì sao không đổi được nữa (rỗng khi vẫn đổi được).</summary>
        public string LockMessage => CanChange
            ? ""
            : TaskStatus == "submitted"
                ? $"Lái xe {HolderName} đã báo giao xong, phiếu đang chờ nộp về kho. Nếu thực ra chưa giao được thì bấm “Trả lại chuyến”, rồi mới đổi được người."
                : $"Phiếu đã giao xong ({HolderName}) nên không đổi người giao hàng được nữa.";
    }

    private static async Task<DeliveryState?> LoadDeliveryState(
        NpgsqlConnection conn, Guid id, NpgsqlTransaction? tx, bool forUpdate = false)
    {
        // FOR UPDATE chỉ khoá dòng documents; work_tasks đọc kèm nên dùng OF d.
        var sql = $"""
            SELECT d.id, d.voucher_no, d.customer_name, d.customer_input_name, d.issued_at, d.cancelled_at,
                   d.delivery_mode, d.delivery_driver_username, d.delivery_driver_name, d.delivery_note,
                   d.delivery_task_id,
                   COALESCE(t.status,'') AS task_status, COALESCE(t.task_no,'') AS task_no,
                   COALESCE(t.assignee_username,'') AS task_assignee,
                   COALESCE(t.assignee_name,'') AS task_assignee_name
            FROM documents d
            LEFT JOIN work_tasks t ON t.id = d.delivery_task_id
            WHERE d.id = @id AND d.document_type = 'document'
            LIMIT 1
            {(forUpdate ? "FOR UPDATE OF d" : "")}
            """;
        await using var r = tx is null
            ? await conn.Cmd(sql).With("@id", id).ExecuteReaderAsync()
            : await conn.Cmd(sql, tx).With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var customer = r.Str("customer_name");
        if (customer.Length == 0) customer = r.Str("customer_input_name");
        var taskOrdinal = r.GetOrdinal("delivery_task_id");
        return new DeliveryState(
            r.GetGuid(r.GetOrdinal("id")),
            r.Str("voucher_no"),
            customer,
            r.IsDBNull(r.GetOrdinal("issued_at")) ? null : r.GetDateTime(r.GetOrdinal("issued_at")),
            r.IsDBNull(r.GetOrdinal("cancelled_at")) ? null : r.GetDateTime(r.GetOrdinal("cancelled_at")),
            r.Str("delivery_mode"),
            r.Str("delivery_driver_username"),
            r.Str("delivery_driver_name"),
            r.Str("delivery_note"),
            r.IsDBNull(taskOrdinal) ? null : r.GetGuid(taskOrdinal),
            r.Str("task_status"),
            r.Str("task_no"),
            r.Str("task_assignee"),
            r.Str("task_assignee_name"));
    }

    /// <param name="Note">Ghi chú giao hàng — lái xe đọc được (giao trước 17h, gọi trước khi tới…).</param>
    /// <param name="Reason">
    /// Lý do ĐỔI NGƯỜI khi lái xe cũ đã nhận chuyến. Bắt buộc trong đúng trường hợp đó; vào nhật ký
    /// việc + nhật ký hoạt động + thông báo gửi lái xe cũ.
    /// </param>
    public record AssignDeliveryReq(string? Mode, string? DriverUsername, string? Note, string? Reason);
}
