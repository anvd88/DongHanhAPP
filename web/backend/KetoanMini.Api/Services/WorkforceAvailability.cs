using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Services;

/// <summary>
/// "Hôm nay ai đang có mặt để nhận việc?" — một nguồn sự thật DUY NHẤT cho mọi ô chọn nhân viên.
///
/// Vì sao phải có lớp này: giao việc cho người đang nghỉ phép hoặc chưa tới công ty thì việc nằm im
/// tới chiều, còn người giao tưởng đã xong. Trước đây ô chọn liệt kê toàn bộ nhân sự đang làm việc
/// nên không có cách nào biết điều đó ngay lúc chọn.
///
/// Chốt Ở MÁY CHỦ (chứ không lọc trên giao diện) vì có hai máy khách — web và app native — cùng gọi
/// một API; lọc ở client thì chỉ cần một bản app cũ là lại giao được cho người đang nghỉ.
///
/// KHÔNG XOÁ người khỏi danh sách: trả về cả những người không chọn được kèm
/// <see cref="WorkAvailability.Label"/> để giao diện hiện chú thích ("Chưa chấm công", "Đang nghỉ
/// phép"). Danh sách ngắn đi một cách bí ẩn khiến người giao tưởng nhân viên đã bị xoá tài khoản.
/// </summary>
public static class WorkforceAvailability
{
    // Trạng thái — cũng là khoá mà giao diện dùng để chọn màu.
    public const string StatusPresent = "present";
    public const string StatusAbsent = "absent";
    public const string StatusLeave = "leave";
    public const string StatusSick = "sick";
    public const string StatusTrip = "trip";

    /// <summary>Kết quả cho một người trong ngày hôm nay.</summary>
    /// <param name="Status">Một trong các hằng <c>Status*</c> ở trên.</param>
    /// <param name="Label">Chú thích hiển thị ngay cạnh tên trong ô chọn.</param>
    /// <param name="Selectable">FALSE ⇒ không được giao việc cho người này lúc này.</param>
    public sealed record WorkAvailability(string Status, string Label, bool Selectable);

    /// <summary>Người không tra được (không hồ sơ, không dòng chấm công) mặc định là CHƯA chấm công.</summary>
    public static readonly WorkAvailability Unknown = new(StatusAbsent, "Chưa chấm công", false);

    /// <summary>
    /// Tra trạng thái của một danh sách tài khoản. Khoá của Dictionary so sánh không phân biệt hoa
    /// thường, đúng cách mọi nơi khác trong hệ thống đối chiếu username.
    ///
    /// Một truy vấn cho cả danh sách chứ không mỗi người một lần: ô chọn nhân viên của một công ty
    /// vài trăm người mà hỏi từng dòng thì mở form đã mất vài giây.
    /// </summary>
    public static async Task<Dictionary<string, WorkAvailability>> ForUsersAsync(
        NpgsqlConnection conn, IReadOnlyCollection<string> usernames,
        NpgsqlTransaction? tx = null, CancellationToken ct = default)
    {
        var result = new Dictionary<string, WorkAvailability>(StringComparer.OrdinalIgnoreCase);
        var keys = usernames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0) return result;

        var todayLocal = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTimeZone));
        var todayText = todayLocal.ToString("yyyy-MM-dd");

        // Ngày trong payload đơn từ là chuỗi ISO "yyyy-MM-dd" nên SO SÁNH BẰNG CHUỖI: ép kiểu ::date
        // sẽ làm hỏng cả truy vấn nếu chỉ một đơn cũ có ngày rỗng/sai định dạng, mà thứ tự chuỗi của
        // ISO trùng đúng thứ tự ngày.
        const string sql = """
            WITH people AS (
                SELECT lower(u) AS username FROM unnest(@users) AS u
            ), att AS (
                SELECT lower(l.username) AS username,
                       MIN(l.occurred_at) FILTER (WHERE l.loai='Vào') AS first_in,
                       MAX(l.occurred_at) FILTER (WHERE l.loai='Ra')  AS last_out
                FROM hr_effective_attendance_log l
                WHERE l.logical_work_date = @today AND lower(l.username) = ANY(@users)
                GROUP BY lower(l.username)
            ), absence AS (
                SELECT DISTINCT ON (lower(e.username))
                       lower(e.username) AS username, r.req_type, r.status
                FROM hr_requests r
                JOIN hr_employees e ON e.id = r.employee_id
                WHERE r.status IN ('Approved','Pending')
                  AND r.req_type IN ('leave','sick','business_trip')
                  AND lower(e.username) = ANY(@users)
                  AND COALESCE(NULLIF(r.payload->>'fromDate',''), '9999-99-99') <= @todayText
                  AND COALESCE(NULLIF(r.payload->>'toDate',''),
                               NULLIF(r.payload->>'fromDate',''), '0000-00-00') >= @todayText
                -- Đơn ĐÃ DUYỆT thắng đơn còn chờ; cùng hạng thì lấy đơn nộp sau cùng.
                ORDER BY lower(e.username), (r.status='Approved') DESC, r.created_at DESC
            )
            SELECT p.username, a.first_in, a.last_out,
                   COALESCE(ab.req_type,'') AS req_type, COALESCE(ab.status,'') AS req_status
            FROM people p
            LEFT JOIN att a ON a.username = p.username
            LEFT JOIN absence ab ON ab.username = p.username
            """;

        var cmd = (tx is null ? conn.Cmd(sql) : conn.Cmd(sql, tx))
            .With("@users", keys)
            .With("@today", todayLocal)
            .With("@todayText", todayText);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var username = r.Str("username");
            result[username] = Classify(
                r.DtNull("first_in"), r.DtNull("last_out"), r.Str("req_type"), r.Str("req_status"));
        }
        return result;
    }

    /// <summary>Tra đúng một người — dùng ở chốt máy chủ lúc lưu (máy khách có thể là bản cũ).</summary>
    public static async Task<WorkAvailability> ForUserAsync(
        NpgsqlConnection conn, string username, NpgsqlTransaction? tx = null, CancellationToken ct = default)
    {
        var map = await ForUsersAsync(conn, [username], tx, ct);
        return map.TryGetValue(username.Trim(), out var found) ? found : Unknown;
    }

    /// <summary>
    /// Thứ tự ưu tiên của các lý do, từ dứt khoát nhất xuống: đang nghỉ (đã duyệt) → đang công tác →
    /// đã chấm công → chưa chấm công. Người nghỉ phép cũng là người "chưa chấm công", nhưng nói
    /// "Đang nghỉ phép" thì người giao việc hiểu ngay, còn nói "chưa chấm công" thì họ sẽ đi hỏi lại.
    /// </summary>
    private static WorkAvailability Classify(
        DateTime? firstIn, DateTime? lastOut, string reqType, string reqStatus)
    {
        var approved = string.Equals(reqStatus, "Approved", StringComparison.Ordinal);
        if (approved && reqType is "leave" or "sick")
            return new WorkAvailability(
                reqType == "sick" ? StatusSick : StatusLeave,
                reqType == "sick" ? "Đang nghỉ ốm (đơn đã duyệt)" : "Đang nghỉ phép (đơn đã duyệt)",
                false);

        // Đi công tác VẪN LÀM VIỆC, chỉ là không có mặt ở công ty để chấm công — vẫn giao việc được.
        if (approved && reqType == "business_trip")
            return new WorkAvailability(StatusTrip, "Đang công tác", true);

        // Đơn nghỉ CHƯA DUYỆT không phải là nghỉ: chỉ ghi thêm vào chú thích để người giao cân nhắc.
        var pendingLeave = string.Equals(reqStatus, "Pending", StringComparison.Ordinal)
                           && reqType is "leave" or "sick";

        if (firstIn is { } inAt)
        {
            var text = $"Đã chấm công vào {LocalHm(inAt)}";
            if (lastOut is { } outAt && outAt > inAt) text += $" · ra {LocalHm(outAt)}";
            if (pendingLeave) text += " · có đơn nghỉ chờ duyệt";
            return new WorkAvailability(StatusPresent, text, true);
        }

        return new WorkAvailability(StatusAbsent,
            pendingLeave ? "Chưa chấm công · có đơn nghỉ chờ duyệt" : "Chưa chấm công", false);
    }

    private static string LocalHm(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTimeZone)
            .ToString("HH:mm");

    private static readonly TimeZoneInfo VietnamTimeZone = LoadVietnamTimeZone();

    private static TimeZoneInfo LoadVietnamTimeZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Bangkok" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* nền tảng khác đặt tên khác */ }
        }
        return TimeZoneInfo.Local;
    }
}
