using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Đối soát phiếu xuất kho khi lái xe giao xong và nộp tờ phiếu về cho kế toán.
///
/// Vì sao cần: số cân (số lượng) khách nhận thực tế gần như không bao giờ trùng khít số ghi lúc
/// xuất kho — hàng hao hụt, khách trả lại một phần, cân lại tại kho khách. Đơn giá cũng có lúc bị
/// viết sai lúc xuất. Tờ phiếu có chữ ký khách mới là con số đúng để ghi công nợ.
///
/// Cách làm:
///   • "Hàng xuất đi" được chụp lại vào <c>document_issued_lines</c> ngay khi phiếu được PHÁT HÀNH
///     (lệnh in chạy xong). Bảng này bất biến — nó chính là con số trên tờ giấy.
///   • Kế toán sửa <c>document_lines</c> thành hàng THỰC NHẬN. Vì mọi báo cáo/công nợ đã tính theo
///     document_lines nên sửa ở đây là toàn hệ thống tự khớp, không phải vá từng chỗ.
///   • Mỗi dòng thực sự đổi đẻ một bản ghi cũ→mới trong <c>document_line_edits</c> kèm lý do và
///     người sửa. Chênh lệch = document_lines − document_issued_lines.
///   • Kế toán xác nhận "phiếu đã về kho" → việc giao hàng chuyển thẳng sang <c>completed</c>.
///
/// KHÔNG CÒN CHẶNG NGHIỆM THU cho việc giao hàng (chốt của người dùng 2026-08-24): khách đã nhận
/// hàng thì tờ phiếu có chữ ký quay về kho chính là bằng chứng, mà người nghiệm thu cũng chính là
/// kế toán sắp bấm "phiếu đã về kho" — hai cú bấm cho cùng một sự thật. Giữ lại đường TRẢ LẠI
/// (submitted → rejected) cho tình huống lái xe báo đã giao nhưng hàng phải quay đầu.
///
/// Đóng được từ BẤT KỲ chặng nào chưa kết thúc, kể cả khi lái xe quên bấm "đã giao" — tờ phiếu về
/// tay kế toán là đủ, và nhật ký ghi rõ nó nhảy từ chặng nào. Trước đây bắt buộc 'accepted' nên chỉ
/// cần lái xe quên một nút là phiếu kẹt vĩnh viễn.
/// </summary>
public static class DeliverySettlementEndpoints
{
    public static void MapDeliverySettlements(this IEndpointRouteBuilder app)
    {
        // Xem: kế toán HOẶC người điều phối giao hàng (thủ kho/trưởng phòng) — họ cần biết phiếu nào
        // còn treo. Sửa số liệu và chốt "đã về kho" thì chỉ kế toán (kiểm tra riêng bên dưới).
        var api = app.MapGroup("/api")
            .RequireAnyPermission(Permissions.AccountingAccess, Permissions.TasksAssign);

        api.MapGet("/documents/{id:guid}/settlement", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var head = await LoadHead(conn, null, id);
            if (head is null) return Results.NotFound();
            return Results.Ok(await BuildPayload(conn, head, u.Can(Permissions.AccountingAccess),
                u.Username(), u.Can(Permissions.UsersManage)));
        });

        // Ghi hàng THỰC NHẬN (và đơn giá đã sửa) cho từng dòng phiếu.
        api.MapPut("/documents/{id:guid}/settlement", async (
            Guid id, SettlementSaveReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.AccountingAccess))
                return Results.Json(new { message = "Chỉ kế toán được chỉnh sửa hàng thực nhận." }, statusCode: 403);

            var reason = (req.Reason ?? "").Trim();
            if (reason.Length > 500) reason = reason[..500];
            var input = req.Lines ?? [];
            if (input.Count == 0)
                return Results.BadRequest(new { message = "Không có dòng hàng nào để lưu." });

            var me = u.Username();
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var head = await LoadHead(conn, tx, id, forUpdate: true);
            if (head is null) return Results.NotFound();
            if (head.CancelledAt is not null)
                return Results.Conflict(new { message = "Phiếu đã hủy, không đối soát được." });
            if (head.IssuedAt is null)
                return Results.BadRequest(new { message = "Phiếu chưa in nên chưa có hàng xuất đi để đối chiếu." });

            // Đọc hiện trạng để so cũ→mới. Sửa TẠI CHỖ theo line_no: không thêm/bớt dòng, vì tờ phiếu
            // khách đã ký có đúng bấy nhiêu dòng — thêm dòng ở đây là ghi khống.
            var current = await LoadCurrentLines(conn, tx, id);
            var changes = new List<LineChange>();
            foreach (var line in input)
            {
                if (!current.TryGetValue(line.LineNo, out var now))
                    return Results.BadRequest(new { message = $"Phiếu không có dòng số {line.LineNo}." });
                if (line.Quantity < 0 || line.UnitPrice < 0)
                    return Results.BadRequest(new { message = "Số lượng và đơn giá không được âm." });

                var qty = decimal.Round(line.Quantity, 2, MidpointRounding.AwayFromZero);
                var price = decimal.Round(line.UnitPrice, 2, MidpointRounding.AwayFromZero);
                if (qty == now.Quantity && price == now.UnitPrice) continue;
                changes.Add(new LineChange(line.LineNo, now.Content, now.Quantity, qty, now.UnitPrice, price));
            }

            if (changes.Count == 0)
            {
                await tx.RollbackAsync();
                return Results.Ok(await BuildPayload(conn, head, canEdit: true, me, u.Can(Permissions.UsersManage)));
            }
            // Lý do là bắt buộc khi có thay đổi: sổ chỉnh sửa mà không nói vì sao thì tháng sau không
            // ai đối chiếu nổi với tờ phiếu giấy.
            if (reason.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng nhập lý do chỉnh sửa hàng thực nhận." });

            var actorName = await TaskAssignmentEndpoints.DisplayName(conn, me);
            foreach (var c in changes)
            {
                await conn.Cmd("""
                    UPDATE document_lines SET quantity=@q, unit_price=@p
                    WHERE document_id=@doc AND line_no=@no
                    """, tx)
                    .With("@q", c.NewQuantity).With("@p", c.NewUnitPrice)
                    .With("@doc", id).With("@no", c.LineNo)
                    .ExecuteNonQueryAsync();
                await conn.Cmd("""
                    INSERT INTO document_line_edits (document_id, line_no, line_content,
                        old_quantity, new_quantity, old_unit_price, new_unit_price,
                        reason, actor_username, actor_name)
                    VALUES (@doc, @no, @content, @oq, @nq, @op, @np, @reason, @au, @an)
                    """, tx)
                    .With("@doc", id).With("@no", c.LineNo).With("@content", c.Content)
                    .With("@oq", c.OldQuantity).With("@nq", c.NewQuantity)
                    .With("@op", c.OldUnitPrice).With("@np", c.NewUnitPrice)
                    .With("@reason", reason).With("@au", me).With("@an", actorName)
                    .ExecuteNonQueryAsync();
            }

            await conn.Cmd("UPDATE documents SET updated_at=CURRENT_TIMESTAMP WHERE id=@id", tx)
                .With("@id", id).ExecuteNonQueryAsync();
            await tx.CommitAsync();

            await db.RecordAudit(me, "Sửa hàng thực nhận", "Document", head.VoucherNo,
                $"{changes.Count} dòng đổi so với hàng xuất đi. Lý do: {reason}");

            await using var read = await db.OpenAsync();
            var after = await LoadHead(read, null, id);
            return Results.Ok(await BuildPayload(read, after!, canEdit: true, me, u.Can(Permissions.UsersManage)));
        });

        // Kế toán xác nhận đã nhận lại TỜ PHIẾU GIẤY → đóng việc giao hàng của lái xe.
        api.MapPost("/documents/{id:guid}/settlement/return", async (
            Guid id, SettlementReturnReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            if (!u.Can(Permissions.AccountingAccess))
                return Results.Json(new { message = "Chỉ kế toán được xác nhận phiếu về kho." }, statusCode: 403);

            var note = (req.Note ?? "").Trim();
            if (note.Length > 500) note = note[..500];
            var me = u.Username();

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var head = await LoadHead(conn, tx, id, forUpdate: true);
            if (head is null) return Results.NotFound();
            if (head.CancelledAt is not null)
                return Results.Conflict(new { message = "Phiếu đã hủy, không xác nhận về kho được." });
            if (head.IssuedAt is null)
                return Results.BadRequest(new { message = "Phiếu chưa in nên không có tờ phiếu nào để nộp về." });
            if (head.ReturnedAt is not null)
                return Results.Conflict(new { message = "Phiếu này đã được xác nhận về kho." });

            // Việc giao hàng KHÔNG có bước nghiệm thu riêng: khách đã nhận hàng thì tờ phiếu ký nhận
            // quay về kho chính là bằng chứng, kế toán bấm một nút là xong (chốt của người dùng
            // 2026-08-24). Chỉ chặn đúng hai thứ không cứu được: việc đã bị huỷ, và việc đã đóng.
            if (head.TaskId is not null && head.TaskStatus == "cancelled")
                return Results.Conflict(new { message = "Việc giao hàng của phiếu này đã bị huỷ." });

            var actorName = await TaskAssignmentEndpoints.DisplayName(conn, me);
            var openTaskId = head.TaskStatus is not ("completed" or "cancelled" or "") ? head.TaskId : null;
            if (openTaskId is { } taskId)
            {
                await conn.Cmd("""
                    UPDATE work_tasks SET status='completed', progress=100, updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id AND status NOT IN ('completed','cancelled')
                    """, tx).With("@id", taskId).ExecuteNonQueryAsync();
                // Lái xe quên bấm "đã giao" là chuyện thường; tờ phiếu về tay kế toán vẫn đóng được
                // việc, nhưng nhật ký phải nói rõ nó nhảy từ chặng nào để dòng thời gian không nói dối.
                var jumped = head.TaskStatus is not ("submitted" or "accepted")
                    ? $" (lái xe chưa báo giao xong — đóng từ chặng \"{TaskStatusText(head.TaskStatus)}\")"
                    : "";
                await TaskAssignmentEndpoints.AddEvent(conn, taskId, me, actorName, "completed",
                    $"Kế toán đã nhận lại phiếu {head.VoucherNo}. Hoàn thành.{jumped}"
                        + (note.Length > 0 ? $" {note}" : ""));
            }

            await conn.Cmd("""
                UPDATE documents
                SET delivery_returned_at=CURRENT_TIMESTAMP, delivery_returned_by=@by,
                    delivery_return_note=@note, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """, tx).With("@by", me).With("@note", note).With("@id", id).ExecuteNonQueryAsync();

            await tx.CommitAsync();

            await db.RecordAudit(me, "Xác nhận phiếu về kho", "Document", head.VoucherNo,
                head.DriverName.Length > 0 ? $"Lái xe {head.DriverName} đã nộp phiếu." : "Phiếu đã về kho.");
            if (openTaskId is not null && head.DriverUsername.Length > 0)
                await push.SendToUserAsync(head.DriverUsername, "Việc giao hàng đã hoàn thành",
                    $"{head.VoucherNo}: kế toán đã nhận lại phiếu.", $"task:{head.TaskId}:completed", "Tasks");

            await using var read = await db.OpenAsync();
            var after = await LoadHead(read, null, id);
            return Results.Ok(await BuildPayload(read, after!, canEdit: true, me, u.Can(Permissions.UsersManage)));
        });
    }

    private static string TaskStatusText(string status) => status switch
    {
        "assigned" => "chờ lái xe nhận",
        "in_progress" => "lái xe đang giao",
        "submitted" => "đã giao, chờ nộp phiếu",
        "rejected" => "bị trả lại",
        "" => "chưa có việc giao hàng",
        _ => status,
    };

    // ── Đọc dữ liệu ──────────────────────────────────────────────────────────────

    private sealed record SettlementHead(
        Guid Id, string VoucherNo, DateOnly DocDate, string CustomerName,
        DateTime? IssuedAt, DateTime? CancelledAt,
        string Mode, string DriverUsername, string DriverName,
        Guid? TaskId, string TaskNo, string TaskStatus,
        string AssignerUsername, string AssignerName, string SubmitNote,
        DateTime? ReturnedAt, string ReturnedBy, string ReturnNote);

    private sealed record CurrentLine(int LineNo, string Content, decimal Quantity, decimal UnitPrice);

    private sealed record LineChange(int LineNo, string Content,
        decimal OldQuantity, decimal NewQuantity, decimal OldUnitPrice, decimal NewUnitPrice);

    private static async Task<SettlementHead?> LoadHead(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid id, bool forUpdate = false)
    {
        // FOR UPDATE OF d: chỉ khoá dòng phiếu; work_tasks đọc kèm để biết đã nghiệm thu chưa.
        var sql = $"""
            SELECT d.id, d.voucher_no, d.doc_date,
                   COALESCE(NULLIF(d.customer_name,''), d.customer_input_name, '') AS customer_name,
                   d.issued_at, d.cancelled_at,
                   d.delivery_mode, d.delivery_driver_username, d.delivery_driver_name,
                   d.delivery_task_id, d.delivery_returned_at, d.delivery_returned_by, d.delivery_return_note,
                   COALESCE(t.task_no,'') AS task_no, COALESCE(t.status,'') AS task_status,
                   COALESCE(t.assigner_username,'') AS assigner_username,
                   COALESCE(t.assigner_name,'') AS assigner_name,
                   COALESCE(t.submit_note,'') AS submit_note
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
        var taskOrdinal = r.GetOrdinal("delivery_task_id");
        return new SettlementHead(
            r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"), r.Str("customer_name"),
            r.DtNull("issued_at"), r.DtNull("cancelled_at"),
            r.Str("delivery_mode"), r.Str("delivery_driver_username"), r.Str("delivery_driver_name"),
            r.IsDBNull(taskOrdinal) ? null : r.GetGuid(taskOrdinal),
            r.Str("task_no"), r.Str("task_status"),
            r.Str("assigner_username"), r.Str("assigner_name"), r.Str("submit_note"),
            r.DtNull("delivery_returned_at"), r.Str("delivery_returned_by"), r.Str("delivery_return_note"));
    }

    private static async Task<Dictionary<int, CurrentLine>> LoadCurrentLines(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid id)
    {
        const string sql = """
            SELECT line_no, line_content, quantity, unit_price
            FROM document_lines WHERE document_id=@id ORDER BY line_no
            """;
        var map = new Dictionary<int, CurrentLine>();
        await using var r = tx is null
            ? await conn.Cmd(sql).With("@id", id).ExecuteReaderAsync()
            : await conn.Cmd(sql, tx).With("@id", id).ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var no = r.Int("line_no");
            map[no] = new CurrentLine(no, r.Str("line_content"), r.Dec("quantity"), r.Dec("unit_price"));
        }
        return map;
    }

    private static async Task<object> BuildPayload(
        NpgsqlConnection conn, SettlementHead head, bool canEdit, string me, bool isAdmin)
    {
        // Ghép hàng xuất đi (ảnh chụp lúc in) với hàng hiện tại theo line_no. FULL JOIN để dòng chỉ
        // có ở một bên vẫn hiện ra thay vì lặng lẽ biến mất khỏi bảng đối chiếu.
        var lines = new List<object>();
        decimal issuedTotal = 0, actualTotal = 0;
        await using (var r = await conn.Cmd("""
            SELECT COALESCE(l.line_no, s.line_no) AS line_no,
                   COALESCE(NULLIF(l.line_content,''), s.line_content, '') AS line_content,
                   COALESCE(NULLIF(l.spec,''), s.spec, '') AS spec,
                   COALESCE(l.note, '') AS note,
                   s.quantity AS issued_quantity, s.unit_price AS issued_unit_price,
                   COALESCE(l.quantity, 0) AS quantity, COALESCE(l.unit_price, 0) AS unit_price,
                   (l.line_no IS NULL) AS removed
            FROM (SELECT * FROM document_lines WHERE document_id = @id) l
            FULL JOIN (SELECT * FROM document_issued_lines WHERE document_id = @id) s
              ON s.line_no = l.line_no
            ORDER BY 1
            """).With("@id", head.Id).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var hasSnapshot = !r.IsDBNull(r.GetOrdinal("issued_quantity"));
                var issuedQty = hasSnapshot ? r.Dec("issued_quantity") : 0m;
                var issuedPrice = hasSnapshot ? r.Dec("issued_unit_price") : 0m;
                var qty = r.Dec("quantity");
                var price = r.Dec("unit_price");
                issuedTotal += issuedQty * issuedPrice;
                actualTotal += qty * price;
                lines.Add(new
                {
                    lineNo = r.Int("line_no"),
                    content = r.Str("line_content"),
                    spec = r.Str("spec"),
                    note = r.Str("note"),
                    hasSnapshot,
                    removed = r.GetBoolean(r.GetOrdinal("removed")),
                    issuedQuantity = issuedQty,
                    issuedUnitPrice = issuedPrice,
                    issuedAmount = issuedQty * issuedPrice,
                    quantity = qty,
                    unitPrice = price,
                    amount = qty * price,
                    quantityDiff = qty - issuedQty,
                    unitPriceDiff = price - issuedPrice,
                    amountDiff = qty * price - issuedQty * issuedPrice,
                });
            }
        }

        var history = new List<object>();
        await using (var r = await conn.Cmd("""
            SELECT id, line_no, line_content, old_quantity, new_quantity, old_unit_price, new_unit_price,
                   reason, actor_username, actor_name, created_at
            FROM document_line_edits WHERE document_id=@id ORDER BY id DESC
            """).With("@id", head.Id).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                history.Add(new
                {
                    id = r.Long("id"),
                    lineNo = r.Int("line_no"),
                    content = r.Str("line_content"),
                    oldQuantity = r.Dec("old_quantity"),
                    newQuantity = r.Dec("new_quantity"),
                    oldUnitPrice = r.Dec("old_unit_price"),
                    newUnitPrice = r.Dec("new_unit_price"),
                    reason = r.Str("reason"),
                    actorUsername = r.Str("actor_username"),
                    actorName = r.Str("actor_name"),
                    createdAt = r.Dt("created_at"),
                });
        }

        // Việc giao hàng của lái xe: dòng thời gian + quyền nghiệm thu. Gộp vào chính payload này
        // để màn Phiếu chỉ cần MỘT lần gọi mạng là dựng được cả trang, thay vì kế toán phải mở
        // sang trang "Việc được giao" mới nghiệm thu được.
        object? task = null;
        if (head.TaskId is not null)
        {
            var events = new List<object>();
            await using (var r = await conn.Cmd("""
                SELECT id, actor_name, kind, note, created_at
                FROM work_task_events WHERE task_id=@id ORDER BY id
                """).With("@id", head.TaskId).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    events.Add(new
                    {
                        id = r.Long("id"),
                        actorName = r.Str("actor_name"),
                        kind = r.Str("kind"),
                        note = r.Str("note"),
                        createdAt = r.Dt("created_at"),
                    });
            }
            // Nghiệm thu là việc của NGƯỜI GIAO (thường chính là kế toán vừa gán phiếu) hoặc Admin.
            var canReview = isAdmin || string.Equals(head.AssignerUsername, me, StringComparison.OrdinalIgnoreCase);
            task = new
            {
                id = head.TaskId,
                taskNo = head.TaskNo,
                status = head.TaskStatus,
                assigneeName = head.DriverName,
                assignerName = head.AssignerName,
                submitNote = head.SubmitNote,
                canReview,
                // Việc giao hàng không còn bước nghiệm thu; chỉ còn đường TRẢ LẠI cho tình huống lái
                // xe báo đã giao nhưng thực tế hàng phải quay đầu.
                canReject = canReview && head.TaskStatus == "submitted",
                events,
            };
        }

        var returned = head.ReturnedAt is not null;
        var open = head.CancelledAt is null && head.IssuedAt is not null;
        return new
        {
            document = new
            {
                id = head.Id,
                voucherNo = head.VoucherNo,
                docDate = head.DocDate,
                customerName = head.CustomerName,
                issuedAt = head.IssuedAt,
                cancelledAt = head.CancelledAt,
            },
            delivery = new
            {
                mode = head.Mode,
                driverUsername = head.DriverUsername,
                driverName = head.DriverName,
                taskId = head.TaskId,
                taskNo = head.TaskNo,
                taskStatus = head.TaskStatus,
                returnedAt = head.ReturnedAt,
                returnedBy = head.ReturnedBy,
                returnNote = head.ReturnNote,
            },
            task,
            lines,
            totals = new { issuedTotal, actualTotal, diffTotal = actualTotal - issuedTotal },
            history,
            flags = new
            {
                // Vẫn cho sửa sau khi phiếu đã về kho: sai sót phát hiện muộn vẫn phải chữa được,
                // và mọi lần chữa đều để lại vết trong lịch sử.
                canEdit = canEdit && open,
                // Không còn đòi nghiệm thu trước: tờ phiếu ký nhận về tới nơi là đóng được, trừ khi
                // việc đã bị huỷ (phiếu không còn đi bằng lái xe nữa).
                canConfirmReturn = canEdit && open && !returned && head.TaskStatus != "cancelled",
                returned,
            },
        };
    }

    public record SettlementLineReq(int LineNo, decimal Quantity, decimal UnitPrice);
    public record SettlementSaveReq(List<SettlementLineReq>? Lines, string? Reason);
    public record SettlementReturnReq(string? Note);
}
