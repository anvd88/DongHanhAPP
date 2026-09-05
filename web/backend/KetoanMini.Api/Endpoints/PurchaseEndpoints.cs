using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// MUA HÀNG — nhà cung cấp + phiếu nhập mua.
///
/// Đây là vế NHẬP mà hệ thống chưa từng có: trước nay chỉ ghi bán ra, tiền, và công nợ phải THU.
/// Không có phiếu nhập thì không thể tính tồn kho ("tồn = nhập − xuất") lẫn giá vốn, nên đây là
/// bước bắt buộc trước khi làm nhập–xuất–tồn.
///
/// Phiếu nhập KHÔNG dùng chung bảng <c>documents</c> với phiếu bán: bảng đó đã gánh cả vòng đời
/// phiếu bán (số in ra bất biến, giao hàng, đối soát, hàng trả về, công nợ phải thu), nhét thêm
/// chiều mua vào là mỗi truy vấn tiền lại phải nhớ loại trừ thêm một loại nữa.
///
/// Công nợ phải trả ở mức GỌN: mỗi phiếu ghi "đã trả bao nhiêu", còn nợ = tổng − đã trả. Chưa dựng
/// sổ chi tiết thanh toán cho nhà cung cấp — khi nào cần thì thêm bảng riêng, cột paid_amount vẫn
/// đúng vì nó là con số tổng.
/// </summary>
public static class PurchaseEndpoints
{
    public static void MapPurchases(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequirePermission(Permissions.AccountingAccess);

        // ── Nhà cung cấp ────────────────────────────────────────────────────────────────────
        api.MapGet("/suppliers", async (string? q, bool? includeInactive, Database db) =>
        {
            var keyword = (q ?? "").Trim();
            var like = $"%{keyword}%";
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT s.id, s.name, s.tax_code, s.phone, s.address, s.note, s.is_active,
                       COALESCE(p.purchase_count, 0)::int AS purchase_count,
                       COALESCE(p.purchased_total, 0) AS purchased_total,
                       COALESCE(p.paid_total, 0) AS paid_total,
                       p.last_purchase_date,
                       COALESCE(a.aliases, '{}'::text[]) AS aliases
                FROM suppliers s
                LEFT JOIN LATERAL (
                    SELECT array_agg(sa.alias ORDER BY sa.alias) AS aliases
                    FROM supplier_aliases sa WHERE sa.supplier_id = s.id
                ) a ON TRUE
                LEFT JOIN LATERAL (
                    SELECT COUNT(*)::int AS purchase_count,
                           SUM(COALESCE(t.total, 0)) AS purchased_total,
                           SUM(pu.paid_amount) AS paid_total,
                           MAX(pu.doc_date) AS last_purchase_date
                    FROM purchases pu
                    LEFT JOIN LATERAL (
                        SELECT SUM(l.quantity * l.unit_price) AS total
                        FROM purchase_lines l WHERE l.purchase_id = pu.id
                    ) t ON TRUE
                    WHERE pu.supplier_id = s.id AND pu.cancelled_at IS NULL
                ) p ON TRUE
                WHERE (@all OR s.is_active = TRUE)
                  AND (@kw = '' OR s.name ILIKE @like OR s.tax_code ILIKE @like OR s.phone ILIKE @like
                       OR EXISTS (SELECT 1 FROM supplier_aliases sa2
                                  WHERE sa2.supplier_id = s.id AND sa2.alias ILIKE @like))
                ORDER BY s.is_active DESC, s.name
                LIMIT 500
                """)
                .With("@all", includeInactive == true).With("@kw", keyword).With("@like", like)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var purchased = r.Dec("purchased_total");
                var paid = r.Dec("paid_total");
                items.Add(new
                {
                    id = r.Guid("id"),
                    name = r.Str("name"),
                    taxCode = r.Str("tax_code"),
                    phone = r.Str("phone"),
                    address = r.Str("address"),
                    note = r.Str("note"),
                    isActive = r.Bool("is_active"),
                    purchaseCount = r.Int("purchase_count"),
                    purchasedTotal = purchased,
                    paidTotal = paid,
                    // Dương = mình còn nợ nhà cung cấp.
                    balance = purchased - paid,
                    lastPurchaseDate = r.IsDBNull(r.GetOrdinal("last_purchase_date"))
                        ? (DateOnly?)null : r.DateOnly("last_purchase_date"),
                    aliases = r.GetFieldValue<string[]>(r.GetOrdinal("aliases")),
                });
            }
            return Results.Ok(new { items });
        });

        // HÀNG CỦA NHÀ CUNG CẤP NÀY CÒN LẠI BAO NHIÊU — nhìn theo chiều ngược với
        // /products/{id}/sources: ở đó là "mặt hàng này lấy của những ai", ở đây là "của người này
        // còn những gì". Thủ kho đi đếm hàng thật trong kho cần đúng bảng này.
        //
        // Chỉ những mặt hàng có trong danh mục mới đếm được: dòng phiếu gõ tay không có mã hàng thì
        // không có gì để cộng hai vế nhập và xuất vào với nhau.
        api.MapGet("/suppliers/{supplierId:guid}/stock", async (Guid supplierId, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT p.id, p.code, p.name, p.spec, p.unit,
                       b.bought,
                       COALESCE(x.sold, 0) AS sold,
                       b.bought - COALESCE(x.sold, 0) AS remaining,
                       b.last_cost, b.last_bought_date
                FROM products p
                JOIN LATERAL (
                    SELECT SUM(pl.quantity) AS bought,
                           (SELECT pl2.unit_price
                            FROM purchase_lines pl2
                            JOIN purchases p2 ON p2.id = pl2.purchase_id
                            WHERE pl2.product_id = p.id AND p2.supplier_id = @sid AND p2.cancelled_at IS NULL
                            ORDER BY p2.doc_date DESC, p2.created_at DESC LIMIT 1) AS last_cost,
                           MAX(pu.doc_date) AS last_bought_date
                    FROM purchase_lines pl
                    JOIN purchases pu ON pu.id = pl.purchase_id
                    WHERE pl.product_id = p.id AND pu.supplier_id = @sid AND pu.cancelled_at IS NULL
                ) b ON b.bought IS NOT NULL
                LEFT JOIN LATERAL (
                    -- Phiếu bán trừ đi, phiếu khách trả về cộng lại.
                    SELECT SUM(CASE WHEN d.document_type = 'return' THEN -l.quantity ELSE l.quantity END) AS sold
                    FROM document_lines l
                    JOIN documents d ON d.id = l.document_id
                    WHERE l.product_id = p.id AND l.supplier_id = @sid
                      AND d.document_type IN ('document', 'return')
                      AND d.cancelled_at IS NULL
                ) x ON TRUE
                ORDER BY remaining DESC, p.name
                """).With("@sid", supplierId).ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    productId = r.Guid("id"),
                    code = r.Str("code"),
                    name = r.Str("name"),
                    spec = r.Str("spec"),
                    unit = r.Str("unit"),
                    bought = r.Dec("bought"),
                    sold = r.Dec("sold"),
                    remaining = r.Dec("remaining"),
                    lastCost = r.IsDBNull(r.GetOrdinal("last_cost")) ? (decimal?)null : r.Dec("last_cost"),
                    lastBoughtDate = r.IsDBNull(r.GetOrdinal("last_bought_date"))
                        ? (DateOnly?)null
                        : r.DateOnly("last_bought_date"),
                });
            return Results.Ok(new { items });
        });

        api.MapPost("/suppliers", async (SaveSupplierReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên nhà cung cấp." });

            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            try
            {
                await conn.Cmd("""
                    INSERT INTO suppliers (id, name, tax_code, phone, address, note)
                    VALUES (@id, @name, @tax, @phone, @address, @note)
                    """)
                    .With("@id", id).With("@name", name).With("@tax", (req.TaxCode ?? "").Trim())
                    .With("@phone", (req.Phone ?? "").Trim()).With("@address", (req.Address ?? "").Trim())
                    .With("@note", (req.Note ?? "").Trim())
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict(new { message = "Đã có nhà cung cấp trùng tên." });
            }
            await db.RecordAudit(u.Username(), "Thêm nhà cung cấp", "Supplier", name, "");
            return Results.Ok(new { id });
        });

        api.MapPut("/suppliers/{id:guid}", async (Guid id, SaveSupplierReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập tên nhà cung cấp." });

            await using var conn = await db.OpenAsync();
            int changed;
            try
            {
                changed = await conn.Cmd("""
                    UPDATE suppliers SET name=@name, tax_code=@tax, phone=@phone, address=@address,
                        note=@note, is_active=COALESCE(@active, is_active), updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id
                    """)
                    .With("@id", id).With("@name", name).With("@tax", (req.TaxCode ?? "").Trim())
                    .With("@phone", (req.Phone ?? "").Trim()).With("@address", (req.Address ?? "").Trim())
                    .With("@note", (req.Note ?? "").Trim())
                    .With("@active", (object?)req.IsActive ?? DBNull.Value)
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict(new { message = "Đã có nhà cung cấp trùng tên." });
            }
            if (changed == 0) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Sửa nhà cung cấp", "Supplier", name, "");
            return Results.NoContent();
        });

        // ── Bí danh nhà cung cấp ────────────────────────────────────────────────────────────
        //
        // "Công ty Đại Phát" trên giấy tờ nhưng người trong kho gọi là "anh A - Đại Phát". Gõ tên
        // nào cũng phải về đúng một nhà cung cấp, nếu không mỗi cách gọi lại đẻ ra một hồ sơ mới và
        // công nợ phải trả bị chẻ nhỏ.
        api.MapPost("/suppliers/{id:guid}/aliases", async (Guid id, SaveAliasReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            var alias = (req.Alias ?? "").Trim();
            if (alias.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập bí danh." });

            await using var conn = await db.OpenAsync();
            var supplier = await conn.Cmd("SELECT name FROM suppliers WHERE id=@id")
                .With("@id", id).ExecuteScalarAsync() as string;
            if (supplier is null) return Results.NotFound();

            // Bí danh trùng TÊN THẬT của một nhà cung cấp khác là cái bẫy tệ nhất: lúc gán tự động,
            // tên thật thắng nên bí danh nằm đó vô dụng, còn người đặt thì tưởng đã xong.
            var clash = await conn.Cmd("SELECT id FROM suppliers WHERE lower(name) = lower(@a) AND id <> @id")
                .With("@a", alias).With("@id", id).ExecuteScalarAsync();
            if (clash is Guid)
                return Results.Conflict(new { message = $"\"{alias}\" đang là tên của một nhà cung cấp khác." });

            try
            {
                await conn.Cmd("INSERT INTO supplier_aliases (supplier_id, alias, created_by) VALUES (@id, @a, @by)")
                    .With("@id", id).With("@a", alias).With("@by", u.Username()).ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                var owner = await conn.Cmd("""
                    SELECT s.name FROM supplier_aliases sa
                    JOIN suppliers s ON s.id = sa.supplier_id
                    WHERE lower(sa.alias) = lower(@a)
                    """).With("@a", alias).ExecuteScalarAsync() as string ?? "";
                return Results.Conflict(new { message = $"Bí danh này đã gán cho \"{owner}\"." });
            }

            await db.RecordAudit(u.Username(), "Thêm bí danh nhà cung cấp", "Supplier", supplier, alias);
            return Results.Ok(new { alias });
        });

        api.MapDelete("/suppliers/{id:guid}/aliases/{aliasId:long}", async (
            Guid id, long aliasId, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            var alias = await conn.Cmd("DELETE FROM supplier_aliases WHERE id=@aid AND supplier_id=@id RETURNING alias")
                .With("@aid", aliasId).With("@id", id).ExecuteScalarAsync() as string;
            if (alias is null) return Results.NotFound();
            await db.RecordAudit(u.Username(), "Xoá bí danh nhà cung cấp", "Supplier", alias, "");
            return Results.NoContent();
        });

        api.MapGet("/suppliers/{id:guid}/aliases", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd(
                "SELECT id, alias, created_by, created_at FROM supplier_aliases WHERE supplier_id=@id ORDER BY alias")
                .With("@id", id).ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    id = r.Long("id"),
                    alias = r.Str("alias"),
                    createdBy = r.Str("created_by"),
                    createdAt = r.Dt("created_at"),
                });
            return Results.Ok(new { items });
        });

        // ── Phiếu nhập mua ──────────────────────────────────────────────────────────────────
        api.MapGet("/purchases", async (Guid? supplierId, string? from, string? to, Database db) =>
        {
            var fromDate = DateOnly.TryParse(from, out var f) ? f : (DateOnly?)null;
            var toDate = DateOnly.TryParse(to, out var t) ? t : (DateOnly?)null;
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT p.id, p.voucher_no, p.doc_date, p.supplier_id, p.supplier_name,
                       p.supplier_invoice_no, p.note, p.paid_amount, p.cancelled_at, p.cancel_reason,
                       p.created_by,
                       COALESCE((SELECT SUM(l.quantity * l.unit_price)
                                 FROM purchase_lines l WHERE l.purchase_id = p.id), 0) AS total
                FROM purchases p
                WHERE (@sid::uuid IS NULL OR p.supplier_id = @sid)
                  AND (@from::date IS NULL OR p.doc_date >= @from)
                  AND (@to::date IS NULL OR p.doc_date <= @to)
                ORDER BY p.doc_date DESC, p.voucher_no DESC
                LIMIT 500
                """)
                .With("@sid", (object?)supplierId ?? DBNull.Value)
                .With("@from", (object?)fromDate ?? DBNull.Value)
                .With("@to", (object?)toDate ?? DBNull.Value)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var total = r.Dec("total");
                var paid = r.Dec("paid_amount");
                items.Add(new
                {
                    id = r.Guid("id"),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.DateOnly("doc_date"),
                    supplierName = r.Str("supplier_name"),
                    supplierInvoiceNo = r.Str("supplier_invoice_no"),
                    note = r.Str("note"),
                    total,
                    paidAmount = paid,
                    remaining = total - paid,
                    cancelledAt = r.DtNull("cancelled_at"),
                    cancelReason = r.Str("cancel_reason"),
                    createdBy = r.Str("created_by"),
                });
            }
            return Results.Ok(new { items });
        });

        api.MapGet("/purchases/{id:guid}", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            object? head = null;
            await using (var r = await conn.Cmd("""
                SELECT id, voucher_no, doc_date, supplier_id, supplier_name, supplier_invoice_no,
                       note, paid_amount, cancelled_at, cancel_reason
                FROM purchases WHERE id=@id
                """).With("@id", id).ExecuteReaderAsync())
            {
                if (!await r.ReadAsync()) return Results.NotFound();
                var supplierOrdinal = r.GetOrdinal("supplier_id");
                head = new
                {
                    id = r.Guid("id"),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.DateOnly("doc_date"),
                    supplierId = r.IsDBNull(supplierOrdinal) ? (Guid?)null : r.GetGuid(supplierOrdinal),
                    supplierName = r.Str("supplier_name"),
                    supplierInvoiceNo = r.Str("supplier_invoice_no"),
                    note = r.Str("note"),
                    paidAmount = r.Dec("paid_amount"),
                    cancelledAt = r.DtNull("cancelled_at"),
                    cancelReason = r.Str("cancel_reason"),
                };
            }

            var lines = new List<object>();
            await using (var r = await conn.Cmd("""
                SELECT line_no, product_id, line_content, spec, quantity, unit_price, note
                FROM purchase_lines WHERE purchase_id=@id ORDER BY line_no
                """).With("@id", id).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var productOrdinal = r.GetOrdinal("product_id");
                    lines.Add(new
                    {
                        lineNo = r.Int("line_no"),
                        productId = r.IsDBNull(productOrdinal) ? (Guid?)null : r.GetGuid(productOrdinal),
                        lineContent = r.Str("line_content"),
                        spec = r.Str("spec"),
                        quantity = r.Dec("quantity"),
                        unitPrice = r.Dec("unit_price"),
                        note = r.Str("note"),
                    });
                }
            }
            return Results.Ok(new { purchase = head, lines });
        });

        api.MapPost("/purchases", async (SavePurchaseReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            return await SavePurchase(db, u, null, req);
        });

        api.MapPut("/purchases/{id:guid}", async (Guid id, SavePurchaseReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersUpdate)) return Results.Forbid();
            return await SavePurchase(db, u, id, req);
        });

        // Hủy chứ không xoá: phiếu ở lại sổ với dấu đã hủy để tháng sau còn đối chiếu được.
        api.MapPut("/purchases/{id:guid}/cancel", async (Guid id, CancelPurchaseReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCancel)) return Results.Forbid();
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do hủy phiếu." });
            if (reason.Length > 500) reason = reason[..500];

            await using var conn = await db.OpenAsync();
            var voucherNo = await conn.Cmd("""
                UPDATE purchases SET cancelled_at=CURRENT_TIMESTAMP, cancelled_by=@by,
                    cancel_reason=@reason, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND cancelled_at IS NULL
                RETURNING voucher_no
                """).With("@id", id).With("@by", u.Username()).With("@reason", reason)
                .ExecuteScalarAsync() as string;
            if (voucherNo is null)
                return Results.Conflict(new { message = "Phiếu nhập không tồn tại hoặc đã hủy." });

            await db.RecordAudit(u.Username(), "Hủy phiếu nhập mua", "Purchase", voucherNo, reason);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Tên gõ tay thành hồ sơ nhà cung cấp. Thứ tự tra: tên thật, rồi bí danh, cuối cùng mới tạo mới.
    /// </summary>
    /// <remarks>
    /// Bí danh phải đứng TRƯỚC bước tạo mới, nếu không "anh A - Đại Phát" sẽ đẻ ra một nhà cung cấp
    /// thứ hai bên cạnh "Công ty Đại Phát" — đúng thứ mà bảng bí danh sinh ra để chặn.
    ///
    /// Tên trả về là tên THẬT của nhà cung cấp chứ không phải chữ vừa gõ: phiếu nhập phải ghi tên
    /// trên giấy tờ, còn bí danh chỉ là lối vào cho nhanh.
    /// </remarks>
    private static async Task<(Guid Id, string Name)> ResolveSupplier(
        NpgsqlConnection conn, NpgsqlTransaction tx, string name)
    {
        await using (var byName = await conn.Cmd(
                "SELECT id, name FROM suppliers WHERE lower(name) = lower(@n)", tx)
            .With("@n", name).ExecuteReaderAsync())
        {
            if (await byName.ReadAsync()) return (byName.Guid("id"), byName.Str("name"));
        }

        await using (var byAlias = await conn.Cmd(
                @"SELECT s.id, s.name FROM supplier_aliases sa
                  JOIN suppliers s ON s.id = sa.supplier_id
                  WHERE lower(sa.alias) = lower(@n)", tx)
            .With("@n", name).ExecuteReaderAsync())
        {
            if (await byAlias.ReadAsync()) return (byAlias.Guid("id"), byAlias.Str("name"));
        }

        var newId = Guid.NewGuid();
        await conn.Cmd("INSERT INTO suppliers (id, name, is_active) VALUES (@id, @n, TRUE)", tx)
            .With("@id", newId).With("@n", name).ExecuteNonQueryAsync();
        return (newId, name);
    }

    private static async Task<IResult> SavePurchase(Database db, ClaimsPrincipal u, Guid? id, SavePurchaseReq req)
    {
        var supplierName = (req.SupplierName ?? "").Trim();
        if (supplierName.Length == 0 && req.SupplierId is null)
            return Results.BadRequest(new { message = "Vui lòng chọn nhà cung cấp." });
        var lines = (req.Lines ?? []).Where(l => (l.LineContent ?? "").Trim().Length > 0).ToList();
        if (lines.Count == 0)
            return Results.BadRequest(new { message = "Phiếu nhập phải có ít nhất một dòng hàng." });
        if (lines.Any(l => l.Quantity < 0 || l.UnitPrice < 0))
            return Results.BadRequest(new { message = "Số lượng và đơn giá không được âm." });
        var paid = decimal.Round(Math.Max(req.PaidAmount ?? 0, 0), 2, MidpointRounding.AwayFromZero);
        var total = lines.Sum(l => decimal.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        if (paid > total)
            return Results.BadRequest(new { message = "Số tiền đã trả không được lớn hơn giá trị phiếu nhập." });

        var me = u.Username();
        await using var conn = await db.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Tên nhà cung cấp lưu KÈM trên phiếu (không chỉ khoá ngoại): đổi tên nhà cung cấp về sau
        // không được làm phiếu cũ hiện tên khác với tờ giấy đã lưu.
        Guid? resolvedSupplierId = req.SupplierId;
        if (req.SupplierId is { } supplierId)
        {
            var known = await conn.Cmd("SELECT name FROM suppliers WHERE id=@id", tx)
                .With("@id", supplierId).ExecuteScalarAsync() as string;
            if (known is null) return Results.BadRequest(new { message = "Nhà cung cấp không tồn tại." });
            if (supplierName.Length == 0) supplierName = known;
        }
        else
        {
            // Gõ tay một cái tên chưa có trong danh mục thì phải DỰNG hồ sơ nhà cung cấp, y như bên
            // bán hàng vẫn tự tạo khách mới. Trước đây phiếu lưu với supplier_id rỗng: công nợ phải
            // trả không cộng vào ai cả, và tồn theo nguồn hàng không thấy lô hàng đó tồn tại.
            (resolvedSupplierId, supplierName) = await ResolveSupplier(conn, tx, supplierName);
        }

        var purchaseId = id ?? Guid.NewGuid();
        var voucherNo = (req.VoucherNo ?? "").Trim();
        if (id is null)
        {
            if (voucherNo.Length == 0) voucherNo = await NextVoucherNo(conn, tx);
            await conn.Cmd("""
                INSERT INTO purchases (id, voucher_no, doc_date, supplier_id, supplier_name,
                    supplier_invoice_no, note, paid_amount, created_by)
                VALUES (@id, @no, @date, @sid, @sname, @inv, @note, @paid, @by)
                """, tx)
                .With("@id", purchaseId).With("@no", voucherNo)
                .With("@date", req.Date ?? DateOnly.FromDateTime(DateTime.Now))
                .With("@sid", (object?)resolvedSupplierId ?? DBNull.Value).With("@sname", supplierName)
                .With("@inv", (req.SupplierInvoiceNo ?? "").Trim()).With("@note", (req.Note ?? "").Trim())
                .With("@paid", paid).With("@by", me)
                .ExecuteNonQueryAsync();
        }
        else
        {
            var cancelled = await conn.Cmd("SELECT cancelled_at FROM purchases WHERE id=@id FOR UPDATE", tx)
                .With("@id", purchaseId).ExecuteScalarAsync();
            if (cancelled is null) return Results.NotFound();
            if (cancelled is not DBNull)
                return Results.Conflict(new { message = "Phiếu đã hủy nên bị khóa để giữ lịch sử." });

            await conn.Cmd("""
                UPDATE purchases SET doc_date=@date, supplier_id=@sid, supplier_name=@sname,
                    supplier_invoice_no=@inv, note=@note, paid_amount=@paid, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """, tx)
                .With("@id", purchaseId).With("@date", req.Date ?? DateOnly.FromDateTime(DateTime.Now))
                .With("@sid", (object?)resolvedSupplierId ?? DBNull.Value).With("@sname", supplierName)
                .With("@inv", (req.SupplierInvoiceNo ?? "").Trim()).With("@note", (req.Note ?? "").Trim())
                .With("@paid", paid)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM purchase_lines WHERE purchase_id=@id", tx)
                .With("@id", purchaseId).ExecuteNonQueryAsync();
        }

        var lineNo = 1;
        foreach (var line in lines)
        {
            // Cùng cách với phiếu bán: client không gửi mã hàng thì máy chủ tự khớp theo tên+quy cách.
            await conn.Cmd("""
                INSERT INTO purchase_lines (purchase_id, line_no, product_id, line_content, spec,
                    quantity, unit_price, note)
                VALUES (@pid, @no,
                        COALESCE(@product, (SELECT p.id FROM products p
                                            WHERE lower(p.name) = lower(BTRIM(@content))
                                              AND lower(p.spec) = lower(BTRIM(@spec))
                                            LIMIT 1)),
                        @content, @spec, @q, @price, @note)
                """, tx)
                .With("@pid", purchaseId).With("@no", lineNo++)
                .With("@product", (object?)line.ProductId ?? DBNull.Value)
                .With("@content", (line.LineContent ?? "").Trim()).With("@spec", (line.Spec ?? "").Trim())
                .With("@q", line.Quantity).With("@price", line.UnitPrice)
                .With("@note", (line.Note ?? "").Trim())
                .ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        await db.RecordAudit(me, id is null ? "Tạo phiếu nhập mua" : "Sửa phiếu nhập mua",
            "Purchase", voucherNo, $"{supplierName}: {lines.Count} dòng, {total:#,##0} ₫.");
        return Results.Ok(new { id = purchaseId, voucherNo, total });
    }

    /// <summary>Số phiếu nhập tự sinh: PN00001… (dải riêng, không đụng số phiếu xuất kho).</summary>
    private static async Task<string> NextVoucherNo(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var seq = Convert.ToInt64(await conn.Cmd("SELECT nextval('purchase_voucher_seq')", tx)
            .ExecuteScalarAsync() ?? 1L);
        return $"PN{seq:00000}";
    }

    public record SaveSupplierReq(string? Name, string? TaxCode, string? Phone, string? Address,
        string? Note, bool? IsActive);
    public record PurchaseLineReq(Guid? ProductId, string? LineContent, string? Spec,
        decimal Quantity, decimal UnitPrice, string? Note);
    /// <param name="PaidAmount">Đã trả nhà cung cấp bao nhiêu. Còn nợ = tổng phiếu − số này.</param>
    public record SaveAliasReq(string? Alias);

    public record SavePurchaseReq(string? VoucherNo, DateOnly? Date, Guid? SupplierId, string? SupplierName,
        string? SupplierInvoiceNo, string? Note, decimal? PaidAmount, List<PurchaseLineReq>? Lines);
    public record CancelPurchaseReq(string? Reason);
}
