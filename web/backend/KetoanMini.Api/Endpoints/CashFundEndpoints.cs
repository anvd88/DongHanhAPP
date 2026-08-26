using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// QUỸ TIỀN MẶT — sổ theo dõi tiền thật đang nằm trong két của công ty.
///
/// Sổ quỹ KHÔNG chép lại số liệu của các nghiệp vụ khác. Nó là một VIEW hợp nhất
/// (<c>cash_fund_ledger</c>) đọc thẳng từ nguồn:
///   • lệnh thu tiền đã hoàn tất  → tiền VÀO,
///   • phiếu chi tiền mặt đã chi  → tiền RA,
///   • phiếu thu/chi ở trang Thu chi (documents) còn hiệu lực → VÀO/RA,
///   • bút toán thủ công (bảng <c>cash_fund_manual_entries</c>) → VÀO/RA.
///
/// Chọn VIEW thay vì bảng bút toán vật lý là có chủ ý: phiếu thu/chi ở trang Thu chi còn SỬA và HỦY
/// được, nên nếu chép số sang một bảng riêng thì mỗi đường sửa lại phải nhớ đồng bộ — chỉ cần sót
/// một nhánh là số dư lệch mà không ai biết. Đọc thẳng nguồn thì sổ quỹ không bao giờ trôi khỏi
/// chứng từ. Đổi lại, muốn sửa một dòng tự động thì phải sửa ở chứng từ gốc, đúng như nghiệp vụ.
/// </summary>
public static class CashFundEndpoints
{
    public const string DirectionIn = "in";
    public const string DirectionOut = "out";

    /// <summary>Bút toán khai số dư có sẵn trong két lúc bắt đầu dùng sổ quỹ. Chỉ được có MỘT.</summary>
    public const string ReasonOpening = "Số dư đầu kỳ";

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS cash_fund_entry_seq START 1;

            CREATE TABLE IF NOT EXISTS cash_fund_manual_entries (
                id uuid PRIMARY KEY,
                entry_no varchar(32) NOT NULL UNIQUE,
                direction varchar(8) NOT NULL,
                amount numeric(18,0) NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                reason varchar(256) NOT NULL DEFAULT '',
                counterparty varchar(256) NOT NULL DEFAULT '',
                note text NOT NULL DEFAULT '',
                is_opening boolean NOT NULL DEFAULT FALSE,
                created_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                reversed_at timestamptz NULL,
                reversed_by varchar(128) NOT NULL DEFAULT '',
                reverse_reason text NOT NULL DEFAULT '',
                CHECK (direction IN ('in','out')),
                CHECK (amount > 0)
            );
            CREATE INDEX IF NOT EXISTS ix_cash_fund_manual_occurred
                ON cash_fund_manual_entries (occurred_at DESC);
            -- Số dư đầu kỳ chỉ được khai một lần; khai lại phải hủy bút toán cũ trước.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_cash_fund_opening
                ON cash_fund_manual_entries (is_opening)
                WHERE is_opening = TRUE AND reversed_at IS NULL;
            """).ExecuteNonQueryAsync(ct);

        // View dựng lại mỗi lần khởi động: định nghĩa nằm trong mã nguồn, không phải trong CSDL.
        await conn.Cmd($"""
            CREATE OR REPLACE VIEW cash_fund_ledger AS
            SELECT o.id                                        AS source_id,
                   'collection'::varchar(24)                   AS source_kind,
                   o.order_no::varchar(64)                     AS source_ref,
                   '{DirectionIn}'::varchar(8)                 AS direction,
                   COALESCE(o.received_amount, o.collected_amount, 0)::numeric(18,0) AS amount,
                   COALESCE(o.received_at, o.updated_at)       AS occurred_at,
                   'Thu tiền khách hàng'::varchar(256)         AS reason,
                   o.customer_name::varchar(256)               AS counterparty,
                   o.received_by::varchar(128)                 AS actor,
                   ('Tài xế ' || o.driver_name)::text          AS note
            FROM cash_collection_orders o
            WHERE o.status = 'Completed'

            UNION ALL
            SELECT v.id, 'payout', v.voucher_no::varchar(64), '{DirectionOut}',
                   v.amount::numeric(18,0),
                   COALESCE(v.paid_at, v.completed_at, v.updated_at),
                   COALESCE(NULLIF(c.name, ''), 'Chi tiền mặt')::varchar(256),
                   COALESCE(e.full_name, '')::varchar(256),
                   v.completed_by::varchar(128),
                   v.reason
            FROM hr_payout_vouchers v
            LEFT JOIN hr_payout_categories c ON c.id = v.category_id
            LEFT JOIN hr_employees e ON e.id = v.employee_id
            WHERE v.status = 'Paid'

            UNION ALL
            SELECT d.id, CASE WHEN d.document_type = 'receipt' THEN 'receipt' ELSE 'payment' END,
                   d.voucher_no::varchar(64),
                   CASE WHEN d.document_type = 'receipt' THEN '{DirectionIn}' ELSE '{DirectionOut}' END,
                   (SELECT COALESCE(SUM(l.quantity * l.unit_price), 0)
                    FROM document_lines l WHERE l.document_id = d.id)::numeric(18,0),
                   d.doc_date::timestamptz,
                   COALESCE(NULLIF(d.content, ''), 'Phiếu thu chi')::varchar(256),
                   d.customer_name::varchar(256),
                   ''::varchar(128),
                   d.note
            FROM documents d
            WHERE d.document_type IN ('receipt','payment') AND d.cancelled_at IS NULL

            UNION ALL
            SELECT m.id, 'manual', m.entry_no::varchar(64), m.direction, m.amount, m.occurred_at,
                   m.reason, m.counterparty, m.created_by::varchar(128), m.note
            FROM cash_fund_manual_entries m
            WHERE m.reversed_at IS NULL;
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapCashFund(this WebApplication app)
    {
        var g = app.MapGroup("/api/cash-fund").RequireAuthorization();

        // Thẻ "Tồn quỹ tiền mặt" của cả ba trang gọi endpoint này — cố tình giữ thật nhẹ (một dòng
        // tổng hợp) để nhúng vào trang nào cũng không làm chậm trang đó.
        g.MapGet("/balance", async (Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var period = NormalizeMonth(month);
            TryMonthRange(period, out var from, out var to);
            await using var r = await conn.Cmd($"""
                SELECT
                    COALESCE(SUM(CASE WHEN direction='{DirectionIn}' THEN amount ELSE -amount END), 0) AS balance,
                    COALESCE(SUM(CASE WHEN direction='{DirectionIn}' AND occurred_at >= @from AND occurred_at < @to
                                      THEN amount ELSE 0 END), 0) AS month_in,
                    COALESCE(SUM(CASE WHEN direction='{DirectionOut}' AND occurred_at >= @from AND occurred_at < @to
                                      THEN amount ELSE 0 END), 0) AS month_out,
                    COUNT(*) FILTER (WHERE occurred_at >= @from AND occurred_at < @to)::int AS month_count
                FROM cash_fund_ledger
                """).With("@from", from).With("@to", to).ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Results.Ok(new { balance = 0m, monthIn = 0m, monthOut = 0m, monthCount = 0, month = period });
            return Results.Ok(new
            {
                balance = r.Dec("balance"),
                monthIn = r.Dec("month_in"),
                monthOut = r.Dec("month_out"),
                monthCount = r.Int("month_count"),
                month = period,
            });
        }).RequirePermission(Permissions.CashFundRead);

        // Sổ quỹ của một tháng: số dư đầu kỳ (mọi phát sinh TRƯỚC tháng) + từng dòng phát sinh.
        g.MapGet("/", async (Database db, string? month, string? direction, string? source, string? q) =>
        {
            await using var conn = await db.OpenAsync();
            var period = NormalizeMonth(month);
            TryMonthRange(period, out var from, out var to);

            var opening = await conn.Cmd($"""
                SELECT COALESCE(SUM(CASE WHEN direction='{DirectionIn}' THEN amount ELSE -amount END), 0)
                FROM cash_fund_ledger WHERE occurred_at < @from
                """).With("@from", from).ExecuteScalarAsync();
            var openingBalance = opening is null or DBNull ? 0m : Convert.ToDecimal(opening);

            var where = new List<string> { "occurred_at >= @from", "occurred_at < @to" };
            var dir = (direction ?? "").Trim().ToLowerInvariant();
            var filterDirection = dir is DirectionIn or DirectionOut;
            if (filterDirection) where.Add("direction = @dir");
            var src = (source ?? "").Trim().ToLowerInvariant();
            var filterSource = src is "collection" or "payout" or "receipt" or "payment" or "manual";
            if (filterSource) where.Add("source_kind = @src");
            var search = (q ?? "").Trim();
            if (search.Length > 0) where.Add("(source_ref ILIKE @q OR reason ILIKE @q OR counterparty ILIKE @q)");

            var cmd = conn.Cmd($"""
                SELECT source_id, source_kind, source_ref, direction, amount, occurred_at,
                       reason, counterparty, actor, note
                FROM cash_fund_ledger
                WHERE {string.Join(" AND ", where)}
                ORDER BY occurred_at, source_ref
                """).With("@from", from).With("@to", to);
            if (filterDirection) cmd.With("@dir", dir);
            if (filterSource) cmd.With("@src", src);
            if (search.Length > 0) cmd.With("@q", $"%{search}%");

            var rows = new List<object>();
            decimal totalIn = 0, totalOut = 0, running = openingBalance;
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    var amount = r.Dec("amount");
                    var isIn = r.Str("direction") == DirectionIn;
                    if (isIn) { totalIn += amount; running += amount; }
                    else { totalOut += amount; running -= amount; }
                    rows.Add(new
                    {
                        sourceId = r.Guid("source_id"),
                        sourceKind = r.Str("source_kind"),
                        sourceRef = r.Str("source_ref"),
                        direction = r.Str("direction"),
                        amount,
                        occurredAt = r.Dt("occurred_at"),
                        reason = r.Str("reason"),
                        counterparty = r.Str("counterparty"),
                        actor = r.Str("actor"),
                        note = r.Str("note"),
                        balanceAfter = running,
                    });
                }

            return Results.Ok(new
            {
                month = period,
                openingBalance,
                totalIn,
                totalOut,
                closingBalance = running,
                entries = rows,
            });
        }).RequirePermission(Permissions.CashFundRead);

        // Bút toán THỦ CÔNG: chỉ dành cho tiền ra/vào không có chứng từ nào khác trong hệ thống
        // (khai số dư đầu kỳ, nộp tiền vào ngân hàng, rút tiền về quỹ, điều chỉnh kiểm kê…).
        g.MapPost("/entries", async (ManualEntryReq req, ClaimsPrincipal u, Database db) =>
        {
            var direction = (req.Direction ?? "").Trim().ToLowerInvariant();
            if (direction is not (DirectionIn or DirectionOut))
                return Results.BadRequest(new { message = "Chiều tiền phải là thu (in) hoặc chi (out)." });
            if (req.Amount <= 0 || req.Amount != decimal.Truncate(req.Amount))
                return Results.BadRequest(new { message = "Số tiền phải là số nguyên dương." });
            if (req.Amount > 999_999_999_999_999m)
                return Results.BadRequest(new { message = "Số tiền vượt giới hạn cho phép." });
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do thu/chi." });
            if (reason.Length > 256) return Results.BadRequest(new { message = "Lý do không được vượt quá 256 ký tự." });
            var counterparty = (req.Counterparty ?? "").Trim();
            if (counterparty.Length > 256) return Results.BadRequest(new { message = "Tên người nộp/nhận quá dài." });
            var note = (req.Note ?? "").Trim();
            if (note.Length > 1000) return Results.BadRequest(new { message = "Ghi chú không được vượt quá 1.000 ký tự." });
            var occurredAt = (req.OccurredAt ?? DateTime.UtcNow).ToUniversalTime();
            if (occurredAt > DateTime.UtcNow.AddDays(1))
                return Results.BadRequest(new { message = "Thời điểm phát sinh không được ở tương lai." });

            var opening = req.IsOpening == true;
            if (opening && direction != DirectionIn)
                return Results.BadRequest(new { message = "Số dư đầu kỳ phải ghi ở chiều thu." });

            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            if (opening)
            {
                var existed = await conn.Cmd(
                    "SELECT entry_no FROM cash_fund_manual_entries WHERE is_opening=TRUE AND reversed_at IS NULL LIMIT 1", tx)
                    .ExecuteScalarAsync();
                if (existed is not null and not DBNull)
                    return Results.BadRequest(new { message = $"Đã có số dư đầu kỳ ({existed}); hãy hủy bút toán đó trước khi khai lại." });
            }

            var id = Guid.NewGuid();
            var no = await NextEntryNo(conn, tx);
            await conn.Cmd("""
                INSERT INTO cash_fund_manual_entries
                    (id, entry_no, direction, amount, occurred_at, reason, counterparty, note, is_opening, created_by)
                VALUES (@id, @no, @dir, @amount, @at, @reason, @party, @note, @opening, @by)
                """, tx)
                .With("@id", id).With("@no", no).With("@dir", direction)
                .With("@amount", decimal.Truncate(req.Amount)).With("@at", occurredAt)
                .With("@reason", opening ? ReasonOpening : reason).With("@party", counterparty)
                .With("@note", note).With("@opening", opening).With("@by", u.Username())
                .ExecuteNonQueryAsync();
            await tx.CommitAsync();

            await db.RecordAudit(u.Username(),
                direction == DirectionIn ? "Ghi thu quỹ tiền mặt" : "Ghi chi quỹ tiền mặt",
                "CashFund", no, $"{decimal.Truncate(req.Amount):N0} đồng; {(opening ? ReasonOpening : reason)}.");
            return Results.Ok(new { id, entryNo = no });
        }).RequirePermission(Permissions.CashFundManage);

        // Chỉ HỦY được bút toán thủ công. Dòng sinh từ lệnh thu/phiếu chi/phiếu thu-chi phải sửa ở
        // chứng từ gốc, nếu không sổ quỹ sẽ nói khác chứng từ.
        g.MapPost("/entries/{id:guid}/reverse", async (Guid id, ReasonReq req, ClaimsPrincipal u, Database db) =>
        {
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do hủy bút toán." });
            if (reason.Length > 1000) return Results.BadRequest(new { message = "Lý do không được vượt quá 1.000 ký tự." });
            await using var conn = await db.OpenAsync();
            var entryNo = await conn.Cmd("""
                UPDATE cash_fund_manual_entries
                SET reversed_at = CURRENT_TIMESTAMP, reversed_by = @by, reverse_reason = @reason
                WHERE id = @id AND reversed_at IS NULL
                RETURNING entry_no
                """).With("@id", id).With("@by", u.Username()).With("@reason", reason).ExecuteScalarAsync();
            if (entryNo is null or DBNull)
                return Results.BadRequest(new { message = "Bút toán không tồn tại hoặc đã bị hủy." });
            await db.RecordAudit(u.Username(), "Hủy bút toán quỹ tiền mặt", "CashFund", entryNo.ToString() ?? "", reason);
            return Results.NoContent();
        }).RequirePermission(Permissions.CashFundManage);

        // Danh sách bút toán thủ công (kể cả đã hủy) — chỗ duy nhất nhìn thấy dấu vết bút toán bị hủy.
        g.MapGet("/entries", async (Database db, string? month) =>
        {
            await using var conn = await db.OpenAsync();
            var period = NormalizeMonth(month);
            TryMonthRange(period, out var from, out var to);
            var rows = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, entry_no, direction, amount, occurred_at, reason, counterparty, note,
                       is_opening, created_by, created_at, reversed_at, reversed_by, reverse_reason
                FROM cash_fund_manual_entries
                WHERE occurred_at >= @from AND occurred_at < @to
                ORDER BY occurred_at DESC, entry_no DESC
                """).With("@from", from).With("@to", to).ExecuteReaderAsync();
            while (await r.ReadAsync())
                rows.Add(new
                {
                    id = r.Guid("id"),
                    entryNo = r.Str("entry_no"),
                    direction = r.Str("direction"),
                    amount = r.Dec("amount"),
                    occurredAt = r.Dt("occurred_at"),
                    reason = r.Str("reason"),
                    counterparty = r.Str("counterparty"),
                    note = r.Str("note"),
                    isOpening = r.Bool("is_opening"),
                    createdBy = r.Str("created_by"),
                    createdAt = r.Dt("created_at"),
                    reversedAt = r.DtNull("reversed_at"),
                    reversedBy = r.Str("reversed_by"),
                    reverseReason = r.Str("reverse_reason"),
                });
            return Results.Ok(new { month = period, entries = rows });
        }).RequirePermission(Permissions.CashFundRead);
    }

    private static async Task<string> NextEntryNo(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var value = await conn.Cmd("SELECT nextval('cash_fund_entry_seq')", tx).ExecuteScalarAsync();
        var seq = Convert.ToInt64(value ?? 1L);
        return $"QTM{DateTime.Now:yyMM}-{seq:D5}";
    }

    private static string NormalizeMonth(string? month)
        => TryMonthRange(month, out _, out _) ? month!.Trim() : DateTime.Now.ToString("yyyy-MM");

    /// <summary>
    /// "yyyy-MM" → khoảng [đầu tháng, đầu tháng sau). Lọc bằng KHOẢNG chứ không to_char(...) để
    /// index trên occurred_at còn dùng được.
    /// </summary>
    private static bool TryMonthRange(string? month, out DateTime start, out DateTime end)
    {
        start = end = default;
        var value = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("yyyy-MM") : month.Trim();
        var parts = value.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)
            || y is < 2000 or > 9999 || m is < 1 or > 12)
        {
            start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, DateTimeKind.Local);
            end = start.AddMonths(1);
            return false;
        }
        start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Local);
        end = start.AddMonths(1);
        return true;
    }

    public record ManualEntryReq(string? Direction, decimal Amount, DateTime? OccurredAt, string? Reason,
        string? Counterparty, string? Note, bool? IsOpening);
    public record ReasonReq(string? Reason);
}
