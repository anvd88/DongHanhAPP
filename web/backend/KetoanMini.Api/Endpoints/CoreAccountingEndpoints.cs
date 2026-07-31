using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Sổ kế toán kép dùng chung cho toàn bộ các phân hệ. Chứng từ đã ghi sổ không bị sửa/xóa;
/// điều chỉnh phải đi bằng một bút toán mới để giữ nguyên dấu vết kiểm toán.
/// </summary>
public static class CoreAccountingEndpoints
{
    private static readonly (string Code, string Name, string Type, string Side, string Parent)[] DefaultAccounts =
    [
        ("111", "Tiền mặt", "Asset", "Debit", ""),
        ("112", "Tiền gửi ngân hàng", "Asset", "Debit", ""),
        ("131", "Phải thu khách hàng", "Asset", "Debit", ""),
        ("1331", "Thuế GTGT được khấu trừ", "Asset", "Debit", "133"),
        ("152", "Nguyên liệu, vật liệu", "Asset", "Debit", ""),
        ("156", "Hàng hóa", "Asset", "Debit", ""),
        ("211", "Tài sản cố định hữu hình", "Asset", "Debit", ""),
        ("214", "Hao mòn tài sản cố định", "Asset", "Credit", ""),
        ("331", "Phải trả người bán", "Liability", "Credit", ""),
        ("3331", "Thuế GTGT phải nộp", "Liability", "Credit", "333"),
        ("334", "Phải trả người lao động", "Liability", "Credit", ""),
        ("341", "Vay và nợ thuê tài chính", "Liability", "Credit", ""),
        ("411", "Vốn đầu tư của chủ sở hữu", "Equity", "Credit", ""),
        ("421", "Lợi nhuận sau thuế chưa phân phối", "Equity", "Credit", ""),
        ("511", "Doanh thu bán hàng và cung cấp dịch vụ", "Revenue", "Credit", ""),
        ("515", "Doanh thu hoạt động tài chính", "Revenue", "Credit", ""),
        ("632", "Giá vốn hàng bán", "Expense", "Debit", ""),
        ("635", "Chi phí tài chính", "Expense", "Debit", ""),
        ("641", "Chi phí bán hàng", "Expense", "Debit", ""),
        ("642", "Chi phí quản lý doanh nghiệp", "Expense", "Debit", ""),
        ("711", "Thu nhập khác", "Revenue", "Credit", ""),
        ("811", "Chi phí khác", "Expense", "Debit", "")
    ];

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS core_accounts (
                code varchar(20) PRIMARY KEY,
                name varchar(240) NOT NULL,
                account_type varchar(20) NOT NULL,
                normal_side varchar(10) NOT NULL,
                parent_code varchar(20) NOT NULL DEFAULT '',
                is_active boolean NOT NULL DEFAULT TRUE,
                is_system boolean NOT NULL DEFAULT FALSE,
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT ck_core_account_type CHECK (account_type IN ('Asset','Liability','Equity','Revenue','Expense')),
                CONSTRAINT ck_core_account_side CHECK (normal_side IN ('Debit','Credit'))
            );

            CREATE TABLE IF NOT EXISTS core_periods (
                period varchar(7) PRIMARY KEY,
                starts_on date NOT NULL,
                ends_on date NOT NULL,
                status varchar(12) NOT NULL DEFAULT 'Open',
                locked_at timestamptz NULL,
                locked_by varchar(128) NOT NULL DEFAULT '',
                reopened_at timestamptz NULL,
                reopened_by varchar(128) NOT NULL DEFAULT '',
                reopen_reason text NOT NULL DEFAULT '',
                CONSTRAINT ck_core_period_status CHECK (status IN ('Open','Locked'))
            );

            CREATE TABLE IF NOT EXISTS core_journal_entries (
                id uuid PRIMARY KEY,
                entry_no varchar(32) NOT NULL UNIQUE,
                entry_date date NOT NULL,
                description text NOT NULL,
                reference varchar(100) NOT NULL DEFAULT '',
                source_module varchar(30) NOT NULL DEFAULT 'Manual',
                source_id varchar(100) NOT NULL DEFAULT '',
                status varchar(12) NOT NULL DEFAULT 'Draft',
                created_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                posted_by varchar(128) NOT NULL DEFAULT '',
                posted_at timestamptz NULL,
                CONSTRAINT ck_core_entry_status CHECK (status IN ('Draft','Posted','Reversed'))
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_core_entry_source
                ON core_journal_entries (source_module, source_id) WHERE source_id <> '';
            CREATE INDEX IF NOT EXISTS ix_core_entry_date ON core_journal_entries (entry_date DESC, entry_no DESC);

            CREATE TABLE IF NOT EXISTS core_journal_lines (
                id bigserial PRIMARY KEY,
                entry_id uuid NOT NULL REFERENCES core_journal_entries(id) ON DELETE RESTRICT,
                line_no integer NOT NULL,
                account_code varchar(20) NOT NULL REFERENCES core_accounts(code),
                description text NOT NULL DEFAULT '',
                debit numeric(18,2) NOT NULL DEFAULT 0,
                credit numeric(18,2) NOT NULL DEFAULT 0,
                partner varchar(240) NOT NULL DEFAULT '',
                cost_center varchar(100) NOT NULL DEFAULT '',
                CONSTRAINT ck_core_line_amount CHECK (
                    debit >= 0 AND credit >= 0 AND
                    ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))
                )
            );
            CREATE INDEX IF NOT EXISTS ix_core_line_entry ON core_journal_lines (entry_id, line_no);
            CREATE INDEX IF NOT EXISTS ix_core_line_account ON core_journal_lines (account_code);

            CREATE TABLE IF NOT EXISTS core_budgets (
                id uuid PRIMARY KEY,
                period varchar(7) NOT NULL REFERENCES core_periods(period),
                account_code varchar(20) NOT NULL REFERENCES core_accounts(code),
                department varchar(120) NOT NULL DEFAULT 'Toàn công ty',
                amount numeric(18,2) NOT NULL DEFAULT 0,
                updated_by varchar(128) NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(period, account_code, department)
            );

            CREATE TABLE IF NOT EXISTS core_reconciliations (
                id uuid PRIMARY KEY,
                period varchar(7) NOT NULL REFERENCES core_periods(period),
                kind varchar(20) NOT NULL,
                subject varchar(240) NOT NULL DEFAULT '',
                book_balance numeric(18,2) NOT NULL DEFAULT 0,
                subledger_balance numeric(18,2) NOT NULL DEFAULT 0,
                status varchar(20) NOT NULL DEFAULT 'Unmatched',
                note text NOT NULL DEFAULT '',
                checked_by varchar(128) NOT NULL DEFAULT '',
                checked_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(period, kind, subject),
                CONSTRAINT ck_core_reconciliation_kind CHECK (kind IN ('Receivable','Payable','Bank','Inventory')),
                CONSTRAINT ck_core_reconciliation_status CHECK (status IN ('Matched','Unmatched','Investigating'))
            );

            CREATE TABLE IF NOT EXISTS core_period_events (
                id bigserial PRIMARY KEY,
                period varchar(7) NOT NULL,
                action varchar(20) NOT NULL,
                reason text NOT NULL DEFAULT '',
                username varchar(128) NOT NULL DEFAULT '',
                occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_core_period_events ON core_period_events (period, occurred_at DESC);
            """).ExecuteNonQueryAsync(ct);

        foreach (var item in DefaultAccounts)
        {
            await conn.Cmd("""
                INSERT INTO core_accounts (code, name, account_type, normal_side, parent_code, is_system)
                VALUES (@code, @name, @type, @side, @parent, TRUE)
                ON CONFLICT (code) DO NOTHING
                """)
                .With("@code", item.Code).With("@name", item.Name).With("@type", item.Type)
                .With("@side", item.Side).With("@parent", item.Parent)
                .ExecuteNonQueryAsync(ct);
        }

        var today = DateTime.UtcNow.AddHours(7);
        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(today.Year, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            await conn.Cmd("""
                INSERT INTO core_periods (period, starts_on, ends_on)
                VALUES (@period, @start, @end)
                ON CONFLICT (period) DO NOTHING
                """)
                .With("@period", $"{today.Year:D4}-{month:D2}")
                .With("@start", start).With("@end", end)
                .ExecuteNonQueryAsync(ct);
        }
    }

    public static void MapCoreAccounting(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/core-accounting")
            .RequirePermission(Permissions.AccountingAccess);

        api.MapGet("/overview", GetOverview);
        api.MapGet("/accounts", GetAccounts);
        api.MapPost("/accounts", SaveAccount).RequirePermission(Permissions.VouchersApprove);
        api.MapGet("/entries", GetEntries);
        api.MapPost("/entries", CreateEntry).RequirePermission(Permissions.VouchersCreate);
        api.MapPost("/entries/{id:guid}/post", PostEntry).RequirePermission(Permissions.VouchersApprove);
        api.MapGet("/periods", GetPeriods);
        api.MapPost("/periods/{period}/lock", LockPeriod).RequirePermission(Permissions.VouchersApprove);
        api.MapPost("/periods/{period}/reopen", ReopenPeriod).RequirePermission(Permissions.VouchersApprove);
        api.MapGet("/reconciliations", GetReconciliations);
        api.MapPost("/reconciliations", SaveReconciliation).RequirePermission(Permissions.VouchersCreate);
        api.MapGet("/budgets", GetBudgets);
        api.MapPost("/budgets", SaveBudget).RequirePermission(Permissions.VouchersCreate);
        api.MapGet("/automation", GetAutomation);
        api.MapPost("/automation/run", RunAutomation).RequirePermission(Permissions.VouchersCreate);
    }

    private static async Task<IResult> GetOverview(string? period, Database db)
    {
        period = NormalizePeriod(period);
        await EnsurePeriod(db, period);
        await using var conn = await db.OpenAsync();

        var periodStatus = Convert.ToString(await conn.Cmd("SELECT status FROM core_periods WHERE period=@p")
            .With("@p", period).ExecuteScalarAsync()) ?? "Open";
        var counts = await ReadEntryCounts(conn, period);
        var metrics = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await conn.Cmd("""
            SELECT a.account_type,
                   COALESCE(SUM(CASE WHEN e.id IS NULL THEN 0
                       WHEN a.account_type IN ('Asset','Expense') THEN l.debit-l.credit
                       ELSE l.credit-l.debit END),0) balance
            FROM core_accounts a
            LEFT JOIN core_journal_lines l ON l.account_code=a.code
            LEFT JOIN core_journal_entries e ON e.id=l.entry_id
                AND e.status='Posted' AND to_char(e.entry_date,'YYYY-MM')=@period
            GROUP BY a.account_type
            """).With("@period", period).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) metrics[r.Str("account_type")] = r.Dec("balance");
        }

        var special = new Dictionary<string, decimal>();
        await using (var r = await conn.Cmd("""
            SELECT a.code,
                   COALESCE(SUM(CASE WHEN e.id IS NULL THEN 0
                       WHEN a.normal_side='Debit' THEN l.debit-l.credit ELSE l.credit-l.debit END),0) balance
            FROM core_accounts a
            LEFT JOIN core_journal_lines l ON l.account_code=a.code
            LEFT JOIN core_journal_entries e ON e.id=l.entry_id
                AND e.status='Posted' AND to_char(e.entry_date,'YYYY-MM')=@period
            WHERE a.code IN ('111','112','1331','3331','632')
            GROUP BY a.code
            """).With("@period", period).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) special[r.Str("code")] = r.Dec("balance");
        }

        var budget = Convert.ToDecimal(await conn.Cmd("SELECT COALESCE(SUM(amount),0) FROM core_budgets WHERE period=@p")
            .With("@p", period).ExecuteScalarAsync() ?? 0m);
        var recUnmatched = Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(*) FROM core_reconciliations
            WHERE period=@p AND status <> 'Matched'
            """).With("@p", period).ExecuteScalarAsync() ?? 0);
        var revenue = metrics.GetValueOrDefault("Revenue");
        var expense = metrics.GetValueOrDefault("Expense");
        var balanceSheet = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await conn.Cmd("""
            SELECT a.account_type,
                   COALESCE(SUM(CASE WHEN e.id IS NULL THEN 0
                       WHEN a.account_type='Asset' THEN l.debit-l.credit
                       ELSE l.credit-l.debit END),0) balance
            FROM core_accounts a
            LEFT JOIN core_journal_lines l ON l.account_code=a.code
            LEFT JOIN core_journal_entries e ON e.id=l.entry_id
                AND e.status='Posted' AND e.entry_date < (@period||'-01')::date + INTERVAL '1 month'
            WHERE a.account_type IN ('Asset','Liability','Equity')
            GROUP BY a.account_type
            """).With("@period", period).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) balanceSheet[r.Str("account_type")] = r.Dec("balance");
        }

        var recent = new List<object>();
        await using (var r = await conn.Cmd("""
            SELECT e.id, e.entry_no, e.entry_date, e.description, e.reference, e.source_module, e.status,
                   COALESCE(SUM(l.debit),0) total
            FROM core_journal_entries e
            JOIN core_journal_lines l ON l.entry_id=e.id
            WHERE to_char(e.entry_date,'YYYY-MM')=@period
            GROUP BY e.id
            ORDER BY e.entry_date DESC, e.entry_no DESC
            LIMIT 8
            """).With("@period", period).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) recent.Add(EntrySummary(r));
        }

        return Results.Ok(new
        {
            period,
            periodStatus,
            counts.total,
            counts.draft,
            counts.posted,
            reconciliationIssues = recUnmatched,
            revenue,
            expenses = expense,
            profit = revenue - expense,
            assets = balanceSheet.GetValueOrDefault("Asset"),
            liabilities = balanceSheet.GetValueOrDefault("Liability"),
            equity = balanceSheet.GetValueOrDefault("Equity"),
            cashFlow = special.GetValueOrDefault("111") + special.GetValueOrDefault("112"),
            vatInput = special.GetValueOrDefault("1331"),
            vatOutput = special.GetValueOrDefault("3331"),
            costOfGoods = special.GetValueOrDefault("632"),
            budget,
            budgetUsed = budget == 0 ? 0 : Math.Round(expense / budget * 100m, 1),
            recent
        });
    }

    private static async Task<IResult> GetAccounts(string? period, Database db)
    {
        period = NormalizePeriod(period);
        await using var conn = await db.OpenAsync();
        var rows = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT a.code, a.name, a.account_type, a.normal_side, a.parent_code, a.is_active, a.is_system,
                   COALESCE(SUM(CASE WHEN e.id IS NULL THEN 0
                       WHEN a.normal_side='Debit' THEN l.debit-l.credit ELSE l.credit-l.debit END),0) balance
            FROM core_accounts a
            LEFT JOIN core_journal_lines l ON l.account_code=a.code
            LEFT JOIN core_journal_entries e ON e.id=l.entry_id
                AND e.status='Posted' AND to_char(e.entry_date,'YYYY-MM')=@period
            GROUP BY a.code
            ORDER BY a.code
            """).With("@period", period).ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new
            {
                code = r.Str("code"), name = r.Str("name"), type = r.Str("account_type"),
                normalSide = r.Str("normal_side"), parentCode = r.Str("parent_code"),
                isActive = r.Bool("is_active"), isSystem = r.Bool("is_system"), balance = r.Dec("balance")
            });
        return Results.Ok(rows);
    }

    private static async Task<IResult> SaveAccount(AccountRequest req, ClaimsPrincipal user, Database db)
    {
        var code = (req.Code ?? "").Trim();
        var name = (req.Name ?? "").Trim();
        var type = (req.Type ?? "").Trim();
        var side = (req.NormalSide ?? "").Trim();
        if (code.Length is < 2 or > 20 || name.Length is < 2 or > 240)
            return Results.BadRequest(new { message = "Mã hoặc tên tài khoản không hợp lệ." });
        if (!new[] { "Asset", "Liability", "Equity", "Revenue", "Expense" }.Contains(type))
            return Results.BadRequest(new { message = "Loại tài khoản không hợp lệ." });
        if (side is not ("Debit" or "Credit"))
            return Results.BadRequest(new { message = "Tính chất số dư không hợp lệ." });

        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO core_accounts (code,name,account_type,normal_side,parent_code,is_active,is_system,updated_at)
            VALUES (@code,@name,@type,@side,@parent,@active,FALSE,CURRENT_TIMESTAMP)
            ON CONFLICT (code) DO UPDATE SET
                name=@name, account_type=@type, normal_side=@side, parent_code=@parent,
                is_active=@active, updated_at=CURRENT_TIMESTAMP
            """)
            .With("@code", code).With("@name", name).With("@type", type).With("@side", side)
            .With("@parent", (req.ParentCode ?? "").Trim()).With("@active", req.IsActive)
            .ExecuteNonQueryAsync();
        await db.RecordAudit(user.Username(), "Cập nhật hệ thống tài khoản", "CoreAccount", code, name);
        return Results.Ok(new { code });
    }

    private static async Task<IResult> GetEntries(string? period, string? search, Database db)
    {
        period = NormalizePeriod(period);
        search = (search ?? "").Trim();
        await using var conn = await db.OpenAsync();
        var entries = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT e.id, e.entry_no, e.entry_date, e.description, e.reference, e.source_module, e.status,
                   e.created_by, e.created_at, e.posted_by, e.posted_at,
                   COALESCE(SUM(l.debit),0) total
            FROM core_journal_entries e
            JOIN core_journal_lines l ON l.entry_id=e.id
            WHERE to_char(e.entry_date,'YYYY-MM')=@period
              AND (@search='' OR e.entry_no ILIKE '%'||@search||'%' OR e.description ILIKE '%'||@search||'%'
                   OR e.reference ILIKE '%'||@search||'%')
            GROUP BY e.id
            ORDER BY e.entry_date DESC, e.entry_no DESC
            LIMIT 500
            """).With("@period", period).With("@search", search).ExecuteReaderAsync();
        while (await r.ReadAsync()) entries.Add(EntrySummary(r, true));
        await r.CloseAsync();

        var lines = new List<object>();
        await using var lineReader = await conn.Cmd("""
            SELECT l.entry_id, l.id, l.line_no, l.account_code, a.name account_name, l.description,
                   l.debit, l.credit, l.partner, l.cost_center
            FROM core_journal_lines l
            JOIN core_accounts a ON a.code=l.account_code
            JOIN core_journal_entries e ON e.id=l.entry_id
            WHERE to_char(e.entry_date,'YYYY-MM')=@period
            ORDER BY l.entry_id, l.line_no
            """).With("@period", period).ExecuteReaderAsync();
        while (await lineReader.ReadAsync())
            lines.Add(new
            {
                entryId = lineReader.Guid("entry_id"), id = lineReader.Long("id"),
                lineNo = lineReader.Int("line_no"), accountCode = lineReader.Str("account_code"),
                accountName = lineReader.Str("account_name"), description = lineReader.Str("description"),
                debit = lineReader.Dec("debit"), credit = lineReader.Dec("credit"),
                partner = lineReader.Str("partner"), costCenter = lineReader.Str("cost_center")
            });
        return Results.Ok(new { entries, lines });
    }

    private static async Task<IResult> CreateEntry(JournalEntryRequest req, ClaimsPrincipal user, Database db)
    {
        if (req.Lines is null || req.Lines.Count < 2)
            return Results.BadRequest(new { message = "Bút toán cần ít nhất hai dòng." });
        if (string.IsNullOrWhiteSpace(req.Description))
            return Results.BadRequest(new { message = "Vui lòng nhập diễn giải bút toán." });

        var debit = req.Lines.Sum(x => x.Debit);
        var credit = req.Lines.Sum(x => x.Credit);
        if (debit <= 0 || Math.Abs(debit - credit) >= 0.01m)
            return Results.BadRequest(new { message = "Tổng phát sinh Nợ phải bằng tổng phát sinh Có." });
        if (req.Lines.Any(x => x.Debit < 0 || x.Credit < 0 ||
                               (x.Debit > 0 && x.Credit > 0) ||
                               (x.Debit == 0 && x.Credit == 0)))
            return Results.BadRequest(new { message = "Mỗi dòng chỉ được ghi một bên Nợ hoặc Có." });

        var period = req.EntryDate.ToString("yyyy-MM");
        await EnsurePeriod(db, period);
        await using var conn = await db.OpenAsync();
        if (!await IsPeriodOpen(conn, period))
            return Results.Conflict(new { message = $"Kỳ {period} đã khóa, không thể thêm bút toán." });

        var accounts = req.Lines.Select(x => (x.AccountCode ?? "").Trim()).Distinct().ToArray();
        var found = Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM core_accounts WHERE code=ANY(@codes) AND is_active=TRUE")
            .With("@codes", accounts).ExecuteScalarAsync() ?? 0);
        if (found != accounts.Length)
            return Results.BadRequest(new { message = "Có tài khoản không tồn tại hoặc đã ngừng sử dụng." });

        var id = Guid.NewGuid();
        await using var tx = await conn.BeginTransactionAsync();
        var nextNo = await NextEntryNo(conn, tx, req.EntryDate);
        await TxCmd(conn, tx, """
            INSERT INTO core_journal_entries
                (id,entry_no,entry_date,description,reference,source_module,source_id,status,created_by)
            VALUES (@id,@no,@date,@description,@reference,@module,@source,'Draft',@by)
            """)
            .With("@id", id).With("@no", nextNo).With("@date", req.EntryDate)
            .With("@description", req.Description.Trim()).With("@reference", (req.Reference ?? "").Trim())
            .With("@module", string.IsNullOrWhiteSpace(req.SourceModule) ? "Manual" : req.SourceModule.Trim())
            .With("@source", (req.SourceId ?? "").Trim()).With("@by", user.Username())
            .ExecuteNonQueryAsync();
        var lineNo = 1;
        foreach (var line in req.Lines)
        {
            await TxCmd(conn, tx, """
                INSERT INTO core_journal_lines
                    (entry_id,line_no,account_code,description,debit,credit,partner,cost_center)
                VALUES (@entry,@line,@account,@description,@debit,@credit,@partner,@cost)
                """)
                .With("@entry", id).With("@line", lineNo++).With("@account", line.AccountCode.Trim())
                .With("@description", (line.Description ?? "").Trim()).With("@debit", line.Debit)
                .With("@credit", line.Credit).With("@partner", (line.Partner ?? "").Trim())
                .With("@cost", (line.CostCenter ?? "").Trim()).ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        await db.RecordAudit(user.Username(), "Tạo bút toán", "CoreJournalEntry", nextNo,
            $"{req.Description.Trim()} · Nợ/Có {debit:N0}");
        return Results.Ok(new { id, entryNo = nextNo, status = "Draft" });
    }

    private static async Task<IResult> PostEntry(Guid id, ClaimsPrincipal user, Database db)
    {
        await using var conn = await db.OpenAsync();
        var period = Convert.ToString(await conn.Cmd("""
            SELECT to_char(entry_date,'YYYY-MM') FROM core_journal_entries WHERE id=@id
            """).With("@id", id).ExecuteScalarAsync());
        if (period is null) return Results.NotFound();
        if (!await IsPeriodOpen(conn, period))
            return Results.Conflict(new { message = $"Kỳ {period} đã khóa, không thể ghi sổ." });

        var changed = await conn.Cmd("""
            UPDATE core_journal_entries
            SET status='Posted', posted_by=@by, posted_at=CURRENT_TIMESTAMP
            WHERE id=@id AND status='Draft'
            """).With("@id", id).With("@by", user.Username()).ExecuteNonQueryAsync();
        if (changed == 0) return Results.Conflict(new { message = "Bút toán không còn ở trạng thái chờ ghi sổ." });
        await db.RecordAudit(user.Username(), "Ghi sổ bút toán", "CoreJournalEntry", id.ToString(), $"Kỳ {period}");
        return Results.Ok(new { status = "Posted" });
    }

    private static async Task<IResult> GetPeriods(Database db)
    {
        await using var conn = await db.OpenAsync();
        var periods = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT p.period,p.starts_on,p.ends_on,p.status,p.locked_at,p.locked_by,p.reopened_at,p.reopened_by,p.reopen_reason,
                   COUNT(e.id) FILTER (WHERE e.status='Draft') draft_count,
                   COUNT(e.id) FILTER (WHERE e.status='Posted') posted_count
            FROM core_periods p
            LEFT JOIN core_journal_entries e ON to_char(e.entry_date,'YYYY-MM')=p.period
            GROUP BY p.period
            ORDER BY p.period DESC
            """).ExecuteReaderAsync();
        while (await r.ReadAsync())
            periods.Add(new
            {
                period = r.Str("period"), startsOn = r.DateOnly("starts_on"), endsOn = r.DateOnly("ends_on"),
                status = r.Str("status"), lockedAt = r.DtNull("locked_at"), lockedBy = r.Str("locked_by"),
                reopenedAt = r.DtNull("reopened_at"), reopenedBy = r.Str("reopened_by"),
                reopenReason = r.Str("reopen_reason"), draftCount = r.Int("draft_count"), postedCount = r.Int("posted_count")
            });
        return Results.Ok(periods);
    }

    private static async Task<IResult> LockPeriod(string period, ClaimsPrincipal user, Database db)
    {
        period = NormalizePeriod(period);
        await EnsurePeriod(db, period);
        await using var conn = await db.OpenAsync();
        var drafts = Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(*) FROM core_journal_entries
            WHERE to_char(entry_date,'YYYY-MM')=@p AND status='Draft'
            """).With("@p", period).ExecuteScalarAsync() ?? 0);
        if (drafts > 0)
            return Results.Conflict(new { message = $"Còn {drafts} bút toán nháp. Hãy ghi sổ trước khi khóa kỳ." });
        await conn.Cmd("""
            UPDATE core_periods SET status='Locked',locked_at=CURRENT_TIMESTAMP,locked_by=@by
            WHERE period=@p AND status='Open'
            """).With("@p", period).With("@by", user.Username()).ExecuteNonQueryAsync();
        await RecordPeriodEvent(conn, period, "Lock", "", user.Username());
        await db.RecordAudit(user.Username(), "Khóa kỳ kế toán", "CorePeriod", period, "Kỳ đã khóa.");
        return Results.Ok(new { period, status = "Locked" });
    }

    private static async Task<IResult> ReopenPeriod(string period, ReopenPeriodRequest req, ClaimsPrincipal user, Database db)
    {
        period = NormalizePeriod(period);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < 10)
            return Results.BadRequest(new { message = "Lý do mở lại kỳ phải có ít nhất 10 ký tự." });
        await EnsurePeriod(db, period);
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            UPDATE core_periods SET status='Open',reopened_at=CURRENT_TIMESTAMP,reopened_by=@by,reopen_reason=@reason
            WHERE period=@p AND status='Locked'
            """).With("@p", period).With("@by", user.Username()).With("@reason", reason).ExecuteNonQueryAsync();
        await RecordPeriodEvent(conn, period, "Reopen", reason, user.Username());
        await db.RecordAudit(user.Username(), "Mở lại kỳ kế toán", "CorePeriod", period, reason);
        return Results.Ok(new { period, status = "Open" });
    }

    private static async Task<IResult> GetReconciliations(string? period, Database db)
    {
        period = NormalizePeriod(period);
        await using var conn = await db.OpenAsync();
        var rows = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT id,period,kind,subject,book_balance,subledger_balance,status,note,checked_by,checked_at
            FROM core_reconciliations WHERE period=@p ORDER BY kind,subject
            """).With("@p", period).ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new
            {
                id = r.Guid("id"), period = r.Str("period"), kind = r.Str("kind"), subject = r.Str("subject"),
                bookBalance = r.Dec("book_balance"), subledgerBalance = r.Dec("subledger_balance"),
                difference = r.Dec("book_balance") - r.Dec("subledger_balance"), status = r.Str("status"),
                note = r.Str("note"), checkedBy = r.Str("checked_by"), checkedAt = r.Dt("checked_at")
            });
        return Results.Ok(rows);
    }

    private static async Task<IResult> SaveReconciliation(ReconciliationRequest req, ClaimsPrincipal user, Database db)
    {
        var period = NormalizePeriod(req.Period);
        await EnsurePeriod(db, period);
        if (!new[] { "Receivable", "Payable", "Bank", "Inventory" }.Contains(req.Kind))
            return Results.BadRequest(new { message = "Loại đối chiếu không hợp lệ." });
        var difference = req.BookBalance - req.SubledgerBalance;
        var status = Math.Abs(difference) < 0.01m ? "Matched" :
            req.Status == "Investigating" ? "Investigating" : "Unmatched";
        await using var conn = await db.OpenAsync();
        var id = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO core_reconciliations
                (id,period,kind,subject,book_balance,subledger_balance,status,note,checked_by,checked_at)
            VALUES (@id,@period,@kind,@subject,@book,@sub,@status,@note,@by,CURRENT_TIMESTAMP)
            ON CONFLICT (period,kind,subject) DO UPDATE SET
                book_balance=@book,subledger_balance=@sub,status=@status,note=@note,
                checked_by=@by,checked_at=CURRENT_TIMESTAMP
            """)
            .With("@id", id).With("@period", period).With("@kind", req.Kind)
            .With("@subject", string.IsNullOrWhiteSpace(req.Subject) ? "Tổng hợp" : req.Subject.Trim())
            .With("@book", req.BookBalance).With("@sub", req.SubledgerBalance).With("@status", status)
            .With("@note", (req.Note ?? "").Trim()).With("@by", user.Username()).ExecuteNonQueryAsync();
        await db.RecordAudit(user.Username(), "Đối chiếu sổ kế toán", "CoreReconciliation",
            $"{req.Kind} · {period}", $"Chênh lệch {difference:N0}");
        return Results.Ok(new { status, difference });
    }

    private static async Task<IResult> GetBudgets(string? period, Database db)
    {
        period = NormalizePeriod(period);
        await using var conn = await db.OpenAsync();
        var rows = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT b.id,b.period,b.account_code,a.name account_name,b.department,b.amount,b.updated_by,b.updated_at,
                   COALESCE(x.actual,0) actual
            FROM core_budgets b
            JOIN core_accounts a ON a.code=b.account_code
            LEFT JOIN (
                SELECT l.account_code,SUM(l.debit-l.credit) actual
                FROM core_journal_lines l JOIN core_journal_entries e ON e.id=l.entry_id
                WHERE e.status='Posted' AND to_char(e.entry_date,'YYYY-MM')=@p
                GROUP BY l.account_code
            ) x ON x.account_code=b.account_code
            WHERE b.period=@p ORDER BY b.account_code,b.department
            """).With("@p", period).ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new
            {
                id = r.Guid("id"), period = r.Str("period"), accountCode = r.Str("account_code"),
                accountName = r.Str("account_name"), department = r.Str("department"), amount = r.Dec("amount"),
                actual = r.Dec("actual"), variance = r.Dec("amount") - r.Dec("actual"),
                updatedBy = r.Str("updated_by"), updatedAt = r.Dt("updated_at")
            });
        return Results.Ok(rows);
    }

    private static async Task<IResult> SaveBudget(BudgetRequest req, ClaimsPrincipal user, Database db)
    {
        var period = NormalizePeriod(req.Period);
        await EnsurePeriod(db, period);
        if (req.Amount < 0) return Results.BadRequest(new { message = "Ngân sách không được âm." });
        await using var conn = await db.OpenAsync();
        var id = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO core_budgets (id,period,account_code,department,amount,updated_by)
            VALUES (@id,@period,@account,@department,@amount,@by)
            ON CONFLICT (period,account_code,department) DO UPDATE SET
                amount=@amount,updated_by=@by,updated_at=CURRENT_TIMESTAMP
            """).With("@id", id).With("@period", period).With("@account", req.AccountCode.Trim())
            .With("@department", string.IsNullOrWhiteSpace(req.Department) ? "Toàn công ty" : req.Department.Trim())
            .With("@amount", req.Amount).With("@by", user.Username()).ExecuteNonQueryAsync();
        await db.RecordAudit(user.Username(), "Cập nhật ngân sách", "CoreBudget",
            $"{period} · {req.AccountCode}", $"{req.Amount:N0}");
        return Results.Ok(new { id });
    }

    private static IResult GetAutomation() => Results.Ok(new[]
    {
        new { module = "Sales", name = "Bán hàng", debit = "131", credit = "511", trigger = "Phiếu xuất kho đã phát hành" },
        new { module = "Purchases", name = "Mua hàng", debit = "156, 1331", credit = "331", trigger = "Hóa đơn mua được duyệt" },
        new { module = "Inventory", name = "Kho", debit = "632", credit = "156", trigger = "Phiếu xuất kho đã phát hành" },
        new { module = "Payroll", name = "Lương", debit = "642", credit = "334", trigger = "Phiếu lương/chi lương hoàn tất" },
        new { module = "Assets", name = "Tài sản", debit = "642", credit = "214", trigger = "Chạy khấu hao tháng" }
    });

    private static async Task<IResult> RunAutomation(AutomationRequest req, ClaimsPrincipal user, Database db)
    {
        var period = NormalizePeriod(req.Period);
        await EnsurePeriod(db, period);
        await using var conn = await db.OpenAsync();
        if (!await IsPeriodOpen(conn, period))
            return Results.Conflict(new { message = $"Kỳ {period} đã khóa, không thể tự động định khoản." });

        var created = 0;
        var modules = string.IsNullOrWhiteSpace(req.Module)
            ? new[] { "Sales", "Inventory", "Cash", "Payroll" }
            : new[] { req.Module.Trim() };

        foreach (var module in modules)
        {
            if (module is "Sales" or "Inventory")
            {
                await using var r = await conn.Cmd("""
                    SELECT d.id,d.doc_date,d.voucher_no,d.customer_name,d.content,
                           COALESCE(SUM(l.quantity*l.unit_price),0) amount
                    FROM documents d JOIN document_lines l ON l.document_id=d.id
                    WHERE d.cancelled_at IS NULL AND d.issued_at IS NOT NULL
                      AND d.document_type='document' AND to_char(d.doc_date,'YYYY-MM')=@period
                      AND NOT EXISTS (
                        SELECT 1 FROM core_journal_entries e
                        WHERE e.source_module=@module AND e.source_id=d.id::text)
                    GROUP BY d.id ORDER BY d.doc_date
                    """).With("@period", period).With("@module", module).ExecuteReaderAsync();
                var sources = new List<AutoSource>();
                while (await r.ReadAsync())
                    sources.Add(new AutoSource(r.Guid("id").ToString(), r.DateOnly("doc_date"),
                        r.Str("voucher_no"), r.Str("customer_name"), r.Str("content"), r.Dec("amount")));
                await r.CloseAsync();
                foreach (var source in sources)
                {
                    if (source.Amount <= 0) continue;
                    var debit = module == "Sales" ? "131" : "632";
                    var credit = module == "Sales" ? "511" : "156";
                    if (await InsertAutomaticEntry(conn, source, module, debit, credit, user.Username())) created++;
                }
            }
            else if (module == "Cash")
            {
                await using var r = await conn.Cmd("""
                    SELECT p.id,p.pay_date,p.customer_name,p.note,p.amount
                    FROM payments p
                    WHERE to_char(p.pay_date,'YYYY-MM')=@period
                      AND NOT EXISTS (
                        SELECT 1 FROM core_journal_entries e
                        WHERE e.source_module='Cash' AND e.source_id=p.id::text)
                    ORDER BY p.pay_date
                    """).With("@period", period).ExecuteReaderAsync();
                var sources = new List<AutoSource>();
                while (await r.ReadAsync())
                    sources.Add(new AutoSource(r.Guid("id").ToString(), r.DateOnly("pay_date"), "",
                        r.Str("customer_name"), r.Str("note"), r.Dec("amount")));
                await r.CloseAsync();
                foreach (var source in sources)
                    if (source.Amount > 0 && await InsertAutomaticEntry(conn, source, "Cash", "111", "131", user.Username())) created++;
            }
            else if (module == "Payroll")
            {
                var exists = Convert.ToBoolean(await conn.Cmd("SELECT to_regclass('public.hr_payout_vouchers') IS NOT NULL")
                    .ExecuteScalarAsync() ?? false);
                if (!exists) continue;
                await using var r = await conn.Cmd("""
                    SELECT v.id,COALESCE(v.paid_at::date,v.created_at::date) entry_date,v.voucher_no,
                           COALESCE(e.full_name,'') employee_name,v.reason,v.amount
                    FROM hr_payout_vouchers v
                    LEFT JOIN hr_employees e ON e.id=v.employee_id
                    LEFT JOIN hr_payout_categories c ON c.id=v.category_id
                    WHERE v.status='Paid' AND c.code='salary'
                      AND to_char(COALESCE(v.paid_at,v.created_at),'YYYY-MM')=@period
                      AND NOT EXISTS (
                        SELECT 1 FROM core_journal_entries j
                        WHERE j.source_module='Payroll' AND j.source_id=v.id::text)
                    ORDER BY entry_date
                    """).With("@period", period).ExecuteReaderAsync();
                var sources = new List<AutoSource>();
                while (await r.ReadAsync())
                    sources.Add(new AutoSource(r.Guid("id").ToString(), r.DateOnly("entry_date"),
                        r.Str("voucher_no"), r.Str("employee_name"), r.Str("reason"), r.Dec("amount")));
                await r.CloseAsync();
                foreach (var source in sources)
                    if (source.Amount > 0 && await InsertAutomaticEntry(conn, source, "Payroll", "642", "111", user.Username())) created++;
            }
        }

        await db.RecordAudit(user.Username(), "Chạy tự động định khoản", "CoreAutomation", period,
            $"Phân hệ: {string.Join(", ", modules)} · Tạo {created} bút toán.");
        return Results.Ok(new { created, period });
    }

    private static async Task<bool> InsertAutomaticEntry(NpgsqlConnection conn, AutoSource source,
        string module, string debitAccount, string creditAccount, string username)
    {
        var id = Guid.NewGuid();
        await using var tx = await conn.BeginTransactionAsync();
        var no = await NextEntryNo(conn, tx, source.Date);
        var description = string.IsNullOrWhiteSpace(source.Description)
            ? $"Tự động định khoản {module}" : source.Description;
        var inserted = await TxCmd(conn, tx, """
            INSERT INTO core_journal_entries
                (id,entry_no,entry_date,description,reference,source_module,source_id,status,created_by,posted_by,posted_at)
            VALUES (@id,@no,@date,@description,@reference,@module,@source,'Posted',@by,@by,CURRENT_TIMESTAMP)
            ON CONFLICT (source_module,source_id) WHERE source_id <> '' DO NOTHING
            """).With("@id", id).With("@no", no).With("@date", source.Date)
            .With("@description", description).With("@reference", source.Reference)
            .With("@module", module).With("@source", source.Id).With("@by", username)
            .ExecuteNonQueryAsync();
        if (inserted == 0)
        {
            await tx.RollbackAsync();
            return false;
        }
        foreach (var (account, debit, credit, lineNo) in new[]
                 {
                     (debitAccount, source.Amount, 0m, 1),
                     (creditAccount, 0m, source.Amount, 2)
                 })
        {
            await TxCmd(conn, tx, """
                INSERT INTO core_journal_lines
                    (entry_id,line_no,account_code,description,debit,credit,partner)
                VALUES (@entry,@line,@account,@description,@debit,@credit,@partner)
                """).With("@entry", id).With("@line", lineNo).With("@account", account)
                .With("@description", description).With("@debit", debit).With("@credit", credit)
                .With("@partner", source.Partner).ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return true;
    }

    private static async Task<(int total, int draft, int posted)> ReadEntryCounts(NpgsqlConnection conn, string period)
    {
        await using var r = await conn.Cmd("""
            SELECT COUNT(*) total,
                   COUNT(*) FILTER (WHERE status='Draft') draft,
                   COUNT(*) FILTER (WHERE status='Posted') posted
            FROM core_journal_entries WHERE to_char(entry_date,'YYYY-MM')=@period
            """).With("@period", period).ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.Int("total"), r.Int("draft"), r.Int("posted"));
    }

    private static object EntrySummary(NpgsqlDataReader r, bool detailed = false) => new
    {
        id = r.Guid("id"), entryNo = r.Str("entry_no"), entryDate = r.DateOnly("entry_date"),
        description = r.Str("description"), reference = r.Str("reference"),
        sourceModule = r.Str("source_module"), status = r.Str("status"), total = r.Dec("total"),
        createdBy = detailed ? r.Str("created_by") : "",
        createdAt = detailed ? r.Dt("created_at") : default,
        postedBy = detailed ? r.Str("posted_by") : "",
        postedAt = detailed ? r.DtNull("posted_at") : null
    };

    private static string NormalizePeriod(string? value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM", out var parsed))
            return parsed.ToString("yyyy-MM");
        var now = DateTime.UtcNow.AddHours(7);
        return $"{now.Year:D4}-{now.Month:D2}";
    }

    private static async Task EnsurePeriod(Database db, string period)
    {
        if (!DateOnly.TryParseExact(period, "yyyy-MM", out var start)) return;
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO core_periods (period,starts_on,ends_on)
            VALUES (@p,@start,@end) ON CONFLICT (period) DO NOTHING
            """).With("@p", period).With("@start", start).With("@end", start.AddMonths(1).AddDays(-1))
            .ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsPeriodOpen(NpgsqlConnection conn, string period)
        => string.Equals(Convert.ToString(await conn.Cmd("SELECT status FROM core_periods WHERE period=@p")
            .With("@p", period).ExecuteScalarAsync()), "Open", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> NextEntryNo(NpgsqlConnection conn, NpgsqlTransaction tx, DateOnly date)
    {
        var prefix = $"BT{date:yyyyMM}-";
        var value = await TxCmd(conn, tx, """
            SELECT COALESCE(MAX(NULLIF(regexp_replace(entry_no,'^.*-','','g'),'')::bigint),0)+1
            FROM core_journal_entries WHERE entry_no LIKE @prefix
            """).With("@prefix", prefix + "%").ExecuteScalarAsync();
        return prefix + Convert.ToInt64(value ?? 1L).ToString("D5");
    }

    private static NpgsqlCommand TxCmd(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        var command = conn.Cmd(sql);
        command.Transaction = tx;
        return command;
    }

    private static async Task RecordPeriodEvent(NpgsqlConnection conn, string period, string action,
        string reason, string username)
    {
        await conn.Cmd("""
            INSERT INTO core_period_events(period,action,reason,username)
            VALUES (@p,@action,@reason,@by)
            """).With("@p", period).With("@action", action).With("@reason", reason).With("@by", username)
            .ExecuteNonQueryAsync();
    }

    private sealed record AutoSource(string Id, DateOnly Date, string Reference, string Partner,
        string Description, decimal Amount);

    public sealed record AccountRequest(string? Code, string? Name, string? Type, string? NormalSide,
        string? ParentCode, bool IsActive = true);
    public sealed record JournalLineRequest(string AccountCode, string? Description, decimal Debit,
        decimal Credit, string? Partner, string? CostCenter);
    public sealed record JournalEntryRequest(DateOnly EntryDate, string Description, string? Reference,
        string? SourceModule, string? SourceId, List<JournalLineRequest> Lines);
    public sealed record ReopenPeriodRequest(string? Reason);
    public sealed record ReconciliationRequest(string? Period, string Kind, string Subject,
        decimal BookBalance, decimal SubledgerBalance, string? Status, string? Note);
    public sealed record BudgetRequest(string? Period, string AccountCode, string? Department, decimal Amount);
    public sealed record AutomationRequest(string? Period, string? Module);
}
