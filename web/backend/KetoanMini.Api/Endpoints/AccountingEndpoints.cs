using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class AccountingEndpoints
{
    private const string TotalSub =
        "(SELECT COALESCE(SUM(l.quantity * l.unit_price), 0) FROM document_lines l WHERE l.document_id = d.id)";

    public static void MapAccounting(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---------- Dashboard ----------
        api.MapGet("/dashboard", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var now = DateTime.Now;

            var activeCustomers = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM customers WHERE is_active = TRUE").ExecuteScalarAsync() ?? 0);
            var totalDocuments = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM documents").ExecuteScalarAsync() ?? 0);
            var totalPayments = (decimal)(await conn.Cmd("SELECT COALESCE(SUM(amount),0) FROM payments").ExecuteScalarAsync() ?? 0m);
            var monthRevenue = (decimal)(await conn.Cmd(
                @"SELECT COALESCE(SUM(l.quantity * l.unit_price),0)
                  FROM documents d JOIN document_lines l ON l.document_id = d.id
                  WHERE EXTRACT(YEAR FROM d.doc_date) = @y AND EXTRACT(MONTH FROM d.doc_date) = @m")
                .With("@y", now.Year).With("@m", now.Month).ExecuteScalarAsync() ?? 0m);

            var recent = new List<RecentDocDto>();
            await using (var r = await conn.Cmd(
                $@"SELECT d.id, d.voucher_no, d.doc_date, d.customer_name, d.content, {TotalSub} AS total
                   FROM documents d ORDER BY d.doc_date DESC, d.voucher_no DESC LIMIT 12").ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    recent.Add(new RecentDocDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                        r.Str("customer_name"), r.Str("content"), r.Dec("total")));
            }

            return Results.Ok(new DashboardDto(activeCustomers, totalDocuments, totalPayments, monthRevenue, now.Month, now.Year, recent));
        });

        // ---------- Documents (Kế toán) ----------
        api.MapGet("/documents", ListDocuments);

        api.MapGet("/documents/{id:guid}", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            DocumentDetailDto? doc = null;
            await using (var r = await conn.Cmd(
                "SELECT id, voucher_no, doc_date, customer_name, content, note FROM documents WHERE id = @id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                    doc = new DocumentDetailDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                        r.Str("customer_name"), r.Str("content"), r.Str("note"), new());
            }
            if (doc is null) return Results.NotFound();

            await using (var r = await conn.Cmd(
                @"SELECT line_content, spec, quantity, unit_price, note FROM document_lines
                  WHERE document_id = @id ORDER BY line_no").With("@id", id).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    doc.Lines.Add(new DocumentLineDto(r.Str("line_content"), r.Str("spec"),
                        r.Dec("quantity"), r.Dec("unit_price"), r.Str("note")));
            }
            return Results.Ok(doc);
        });

        api.MapPost("/documents", async (SaveDocumentRequest req, ClaimsPrincipal u, Database db) =>
            await SaveDocument(db, u, null, req));

        api.MapPut("/documents/{id:guid}", async (Guid id, SaveDocumentRequest req, ClaimsPrincipal u, Database db) =>
            await SaveDocument(db, u, id, req));

        api.MapDelete("/documents/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var n = await conn.Cmd("DELETE FROM documents WHERE id = @id").With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa phiếu kế toán", "Document", id.ToString(), "Xóa phiếu kế toán (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        // ---------- Customers ----------
        api.MapGet("/customers", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<CustomerDto>();
            await using var r = await conn.Cmd(
                "SELECT id, name, tax_code, phone, address, is_active FROM customers WHERE is_active = TRUE ORDER BY name")
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new CustomerDto(r.Guid("id"), r.Str("name"), r.Str("tax_code"),
                    r.Str("phone"), r.Str("address"), r.Bool("is_active")));
            return Results.Ok(list);
        });

        api.MapGet("/customers/{id:guid}/report", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var customer = await ReadCustomer(conn, id);
            if (customer is null) return Results.NotFound();

            var documents = await ReadCustomerDocuments(conn, id, customer.Name);
            var receiptTotal = documents
                .Where(d => d.DocumentType.Contains("thu", StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Total);
            var paymentTotal = documents
                .Where(d => d.DocumentType.Contains("chi", StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Total);
            var salesTotal = documents
                .Where(d => !d.DocumentType.Contains("thu", StringComparison.OrdinalIgnoreCase)
                            && !d.DocumentType.Contains("chi", StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Total);

            return Results.Ok(new CustomerReportDto(customer, documents.Count, documents.Sum(d => d.Total),
                receiptTotal, paymentTotal, salesTotal, documents));
        });

        api.MapPost("/customers", async (SaveCustomerRequest req, ClaimsPrincipal u, Database db) =>
            await SaveCustomer(db, u, null, req));

        api.MapPut("/customers/{id:guid}", async (Guid id, SaveCustomerRequest req, ClaimsPrincipal u, Database db) =>
            await SaveCustomer(db, u, id, req));

        api.MapDelete("/customers/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                var nameCmd = new NpgsqlCommand("SELECT name FROM customers WHERE id = @id", conn, tx);
                nameCmd.Parameters.AddWithValue("@id", id);
                var name = await nameCmd.ExecuteScalarAsync() as string;
                if (name is null)
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                var deleteLines = new NpgsqlCommand(
                    @"DELETE FROM document_lines l
                      USING documents d
                      WHERE d.id = l.document_id
                        AND (d.customer_id = @id OR d.customer_name = @name OR d.customer_input_name = @name)", conn, tx);
                deleteLines.Parameters.AddWithValue("@id", id);
                deleteLines.Parameters.AddWithValue("@name", name);
                var deletedLines = await deleteLines.ExecuteNonQueryAsync();

                var deleteDocs = new NpgsqlCommand(
                    "DELETE FROM documents WHERE customer_id = @id OR customer_name = @name OR customer_input_name = @name", conn, tx);
                deleteDocs.Parameters.AddWithValue("@id", id);
                deleteDocs.Parameters.AddWithValue("@name", name);
                var deletedDocs = await deleteDocs.ExecuteNonQueryAsync();

                var deletePayments = new NpgsqlCommand(
                    "DELETE FROM payments WHERE customer_id = @id OR customer_name = @name OR customer_input_name = @name", conn, tx);
                deletePayments.Parameters.AddWithValue("@id", id);
                deletePayments.Parameters.AddWithValue("@name", name);
                var deletedPayments = await deletePayments.ExecuteNonQueryAsync();

                var deleteAliases = new NpgsqlCommand(
                    "DELETE FROM customer_aliases WHERE customer_id = @id OR customer_name = @name OR alias = @name", conn, tx);
                deleteAliases.Parameters.AddWithValue("@id", id);
                deleteAliases.Parameters.AddWithValue("@name", name);
                var deletedAliases = await deleteAliases.ExecuteNonQueryAsync();

                var deleteCustomer = new NpgsqlCommand("DELETE FROM customers WHERE id = @id", conn, tx);
                deleteCustomer.Parameters.AddWithValue("@id", id);
                var deletedCustomers = await deleteCustomer.ExecuteNonQueryAsync();
                if (deletedCustomers == 0)
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                await tx.CommitAsync();
                await db.RecordAudit(u.Username(), "Xóa khách hàng", "Customer", name,
                    $"Xóa vĩnh viễn khách hàng và dữ liệu liên quan (web). Phiếu: {deletedDocs}, dòng hàng: {deletedLines}, thanh toán: {deletedPayments}, alias: {deletedAliases}.");
                return Results.NoContent();
            }
            catch (NpgsqlException ex)
            {
                await tx.RollbackAsync();
                return Results.Json(new { message = "Lỗi xóa khách hàng: " + ex.Message }, statusCode: 400);
            }
        });

        // ---------- Reports (Báo cáo) ----------
        api.MapGet("/reports", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var now = DateTime.Now;
            var totalPayments = (decimal)(await conn.Cmd("SELECT COALESCE(SUM(amount),0) FROM payments").ExecuteScalarAsync() ?? 0m);
            var totalDocuments = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM documents").ExecuteScalarAsync() ?? 0);
            var activeCustomers = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM customers WHERE is_active = TRUE").ExecuteScalarAsync() ?? 0);
            var monthRevenue = (decimal)(await conn.Cmd(
                @"SELECT COALESCE(SUM(l.quantity * l.unit_price),0) FROM documents d
                  JOIN document_lines l ON l.document_id = d.id
                  WHERE EXTRACT(YEAR FROM d.doc_date)=@y AND EXTRACT(MONTH FROM d.doc_date)=@m")
                .With("@y", now.Year).With("@m", now.Month).ExecuteScalarAsync() ?? 0m);

            var monthly = new List<MonthlyRowDto>();
            await using var r = await conn.Cmd(
                @"SELECT EXTRACT(YEAR FROM d.doc_date)::int AS y, EXTRACT(MONTH FROM d.doc_date)::int AS m,
                         COUNT(DISTINCT d.id)::int AS docs,
                         COALESCE(SUM(l.quantity * l.unit_price),0) AS total
                  FROM documents d LEFT JOIN document_lines l ON l.document_id = d.id
                  GROUP BY EXTRACT(YEAR FROM d.doc_date), EXTRACT(MONTH FROM d.doc_date)
                  ORDER BY y DESC, m DESC LIMIT 12").ExecuteReaderAsync();
            while (await r.ReadAsync())
                monthly.Add(new MonthlyRowDto(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), 0, r.GetDecimal(3)));

            return Results.Ok(new ReportsDto(totalPayments, monthRevenue, totalDocuments, activeCustomers, monthly));
        });

        // ---------- Audit log (Sao lưu) ----------
        // Nhật ký thao tác quản trị chứa dấu vết mọi hành động nhạy cảm → chỉ Admin được xem.
        // Hỗ trợ lọc theo từ khóa (người dùng/hành động/đối tượng/chi tiết) để tra cứu nhanh.
        api.MapGet("/audit", async (Database db, int? take, string? search) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<AuditDto>();
            var hasSearch = !string.IsNullOrWhiteSpace(search);
            var where = hasSearch
                ? "WHERE (username ILIKE @s OR action ILIKE @s OR entity ILIKE @s OR entity_name ILIKE @s OR details ILIKE @s)"
                : "";
            var cmd = conn.Cmd(
                $@"SELECT occurred_at, username, action, entity, entity_name, details
                   FROM audit_logs {where} ORDER BY occurred_at DESC LIMIT @n")
                .With("@n", take is > 0 and <= 1000 ? take.Value : 100);
            if (hasSearch) cmd.With("@s", $"%{search!.Trim()}%");
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new AuditDto(r.Dt("occurred_at"), r.Str("username"), r.Str("action"),
                    r.Str("entity"), r.Str("entity_name"), r.Str("details")));
            return Results.Ok(list);
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    private static async Task<IResult> ListDocuments(Database db)
    {
        await using var conn = await db.OpenAsync();
        var list = new List<DocumentListItemDto>();
        await using var r = await conn.Cmd(
            $@"SELECT d.id, d.voucher_no, d.doc_date,
                      CASE
                        WHEN UPPER(d.voucher_no) LIKE 'PT%' OR LOWER(d.content) LIKE '%phiếu thu%' OR LOWER(d.content) LIKE '%thu tiền%' THEN 'Phiếu thu'
                        WHEN UPPER(d.voucher_no) LIKE 'PC%' OR LOWER(d.content) LIKE '%phiếu chi%' OR LOWER(d.content) LIKE '%chi tiền%' THEN 'Phiếu chi'
                        ELSE 'Phiếu xuất kho bán hàng'
                      END AS document_type,
                      d.customer_name, d.content, {TotalSub} AS total,
                      COALESCE(NULLIF(au.full_name, ''), creator.username, '') AS created_by
               FROM documents d
               LEFT JOIN LATERAL (
                   SELECT a.username
                   FROM audit_logs a
                   WHERE a.entity = 'Document'
                     AND (a.entity_name = d.voucher_no OR a.entity_name = d.id::text)
                     AND (a.action ILIKE 'Tạo%' OR a.details ILIKE 'Tạo%')
                   ORDER BY a.occurred_at ASC
                   LIMIT 1
               ) creator ON TRUE
               LEFT JOIN app_users au ON au.username = creator.username
               ORDER BY d.doc_date DESC, d.voucher_no DESC").ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new DocumentListItemDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                r.Str("document_type"), r.Str("customer_name"), r.Str("content"), r.Dec("total"), r.Str("created_by")));
        return Results.Ok(list);
    }

    private static async Task<CustomerDto?> ReadCustomer(NpgsqlConnection conn, Guid id)
    {
        await using var r = await conn.Cmd(
            "SELECT id, name, tax_code, phone, address, is_active FROM customers WHERE id = @id")
            .With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new CustomerDto(r.Guid("id"), r.Str("name"), r.Str("tax_code"),
            r.Str("phone"), r.Str("address"), r.Bool("is_active"));
    }

    private static async Task<List<DocumentListItemDto>> ReadCustomerDocuments(NpgsqlConnection conn, Guid customerId, string customerName)
    {
        var list = new List<DocumentListItemDto>();
        await using var r = await conn.Cmd(
            $@"SELECT d.id, d.voucher_no, d.doc_date,
                      CASE
                        WHEN UPPER(d.voucher_no) LIKE 'PT%' OR LOWER(d.content) LIKE '%phiếu thu%' OR LOWER(d.content) LIKE '%thu tiền%' THEN 'Phiếu thu'
                        WHEN UPPER(d.voucher_no) LIKE 'PC%' OR LOWER(d.content) LIKE '%phiếu chi%' OR LOWER(d.content) LIKE '%chi tiền%' THEN 'Phiếu chi'
                        ELSE 'Phiếu xuất kho bán hàng'
                      END AS document_type,
                      d.customer_name, d.content, {TotalSub} AS total,
                      COALESCE(NULLIF(au.full_name, ''), creator.username, '') AS created_by
               FROM documents d
               LEFT JOIN LATERAL (
                   SELECT a.username
                   FROM audit_logs a
                   WHERE a.entity = 'Document'
                     AND (a.entity_name = d.voucher_no OR a.entity_name = d.id::text)
                     AND (a.action ILIKE 'Tạo%' OR a.details ILIKE 'Tạo%')
                   ORDER BY a.occurred_at ASC
                   LIMIT 1
               ) creator ON TRUE
               LEFT JOIN app_users au ON au.username = creator.username
               WHERE d.customer_id = @id OR d.customer_name = @name
               ORDER BY d.doc_date DESC, d.voucher_no DESC")
            .With("@id", customerId)
            .With("@name", customerName)
            .ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new DocumentListItemDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                r.Str("document_type"), r.Str("customer_name"), r.Str("content"), r.Dec("total"), r.Str("created_by")));
        return list;
    }

    private static async Task<IResult> SaveCustomer(Database db, ClaimsPrincipal u, Guid? id, SaveCustomerRequest req)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Results.BadRequest(new { message = "Vui lòng nhập tên khách hàng." });

        await using var conn = await db.OpenAsync();
        await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            var duplicateCmd = new NpgsqlCommand(
                id is null
                    ? @"SELECT id FROM customers WHERE is_active = TRUE AND name = @name LIMIT 1"
                    : @"SELECT id FROM customers WHERE is_active = TRUE AND name = @name AND id <> @id LIMIT 1", conn, tx);
            duplicateCmd.Parameters.AddWithValue("@name", name);
            if (id is not null) duplicateCmd.Parameters.AddWithValue("@id", id.Value);
            if (await duplicateCmd.ExecuteScalarAsync() is Guid)
            {
                await tx.RollbackAsync();
                return Results.BadRequest(new { message = "Tên khách hàng đã tồn tại." });
            }

            var customerId = id ?? Guid.NewGuid();
            if (id is null)
            {
                var cmd = new NpgsqlCommand(
                    @"INSERT INTO customers (id, name, tax_code, phone, address, is_active)
                      VALUES (@id, @name, @tax, @phone, @address, TRUE)", conn, tx);
                FillCustomer(cmd, customerId, name, req);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var cmd = new NpgsqlCommand(
                    @"UPDATE customers
                      SET name = @name, tax_code = @tax, phone = @phone, address = @address, is_active = TRUE
                      WHERE id = @id", conn, tx);
                FillCustomer(cmd, customerId, name, req);
                var updated = await cmd.ExecuteNonQueryAsync();
                if (updated == 0)
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                var sync = new NpgsqlCommand(
                    @"UPDATE documents
                      SET customer_name = @name, customer_input_name = @name
                      WHERE customer_id = @id", conn, tx);
                sync.Parameters.AddWithValue("@name", name);
                sync.Parameters.AddWithValue("@id", customerId);
                await sync.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), id is null ? "Tạo khách hàng" : "Cập nhật khách hàng",
                "Customer", name, $"{(id is null ? "Tạo" : "Cập nhật")} khách hàng (web).");
            return Results.Ok(new { id = customerId });
        }
        catch (NpgsqlException ex)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Lỗi lưu khách hàng: " + ex.Message }, statusCode: 400);
        }
    }

    private static void FillCustomer(NpgsqlCommand cmd, Guid customerId, string name, SaveCustomerRequest req)
    {
        cmd.Parameters.AddWithValue("@id", customerId);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@tax", req.TaxCode ?? "");
        cmd.Parameters.AddWithValue("@phone", req.Phone ?? "");
        cmd.Parameters.AddWithValue("@address", req.Address ?? "");
    }

    private static async Task<IResult> SaveDocument(Database db, ClaimsPrincipal u, Guid? id, SaveDocumentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.VoucherNo))
            return Results.BadRequest(new { message = "Vui lòng nhập số phiếu." });

        await using var conn = await db.OpenAsync();
        await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            var customerId = await ResolveCustomer(conn, tx, req.CustomerName);
            var docId = id ?? Guid.NewGuid();
            var docDate = req.Date;

            if (id is null)
            {
                var cmd = new NpgsqlCommand(
                    @"INSERT INTO documents (id, voucher_no, doc_date, customer_id, customer_name, customer_input_name, content, note)
                      VALUES (@id, @v, @dt, @cid, @cn, @cin, @c, @n)", conn, tx);
                Fill(cmd, docId, req, customerId, docDate);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var cmd = new NpgsqlCommand(
                    @"UPDATE documents SET voucher_no=@v, doc_date=@dt, customer_id=@cid,
                        customer_name=@cn, customer_input_name=@cin, content=@c, note=@n WHERE id=@id", conn, tx);
                Fill(cmd, docId, req, customerId, docDate);
                var updated = await cmd.ExecuteNonQueryAsync();
                if (updated == 0) { await tx.RollbackAsync(); return Results.NotFound(); }

                await new NpgsqlCommand("DELETE FROM document_lines WHERE document_id=@id", conn, tx)
                    { Parameters = { new("@id", docId) } }.ExecuteNonQueryAsync();
            }

            var lineNo = 1;
            foreach (var line in req.Lines ?? new())
            {
                var lc = new NpgsqlCommand(
                    @"INSERT INTO document_lines (document_id, line_no, line_content, category, spec, quantity, unit_price, note)
                      VALUES (@d, @ln, @lc, '', @sp, @q, @up, @nt)", conn, tx);
                lc.Parameters.AddWithValue("@d", docId);
                lc.Parameters.AddWithValue("@ln", lineNo++);
                lc.Parameters.AddWithValue("@lc", line.LineContent ?? "");
                lc.Parameters.AddWithValue("@sp", line.Spec ?? "");
                lc.Parameters.AddWithValue("@q", line.Quantity);
                lc.Parameters.AddWithValue("@up", line.UnitPrice);
                lc.Parameters.AddWithValue("@nt", line.Note ?? "");
                await lc.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), id is null ? "Tạo phiếu kế toán" : "Cập nhật phiếu kế toán",
                "Document", req.VoucherNo, $"{(id is null ? "Tạo" : "Cập nhật")} phiếu kế toán (web).");
            return Results.Ok(new { id = docId });
        }
        catch (NpgsqlException ex)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Lỗi lưu phiếu kế toán: " + ex.Message }, statusCode: 400);
        }
    }

    private static void Fill(NpgsqlCommand cmd, Guid docId, SaveDocumentRequest req, Guid customerId, DateOnly docDate)
    {
        cmd.Parameters.AddWithValue("@id", docId);
        cmd.Parameters.AddWithValue("@v", req.VoucherNo.Trim());
        cmd.Parameters.AddWithValue("@dt", docDate);
        cmd.Parameters.AddWithValue("@cid", customerId);
        cmd.Parameters.AddWithValue("@cn", req.CustomerName ?? "");
        cmd.Parameters.AddWithValue("@cin", req.CustomerName ?? "");
        cmd.Parameters.AddWithValue("@c", req.Content ?? "");
        cmd.Parameters.AddWithValue("@n", req.Note ?? "");
    }

    /// <summary>Tìm khách hàng theo tên, tạo mới nếu chưa có — giống logic AddDocument của app desktop.</summary>
    private static async Task<Guid> ResolveCustomer(NpgsqlConnection conn, NpgsqlTransaction tx, string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = "Khách lẻ";

        var find = new NpgsqlCommand("SELECT id FROM customers WHERE name = @n", conn, tx);
        find.Parameters.AddWithValue("@n", name);
        if (await find.ExecuteScalarAsync() is Guid existing) return existing;

        var newId = Guid.NewGuid();
        var ins = new NpgsqlCommand(
            "INSERT INTO customers (id, name, is_active) VALUES (@id, @n, TRUE)", conn, tx);
        ins.Parameters.AddWithValue("@id", newId);
        ins.Parameters.AddWithValue("@n", name);
        await ins.ExecuteNonQueryAsync();
        return newId;
    }
}
