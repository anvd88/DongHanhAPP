using KetoanMini.Api.Data;
using Microsoft.Data.SqlClient;

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

    private static readonly TimeSpan CheckOutStartsAt = TimeSpan.FromHours(17);
    private static readonly TimeZoneInfo VietnamTimeZone = LoadVietnamTimeZone();

    public static async Task<AttendanceDecision> DecideAsync(
        SqlConnection conn,
        string username,
        string displayName,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, VietnamTimeZone);
        var dayStartLocal = nowLocal.Date;
        var dayEndLocal = dayStartLocal.AddDays(1);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, VietnamTimeZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, VietnamTimeZone);

        DateTime? checkInAt = null;
        DateTime? checkOutAt = null;
        await using (var reader = await conn.Cmd(
            @"SELECT loai, occurred_at FROM dbo.cham_cong_log
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
