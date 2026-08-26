using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// DANH MỤC HÀNG HOÁ — tên chuẩn cho chủng loại + quy cách.
///
/// Vì sao cần: mọi dòng phiếu trước nay đều là chữ tự do, nên cùng một mặt hàng gõ ba kiểu là ba
/// mặt hàng khác nhau với máy. Hệ quả thấy ngay: không thống kê nổi bán được bao nhiêu tấn thép tấm
/// 10mm, và màn "hàng khách trả về" phải dò chữ để tìm đơn nguồn — gõ lệch một dấu là không ra.
///
/// Nguyên tắc: **gợi ý, không ép**. Ô nhập trên phiếu vẫn gõ tay được như cũ (phiếu cũ, hàng lạ,
/// hàng gia công một lần vẫn phải lập được). Chọn từ danh mục chỉ là đường nhanh, và khi chọn thì
/// dòng phiếu mang theo product_id để thống kê bám vào MÃ chứ không bám vào chính tả.
///
/// Đây cũng là nền móng bắt buộc nếu sau này làm nhập–xuất–tồn: không có tên chuẩn thì không thể
/// cộng dồn tồn kho của một mặt hàng.
/// </summary>
public static class ProductCatalogEndpoints
{
    public static void MapProductCatalog(this IEndpointRouteBuilder app)
    {
        // Xem: ai làm kế toán cũng cần tra. Sửa danh mục: ai lập được phiếu — chính họ là người gõ
        // tên hàng nên phải tự thêm được, không thì lại quay về gõ tự do.
        var api = app.MapGroup("/api").RequirePermission(Permissions.AccountingAccess);

        // Danh mục + số liệu bán hàng đi kèm. "Giá bán gần nhất" là thứ người lập phiếu hay phải
        // lục lại phiếu cũ để tra, nên trả luôn ở đây.
        api.MapGet("/products", async (string? q, bool? includeInactive, Database db) =>
        {
            var keyword = (q ?? "").Trim();
            var like = $"%{keyword}%";
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT p.id, p.code, p.name, p.spec, p.unit, p.note, p.is_active,
                       COALESCE(s.times_used, 0)::int AS times_used,
                       COALESCE(s.sold_quantity, 0) AS sold_quantity,
                       COALESCE(s.sold_amount, 0) AS sold_amount,
                       s.last_price, s.last_sold_date,
                       COALESCE(b.bought_quantity, 0) AS bought_quantity,
                       COALESCE(b.bought_amount, 0) AS bought_amount,
                       b.last_cost, b.last_bought_date
                FROM products p
                LEFT JOIN LATERAL (
                    SELECT COUNT(*) AS times_used,
                           SUM(l.quantity) AS sold_quantity,
                           SUM(l.quantity * l.unit_price) AS sold_amount,
                           (SELECT l2.unit_price
                            FROM document_lines l2
                            JOIN documents d2 ON d2.id = l2.document_id
                            WHERE l2.product_id = p.id AND d2.document_type = 'document'
                              AND d2.cancelled_at IS NULL
                            ORDER BY d2.doc_date DESC, d2.created_at DESC LIMIT 1) AS last_price,
                           MAX(d.doc_date) AS last_sold_date
                    FROM document_lines l
                    JOIN documents d ON d.id = l.document_id
                    WHERE l.product_id = p.id AND d.document_type = 'document'
                      AND d.cancelled_at IS NULL
                ) s ON TRUE
                LEFT JOIN LATERAL (
                    SELECT SUM(l.quantity) AS bought_quantity,
                           SUM(l.quantity * l.unit_price) AS bought_amount,
                           (SELECT l2.unit_price
                            FROM purchase_lines l2
                            JOIN purchases p2 ON p2.id = l2.purchase_id
                            WHERE l2.product_id = p.id AND p2.cancelled_at IS NULL
                            ORDER BY p2.doc_date DESC, p2.created_at DESC LIMIT 1) AS last_cost,
                           MAX(pu.doc_date) AS last_bought_date
                    FROM purchase_lines l
                    JOIN purchases pu ON pu.id = l.purchase_id
                    WHERE l.product_id = p.id AND pu.cancelled_at IS NULL
                ) b ON TRUE
                WHERE (@all OR p.is_active = TRUE)
                  AND (@kw = '' OR p.name ILIKE @like OR p.spec ILIKE @like OR p.code ILIKE @like)
                ORDER BY p.is_active DESC, p.name, p.spec
                LIMIT 1000
                """)
                .With("@all", includeInactive == true).With("@kw", keyword).With("@like", like)
                .ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    id = r.Guid("id"),
                    code = r.Str("code"),
                    name = r.Str("name"),
                    spec = r.Str("spec"),
                    unit = r.Str("unit"),
                    note = r.Str("note"),
                    isActive = r.Bool("is_active"),
                    timesUsed = r.Int("times_used"),
                    soldQuantity = r.Dec("sold_quantity"),
                    soldAmount = r.Dec("sold_amount"),
                    lastPrice = r.IsDBNull(r.GetOrdinal("last_price")) ? (decimal?)null : r.Dec("last_price"),
                    lastSoldDate = r.IsDBNull(r.GetOrdinal("last_sold_date")) ? (DateOnly?)null : r.DateOnly("last_sold_date"),
                    boughtQuantity = r.Dec("bought_quantity"),
                    boughtAmount = r.Dec("bought_amount"),
                    lastCost = r.IsDBNull(r.GetOrdinal("last_cost")) ? (decimal?)null : r.Dec("last_cost"),
                    lastBoughtDate = r.IsDBNull(r.GetOrdinal("last_bought_date")) ? (DateOnly?)null : r.DateOnly("last_bought_date"),
                });
            return Results.Ok(new { items });
        });

        // Gợi ý dựng danh mục TỪ CHÍNH DỮ LIỆU CŨ: gom các cặp (chủng loại, quy cách) đã gõ trên
        // phiếu mà chưa có trong danh mục, xếp theo mức hay dùng. Không có bước này thì kế toán phải
        // gõ lại hàng trăm mặt hàng bằng tay và danh mục sẽ chết yểu.
        api.MapGet("/products/suggestions", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var items = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT BTRIM(l.line_content) AS name, BTRIM(l.spec) AS spec,
                       COUNT(*)::int AS times_used, MAX(d.doc_date) AS last_used
                FROM document_lines l
                JOIN documents d ON d.id = l.document_id
                WHERE d.document_type = 'document'
                  AND BTRIM(l.line_content) <> ''
                  AND l.product_id IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM products p
                      WHERE lower(p.name) = lower(BTRIM(l.line_content))
                        AND lower(p.spec) = lower(BTRIM(l.spec)))
                GROUP BY BTRIM(l.line_content), BTRIM(l.spec)
                ORDER BY times_used DESC, last_used DESC
                LIMIT 300
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                items.Add(new
                {
                    name = r.Str("name"),
                    spec = r.Str("spec"),
                    timesUsed = r.Int("times_used"),
                    lastUsed = r.IsDBNull(r.GetOrdinal("last_used")) ? (DateOnly?)null : r.DateOnly("last_used"),
                });
            return Results.Ok(new { items });
        });

        api.MapPost("/products", async (SaveProductReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng nhập tên hàng hoá." });

            await using var conn = await db.OpenAsync();
            var id = Guid.NewGuid();
            var code = (req.Code ?? "").Trim();
            if (code.Length == 0) code = await NextCode(conn, null);
            try
            {
                await conn.Cmd("""
                    INSERT INTO products (id, code, name, spec, unit, note)
                    VALUES (@id, @code, @name, @spec, @unit, @note)
                    """)
                    .With("@id", id).With("@code", code).With("@name", name)
                    .With("@spec", (req.Spec ?? "").Trim()).With("@unit", NormalizeUnit(req.Unit))
                    .With("@note", (req.Note ?? "").Trim())
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict(new { message = "Mặt hàng này đã có trong danh mục." });
            }

            await db.RecordAudit(u.Username(), "Thêm hàng hoá", "Product", code, $"{name} {(req.Spec ?? "").Trim()}".Trim());
            return Results.Ok(new { id, code });
        });

        api.MapPut("/products/{id:guid}", async (Guid id, SaveProductReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng nhập tên hàng hoá." });

            await using var conn = await db.OpenAsync();
            int changed;
            try
            {
                changed = await conn.Cmd("""
                    UPDATE products SET name=@name, spec=@spec, unit=@unit, note=@note,
                        is_active=COALESCE(@active, is_active), updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id
                    """)
                    .With("@id", id).With("@name", name).With("@spec", (req.Spec ?? "").Trim())
                    .With("@unit", NormalizeUnit(req.Unit)).With("@note", (req.Note ?? "").Trim())
                    .With("@active", (object?)req.IsActive ?? DBNull.Value)
                    .ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict(new { message = "Đã có mặt hàng khác trùng tên và quy cách." });
            }
            if (changed == 0) return Results.NotFound();

            await db.RecordAudit(u.Username(), "Sửa hàng hoá", "Product", name, (req.Spec ?? "").Trim());
            return Results.NoContent();
        });

        // Thêm hàng loạt từ màn gợi ý. Bỏ qua (không báo lỗi) những dòng đã có: người dùng bấm chọn
        // 50 dòng thì không thể để một dòng trùng làm hỏng cả mẻ.
        api.MapPost("/products/import", async (ImportProductsReq req, ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.VouchersCreate)) return Results.Forbid();
            var rows = req.Items ?? [];
            if (rows.Count == 0) return Results.BadRequest(new { message = "Chưa chọn mặt hàng nào." });

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var added = 0;
            foreach (var row in rows)
            {
                var name = (row.Name ?? "").Trim();
                if (name.Length == 0) continue;
                var code = await NextCode(conn, tx);
                var inserted = await conn.Cmd("""
                    INSERT INTO products (id, code, name, spec, unit)
                    VALUES (@id, @code, @name, @spec, @unit)
                    ON CONFLICT DO NOTHING
                    """, tx)
                    .With("@id", Guid.NewGuid()).With("@code", code).With("@name", name)
                    .With("@spec", (row.Spec ?? "").Trim()).With("@unit", NormalizeUnit(req.Unit))
                    .ExecuteNonQueryAsync();
                added += inserted;
            }
            await tx.CommitAsync();

            // Dòng phiếu cũ khớp đúng tên+quy cách thì đóng dấu luôn mã hàng: nhờ vậy thống kê theo
            // mặt hàng có số liệu từ ngày đầu chứ không phải đợi phiếu mới.
            var linked = await conn.Cmd("""
                UPDATE document_lines l SET product_id = p.id
                FROM products p
                WHERE l.product_id IS NULL
                  AND lower(BTRIM(l.line_content)) = lower(p.name)
                  AND lower(BTRIM(l.spec)) = lower(p.spec)
                """).ExecuteNonQueryAsync();
            linked += await conn.Cmd("""
                UPDATE purchase_lines l SET product_id = p.id
                FROM products p
                WHERE l.product_id IS NULL
                  AND lower(BTRIM(l.line_content)) = lower(p.name)
                  AND lower(BTRIM(l.spec)) = lower(p.spec)
                """).ExecuteNonQueryAsync();

            await db.RecordAudit(u.Username(), "Thêm hàng hoá hàng loạt", "Product", $"{added} mặt hàng",
                $"Gắn mã cho {linked} dòng phiếu cũ.");
            return Results.Ok(new { added, linkedLines = linked });
        });
    }

    /// <summary>Mã hàng tự sinh khi người dùng không tự đặt: HH00001, HH00002…</summary>
    private static async Task<string> NextCode(NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        var seq = Convert.ToInt64(await (tx is null
            ? conn.Cmd("SELECT nextval('product_code_seq')")
            : conn.Cmd("SELECT nextval('product_code_seq')", tx)).ExecuteScalarAsync() ?? 1L);
        return $"HH{seq:00000}";
    }

    private static string NormalizeUnit(string? unit)
    {
        var value = (unit ?? "").Trim();
        return value.Length == 0 ? "kg" : value.Length > 24 ? value[..24] : value;
    }

    public record SaveProductReq(string? Code, string? Name, string? Spec, string? Unit, string? Note, bool? IsActive);
    public record ImportProductRow(string? Name, string? Spec);
    public record ImportProductsReq(List<ImportProductRow>? Items, string? Unit);
}
