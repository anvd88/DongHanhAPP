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
        var dayStartLocal = nowLocal.Date;
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, VietnamTimeZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, VietnamTimeZone);

        DateTime? checkInAt = null;
        DateTime? checkOutAt = null;
        await using (var reader = await conn.Cmd(
            @"SELECT loai, occurred_at FROM cham_cong_log
              WHERE username=@u AND occurred_at >= @startUtc AND occurred_at < @endUtc
              ORDER BY occurred_at ASC")
            .With("@u", username)
            .With("@startUtc", dayStartUtc)
            .With("@endUtc", dayEndUtc)
            .ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var loai = reader.Str("loai");
                var occurredAt = reader.Dt("occurred_at");
                if (loai == CheckInTypeIn && checkInAt is null)
                    checkInAt = occurredAt;
                else if (loai == CheckInTypeOut && checkOutAt is null)
                    checkOutAt = occurredAt;
            }
        }

        if (nowLocal.TimeOfDay < CheckOutStartsAt)
        {
            if (checkInAt is { } existingIn)
                return new AttendanceDecision(false, CheckInTypeIn, existingIn,
                    $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng V\u00e0o h\u00f4m nay r\u1ed3i.");

            return new AttendanceDecision(true, CheckInTypeIn, null,
                $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng V\u00e0o.");
        }

        if (checkOutAt is { } existingOut)
            return new AttendanceDecision(false, CheckInTypeOut, existingOut,
                $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng Ra h\u00f4m nay r\u1ed3i.");

        return new AttendanceDecision(true, CheckInTypeOut, null,
            $"{displayName} \u0111\u00e3 ch\u1ea5m c\u00f4ng Ra.");
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
