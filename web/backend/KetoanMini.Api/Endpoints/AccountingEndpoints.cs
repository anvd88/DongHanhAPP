using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

public static class AccountingEndpoints
{
    private const string TotalSub =
        "(SELECT COALESCE(SUM(l.quantity * l.unit_price), 0) FROM document_lines l WHERE l.document_id = d.id)";
    // Một máy chủ chỉ có một Excel/máy in mặc định. Giữ trọn chuỗi đọc → in → phát hành trong cùng
    // khóa để hai người không thể đồng thời in hai số khác nhau cho cùng một chứng từ.
    private static readonly SemaphoreSlim WarehouseIssueGate = new(1, 1);

    public static void MapAccounting(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequirePermission(Permissions.AccountingAccess);

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
                  WHERE d.cancelled_at IS NULL
                    AND EXTRACT(YEAR FROM d.doc_date) = @y AND EXTRACT(MONTH FROM d.doc_date) = @m")
                .With("@y", now.Year).With("@m", now.Month).ExecuteScalarAsync() ?? 0m);

            var recent = new List<RecentDocDto>();
            await using (var r = await conn.Cmd(
                $@"SELECT d.id, d.voucher_no, d.doc_date, d.customer_name, d.content, {TotalSub} AS total
                   FROM documents d
                   WHERE d.cancelled_at IS NULL AND d.document_type <> 'return'
                   ORDER BY d.doc_date DESC, d.voucher_no DESC LIMIT 12").ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    recent.Add(new RecentDocDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                        r.Str("customer_name"), r.Str("content"), r.Dec("total")));
            }

            return Results.Ok(new DashboardDto(activeCustomers, totalDocuments, totalPayments, monthRevenue, now.Month, now.Year, recent));
        });

        // ---------- Phiếu xuất kho (trang Kế toán) ----------
        api.MapGet("/documents", (Database db) => ListDocuments(db, cashOnly: false));

        // Phiếu thu/chi thông thường đã tách thành mô-đun và hợp đồng API riêng. Backend chốt loại
        // chứng từ, không dựa vào việc frontend ẩn tab để tránh ghi nhầm sang sổ xuất kho.
        api.MapGet("/cash-vouchers", (Database db) => ListDocuments(db, cashOnly: true));

        api.MapGet("/accounting/system-status", (
            HttpContext httpContext,
            WarehouseVoucherPrintService printer) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return Results.Ok(printer.GetSystemStatus());
        });

        // "Ngăn xếp phiếu" ở rìa trái trang chi tiết: danh sách gọn của MỘT NGÀY, tìm được theo tên
        // khách, số phiếu, và cả CHỦNG LOẠI HÀNG / QUY CÁCH — thứ chỉ có trong document_lines nên
        // phải lọc ở máy chủ, không thể lọc trên máy trạm (danh sách phiếu không mang theo dòng hàng).
        api.MapGet("/documents/stack", async (string? date, string? q, Database db) =>
        {
            var day = DateOnly.TryParse(date, out var parsed) ? parsed : (DateOnly?)null;
            var keyword = (q ?? "").Trim();
            var like = $"%{keyword}%";

            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd($"""
                SELECT d.id, d.voucher_no, d.doc_date,
                       COALESCE(NULLIF(d.customer_name,''), d.customer_input_name, '') AS customer_name,
                       {TotalSub} AS total,
                       d.issued_at, d.cancelled_at,
                       d.delivery_mode, d.delivery_returned_at,
                       COALESCE(t.status,'') AS delivery_task_status
                FROM documents d
                LEFT JOIN work_tasks t ON t.id = d.delivery_task_id
                WHERE d.document_type = 'document'
                  AND (@day IS NULL OR d.doc_date = @day)
                  AND (@q = '' OR d.voucher_no ILIKE @like
                       OR d.customer_name ILIKE @like OR d.customer_input_name ILIKE @like
                       OR EXISTS (
                            SELECT 1 FROM document_lines l
                            WHERE l.document_id = d.id
                              AND (l.line_content ILIKE @like OR l.spec ILIKE @like)))
                ORDER BY d.doc_date DESC, d.voucher_no DESC
                LIMIT 300
                """)
                .With("@day", (object?)day ?? DBNull.Value)
                .With("@q", keyword)
                .With("@like", like)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    id = r.Guid("id"),
                    voucherNo = r.Str("voucher_no"),
                    docDate = r.DateOnly("doc_date"),
                    customerName = r.Str("customer_name"),
                    total = r.Dec("total"),
                    issuedAt = r.DtNull("issued_at"),
                    cancelledAt = r.DtNull("cancelled_at"),
                    deliveryMode = r.Str("delivery_mode"),
                    deliveryTaskStatus = r.Str("delivery_task_status"),
                    deliveryReturnedAt = r.DtNull("delivery_returned_at"),
                });
            return Results.Ok(new { items });
        });

        api.MapGet("/documents/{id:guid}", async (Guid id, Database db) =>
        {
            var document = await ReadDocument(db, id, cashOnly: false);
            return document is null ? Results.NotFound() : Results.Ok(document);
        });

        api.MapPost("/documents", async (SaveDocumentRequest req, ClaimsPrincipal u, Database db) =>
            await SaveDocument(db, u, null, req, cashOnly: false));

        api.MapPut("/documents/{id:guid}", async (Guid id, SaveDocumentRequest req, ClaimsPrincipal u, Database db) =>
            await SaveDocument(db, u, id, req, cashOnly: false));

        api.MapGet("/cash-vouchers/{id:guid}", async (Guid id, Database db) =>
        {
            var document = await ReadDocument(db, id, cashOnly: true);
            return document is null ? Results.NotFound() : Results.Ok(document);
        });

        api.MapPost("/cash-vouchers", async (SaveDocumentRequest req, ClaimsPrincipal u, Database db) =>
            await SaveDocument(db, u, null, req, cashOnly: true));

        api.MapPut("/cash-vouchers/{id:guid}", async (Guid id, SaveDocumentRequest req, ClaimsPrincipal u, Database db) =>
            await SaveDocument(db, u, id, req, cashOnly: true));

        api.MapPost("/documents/{id:guid}/warehouse-print", async (
            Guid id,
            WarehousePrintRequest req,
            ClaimsPrincipal u,
            Database db,
            WarehouseVoucherPrintService printer,
            CancellationToken cancellationToken) =>
        {
            var voucherNo = (req.VoucherNo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(voucherNo))
                return Results.BadRequest(new { message = "Vui lòng nhập số phiếu trước khi in." });
            if (voucherNo.Length > 64)
                return Results.BadRequest(new { message = "Số phiếu không được vượt quá 64 ký tự." });

            await WarehouseIssueGate.WaitAsync(cancellationToken);
            try
            {
                var document = await ReadDocument(db, id, cashOnly: false);
                if (document is null) return Results.NotFound();
                var issueState = await ReadWarehouseIssueState(db, id);
                if (issueState is null) return Results.NotFound();
                if (issueState.CancelledAt is not null)
                    return Results.Conflict(new { message = "Phiếu đã hủy, không thể phát hành hoặc in." });
                if (issueState.IssuedAt is not null)
                {
                    if (!string.Equals(voucherNo, issueState.VoucherNo, StringComparison.Ordinal))
                    {
                        return Results.Conflict(new
                        {
                            message = $"Phiếu đã phát hành với số {issueState.VoucherNo}; không thể thay đổi số phiếu.",
                        });
                    }

                    voucherNo = issueState.VoucherNo;
                }
                if (await WarehouseVoucherNoExists(db, id, voucherNo))
                    return Results.Conflict(new { message = $"Số phiếu {voucherNo} đã được dùng cho phiếu xuất kho khác." });

                // Chỉ dùng số dự kiến trên bản in trong RAM. Database CHƯA nhận số ở thời điểm này.
                var printDocument = document with { VoucherNo = voucherNo };
                var printResult = await printer.PrintAsync(printDocument, cancellationToken);

                // PrintOut đã nhận lệnh thành công mới phát hành: số phiếu và thời điểm phát hành được
                // ghi trong CÙNG một UPDATE, không tồn tại trạng thái "có số nhưng chưa phát hành".
                var issuedAt = await FinalizeWarehouseIssue(db, id, voucherNo);
                if (issuedAt is null) return Results.NotFound();

                await db.RecordAudit(u.Username(), "In phiếu xuất kho", "Document",
                    voucherNo,
                    $"Đã gửi phiếu từ Excel tới máy in máy chủ: {printResult.PrinterName}.");
                return Results.Ok(new
                {
                    voucherNo,
                    issuedAt,
                    printerName = printResult.PrinterName,
                    submittedAt = printResult.SubmittedAt,
                });
            }
            catch (WarehousePrintValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (WarehousePrintUnavailableException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            finally
            {
                WarehouseIssueGate.Release();
            }
        });

        api.MapGet("/documents/{id:guid}/warehouse-preview", async (
            Guid id,
            string? voucherNo,
            HttpContext httpContext,
            Database db,
            WarehouseVoucherPrintService printer,
            CancellationToken cancellationToken) =>
        {
            voucherNo = (voucherNo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(voucherNo))
                return Results.BadRequest(new { message = "Vui lòng nhập số phiếu trước khi xem trước." });
            if (voucherNo.Length > 64)
                return Results.BadRequest(new { message = "Số phiếu không được vượt quá 64 ký tự." });

            try
            {
                var document = await ReadDocument(db, id, cashOnly: false);
                if (document is null) return Results.NotFound();
                var issueState = await ReadWarehouseIssueState(db, id);
                if (issueState is null) return Results.NotFound();
                if (issueState.CancelledAt is not null)
                    return Results.Conflict(new { message = "Phiếu đã hủy, không thể xem trước để in." });
                if (issueState.IssuedAt is not null)
                {
                    if (!string.Equals(voucherNo, issueState.VoucherNo, StringComparison.Ordinal))
                    {
                        return Results.Conflict(new
                        {
                            message = $"Phiếu đã phát hành với số {issueState.VoucherNo}; không thể thay đổi số phiếu.",
                        });
                    }

                    voucherNo = issueState.VoucherNo;
                }
                if (await WarehouseVoucherNoExists(db, id, voucherNo))
                    return Results.Conflict(new { message = $"Số phiếu {voucherNo} đã được dùng cho phiếu xuất kho khác." });

                // Chỉ dựng PDF từ mẫu Excel đã chốt; xem trước tuyệt đối không phát hành hay ghi số vào DB.
                var pdf = await printer.CreatePreviewPdfAsync(
                    document with { VoucherNo = voucherNo },
                    cancellationToken);

                // Toàn hệ thống mặc định cấm nhúng trang. Riêng PDF xem trước này được phép nằm
                // trong iframe CÙNG origin của trang kế toán; không cho website bên ngoài nhúng.
                httpContext.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                httpContext.Response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; frame-ancestors 'self'";
                return Results.File(pdf, "application/pdf", enableRangeProcessing: true);
            }
            catch (WarehousePrintValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (WarehousePrintUnavailableException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        api.MapPut("/cash-vouchers/{id:guid}/issued", async (
            Guid id,
            ClaimsPrincipal u,
            Database db) =>
        {
            var issuedAt = await MarkDocumentIssued(db, id, cashOnly: true);
            if (issuedAt is null)
            {
                if (await DocumentIsCancelled(db, id, cashOnly: true))
                    return Results.Conflict(new { message = "Phiếu đã hủy, không thể phát hành hoặc in." });
                return Results.NotFound();
            }

            await db.RecordAudit(u.Username(), "Phát hành phiếu thu chi", "Document", id.ToString(),
                "Bản in phiếu thu chi đã được tạo trên web.");
            return Results.Ok(new { issuedAt });
        });

        api.MapPut("/documents/{id:guid}/cancel", (Guid id, CancelDocumentRequest req, ClaimsPrincipal u, Database db) =>
            CancelDocument(db, u, id, req.Reason, cashOnly: false))
            .RequirePermission(Permissions.VouchersCancel);

        api.MapPut("/cash-vouchers/{id:guid}/cancel", (Guid id, CancelDocumentRequest req, ClaimsPrincipal u, Database db) =>
            CancelDocument(db, u, id, req.Reason, cashOnly: true))
            .RequirePermission(Permissions.VouchersCancel);

        // Giữ tương thích với các bản giao diện cũ nhưng không xóa dữ liệu: DELETE cũng chỉ chuyển trạng thái sang hủy.
        api.MapDelete("/documents/{id:guid}", (Guid id, ClaimsPrincipal u, Database db) =>
            CancelDocument(db, u, id, "Hủy từ yêu cầu xóa của phiên bản cũ.", cashOnly: false))
            .RequirePermission(Permissions.VouchersCancel);

        api.MapDelete("/cash-vouchers/{id:guid}", (Guid id, ClaimsPrincipal u, Database db) =>
            CancelDocument(db, u, id, "Hủy từ yêu cầu xóa của phiên bản cũ.", cashOnly: true))
            .RequirePermission(Permissions.VouchersCancel);

        api.MapDelete("/cash-vouchers/{id:guid}/permanent", (
            Guid id,
            ClaimsPrincipal u,
            Database db) => DeleteCashVoucher(db, u, id))
            .RequirePermission(Permissions.VouchersCancel);

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
            var activeDocuments = documents.Where(d => d.CancelledAt is null).ToList();
            var receiptTotal = activeDocuments
                .Where(d => d.DocumentType.Contains("thu", StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Total);
            var paymentTotal = activeDocuments
                .Where(d => d.DocumentType.Contains("chi", StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Total);
            var salesTotal = activeDocuments
                .Where(d => !d.DocumentType.Contains("thu", StringComparison.OrdinalIgnoreCase)
                            && !d.DocumentType.Contains("chi", StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Total);

            return Results.Ok(new CustomerReportDto(customer, documents.Count, activeDocuments.Sum(d => d.Total),
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

                // Giữ nguyên toàn bộ phiếu và dòng hàng để bảo toàn sổ kế toán. Chỉ bỏ liên kết tới
                // khách hàng sắp xóa; tên đã nhập trên phiếu vẫn được lưu nguyên trạng.
                var preserveDocs = new NpgsqlCommand(
                    @"UPDATE documents
                      SET customer_id = NULL, updated_at = CURRENT_TIMESTAMP
                      WHERE customer_id = @id", conn, tx);
                preserveDocs.Parameters.AddWithValue("@id", id);
                var preservedDocs = await preserveDocs.ExecuteNonQueryAsync();

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
                    $"Xóa khách hàng và dữ liệu phụ liên quan (web). Đã giữ nguyên {preservedDocs} phiếu kế toán; thanh toán xóa: {deletedPayments}, alias xóa: {deletedAliases}.");
                return Results.NoContent();
            }
            catch (NpgsqlException)
            {
                await tx.RollbackAsync();
                return Results.Json(new { message = "Không thể xóa khách hàng (dữ liệu liên quan hoặc ràng buộc)." }, statusCode: 400);
            }
        });

        // ---------- Công nợ khách hàng ----------
        api.MapGet("/debts", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var customers = new List<DebtSummaryDto>();
            await using var r = await conn.Cmd(
                $@"SELECT c.id, c.name, c.tax_code, c.phone, c.address, c.is_active,
                          COALESCE(ob.amount, 0) AS opening_balance,
                          ob.as_of_date AS opening_date,
                          COALESCE(ob.note, '') AS opening_note,
                          COALESCE(s.sales_total, 0) AS sales_total,
                          COALESCE(s.invoice_count, 0)::int AS invoice_count,
                          COALESCE(rt.return_total, 0) AS return_total,
                          COALESCE(rc.receipt_total, 0) + COALESCE(p.payment_total, 0) AS collected_total,
                          GREATEST(ob.as_of_date, s.last_date, rc.last_date, p.last_date, rt.last_date) AS last_activity_date
                   FROM customers c
                   LEFT JOIN customer_opening_balances ob ON ob.customer_id = c.id
                   LEFT JOIN LATERAL (
                     SELECT COALESCE(SUM({TotalSub}), 0) AS sales_total,
                            COUNT(*)::int AS invoice_count,
                            MAX(d.doc_date) AS last_date
                     FROM documents d
                     WHERE (d.customer_id = c.id OR (d.customer_id IS NULL AND d.customer_name = c.name))
                       AND d.document_type = 'document'
                       AND d.cancelled_at IS NULL
                       AND (ob.as_of_date IS NULL OR d.doc_date >= ob.as_of_date)
                   ) s ON TRUE
                   LEFT JOIN LATERAL (
                     -- Hàng khách trả về: giảm số ĐÃ BÁN, không phải khách trả tiền — để riêng chứ
                     -- không cộng vào 'đã thu', nếu không sổ nói dối là khách đã thanh toán.
                     SELECT COALESCE(SUM({TotalSub}), 0) AS return_total,
                            MAX(d.doc_date) AS last_date
                     FROM documents d
                     WHERE (d.customer_id = c.id OR (d.customer_id IS NULL AND d.customer_name = c.name))
                       AND d.document_type = 'return'
                       AND d.cancelled_at IS NULL
                       AND (ob.as_of_date IS NULL OR d.doc_date >= ob.as_of_date)
                   ) rt ON TRUE
                   LEFT JOIN LATERAL (
                     SELECT COALESCE(SUM({TotalSub}), 0) AS receipt_total,
                            MAX(d.doc_date) AS last_date
                     FROM documents d
                     WHERE (d.customer_id = c.id OR (d.customer_id IS NULL AND d.customer_name = c.name))
                       AND d.document_type = 'receipt'
                       AND d.cancelled_at IS NULL
                       AND (ob.as_of_date IS NULL OR d.doc_date >= ob.as_of_date)
                   ) rc ON TRUE
                   LEFT JOIN LATERAL (
                     SELECT COALESCE(SUM(p.amount), 0) AS payment_total,
                            MAX(p.pay_date) AS last_date
                     FROM payments p
                     WHERE (p.customer_id = c.id
                        OR (p.customer_id IS NULL AND (p.customer_name = c.name OR p.customer_input_name = c.name)))
                       AND (ob.as_of_date IS NULL OR p.pay_date >= ob.as_of_date)
                   ) p ON TRUE
                   WHERE c.is_active = TRUE
                   ORDER BY (COALESCE(ob.amount, 0) + COALESCE(s.sales_total, 0) - COALESCE(rt.return_total, 0)
                             - COALESCE(rc.receipt_total, 0) - COALESCE(p.payment_total, 0)) DESC,
                            c.name").ExecuteReaderAsync();

            while (await r.ReadAsync())
            {
                var customer = new CustomerDto(r.Guid("id"), r.Str("name"), r.Str("tax_code"),
                    r.Str("phone"), r.Str("address"), r.Bool("is_active"));
                var openingBalance = r.Dec("opening_balance");
                DateOnly? openingDate = r.IsDBNull(r.GetOrdinal("opening_date"))
                    ? null
                    : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("opening_date")));
                var sales = r.Dec("sales_total");
                var returns = r.Dec("return_total");
                var collected = r.Dec("collected_total");
                customers.Add(new DebtSummaryDto(customer, openingBalance, openingDate, r.Str("opening_note"),
                    sales, returns, collected, openingBalance + sales - returns - collected,
                    r.GetInt32(r.GetOrdinal("invoice_count")),
                    r.IsDBNull(r.GetOrdinal("last_activity_date"))
                        ? null
                        : DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("last_activity_date")))));
            }

            return Results.Ok(new DebtOverviewDto(
                customers.Sum(x => x.OpeningBalance),
                customers.Sum(x => x.SalesTotal),
                customers.Sum(x => x.ReturnsTotal),
                customers.Sum(x => x.CollectedTotal),
                customers.Sum(x => Math.Max(x.Balance, 0)),
                customers.Count(x => x.Balance > 0),
                customers));
        });

        api.MapGet("/debts/{customerId:guid}", async (Guid customerId, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var customer = await ReadCustomer(conn, customerId);
            if (customer is null) return Results.NotFound();

            decimal openingBalance = 0;
            DateOnly? openingDate = null;
            var openingNote = "";
            await using (var r = await conn.Cmd(
                    @"SELECT amount, as_of_date, note
                      FROM customer_opening_balances
                      WHERE customer_id = @id")
                .With("@id", customerId)
                .ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    openingBalance = r.Dec("amount");
                    openingDate = r.DateOnly("as_of_date");
                    openingNote = r.Str("note");
                }
            }

            var raw = new List<(Guid Id, DateOnly Date, string Reference, string Kind, string Description,
                decimal Debit, decimal Credit, bool Cancelled)>();
            if (openingDate is not null)
            {
                raw.Add((customerId, openingDate.Value, "ĐẦU KỲ", "opening",
                    string.IsNullOrWhiteSpace(openingNote) ? "Số dư công nợ đầu kỳ" : openingNote,
                    Math.Max(openingBalance, 0), Math.Max(-openingBalance, 0), false));
            }
            var cutoff = openingDate ?? DateOnly.MinValue;

            await using (var r = await conn.Cmd(
                $@"SELECT d.id, d.doc_date, d.voucher_no, d.document_type, d.content,
                          {TotalSub} AS total, d.cancelled_at
                   FROM documents d
                   WHERE (d.customer_id = @id OR (d.customer_id IS NULL AND d.customer_name = @name))
                     AND d.document_type IN ('document', 'receipt', 'return')
                     AND d.doc_date >= @cutoff
                   ORDER BY d.doc_date, d.created_at, d.id")
                .With("@id", customerId)
                .With("@name", customer.Name)
                .With("@cutoff", cutoff)
                .ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var type = r.Str("document_type");
                    var cancelled = !r.IsDBNull(r.GetOrdinal("cancelled_at"));
                    var total = cancelled ? 0 : r.Dec("total");
                    // Phiếu xuất kho ghi NỢ; phiếu thu và phiếu trả hàng đều ghi CÓ, nhưng tách hai
                    // loại để sổ nói rõ "khách trả tiền" khác "khách trả hàng".
                    var kind = type == "receipt" ? "receipt" : type == "return" ? "return" : "sale";
                    raw.Add((r.Guid("id"), r.DateOnly("doc_date"), r.Str("voucher_no"),
                        kind, r.Str("content"),
                        kind == "sale" ? total : 0, kind == "sale" ? 0 : total, cancelled));
                }
            }

            await using (var r = await conn.Cmd(
                @"SELECT p.id, p.pay_date, p.note, p.amount
                  FROM payments p
                  WHERE (p.customer_id = @id
                     OR (p.customer_id IS NULL AND (p.customer_name = @name OR p.customer_input_name = @name)))
                    AND p.pay_date >= @cutoff
                  ORDER BY p.pay_date, p.created_at, p.id")
                .With("@id", customerId)
                .With("@name", customer.Name)
                .With("@cutoff", cutoff)
                .ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    raw.Add((r.Guid("id"), r.DateOnly("pay_date"), "", "payment", r.Str("note"),
                        0, r.Dec("amount"), false));
                }
            }

            var balance = 0m;
            var transactions = raw
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Kind == "opening" ? 0 : x.Kind == "sale" ? 1 : x.Kind == "return" ? 2 : 3)
                .ThenBy(x => x.Id)
                .Select(x =>
                {
                    balance += x.Debit - x.Credit;
                    return new DebtTransactionDto(x.Id, x.Date, x.Reference, x.Kind, x.Description,
                        x.Debit, x.Credit, balance, x.Cancelled);
                })
                .Reverse()
                .ToList();

            var active = raw.Where(x => !x.Cancelled).ToList();
            var sales = active.Where(x => x.Kind == "sale").Sum(x => x.Debit);
            var returns = active.Where(x => x.Kind == "return").Sum(x => x.Credit);
            var collected = active.Where(x => x.Kind is "receipt" or "payment").Sum(x => x.Credit);
            var lastDate = active.Count == 0 ? (DateOnly?)null : active.Max(x => x.Date);
            var summary = new DebtSummaryDto(customer, openingBalance, openingDate, openingNote,
                sales, returns, collected, openingBalance + sales - returns - collected,
                active.Count(x => x.Kind == "sale"), lastDate);
            return Results.Ok(new DebtDetailDto(customer, summary, transactions));
        });

        api.MapPut("/debts/{customerId:guid}/opening-balance", async (
            Guid customerId,
            SaveOpeningBalanceRequest req,
            ClaimsPrincipal u,
            Database db) =>
        {
            if (Math.Abs(req.Amount) > 999_999_999_999_999.99m)
                return Results.BadRequest(new { message = "Số dư đầu kỳ vượt quá giới hạn cho phép." });
            if (req.AsOfDate > DateOnly.FromDateTime(DateTime.Today))
                return Results.BadRequest(new { message = "Ngày đầu kỳ không được lớn hơn ngày hiện tại." });

            var note = (req.Note ?? "").Trim();
            if (note.Length > 1000)
                return Results.BadRequest(new { message = "Ghi chú đầu kỳ không được vượt quá 1.000 ký tự." });

            await using var conn = await db.OpenAsync();
            var customer = await ReadCustomer(conn, customerId);
            if (customer is null || !customer.IsActive) return Results.NotFound();

            await conn.Cmd(
                    @"INSERT INTO customer_opening_balances
                        (customer_id, amount, as_of_date, note, updated_by)
                      VALUES (@customerId, @amount, @date, @note, @username)
                      ON CONFLICT (customer_id) DO UPDATE SET
                        amount = EXCLUDED.amount,
                        as_of_date = EXCLUDED.as_of_date,
                        note = EXCLUDED.note,
                        updated_by = EXCLUDED.updated_by,
                        updated_at = CURRENT_TIMESTAMP")
                .With("@customerId", customerId)
                .With("@amount", req.Amount)
                .With("@date", req.AsOfDate)
                .With("@note", note)
                .With("@username", u.Username())
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Cập nhật nợ đầu kỳ", "CustomerOpeningBalance",
                customer.Name,
                $"Số dư {req.Amount:N0} đồng tại ngày {req.AsOfDate:dd/MM/yyyy}. {note}");
            return Results.NoContent();
        }).RequirePermission(Permissions.VouchersUpdate);

        api.MapPost("/debts/{customerId:guid}/payments", async (
            Guid customerId,
            SaveDebtPaymentRequest req,
            ClaimsPrincipal u,
            Database db) =>
        {
            if (req.Amount <= 0)
                return Results.BadRequest(new { message = "Số tiền thu phải lớn hơn 0." });
            if (req.Amount > 999_999_999_999_999.99m)
                return Results.BadRequest(new { message = "Số tiền thu vượt quá giới hạn cho phép." });

            var note = (req.Note ?? "").Trim();
            if (note.Length > 1000)
                return Results.BadRequest(new { message = "Nội dung thu nợ không được vượt quá 1.000 ký tự." });

            await using var conn = await db.OpenAsync();
            var customer = await ReadCustomer(conn, customerId);
            if (customer is null || !customer.IsActive) return Results.NotFound();

            var paymentId = Guid.NewGuid();
            await conn.Cmd(
                    @"INSERT INTO payments
                        (id, customer_id, customer_name, customer_input_name, amount, pay_date, note)
                      VALUES (@id, @customerId, @name, @name, @amount, @date, @note)")
                .With("@id", paymentId)
                .With("@customerId", customerId)
                .With("@name", customer.Name)
                .With("@amount", req.Amount)
                .With("@date", req.Date)
                .With("@note", note)
                .ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Ghi nhận thu công nợ", "DebtPayment", customer.Name,
                $"Thu {req.Amount:N0} đồng ngày {req.Date:dd/MM/yyyy}. {note}");
            return Results.Ok(new { id = paymentId });
        }).RequirePermission(Permissions.VouchersCreate);

        // ---------- Reports (Báo cáo) ----------
        api.MapGet("/reports", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var now = DateTime.Now;
            var totalPayments = (decimal)(await conn.Cmd("SELECT COALESCE(SUM(amount),0) FROM payments").ExecuteScalarAsync() ?? 0m);
            var totalDocuments = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM documents").ExecuteScalarAsync() ?? 0);
            var activeCustomers = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM customers WHERE is_active = TRUE").ExecuteScalarAsync() ?? 0);
            // Doanh thu = bán RÒNG (đã trừ hàng khách trả về). Phải lọc document_type rõ ràng:
            // phiếu trả hàng cũng có dòng hàng, không lọc là nó cộng vào doanh thu thay vì trừ đi.
            var monthRevenue = (decimal)(await conn.Cmd(
                @"SELECT COALESCE(SUM(CASE WHEN d.document_type = 'return' THEN -1 ELSE 1 END
                                          * l.quantity * l.unit_price),0)
                  FROM documents d
                  JOIN document_lines l ON l.document_id = d.id
                  WHERE d.cancelled_at IS NULL
                    AND d.document_type IN ('document', 'return')
                    AND EXTRACT(YEAR FROM d.doc_date)=@y AND EXTRACT(MONTH FROM d.doc_date)=@m")
                .With("@y", now.Year).With("@m", now.Month).ExecuteScalarAsync() ?? 0m);

            var monthly = new List<MonthlyRowDto>();
            await using var r = await conn.Cmd(
                @"SELECT EXTRACT(YEAR FROM d.doc_date)::int AS y, EXTRACT(MONTH FROM d.doc_date)::int AS m,
                         COUNT(DISTINCT d.id) FILTER (WHERE d.document_type = 'document')::int AS docs,
                         COALESCE(SUM(CASE WHEN d.document_type = 'return' THEN -1 ELSE 1 END
                                          * l.quantity * l.unit_price),0) AS total
                  FROM documents d LEFT JOIN document_lines l ON l.document_id = d.id
                  WHERE d.cancelled_at IS NULL
                    AND d.document_type IN ('document', 'return')
                  GROUP BY EXTRACT(YEAR FROM d.doc_date), EXTRACT(MONTH FROM d.doc_date)
                  ORDER BY y DESC, m DESC LIMIT 12").ExecuteReaderAsync();
            while (await r.ReadAsync())
                monthly.Add(new MonthlyRowDto(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), 0, r.GetDecimal(3)));

            return Results.Ok(new ReportsDto(totalPayments, monthRevenue, totalDocuments, activeCustomers, monthly));
        });

        // Nhật ký hệ thống (/api/audit) đã tách sang AuditEndpoints.MapAudit() — bản đầy đủ có phân trang,
        // lọc theo người dùng/hành động/đối tượng/thời gian, che dữ liệu nhạy cảm và xuất CSV/Excel.
    }

    private static async Task<IResult> ListDocuments(Database db, bool cashOnly)
    {
        await using var conn = await db.OpenAsync();
        var list = new List<DocumentListItemDto>();
        await using var r = await conn.Cmd(
            $@"SELECT d.id, d.voucher_no, d.doc_date,
                       CASE
                         WHEN d.document_type = 'receipt' THEN 'Phiếu thu'
                         WHEN d.document_type = 'payment' THEN 'Phiếu chi'
                         ELSE 'Phiếu xuất kho bán hàng'
                       END AS document_type,
                      d.customer_name, d.content, {TotalSub} AS total,
                      COALESCE(NULLIF(au.full_name, ''), creator.username, '') AS created_by,
                      d.issued_at, d.cancelled_at, d.cancelled_by, d.cancel_reason,
                      d.delivery_mode, d.delivery_driver_name,
                      d.delivery_returned_at, COALESCE(dt.status, '') AS delivery_task_status
               FROM documents d
               LEFT JOIN work_tasks dt ON dt.id = d.delivery_task_id
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
                WHERE {(cashOnly ? "d.document_type IN ('receipt', 'payment')" : "d.document_type = 'document'")}
                ORDER BY d.doc_date DESC, d.voucher_no DESC").ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new DocumentListItemDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                r.Str("document_type"), r.Str("customer_name"), r.Str("content"), r.Dec("total"),
                r.Str("created_by"), r.DtNull("issued_at"), r.DtNull("cancelled_at"),
                r.Str("cancelled_by"), r.Str("cancel_reason"),
                r.Str("delivery_mode"), r.Str("delivery_driver_name"),
                r.Str("delivery_task_status"), r.DtNull("delivery_returned_at")));
        return Results.Ok(list);
    }

    private static async Task<DateTime?> MarkDocumentIssued(Database db, Guid id, bool cashOnly)
    {
        await using var conn = await db.OpenAsync();
        var value = await conn.Cmd(
                $@"UPDATE documents
                  SET issued_at = COALESCE(issued_at, CURRENT_TIMESTAMP), updated_at = CURRENT_TIMESTAMP
                  WHERE id = @id
                    AND {(cashOnly ? "document_type IN ('receipt', 'payment')" : "document_type = 'document'")}
                    AND cancelled_at IS NULL
                  RETURNING issued_at")
            .With("@id", id)
            .ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToDateTime(value);
    }

    private static async Task<bool> WarehouseVoucherNoExists(Database db, Guid id, string voucherNo)
    {
        await using var conn = await db.OpenAsync();
        var value = await conn.Cmd(
                @"SELECT EXISTS (
                    SELECT 1
                    FROM documents
                    WHERE id <> @id
                      AND document_type = 'document'
                      AND issued_at IS NOT NULL
                      AND LOWER(voucher_no) = LOWER(@voucherNo)
                )")
            .With("@id", id)
            .With("@voucherNo", voucherNo)
            .ExecuteScalarAsync();
        return value is true;
    }

    private static async Task<WarehouseIssueState?> ReadWarehouseIssueState(Database db, Guid id)
    {
        await using var conn = await db.OpenAsync();
        await using var reader = await conn.Cmd(
                @"SELECT voucher_no, issued_at, cancelled_at
                  FROM documents
                  WHERE id = @id AND document_type = 'document'")
            .With("@id", id)
            .ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new WarehouseIssueState(reader.Str("voucher_no"), reader.DtNull("issued_at"),
            reader.DtNull("cancelled_at"));
    }

    private static async Task<DateTime?> FinalizeWarehouseIssue(Database db, Guid id, string voucherNo)
    {
        await using var conn = await db.OpenAsync();
        var value = await conn.Cmd(
                @"UPDATE documents
                  SET voucher_no = @voucherNo,
                      issued_at = COALESCE(issued_at, CURRENT_TIMESTAMP),
                      updated_at = CURRENT_TIMESTAMP
                  WHERE id = @id
                    AND document_type = 'document'
                    AND cancelled_at IS NULL
                    AND (issued_at IS NULL OR voucher_no = @voucherNo)
                  RETURNING issued_at")
            .With("@id", id)
            .With("@voucherNo", voucherNo)
            .ExecuteScalarAsync();
        if (value is null or DBNull) return null;

        // Chốt "hàng xuất đi": bản in đã rời máy in nên các dòng lúc này là con số trên tờ giấy
        // khách sẽ ký. Đây là mốc để đối chiếu với hàng thực nhận khi lái xe nộp phiếu về.
        // In lại phiếu cũ không được ghi đè mốc (ON CONFLICT DO NOTHING).
        await conn.Cmd(
                @"INSERT INTO document_issued_lines
                      (document_id, line_no, line_content, spec, quantity, unit_price, note)
                  SELECT l.document_id, l.line_no, l.line_content, l.spec, l.quantity, l.unit_price, l.note
                  FROM document_lines l
                  WHERE l.document_id = @id
                  ON CONFLICT (document_id, line_no) DO NOTHING")
            .With("@id", id)
            .ExecuteNonQueryAsync();
        return Convert.ToDateTime(value);
    }

    private static async Task<DocumentDetailDto?> ReadDocument(Database db, Guid id, bool cashOnly)
    {
        await using var conn = await db.OpenAsync();
        DocumentDetailDto? document = null;
        await using (var r = await conn.Cmd(
            $@"SELECT id, voucher_no, doc_date, customer_name, content, note,
                      issued_at, cancelled_at, cancelled_by, cancel_reason
               FROM documents
               WHERE id = @id
                 AND {(cashOnly ? "document_type IN ('receipt', 'payment')" : "document_type = 'document'")}")
            .With("@id", id).ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
                document = new DocumentDetailDto(r.Guid("id"), r.Str("voucher_no"), r.DateOnly("doc_date"),
                    r.Str("customer_name"), r.Str("content"), r.Str("note"), new(),
                    r.DtNull("issued_at"), r.DtNull("cancelled_at"), r.Str("cancelled_by"),
                    r.Str("cancel_reason"));
        }
        if (document is null) return null;

        await using (var r = await conn.Cmd(
            @"SELECT line_content, spec, quantity, unit_price, note, product_id FROM document_lines
              WHERE document_id = @id ORDER BY line_no").With("@id", id).ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var productOrdinal = r.GetOrdinal("product_id");
                document.Lines.Add(new DocumentLineDto(r.Str("line_content"), r.Str("spec"),
                    r.Dec("quantity"), r.Dec("unit_price"), r.Str("note"),
                    r.IsDBNull(productOrdinal) ? null : r.GetGuid(productOrdinal)));
            }
        }
        return document;
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
                         WHEN d.document_type = 'receipt' THEN 'Phiếu thu'
                         WHEN d.document_type = 'payment' THEN 'Phiếu chi'
                         ELSE 'Phiếu xuất kho bán hàng'
                       END AS document_type,
                      d.customer_name, d.content, {TotalSub} AS total,
                      COALESCE(NULLIF(au.full_name, ''), creator.username, '') AS created_by,
                      d.issued_at, d.cancelled_at, d.cancelled_by, d.cancel_reason
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
                r.Str("document_type"), r.Str("customer_name"), r.Str("content"), r.Dec("total"),
                r.Str("created_by"), r.DtNull("issued_at"), r.DtNull("cancelled_at"),
                r.Str("cancelled_by"), r.Str("cancel_reason")));
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
        catch (NpgsqlException)
        {
            await tx.RollbackAsync();
            return Results.Json(new { message = "Không lưu được khách hàng (dữ liệu không hợp lệ hoặc trùng lặp)." }, statusCode: 400);
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

    private static async Task<IResult> SaveDocument(
        Database db,
        ClaimsPrincipal u,
        Guid? id,
        SaveDocumentRequest req,
        bool cashOnly)
    {
        var documentType = NormalizeDocumentType(req.DocumentType);
        if (documentType is null
            || (cashOnly && documentType is not ("receipt" or "payment"))
            || (!cashOnly && documentType != "document"))
        {
            return Results.BadRequest(new
            {
                message = cashOnly
                    ? "API Thu chi chỉ chấp nhận phiếu thu hoặc phiếu chi."
                    : "Trang Kế toán chỉ chấp nhận phiếu xuất kho.",
            });
        }

        var isWarehouseDocument = documentType == "document";
        var requestedVoucherNo = (req.VoucherNo ?? "").Trim();
        var persistedVoucherNo = isWarehouseDocument ? "" : requestedVoucherNo;
        if (string.IsNullOrWhiteSpace(req.VoucherNo) && !isWarehouseDocument)
            return Results.BadRequest(new { message = "Vui lòng nhập số phiếu." });
        if ((req.VoucherNo?.Trim().Length ?? 0) > 64)
            return Results.BadRequest(new { message = "Số phiếu không được vượt quá 64 ký tự." });

        await using var conn = await db.OpenAsync();
        await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            if (id is not null)
            {
                await using var currentReader = await new NpgsqlCommand(
                    $@"SELECT voucher_no, issued_at, cancelled_at
                      FROM documents
                      WHERE id = @id
                        AND {(cashOnly ? "document_type IN ('receipt', 'payment')" : "document_type = 'document'")}
                      FOR UPDATE", conn, tx)
                {
                    Parameters = { new("@id", id.Value) },
                }.ExecuteReaderAsync();

                if (!await currentReader.ReadAsync())
                {
                    await currentReader.DisposeAsync();
                    await tx.RollbackAsync();
                    return Results.NotFound();
                }

                var currentVoucherNo = currentReader.Str("voucher_no");
                var currentIssuedAt = currentReader.DtNull("issued_at");
                var currentCancelledAt = currentReader.DtNull("cancelled_at");
                await currentReader.DisposeAsync();
                if (currentCancelledAt is not null)
                {
                    await tx.RollbackAsync();
                    return Results.Conflict(new
                    {
                        message = "Phiếu đã hủy và được khóa để bảo toàn lịch sử; không thể chỉnh sửa.",
                    });
                }

                if (isWarehouseDocument && currentIssuedAt is not null)
                {
                    if (!string.IsNullOrEmpty(requestedVoucherNo)
                        && !string.Equals(requestedVoucherNo, currentVoucherNo, StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync();
                        return Results.Conflict(new
                        {
                            message = $"Phiếu đã phát hành với số {currentVoucherNo}; không thể thay đổi số phiếu.",
                        });
                    }

                    persistedVoucherNo = currentVoucherNo;
                }
            }

            var customerId = await ResolveCustomer(conn, tx, req.CustomerName);
            var docId = id ?? Guid.NewGuid();
            var docDate = req.Date;

            if (id is null)
            {
                var cmd = new NpgsqlCommand(
                    @"INSERT INTO documents (id, voucher_no, doc_date, customer_id, customer_name, customer_input_name, document_type, content, note)
                      VALUES (@id, @v, @dt, @cid, @cn, @cin, @type, @c, @n)", conn, tx);
                Fill(cmd, docId, req, customerId, docDate, documentType, persistedVoucherNo);
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var issuanceAssignments = isWarehouseDocument
                    ? "voucher_no = CASE WHEN issued_at IS NULL THEN @v ELSE voucher_no END,"
                    : "voucher_no = @v, issued_at = NULL,";
                var cmd = new NpgsqlCommand(
                    $@"UPDATE documents SET {issuanceAssignments} doc_date=@dt, customer_id=@cid,
                        customer_name=@cn, customer_input_name=@cin, document_type=@type, content=@c, note=@n,
                        updated_at=CURRENT_TIMESTAMP
                      WHERE id=@id
                        AND {(cashOnly ? "document_type IN ('receipt', 'payment')" : "document_type = 'document'")}
                      RETURNING voucher_no", conn, tx);
                Fill(cmd, docId, req, customerId, docDate, documentType, persistedVoucherNo);
                var savedVoucherNo = await cmd.ExecuteScalarAsync();
                if (savedVoucherNo is null or DBNull) { await tx.RollbackAsync(); return Results.NotFound(); }
                persistedVoucherNo = Convert.ToString(savedVoucherNo) ?? "";

                await new NpgsqlCommand("DELETE FROM document_lines WHERE document_id=@id", conn, tx)
                    { Parameters = { new("@id", docId) } }.ExecuteNonQueryAsync();
            }

            var lineNo = 1;
            foreach (var line in req.Lines ?? new())
            {
                // product_id: nếu người lập phiếu không chọn từ danh mục thì thử khớp đúng
                // tên + quy cách. Khớp được thì thống kê theo mặt hàng có số liệu ngay, không khớp
                // vẫn lưu bình thường — danh mục là gợi ý, không phải rào chắn.
                var lc = new NpgsqlCommand(
                    @"INSERT INTO document_lines (document_id, line_no, line_content, category, spec, quantity, unit_price, note, product_id)
                      VALUES (@d, @ln, @lc, '', @sp, @q, @up, @nt,
                              COALESCE(@pid, (SELECT p.id FROM products p
                                              WHERE lower(p.name) = lower(BTRIM(@lc))
                                                AND lower(p.spec) = lower(BTRIM(@sp))
                                              LIMIT 1)))", conn, tx);
                lc.Parameters.AddWithValue("@d", docId);
                lc.Parameters.AddWithValue("@ln", lineNo++);
                lc.Parameters.AddWithValue("@lc", line.LineContent ?? "");
                lc.Parameters.AddWithValue("@sp", line.Spec ?? "");
                lc.Parameters.AddWithValue("@q", line.Quantity);
                lc.Parameters.AddWithValue("@up", line.UnitPrice);
                lc.Parameters.AddWithValue("@nt", line.Note ?? "");
                lc.Parameters.AddWithValue("@pid", (object?)line.ProductId ?? DBNull.Value);
                await lc.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            var auditEntityName = string.IsNullOrWhiteSpace(persistedVoucherNo) ? docId.ToString() : persistedVoucherNo;
            var areaName = cashOnly ? "phiếu thu chi" : "phiếu xuất kho";
            await db.RecordAudit(u.Username(), id is null ? $"Tạo {areaName}" : $"Cập nhật {areaName}",
                "Document", auditEntityName, $"{(id is null ? "Tạo" : "Cập nhật")} {areaName} (web).");
            return Results.Ok(new { id = docId });
        }
        catch (NpgsqlException)
        {
            await tx.RollbackAsync();
            return Results.Json(new
            {
                message = cashOnly
                    ? "Không lưu được phiếu thu chi (dữ liệu không hợp lệ hoặc trùng lặp)."
                    : "Không lưu được phiếu xuất kho (dữ liệu không hợp lệ hoặc trùng lặp).",
            }, statusCode: 400);
        }
    }

    private static void Fill(
        NpgsqlCommand cmd,
        Guid docId,
        SaveDocumentRequest req,
        Guid customerId,
        DateOnly docDate,
        string documentType,
        string voucherNo)
    {
        cmd.Parameters.AddWithValue("@id", docId);
        cmd.Parameters.AddWithValue("@v", voucherNo);
        cmd.Parameters.AddWithValue("@dt", docDate);
        cmd.Parameters.AddWithValue("@cid", customerId);
        cmd.Parameters.AddWithValue("@cn", req.CustomerName ?? "");
        cmd.Parameters.AddWithValue("@cin", req.CustomerName ?? "");
        cmd.Parameters.AddWithValue("@type", documentType);
        cmd.Parameters.AddWithValue("@c", req.Content ?? "");
        cmd.Parameters.AddWithValue("@n", req.Note ?? "");
    }

    private sealed record WarehouseIssueState(string VoucherNo, DateTime? IssuedAt, DateTime? CancelledAt);

    private static string? NormalizeDocumentType(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized is "receipt" or "payment" or "document" ? normalized : null;
    }

    private static async Task<IResult> CancelDocument(
        Database db,
        ClaimsPrincipal u,
        Guid id,
        string? reason,
        bool cashOnly)
    {
        reason = (reason ?? "").Trim();
        if (reason.Length > 500)
            return Results.BadRequest(new { message = "Lý do hủy không được vượt quá 500 ký tự." });

        await using var conn = await db.OpenAsync();
        await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
        await using var reader = await new NpgsqlCommand(
            $@"SELECT voucher_no, cancelled_at
               FROM documents
               WHERE id = @id
                 AND {(cashOnly ? "document_type IN ('receipt', 'payment')" : "document_type = 'document'")}
               FOR UPDATE", conn, tx)
        {
            Parameters = { new("@id", id) },
        }.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            await reader.DisposeAsync();
            await tx.RollbackAsync();
            return Results.NotFound();
        }

        var voucherNo = reader.Str("voucher_no");
        var cancelledAt = reader.DtNull("cancelled_at");
        await reader.DisposeAsync();
        if (cancelledAt is not null)
        {
            await tx.RollbackAsync();
            return Results.Conflict(new { message = "Phiếu đã ở trạng thái hủy." });
        }

        cancelledAt = Convert.ToDateTime(await new NpgsqlCommand(
            @"UPDATE documents
              SET cancelled_at = CURRENT_TIMESTAMP,
                  cancelled_by = @by,
                  cancel_reason = @reason,
                  updated_at = CURRENT_TIMESTAMP
              WHERE id = @id AND cancelled_at IS NULL
              RETURNING cancelled_at", conn, tx)
        {
            Parameters =
            {
                new("@id", id),
                new("@by", u.Username()),
                new("@reason", reason),
            },
        }.ExecuteScalarAsync());

        await tx.CommitAsync();
        var name = cashOnly ? "phiếu thu chi" : "phiếu xuất kho";
        var entityName = string.IsNullOrWhiteSpace(voucherNo) ? id.ToString() : voucherNo;
        await db.RecordAudit(u.Username(), $"Hủy {name}", "Document", entityName,
            string.IsNullOrWhiteSpace(reason) ? $"Chuyển {name} sang trạng thái hủy (web)." : $"Lý do: {reason}");
        return Results.Ok(new { cancelledAt, cancelledBy = u.Username(), cancelReason = reason });
    }

    private static async Task<IResult> DeleteCashVoucher(
        Database db,
        ClaimsPrincipal u,
        Guid id)
    {
        await using var conn = await db.OpenAsync();
        await using var tx = (NpgsqlTransaction)await conn.BeginTransactionAsync();
        await using var reader = await new NpgsqlCommand(
            @"SELECT voucher_no, document_type, issued_at, cancelled_at
              FROM documents
              WHERE id = @id
                AND document_type IN ('receipt', 'payment')
              FOR UPDATE", conn, tx)
        {
            Parameters = { new("@id", id) },
        }.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            await reader.DisposeAsync();
            await tx.RollbackAsync();
            return Results.NotFound();
        }

        var voucherNo = reader.Str("voucher_no");
        var documentType = reader.Str("document_type");
        var issuedAt = reader.DtNull("issued_at");
        var cancelledAt = reader.DtNull("cancelled_at");
        await reader.DisposeAsync();

        if (issuedAt is not null && cancelledAt is null)
        {
            await tx.RollbackAsync();
            return Results.Conflict(new
            {
                message = "Phiếu đã phát hành phải được hủy trước khi xóa vĩnh viễn.",
            });
        }

        var deleted = await new NpgsqlCommand(
            @"DELETE FROM documents
              WHERE id = @id
                AND document_type IN ('receipt', 'payment')", conn, tx)
        {
            Parameters = { new("@id", id) },
        }.ExecuteNonQueryAsync();

        if (deleted == 0)
        {
            await tx.RollbackAsync();
            return Results.NotFound();
        }

        await tx.CommitAsync();
        var label = documentType == "receipt" ? "phiếu thu" : "phiếu chi";
        await db.RecordAudit(u.Username(), $"Xóa vĩnh viễn {label}", "Document",
            string.IsNullOrWhiteSpace(voucherNo) ? id.ToString() : voucherNo,
            $"Đã xóa vĩnh viễn {label} {(cancelledAt is null ? "nháp" : "đã hủy")} khỏi sổ Thu chi.");
        return Results.NoContent();
    }

    private static async Task<bool> DocumentIsCancelled(Database db, Guid id, bool cashOnly)
    {
        await using var conn = await db.OpenAsync();
        var value = await conn.Cmd(
                $@"SELECT cancelled_at IS NOT NULL
                   FROM documents
                   WHERE id = @id
                     AND {(cashOnly ? "document_type IN ('receipt', 'payment')" : "document_type = 'document'")}")
            .With("@id", id)
            .ExecuteScalarAsync();
        return value is true;
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
