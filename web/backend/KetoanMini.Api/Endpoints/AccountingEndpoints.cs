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

        // ---------- Documents (Kế toán) ----------
        api.MapGet("/documents", (Database db) => ListDocuments(db, salesOnly: false));
        api.MapGet("/sales", (Database db) => ListDocuments(db, salesOnly: true));

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
            if (n > 0) await db.RecordAudit(u.Username(), "Xóa chứng từ", "Document", id.ToString(), "Xóa chứng từ (web).");
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

        // ---------- Reports (Báo cáo) ----------
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

        // ---------- Audit log (Sao lưu) ----------
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

    private static async Task<IResult> ListDocuments(Database db, bool salesOnly)
    {
        await using var conn = await db.OpenAsync();
        var where = salesOnly
            ? "WHERE LOWER(d.content) LIKE N'%bán%' OR d.voucher_no LIKE N'BH%'"
            : "";
        var list = new List<DocumentListItemDto>();
        await using var r = await conn.Cmd(
            $@"SELECT d.id, d.voucher_no, d.doc_date, d.customer_name, d.content, {TotalSub} AS total
               FROM dbo.documents d {where}
               ORDER BY d.doc_date DESC, d.voucher_no DESC").ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new DocumentListItemDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                r.Str("customer_name"), r.Str("content"), r.Dec("total")));
        return Results.Ok(list);
    }

    private static async Task<IResult> SaveDocument(Database db, ClaimsPrincipal u, Guid? id, SaveDocumentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.VoucherNo))
            return Results.BadRequest(new { message = "Vui lòng nhập số phiếu." });

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
            await db.RecordAudit(u.Username(), id is null ? "Tạo chứng từ" : "Cập nhật chứng từ",
                "Document", req.VoucherNo, $"{(id is null ? "Tạo" : "Cập nhật")} chứng từ (web).");
            return Results.Ok(new { id = docId });
        }
        catch (SqlException ex)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Lỗi lưu chứng từ: " + ex.Message }, statusCode: 400);
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

    /// <summary>Tìm khách hàng theo tên, tạo mới nếu chưa có — giống logic AddDocument của app desktop.</summary>
    private static async Task<Guid> ResolveCustomer(SqlConnection conn, SqlTransaction tx, string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) name = "Khách lẻ";

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
