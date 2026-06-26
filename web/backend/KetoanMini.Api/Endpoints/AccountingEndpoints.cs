using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Endpoints;

public static class AccountingEndpoints
{
    private const string TotalSub =
        "(SELECT ISNULL(SUM(l.quantity * l.unit_price), 0) FROM dbo.document_lines l WHERE l.document_id = d.id)";

    public static void MapAccounting(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---------- Dashboard ----------
        api.MapGet("/dashboard", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var now = DateTime.Now;

            var activeCustomers = (int)(await conn.Cmd("SELECT COUNT(*) FROM dbo.customers WHERE is_active = 1").ExecuteScalarAsync() ?? 0);
            var totalDocuments = (int)(await conn.Cmd("SELECT COUNT(*) FROM dbo.documents").ExecuteScalarAsync() ?? 0);
            var totalPayments = (decimal)(await conn.Cmd("SELECT ISNULL(SUM(amount),0) FROM dbo.payments").ExecuteScalarAsync() ?? 0m);
            var monthRevenue = (decimal)(await conn.Cmd(
                @"SELECT ISNULL(SUM(l.quantity * l.unit_price),0)
                  FROM dbo.documents d JOIN dbo.document_lines l ON l.document_id = d.id
                  WHERE YEAR(d.doc_date) = @y AND MONTH(d.doc_date) = @m")
                .With("@y", now.Year).With("@m", now.Month).ExecuteScalarAsync() ?? 0m);

            var recent = new List<RecentDocDto>();
            await using (var r = await conn.Cmd(
                $@"SELECT TOP 12 d.id, d.voucher_no, d.doc_date, d.customer_name, d.content, {TotalSub} AS total
                   FROM dbo.documents d ORDER BY d.doc_date DESC, d.voucher_no DESC").ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    recent.Add(new RecentDocDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                        r.Str("customer_name"), r.Str("content"), r.Dec("total")));
            }

            return Results.Ok(new DashboardDto(activeCustomers, totalDocuments, totalPayments, monthRevenue, now.Month, now.Year, recent));
        });

        // ---------- Documents (Káº¿ toÃ¡n) ----------
        api.MapGet("/documents", ListDocuments);

        api.MapGet("/documents/{id:guid}", async (Guid id, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            DocumentDetailDto? doc = null;
            await using (var r = await conn.Cmd(
                "SELECT id, voucher_no, doc_date, customer_name, content, note FROM dbo.documents WHERE id = @id")
                .With("@id", id).ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                    doc = new DocumentDetailDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                        r.Str("customer_name"), r.Str("content"), r.Str("note"), new());
            }
            if (doc is null) return Results.NotFound();

            await using (var r = await conn.Cmd(
                @"SELECT line_content, spec, quantity, unit_price, note FROM dbo.document_lines
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
            var n = await conn.Cmd("DELETE FROM dbo.documents WHERE id = @id").With("@id", id).ExecuteNonQueryAsync();
            if (n > 0) await db.RecordAudit(u.Username(), "XÃ³a phiáº¿u káº¿ toÃ¡n", "Document", id.ToString(), "XÃ³a phiáº¿u káº¿ toÃ¡n (web).");
            return n > 0 ? Results.NoContent() : Results.NotFound();
        });

        // ---------- Customers ----------
        api.MapGet("/customers", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<CustomerDto>();
            await using var r = await conn.Cmd(
                "SELECT id, name, tax_code, phone, address, is_active FROM dbo.customers WHERE is_active = 1 ORDER BY name")
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
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                var nameCmd = new SqlCommand("SELECT name FROM dbo.customers WHERE id = @id", conn, tx);
                nameCmd.Parameters.AddWithValue("@id", id);
                var name = await nameCmd.ExecuteScalarAsync() as string;
                if (name is null)
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                var deleteLines = new SqlCommand(
                    @"DELETE l
                      FROM dbo.document_lines l
                      INNER JOIN dbo.documents d ON d.id = l.document_id
                      WHERE d.customer_id = @id OR d.customer_name = @name OR d.customer_input_name = @name", conn, tx);
                deleteLines.Parameters.AddWithValue("@id", id);
                deleteLines.Parameters.AddWithValue("@name", name);
                var deletedLines = await deleteLines.ExecuteNonQueryAsync();

                var deleteDocs = new SqlCommand(
                    "DELETE FROM dbo.documents WHERE customer_id = @id OR customer_name = @name OR customer_input_name = @name", conn, tx);
                deleteDocs.Parameters.AddWithValue("@id", id);
                deleteDocs.Parameters.AddWithValue("@name", name);
                var deletedDocs = await deleteDocs.ExecuteNonQueryAsync();

                var deletePayments = new SqlCommand(
                    "DELETE FROM dbo.payments WHERE customer_id = @id OR customer_name = @name OR customer_input_name = @name", conn, tx);
                deletePayments.Parameters.AddWithValue("@id", id);
                deletePayments.Parameters.AddWithValue("@name", name);
                var deletedPayments = await deletePayments.ExecuteNonQueryAsync();

                var deleteAliases = new SqlCommand(
                    "DELETE FROM dbo.customer_aliases WHERE customer_id = @id OR customer_name = @name OR alias = @name", conn, tx);
                deleteAliases.Parameters.AddWithValue("@id", id);
                deleteAliases.Parameters.AddWithValue("@name", name);
                var deletedAliases = await deleteAliases.ExecuteNonQueryAsync();

                var deleteCustomer = new SqlCommand("DELETE FROM dbo.customers WHERE id = @id", conn, tx);
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
            catch (SqlException ex)
            {
                await tx.RollbackAsync();
                return Results.Json(new { message = "Lỗi xóa khách hàng: " + ex.Message }, statusCode: 400);
            }
        });

        // ---------- Reports (BÃ¡o cÃ¡o) ----------
        api.MapGet("/reports", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var now = DateTime.Now;
            var totalPayments = (decimal)(await conn.Cmd("SELECT ISNULL(SUM(amount),0) FROM dbo.payments").ExecuteScalarAsync() ?? 0m);
            var totalDocuments = (int)(await conn.Cmd("SELECT COUNT(*) FROM dbo.documents").ExecuteScalarAsync() ?? 0);
            var activeCustomers = (int)(await conn.Cmd("SELECT COUNT(*) FROM dbo.customers WHERE is_active = 1").ExecuteScalarAsync() ?? 0);
            var monthRevenue = (decimal)(await conn.Cmd(
                @"SELECT ISNULL(SUM(l.quantity * l.unit_price),0) FROM dbo.documents d
                  JOIN dbo.document_lines l ON l.document_id = d.id
                  WHERE YEAR(d.doc_date)=@y AND MONTH(d.doc_date)=@m")
                .With("@y", now.Year).With("@m", now.Month).ExecuteScalarAsync() ?? 0m);

            var monthly = new List<MonthlyRowDto>();
            await using var r = await conn.Cmd(
                @"SELECT TOP 12 YEAR(d.doc_date) AS y, MONTH(d.doc_date) AS m,
                         COUNT(DISTINCT d.id) AS docs,
                         ISNULL(SUM(l.quantity * l.unit_price),0) AS total
                  FROM dbo.documents d LEFT JOIN dbo.document_lines l ON l.document_id = d.id
                  GROUP BY YEAR(d.doc_date), MONTH(d.doc_date)
                  ORDER BY y DESC, m DESC").ExecuteReaderAsync();
            while (await r.ReadAsync())
                monthly.Add(new MonthlyRowDto(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), 0, r.GetDecimal(3)));

            return Results.Ok(new ReportsDto(totalPayments, monthRevenue, totalDocuments, activeCustomers, monthly));
        });

        // ---------- Audit log (Sao lÆ°u) ----------
        api.MapGet("/audit", async (Database db, int? take) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<AuditDto>();
            await using var r = await conn.Cmd(
                @"SELECT TOP (@n) occurred_at, username, action, entity, entity_name, details
                  FROM dbo.audit_logs ORDER BY occurred_at DESC")
                .With("@n", take is > 0 and <= 1000 ? take.Value : 100).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new AuditDto(r.Dt("occurred_at"), r.Str("username"), r.Str("action"),
                    r.Str("entity"), r.Str("entity_name"), r.Str("details")));
            return Results.Ok(list);
        });
    }

    private static async Task<IResult> ListDocuments(Database db)
    {
        await using var conn = await db.OpenAsync();
        var list = new List<DocumentListItemDto>();
        await using var r = await conn.Cmd(
            $@"SELECT d.id, d.voucher_no, d.doc_date,
                      CASE
                        WHEN UPPER(d.voucher_no) LIKE N'PT%' OR LOWER(d.content) LIKE N'%phiáº¿u thu%' OR LOWER(d.content) LIKE N'%thu tiá»n%' THEN N'Phiáº¿u thu'
                        WHEN UPPER(d.voucher_no) LIKE N'PC%' OR LOWER(d.content) LIKE N'%phiáº¿u chi%' OR LOWER(d.content) LIKE N'%chi tiá»n%' THEN N'Phiáº¿u chi'
                        ELSE N'Phiáº¿u xuáº¥t kho bÃ¡n hÃ ng'
                      END AS document_type,
                      d.customer_name, d.content, {TotalSub} AS total,
                      COALESCE(NULLIF(au.full_name, N''), creator.username, N'') AS created_by
               FROM dbo.documents d
               OUTER APPLY (
                   SELECT TOP 1 a.username
                   FROM dbo.audit_logs a
                   WHERE a.entity = N'Document'
                     AND (a.entity_name = d.voucher_no OR a.entity_name = CONVERT(NVARCHAR(36), d.id))
                     AND (a.action LIKE N'Táº¡o%' OR a.details LIKE N'Táº¡o%')
                   ORDER BY a.occurred_at ASC
               ) creator
               LEFT JOIN dbo.app_users au ON au.username = creator.username
               ORDER BY d.doc_date DESC, d.voucher_no DESC").ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new DocumentListItemDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                r.Str("document_type"), r.Str("customer_name"), r.Str("content"), r.Dec("total"), r.Str("created_by")));
        return Results.Ok(list);
    }

    private static async Task<CustomerDto?> ReadCustomer(SqlConnection conn, Guid id)
    {
        await using var r = await conn.Cmd(
            "SELECT id, name, tax_code, phone, address, is_active FROM dbo.customers WHERE id = @id")
            .With("@id", id).ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new CustomerDto(r.Guid("id"), r.Str("name"), r.Str("tax_code"),
            r.Str("phone"), r.Str("address"), r.Bool("is_active"));
    }

    private static async Task<List<DocumentListItemDto>> ReadCustomerDocuments(SqlConnection conn, Guid customerId, string customerName)
    {
        var list = new List<DocumentListItemDto>();
        await using var r = await conn.Cmd(
            $@"SELECT d.id, d.voucher_no, d.doc_date,
                      CASE
                        WHEN UPPER(d.voucher_no) LIKE N'PT%' OR LOWER(d.content) LIKE N'%phiáº¿u thu%' OR LOWER(d.content) LIKE N'%thu tiá»n%' THEN N'Phiáº¿u thu'
                        WHEN UPPER(d.voucher_no) LIKE N'PC%' OR LOWER(d.content) LIKE N'%phiáº¿u chi%' OR LOWER(d.content) LIKE N'%chi tiá»n%' THEN N'Phiáº¿u chi'
                        ELSE N'Phiáº¿u xuáº¥t kho bÃ¡n hÃ ng'
                      END AS document_type,
                      d.customer_name, d.content, {TotalSub} AS total,
                      COALESCE(NULLIF(au.full_name, N''), creator.username, N'') AS created_by
               FROM dbo.documents d
               OUTER APPLY (
                   SELECT TOP 1 a.username
                   FROM dbo.audit_logs a
                   WHERE a.entity = N'Document'
                     AND (a.entity_name = d.voucher_no OR a.entity_name = CONVERT(NVARCHAR(36), d.id))
                     AND (a.action LIKE N'Táº¡o%' OR a.details LIKE N'Táº¡o%')
                   ORDER BY a.occurred_at ASC
               ) creator
               LEFT JOIN dbo.app_users au ON au.username = creator.username
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
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            var duplicateCmd = new SqlCommand(
                @"SELECT TOP 1 id FROM dbo.customers
                  WHERE is_active = 1 AND name = @name AND (@id IS NULL OR id <> @id)", conn, tx);
            duplicateCmd.Parameters.AddWithValue("@name", name);
            duplicateCmd.Parameters.AddWithValue("@id", id is null ? DBNull.Value : id.Value);
            if (await duplicateCmd.ExecuteScalarAsync() is Guid)
            {
                await tx.RollbackAsync();
                return Results.BadRequest(new { message = "Tên khách hàng đã tồn tại." });
            }

            var customerId = id ?? Guid.NewGuid();
            if (id is null)
            {
                var cmd = new SqlCommand(
                    @"INSERT INTO dbo.customers (id, name, tax_code, phone, address, is_active)
                      VALUES (@id, @name, @tax, @phone, @address, 1)", conn, tx);
                FillCustomer(cmd, customerId, name, req);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var cmd = new SqlCommand(
                    @"UPDATE dbo.customers
                      SET name = @name, tax_code = @tax, phone = @phone, address = @address, is_active = 1
                      WHERE id = @id", conn, tx);
                FillCustomer(cmd, customerId, name, req);
                var updated = await cmd.ExecuteNonQueryAsync();
                if (updated == 0)
                {
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                var sync = new SqlCommand(
                    @"UPDATE dbo.documents
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
        catch (SqlException ex)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Lỗi lưu khách hàng: " + ex.Message }, statusCode: 400);
        }
    }

    private static void FillCustomer(SqlCommand cmd, Guid customerId, string name, SaveCustomerRequest req)
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
            return Results.BadRequest(new { message = "Vui lÃ²ng nháº­p sá»‘ phiáº¿u." });

        await using var conn = await db.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            var customerId = await ResolveCustomer(conn, tx, req.CustomerName);
            var docId = id ?? Guid.NewGuid();
            var docDate = req.Date.ToDateTime(TimeOnly.MinValue);

            if (id is null)
            {
                var cmd = new SqlCommand(
                    @"INSERT INTO dbo.documents (id, voucher_no, doc_date, customer_id, customer_name, customer_input_name, content, note)
                      VALUES (@id, @v, @dt, @cid, @cn, @cin, @c, @n)", conn, tx);
                Fill(cmd, docId, req, customerId, docDate);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var cmd = new SqlCommand(
                    @"UPDATE dbo.documents SET voucher_no=@v, doc_date=@dt, customer_id=@cid,
                        customer_name=@cn, customer_input_name=@cin, content=@c, note=@n WHERE id=@id", conn, tx);
                Fill(cmd, docId, req, customerId, docDate);
                var updated = await cmd.ExecuteNonQueryAsync();
                if (updated == 0) { await tx.RollbackAsync(); return Results.NotFound(); }

                await new SqlCommand("DELETE FROM dbo.document_lines WHERE document_id=@id", conn, tx)
                    { Parameters = { new("@id", docId) } }.ExecuteNonQueryAsync();
            }

            var lineNo = 1;
            foreach (var line in req.Lines ?? new())
            {
                var lc = new SqlCommand(
                    @"INSERT INTO dbo.document_lines (document_id, line_no, line_content, category, spec, quantity, unit_price, note)
                      VALUES (@d, @ln, @lc, N'', @sp, @q, @up, @nt)", conn, tx);
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
            await db.RecordAudit(u.Username(), id is null ? "Táº¡o phiáº¿u káº¿ toÃ¡n" : "Cáº­p nháº­t phiáº¿u káº¿ toÃ¡n",
                "Document", req.VoucherNo, $"{(id is null ? "Táº¡o" : "Cáº­p nháº­t")} phiáº¿u káº¿ toÃ¡n (web).");
            return Results.Ok(new { id = docId });
        }
        catch (SqlException ex)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Lá»—i lÆ°u phiáº¿u káº¿ toÃ¡n: " + ex.Message }, statusCode: 400);
        }
    }

    private static void Fill(SqlCommand cmd, Guid docId, SaveDocumentRequest req, Guid customerId, DateTime docDate)
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

    /// <summary>TÃ¬m khÃ¡ch hÃ ng theo tÃªn, táº¡o má»›i náº¿u chÆ°a cÃ³ â€” giá»‘ng logic AddDocument cá»§a app desktop.</summary>
    private static async Task<Guid> ResolveCustomer(SqlConnection conn, SqlTransaction tx, string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = "KhÃ¡ch láº»";

        var find = new SqlCommand("SELECT id FROM dbo.customers WHERE name = @n", conn, tx);
        find.Parameters.AddWithValue("@n", name);
        if (await find.ExecuteScalarAsync() is Guid existing) return existing;

        var newId = Guid.NewGuid();
        var ins = new SqlCommand(
            "INSERT INTO dbo.customers (id, name, is_active) VALUES (@id, @n, 1)", conn, tx);
        ins.Parameters.AddWithValue("@id", newId);
        ins.Parameters.AddWithValue("@n", name);
        await ins.ExecuteNonQueryAsync();
        return newId;
    }
}
