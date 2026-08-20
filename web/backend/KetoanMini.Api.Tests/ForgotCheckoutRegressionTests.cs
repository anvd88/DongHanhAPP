using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Enterprise regression gates for the Android "missing checkout" journey. These tests intentionally
/// exercise the public HTTP contract plus PostgreSQL, because the highest-risk failures cross the
/// timesheet, request approval and attendance-correction boundaries.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ForgotCheckoutRegressionTests : IAsyncLifetime
{
    private const string Requester = "__test_forgot_checkout_employee__";
    private const string HrApprover = "__test_forgot_checkout_hr__";
    private const string ShiftCodePrefix = "__FC_REG_";

    private readonly ApiFactory _factory;
    private Guid _employeeId;

    public ForgotCheckoutRegressionTests(ApiFactory factory) => _factory = factory;

    private static DateOnly TodayVietnam => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    public async Task InitializeAsync()
    {
        await using var conn = await Db().OpenAsync();
        await CleanupAsync(conn);

        foreach (var (username, role) in new[]
                 {
                     (Requester, AppRoles.Employee),
                     (HrApprover, AppRoles.Hr),
                 })
        {
            await conn.Cmd("""
                INSERT INTO app_users
                    (id, username, full_name, email, role, password_hash, is_active,
                     approval_status, approved_at, approved_by, created_at, is_deleted)
                VALUES
                    (@id, @u, @u, '', @role, @hash, TRUE,
                     'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
                ON CONFLICT (username) DO UPDATE SET
                    role=@role, is_active=TRUE, is_deleted=FALSE, approval_status='Approved'
                """)
                .With("@id", Guid.NewGuid()).With("@u", username).With("@role", role)
                .With("@hash", PasswordHasher.Hash("test-pass"))
                .ExecuteNonQueryAsync();
        }

        _employeeId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO hr_employees (id, username, full_name, status)
            VALUES (@id, @u, 'Missing checkout regression employee', 'Active')
            """)
            .With("@id", _employeeId).With("@u", Requester)
            .ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = await Db().OpenAsync();
        await CleanupAsync(conn);
    }

    private static async Task CleanupAsync(NpgsqlConnection conn)
    {
        // Corrections deliberately RESTRICT deletion of their source request so audit evidence cannot
        // disappear accidentally in production. Test cleanup must therefore remove owned ledger rows
        // explicitly before deleting the request/employee fixtures.
        await conn.Cmd("DELETE FROM hr_attendance_corrections WHERE lower(username)=lower(@u)")
            .With("@u", Requester).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_attendance_reminders WHERE lower(username)=lower(@u)")
            .With("@u", Requester).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_requests WHERE requester_username=@u")
            .With("@u", Requester).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM cham_cong_log WHERE lower(username)=lower(@u)")
            .With("@u", Requester).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_employees WHERE username=ANY(@users)")
            .With("@users", new[] { Requester, HrApprover }).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=ANY(@users)")
            .With("@users", new[] { Requester, HrApprover }).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_shifts WHERE code LIKE @prefix")
            .With("@prefix", ShiftCodePrefix + "%").ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Timesheet_DuplicateCheckIns_DoNotInventCheckout()
    {
        var day = TodayVietnam.AddDays(-3);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        await SeedLogAsync(day, new TimeOnly(8, 7), AttendancePolicy.CheckInTypeIn);

        using var client = await ClientAsAsync(Requester);
        using var response = await client.GetAsync($"/api/timesheet/me?month={day:yyyy-MM}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = FindDay(json, day);

        Assert.Equal("08:00", row.GetProperty("checkIn").GetString());
        Assert.Null(OptionalString(row, "checkOut"));
        Assert.Equal("Thiếu giờ ra", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Timesheet_LoneCheckout_DoesNotBecomeCheckIn()
    {
        var day = TodayVietnam.AddDays(-4);
        await SeedLogAsync(day, new TimeOnly(18, 0), AttendancePolicy.CheckInTypeOut);

        using var client = await ClientAsAsync(Requester);
        using var response = await client.GetAsync($"/api/timesheet/me?month={day:yyyy-MM}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = FindDay(json, day);

        Assert.Null(OptionalString(row, "checkIn"));
        Assert.Equal("18:00", row.GetProperty("checkOut").GetString());
    }

    [Fact]
    public async Task Timesheet_MultipleSessions_UseEarliestInAndLatestOutByType()
    {
        var day = TodayVietnam.AddDays(-5);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        await SeedLogAsync(day, new TimeOnly(12, 0), AttendancePolicy.CheckInTypeOut);
        await SeedLogAsync(day, new TimeOnly(13, 0), AttendancePolicy.CheckInTypeIn);
        await SeedLogAsync(day, new TimeOnly(18, 0), AttendancePolicy.CheckInTypeOut);

        using var client = await ClientAsAsync(Requester);
        using var response = await client.GetAsync($"/api/timesheet/me?month={day:yyyy-MM}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = FindDay(json, day);

        Assert.Equal("08:00", row.GetProperty("checkIn").GetString());
        Assert.Equal("18:00", row.GetProperty("checkOut").GetString());
    }

    [Fact]
    public async Task Timesheet_OvernightCheckoutAcrossMonthBoundary_BelongsToWorkDate()
    {
        var firstOfThisMonth = new DateOnly(TodayVietnam.Year, TodayVietnam.Month, 1);
        var workDate = firstOfThisMonth.AddDays(-1);
        await SeedOvernightShiftAsync(workDate);

        await SeedLogAsync(workDate, new TimeOnly(22, 0), AttendancePolicy.CheckInTypeIn);
        await SeedLogAsync(workDate.AddDays(1), new TimeOnly(6, 0), AttendancePolicy.CheckInTypeOut);

        using var client = await ClientAsAsync(Requester);
        using var response = await client.GetAsync($"/api/timesheet/me?month={workDate:yyyy-MM}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = FindDay(json, workDate);

        Assert.Equal("22:00", row.GetProperty("checkIn").GetString());
        Assert.Equal("06:00", row.GetProperty("checkOut").GetString());
        Assert.NotEqual("Thiếu giờ ra", row.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false, "out", "18:00", "Quên chấm giờ ra")]
    [InlineData(true, "sideways", "18:00", "Quên chấm giờ ra")]
    [InlineData(true, "out", "25:61", "Quên chấm giờ ra")]
    [InlineData(true, "out", "18:00", "   ")]
    public async Task CreateForgotCheckout_InvalidRequiredPayload_Returns400(
        bool includeValidDate, string direction, string time, string reason)
    {
        var day = TodayVietnam.AddDays(-3);
        var date = includeValidDate ? day.ToString("yyyy-MM-dd") : "";
        if (includeValidDate)
            await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);

        using var client = await ClientAsAsync(Requester);
        using var response = await client.PostAsJsonAsync("/api/requests", new
        {
            type = "forgot_checkin",
            title = "Báo quên chấm công",
            payload = new { date, direction, time, reason },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateForgotCheckout_TodayOrFutureDate_Returns400()
    {
        await SeedLogAsync(TodayVietnam, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        using var client = await ClientAsAsync(Requester);
        using var response = await client.PostAsJsonAsync("/api/requests", new
        {
            type = "forgot_checkin",
            title = "Báo quên chấm công",
            payload = new
            {
                date = TodayVietnam.ToString("yyyy-MM-dd"),
                direction = "out",
                time = "18:00",
                reason = "Không được báo quên cho hôm nay",
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateForgotCheckout_ConcurrentDuplicatePending_ExactlyOneWins()
    {
        var day = TodayVietnam.AddDays(-3);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        using var firstClient = await ClientAsAsync(Requester);
        using var secondClient = await ClientAsAsync(Requester);

        var first = PostForgotAsync(firstClient, day, "18:00");
        var second = PostForgotAsync(secondClient, day, "18:00");
        var responses = await Task.WhenAll(first, second);
        using var response1 = responses[0];
        using var response2 = responses[1];

        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            new[] { response1.StatusCode, response2.StatusCode }.OrderBy(x => (int)x).ToArray());

        await using var conn = await Db().OpenAsync();
        var count = Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(*) FROM hr_requests
            WHERE employee_id=@employee AND req_type='forgot_checkin' AND status='Pending'
              AND payload->>'date'=@date AND payload->>'direction'='out'
            """)
            .With("@employee", _employeeId).With("@date", day.ToString("yyyy-MM-dd"))
            .ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task FinalApproval_CreatesCorrectionWithoutDeletingRawAttendance()
    {
        var day = TodayVietnam.AddDays(-3);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        using var requester = await ClientAsAsync(Requester);
        using var created = await PostForgotAsync(requester, day, "18:00");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var requestId = await ReadIdAsync(created);

        using var approver = await ClientAsAsync(HrApprover);
        using var approved = await approver.PostAsJsonAsync($"/api/requests/{requestId}/approve", new { comment = "OK" });
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);

        await using (var conn = await Db().OpenAsync())
        {
            var rawIn = Convert.ToInt32(await conn.Cmd("""
                SELECT COUNT(*) FROM cham_cong_log
                WHERE lower(username)=lower(@u) AND loai=@loai
                  AND (occurred_at AT TIME ZONE @tz)::date=@date
                """)
                .With("@u", Requester).With("@loai", AttendancePolicy.CheckInTypeIn)
                .With("@tz", AttendancePolicy.TzId).With("@date", day)
                .ExecuteScalarAsync());
            var rawOut = Convert.ToInt32(await conn.Cmd("""
                SELECT COUNT(*) FROM cham_cong_log
                WHERE lower(username)=lower(@u) AND loai=@loai
                  AND (occurred_at AT TIME ZONE @tz)::date=@date
                """)
                .With("@u", Requester).With("@loai", AttendancePolicy.CheckInTypeOut)
                .With("@tz", AttendancePolicy.TzId).With("@date", day)
                .ExecuteScalarAsync());
            var correctionCount = Convert.ToInt32(await conn.Cmd(
                    "SELECT COUNT(*) FROM hr_attendance_corrections WHERE request_id=@id")
                .With("@id", requestId).ExecuteScalarAsync());

            Assert.Equal(1, rawIn);
            Assert.Equal(0, rawOut);
            Assert.Equal(1, correctionCount);
        }

        using var timesheet = await requester.GetAsync($"/api/timesheet/me?month={day:yyyy-MM}");
        Assert.Equal(HttpStatusCode.OK, timesheet.StatusCode);
        using var json = JsonDocument.Parse(await timesheet.Content.ReadAsStringAsync());
        var row = FindDay(json, day);
        Assert.Equal("08:00", row.GetProperty("checkIn").GetString());
        Assert.Equal("18:00", row.GetProperty("checkOut").GetString());
    }

    [Fact]
    public async Task ApprovedOvernightForgotCheckout_StoresNextDayInstantOnWorkDateRow()
    {
        var workDate = TodayVietnam.AddDays(-3);
        await SeedOvernightShiftAsync(workDate, checkoutGraceMinutes: 0);
        await SeedLogAsync(workDate, new TimeOnly(22, 0), AttendancePolicy.CheckInTypeIn);
        using var requester = await ClientAsAsync(Requester);
        using var created = await PostForgotAsync(requester, workDate, "06:00");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var requestId = await ReadIdAsync(created);

        using var approver = await ClientAsAsync(HrApprover);
        using var approved = await approver.PostAsJsonAsync($"/api/requests/{requestId}/approve", new { comment = "OK" });
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);

        await using (var conn = await Db().OpenAsync())
        await using (var correction = await conn.Cmd("""
            SELECT work_date,
                   (occurred_at AT TIME ZONE @tz)::date AS effective_date,
                   to_char(occurred_at AT TIME ZONE @tz, 'HH24:MI') AS effective_time
            FROM hr_attendance_corrections
            WHERE request_id=@id AND loai=@loai
            """)
            .With("@tz", AttendancePolicy.TzId).With("@id", requestId)
            .With("@loai", AttendancePolicy.CheckInTypeOut)
            .ExecuteReaderAsync())
        {
            Assert.True(await correction.ReadAsync());
            Assert.Equal(workDate, correction.DateOnly("work_date"));
            Assert.Equal(workDate.AddDays(1), correction.DateOnly("effective_date"));
            Assert.Equal("06:00", correction.Str("effective_time"));
            Assert.False(await correction.ReadAsync());
        }

        using var timesheet = await requester.GetAsync($"/api/timesheet/me?month={workDate:yyyy-MM}");
        Assert.Equal(HttpStatusCode.OK, timesheet.StatusCode);
        using var json = JsonDocument.Parse(await timesheet.Content.ReadAsStringAsync());
        var row = FindDay(json, workDate);
        Assert.Equal("22:00", row.GetProperty("checkIn").GetString());
        Assert.Equal("06:00", row.GetProperty("checkOut").GetString());
    }

    [Fact]
    public async Task RealCheckoutBeforeFinalApproval_RollsBackDecisionAndKeepsRawLog()
    {
        var day = TodayVietnam.AddDays(-3);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        using var requester = await ClientAsAsync(Requester);
        using var created = await PostForgotAsync(requester, day, "18:00");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var requestId = await ReadIdAsync(created);

        // A real device supplies the missing checkout while the request is waiting in the HR queue.
        await SeedLogAsync(day, new TimeOnly(17, 42), AttendancePolicy.CheckInTypeOut);

        using var approver = await ClientAsAsync(HrApprover);
        using var response = await approver.PostAsJsonAsync($"/api/requests/{requestId}/approve", new { comment = "OK" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var conn = await Db().OpenAsync();
        await using (var reader = await conn.Cmd("""
            SELECT r.status, a.status AS approval_status
            FROM hr_requests r
            JOIN hr_request_approvals a ON a.request_id=r.id AND a.step_no=r.current_step
            WHERE r.id=@id
            """).With("@id", requestId).ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Pending", reader.Str("status"));
            Assert.Equal("Pending", reader.Str("approval_status"));
        }

        var rawOut = Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(*) FROM cham_cong_log
            WHERE lower(username)=lower(@u) AND loai=@loai
              AND (occurred_at AT TIME ZONE @tz)::date=@date
            """)
            .With("@u", Requester).With("@loai", AttendancePolicy.CheckInTypeOut)
            .With("@tz", AttendancePolicy.TzId).With("@date", day)
            .ExecuteScalarAsync());
        var correctionCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM hr_attendance_corrections WHERE request_id=@id")
            .With("@id", requestId).ExecuteScalarAsync());
        Assert.Equal(1, rawOut);
        Assert.Equal(0, correctionCount);
    }

    [Fact]
    public async Task CancelRacingFinalApproval_HasExactlyOneTerminalOutcomeAndMatchingEffect()
    {
        var day = TodayVietnam.AddDays(-3);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        using var requester = await ClientAsAsync(Requester);
        using var created = await PostForgotAsync(requester, day, "18:00");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var requestId = await ReadIdAsync(created);
        using var approver = await ClientAsAsync(HrApprover);

        var cancelTask = requester.PostAsync($"/api/requests/{requestId}/cancel", content: null);
        var approveTask = approver.PostAsJsonAsync($"/api/requests/{requestId}/approve", new { comment = "OK" });
        var responses = await Task.WhenAll(cancelTask, approveTask);
        using var cancelled = responses[0];
        using var approved = responses[1];

        Assert.Equal(
            new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict },
            new[] { cancelled.StatusCode, approved.StatusCode }.OrderBy(x => (int)x).ToArray());

        await using var conn = await Db().OpenAsync();
        var status = Convert.ToString(await conn.Cmd("SELECT status FROM hr_requests WHERE id=@id")
            .With("@id", requestId).ExecuteScalarAsync());
        var correctionCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM hr_attendance_corrections WHERE request_id=@id")
            .With("@id", requestId).ExecuteScalarAsync());

        Assert.Contains(status, new[] { "Cancelled", "Approved" });
        Assert.Equal(status == "Approved" ? 1 : 0, correctionCount);
    }

    [Fact]
    public async Task EditAfterAnyApprovalStep_Returns409AndPreservesOriginalPayload()
    {
        var day = TodayVietnam.AddDays(-3);
        var requestId = Guid.NewGuid();
        await using (var conn = await Db().OpenAsync())
        {
            await conn.Cmd("""
                INSERT INTO hr_requests
                    (id, request_no, req_type, title, employee_id, requester_username,
                     payload, status, current_step)
                VALUES
                    (@id, @no, 'forgot_checkin', 'Báo quên chấm công', @employee, @requester,
                     @payload::jsonb, 'Pending', 2)
                """)
                .With("@id", requestId).With("@no", "FC-EDIT-" + Guid.NewGuid().ToString("N")[..8])
                .With("@employee", _employeeId).With("@requester", Requester)
                .With("@payload", JsonSerializer.Serialize(new
                {
                    date = day.ToString("yyyy-MM-dd"), direction = "out", time = "18:00", reason = "Original",
                }))
                .ExecuteNonQueryAsync();
            await conn.Cmd("""
                INSERT INTO hr_request_approvals
                    (request_id, step_no, approver_role, approver_username, approver_name,
                     status, decided_at, decided_by)
                VALUES
                    (@id, 1, 'Manager', 'manager', 'Manager', 'Approved', CURRENT_TIMESTAMP, 'manager'),
                    (@id, 2, 'HR', '', 'HR', 'Pending', NULL, '')
                """)
                .With("@id", requestId).ExecuteNonQueryAsync();
        }

        using var requester = await ClientAsAsync(Requester);
        using var response = await requester.PutAsJsonAsync($"/api/requests/{requestId}", new
        {
            type = "forgot_checkin",
            title = "Changed after approval",
            payload = new
            {
                date = day.ToString("yyyy-MM-dd"), direction = "out", time = "23:59", reason = "Tampered",
            },
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verify = await Db().OpenAsync();
        var storedTime = Convert.ToString(await verify.Cmd(
                "SELECT payload->>'time' FROM hr_requests WHERE id=@id")
            .With("@id", requestId).ExecuteScalarAsync());
        Assert.Equal("18:00", storedTime);
    }

    private Database Db() => _factory.Services.GetRequiredService<Database>();

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> ClientAsAsync(string username)
    {
        await using var conn = await Db().OpenAsync();
        Guid id;
        string role;
        await using (var reader = await conn.Cmd("SELECT id, role FROM app_users WHERE username=@u")
            .With("@u", username).ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            id = reader.Guid("id");
            role = reader.Str("role");
        }

        using var scope = _factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<TokenService>().CreateToken(
            new UserDto(id, username, username, "", role, true, "Approved", DateTime.UtcNow));
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedLogAsync(DateOnly day, TimeOnly time, string type)
    {
        await using var conn = await Db().OpenAsync();
        await conn.Cmd("""
            INSERT INTO cham_cong_log
                (username, full_name, loai, similarity, occurred_at, ghi_chu)
            VALUES (@u, 'Missing checkout regression employee', @type, 1, @occurred, 'regression-test')
            """)
            .With("@u", Requester).With("@type", type)
            .With("@occurred", AttendancePolicy.LocalToUtc(day.ToDateTime(time)))
            .ExecuteNonQueryAsync();
    }

    private async Task SeedOvernightShiftAsync(DateOnly workDate, int checkoutGraceMinutes = 120)
    {
        var shiftId = Guid.NewGuid();
        await using var conn = await Db().OpenAsync();
        await conn.Cmd("""
            INSERT INTO hr_shifts
                (id, code, name, start_time, end_time, break_minutes,
                 late_grace_minutes, standard_hours, is_overnight, checkout_grace_minutes)
            VALUES (@id, @code, 'Overnight regression', '22:00', '06:00', 0, 5, 8, TRUE, @grace)
            """)
            .With("@id", shiftId).With("@code", ShiftCodePrefix + Guid.NewGuid().ToString("N")[..20])
            .With("@grace", checkoutGraceMinutes)
            .ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_shift_assignments (id, employee_id, shift_id, work_date)
            VALUES (@id, @employee, @shift, @date)
            """)
            .With("@id", Guid.NewGuid()).With("@employee", _employeeId)
            .With("@shift", shiftId).With("@date", workDate)
            .ExecuteNonQueryAsync();
    }

    private static Task<HttpResponseMessage> PostForgotAsync(HttpClient client, DateOnly day, string time) =>
        client.PostAsJsonAsync("/api/requests", new
        {
            type = "forgot_checkin",
            title = "Báo quên chấm công",
            payload = new
            {
                date = day.ToString("yyyy-MM-dd"),
                direction = "out",
                time,
                reason = "Quên chấm giờ ra",
            },
        });

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static JsonElement FindDay(JsonDocument json, DateOnly day)
    {
        var expected = day.ToString("yyyy-MM-dd");
        return json.RootElement.GetProperty("days").EnumerateArray()
            .Single(row => row.GetProperty("date").GetString() == expected);
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;
}
