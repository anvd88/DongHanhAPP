using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Nhật ký hệ thống (audit) — Đợt 1, nhiệm vụ 2. Chuẩn hóa API /api/audit:
///  • Phân trang (page/pageSize) + tương thích tham số cũ (take).
///  • Lọc theo người dùng, hành động, đối tượng, THÁNG (yyyy-MM), NHÓM NGHIỆP VỤ và khoảng thời gian.
///  • Trả nội dung TRƯỚC/SAU khi thay đổi (nếu có) ở định dạng an toàn (đã che bí mật).
///  • Che dữ liệu nhạy cảm (mật khẩu, token, hash, embedding…) trong mọi trường trả về.
///  • Xuất CSV / Excel áp dụng đúng bộ lọc đang xem.
///
/// QUYỀN XEM (<see cref="ResolveScopeAsync"/>): Admin xem toàn bộ. Kế toán (role Accounting + thuộc phòng
/// ban is_accounting) chỉ xem được PHẦN TIỀN — <see cref="MoneyEntities"/> — và phạm vi này do SERVER ép,
/// không phụ thuộc tham số client gửi lên. Mọi tài khoản khác bị từ chối. Nới thêm nhóm nào thì sửa đúng
/// một chỗ là <see cref="ResolveScopeAsync"/>.
/// </summary>
public static class AuditEndpoints
{
    /// <summary>Đối tượng thuộc "phần tiền" mà phòng kế toán được tra cứu.</summary>
    private static readonly string[] MoneyEntities = { "PayoutVoucher", "PenaltyRefund" };

    /// <summary>
    /// Nhóm nghiệp vụ → danh sách entity, để người dùng lọc theo việc thay vì phải nhớ tên kỹ thuật.
    /// Tên nhóm phải khớp với danh sách bày ở frontend (pages/SaoLuu.tsx).
    /// </summary>
    private static readonly Dictionary<string, string[]> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["payout"] = new[] { "PayoutVoucher", "PenaltyRefund" },
        ["payroll"] = new[] { "Payslip", "Salary", "Contract", "LeaveBalance", "BankAccount" },
        ["penalty"] = new[] { "Penalty", "PenaltyRefund" },
        ["attendance"] = new[] { "ChamCong", "Shift", "ShiftAssignment", "Holiday" },
        ["request"] = new[] { "Request" },
        ["hr"] = new[] { "Employee", "Department" },
        ["auth"] = new[] { "Auth", "User" },
        ["system"] = new[] { "AppConfig", "Release", "Feedback", "PortalPost", "PortalAbout", "GiaCong" },
    };

    private enum AuditScope { Denied, MoneyOnly, Full }

    /// <summary>Ai được xem gì. Kế toán chỉ tra cứu được phần tiền; ngoài admin & kế toán thì cấm.</summary>
    private static async Task<AuditScope> ResolveScopeAsync(NpgsqlConnection conn, ClaimsPrincipal u)
    {
        if (u.Can(Permissions.CompanyScopeAll)) return AuditScope.Full;
        if (await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return AuditScope.MoneyOnly;
        return AuditScope.Denied;
    }

    /// <summary>
    /// Danh sách entity được phép xem sau khi giao cắt yêu cầu của client với quyền thật.
    /// Trả null = không giới hạn (admin, không chọn nhóm/đối tượng).
    /// </summary>
    private static string[]? AllowedEntities(AuditScope scope, string? group, string? entity)
    {
        // Kế toán: luôn ép về phần tiền TRƯỚC, rồi mới giao với lựa chọn của họ.
        var allowed = scope == AuditScope.MoneyOnly ? MoneyEntities : null;

        if (!string.IsNullOrWhiteSpace(group) && Groups.TryGetValue(group.Trim(), out var groupEntities))
            allowed = allowed is null ? groupEntities : allowed.Intersect(groupEntities, StringComparer.OrdinalIgnoreCase).ToArray();

        if (!string.IsNullOrWhiteSpace(entity))
        {
            var one = new[] { entity.Trim() };
            allowed = allowed is null ? one : allowed.Intersect(one, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // Giao cắt rỗng (vd kế toán cố lọc entity=Auth) → mảng rỗng ⇒ không trả dòng nào.
        return allowed;
    }
    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        // Bảng audit_logs tạo ở PostgresSchema; ở đây bổ sung cột trước/sau (tùy chọn) + chỉ mục lọc.
        await conn.Cmd("""
            ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS before_data jsonb NULL;
            ALTER TABLE audit_logs ADD COLUMN IF NOT EXISTS after_data jsonb NULL;
            CREATE INDEX IF NOT EXISTS ix_audit_logs_username ON audit_logs (username, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_entity ON audit_logs (entity, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_action ON audit_logs (action, occurred_at DESC);
            -- Màn hình mặc định (mới nhất trước) và lọc theo tháng KHÔNG kèm người/đối tượng nên ba index
            -- trên đều không dùng được; đo trên 1,2 triệu dòng: 135ms → 0,1ms khi có index riêng cho thời gian.
            CREATE INDEX IF NOT EXISTS ix_audit_logs_time ON audit_logs (occurred_at DESC);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapAudit(this WebApplication app)
    {
        // Cửa vào chốt bằng quyền audit.read (admin + kế toán có). PHẠM VI thì hẹp hơn quyền: kế toán
        // CHỈ thấy phần tiền, và phạm vi đó do server ép trong từng handler (cần tra DB để biết phòng
        // ban) — xem ResolveScopeAsync. Quyền mở cửa, phạm vi mới quyết định thấy được gì.
        var g = app.MapGroup("/api/audit").RequirePermission(Permissions.AuditRead);

        // Danh sách có phân trang + lọc. Trả envelope { items, total, page, pageSize }.
        g.MapGet("/", async (Database db, ClaimsPrincipal u, HttpRequest http,
            int? page, int? pageSize, int? take, string? search,
            string? username, string? action, string? entity, string? group,
            string? month, string? from, string? to) =>
        {
            await using var conn = await db.OpenAsync();
            var scope = await ResolveScopeAsync(conn, u);
            if (scope == AuditScope.Denied) return Results.Forbid();

            var f = AuditFilter.Build(search, username, action, AllowedEntities(scope, group, entity), month, from, to);

            // Tương thích tham số cũ (?take=N): trả về trang đầu với kích thước = take.
            int size = pageSize is > 0 ? Math.Clamp(pageSize.Value, 1, 200)
                     : take is > 0 ? Math.Clamp(take.Value, 1, 1000)
                     : 50;
            int p = page is > 0 ? page.Value : 1;
            int offset = (p - 1) * size;

            var total = Convert.ToInt64(await conn.Cmd(
                $"SELECT COUNT(*) FROM audit_logs {f.Where}").Apply(f).ExecuteScalarAsync());

            var items = new List<AuditItemDto>();
            await using (var r = await conn.Cmd(
                $@"SELECT id, occurred_at, username, action, entity, entity_name, details,
                          before_data::text AS before_data, after_data::text AS after_data
                   FROM audit_logs {f.Where}
                   ORDER BY occurred_at DESC, id DESC
                   LIMIT @__limit OFFSET @__offset")
                .Apply(f).With("@__limit", size).With("@__offset", offset).ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) items.Add(ReadItem(r));
            }

            return Results.Ok(new AuditPageDto(items, total, p, size));
        });

        // Giá trị lọc gợi ý cho giao diện (hành động & đối tượng đã dùng). Nhẹ, có giới hạn.
        // Kế toán chỉ nhận gợi ý trong phần tiền — tránh lộ tên hành động của nghiệp vụ họ không được xem.
        g.MapGet("/filters", async (Database db, ClaimsPrincipal u) =>
        {
            await using var conn = await db.OpenAsync();
            var scope = await ResolveScopeAsync(conn, u);
            if (scope == AuditScope.Denied) return Results.Forbid();
            var limitTo = scope == AuditScope.MoneyOnly ? MoneyEntities : null;

            var actions = await Distinct(conn, "action", limitTo);
            var entities = await Distinct(conn, "entity", limitTo);
            var groups = (scope == AuditScope.MoneyOnly ? new[] { "payout" } : Groups.Keys.ToArray())
                .Select(k => new { key = k, label = GroupLabel(k) });
            // Các tháng thật sự có nhật ký → giao diện chỉ chào những tháng có dữ liệu.
            var months = await MonthsAsync(conn, limitTo);
            return Results.Ok(new { actions, entities, groups, months, canSeeAll = scope == AuditScope.Full });
        });

        // Xuất CSV / Excel theo đúng bộ lọc hiện tại (không phân trang; có trần dòng an toàn).
        g.MapGet("/export", async (Database db, ClaimsPrincipal u,
            string? format, string? search,
            string? username, string? action, string? entity, string? group,
            string? month, string? from, string? to) =>
        {
            await using var conn = await db.OpenAsync();
            var scope = await ResolveScopeAsync(conn, u);
            if (scope == AuditScope.Denied) return Results.Forbid();

            var f = AuditFilter.Build(search, username, action, AllowedEntities(scope, group, entity), month, from, to);
            var rows = new List<AuditItemDto>();
            await using (var r = await conn.Cmd(
                $@"SELECT id, occurred_at, username, action, entity, entity_name, details,
                          before_data::text AS before_data, after_data::text AS after_data
                   FROM audit_logs {f.Where}
                   ORDER BY occurred_at DESC, id DESC
                   LIMIT @__limit").Apply(f).With("@__limit", ExportRowCap).ExecuteReaderAsync())
            {
                while (await r.ReadAsync()) rows.Add(ReadItem(r));
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
            if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.File(BuildXlsx(rows),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"NhatKy_{stamp}.xlsx");

            return Results.File(BuildCsv(rows), "text/csv; charset=utf-8", $"NhatKy_{stamp}.csv");
        });
    }

    // Trần số dòng khi xuất — chặn OOM nếu nhật ký quá lớn.
    private const int ExportRowCap = 50_000;

    private static AuditItemDto ReadItem(NpgsqlDataReader r) => new(
        r.Long("id"),
        r.Dt("occurred_at"),
        r.Str("username"),
        r.Str("action"),
        r.Str("entity"),
        r.Str("entity_name"),
        SensitiveMask.Text(r.Str("details")),
        SensitiveMask.Json(r.Str("before_data")),
        SensitiveMask.Json(r.Str("after_data")));

    /// <param name="limitTo">null = mọi entity; khác null = chỉ thống kê trong các entity này.</param>
    private static async Task<List<string>> Distinct(NpgsqlConnection conn, string col, string[]? limitTo)
    {
        var list = new List<string>();
        await using var r = await conn.Cmd(
            $"""
             SELECT DISTINCT {col} FROM audit_logs
             WHERE {col} <> '' AND (@limit::text[] IS NULL OR entity = ANY(@limit))
             ORDER BY {col} LIMIT 200
             """)
            .With("@limit", (object?)limitTo ?? DBNull.Value).ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.Str(0));
        return list;
    }

    /// <summary>
    /// Các tháng "yyyy-MM" để chào trong ô lọc, mới nhất trước — suy ra từ MỐC ĐẦU và MỐC CUỐI của nhật ký
    /// (hai lần tra index) thay vì DISTINCT to_char(...) quét cả bảng: đo trên 1,2 triệu dòng là 1080ms → 0,1ms,
    /// mà truy vấn này chạy mỗi lần mở trang. Đổi lại danh sách là dải tháng LIÊN TỤC, nên về lý thuyết có thể
    /// chào một tháng rỗng (nhật ký thưa) — chọn trúng thì chỉ hiện "không có bản ghi", không hại gì.
    /// </summary>
    private static async Task<List<string>> MonthsAsync(NpgsqlConnection conn, string[]? limitTo)
    {
        var list = new List<string>();
        await using var r = await conn.Cmd(
            """
            SELECT MIN(occurred_at) AS lo, MAX(occurred_at) AS hi FROM audit_logs
            WHERE (@limit::text[] IS NULL OR entity = ANY(@limit))
            """)
            .With("@limit", (object?)limitTo ?? DBNull.Value).ExecuteReaderAsync();
        if (!await r.ReadAsync() || r.IsDBNull(0) || r.IsDBNull(1)) return list;

        var lo = r.GetDateTime(0).ToLocalTime();
        var hi = r.GetDateTime(1).ToLocalTime();
        var cursor = new DateTime(hi.Year, hi.Month, 1);
        var first = new DateTime(lo.Year, lo.Month, 1);
        while (cursor >= first && list.Count < 120)
        {
            list.Add($"{cursor.Year:D4}-{cursor.Month:D2}");
            cursor = cursor.AddMonths(-1);
        }
        return list;
    }

    private static string GroupLabel(string key) => key switch
    {
        "payout" => "Thu chi tiền mặt",
        "payroll" => "Lương & phúc lợi",
        "penalty" => "Kỷ luật",
        "attendance" => "Chấm công",
        "request" => "Đơn từ",
        "hr" => "Nhân sự",
        "auth" => "Đăng nhập & tài khoản",
        "system" => "Hệ thống",
        _ => key,
    };

    // ---------- Xuất tệp ----------

    private static byte[] BuildCsv(List<AuditItemDto> rows)
    {
        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM để Excel đọc đúng tiếng Việt.
        sb.AppendLine("Thời gian,Người dùng,Hành động,Đối tượng,Tên đối tượng,Chi tiết,Trước,Sau");
        foreach (var x in rows)
            sb.Append(Csv(x.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(x.Username)).Append(',').Append(Csv(x.Action)).Append(',')
              .Append(Csv(x.Entity)).Append(',').Append(Csv(x.EntityName)).Append(',')
              .Append(Csv(x.Details)).Append(',').Append(Csv(x.Before ?? "")).Append(',')
              .Append(Csv(x.After ?? "")).Append("\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Csv(string v)
    {
        v = v.Replace("\"", "\"\"");
        return $"\"{v}\"";
    }

    private static byte[] BuildXlsx(List<AuditItemDto> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("NhatKy");
        string[] headers = { "Thời gian", "Người dùng", "Hành động", "Đối tượng", "Tên đối tượng", "Chi tiết", "Trước", "Sau" };
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var x in rows)
        {
            ws.Cell(row, 1).Value = x.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            ws.Cell(row, 2).Value = x.Username;
            ws.Cell(row, 3).Value = x.Action;
            ws.Cell(row, 4).Value = x.Entity;
            ws.Cell(row, 5).Value = x.EntityName;
            ws.Cell(row, 6).Value = x.Details;
            ws.Cell(row, 7).Value = x.Before ?? "";
            ws.Cell(row, 8).Value = x.After ?? "";
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public record AuditItemDto(long Id, DateTime OccurredAt, string Username, string Action,
        string Entity, string EntityName, string Details, string? Before, string? After);
    public record AuditPageDto(IReadOnlyList<AuditItemDto> Items, long Total, int Page, int PageSize);
}

/// <summary>Bộ lọc nhật ký + gắn tham số vào lệnh (dùng chung cho đếm, liệt kê và xuất).</summary>
internal sealed class AuditFilter
{
    public required string Where { get; init; }
    public string? Search { get; init; }
    public string? Username { get; init; }
    public string? Action { get; init; }
    /// <summary>null = không giới hạn đối tượng; mảng RỖNG = không dòng nào (quyền giao cắt ra rỗng).</summary>
    public string[]? Entities { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }

    /// <param name="month">"yyyy-MM": nếu hợp lệ thì THẮNG from/to (người dùng chọn tháng là muốn cả tháng).</param>
    public static AuditFilter Build(string? search, string? username, string? action, string[]? entities,
        string? month, string? from, string? to)
    {
        DateTime? fromDt = TryDate(from, endOfDay: false);
        DateTime? toDt = TryDate(to, endOfDay: true); // "to" bao trọn cả ngày được chọn.
        if (TryMonth(month, out var monthStart, out var monthEnd))
        {
            fromDt = monthStart;
            toDt = monthEnd;
        }
        // Cast tham số để PostgreSQL biết kiểu khi giá trị là NULL (tránh lỗi "could not determine data type").
        var where =
            @"WHERE (@search::text IS NULL OR (username ILIKE @search OR action ILIKE @search
                        OR entity ILIKE @search OR entity_name ILIKE @search OR details ILIKE @search))
                AND (@username::text IS NULL OR username ILIKE @username)
                AND (@action::text IS NULL OR action = @action)
                AND (@entities::text[] IS NULL OR entity = ANY(@entities))
                AND (@from::timestamptz IS NULL OR occurred_at >= @from)
                AND (@to::timestamptz IS NULL OR occurred_at < @to)";
        return new AuditFilter
        {
            Where = where,
            Search = Blank(search) ? null : $"%{search!.Trim()}%",
            Username = Blank(username) ? null : $"%{username!.Trim()}%",
            Action = Blank(action) ? null : action!.Trim(),
            Entities = entities,
            From = fromDt,
            To = toDt,
        };
    }

    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);

    /// <summary>"yyyy-MM" → [00:00 ngày 1, 00:00 ngày 1 tháng sau). Giờ địa phương vì người dùng nghĩ theo lịch của họ.</summary>
    private static bool TryMonth(string? month, out DateTime start, out DateTime end)
    {
        start = end = default;
        if (string.IsNullOrWhiteSpace(month)) return false;
        var parts = month.Trim().Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)
            || y is < 2000 or > 9999 || m is < 1 or > 12) return false;
        start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Local);
        end = start.AddMonths(1);
        return true;
    }

    private static DateTime? TryDate(string? s, bool endOfDay)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        // Chỉ có ngày (yyyy-MM-dd) → mốc dưới = 00:00, mốc trên = 00:00 hôm sau (bao trọn ngày).
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            var midnight = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            return endOfDay ? midnight.AddDays(1) : midnight;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }
}

internal static class AuditFilterExtensions
{
    /// <summary>Gắn các tham số lọc vào lệnh (null → DBNull, giữ nguyên ý nghĩa "@x IS NULL").</summary>
    public static NpgsqlCommand Apply(this NpgsqlCommand cmd, AuditFilter f)
        => cmd.With("@search", (object?)f.Search ?? DBNull.Value)
              .With("@username", (object?)f.Username ?? DBNull.Value)
              .With("@action", (object?)f.Action ?? DBNull.Value)
              .With("@entities", (object?)f.Entities ?? DBNull.Value)
              .With("@from", (object?)f.From ?? DBNull.Value)
              .With("@to", (object?)f.To ?? DBNull.Value);
}

/// <summary>
/// Che dữ liệu nhạy cảm trước khi trả ra ngoài: giá trị của các khóa nhạy cảm (mật khẩu, token, hash,
/// embedding…) trong JSON trước/sau, và các chuỗi bí mật lộ trong văn bản chi tiết. Chỉ để phòng thủ —
/// mã ghi nhật ký của hệ thống vốn không ghi bí mật, nhưng lớp này đảm bảo an toàn kể cả khi lỡ ghi.
/// </summary>
internal static class SensitiveMask
{
    private static readonly string[] SensitiveKeys =
    {
        "password", "pass", "pwd", "token", "secret", "hash", "embedding", "otp",
        "apikey", "api_key", "privatekey", "private_key", "signature", "cccd", "cmnd", "cardnumber", "card_number",
    };
    private const string Redacted = "***";

    private static bool IsSensitiveKey(string key)
    {
        var k = key.ToLowerInvariant();
        foreach (var s in SensitiveKeys) if (k.Contains(s)) return true;
        return false;
    }

    /// <summary>Che văn bản tự do: cặp "khóa nhạy cảm: giá trị", "Bearer xxx", và chuỗi hex/base64 rất dài.</summary>
    public static string Text(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        s = System.Text.RegularExpressions.Regex.Replace(s,
            @"(?i)\b(password|pwd|mật\s*khẩu|token|secret|api[_-]?key|hash)\b\s*[:=]\s*\S+", $"$1: {Redacted}");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)\bBearer\s+[A-Za-z0-9._\-]+", $"Bearer {Redacted}");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b[A-Za-z0-9+/]{40,}={0,2}\b", Redacted);
        return s;
    }

    /// <summary>Che JSON: đệ quy, thay giá trị của mọi khóa nhạy cảm bằng ***. Trả null nếu rỗng.</summary>
    public static string? Json(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() is "{}" or "null") return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms)) Write(doc.RootElement, w);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch { return Text(json); } // không phải JSON hợp lệ → che kiểu văn bản.
    }

    private static void Write(JsonElement el, Utf8JsonWriter w)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var prop in el.EnumerateObject())
                {
                    w.WritePropertyName(prop.Name);
                    if (IsSensitiveKey(prop.Name)) w.WriteStringValue(Redacted);
                    else Write(prop.Value, w);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in el.EnumerateArray()) Write(item, w);
                w.WriteEndArray();
                break;
            default:
                el.WriteTo(w);
                break;
        }
    }
}
