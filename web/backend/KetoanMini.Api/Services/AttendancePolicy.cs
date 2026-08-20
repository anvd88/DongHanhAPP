using KetoanMini.Api.Data;
using Npgsql;

namespace KetoanMini.Api.Services;

public sealed record AttendanceDecision(
    bool ShouldRecord,
    string Loai,
    DateTime? ExistingAt,
    string Message);

public static class AttendancePolicy
{
    public const string CheckInTypeIn = "V\u00e0o";
    public const string CheckInTypeOut = "Ra";

    /// <summary>\u0110\u1ecbnh danh m\u00fai gi\u1edd VN d\u00f9ng cho c\u00e1c truy v\u1ea5n Postgres AT TIME ZONE.</summary>
    public const string TzId = "Asia/Ho_Chi_Minh";

    private static readonly TimeSpan CheckOutStartsAt = TimeSpan.FromHours(17);

    // Lần chấm cách lần chấm ĐẦU ngày chưa tới ngần này phút ⇒ coi là bấm nhầm lặp lại của "Vào",
    // KHÔNG biến thành "Ra" (tránh tạo giờ Ra 0 phút ngay sau khi vừa vào).
    private const int MinSessionMinutes = 5;
    // Lần chấm cách lần chấm GẦN NHẤT chưa tới ngần này phút ⇒ coi như đã ghi (tránh nhân đôi dòng Ra).
    private const int MinReCheckoutMinutes = 3;

    private static readonly TimeZoneInfo VietnamTimeZone = LoadVietnamTimeZone();

    /// <summary>Quy \u0111\u1ed5i m\u1ed9t m\u1ed1c gi\u1edd VN (kh\u00f4ng k\u00e8m kind) sang UTC \u0111\u1ec3 l\u01b0u v\u00e0o timestamptz.</summary>
    public static DateTime LocalToUtc(DateTime localNaive) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localNaive, DateTimeKind.Unspecified), VietnamTimeZone);

    /// <summary>Suy ra lo\u1ea1i ch\u1ea5m c\u00f4ng (V\u00e0o/Ra) theo gi\u1edd trong ng\u00e0y: tr\u01b0\u1edbc 17h l\u00e0 V\u00e0o, c\u00f2n l\u1ea1i l\u00e0 Ra.</summary>
    public static string LoaiForLocalTime(TimeSpan localTimeOfDay) =>
        localTimeOfDay < CheckOutStartsAt ? CheckInTypeIn : CheckInTypeOut;

    public static async Task<AttendanceDecision> DecideAsync(
        NpgsqlConnection conn,
        string username,
        string displayName,
        CancellationToken ct = default,
        DateTime? atUtc = null)
    {
        // atUtc khác null khi đồng bộ chấm công ngoại tuyến: quyết định Vào/Ra và chống trùng theo
        // đúng thời điểm chấm thật (lúc mất mạng), không phải thời điểm gửi lên.
        var nowUtc = atUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, VietnamTimeZone);
        var workDay = DateOnly.FromDateTime(nowLocal);
        var overnightDay = await conn.Cmd("""
            SELECT a.work_date
            FROM hr_employees e
            JOIN hr_shift_assignments a ON a.employee_id=e.id
            JOIN hr_shifts s ON s.id=a.shift_id AND s.is_overnight=TRUE
            WHERE lower(e.username)=lower(@u)
              AND @localAt >= a.work_date + s.start_time
              AND @localAt <= (a.work_date + 1) + s.end_time
                  + make_interval(mins => s.checkout_grace_minutes)
            ORDER BY a.work_date DESC LIMIT 1
            """).With("@u", username).With("@localAt", nowLocal).ExecuteScalarAsync(ct);
        if (overnightDay is DateOnly assignedDay) workDay = assignedDay;

        // M\u00f4 h\u00ecnh chu\u1ea9n: l\u1ea7n ch\u1ea5m \u0110\u1ea6U ng\u00e0y = gi\u1edd V\u00e0o; c\u00e1c l\u1ea7n sau = gi\u1edd Ra v\u00e0 L\u1ea4Y MU\u1ed8N NH\u1ea4T. Kh\u00f4ng ph\u00e2n
        // lo\u1ea1i theo m\u1ed1c 17:00 n\u1eefa n\u00ean \u1edf l\u1ea1i t\u0103ng ca ch\u1ea5m l\u1ea1i th\u00ec c\u1eadp nh\u1eadt \u0111\u01b0\u1ee3c gi\u1edd ra mu\u1ed9n h\u01a1n, v\u00e0 v\u1ec1 s\u1edbm
        // v\u1eabn ghi \u0111\u01b0\u1ee3c gi\u1edd ra. \u0110\u1ecdc gi\u1edd ch\u1ea5m \u0111\u1ea7u (MIN) v\u00e0 gi\u1edd ch\u1ea5m g\u1ea7n nh\u1ea5t trong ng\u00e0y (b\u1ea5t k\u1ec3 loai \u2014 kh\u1edbp
        // \u0111\u00fang c\u00e1ch b\u1ea3ng c\u00f4ng t\u00ednh gi\u1edd v\u00e0o = MIN, gi\u1edd ra = MAX c\u1ee7a m\u1ecdi l\u1ea7n ch\u1ea5m).
        DateTime? firstAt = null, lastAt = null;
        await using (var reader = await conn.Cmd("""
            SELECT MIN(occurred_at) FILTER (WHERE loai='Vào') AS first_in,
                   MAX(occurred_at) FILTER (WHERE loai='Ra') AS last_out
            FROM hr_effective_attendance_log
            WHERE lower(username)=lower(@u) AND logical_work_date=@workDay
            """)
            .With("@u", username)
            .With("@workDay", workDay)
            .ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                firstAt = reader.DtNull("first_in");
                lastAt = reader.DtNull("last_out");
            }
        }

        string LocalHm(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTimeZone)
                .ToString("HH:mm");

        // 1) Ch\u01b0a c\u00f3 l\u1ea7n ch\u1ea5m n\u00e0o h\u00f4m nay \u21d2 \u0111\u00e2y l\u00e0 gi\u1edd V\u00c0O.
        if (firstAt is null)
            return new AttendanceDecision(true, CheckInTypeIn, null,
                $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng V\u00e0o.");

        var checkInAt = firstAt.Value;

        // 2) B\u1ea5m l\u1ea1i qu\u00e1 s\u00e1t gi\u1edd V\u00e0o \u21d2 coi l\u00e0 tr\u00f9ng (kh\u00f4ng ghi), tr\u00e1nh t\u1ea1o gi\u1edd Ra ngay sau khi v\u1eeba v\u00e0o.
        if ((nowUtc - checkInAt).TotalMinutes < MinSessionMinutes)
            return new AttendanceDecision(false, CheckInTypeIn, checkInAt,
                $"{displayName} v\u1eeba ch\u1ea5m c\u00f4ng V\u00e0o l\u00fac {LocalHm(checkInAt)} r\u1ed3i.");

        // 3) \u0110\u00e3 c\u00f3 gi\u1edd Ra v\u00e0 l\u1ea7n n\u00e0y qu\u00e1 s\u00e1t l\u1ea7n ch\u1ea5m g\u1ea7n nh\u1ea5t \u21d2 \u0111\u00e3 ghi, kh\u1ecfi nh\u00e2n \u0111\u00f4i d\u00f2ng.
        var hadCheckout = lastAt is { } lo && lo > checkInAt;
        if (hadCheckout && (nowUtc - lastAt!.Value).TotalMinutes is >= 0 and < MinReCheckoutMinutes)
            return new AttendanceDecision(false, CheckInTypeOut, lastAt,
                $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng Ra l\u00fac {LocalHm(lastAt.Value)} r\u1ed3i.");

        // 4) Ghi gi\u1edd RA (l\u1ea5y m\u1ed1c mu\u1ed9n nh\u1ea5t). \u0110\u00e3 c\u00f3 gi\u1edd ra tr\u01b0\u1edbc \u0111\u00f3 \u21d2 \u0111\u00e2y l\u00e0 C\u1eacP NH\u1eacT (\u1edf l\u1ea1i th\u00eam/t\u0103ng ca).
        var message = hadCheckout
            ? $"{displayName} \u0111\u00e3 c\u1eadp nh\u1eadt gi\u1edd Ra {LocalHm(nowUtc)} (\u1edf l\u1ea1i th\u00eam)."
            : $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng Ra.";
        return new AttendanceDecision(true, CheckInTypeOut, null, message);
    }

    private static TimeZoneInfo LoadVietnamTimeZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Bangkok" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* try next platform id */ }
        }

        return TimeZoneInfo.Local;
    }
}
