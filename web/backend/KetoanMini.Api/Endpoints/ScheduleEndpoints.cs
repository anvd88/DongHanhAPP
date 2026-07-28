using System.Globalization;
using System.Security.Claims;
using System.Text;
using KetoanMini.Api.Data;
using KetoanMini.Api.Services;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Lịch làm việc cá nhân — Đợt 3, nhiệm vụ 8 (phần xuất iCalendar). Cho phép nhân viên tải lịch ca của
/// CHÍNH MÌNH dưới dạng .ics để đưa vào Google/Apple/Outlook Calendar. Việc quản lý ca, đăng ký đổi ca,
/// kiểm xung đột đã nằm ở ShiftEndpoints; đây bổ sung nút "xuất lịch" mà spec yêu cầu.
/// </summary>
public static class ScheduleEndpoints
{
    public static void MapSchedule(this WebApplication app)
    {
        var g = app.MapGroup("/api/schedule").RequireAuthorization();

        // Xuất lịch ca của người dùng hiện tại ra iCalendar (.ics). Mặc định 60 ngày tới.
        g.MapGet("/ical", async (ClaimsPrincipal u, Database db, string? from, string? to) =>
        {
            var start = DateOnly.TryParse(from, out var f) ? f : DateOnly.FromDateTime(DateTime.UtcNow);
            var end = DateOnly.TryParse(to, out var t) ? t : start.AddDays(60);

            await using var conn = await db.OpenAsync();
            var sb = new StringBuilder();
            sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Dong Hanh//Lich lam viec//VI\r\nCALSCALE:GREGORIAN\r\n");
            var stampUtc = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            await using (var r = await conn.Cmd("""
                SELECT a.id, a.work_date, s.name, s.start_time, s.end_time, s.is_overnight
                FROM hr_shift_assignments a
                JOIN hr_shifts s ON s.id = a.shift_id
                JOIN hr_employees e ON e.id = a.employee_id
                WHERE e.username = @me AND a.work_date BETWEEN @from AND @to
                ORDER BY a.work_date
                """).With("@me", u.Username()).With("@from", start).With("@to", end).ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                {
                    var id = r.Guid("id");
                    var day = r.DateOnly("work_date");
                    var name = r.Str("name");
                    var startTime = (TimeSpan)r.GetValue(r.GetOrdinal("start_time"));
                    var endTime = (TimeSpan)r.GetValue(r.GetOrdinal("end_time"));
                    var overnight = r.Bool("is_overnight");

                    var startLocal = day.ToDateTime(TimeOnly.FromTimeSpan(startTime));
                    var endDay = overnight ? day.AddDays(1) : day;
                    var endLocal = endDay.ToDateTime(TimeOnly.FromTimeSpan(endTime));

                    var startUtc = AttendancePolicy.LocalToUtc(startLocal).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
                    var endUtc = AttendancePolicy.LocalToUtc(endLocal).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

                    sb.Append("BEGIN:VEVENT\r\n")
                      .Append("UID:shift-").Append(id).Append("@ketoanmini\r\n")
                      .Append("DTSTAMP:").Append(stampUtc).Append("\r\n")
                      .Append("DTSTART:").Append(startUtc).Append("\r\n")
                      .Append("DTEND:").Append(endUtc).Append("\r\n")
                      .Append("SUMMARY:").Append(Escape(string.IsNullOrWhiteSpace(name) ? "Ca làm việc" : name)).Append("\r\n")
                      .Append("END:VEVENT\r\n");
                }
            }
            sb.Append("END:VCALENDAR\r\n");

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/calendar; charset=utf-8", "LichLamViec.ics");
        });
    }

    /// <summary>Thoát ký tự đặc biệt trong text iCalendar (RFC 5545).</summary>
    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n");
}
