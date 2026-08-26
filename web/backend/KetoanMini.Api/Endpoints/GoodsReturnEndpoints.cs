using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// HÀNG KHÁCH TRẢ VỀ — khách không nhận hàng, hoặc nhận rồi trả lại một phần.
///
/// Vì sao không thể chỉ "nhập số tiền trả lại": hệ thống KHÔNG có bảng giá, hàng hoá là chữ tự do
/// và cùng một mặt hàng mỗi đơn một giá. Muốn trừ công nợ đúng thì phải biết món hàng quay về NẰM Ở
/// ĐƠN NÀO — đơn vừa giao hay đơn tháng trước — rồi lấy đúng đơn giá của dòng đó.
///
/// Hai đường ghi sổ, do người dùng chốt (2026-08-25):
///   • Nguồn là CHÍNH ĐƠN VỪA GIAO và phiếu chưa xác nhận về kho ⇒ hạ thẳng số lượng trên đơn đó
///     (đúng cơ chế "hàng thực nhận" đang có, để lại vết ở document_line_edits). Tờ phiếu chưa chốt
///     thì khách nhận bao nhiêu ghi bấy nhiêu.
///   • Mọi trường hợp khác (đơn đã chốt về kho, đơn cũ) ⇒ sinh PHIẾU TRẢ HÀNG riêng
///     (documents.document_type='return'), đơn gốc giữ nguyên số đã in.
/// Một dòng chỉ đi ĐÚNG MỘT đường nên không thể trừ công nợ hai lần.
///
/// Bất biến: tổng đã trả của một dòng nguồn không bao giờ vượt số lượng đã bán trên dòng đó.
/// </summary>
public static class GoodsReturnEndpoints
{
    public const string TypeReturn = "return";

    public static void MapGoodsReturns(this IEndpointRouteBuilder app)
    {
        // Đụng thẳng vào công nợ ⇒ chỉ kế toán.
        var api = app.MapGroup("/api").RequirePermission(Permissions.AccountingAccess);

        // Các dòng hàng ĐÃ BÁN cho một khách, kèm số đã trả và số còn có thể trả.
        // Đây là bảng tra "món này nằm ở đơn nào, giá bao nhiêu" mà kế toán cần khi cân hàng về.
        api.MapGet("/returns/sources", async (
            Guid? customerId, string? customerName, string? q, Guid? preferDocumentId, Database db) =>
        {
            var keyword = (q ?? "").Trim();
            var like = $"%{keyword}%";
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT d.id, d.voucher_no, d.doc_date, d.delivery_returned_at,
                       l.line_no, l.line_content, l.spec, l.quantity, l.unit_price,
                       COALESCE(rt.returned, 0) AS returned
                FROM documents d
                JOIN document_lines l ON l.document_id = d.id
                LEFT JOIN LATERAL (
                    SELECT SUM(rl.quantity) AS returned
                    FROM document_lines rl
                    JOIN documents rd ON rd.id = rl.document_id
                    WHERE rl.source_document_id = d.id AND rl.source_line_no = l.line_no
                      AND rd.document_type = 'return' AND rd.cancelled_at IS NULL
                ) rt ON TRUE
                WHERE d.document_type = 'document'
                  AND d.issued_at IS NOT NULL
                  AND d.cancelled_at IS NULL
                  AND ((@cid::uuid IS NOT NULL AND d.customer_id = @cid)
                    OR (@cid::uuid IS NULL AND @cname <> ''
                        AND (d.customer_name = @cname OR d.customer_input_name = @cname)))
                  AND (@kw = '' OR l.line_content ILIKE @like OR l.spec ILIKE @like
                       OR d.voucher_no ILIKE @like)
                  AND l.quantity > COALESCE(rt.returned, 0)
                ORDER BY (d.id = @prefer) DESC, d.doc_date DESC, d.voucher_no DESC, l.line_no
                LIMIT 200
                """)
                .With("@cid", (object?)customerId ?? DBNull.Value)
                .With("@cname", (customerName ?? "").Trim())
                .With("@kw", keyword).With("@like", like)
                .With("@prefer", (object?)preferDocumentId ?? DBNull.Value)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var quantity = r.Dec("quantity");
                var returned = r.Dec("returned");
                items.Add(new
                {
                    documentId = r.Guid("id"),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.DateOnly("doc_date"),
                    lineNo = r.Int("line_no"),
                    content = r.Str("line_content"),
                    spec = r.Str("spec"),
                    quantity,
                    unitPrice = r.Dec("unit_price"),
                    returnedQuantity = returned,
                    remaining = quantity - returned,
                    // Phiếu chưa chốt về kho ⇒ còn hạ thẳng số lượng được, không cần phiếu trả.
                    settled = !r.IsDBNull(r.GetOrdinal("delivery_returned_at")),
                });
            }
            return Results.Ok(new { items });
        });

        // Nhận hàng trả về: một lần bấm cho cả xe hàng, máy tự chia hai đường ghi sổ.
        api.MapPost("/returns", async (GoodsReturnReq req, ClaimsPrincipal u, Database db) =>
        {
            var lines = req.Lines ?? [];
            if (lines.Count == 0)
                return Results.BadRequest(new { message = "Chưa có dòng hàng trả nào." });
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng nhập lý do khách trả hàng." });
            if (reason.Length > 500) reason = reason[..500];
            var note = (req.Note ?? "").Trim();
            if (note.Length > 1000) note = note[..1000];
            var date = req.Date ?? DateOnly.FromDateTime(DateTime.Now);

            var me = u.Username();
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var actorName = await TaskAssignmentEndpoints.DisplayName(conn, me);

            // ── Kiểm từng dòng: đúng đơn, đúng giá, không trả quá số đã bán ──────────────────
            var resolved = new List<ResolvedLine>();
            Guid? customerId = null;
            var customerName = "";
            foreach (var line in lines)
            {
                var qty = decimal.Round(line.Quantity, 2, MidpointRounding.AwayFromZero);
                if (qty <= 0)
                    return Results.BadRequest(new { message = "Số cân thực nhận phải lớn hơn 0." });

                var source = await LoadSourceLine(conn, tx, line.SourceDocumentId, line.SourceLineNo);
                if (source is null)
                    return Results.BadRequest(new { message = "Không tìm thấy dòng hàng của đơn nguồn." });
                if (source.CancelledAt is not null)
                    return Results.BadRequest(new { message = $"Đơn {source.VoucherNo} đã hủy, không trả hàng vào đó được." });

                // Cả xe hàng phải của CÙNG một khách: trộn khách vào một phiếu trả là trừ nhầm công nợ.
                if (resolved.Count == 0)
                {
                    customerId = source.CustomerId;
                    customerName = source.CustomerName;
                }
                else if (source.CustomerId != customerId
                    || !string.Equals(source.CustomerName, customerName, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new
                    {
                        message = "Các dòng hàng trả phải thuộc cùng một khách hàng. Lập phiếu riêng cho khách khác.",
                    });
                }

                var remaining = source.Quantity - source.Returned;
                if (qty > remaining)
                {
                    return Results.BadRequest(new
                    {
                        message = $"Đơn {source.VoucherNo} · {source.Content}: chỉ còn {remaining:0.##} có thể trả "
                                  + $"(đã bán {source.Quantity:0.##}, đã trả {source.Returned:0.##}).",
                    });
                }

                // Hạ thẳng CHỈ khi đây đúng là đơn kế toán đang xử lý và tờ phiếu chưa chốt về kho.
                // Không suy theo ngày tháng: "đơn vừa giao" là đơn đang mở trên màn hình.
                var adjustInPlace = req.ContextDocumentId is { } ctx
                    && ctx == source.DocumentId
                    && source.ReturnedToWarehouseAt is null;
                resolved.Add(new ResolvedLine(source, qty, adjustInPlace));
            }

            // ── Đường 1: hạ thẳng số lượng trên đơn chưa chốt ────────────────────────────────
            foreach (var item in resolved.Where(x => x.AdjustInPlace))
            {
                var s = item.Source;
                var newQty = s.Quantity - item.Quantity;
                await conn.Cmd("""
                    UPDATE document_lines SET quantity=@q WHERE document_id=@doc AND line_no=@no
                    """, tx)
                    .With("@q", newQty).With("@doc", s.DocumentId).With("@no", s.LineNo)
                    .ExecuteNonQueryAsync();
                await conn.Cmd("""
                    INSERT INTO document_line_edits (document_id, line_no, line_content,
                        old_quantity, new_quantity, old_unit_price, new_unit_price,
                        reason, actor_username, actor_name)
                    VALUES (@doc, @no, @content, @oq, @nq, @price, @price, @reason, @au, @an)
                    """, tx)
                    .With("@doc", s.DocumentId).With("@no", s.LineNo).With("@content", s.Content)
                    .With("@oq", s.Quantity).With("@nq", newQty).With("@price", s.UnitPrice)
                    .With("@reason", $"Khách trả lại {item.Quantity:0.##}: {reason}")
                    .With("@au", me).With("@an", actorName)
                    .ExecuteNonQueryAsync();
                await conn.Cmd("UPDATE documents SET updated_at=CURRENT_TIMESTAMP WHERE id=@id", tx)
                    .With("@id", s.DocumentId).ExecuteNonQueryAsync();
            }

            // ── Đường 2: phiếu trả hàng riêng cho hàng thuộc đơn đã chốt ─────────────────────
            var vouchered = resolved.Where(x => !x.AdjustInPlace).ToList();
            Guid? returnId = null;
            var returnNo = "";
            decimal returnTotal = 0;
            if (vouchered.Count > 0)
            {
                returnId = Guid.NewGuid();
                returnNo = await NextReturnNo(conn, tx);
                await conn.Cmd("""
                    INSERT INTO documents (id, voucher_no, doc_date, customer_id, customer_name,
                        document_type, content, note, issued_at)
                    VALUES (@id, @no, @date, @cid, @cname, 'return', @content, @note, CURRENT_TIMESTAMP)
                    """, tx)
                    .With("@id", returnId).With("@no", returnNo).With("@date", date)
                    .With("@cid", (object?)customerId ?? DBNull.Value).With("@cname", customerName)
                    .With("@content", $"Khách trả hàng: {reason}").With("@note", note)
                    .ExecuteNonQueryAsync();

                var lineNo = 0;
                foreach (var item in vouchered)
                {
                    lineNo++;
                    var s = item.Source;
                    returnTotal += item.Quantity * s.UnitPrice;
                    await conn.Cmd("""
                        INSERT INTO document_lines (document_id, line_no, line_content, spec, quantity,
                            unit_price, note, source_document_id, source_line_no)
                        VALUES (@doc, @no, @content, @spec, @q, @price, @note, @sdoc, @sline)
                        """, tx)
                        .With("@doc", returnId).With("@no", lineNo).With("@content", s.Content)
                        .With("@spec", s.Spec).With("@q", item.Quantity).With("@price", s.UnitPrice)
                        .With("@note", $"Trả về từ phiếu {s.VoucherNo} ngày {s.DocDate:dd/MM/yyyy}")
                        .With("@sdoc", s.DocumentId).With("@sline", s.LineNo)
                        .ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();

            var adjusted = resolved.Count(x => x.AdjustInPlace);
            var summary = new List<string>();
            if (returnId is not null) summary.Add($"Phiếu trả {returnNo}: {vouchered.Count} dòng, {returnTotal:#,##0} ₫");
            if (adjusted > 0) summary.Add($"{adjusted} dòng hạ thẳng trên phiếu chưa chốt");
            await db.RecordAudit(me, "Nhận hàng khách trả về", "Document",
                returnNo.Length > 0 ? returnNo : resolved[0].Source.VoucherNo,
                $"{customerName}. {string.Join(". ", summary)}. Lý do: {reason}");

            return Results.Ok(new
            {
                returnId,
                returnNo,
                returnTotal,
                adjustedLines = adjusted,
                vouchisedLines = vouchered.Count,
            });
        });

        // Các phiếu trả hàng đã lập — lọc theo khách, theo đơn nguồn, hoặc theo khoảng ngày.
        api.MapGet("/returns", async (Guid? customerId, Guid? sourceDocumentId, string? from, string? to, Database db) =>
        {
            var fromDate = DateOnly.TryParse(from, out var f) ? f : (DateOnly?)null;
            var toDate = DateOnly.TryParse(to, out var t) ? t : (DateOnly?)null;
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT d.id, d.voucher_no, d.doc_date, d.customer_name, d.content, d.note,
                       d.cancelled_at, d.cancel_reason,
                       (SELECT COALESCE(SUM(l.quantity * l.unit_price), 0)
                        FROM document_lines l WHERE l.document_id = d.id) AS total
                FROM documents d
                WHERE d.document_type = 'return'
                  AND (@cid::uuid IS NULL OR d.customer_id = @cid)
                  AND (@from::date IS NULL OR d.doc_date >= @from)
                  AND (@to::date IS NULL OR d.doc_date <= @to)
                  AND (@src::uuid IS NULL OR EXISTS (
                        SELECT 1 FROM document_lines l
                        WHERE l.document_id = d.id AND l.source_document_id = @src))
                ORDER BY d.doc_date DESC, d.voucher_no DESC
                LIMIT 300
                """)
                .With("@cid", (object?)customerId ?? DBNull.Value)
                .With("@src", (object?)sourceDocumentId ?? DBNull.Value)
                .With("@from", (object?)fromDate ?? DBNull.Value)
                .With("@to", (object?)toDate ?? DBNull.Value)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    id = r.Guid("id"),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.DateOnly("doc_date"),
                    customerName = r.Str("customer_name"),
                    content = r.Str("content"),
                    note = r.Str("note"),
                    total = r.Dec("total"),
                    cancelledAt = r.DtNull("cancelled_at"),
                    cancelReason = r.Str("cancel_reason"),
                });
            return Results.Ok(new { items });
        });

        api.MapGet("/returns/{id:guid}", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            object? head = null;
            await using (var r = await conn.Cmd("""
                SELECT id, voucher_no, doc_date, customer_id, customer_name, content, note,
                       cancelled_at, cancel_reason, created_at
                FROM documents WHERE id=@id AND document_type='return'
                """).With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                head = new
                {
                    id = r.Guid("id"),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.DateOnly("doc_date"),
                    customerName = r.Str("customer_name"),
                    content = r.Str("content"),
                    note = r.Str("note"),
                    cancelledAt = r.DtNull("cancelled_at"),
                    cancelReason = r.Str("cancel_reason"),
                    createdAt = r.Dt("created_at"),
                };
            }

            var lines = new List<object>();
            await using (var r = await conn.Cmd("""
                SELECT l.line_no, l.line_content, l.spec, l.quantity, l.unit_price,
                       l.source_document_id, l.source_line_no,
                       COALESCE(s.voucher_no, '') AS source_voucher_no, s.doc_date AS source_date
                FROM document_lines l
                LEFT JOIN documents s ON s.id = l.source_document_id
                WHERE l.document_id=@id ORDER BY l.line_no
                """).With("@id", id).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var sourceOrdinal = r.GetOrdinal("source_document_id");
                    lines.Add(new
                    {
                        lineNo = r.Int("line_no"),
                        content = r.Str("line_content"),
                        spec = r.Str("spec"),
                        quantity = r.Dec("quantity"),
                        unitPrice = r.Dec("unit_price"),
                        amount = r.Dec("quantity") * r.Dec("unit_price"),
                        sourceDocumentId = r.IsDBNull(sourceOrdinal) ? (Guid?)null : r.GetGuid(sourceOrdinal),
                        sourceVoucherNo = r.Str("source_voucher_no"),
                        sourceDate = r.IsDBNull(r.GetOrdinal("source_date"))
                            ? (DateOnly?)null
                            : r.DateOnly("source_date"),
                    });
                }
            }
            return Results.Ok(new { document = head, lines });
        });

        // Lập nhầm thì hủy — phiếu ở lại sổ với dấu đã hủy, và số đã trả của dòng nguồn tự nhả ra.
        api.MapPut("/returns/{id:guid}/cancel", async (Guid id, CancelReturnReq req, ClaimsPrincipal u, Database db) =>
        {
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng nhập lý do hủy phiếu trả hàng." });
            if (reason.Length > 500) reason = reason[..500];

            var me = u.Username();
            await using var conn = await db.OpenAsync();
            var voucherNo = await conn.Cmd("""
                UPDATE documents
                SET cancelled_at=CURRENT_TIMESTAMP, cancelled_by=@by, cancel_reason=@reason,
                    updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND document_type='return' AND cancelled_at IS NULL
                RETURNING voucher_no
                """).With("@id", id).With("@by", me).With("@reason", reason)
                .ExecuteScalarAsync() as string;
            if (voucherNo is null)
                return Results.Conflict(new { message = "Phiếu trả hàng không tồn tại hoặc đã hủy." });

            await db.RecordAudit(me, "Hủy phiếu trả hàng", "Document", voucherNo, reason);
            return Results.NoContent();
        });
    }

    /// <summary>Số phiếu trả hàng: TH + số thứ tự tăng dần, không đụng tới dải số phiếu xuất kho.</summary>
    private static async Task<string> NextReturnNo(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var seq = Convert.ToInt64(await conn.Cmd("SELECT nextval('goods_return_seq')", tx).ExecuteScalarAsync() ?? 1L);
        return $"TH{seq:00000}";
    }

    /// <summary>
    /// Dòng hàng của đơn nguồn + số đã trả. Khoá dòng lại (FOR UPDATE) để hai người cùng nhận hàng
    /// trả về không thể cùng nhìn thấy "còn 1.000kg" rồi mỗi người trả 800kg.
    /// </summary>
    private static async Task<SourceLine?> LoadSourceLine(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid documentId, int lineNo)
    {
        // Khoá dòng hàng trước, rồi mới đọc phần tổng hợp: FOR UPDATE không dùng chung với LATERAL
        // tổng hợp trong cùng một câu.
        await using (var lockReader = await conn.Cmd("""
            SELECT 1 FROM document_lines WHERE document_id=@doc AND line_no=@no FOR UPDATE
            """, tx).With("@doc", documentId).With("@no", lineNo).ExecuteReaderAsync())
        {
            if (!await lockReader.ReadAsync()) return null;
        }

        await using var r = await conn.Cmd("""
            SELECT d.id, d.voucher_no, d.doc_date, d.customer_id, d.customer_name,
                   d.customer_input_name, d.cancelled_at, d.delivery_returned_at, d.document_type,
                   l.line_no, l.line_content, l.spec, l.quantity, l.unit_price,
                   COALESCE((
                        SELECT SUM(rl.quantity)
                        FROM document_lines rl
                        JOIN documents rd ON rd.id = rl.document_id
                        WHERE rl.source_document_id = d.id AND rl.source_line_no = l.line_no
                          AND rd.document_type = 'return' AND rd.cancelled_at IS NULL
                   ), 0) AS returned
            FROM documents d
            JOIN document_lines l ON l.document_id = d.id
            WHERE d.id=@doc AND l.line_no=@no AND d.document_type='document'
            """, tx).With("@doc", documentId).With("@no", lineNo).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var customer = r.Str("customer_name");
        if (customer.Length == 0) customer = r.Str("customer_input_name");
        var customerOrdinal = r.GetOrdinal("customer_id");
        return new SourceLine(
            r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
            r.IsDBNull(customerOrdinal) ? null : r.GetGuid(customerOrdinal), customer,
            r.DtNull("cancelled_at"), r.DtNull("delivery_returned_at"),
            r.Int("line_no"), r.Str("line_content"), r.Str("spec"),
            r.Dec("quantity"), r.Dec("unit_price"), r.Dec("returned"));
    }

    private sealed record SourceLine(
        Guid DocumentId, string VoucherNo, DateOnly DocDate, Guid? CustomerId, string CustomerName,
        DateTime? CancelledAt, DateTime? ReturnedToWarehouseAt,
        int LineNo, string Content, string Spec, decimal Quantity, decimal UnitPrice, decimal Returned);

    private sealed record ResolvedLine(SourceLine Source, decimal Quantity, bool AdjustInPlace);

    /// <param name="SourceDocumentId">Đơn mà món hàng này đã được bán ra — chốt đơn giá để trừ công nợ.</param>
    public record GoodsReturnLineReq(Guid SourceDocumentId, int SourceLineNo, decimal Quantity);

    /// <param name="ContextDocumentId">
    /// Phiếu kế toán đang mở trên màn hình. Dòng nào trả về CHÍNH phiếu này mà nó chưa chốt về kho
    /// thì hạ thẳng số lượng thay vì sinh phiếu trả.
    /// </param>
    public record GoodsReturnReq(DateOnly? Date, string? Reason, string? Note,
        Guid? ContextDocumentId, List<GoodsReturnLineReq>? Lines);

    public record CancelReturnReq(string? Reason);
}
