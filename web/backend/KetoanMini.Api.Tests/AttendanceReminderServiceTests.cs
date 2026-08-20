using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Durable reminder-ledger/outbox contract. The hosted worker is disabled by ApiFactory; every case
/// supplies an explicit Vietnam-time boundary through ReconcileAsync so midnight tests stay deterministic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AttendanceReminderServiceTests : IAsyncLifetime
{
    private const string Username = "__test_attendance_reminder__";
    private readonly ApiFactory _factory;
    private Guid _employeeId;

    public AttendanceReminderServiceTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await using var conn = await Db().OpenAsync();
        await CleanupAsync(conn);
        _employeeId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO hr_employees (id, username, full_name, status)
            VALUES (@id, @username, 'Reminder regression employee', 'Active')
            """)
            .With("@id", _employeeId).With("@username", Username)
            .ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = await Db().OpenAsync();
        await CleanupAsync(conn);
    }

    [Fact]
    public async Task ReconcileTwice_EnqueuesExactlyOneDurableOutboxMessage()
    {
        var day = new DateOnly(2026, 8, 1);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        var now = Utc(day.AddDays(1), new TimeOnly(7, 0));

        var first = await Service().ReconcileAsync(now);
        var second = await Service().ReconcileAsync(now);

        Assert.Equal(1, first.Candidates);
        Assert.Equal(1, first.Enqueued);
        Assert.Equal(1, second.Candidates);
        Assert.Equal(0, second.Enqueued);
        var notificationId = $"attendance:missing-checkout:{day:yyyy-MM-dd}";
        Assert.Equal(1, await OutboxCountAsync(notificationId));

        await using var conn = await Db().OpenAsync();
        await using var row = await conn.Cmd("""
            SELECT status,notification_id,notification_enqueued_at
            FROM hr_attendance_reminders
            WHERE employee_id=@employee AND work_date=@date AND direction='out'
            """).With("@employee", _employeeId).With("@date", day).ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("Pending", row.Str("status"));
        Assert.Equal(notificationId, row.Str("notification_id"));
        Assert.False(row.IsDBNull(row.GetOrdinal("notification_enqueued_at")));
    }

    [Fact]
    public async Task PendingRequest_SuppressesReminderAndLinksLedgerToRequest()
    {
        var day = new DateOnly(2026, 8, 1);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        var requestId = await SeedForgotRequestAsync(day, "Pending");

        var result = await Service().ReconcileAsync(Utc(day.AddDays(1), new TimeOnly(7, 0)));

        Assert.Equal(1, result.Candidates);
        Assert.Equal(0, result.Enqueued);
        Assert.Equal(1, result.SuppressedByRequest);
        Assert.Equal(0, await AttendanceOutboxCountAsync());

        await using var conn = await Db().OpenAsync();
        await using var row = await conn.Cmd("""
            SELECT status,request_id,notification_enqueued_at,resolution_source
            FROM hr_attendance_reminders
            WHERE employee_id=@employee AND work_date=@date AND direction='out'
            """).With("@employee", _employeeId).With("@date", day).ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("RequestCreated", row.Str("status"));
        Assert.Equal(requestId, row.Guid("request_id"));
        Assert.True(row.IsDBNull(row.GetOrdinal("notification_enqueued_at")));
        Assert.Equal("request", row.Str("resolution_source"));
    }

    [Fact]
    public async Task RejectedRequest_RearmsOneRetryGeneration()
    {
        var day = new DateOnly(2026, 8, 1);
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        var requestId = await SeedForgotRequestAsync(day, "Rejected");
        var expectedId = $"attendance:missing-checkout:{day:yyyy-MM-dd}:retry:{requestId}";

        var first = await Service().ReconcileAsync(Utc(day.AddDays(1), new TimeOnly(7, 0)));
        var second = await Service().ReconcileAsync(Utc(day.AddDays(1), new TimeOnly(7, 1)));

        Assert.Equal(1, first.Enqueued);
        Assert.Equal(0, second.Enqueued);
        Assert.Equal(1, await OutboxCountAsync(expectedId));

        await using var conn = await Db().OpenAsync();
        await using var row = await conn.Cmd("""
            SELECT status,request_id,notification_id
            FROM hr_attendance_reminders
            WHERE employee_id=@employee AND work_date=@date AND direction='out'
            """).With("@employee", _employeeId).With("@date", day).ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("Pending", row.Str("status"));
        Assert.Equal(requestId, row.Guid("request_id"));
        Assert.Equal(expectedId, row.Str("notification_id"));
    }

    [Fact]
    public async Task RealCheckout_ResolvesExistingLedgerWithoutAnotherOutboxMessage()
    {
        var day = new DateOnly(2026, 8, 1);
        var now = Utc(day.AddDays(1), new TimeOnly(7, 0));
        await SeedLogAsync(day, new TimeOnly(8, 0), AttendancePolicy.CheckInTypeIn);
        var detected = await Service().ReconcileAsync(now);
        Assert.Equal(1, detected.Enqueued);

        await SeedLogAsync(day, new TimeOnly(17, 42), AttendancePolicy.CheckInTypeOut);
        var reconciled = await Service().ReconcileAsync(now.AddMinutes(1));

        Assert.Equal(0, reconciled.Candidates);
        Assert.Equal(1, reconciled.Resolved);
        Assert.Equal(1, await AttendanceOutboxCountAsync());

        await using var conn = await Db().OpenAsync();
        await using var row = await conn.Cmd("""
            SELECT status,resolved_at,resolution_source
            FROM hr_attendance_reminders
            WHERE employee_id=@employee AND work_date=@date AND direction='out'
            """).With("@employee", _employeeId).With("@date", day).ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("Resolved", row.Str("status"));
        Assert.False(row.IsDBNull(row.GetOrdinal("resolved_at")));
        Assert.Equal("reconciled", row.Str("resolution_source"));
    }

    [Fact]
    public async Task NoShiftMetadata_WaitsUntilSixAmFollowingDay()
    {
        var day = new DateOnly(2026, 8, 1);
        await SeedLogAsync(day, new TimeOnly(22, 0), AttendancePolicy.CheckInTypeIn);

        var tooEarly = await Service().ReconcileAsync(Utc(day.AddDays(1), new TimeOnly(5, 59)));
        Assert.Equal(0, tooEarly.Candidates);
        Assert.Equal(0, await AttendanceOutboxCountAsync());

        var eligible = await Service().ReconcileAsync(Utc(day.AddDays(1), new TimeOnly(6, 0)));
        Assert.Equal(1, eligible.Candidates);
        Assert.Equal(1, eligible.Enqueued);
        Assert.Equal(1, await AttendanceOutboxCountAsync());
    }

    private Database Db() => _factory.Services.GetRequiredService<Database>();

    private AttendanceReminderService Service() =>
        _factory.Services.GetRequiredService<AttendanceReminderService>();

    private static DateTime Utc(DateOnly day, TimeOnly time) =>
        AttendancePolicy.LocalToUtc(day.ToDateTime(time));

    private async Task SeedLogAsync(DateOnly day, TimeOnly time, string type)
    {
        await using var conn = await Db().OpenAsync();
        await conn.Cmd("""
            INSERT INTO cham_cong_log
                (username,full_name,loai,similarity,occurred_at,ghi_chu)
            VALUES (@username,'Reminder regression employee',@type,1,@occurred,'reminder-regression')
            """)
            .With("@username", Username).With("@type", type)
            .With("@occurred", Utc(day, time)).ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedForgotRequestAsync(DateOnly day, string status)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            date = day.ToString("yyyy-MM-dd"),
            direction = "out",
            time = "18:00",
            reason = "Reminder service regression",
        });
        await using var conn = await Db().OpenAsync();
        await conn.Cmd("""
            INSERT INTO hr_requests
                (id,request_no,req_type,title,employee_id,requester_username,payload,status,current_step)
            VALUES
                (@id,@no,'forgot_checkin','Reminder service regression',@employee,@username,
                 @payload::jsonb,@status,1)
            """)
            .With("@id", id).With("@no", "REM-" + Guid.NewGuid().ToString("N")[..8])
            .With("@employee", _employeeId).With("@username", Username)
            .With("@payload", payload).With("@status", status)
            .ExecuteNonQueryAsync();
        return id;
    }

    private async Task<int> OutboxCountAsync(string notificationId)
    {
        await using var conn = await Db().OpenAsync();
        var dedupe = PushService.DedupeKey(OutboxQueue.KindUserPush, Username, notificationId);
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM app_outbox WHERE dedupe_key=@key")
            .With("@key", dedupe).ExecuteScalarAsync());
    }

    private async Task<int> AttendanceOutboxCountAsync()
    {
        await using var conn = await Db().OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM app_outbox WHERE dedupe_key LIKE @prefix")
            .With("@prefix", AttendanceOutboxPrefix()).ExecuteScalarAsync());
    }

    private static string AttendanceOutboxPrefix() =>
        $"{OutboxQueue.KindUserPush}|{Username.ToLowerInvariant()}|attendance:missing-checkout:%";

    private static async Task CleanupAsync(NpgsqlConnection conn)
    {
        await conn.Cmd("DELETE FROM app_outbox WHERE dedupe_key LIKE @prefix")
            .With("@prefix", AttendanceOutboxPrefix()).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_requests WHERE requester_username=@username")
            .With("@username", Username).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM cham_cong_log WHERE lower(username)=lower(@username)")
            .With("@username", Username).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_employees WHERE username=@username")
            .With("@username", Username).ExecuteNonQueryAsync();
    }
}
